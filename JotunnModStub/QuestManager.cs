using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;

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

        public IEnumerable<Quest> GetPlayerQuests(long playerID)
        {
            foreach (var quest in _active.Values)
            {
                if (quest.PlayerID == playerID && quest.Stage != QuestStage.Hidden)
                    yield return quest;
            }
        }

        QuestManager(string worldID)
        {
            _savePath = Path.Combine(DataLoadPath, worldID + ".json");
            _data = File.Exists(_savePath)
                ? JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(_savePath))
                : new SaveData { WorldID = worldID };
            if (_data == null || _data.Version != 1 || _data.WorldID != worldID || _data.Quests == null)
                throw new InvalidDataException("Invalid or unsupported quest save: " + _savePath);
            if (File.Exists(_savePath) && _data.Quests.Count == 0)
                Jotunn.Logger.LogWarning("Quest save contains no player quest records; previously omitted progress cannot be restored: " + _savePath);
            var keys = new HashSet<string>();
            foreach (var record in _data.Quests)
            {
                if (record == null || !long.TryParse(record.PlayerID, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedPlayerID) || parsedPlayerID == 0 ||
                    string.IsNullOrEmpty(record.QuestID) || record.Stage < 0 || (record.Stage & ~31) != 0 ||
                    !keys.Add(parsedPlayerID.ToString(CultureInfo.InvariantCulture) + ":" + record.QuestID))
                    throw new InvalidDataException("Invalid quest record in " + _savePath);
                record.PlayerID = parsedPlayerID.ToString(CultureInfo.InvariantCulture);
            }
            // Validate the whole file before attaching any quest listeners.
            try
            {
                foreach (var record in _data.Quests)
                {
                    // Retain records for unavailable quest definitions without discarding them.
                    if (Factories.ContainsKey(record.QuestID))
                        Activate(record);
                }
            }
            catch
            {
                foreach (var quest in _active.Values) 
                    quest.Unsubscribe();
                
                throw;
            }
            Jotunn.Logger.LogInfo("Server quest data loaded: " + _savePath);
        }

        public static void RegisterQuest(string id, Func<long, QuestInventory, Quest> factory)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A quest needs an ID.", nameof(id));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            Factories[id] = factory;
            var manager = WorldInstance;
            if (manager == null)
                return;

            foreach (var record in manager._data.Quests)
            {
                if (record.QuestID == id && !manager._active.ContainsKey(record))
                    manager.Activate(record);
            }
        }

        public Quest GetOrAddQuest(long playerID, string questID, out bool created)
        {
            if (playerID == 0)
                throw new ArgumentException("A quest needs a valid character ID.", nameof(playerID));
            
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
            if (manager == null)
                return;
            
            try 
            { 
                manager.Save();
            }
            finally
            {
                foreach (var quest in manager._active.Values) 
                    quest.Unsubscribe();
                
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

            try 
            { 
                quest.Subscribe();
            }
            catch
            {
                quest.Unsubscribe();
                _active.Remove(record);
                throw;
            }
            return quest;
        }

        // Snapshot runtime state at world-save/unload boundaries, never per observation.
        public void Save()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer())
                return;
            
            try
            {
                Directory.CreateDirectory(DataLoadPath);
                foreach (var entry in _active)
                {
                    entry.Key.Stage = (int)entry.Value.Stage;
                    entry.Key.Progress = entry.Value.SaveProgress();
                }
                
                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                // Never replace the previous save if the serializer omits player records.
                var check = JsonConvert.DeserializeObject<SaveData>(json);
                if (check == null || check.Quests == null || check.Quests.Count != _data.Quests.Count)
                    throw new InvalidDataException("Quest serialization omitted player records.");
                for (int i = 0; i < _data.Quests.Count; i++)
                {
                    var expected = _data.Quests[i];
                    var actual = check.Quests[i];
                    if (actual == null || actual.PlayerID != expected.PlayerID || actual.QuestID != expected.QuestID ||
                        actual.Stage != expected.Stage || actual.Progress != expected.Progress)
                        throw new InvalidDataException("Quest serialization changed player progress.");
                }

                string temporary = _savePath + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(_savePath))
                    File.Replace(temporary, _savePath, _savePath + ".bak");
                else
                    File.Move(temporary, _savePath);
                
                Jotunn.Logger.LogInfo("Server quest data saved (" + _data.Quests.Count + " quest records): " + _savePath);
            }
            catch (Exception ex) 
            { 
                Jotunn.Logger.LogError("Quest save failed: " + ex); 
            }
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
                if (!__instance.IsServer())
                    return;
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
                CreatureDiscoveryObservationService.Reset();
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
