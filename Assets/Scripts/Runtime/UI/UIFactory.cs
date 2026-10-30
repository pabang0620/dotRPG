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
            // [I18N] Chinese needs hanzi the pixel font lacks: use the device's CJK font (pixel look is lost there).
            if (L.NeedsOsFont)
                font = Font.CreateDynamicFontFromOSFont(L.Lang == "zh-Hant"
                    ? new[] { "Noto Sans CJK TC", "Noto Sans TC", "Microsoft JhengHei", "PingFang TC", "Droid Sans Fallback", "Arial" }
                    : new[] { "Noto Sans CJK SC", "Noto Sans SC", "Microsoft YaHei", "PingFang SC", "Droid Sans Fallback", "Arial" }, 32);
            if (font != null) return font;
            font = Resources.Load<Font>("Fonts/UIFont");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic", // i18n-ignore font family name
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
        /// <summary>Reference resolution of the UI canvas scaler: a phone uses 1024x576 (everything about 1.25x bigger).</summary>
        public static Vector2 CanvasReference => TouchUi.Enabled ? new Vector2(1024, 576) : ReferenceResolution;
        /// <summary>Smallest font size of any UI text (22 with touch controls, 14 otherwise).</summary>
        public static int MinFontSize => TouchUi.Enabled ? 22 : 14;
        /// <summary>[UI 2026-10-30] Best-fit never shrinks a label below this (UiTheme.FontMin 16 x TextScale 0.88).</summary>
        public const int ReadableFloor = 14;
        const float UiPixelScale = 3f;
        /// <summary>Density-1 art resolution (pixels per tile). HD (density-2) UI frames are sized
        /// against this so their on-screen 9-slice borders match the old 16px frames.</summary>
        const float BaseArtPixels = 16f;

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
                // Keep the on-screen border thickness the same for every UI frame regardless of the
                // sprite's resolution. The HD (density-2) frames are drawn at twice the pixels AND
                // twice the border, so sizing them against the density-1 reference PPU (BaseArtPixels)
                // cancels the extra pixels out: a 32px/10px-border frame renders exactly like the old
                // 16px/5px-border frame. (The old code divided by the sprite's own PPU, which halved
                // the multiplier for HD frames and - unless borders were doubled - doubled their size.)
                img.pixelsPerUnitMultiplier = 100f / BaseArtPixels / UiPixelScale;
            }
            else
            {
                img.preserveAspect = true;
            }
            ApplySharp(img, sprite);
            return img;
        }

        /// <summary>
        /// Puts the UI sharp-bilinear material on an Image whose sprite is high-resolution (density-2)
        /// pixel art, so bilinear-filtered frames and icons stay crisp at non-integer canvas scales
        /// (1280x720 / 1920x1080). Point-filtered density-1 sprites and solid fills are left on the
        /// default UI material.
        /// </summary>
        public static void ApplySharp(Graphic g, Sprite sprite)
        {
            if (g == null || sprite == null || sprite.texture == null) return;
            if (sprite.texture.filterMode == FilterMode.Point) return; // point art is already crisp
            var mat = UiMaterials.Sharp;
            if (mat != null) g.material = mat;
        }

        /// <summary>
        /// An icon Image for a slot that starts empty and is filled with high-resolution pixel-art
        /// icons later (bag cells, gem sockets, shop rows, tooltip icons). The sharp material is set up
        /// front so the crisp shader is in place before the first sprite is assigned - the plain
        /// <see cref="Image"/> path only knows to add it when a sprite is present at creation time.
        /// </summary>
        public static Image SharpIcon(Transform parent, string name, Color color)
        {
            var img = Image(parent, name, null, color);
            var mat = UiMaterials.Sharp;
            if (mat != null) img.material = mat;
            return img;
        }

        public static Image Panel(Transform parent, string name, bool dark)
        {
            return Image(parent, name, Game.Art.Get(dark ? "ui_dark" : "ui_panel"), Color.white);
        }

        /// <summary>All UI text a notch smaller than the layout sizes (big letters crowded the panels).</summary>
        public const float TextScale = 0.88f;

        public static Text Text(Transform parent, string name, string content, int size, Color color, TextAnchor align, bool shadow = false)
        {
            var rt = Rect(parent, name);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = UIFont.Get();
            text.text = content;
            text.fontSize = Mathf.Max(MinFontSize, Mathf.RoundToInt(size * TextScale));
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
            UIWordWrap.Attach(text); // [UI] breaks at spaces only, never inside a Korean word; one-line boxes never wrap
            return text;
        }

        /// <summary>
        /// [UI] A label that must stay on one line whatever its box (grade names, numbers with units, column cells):
        /// it overflows sideways instead of wrapping. With <paramref name="shrink"/> it is shrunk to its box first
        /// (down to the readable floor <see cref="ReadableFloor"/>) so dense table cells do not run into their neighbours.
        /// </summary>
        public static Text OneLine(Text t, bool shrink = false)
        {
            if (shrink)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMaxSize = t.fontSize;
                t.resizeTextMinSize = Mathf.Min(t.fontSize, ReadableFloor); // [UI 2026-10-30] never below the readable floor (was 70%, at least 11)
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                return t;
            }
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        /// <summary>On-screen thickness of the ui_btn / ui_btngray rim (8 px border sliced at this canvas scale).</summary>
        public const float FrameRim = 12f;

        /// <summary>
        /// [UI] A label inside a bordered button: kept inside the rim (never drawn over the frame) and shrunk to fit
        /// (down to the readable floor <see cref="ReadableFloor"/>) instead of wrapping out above and below the button.
        /// </summary>
        public static Text FitInFrame(Text t, float sideInset = FrameRim - 2f, float vInset = 3f)
        {
            Stretch(t.rectTransform, sideInset, vInset, sideInset, vInset);
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = t.fontSize;
            t.resizeTextMinSize = Mathf.Min(t.fontSize, ReadableFloor); // [UI 2026-10-30] never below the readable floor (was 60%, at least 11)
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
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
