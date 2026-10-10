# RobleysTestQuests

Optional development plugin containing Gather 10 wood, Kill 5 necks, A boar's trust, and Hildir's Favor. Requires RobleysQuestingFramework and Jotunn.

## Hildir's Favor

Discovered once Haldor's and Hildir's location icons are revealed and their positions are explored on the player's map. Existing explored trader locations count; discovery is checked every five seconds. Discovery alone does not start the quest: talk to Hildir to accept it. Discovery flags and progress are saved per character in the world quest save.

Spend a cumulative 500 gold at Haldor after accepting, then talk to Hildir again to complete the quest and receive 1000 gold. Purchases before acceptance, sales, failed purchases, and purchases from other traders do not count. Normal shop interactions remain available throughout: talking to Hildir also opens her normal shop. The framework observes interactions without replacing `Trader.Interact`. The reward uses the persistent delivery outbox; coins fill available inventory space and any overflow drops beside the player in valid stacks. Dropped items count as delivered, so normal delivery retries do not repeat the payout. Existing started quests retain their progress, and completed quests remain completed.

In-game checks (host and remote client): reveal one trader and verify the quest stays hidden; reveal the second and wait for the journal to say to talk to Hildir. Purchases must not count until talking to her to accept. Confirm both shops still open. Buy at Hildir, sell valuables, and attempt a purchase without enough gold or inventory space; none should advance progress. Spend below 500 at Haldor, talk to Hildir, save/reconnect, and verify the quest remains active with its count preserved. Cross 500 with another purchase and verify no payout until returning to Hildir. Save/reconnect before returning, then talk to her for one 1000-coin payout. Repeated interactions and reconnects must not pay again. Repeat with full and partially full inventories; verify inventory coins plus ground coins total 1000 and that reconnecting does not produce another payout. Another character's discoveries, interactions, and purchases must not advance yours.

Install both plugin DLLs in separate folders under `BepInEx/plugins` on the server and all clients. Remove the RobleysTestQuests folder while the game is stopped to run only the framework. Restart after changing plugins; runtime unloading is not supported.

Quest IDs, boar world keys, and payment receipts are unchanged. Progress remains in the framework's world save; unavailable quest records are retained and resume when this plugin is installed again. Existing spawned boars remain in the world when the plugin is removed.

See [the boar guide](../BOAR-QUEST.md) for gameplay checks. The wood quest queues an owner-client payout: remove 10 wood and raise WoodCutting by 100 through `RaiseSkill`. If the character no longer has 10 wood, delivery remains pending until they do. Both plugins must be installed on the owning client. See [reward delivery](../QUEST-AUTHORING.md#owner-client-rewards) for retries and persistence limits.

Debug builds deploy only this plugin DLL and symbols. Release builds produce a local installation ZIP; Thunderstore publication requires adding a manifest with the framework's published dependency identifier and an icon.
