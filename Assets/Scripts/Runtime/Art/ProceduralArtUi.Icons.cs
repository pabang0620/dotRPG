using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
