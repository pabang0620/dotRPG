using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Full-screen window in the bag's style: blue background, header with back button + title.
    /// Esc / Cancel / the bag key close it and return to the game.
    /// </summary>
    public abstract class WindowScreen : MenuScreen
    {
        protected RectTransform content;

        protected static T CreateWindow<T>(Transform canvas, string name, string title, string icon) where T : WindowScreen
        {
            var root = CreateRoot(canvas, name, false);
            var w = root.gameObject.AddComponent<T>();
            // [UI] Colours / sizes from UiTheme; the back button names its key (Esc) so keyboard and pad
            // players see how to close every window, and a thin accent line separates header and content.
            var bg = UIFactory.Overlay(root, "Bg", UiTheme.Background);
            bg.raycastTarget = true;
            // [UI] Header and content are laid out together on a fixed 1280x720 rect (pinned to the top centre) that
            // scales uniformly to fit the window (UI scale 1.15 / 1.3, small screens); it never grows. The background
            // above stays full-screen.
            var layout = UIFactory.Place(UIFactory.Rect(SafeAreaFitter.Wrap(root), "Layout"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, UIFactory.ReferenceResolution);
            layout.gameObject.AddComponent<FitToParent>().design = UIFactory.ReferenceResolution;
            var header = Img(layout, "Header", "ui_header", Color.white); // [UI] wooden header strip (9-slice)
            UIFactory.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, UiTheme.HeaderHeight));
            var headerLine = Img(layout, "HeaderLine", "ui_white", new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.35f));
            UIFactory.Place(headerLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -UiTheme.HeaderHeight), new Vector2(4000f, 2f));
            var back = Button(layout, "Back", "◀", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(96f, 52f), w.Close, 24);
            var backText = back.GetComponentInChildren<Text>();
            backText.text = "◀ <size=17>ESC</size>";
            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIFactory.Image(layout, "Icon", Game.Art.Get(icon), Color.white);
                UIFactory.Place(ic.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(134f, -14f), new Vector2(48f, 48f));
            }
            var t = UIFactory.Text(layout, "Title", title, UiTheme.FontTitle, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(192f, -12f), new Vector2(560f, 52f));
            w.keeperLine = UIFactory.Text(layout, "Keeper", "", 19, new Color32(246, 231, 200, 255), TextAnchor.MiddleRight, true);
            UIFactory.Place(w.keeperLine.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -12f), new Vector2(760f, 52f));
            // Every window's content is the same 1220x594 rect (1280x720 minus margins and header).
            var area = UIFactory.Stretch(UIFactory.Rect(layout, "ContentArea"), 30f, 30f, 30f, 96f);
            w.content = UIFactory.Place(UIFactory.Rect(area, "Content"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, ContentSize);
            return w;
        }

        /// <summary>The design size of every window's content.</summary>
        public static readonly Vector2 ContentSize = new Vector2(1220f, 594f);

        Text keeperLine;

        /// <summary>Shows who runs this window and what they say (empty = nothing).</summary>
        public void SetKeeper(string npcName, string line)
        {
            if (keeperLine == null) return;
            keeperLine.text = string.IsNullOrEmpty(npcName) ? "" : $"<color=#ffe066>{npcName}</color>  “{line}”";
        }

        public virtual void Close() => Game.Flow.CloseInventory();

        protected static Image Img(Transform parent, string name, string sprite, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            return img;
        }

        protected static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            // [UI] Dark content panels get the framed dark panel (9-slice); light or translucent fills stay flat.
            float lum = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
            bool framed = color.a > 0.8f && lum < 0.3f && size.x >= 80f && size.y >= 60f;
            var img = framed ? Img(parent, name, "ui_dark", new Color(1f, 1f, 1f, color.a)) : Img(parent, name, "ui_white", color);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            return img;
        }

        protected static Button Button(Transform parent, string name, string label, string sprite, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Action onClick, int font)
        {
            var img = Img(parent, name, sprite, Color.white);
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => onClick());
            UiButton.Attach(button); // [UI] hover / press / disabled feedback + hover sound
            var text = UIFactory.Text(img.transform, "Text", label, UiTheme.Size(font), Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(text.rectTransform);
            return button;
        }

        protected static Text Label(Transform parent, string name, string text, int size, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = UIFactory.Text(parent, name, text, size, Color.white, align, true);
            t.lineSpacing = 1.2f;
            UIFactory.Place(t.rectTransform, anchor, pivot, pos, box);
            return t;
        }

        public override void Show()
        {
            base.Show();
            Refresh();
        }

        protected abstract void Refresh();

        /// <summary>False while a dialog is drawn over this window (it then takes no input at all).</summary>
        protected bool IsTop => Game.UI == null || Game.UI.Top == this;

        /// <summary>True when this frame's keys belong to this window (on top, not the frame it was shown or the state changed).</summary>
        protected bool TakesInput => IsTop && Time.frameCount != shownFrame && !Game.State.ChangedThisFrame;

        /// <summary>[UX] Windows that switch pages with Tab: Tab is not the bag key there (I / gamepad Back still close).</summary>
        protected virtual bool UsesTabKey => false;

        /// <summary>This frame's Inventory press is the Tab key of a window that switches pages with it (GameFlow skips it too).</summary>
        public bool TabSwallowsInventory => UsesTabKey && UnityEngine.Input.GetKeyDown(KeyCode.Tab);

        /// <summary>[UX] Windows whose labels carry key tags ("[2]" / "[L3]") redraw when the player switches keyboard / gamepad.</summary>
        protected virtual bool HasKeyTags => false;
        bool keyTagsPad;

        protected virtual void Update()
        {
            PadNavigate();
            bool pad = Game.Input != null && Game.Input.UsingGamepad;
            if (pad != keyTagsPad)
            {
                keyTagsPad = pad;
                if (HasKeyTags && gameObject.activeInHierarchy) Refresh();
            }
            if (!TakesInput) return;
            var input = Game.Input;
            if ((input.InventoryPressed && !TabSwallowsInventory) || input.CancelPressed || (input.MapPressed && this is WorldMapScreen))
            {
                Game.Audio.PlaySfx("cancel");
                Close();
            }
        }

        // ---------------- [UI] gamepad: move between this window's buttons ----------------

        /// <summary>
        /// Windows without their own cursor turn this on: with a gamepad the stick / d-pad moves between the window's
        /// buttons (Unity UI navigation) and A presses the one under the yellow frame.
        /// </summary>
        protected virtual bool PadNavigation => false;
        Image padCursor;

        protected void PadNavigate()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            bool mine = current != null && current.transform.IsChildOf(transform);
            bool on = PadNavigation && IsTop && Game.Input != null && Game.Input.UsingGamepad && gameObject.activeInHierarchy;
            if (!on)
            {
                // Nothing of this window stays selected under a dialog or with mouse and keyboard: A would press it.
                if (mine) es.SetSelectedGameObject(null);
                if (padCursor != null) padCursor.enabled = false;
                return;
            }
            var selectable = mine && current.activeInHierarchy ? current.GetComponent<Selectable>() : null;
            if (selectable == null || !selectable.IsInteractable())
            {
                selectable = null;
                foreach (var s in GetComponentsInChildren<Selectable>(false))
                    if (s.IsInteractable() && s.navigation.mode != Navigation.Mode.None) { selectable = s; break; }
                if (selectable == null) { if (padCursor != null) padCursor.enabled = false; return; }
                es.SetSelectedGameObject(selectable.gameObject);
            }
            var target = (RectTransform)selectable.transform;
            KeepInView(target);
            if (padCursor == null)
            {
                padCursor = UIFactory.Image(transform, "PadCursor", Game.Art.Get("ui_frame"), new Color32(255, 211, 74, 255));
                padCursor.preserveAspect = false;
                padCursor.raycastTarget = false;
                padCursor.rectTransform.anchorMin = padCursor.rectTransform.anchorMax = padCursor.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            var root = (RectTransform)transform;
            var r = target.rect;
            Vector2 min = root.InverseTransformPoint(target.TransformPoint(r.min));
            Vector2 max = root.InverseTransformPoint(target.TransformPoint(r.max));
            padCursor.rectTransform.anchoredPosition = (min + max) * 0.5f;
            padCursor.rectTransform.sizeDelta = max - min + new Vector2(8f, 8f);
            padCursor.rectTransform.SetAsLastSibling();
            padCursor.enabled = true;
        }

        /// <summary>Scrolls a list so the selected row is inside its viewport.</summary>
        static void KeepInView(RectTransform target)
        {
            var scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null || !target.IsChildOf(scroll.content)) return;
            var view = scroll.viewport;
            Vector3[] c = new Vector3[4];
            target.GetWorldCorners(c);
            float top = view.InverseTransformPoint(c[1]).y, bottom = view.InverseTransformPoint(c[0]).y;
            float viewTop = view.rect.yMax, viewBottom = view.rect.yMin;
            float shift = top > viewTop ? top - viewTop : bottom < viewBottom ? bottom - viewBottom : 0f;
            if (Mathf.Abs(shift) > 0.5f) scroll.content.anchoredPosition -= new Vector2(0f, shift);
        }
    }
}
