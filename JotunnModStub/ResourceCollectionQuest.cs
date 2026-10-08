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

            var requirements = new List<ResourceCollectionCriteria>();
            foreach (var requirement in criteria)
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

            SetCriteriaMet(HasRequiredResources());
        }

        bool HasRequiredResources()
        {
            // Exact-quality requirements reserve those items. The total requirement
            // also includes any-quality entries, which must use the remaining items.
            var totalRequired = new Dictionary<string, long>();
            var qualityRequired = new Dictionary<Tuple<string, int>, long>();
            foreach (var requirement in Criteria)
            {
                totalRequired.TryGetValue(requirement.PrefabName, out long total);
                totalRequired[requirement.PrefabName] = total + requirement.Quantity;

                if (requirement.Quality.HasValue)
                {
                    var key = Tuple.Create(requirement.PrefabName, requirement.Quality.Value);
                    qualityRequired.TryGetValue(key, out long quantity);
                    qualityRequired[key] = quantity + requirement.Quantity;
                }
            }

            foreach (var requirement in qualityRequired)
            {
                if (_inventory.CountItems(requirement.Key.Item1, requirement.Key.Item2) < requirement.Value)
                    return false;
            }

            foreach (var requirement in totalRequired)
            {
                if (_inventory.CountItems(requirement.Key) < requirement.Value)
                    return false;
            }

            return true;
        }
    }
}
