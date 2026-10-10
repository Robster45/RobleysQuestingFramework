using System;
using System.Collections.Generic;
using System.Linq;

namespace RobleysQuestingFramework
{
    public partial class QuestManager
    {
        static readonly HashSet<string> DefinitionOwners = new HashSet<string>(StringComparer.Ordinal);

        // Validate the entire module before changing registrations. Dispose before registering the same IDs again.
        public static IDisposable RegisterDefinitions(string plugin, params QuestDefinition[] definitions)
        {
            if (string.IsNullOrWhiteSpace(plugin) || definitions == null || definitions.Length == 0)
                throw new ArgumentException("Provide a plugin ID and definitions.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (QuestDefinition definition in definitions)
            {
                if (definition == null)
                    throw new ArgumentException(plugin + ": null definition.");
                definition.Validate(plugin);
                if (!ids.Add(definition.ID) || Factories.ContainsKey(definition.ID))
                    throw new ArgumentException(plugin + "/" + definition.ID + ": quest ID is already registered.");
            }
            DefinitionRegistration registration = new DefinitionRegistration(ids.ToArray());
            try
            {
                foreach (QuestDefinition definition in definitions)
                {
                    RegisterQuest(definition.ID, (player, inventory) => new DefinedQuest(player, definition, plugin), true);
                    DefinitionOwners.Add(definition.ID);
                }
                return registration;
            }
            catch
            {
                registration.Dispose();
                throw;
            }
        }

        static void NotifyAvailable(Quest quest, long player)
        {
            if (quest is DefinedQuest defined)
                defined.OnAvailable(player);
        }

        class DefinitionRegistration : IDisposable
        {
            readonly string[] _ids;
            bool _disposed;
            public DefinitionRegistration(string[] ids) { _ids = ids; }
            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (string id in _ids)
                {
                    Factories.Remove(id);
                    DiscoveryQuests.Remove(id);
                    DefinitionOwners.Remove(id);
                    QuestManager manager = WorldInstance;
                    if (manager == null)
                        continue;
                    foreach (QuestRecord record in manager._active.Keys.Where(record => record.QuestID == id).ToArray())
                    {
                        Quest quest = manager._active[record];
                        Snapshot(record, quest);
                        quest.Unsubscribe();
                        manager._worldRestorePending.Remove(quest);
                        manager._active.Remove(record);
                    }
                    manager.Save();
                }
            }
        }

        static void Snapshot(QuestRecord record, Quest quest)
        {
            record.JournalDismissed = quest.JournalDismissed;
            record.Stage = (int)quest.Stage;
            record.Progress = quest.SaveProgress();
            record.State = quest.SaveState();
            record.GiverUser = quest.QuestGiverId.UserID;
            record.GiverID = quest.QuestGiverId.ID;
        }

        // No delivery or public callbacks between staging the whole plan and committing terminal state.
        internal Action StageCompletionPlan(DefinedQuest quest)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer())
                throw new InvalidOperationException("Only the server can stage completion.");
            QuestRecord record = _active.FirstOrDefault(entry => ReferenceEquals(entry.Value, quest)).Key;
            if (record == null)
                throw new ArgumentException("Completion quest is not owned by this manager.");
            List<RewardDelivery> additions = new List<RewardDelivery>();
            foreach (ItemReward reward in quest.Definition.Rewards)
            {
                string id = RewardDelivery.CreateID(_data.WorldID, quest.PlayerID, record.QuestID, record.InstanceKey, reward.Slot);
                string payload = reward.Payload();
                RewardDelivery existing = _data.Rewards.Find(delivery => delivery.ID == id);
                if (existing != null)
                {
                    if (existing.Handler != BuiltInItemRewards.Handler || existing.Payload != payload)
                        throw new InvalidOperationException(record.QuestID + "/" + reward.Slot + ": reward slot contents changed.");
                }
                else
                    additions.Add(new RewardDelivery { ID = id, PlayerID = quest.PlayerID, Handler = BuiltInItemRewards.Handler, Payload = payload });
            }
            _data.Rewards.AddRange(additions);
            _rewardsDirty = true;
            return Save;
        }
    }
}
