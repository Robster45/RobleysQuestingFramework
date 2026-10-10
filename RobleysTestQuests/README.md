# RobleysTestQuests

Optional development plugin containing Gather 10 wood, Kill 5 necks, A boar's trust, Revenge, Clear the greydwarf nests, Clear burial chambers, and Hildir's Favor. Requires RobleysQuestingFramework and Jotunn.

## Clear burial chambers

Entering a burial chamber automatically starts a quest for that specific chamber. Each player receives at most one attempt per chamber, identified by its persistent dungeon ZDO; re-entering or reconnecting resumes the existing quest. The journal title includes coordinates to distinguish chambers. Other dungeon types do not trigger this quest.

Clear all hostile creatures and active spawners, including evil bone piles. Untriggered creature spawners in unexplored rooms block completion until their defenders spawn and are dealt with. The complete dungeon must be loaded, and three consecutive empty observations are required. Outdoor enemies and neighbouring dungeons do not count. Leaving or dying retains the quest; return to finish it. Other players may help, and a first visit to an already-cleared chamber qualifies once the empty-state checks pass.

Completion automatically grants **50 coins** through the persistent reward outbox. Full inventories receive the remainder as a ground drop. Every chamber has its own reward receipt; a completed chamber does not pay again on later entry.

Quest ID: `test.clear_burial_chambers`. The quest derives from the framework's `ClearQuest`; dungeon observation and networking remain in the framework. See [authoring and in-game checks](../CLEAR-QUESTS.md).

## Clear the greydwarf nests

Find a greydwarf nest by approaching within 30 metres. The quest automatically discovers and starts, then tracks three nests personally destroyed, including the first one. Destroying a nest at range before the discovery poll also starts the quest and counts that nest. Other spawners, greydwarf creature kills, and other players' destruction do not count. Progress survives world saves and reconnects.

The third nest automatically completes the quest and queues one full-durability **quality 6 bronze axe**. The reward goes into the player's inventory, or drops beside them if it is full. It uses the existing persistent reward outbox and receipt system. The item's quality is set explicitly, without changing the bronze axe prefab or its crafting upgrade limit.

Quest ID: `test.clear_3_greydwarf_nests`. The concrete quest is in `GreydwarfNestsQuest.cs`, deriving from the reusable `DestroySpawnersQuest`; generic discovery/destruction observations remain in the framework's `SpawnerObservationService`. Existing IDs, saved counts and reward receipts are unchanged. Prefabs are `Spawner_GreydwarfNest` and `AxeBronze`, verified against the [Jötunn prefab list](https://valheim-modding.github.io/Jotunn/data/prefabs/prefab-list.html) and [item list](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html).

In-game checks on both host and remote client: approach the first nest and verify discovery at 0/3; destroy it and verify 1/3; save/restart and verify progress; destroy two more and verify exactly one quality 6 bronze axe at full durability. Repeat with a full inventory, pick up the dropped reward, and save/reconnect to check retained quality. Confirm unrelated spawners, creature kills, and another player's nest destruction do not advance your quest. Check a first ranged destruction starts at 1/3, and that later nests or reconnecting after completion do not repeat the reward. The automated harness checks quest/reward logic with fake game boundaries; real RPCs and Harmony hooks require these in-game checks.

## Revenge

The automatic Revenge quest starts when an enemy kills a player, promotes that enemy by one star, and ends when the player kills that exact enemy or dies again. Its quest rules and registration live in this plugin; the framework provides death events, network transport and promotion handling. See [Explore and revenge quests](../EXPLORE-REVENGE-QUESTS.md) for details and verification steps.

## Hildir's Favor

Discovered once Haldor's and Hildir's location icons are revealed and their positions are explored on the player's map. Existing explored trader locations count; discovery is checked every five seconds. Discovery alone does not start the quest: talk to Hildir to accept it. Discovery flags and progress are saved per character in the world quest save.

Spend a cumulative 500 gold at Haldor after accepting, then talk to Hildir again to complete the quest and receive 1000 gold. Purchases before acceptance, sales, failed purchases, and purchases from other traders do not count. Normal shop interactions remain available throughout: talking to Hildir also opens her normal shop. The framework observes interactions without replacing `Trader.Interact`. The reward uses the persistent delivery outbox; coins fill available inventory space and any overflow drops beside the player in valid stacks. Dropped items count as delivered, so normal delivery retries do not repeat the payout. Existing started quests retain their progress, and completed quests remain completed.

In-game checks (host and remote client): reveal one trader and verify the quest stays hidden; reveal the second and wait for the journal to say to talk to Hildir. Purchases must not count until talking to her to accept. Confirm both shops still open. Buy at Hildir, sell valuables, and attempt a purchase without enough gold or inventory space; none should advance progress. Spend below 500 at Haldor, talk to Hildir, save/reconnect, and verify the quest remains active with its count preserved. Cross 500 with another purchase and verify no payout until returning to Hildir. Save/reconnect before returning, then talk to her for one 1000-coin payout. Repeated interactions and reconnects must not pay again. Repeat with full and partially full inventories; verify inventory coins plus ground coins total 1000 and that reconnecting does not produce another payout. Another character's discoveries, interactions, and purchases must not advance yours.

Install both plugin DLLs in separate folders under `BepInEx/plugins` on the server and all clients. Remove the RobleysTestQuests folder while the game is stopped to run only the framework. Restart after changing plugins; runtime unloading is not supported.

Quest IDs, boar world keys, and payment receipts are unchanged. Progress remains in the framework's world save; unavailable quest records are retained and resume when this plugin is installed again. Existing spawned boars remain in the world when the plugin is removed.

See [the boar guide](../BOAR-QUEST.md) for gameplay checks. The wood quest queues an owner-client payout: remove 10 wood and raise WoodCutting by 100 through `RaiseSkill`. If the character no longer has 10 wood, delivery remains pending until they do. Both plugins must be installed on the owning client. See [reward delivery](../QUEST-AUTHORING.md#owner-client-rewards) for retries and persistence limits.

Debug builds deploy only this plugin DLL and symbols. Release builds produce a local installation ZIP; Thunderstore publication requires adding a manifest with the framework's published dependency identifier and an icon.

## Declarative examples

`DeclarativeExamples.Register()` is called from plugin Awake and its disposable handle is released in OnDestroy. It adds `test.defined.necks_then_nest` (personally kill five Necks, then destroy one nest, receive 50 Coins) and `test.defined.first_neck` (encounter a Neck, personally kill one, receive 5 Coins). Existing quest IDs and rewards are unchanged. See [declarative authoring and manual checks](../DECLARATIVE-QUESTS.md).
