using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;

namespace RobleysQuestingFramework
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class RobleysQuestingFramework : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.robleysquestingframework";

        public const string PluginName = "RobleysQuestingFramework";

        public const string PluginVersion = "0.0.1";

        
        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        Harmony _harmony;

        void Awake()
        {
            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("ModStub has landed");

            // To learn more about Jotunn's features, go to
            // https://valheim-modding.github.io/Jotunn/tutorials/overview.html

            QuestJournal.Initialize(Config);
            TestQuestBootstrap.Register();
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(QuestManager).Assembly);
        }

        void Update()
        {
            InventoryObservationService.Tick();
            QuestJournal.Tick();
        }

        void OnDestroy()
        {
            QuestJournal.Destroy();
            TestQuestBootstrap.Unregister();
            QuestManager.Unload();
            InventoryObservationService.Reset();
            KillObservationService.Reset();
            CreatureDiscoveryObservationService.Reset();
            _harmony?.UnpatchSelf();
        }
    }
}
