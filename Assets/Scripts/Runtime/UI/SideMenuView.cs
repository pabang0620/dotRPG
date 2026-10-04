using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Menu button just right of the HP / MP bars (top-left HUD). Clicking it folds out a three-column grid of
    /// icons under it that open the bag, skills, map, quest log, dungeon, party and the other windows.
    /// </summary>
    public class SideMenuView : MonoBehaviour
    {
        // Grid cell: icon button centred at the top, its label centred under it inside the same width (labels like
        // "파티 찾기" / "외형 상점" used to spill into the next cell at 72 px).
        const float Size = 50f, StepX = 88f, StepY = 86f, Pad = 10f;
        const int Columns = 3;
        /// <summary>Right of the status bars (StatusBarsView: 18 + 76 + 296) with a small gap.</summary>
        const float Left = 404f, Top = -16f;

        /// <summary>Top-left of the i-th icon button: centred in its cell (three columns; each label sits under its icon).</summary>
        static Vector2 SlotPos(int i) => new Vector2(Pad + (i % Columns) * StepX + (StepX - Size) * 0.5f, -Pad - (i / Columns) * StepY);

        Image gridBg;

        RectTransform column;
        bool open;
        /// <summary>[F5] The fold-out grid reaches the chat box: the chat hides while it is open.</summary>
        public static bool IsOpen { get; private set; }
        readonly List<RectTransform> entries = new List<RectTransform>();
        float anim;

        public static SideMenuView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "SideMenu"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Left, Top), new Vector2(Size, Size));
            var view = root.gameObject.AddComponent<SideMenuView>();
            view.MakeIcon(root, "menuicon_menu", "메뉴", view.Toggle, false);

            view.column = UIFactory.Place(UIFactory.Rect(root, "Column"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-(StepX - Size) * 0.5f - Pad, -Size - 12f), new Vector2(Size, 600f));
            // A plate behind the grid so the cells read as one tidy panel.
            view.gridBg = UIFactory.Image(view.column, "Plate", Game.Art.Get("ui_white"), UiTheme.HudPlate);
            view.gridBg.preserveAspect = false;
            view.gridBg.raycastTarget = true; // clicks between icons don't fall through to the world
            view.Add("menuicon_bag", "가방", () => Game.Flow.OpenWindow(null));
            view.Add("menuicon_skill", "스킬", () => Game.Flow.OpenWindow(Game.UI.Skills));
            view.Add("menuicon_map", "지도", () => Game.Flow.OpenWindow(Game.UI.WorldMap));
            view.Add("menuicon_quest", "퀘스트", () => Game.Flow.OpenWindow(Game.UI.QuestLog));
            view.Add("menuicon_dungeon", "요일던전", () => Game.UI.Dungeon.Open(false)); // [DUNGEON] 던전 선택 window
            view.Add("menuicon_raid", "레이드", () => Game.UI.Dungeon.Open(true)); // [DUNGEON] its raid tab
            // [PARTY] Online with a server party: the lobby; otherwise the mercenary party window.
            // [AI] Online the party window is one place: people, the AI who fill free seats, invites and the AI roster button.
            view.Add("menuicon_party", "파티", () => Game.Flow.OpenWindow(OnlineSession.Playing && PartyLobbyScreen.Instance != null ? (WindowScreen)PartyLobbyScreen.Instance : Game.UI.Party));
            view.Add("menuicon_finder", "파티 찾기", () => Game.Flow.OpenWindow(PartyFinderScreen.Instance)); view.Add("menuicon_auction", "경매장", () => Game.Flow.OpenWindow(AuctionScreen.Instance)); // [ONLINE]
            view.Add("menuicon_friend", "친구", () => Game.Flow.OpenWindow(SocialScreen.Instance)); // [F5]
            view.Add("menuicon_cosmetics", "외형 상점", () => Game.Flow.OpenWindow(Game.UI.Cosmetics));
            int rows = (view.entries.Count + Columns - 1) / Columns;
            UIFactory.Place(view.gridBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Pad * 2f + Columns * StepX, Pad * 2f + rows * StepY - 8f));
            view.column.gameObject.SetActive(false);
            return view;
        }

        void Add(string icon, string label, Action onClick)
        {
            var rt = MakeIcon(column, icon, label, () => { Toggle(); onClick(); }, true);
            UIFactory.Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), SlotPos(entries.Count), new Vector2(Size, Size));
            entries.Add(rt);
        }

        RectTransform MakeIcon(Transform parent, string icon, string label, Action onClick, bool showLabel)
        {
            var bg = UIFactory.Image(parent, "Btn_" + label, Game.Art.Get("ui_btn"), Color.white);
            bg.preserveAspect = false;
            bg.raycastTarget = true;
            var rt = bg.rectTransform;
            if (parent is RectTransform p && p.GetComponent<SideMenuView>() != null) UIFactory.Stretch(rt);
            var img = UIFactory.Image(rt, "Icon", Game.Art.Get(icon), Color.white);
            // Inside the button's rim (ui_btn slices 12 px of frame at this size): centred, not pushed up past the edge.
            UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
            if (showLabel)
            {
                // Label sits just under the button.
                // [UI] One line under the icon in the pixel font (two-word labels like "파티 찾기" stay on one line).
                var t = UIFactory.Text(rt, "Label", label, 14, Color.white, TextAnchor.UpperCenter, true);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.resizeTextForBestFit = true; // long labels shrink to fit the cell instead of spilling over
                t.resizeTextMinSize = 10;
                t.resizeTextMaxSize = 14;
                UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(StepX - 6f, 20f));
            }
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            UiButton.Attach(button, false); // [UI] tint + hover sound; the column animates the scale itself
            button.onClick.AddListener(() =>
            {
                if (!Game.IsPlaying) return;
                Game.Audio.PlaySfx("select");
                onClick();
            });
            return rt;
        }

        void Toggle()
        {
            open = !open;
            IsOpen = open;
            column.gameObject.SetActive(open);
            anim = 0f;
        }

        void Update()
        {
            if (!open) return;
            // Close when the game leaves normal play (a window opened, pause, dialogue).
            if (!Game.IsPlaying) { open = IsOpen = false; column.gameObject.SetActive(false); return; }
            // Small fold-out animation.
            anim = Mathf.Min(1f, anim + Time.unscaledDeltaTime * 6f);
            for (int i = 0; i < entries.Count; i++)
            {
                float t = Mathf.Clamp01(anim * entries.Count - i * 0.6f);
                entries[i].anchoredPosition = Vector2.Lerp(SlotPos(0), SlotPos(i), t);
                entries[i].localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, t);
            }
        }
    }
}
