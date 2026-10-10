using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    // Discovery means a creature senses or is alerted to a player it is targeting.
    // AI runs on the creature's owning peer, which forwards observations to the server.
    public static class CreatureDiscoveryObservationService
    {
        const string DiscoveryRPC = "RobleysQuestingFramework.CreatureDiscovered";
        const float ReportInterval = 5f;
        static readonly Dictionary<CreatureDiscoveryKey, float> NextReport =
            new Dictionary<CreatureDiscoveryKey, float>();

        public static event Action<long, int> PlayerDiscoveredCreature;

        public static void Reset() => NextReport.Clear();

        static void Report(MonsterAI creature, ZNetView view, bool sensed)
        {
            if (!ZNet.instance || ZRoutedRpc.instance == null ||
                !view || !view.IsValid() || !view.IsOwner() ||
                (!sensed && !creature.IsAlerted()))
                return;

            Player player = creature.GetTargetCreature() as Player;
            if (!player || player.IsDead() || player.GetPlayerID() == 0)
                return;

            ZDO zdo = view.GetZDO();
            CreatureDiscoveryKey key = new CreatureDiscoveryKey(player.GetPlayerID(), zdo.GetPrefab());
            if (NextReport.TryGetValue(key, out float next) && Time.time < next)
                return;
            // Retry during sustained encounters so loading/reconnecting cannot lose discovery.
            NextReport[key] = Time.time + ReportInterval;

            ZPackage package = new ZPackage();
            package.Write(zdo.m_uid);
            package.Write(player.GetPlayerID());
            if (ZNet.instance.IsServer())
            {
                package.SetPos(0);
                ReceiveDiscovery(ZNet.GetUID(), package);
            }
            else
                ZRoutedRpc.instance.InvokeRoutedRPC(
                    ZRoutedRpc.instance.GetServerPeerID(), DiscoveryRPC, package);
        }

        static void ReceiveDiscovery(long sender, ZPackage package)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer() || QuestManager.WorldInstance == null)
                return;
            if (sender != ZNet.GetUID() && ZNet.instance.GetPeer(sender) == null)
                return;
            try
            {
                ZDOID creatureID = package.ReadZDOID();
                long playerID = package.ReadLong();
                ZDO creature = ZDOMan.instance.GetZDO(creatureID);
                if (playerID == 0 || creature == null || creature.GetOwner() != sender)
                    return;
                // As with inventory reports, trust observations from modded peers,
                // but derive the prefab from the server's ZDO rather than the packet.
                PlayerDiscoveredCreature?.Invoke(playerID, creature.GetPrefab());
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid creature discovery report: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCPatch
        {
            [HarmonyPostfix]
            static void Postfix() => ZRoutedRpc.instance.Register<ZPackage>(DiscoveryRPC, ReceiveDiscovery);
        }

        [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
        static class TargetPatch
        {
            [HarmonyPostfix]
            static void Postfix(MonsterAI __instance, ZNetView ___m_nview,
                bool canHearTarget, bool canSeeTarget)
                => Report(__instance, ___m_nview, canHearTarget || canSeeTarget);
        }
    }
}
