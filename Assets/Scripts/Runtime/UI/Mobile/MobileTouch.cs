using System;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Helpers for the phone layout (only called when <see cref="TouchUi.Enabled"/>): taller tap areas for small
    /// window buttons, remembered HUD fold states, small HUD buttons.
    /// </summary>
    public static class MobileTouch
    {
        /// <summary>Tap height inside a 1280x720 window layout: windows fit the 1024x576 phone canvas at 0.8, 56 * 0.8 = 44.8.</summary>
        public const float WindowTapHeight = 56f;
        /// <summary>Tap size of a HUD button on the 1024x576 phone canvas.</summary>
        public const float HudTap = 48f;

        /// <summary>Which edge stays put when a button grows: Up keeps the bottom edge, Down the top edge, Centre the middle.</summary>
        public enum Grow { Up, Down, Centre }

        /// <summary>Makes <paramref name="rt"/> at least <paramref name="minHeight"/> tall, growing the way <paramref name="mode"/> says.</summary>
        public static void GrowHeight(RectTransform rt, float minHeight, Grow mode)
        {
            if (rt == null) return;
            var size = rt.sizeDelta;
            float delta = minHeight - size.y;
            if (delta <= 0.01f) return;
            // The pivot decides which edge the anchored position holds: move it so the chosen edge stays where it was.
            float keep = mode == Grow.Up ? 0f : mode == Grow.Down ? 1f : 0.5f;
            var pos = rt.anchoredPosition;
            pos.y += delta * (rt.pivot.y - keep);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size.x, minHeight);
        }

        /// <summary><see cref="GrowHeight"/> for every descendant of <paramref name="root"/> named <paramref name="name"/>.</summary>
        public static void GrowNamed(Transform root, string name, float minHeight, Grow mode)
        {
            if (root == null) return;
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) GrowHeight(rt, minHeight, mode);
        }

        /// <summary>A remembered on / off HUD state (per device). Storage errors fall back to <paramref name="fallback"/>.</summary>
        public static bool GetFlag(string key, bool fallback)
        {
            try { return PlayerPrefs.GetInt("mobile_hud_" + key, fallback ? 1 : 0) != 0; }
            catch (Exception) { return fallback; }
        }

        public static void SetFlag(string key, bool value)
        {
            try
            {
                PlayerPrefs.SetInt("mobile_hud_" + key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception) { /* the state just is not remembered */ }
        }

        /// <summary>A square-ish HUD button with a fitted label (dark plate, gold frame like the other HUD buttons).</summary>
        public static Button HudButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, out Text text)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get("ui_btn"), Color.white);
            img.preserveAspect = false;
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            UiButton.Attach(button);
            text = UIFactory.Text(img.transform, "Label", label, 20, Color.white, TextAnchor.MiddleCenter, true);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = text.fontSize;
            text.verticalOverflow = VerticalWrapMode.Truncate; // best fit only shrinks a wrapping label
            UIFactory.Stretch(text.rectTransform, UIFactory.FrameRim - 4f, 2f, UIFactory.FrameRim - 4f, 2f);
            return button;
        }

        /// <summary>A small red count badge on the top-right corner of <paramref name="parent"/> (hidden until set).</summary>
        public static Text Badge(RectTransform parent)
        {
            var dot = UIFactory.Image(parent, "Badge", Game.Art.Get("ui_circle"), new Color32(232, 64, 64, 255));
            dot.raycastTarget = false;
            UIFactory.Place(dot.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-4f, -4f), new Vector2(26f, 26f));
            var t = UIFactory.Text(dot.transform, "Count", "", 16, Color.white, TextAnchor.MiddleCenter, false);
            t.raycastTarget = false;
            t.resizeTextForBestFit = true; // "99+" in a 26 px dot
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = t.fontSize;
            UIFactory.Stretch(t.rectTransform, 2f, 2f, 2f, 2f);
            dot.gameObject.SetActive(false);
            return t;
        }

        /// <summary>Shows <paramref name="count"/> on a badge from <see cref="Badge"/> (0 hides it, 0 &lt; n: the number, 99+).</summary>
        public static void SetBadge(Text badge, int count)
        {
            if (badge == null) return;
            bool show = count > 0;
            var go = badge.transform.parent.gameObject;
            if (go.activeSelf != show) go.SetActive(show);
            if (show) badge.text = count > 99 ? "99+" : count.ToString();
        }
    }
}
