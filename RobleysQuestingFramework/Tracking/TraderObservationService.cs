using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    // Like inventory snapshots, these are observations trusted from the character's client.
    // These hooks observe vanilla actions without suppressing any shop operation.
    public static class TraderObservationService
    {
        const string DiscoveryRPC = "RQF.TraderDiscovery";
        const string PurchaseRPC = "RQF.TraderPurchase";
        const string InteractionRPC = "RQF.TraderInteraction";
        public static event Action<long, string> DiscoveredLocation;
        public static event Action<long, string> Interacted;
        public static event Action<long, string, int> GoldSpent;

        static void Send(string rpc, ZPackage package)
        {
            if (!ZNet.instance || ZRoutedRpc.instance == null || !Player.m_localPlayer)
            {
                return;
            }
            if (QuestNetwork.IsServer)
            {
                package.SetPos(0);
                if (rpc == DiscoveryRPC)
                {
                    ReceiveDiscovery(ZNet.GetUID(), package);
                }
                else if (rpc == InteractionRPC)
                {
                    ReceiveInteraction(ZNet.GetUID(), package);
                }
                else
                {
                    ReceivePurchase(ZNet.GetUID(), package);
                }
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), rpc, package);
            }
        }

        static void ReceiveDiscovery(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                !QuestNetwork.PlayerInfo(sender, out long playerID, out _))
            {
                return;
            }
            try
            {
                string location = package.ReadString();
                if (location == "Vendor_BlackForest" || location == "Hildir_camp")
                {
                    DiscoveredLocation?.Invoke(playerID, location);
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid trader discovery: " + ex.Message);
            }
        }

        static void ReceiveInteraction(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                !QuestNetwork.PlayerInfo(sender, out long playerID, out Vector3 position))
            {
                return;
            }
            try
            {
                string trader = package.ReadString();
                Vector3 shop = package.ReadVector3();
                if (trader.Length <= 128 && Vector3.Distance(position, shop) <= 20f)
                {
                    Interacted?.Invoke(playerID, trader);
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid trader interaction: " + ex.Message);
            }
        }

        static void ReceivePurchase(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                !QuestNetwork.PlayerInfo(sender, out long playerID, out Vector3 position))
            {
                return;
            }
            try
            {
                string trader = package.ReadString();
                int spent = package.ReadInt();
                Vector3 shop = package.ReadVector3();
                if (trader.Length <= 128 && spent > 0 && Vector3.Distance(position, shop) <= 20f)
                {
                    GoldSpent?.Invoke(playerID, trader, spent);
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid trader purchase: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                ZRoutedRpc.instance.Register<ZPackage>(DiscoveryRPC, ReceiveDiscovery);
                ZRoutedRpc.instance.Register<ZPackage>(PurchaseRPC, ReceivePurchase);
                ZRoutedRpc.instance.Register<ZPackage>(InteractionRPC, ReceiveInteraction);
            }
        }

        [HarmonyPatch(typeof(Trader), "Interact")]
        static class InteractionPatch
        {
            [HarmonyPostfix]
            static void Postfix(Trader __instance, Humanoid __0, bool __1)
            {
                // Vanilla returns false even when opening the shop. Ignore held-use repeats,
                // but do not use that return value as an indication that interaction failed.
                if (__1 || !Player.m_localPlayer || __0 != Player.m_localPlayer)
                {
                    return;
                }
                ZPackage package = new ZPackage();
                package.Write(Utils.GetPrefabName(__instance.gameObject));
                package.Write(__instance.transform.position);
                Send(InteractionRPC, package);
            }
        }

        [HarmonyPatch(typeof(Minimap), "UpdateLocationPins")]
        static class DiscoveryPatch
        {
            [HarmonyPrefix]
            static void Prefix(Minimap __instance, float ___m_updateLocationsTimer, float dt)
            {
                if (___m_updateLocationsTimer > dt || !ZoneSystem.instance || !Player.m_localPlayer)
                {
                    return;
                }
                Dictionary<Vector3, string> icons = new Dictionary<Vector3, string>();
                ZoneSystem.instance.GetLocationIcons(icons);
                foreach (KeyValuePair<Vector3, string> icon in icons)
                {
                    if ((icon.Value == "Vendor_BlackForest" || icon.Value == "Hildir_camp") &&
                        __instance.IsExplored(icon.Key))
                    {
                        ZPackage package = new ZPackage();
                        package.Write(icon.Value);
                        Send(DiscoveryRPC, package);
                    }
                }
            }
        }

        public class PurchaseObservation
        {
            public Player Player;
            public Trader Trader;
            public string Currency;
            public int CoinsBefore;
        }

        [HarmonyPatch(typeof(StoreGui), "BuySelectedItem")]
        static class PurchasePatch
        {
            [HarmonyPrefix]
            static void Prefix(Trader ___m_trader, ItemDrop ___m_coinPrefab, out PurchaseObservation __state)
            {
                __state = null;
                Player player = Player.m_localPlayer;
                if (!player || !___m_trader || !___m_coinPrefab || ___m_coinPrefab.name != "Coins")
                {
                    return;
                }
                string currency = ___m_coinPrefab.m_itemData.m_shared.m_name;
                __state = new PurchaseObservation
                {
                    Player = player, Trader = ___m_trader, Currency = currency,
                    CoinsBefore = player.GetInventory().CountItems(currency)
                };
            }

            [HarmonyPostfix]
            static void Postfix(PurchaseObservation __state)
            {
                if (__state == null || !__state.Player || !__state.Trader)
                {
                    return;
                }
                int spent = __state.CoinsBefore - __state.Player.GetInventory().CountItems(__state.Currency);
                if (spent <= 0)
                {
                    return;
                }
                ZPackage package = new ZPackage();
                package.Write(Utils.GetPrefabName(__state.Trader.gameObject));
                package.Write(spent);
                package.Write(__state.Trader.transform.position);
                Send(PurchaseRPC, package);
            }
        }
    }
}
