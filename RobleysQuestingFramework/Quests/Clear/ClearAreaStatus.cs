using System;

namespace RobleysQuestingFramework
{
    public readonly struct ClearAreaStatus
    {
        public int Enemies { get; }
        public int Spawners { get; }
        public bool IsClear => Enemies == 0 && Spawners == 0;

        public ClearAreaStatus(int enemies, int spawners)
        {
            if (enemies < 0 || spawners < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(enemies), "Counts must be nonnegative.");
            }
            Enemies = enemies;
            Spawners = spawners;
        }
    }
}
