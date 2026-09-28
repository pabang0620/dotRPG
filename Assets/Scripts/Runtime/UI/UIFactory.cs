using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Font used by all UI text. Put a TTF/OTF at Resources/Fonts/UIFont to override (for release,
    /// ship a Korean-capable pixel font with an open licence, e.g. Galmuri or NeoDunggeunmo).
    /// Otherwise the OS Korean font is used.
    /// </summary>
    public static class UIFont
    {
        static Font font;

        public static Font Get()
        {
            if (font != null) return font;
            font = Resources.Load<Font>("Fonts/UIFont");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic",
                    "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial",
                }, 32);
            }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static class UIColors
    {
        public static readonly Color Ink = new Color32(62, 39, 26, 255);
        public static readonly Color InkSoft = new Color32(120, 84, 58, 255);
        public static readonly Color Cream = new Color32(246, 231, 200, 255);
        public static readonly Color Highlight = new Color32(255, 211, 74, 255);
        public static readonly Color Disabled = new Color32(150, 140, 130, 255);
        public static readonly Color Good = new Color32(120, 220, 120, 255);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);
    }

    /// <summary>Helpers for building uGUI hierarchies from code (keeps the scene free of UI prefabs for now).</summary>
    public static class UIFactory
    {
        /// <summary>Canvas reference resolution. Art is 16px; UI panels are drawn at 3x.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1280, 720);
        const float UiPixelScale = 3f;

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchors at a normalized point and sets pivot, position and size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero)
            {
                img.type = UnityEngine.UI.Image.Type.Sliced;
                // Show 9-slice borders at the UI pixel scale regardless of the sprite's PPU.
                img.pixelsPerUnitMultiplier = 100f / sprite.pixelsPerUnit / UiPixelScale;
            }
            else
            {
                img.preserveAspect = true;
            }
            return img;
        }

        public static Image Panel(Transform parent, string name, bool dark)
        {
            return Image(parent, name, Game.Art.Get(dark ? "ui_dark" : "ui_panel"), Color.white);
        }

        public static Text Text(Transform parent, string name, string content, int size, Color color, TextAnchor align, bool shadow = false)
        {
            var rt = Rect(parent, name);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = UIFont.Get();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = UIColors.Shadow;
                s.effectDistance = new Vector2(2f, -2f);
            }
            return text;
        }

        public static Image Overlay(Transform parent, string name, Color color)
        {
            var img = Image(parent, name, Game.Art.Get("ui_white"), color);
            img.preserveAspect = false;
            Stretch(img.rectTransform);
            return img;
        }
    }
}
