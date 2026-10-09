using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RobleysQuestingFramework
{
    // Read-only HUD. Only visible clients subscribe to server-owned journal snapshots.
    public static class QuestJournal
    {
        const string VisibilityRPC = "RobleysQuestingFramework.JournalVisibility";
        const string SnapshotRPC = "RobleysQuestingFramework.JournalSnapshot";
        static readonly Dictionary<long, long> Viewers = new Dictionary<long, long>();
        static readonly HashSet<long> DirtyPlayers = new HashSet<long>();
        static ConfigEntry<KeyboardShortcut> ToggleKey;
        static ConfigEntry<float> Width, Gap, OffsetX, OffsetY, Opacity;
        static ConfigEntry<int> FontSize;
        static ConfigEntry<bool> ShowCancelled;
        static GameObject Panel;
        static Text Body;
        static Player LocalPlayer;
        static bool Visible, LayoutDirty;
        static string Snapshot = "Loading quests...";
        static readonly Vector3[] Corners = new Vector3[4];

        public static void Initialize(ConfigFile config)
        {
            ToggleKey = config.Bind("Journal", "ToggleKey", new KeyboardShortcut(KeyCode.J), "Toggle the quest journal.");
            Width = config.Bind("Journal", "Width", 280f, new ConfigDescription("Panel width in UI units.", new AcceptableValueRange<float>(160, 800)));
            Gap = config.Bind("Journal", "MinimapGap", 12f, new ConfigDescription("Gap below the minimap in UI units.", new AcceptableValueRange<float>(0, 300)));
            OffsetX = config.Bind("Journal", "OffsetX", 0f, "Horizontal offset from the minimap's right edge; positive moves right.");
            OffsetY = config.Bind("Journal", "OffsetY", 0f, "Vertical offset; positive moves up.");
            FontSize = config.Bind("Journal", "FontSize", 16, new ConfigDescription("Journal text size.", new AcceptableValueRange<int>(10, 32)));
            Opacity = config.Bind("Journal", "BackgroundOpacity", 0.8f, new ConfigDescription("Panel opacity.", new AcceptableValueRange<float>(0, 1)));
            ShowCancelled = config.Bind("Journal", "ShowCancelledQuests", true, "Include cancelled quests. Completed quests are always removed from the journal.");
            config.SettingChanged += SettingsChanged;
            Quest.TrackingChanged += QuestChanged;
            GUIManager.OnCustomGUIAvailable += Recreate;
        }

        static void SettingsChanged(object sender, SettingChangedEventArgs args)
        {
            LayoutDirty = true;
            if (Visible && args.ChangedSetting == ShowCancelled)
                SetSubscription(true);
        }

        static void QuestChanged(Quest quest)
        {
            if (Viewers.ContainsValue(quest.PlayerID)) DirtyPlayers.Add(quest.PlayerID);
        }

        public static void Tick()
        {
            if (LocalPlayer != Player.m_localPlayer)
            {
                if (Visible) 
                    SetSubscription(false);
                
                Visible = false;
                LocalPlayer = Player.m_localPlayer;
                Snapshot = "Loading quests...";
                
                if (Panel) 
                    Panel.SetActive(false);
            }
            
            if (LocalPlayer && ZNet.instance && ZRoutedRpc.instance != null &&
                !Console.IsVisible() && !(Chat.instance && Chat.instance.HasFocus()) &&
                !TextInput.IsVisible() && !Menu.IsVisible() && !InventoryGui.IsVisible() &&
                ToggleKey.Value.IsDown())
            {
                Visible = !Visible;
                if (Visible)
                {
                    Snapshot = "Loading quests...";
                    EnsurePanel();
                    LayoutDirty = true;
                }
                
                if (Panel) 
                    Panel.SetActive(Visible);
                
                SetSubscription(Visible);
            }

            if (ZNet.instance && ZNet.instance.IsServer() && QuestManager.WorldInstance != null)
            {
                foreach (var viewer in Viewers.ToArray())
                {
                    long playerID = ResolvePlayer(viewer.Key);
                    if (playerID == 0 || playerID != viewer.Value)
                    {
                        Viewers.Remove(viewer.Key);
                        continue;
                    }
                    if (!DirtyPlayers.Contains(playerID)) continue;
                    string text = BuildSnapshot(playerID);
                    if (viewer.Key == ZNet.GetUID()) 
                        ReceiveSnapshot(viewer.Key, text);
                    else 
                        ZRoutedRpc.instance.InvokeRoutedRPC(viewer.Key, SnapshotRPC, text);
                }
                DirtyPlayers.Clear();
            }
            
            if (!Visible) 
                return;
            
            EnsurePanel();
            
            if (!Panel) 
                return;
            
            if (LayoutDirty) 
                Refresh();
            
            Position();
        }

        static long ResolvePlayer(long sender)
        {
            if (sender == ZNet.GetUID())
                return Player.m_localPlayer ? Player.m_localPlayer.GetPlayerID() : 0;
            return ZNet.instance.GetPeer(sender)?.m_playerID ?? 0;
        }

        static void SetSubscription(bool enabled)
        {
            if (!ZNet.instance || ZRoutedRpc.instance == null) 
                return;

            if (ZNet.instance.IsServer()) 
                ReceiveVisibility(ZNet.GetUID(), enabled);
            else 
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), VisibilityRPC, enabled);
        }

        static void ReceiveVisibility(long sender, bool enabled)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) 
                return;
            
            if (!enabled) 
            { 
                Viewers.Remove(sender); 
                return; 
            }
            
            long id = ResolvePlayer(sender);
            if (id == 0) 
                return;
            
            Viewers[sender] = id;
            DirtyPlayers.Add(id);
        }

        static void ReceiveSnapshot(long sender, string text)
        {
            if (!Visible || !ZNet.instance || ZRoutedRpc.instance == null) return;
            long server = ZNet.instance.IsServer() ? ZNet.GetUID() : ZRoutedRpc.instance.GetServerPeerID();
            if (sender != server) return;
            Snapshot = text;
            LayoutDirty = true;
        }

        static string BuildSnapshot(long playerID)
        {
            // Prefix each entry with a status marker so filtering stays client-local.
            var entries = new List<string>();
            foreach (var quest in QuestManager.WorldInstance.GetPlayerQuests(playerID).OrderBy(q => q.Name))
            {
                if ((quest.Stage & QuestStage.Completed) != 0)
                    continue;
                bool cancelled = (quest.Stage & QuestStage.Cancelled) != 0;
                string status = cancelled ? "Cancelled" :
                    (quest.Stage & QuestStage.CriteriaMet) != 0 ? "Ready to turn in" :
                    (quest.Stage & QuestStage.Started) != 0 ? "Active" : "Discovered";
                string progress = cancelled ? "" : quest.JournalProgress;
                entries.Add((cancelled ? "F" : "A") + quest.Name.Replace("\u001e", "") + " — " + status +
                    (string.IsNullOrEmpty(progress) ? "" : "\n" + progress.Replace("\u001e", "")));
            }
            return string.Join("\u001e", entries);
        }

        static void EnsurePanel()
        {
            if (Panel || GUIManager.IsHeadless() || !GUIManager.CustomGUIFront) return;
            Panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
                Vector2.one, Vector2.one, Vector2.zero, Width.Value, 100, false);
            Panel.name = "RQF_QuestJournal";
            Panel.GetComponent<RectTransform>().pivot = Vector2.one;
            var obj = GUIManager.Instance.CreateText("", Panel.transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(12, -12), GUIManager.Instance.AveriaSerifBold, FontSize.Value,
                GUIManager.Instance.ValheimBeige, true, Color.black, Width.Value - 24, 100, false);
            Body = obj.GetComponent<Text>();
            Body.rectTransform.pivot = new Vector2(0, 1);
            Body.alignment = TextAnchor.UpperLeft;
            Body.supportRichText = false;
            Body.horizontalOverflow = HorizontalWrapMode.Wrap;
            Body.verticalOverflow = VerticalWrapMode.Overflow;
            foreach (var graphic in Panel.GetComponentsInChildren<Graphic>()) 
                graphic.raycastTarget = false;
            
            Panel.SetActive(Visible);
            LayoutDirty = true;
        }

        static void Refresh()
        {
            var text = new StringBuilder("Quest journal\n");
            int count = 0;
            if (Snapshot == "Loading quests...") 
                text.Append("\nLoading quests...");
            else
            {
                foreach (string entry in Snapshot.Split('\u001e'))
                {
                    if (entry.Length == 0 || (entry[0] == 'F' && !ShowCancelled.Value)) 
                        continue;
                    
                    text.Append("\n").Append(entry.Substring(1)).Append("\n");
                    count++;
                }
                if (count == 0) 
                    text.Append("\nNo quests to show.");
            }
            Body.fontSize = FontSize.Value;
            Body.rectTransform.sizeDelta = new Vector2(Width.Value - 24, 100);
            Body.text = text.ToString().TrimEnd();
            float height = Body.preferredHeight;
            Body.rectTransform.sizeDelta = new Vector2(Width.Value - 24, height);
            Panel.GetComponent<RectTransform>().sizeDelta = new Vector2(Width.Value, height + 24);
            foreach (var graphic in Panel.GetComponentsInChildren<Image>())
            {
                Color color = graphic.color;
                color.a = Opacity.Value;
                graphic.color = color;
            }
            LayoutDirty = false;
        }

        static void Position()
        {
            var rect = Panel.GetComponent<RectTransform>();
            var parent = (RectTransform)rect.parent;
            if (Minimap.instance && Minimap.instance.m_smallRoot)
            {
                var map = Minimap.instance.m_smallRoot.GetComponent<RectTransform>();
                map.GetWorldCorners(Corners);
                Vector3 bottomRight = parent.InverseTransformPoint(Corners[3]);
                rect.localPosition = new Vector3(bottomRight.x + OffsetX.Value, bottomRight.y - Gap.Value + OffsetY.Value, 0);
            }
            else 
                rect.anchoredPosition = new Vector2(-30 + OffsetX.Value, -240 - Gap.Value + OffsetY.Value);
        }

        static void Recreate()
        {
            if (Panel) 
                UnityEngine.Object.Destroy(Panel);
            
            Panel = null;
            Body = null;
            if (Visible) 
                EnsurePanel();
        }

        public static void Reset()
        {
            Viewers.Clear();
            DirtyPlayers.Clear();
            Visible = false;
            LocalPlayer = null;
            Snapshot = "Loading quests...";
            if (Panel) 
                Panel.SetActive(false);
        }

        public static void Destroy()
        {
            SetSubscription(false);
            ToggleKey.ConfigFile.SettingChanged -= SettingsChanged;
            Quest.TrackingChanged -= QuestChanged;
            GUIManager.OnCustomGUIAvailable -= Recreate;
            Reset();
            if (Panel) 
                UnityEngine.Object.Destroy(Panel);
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        static class RegisterRPCPatch
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                Reset();
                ZRoutedRpc.instance.Register<bool>(VisibilityRPC, ReceiveVisibility);
                ZRoutedRpc.instance.Register<string>(SnapshotRPC, ReceiveSnapshot);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        static class ShutdownPatch
        {
            [HarmonyPrefix]
            static void Prefix() => Reset();
        }
    }
}
