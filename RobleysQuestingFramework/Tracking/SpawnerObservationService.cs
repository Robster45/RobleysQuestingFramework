using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>Server observations for nearby creature spawners and player destruction of them.</summary>
    public static class SpawnerObservationService
    {
        const string DiscoveryRPC = "RQF.SpawnerDiscovered";
        const string DestroyedRPC = "RQF.SpawnerDestroyed";
        public const float DiscoveryRange = 30f;
        static readonly HashSet<ZDOID> Destroyed = new HashSet<ZDOID>();
        public static event Action<long, int> PlayerDiscoveredSpawner;
        public static event Action<long, int> PlayerDestroyedSpawner;

        public static void Reset() => Destroyed.Clear();

        static void Send(string rpc, ZPackage package)
        {
            if (QuestNetwork.IsServer)
            {
                package.SetPos(0);
                if (rpc == DiscoveryRPC)
                {
                    ReceiveDiscovery(ZNet.GetUID(), package);
                }
                else
                {
                    ReceiveDestroyed(ZNet.GetUID(), package);
                }
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), rpc, package);
            }
        }

        static void ReceiveDiscovery(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null)
            {
                return;
            }
            try
            {
                ZDO spawner = ZDOMan.instance.GetZDO(package.ReadZDOID());
                if (spawner == null || !QuestNetwork.PlayerInfo(sender, out long player, out Vector3 position) ||
                    Vector3.Distance(position, spawner.GetPosition()) > DiscoveryRange)
                {
                    return;
                }
                PlayerDiscoveredSpawner?.Invoke(player, spawner.GetPrefab());
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid spawner discovery report: " + ex.Message);
            }
        }

        static void ReceiveDestroyed(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                (sender != ZNet.GetUID() && ZNet.instance.GetPeer(sender) == null))
            {
                return;
            }
            try
            {
                ZDOID victim = package.ReadZDOID();
                long player = package.ReadLong();
                int prefab = package.ReadInt();
                // Like kill reports, destruction observations come from the object's owner.
                // Its ZDO may already be deleted when the report reaches the server.
                if (victim == ZDOID.None || player == 0 || !Destroyed.Add(victim))
                {
                    return;
                }
                // A first encounter can be a ranged killing blow before the proximity poll.
                PlayerDiscoveredSpawner?.Invoke(player, prefab);
                PlayerDestroyedSpawner?.Invoke(player, prefab);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid spawner destruction report: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                ZRoutedRpc.instance.Register<ZPackage>(DiscoveryRPC, ReceiveDiscovery);
                ZRoutedRpc.instance.Register<ZPackage>(DestroyedRPC, ReceiveDestroyed);
            }
        }

        [HarmonyPatch(typeof(SpawnArea), "UpdateSpawn")]
        static class DiscoveryPatch
        {
            [HarmonyPostfix]
            static void Postfix(ZNetView ___m_nview)
            {
                Player player = Player.m_localPlayer;
                if (!ZNet.instance || ZRoutedRpc.instance == null || !player || player.IsDead() ||
                    !___m_nview || !___m_nview.IsValid() ||
                    Vector3.Distance(player.transform.position, ___m_nview.GetZDO().GetPosition()) > DiscoveryRange)
                {
                    return;
                }
                ZPackage package = new ZPackage();
                package.Write(___m_nview.GetZDO().m_uid);
                Send(DiscoveryRPC, package);
            }
        }

        [HarmonyPatch(typeof(Destructible), "Destroy", new[] { typeof(HitData) })]
        static class DestructionPatch
        {
            [HarmonyPrefix]
            static void Prefix(Destructible __instance, HitData __0, ZNetView ___m_nview, out ZPackage __state)
            {
                __state = null;
                if (!ZNet.instance || ZRoutedRpc.instance == null || __instance.m_destroyed ||
                    !___m_nview || !___m_nview.IsValid() || !___m_nview.IsOwner() ||
                    !__instance.GetComponentInChildren<SpawnArea>())
                {
                    return;
                }
                Player player = __0?.GetAttacker() as Player;
                if (!player || player.GetPlayerID() == 0)
                {
                    return;
                }
                ZDO victim = ___m_nview.GetZDO();
                __state = new ZPackage();
                __state.Write(victim.m_uid);
                __state.Write(player.GetPlayerID());
                __state.Write(victim.GetPrefab());
            }

            [HarmonyPostfix]
            static void Postfix(Destructible __instance, ZPackage __state)
            {
                if (__state != null && __instance.m_destroyed)
                {
                    Send(DestroyedRPC, __state);
                }
            }
        }
    }
}
