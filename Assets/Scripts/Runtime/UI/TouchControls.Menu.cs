using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Top right: pause button and the menu button. The menu opens a sheet with every window the PC side menu has
    /// (same targets, same red dots) and the currency row the phone HUD leaves out.
    /// </summary>
    public sealed partial class TouchControls
    {
        const float TopButton = 64f;
        const int MenuColumns = 6;
        const float CellW = 112f, CellH = 92f, CellGap = 8f, CurrencyH = 48f;

        enum Dot { None, Mail, Reward, Daily }

        // The PC side menu's entries (SideMenuView.Create) with the same targets.
        static readonly (string icon, string label, Dot dot, Action open)[] MenuEntries =
        {
            ("menuicon_quest", L.T("할 일"), Dot.None, TodayScreen.Open), // [TODAY]
            ("menuicon_bag", L.T("가방"), Dot.None, () => Game.Flow.OpenWindow(null)),
            ("menuicon_skill", L.T("스킬"), Dot.None, () => Game.Flow.OpenWindow(Game.UI.Skills)),
            ("menuicon_map", L.T("지도"), Dot.None, () => Game.Flow.OpenWindow(Game.UI.WorldMap)),
            ("menuicon_quest", L.T("퀘스트"), Dot.None, () => Game.Flow.OpenWindow(Game.UI.QuestLog)),
            ("menuicon_dungeon", L.T("요일던전"), Dot.None, () => Game.UI.Dungeon.Open(false)),
            ("menuicon_raid", L.T("레이드"), Dot.None, () => Game.UI.Dungeon.Open(true)),
            ("menuicon_party", L.T("파티"), Dot.None, () => PartyHub.Open()),
            ("menuicon_finder", L.T("파티 찾기"), Dot.None, () => PartyHub.Open(PartyHub.Page.Finder)),
            ("menuicon_auction", L.T("경매장"), Dot.None, () => Game.Flow.OpenWindow(AuctionScreen.Instance)),
            ("menuicon_friend", L.T("친구"), Dot.None, () => Game.Flow.OpenWindow(SocialScreen.Instance)),
            ("menuicon_guild", L.T("길드"), Dot.None, GuildScreen.Open), // [GUILD]
            ("menuicon_worldboss", L.T("월드·길드보스"), Dot.None, () => BossRushScreen.Open()), // [BOSS]
            ("menuicon_ranking", L.T("랭킹"), Dot.None, () => RankingScreen.OpenBoard(RankingClient.WorldBoard)), // [RANK]
            ("menuicon_mail", L.T("우편"), Dot.Mail, () => Game.Flow.OpenWindow(MailScreen.Instance)),
            ("menuicon_achievement", L.T("업적"), Dot.None, () => Game.Flow.OpenWindow(AchievementScreen.Instance)),
            ("menuicon_levelreward", L.T("레벨 보상"), Dot.Reward, () => Game.Flow.OpenWindow(LevelRewardScreen.Instance)),
            ("menuicon_quest", L.T("일일 의뢰"), Dot.Daily, () => Game.Flow.OpenWindow(DailyQuestScreen.Instance)),
            ("menuicon_cashshop", L.T("캐시샵"), Dot.None, () => Game.Flow.OpenWindow(GachaScreen.Instance)),
            ("menuicon_cosmetics", L.T("옷장"), Dot.None, () => Game.Flow.OpenWindow(Game.UI.Cosmetics)),
        };

        RectTransform menuLayer;
        readonly List<(CanvasGroup group, ContentUnlocks.Feature feature)> menuLocks = new List<(CanvasGroup, ContentUnlocks.Feature)>();
        Image menuButtonDot;
        readonly List<(Dot kind, Image image)> entryDots = new List<(Dot, Image)>();
        readonly List<(string id, Text text)> currencyTexts = new List<(string, Text)>();

        /// <summary>A square button hanging from the top-right corner, left of the minimap (176 wide, 20 from the edge).</summary>
        Button TopButtonAt(string name, string label, float centerFromRight)
        {
            var img = UIFactory.Image(root, name, Game.Art.Get("ui_btn"), Color.white);
            img.preserveAspect = false;
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-centerFromRight, -(16f + TopButton * 0.5f)), new Vector2(TopButton, TopButton));
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            UiButton.Attach(button);
            var text = UIFactory.Text(img.transform, "Label", label, 22, Color.white, TextAnchor.MiddleCenter, true);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = text.fontSize;
            text.verticalOverflow = VerticalWrapMode.Truncate; // best fit only shrinks a wrapping label
            UIFactory.Stretch(text.rectTransform, UIFactory.FrameRim - 2f, 4f, UIFactory.FrameRim - 2f, 4f); // inside the rim
            return button;
        }

        void BuildTop()
        {
            float menuX = 20f + MinimapView.Diameter + 12f + TopButton * 0.5f;
            var menu = TopButtonAt("MenuButton", L.T("메뉴"), menuX);
            menu.onClick.AddListener(() => { if (Game.IsPlaying) SetMenu(menuLayer == null || !menuLayer.gameObject.activeSelf); });
            menuButtonDot = RedDot((RectTransform)menu.transform);
            var pause = TopButtonAt("PauseButton", "II", menuX + TopButton + 8f);
            pause.onClick.AddListener(() => { if (Game.IsPlaying) Game.Flow.Pause(); });
        }

        static Image RedDot(RectTransform parent)
        {
            var dot = UIFactory.Image(parent, "Dot", Game.Art.Get("ui_circle"), new Color32(232, 64, 64, 255));
            dot.raycastTarget = false;
            UIFactory.Place(dot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(4f, 4f), new Vector2(18f, 18f));
            dot.enabled = false;
            return dot;
        }

        void BuildMenu()
        {
            // Full-screen dim layer: a tap outside the panel closes the sheet.
            var dim = UIFactory.Overlay(root, "MenuLayer", new Color(0f, 0f, 0f, 0.4f));
            dim.raycastTarget = true;
            var close = dim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(() => SetMenu(false));
            menuLayer = dim.rectTransform;

            int rows = (MenuEntries.Length + MenuColumns - 1) / MenuColumns;
            var panel = UIFactory.Panel(menuLayer, "Panel", true);
            panel.raycastTarget = true; // a tap between the buttons must not close the sheet
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(MenuColumns * CellW + (MenuColumns + 1) * CellGap, CurrencyH + rows * CellH + (rows + 2) * CellGap));
            BuildCurrency(panel.rectTransform);
            for (int i = 0; i < MenuEntries.Length; i++)
            {
                var entry = MenuEntries[i];
                var img = UIFactory.Image(panel.transform, "Entry_" + entry.label, Game.Art.Get("ui_btn"), Color.white);
                img.preserveAspect = false;
                img.raycastTarget = true;
                UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(CellGap + (i % MenuColumns) * (CellW + CellGap), -(CellGap * 2f + CurrencyH + (i / MenuColumns) * (CellH + CellGap))), new Vector2(CellW, CellH));
                var icon = UIFactory.Image(img.transform, "Icon", Game.Art.Get(entry.icon), Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(42f, 42f));
                var text = UIFactory.Text(img.transform, "Label", entry.label, 22, Color.white, TextAnchor.MiddleCenter, true);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 12;
                text.resizeTextMaxSize = text.fontSize;
                text.verticalOverflow = VerticalWrapMode.Truncate; // best fit only shrinks a wrapping label
                UIFactory.Stretch(text.rectTransform, UIFactory.FrameRim - 2f, 6f, UIFactory.FrameRim - 2f, 52f);
                var button = img.gameObject.AddComponent<Button>();
                button.targetGraphic = img;
                UiButton.Attach(button);
                var open = entry.open;
                button.onClick.AddListener(() => OpenEntry(open));
                if (entry.dot != Dot.None) entryDots.Add((entry.dot, RedDot(img.rectTransform)));
                // [UNLOCK] Content the main quest has not opened reads dimmed (a tap still says which quest opens it).
                var feature = ContentUnlocks.FeatureOfMenu(entry.icon, entry.dot == Dot.Daily, entry.label);
                if (feature.HasValue) menuLocks.Add((img.gameObject.AddComponent<CanvasGroup>(), feature.Value));
            }
            menuLayer.gameObject.SetActive(false);
        }

        /// <summary>Gold and the other HUD currencies (the phone HUD hides that row): one strip at the top of the sheet.</summary>
        void BuildCurrency(RectTransform panel)
        {
            var strip = UIFactory.Place(UIFactory.Rect(panel, "Currency"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CellGap, -CellGap),
                new Vector2(MenuColumns * CellW + (MenuColumns - 1) * CellGap, CurrencyH));
            var plate = UIFactory.Image(strip, "Plate", Game.Art.Get("ui_white"), UiTheme.HudPlate);
            plate.preserveAspect = false;
            plate.raycastTarget = false;
            UIFactory.Stretch(plate.rectTransform);
            var shown = new List<(string id, string icon)> { (ConsumableDatabase.Gold, "icon_gold") };
            foreach (var def in Game.Config.items) shown.Add((def.id, def.iconKey));
            float cellW = strip.sizeDelta.x / Mathf.Max(1, shown.Count);
            for (int i = 0; i < shown.Count; i++)
            {
                var (id, iconKey) = shown[i];
                var icon = UIFactory.Image(strip, "Icon_" + id, Game.Art.Get(iconKey), Color.white);
                icon.raycastTarget = false;
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * cellW + 8f, 0f), new Vector2(28f, 28f));
                bool gold = id == ConsumableDatabase.Gold;
                var count = UIFactory.Text(strip, "Count_" + id, "0", 22, gold ? (Color)new Color32(255, 216, 74, 255) : UIColors.Cream, TextAnchor.MiddleLeft, true);
                count.raycastTarget = false;
                count.resizeTextForBestFit = true;
                count.resizeTextMinSize = 12;
                count.resizeTextMaxSize = count.fontSize;
                UIFactory.Place(count.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * cellW + 40f, 0f), new Vector2(cellW - 44f, CurrencyH - 8f));
                currencyTexts.Add((id, count));
            }
        }

        void RefreshCurrency()
        {
            if (Game.Session == null) return;
            var inventory = Game.Session.Inventory;
            foreach (var (id, text) in currencyTexts) text.text = HudView.MobileCountText(id, inventory.Count(id));
        }

        /// <summary>Red dots like the side menu's (mail waiting, level reward to take, daily job actionable).</summary>
        void UpdateMenuDots()
        {
            bool online = OnlineSession.Playing;
            bool mail = online && OnlineServices.Auction.UnclaimedMail > 0;
            bool reward = online && LevelRewardClient.AnyClaimable;
            bool daily = online && DailyQuestClient.AnyActionable;
            bool menuOpen = menuLayer != null && menuLayer.gameObject.activeSelf;
            if (menuButtonDot != null) menuButtonDot.enabled = (mail || reward || daily) && !menuOpen;
            foreach (var (kind, image) in entryDots)
                image.enabled = kind == Dot.Mail ? mail : kind == Dot.Reward ? reward : daily;
        }

        void SetMenu(bool open)
        {
            if (menuLayer == null) return;
            if (open)
            {
                menuLayer.SetAsLastSibling();
                RefreshCurrency();
                foreach (var (group, feature) in menuLocks) if (group != null) group.alpha = ContentUnlocks.IsOpen(feature) ? 1f : .4f;
            }
            menuLayer.gameObject.SetActive(open);
        }

        void CloseMenu() => SetMenu(false);

        /// <summary>Same targets as the PC side menu (SideMenuView).</summary>
        void OpenEntry(Action open)
        {
            SetMenu(false);
            if (!Game.IsPlaying) return;
            Game.Audio.PlaySfx("select");
            open();
        }
    }
}
