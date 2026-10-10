using Newtonsoft.Json;

namespace RobleysQuestingFramework
{
    public class ItemRewardPayload
    {
        [JsonProperty(Required = Required.Always)]
        public int Version = 1;
        [JsonProperty(Required = Required.Always)]
        public string Prefab;
        [JsonProperty(Required = Required.Always)]
        public int Amount;
        [JsonProperty(Required = Required.Always)]
        public int Quality;
    }

    public static class BuiltInItemRewards
    {
        public const string Handler = "rqf.item.v1";
        static bool Initialized;
        public static void Initialize()
        {
            if (Initialized)
                return;
            QuestRewardService.Register(Handler, Apply);
            Initialized = true;
        }
        static bool Apply(Player player, string json)
        {
            ItemRewardPayload payload = JsonConvert.DeserializeObject<ItemRewardPayload>(json);
            return payload != null && payload.Version == 1 && !string.IsNullOrWhiteSpace(payload.Prefab) &&
                ItemRewardService.GiveOrDrop(player, payload.Prefab, payload.Amount, payload.Quality);
        }
    }
}
