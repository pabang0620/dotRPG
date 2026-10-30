using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI 2026-10-30] A small "?" badge that holds an explanation instead of a line of small print on the screen
    /// (Docs/UI_AUDIT.md, bonghwa principle "long explanations become a ? icon"). Hover (mouse) or tap (touch) shows the
    /// text in a dark tooltip box next to the badge; leaving or tapping again hides it. The text can change at any time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiHelpTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public const float BadgeSize = 32f;

        string text = "";
        RectTransform box;
        Text label;
        bool open;

        /// <summary>A "?" badge at <paramref name="pos"/> (anchor = pivot) in <paramref name="parent"/> explaining <paramref name="help"/>.</summary>
        public static UiHelpTip Create(Transform parent, string name, Vector2 anchor, Vector2 pos, string help)
        {
            var bg = UIFactory.Image(parent, name, Game.Art.Get("ui_btngray"), Color.white);
            bg.preserveAspect = false;
            bg.raycastTarget = true;
            UIFactory.Place(bg.rectTransform, anchor, anchor, pos, new Vector2(BadgeSize, BadgeSize));
            var q = UIFactory.Text(bg.transform, "Q", "?", 20, UiTheme.AccentLight, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(q.rectTransform);
            q.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tip = bg.gameObject.AddComponent<UiHelpTip>();
            tip.text = help ?? "";
            return tip;
        }

        public void SetText(string help)
        {
            text = help ?? "";
            if (open) Show();
        }

        public void OnPointerEnter(PointerEventData e) => Show();
        public void OnPointerExit(PointerEventData e) => Hide();
        public void OnPointerClick(PointerEventData e) { if (open && TouchUi.Enabled) Hide(); else Show(); }

        void OnDisable() => Hide();

        void Show()
        {
            if (string.IsNullOrEmpty(text)) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var root = canvas.rootCanvas.transform;
            if (box == null)
            {
                var img = UIFactory.Image(root, "HelpTip", Game.Art.Get("ui_white"), UiTheme.Overlay);
                img.preserveAspect = false;
                img.raycastTarget = false;
                box = img.rectTransform;
                label = UIFactory.Text(box, "Text", "", 18, UiTheme.TextPrimary, TextAnchor.UpperLeft);
                label.lineSpacing = 1.15f;
                UIFactory.Stretch(label.rectTransform, UiTheme.SpaceM, UiTheme.SpaceS, UiTheme.SpaceM, UiTheme.SpaceS);
            }
            box.gameObject.SetActive(true);
            box.SetAsLastSibling();
            label.text = text;
            float w = UiTheme.TooltipWidth + 80f;
            var gen = label.cachedTextGeneratorForLayout;
            var settings = label.GetGenerationSettings(new Vector2(w - 2f * UiTheme.SpaceM, 0f));
            float h = gen.GetPreferredHeight(text, settings) / label.pixelsPerUnit + 2f * UiTheme.SpaceS + 4f;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(w, h);
            // Below-right of the badge, flipped to stay on the canvas.
            var rootRect = (RectTransform)root;
            Vector3 world = transform.position;
            Vector2 local = rootRect.InverseTransformPoint(world);
            var half = rootRect.rect.size * 0.5f;
            float x = local.x + BadgeSize * 0.5f + 6f, y = local.y - BadgeSize * 0.5f;
            if (x + w > half.x - 8f) x = local.x - BadgeSize * 0.5f - 6f - w;
            if (y - h < -half.y + 8f) y = local.y + BadgeSize * 0.5f + h;
            box.pivot = new Vector2(0f, 1f);
            box.anchoredPosition = new Vector2(x, y);
            open = true;
        }

        void Hide()
        {
            open = false;
            if (box != null) box.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (box != null) Destroy(box.gameObject);
        }
    }
}
