using System;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        static PixelCanvas DrawHouse(string variant)
        {
            var c = new PixelCanvas(52, 52);
            var roof = PixelCanvas.Hex("#3b8fe3");
            var roofLight = PixelCanvas.Hex("#7cc4ff");
            var roofDark = PixelCanvas.Hex("#2a6fbf");
            var roofPanel = PixelCanvas.Hex("#4aa2f0");
            if (variant == "red")
            {
                roof = PixelCanvas.Hex("#c0392b"); roofLight = PixelCanvas.Hex("#e8674f");
                roofDark = PixelCanvas.Hex("#8e2a20"); roofPanel = PixelCanvas.Hex("#d24a36");
            }
            else if (variant == "green")
            {
                roof = PixelCanvas.Hex("#3f8f4f"); roofLight = PixelCanvas.Hex("#6cc070");
                roofDark = PixelCanvas.Hex("#2a6a38"); roofPanel = PixelCanvas.Hex("#4ea45c");
            }
            var trim = PixelCanvas.Hex("#f2ede4");
            var trimRed = PixelCanvas.Hex("#c4553d");
            // Walls.
            c.Rect(6, 28, 40, 20, PixelCanvas.Hex("#d9955a"));
            for (int y = 31; y < 48; y += 3) c.HLine(6, 45, y, PixelCanvas.Hex("#a86a3a"));
            c.VLine(6, 28, 47, WoodDark); c.VLine(45, 28, 47, WoodDark);
            // Door (dark opening like a barn).
            c.Rect(17, 34, 18, 14, PixelCanvas.Hex("#3a2a2a"));
            c.Rect(18, 35, 16, 2, PixelCanvas.Hex("#2a1d1d"));
            c.HLine(16, 35, 33, trim);
            c.VLine(16, 33, 47, trim); c.VLine(35, 33, 47, trim);
            // Foundation.
            c.Rect(5, 48, 42, 2, StoneDark);
            // Roof.
            c.Rect(3, 6, 46, 23, roof);
            c.Rect(2, 26, 48, 3, trim);
            c.HLine(2, 49, 29, trimRed);
            c.Rect(3, 3, 46, 4, trim);
            c.HLine(3, 48, 3, trimRed);
            for (int x = 8; x < 46; x += 5) c.VLine(x, 8, 24, roofLight);
            c.HLine(3, 48, 25, roofDark);
            c.Rect(14, 9, 20, 13, roofPanel);
            for (int x = 16; x < 33; x += 4) c.VLine(x, 10, 20, roofLight);
            // Roof vent box (like the reference's rooftop crate).
            c.Rect(8, 1, 8, 7, StoneDark);
            c.Rect(9, 2, 6, 5, SteelDark);
            c.HLine(9, 14, 2, StoneLight);
            c.Outline(Outline);
            return c.WithPivot(26, 3.5f);
        }

        static PixelCanvas DrawBarrel()
        {
            var c = new PixelCanvas(12, 15);
            c.Rect(2, 1, 8, 13, Wood);
            c.VLine(1, 3, 11, Wood); c.VLine(10, 3, 11, Wood);
            c.VLine(3, 2, 12, WoodLight);
            c.VLine(8, 2, 12, WoodDark);
            c.HLine(1, 10, 3, SteelDark); c.HLine(1, 10, 10, SteelDark);
            c.Ellipse(6, 1.5f, 4, 1.4f, WoodDark);
            c.PaintEllipse(6, 1.5f, 2.5f, 0.8f, Bark);
            c.Outline(Outline);
            return c.WithPivot(6, 1.5f);
        }

        /// <summary>Glowing ground arrow that marks a map exit.</summary>
        static PixelCanvas DrawArrow(string dir)
        {
            var c = new PixelCanvas(14, 14);
            string[] shape =
            {
                "......x.......",
                "......xx......",
                "......xxx.....",
                "xxxxxxxxxx....",
                "xxxxxxxxxxx...",
                "xxxxxxxxxxxx..",
                "xxxxxxxxxxx...",
                "xxxxxxxxxx....",
                "......xxx.....",
                "......xx......",
                "......x.......",
            };
            for (int y = 0; y < shape.Length; y++)
                for (int x = 0; x < shape[y].Length; x++)
                {
                    if (shape[y][x] != 'x') continue;
                    int px = x, py = y + 1;
                    switch (dir)
                    {
                        case "left": px = 13 - x; break;
                        case "up": px = y + 1; py = 13 - x; break;
                        case "down": px = y + 1; py = x; break;
                    }
                    c.Set(px, py, PixelCanvas.WithAlpha(Yellow, 210));
                }
            c.Outline(PixelCanvas.WithAlpha(Outline, 200));
            return c;
        }

        static PixelCanvas DrawBlueprint()
        {
            var c = new PixelCanvas(52, 52);
            var ghost = PixelCanvas.Hex("#86e0a0", 120);
            var ghostLine = PixelCanvas.Hex("#c8ffd6", 170);
            var bracket = PixelCanvas.Hex("#f2e6c9");
            c.Rect(6, 8, 40, 38, ghost);
            for (int x = 10; x < 44; x += 5) c.VLine(x, 10, 26, ghostLine);
            c.HLine(6, 45, 27, ghostLine);
            for (int y = 31; y < 45; y += 4) c.HLine(8, 43, y, PixelCanvas.WithAlpha(ghostLine, 110));
            c.Rect(20, 34, 12, 12, PixelCanvas.Hex("#5ab87a", 150));
            // Corner brackets.
            c.Rect(2, 4, 6, 2, bracket); c.Rect(2, 4, 2, 6, bracket);
            c.Rect(44, 4, 6, 2, bracket); c.Rect(48, 4, 2, 6, bracket);
            c.Rect(2, 46, 6, 2, bracket); c.Rect(2, 42, 2, 6, bracket);
            c.Rect(44, 46, 6, 2, bracket); c.Rect(48, 42, 2, 6, bracket);
            return c.WithPivot(26, 3.5f);
        }

        static PixelCanvas DrawWorkshop()
        {
            var c = new PixelCanvas(52, 52);
            var roof = PixelCanvas.Hex("#3fae6b");
            var roofLight = PixelCanvas.Hex("#86e0a0");
            var roofDark = PixelCanvas.Hex("#2b8a52");
            var trim = PixelCanvas.Hex("#f2ede4");
            c.Rect(6, 28, 40, 20, PixelCanvas.Hex("#c98a55"));
            for (int y = 31; y < 48; y += 3) c.HLine(6, 45, y, PixelCanvas.Hex("#9a6236"));
            c.Rect(18, 34, 16, 14, PixelCanvas.Hex("#6b3f22"));
            c.VLine(25, 34, 47, PixelCanvas.Hex("#4a2a16"));
            c.Set(23, 41, Gold); c.Set(28, 41, Gold);
            c.Rect(9, 33, 6, 5, PixelCanvas.Hex("#9bd8ff"));
            c.Rect(37, 33, 6, 5, PixelCanvas.Hex("#9bd8ff"));
            c.Rect(5, 48, 42, 2, StoneDark);
            c.Rect(3, 6, 46, 23, roof);
            c.Rect(2, 26, 48, 3, trim);
            for (int x = 8; x < 46; x += 4) c.VLine(x, 7, 24, roofLight);
            c.HLine(3, 48, 25, roofDark);
            c.Rect(3, 3, 46, 4, trim);
            // Hammer sign on the roof.
            c.Rect(22, 12, 8, 3, SteelDark);
            c.Rect(25, 15, 2, 7, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(26, 3.5f);
        }

        static PixelCanvas DrawPile(bool stone)
        {
            var c = new PixelCanvas(18, 12);
            if (stone)
            {
                c.Ellipse(5, 8, 4, 3, Stone); c.Ellipse(12, 8, 4.5f, 3, Stone); c.Ellipse(8.5f, 4.5f, 4, 3, Stone);
                c.PaintEllipse(7.5f, 3.5f, 2, 1.2f, StoneLight);
                c.PaintEllipse(4, 7, 1.5f, 1, StoneLight);
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    int y = 8 - i * 3, x0 = 2 + i * 2, x1 = 15 - i * 2;
                    c.Rect(x0, y, x1 - x0 + 1, 3, Bark);
                    c.HLine(x0, x1, y, WoodLight);
                    c.Rect(x1 - 1, y, 2, 3, WoodLight);
                    c.Set(x1, y + 1, Wood);
                }
            }
            c.Outline(Outline);
            return c.WithPivot(9, 1.5f);
        }

        static PixelCanvas DrawCrop(string state)
        {
            var c = new PixelCanvas(16, 16);
            var hole = PixelCanvas.Hex("#8a5530");
            var holeDark = PixelCanvas.Hex("#5e3a1e");
            c.Ellipse(8, 12, 5, 3, hole);
            c.PaintEllipse(8, 11.5f, 3.5f, 1.8f, holeDark);
            if (state == "carrot")
            {
                c.Rect(5, 7, 6, 5, Carrot);
                c.Rect(6, 12, 4, 1, Carrot);
                c.HLine(5, 10, 7, CarrotLight);
                c.Set(7, 9, PixelCanvas.Shade(Carrot, 0.8f));
                c.Rect(7, 2, 2, 5, Leaf);
                c.Rect(5, 3, 2, 3, LeafLight);
                c.Rect(9, 3, 2, 3, LeafLight);
                c.Set(8, 1, LeafLight);
            }
            else if (state == "sprout")
            {
                c.Rect(7, 8, 2, 3, Leaf);
                c.Set(6, 8, LeafLight); c.Set(9, 8, LeafLight);
            }
            c.Outline(PixelCanvas.WithAlpha(Outline, 200));
            return c.WithPivot(8, 2.5f);
        }

        // ---------- Tools & FX ----------

        static PixelCanvas DrawFx(string kind)
        {
            if (HdEffects) { var hd = DrawFxHd(kind); if (hd != null) return hd; }
            switch (kind)
            {
                case "slash":
                {
                    // Crescent arc facing right; rotated at runtime.
                    var c = new PixelCanvas(20, 24);
                    c.Ellipse(8, 12, 11, 11, PixelCanvas.WithAlpha(White, 230));
                    // Carve out the inside to make a crescent.
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 20; x++)
                        {
                            float nx = (x + 0.5f - 4f) / 10f, ny = (y + 0.5f - 12f) / 10.5f;
                            if (nx * nx + ny * ny <= 1f) c.Pixels[y * 20 + x] = PixelCanvas.Clear;
                        }
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 20; x++)
                        {
                            var p = c.Pixels[y * 20 + x];
                            if (p.a > 0 && Math.Abs(y - 12) > 7) c.Pixels[y * 20 + x] = PixelCanvas.WithAlpha(Yellow, 200);
                        }
                    return c.WithPivot(4, 12);
                }
                case "sparkle":
                {
                    var c = new PixelCanvas(11, 11);
                    c.VLine(5, 0, 10, Yellow); c.HLine(0, 10, 5, Yellow);
                    c.Rect(4, 3, 3, 5, Yellow); c.Rect(3, 4, 5, 3, Yellow);
                    c.Set(5, 5, White); c.Set(5, 4, White); c.Set(4, 5, White);
                    c.Set(2, 2, Yellow); c.Set(8, 2, Yellow); c.Set(2, 8, Yellow); c.Set(8, 8, Yellow);
                    return c;
                }
                case "dust":
                {
                    var c = new PixelCanvas(6, 6);
                    c.Circle(3, 3, 2.6f, PixelCanvas.Hex("#fffaf0", 220));
                    c.PaintEllipse(3.6f, 3.8f, 1.6f, 1.2f, PixelCanvas.Hex("#e0d8c8", 220));
                    return c;
                }
                case "leaf":
                {
                    var c = new PixelCanvas(3, 3);
                    c.Set(1, 0, LeafLight); c.Set(0, 1, Leaf); c.Set(1, 1, LeafLight); c.Set(2, 1, Leaf); c.Set(1, 2, LeafDark);
                    return c;
                }
                case "chip":
                {
                    var c = new PixelCanvas(3, 3);
                    c.Rect(0, 0, 2, 2, Stone); c.Set(1, 1, StoneDark); c.Set(2, 2, StoneDark);
                    return c;
                }
                case "bone":
                {
                    var c = new PixelCanvas(6, 3);
                    c.HLine(1, 4, 1, PixelCanvas.Hex("#f0e8d4"));
                    c.Set(0, 0, PixelCanvas.Hex("#f0e8d4")); c.Set(0, 2, PixelCanvas.Hex("#f0e8d4"));
                    c.Set(5, 0, PixelCanvas.Hex("#f0e8d4")); c.Set(5, 2, PixelCanvas.Hex("#f0e8d4"));
                    return c;
                }
                case "water":
                {
                    var c = new PixelCanvas(2, 3);
                    c.Rect(0, 0, 2, 3, PixelCanvas.Hex("#6fc0ff"));
                    c.Set(0, 0, White);
                    return c;
                }
                case "bolt":
                {
                    var c = new PixelCanvas(12, 12);
                    c.Circle(6, 6, 5.5f, PixelCanvas.WithAlpha(Magic, 140));
                    c.Circle(6, 6, 4f, Magic);
                    c.Circle(6, 6, 2.8f, MagicLight);
                    c.Circle(6, 6, 1.5f, White);
                    c.Set(3, 2, MagicCore); c.Set(9, 3, MagicCore); c.Set(2, 8, MagicCore);
                    return c;
                }
                case "magic":
                {
                    var c = new PixelCanvas(5, 5);
                    c.VLine(2, 0, 4, Magic); c.HLine(0, 4, 2, Magic);
                    c.Set(2, 2, MagicLight);
                    return c;
                }
                case "ring":
                {
                    var c = new PixelCanvas(32, 32);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
                            if (d <= 15.5f && d >= 12.5f) c.Set(x, y, White);
                            else if (d < 12.5f && d >= 11f) c.Set(x, y, PixelCanvas.WithAlpha(White, 110));
                            else if (d < 11f) c.Set(x, y, PixelCanvas.WithAlpha(White, 30));
                        }
                    return c;
                }
                case "alert":
                {
                    var c = new PixelCanvas(5, 9);
                    c.Rect(1, 0, 3, 5, Red); c.Rect(1, 6, 3, 2, Red);
                    c.VLine(2, 1, 3, RedLight);
                    c.Outline(Outline);
                    return c.WithBottomPivot();
                }
            }
            return DrawSkillFx(kind);
        }
    }
}
