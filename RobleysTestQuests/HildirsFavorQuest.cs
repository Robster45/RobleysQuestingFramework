using System;
using RobleysQuestingFramework;
using UnityEngine;

namespace RobleysTestQuests
{
    public class HildirsFavorQuest : Quest
    {
        public const string ID = "test.hildirs_favor";
        public const string RewardHandler = "robleys.test.hildirs-favor-gold-v1";
        public const int RequiredGold = 500;
        public const int RewardGold = 1000;
        int _discoveries;
        int _spent;

        public HildirsFavorQuest(long playerID) : base(playerID)
        {
            Name = "Hildir's Favor";
        }

        protected override bool RequiresInteraction => true;

        public override string JournalProgress => !Has(Stage, QuestStage.Started)
            ? "Talk to Hildir to accept (reward: 1000 gold)"
            : _spent >= RequiredGold
                ? "Return to Hildir to complete (reward: 1000 gold)"
                : $"Spend gold at Haldor: {_spent}/{RequiredGold} (reward: {RewardGold} gold)";

        protected override void SubscribeDiscoveryEvents()
            => TraderObservationService.DiscoveredLocation += ObserveDiscovery;

        protected override void UnsubscribeDiscoveryEvents()
            => TraderObservationService.DiscoveredLocation -= ObserveDiscovery;

        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(stage => Has(stage, QuestStage.Discovered) && !IsFinishedStage(stage),
                () => TraderObservationService.Interacted += ObserveInteraction,
                () => TraderObservationService.Interacted -= ObserveInteraction);
            RegisterSubscription(stage => Has(stage, QuestStage.Started) && !IsFinishedStage(stage),
                () => TraderObservationService.GoldSpent += ObservePurchase,
                () => TraderObservationService.GoldSpent -= ObservePurchase);
        }

        void ObserveDiscovery(long playerID, string location)
        {
            if (playerID != PlayerID || Stage != QuestStage.Hidden)
            {
                return;
            }
            _discoveries |= location == "Vendor_BlackForest" ? 1 : location == "Hildir_camp" ? 2 : 0;
            if (_discoveries == 3)
            {
                Discover();
                QuestNetwork.Tell(PlayerID, "Hildir's Favor discovered: talk to Hildir to accept her quest.");
            }
        }

        void ObserveInteraction(long playerID, string trader)
        {
            if (playerID != PlayerID || trader != "Hildir" ||
                !Has(Stage, QuestStage.Discovered) || IsFinishedStage(Stage))
            {
                return;
            }
            if (!Has(Stage, QuestStage.Started))
            {
                StartQuest();
                QuestNetwork.Tell(PlayerID, "Hildir's Favor accepted: spend 500 gold at Haldor, then return to Hildir.");
            }
            else if (_spent >= RequiredGold)
            {
                CheckCompletionCriteria();
                CompleteQuest();
                QuestNetwork.Tell(PlayerID, "Hildir's Favor completed: your reward is 1000 gold.");
            }
            else
            {
                QuestNetwork.Tell(PlayerID, $"Hildir's Favor: spend {RequiredGold - _spent} more gold at Haldor, then return to Hildir.");
            }
        }

        void ObservePurchase(long playerID, string trader, int gold)
        {
            if (playerID != PlayerID || trader != "Haldor" || gold <= 0 ||
                !Has(Stage, QuestStage.Started) || IsFinishedStage(Stage))
            {
                return;
            }
            _spent += Math.Min(gold, RequiredGold - _spent);
            NotifyTrackingChanged();
            CheckCompletionCriteria();
        }

        protected override void CheckCompletionCriteria() => SetCriteriaMet(_spent >= RequiredGold);

        // Two discovery bits followed by the cumulative spending count.
        public override int SaveProgress() => (_spent << 2) | _discoveries;

        public override void RestoreProgress(int progress)
        {
            _discoveries = Math.Max(0, progress) & 3;
            _spent = Math.Min(RequiredGold, Math.Max(0, progress) >> 2);
        }

        protected override void ActivateQuestCompletionEffects()
        {
            QuestManager.WorldInstance.QueueReward(this, "gold-payout-v1", RewardHandler);
        }

        public static bool ApplyReward(Player player, string payload)
        {
            if (payload != "" || !ObjectDB.instance)
            {
                return false;
            }
            GameObject coins = ObjectDB.instance.GetItemPrefab("Coins");
            Inventory inventory = player.GetInventory();
            // Check the entire payout before adding anything so a retry cannot duplicate a partial payout.
            if (!coins || !inventory.CanAddItem(coins, RewardGold))
            {
                return false;
            }
            return inventory.AddItem("Coins", RewardGold, 1, 0, 0L, "", false, false) != null;
        }
    }
}
