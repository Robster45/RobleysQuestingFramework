using System;

namespace RobleysQuestingFramework
{
    // Identifies an inventory item by prefab and quality, independently of its stack count.
    public readonly struct InventoryItemKey : IEquatable<InventoryItemKey>
    {
        public string PrefabName { get; }
        public int Quality { get; }

        public InventoryItemKey(string prefabName, int quality)
        {
            PrefabName = prefabName;
            Quality = quality;
        }

        public bool Equals(InventoryItemKey other)
            => string.Equals(PrefabName, other.PrefabName, StringComparison.Ordinal) && Quality == other.Quality;

        public override bool Equals(object obj) => obj is InventoryItemKey other && Equals(other);

        public override int GetHashCode()
            => unchecked(((PrefabName == null ? 0 : StringComparer.Ordinal.GetHashCode(PrefabName)) * 397) ^ Quality.GetHashCode());

        public static bool operator ==(InventoryItemKey left, InventoryItemKey right) => left.Equals(right);
        public static bool operator !=(InventoryItemKey left, InventoryItemKey right) => !left.Equals(right);
    }
}
