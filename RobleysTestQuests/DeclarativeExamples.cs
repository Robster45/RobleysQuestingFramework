using System;
using RobleysQuestingFramework;

namespace RobleysTestQuests
{
    public static class DeclarativeExamples
    {
        public static IDisposable Register() => QuestManager.RegisterDefinitions(Plugin.PluginGUID,
            new QuestDefinition("test.defined.necks_then_nest", 1, "Necks, then a nest",
                QuestDiscovery.PlayerAvailable,
                QuestObjective.Sequence("hunt",
                    QuestObjective.Kill("necks", "Neck", 5),
                    QuestObjective.DestroySpawner("nest", "Spawner_GreydwarfNest", 1)),
                new ItemReward("coins", "Coins", 50)),
            new QuestDefinition("test.defined.first_neck", 1, "A personal hunt",
                QuestDiscovery.CreatureEncounter("Neck"),
                QuestObjective.Kill("neck", "Neck", 1),
                new ItemReward("coins", "Coins", 5)));
    }
}
