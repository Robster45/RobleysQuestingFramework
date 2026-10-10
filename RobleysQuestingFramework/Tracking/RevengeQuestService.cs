using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    public static class RevengeQuestService
    {
        const string DeathRPC = "RQF.PlayerDied";
        const string PromoteRPC = "RQF.RevengePromote";
        static readonly HashSet<string> Deaths = new HashSet<string>();
        static float NextTick;

        /// <summary>Validated server observations: player ID, hostile killer (or None), unique death token.</summary>
        public static event Action<long, ZDOID, string> PlayerDied;

        public static void Reset()
        {
            Deaths.Clear();
            NextTick = 0;
        }

        static void ReceiveDeath(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null)
            {
                return;
            }
            try
            {
                long player = package.ReadLong();
                string death = package.ReadString();
                ZDOID killer = package.ReadZDOID();
                // The reporting character is dead, so the living-player helper cannot be used.
                long owner = sender == ZNet.GetUID()
                    ? (Player.m_localPlayer ? Player.m_localPlayer.GetPlayerID() : 0)
                    : (ZNet.instance.GetPeer(sender)?.m_playerID ?? 0);
                if (player == 0 || owner != player || !Guid.TryParseExact(death, "N", out _) ||
                    !Deaths.Add(player.ToString(CultureInfo.InvariantCulture) + ":" + death))
                {
                    return;
                }
                PlayerDied?.Invoke(player, killer, death);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid player death report: " + ex.Message);
            }
        }

        // Repeat until the owner has persisted the promotion. This also handles ownership changes
        // and a target becoming loaded after reconnect. The ZDO receipt prevents extra stars.
        public static void Tick()
        {
            if (!ZNet.instance || Time.time < NextTick)
            {
                return;
            }
            NextTick = Time.time + 2;
            // Valheim caches the level in Character after Awake. Refresh remote visuals and
            // cached combat level when the owner's promoted ZDO reaches another peer.
            foreach (Character character in Character.GetAllCharacters())
            {
                ZNetView view = character ? character.GetComponent<ZNetView>() : null;
                if (!view || !view.IsValid() || view.IsOwner() || !view.GetZDO().GetBool("rqf.revenge.promoted", false))
                {
                    continue;
                }
                int level = view.GetZDO().GetInt("level", 1);
                if (level > 0 && character.m_level != level)
                {
                    character.m_level = level;
                    character.m_onLevelSet?.Invoke(level);
                }
            }
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null)
            {
                return;
            }
            foreach (Quest quest in QuestManager.WorldInstance.AllQuests)
            {
                // Cancellation must not revoke a promotion still waiting for the enemy owner.
                if (quest.Stage != QuestStage.Hidden && quest is IRevengeTarget target)
                {
                    Promote(quest, target.TargetID);
                }
            }
        }

        static void Promote(Quest quest, ZDOID targetID)
        {
            ZDO target = ZDOMan.instance.GetZDO(targetID);
            string receipt = "rqf.revenge." + quest.InstanceKey;
            if (target == null || target.GetBool(receipt, false))
            {
                return;
            }
            ZPackage package = new ZPackage();
            package.Write(targetID);
            package.Write(receipt);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, PromoteRPC, package);
        }

        static void ReceivePromotion(long sender, ZPackage package)
        {
            if (sender != ZRoutedRpc.instance.GetServerPeerID())
            {
                return;
            }
            ZDOID target = package.ReadZDOID();
            string receipt = package.ReadString();
            GameObject instance = ZNetScene.instance ? ZNetScene.instance.FindInstance(target) : null;
            Character character = instance ? instance.GetComponent<Character>() : null;
            ZNetView view = instance ? instance.GetComponent<ZNetView>() : null;
            if (!character || character.IsPlayer() || character.IsDead() || !view || !view.IsValid() || !view.IsOwner() ||
                view.GetZDO().GetBool(receipt, false))
            {
                return;
            }
            character.SetLevel(checked(character.GetLevel() + 1));
            view.GetZDO().Set(receipt, true);
            view.GetZDO().Set("rqf.revenge.promoted", true);
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                ZRoutedRpc.instance.Register<ZPackage>(DeathRPC, ReceiveDeath);
                ZRoutedRpc.instance.Register<ZPackage>(PromoteRPC, ReceivePromotion);
            }
        }

        [HarmonyPatch(typeof(Player), "OnDeath")]
        static class PlayerDeathPatch
        {
            [HarmonyPrefix]
            static void Prefix(Player __instance, ZNetView ___m_nview, HitData ___m_lastHit)
            {
                if (!ZNet.instance || ZRoutedRpc.instance == null || !___m_nview ||
                    !___m_nview.IsValid() || !___m_nview.IsOwner() || __instance.IsDead())
                {
                    return;
                }
                Character attacker = ___m_lastHit?.GetAttacker();
                ZNetView enemyView = attacker ? attacker.GetComponent<ZNetView>() : null;
                ZDOID killer = attacker && !attacker.IsPlayer() && !attacker.IsDead() &&
                    BaseAI.IsEnemy(__instance, attacker) && enemyView && enemyView.IsValid()
                    ? enemyView.GetZDO().m_uid : ZDOID.None;
                ZPackage package = new ZPackage();
                package.Write(__instance.GetPlayerID());
                package.Write(Guid.NewGuid().ToString("N"));
                package.Write(killer);
                if (QuestNetwork.IsServer)
                {
                    package.SetPos(0);
                    ReceiveDeath(ZNet.GetUID(), package);
                }
                else
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), DeathRPC, package);
                }
            }
        }
    }
}
