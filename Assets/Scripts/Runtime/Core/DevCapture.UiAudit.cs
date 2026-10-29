using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        [Serializable] sealed class UiAuditReport
        {
            public string language, saveDirectory;
            public int uiScale, width, height, level;
            public bool completed;
            public string remaining = "Populated online screens and party lobby; result screens; long-list scrolling; transaction modals. Touch menu is covered only with -touchUI. Individual tab content still requires visual review.";
            public string scope = "Offline layout audit. Online data, transactions, populated social lists, combat results and unlock progression are NOT verified. Developer content-unlock bypass is active.";
            public List<UiAuditFrame> frames = new List<UiAuditFrame>();
        }
        [Serializable] sealed class UiAuditFrame
        {
            public string id, expected, actual, status, tabSelection;
            public List<UiAuditText> candidates = new List<UiAuditText>();
        }
        [Serializable] sealed class UiAuditText
        {
            public string path, text, reason;
            public float preferredWidth, preferredHeight, width, height;
        }
        UiAuditReport uiAudit;

        static bool UiAuditRequested()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-uiAudit");
            return i >= 0 && i + 1 < args.Length && args[i + 1] == "1";
        }

        IEnumerator UiAuditRun()
        {
            if (!string.IsNullOrEmpty(UiAuditArgument("-uiAuditOnline"))) { yield return UiAuditOnlineRun(); yield break; }
            // Never let this developer fixture touch a real save or online character.
            string expected = Path.GetFullPath(Path.Combine(folder, "saves"));
            if (string.IsNullOrEmpty(SaveSystem.DirectoryOverride) ||
                !string.Equals(Path.GetFullPath(SaveSystem.DirectoryOverride), expected, StringComparison.OrdinalIgnoreCase) || OnlineSession.Playing)
            { Log("UI AUDIT ABORT: save isolation or offline requirement failed"); yield break; }
            ApplyRequestedResolution();
            yield return Wait(1.2f);
            uiAudit = new UiAuditReport { language = Game.Settings.Data.language, uiScale = Game.Settings.Data.uiScale,
                saveDirectory = expected, width = Screen.width, height = Screen.height };
            yield return UiAuditCapture("title", Game.UI.Title);
            foreach (var screen in new MenuScreen[] { Game.UI.Settings, Game.UI.KeyBind, Game.UI.LootFilter, Game.UI.Help,
                Game.UI.Controls, Game.UI.Credits, Game.UI.Policies, Game.UI.CharacterSelect, Game.UI.OnlineLogin,
                Game.UI.OnlineCreate, Game.UI.OnlineRename, Game.UI.Consent })
            {
                Game.UI.Push(screen);
                yield return UiAuditCapture("front_" + screen.name, screen);
                if (screen == Game.UI.KeyBind)
                    foreach (string tab in new[] { "CombatTab", "WindowTab" }) yield return UiAuditTab("keybind_" + tab, screen, tab);
                Game.UI.Pop();
            }
            bool chapter3 = Game.Config.chapter3Open;
            Game.Config.chapter3Open = true;
            try
            {
                Game.Flow.NewGame(CharacterClass.Mage);
                float deadline = Time.realtimeSinceStartup + 60f;
                while ((Game.Flow.IsTransitioning || Game.Session == null || Game.Player == null || !Game.IsPlaying) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (Game.Flow.IsTransitioning || Game.Session == null || Game.Player == null || !Game.IsPlaying)
                { Log("UI AUDIT ABORT: new game timed out"); yield break; }
                QuestAutoPilot.Stop("");
                Game.Session.Progression.SetFromServer(50, 0);
                Game.Session.Progression.Promote(Career.Arcanist);
                uiAudit.level = Game.Session.Progression.Level;
                if (uiAudit.level != 50) { Log("UI AUDIT ABORT: Lv50 fixture failed"); yield break; }
                var bag = Game.Session.Inventory;
                bag.Add(ConsumableDatabase.Gold, 99999);
                foreach (var id in new[] { "eq_staff_10_u", "eq_neck_1_c", "eq_ring_10_r", "eq_robe_1_u", "eq_skirt_1_c", "mat_bone", "mat_ore", "mat_essence" }) bag.Add(id, 3);
                bag.Add(ConsumableDatabase.HpPotion, 12);
                bag.Add(ConsumableDatabase.MpPotion, 8);
                Game.Session.Equipment.AutoEquip(CharacterClass.Mage);
                Game.Flow.SaveGame();
                yield return Wait(2f);
                QuestAutoPilot.Stop("");
                if (UiAuditArgument("-uiAuditFocus") == "careers")
                {
                    yield return UiAuditCareers();
                    uiAudit.completed = true;
                    yield break;
                }
                yield return UiAuditCapture("hud", null);
                yield return UiAuditHudStress();
                var sideMenu = Game.UI.GetComponentInChildren<SideMenuView>();
                Button menuToggle = null;
                if (sideMenu != null)
                    foreach (var button in sideMenu.GetComponentsInChildren<Button>(true))
                        if (button.transform.parent == sideMenu.transform) { menuToggle = button; break; }
                var touchControls = Game.UI.GetComponentInChildren<TouchControls>();
                if (touchControls != null)
                {
                    // The component lives on TouchStickZone; buttons live in its sibling TouchControls area.
                    var touchArea = UiAuditFind(Game.UI.Hud.transform, "TouchControls");
                    var button = touchArea == null ? null : UiAuditFind(touchArea, "MenuButton")?.GetComponent<Button>();
                    button?.onClick.Invoke();
                    yield return UiAuditCapture("touch_menu", null);
                    var layer = touchArea == null ? null : UiAuditFind(touchArea, "MenuLayer");
                    UiAuditEvidence(layer != null && layer.gameObject.activeInHierarchy, "touch menu open");
                    if (layer != null && layer.gameObject.activeInHierarchy) button?.onClick.Invoke();
                }
                else
                {
                    if (menuToggle != null) menuToggle.onClick.Invoke();
                    yield return UiAuditCapture("side_menu", null);
                    UiAuditEvidence(SideMenuView.IsOpen, "side menu open");
                    if (menuToggle != null && SideMenuView.IsOpen) menuToggle.onClick.Invoke();
                }
                if (UiAuditArgument("-uiAuditFocus") == "hud")
                {
                    uiAudit.completed = true;
                    yield break;
                }
                yield return UiAuditWindow("bag", Game.UI.Equipment);
                var weaponSlot = UiAuditFind(Game.UI.Equipment.transform, "Worn_Weapon");
                bool hovered = weaponSlot != null && ExecuteEvents.Execute(weaponSlot.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                yield return UiAuditCapture("bag_weapon_tooltip", Game.UI.Equipment);
                UiAuditEvidence(hovered, "tooltip pointer-enter dispatched; visual content review required");
                if (weaponSlot != null) ExecuteEvents.Execute(weaponSlot.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
                for (int i = 0; i < 6; i++)
                { Game.UI.Equipment.DevSelectTab(i); yield return UiAuditCapture("bag_tab_" + i, Game.UI.Equipment); }
                yield return UiAuditWindow("skills", Game.UI.Skills);
                foreach (string id in new[] { "Tab0", "Tab1", "Tab2" }) yield return UiAuditTab("skills_" + id, Game.UI.Skills, id);
                yield return UiAuditWindow("map", Game.UI.WorldMap);
                foreach (string id in new[] { "WorldSurface", "WorldUnderground" }) yield return UiAuditTab("map_" + id, Game.UI.WorldMap, id);
                yield return UiAuditWindow("quests", Game.UI.QuestLog);
                Game.Flow.CloseInventory(); Game.UI.Dungeon.Open(false);
                yield return UiAuditCapture("dungeons", Game.UI.Dungeon);
                foreach (string id in new[] { "TabWeekday", "TabNormal", "TabRaid" }) yield return UiAuditTab("dungeon_" + id, Game.UI.Dungeon, id);
                Game.Flow.CloseInventory(); Game.UI.MiniDungeon.Open();
                yield return UiAuditCapture("mini_dungeon", Game.UI.MiniDungeon);
                foreach (var screen in new MenuScreen[] { Game.UI.Shop, Game.UI.Storage, Game.UI.Enhance, Game.UI.Party, Game.UI.Cosmetics,
                    GachaScreen.Instance, CosmeticSynthScreen.Instance, ExchangeShopScreen.Instance,
                    AuctionScreen.Instance, SocialScreen.Instance, MailScreen.Instance, AchievementScreen.Instance,
                    LevelRewardScreen.Instance, DailyQuestScreen.Instance })
                    if (screen != null) yield return UiAuditWindow("window_" + screen.name, screen);
                foreach (var npc in Game.Config.npcs)
                {
                    WindowScreen service = npc.service == NpcService.Shop ? (WindowScreen)Game.UI.Shop
                        : npc.service == NpcService.Blacksmith ? Game.UI.Enhance
                        : npc.service == NpcService.Storage ? Game.UI.Storage : null;
                    if (service == null) continue;
                    Game.Flow.CloseInventory(); Game.UI.OpenService(npc);
                    yield return UiAuditCapture("service_" + npc.npcId, service);
                    service.SetKeeper("", "");
                }
                yield return UiAuditWindow("daily_tabs", DailyQuestScreen.Instance);
                foreach (string id in new[] { "TabDaily", "TabWeekly" }) yield return UiAuditTab("daily_" + id, DailyQuestScreen.Instance, id);
                yield return UiAuditWindow("wardrobe_tabs", Game.UI.Cosmetics);
                foreach (string id in new[] { "Tab_Aura", "Tab_WarriorSkin", "Tab_MageSkin", "Tab_RogueSkin" }) yield return UiAuditTab("wardrobe_" + id, Game.UI.Cosmetics, id);
                yield return UiAuditWindow("synth_tabs", CosmeticSynthScreen.Instance);
                foreach (string id in new[] { "TabSynth", "TabCollection" }) yield return UiAuditTab("synth_" + id, CosmeticSynthScreen.Instance, id);
                yield return UiAuditWindow("exchange_tabs", ExchangeShopScreen.Instance);
                foreach (string id in new[] { "TabGoods", "TabGear" }) yield return UiAuditTab("exchange_" + id, ExchangeShopScreen.Instance, id);
                yield return UiAuditWindow("enhance_tabs", Game.UI.Enhance);
                yield return UiAuditTab("enhance_inherit", Game.UI.Enhance, "InheritTab");
                yield return UiAuditWindow("achievement_tabs", AchievementScreen.Instance);
                foreach (string id in new[] { "growth", "combat", "dungeon", "enhance", "wealth", "cash", AchievementClient.RankCategory })
                    yield return UiAuditTab("achievement_" + id, AchievementScreen.Instance, "Tab_" + id);
                yield return UiAuditWindow("auction_tabs_offline", AuctionScreen.Instance);
                for (int i = 0; i < 4; i++) yield return UiAuditTab("auction_tab_" + i, AuctionScreen.Instance, "Tab" + i);
                foreach (PartyHub.Page page in Enum.GetValues(typeof(PartyHub.Page)))
                {
                    Game.Flow.CloseInventory(); PartyHub.Open(page);
                    yield return UiAuditCapture("party_" + page, PartyHub.WindowOf(page));
                    if (page == PartyHub.Page.MyParty) { uiAudit.frames[uiAudit.frames.Count - 1].status = "UNVERIFIED ONLINE: offline fallback to companions captured"; UiAuditWrite(); }
                }
                yield return UiAuditWindow("gacha_banners", GachaScreen.Instance);
                foreach (string banner in new[] { "skin", "aura", "weapon", "armor", "accessory", "sealed" })
                {
                    yield return UiAuditTab("gacha_" + banner, GachaScreen.Instance, "Card_" + banner);
                    yield return UiAuditTab("gacha_rates_" + banner, GachaScreen.Instance, "Rates");
                    var modal = UiAuditFind(GachaScreen.Instance.transform, "RateModal");
                    UiAuditEvidence(modal != null && modal.gameObject.activeInHierarchy, "rates modal active; server data unverified");
                    var close = modal == null ? null : UiAuditFind(modal, "Close");
                    if (close != null) close.GetComponent<Button>()?.onClick.Invoke();
                }
                Game.Flow.CloseInventory(); TodayScreen.Open(); yield return UiAuditCapture("today", TodayScreen.Instance);
                Game.Flow.CloseInventory(); GuildScreen.Open(); yield return UiAuditCapture("guild_offline", GuildScreen.Instance);
                Game.Flow.CloseInventory(); BossRushScreen.Open(); yield return UiAuditCapture("boss_offline", BossRushScreen.Instance);
                foreach (string tab in new[] { "TabWorld", "TabGuild" }) yield return UiAuditTab("boss_" + tab, BossRushScreen.Instance, tab);
                Game.Flow.CloseInventory(); RankingScreen.OpenBoard(RankingClient.WorldBoard); yield return UiAuditCapture("ranking_offline", RankingScreen.Instance);
                foreach (string tab in new[] { "Tab0", "Tab1", "Tab2" }) yield return UiAuditTab("ranking_" + tab, RankingScreen.Instance, tab);
                Game.Flow.CloseInventory();
                Game.Dialogue.Play("chief_intro", null, L.T("촌장"));
                yield return Wait(2f);
                yield return UiAuditCapture("dialogue", null);
                UiAuditEvidence(Game.Dialogue.IsOpen && Game.UI.DialogueBox.gameObject.activeInHierarchy, "dialogue visible");
                Game.Dialogue.Abort(); Game.State.Set(GameState.Playing);
                Game.Flow.CloseInventory(); Game.Flow.Pause(); yield return UiAuditCapture("pause", Game.UI.Pause);
                Game.Flow.Resume();
                yield return UiAuditResults();
                yield return UiAuditCareers();
                uiAudit.completed = true;
            }
            finally { Game.Config.chapter3Open = chapter3; UiAuditWrite(); }
        }

        IEnumerator UiAuditCareers()
        {
            // Reuse only fixture population, never the measurement loop, input or clock controls.
            foreach (var career in new[] { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop, Career.Assassin, Career.Ninja })
            {
                if (OnlineSession.Playing) { Log("UI career fixture aborted: online session"); yield break; }
                Game.Flow.CloseInventory();
                var cls = CareerCatalog.Base(career);
                Game.Flow.NewGame(cls);
                yield return null;
                float deadline = Time.realtimeSinceStartup + 60f;
                while ((Game.Flow.IsTransitioning || Game.Player == null || !Game.IsPlaying) && Time.realtimeSinceStartup < deadline) yield return null;
                if (Game.Flow.IsTransitioning || Game.Player == null || !Game.IsPlaying)
                {
                    uiAudit.frames.Add(new UiAuditFrame { id = "career_" + career, status = "FAILED: career fixture transition timed out" });
                    UiAuditWrite(); continue;
                }
                MeasureLevel(cls, career, 50, "u");
                Game.UI.Hud.ClearToasts();
                var progression = Game.Session.Progression;
                if (progression.Level != 50 || progression.Career != career)
                {
                    uiAudit.frames.Add(new UiAuditFrame { id = "career_" + career, status = "FAILED: level or career fixture mismatch" });
                    UiAuditWrite(); continue;
                }
                yield return UiAuditWindow("career_" + career + "_before_awakening", Game.UI.Skills);
                foreach (var tab in new[] { "Tab0", "Tab1", "Tab2" }) yield return UiAuditTab("career_" + career + "_" + tab, Game.UI.Skills, tab);
                progression.SyncAwakening(5);
                Game.UI.Skills.ShowAwakening();
                yield return UiAuditCapture("career_" + career + "_awakened", Game.UI.Skills);
                UiAuditEvidence(progression.Awakened, "awakening fixture state (quest completion flow not tested)");
                yield return UiAuditTab("career_" + career + "_awakened_slots", Game.UI.Skills, "Tab1");
                yield return UiAuditTab("career_" + career + "_awakened_specs", Game.UI.Skills, "Tab2");
            }
        }

        IEnumerator UiAuditWindow(string id, MenuScreen screen)
        {
            QuestAutoPilot.Stop("");
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Game.Flow.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            Game.Flow.CloseInventory();
            Game.Flow.OpenWindow(screen);
            yield return UiAuditCapture(id, screen);
            if (screen == Game.UI.Party)
            {
                foreach (var scroll in screen.GetComponentsInChildren<ScrollRect>())
                    if (scroll.content != null && scroll.viewport != null) scroll.verticalNormalizedPosition = 0f;
                yield return UiAuditCapture(id + "_descriptions_bottom", screen);
                foreach (var scroll in screen.GetComponentsInChildren<ScrollRect>()) scroll.verticalNormalizedPosition = 1f;
            }
        }

        static Transform UiAuditFind(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        void UiAuditEvidence(bool valid, string evidence)
        {
            var frame = uiAudit.frames[uiAudit.frames.Count - 1];
            frame.tabSelection = (valid ? "CONFIRMED: " : "FAILED: ") + evidence;
            if (!valid) frame.status = "FAILED: expected UI state absent";
            UiAuditWrite();
        }

        IEnumerator UiAuditTab(string id, MenuScreen screen, string childName)
        {
            Transform found = null;
            foreach (var t in screen.GetComponentsInChildren<Transform>(true)) if (t.name == childName) { found = t; break; }
            if (Game.UI.Top != screen || found == null || !found.gameObject.activeInHierarchy)
            {
                uiAudit.frames.Add(new UiAuditFrame { id = id, expected = screen.name + "/" + childName, actual = Game.UI.Top == null ? "none" : Game.UI.Top.name, status = "FAILED: tab unavailable" });
                UiAuditWrite(); yield break;
            }
            var button = found.GetComponent<Button>();
            if (button != null && button.interactable) button.onClick.Invoke();
            else if (button == null) ExecuteEvents.Execute(found.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            else { uiAudit.frames.Add(new UiAuditFrame { id = id, status = "UNVERIFIED: tab disabled" }); UiAuditWrite(); yield break; }
            yield return UiAuditCapture(id, screen);
            // These screens explicitly use UiTheme.Tab(on) when refreshing selection.
            bool themed = screen == Game.UI.Skills || screen == Game.UI.WorldMap || screen == Game.UI.Dungeon ||
                screen == DailyQuestScreen.Instance || screen == CosmeticSynthScreen.Instance ||
                screen == ExchangeShopScreen.Instance || screen == AchievementScreen.Instance || screen == AuctionScreen.Instance;
            var tabImage = found.GetComponent<Image>();
            var frame = uiAudit.frames[uiAudit.frames.Count - 1];
            frame.tabSelection = themed && tabImage != null
                ? (tabImage.sprite == UiTheme.Tab(true) ? "selected-sprite-confirmed" : "FAILED: selected sprite absent")
                : "UNVERIFIED: inspect captured tab content";
            if (tabImage != null && (screen == BossRushScreen.Instance || screen == RankingScreen.Instance))
                frame.tabSelection = tabImage.sprite == Game.Art.Get("ui_btn") ? "selected-sprite-confirmed" : "FAILED: selected sprite absent";
            if (tabImage != null && screen == Game.UI.KeyBind)
                frame.tabSelection = tabImage.color == UiTheme.SelectBlue ? "selected-color-confirmed" : "FAILED: selected color absent";
            if (screen == GachaScreen.Instance && childName.StartsWith("Card_", StringComparison.Ordinal))
            {
                var marker = UiAuditFind(found, "Frame");
                frame.tabSelection = marker != null && marker.GetComponent<Image>().color.a > .9f
                    ? "selected-banner-marker-confirmed" : "FAILED: selected banner marker absent";
            }
            if (frame.tabSelection.StartsWith("FAILED", StringComparison.Ordinal)) frame.status = "FAILED: tab selection absent";
            UiAuditWrite();
            if (screen == Game.UI.Skills && childName == "Tab2")
            {
                var choices = new List<Button>();
                foreach (var choice in screen.GetComponentsInChildren<Button>())
                    if (choice.name == "A" && choice.GetComponent<PointerRelay>() != null) choices.Add(choice);
                for (int i = 0; i < choices.Count; i++)
                {
                    if (i != 0 && i != choices.Count - 1) continue;
                    var choice = choices[i];
                    ExecuteEvents.Execute(choice.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    yield return UiAuditCapture(id + "_detail_" + i, screen);
                    ExecuteEvents.Execute(choice.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
                }
            }
            if (screen == Game.UI.Skills)
                foreach (var scroll in screen.GetComponentsInChildren<ScrollRect>())
                {
                    if (scroll.content == null || scroll.viewport == null || scroll.content.rect.height <= scroll.viewport.rect.height + 1f) continue;
                    float previous = scroll.verticalNormalizedPosition;
                    scroll.verticalNormalizedPosition = 0f;
                    yield return UiAuditCapture(id + "_bottom_" + scroll.name, screen);
                    UiAuditEvidence(scroll.verticalNormalizedPosition < .01f, "Scrollable skill content bottom reached");
                    scroll.verticalNormalizedPosition = previous;
                }
        }

        IEnumerator UiAuditCapture(string id, MenuScreen expected)
        {
            yield return Wait(.35f);
            Canvas.ForceUpdateCanvases();
            bool matched = Game.UI.Top == expected && (expected == null || expected.gameObject.activeInHierarchy);
            var frame = new UiAuditFrame { id = id, expected = expected == null ? "HUD" : expected.name,
                actual = Game.UI.Top == null ? "HUD" : Game.UI.Top.name,
                status = matched ? "CAPTURED: visual review required; online data unverified" : "FAILED: wrong screen" };
            foreach (var text in Game.UI.GetComponentsInChildren<Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)) continue;
                var rect = text.rectTransform.rect;
                var settings = text.GetGenerationSettings(rect.size);
                var gen = new TextGenerator();
                float ph = gen.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
                float pw = gen.GetPreferredWidth(text.text, settings) / text.pixelsPerUnit;
                string reason = "";
                if (!text.resizeTextForBestFit && text.verticalOverflow == VerticalWrapMode.Truncate && ph > rect.height + 2f) reason += "height-clipping-candidate;";
                if (!text.resizeTextForBestFit && text.horizontalOverflow == HorizontalWrapMode.Overflow && pw > rect.width + 2f) reason += "width-overflow-candidate;";
                var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners) { var p = RectTransformUtility.WorldToScreenPoint(null, corner); if (p.x < -1 || p.y < -1 || p.x > Screen.width + 1 || p.y > Screen.height + 1) { reason += "outside-screen-candidate;"; break; } }
                // Re-layout plain visible text so line indices cannot point into rich-text tags.
                string plain = text.supportRichText ? Regex.Replace(text.text, "<[^>]*>", "") : text.text;
                settings.richText = false;
                gen.Populate(plain, settings);
                for (int i = 0; gen.lines.Count > 1 && plain.Trim().Length > 1 && i < gen.lines.Count; i++)
                {
                    int start = Mathf.Clamp(gen.lines[i].startCharIdx, 0, plain.Length);
                    int end = i + 1 < gen.lines.Count ? Mathf.Clamp(gen.lines[i + 1].startCharIdx, start, plain.Length) : plain.Length;
                    string visible = plain.Substring(start, end - start).Trim();
                    if (visible.Length == 1 && visible[0] >= '\uac00' && visible[0] <= '\ud7a3') { reason += "single-hangul-line-candidate;"; break; }
                }
                if (reason.Length == 0) continue;
                string path = text.name; for (Transform p = text.transform.parent; p != null && p != Game.UI.transform; p = p.parent) path = p.name + "/" + path;
                frame.candidates.Add(new UiAuditText { path = path, text = text.text, reason = reason, preferredWidth = pw, preferredHeight = ph, width = rect.width, height = rect.height });
            }
            uiAudit.frames.Add(frame); UiAuditWrite();
            yield return PresentationShot((matched ? "audit_" : "audit_FAILED_") + id);
        }

        void UiAuditWrite() => File.WriteAllText(Path.Combine(folder, "ui-audit.json"), JsonUtility.ToJson(uiAudit, true));
    }
}
