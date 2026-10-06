using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jotunn;

namespace RobleysQuestingFramework
{
    [Flags]
    enum QuestStage
    {
        Hidden = 0,
        Discovered = 1 << 0,
        Started = 1 << 1,
        CriteriaMet = 1 << 2,
        Completed = 1 << 3,
        Cancelled = 1 << 4,
    }

    [Serializable]
    internal abstract class Quest
    {
        public string Name = "Unnamed quest";
        public ZDOID QuestGiverId = ZDOID.None;

        public QuestStage Stage { get; private set; } = QuestStage.Hidden;

        internal void RestoreStage(QuestStage stage)
        {
            if (subscriptionsEnabled)
                throw new InvalidOperationException("Restore state before subscribing.");
            Stage = stage;
        }

        private sealed class SubscriptionRule
        {
            public Func<QuestStage, bool> ShouldBeActive;
            public Action Subscribe;
            public Action Unsubscribe;
            public bool IsActive;
        }

        [NonSerialized]
        private List<SubscriptionRule> subscriptions;

        [NonSerialized]
        private bool subscriptionsEnabled;

        // Called once per runtime instance, including after deserialization.
        protected abstract void ConfigureSubscriptions();

        // Callbacks should only attach/detach listeners, not change quest state.
        protected void RegisterSubscription(Func<QuestStage, bool> condition, Action subscribe, Action unsubscribe)
        {
            if (condition == null) 
                throw new ArgumentNullException(nameof(condition));
            
            if (subscribe == null) 
                throw new ArgumentNullException(nameof(subscribe));
            
            if (unsubscribe == null) 
                throw new ArgumentNullException(nameof(unsubscribe));

            subscriptions.Add(new SubscriptionRule
            {
                ShouldBeActive = condition,
                Subscribe = subscribe,
                Unsubscribe = unsubscribe
            });
        }

        protected static bool Has(QuestStage stage, QuestStage flags)
            => (stage & flags) == flags;

        protected static bool IsFinishedStage(QuestStage stage)
            => (stage & (QuestStage.Completed | QuestStage.Cancelled)) != 0;

        private void RefreshSubscriptions()
        {
            if (subscriptions == null)
                return;

            // Detach obsolete listeners before attaching new ones.
            foreach (var rule in subscriptions)
            {
                if (rule.IsActive &&
                    (!subscriptionsEnabled || !rule.ShouldBeActive(Stage)))
                {
                    rule.Unsubscribe();
                    rule.IsActive = false;
                }
            }

            if (!subscriptionsEnabled)
                return;

            foreach (var rule in subscriptions)
            {
                if (!rule.IsActive && rule.ShouldBeActive(Stage))
                {
                    rule.Subscribe();
                    rule.IsActive = true;
                }
            }
        }

        [field: NonSerialized]
        public event Action<QuestStage, QuestStage> StageChanged;

        protected void SetStage(QuestStage next)
        {
            if (Stage == next)
                return;

            Jotunn.Logger.LogInfo($"{Name} set to next stage: {next}");
            var previous = Stage;
            Stage = next;
            RefreshSubscriptions();
            StageChanged?.Invoke(previous, next);
        }

        public void Discover()
        {
            Jotunn.Logger.LogInfo($"Discovered Quest: {Name}");
            SetStage(Stage | QuestStage.Discovered);
        }

        public void StartQuest()
        {
            if ((Stage & QuestStage.Discovered) == 0 || IsFinished)
                return;

            Jotunn.Logger.LogInfo($"Starting Quest: {Name}");
            SetStage(Stage | QuestStage.Started);
        }

        protected void SetCriteriaMet(bool met)
        {
            if ((Stage & QuestStage.Started) == 0 || IsFinished)
                return;

            SetStage(met
                ? Stage | QuestStage.CriteriaMet
                : Stage & ~QuestStage.CriteriaMet);
        }

        public void CompleteQuest()
        {
            var required = QuestStage.Started | QuestStage.CriteriaMet;

            if (IsFinished || (Stage & required) != required)
                return;

            Jotunn.Logger.LogInfo($"Completing Quest: {Name}");
            SetStage(Stage | QuestStage.Completed);
        }

        public void CancelQuest()
        {
            if (IsFinished)
                return;

            Jotunn.Logger.LogInfo($"Cancelling Quest: {Name}");
            SetStage(
                (Stage & ~(QuestStage.Started |
                           QuestStage.CriteriaMet |
                           QuestStage.Completed))
                | QuestStage.Cancelled);
        }

        private bool IsFinished =>
            (Stage & (QuestStage.Completed | QuestStage.Cancelled)) != 0;

        // Attach/detach this quest's inventory, interaction, etc. listeners.
        public void Subscribe()
        {
            if (subscriptions == null)
            {
                subscriptions = new List<SubscriptionRule>();
                ConfigureSubscriptions();
            }

            subscriptionsEnabled = true;
            RefreshSubscriptions();
        }

        public void Unsubscribe()
        {
            subscriptionsEnabled = false;
            RefreshSubscriptions();
        }
    }
}

