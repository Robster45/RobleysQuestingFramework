namespace RobleysQuestingFramework
{
    // Temporary quest registration; individual quests own their discovery triggers.
    public static class TestQuestBootstrap
    {
        const string WoodQuestID = "test.gather_10_wood";
        const string KillQuestID = "test.kill_5_necks";

        public static void Register()
        {
            QuestManager.RegisterQuest(WoodQuestID, (playerID, inventory) => new TestQuest_10Wood(inventory, playerID));
            QuestManager.RegisterQuest(KillQuestID, (playerID, inventory) => new TestQuest_KillNecks(playerID));
            InventoryObservationService.PlayerAvailable += LoadTestQuests;
        }

        public static void Unregister() => InventoryObservationService.PlayerAvailable -= LoadTestQuests;

        static void LoadTestQuests(long playerID)
        {
            QuestManager.WorldInstance.GetOrAddQuest(playerID, WoodQuestID, out _);
            QuestManager.WorldInstance.GetOrAddQuest(playerID, KillQuestID, out _);
        }
    }
}
