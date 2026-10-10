using System;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>Visit a configured area and clear its living hostile creatures.</summary>
    public abstract class ExploreQuest : ClearQuest
    {
        public Vector3 Destination { get; }
        public float Radius { get; }
        public override string JournalProgress => AreaCleared ? "Area cleared" : !Visited
            ? $"Explore ({Destination.x:0}, {Destination.z:0})"
            : RemainingEnemies < 0 ? $"Return to ({Destination.x:0}, {Destination.z:0}) to finish clearing it"
            : $"Clear ({Destination.x:0}, {Destination.z:0}): {RemainingEnemies} enemies remaining";

        protected ExploreQuest(long playerID, Vector3 destination, float radius) : base(playerID)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 64 ||
                !Finite(destination.x) || !Finite(destination.y) || !Finite(destination.z))
            {
                throw new ArgumentException("Provide a finite destination and radius between 0 and 64 metres.");
            }
            Destination = destination;
            Radius = radius;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        protected override IDisposable SubscribeArea(Action<ClearAreaStatus> callback)
            => ExploreObservationService.Subscribe(PlayerID, Destination, Radius, enemies => callback(new ClearAreaStatus(enemies, 0)));
    }
}
