using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RobleysQuestingFramework
{
    /// <summary>Loaded interior observations from the player's peer, validated and routed on the server.</summary>
    public static class DungeonObservationService
    {
        const string SampleRPC = "RQF.DungeonSample";
        static readonly HashSet<DungeonGenerator> Generators = new HashSet<DungeonGenerator>();
        static readonly HashSet<DungeonGenerator> ReadyGenerators = new HashSet<DungeonGenerator>();
        static readonly List<Watch> Watches = new List<Watch>();
        static readonly Dictionary<long, Observation> Observations = new Dictionary<long, Observation>();
        static float NextTick;
        public static event Action<DungeonEncounter> Entered;

        public static bool TryGetPosition(ZDOID dungeon, out Vector3 position)
        {
            ZDO data = ZDOMan.instance?.GetZDO(dungeon);
            position = data == null ? default : data.GetPosition();
            return data != null;
        }

        public static IDisposable Subscribe(long player, ZDOID dungeon, Action<ClearAreaStatus> callback)
        {
            if (player == 0 || dungeon == ZDOID.None || callback == null)
            {
                throw new ArgumentException("Provide a player, dungeon identity and callback.");
            }
            Watch watch = new Watch { Player = player, Dungeon = dungeon, Callback = callback };
            Watches.Add(watch);
            return watch;
        }

        public static void Reset()
        {
            Generators.Clear();
            ReadyGenerators.Clear();
            Observations.Clear();
            Watches.Clear();
            NextTick = 0;
        }

        // Vanilla interiors occupy their own elevated zone. Outdoor enemies, adjacent
        // dungeons and surface spawners must never affect an interior's clear state.
        static bool SameInterior(Vector3 position, Vector3 dungeon)
            => Character.InInterior(position) && Character.InInterior(dungeon) &&
                ZoneSystem.GetZone(position).Equals(ZoneSystem.GetZone(dungeon)) && Mathf.Abs(position.y - dungeon.y) < 128f;

        public static void Tick()
        {
            if (!ZNet.instance || ZRoutedRpc.instance == null || Time.time < NextTick)
            {
                return;
            }
            NextTick = Time.time + 2f;
            Player player = Player.m_localPlayer;
            if (!player)
            {
                return;
            }
            DungeonGenerator current = null;
            if (!player.IsDead())
            {
                foreach (DungeonGenerator generator in Generators)
                {
                    if (generator && generator.m_nview && generator.m_nview.IsValid() &&
                        SameInterior(player.transform.position, generator.m_nview.GetZDO().GetPosition()))
                    {
                        current = generator;
                        break;
                    }
                }
            }
            ZPackage package = new ZPackage();
            if (!current)
            {
                package.Write(ZDOID.None);
            }
            else
            {
                Vector3 center = current.m_nview.GetZDO().GetPosition();
                bool ready = ReadyGenerators.Contains(current) && current.m_roomsToLoad == 0 &&
                    ZNetScene.instance && ZNetScene.instance.IsAreaReady(center);
                package.Write(current.m_nview.GetZDO().m_uid);
                package.Write(ready);
                ClearAreaStatus status = ready ? Scan(player, center) : default;
                package.Write(status.Enemies);
                package.Write(status.Spawners);
            }
            if (QuestNetwork.IsServer)
            {
                package.SetPos(0);
                ReceiveSample(ZNet.GetUID(), package);
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), SampleRPC, package);
            }
        }

        static ClearAreaStatus Scan(Player player, Vector3 center)
        {
            int enemies = 0;
            int spawners = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character && !character.IsPlayer() && !character.IsDead() &&
                    SameInterior(character.transform.position, center) && BaseAI.IsEnemy(player, character))
                {
                    enemies++;
                }
            }
            foreach (SpawnArea area in UnityEngine.Object.FindObjectsByType<SpawnArea>(FindObjectsSortMode.None))
            {
                if (area && area.m_nview && area.m_nview.IsValid() && SameInterior(area.transform.position, center))
                {
                    spawners++;
                }
            }
            foreach (CreatureSpawner spawner in CreatureSpawner.m_creatureSpawners)
            {
                if (!spawner || !spawner.m_nview || !spawner.m_nview.IsValid() ||
                    !SameInterior(spawner.transform.position, center) || spawner.CheckGlobalKeys() || spawner.CheckGroupSpawnBlocked())
                {
                    continue;
                }
                ZDOConnection connection = spawner.m_nview.GetZDO().GetConnection();
                // One-shot dungeon spawners retain their Spawned connection after the creature
                // dies. Untriggered rooms must block clearing until their defenders have spawned.
                if (connection == null || connection.m_type != ZDOExtraData.ConnectionType.Spawned)
                {
                    spawners++;
                }
            }
            return new ClearAreaStatus(enemies, spawners);
        }

        static void ReceiveSample(long sender, ZPackage package)
        {
            if (!QuestNetwork.IsServer || QuestManager.WorldInstance == null ||
                !QuestNetwork.PlayerInfo(sender, out long player, out Vector3 position))
            {
                return;
            }
            try
            {
                ZDOID id = package.ReadZDOID();
                if (id == ZDOID.None)
                {
                    Observations.Remove(player);
                    return;
                }
                bool ready = package.ReadBool();
                int enemies = package.ReadInt();
                int spawners = package.ReadInt();
                ZDO dungeon = ZDOMan.instance.GetZDO(id);
                if (dungeon == null || !SameInterior(position, dungeon.GetPosition()) || enemies < 0 || spawners < 0)
                {
                    Observations.Remove(player);
                    return;
                }
                if (!Observations.TryGetValue(player, out Observation observation) || observation.Dungeon != id)
                {
                    observation = new Observation { Dungeon = id };
                    Observations[player] = observation;
                }
                ClearAreaStatus status = new ClearAreaStatus(enemies, spawners);
                Entered?.Invoke(new DungeonEncounter(player, id, dungeon.GetPrefab()));
                if (!observation.Confirmation.Observe(Time.time, ready, status))
                {
                    return;
                }
                foreach (Watch watch in Watches.ToArray())
                {
                    if (!watch.Disposed && watch.Player == player && watch.Dungeon == id)
                    {
                        watch.Callback(status);
                    }
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning("Invalid dungeon observation: " + ex.Message);
            }
        }

        class Observation
        {
            public ZDOID Dungeon;
            public readonly ClearConfirmation Confirmation = new ClearConfirmation();
        }

        class Watch : IDisposable
        {
            public long Player;
            public ZDOID Dungeon;
            public Action<ClearAreaStatus> Callback;
            public bool Disposed;
            public void Dispose()
            {
                Disposed = true;
                Watches.Remove(this);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPC
        {
            [HarmonyPostfix]
            static void Postfix() => ZRoutedRpc.instance.Register<ZPackage>(SampleRPC, ReceiveSample);
        }

        [HarmonyPatch(typeof(DungeonGenerator), "Awake")]
        static class GeneratorAwake
        {
            [HarmonyPostfix]
            static void Postfix(DungeonGenerator __instance) => Generators.Add(__instance);
        }

        [HarmonyPatch(typeof(DungeonGenerator), "Spawn")]
        static class RoomsLoaded
        {
            [HarmonyPostfix]
            static void Postfix(DungeonGenerator __instance) => ReadyGenerators.Add(__instance);
        }

        [HarmonyPatch(typeof(DungeonGenerator), "Generate", new[] { typeof(int), typeof(ZoneSystem.SpawnMode) })]
        static class RoomsGenerated
        {
            [HarmonyPostfix]
            static void Postfix(DungeonGenerator __instance) => ReadyGenerators.Add(__instance);
        }

        [HarmonyPatch(typeof(DungeonGenerator), "OnDestroy")]
        static class GeneratorDestroyed
        {
            [HarmonyPrefix]
            static void Prefix(DungeonGenerator __instance)
            {
                Generators.Remove(__instance);
                ReadyGenerators.Remove(__instance);
            }
        }
    }
}
