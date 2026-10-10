using System;

namespace RobleysQuestingFramework
{
    // Identifies a player's discovery of a creature prefab for report throttling.
    public readonly struct CreatureDiscoveryKey : IEquatable<CreatureDiscoveryKey>
    {
        public long PlayerID { get; }
        public int CreaturePrefabHash { get; }

        public CreatureDiscoveryKey(long playerID, int creaturePrefabHash)
        {
            PlayerID = playerID;
            CreaturePrefabHash = creaturePrefabHash;
        }

        public bool Equals(CreatureDiscoveryKey other)
            => PlayerID == other.PlayerID && CreaturePrefabHash == other.CreaturePrefabHash;

        public override bool Equals(object obj) => obj is CreatureDiscoveryKey other && Equals(other);

        public override int GetHashCode()
            => unchecked((PlayerID.GetHashCode() * 397) ^ CreaturePrefabHash.GetHashCode());

        public static bool operator ==(CreatureDiscoveryKey left, CreatureDiscoveryKey right) => left.Equals(right);
        public static bool operator !=(CreatureDiscoveryKey left, CreatureDiscoveryKey right) => !left.Equals(right);
    }
}
