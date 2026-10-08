using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    // Owns server-side state only. Quest definitions and test setup live elsewhere.
    public class QuestManager
    {
        static readonly Dictionary<string, Func<long, QuestInventory, Quest>> Factories =
            new Dictionary<string, Func<long, QuestInventory, Quest>>();

        static readonly string DataLoadPath = Path.Combine(
            BepInEx.Paths.ConfigPath, "RobleysQuestingFramework", "worlds");

        readonly Dictionary<QuestRecord, Quest> _active = new Dictionary<QuestRecord, Quest>();

        readonly SaveData _data;

        readonly string _savePath;

        public static QuestManager WorldInstance { get; private set; }

        QuestManager(string worldID)
        {
            _savePath = Path.Combine(DataLoadPath, worldID + ".json");
            _data = File.Exists(_savePath)
                ? JsonUtility.FromJson<SaveData>(File.ReadAllText(_savePath))
                : new SaveData { WorldID = worldID };
            if (_data == null || _data.Version != 1 || _data.WorldID != worldID || _data.Quests == null)
                throw new InvalidDataException("Invalid or unsupported quest save: " + _savePath);
            var keys = new HashSet<string>();
            foreach (var record in _data.Quests)
            {
                if (record == null || !long.TryParse(record.PlayerID, out long parsedPlayerID) || parsedPlayerID == 0 ||
                    string.IsNullOrEmpty(record.QuestID) || record.Stage < 0 || (record.Stage & ~31) != 0 ||
                    !keys.Add(record.PlayerID + ":" + record.QuestID))
                    throw new InvalidDataException("Invalid quest record in " + _savePath);
            }
            // Validate the whole file before attaching any quest listeners.
            foreach (var record in _data.Quests)
            {
                // Retain records for unavailable quest definitions without discarding them.
                if (Factories.ContainsKey(record.QuestID))
                    Activate(record);
            }
            Jotunn.Logger.LogInfo("Server quest data loaded: " + _savePath);
        }

        public static void RegisterQuest(string id, Func<long, QuestInventory, Quest> factory) => Factories[id] = factory;

        public Quest GetOrAddQuest(long playerID, string questID, out bool created)
        {
            string id = playerID.ToString(CultureInfo.InvariantCulture);
            var record = _data.Quests.Find(q => q.PlayerID == id && q.QuestID == questID);
            created = record == null;
            if (created)
            {
                if (!Factories.ContainsKey(questID))
                    throw new ArgumentException("Unknown quest: " + questID);
                record = new QuestRecord { PlayerID = id, QuestID = questID };
                _data.Quests.Add(record);
            }
            return _active.TryGetValue(record, out var quest) ? quest : Activate(record);
        }

        public static void Unload()
        {
            var manager = WorldInstance;
            if (manager == null) return;
            try { manager.Save(); }
            finally
            {
                foreach (var quest in manager._active.Values) quest.Unsubscribe();
                WorldInstance = null;
            }
        }

        Quest Activate(QuestRecord record)
        {
            long playerID = long.Parse(record.PlayerID, CultureInfo.InvariantCulture);
            var quest = Factories[record.QuestID](playerID, InventoryObservationService.ForPlayer(playerID));
            quest.RestoreProgress(record.Progress);
            quest.RestoreStage((QuestStage)record.Stage);
            _active.Add(record, quest);
            quest.StageChanged += (previous, current) => record.Stage = (int)current;
            quest.Subscribe();
            return quest;
        }

        void Save()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            try
            {
                Directory.CreateDirectory(DataLoadPath);
                foreach (var entry in _active) entry.Key.Progress = entry.Value.SaveProgress();
                string temporary = _savePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(_data, true));
                if (File.Exists(_savePath))
                    File.Replace(temporary, _savePath, _savePath + ".bak");
                else
                    File.Move(temporary, _savePath);
                Jotunn.Logger.LogInfo("Server quest data saved: " + _savePath);
            }
            catch (Exception ex) { Jotunn.Logger.LogError("Quest save failed: " + ex); }
        }

        [Serializable]
        class QuestRecord
        {
            public string PlayerID;
            public string QuestID;
            public int Stage;
            public int Progress;
        }

        [Serializable]
        class SaveData
        {
            public int Version = 1;
            public string WorldID;
            public List<QuestRecord> Quests = new List<QuestRecord>();
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class WorldStartedPatch
        {
            [HarmonyPostfix]
            static void Postfix(ZNet __instance)
            {
                if (!__instance.IsServer()) return;
                try
                {
                    WorldInstance = new QuestManager(__instance.GetWorldUID().ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    WorldInstance = null;
                    InventoryObservationService.Reset();
                    Jotunn.Logger.LogError("Quest load failed; existing save left untouched: " + ex);
                }
            }
        }

        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        static class WorldClosingPatch
        {
            [HarmonyPrefix]
            static void Prefix()
            {
                Unload();
                InventoryObservationService.Reset();
                KillObservationService.Reset();
            }
        }

        [HarmonyPatch(typeof(ZNet), "Save")]
        static class WorldSavingPatch
        {
            [HarmonyPrefix]
            static void Prefix() => WorldInstance?.Save();
        }
    }
}
