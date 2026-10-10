using System.Globalization;

namespace RobleysQuestingFramework
{
    public class DungeonEncounter
    {
        public long PlayerID { get; }
        public ZDOID DungeonID { get; }
        public int PrefabHash { get; }
        public string InstanceKey => KeyFor(DungeonID);

        public DungeonEncounter(long playerID, ZDOID dungeonID, int prefabHash)
        {
            PlayerID = playerID;
            DungeonID = dungeonID;
            PrefabHash = prefabHash;
        }

        public static string KeyFor(ZDOID id) => id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

        public static ZDOID ParseKey(string key)
        {
            string[] parts = (key ?? "").Split(':');
            return parts.Length == 2 &&
                long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) &&
                uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id)
                ? new ZDOID(user, id) : ZDOID.None;
        }
    }
}
