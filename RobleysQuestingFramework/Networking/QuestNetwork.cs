using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    // Shared connection validation and player-facing messages; no quest-specific rules.
    public static class QuestNetwork
    {
        const string MessageRPC = "RQF.QuestMessage";
        public static bool IsServer => ZNet.instance && ZNet.instance.IsServer();

        public static bool PlayerInfo(long sender, out long id, out Vector3 position)
        {
            id = 0;
            position = default;
            if (sender == ZNet.GetUID() && Player.m_localPlayer && !Player.m_localPlayer.IsDead())
            {
                id = Player.m_localPlayer.GetPlayerID();
                position = Player.m_localPlayer.transform.position;
            }
            else
            {
                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                if (peer == null || peer.m_playerID == 0)
                    return false;
                ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (character == null || character.GetFloat("health", 0) <= 0)
                    return false;
                id = peer.m_playerID;
                position = character.GetPosition();
            }
            return id != 0;
        }

        public static bool FindPlayer(long playerID, out long peerID, out Vector3 position)
        {
            peerID = 0;
            position = default;
            if (!IsServer)
                return false;
            if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerID() == playerID)
                peerID = ZNet.GetUID();
            else
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    if (peer.m_playerID == playerID)
                    { peerID = peer.m_uid; break; }
            return peerID != 0 && PlayerInfo(peerID, out long found, out position) && found == playerID;
        }

        public static void Tell(long playerID, string message)
        {
            if (!FindPlayer(playerID, out long peer, out _))
                return;
            if (peer == ZNet.GetUID())
                ShowMessage(peer, message);
            else
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, MessageRPC, message);
        }

        public static void ShowMessage(long sender, string message)
        {
            if (sender == ZRoutedRpc.instance.GetServerPeerID() && MessageHud.instance)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix() => ZRoutedRpc.instance.Register<string>(MessageRPC, ShowMessage);
        }
    }
}
