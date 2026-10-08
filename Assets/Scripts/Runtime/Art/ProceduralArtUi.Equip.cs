using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
