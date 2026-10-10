# Clear and destroy-spawner quests

## ClearQuest

`ClearQuest` owns clear-area progress and lifecycle. A subclass supplies discovery, rewards, and `SubscribeArea(Action<ClearAreaStatus>)`, returning a disposable subscription. The observation source supplies counts for living hostile creatures and remaining spawners. Zero for both means cleared, so the source must establish that the entire area is loaded and settled before sending an empty observation.

The base listens only while the quest is started, unfinished and not ready for turn-in. With no giver it completes automatically; giver-based quests retain the normal turn-in flow. Progress values are 0 (unvisited), 1 (visited) and 2 (cleared). Remaining counts are refreshed after reload rather than persisted as an authoritative enemy roster.

`ExploreQuest` now inherits this lifecycle and keeps its existing radius-based observation source, constructors, progress values and journal behavior. `ClearBurialChambersQuest` supplies a dungeon-specific source instead. Dungeon clearing includes spawners; the existing exploration source continues to count only living hostiles within its configured sphere.

## Dungeon observations and burial chambers

`DungeonObservationService.Entered` supplies a player ID, persistent dungeon ZDO ID and prefab hash on the server. The event may repeat while a player is inside; callers create instances with `GetOrAddQuest` using `DungeonEncounter.InstanceKey`. `DungeonObservationService.Subscribe` routes clear observations to a particular player and dungeon.

The test plugin registers `ClearBurialChambersQuest` for the `DG_ForestCrypt` prefab, listed in the [Jötunn prefab catalog](https://valheim-modding.github.io/Jotunn/data/prefabs/prefab-list.html). It creates one quest per player per chamber when the player is observed inside, rather than near the exterior entrance. No quest-giver interaction is required. The 50-coin completion reward uses the existing persistent outbox with a separate receipt per chamber.

The client's two-second interior scan waits for dungeon room generation/loading and network-object readiness. It counts living hostiles, active `SpawnArea` objects, and creature spawners that have not yet produced their defenders. Finished one-shot spawners retain their Spawned connection and no longer block completion. Global-key and group-limit blocked creature spawners are excluded. Three consecutive loaded, empty samples are required; loading, spawners, enemies, a reporting gap or leaving the dungeon resets confirmation.

The server verifies the reporting player and interior zone against the dungeon ZDO, derives the prefab itself, and routes progress only to the matching quest. As with existing observations, enemy counts are trusted reports from modded peers, not an anti-cheat protocol. The scanner uses vanilla elevated dungeon zones; custom dungeon layouts extending beyond their zone require a different observation source. Surface enemies and neighbouring interiors are excluded.

Quest progress and outcomes are stored under the original chamber ZDO ID. Re-entry, death or restart does not generate another attempt. Completing another chamber grants another 50 coins. Another player's progress and reward are independent. A first visit to an already-cleared chamber can complete after the normal confirmation checks.

## DestroySpawnersQuest

`DestroySpawnersQuest` takes a player ID, spawner prefab and positive required count. It discovers on the player's first encounter with the matching spawner, counts personal destruction reports, saves the count, filters unrelated players/prefabs, and handles subscription cleanup. Subclasses supply presentation and completion effects. Override `OnSpawnerDiscovered` for a discovery message or the usual discovery subscription methods for a different trigger.

```csharp
public class DestroyNestsQuest : DestroySpawnersQuest
{
    public DestroyNestsQuest(long playerID)
        : base(playerID, "Spawner_GreydwarfNest", 3)
    {
        Name = "Destroy three greydwarf nests";
    }

    protected override void ActivateQuestCompletionEffects()
    {
        // Queue your reward through QuestManager.WorldInstance.QueueReward.
    }
}
```

Register the factory with `discoverForEveryPlayer: true` to install hidden discovery watchers. `GreydwarfNestsQuest` now derives from this base, preserving its existing quest ID, `ClearedNests` property, integer progress, 3-nest threshold and quality 6 bronze axe reward.

## In-game verification

Install both plugin DLLs on host/server and clients. Verify in solo/host play and with a remote player on a dedicated server:

1. Approach the exterior of a burial chamber: no quest. Enter: one started quest with its coordinates. Leave and re-enter: the same instance remains. Other dungeon types must not create it.
2. Remain in the entrance while deeper rooms contain enemies or untriggered creature spawners: the quest must not complete. Visit the rooms, defeat their enemies and destroy evil bone piles. Confirm 50 coins arrive after the loaded-empty confirmation.
3. Save/restart during clearing and after completion. Check progress resumes, and a completed chamber never pays again. Clear a second chamber and verify a second 50-coin payout.
4. Have two players enter the same chamber, and one visit a different chamber. Verify instance isolation and that each present player can receive their own reward. Clearing elsewhere must not complete the current chamber's quest.
5. Leave or disconnect during loading or empty confirmation. Re-enter and ensure the scan waits for rooms and network objects. Test with a full inventory and verify the coin remainder drops beside the player.
6. Re-run the greydwarf nest checks: existing partial saves resume, first discovery and destruction count correctly, and the third nest gives the unchanged quality 6 bronze axe reward.

Automated checks: `dotnet run --project tests/Persistence/Persistence.csproj -p:UseSharedCompilation=false`. Build without deployment: `dotnet build RobleysQuestingFramework.sln -p:SkipPublish=true -p:UseSharedCompilation=false`.
