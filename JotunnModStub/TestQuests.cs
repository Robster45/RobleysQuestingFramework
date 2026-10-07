namespace RobleysQuestingFramework
{
    public class TestQuest_10Wood : ResourceCollectionQuest
    {
        const int RequiredWood = 10;
        internal TestQuest_10Wood(QuestInventory inventory, long playerID)
            : base(inventory, playerID, new ResourceCollectionCriteria("Wood", RequiredWood))
        {
            Name = "Gather 10 wood";
        }

        protected override void ActivateQuestCompletionEffects()
        {
            Player player = Player.GetPlayer(PlayerID);
            Jotunn.Logger.LogInfo($"Wood payout: found={player != null}, local={player && player == Player.m_localPlayer}");

            // This example requires the player object to be loaded on the server.
            // Remote-client reward delivery still needs a separate implementation.
            if (player)
            {
                Jotunn.Logger.LogInfo($"QUEST COMPLETED: Removing 10 wood, adding 100 xp to woodcutting skill");
                player.RaiseSkill(Skills.SkillType.WoodCutting, 100);
                player.GetInventory().RemoveItem("$item_wood", RequiredWood);
            }
        }
    }

    public class TestQuest_KillNecks : KillQuest
    {
        internal TestQuest_KillNecks(long playerID)
            : base(playerID, new KillCriteria("Neck", 5))
        {
            Name = "Kill 5 necks";
        }

        protected override void ActivateQuestCompletionEffects()
        {
            Jotunn.Logger.LogInfo($"QUEST COMPLETED: Triggering retaliatory raid");
            Player player = Player.GetPlayer(PlayerID);

            if (ZNet.instance && ZNet.instance.IsServer() && RandEventSystem.instance && player)
                RandEventSystem.instance.SetRandomEventByName("army_eikthyr", player.transform.position);
        }
    }
}
