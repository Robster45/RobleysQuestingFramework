using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RobleysQuestingFramework
{
    // Like inventory snapshots, kill reports are trusted observations from modded peers.
    internal static class KillObservationService
    {
        const string KillRPC = "RobleysQuestingFramework.CreatureKilled";
        static readonly HashSet<ZDOID> Reported = new HashSet<ZDOID>();
        internal static event Action<long, int> PlayerKilledCreature;

        internal static void Reset() => Reported.Clear();

        static void ReceiveKill(long sender, ZPackage package)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer())
                return;
            if (sender != ZNet.GetUID() && ZNet.instance.GetPeer(sender) == null)
                return;
            try
            {
                var victim = package.ReadZDOID();
                long playerID = package.ReadLong();
                int prefab = package.ReadInt();
                if (victim == ZDOID.None || playerID == 0 || !Reported.Add(victim))
                    return;
                PlayerKilledCreature?.Invoke(playerID, prefab);
            }
            catch (Exception ex) { Jotunn.Logger.LogWarning("Invalid kill report: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCPatch
        {
            [HarmonyPostfix]
            static void Postfix() => ZRoutedRpc.instance.Register<ZPackage>(KillRPC, ReceiveKill);
        }

        [HarmonyPatch(typeof(Character), "OnDeath")]
        static class DeathPatch
        {
            [HarmonyPrefix]
            static void Prefix(Character __instance, ZNetView ___m_nview, HitData ___m_lastHit)
            {
                if (!ZNet.instance || ZRoutedRpc.instance == null || __instance.IsPlayer() ||
                    !___m_nview || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;
                var killer = ___m_lastHit?.GetAttacker() as Player;
                if (!killer || killer.GetPlayerID() == 0)
                    return;
                var victim = ___m_nview.GetZDO();
                var package = new ZPackage();
                package.Write(victim.m_uid);
                package.Write(killer.GetPlayerID());
                package.Write(victim.GetPrefab());
                if (ZNet.instance.IsServer())
                {
                    package.SetPos(0);
                    ReceiveKill(ZNet.GetUID(), package);
                }
                else ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), KillRPC, package);
            }
        }
    }
}
