using System;
using RobleysQuestingFramework;
using UnityEngine;

namespace RobleysTestQuests
{
    public class ClearBurialChambersQuest : ClearQuest
    {
        public const string ID = "test.clear_burial_chambers";
        public const string DungeonPrefab = "DG_ForestCrypt";
        public const string RewardHandler = "robleys.test.burial-chambers-coins-v1";
        public const int RewardCoins = 50;
        static bool Registered;
        public ZDOID DungeonID => DungeonEncounter.ParseKey(InstanceKey);
        public override string JournalProgress => base.JournalProgress + " (reward: 50 coins)";
        protected override bool RequiresInteraction => false;

        public ClearBurialChambersQuest(long playerID) : base(playerID)
        {
            Name = "Clear burial chambers";
        }

        public static void Register()
        {
            if (Registered)
            {
                return;
            }
            QuestRewardService.Register(RewardHandler, ApplyReward);
            QuestManager.RegisterQuest(ID, (player, inventory) => new ClearBurialChambersQuest(player));
            DungeonObservationService.Entered -= OnEntered;
            DungeonObservationService.Entered += OnEntered;
            Registered = true;
        }

        public static void Unregister()
        {
            DungeonObservationService.Entered -= OnEntered;
            QuestRewardService.Unregister(RewardHandler);
            Registered = false;
        }

        static void OnEntered(DungeonEncounter encounter)
        {
            QuestManager manager = QuestManager.WorldInstance;
            if (manager == null || encounter.PrefabHash != DungeonPrefab.GetStableHashCode())
            {
                return;
            }
            ClearBurialChambersQuest quest = (ClearBurialChambersQuest)manager.GetOrAddQuest(encounter.PlayerID, ID, encounter.InstanceKey, out bool created);
            quest.RefreshName();
            if (created)
            {
                quest.Discover();
                QuestNetwork.Tell(encounter.PlayerID, "Clear these burial chambers of enemies and spawners to earn 50 coins.");
            }
        }

        protected override IDisposable SubscribeArea(Action<ClearAreaStatus> callback)
            => DungeonObservationService.Subscribe(PlayerID, DungeonID, callback);
        public override void OnWorldReady() => RefreshName();

        void RefreshName()
        {
            if (DungeonObservationService.TryGetPosition(DungeonID, out Vector3 position))
            {
                string name = $"Clear burial chambers ({position.x:0}, {position.z:0})";
                if (Name != name)
                {
                    Name = name;
                    NotifyTrackingChanged();
                }
            }
        }
        protected override void SubscribeDiscoveryEvents() { }
        protected override void UnsubscribeDiscoveryEvents() { }
        protected override void ActivateQuestCompletionEffects()
        {
            QuestManager.WorldInstance.QueueReward(this, "coins-50-v1", RewardHandler);
            QuestNetwork.Tell(PlayerID, "Burial chambers cleared! Your reward is 50 coins.");
        }
        public static bool ApplyReward(Player player, string payload)
            => payload == "" && ItemRewardService.GiveOrDrop(player, "Coins", RewardCoins);
    }
}
