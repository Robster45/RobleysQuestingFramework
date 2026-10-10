# RobleysTestQuests

Optional development plugin containing Gather 10 wood, Kill 5 necks, and A boar's trust. Requires RobleysQuestingFramework and Jotunn.

Install both plugin DLLs in separate folders under `BepInEx/plugins` on the server and all clients. Remove the RobleysTestQuests folder while the game is stopped to run only the framework. Restart after changing plugins; runtime unloading is not supported.

Quest IDs, boar world keys, and payment receipts are unchanged. Progress remains in the framework's world save; unavailable quest records are retained and resume when this plugin is installed again. Existing spawned boars remain in the world when the plugin is removed.

See [the boar guide](../BOAR-QUEST.md) for gameplay checks. The wood quest queues an owner-client payout: remove 10 wood and raise WoodCutting by 100 through `RaiseSkill`. If the character no longer has 10 wood, delivery remains pending until they do. Both plugins must be installed on the owning client. See [reward delivery](../QUEST-AUTHORING.md#owner-client-rewards) for retries and persistence limits.

Debug builds deploy only this plugin DLL and symbols. Release builds produce a local installation ZIP; Thunderstore publication requires adding a manifest with the framework's published dependency identifier and an icon.
