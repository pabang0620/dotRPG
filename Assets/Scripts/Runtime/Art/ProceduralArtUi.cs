using UnityEngine;

namespace DotRPG
{
    /// <summary>Icons for enhancement materials and the side menu, plus the blacksmith anvil.</summary>
    public static partial class ProceduralArt
    {
        /// <summary>
        /// 16x16 icons for money and usable items: gold coin (with a square hole like an old Korean
        /// coin), red / blue potion flasks, the rolled return scroll and a storage chest.
        /// Returns null for other kinds.
        /// </summary>
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
                    // A second coin peeking out behind the first.
                    c.Ellipse(10.5f, 6.5f, 4.6f, 4.6f, rim);
                    c.Ellipse(10.5f, 6.5f, 3.6f, 3.6f, face);
                    c.Ellipse(7f, 9.5f, 5.8f, 5.8f, rim);
                    c.Ellipse(7f, 9.5f, 4.8f, 4.8f, face);
                    c.PaintEllipse(5.5f, 8f, 2.6f, 2.2f, shine);
                    c.Rect(6, 8, 3, 3, rim);               // square hole
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
                    c.Circle(8f, 10.5f, 5.2f, glass);                 // round flask
                    c.Rect(6, 3, 4, 4, glass);                         // neck
                    for (int y = 8; y < 16; y++)                       // liquid below the shoulder
                        for (int x = 0; x < 16; x++)
                            if (c.IsOpaque(x, y)) c.Set(x, y, y >= 13 ? liquidDark : liquid);
                    c.PaintEllipse(6f, 10f, 1.8f, 1.2f, liquidLight);
                    c.Rect(6, 1, 4, 2, PixelCanvas.Hex("#9a6a3c"));   // cork
                    c.HLine(6, 9, 1, PixelCanvas.Hex("#c49058"));
                    c.Set(5, 9, White); c.Set(5, 10, White);            // glass shine
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
                    // Glowing blue rune and a red wax seal.
                    var rune = PixelCanvas.Hex("#4f8fe8");
                    c.Circle(8f, 7.5f, 2.6f, PixelCanvas.Hex("#9fd0ff"));
                    c.VLine(8, 5, 10, rune); c.HLine(6, 10, 7, rune);
                    c.Rect(7, 11, 2, 2, PixelCanvas.Hex("#c8323a"));
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

        /// <summary>16x16 material icon: bone, ore, essence.</summary>
        static PixelCanvas DrawMaterialIcon(string kind)
        {
            var c = new PixelCanvas(16, 16);
            switch (kind)
            {
                case "bone":
                {
                    var bone = PixelCanvas.Hex("#f0e8d4");
                    var dark = PixelCanvas.Hex("#c8bca0");
                    c.Line(4, 11, 11, 4, bone); c.Line(5, 11, 12, 4, bone); c.Line(4, 10, 11, 3, dark);
                    c.Circle(3.5f, 11f, 2f, bone); c.Circle(5f, 12.5f, 2f, bone);
                    c.Circle(11f, 3.5f, 2f, bone); c.Circle(12.5f, 5f, 2f, bone);
                    break;
                }
                case "ore":
                {
                    c.Ellipse(8, 10, 6.5f, 4.5f, StoneDark);
                    c.PaintEllipse(7.5f, 9.5f, 5.5f, 3.5f, Stone);
                    // Blue crystals growing out of the rock.
                    var blue = PixelCanvas.Hex("#5aa9ff");
                    var light = PixelCanvas.Hex("#bfe4ff");
                    c.Rect(5, 3, 2, 7, blue); c.Set(5, 2, light); c.VLine(5, 3, 8, light);
                    c.Rect(8, 1, 3, 9, blue); c.VLine(8, 1, 8, light); c.Set(9, 0, light);
                    c.Rect(11, 5, 2, 5, blue); c.VLine(11, 5, 8, light);
                    break;
                }
                default: // essence
                {
                    c.Circle(8f, 8f, 6.5f, PixelCanvas.WithAlpha(Magic, 120));
                    // Diamond-shaped crystal.
                    for (int y = 0; y < 12; y++)
                    {
                        int half = y < 5 ? y : 11 - y;
                        c.HLine(8 - half, 8 + half, y + 2, y < 5 ? MagicLight : Magic);
                    }
                    c.VLine(8, 3, 12, MagicCore);
                    c.Set(6, 5, White);
                    break;
                }
            }
            c.Outline(Outline);
            return c;
        }

        /// <summary>24x24 side-menu icon: menu, bag, skill, map, quest, dungeon, raid.</summary>
        static PixelCanvas DrawMenuIcon(string kind)
        {
            var c = new PixelCanvas(24, 24);
            var cream = PixelCanvas.Hex("#f6e7c8");
            switch (kind)
            {
                case "menu":
                    c.Rect(3, 5, 18, 3, cream); c.Rect(3, 11, 18, 3, cream); c.Rect(3, 17, 18, 3, cream);
                    break;
                case "bag":
                    c.Rect(4, 8, 16, 13, Wood); c.HLine(4, 19, 8, WoodLight); c.HLine(4, 19, 20, BarkDark);
                    c.Rect(8, 3, 8, 2, WoodDark); c.VLine(8, 3, 8, WoodDark); c.VLine(15, 3, 8, WoodDark);
                    c.Rect(4, 12, 16, 2, WoodDark); c.Rect(10, 11, 4, 4, Gold);
                    break;
                case "skill":
                    // Spell book with a star.
                    c.Rect(4, 3, 16, 18, PixelCanvas.Hex("#3b5bb8")); c.VLine(5, 3, 20, PixelCanvas.Hex("#27418c"));
                    c.Rect(7, 4, 12, 16, PixelCanvas.Hex("#4a6fd6"));
                    c.VLine(13, 7, 15, Yellow); c.HLine(9, 17, 11, Yellow); c.Rect(12, 10, 3, 3, Yellow);
                    c.Set(10, 8, Yellow); c.Set(16, 8, Yellow); c.Set(10, 14, Yellow); c.Set(16, 14, Yellow);
                    break;
                case "map":
                    c.Rect(3, 4, 18, 16, PixelCanvas.Hex("#e8d3a4"));
                    c.VLine(9, 4, 19, PixelCanvas.Hex("#c7ad78")); c.VLine(15, 4, 19, PixelCanvas.Hex("#c7ad78"));
                    c.Line(5, 16, 12, 9, Red); c.Line(12, 9, 18, 12, Red);
                    c.Rect(16, 6, 3, 3, Red);
                    c.Ellipse(6, 8, 2, 1.5f, Leaf);
                    break;
                case "quest":
                    c.Rect(5, 3, 14, 18, PixelCanvas.Hex("#f2e6c9")); c.HLine(5, 18, 3, WoodLight);
                    c.Rect(10, 6, 4, 8, Red); c.Rect(10, 16, 4, 3, Red);
                    break;
                case "dungeon":
                    // Arched stone door.
                    c.Rect(3, 6, 18, 16, WallStone);
                    c.Ellipse(12, 7, 8, 5, WallStone);
                    c.Rect(7, 9, 10, 13, PixelCanvas.Hex("#1d1410"));
                    c.Ellipse(12, 10, 5, 3.5f, PixelCanvas.Hex("#1d1410"));
                    c.Rect(10, 13, 4, 4, PixelCanvas.Hex("#f0e8d4")); c.Set(11, 14, Outline); c.Set(13, 14, Outline);
                    break;
                case "raid":
                    // Crossed swords over a red emblem.
                    c.Circle(12f, 12f, 10f, PixelCanvas.Hex("#8e2a20"));
                    c.Line(5, 18, 18, 5, Steel); c.Line(6, 18, 19, 5, SteelDark);
                    c.Line(5, 5, 18, 18, Steel); c.Line(5, 6, 18, 19, SteelDark);
                    c.Rect(3, 18, 3, 3, Gold); c.Rect(18, 18, 3, 3, Gold);
                    break;
            }
            c.Outline(Outline);
            return c;
        }

        /// <summary>Blacksmith anvil on a stump with a hammer — the enhancement spot.</summary>
        static PixelCanvas DrawAnvil()
        {
            var c = new PixelCanvas(28, 24);
            c.Rect(8, 14, 12, 9, Bark); c.HLine(8, 19, 14, WoodLight); c.VLine(10, 15, 22, BarkDark); c.VLine(17, 15, 22, BarkDark);
            c.Rect(4, 6, 20, 5, SteelDark); c.HLine(4, 23, 6, Steel);
            c.Rect(1, 7, 4, 3, SteelDark); c.Set(0, 8, SteelDark);
            c.Rect(10, 11, 8, 3, SteelDark);
            c.Line(20, 2, 25, 7, handleColor); c.Rect(18, 0, 5, 3, Steel);
            c.Set(7, 5, Carrot); c.Set(13, 4, Yellow); c.Set(16, 5, Carrot); // sparks
            c.Outline(Outline);
            return c.WithPivot(14, 1.5f);
        }

        static readonly Color32 handleColor = PixelCanvas.Hex("#6b4226");

        /// <summary>12x12 white glyphs drawn inside the big passive tree nodes (tinted by the UI).</summary>
        static PixelCanvas DrawNodeGlyph(string kind)
        {
            var c = new PixelCanvas(12, 12);
            var w = White;
            float D(int x, int y) => Mathf.Sqrt((x - 5.5f) * (x - 5.5f) + (y - 5.5f) * (y - 5.5f));
            switch (kind)
            {
                case "area":
                    for (int y = 0; y < 12; y++)
                        for (int x = 0; x < 12; x++)
                        {
                            float d = D(x, y);
                            if (d >= 4.4f && d < 5.6f || d >= 2.1f && d < 2.9f) c.Set(x, y, w);
                        }
                    break;
                case "dmg":
                    c.Line(2, 8, 9, 1, w); c.Line(3, 8, 10, 1, w); c.Line(3, 9, 10, 2, w);
                    c.Line(1, 6, 5, 10, w);
                    c.Line(1, 10, 2, 9, w); c.Set(0, 11, w);
                    break;
                case "cd":
                    c.HLine(1, 10, 0, w); c.HLine(1, 10, 11, w);
                    c.Line(2, 1, 5, 5, w); c.Line(9, 1, 6, 5, w); c.Line(5, 6, 2, 10, w); c.Line(6, 6, 9, 10, w);
                    c.HLine(3, 8, 10, w); c.HLine(4, 7, 9, w);
                    break;
                case "mastery":
                    c.VLine(5, 0, 11, w); c.VLine(6, 0, 11, w); c.HLine(0, 11, 5, w); c.HLine(0, 11, 6, w);
                    c.Rect(3, 3, 6, 6, w);
                    c.Set(1, 1, w); c.Set(10, 1, w); c.Set(1, 10, w); c.Set(10, 10, w);
                    break;
                case "start":
                    for (int y = 0; y < 12; y++)
                        for (int x = 0; x < 12; x++)
                        {
                            float d = D(x, y);
                            if (d >= 4.4f && d < 5.6f || d < 2f) c.Set(x, y, w);
                        }
                    break;
                case "shield":
                    c.Rect(2, 1, 8, 6, w);
                    for (int y = 7; y <= 10; y++) c.HLine(2 + (y - 6), 9 - (y - 6), y, w);
                    break;
                case "burst":
                    c.Line(5, 0, 5, 11, w); c.Line(0, 5, 11, 5, w); c.Line(1, 1, 10, 10, w); c.Line(10, 1, 1, 10, w);
                    c.Rect(4, 4, 4, 4, w);
                    break;
                case "drop":
                    c.Circle(5.5f, 7.5f, 3.6f, w);
                    for (int y = 1; y <= 5; y++) c.HLine(6 - (y + 1) / 2, 5 + (y + 1) / 2, y, w);
                    break;
                default: // sun
                    c.Circle(5.5f, 5.5f, 2.8f, w);
                    c.VLine(5, 0, 1, w); c.VLine(6, 0, 1, w); c.VLine(5, 10, 11, w); c.VLine(6, 10, 11, w);
                    c.HLine(0, 1, 5, w); c.HLine(0, 1, 6, w); c.HLine(10, 11, 5, w); c.HLine(10, 11, 6, w);
                    c.Set(1, 1, w); c.Set(10, 1, w); c.Set(1, 10, w); c.Set(10, 10, w);
                    break;
            }
            return c;
        }

        /// <summary>Skill gem: red (strength) / blue (intelligence) actives, green / grey supports.</summary>
        static PixelCanvas DrawGem(string id)
        {
            var c = new PixelCanvas(20, 20);
            bool support = id.StartsWith("sup_");
            bool ultimate = id == "blades" || id == "meteor";
            Color32 baseCol = ultimate ? PixelCanvas.Hex("#e0a020")
                : id == "whirl" || id == "slam" || id == "wave" || id == "cry" ? PixelCanvas.Hex("#d9443a")
                : id == "arc" || id == "nova" || id == "frostorb" || id == "thunder" ? PixelCanvas.Hex("#3f7fe0")
                : id == "sup_leech" ? PixelCanvas.Hex("#c0304a")
                : id == "sup_chain" || id == "sup_eff" ? PixelCanvas.Hex("#4a78d8")
                : PixelCanvas.Hex("#4fb34a");
            var light = PixelCanvas.Shade(baseCol, 1.35f);
            var dark = PixelCanvas.Shade(baseCol, 0.65f);
            if (support)
            {
                c.Circle(9.5f, 9.5f, 9f, dark);
                c.Circle(9.5f, 9.5f, 7.5f, baseCol);
                c.Circle(7.5f, 7f, 3f, light);
            }
            else
            {
                // Faceted diamond (awakening skills get a four-pointed gold star behind it).
                if (ultimate)
                {
                    c.HLine(0, 19, 9, dark); c.HLine(0, 19, 10, dark);
                    c.HLine(2, 17, 8, baseCol); c.HLine(2, 17, 11, baseCol);
                }
                for (int y = 0; y < 18; y++)
                {
                    int half = y < 9 ? y : 17 - y;
                    c.HLine(9 - half, 10 + half, y + 1, y < 7 ? light : y < 12 ? baseCol : dark);
                }
                c.VLine(9, 2, 17, PixelCanvas.Shade(baseCol, 1.15f));
            }
            var mark = White;
            switch (id)
            {
                case "wave":
                    c.Circle(9.5f, 9.5f, 5f, mark); c.Circle(7.5f, 9.5f, 4.3f, baseCol); c.Circle(7.5f, 7.5f, 1.5f, baseCol);
                    break;
                case "cry":
                    c.Set(5, 9, mark); c.Set(5, 10, mark);
                    for (int r = 3; r <= 7; r += 2)
                        for (int a = -45; a <= 45; a += 9)
                            c.Set(Mathf.RoundToInt(5f + Mathf.Cos(a * Mathf.Deg2Rad) * r), Mathf.RoundToInt(9.5f + Mathf.Sin(a * Mathf.Deg2Rad) * r), mark);
                    break;
                case "blades":
                    c.Rect(9, 3, 2, 9, mark); c.Rect(6, 12, 8, 1, mark); c.Rect(9, 13, 2, 3, PixelCanvas.Hex("#6b4226")); c.Set(9, 2, mark);
                    break;
                case "frostorb":
                    // Ice orb with a lightning bolt across it.
                    c.Circle(9.5f, 10f, 4.6f, mark);
                    c.Circle(9.5f, 10f, 3.3f, PixelCanvas.Hex("#9fe3ff"));
                    c.Set(8, 8, mark); c.Set(7, 9, mark);
                    c.Line(12, 3, 9, 9, PixelCanvas.Hex("#ffe070")); c.Line(9, 9, 12, 10, PixelCanvas.Hex("#ffe070")); c.Line(12, 10, 8, 16, PixelCanvas.Hex("#ffe070"));
                    break;
                case "thunder":
                    c.Ellipse(9.5f, 5.5f, 5f, 2.3f, mark);
                    c.Line(10, 8, 8, 12, mark); c.Line(8, 12, 11, 12, mark); c.Line(11, 12, 9, 16, mark);
                    break;
                case "meteor":
                    c.Line(3, 3, 9, 9, PixelCanvas.Hex("#ffe070")); c.Line(5, 3, 10, 8, PixelCanvas.Hex("#ffe070")); c.Line(3, 5, 8, 10, PixelCanvas.Hex("#ffe070"));
                    c.Circle(12f, 12f, 3.4f, mark); c.Circle(12.5f, 12.5f, 1.8f, PixelCanvas.Hex("#e8401c"));
                    break;
                case "whirl": c.Circle(9.5f, 9.5f, 3.5f, mark); c.Circle(9.5f, 9.5f, 2f, baseCol); c.Set(13, 7, mark); break;
                case "slam": c.Rect(8, 5, 4, 7, mark); c.Rect(6, 12, 8, 2, mark); break;
                case "arc": c.Line(11, 4, 7, 10, mark); c.Line(7, 10, 12, 10, mark); c.Line(12, 10, 8, 16, mark); break;
                case "nova": c.VLine(9, 4, 15, mark); c.HLine(4, 15, 9, mark); c.Line(6, 6, 13, 13, mark); c.Line(13, 6, 6, 13, mark); break;
                case "sup_dmg": c.VLine(9, 5, 14, mark); c.HLine(5, 14, 9, mark); c.VLine(10, 5, 14, mark); c.HLine(5, 14, 10, mark); break;
                case "sup_aoe": c.Circle(9.5f, 9.5f, 5f, mark); c.Circle(9.5f, 9.5f, 3.6f, baseCol); break;
                case "sup_multi": c.Rect(5, 6, 3, 8, mark); c.Rect(12, 6, 3, 8, mark); break;
                case "sup_eff": c.Ellipse(9.5f, 10.5f, 3.5f, 4f, mark); c.Set(9, 5, mark); break;
                case "sup_leech": c.Ellipse(9.5f, 11f, 3.5f, 3.5f, mark); c.Line(9, 5, 7, 9, mark); c.Line(10, 5, 12, 9, mark); break;
                case "sup_chain": c.Circle(6.5f, 9.5f, 2.5f, mark); c.Circle(12.5f, 9.5f, 2.5f, mark); c.Circle(6.5f, 9.5f, 1.2f, baseCol); c.Circle(12.5f, 9.5f, 1.2f, baseCol); break;
            }
            c.Outline(Outline);
            return c;
        }
    }
}
