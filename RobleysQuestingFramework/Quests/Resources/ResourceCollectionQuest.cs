using System;
using System.Collections.Generic;

namespace RobleysQuestingFramework
{
    /// <summary>
    /// Observes the owner's inventory and requires all configured resources.
    /// Derived quests provide their completion effects.
    /// </summary>
    public abstract class ResourceCollectionQuest : Quest
    {
        readonly QuestInventory _inventory;
        long[] _journalCounts;
        public override string JournalProgress
        {
            get
            {
                if (_journalCounts == null)
                    return "Waiting for inventory...";

                List<string> lines = new List<string>();
                for (int i = 0; i < Criteria.Count; i++)
                {
                    ResourceCollectionCriteria requirement = Criteria[i];
                    string quality = requirement.Quality.HasValue ? $" (quality {requirement.Quality})" : "";
                    lines.Add($"{requirement.PrefabName}{quality}: {_journalCounts[i]}/{requirement.Quantity}");
                }
                return string.Join("\n", lines);
            }
        }

        public IReadOnlyList<ResourceCollectionCriteria> Criteria { get; }

        public ResourceCollectionQuest(long playerID, params ResourceCollectionCriteria[] criteria)
            : this(InventoryObservationService.ForPlayer(playerID), playerID, criteria)
        {

        }

        public ResourceCollectionQuest(QuestInventory inventory, long playerID,
            params ResourceCollectionCriteria[] criteria) : base(playerID)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (criteria == null)
                throw new ArgumentNullException(nameof(criteria));
            if (criteria.Length == 0)
                throw new ArgumentException("Configure at least one resource requirement.", nameof(criteria));

            List<ResourceCollectionCriteria> requirements = new List<ResourceCollectionCriteria>();
            foreach (ResourceCollectionCriteria requirement in criteria)
            {
                if (requirement == null)
                    throw new ArgumentException("Resource requirements cannot be null.", nameof(criteria));
                requirements.Add(requirement);
            }

            _inventory = inventory;
            Criteria = requirements.AsReadOnly();
        }

        /// <summary>
        /// Observes inventory changes while the quest is started and unfinished.
        /// Overrides can call base.ConfigureSubscriptions() before registering additional listeners.
        /// </summary>
        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(
                stage => Has(stage, QuestStage.Started) && !IsFinishedStage(stage),
                () => _inventory.Changed += CheckCompletionCriteria,
                () => _inventory.Changed -= CheckCompletionCriteria);
        }

        /// <summary>
        /// Updates criteria from the latest inventory snapshot without counting any item twice.
        /// SetCriteriaMet handles automatic completion when no quest giver is assigned.
        /// </summary>
        protected override void CheckCompletionCriteria()
        {
            if (!_inventory.HasSnapshot)
                return;

            // Keep counts before completion can invalidate the inventory snapshot.
            // Format journal text lazily, only when a visible client needs it.
            _journalCounts = new long[Criteria.Count];
            for (int i = 0; i < Criteria.Count; i++)
                _journalCounts[i] = Math.Min(_inventory.CountItems(Criteria[i].PrefabName, Criteria[i].Quality), Criteria[i].Quantity);
            SetCriteriaMet(HasRequiredResources());
            NotifyTrackingChanged();
        }

        bool HasRequiredResources()
        {
            // Exact-quality requirements reserve those items. The total requirement
            // also includes any-quality entries, which must use the remaining items.
            Dictionary<string, long> totalRequired = new Dictionary<string, long>();
            Dictionary<InventoryItemKey, long> qualityRequired = new Dictionary<InventoryItemKey, long>();
            foreach (ResourceCollectionCriteria requirement in Criteria)
            {
                totalRequired.TryGetValue(requirement.PrefabName, out long total);
                totalRequired[requirement.PrefabName] = total + requirement.Quantity;

                if (requirement.Quality.HasValue)
                {
                    InventoryItemKey key = new InventoryItemKey(requirement.PrefabName, requirement.Quality.Value);
                    qualityRequired.TryGetValue(key, out long quantity);
                    qualityRequired[key] = quantity + requirement.Quantity;
                }
            }

            foreach (KeyValuePair<InventoryItemKey, long> requirement in qualityRequired)
            {
                if (_inventory.CountItems(requirement.Key.PrefabName, requirement.Key.Quality) < requirement.Value)
                    return false;
            }

            foreach (KeyValuePair<string, long> requirement in totalRequired)
            {
                if (_inventory.CountItems(requirement.Key) < requirement.Value)
                    return false;
            }

            return true;
        }
    }
}
