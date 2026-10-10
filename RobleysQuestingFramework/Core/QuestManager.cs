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
        static readonly HashSet<string> DiscoveryQuests = new HashSet<string>();

        static QuestManager() => InventoryObservationService.PlayerAvailable += EnsurePlayerQuests;

        static void EnsurePlayerQuests(long playerID)
        {
            if (WorldInstance == null)
                return;
            foreach (string id in DiscoveryQuests)
                WorldInstance.GetOrAddQuest(playerID, id, out _);
        }

        static readonly string DataLoadPath = Path.Combine(
            BepInEx.Paths.ConfigPath, "RobleysQuestingFramework", "worlds");

        readonly Dictionary<QuestRecord, Quest> _active = new Dictionary<QuestRecord, Quest>();
        readonly List<Quest> _worldRestorePending = new List<Quest>();

        readonly SaveData _data;
        bool _rewardsDirty;

        readonly string _savePath;

        public static QuestManager WorldInstance { get; private set; }

        public IEnumerable<Quest> AllQuests => _active.Values;

        public IEnumerable<Quest> GetPlayerQuests(long playerID)
        {
            foreach (Quest quest in _active.Values)
            {
                if (quest.PlayerID == playerID && quest.Stage != QuestStage.Hidden && !quest.JournalDismissed)
                    yield return quest;
            }
        }

        // Use world time on the server, including dedicated servers and sleep/time skips.
        public void UpdateJournalDay()
        {
            if (ZNet.instance && ZNet.instance.IsServer() && EnvMan.instance)
                AdvanceJournalDay(EnvMan.instance.GetDay());
        }

        public void AdvanceJournalDay(int day)
        {
            int? previousDay = _data.JournalDay;
            if (previousDay == day)
                return;
            _data.JournalDay = day;
            if (!previousDay.HasValue || day <= previousDay.Value)
                return;

            foreach (QuestRecord record in _data.Quests)
            {
                if (_active.TryGetValue(record, out Quest quest))
                {
                    quest.DismissFromJournal();
                    record.JournalDismissed = quest.JournalDismissed;
                }
                else if (((QuestStage)record.Stage & QuestStage.Cancelled) != 0)
                    record.JournalDismissed = true;
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
            HashSet<string> keys = new HashSet<string>();
            foreach (QuestRecord record in _data.Quests)
            {
                if (record == null || !long.TryParse(record.PlayerID, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedPlayerID) || parsedPlayerID == 0 ||
                    string.IsNullOrEmpty(record.QuestID) || record.Stage < 0 || (record.Stage & ~31) != 0 ||
                    !keys.Add(parsedPlayerID.ToString(CultureInfo.InvariantCulture) + ":" + record.QuestID + ":" + (record.InstanceKey ?? "")))
                    throw new InvalidDataException("Invalid quest record in " + _savePath);
                record.PlayerID = parsedPlayerID.ToString(CultureInfo.InvariantCulture);
            }
            if (_data.Rewards == null)
                throw new InvalidDataException("Missing reward records.");
            HashSet<string> rewardIDs = new HashSet<string>();
            foreach (RewardDelivery reward in _data.Rewards)
            {
                if (reward == null || reward.ID == null || reward.ID.Length != 64 ||
                    reward.PlayerID == 0 || string.IsNullOrWhiteSpace(reward.Handler) ||
                    reward.Handler.Length > 128 || reward.Payload == null || reward.Payload.Length > 8192 ||
                    !rewardIDs.Add(reward.ID))
                    throw new InvalidDataException("Invalid reward record in " + _savePath);
            }
            // Validate the whole file before attaching any quest listeners.
            try
            {
                foreach (QuestRecord record in _data.Quests)
                {
                    // Retain records for unavailable quest definitions without discarding them.
                    if (Factories.ContainsKey(record.QuestID))
                        Activate(record);
                }
            }
            catch
            {
                foreach (Quest quest in _active.Values)
                    quest.Unsubscribe();

                throw;
            }
            Jotunn.Logger.LogInfo("Server quest data loaded: " + _savePath);
        }

        public static void RegisterQuest(string id, Func<long, QuestInventory, Quest> factory)
            => RegisterQuest(id, factory, false);

        // Automatic creation covers both currently available players and future arrivals.
        public static void RegisterQuest(string id, Func<long, QuestInventory, Quest> factory, bool discoverForEveryPlayer)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A quest needs an ID.", nameof(id));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            Factories[id] = factory;
            if (discoverForEveryPlayer)
                DiscoveryQuests.Add(id);
            else
                DiscoveryQuests.Remove(id);
            QuestManager manager = WorldInstance;
            if (manager == null)
                return;

            foreach (QuestRecord record in manager._data.Quests)
            {
                if (record.QuestID == id && !manager._active.ContainsKey(record))
                    manager.Activate(record);
            }

            if (discoverForEveryPlayer)
            {
                foreach (long playerID in InventoryObservationService.GetAvailablePlayerIDs())
                    manager.GetOrAddQuest(playerID, id, out _);
            }
        }

        public Quest GetOrAddQuest(long playerID, string questID, out bool created)
            => GetOrAddQuest(playerID, questID, "", out created);

        public Quest GetOrAddQuest(long playerID, string questID, string instanceKey, out bool created)
        {
            instanceKey = instanceKey ?? "";
            if (playerID == 0)
                throw new ArgumentException("A quest needs a valid character ID.", nameof(playerID));

            string id = playerID.ToString(CultureInfo.InvariantCulture);
            QuestRecord record = _data.Quests.Find(q => q.PlayerID == id && q.QuestID == questID && (q.InstanceKey ?? "") == instanceKey);
            created = record == null;
            if (created)
            {
                if (!Factories.ContainsKey(questID))
                    throw new ArgumentException("Unknown quest: " + questID);

                record = new QuestRecord { PlayerID = id, QuestID = questID, InstanceKey = instanceKey };
                _data.Quests.Add(record);
            }

            return _active.TryGetValue(record, out Quest quest) ? quest : Activate(record);
        }

        // One stable slot per managed quest instance. Reuse must have identical contents.
        public string QueueReward(Quest quest, string rewardID, string handler, string payload = "")
        {
            if (!ZNet.instance || !ZNet.instance.IsServer())
                throw new InvalidOperationException("Only the server can queue rewards.");
            if (quest == null || (quest.Stage & QuestStage.Completed) == 0 ||
                string.IsNullOrWhiteSpace(rewardID) || rewardID.Length > 128 ||
                string.IsNullOrWhiteSpace(handler) || handler.Length > 128 || payload == null || payload.Length > 8192)
                throw new ArgumentException("Provide a completed quest, reward slot, handler and bounded payload.");
            QuestRecord record = null;
            foreach (KeyValuePair<QuestRecord, Quest> entry in _active)
            {
                if (ReferenceEquals(entry.Value, quest))
                    record = entry.Key;
            }
            if (record == null)
                throw new ArgumentException("Reward quest is not owned by this manager.");
            string id = RewardDelivery.CreateID(_data.WorldID, quest.PlayerID, record.QuestID, record.InstanceKey, rewardID);
            RewardDelivery existing = _data.Rewards.Find(r => r.ID == id);
            if (existing != null)
            {
                if (existing.Handler != handler || existing.Payload != payload)
                    throw new InvalidOperationException("A reward ID cannot be reused with different contents.");
                return id;
            }
            _data.Rewards.Add(new RewardDelivery { ID = id, PlayerID = quest.PlayerID, Handler = handler, Payload = payload });
            _rewardsDirty = true;
            // Persist completion and its outbox entry together before allowing delivery.
            Save();
            return id;
        }

        public RewardDelivery[] GetPendingRewards()
        {
            if (_rewardsDirty)
                Save();
            return _rewardsDirty ? Array.Empty<RewardDelivery>() : _data.Rewards.FindAll(r => !r.Acknowledged).ToArray();
        }

        public void AcknowledgeReward(long playerID, string id)
        {
            RewardDelivery reward = _data.Rewards.Find(r => r.ID == id && r.PlayerID == playerID);
            if (reward == null || reward.Acknowledged)
                return;
            reward.Acknowledged = true;
            _rewardsDirty = true;
            Save();
        }

        public static void Unload()
        {
            QuestManager manager = WorldInstance;
            if (manager == null)
                return;

            try
            {
                manager.Save();
            }
            finally
            {
                foreach (Quest quest in manager._active.Values)
                    quest.Unsubscribe();

                WorldInstance = null;
            }
        }

        Quest Activate(QuestRecord record)
        {
            long playerID = long.Parse(record.PlayerID, CultureInfo.InvariantCulture);
            Quest quest = Factories[record.QuestID](playerID, InventoryObservationService.ForPlayer(playerID));
            quest.InstanceKey = record.InstanceKey ?? "";
            quest.QuestGiverId = new ZDOID(record.GiverUser, record.GiverID);
            quest.RestoreProgress(record.Progress);
            quest.RestoreStage((QuestStage)record.Stage);
            quest.RestoreJournalDismissed(record.JournalDismissed);
            _active.Add(record, quest);

            try
            {
                quest.Subscribe();
                _worldRestorePending.Add(quest);
            }
            catch
            {
                quest.Unsubscribe();
                _active.Remove(record);
                throw;
            }
            return quest;
        }

        public void RestoreWorldEffects()
        {
            Quest[] pending = _worldRestorePending.ToArray();
            _worldRestorePending.Clear();
            foreach (Quest quest in pending)
                quest.OnWorldReady();
        }

        // Snapshot runtime state at world-save/unload boundaries, never per observation.
        public void Save()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer())
                return;

            try
            {
                Directory.CreateDirectory(DataLoadPath);
                foreach (KeyValuePair<QuestRecord, Quest> entry in _active)
                {
                    entry.Key.JournalDismissed = entry.Value.JournalDismissed;
                    entry.Key.Stage = (int)entry.Value.Stage;
                    entry.Key.Progress = entry.Value.SaveProgress();
                    entry.Key.GiverUser = entry.Value.QuestGiverId.UserID;
                    entry.Key.GiverID = entry.Value.QuestGiverId.ID;
                }

                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                // Never replace the previous save if the serializer omits player records.
                SaveData check = JsonConvert.DeserializeObject<SaveData>(json);
                if (check == null || check.Quests == null || check.Quests.Count != _data.Quests.Count || check.JournalDay != _data.JournalDay ||
                    JsonConvert.SerializeObject(check.Rewards) != JsonConvert.SerializeObject(_data.Rewards))
                    throw new InvalidDataException("Quest serialization omitted player records.");
                for (int i = 0; i < _data.Quests.Count; i++)
                {
                    QuestRecord expected = _data.Quests[i];
                    QuestRecord actual = check.Quests[i];
                    if (actual == null || actual.PlayerID != expected.PlayerID || actual.QuestID != expected.QuestID ||
                        actual.JournalDismissed != expected.JournalDismissed || actual.InstanceKey != expected.InstanceKey || actual.Stage != expected.Stage || actual.Progress != expected.Progress ||
                        actual.GiverUser != expected.GiverUser || actual.GiverID != expected.GiverID)
                        throw new InvalidDataException("Quest serialization changed player progress.");
                }

                string temporary = _savePath + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(_savePath))
                    File.Replace(temporary, _savePath, _savePath + ".bak");
                else
                    File.Move(temporary, _savePath);

                _rewardsDirty = false;
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
            public string InstanceKey;
            public bool JournalDismissed;
            public int Stage;
            public int Progress;
            public long GiverUser;
            public uint GiverID;
        }

        [Serializable]
        class SaveData
        {
            public int Version = 1;
            public int? JournalDay;
            public string WorldID;
            public List<QuestRecord> Quests = new List<QuestRecord>();
            public List<RewardDelivery> Rewards = new List<RewardDelivery>();
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
                SpawnerObservationService.Reset();
                CreatureDiscoveryObservationService.Reset();
                ExploreObservationService.Reset();
                DungeonObservationService.Reset();
                RevengeQuestService.Reset();
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
