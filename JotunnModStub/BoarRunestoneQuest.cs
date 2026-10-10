using System;

namespace RobleysQuestingFramework
{
    public class BoarRunestoneQuest : ResourceCollectionQuest
    {
        public const string ID = "boar.runestone_raspberries";
        public const int RequiredRaspberries = 100;
        static readonly QuestNpcDefinition Boar = new QuestNpcDefinition(
            ID, "Boar", 3,
            "Friendly boar ★★\n[<color=yellow><b>$KEY_Use</b></color>] Talk / Hand in 100 raspberries",
            friendly: true, hostileWhenAttacked: true, preventNormalTaming: true,
            markerKey: "rqf_quest_boar", tamedKey: "rqf_boar_reward", attackedKey: "rqf_boar_hostile");

        [NonSerialized] IDisposable _locationWatch;

        public static void Register()
        {
            QuestNpcService.Register(Boar);
            QuestManager.RegisterQuest(ID, (playerID, inventory) => new BoarRunestoneQuest(playerID, inventory),
                discoverForEveryPlayer: true);
        }

        public BoarRunestoneQuest(long playerID, QuestInventory inventory)
            : base(inventory, playerID, new ResourceCollectionCriteria("Raspberry", RequiredRaspberries))
        {
            Name = "A boar's trust";
        }

        protected override void SubscribeDiscoveryEvents()
            => _locationWatch = LocationObservationService.Subscribe(PlayerID, "Runestone_Boars", 12f, EncounterStone);

        protected override void UnsubscribeDiscoveryEvents()
        {
            _locationWatch?.Dispose();
            _locationWatch = null;
        }

        protected override void ConfigureSubscriptions()
        {
            base.ConfigureSubscriptions();
            // Hidden players may also discover a boar spawned by someone else by interacting.
            RegisterSubscription(stage => !IsFinishedStage(stage),
                () => QuestNpcService.Interacted += InteractWithBoar,
                () => { QuestNpcService.Interacted -= InteractWithBoar; QuestHandInService.Cancel(this); });
            RegisterSubscription(stage => Has(stage, QuestStage.Discovered) && !IsFinishedStage(stage),
                () => { QuestNpcService.AttackedByPlayer += BoarAttacked; QuestNpcService.Updated += CheckBoar; },
                () => { QuestNpcService.AttackedByPlayer -= BoarAttacked; QuestNpcService.Updated -= CheckBoar; });
        }

        void EncounterStone(LocationEncounter encounter)
        {
            if (Stage != QuestStage.Hidden || encounter.PlayerID != PlayerID)
                return;
            ZDOID giver = QuestNpcService.SpawnOnce(PlayerID, encounter.PersistentKey("rqf_boar_stone"), Boar);
            if (giver == ZDOID.None)
                return;
            QuestGiverId = giver;
            Discover();
            QuestManager.WorldInstance.Save();
            QuestNetwork.Tell(PlayerID, "A friendly two-star boar appears nearby. Interact with it to earn its trust.");
        }

        void InteractWithBoar(long playerID, ZDOID giver)
        {
            if (playerID != PlayerID || IsFinishedStage(Stage) || !QuestNpcService.Matches(giver, ID))
                return;
            if (QuestGiverId != ZDOID.None && QuestGiverId != giver)
            {
                QuestNetwork.Tell(PlayerID, "Return to the boar that gave you your quest.");
                return;
            }
            if (!QuestNpcService.CanClaim(this, giver))
            {
                QuestNetwork.Tell(PlayerID, "This boar is already waiting for another adventurer's raspberries.");
                return;
            }
            if (!Has(Stage, QuestStage.Started))
            {
                QuestGiverId = giver;
                if (Stage == QuestStage.Hidden)
                    Discover();
                Accept();
                QuestManager.WorldInstance.Save();
                QuestNetwork.Tell(PlayerID, "A boar's trust: bring me 100 raspberries, then interact again to hand them over.");
                return;
            }
            QuestHandInService.Request(this, "rqf_boar_payment", Criteria, FinishHandIn,
                "Bring all 100 raspberries in your inventory before handing them in.");
        }

        void BoarAttacked(ZDOID giver, long attackerID)
        {
            if (giver == QuestGiverId)
                CancelWithMessage("A boar's trust cancelled: the boar was attacked and is now hostile.");
        }

        void CheckBoar()
        {
            if (QuestGiverId == ZDOID.None || IsFinishedStage(Stage))
                return;
            if (QuestNpcService.WasAttacked(QuestGiverId))
                CancelWithMessage("A boar's trust cancelled: the boar was attacked and is now hostile.");
            else if (!QuestNpcService.Exists(QuestGiverId))
                CancelWithMessage("A boar's trust cancelled: the boar is gone.");
            else if (!Has(Stage, QuestStage.Started) &&
                (!QuestNpcService.IsAvailable(QuestGiverId) || QuestNpcService.IsClaimedByAnother(this, QuestGiverId)))
                CancelWithMessage("A boar's trust cancelled: another adventurer claimed this boar.");
        }

        void CancelWithMessage(string message)
        {
            if (IsFinishedStage(Stage))
                return;
            CancelQuest();
            QuestManager.WorldInstance.Save();
            QuestNetwork.Tell(PlayerID, message);
        }

        protected override void ActivateQuestCompletionEffects()
        {
            QuestNpcService.Tame(QuestGiverId);
            QuestNetwork.Tell(PlayerID, "The boar accepts your 100 raspberries. You have earned its trust!");
        }

        public override void OnWorldReady()
        {
            // Idempotent world-state repair, not a replay of item rewards or dialogue.
            if (Has(Stage, QuestStage.Completed))
                QuestNpcService.Tame(QuestGiverId);
            else
                CheckBoar();
        }

        public void Accept()
        {
            StartQuest();
            CheckCompletionCriteria();
        }

        // Called only after the paying client's inventory removal is acknowledged.
        public void FinishHandIn()
        {
            if (IsFinishedStage(Stage))
                return;
            SetCriteriaMet(true);
            CompleteQuest();
            QuestManager.WorldInstance.Save();
        }
    }
}
