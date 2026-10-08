using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution (density 2, 32px-per-tile scale) window frames, HUD pieces and icons.
    /// Every window / HUD / icon sprite key goes through <see cref="DrawUiFamily"/>
    /// (ui_*, icon_*, eqicon_*, maticon_*, menuicon_*, gem_*, node_*, heart_*), so the whole UI art
    /// set is authored here in one place. Each canvas sets <c>Density = 2</c>, which makes
    /// <see cref="SpriteLibrary"/> import it at twice the pixels per unit - same on-screen size as the
    /// old 16px art, twice the detail. 9-slice borders are given in the (doubled) HD pixel space and
    /// <see cref="UIFactory"/> keeps the on-screen border thickness constant across densities.
    /// </summary>
    public static partial class ProceduralArt
    {
        /// <summary>Marks a canvas as high-resolution so the sprite library imports it at density-2 PPU.</summary>
        static PixelCanvas Hd2(PixelCanvas c) { c.Density = 2; return c; }

        /// <summary>
        /// Every window / HUD / icon sprite key goes through here (ui_*, icon_*, eqicon_*, maticon_*,
        /// menuicon_*, gem_*, node_*, heart_*), so the UI art can be swapped in one place.
        /// </summary>
        static PixelCanvas DrawUiFamily(string key, string[] parts)
        {
            switch (parts[0])
            {
                case "ui": return DrawUiHd(parts[1]);
                case "icon": return DrawIconHd(key.Substring(5));
                case "eqicon": return DrawEquipIconHd(parts[1], parts.Length > 2 ? int.Parse(parts[2]) : 0);
                case "maticon": return DrawMaterialIconHd(parts[1]);
                case "menuicon": return DrawMenuIconHd(parts[1]);
                case "gem": return DrawGemHd(key.Substring(4));
                case "node": return DrawNodeGlyphHd(parts[1]);
                case "heart": return DrawHeartHd(parts[1]);
            }
            return null;
        }

        // ===================================================================================
        //  Window frames, slots, buttons, tooltips, minimap and hearts (ui_*).
        //  All 9-slice pieces are 32x32 with 10px borders (the old 16x16 / 5px, doubled).
        // ===================================================================================

        static PixelCanvas DrawUiHd(string kind)
        {
            switch (kind)
            {
                case "panel":
                {
                    // Cream parchment with a warm brown frame (9-slice). Bevelled: dark outer edge,
                    // lit inner lip, soft parchment centre.
                    var c = new PixelCanvas(32, 32);
                    var fill = PixelCanvas.Hex("#f6e7c8");
                    var fillLo = PixelCanvas.Hex("#ecd9b2");
                    var frame = PixelCanvas.Hex("#6b3f22");
                    var frameDark = PixelCanvas.Hex("#4c2c17");
                    var frameLight = PixelCanvas.Hex("#b87b45");
                    c.Rect(0, 0, 32, 32, frameDark);
                    c.Rect(1, 1, 30, 30, frame);
                    c.Rect(3, 3, 26, 26, frameLight);       // lit bevel
                    c.HLine(3, 28, 3, PixelCanvas.Hex("#d29a5e"));
                    c.Rect(5, 5, 22, 22, fill);
                    // Faint corner shading in the centre for a soft parchment look.
                    c.HLine(5, 26, 26, fillLo);
                    c.VLine(26, 5, 26, fillLo);
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 10;
                    return Hd2(c);
                }
                case "dark":
                {
                    var c = new PixelCanvas(32, 32);
                    var fill = PixelCanvas.Hex("#1d1a26", 225);
                    var fillLo = PixelCanvas.Hex("#161320", 225);
                    var frame = PixelCanvas.Hex("#f6e7c8", 235);
                    var frameDark = PixelCanvas.Hex("#b79a6a", 235);
                    c.Rect(0, 0, 32, 32, frameDark);
                    c.Rect(1, 1, 30, 30, frame);
                    for (int y = 4; y < 28; y++)
                        for (int x = 4; x < 28; x++)
                            c.Pixels[y * 32 + x] = (x + y) % 2 == 0 && y > 15 ? fillLo : fill;
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 10;
                    return Hd2(c);
                }
                case "select":
                {
                    // Soft golden selection glow (9-slice).
                    var c = new PixelCanvas(16, 16);
                    c.Rect(0, 0, 16, 16, PixelCanvas.Hex("#ffd34a", 110));
                    c.Rect(2, 2, 12, 12, PixelCanvas.Hex("#ffe89a", 70));
                    c.Rect(4, 4, 8, 8, PixelCanvas.Hex("#fff4c8", 40));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 4;
                    return Hd2(c);
                }
                case "white":
                {
                    var c = new PixelCanvas(8, 8);
                    c.Rect(0, 0, 8, 8, White);
                    return Hd2(c);
                }
                case "circle":
                {
                    // Solid disc used as the minimap's round mask.
                    var c = new PixelCanvas(128, 128);
                    c.Circle(63.5f, 63.5f, 64f, White);
                    return Hd2(c);
                }
                case "ring":
                {
                    // Minimap frame: brown wood ring with a cream inner edge; centre transparent.
                    var c = new PixelCanvas(128, 128);
                    for (int y = 0; y < 128; y++)
                        for (int x = 0; x < 128; x++)
                        {
                            float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f));
                            if (d > 64f || d < 56f) continue;
                            Color32 col = d > 62f ? Outline
                                : d > 59f ? PixelCanvas.Hex("#8e5a32")
                                : d > 57.5f ? PixelCanvas.Hex("#b87b45")
                                : PixelCanvas.Hex("#f6e7c8");
                            c.Set(x, y, col);
                        }
                    return Hd2(c);
                }
                case "dot":
                {
                    var c = new PixelCanvas(14, 14);
                    c.Circle(6.5f, 6.5f, 7f, Outline);
                    c.Circle(6.5f, 6.5f, 5f, White);
                    c.Circle(5f, 5f, 1.6f, White);
                    return Hd2(c);
                }
                case "slot":
                {
                    // Bag cell: dark steel square with a lit top edge (9-slice).
                    var c = new PixelCanvas(32, 32);
                    c.Rect(0, 0, 32, 32, PixelCanvas.Hex("#15181f"));
                    c.Rect(2, 2, 28, 28, PixelCanvas.Hex("#3b404b"));
                    c.Rect(4, 6, 24, 22, PixelCanvas.Hex("#454b58"));
                    c.HLine(2, 29, 2, PixelCanvas.Hex("#5c6373"));
                    c.HLine(2, 29, 3, PixelCanvas.Hex("#4c515d"));
                    c.HLine(2, 29, 29, PixelCanvas.Hex("#2a2e37"));
                    c.VLine(2, 3, 28, PixelCanvas.Hex("#4c515d"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 6;
                    return Hd2(c);
                }
                case "slotblue":
                {
                    // Equipped slot: blue-tinted cell like the character panel's slots.
                    var c = new PixelCanvas(32, 32);
                    c.Rect(0, 0, 32, 32, PixelCanvas.Hex("#0f1826"));
                    c.Rect(2, 2, 28, 28, PixelCanvas.Hex("#27456b"));
                    for (int y = 4; y < 28; y++) c.HLine(4, 27, y, PixelCanvas.Shade(PixelCanvas.Hex("#335a88"), 1.12f - (y - 4) * 0.018f));
                    c.HLine(2, 29, 2, PixelCanvas.Hex("#5f8ec2"));
                    c.HLine(2, 29, 3, PixelCanvas.Hex("#4a70a0"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 6;
                    return Hd2(c);
                }
                case "frame":
                {
                    // Hollow frame, tinted per rarity / used as the cursor (9-slice).
                    var c = new PixelCanvas(32, 32);
                    c.Rect(0, 0, 32, 32, White);
                    c.Rect(4, 4, 24, 24, PixelCanvas.Clear);
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 6;
                    return Hd2(c);
                }
                case "corner":
                {
                    // Green corner triangle = "better than what you wear".
                    var c = new PixelCanvas(16, 16);
                    for (int y = 0; y < 16; y++) c.HLine(0, 15 - y, y, PixelCanvas.Hex("#5ee04a"));
                    for (int y = 0; y < 16; y++) c.Set(15 - y, y, PixelCanvas.Hex("#bdffab"));
                    return Hd2(c);
                }
                case "btn":
                {
                    var c = new PixelCanvas(32, 32);
                    c.Rect(0, 0, 32, 32, PixelCanvas.Hex("#0d1420"));
                    for (int y = 2; y < 30; y++) c.HLine(2, 29, y, Color32.Lerp(PixelCanvas.Hex("#3f7fc7"), PixelCanvas.Hex("#1d3f6e"), (y - 2) / 27f));
                    c.HLine(2, 29, 2, PixelCanvas.Hex("#7fb9ff"));
                    c.HLine(2, 29, 3, PixelCanvas.Hex("#5c9be0"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 6;
                    return Hd2(c);
                }
                case "btngray":
                {
                    var c = new PixelCanvas(32, 32);
                    c.Rect(0, 0, 32, 32, PixelCanvas.Hex("#0d1016"));
                    for (int y = 2; y < 30; y++) c.HLine(2, 29, y, Color32.Lerp(PixelCanvas.Hex("#4b5260"), PixelCanvas.Hex("#2b3039"), (y - 2) / 27f));
                    c.HLine(2, 29, 2, PixelCanvas.Hex("#7a8394"));
                    c.HLine(2, 29, 3, PixelCanvas.Hex("#5e6675"));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 6;
                    return Hd2(c);
                }
                case "tooltip":
                {
                    var c = new PixelCanvas(32, 32);
                    c.Rect(0, 0, 32, 32, PixelCanvas.Hex("#c9b27a"));
                    c.Rect(1, 1, 30, 30, PixelCanvas.Hex("#8a7548"));
                    c.Rect(2, 2, 28, 28, PixelCanvas.Hex("#10141c", 245));
                    c.HLine(4, 27, 4, PixelCanvas.Hex("#2a3242", 245));
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 6;
                    return Hd2(c);
                }
            }
            return null;
        }
    }
}
