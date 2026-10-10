using RobleysQuestingFramework;
namespace RobleysTestQuests
{
    public class TestQuest_10Wood : ResourceCollectionQuest
    {
        const int RequiredWood = 10;
        readonly QuestInventory _discoveryInventory;
        public TestQuest_10Wood(QuestInventory inventory, long playerID)
            : base(inventory, playerID, new ResourceCollectionCriteria("Wood", RequiredWood))
        {
            Name = "Gather 10 wood";
            _discoveryInventory = inventory;
        }

        protected override void SubscribeDiscoveryEvents()
            => _discoveryInventory.Changed += DiscoverFromWood;

        protected override void UnsubscribeDiscoveryEvents()
            => _discoveryInventory.Changed -= DiscoverFromWood;

        void DiscoverFromWood()
        {
            if (Stage == QuestStage.Hidden && _discoveryInventory.CountItems("AxeStone") > 0)
                Discover();
        }

        protected override void ActivateQuestCompletionEffects()
        {
            QuestManager.WorldInstance.QueueReward(this, "wood-payout-v1", RewardHandler);
        }

        public const string RewardHandler = "robleys.test.wood-payout-v1";

        public static bool ApplyReward(Player player, string payload)
        {
            if (payload != "" || player.GetInventory().CountItems("$item_wood") < RequiredWood)
                return false;
            player.GetInventory().RemoveItem("$item_wood", RequiredWood);
            player.RaiseSkill(Skills.SkillType.WoodCutting, 100);
            return true;
        }
    }

    public class TestQuest_KillNecks : KillQuest
    {
        static readonly int NeckPrefabHash = "Neck".GetStableHashCode();

        public TestQuest_KillNecks(long playerID)
            : base(playerID, new KillCriteria("Neck", 5))
        {
            Name = "Kill 5 necks";
        }

        protected override void SubscribeDiscoveryEvents()
            => CreatureDiscoveryObservationService.PlayerDiscoveredCreature += DiscoverFromNeck;

        protected override void UnsubscribeDiscoveryEvents()
            => CreatureDiscoveryObservationService.PlayerDiscoveredCreature -= DiscoverFromNeck;

        void DiscoverFromNeck(long playerID, int prefabHash)
        {
            if (Stage == QuestStage.Hidden && playerID == PlayerID && prefabHash == NeckPrefabHash)
                Discover();
        }

        protected override void ActivateQuestCompletionEffects()
        {
            Jotunn.Logger.LogInfo($"QUEST COMPLETED: Triggering retaliatory raid");
            Player player = Player.GetPlayer(PlayerID);

            if (ZNet.instance && ZNet.instance.IsServer() && RandEventSystem.instance && player)
                RandEventSystem.instance.SetRandomEventByName("army_eikthyr", player.transform.position);
        }
    }
}
