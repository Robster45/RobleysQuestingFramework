using System;
using System.Collections.Generic;

namespace RobleysQuestingFramework
{
    [Flags]
    public enum QuestStage
    {
        Hidden = 0,
        Discovered = 1 << 0,
        Started = 1 << 1,
        CriteriaMet = 1 << 2,
        Completed = 1 << 3,
        Cancelled = 1 << 4,
    }

    [Serializable]
    public abstract class Quest
    {
        public string Name = "Unnamed quest";

        // Presentation only; quest authors can override this for custom objectives.
        public virtual string JournalProgress => "";
        public static event Action<Quest> TrackingChanged;
        protected void NotifyTrackingChanged() => TrackingChanged?.Invoke(this);

        public ZDOID QuestGiverId = ZDOID.None;

        [NonSerialized]
        List<SubscriptionRule> _subscriptions;

        [NonSerialized]
        bool _subscriptionsEnabled;

        [field: NonSerialized]
        public event Action<QuestStage, QuestStage> StageChanged;

        [field: NonSerialized]
        public event Action Completed;

        // Stable character ID, also stored in QuestManager's save record.
        public long PlayerID { get; }

        public QuestStage Stage { get; private set; } = QuestStage.Hidden;

        bool IsFinished =>
            (Stage & (QuestStage.Completed | QuestStage.Cancelled)) != 0;

        protected Quest(long playerID)
        {
            if (playerID == 0)
                throw new ArgumentException("A quest needs a valid character ID.", nameof(playerID));

            PlayerID = playerID;
        }

        /// <summary>
        /// Checks current progress and calls SetCriteriaMet to update the quest's criteria.
        /// Quest event handlers call this method when relevant progress changes.
        /// </summary>
        protected abstract void CheckCompletionCriteria();

        /// <summary>
        /// Applies this quest's rewards or other completion effects after its stage becomes Completed.
        /// Called once for a successful completion transition, never when loading saved state.
        /// </summary>
        protected abstract void ActivateQuestCompletionEffects();

        /// <summary>
        /// Registers the quest's event listeners and the stages in which they should be active.
        /// Called on the first Subscribe call for each runtime instance, including after deserialization.
        /// </summary>
        protected abstract void ConfigureSubscriptions();

        /// <summary>
        /// Attaches discovery watchers for this player while the quest is Hidden.
        /// Only attach listeners here; discover the quest from an event callback.
        /// </summary>
        protected abstract void SubscribeDiscoveryEvents();

        /// <summary>
        /// Removes discovery watchers when leaving Hidden or unloading the quest.
        /// </summary>
        protected abstract void UnsubscribeDiscoveryEvents();

        /// <summary>
        /// Returns the quest-specific progress value to store in the world save.
        /// Override this for quests that track progress; the default returns zero.
        /// </summary>
        public virtual int SaveProgress() => 0;

        /// <summary>
        /// Restores quest-specific progress from the world save before listeners are attached.
        /// Override this to validate and apply the saved value; the default does nothing.
        /// </summary>
        public virtual void RestoreProgress(int progress) { }

        /// <summary>
        /// Called once after activation when the server world is ready. Reconcile persistent
        /// world effects here; do not replay consumable rewards. Subscriptions are already attached.
        /// </summary>
        public virtual void OnWorldReady() { }

        public void RestoreStage(QuestStage stage)
        {
            if (_subscriptionsEnabled)
                throw new InvalidOperationException("Restore state before subscribing.");
            Stage = stage;
        }

        public void Discover()
        {
            Jotunn.Logger.LogInfo($"Discovered Quest: {Name}");
            SetStage(Stage | QuestStage.Discovered);

            if (QuestGiverId == ZDOID.None)
                StartQuest();
        }

        public void StartQuest()
        {
            if ((Stage & QuestStage.Discovered) == 0 || IsFinished)
                return;

            Jotunn.Logger.LogInfo($"Starting Quest: {Name}");
            SetStage(Stage | QuestStage.Started);

            if (MessageHud.instance)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"{Name} Started");
        }

        public void CompleteQuest()
        {
            QuestStage required = QuestStage.Started | QuestStage.CriteriaMet;

            if (IsFinished || (Stage & required) != required)
                return;

            Jotunn.Logger.LogInfo($"Completing Quest: {Name}");
            SetStage(Stage | QuestStage.Completed);
            ActivateQuestCompletionEffects();

            if (MessageHud.instance)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"{Name} Completed");

            Completed?.Invoke();
        }

        public void CancelQuest()
        {
            if (IsFinished)
                return;

            Jotunn.Logger.LogInfo($"Cancelling Quest: {Name}");
            SetStage((Stage & ~(QuestStage.Started | QuestStage.CriteriaMet | QuestStage.Completed)) | QuestStage.Cancelled);
        }

        // Attach/detach this quest's inventory, interaction, etc. listeners.
        public void Subscribe()
        {
            if (_subscriptions == null)
            {
                _subscriptions = new List<SubscriptionRule>();
                RegisterSubscription(
                    stage => stage == QuestStage.Hidden,
                    SubscribeDiscoveryEvents,
                    UnsubscribeDiscoveryEvents);
                ConfigureSubscriptions();
            }

            _subscriptionsEnabled = true;
            RefreshSubscriptions();
        }

        public void Unsubscribe()
        {
            _subscriptionsEnabled = false;
            RefreshSubscriptions();
        }

        // Callbacks should only attach/detach listeners, not change quest state.
        protected void RegisterSubscription(Func<QuestStage, bool> condition, Action subscribe, Action unsubscribe)
        {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));

            if (subscribe == null)
                throw new ArgumentNullException(nameof(subscribe));

            if (unsubscribe == null)
                throw new ArgumentNullException(nameof(unsubscribe));

            _subscriptions.Add(new SubscriptionRule
            {
                ShouldBeActive = condition,
                Subscribe = subscribe,
                Unsubscribe = unsubscribe
            });
        }

        protected void SetCriteriaMet(bool met)
        {
            if ((Stage & QuestStage.Started) == 0 || IsFinished)
                return;

            SetStage(met
                ? Stage | QuestStage.CriteriaMet
                : Stage & ~QuestStage.CriteriaMet);

            if (met && QuestGiverId == ZDOID.None)
                CompleteQuest();
        }

        protected void SetStage(QuestStage next)
        {
            if (Stage == next)
                return;

            Jotunn.Logger.LogInfo($"{Name} set to next stage: {next}");
            QuestStage previous = Stage;
            Stage = next;
            RefreshSubscriptions();
            StageChanged?.Invoke(previous, next);
            NotifyTrackingChanged();
        }

        protected static bool Has(QuestStage stage, QuestStage flags)
            => (stage & flags) == flags;

        protected static bool IsFinishedStage(QuestStage stage)
            => (stage & (QuestStage.Completed | QuestStage.Cancelled)) != 0;

        void RefreshSubscriptions()
        {
            if (_subscriptions == null)
                return;

            // Detach obsolete listeners before attaching new ones.
            foreach (SubscriptionRule rule in _subscriptions)
            {
                if (rule.IsActive &&
                    (!_subscriptionsEnabled || !rule.ShouldBeActive(Stage)))
                {
                    rule.Unsubscribe();
                    rule.IsActive = false;
                }
            }

            if (!_subscriptionsEnabled)
                return;

            foreach (SubscriptionRule rule in _subscriptions)
            {
                if (!rule.IsActive && rule.ShouldBeActive(Stage))
                {
                    rule.Subscribe();
                    rule.IsActive = true;
                }
            }
        }

        class SubscriptionRule
        {
            public Func<QuestStage, bool> ShouldBeActive;
            public Action Subscribe;
            public Action Unsubscribe;
            public bool IsActive;
        }
    }
}
