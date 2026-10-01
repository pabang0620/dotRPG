using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// The UI design system in one place (Docs/UI_AUDIT.md, checklist E2): colour tokens, the font size
    /// scale, spacing, standard button sizes and state colours, panel / tooltip / HUD plate styles.
    /// Sizes are in canvas units at the 1280x720 reference resolution. New and touched UI code should use
    /// these tokens instead of colour / size literals. Rarity colours stay in <see cref="EquipmentDatabase.RarityColor"/>.
    /// </summary>
    public static class UiTheme
    {
        // ---------- Colours ----------
        /// <summary>Full-screen window background.</summary>
        public static readonly Color Background = new Color32(34, 52, 76, 255);
        /// <summary>Window header strip.</summary>
        public static readonly Color Header = new Color32(22, 31, 46, 255);
        /// <summary>Content panel inside a window.</summary>
        public static readonly Color Panel = new Color32(24, 36, 54, 235);
        /// <summary>Alternate / nested panel (rows, side columns).</summary>
        public static readonly Color PanelAlt = new Color32(32, 47, 70, 235);
        /// <summary>Deep inset (map frames, bar tracks).</summary>
        public static readonly Color PanelDeep = new Color32(18, 26, 40, 255);
        /// <summary>Translucent plate under HUD text that sits on the world (toasts, labels, hints).</summary>
        public static readonly Color HudPlate = new Color(0.05f, 0.06f, 0.09f, 0.62f);
        /// <summary>Thin separator / frame line.</summary>
        public static readonly Color Line = new Color32(70, 92, 124, 255);

        public static readonly Color Accent = new Color32(255, 211, 74, 255);
        public static readonly Color AccentBlue = new Color32(120, 200, 255, 255);

        public static readonly Color TextPrimary = new Color32(246, 240, 228, 255);
        public static readonly Color TextSecondary = new Color32(184, 196, 216, 255);
        public static readonly Color TextMuted = new Color32(140, 150, 168, 255);
        public static readonly Color TextDisabled = new Color32(120, 126, 138, 255);

        public static readonly Color Good = new Color32(143, 226, 143, 255);
        public static readonly Color Bad = new Color32(255, 122, 110, 255);
        public static readonly Color Warn = new Color32(255, 159, 67, 255);

        /// <summary>Rich-text hex of the same tokens.</summary>
        public const string HexAccent = "#ffd34a", HexGood = "#8fe28f", HexBad = "#ff7a6e", HexWarn = "#ff9f43",
            HexSecondary = "#b8c4d8", HexMuted = "#8c96a8", HexKey = "#ffe066";

        // ---------- Font size scale ----------
        public const int FontDisplay = 52;   // banners (boss intro, CLEAR)
        public const int FontTitle = 40;     // window titles
        public const int FontHeading = 26;   // panel headings
        public const int FontSubheading = 22;
        public const int FontBody = 20;
        public const int FontCaption = 17;   // secondary info, key hints
        public const int FontNumber = 22;    // counters, prices
        /// <summary>Smallest size any player-facing text may use (720p reference).</summary>
        public const int FontMin = 16;

        /// <summary>Clamps a size to <see cref="FontMin"/>.</summary>
        public static int Size(int size) => Mathf.Max(FontMin, size);

        // ---------- Spacing ----------
        public const float SpaceXS = 4f, SpaceS = 8f, SpaceM = 12f, SpaceL = 20f, SpaceXL = 30f;
        /// <summary>Window content margin (left/right/bottom) and header height.</summary>
        public const float WindowMargin = 30f, HeaderHeight = 76f;
        /// <summary>Height reserved at the bottom of a window for the key-hint footer.</summary>
        public const float FooterHeight = 28f;

        // ---------- HUD layout (720p reference) ----------
        // Left column: status bars / currency / side menu / party frames end at x = 356 (PartyFramesView).
        // Right column: minimap / room map / quest tracker start at x = 1280 - 380 = 900.
        /// <summary>Width of centre-screen banners (boss intro, awakening, CLEAR) so they stay between the columns.</summary>
        public const float HudBannerWidth = 520f;
        /// <summary>Right edge of the currency bar (must stay left of the boss bar at 640 - 232 = 408).</summary>
        public const float HudCurrencyMaxRight = 400f;

        // ---------- Buttons ----------
        public const float ButtonHeight = 56f, ButtonHeightSmall = 44f, ButtonWidth = 250f;
        public const int ButtonFont = 24, ButtonFontSmall = 20;
        public const float HoverScale = 1.04f, PressScale = 0.95f;

        /// <summary>Tint block for sprite buttons: normal is slightly dimmed so hover visibly brightens.</summary>
        public static ColorBlock ButtonColors()
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = new Color(0.9f, 0.9f, 0.92f, 1f);
            c.highlightedColor = Color.white;
            c.selectedColor = Color.white;
            c.pressedColor = new Color(0.7f, 0.7f, 0.74f, 1f);
            c.disabledColor = new Color(0.42f, 0.43f, 0.48f, 0.9f);
            c.colorMultiplier = 1f;
            c.fadeDuration = 0.08f;
            return c;
        }

        // ---------- Tooltip ----------
        public static readonly Color TooltipBg = new Color32(16, 22, 34, 245);
        public static readonly Color TooltipBorder = new Color32(110, 134, 170, 255);
        public const float TooltipPadding = 14f, TooltipWidth = 320f;

        /// <summary>Adds a translucent plate behind a HUD element (stretched under it, drawn first).</summary>
        public static Image AddPlate(RectTransform target, float padX = 10f, float padY = 4f)
        {
            var plate = UIFactory.Image(target, "Plate", Game.Art.Get("ui_white"), HudPlate);
            plate.preserveAspect = false;
            UIFactory.Stretch(plate.rectTransform, -padX, -padY, -padX, -padY);
            plate.transform.SetAsFirstSibling();
            return plate;
        }
    }
}
