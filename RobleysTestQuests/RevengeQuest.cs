using System;
using System.Globalization;
using System.Linq;
using RobleysQuestingFramework;

namespace RobleysTestQuests
{
    /// <summary>A single attempt to avenge a death against one persistent creature.</summary>
    public class RevengeQuest : Quest, IRevengeTarget
    {
        public const string ID = "rqf.revenge";

        public static void Register()
        {
            QuestManager.RegisterQuest(ID, (player, inventory) => new RevengeQuest(player));
            RevengeQuestService.PlayerDied -= OnPlayerDied;
            RevengeQuestService.PlayerDied += OnPlayerDied;
        }

        public static void Unregister()
        {
            RevengeQuestService.PlayerDied -= OnPlayerDied;
        }

        static void OnPlayerDied(long player, ZDOID killer, string death)
        {
            StartAttempt(player, killer, death);
        }

        public ZDOID TargetID
        {
            get
            {
                string[] parts = InstanceKey.Split(':');
                return parts.Length == 3 &&
                    long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) &&
                    uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id)
                    ? new ZDOID(user, id) : ZDOID.None;
            }
        }

        public override string JournalProgress => Has(Stage, QuestStage.Completed)
            ? "Revenge taken" : Has(Stage, QuestStage.Cancelled)
                ? "Revenge lost" : "Kill the creature that killed you before your next death";

        public RevengeQuest(long playerID) : base(playerID)
        {
            Name = "Revenge";
        }

        /// <summary>Ends the previous attempt and starts a unique attempt for an enemy-caused death.</summary>
        public static RevengeQuest StartAttempt(long player, ZDOID killer, string death)
        {
            QuestManager manager = QuestManager.WorldInstance;
            if (!QuestNetwork.IsServer || manager == null)
            {
                return null;
            }
            if (player == 0 || !Guid.TryParseExact(death, "N", out _))
            {
                throw new ArgumentException("Provide a player and unique death token.");
            }
            string key = killer.UserID.ToString(CultureInfo.InvariantCulture) + ":" +
                killer.ID.ToString(CultureInfo.InvariantCulture) + ":" + death;
            // Replayed reports must not cancel an attempt, including after a save/reload.
            if (manager.AllQuests.OfType<RevengeQuest>().Any(quest => quest.PlayerID == player && quest.InstanceKey == key))
            {
                return null;
            }
            foreach (RevengeQuest quest in manager.AllQuests.OfType<RevengeQuest>().ToArray())
            {
                if (quest.PlayerID == player && !IsFinishedStage(quest.Stage))
                {
                    quest.CancelQuest();
                }
            }
            if (killer == ZDOID.None)
            {
                return null;
            }
            RevengeQuest revenge = (RevengeQuest)manager.GetOrAddQuest(player, ID, key, out bool created);
            if (!created)
            {
                return null;
            }
            revenge.Discover();
            return revenge;
        }

        protected override bool RequiresInteraction => false;
        protected override void SubscribeDiscoveryEvents() { }
        protected override void UnsubscribeDiscoveryEvents() { }
        protected override void ActivateQuestCompletionEffects() { }
        protected override void CheckCompletionCriteria() => SetCriteriaMet(true);

        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(stage => Has(stage, QuestStage.Started) && !IsFinishedStage(stage),
                () => KillObservationService.CreatureDied += OnCreatureDied,
                () => KillObservationService.CreatureDied -= OnCreatureDied);
        }

        void OnCreatureDied(long killer, ZDOID victim)
        {
            if (victim != TargetID)
            {
                return;
            }
            if (killer == PlayerID)
            {
                CheckCompletionCriteria();
            }
            else
            {
                // Another player or the environment removed the only valid target.
                CancelQuest();
            }
        }
    }
}
