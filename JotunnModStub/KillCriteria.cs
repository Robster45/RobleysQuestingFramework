using System;

namespace RobleysQuestingFramework
{
    /// <summary>
    /// The creature prefab and number of kills required from the quest's owner.
    /// This immutable configuration can be shared; progress belongs to each quest.
    /// </summary>
    public sealed class KillCriteria
    {
        public string PrefabName { get; }
        public int Quantity { get; }

        public KillCriteria(string prefabName, int quantity)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
                throw new ArgumentException("A kill requirement needs a creature prefab name.", nameof(prefabName));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");

            PrefabName = prefabName;
            Quantity = quantity;
        }
    }
}
