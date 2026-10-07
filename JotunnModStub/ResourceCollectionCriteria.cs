using System;

namespace RobleysQuestingFramework
{
    /// <summary>
    /// One required resource, identified by prefab name rather than its translated display name.
    /// A null quality accepts any quality; a value requires that exact quality.
    /// </summary>
    public sealed class ResourceCollectionCriteria
    {
        public string PrefabName { get; }
        public int Quantity { get; }
        public int? Quality { get; }

        public ResourceCollectionCriteria(string prefabName, int quantity, int? quality = null)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
                throw new ArgumentException("A resource needs a prefab name.", nameof(prefabName));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
            if (quality.HasValue && quality.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be positive when specified.");

            PrefabName = prefabName;
            Quantity = quantity;
            Quality = quality;
        }
    }
}
