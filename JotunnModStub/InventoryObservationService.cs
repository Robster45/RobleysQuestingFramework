using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace RobleysQuestingFramework
{
    // Transport only: no quest IDs, item requirements, or persistence.
    // Reports are trusted observations from the connected character's client.
    internal static class InventoryObservationService
    {
        const string TrackingRPC = "RobleysQuestingFramework.InventoryTrackingV2";

        const string SnapshotRPC = "RobleysQuestingFramework.InventorySnapshotV2";

        static readonly Dictionary<long, QuestInventory> Sources = new Dictionary<long, QuestInventory>();

        static readonly Dictionary<long, long> Connections = new Dictionary<long, long>();

        static Inventory LocalInventory;

        static Player LocalPlayer;

        static bool TrackingRequested;

        static bool SnapshotRequested;

        internal static event Action<long> PlayerAvailable;

        internal static QuestInventory ForPlayer(long id)
        {
            if (!Sources.TryGetValue(id, out var source))
                Sources[id] = source = new QuestInventory(enabled => SetTracking(id, enabled));
            return source;
        }

        internal static void Tick()
        {
            if (!ZNet.instance || ZRoutedRpc.instance == null) return;
            if (ZNet.instance.IsServer() && QuestManager.WorldInstance != null)
            {
                var present = new Dictionary<long, long>();
                foreach (var peer in ZNet.instance.GetPeers())
                    if (peer.m_playerID != 0) present[peer.m_playerID] = peer.m_uid;
                if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerID() != 0)
                    present[Player.m_localPlayer.GetPlayerID()] = ZNet.GetUID();
                foreach (var id in Connections.Keys.ToArray())
                    if (!present.ContainsKey(id) || present[id] != Connections[id])
                    {
                        ForPlayer(id).Invalidate();
                        Connections.Remove(id);
                    }
                foreach (var entry in present)
                    if (!Connections.ContainsKey(entry.Key))
                    {
                        Connections[entry.Key] = entry.Value;
                        PlayerAvailable?.Invoke(entry.Key);
                        SetTracking(entry.Key, ForPlayer(entry.Key).IsObserved);
                    }
            }

            var player = Player.m_localPlayer;
            var inventory = TrackingRequested && player && player.GetPlayerID() != 0
                ? player.GetInventory() : null;
            if (LocalInventory != inventory || LocalPlayer != player)
            {
                Detach();
                LocalPlayer = player;
                LocalInventory = inventory;
                if (LocalInventory != null)
                {
                    LocalInventory.m_onChanged += ReportSnapshot;
                    ReportSnapshot();
                }
            }
            if (SnapshotRequested && LocalInventory != null) ReportSnapshot();
            // Snapshot callbacks may create quests or change subscriptions.
            foreach (var source in Sources.Values.ToArray()) source.Dispatch();
        }

        internal static void Reset()
        {
            Detach();
            TrackingRequested = false;
            SnapshotRequested = false;
            Connections.Clear();
            foreach (var source in Sources.Values) source.Invalidate();
            Sources.Clear();
        }

        static void SetTracking(long id, bool enabled)
        {
            if (!Connections.TryGetValue(id, out long peer)) return;
            if (peer == ZNet.GetUID())
            {
                TrackingRequested = enabled;
                SnapshotRequested = enabled;
            }
            else
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, TrackingRPC, enabled);
        }

        static void Detach()
        {
            if (LocalInventory != null) LocalInventory.m_onChanged -= ReportSnapshot;
            LocalInventory = null;
            LocalPlayer = null;
        }

        static void ReportSnapshot()
        {
            if (LocalInventory == null || !LocalPlayer || !ZNet.instance) return;
            SnapshotRequested = false;
            var counts = new Dictionary<Tuple<string, int>, int>();
            foreach (var item in LocalInventory.GetAllItems())
            {
                if (!item.m_dropPrefab || item.m_stack <= 0) continue;
                var key = Tuple.Create(item.m_dropPrefab.name, item.m_quality);
                counts.TryGetValue(key, out int previous);
                counts[key] = checked(previous + item.m_stack);
            }
            if (ZNet.instance.IsServer())
            {
                var source = ForPlayer(LocalPlayer.GetPlayerID());
                if (source.IsObserved) source.SetSnapshot(counts);
                return;
            }
            var package = new ZPackage();
            package.Write(counts.Count);
            foreach (var entry in counts)
            {
                package.Write(entry.Key.Item1);
                package.Write(entry.Key.Item2);
                package.Write(entry.Value);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), SnapshotRPC, package);
        }

        static void ReceiveTracking(long sender, bool enabled)
        {
            if (ZNet.instance && !ZNet.instance.IsServer() &&
                sender == ZRoutedRpc.instance.GetServerPeerID())
            {
                TrackingRequested = enabled;
                SnapshotRequested = enabled;
            }
        }

        static void ReceiveSnapshot(long sender, ZPackage package)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            var peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_playerID == 0 ||
                !Sources.TryGetValue(peer.m_playerID, out var source) || !source.IsObserved) return;
            try
            {
                int entries = package.ReadInt();
                if (entries < 0 || entries > 4096) return;
                var counts = new Dictionary<Tuple<string, int>, int>();
                for (int i = 0; i < entries; i++)
                {
                    string id = package.ReadString();
                    int quality = package.ReadInt();
                    int count = package.ReadInt();
                    var key = Tuple.Create(id, quality);
                    if (string.IsNullOrEmpty(id) || id.Length > 256 || quality <= 0 || count < 0 || counts.ContainsKey(key)) return;
                    counts.Add(key, count);
                }
                source.SetSnapshot(counts);
            }
            catch (Exception ex) { Jotunn.Logger.LogWarning("Invalid inventory snapshot: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCPatch
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                ZRoutedRpc.instance.Register<bool>(TrackingRPC, ReceiveTracking);
                ZRoutedRpc.instance.Register<ZPackage>(SnapshotRPC, ReceiveSnapshot);
            }
        }
    }
}
