using System;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    // Initial quests track local character progress, including on remote clients.
    internal class QuestManager
    {
        [Serializable]
        public class SaveData
        {
            public int Version = 1;
            public string WorldID;
            public string PlayerID;
            public int WoodQuestStage;
        }

        public static readonly string DataLoadPath = Path.Combine(
            BepInEx.Paths.ConfigPath, "RobleysQuestingFramework", "worlds");
        public static QuestManager WorldInstance { get; private set; }
        private static bool initializationFailed;
        private readonly Player player;
        private readonly Inventory inventory;
        private readonly TestQuest_10Wood quest;
        private readonly string worldID;
        private readonly string playerID;
        private readonly string savePath;
        private bool dirty;
        private float nextSave;

        private QuestManager(Player player, string worldID)
        {
            this.player = player;
            this.worldID = worldID;
            playerID = player.GetPlayerID().ToString(CultureInfo.InvariantCulture);
            inventory = player.GetInventory();
            savePath = Path.Combine(DataLoadPath, worldID, playerID + ".json");
            quest = new TestQuest_10Wood(inventory);
            if (File.Exists(savePath))
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(savePath));
                if (data == null || data.Version != 1 || data.WorldID != worldID ||
                    data.PlayerID != playerID || data.WoodQuestStage < 0 ||
                    (data.WoodQuestStage & ~31) != 0)
                    throw new InvalidDataException("Invalid or unsupported quest save: " + savePath);
                quest.RestoreStage((QuestStage)data.WoodQuestStage);
            }
            else
            {
                quest.Discover();
                quest.StartQuest();
            }
            quest.StageChanged += OnStageChanged;
            quest.Subscribe();
            quest.CheckHas10Wood();
            dirty = true;
            Jotunn.Logger.LogInfo($"{quest.Name}: {quest.Stage}. Save: {savePath}");
        }

        private void OnStageChanged(QuestStage previous, QuestStage current)
        {
            dirty = true;
            Jotunn.Logger.LogInfo($"{quest.Name}: {previous} -> {current}");
        }

        internal static void Tick()
        {
            if (!ZNet.instance || !Player.m_localPlayer)
            {
                if (WorldInstance != null) Unload();
                return;
            }
            if (ZNet.instance.GetWorld() == null) return;
            var localPlayer = Player.m_localPlayer;
            if (localPlayer.GetPlayerID() == 0 || localPlayer.GetInventory() == null) return;
            string id = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            if (WorldInstance != null && (WorldInstance.player != localPlayer ||
                WorldInstance.inventory != localPlayer.GetInventory() || WorldInstance.worldID != id))
                Unload();

            if (WorldInstance == null && !initializationFailed)
            {
                try { WorldInstance = new QuestManager(localPlayer, id); }
                catch (Exception ex)
                {
                    initializationFailed = true;
                    Jotunn.Logger.LogError($"Quest load failed; existing save left untouched: {ex}");
                }
            }
            if (WorldInstance != null && WorldInstance.dirty && Time.unscaledTime >= WorldInstance.nextSave)
                WorldInstance.Save();
        }

        private void Save()
        {
            nextSave = Time.unscaledTime + 2f;
            if (!dirty) return;
            try
            {
                var data = new SaveData
                {
                    WorldID = worldID, PlayerID = playerID, WoodQuestStage = (int)quest.Stage
                };
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                string temporary = savePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(savePath))
                    File.Replace(temporary, savePath, savePath + ".bak");
                else
                    File.Move(temporary, savePath);
                dirty = false;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"Quest save failed: {ex}");
            }
        }

        internal static void Unload()
        {
            var manager = WorldInstance;
            WorldInstance = null;
            initializationFailed = false;
            if (manager == null) return;
            try { manager.Save(); }
            finally
            {
                manager.quest.Unsubscribe();
                manager.quest.StageChanged -= manager.OnStageChanged;
            }
        }

        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static class WorldClosingPatch
        {
            [HarmonyPrefix]
            private static void Prefix() => Unload();
        }

        [HarmonyPatch(typeof(ZNet), "Save")]
        private static class WorldSavingPatch
        {
            [HarmonyPrefix]
            private static void Prefix() => WorldInstance?.Save();
        }
    }
}
