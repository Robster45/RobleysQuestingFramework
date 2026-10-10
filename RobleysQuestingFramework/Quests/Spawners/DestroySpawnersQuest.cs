using System;

namespace RobleysQuestingFramework
{
    /// <summary>Counts personal destruction of one spawner prefab; discovers on its first encounter.</summary>
    public abstract class DestroySpawnersQuest : Quest
    {
        readonly int _prefabHash;
        public string SpawnerPrefab { get; }
        public int RequiredSpawners { get; }
        public int DestroyedSpawners { get; private set; }
        public override string JournalProgress => $"Destroy spawners: {DestroyedSpawners}/{RequiredSpawners}";

        protected DestroySpawnersQuest(long playerID, string prefab, int quantity) : base(playerID)
        {
            if (string.IsNullOrWhiteSpace(prefab) || quantity <= 0)
            {
                throw new ArgumentException("Provide a spawner prefab and positive quantity.");
            }
            SpawnerPrefab = prefab;
            RequiredSpawners = quantity;
            _prefabHash = prefab.GetStableHashCode();
        }

        protected override void SubscribeDiscoveryEvents() => SpawnerObservationService.PlayerDiscoveredSpawner += OnDiscovery;
        protected override void UnsubscribeDiscoveryEvents() => SpawnerObservationService.PlayerDiscoveredSpawner -= OnDiscovery;
        protected virtual void OnSpawnerDiscovered() { }

        void OnDiscovery(long player, int prefab)
        {
            if (player == PlayerID && prefab == _prefabHash && Stage == QuestStage.Hidden)
            {
                Discover();
                OnSpawnerDiscovered();
            }
        }

        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(stage => Has(stage, QuestStage.Started) && !Has(stage, QuestStage.CriteriaMet) && !IsFinishedStage(stage),
                () => SpawnerObservationService.PlayerDestroyedSpawner += OnDestroyed,
                () => SpawnerObservationService.PlayerDestroyedSpawner -= OnDestroyed);
        }

        void OnDestroyed(long player, int prefab)
        {
            if (player != PlayerID || prefab != _prefabHash)
            {
                return;
            }
            DestroyedSpawners = Math.Min(RequiredSpawners, DestroyedSpawners + 1);
            CheckCompletionCriteria();
            NotifyTrackingChanged();
        }

        public override int SaveProgress() => DestroyedSpawners;
        public override void RestoreProgress(int progress) => DestroyedSpawners = Math.Max(0, Math.Min(RequiredSpawners, progress));
        protected override void CheckCompletionCriteria() => SetCriteriaMet(DestroyedSpawners >= RequiredSpawners);
    }
}
