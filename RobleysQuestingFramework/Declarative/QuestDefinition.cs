using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RobleysQuestingFramework
{
    public enum ObjectiveKind { Kill, DestroySpawner, All, Sequence }

    // Immutable declarations: only the supported factories can construct objective nodes.
    public class QuestObjective
    {
        public string ID { get; }
        public ObjectiveKind Kind { get; }
        public string Prefab { get; }
        public int Quantity { get; }
        public IReadOnlyList<QuestObjective> Children { get; }
        private QuestObjective(string id, ObjectiveKind kind, string prefab, int quantity, QuestObjective[] children)
        {
            ID = id;
            Kind = kind;
            Prefab = prefab;
            Quantity = quantity;
            Children = Array.AsReadOnly((QuestObjective[])(children ?? Array.Empty<QuestObjective>()).Clone());
        }
        public static QuestObjective Kill(string id, string prefab, int quantity) => new QuestObjective(id, ObjectiveKind.Kill, prefab, quantity, null);
        public static QuestObjective DestroySpawner(string id, string prefab, int quantity) => new QuestObjective(id, ObjectiveKind.DestroySpawner, prefab, quantity, null);
        public static QuestObjective All(string id, params QuestObjective[] children) => new QuestObjective(id, ObjectiveKind.All, null, 0, children);
        public static QuestObjective Sequence(string id, params QuestObjective[] children) => new QuestObjective(id, ObjectiveKind.Sequence, null, 0, children);
    }

    public class QuestDiscovery
    {
        public string CreaturePrefab { get; }
        private QuestDiscovery(string prefab) { CreaturePrefab = prefab; }
        public static QuestDiscovery PlayerAvailable { get; } = new QuestDiscovery(null);
        public static QuestDiscovery CreatureEncounter(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
                throw new ArgumentException("Creature encounter needs a prefab.");
            return new QuestDiscovery(prefab);
        }
    }

    public class ItemReward
    {
        public string Slot { get; }
        public string Prefab { get; }
        public int Amount { get; }
        public int Quality { get; }
        public ItemReward(string slot, string prefab, int amount, int quality = 1)
        {
            Slot = slot;
            Prefab = prefab;
            Amount = amount;
            Quality = quality;
        }
        public string Payload() => JsonConvert.SerializeObject(new ItemRewardPayload { Prefab = Prefab, Amount = Amount, Quality = Quality });
    }

    public class QuestDefinition
    {
        public string ID { get; }
        public int Version { get; }
        public string Name { get; }
        public QuestDiscovery Discovery { get; }
        public QuestObjective Objective { get; }
        public IReadOnlyList<ItemReward> Rewards { get; }
        public QuestDefinition(string id, int version, string name, QuestDiscovery discovery, QuestObjective objective, params ItemReward[] rewards)
        {
            ID = id;
            Version = version;
            Name = name;
            Discovery = discovery;
            Objective = objective;
            Rewards = Array.AsReadOnly((ItemReward[])(rewards ?? Array.Empty<ItemReward>()).Clone());
        }
        public void Validate(string plugin)
        {
            string context = plugin + "/" + ID;
            if (string.IsNullOrWhiteSpace(ID) || Version <= 0 || string.IsNullOrWhiteSpace(Name) || Discovery == null)
                throw new ArgumentException(context + ": require quest ID, positive definition version, name and discovery.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateNode(Objective, ids, context);
            ids.Clear();
            foreach (ItemReward reward in Rewards)
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.Slot) || reward.Slot.Length > 128 || !ids.Add(reward.Slot) ||
                    string.IsNullOrWhiteSpace(reward.Prefab) || reward.Amount <= 0 || reward.Quality <= 0 || reward.Payload().Length > 8192)
                    throw new ArgumentException(context + ": invalid or duplicate reward slot " + reward?.Slot);
            }
        }
        static void ValidateNode(QuestObjective node, HashSet<string> ids, string context)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.ID) || !ids.Add(node.ID))
                throw new ArgumentException(context + ": missing/duplicate objective ID " + node?.ID);
            if (node.Kind == ObjectiveKind.All || node.Kind == ObjectiveKind.Sequence)
            {
                if (node.Children.Count == 0)
                    throw new ArgumentException(context + "/" + node.ID + ": empty composition.");
                foreach (QuestObjective child in node.Children)
                    ValidateNode(child, ids, context);
            }
            else if (string.IsNullOrWhiteSpace(node.Prefab) || node.Quantity <= 0)
                throw new ArgumentException(context + "/" + node.ID + ": require prefab and positive quantity.");
        }

        // Called after world registries initialize, never during early plugin registration.
        public void ValidatePrefabs(string plugin)
        {
            if (!ZNetScene.instance || !ObjectDB.instance)
                return;
            ValidatePrefabNode(Objective, plugin + "/" + ID);
            if (Discovery.CreaturePrefab != null && !ZNetScene.instance.GetPrefab(Discovery.CreaturePrefab))
                throw new ArgumentException(plugin + "/" + ID + ": missing discovery prefab " + Discovery.CreaturePrefab);
            foreach (ItemReward reward in Rewards)
            {
                UnityEngine.GameObject prefab = ObjectDB.instance.GetItemPrefab(reward.Prefab);
                if (!prefab || !prefab.GetComponent<ItemDrop>())
                    throw new ArgumentException(plugin + "/" + ID + "/" + reward.Slot + ": missing item prefab " + reward.Prefab);
            }
        }
        static void ValidatePrefabNode(QuestObjective node, string context)
        {
            if (node.Prefab != null && !ZNetScene.instance.GetPrefab(node.Prefab))
                throw new ArgumentException(context + "/" + node.ID + ": missing prefab " + node.Prefab);
            foreach (QuestObjective child in node.Children)
                ValidatePrefabNode(child, context);
        }
    }
}
