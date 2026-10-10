# Declarative quests (phase 1)

Common personal kill and spawner quests use immutable C# declarations. The framework owns filtering, counters, stages, subscriptions, journal text, persistence and owner-client item rewards. Existing `Quest`, `KillQuest`, `DestroySpawnersQuest` and custom reward handlers remain supported.

## Complete plugin example

Reference the framework DLL/project and BepInEx. Install the content plugin and framework on the server **and every client**, and register in `Awake` before entering a world:

```csharp
using System;
using BepInEx;
using RobleysQuestingFramework;

[BepInPlugin("example.hunt", "Example Hunt", "1.0.0")]
[BepInDependency(RobleysQuestingFramework.RobleysQuestingFramework.PluginGUID)]
public class HuntPlugin : BaseUnityPlugin
{
    IDisposable _quests;

    void Awake()
    {
        _quests = QuestManager.RegisterDefinitions("example.hunt",
            new QuestDefinition("example.hunt.necks_then_nest", 1,
                "Necks, then a nest", QuestDiscovery.PlayerAvailable,
                QuestObjective.Sequence("hunt",
                    QuestObjective.Kill("necks", "Neck", 5),
                    QuestObjective.DestroySpawner("nest", "Spawner_GreydwarfNest", 1)),
                new ItemReward("coins", "Coins", 50)));
    }

    void OnDestroy() => _quests?.Dispose();
}
```

No quest subclass, Harmony patch, RPC, Update loop, subscription/filtering code, serializer, reward handler, receipt code or journal override is required. `RobleysTestQuests/DeclarativeExamples.cs` registers the playable two-step proof and a one-objective encounter quest with distinct new identities. Existing examples and rewards are unchanged.

A single objective can replace the sequence:

```csharp
new QuestDefinition("example.first_neck", 1, "A personal hunt",
    QuestDiscovery.CreatureEncounter("Neck"),
    QuestObjective.Kill("neck", "Neck", 1),
    new ItemReward("coins", "Coins", 5))
```

For simultaneous requirements use `QuestObjective.All("both", ...children...)`. All and Sequence can nest. Every objective and group needs a unique ID within its quest. Reward slots must also be unique within their quest; quantity and quality must be positive. `ItemReward` defaults to quality 1 and supports explicit quality, including values above crafting caps as the existing item helper does.

## Runtime semantics

- Discovery on availability covers both current connected characters and future arrivals. Creature encounter means the creature senses/is alerted to the player it is targeting, not simple proximity. Discovery automatically accepts; satisfying the root objective automatically completes.
- Personal kill/destruction credit uses the existing observation services and their attribution rules. Wrong players/prefabs do not advance counters. No event history is replayed or backfilled. Existing observation deduplication is in-memory; gameplay reports are not durably delivered.
- All children progress independently. One matching report can count for multiple active AND leaves. Sequence only activates its first unfinished child. The eligible leaves are captured before processing each report, so a report finishing a child cannot also advance the next child, even for identical prefabs.
- Counters clamp to their requirement. Journal text lists each leaf as Done, Current or Later with its count. It uses prefab names, without introducing a localization system. Progress/stage changes use the existing player-filtered server journal notifications.
- Attach/detach callbacks only manage listeners. Completion, cancellation, module disposal and world unload detach runtime observations. Partial progress saves at the existing world-save/unload boundaries; completion additionally saves its entire reward plan. There is no per-event durable checkpoint.

## IDs, versions and saves

Quest IDs are global. Objective/group and reward-slot IDs are stable strings, never array offsets. Keep them unchanged for the same logical content. Definition versions belong to content authors; structured schema version 1 and objective format version 1 belong to the framework. They are separate from the world-file format.

The manager reads world-file v1 and v2. Legacy-only worlds remain v1. Saving any structured record upgrades the file to v2, including records for unavailable plugins. Each record retains the existing integer `Progress`; declarative records additionally have `State`, containing schema/definition versions, a semantic layout, and an ID-keyed node dictionary with counters, completion and current sequence-child IDs. Restore occurs before subscriptions attach. Legacy subclasses keep their integer SaveProgress/RestoreProgress behavior and original SetStage method signature.

The first v1-to-v2 replacement preserves the previous file as `<world>.json.v1.bak` (if it exists); subsequent saves retain the existing rotating `<world>.json.bak`. Back up quest JSON, character and world saves before upgrading. Old framework builds reject v2; **do not downgrade by merely changing the header**. Restoring the v1 backup loses subsequent quest progress and must be coordinated with independent world/character backups. A new world first saved as v2 has no v1 predecessor backup.

Compatibility is deliberately strict in this milestone:

- Reordering AND children or reward declarations with unchanged IDs/contents is compatible. Display-name changes are compatible.
- Changing definition version, objective kind/version, quantity, prefab, group membership, sequence order, discovery or reward contents is incompatible, even if IDs match. Reordering a sequence changes its meaning and is not an ID-preserving migration.
- No automatic migration or migration registry is implemented. Restore the matching content build, or develop an explicit, separately reviewed offline migration against backed-up saves. Do not reuse legacy playable quest IDs for declarative definitions; missing structured state is not inferred from their integer progress.
- Unsupported versions, inconsistent state and unknown structured fields leave the record inactive and preserve its payload. Diagnostics identify quest, character/instance and, when available, plugin/objective and the remedy. Unavailable definitions remain in the save. `GetOrAddQuest` returns null for an incompatible structured record and `created` stays false; it never makes a reset duplicate. Unsupported world-file versions fail the whole load without replacing the file.
- Completed records never replay completion hooks on load. An unfinished declarative record already containing all objective progress can finalize its deterministic completion plan at world readiness; this handles a checkpoint taken by an observer immediately before the terminal transition.

## Registration and teardown

`RegisterDefinitions(pluginID, definitions...)` validates declarations and rejects duplicate quest IDs, including collisions with legacy factories. Dispose its handle before registering the same IDs again. Disposal snapshots progress, removes factories and runtime listeners, and attempts a save without removing saved records or queued rewards. Re-registering compatible content restores those records and fills missing current-player instances; future arrivals use the same path. General DLL hot reload, in-place edits and automatic content migrations are not supported.

Legacy `RegisterQuest` still overwrites legacy factories as before. It now rejects overwriting a currently owned declarative ID, preventing one plugin from stealing another module's lifecycle. Registrations persist across world unloads; instances and their listeners do not.

Prefab validation runs at world readiness when ZNetScene and ObjectDB exist. Missing references produce contextual warnings; registration before those registries initialize is valid. Fix the content reference before expecting progress or delivery. Item delivery independently validates its descriptor and item prefab and leaves unavailable rewards pending.

## Completion and rewards

The framework initializes `rqf.item.v1` on every peer. Authors declare slots, prefabs, quantities and optional quality; the framework owns payloads and delivery. Content-module teardown does not unregister this handler.

Declarative completion validates all reward identities, stages the entire plan, sets terminal state and attempts to persist them together **before completion notifications or delivery**. A failed save keeps the same intents in memory, withholds outbox delivery, and retries persistence through the existing reward tick. Ordinary save/unload also retries. Duplicate completion calls do not append rewards. Every delivery retains the existing SHA-256 identity scoped by world, character, registered quest, instance key and slot. Acknowledgements remain tombstones. Full inventories use GiveOrDrop, including stack splitting, quality and ground overflow.

This guarantees durable retry/deduplication under normal saved-state continuity, not crash-atomic exactly-once rewards. Quest JSON, world saves and character receipt saves are independent. Crashes, partial restores and character rollback can lose or repeat effects. A failed completion write lost to a process crash cannot be recovered beyond the last successful save. Existing custom completion hooks and per-slot QueueReward semantics are unchanged; arbitrary callbacks are never replayed on load.

## Extension boundaries

Phase 1 supports personal kills, personal spawner destruction, AND and ordered sequences only. Custom observation families, dialogue/hand-in flows and effects continue through existing quest subclasses and services. Extend the typed objective implementation and version its persisted semantics when adding a supported objective family; do not wrap whole Quest instances as children. No OR, optional/branching objectives, party credit, historical event bus, JSON authoring, repeatability or hot reload is implied.

## Validation and manual checks

Automated production-code tests (game/RPC boundaries stubbed):

```powershell
dotnet run --project tests/Persistence/Persistence.csproj -p:UseSharedCompilation=false
dotnet build RobleysQuestingFramework.sln -p:SkipPublish=true -p:UseSharedCompilation=false
```

`SkipPublish=true` suppresses publishing and optional Unity assembly copying. If parallel MSBuild restore fails without diagnostics in the sandbox, append `-m:1`. For temporary-directory restrictions set process-local TEMP and TMP to a writable workspace folder. Test sources are included in Git; bin/obj and tests/.tmp stay ignored.

Manual checks below are required and were **not performed** by the automated harness:

1. **Host play:** enable both plugins. Verify availability discovery of “Necks, then a nest”; kill five Necks personally, then destroy one nest. Earlier nest destruction must not count. Confirm generated journal steps and 50 Coins once. For “A personal hunt”, verify creature sensing/targeting triggers discovery and a subsequent personal Neck kill pays 5 Coins. Existing neck/raid and nest/axe quests should keep their old behavior.
2. **Dedicated server, two remote players:** install matching plugins on all peers. Split kills and destruction between players, including unrelated prefabs. Only each credited character's counters/rewards should advance; inspect each client journal. Complete both characters independently.
3. **Disconnect/reconnect mid-step:** save at 3/5 Necks, reconnect as the same character, and confirm 3/5 persists with one instance. Switch characters and confirm independent progress. Disconnect before the next sequence step and verify reconnect does not backfill events.
4. **World/server restart:** save mid-step and at the 5/5 sequence boundary. Restart and complete the next step. Restart again with a pending reward (recipient disconnected or delivery unavailable), reconnect, and verify delivery and receipt replay without a second application under normal character saves. Completed state must not replay arbitrary effects.
5. **Ownership transfer:** move the victim/spawner's network ownership between clients before the credited death/destruction. Verify attribution and one observed increment under existing service guarantees; do not infer durable event delivery from this check.
6. **Full inventory:** complete with no free slots, then with partially filled coin stacks. Verify the remainder drops beside the intended player, totals 50, and retries do not spawn another drop after a receipt exists.
7. **Unloaded areas:** leave targets unloaded, disconnect observers, and restart. No kills/destruction or sequence advancement should be fabricated merely because objects are absent.
