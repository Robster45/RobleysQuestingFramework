using RobleysQuestingFramework;

namespace RobleysTestQuests
{
    public class GreydwarfNestsQuest : DestroySpawnersQuest
    {
        public const string ID = "test.clear_3_greydwarf_nests";
        public const string NestPrefab = "Spawner_GreydwarfNest";
        public const string RewardHandler = "robleys.test.greydwarf-nests-axe-v1";
        public const int RequiredNests = 3;
        public int ClearedNests => DestroyedSpawners;

        public GreydwarfNestsQuest(long playerID) : base(playerID, NestPrefab, RequiredNests)
        {
            Name = "Clear the greydwarf nests";
        }

        public override string JournalProgress => $"Destroy greydwarf nests: {ClearedNests}/{RequiredNests} (reward: quality 6 bronze axe)";
        protected override bool RequiresInteraction => false;
        protected override void OnSpawnerDiscovered()
            => QuestNetwork.Tell(PlayerID, "Clear the greydwarf nests: destroy 3 nests to earn a quality 6 bronze axe.");
        protected override void ActivateQuestCompletionEffects()
        {
            QuestManager.WorldInstance.QueueReward(this, "bronze-axe-quality-6-v1", RewardHandler);
            QuestNetwork.Tell(PlayerID, "Greydwarf nests cleared! Your reward is a quality 6 bronze axe.");
        }

        public static bool ApplyReward(Player player, string payload)
            => payload == "" && ItemRewardService.GiveOrDrop(player, "AxeBronze", 1, 6);
    }
}
