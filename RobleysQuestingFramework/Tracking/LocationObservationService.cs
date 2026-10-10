using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RobleysQuestingFramework
{
    public class LocationEncounter
    {
        public long PlayerID { get; }
        public string PrefabName { get; }
        public Vector3 Position { get; }
        public LocationEncounter(long playerID, string prefabName, Vector3 position)
        { PlayerID = playerID; PrefabName = prefabName; Position = position; }

        public string PersistentKey(string prefix) => prefix + "_" +
            Mathf.RoundToInt(Position.x).ToString(CultureInfo.InvariantCulture) + "_" +
            Mathf.RoundToInt(Position.z).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Server observations for location prefabs requested by quest subscriptions.</summary>
    public static class LocationObservationService
    {
        static readonly List<Watch> Watches = new List<Watch>();

        public static IDisposable Subscribe(long playerID, string prefabName, float range, Action<LocationEncounter> callback)
        {
            if (playerID == 0 || string.IsNullOrWhiteSpace(prefabName) || range <= 0 || float.IsNaN(range) || float.IsInfinity(range))
                throw new ArgumentException("Provide a player, location prefab, and positive finite range.");
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            Watch watch = new Watch { PlayerID = playerID, PrefabName = prefabName, Range = range, Callback = callback };
            Watches.Add(watch);
            return watch;
        }

        public static void Tick()
        {
            if (!QuestNetwork.IsServer || !ZoneSystem.instance || QuestManager.WorldInstance == null)
                return;
            foreach (Watch watch in Watches.ToArray())
            {
                if (watch.Disposed || !QuestNetwork.FindPlayer(watch.PlayerID, out _, out Vector3 position))
                    continue;
                if (ZoneSystem.instance.FindClosestLocation(watch.PrefabName, position, out ZoneSystem.LocationInstance location) &&
                    location.m_placed && Vector3.Distance(position, location.m_position) <= watch.Range)
                    watch.Callback(new LocationEncounter(watch.PlayerID, watch.PrefabName, location.m_position));
            }
        }

        class Watch : IDisposable
        {
            public long PlayerID;
            public string PrefabName;
            public float Range;
            public Action<LocationEncounter> Callback;
            public bool Disposed;
            public void Dispose() { Disposed = true; Watches.Remove(this); }
        }
    }
}
