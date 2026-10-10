using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>NPC transport and world operations. Quest classes own all stage transitions.</summary>
    public static class QuestNpcService
    {
        const string InteractRPC = "RQF.NpcInteract";
        const string AttackRPC = "RQF.NpcAttacked";
        const string ProfileKey = "rqf_npc_profile";
        public const float InteractionRange = 6f;
        static readonly Dictionary<string, QuestNpcDefinition> Definitions = new Dictionary<string, QuestNpcDefinition>();
        static readonly HashSet<ZDOID> AttackedNPCs = new HashSet<ZDOID>();
        static float nextTick;

        public static event Action<long, ZDOID> Interacted;
        public static event Action<ZDOID, long> AttackedByPlayer;
        // Used for reconciliation of unloaded/destroyed givers and replicated state.
        public static event Action Updated;

        public static void Register(QuestNpcDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            Definitions[definition.ID] = definition;
        }

        public static void Reset()
        {
            nextTick = 0;
            AttackedNPCs.Clear();
            QuestHandInService.Reset();
        }

        static ZDO Data(Component component)
        {
            ZNetView view = component ? component.GetComponent<ZNetView>() : null;
            return view && view.IsValid() ? view.GetZDO() : null;
        }

        static QuestNpcDefinition Definition(ZDO data)
        {
            if (data == null)
                return null;
            if (Definitions.TryGetValue(data.GetString(ProfileKey), out QuestNpcDefinition definition) &&
                data.GetPrefab() == definition.PrefabName.GetStableHashCode())
                return definition;
            // Legacy markers are supplied by authors; the framework knows no concrete quest.
            return Definitions.Values.FirstOrDefault(d => data.GetPrefab() == d.PrefabName.GetStableHashCode() && data.GetBool(d.MarkerKey));
        }

        static bool WasAttacked(ZDO data, QuestNpcDefinition definition)
            => data.GetBool(definition.AttackedKey) || AttackedNPCs.Contains(data.m_uid);

        static bool Available(ZDO data, QuestNpcDefinition definition)
            => definition != null && !data.GetBool(definition.TamedKey) && !WasAttacked(data, definition);

        public static bool Exists(ZDOID id) => ZDOMan.instance != null && ZDOMan.instance.GetZDO(id) != null;
        public static bool Matches(ZDOID id, string definitionID)
            => Definition(ZDOMan.instance.GetZDO(id))?.ID == definitionID;
        public static bool WasAttacked(ZDOID id)
        {
            ZDO data = ZDOMan.instance.GetZDO(id);
            QuestNpcDefinition definition = Definition(data);
            return definition != null && WasAttacked(data, definition);
        }
        public static bool IsAvailable(ZDOID id)
        {
            ZDO data = ZDOMan.instance.GetZDO(id);
            return data != null && Available(data, Definition(data)) && data.GetFloat("health", 1) > 0;
        }

        /// <summary>Opt an existing matching creature or trader into a registered NPC definition.</summary>
        public static bool Attach(ZDOID id, QuestNpcDefinition definition)
        {
            if (!QuestNetwork.IsServer || definition == null || !Definitions.ContainsKey(definition.ID))
                return false;
            ZDO data = ZDOMan.instance.GetZDO(id);
            if (data == null || data.GetPrefab() != definition.PrefabName.GetStableHashCode())
                return false;
            data.SetOwner(ZNet.GetUID());
            data.Set(ProfileKey, definition.ID);
            data.Set(definition.MarkerKey, true);
            return true;
        }

        public static bool IsClaimedByAnother(Quest quest, ZDOID id)
            => QuestManager.WorldInstance.AllQuests.Any(q => q != quest && q.QuestGiverId == id &&
                (q.Stage & QuestStage.Started) != 0 && (q.Stage & QuestStage.Cancelled) == 0);

        /// <summary>Only call from a server event. Set QuestGiverId before starting the quest.</summary>
        public static bool CanClaim(Quest quest, ZDOID id)
            => QuestNetwork.IsServer && IsAvailable(id) && !IsClaimedByAnother(quest, id);

        /// <summary>Creates one NPC for a persistent encounter key. Existing keys never respawn.</summary>
        public static ZDOID SpawnOnce(long playerID, string encounterKey, QuestNpcDefinition definition)
        {
            if (!QuestNetwork.IsServer || ZoneSystem.instance.GetGlobalKey(encounterKey) ||
                !QuestNetwork.FindPlayer(playerID, out long peer, out Vector3 position))
                return ZDOID.None;
            GameObject prefab = ZNetScene.instance.GetPrefab(definition.PrefabName);
            if (!prefab)
                return ZDOID.None;
            position += Vector3.right * 2;
            if (ZoneSystem.instance.GetGroundHeight(position, out float height))
                position.y = height;
            position.y += 0.5f;
            GameObject npc = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            ZDO data = Data(npc.transform);
            if (data == null)
            { UnityEngine.Object.Destroy(npc); return ZDOID.None; }
            data.Set(ProfileKey, definition.ID);
            data.Set(definition.MarkerKey, true);
            npc.GetComponent<Character>()?.SetLevel(definition.Level);
            npc.GetComponent<MonsterAI>()?.SetAlerted(false);
            // Nearby clients have terrain loaded even when a dedicated server does not.
            data.SetOwner(peer);
            ZoneSystem.instance.SetGlobalKey(encounterKey);
            return data.m_uid;
        }

        public static void Tame(ZDOID id)
        {
            if (!QuestNetwork.IsServer)
                return;
            ZDO data = ZDOMan.instance.GetZDO(id);
            QuestNpcDefinition definition = Definition(data);
            if (!Available(data, definition))
                return;
            data.SetOwner(ZNet.GetUID());
            data.Set(definition.TamedKey, true);
        }

        public static void Tick()
        {
            if (!ZNet.instance || !ZNetScene.instance || !ZoneSystem.instance ||
                ZRoutedRpc.instance == null || Time.time < nextTick)
                return;
            nextTick = Time.time + 2;
            foreach (Character character in Character.GetAllCharacters())
            {
                ZDO data = Data(character);
                QuestNpcDefinition definition = Definition(data);
                if (definition == null || !data.GetBool(definition.TamedKey) ||
                    WasAttacked(data, definition) || character.IsTamed())
                    continue;
                if (character.GetComponent<ZNetView>().IsOwner())
                    character.GetComponent<MonsterAI>()?.MakeTame();
            }
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null)
                return;
            QuestManager.WorldInstance.RestoreWorldEffects();
            LocationObservationService.Tick();
            Updated?.Invoke();
        }

        static void Interact(long sender, ZDOID id)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                !QuestNetwork.PlayerInfo(sender, out long playerID, out Vector3 position) || !IsAvailable(id))
                return;
            if (Vector3.Distance(position, ZDOMan.instance.GetZDO(id).GetPosition()) > InteractionRange)
                return;
            Interacted?.Invoke(playerID, id);
        }

        static void ReceiveAttack(long sender, ZDOID id, long attackerID)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null)
                return;
            ZDO data = ZDOMan.instance.GetZDO(id);
            QuestNpcDefinition definition = Definition(data);
            if (definition == null || data.GetOwner() != sender || data.GetBool(definition.TamedKey) || attackerID == 0)
                return;
            if (definition.HostileWhenAttacked)
                AttackedNPCs.Add(id);
            AttackedByPlayer?.Invoke(id, attackerID);
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCs
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                Reset();
                ZRoutedRpc.instance.Register<ZDOID>(InteractRPC, Interact);
                ZRoutedRpc.instance.Register<ZDOID, long>(AttackRPC, ReceiveAttack);
            }
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        static class AttackObservation
        {
            [HarmonyPrefix]
            static void Prefix(Character __instance, HitData hit)
            {
                ZDO data = Data(__instance);
                QuestNpcDefinition definition = Definition(data);
                if (!Available(data, definition) || __instance.IsDead() || hit == null)
                    return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                Player attacker = hit.GetAttacker() as Player;
                if (!view.IsOwner() || !attacker)
                    return;
                if (definition.HostileWhenAttacked)
                {
                    data.Set(definition.AttackedKey, true);
                    MonsterAI ai = __instance.GetComponent<MonsterAI>();
                    if (ai)
                    { ai.SetTarget(attacker); ai.SetAlerted(true); }
                }
                if (QuestNetwork.IsServer)
                    ReceiveAttack(ZNet.GetUID(), data.m_uid, attacker.GetPlayerID());
                else
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), AttackRPC, data.m_uid, attacker.GetPlayerID());
            }
        }

        // Both adapters publish the same interaction event; neither knows any quest IDs.
        [HarmonyPatch]
        static class Hover
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Tameable), "GetHoverText");
                yield return AccessTools.Method(typeof(Trader), "GetHoverText");
            }
            [HarmonyPrefix]
            static bool Prefix(Component __instance, ref string __result)
            {
                ZDO data = Data(__instance);
                QuestNpcDefinition definition = Definition(data);
                if (!Available(data, definition))
                    return true;
                __result = Localization.instance.Localize(definition.HoverText);
                return false;
            }
        }

        [HarmonyPatch]
        static class Interaction
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Tameable), "Interact");
                yield return AccessTools.Method(typeof(Trader), "Interact");
            }
            [HarmonyPrefix]
            static bool Prefix(Component __instance, Humanoid __0, bool __1, ref bool __result)
            {
                ZDO data = Data(__instance);
                if (!Available(data, Definition(data)))
                    return true;
                __result = true;
                if (__1 || __0 != Player.m_localPlayer)
                    return false;
                if (QuestNetwork.IsServer)
                    Interact(ZNet.GetUID(), data.m_uid);
                else
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), InteractRPC, data.m_uid);
                return false;
            }
        }

        static bool BlocksTaming(Component npc)
        {
            ZDO data = Data(npc);
            QuestNpcDefinition definition = Definition(data);
            return Available(data, definition) && definition.PreventNormalTaming;
        }

        [HarmonyPatch(typeof(Tameable), "TamingUpdate")]
        static class PreventNormalTaming
        {
            [HarmonyPrefix]
            static bool Prefix(Tameable __instance) => !BlocksTaming(__instance);
        }

        [HarmonyPatch(typeof(MonsterAI), "UpdateConsumeItem")]
        static class PreventGroundFeeding
        {
            [HarmonyPrefix]
            static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                if (!BlocksTaming(__instance))
                    return true;
                __result = false;
                return false;
            }
        }

        static bool IsFriendly(Component npc)
        {
            ZDO data = Data(npc);
            QuestNpcDefinition definition = Definition(data);
            return Available(data, definition) && definition.Friendly;
        }

        [HarmonyPatch(typeof(BaseAI), "IsEnemy", new[] { typeof(Character), typeof(Character) })]
        static class Friendly
        {
            [HarmonyPostfix]
            static void Postfix(Character a, Character b, ref bool __result)
            {
                // Melee, area attacks, and projectiles also use IsEnemy to filter hits.
                // Let players hit NPCs that become hostile when attacked, so RPC_Damage
                // can record betrayal. The reverse direction still keeps the NPC peaceful.
                if (a is Player)
                {
                    ZDO targetData = Data(b);
                    QuestNpcDefinition targetDefinition = Definition(targetData);
                    if (Available(targetData, targetDefinition) && targetDefinition.HostileWhenAttacked)
                        return;
                }
                if (IsFriendly(a) || IsFriendly(b))
                    __result = false;
            }
        }

        [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
        static class Peaceful
        {
            [HarmonyPrefix]
            static void Prefix(MonsterAI __instance, ref Character ___m_targetCreature, ref StaticTarget ___m_targetStatic)
            {
                if (!IsFriendly(__instance))
                    return;
                ___m_targetCreature = null;
                ___m_targetStatic = null;
                __instance.SetAlerted(false);
            }
        }
    }
}
