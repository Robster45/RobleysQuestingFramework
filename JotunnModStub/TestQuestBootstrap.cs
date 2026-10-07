namespace RobleysQuestingFramework
{
    // Temporary quest registration and discovery; no inventory or networking logic.
    public static class TestQuestBootstrap
    {
        const string QuestID = "test.gather_10_wood";
        const string KillQuestID = "test.kill_5_necks";
        internal static void Register()
        {
            QuestManager.RegisterQuest(QuestID, (playerID, inventory) => new TestQuest_10Wood(inventory, playerID));
            QuestManager.RegisterQuest(KillQuestID, (playerID, inventory) => new TestQuest_KillNecks(playerID));
            InventoryObservationService.PlayerAvailable += StartTestQuest;
        }

        internal static void Unregister() =>
            InventoryObservationService.PlayerAvailable -= StartTestQuest;

        static void StartTestQuest(long playerID)
        {
            StartQuest(playerID, QuestID);
            StartQuest(playerID, KillQuestID);
        }

        static void StartQuest(long playerID, string questID)
        {
            var quest = QuestManager.WorldInstance.GetOrAddQuest(playerID, questID, out bool created);
            if (!created) return;
            quest.Discover();
            quest.StartQuest();
        }
    }
}
