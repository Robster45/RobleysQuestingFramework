using System;

namespace RobleysQuestingFramework
{
    /// <summary>
    /// Counts the owner's kills of one configured creature prefab.
    /// Derived quests supply completion effects; the base owns observation and saved progress.
    /// </summary>
    public abstract class KillQuest : Quest
    {
        readonly int _creaturePrefabHash;

        public KillCriteria Criteria { get; }
        public int KillCount { get; private set; }

        protected KillQuest(long playerID, KillCriteria criteria) : base(playerID)
        {
            Criteria = criteria ?? throw new ArgumentNullException(nameof(criteria));
            _creaturePrefabHash = criteria.PrefabName.GetStableHashCode();
        }

        /// <summary>
        /// Returns the current kill count for the existing integer progress save field.
        /// </summary>
        internal override int SaveProgress() => KillCount;

        /// <summary>
        /// Restores the saved kill count, clamped to the configured requirement.
        /// Loading progress does not trigger completion effects.
        /// </summary>
        internal override void RestoreProgress(int progress)
        {
            KillCount = Math.Max(0, Math.Min(Criteria.Quantity, progress));
        }

        /// <summary>
        /// Observes kills while started and unfinished, until the required count is reached.
        /// Overrides can call base.ConfigureSubscriptions() to retain this observation.
        /// </summary>
        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(
                stage => Has(stage, QuestStage.Started) && !Has(stage, QuestStage.CriteriaMet) && !IsFinishedStage(stage),
                SubscribeKillEvents,
                UnsubscribeKillEvents);
        }

        /// <summary>
        /// Updates criteria from the kill count. Quests without a giver complete automatically;
        /// quests with a giver remain ready for manual turn-in.
        /// </summary>
        protected override void CheckCompletionCriteria()
        {
            SetCriteriaMet(KillCount >= Criteria.Quantity);
        }

        void SubscribeKillEvents()
        {
            KillObservationService.PlayerKilledCreature += RegisterKillEvent;
        }

        void UnsubscribeKillEvents()
        {
            KillObservationService.PlayerKilledCreature -= RegisterKillEvent;
        }

        void RegisterKillEvent(long killerID, int prefabHash)
        {
            if (killerID != PlayerID || prefabHash != _creaturePrefabHash)
                return;

            if (KillCount < Criteria.Quantity)
                KillCount++;

            CheckCompletionCriteria();
        }
    }
}
