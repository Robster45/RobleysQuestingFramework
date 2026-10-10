# RobleysQuestingFramework

Server-owned quest framework for Valheim, with quest persistence, inventory and kill tracking, NPC interactions, hand-ins, and a quest journal.

Requires BepInEx and Jotunn. Install the framework DLL under `BepInEx/plugins` on the server and clients.

Playable examples are supplied separately by the optional `RobleysTestQuests` plugin. The framework alone registers no test quests. Install both plugins to test the wood, neck, and boar quests; remove the test plugin while the game is stopped for framework-only use.

## Source layout

`RobleysQuestingFramework.cs` is the plugin entry point and wires up the framework services. Source files are grouped by responsibility:

| Directory | Responsibility |
| --- | --- |
| `Core/` | Quest lifecycle, registration, player state, and persistence (`Quest`, `QuestManager`). |
| `Quests/Kills/` | Reusable kill quests and matching criteria. |
| `Quests/Clear/` | Reusable clear-area lifecycle, enemy/spawner status and loaded-empty confirmation. |
| `Quests/Explore/` | Reusable visit-and-clear location quests. |
| `Quests/Spawners/` | Reusable discover-and-destroy spawner quests with saved counts. |
| `Quests/Resources/` | Reusable resource collection quests and matching criteria. |
| `Tracking/` | Observing inventory changes, kills, creature discovery, locations and player deaths; enemy promotion through `IRevengeTarget`. |
| `Inventory/` | Server-side inventory model and item identity. |
| `Npcs/` | Quest NPC definitions, spawning, and interactions. |
| `HandIns/` | Item payment transport and resource allocation plans. |
| `Networking/` | Shared network helpers and player lookup. |
| `UI/` | Client quest journal and its synchronization. |
| `Properties/` | Assembly metadata and access attributes. |
| `Package/` | Distribution README, manifest, and icon. |

Start with `Core/Quest.cs` for the quest API and `Quests/` for reusable implementations. See [Writing quests](../QUEST-AUTHORING.md) for authoring guidance and `../RobleysTestQuests/` for playable examples.

See [Explore and revenge quests](../EXPLORE-REVENGE-QUESTS.md) for location authoring, automatic revenge behavior, and multiplayer checks.

See [Clear and destroy-spawner quests](../CLEAR-QUESTS.md) for reusable bases and the per-chamber burial quest. Dungeon entry, loaded-area sampling and networking remain framework services; concrete quest registrations and rewards belong to the test plugin.

All framework types retain the `RobleysQuestingFramework` namespace; folders organize the source without changing the public API. The SDK project automatically includes source files in these subdirectories. The regression harness in `../tests/Persistence/` links selected production files explicitly, so update its project references when moving those files.
