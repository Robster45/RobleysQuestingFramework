using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>Samples loaded quest areas on the quest owner's peer, including dedicated-server play.</summary>
    public static class ExploreObservationService
    {
        const string RequestRPC = "RQF.ExploreSample";
        const string ResponseRPC = "RQF.ExploreResult";
        static readonly Dictionary<string, Watch> Watches = new Dictionary<string, Watch>();
        static float NextTick;

        public static IDisposable Subscribe(long player, Vector3 center, float radius, Action<int> callback)
        {
            if (player == 0 || callback == null || radius <= 0 || radius > 64 ||
                float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(center.sqrMagnitude) || float.IsInfinity(center.sqrMagnitude))
            {
                throw new ArgumentException("Provide a player, finite area (up to 64 metres), and callback.");
            }
            Watch watch = new Watch(player, center, radius, callback);
            Watches.Add(watch.ID, watch);
            return watch;
        }

        public static void Reset()
        {
            Watches.Clear();
            NextTick = 0;
        }

        public static void Tick()
        {
            if (!QuestNetwork.IsServer || Time.time < NextTick)
            {
                return;
            }
            NextTick = Time.time + 2;
            foreach (Watch watch in Watches.Values.ToArray())
            {
                if (!QuestNetwork.FindPlayer(watch.Player, out long peer, out Vector3 position) ||
                    Vector3.Distance(position, watch.Center) > watch.Radius)
                {
                    watch.EmptySamples = 0;
                    watch.Pending = false;
                    continue;
                }
                if (watch.Pending)
                {
                    watch.EmptySamples = 0;
                }
                watch.Pending = true;
                watch.Sequence++;
                ZPackage package = new ZPackage();
                package.Write(watch.ID);
                package.Write(watch.Sequence);
                package.Write(watch.Center);
                package.Write(watch.Radius);
                if (peer == ZNet.GetUID())
                {
                    package.SetPos(0);
                    ReceiveRequest(peer, package);
                }
                else
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer, RequestRPC, package);
                }
            }
        }

        static void ReceiveRequest(long sender, ZPackage package)
        {
            if (sender != ZRoutedRpc.instance.GetServerPeerID())
            {
                return;
            }
            string id = package.ReadString();
            int sequence = package.ReadInt();
            Vector3 center = package.ReadVector3();
            float radius = package.ReadSingle();
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead() || !ZNetScene.instance || !ZoneSystem.instance ||
                Vector3.Distance(player.transform.position, center) > radius)
            {
                return;
            }
            // Every zone intersecting the bounding square must be instantiated before counting.
            // IsAreaReady also waits for network objects, rather than just terrain generation.
            Vector2s min = ZoneSystem.GetZone(center - new Vector3(radius, 0, radius));
            Vector2s max = ZoneSystem.GetZone(center + new Vector3(radius, 0, radius));
            for (int x = min.x; x <= max.x; x++)
            {
                for (int z = min.y; z <= max.y; z++)
                {
                    Vector3 point = ZoneSystem.GetZonePos(new Vector2s(x, z));
                    if (!ZoneSystem.instance.IsZoneLoaded(point) || !ZNetScene.instance.IsAreaReady(point))
                    {
                        return;
                    }
                }
            }
            int enemies = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character && !character.IsPlayer() && !character.IsDead() &&
                    Vector3.Distance(character.transform.position, center) <= radius && BaseAI.IsEnemy(player, character))
                {
                    enemies++;
                }
            }
            ZPackage response = new ZPackage();
            response.Write(id);
            response.Write(sequence);
            response.Write(enemies);
            if (QuestNetwork.IsServer)
            {
                response.SetPos(0);
                ReceiveResponse(ZNet.GetUID(), response);
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), ResponseRPC, response);
            }
        }

        static void ReceiveResponse(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer)
            {
                return;
            }
            string id = package.ReadString();
            int sequence = package.ReadInt();
            int enemies = package.ReadInt();
            if (!Watches.TryGetValue(id, out Watch watch) || !watch.Pending || sequence != watch.Sequence || enemies < 0 ||
                !QuestNetwork.FindPlayer(watch.Player, out long peer, out Vector3 position) || sender != peer ||
                Vector3.Distance(position, watch.Center) > watch.Radius)
            {
                return;
            }
            watch.Pending = false;
            watch.EmptySamples = enemies == 0 ? watch.EmptySamples + 1 : 0;
            // Require three consecutive loaded, empty observations to let spawners settle.
            if (enemies > 0 || watch.EmptySamples >= 3)
            {
                watch.Callback(enemies);
            }
        }

        class Watch : IDisposable
        {
            public readonly string ID = Guid.NewGuid().ToString("N");
            public readonly long Player;
            public readonly Vector3 Center;
            public readonly float Radius;
            public readonly Action<int> Callback;
            public int EmptySamples;
            public int Sequence;
            public bool Pending;
            public Watch(long player, Vector3 center, float radius, Action<int> callback)
            {
                Player = player;
                Center = center;
                Radius = radius;
                Callback = callback;
            }
            public void Dispose() => Watches.Remove(ID);
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                ZRoutedRpc.instance.Register<ZPackage>(RequestRPC, ReceiveRequest);
                ZRoutedRpc.instance.Register<ZPackage>(ResponseRPC, ReceiveResponse);
            }
        }
    }
}
