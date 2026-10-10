# Writing a quest

Write a quest class, not a quest-specific service. [`BoarRunestoneQuest.cs`](RobleysTestQuests/BoarRunestoneQuest.cs) is a complete NPC quest example: it contains its location trigger, spawn settings, dialogue, acceptance, resource requirements, attack/death cancellation, and taming reward. The shared services contain no boar quest IDs or item requirements.

## Registration

Register definitions on **every peer before entering a world**. Register your factory with `discoverForEveryPlayer: true` to automatically create a hidden quest for each connected character. The manager restores saved quests instead of creating duplicates. The existing two-argument `RegisterQuest` remains available for quests explicitly created elsewhere.

```csharp
QuestManager.RegisterQuest("my.quest", (playerID, inventory) =>
    new MyQuest(playerID, inventory), discoverForEveryPlayer: true);
```

The boar example groups its NPC definition registration and quest factory in `BoarRunestoneQuest.Register()`. That method is called once from the optional `RobleysTestQuests` plugin startup. External quest plugins reference the framework project or DLL and declare a BepInEx dependency on `RobleysQuestingFramework.PluginGUID`; the framework does not register their content. Nothing needs a per-quest `Update` method or RPC handler.

## Stage-based subscriptions

Override `SubscribeDiscoveryEvents` and `UnsubscribeDiscoveryEvents` to watch for discovery while the quest is hidden. For a location trigger:

```csharp
protected override void SubscribeDiscoveryEvents()
    => _locationWatch = LocationObservationService.Subscribe(
        PlayerID, "Runestone_Boars", 12f, EncounterStone);

protected override void UnsubscribeDiscoveryEvents()
{
    _locationWatch?.Dispose();
    _locationWatch = null;
}
```

Use `ConfigureSubscriptions` and `RegisterSubscription` for other stages. `ResourceCollectionQuest` already subscribes to the owner's inventory while started and unfinished, so call `base.ConfigureSubscriptions()` when extending it. The boar example listens for:

| Event | Active stages | Quest decision |
| --- | --- | --- |
| Location encounter | Hidden | Spawn the giver, assign `QuestGiverId`, call `Discover()` |
| NPC interaction | Unfinished, including hidden | Discover an existing giver, accept, or request hand-in |
| NPC attacked by a player | Discovered or started | Cancel this quest |
| NPC world update | Discovered or started | Cancel if the giver disappeared or its attacked state arrived over the network |
| Inventory change | Started | Update the raspberry requirement through the base resource quest |

Handlers filter the player and/or giver ID. Attach/detach callbacks **only manage listeners**; transition stages in event handlers. Stage changes refresh subscriptions automatically. Completion, cancellation, and world unload detach listeners. Revoke pending hand-ins when detaching interaction handlers with `QuestHandInService.Cancel(this)`.

Set `QuestGiverId` **before** `Discover()`. A quest with no giver automatically starts on discovery and completes when its criteria are met; a quest with a giver awaits explicit acceptance and completion. Stage values are flags: use `Has`/`IsFinishedStage` instead of assuming each stage replaces all earlier flags.

## Shared operations

| API | Responsibility |
| --- | --- |
| `LocationObservationService.Subscribe` | Server-side proximity observations for the requested player/location/range; returns a disposable watch |
| `QuestNpcDefinition` | Prefab, level, hover text, friendliness, attack response, and whether ordinary feeding/taming is blocked |
| `QuestNpcService.Register` | Make an NPC definition available on each peer |
| `QuestNpcService.SpawnOnce` | Spawn near a connected player and mark a persistent encounter key so it cannot spawn twice |
| `QuestNpcService.Attach` | Apply a registered definition to an existing matching NPC or trader |
| `QuestNpcService.Interacted` | Validated server event `(playerID, giverID)` for a designated NPC |
| `QuestNpcService.AttackedByPlayer` | Server event `(giverID, attackerPlayerID)` reported by the NPC's network owner |
| `QuestNpcService.CanClaim` | Check availability and that another started/completed quest has not claimed this giver; the quest then binds its giver and starts |
| `QuestNpcService.Tame` | Persist an idempotent taming request for the NPC's current network owner |
| `QuestHandInService.Request` | Check/remove the required items on the paying client and invoke the quest callback after acknowledgement |
| `QuestNetwork.Tell` | Deliver a message to the intended player, including remote clients |

Interaction adapters currently cover `Tameable` creatures and `Trader`. Only explicitly registered/tagged NPCs are affected. A tagged trader's ordinary interaction opens the quest interaction path instead of its store; a combined trade/quest menu is not implemented. Other interaction component types need one shared adapter, not a service per quest.

For a resource quest, pass `Criteria` directly to `QuestHandInService.Request`. It supports multiple prefab requirements, split stacks, and exact-quality requirements without double-counting items. A single request is all-or-nothing; there are no partial contributions. Use a stable, unique `paymentID` for each one-time payment and keep it stable across reconnects. Never reuse an ID for a different charge against the same player/giver/world.

The request callback belongs to the quest: it decides whether to mark criteria met, complete, and grant effects. RPC validation, inventory removal, receipt replay, and rejection of inactive/cancelled quests belong to the shared hand-in service. As with existing inventory observations, remote clients are trusted to report their own inventory changes.

## Persistence and restoration

The manager saves player/quest identity, stage flags, progress, and the exact `QuestGiverId`. Override `SaveProgress`/`RestoreProgress` for additional integer progress. The boar's one-time encounter keys and NPC state live in the world; payment receipts live with the player's character.

`OnWorldReady()` runs once per activation after subscriptions are attached and the server world is available. Use it for **idempotent world-state repair**, such as ensuring a completed quest's boar is tamed. Do not replay consumable rewards or dialogue there. Ordinary completion effects still run only for a successful completion transition.

The refactor preserves the boar's existing quest ID, legacy world flags, runestone encounter keys, and payment receipt IDs. `QuestNpcDefinition` allows legacy marker/tamed/attacked keys so this migration logic stays with the quest definition. Existing accepted quests resume with their saved giver. New quests become discovered on encountering their runestone, and start only when the player interacts.

Character, quest JSON, and world saves are independent. They support normal save/reload, but hard crashes or rolling back only one save are not atomic. Unity spawning, actual RPC delivery, and owner transfer still require in-game testing.


## Owner-client rewards

Completion effects execute on the server. For character-owned effects, register a namespaced handler on every client during plugin initialization, and queue a named reward slot from `ActivateQuestCompletionEffects`:

```csharp
QuestRewardService.Register("my.mod.skill-v1", (player, payload) =>
{
    player.RaiseSkill(Skills.SkillType.WoodCutting, 100);
    return true;
});
// In the managed quest's completion hook:
QuestManager.WorldInstance.QueueReward(this, "completion-skill-v1", "my.mod.skill-v1");
```

Unregister the handler during plugin teardown. The optional string payload is limited to 8192 characters; handler and slot IDs are limited to 128 characters. Validate payloads and prerequisites before applying effects. Return `false` only if no effects were applied. A throwing or partially successful handler can repeat partial effects on retry; handlers must prevent that themselves.

The stable SHA-256 delivery ID includes world ID, character ID, registered quest ID, instance key, and reward slot. Requeuing identical contents is a no-op, including after acknowledgement. Reusing a slot with different contents throws. Use distinct instance keys for repeatable quests. Acknowledged records remain as tombstones in the world's quest JSON.

Enqueue saves quest completion and the outbox together before delivery. Pending rewards retry every five seconds while the character is connected and alive, including after a server restart. Failed saves withhold delivery until persistence succeeds. Missing client handlers leave rewards pending. Only the server can send accepted deliveries; the receiver checks the intended character, and the server accepts acknowledgements only from that character's peer. This trusts the character-owning client, like inventory observations; it is not anti-cheat verification.

Successful handlers write a receipt in `Player.m_customData`. Duplicate deliveries replay the acknowledgement without applying effects, even if the handler was subsequently removed. Receipts persist with ordinary character saves. This is retry deduplication, **not crash-atomic exactly-once delivery**: a client crash or character rollback after acknowledgement but before saving can lose a reward, and an exception between effects and receipt creation can repeat effects. Completion hooks are not replayed on load, so previously completed quests without outbox entries are not retroactively paid. Queuing multiple slots is not a single transaction across the entire completion hook.

Manual multiplayer check (host and dedicated server): install both plugins on server and clients, complete the wood quest remotely, and verify only the intended character loses 10 wood and gains skill progress. Disconnect before delivery, restart the server, reconnect, and verify the pending entry delivers. Suppress an acknowledgement in a development build and verify repeated packets only replay receipts. Save/reload the character and world and verify acknowledged entries do not pay again. Repeat with two characters; inspect `Rewards` in the world's quest JSON for pending and acknowledged records.
