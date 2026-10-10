using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RobleysQuestingFramework
{
    public class DefinedQuest : Quest
    {
        readonly QuestDefinition _definition;
        readonly string _plugin;
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool _observing;
        bool _discovering;
        public QuestDefinition Definition => _definition;
        protected override bool RequiresInteraction => false;

        public DefinedQuest(long playerID, QuestDefinition definition, string plugin) : base(playerID)
        {
            definition.Validate(plugin);
            _definition = definition;
            _plugin = plugin;
            Name = definition.Name;
            foreach (QuestObjective node in Nodes(definition.Objective))
                _counts.Add(node.ID, 0);
        }
        static IEnumerable<QuestObjective> Nodes(QuestObjective root)
        {
            yield return root;
            foreach (QuestObjective child in root.Children)
                foreach (QuestObjective node in Nodes(child))
                    yield return node;
        }
        bool Done(QuestObjective node) => node.Children.Count == 0
            ? _counts[node.ID] >= node.Quantity : node.Children.All(Done);
        string Current(QuestObjective node) => node.Kind == ObjectiveKind.Sequence
            ? node.Children.FirstOrDefault(child => !Done(child))?.ID : null;
        IEnumerable<QuestObjective> Active(QuestObjective node)
        {
            if (Done(node))
                yield break;
            if (node.Children.Count == 0)
                yield return node;
            else
            {
                IEnumerable<QuestObjective> children = node.Kind == ObjectiveKind.Sequence
                    ? node.Children.Where(child => child.ID == Current(node)) : node.Children;
                foreach (QuestObjective child in children)
                    foreach (QuestObjective leaf in Active(child))
                        yield return leaf;
            }
        }
        public int Count(string objectiveID) => _counts[objectiveID];
        public override string JournalProgress
        {
            get
            {
                HashSet<string> active = new HashSet<string>(Active(_definition.Objective).Select(node => node.ID));
                return string.Join("; ", Nodes(_definition.Objective).Where(node => node.Children.Count == 0).Select(node =>
                    (Done(node) ? "Done: " : active.Contains(node.ID) ? "Current: " : "Later: ") +
                    (node.Kind == ObjectiveKind.Kill ? "Kill " : "Destroy ") + node.Prefab + ": " + _counts[node.ID] + "/" + node.Quantity));
            }
        }
        protected override void SubscribeDiscoveryEvents()
        {
            _discovering = true;
            InventoryObservationService.PlayerAvailable += OnAvailable;
            CreatureDiscoveryObservationService.PlayerDiscoveredCreature += OnEncounter;
        }
        protected override void UnsubscribeDiscoveryEvents()
        {
            _discovering = false;
            InventoryObservationService.PlayerAvailable -= OnAvailable;
            CreatureDiscoveryObservationService.PlayerDiscoveredCreature -= OnEncounter;
        }
        public void OnAvailable(long playerID)
        {
            if (_discovering && playerID == PlayerID && Stage == QuestStage.Hidden && _definition.Discovery.CreaturePrefab == null)
                Discover();
        }
        void OnEncounter(long playerID, int prefab)
        {
            if (_discovering && playerID == PlayerID && Stage == QuestStage.Hidden && _definition.Discovery.CreaturePrefab != null &&
                prefab == _definition.Discovery.CreaturePrefab.GetStableHashCode())
                Discover();
        }
        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(stage => Has(stage, QuestStage.Started) && !IsFinishedStage(stage), Attach, Detach);
        }
        void Attach()
        {
            _observing = true;
            KillObservationService.PlayerKilledCreature += OnKill;
            SpawnerObservationService.PlayerDestroyedSpawner += OnDestroy;
        }
        void Detach()
        {
            _observing = false;
            KillObservationService.PlayerKilledCreature -= OnKill;
            SpawnerObservationService.PlayerDestroyedSpawner -= OnDestroy;
        }
        void OnKill(long player, int prefab) => Observe(player, prefab, ObjectiveKind.Kill);
        void OnDestroy(long player, int prefab) => Observe(player, prefab, ObjectiveKind.DestroySpawner);
        void Observe(long player, int prefab, ObjectiveKind kind)
        {
            if (!_observing || player != PlayerID || IsFinishedStage(Stage))
                return;
            // Freeze eligible leaves before changing any counters. A single report cannot cross a sequence boundary.
            QuestObjective[] eligible = Active(_definition.Objective).Where(node => node.Kind == kind &&
                node.Prefab.GetStableHashCode() == prefab).ToArray();
            if (eligible.Length == 0)
                return;
            foreach (QuestObjective node in eligible)
                _counts[node.ID] = Math.Min(node.Quantity, _counts[node.ID] + 1);
            CheckCompletionCriteria();
            NotifyTrackingChanged();
        }
        protected override void CheckCompletionCriteria() => SetCriteriaMet(Done(_definition.Objective));
        protected override void ActivateQuestCompletionEffects() { }
        protected override Action PrepareCompletion()
        {
            if (QuestManager.WorldInstance == null)
                throw new InvalidOperationException(_plugin + "/" + _definition.ID + ": completion requires a managed server quest.");
            return QuestManager.WorldInstance.StageCompletionPlan(this);
        }
        public override void OnWorldReady()
        {
            try { _definition.ValidatePrefabs(_plugin); }
            catch (ArgumentException ex) { Jotunn.Logger.LogWarning(ex.Message); }
            if (InventoryObservationService.GetAvailablePlayerIDs().Contains(PlayerID))
                OnAvailable(PlayerID);
            if (Has(Stage, QuestStage.Started) && !IsFinishedStage(Stage) && Done(_definition.Objective))
                CheckCompletionCriteria();
        }
        public void ValidateRestoredStage(QuestStage stage)
        {
            if ((Has(stage, QuestStage.Completed) || Has(stage, QuestStage.CriteriaMet)) && !Done(_definition.Objective))
                throw new InvalidDataException(_plugin + "/" + _definition.ID + ": terminal/ready stage has unfinished objectives.");
        }
        static JToken Text(string value) => value == null ? JValue.CreateNull() : new JValue(value);

        JObject Layout()
        {
            JObject layout = new JObject();
            foreach (QuestObjective node in Nodes(_definition.Objective).OrderBy(node => node.ID, StringComparer.Ordinal))
            {
                IEnumerable<string> children = node.Children.Select(child => child.ID);
                if (node.Kind == ObjectiveKind.All)
                    children = children.OrderBy(id => id, StringComparer.Ordinal);
                layout[node.ID] = new JObject { ["Version"] = 1, ["Kind"] = node.Kind.ToString(), ["Prefab"] = Text(node.Prefab),
                    ["Quantity"] = node.Quantity, ["Children"] = new JArray(children) };
            }
            return new JObject { ["Root"] = _definition.Objective.ID, ["Objectives"] = layout,
                ["Discovery"] = Text(_definition.Discovery.CreaturePrefab),
                ["Rewards"] = new JArray(_definition.Rewards.OrderBy(reward => reward.Slot, StringComparer.Ordinal)
                    .Select(reward => new JObject { ["Slot"] = reward.Slot, ["Payload"] = reward.Payload() })) };
        }
        public override JToken SaveState()
        {
            JObject nodes = new JObject();
            foreach (QuestObjective node in Nodes(_definition.Objective))
                nodes[node.ID] = new JObject { ["Count"] = _counts[node.ID], ["Complete"] = Done(node), ["Current"] = Text(Current(node)) };
            return new JObject { ["SchemaVersion"] = 1, ["DefinitionVersion"] = _definition.Version, ["Layout"] = Layout(), ["Nodes"] = nodes };
        }
        public override void RestoreState(JToken state)
        {
            if (_observing || _discovering)
                throw new InvalidOperationException("Restore declarative state before subscribing.");
            if (!(state is JObject saved) || (int?)saved["SchemaVersion"] != 1 || (int?)saved["DefinitionVersion"] != _definition.Version ||
                !JToken.DeepEquals(saved["Layout"], Layout()) || !(saved["Nodes"] is JObject nodes))
                throw new InvalidDataException(_plugin + "/" + _definition.ID + ": unsupported state/definition/objective version or changed layout; restore the matching content version or supply an explicit migration.");
            foreach (QuestObjective node in Nodes(_definition.Objective))
            {
                if (!(nodes[node.ID] is JObject entry) || entry["Count"]?.Type != JTokenType.Integer)
                    throw new InvalidDataException(_plugin + "/" + _definition.ID + "/" + node.ID + ": missing objective counter.");
                int count = (int)entry["Count"];
                _counts[node.ID] = Math.Max(0, Math.Min(node.Quantity, count));
            }
            JObject normalized = (JObject)saved.DeepClone();
            foreach (QuestObjective node in Nodes(_definition.Objective))
                normalized["Nodes"][node.ID]["Count"] = _counts[node.ID];
            if (!JToken.DeepEquals(normalized, SaveState()))
                throw new InvalidDataException(_plugin + "/" + _definition.ID + ": unknown fields or inconsistent objective/sequence state; record retained without activation.");
            ValidateSequence(_definition.Objective);
        }
        void ValidateSequence(QuestObjective node)
        {
            bool waiting = false;
            foreach (QuestObjective child in node.Children)
            {
                if (node.Kind == ObjectiveKind.Sequence && waiting && Nodes(child).Any(leaf => _counts[leaf.ID] != 0))
                    throw new InvalidDataException(_plugin + "/" + _definition.ID + "/" + child.ID + ": future step has progress.");
                waiting |= !Done(child);
                ValidateSequence(child);
            }
        }
    }
}
