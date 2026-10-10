# Explore and revenge quests

## Explore quests

Derive from `ExploreQuest`, providing a fixed world position and a radius greater than zero and at most 64 metres. Register its factory with `QuestManager` as usual; keep the destination stable across reloads. The base class owns observation, progress, and persistence. Your subclass supplies discovery and completion effects, just like `KillQuest`.

```csharp
public class ClearCampQuest : ExploreQuest
{
    public ClearCampQuest(long playerID)
        : base(playerID, new UnityEngine.Vector3(100, 30, 200), 30)
    {
        Name = "Clear the camp";
    }

    protected override void SubscribeDiscoveryEvents() { }
    protected override void UnsubscribeDiscoveryEvents() { }
    protected override void ActivateQuestCompletionEffects() { }
}

// Replace the example coordinates with the actual camp's position, including elevation.
QuestManager.RegisterQuest("my_mod.clear_camp", (id, inventory) => new ClearCampQuest(id));
// On the server, from your quest acceptance/discovery callback:
QuestManager.WorldInstance.GetOrAddQuest(playerID, "my_mod.clear_camp", out _).Discover();
```

The player must be alive and inside the sphere. All intersecting zones and their network objects must be loaded on that player's peer. Living non-player creatures hostile to that player count as enemies. Neutral and tamed creatures do not count. Other players may help clear the area; the quest does not require personal killing blows. An already-empty area also qualifies after a visit. Enemies leaving the sphere no longer count. This is an area-clear objective, not a fixed roster of enemies or a requirement to destroy spawners.

The server polls every two seconds and requires three consecutive loaded, empty observations. Missing responses, leaving the area, dying, or finding enemies resets that confirmation. The journal shows destination coordinates and observed enemy count. Visits and cleared progress survive saves; remaining enemy counts are refreshed after loading. A quest without a giver completes automatically; setting `QuestGiverId` retains the usual acceptance and turn-in behavior. No arbitrary destination is enabled as a sample quest by the framework.

## Revenge quests

`rqf.revenge` is registered by the optional `RobleysTestQuests` plugin. Its concrete quest and lifecycle rules live in `RobleysTestQuests/RevengeQuest.cs`; the framework retains death observation, RPC validation, enemy promotion, retries and level synchronization. An owner-client player-death observation is sent to the server. The test quest handles that server event and cancels the player's previous unfinished revenge and, if a living hostile non-player creature caused the death, starts a new attempt. Environmental deaths and PvP deaths end the existing attempt without starting another.

Each attempt stores the killer's persistent ZDO identity in its instance key. The player must personally kill that exact creature to succeed. Killing a different creature of the same prefab does not count. If someone else or the environment kills the target, the quest is cancelled because it cannot be completed. A subsequent death to the same enemy cancels the old attempt, starts another, and adds another star.

The enemy's network owner applies `SetLevel(currentLevel + 1)`. A persistent receipt on the enemy prevents retries or reconnects from granting additional stars for the same attempt. The increase remains after cancellation, uses the game's level system without a two-star cap, and updates the cached level and visuals on other clients. The quest outcome and target survive world saves. There is no default item reward.

Install both the framework and test plugin on the server and clients to enable revenge quests. The framework alone creates no revenge attempts. The quest ID and instance keys are unchanged, so existing saves resume when the test plugin is installed. As with existing kill observations, modded clients are trusted for game observations; sender identity, pending exploration requests, player proximity, and duplicate deaths are checked by the server. This is not an anti-cheat protocol.

## Verification

For custom quest plugins, subscribe to `RevengeQuestService.PlayerDied` when registering and unsubscribe when unloading. It supplies the player ID, hostile killer ZDO ID (or `None`), and a unique death token on the server. Implement `IRevengeTarget` on a managed quest to let the framework apply and retry its promotion. Use a unique, persistent `InstanceKey` for each attempt; promotion receipts retain the existing `rqf.revenge.` prefix for save compatibility. The service does not register or instantiate concrete quests.

Build with `dotnet build RobleysQuestingFramework.sln -p:SkipPublish=true -p:UseSharedCompilation=false`. Run `dotnet run --project tests/Persistence/Persistence.csproj -p:UseSharedCompilation=false` for persistence and lifecycle regression checks. The harness uses fake game services; real loading, RPC delivery, death attribution and star visuals require in-game verification.

1. On a host and on a dedicated server with a remote client, let a zero-star enemy kill the player. Verify one active revenge quest and a one-star enemy. Reconnect and save/restart; verify neither creates another star or another quest.
2. Kill a different creature of the same type; verify revenge stays active. Kill the actual target; verify completion. Repeat with another player killing the target; verify cancellation.
3. Start revenge and die from falling; verify cancellation without replacement. Die to a different enemy; verify replacement and its star increase. Die to the same enemy again; verify one more star. Check another player's attempt remains independent.
4. Have two players watch the enemy's stars, including after ownership moves between clients. Confirm both see the promoted level and the increase persists after world reload.
5. Register the exploration example at an actual camp. Visit with enemies alive, save/reload, and clear it while inside the radius. Verify progress and one completion. Check another player's visit cannot complete your quest.
6. Approach an unloaded camp, leave before the empty confirmation, and return. Verify it never completes from missing/unloaded enemy objects. Repeat with a giver and verify clearing enables turn-in rather than bypassing it.
