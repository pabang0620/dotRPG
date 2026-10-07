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

        // ===================================================================================
        //  Resource / item icons (icon_*). 24x24 for resources, 32x32 for items.
        // ===================================================================================

        static PixelCanvas DrawIconHd(string kind)
        {
            // [MERGE] The protection ticket has no 32px art yet: use the 16px one (same on-screen size).
            if (kind == "ticket") return DrawItemIcon16(kind);
            var item = DrawItemIcon32(kind);
            if (item != null) return item;
            var c = new PixelCanvas(24, 24);
            switch (kind)
            {
                case "wood":
                {
                    var barkA = Bark; var barkB = PixelCanvas.Hex("#a06a38");
                    c.Rect(2, 8, 18, 10, barkA);
                    c.HLine(2, 19, 8, WoodLight);
                    c.HLine(2, 19, 9, PixelCanvas.Hex("#b0824c"));
                    c.HLine(2, 19, 16, BarkDark);
                    c.HLine(2, 19, 17, PixelCanvas.Hex("#4a2c16"));
                    // End grain rings on the right.
                    c.Ellipse(20, 13, 3.4f, 5f, WoodLight);
                    c.Ellipse(20, 13, 2.2f, 3.4f, Wood);
                    c.Ellipse(20, 13, 1f, 1.8f, WoodDark);
                    break;
                }
                case "stone":
                {
                    c.Ellipse(12, 14, 10, 8, StoneDark);
                    c.PaintEllipse(11, 13, 8.5f, 6.5f, Stone);
                    c.PaintEllipse(9, 10, 4.5f, 3.2f, StoneLight);
                    c.Paint(16, 17, StoneDark); c.Paint(17, 15, StoneDark);
                    c.Set(7, 8, White);
                    break;
                }
                case "carrot":
                {
                    var body = Carrot; var bodyLo = PixelCanvas.Shade(Carrot, 0.8f);
                    // Tapered root.
                    for (int y = 6; y <= 20; y++)
                    {
                        int half = Mathf.RoundToInt(Mathf.Lerp(4.5f, 0.5f, (y - 6) / 14f));
                        c.HLine(9 - half, 9 + half, y, body);
                    }
                    c.Line(6, 8, 12, 18, bodyLo);
                    c.HLine(5, 12, 9, CarrotLight);
                    c.HLine(6, 11, 12, CarrotLight);
                    // Notches.
                    c.Set(11, 11, bodyLo); c.Set(8, 14, bodyLo); c.Set(11, 16, bodyLo);
                    // Leafy top.
                    c.Rect(8, 2, 2, 5, Leaf);
                    c.Line(9, 6, 4, 1, Leaf); c.Line(9, 6, 14, 1, Leaf); c.Line(9, 6, 9, 0, LeafLight);
                    c.Set(4, 1, LeafLight); c.Set(14, 1, LeafLight); c.Set(9, 0, LeafShine);
                    break;
                }
                default:
                    c.Rect(6, 6, 12, 12, Red);
                    break;
            }
            c.Outline(Outline);
            return Hd2(c);
        }

        /// <summary>32x32 item icons: gold coins, potion flasks, return scroll, storage chest, anvil.</summary>
        static PixelCanvas DrawItemIcon32(string kind)
        {
            switch (kind)
            {
                case "gold":
                {
                    var c = new PixelCanvas(32, 32);
                    var rim = PixelCanvas.Hex("#b07a1c");
                    var rimDark = PixelCanvas.Hex("#8a5e12");
                    var face = PixelCanvas.Hex("#f2c14e");
                    var shine = PixelCanvas.Hex("#ffe89a");
                    // A second coin peeking out behind the first.
                    c.Ellipse(21f, 13f, 9.2f, 9.2f, rimDark);
                    c.Ellipse(21f, 13f, 7.4f, 7.4f, face);
                    c.Ellipse(14f, 19f, 11.6f, 11.6f, rim);
                    c.Ellipse(14f, 19f, 9.6f, 9.6f, face);
                    c.PaintEllipse(10f, 15f, 5.2f, 4.2f, shine);
                    c.Rect(11, 16, 6, 6, rimDark);            // square hole
                    c.Rect(13, 18, 2, 2, PixelCanvas.Clear);
                    c.Set(8, 12, White); c.Set(9, 12, White);
                    c.Outline(Outline);
                    return Hd2(c.WithPivot(16f, 3f));
                }
                case "potion_hp":
                case "potion_mp":
                {
                    bool hp = kind == "potion_hp";
                    var c = new PixelCanvas(32, 32);
                    var glass = PixelCanvas.Hex("#d8eef6");
                    var glassLo = PixelCanvas.Hex("#aacada");
                    var liquid = hp ? PixelCanvas.Hex("#e0413a") : PixelCanvas.Hex("#3f7ee8");
                    var liquidLight = hp ? PixelCanvas.Hex("#ff8a72") : PixelCanvas.Hex("#86b8ff");
                    var liquidDark = hp ? PixelCanvas.Hex("#a82a2a") : PixelCanvas.Hex("#2a53b0");
                    c.Circle(16f, 21f, 10.4f, glass);                 // round flask
                    c.Rect(12, 6, 8, 8, glass);                       // neck
                    for (int y = 16; y < 32; y++)                     // liquid below the shoulder
                        for (int x = 0; x < 32; x++)
                            if (c.IsOpaque(x, y)) c.Set(x, y, y >= 26 ? liquidDark : liquid);
                    c.PaintEllipse(12f, 20f, 3.6f, 2.4f, liquidLight);
                    c.PaintEllipse(20f, 25f, 2f, 3f, liquidDark);
                    c.Rect(12, 2, 8, 4, PixelCanvas.Hex("#9a6a3c"));  // cork
                    c.HLine(12, 19, 2, PixelCanvas.Hex("#c49058"));
                    c.VLine(10, 16, 22, glassLo);                     // glass rim shade
                    c.Set(10, 18, White); c.Set(10, 20, White); c.VLine(9, 18, 22, White); // shine
                    c.Outline(Outline);
                    return Hd2(c.WithPivot(16f, 2f));
                }
                case "scroll":
                {
                    var c = new PixelCanvas(32, 32);
                    var paper = PixelCanvas.Hex("#f3e2b8");
                    var paperDark = PixelCanvas.Hex("#d7bf8a");
                    var roll = PixelCanvas.Hex("#c9a46a");
                    var rollDark = PixelCanvas.Hex("#a8844e");
                    c.Rect(6, 8, 20, 16, paper);
                    c.HLine(6, 25, 23, paperDark);
                    c.HLine(6, 25, 9, PixelCanvas.Hex("#fff2cf"));
                    c.Rect(2, 6, 6, 20, roll); c.VLine(2, 8, 22, rollDark); c.VLine(3, 8, 22, PixelCanvas.Hex("#e0c088"));
                    c.Rect(24, 6, 6, 20, roll); c.VLine(29, 8, 22, rollDark); c.VLine(28, 8, 22, PixelCanvas.Hex("#e0c088"));
                    // Glowing blue rune and a red wax seal.
                    var rune = PixelCanvas.Hex("#4f8fe8");
                    c.Circle(16f, 15f, 5.2f, PixelCanvas.Hex("#9fd0ff"));
                    c.VLine(16, 10, 20, rune); c.VLine(15, 10, 20, rune); c.HLine(12, 20, 15, rune);
                    c.Rect(14, 22, 4, 4, PixelCanvas.Hex("#c8323a")); c.Set(15, 22, PixelCanvas.Hex("#e8646a"));
                    c.Outline(Outline);
                    return Hd2(c.WithPivot(16f, 2f));
                }
                case "chest":
                {
                    var c = new PixelCanvas(32, 32);
                    c.Rect(2, 12, 28, 18, Wood);
                    c.Rect(2, 6, 28, 8, WoodLight);
                    c.Ellipse(16, 6, 14, 4, WoodLight);
                    c.HLine(2, 29, 12, WoodDark);
                    c.VLine(8, 6, 29, Gold); c.VLine(9, 6, 29, PixelCanvas.Hex("#f2c14e"));
                    c.VLine(22, 6, 29, Gold); c.VLine(23, 6, 29, PixelCanvas.Hex("#f2c14e"));
                    c.Rect(14, 12, 4, 6, Yellow); c.Set(15, 14, Outline);
                    c.HLine(2, 29, 28, WoodDark);
                    c.Outline(Outline);
                    return Hd2(c.WithPivot(16f, 2f));
                }
                case "anvil":
                {
                    var c = new PixelCanvas(32, 32);
                    c.Rect(6, 8, 24, 6, Steel); c.HLine(6, 29, 8, White);
                    c.Rect(0, 10, 8, 4, Steel);
                    c.Rect(12, 14, 12, 6, SteelDark);
                    c.Rect(8, 20, 20, 6, SteelDark); c.HLine(8, 27, 20, Steel);
                    c.Set(12, 4, Carrot); c.Set(20, 2, Yellow); c.Set(16, 5, CarrotLight);
                    c.Outline(Outline);
                    return Hd2(c.WithPivot(16f, 3f));
                }
            }
            return null;
        }

        // ===================================================================================
        //  Enhancement material icons (maticon_*). 32x32.
        // ===================================================================================

        static PixelCanvas DrawMaterialIconHd(string kind)
        {
            var c = new PixelCanvas(32, 32);
            switch (kind)
            {
                case "bone":
                {
                    var bone = PixelCanvas.Hex("#f0e8d4");
                    var dark = PixelCanvas.Hex("#c8bca0");
                    for (int i = 0; i < 3; i++)
                    {
                        c.Line(8 + i, 22, 23 + i, 7, bone);
                    }
                    c.Line(8, 21, 23, 6, dark);
                    c.Circle(7f, 22f, 4f, bone); c.Circle(10f, 25f, 4f, bone);
                    c.Circle(22f, 7f, 4f, bone); c.Circle(25f, 10f, 4f, bone);
                    c.PaintEllipse(6f, 21f, 1.6f, 1.6f, White);
                    c.PaintEllipse(21f, 6f, 1.6f, 1.6f, White);
                    break;
                }
                case "ore":
                {
                    c.Ellipse(16, 20, 13, 9, StoneDark);
                    c.PaintEllipse(15, 19, 11, 7, Stone);
                    c.PaintEllipse(11, 16, 5, 3, StoneLight);
                    // Blue crystals growing out of the rock.
                    var blue = PixelCanvas.Hex("#5aa9ff");
                    var light = PixelCanvas.Hex("#bfe4ff");
                    var deep = PixelCanvas.Hex("#2f6fd0");
                    c.Rect(10, 6, 4, 14, blue); c.VLine(10, 6, 19, light); c.VLine(13, 6, 19, deep); c.Set(10, 4, light);
                    c.Rect(16, 2, 6, 18, blue); c.VLine(16, 2, 19, light); c.VLine(21, 2, 19, deep); c.Set(18, 0, light);
                    c.Rect(22, 10, 4, 10, blue); c.VLine(22, 10, 19, light); c.VLine(25, 10, 19, deep);
                    break;
                }
                default: // essence
                {
                    c.Circle(16f, 16f, 13f, PixelCanvas.WithAlpha(Magic, 120));
                    c.Circle(16f, 16f, 9f, PixelCanvas.WithAlpha(Magic, 90));
                    // Diamond-shaped crystal.
                    for (int y = 0; y < 24; y++)
                    {
                        int half = y < 10 ? y : 23 - y;
                        c.HLine(16 - half, 16 + half, y + 4, y < 10 ? MagicLight : Magic);
                    }
                    c.VLine(16, 6, 27, MagicCore); c.VLine(15, 6, 27, MagicCore);
                    c.Line(11, 12, 16, 8, MagicLight);
                    c.Set(12, 11, White); c.Set(13, 10, White);
                    break;
                }
            }
            c.Outline(Outline);
            return Hd2(c);
        }

        // ===================================================================================
        //  Side-menu icons (menuicon_*). 48x48.
        // ===================================================================================

        static PixelCanvas DrawMenuIconHd(string kind)
        {
            var c = new PixelCanvas(48, 48);
            var cream = PixelCanvas.Hex("#f6e7c8");
            var creamLo = PixelCanvas.Hex("#d8c19a");
            switch (kind)
            {
                case "menu":
                    for (int i = 0; i < 3; i++)
                    {
                        int y = 10 + i * 12;
                        c.Rect(6, y, 36, 6, cream);
                        c.HLine(6, 41, y + 5, creamLo);
                    }
                    break;
                case "bag":
                    c.Rect(8, 16, 32, 26, Wood); c.HLine(8, 39, 16, WoodLight); c.HLine(8, 39, 40, BarkDark);
                    c.Rect(16, 6, 16, 4, WoodDark); c.VLine(16, 6, 16, WoodDark); c.VLine(31, 6, 16, WoodDark);
                    c.Rect(8, 24, 32, 4, WoodDark); c.Rect(20, 22, 8, 8, Gold); c.Set(23, 24, Yellow);
                    c.HLine(10, 37, 17, PixelCanvas.Hex("#f0b878"));
                    break;
                case "skill":
                    // Spell book with a star.
                    c.Rect(8, 6, 32, 36, PixelCanvas.Hex("#3b5bb8")); c.VLine(10, 6, 41, PixelCanvas.Hex("#27418c"));
                    c.Rect(14, 8, 24, 32, PixelCanvas.Hex("#4a6fd6"));
                    c.HLine(14, 37, 8, PixelCanvas.Hex("#6f92ec"));
                    // Four-point star.
                    for (int y = 0; y < 20; y++) { int h = y < 10 ? y : 19 - y; c.HLine(26 - h / 2, 26 + h / 2, 14 + y, Yellow); }
                    for (int x = 0; x < 20; x++) { int h = x < 10 ? x : 19 - x; c.VLine(16 + x, 24 - h / 2, 24 + h / 2, Yellow); }
                    c.Set(25, 23, White);
                    break;
                case "map":
                    c.Rect(6, 8, 36, 32, PixelCanvas.Hex("#e8d3a4"));
                    c.VLine(18, 8, 39, PixelCanvas.Hex("#c7ad78")); c.VLine(30, 8, 39, PixelCanvas.Hex("#c7ad78"));
                    c.HLine(6, 41, 9, PixelCanvas.Hex("#f2e2bc"));
                    c.Line(10, 32, 24, 18, Red); c.Line(24, 18, 36, 24, Red); c.Line(11, 32, 25, 18, PixelCanvas.Hex("#ff7a7f"));
                    c.Rect(32, 12, 5, 5, Red); c.Set(33, 12, RedLight);
                    c.Ellipse(12, 16, 4, 3, Leaf);
                    break;
                case "quest":
                    c.Rect(10, 6, 28, 36, PixelCanvas.Hex("#f2e6c9")); c.HLine(10, 37, 6, WoodLight);
                    c.HLine(10, 37, 7, PixelCanvas.Hex("#fff5da"));
                    c.Rect(20, 12, 8, 16, Red); c.Rect(20, 32, 8, 6, Red); c.Set(23, 14, RedLight);
                    break;
                case "dungeon":
                    // Arched stone door.
                    c.Rect(6, 12, 36, 32, WallStone);
                    c.Ellipse(24, 14, 16, 10, WallStone);
                    c.Rect(14, 18, 20, 26, PixelCanvas.Hex("#1d1410"));
                    c.Ellipse(24, 20, 10, 7, PixelCanvas.Hex("#1d1410"));
                    c.Rect(20, 26, 8, 8, PixelCanvas.Hex("#f0e8d4")); c.Set(22, 28, Outline); c.Set(26, 28, Outline);
                    c.HLine(6, 41, 12, PixelCanvas.Hex("#b8aa96"));
                    break;
                case "raid":
                    // Crossed swords over a red emblem.
                    c.Circle(24f, 24f, 20f, PixelCanvas.Hex("#8e2a20"));
                    c.Circle(24f, 24f, 20f, PixelCanvas.WithAlpha(Red, 0));
                    c.Line(10, 36, 36, 10, Steel); c.Line(11, 37, 37, 11, SteelDark); c.Line(12, 36, 38, 10, Steel);
                    c.Line(10, 10, 36, 36, Steel); c.Line(11, 11, 37, 37, SteelDark); c.Line(10, 12, 36, 38, Steel);
                    c.Rect(6, 36, 6, 6, Gold); c.Rect(36, 36, 6, 6, Gold);
                    c.Rect(6, 6, 6, 6, Gold); c.Rect(36, 6, 6, 6, Gold);
                    break;
            }
            c.Outline(Outline);
            return Hd2(c);
        }

        // ===================================================================================
        //  Equipment / bag icons (eqicon_*). 32x32. tier 0/1/2/3 = common/rare/epic/legendary.
        // ===================================================================================

        static PixelCanvas DrawEquipIconHd(string kind, int tier)
        {
            var c = new PixelCanvas(32, 32);
            var handle = PixelCanvas.Hex("#6b4226");
            var handleLo = PixelCanvas.Hex("#4a2c16");
            var bone = PixelCanvas.Hex("#f0e8d4");
            switch (kind)
            {
                case "sword":
                {
                    var blade = tier == 0 ? WoodLight : tier == 1 ? Steel : tier == 2 ? bone : PixelCanvas.Hex("#ffe08a");
                    var bladeHi = PixelCanvas.Shade(blade, 1.15f);
                    var edge = tier == 0 ? Wood : tier == 1 ? SteelDark : tier == 2 ? Gold : Red;
                    for (int i = 0; i < 4; i++) c.Line(6 + i, 24, 22 + i, 8, blade);
                    c.Line(9, 24, 25, 8, edge);
                    c.Line(7, 23, 22, 8, bladeHi);
                    c.Set(24, 6, blade); c.Set(25, 6, blade); c.Set(26, 6, bladeHi);
                    c.Line(4, 20, 12, 28, tier == 2 ? Red : Gold); c.Line(5, 20, 13, 28, PixelCanvas.Shade(Gold, 0.8f));
                    c.Rect(2, 26, 6, 6, handle); c.VLine(3, 27, 31, handleLo);
                    c.Rect(1, 30, 6, 2, Gold);
                    break;
                }
                case "staff":
                {
                    var orb = tier == 0 ? LeafLight : tier == 1 ? MagicCore : tier == 2 ? PixelCanvas.Hex("#fff3b0") : PixelCanvas.Hex("#ffd84a");
                    c.Line(6, 28, 20, 12, handle); c.Line(7, 28, 21, 12, handleLo); c.Line(8, 28, 22, 12, BarkDark);
                    c.Circle(23f, 9f, 6.4f, tier >= 2 ? Gold : Magic);
                    c.Circle(23f, 9f, 4f, orb);
                    c.Circle(21.5f, 7.5f, 1.6f, White);
                    if (tier >= 3) { c.Set(23, 1, Yellow); c.Set(31, 9, Yellow); c.Set(15, 9, Yellow); c.Set(23, 17, Yellow); c.Set(28, 4, Yellow); c.Set(18, 4, Yellow); }
                    break;
                }
                case "neck":
                {
                    var chain = tier == 0 ? LeafDark : tier == 1 ? StoneLight : Gold;
                    for (int i = 0; i <= 20; i++)
                    {
                        float a = Mathf.PI * i / 20f;
                        c.Set(Mathf.RoundToInt(16 + Mathf.Cos(a) * 12), Mathf.RoundToInt(6 + Mathf.Sin(a) * 12), chain);
                        c.Set(Mathf.RoundToInt(16 + Mathf.Cos(a) * 12) + 1, Mathf.RoundToInt(6 + Mathf.Sin(a) * 12), chain);
                    }
                    if (tier == 0) { c.Ellipse(16, 23, 5, 6, Leaf); c.VLine(16, 20, 26, LeafShine); }
                    else if (tier == 1) { c.Rect(14, 18, 4, 10, bone); c.Rect(12, 18, 8, 2, bone); c.Rect(12, 26, 8, 2, bone); }
                    else { c.Ellipse(16, 23, 6, 6, Gold); c.Ellipse(16, 23, 3.6f, 3.6f, Red); c.Set(14, 20, White); }
                    break;
                }
                case "ring":
                {
                    var band = tier == 0 ? PixelCanvas.Hex("#c87a4a") : tier == 1 ? PixelCanvas.Hex("#bfe8f0") : Gold;
                    var bandHi = PixelCanvas.Shade(band, 1.2f);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float d = Mathf.Sqrt((x - 15f) * (x - 15f) + (y - 18f) * (y - 18f));
                            if (d <= 11f && d >= 7f) c.Set(x, y, x < 15 ? bandHi : band);
                        }
                    var gem = tier == 0 ? PixelCanvas.Hex("#e0a06a") : tier == 1 ? PixelCanvas.Hex("#6fe7ff") : Red;
                    c.Rect(12, 4, 8, 6, gem); c.Set(13, 5, White); c.Set(12, 4, White);
                    break;
                }
                case "top":
                {
                    var cloth = tier == 0 ? PixelCanvas.Hex("#d9c7a0") : tier == 1 ? PixelCanvas.Hex("#a0643a") : Steel;
                    var dark = PixelCanvas.Shade(cloth, 0.75f);
                    var light = PixelCanvas.Shade(cloth, 1.12f);
                    c.Rect(8, 6, 16, 22, cloth);
                    c.Rect(2, 6, 6, 12, cloth); c.Rect(24, 6, 6, 12, cloth);
                    c.Rect(12, 4, 8, 4, PixelCanvas.Clear);
                    c.VLine(16, 8, 26, dark); c.HLine(8, 23, 26, dark);
                    c.VLine(9, 8, 25, light);
                    if (tier == 2) { c.HLine(8, 23, 14, SteelDark); c.Set(16, 12, Gold); c.Set(15, 12, Gold); }
                    break;
                }
                default: // bottoms
                {
                    var cloth = tier == 0 ? PixelCanvas.Hex("#8aa0c8") : PixelCanvas.Hex("#7a4a2a");
                    var dark = PixelCanvas.Shade(cloth, 0.75f);
                    var light = PixelCanvas.Shade(cloth, 1.12f);
                    c.Rect(6, 4, 20, 8, cloth);
                    c.Rect(6, 12, 8, 16, cloth); c.Rect(18, 12, 8, 16, cloth);
                    c.HLine(6, 25, 6, light);
                    c.HLine(6, 25, 7, dark);
                    c.VLine(16, 8, 12, dark);
                    break;
                }
            }
            c.Outline(Outline);
            return Hd2(c);
        }

        // ===================================================================================
        //  Passive-tree node glyphs (node_*). 24x24 white glyphs, tinted by the UI.
        // ===================================================================================

        static PixelCanvas DrawNodeGlyphHd(string kind)
        {
            var c = new PixelCanvas(24, 24);
            var w = White;
            float D(int x, int y) => Mathf.Sqrt((x - 11.5f) * (x - 11.5f) + (y - 11.5f) * (y - 11.5f));
            switch (kind)
            {
                case "area":
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 24; x++)
                        {
                            float d = D(x, y);
                            if (d >= 8.6f && d < 11.2f || d >= 4.2f && d < 5.8f) c.Set(x, y, w);
                        }
                    break;
                case "dmg":
                    for (int i = 0; i < 3; i++) c.Line(4 + i, 16, 18 + i, 2, w);
                    c.Line(2, 12, 10, 20, w); c.Line(3, 12, 11, 20, w);
                    c.Line(2, 20, 5, 17, w); c.Set(0, 22, w); c.Set(1, 21, w);
                    break;
                case "cd":
                    c.HLine(2, 21, 0, w); c.HLine(2, 21, 1, w); c.HLine(2, 21, 22, w); c.HLine(2, 21, 23, w);
                    c.Line(4, 2, 11, 11, w); c.Line(19, 2, 12, 11, w); c.Line(11, 12, 4, 21, w); c.Line(12, 12, 19, 21, w);
                    c.Line(5, 2, 12, 11, w); c.Line(18, 2, 11, 11, w);
                    break;
                case "mastery":
                    c.VLine(11, 0, 23, w); c.VLine(12, 0, 23, w); c.HLine(0, 23, 11, w); c.HLine(0, 23, 12, w);
                    c.Rect(7, 7, 10, 10, w); c.Rect(9, 9, 6, 6, PixelCanvas.Clear);
                    c.Rect(2, 2, 3, 3, w); c.Rect(19, 2, 3, 3, w); c.Rect(2, 19, 3, 3, w); c.Rect(19, 19, 3, 3, w);
                    break;
                case "start":
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 24; x++)
                        {
                            float d = D(x, y);
                            if (d >= 8.6f && d < 11.2f || d < 4f) c.Set(x, y, w);
                        }
                    break;
                case "shield":
                    c.Rect(4, 2, 16, 12, w);
                    for (int y = 14; y <= 21; y++) { int inset = (y - 13); c.HLine(4 + inset, 19 - inset, y, w); }
                    c.Rect(9, 6, 6, 6, PixelCanvas.Clear);
                    break;
                case "burst":
                    c.VLine(11, 0, 23, w); c.VLine(12, 0, 23, w); c.HLine(0, 23, 11, w); c.HLine(0, 23, 12, w);
                    c.Line(2, 2, 21, 21, w); c.Line(21, 2, 2, 21, w);
                    c.Circle(11.5f, 11.5f, 4.5f, w); c.Circle(11.5f, 11.5f, 2f, PixelCanvas.Clear);
                    break;
                case "drop":
                    c.Circle(11.5f, 15f, 7.2f, w);
                    for (int y = 2; y <= 10; y++) { int h = (y) / 2; c.HLine(11 - h, 12 + h, y, w); }
                    c.Circle(9f, 13f, 2f, PixelCanvas.Clear);
                    break;
                default: // sun
                    c.Circle(11.5f, 11.5f, 5.6f, w);
                    c.VLine(11, 0, 3, w); c.VLine(12, 0, 3, w); c.VLine(11, 20, 23, w); c.VLine(12, 20, 23, w);
                    c.HLine(0, 3, 11, w); c.HLine(0, 3, 12, w); c.HLine(20, 23, 11, w); c.HLine(20, 23, 12, w);
                    c.Rect(2, 2, 3, 3, w); c.Rect(19, 2, 3, 3, w); c.Rect(2, 19, 3, 3, w); c.Rect(19, 19, 3, 3, w);
                    break;
            }
            return Hd2(c);
        }

        // ===================================================================================
        //  Passive-tree big node glyph white version reused; hearts (heart_*). 22x20.
        // ===================================================================================

        static PixelCanvas DrawHeartHd(string kind)
        {
            var c = new PixelCanvas(22, 20);
            var empty = PixelCanvas.Hex("#5a2d35");
            var emptyHi = PixelCanvas.Hex("#7a3d47");
            // Heart silhouette at 2x the old resolution.
            string[] shape =
            {
                "..xxxx....xxxx..",
                ".xxxxxx..xxxxxx.",
                "xxxxxxxxxxxxxxxx",
                "xxxxxxxxxxxxxxxx",
                "xxxxxxxxxxxxxxxx",
                ".xxxxxxxxxxxxxx.",
                ".xxxxxxxxxxxxxx.",
                "..xxxxxxxxxxxx..",
                "...xxxxxxxxxx...",
                "....xxxxxxxx....",
                ".....xxxxxx.....",
                "......xxxx......",
                ".......xx.......",
            };
            for (int y = 0; y < shape.Length; y++)
                for (int x = 0; x < shape[y].Length; x++)
                {
                    if (shape[y][x] != 'x') continue;
                    bool filled = kind == "full" || (kind == "half" && x < 8);
                    c.Set(x + 3, y + 2, filled ? Red : (kind == "empty" ? empty : empty));
                }
            if (kind != "empty")
            {
                // Glossy highlight on the top-left lobe.
                c.Set(5, 4, RedLight); c.Set(6, 4, RedLight); c.Set(5, 5, RedLight); c.Set(7, 4, RedLight);
                c.Set(6, 5, PixelCanvas.Hex("#ffc0c4"));
            }
            if (kind == "half")
            {
                // Faint outline on the empty right half so it still reads as a heart.
                for (int y = 0; y < shape.Length; y++)
                    for (int x = 8; x < shape[y].Length; x++)
                        if (shape[y][x] == 'x') c.Set(x + 3, y + 2, emptyHi);
            }
            c.Outline(Outline);
            return Hd2(c);
        }

        // ===================================================================================
        //  Skill / support gems (gem_*). 40x40.
        // ===================================================================================

        static PixelCanvas DrawGemHd(string id)
        {
            var c = new PixelCanvas(40, 40);
            bool support = id.StartsWith("sup_");
            bool ultimate = id == "blades" || id == "meteor";
            Color32 baseCol = ultimate ? PixelCanvas.Hex("#e0a020")
                : id == "whirl" || id == "cry" ? PixelCanvas.Hex("#d9443a")
                : id == "nova" || id == "frostorb" ? PixelCanvas.Hex("#3f7fe0")
                : id == "sup_leech" ? PixelCanvas.Hex("#c0304a")
                : id == "sup_chain" || id == "sup_eff" ? PixelCanvas.Hex("#4a78d8")
                : PixelCanvas.Hex("#4fb34a");
            var light = PixelCanvas.Shade(baseCol, 1.35f);
            var lighter = PixelCanvas.Shade(baseCol, 1.6f);
            var dark = PixelCanvas.Shade(baseCol, 0.65f);
            if (support)
            {
                c.Circle(19.5f, 19.5f, 18f, dark);
                c.Circle(19.5f, 19.5f, 15f, baseCol);
                c.Circle(15f, 14f, 6f, light);
                c.Circle(13.5f, 12.5f, 2.5f, lighter);
            }
            else
            {
                // Faceted diamond (awakening skills get a four-pointed gold star behind it).
                if (ultimate)
                {
                    c.HLine(0, 39, 18, dark); c.HLine(0, 39, 21, dark);
                    c.HLine(4, 35, 16, baseCol); c.HLine(4, 35, 23, baseCol);
                    c.VLine(18, 2, 37, PixelCanvas.WithAlpha(light, 120)); c.VLine(21, 2, 37, PixelCanvas.WithAlpha(light, 120));
                }
                for (int y = 0; y < 36; y++)
                {
                    int half = y < 18 ? y : 35 - y;
                    c.HLine(19 - half, 20 + half, y + 2, y < 14 ? light : y < 24 ? baseCol : dark);
                }
                c.VLine(19, 4, 35, PixelCanvas.Shade(baseCol, 1.15f)); c.VLine(20, 4, 35, PixelCanvas.Shade(baseCol, 1.15f));
                // Corner facet highlights.
                c.Line(14, 8, 19, 4, lighter); c.Line(20, 4, 25, 8, light);
            }
            var mark = White;
            switch (id)
            {
                case "cry":
                    c.Set(10, 18, mark); c.Set(10, 19, mark); c.Set(10, 20, mark); c.Set(11, 19, mark);
                    for (int r = 6; r <= 14; r += 4)
                        for (int a = -45; a <= 45; a += 6)
                            c.Set(Mathf.RoundToInt(10f + Mathf.Cos(a * Mathf.Deg2Rad) * r), Mathf.RoundToInt(19f + Mathf.Sin(a * Mathf.Deg2Rad) * r), mark);
                    break;
                case "blades":
                    c.Rect(18, 6, 4, 18, mark); c.Rect(12, 24, 16, 2, mark); c.Rect(18, 26, 4, 6, PixelCanvas.Hex("#6b4226")); c.Set(18, 4, mark); c.Set(19, 4, mark);
                    break;
                case "frostorb":
                    c.Circle(19f, 20f, 9.2f, mark);
                    c.Circle(19f, 20f, 6.6f, PixelCanvas.Hex("#9fe3ff"));
                    c.Set(16, 16, mark); c.Set(14, 18, mark); c.Set(15, 15, mark);
                    c.Line(24, 6, 18, 18, PixelCanvas.Hex("#ffe070")); c.Line(18, 18, 24, 20, PixelCanvas.Hex("#ffe070")); c.Line(24, 20, 16, 32, PixelCanvas.Hex("#ffe070"));
                    break;
                case "meteor":
                    c.Line(6, 6, 18, 18, PixelCanvas.Hex("#ffe070")); c.Line(10, 6, 20, 16, PixelCanvas.Hex("#ffe070")); c.Line(6, 10, 16, 20, PixelCanvas.Hex("#ffe070"));
                    c.Circle(24f, 24f, 6.8f, mark); c.Circle(25f, 25f, 3.6f, PixelCanvas.Hex("#e8401c"));
                    break;
                case "whirl": c.Circle(19f, 19f, 7f, mark); c.Circle(19f, 19f, 4f, baseCol); c.Set(26, 14, mark); c.Set(12, 24, mark); break;
                case "nova": c.VLine(18, 8, 31, mark); c.VLine(19, 8, 31, mark); c.HLine(8, 31, 18, mark); c.HLine(8, 31, 19, mark); c.Line(12, 12, 27, 27, mark); c.Line(27, 12, 12, 27, mark); break;
                case "sup_dmg": c.VLine(18, 10, 29, mark); c.VLine(19, 10, 29, mark); c.HLine(10, 29, 18, mark); c.HLine(10, 29, 19, mark); break;
                case "sup_aoe": c.Circle(19f, 19f, 10f, mark); c.Circle(19f, 19f, 7f, baseCol); break;
                case "sup_multi": c.Rect(10, 12, 5, 16, mark); c.Rect(24, 12, 5, 16, mark); c.Rect(17, 12, 5, 16, mark); break;
                case "sup_eff": c.Ellipse(19f, 21f, 7f, 8f, mark); c.Set(18, 10, mark); c.Set(19, 10, mark); break;
                case "sup_leech": c.Ellipse(19f, 22f, 7f, 7f, mark); c.Line(18, 10, 14, 18, mark); c.Line(20, 10, 24, 18, mark); break;
                case "sup_chain": c.Circle(13f, 19f, 5f, mark); c.Circle(25f, 19f, 5f, mark); c.Circle(13f, 19f, 2.4f, baseCol); c.Circle(25f, 19f, 2.4f, baseCol); break;
            }
            c.Outline(Outline);
            return Hd2(c);
        }

        // ===================================================================================
        //  Legacy helpers still referenced by the shared ProceduralArt.cs (do not remove).
        //  DrawItemIcon16 backs the 16px ticket icon; DrawAnvil is the "anvil" world sprite
        //  fallback (the HD town anvil comes from the town stream's town_* keys).
        // ===================================================================================

        static readonly Color32 handleColor = PixelCanvas.Hex("#6b4226");

        /// <summary>16x16 item icons (only the ticket icon still routes here).</summary>
        static PixelCanvas DrawItemIcon16(string kind)
        {
            switch (kind)
            {
                case "gold":
                {
                    var c = new PixelCanvas(16, 16);
                    var rim = PixelCanvas.Hex("#b07a1c");
                    var face = PixelCanvas.Hex("#f2c14e");
                    var shine = PixelCanvas.Hex("#ffe89a");
                    c.Ellipse(10.5f, 6.5f, 4.6f, 4.6f, rim);
                    c.Ellipse(10.5f, 6.5f, 3.6f, 3.6f, face);
                    c.Ellipse(7f, 9.5f, 5.8f, 5.8f, rim);
                    c.Ellipse(7f, 9.5f, 4.8f, 4.8f, face);
                    c.PaintEllipse(5.5f, 8f, 2.6f, 2.2f, shine);
                    c.Rect(6, 8, 3, 3, rim);
                    c.Rect(7, 9, 1, 1, PixelCanvas.Clear);
                    c.Set(4, 6, White);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1.5f);
                }
                case "potion_hp":
                case "potion_mp":
                {
                    bool hp = kind == "potion_hp";
                    var c = new PixelCanvas(16, 16);
                    var glass = PixelCanvas.Hex("#d8eef6");
                    var liquid = hp ? PixelCanvas.Hex("#e0413a") : PixelCanvas.Hex("#3f7ee8");
                    var liquidLight = hp ? PixelCanvas.Hex("#ff8a72") : PixelCanvas.Hex("#86b8ff");
                    var liquidDark = hp ? PixelCanvas.Hex("#a82a2a") : PixelCanvas.Hex("#2a53b0");
                    c.Circle(8f, 10.5f, 5.2f, glass);
                    c.Rect(6, 3, 4, 4, glass);
                    for (int y = 8; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                            if (c.IsOpaque(x, y)) c.Set(x, y, y >= 13 ? liquidDark : liquid);
                    c.PaintEllipse(6f, 10f, 1.8f, 1.2f, liquidLight);
                    c.Rect(6, 1, 4, 2, PixelCanvas.Hex("#9a6a3c"));
                    c.HLine(6, 9, 1, PixelCanvas.Hex("#c49058"));
                    c.Set(5, 9, White); c.Set(5, 10, White);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "scroll":
                {
                    var c = new PixelCanvas(16, 16);
                    var paper = PixelCanvas.Hex("#f3e2b8");
                    var paperDark = PixelCanvas.Hex("#d7bf8a");
                    var roll = PixelCanvas.Hex("#c9a46a");
                    c.Rect(3, 4, 10, 8, paper);
                    c.HLine(3, 12, 11, paperDark);
                    c.Rect(1, 3, 3, 10, roll); c.VLine(1, 4, 11, PixelCanvas.Hex("#a8844e"));
                    c.Rect(12, 3, 3, 10, roll); c.VLine(14, 4, 11, PixelCanvas.Hex("#a8844e"));
                    var rune = PixelCanvas.Hex("#4f8fe8");
                    c.Circle(8f, 7.5f, 2.6f, PixelCanvas.Hex("#9fd0ff"));
                    c.VLine(8, 5, 10, rune); c.HLine(6, 10, 7, rune);
                    c.Rect(7, 11, 2, 2, PixelCanvas.Hex("#c8323a"));
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "ticket":
                {
                    // 장비 보호권: a gold-edged voucher with coupon notches, a blue shield crest and a red wax seal.
                    var c = new PixelCanvas(16, 16);
                    var edge = PixelCanvas.Hex("#c8901e");
                    var edgeLight = PixelCanvas.Hex("#ffd84a");
                    var paper = PixelCanvas.Hex("#f6e7c8");
                    var paperDark = PixelCanvas.Hex("#dcc596");
                    c.Rect(1, 3, 14, 10, edge);
                    c.HLine(2, 13, 3, edgeLight);
                    c.Rect(2, 4, 12, 8, paper);
                    c.HLine(2, 13, 11, paperDark);
                    // Notches halfway down both short sides.
                    c.Rect(1, 7, 1, 2, PixelCanvas.Clear); c.Rect(14, 7, 1, 2, PixelCanvas.Clear);
                    c.VLine(2, 7, 8, edge); c.VLine(13, 7, 8, edge);
                    // Shield crest with a gold gem.
                    var shield = PixelCanvas.Hex("#3f7ee8");
                    var shieldLight = PixelCanvas.Hex("#86b8ff");
                    c.Rect(5, 4, 6, 4, shield);
                    c.HLine(6, 9, 8, shield);
                    c.HLine(7, 8, 9, shield);
                    c.VLine(5, 4, 7, shieldLight); c.Set(6, 8, shieldLight);
                    c.Rect(7, 5, 2, 2, edgeLight);
                    // Wax seal on the corner.
                    c.Circle(12.5f, 11.5f, 2.3f, PixelCanvas.Hex("#c8323a"));
                    c.Set(12, 11, PixelCanvas.Hex("#ff7a6a"));
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "chest":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(1, 6, 14, 9, Wood);
                    c.Rect(1, 3, 14, 4, WoodLight);
                    c.HLine(1, 14, 6, WoodDark);
                    c.VLine(4, 3, 14, Gold); c.VLine(11, 3, 14, Gold);
                    c.Rect(7, 6, 2, 3, Yellow);
                    c.HLine(1, 14, 14, WoodDark);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "anvil":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(3, 4, 12, 3, Steel); c.HLine(3, 14, 4, White);
                    c.Rect(0, 5, 4, 2, Steel);
                    c.Rect(6, 7, 6, 3, SteelDark);
                    c.Rect(4, 10, 10, 3, SteelDark); c.HLine(4, 13, 10, Steel);
                    c.Set(6, 2, Carrot); c.Set(10, 1, Yellow);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
            }
            return null;
        }

        /// <summary>Blacksmith anvil on a stump with a hammer - the "anvil" world sprite fallback.</summary>
        static PixelCanvas DrawAnvil()
        {
            var c = new PixelCanvas(28, 24);
            c.Rect(8, 14, 12, 9, Bark); c.HLine(8, 19, 14, WoodLight); c.VLine(10, 15, 22, BarkDark); c.VLine(17, 15, 22, BarkDark);
            c.Rect(4, 6, 20, 5, SteelDark); c.HLine(4, 23, 6, Steel);
            c.Rect(1, 7, 4, 3, SteelDark); c.Set(0, 8, SteelDark);
            c.Rect(10, 11, 8, 3, SteelDark);
            c.Line(20, 2, 25, 7, handleColor); c.Rect(18, 0, 5, 3, Steel);
            c.Set(7, 5, Carrot); c.Set(13, 4, Yellow); c.Set(16, 5, Carrot);
            c.Outline(Outline);
            return c.WithPivot(14, 1.5f);
        }
    }
}
