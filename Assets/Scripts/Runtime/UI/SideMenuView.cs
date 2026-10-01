using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Menu button on the left edge of the HUD. Clicking it folds out a two-column grid of icons that
    /// open the bag, skills, map, quest log, mini-dungeon and raid windows.
    /// </summary>
    public class SideMenuView : MonoBehaviour
    {
        const float Size = 50f, StepX = 66f, StepY = 80f;
        const int Columns = 2;

        /// <summary>Grid position of the i-th icon (two columns; each label sits under its icon).</summary>
        static Vector2 SlotPos(int i) => new Vector2((i % Columns) * StepX, -(i / Columns) * StepY);

        RectTransform column;
        bool open;
        readonly List<RectTransform> entries = new List<RectTransform>();
        float anim;

        public static SideMenuView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "SideMenu"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -152f), new Vector2(Size, Size));
            var view = root.gameObject.AddComponent<SideMenuView>();
            view.MakeIcon(root, "menuicon_menu", "메뉴", view.Toggle, false);

            view.column = UIFactory.Place(UIFactory.Rect(root, "Column"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -Size - 14f), new Vector2(Size, 600f));
            view.Add("menuicon_bag", "가방", () => Game.Flow.OpenWindow(null));
            view.Add("menuicon_skill", "스킬", () => Game.Flow.OpenWindow(Game.UI.Skills));
            view.Add("menuicon_map", "지도", () => Game.Flow.OpenWindow(Game.UI.WorldMap));
            view.Add("menuicon_quest", "퀘스트", () => Game.Flow.OpenWindow(Game.UI.QuestLog));
            view.Add("menuicon_dungeon", "요일던전", () => Game.UI.Dungeon.Open(false)); // [DUNGEON] 던전 선택 window
            view.Add("menuicon_raid", "레이드", () => Game.UI.Dungeon.Open(true)); // [DUNGEON] its raid tab
            view.Add("menuicon_party", "파티", () => Game.Flow.OpenWindow(Game.UI.Party)); // [PARTY]
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
            UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, showLabel ? 6f : 0f), new Vector2(40f, 40f));
            if (showLabel)
            {
                // Label sits just under the button.
                var t = UIFactory.Text(rt, "Label", label, UiTheme.FontMin, Color.white, TextAnchor.UpperCenter, true);
                UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -3f), new Vector2(StepX, 22f));
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
            column.gameObject.SetActive(open);
            anim = 0f;
        }

        void Update()
        {
            if (!open) return;
            // Close when the game leaves normal play (a window opened, pause, dialogue).
            if (!Game.IsPlaying) { open = false; column.gameObject.SetActive(false); return; }
            // Small fold-out animation.
            anim = Mathf.Min(1f, anim + Time.unscaledDeltaTime * 6f);
            for (int i = 0; i < entries.Count; i++)
            {
                float t = Mathf.Clamp01(anim * entries.Count - i * 0.6f);
                entries[i].anchoredPosition = SlotPos(i) * t;
                entries[i].localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, t);
            }
        }
    }
}
