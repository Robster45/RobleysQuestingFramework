using BepInEx;
using System;

namespace RobleysTestQuests
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency(RobleysQuestingFramework.RobleysQuestingFramework.PluginGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.robleystestquests";
        public const string PluginName = "RobleysTestQuests";
        public const string PluginVersion = "0.0.1";

        IDisposable _declarative;

        void Awake()
        {
            _declarative = DeclarativeExamples.Register();
            TestQuestBootstrap.Register();
            BoarRunestoneQuest.Register();
        }

        void OnDestroy()
        {
            _declarative?.Dispose();
            TestQuestBootstrap.Unregister();
        }
    }
}
