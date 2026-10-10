using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>Reusable, whole-inventory hand-in transport. Clients own their inventories.</summary>
    public static class QuestHandInService
    {
        const string PaymentRPC = "RQF.ItemPayment";
        const string ReceiptRPC = "RQF.ItemReceipt";
        static readonly Dictionary<string, PendingPayment> Pending = new Dictionary<string, PendingPayment>();

        class PendingPayment
        {
            public Quest Quest;
            public ZDOID Giver;
            public Action OnPaid;
        }

        public static void Reset() => Pending.Clear();

        static string Token(string paymentID, long playerID, ZDOID giver) => paymentID + "_" +
            ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture) + "_" +
            playerID.ToString(CultureInfo.InvariantCulture) + "_" + giver;

        static bool Active(Quest quest) => (quest.Stage & QuestStage.Started) != 0 &&
            (quest.Stage & (QuestStage.Cancelled | QuestStage.Completed)) == 0;

        /// <summary>Call from a validated NPC interaction. paymentID must identify this one-time payment.</summary>
        public static void Request(Quest quest, string paymentID, IReadOnlyList<ResourceCollectionCriteria> requirements,
            Action onPaid, string missingItemsMessage)
        {
            if (quest == null || onPaid == null)
                throw new ArgumentNullException();
            if (string.IsNullOrWhiteSpace(paymentID) || paymentID.Length > 128 || requirements == null ||
                requirements.Count == 0 || requirements.Count > 64 || requirements.Any(r => r == null))
                throw new ArgumentException("Provide a payment ID and 1-64 item requirements.");
            if (!QuestNetwork.IsServer || !Active(quest) || !QuestNpcService.IsAvailable(quest.QuestGiverId) ||
                !QuestNetwork.FindPlayer(quest.PlayerID, out long peer, out Vector3 position) ||
                Vector3.Distance(position, ZDOMan.instance.GetZDO(quest.QuestGiverId).GetPosition()) > QuestNpcService.InteractionRange)
                return;
            string token = Token(paymentID, quest.PlayerID, quest.QuestGiverId);
            Pending[token] = new PendingPayment { Quest = quest, Giver = quest.QuestGiverId, OnPaid = onPaid };
            ZPackage package = new ZPackage();
            package.Write(quest.QuestGiverId);
            package.Write(paymentID);
            package.Write(missingItemsMessage ?? "Required items are missing.");
            package.Write(requirements.Count);
            foreach (ResourceCollectionCriteria requirement in requirements)
            {
                package.Write(requirement.PrefabName);
                package.Write(requirement.Quantity);
                package.Write(requirement.Quality ?? 0);
            }
            if (peer == ZNet.GetUID())
            { package.SetPos(0); Pay(peer, package); }
            else
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, PaymentRPC, package);
        }

        public static void Cancel(Quest quest)
        {
            foreach (string key in Pending.Where(p => p.Value.Quest == quest).Select(p => p.Key).ToArray())
                Pending.Remove(key);
        }

        static void Pay(long sender, ZPackage package)
        {
            if (sender != ZRoutedRpc.instance.GetServerPeerID() || !Player.m_localPlayer)
                return;
            try
            {
                ZDOID giver = package.ReadZDOID();
                string paymentID = package.ReadString();
                string missingMessage = package.ReadString();
                int count = package.ReadInt();
                if (string.IsNullOrWhiteSpace(paymentID) || paymentID.Length > 128 || count < 1 || count > 64)
                    return;
                List<ResourceCollectionCriteria> requirements = new List<ResourceCollectionCriteria>();
                for (int i = 0; i < count; i++)
                {
                    string prefab = package.ReadString();
                    int quantity = package.ReadInt();
                    int quality = package.ReadInt();
                    if (quality < 0 || prefab.Length > 256)
                        return;
                    requirements.Add(new ResourceCollectionCriteria(prefab, quantity, quality == 0 ? (int?)null : quality));
                }
                Player player = Player.m_localPlayer;
                string token = Token(paymentID, player.GetPlayerID(), giver);
                if (!player.m_customData.ContainsKey(token))
                {
                    if (player.IsDead() || !QuestNpcService.IsAvailable(giver) ||
                        Vector3.Distance(player.transform.position, ZDOMan.instance.GetZDO(giver).GetPosition()) > QuestNpcService.InteractionRange)
                        return;
                    Inventory inventory = player.GetInventory();
                    ItemDrop.ItemData[] items = inventory.GetAllItems().ToArray();
                    HandInStack[] stacks = items.Select(item => new HandInStack(item.m_dropPrefab ? item.m_dropPrefab.name : "",
                        item.m_quality, item.m_stack)).ToArray();
                    int[] removal = ResourceHandInPlan.Create(requirements, stacks);
                    if (removal == null)
                    { QuestNetwork.ShowMessage(sender, missingMessage); return; }
                    for (int i = 0; i < items.Length; i++)
                        if (removal[i] > 0)
                            inventory.RemoveItem(items[i], removal[i]);
                    player.m_customData[token] = "paid";
                }
                // Replay receipts on retries, never charge a successful payment twice.
                if (QuestNetwork.IsServer)
                    Receipt(ZNet.GetUID(), token);
                else
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), ReceiptRPC, token);
            }
            catch (Exception ex) { Jotunn.Logger.LogWarning("Invalid quest hand-in: " + ex.Message); }
        }

        static void Receipt(long sender, string token)
        {
            if (!QuestNetwork.IsServer || !QuestNetwork.PlayerInfo(sender, out long playerID, out _) ||
                !Pending.TryGetValue(token, out PendingPayment payment) || payment.Quest.PlayerID != playerID)
                return;
            Pending.Remove(token);
            if (!Active(payment.Quest) || payment.Quest.QuestGiverId != payment.Giver ||
                !QuestNpcService.IsAvailable(payment.Giver))
                return;
            payment.OnPaid();
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                Reset();
                ZRoutedRpc.instance.Register<ZPackage>(PaymentRPC, Pay);
                ZRoutedRpc.instance.Register<string>(ReceiptRPC, Receipt);
            }
        }
    }
}
