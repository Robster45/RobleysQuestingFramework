using RobleysQuestingFramework;
namespace RobleysTestQuests
{
    // Temporary quest registration; individual quests own their discovery triggers.
    public static class TestQuestBootstrap
    {
        const string WoodQuestID = "test.gather_10_wood";
        const string KillQuestID = "test.kill_5_necks";

        public static void Register()
        {
            RevengeQuest.Register();
            ClearBurialChambersQuest.Register();
            QuestRewardService.Register(GreydwarfNestsQuest.RewardHandler, GreydwarfNestsQuest.ApplyReward);
            QuestManager.RegisterQuest(GreydwarfNestsQuest.ID, (playerID, inventory) => new GreydwarfNestsQuest(playerID),
                discoverForEveryPlayer: true);
            QuestRewardService.Register(HildirsFavorQuest.RewardHandler, HildirsFavorQuest.ApplyReward);
            QuestManager.RegisterQuest(HildirsFavorQuest.ID, (playerID, inventory) => new HildirsFavorQuest(playerID),
                discoverForEveryPlayer: true);
            QuestRewardService.Register(TestQuest_10Wood.RewardHandler, TestQuest_10Wood.ApplyReward);
            QuestManager.RegisterQuest(WoodQuestID, (playerID, inventory) => new TestQuest_10Wood(inventory, playerID));
            QuestManager.RegisterQuest(KillQuestID, (playerID, inventory) => new TestQuest_KillNecks(playerID));
            InventoryObservationService.PlayerAvailable += LoadTestQuests;
        }

        public static void Unregister()
        {
            RevengeQuest.Unregister();
            ClearBurialChambersQuest.Unregister();
            QuestRewardService.Unregister(GreydwarfNestsQuest.RewardHandler);
            QuestRewardService.Unregister(HildirsFavorQuest.RewardHandler);
            InventoryObservationService.PlayerAvailable -= LoadTestQuests;
            QuestRewardService.Unregister(TestQuest_10Wood.RewardHandler);
        }

        static void LoadTestQuests(long playerID)
        {
            QuestManager.WorldInstance.GetOrAddQuest(playerID, WoodQuestID, out _);
            QuestManager.WorldInstance.GetOrAddQuest(playerID, KillQuestID, out _);
        }
    }
}
