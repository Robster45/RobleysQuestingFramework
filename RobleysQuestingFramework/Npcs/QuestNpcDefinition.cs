namespace RobleysQuestingFramework
{
    /// <summary>Reusable NPC behavior. Register on every peer before entering a world.</summary>
    public class QuestNpcDefinition
    {
        public string ID { get; }
        public string PrefabName { get; }
        public int Level { get; }
        public string HoverText { get; }
        public bool Friendly { get; }
        public bool HostileWhenAttacked { get; }
        public bool PreventNormalTaming { get; }
        // Optional keys let authors preserve existing world data when migrating an NPC.
        public string MarkerKey { get; }
        public string TamedKey { get; }
        public string AttackedKey { get; }

        public QuestNpcDefinition(string id, string prefabName, int level, string hoverText,
            bool friendly = true, bool hostileWhenAttacked = true, bool preventNormalTaming = true,
            string markerKey = null, string tamedKey = null, string attackedKey = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(prefabName) || level < 1)
                throw new System.ArgumentException("An NPC needs an ID, prefab, and positive level.");
            ID = id;
            PrefabName = prefabName;
            Level = level;
            HoverText = hoverText ?? "";
            Friendly = friendly;
            HostileWhenAttacked = hostileWhenAttacked;
            PreventNormalTaming = preventNormalTaming;
            MarkerKey = markerKey ?? "rqf_npc_" + id;
            TamedKey = tamedKey ?? MarkerKey + "_tamed";
            AttackedKey = attackedKey ?? MarkerKey + "_attacked";
        }
    }
}
