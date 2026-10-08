using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[TOUCH] Top right: pause button and the menu button with its window grid.</summary>
    public sealed partial class TouchControls
    {
        const float TopButton = 64f;

        // The 11 hotkey windows (InputReader.WindowActions) with the icon and label the side menu uses.
        static readonly (GameAction action, string icon, string label)[] MenuEntries =
        {
            (GameAction.Inventory, "menuicon_bag", "가방"),
            (GameAction.Map, "menuicon_map", "지도"),
            (GameAction.SkillWindow, "menuicon_skill", "스킬"),
            (GameAction.QuestWindow, "menuicon_quest", "퀘스트"),
            (GameAction.WeekdayDungeon, "menuicon_dungeon", "요일던전"),
            (GameAction.RaidWindow, "menuicon_raid", "레이드"),
            (GameAction.PartyWindow, "menuicon_party", "파티"),
            (GameAction.PartyFinder, "menuicon_finder", "파티 찾기"),
            (GameAction.Auction, "menuicon_auction", "경매장"),
            (GameAction.Friends, "menuicon_friend", "친구"),
            (GameAction.Cosmetics, "menuicon_cosmetics", "옷장"),
        };

        RectTransform menuLayer;

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
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(text.rectTransform, 4f, 4f, 4f, 4f);
            return button;
        }

        void BuildTop()
        {
            float menuX = 20f + MinimapView.Diameter + 12f + TopButton * 0.5f;
            var menu = TopButtonAt("MenuButton", "메뉴", menuX);
            menu.onClick.AddListener(() => { if (Game.IsPlaying) SetMenu(menuLayer == null || !menuLayer.gameObject.activeSelf); });
            var pause = TopButtonAt("PauseButton", "II", menuX + TopButton + 8f);
            pause.onClick.AddListener(() => { if (Game.IsPlaying) Game.Flow.Pause(); });
        }

        void BuildMenu()
        {
            // Full-screen dim layer: a tap outside the panel closes the grid.
            var dim = UIFactory.Overlay(root, "MenuLayer", new Color(0f, 0f, 0f, 0.4f));
            dim.raycastTarget = true;
            var close = dim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(() => SetMenu(false));
            menuLayer = dim.rectTransform;

            const int columns = 4;
            const float cellW = 112f, cellH = 98f, gap = 8f;
            int rows = (MenuEntries.Length + columns - 1) / columns;
            var panel = UIFactory.Panel(menuLayer, "Panel", true);
            panel.raycastTarget = true; // a tap between the buttons must not close the grid
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(columns * cellW + (columns + 1) * gap, rows * cellH + (rows + 1) * gap));
            for (int i = 0; i < MenuEntries.Length; i++)
            {
                var entry = MenuEntries[i];
                var img = UIFactory.Image(panel.transform, "Entry_" + entry.label, Game.Art.Get("ui_btn"), Color.white);
                img.preserveAspect = false;
                img.raycastTarget = true;
                UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(gap + (i % columns) * (cellW + gap), -(gap + (i / columns) * (cellH + gap))), new Vector2(cellW, cellH));
                var icon = UIFactory.Image(img.transform, "Icon", Game.Art.Get(entry.icon), Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(44f, 44f));
                var text = UIFactory.Text(img.transform, "Label", entry.label, 22, Color.white, TextAnchor.LowerCenter, true);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 12;
                text.resizeTextMaxSize = text.fontSize;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Stretch(text.rectTransform, 4f, 6f, 4f, 0f);
                var button = img.gameObject.AddComponent<Button>();
                button.targetGraphic = img;
                UiButton.Attach(button);
                var action = entry.action;
                button.onClick.AddListener(() => OpenEntry(action));
            }
            menuLayer.gameObject.SetActive(false);
        }

        void SetMenu(bool open)
        {
            if (menuLayer == null) return;
            if (open) menuLayer.SetAsLastSibling();
            menuLayer.gameObject.SetActive(open);
        }

        void CloseMenu() => SetMenu(false);

        /// <summary>Same targets as the keyboard shortcuts (GameFlow.ProcessWindowShortcuts) and the side menu.</summary>
        void OpenEntry(GameAction action)
        {
            SetMenu(false);
            if (!Game.IsPlaying) return;
            if (action == GameAction.WeekdayDungeon || action == GameAction.RaidWindow)
            {
                Game.UI.Dungeon.Open(action == GameAction.RaidWindow);
                return;
            }
            var window = Game.Flow.WindowFor(action);
            if (window != null) Game.Flow.OpenWindow(window);
        }
    }
}
