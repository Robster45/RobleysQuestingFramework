using System;

namespace RobleysQuestingFramework
{
    /// <summary>Clear a visited area. The observation source defines its bounds and readiness.</summary>
    public abstract class ClearQuest : Quest
    {
        IDisposable _watch;
        protected bool AreaCleared { get; private set; }
        public bool Visited { get; private set; }
        public int RemainingEnemies { get; private set; } = -1;
        public int RemainingSpawners { get; private set; } = -1;
        public override string JournalProgress => AreaCleared ? "Area cleared"
            : RemainingEnemies < 0 ? "Enter the area and clear its enemies and spawners"
            : $"Clear the area: {RemainingEnemies} enemies, {RemainingSpawners} spawners remaining";

        protected ClearQuest(long playerID) : base(playerID) { }

        // A zero observation must already have passed the source's loading/settling checks.
        protected abstract IDisposable SubscribeArea(Action<ClearAreaStatus> callback);

        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(stage => Has(stage, QuestStage.Started) && !Has(stage, QuestStage.CriteriaMet) && !IsFinishedStage(stage),
                () => _watch = SubscribeArea(Observe),
                () => { _watch?.Dispose(); _watch = null; });
        }

        void Observe(ClearAreaStatus status)
        {
            bool changed = !Visited || RemainingEnemies != status.Enemies || RemainingSpawners != status.Spawners;
            Visited = true;
            RemainingEnemies = status.Enemies;
            RemainingSpawners = status.Spawners;
            AreaCleared = status.IsClear;
            CheckCompletionCriteria();
            if (changed)
            {
                NotifyTrackingChanged();
            }
        }

        public override int SaveProgress() => AreaCleared ? 2 : Visited ? 1 : 0;
        public override void RestoreProgress(int progress)
        {
            Visited = progress > 0;
            AreaCleared = progress >= 2;
        }
        protected override void CheckCompletionCriteria() => SetCriteriaMet(Visited && AreaCleared);
    }
}
