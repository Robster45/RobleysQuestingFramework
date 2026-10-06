using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jotunn.Managers;

namespace RobleysQuestingFramework
{
    internal class TestQuest_10Wood : Quest
    {
        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(
                s => Has(s, QuestStage.Started) && !IsFinishedStage(s),
                SubscribeInventoryChanges,
                UnsubscribeInventoryChanges);
        }

        private readonly Inventory inventory;

        public TestQuest_10Wood(Inventory inventory)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Name = "Gather 10 wood";
        }

        private void SubscribeInventoryChanges()
        {
            inventory.m_onChanged += CheckHas10Wood;
        }

        private void UnsubscribeInventoryChanges()
        {
            inventory.m_onChanged -= CheckHas10Wood;
        }

        internal void CheckHas10Wood()
        {
            SetCriteriaMet(inventory.CountItems("$item_wood") >= 10);
            Jotunn.Logger.LogInfo($"Met Criteria? {Stage.HasFlag(QuestStage.CriteriaMet)}");
        }
    }
    internal class TestQuest_KillNecks : Quest
    {
        protected override void ConfigureSubscriptions()
        {
            RegisterSubscription(
                s => Has(s, QuestStage.Started) && !IsFinishedStage(s),
                SubscribeKillEvents,
                UnsubscribeKillEvents);
        }

        private void SubscribeKillEvents()
        {
            Jotunn.Logger.LogInfo("KillNecks Quest Loaded");
        }

        private void UnsubscribeKillEvents()
        {
            Jotunn.Logger.LogInfo("KillNecks Quest unloaded");
        }
    }
}

