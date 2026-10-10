using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>Server outbox to character-owner delivery. Handlers must be registered on clients.</summary>
    public static class QuestRewardService
    {
        const string DeliveryRPC = "RQF.Reward";
        const string ReceiptRPC = "RQF.RewardReceipt";
        const string ReceiptPrefix = "RQF.reward.";
        static readonly Dictionary<string, Func<Player, string, bool>> Handlers =
            new Dictionary<string, Func<Player, string, bool>>();
        static float NextRetry;

        // Return false only when no effects were applied; that leaves the delivery pending.
        public static void Register(string id, Func<Player, string, bool> handler)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || handler == null)
                throw new ArgumentException("Provide a bounded handler ID and callback.");
            Handlers.Add(id, handler);
        }

        public static void Unregister(string id) => Handlers.Remove(id);

        public static void Tick()
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                ZRoutedRpc.instance == null || Time.unscaledTime < NextRetry)
                return;
            NextRetry = Time.unscaledTime + 5f;
            foreach (RewardDelivery reward in QuestManager.WorldInstance.GetPendingRewards())
            {
                if (!QuestNetwork.FindPlayer(reward.PlayerID, out long peer, out _))
                    continue;
                ZPackage package = new ZPackage();
                package.Write(reward.ID);
                package.Write(reward.PlayerID);
                package.Write(reward.Handler);
                package.Write(reward.Payload);
                if (peer == ZNet.GetUID())
                {
                    package.SetPos(0);
                    Deliver(peer, package);
                }
                else
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer, DeliveryRPC, package);
            }
        }

        static void Deliver(long sender, ZPackage package)
        {
            if (sender != ZRoutedRpc.instance.GetServerPeerID() || !Player.m_localPlayer)
                return;
            try
            {
                string id = package.ReadString();
                long playerID = package.ReadLong();
                string handler = package.ReadString();
                string payload = package.ReadString();
                Player player = Player.m_localPlayer;
                if (id.Length != 64 || player.GetPlayerID() != playerID ||
                    string.IsNullOrWhiteSpace(handler) || handler.Length > 128 || payload.Length > 8192)
                    return;
                string receipt = ReceiptPrefix + id;
                if (!player.m_customData.ContainsKey(receipt))
                {
                    if (player.IsDead() || !Handlers.TryGetValue(handler, out Func<Player, string, bool> apply) ||
                        !apply(player, payload))
                        return;
                    player.m_customData[receipt] = "delivered";
                }
                // A dropped acknowledgement must not replay the effect.
                if (QuestNetwork.IsServer)
                    Acknowledge(ZNet.GetUID(), id);
                else
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReceiptRPC, id);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Quest reward delivery failed: " + ex.Message);
            }
        }

        static void Acknowledge(long sender, string id)
        {
            if (!QuestNetwork.IsServer || id == null || id.Length != 64 ||
                !QuestNetwork.PlayerInfo(sender, out long playerID, out _))
                return;
            QuestManager.WorldInstance?.AcknowledgeReward(playerID, id);
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                NextRetry = 0;
                ZRoutedRpc.instance.Register<ZPackage>(DeliveryRPC, Deliver);
                ZRoutedRpc.instance.Register<string>(ReceiptRPC, Acknowledge);
            }
        }
    }
}
