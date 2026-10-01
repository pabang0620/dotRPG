using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 32px (density 2) art of the winter village: sprite keys "wnt_*". Twice the detail of the old
    /// snow_* set at the same world size, in the same house style as the town HD art (dark grey-brown
    /// outline, light from the top-left, soft 3-5 step ramps, no single-pixel dithering, clear
    /// silhouettes). Warm-white / light-sky snow, pastel teal shadows, grey-violet cliffs, warm brown
    /// wood, glowing warm windows. The ground itself is painted by <see cref="WinterTerrain"/>; these are
    /// the trees, buildings and props that stand on it.
    /// </summary>
    public static partial class ProceduralArt
    {
        // ---------- Palette ----------

        static readonly Color32 WhSnow = PixelCanvas.Hex("#f7f8f3");   // warm white
        static readonly Color32 WhSnowL = PixelCanvas.Hex("#eef4f4");  // very light sky
        static readonly Color32 WhSnowS = PixelCanvas.Hex("#d2e6e8");  // pastel teal shadow
        static readonly Color32 WhSnowSD = PixelCanvas.Hex("#adcfd5"); // deeper shadow
        static readonly Color32 WhCrest = PixelCanvas.Hex("#ffffff");
        static readonly Color32 WhIce = PixelCanvas.Hex("#d8f1f8");
        static readonly Color32 WhIceD = PixelCanvas.Hex("#a6d4e7");

        static readonly Color32 WhWood = PixelCanvas.Hex("#a8704a");
        static readonly Color32 WhWoodL = PixelCanvas.Hex("#c99068");
        static readonly Color32 WhWoodD = PixelCanvas.Hex("#7a4e34");
        static readonly Color32 WhWoodDD = PixelCanvas.Hex("#583726");

        static readonly Color32 WhLeaf1 = PixelCanvas.Hex("#7cc6b4");  // lit teal foliage
        static readonly Color32 WhLeaf2 = PixelCanvas.Hex("#5aa896");
        static readonly Color32 WhLeaf3 = PixelCanvas.Hex("#3f8579");
        static readonly Color32 WhLeaf4 = PixelCanvas.Hex("#2f665d");  // deepest shade
        static readonly Color32 WhPine1 = PixelCanvas.Hex("#63b199");
        static readonly Color32 WhPine2 = PixelCanvas.Hex("#468f79");
        static readonly Color32 WhPine3 = PixelCanvas.Hex("#316f62");

        static readonly Color32 WhCliff = PixelCanvas.Hex("#9a8ea6");
        static readonly Color32 WhCliffL = PixelCanvas.Hex("#b2a7bd");
        static readonly Color32 WhCliffD = PixelCanvas.Hex("#7a6e88");
        static readonly Color32 WhCliffDD = PixelCanvas.Hex("#5e536b");

        static readonly Color32 WhWarm = PixelCanvas.Hex("#ffd98a");
        static readonly Color32 WhWarmL = PixelCanvas.Hex("#fff1c4");
        static readonly Color32 WhLamp = PixelCanvas.Hex("#3f7d5c");
        static readonly Color32 WhLampD = PixelCanvas.Hex("#2d5c44");
        static readonly Color32 WhTerracotta = PixelCanvas.Hex("#c7764a");
        static readonly Color32 WhBarn = PixelCanvas.Hex("#c9a27a");
        static readonly Color32 WhBarnD = PixelCanvas.Hex("#a3805c");
        static readonly Color32 WhCream = PixelCanvas.Hex("#f1e6cc");
        static readonly Color32 WhHay = PixelCanvas.Hex("#e6c060");
        static readonly Color32 WhHayD = PixelCanvas.Hex("#c49a3e");
        static readonly Color32 WhStone = PixelCanvas.Hex("#a39bb0");
        static readonly Color32 WhStoneD = PixelCanvas.Hex("#7e7690");
        static readonly Color32 WhStoneL = PixelCanvas.Hex("#c2bccc");
        static readonly Color32 WhOutline = PixelCanvas.Hex("#3f363a");

        /// <summary>A density-2 canvas (32 px per world tile).</summary>
        static PixelCanvas WHd(int w, int h) => new PixelCanvas(w, h) { Density = 2 };

        static Color32 WMix(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));
        }

        static bool WSame(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        // ---------- Dispatch ----------

        /// <summary>32px (density 2) art of the winter village: sprite keys "wnt_*".</summary>
        static PixelCanvas DrawWinterHd(string[] p)
        {
            int Arg(int i, int fallback = 0) => p.Length > i && int.TryParse(p[i], out int v) ? v : fallback;
            switch (p[1])
            {
                case "tree": return WinterHdTree(Arg(2), Arg(3));
                case "pine": return WinterHdPine(Arg(2), Arg(3));
                case "bush": return WinterHdBush(Arg(2));
                case "grass": return WinterHdGrass(Arg(2));
                case "rock": return WinterHdRock(Arg(2));
                case "ice": return WinterHdIce(Arg(2));
                case "house": return WinterHdHouse();
                case "barn": return WinterHdBarn();
                case "hay": return WinterHdHay();
                case "well": return WinterHdWell();
                case "bench": return WinterHdBench();
                case "fire": return WinterHdCampfire(Arg(2));
                case "log": return WinterHdLogSeat();
                case "workbench": return WinterHdWorkbench();
                case "lamp": return WinterHdLamp();
                case "mailbox": return WinterHdMailbox();
                case "pot": return WinterHdPot();
                case "woodpile": return WinterHdWoodpile();
                case "barrel": return WinterHdBarrel();
                case "crate": return WinterHdCrate();
                case "sign": return WinterHdSign();
                case "gate": return WinterHdGate();
                case "fence": return WinterHdFence(Arg(2));
                case "bed": return WinterHdGardenBed(Arg(2));
            }
            return null;
        }

        // ---------- Trees ----------

        /// <summary>Round broadleaf tree with a thick snow cap over teal foliage. size 0=large,1=medium,2=small.</summary>
        static PixelCanvas WinterHdTree(int size, int variant)
        {
            int w = size == 0 ? 92 : size == 1 ? 72 : 56;
            int h = size == 0 ? 112 : size == 1 ? 92 : 72;
            var c = WHd(w, h);
            float cx = w / 2f;
            int ground = h - 6;
            c.Ellipse(cx, ground, w * 0.36f, 5.6f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            // Trunk with root flares.
            int tw = size == 0 ? 12 : size == 1 ? 10 : 8, th = size == 0 ? 26 : size == 1 ? 22 : 16;
            int tx = Mathf.RoundToInt(cx - tw / 2f), ty = ground - th;
            c.Rect(tx, ty, tw, th + 2, WhWood);
            c.VLine(tx, ty, ground, WhWoodL);
            c.VLine(tx + tw - 1, ty, ground, WhWoodD);
            for (int k = 1; k <= 3; k++) { c.Set(tx - k, ground, WhWoodD); c.Set(tx + tw - 1 + k, ground, WhWoodD); c.Set(tx - k, ground - 1, WhWoodD); }
            // bark grooves
            c.VLine(tx + tw / 2, ty + 3, ground - 3, WhWoodD);
            // Canopy from overlapping circles.
            float r = w * 0.4f, cy = ty - r * 0.55f;
            var rng = new System.Random(size * 101 + variant * 17 + 3);
            var blobs = new List<Vector3>
            {
                new Vector3(cx, cy, r),
                new Vector3(cx - r * 0.64f, cy + r * 0.3f, r * 0.62f),
                new Vector3(cx + r * 0.62f, cy + r * 0.26f, r * 0.64f),
                new Vector3(cx + (variant % 2 == 0 ? -1f : 1f) * r * 0.3f, cy - r * 0.52f, r * 0.62f),
            };
            if (size == 0)
            {
                blobs.Add(new Vector3(cx + (variant % 2 == 0 ? 1f : -1f) * r * 0.48f, cy - r * 0.22f, r * 0.58f));
                blobs.Add(new Vector3(cx, cy - r * 0.2f, r * 0.86f));
            }
            var canopy = new bool[w, h];
            foreach (var b in blobs)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f - b.x) / b.z, dy = (y + 0.5f - b.y) / b.z;
                        if (dx * dx + dy * dy <= 1f) canopy[x, y] = true;
                    }
            for (int x = 0; x < w; x++)
            {
                int top = -1, bottom = -1;
                for (int y = 0; y < h; y++) if (canopy[x, y]) { if (top < 0) top = y; bottom = y; }
                if (top < 0) continue;
                int span = bottom - top;
                int snow = Mathf.RoundToInt(span * 0.52f + 3f * Mathf.Sin(x * 0.42f + variant * 1.7f) + (rng.NextDouble() < 0.12 ? 3 : 0));
                for (int y = top; y <= bottom; y++)
                {
                    Color32 col;
                    int k = y - top;
                    if (k <= snow)
                    {
                        float t = k / (float)Mathf.Max(1, snow);
                        col = t < 0.5f ? WhCrest : t < 0.78f ? WhSnow : t < 0.92f ? WhSnowL : WhSnowS;
                        if (x > cx + r * 0.32f && t > 0.3f) col = WMix(col, WhSnowS, 0.5f);
                    }
                    else if (k <= snow + 2) col = WhLeaf4;   // shadow under the snow
                    else
                    {
                        float t = (y - top - snow) / (float)Mathf.Max(1, bottom - top - snow);
                        col = t > 0.78f ? WhLeaf4 : x < cx - r * 0.2f ? WhLeaf1 : x < cx + r * 0.1f ? WhLeaf2 : WhLeaf3;
                        if ((x * 3 + y * 5 + variant) % 13 == 0 && col.g < WhLeaf1.g) col = WhLeaf1;   // leaf glints
                    }
                    c.Set(x, y, col);
                }
            }
            // a few snow clumps clinging lower on the foliage
            for (int k = 0; k < (size == 0 ? 4 : 2); k++)
            {
                int sx = (int)(cx + (rng.NextDouble() - 0.5) * r * 1.4f), sy = (int)(cy + r * (0.2 + rng.NextDouble() * 0.6));
                if (sx >= 0 && sx < w && sy >= 0 && sy < h && c.IsOpaque(sx, sy)) c.Ellipse(sx, sy, 3.2f, 1.8f, WhSnowL);
            }
            c.Outline(WhOutline);
            return c.WithPivot(cx, 6f);
        }

        /// <summary>Conifer with snow on every tier. size 0=large,1=small.</summary>
        static PixelCanvas WinterHdPine(int size, int variant)
        {
            int w = size == 0 ? 64 : 44, h = size == 0 ? 116 : 80;
            var c = WHd(w, h);
            float cx = w / 2f;
            int ground = h - 6;
            c.Ellipse(cx, ground, w * 0.42f, 5.2f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            int tw = size == 0 ? 8 : 6, th = size == 0 ? 14 : 10;
            int tx = Mathf.RoundToInt(cx - tw / 2f);
            c.Rect(tx, ground - th, tw, th + 2, WhWood);
            c.VLine(tx + tw - 1, ground - th, ground, WhWoodD);
            c.VLine(tx, ground - th, ground, WhWoodL);
            int tiers = size == 0 ? 5 : 4;
            float tierH = (ground - th - 4) / (float)tiers * 1.28f;
            for (int i = 0; i < tiers; i++)
            {
                float bottom = ground - th + 2 - i * tierH * 0.72f;
                float half = (w / 2f - 2f) * (1f - i * 0.16f);
                float top = bottom - tierH;
                float snowLine = 0.42f + 0.05f * ((variant + i) % 3);
                for (int y = Mathf.FloorToInt(top); y <= Mathf.FloorToInt(bottom); y++)
                {
                    float t = (y - top) / tierH;
                    if (t < 0f) continue;
                    float hw = half * Mathf.Pow(Mathf.Clamp01(t), 0.85f);
                    for (int x = Mathf.FloorToInt(cx - hw); x <= Mathf.CeilToInt(cx + hw) - 1; x++)
                    {
                        float wave = 0.05f * Mathf.Sin(x * 0.7f + i * 2f + variant);
                        Color32 col;
                        if (t < snowLine + wave) col = x > cx + hw * 0.35f ? WhSnowL : WhCrest;
                        else if (t < snowLine + wave + 0.1f) col = WhSnowS;
                        else col = t > 0.86f ? WhPine3 : x < cx - hw * 0.15f ? WhPine1 : WhPine2;
                        c.Set(x, y, col);
                    }
                }
            }
            c.Set(Mathf.FloorToInt(cx), 0, WhCrest); c.Set(Mathf.FloorToInt(cx) - 1, 1, WhCrest); c.Set(Mathf.FloorToInt(cx), 1, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(cx, 6f);
        }

        static PixelCanvas WinterHdBush(int v)
        {
            var c = WHd(44, 32);
            c.Ellipse(22, 26, 18, 4, PixelCanvas.WithAlpha(WhSnowSD, 140));
            c.Circle(14, 20, 10, WhLeaf2); c.Circle(28, 20, 11, WhLeaf2); c.Circle(22, 14, 10, WhLeaf2);
            c.PaintEllipse(22, 24, 18, 5, WhLeaf3);
            c.PaintEllipse(12 + v % 2, 18, 4, 3, WhLeaf1);
            for (int x = 4; x < 40; x++)
            {
                int top = -1;
                for (int y = 0; y < 32; y++) if (c.Get(x, y).a > 0 && c.Get(x, y).g > 120) { top = y; break; }
                if (top < 0) continue;
                int depth = 6 + Mathf.RoundToInt(2.4f * Mathf.Sin((x - 4) * 0.4f + v));
                for (int y = top; y < top + depth; y++) c.Set(x, y, y < top + depth - 2 ? WhSnow : WhSnowS);
            }
            c.Outline(WhOutline);
            return c.WithPivot(22, 5f);
        }

        static PixelCanvas WinterHdGrass(int v)
        {
            var c = WHd(18, 16);
            var blade = PixelCanvas.Hex("#8cc9b4");
            var bladeD = PixelCanvas.Hex("#5f9d88");
            c.Ellipse(9, 13, 8, 2.8f, WhSnowL);
            c.Line(4, 12, 2, 4, blade); c.Line(8, 12, 8, 2 + v % 2, bladeD); c.Line(12, 12, 14, 6, blade);
            if (v % 2 == 0) c.Line(10, 12, 12, 4, bladeD);
            c.HLine(2, 15, 12, WhSnow);
            return c.WithPivot(9, 2f);
        }

        static PixelCanvas WinterHdRock(int v)
        {
            var c = WHd(36, 28);
            c.Ellipse(18, 24, 16, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Ellipse(18, 17, 15f - v % 2, 10, WhStone);
            c.PaintEllipse(22, 20, 12, 6, WhStoneD);
            c.PaintEllipse(12, 12, 5, 3, WhStoneL);
            c.Ellipse(17, 9.6f, 12f - v % 2, 4.8f, WhSnow);
            c.PaintEllipse(15, 8, 7, 2.5f, WhCrest);
            c.HLine(8, 26 - v % 2, 12, WhSnowL);
            c.Outline(WhOutline);
            return c.WithPivot(18, 4f);
        }

        static PixelCanvas WinterHdIce(int v)
        {
            var c = WHd(28, 16);
            c.Ellipse(14, 8, 12f - v % 2, 5, WhIce);
            c.HLine(4, 22 - v % 2, 10, WhIceD);
            c.HLine(6, 18, 4, WhCrest);
            c.Set(8 + v % 3, 6, WhCrest); c.Set(16, 7, WhCrest);
            c.Outline(PixelCanvas.WithAlpha(WhIceD, 200));
            return c;
        }

        // ---------- Buildings ----------

        /// <summary>
        /// Snowy front-facing roof: shingles, a thick snow layer with a wavy lower edge, dark bargeboards,
        /// a wooden fascia and icicles.
        /// </summary>
        static void WSnowRoof(PixelCanvas c, int cx, int top, int eave, int topHalf, int eaveHalf, Color32 roof)
        {
            var roofDark = PixelCanvas.Shade(roof, 0.72f);
            var roofLight = PixelCanvas.Shade(roof, 1.18f);
            int HalfAt(int y) => Mathf.RoundToInt(Mathf.Lerp(topHalf, eaveHalf, (y - top) / (float)Mathf.Max(1, eave - top)));
            for (int y = top; y <= eave; y++)
            {
                int hw = HalfAt(y);
                for (int x = cx - hw; x <= cx + hw; x++)
                {
                    int row = (y - top) % 4;
                    var col = row == 3 ? roofDark : roof;
                    if (row == 0 && ((x + (y / 4) * 2) % 6 == 0)) col = roofLight;
                    c.Set(x, y, col);
                }
            }
            int snowTo = top + Mathf.RoundToInt((eave - top) * 0.72f);
            for (int x = cx - eaveHalf; x <= cx + eaveHalf; x++)
            {
                int bottom = snowTo + Mathf.RoundToInt(3.5f * Mathf.Sin(x * 0.5f)) - (x > cx + eaveHalf / 2 ? 2 : 0);
                for (int y = top; y <= bottom && y <= eave; y++)
                {
                    if (Mathf.Abs(x - cx) > HalfAt(y)) continue;
                    var col = x > cx + 3 ? WhSnowL : WhSnow;
                    if (y >= bottom - 2) col = WhSnowS;
                    if (y <= top + 1) col = WhCrest;
                    c.Set(x, y, col);
                }
            }
            for (int y = top; y <= eave; y++)
            {
                int hw = HalfAt(y);
                c.Set(cx - hw, y, WhWoodDD); c.Set(cx - hw - 1, y, WhWoodDD);
                c.Set(cx + hw, y, WhWoodDD); c.Set(cx + hw + 1, y, WhWoodDD);
            }
            c.HLine(cx - eaveHalf - 1, cx + eaveHalf + 1, eave + 1, WhWood);
            c.HLine(cx - eaveHalf - 1, cx + eaveHalf + 1, eave + 2, WhWoodDD);
            for (int x = cx - eaveHalf; x <= cx + eaveHalf; x++)
            {
                if ((x * 3) % 7 < 3) c.Set(x, eave + 1, WhSnow);
                if ((x * 5) % 13 == 0) { c.Set(x, eave + 3, WhIce); c.Set(x, eave + 4, WhIce); c.Set(x, eave + 5, WhIceD); }
            }
        }

        /// <summary>Log cabin (3 tiles wide): warm log walls, glowing windows, wreath, deep snow roof, chimney.</summary>
        static PixelCanvas WinterHdHouse()
        {
            var c = WHd(104, 128);
            int x0 = 8, x1 = 95, y0 = 68, y1 = 116;
            for (int y = y0; y <= y1; y++)
            {
                int k = (y - y0) % 8;
                c.HLine(x0, x1, y, k < 2 ? WhWoodL : k >= 6 ? WhWoodD : WhWood);
            }
            // corner log ends
            for (int y = y0; y <= y1; y += 8)
            {
                c.Rect(x0 - 4, y, 6, 6, WhWoodL); c.Set(x0 - 2, y + 2, WhWoodD); c.Ellipse(x0 - 1, y + 2, 2, 2, WhWoodD);
                c.Rect(x1 - 1, y, 6, 6, WhWoodL); c.Ellipse(x1 + 2, y + 2, 2, 2, WhWoodD);
            }
            c.Rect(x0 - 2, 116, x1 - x0 + 5, 6, PixelCanvas.Hex("#9aa0b0"));
            c.HLine(x0 - 2, x1 + 2, 116, PixelCanvas.Hex("#c2c7d4"));
            // Door with a wreath and a stone step.
            c.Rect(42, 84, 20, 34, WhWoodDD);
            c.VLine(52, 84, 116, PixelCanvas.Hex("#3e2618"));
            c.HLine(42, 61, 84, WhWoodD);
            c.Set(58, 102, Gold); c.Set(58, 103, Gold);
            c.Circle(52, 92, 5.4f, WhPine2); c.Circle(52, 92, 2.6f, WhWoodDD);
            c.Set(48, 90, Red); c.Set(56, 94, Red); c.Set(52, 96, Red); c.Set(50, 95, Red);
            c.Rect(38, 118, 28, 6, PixelCanvas.Hex("#b4b8c6")); c.HLine(38, 65, 118, WhSnow);
            // Windows with warm light and snowy sills.
            foreach (int wx in new[] { 16, 72 })
            {
                c.Rect(wx, 80, 18, 18, WhWarm);
                c.Rect(wx + 2, 82, 6, 6, WhWarmL);
                c.VLine(wx + 8, 80, 97, WhWoodDD); c.HLine(wx, wx + 17, 88, WhWoodDD);
                c.Rect(wx - 2, 98, 22, 4, WhSnow); c.HLine(wx - 2, wx + 19, 100, WhSnowS);
                // flower box
                c.Rect(wx - 2, 102, 22, 4, WhWoodD); c.Set(wx + 4, 100, Red); c.Set(wx + 12, 100, Red); c.Set(wx + 8, 100, WhWarm);
            }
            // Lantern beside the door.
            c.Rect(66, 86, 6, 8, WhWarm); c.Set(68, 84, WhLampD); c.Rect(66, 86, 6, 1, WhLampD);
            // Chimney behind the roof with a snow cap and a curl of smoke.
            c.Rect(70, 10, 14, 28, PixelCanvas.Hex("#8e93a3"));
            c.VLine(82, 10, 37, PixelCanvas.Hex("#6e7384"));
            c.Rect(68, 6, 18, 6, WhSnow); c.HLine(68, 85, 10, WhSnowS);
            c.Circle(76, 3f, 3f, PixelCanvas.WithAlpha(WhSnowSD, 190));
            c.Circle(82, 1f, 2f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            // Roof.
            WSnowRoof(c, 52, 18, 66, 16, 52, PixelCanvas.Hex("#a44a3a"));
            c.HLine(x0, x1, 74, WhWoodDD); // eave shadow on the wall
            c.Outline(WhOutline);
            return c.WithPivot(52, 7f);
        }

        /// <summary>Farm building (5 tiles wide): board walls, big braced doors, hay loft, snowy gambrel roof, weathervane.</summary>
        static PixelCanvas WinterHdBarn()
        {
            var c = WHd(168, 164);
            int x0 = 8, x1 = 159, y0 = 76, y1 = 154;
            for (int x = x0; x <= x1; x++)
            {
                var col = (x - x0) % 12 == 0 ? WhBarnD : (x - x0) % 12 == 1 ? PixelCanvas.Shade(WhBarn, 1.08f) : WhBarn;
                c.VLine(x, y0, y1, col);
            }
            c.Rect(x0 - 2, 154, x1 - x0 + 5, 6, PixelCanvas.Hex("#9aa0b0"));
            // Big double door with X braces.
            c.Rect(56, 104, 56, 52, WhWoodD);
            c.VLine(82, 104, 154, WhWoodDD); c.VLine(84, 104, 154, WhWoodDD);
            c.Line(58, 106, 80, 152, WhCream); c.Line(58, 152, 80, 106, WhCream);
            c.Line(86, 106, 108, 152, WhCream); c.Line(86, 152, 108, 106, WhCream);
            c.Rect(56, 104, 56, 4, WhCream); c.Rect(56, 152, 56, 4, WhCream);
            c.VLine(56, 104, 154, WhCream); c.VLine(111, 104, 154, WhCream);
            // Hay loft.
            c.Rect(68, 80, 32, 18, WhWoodDD);
            c.Rect(70, 88, 28, 10, WhHay); c.HLine(70, 96, 88, PixelCanvas.Shade(WhHay, 1.1f)); c.Set(76, 90, WhHayD); c.Set(88, 92, WhHayD);
            c.Rect(66, 78, 36, 2, WhCream); c.VLine(66, 78, 98, WhCream); c.VLine(100, 78, 98, WhCream);
            // Small windows.
            foreach (int wx in new[] { 20, 132 })
            {
                c.Rect(wx, 112, 16, 16, WhWarm);
                c.VLine(wx + 8, 112, 127, WhWoodDD); c.HLine(wx, wx + 15, 120, WhWoodDD);
                c.Rect(wx - 2, 128, 20, 4, WhSnow); c.HLine(wx - 2, wx + 17, 130, WhSnowS);
            }
            // Roof: dark red shingles under a thick snow layer.
            WSnowRoof(c, 84, 18, 72, 24, 82, PixelCanvas.Hex("#8a3b32"));
            c.HLine(x0, x1, 80, PixelCanvas.WithAlpha(WhWoodDD, 200));
            // Weathervane.
            c.VLine(84, 2, 16, WhOutline);
            c.HLine(78, 90, 6, WhOutline);
            c.Rect(86, 0, 6, 4, PixelCanvas.Hex("#d9a441"));
            c.Outline(WhOutline);
            return c.WithPivot(84, 7f);
        }

        // ---------- Props ----------

        static PixelCanvas WinterHdHay()
        {
            var c = WHd(36, 32);
            c.Ellipse(18, 28, 16, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Rect(2, 10, 32, 18, WhHay);
            for (int x = 4; x < 34; x += 6) c.VLine(x, 14, 26, WhHayD);
            c.HLine(2, 33, 18, WhWoodD);
            c.Rect(2, 6, 32, 6, WhSnow); c.HLine(2, 33, 10, WhSnowS);
            c.Set(6, 4, WhCrest); c.Set(24, 4, WhCrest);
            c.Outline(WhOutline);
            return c.WithPivot(18, 5f);
        }

        static PixelCanvas WinterHdWell()
        {
            var c = WHd(64, 76);
            c.Ellipse(32, 68, 26, 5, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Ellipse(32, 58, 26, 14, WhStoneD);
            c.Ellipse(32, 56, 24, 12, WhStone);
            c.Ellipse(32, 54, 16, 7, PixelCanvas.Hex("#3d85c3"));
            c.PaintEllipse(28, 52, 6, 2, PixelCanvas.Hex("#80c2ee"));
            for (int a = 0; a < 12; a++) { double ang = a / 12.0 * 6.28; c.Set(32 + (int)(20 * Mathf.Cos((float)ang)), 58 + (int)(9 * Mathf.Sin((float)ang)), WhStoneD); }
            c.Ellipse(32, 47, 24, 3.2f, WhSnow);
            // Posts + winch.
            c.Rect(8, 14, 4, 42, WhWoodD); c.Rect(52, 14, 4, 42, WhWoodD);
            c.HLine(12, 51, 26, WhWoodDD);
            c.VLine(32, 26, 38, PixelCanvas.Hex("#c8b89a"));
            c.Rect(28, 38, 10, 8, WhWood); c.HLine(28, 37, 38, WhSnow);
            // Little snowy roof.
            for (int y = 4; y <= 18; y++)
            {
                int hw = 12 + (y - 4) * 20 / 14;
                c.HLine(32 - hw, 31 + hw, y, y > 14 ? WhSnowS : y > 8 ? WhSnowL : WhSnow);
            }
            c.HLine(2, 61, 20, WhWood);
            for (int x = 6; x < 60; x += 8) { c.Set(x, 22, WhIce); c.Set(x, 23, WhIceD); }
            c.Outline(WhOutline);
            return c.WithPivot(32, 7f);
        }

        static PixelCanvas WinterHdBench()
        {
            var c = WHd(64, 36);
            c.Ellipse(32, 32, 26, 3, PixelCanvas.WithAlpha(WhSnowSD, 130));
            c.Rect(4, 10, 56, 6, WhWood); c.HLine(4, 59, 10, WhWoodL);
            c.Rect(4, 20, 56, 6, WhWood); c.HLine(4, 59, 25, WhWoodD);
            c.Rect(8, 26, 4, 8, WhWoodD); c.Rect(52, 26, 4, 8, WhWoodD);
            c.Rect(4, 6, 56, 4, WhSnow); c.HLine(4, 59, 9, WhSnowS);
            c.Rect(6, 18, 52, 2, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(32, 3f);
        }

        /// <summary>Campfire: stone ring with snow caps, crossed logs, flames (frame 0-2).</summary>
        static PixelCanvas WinterHdCampfire(int frame)
        {
            var c = WHd(48, 48);
            var stone = PixelCanvas.Hex("#9ea3b3");
            var stoneD = PixelCanvas.Hex("#787d8d");
            c.Ellipse(24, 40, 20, 6, PixelCanvas.WithAlpha(WhSnowSD, 120));
            c.Ellipse(24, 38, 14, 4.8f, PixelCanvas.Hex("#5a4a44"));
            c.Line(12, 40, 34, 32, WhWoodD); c.Line(12, 32, 34, 40, WhWood);
            c.Line(14, 40, 36, 32, WhWoodD);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                float sx = 24 + Mathf.Cos(a) * 18, sy = 38 + Mathf.Sin(a) * 6.8f;
                c.Ellipse(sx, sy, 4.4f, 3.2f, stone);
                c.PaintEllipse(sx - 1, sy - 1.5f, 2.5f, 1.5f, WhStoneL);
                c.PaintEllipse(sx + 1, sy + 1.5f, 2.5f, 1.5f, stoneD);
                c.Ellipse(sx, sy - 2.5f, 2.5f, 1.2f, WhSnow);
            }
            float sway = frame == 0 ? 0f : frame == 1 ? 2f : -2f;
            int fh = frame == 1 ? 24 : frame == 2 ? 20 : 22;
            for (int y = 0; y < fh; y++)
            {
                float t = y / (float)fh;
                float hw = 9f * (1f - t) * (0.85f + 0.15f * Mathf.Sin(y * 0.5f + frame));
                float fx = 24 + sway * t * 2f;
                int yy = 34 - y;
                for (int x = Mathf.FloorToInt(fx - hw); x <= Mathf.CeilToInt(fx + hw); x++)
                {
                    float e = Mathf.Abs(x + 0.5f - fx) / Mathf.Max(0.5f, hw);
                    var col = e > 0.7f ? PixelCanvas.Hex("#ff7a24") : e > 0.35f || t > 0.6f ? PixelCanvas.Hex("#ffc84a") : PixelCanvas.Hex("#fff1b8");
                    c.Set(x, yy, col);
                }
            }
            c.Set(22 + frame, 34 - fh - 1, PixelCanvas.Hex("#ffc84a"));
            return c.WithPivot(24, 7f);
        }

        static PixelCanvas WinterHdLogSeat()
        {
            var c = WHd(48, 28);
            c.Ellipse(24, 24, 20, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            c.Rect(8, 8, 36, 16, WhWood);
            c.HLine(8, 43, 22, WhWoodD);
            for (int x = 14; x < 42; x += 8) c.VLine(x, 12, 20, WhWoodD);
            c.Ellipse(8, 16, 6, 8, PixelCanvas.Hex("#dcb488"));
            c.Ellipse(8, 16, 3.2f, 4.4f, PixelCanvas.Hex("#b88a5c"));
            c.Set(8, 16, WhWoodD);
            c.Rect(10, 4, 34, 6, WhSnow); c.HLine(12, 43, 8, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(24, 4f);
        }

        static PixelCanvas WinterHdWorkbench()
        {
            var c = WHd(72, 52);
            c.Rect(4, 16, 64, 10, WhWoodL); c.HLine(4, 67, 16, PixelCanvas.Shade(WhWoodL, 1.08f)); c.HLine(4, 67, 25, WhWoodD);
            c.Rect(8, 26, 6, 22, WhWoodD); c.Rect(58, 26, 6, 22, WhWoodD);
            c.Rect(8, 38, 56, 4, WhWood);
            // Saw, hammer, plane, vice.
            c.Rect(16, 10, 22, 6, PixelCanvas.Hex("#c2c7d4")); c.HLine(16, 36, 14, PixelCanvas.Hex("#8e93a3"));
            for (int x = 18; x < 36; x += 3) c.Set(x, 16, PixelCanvas.Hex("#8e93a3"));
            c.Rect(38, 10, 6, 6, WhWood); c.Rect(40, 6, 3, 6, PixelCanvas.Hex("#8e93a3"));
            c.Rect(48, 12, 12, 4, WhWoodD); c.Rect(56, 6, 6, 8, PixelCanvas.Hex("#8e93a3"));
            c.Rect(4, 8, 8, 8, PixelCanvas.Hex("#6e7384"));
            c.Set(24, 26, WhWoodL); c.Set(40, 28, WhWoodL);
            c.HLine(4, 14, 14, WhSnow); c.HLine(60, 67, 14, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(36, 4f);
        }

        static PixelCanvas WinterHdLamp()
        {
            var c = WHd(28, 92);
            c.Ellipse(14, 88, 8, 2.6f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Rect(8, 80, 12, 8, WhLampD); c.HLine(8, 19, 80, WhLamp);
            c.Rect(12, 28, 4, 52, WhLamp); c.VLine(15, 28, 79, WhLampD);
            c.Rect(10, 44, 8, 2, WhLamp);
            c.Rect(6, 24, 16, 4, WhLampD);
            c.Rect(6, 10, 16, 14, WhWarm); c.Rect(8, 12, 6, 6, WhWarmL);
            c.VLine(6, 10, 23, WhLampD); c.VLine(21, 10, 23, WhLampD); c.VLine(14, 10, 23, WhLampD);
            c.Rect(4, 4, 20, 6, WhLamp); c.HLine(8, 19, 2, WhLampD);
            c.HLine(4, 23, 4, WhSnow); c.HLine(8, 19, 2, WhSnow); c.Set(12, 0, WhCrest); c.Set(14, 0, WhCrest); c.Set(13, 1, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(14, 4f);
        }

        static PixelCanvas WinterHdMailbox()
        {
            var c = WHd(28, 44);
            var red = PixelCanvas.Hex("#c8463c");
            c.Rect(12, 20, 4, 22, WhWood); c.VLine(15, 20, 41, WhWoodD);
            c.Rect(4, 8, 20, 14, red); c.Ellipse(14, 8, 10, 4, red);
            c.HLine(4, 23, 8, PixelCanvas.Shade(red, 0.75f));
            c.Rect(6, 12, 10, 2, PixelCanvas.Shade(red, 0.7f));
            c.Rect(22, 6, 4, 8, Yellow);
            c.Rect(4, 4, 20, 4, WhSnow); c.Ellipse(14, 5, 10, 3, WhSnow); c.HLine(4, 23, 8, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(14, 3f);
        }

        static PixelCanvas WinterHdPot()
        {
            var c = WHd(28, 28);
            c.Rect(6, 16, 16, 10, WhTerracotta); c.Rect(4, 14, 20, 4, PixelCanvas.Shade(WhTerracotta, 1.12f));
            c.VLine(20, 16, 24, PixelCanvas.Shade(WhTerracotta, 0.78f));
            c.Circle(14, 10, 7.2f, WhPine2); c.PaintEllipse(12, 8, 4, 3, WhPine1);
            c.Set(10, 10, Red); c.Set(16, 12, Red); c.Set(14, 6, Red);
            c.HLine(8, 19, 4, WhSnow); c.Set(12, 2, WhCrest); c.Set(14, 3, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(14, 3f);
        }

        static PixelCanvas WinterHdWoodpile()
        {
            var c = WHd(60, 40);
            var ring = PixelCanvas.Hex("#dcb488");
            var ringD = PixelCanvas.Hex("#b88a5c");
            c.Ellipse(30, 36, 26, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            for (int row = 0; row < 3; row++)
            {
                int n = 4 - row;
                for (int i = 0; i < n; i++)
                {
                    float x = 12 + i * 12 + row * 6, y = 30 - row * 10;
                    c.Circle(x, y, 6, WhWood);
                    c.Circle(x, y, 4, ring);
                    c.Circle(x, y, 1.6f, ringD);
                    c.PaintEllipse(x - 1.5f, y - 1.5f, 2, 1.4f, PixelCanvas.Hex("#eccca0"));
                }
            }
            c.Ellipse(30, 10, 14, 4, WhSnow); c.Ellipse(18, 20, 8, 2.4f, WhSnow); c.Ellipse(44, 20, 8, 2.4f, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(30, 4f);
        }

        static PixelCanvas WinterHdBarrel()
        {
            var c = WHd(28, 36);
            c.Ellipse(14, 32, 12, 2.8f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            c.Rect(4, 10, 20, 22, WhWood);
            c.VLine(4, 12, 30, WhWoodL); c.VLine(23, 12, 30, WhWoodD);
            c.HLine(4, 23, 14, WhLampD); c.HLine(4, 23, 26, WhLampD);
            c.Rect(6, 10, 16, 4, WhWoodD);
            c.Ellipse(14, 10, 10, 3.6f, WhSnow); c.HLine(6, 21, 12, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(14, 4f);
        }

        static PixelCanvas WinterHdCrate()
        {
            var c = WHd(32, 32);
            c.Rect(2, 8, 28, 22, WhWood);
            c.HLine(2, 29, 18, WhWoodD); c.VLine(2, 8, 29, WhWoodL); c.VLine(29, 8, 29, WhWoodD);
            c.Rect(2, 8, 28, 2, WhWoodL); c.Rect(2, 28, 28, 2, WhWoodD);
            c.Line(4, 10, 27, 27, WhWoodD); c.Line(27, 10, 4, 27, WhWoodD);
            c.Rect(2, 4, 28, 5, WhSnow); c.HLine(2, 29, 8, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }

        static PixelCanvas WinterHdSign()
        {
            var c = WHd(32, 36);
            c.Rect(14, 20, 4, 14, WhWoodD);
            c.Rect(2, 6, 28, 16, WhWood);
            c.HLine(2, 29, 20, WhWoodDD);
            c.HLine(6, 25, 12, WhWoodDD); c.HLine(6, 19, 16, WhWoodDD);
            c.Rect(2, 6, 28, 2, WhWoodL);
            c.Rect(2, 2, 28, 5, WhSnow); c.HLine(2, 29, 6, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }

        /// <summary>North exit: a snowy timber arch with lanterns, closed by a drift and a barrier.</summary>
        static PixelCanvas WinterHdGate()
        {
            var c = WHd(128, 108);
            c.Rect(12, 20, 12, 84, WhWood); c.VLine(12, 20, 103, WhWoodL); c.VLine(23, 20, 103, WhWoodD);
            c.Rect(104, 20, 12, 84, WhWood); c.VLine(104, 20, 103, WhWoodL); c.VLine(115, 20, 103, WhWoodD);
            c.Rect(0, 12, 128, 10, WhWood); c.HLine(0, 127, 20, WhWoodDD);
            c.Rect(8, 30, 112, 6, WhWoodD);
            c.Rect(0, 6, 128, 8, WhSnow); c.HLine(0, 127, 12, WhSnowS);
            c.Rect(10, 28, 108, 2, WhSnow);
            foreach (int lx in new[] { 36, 88 })
            {
                c.VLine(lx + 1, 36, 42, WhOutline);
                c.Rect(lx - 3, 44, 8, 8, WhWarm); c.Set(lx, 46, WhWarmL); c.Rect(lx - 3, 44, 8, 1, WhLampD);
            }
            // Barrier and drift closing the path for now.
            c.Rect(28, 76, 72, 6, WhWoodL); c.Rect(28, 88, 72, 6, WhWoodL);
            c.HLine(28, 99, 80, WhWoodD); c.HLine(28, 99, 92, WhWoodD);
            c.Ellipse(64, 100, 44, 8, WhSnow); c.Ellipse(64, 102, 40, 5, WhSnowL); c.PaintEllipse(64, 98, 30, 4, WhCrest);
            c.Outline(WhOutline);
            return c.WithPivot(64, 7f);
        }

        /// <summary>Wooden fence with snow on rails and posts (mask 1=left,2=right,4=up neighbour).</summary>
        static PixelCanvas WinterHdFence(int mask)
        {
            var c = WHd(32, 36);
            bool left = (mask & 1) != 0, right = (mask & 2) != 0, up = (mask & 4) != 0;
            if (left) { c.Rect(0, 14, 15, 4, WhWood); c.Rect(0, 24, 15, 4, WhWood); c.HLine(0, 13, 12, WhSnow); c.HLine(0, 13, 22, WhSnow); }
            if (right) { c.Rect(17, 14, 15, 4, WhWood); c.Rect(17, 24, 15, 4, WhWood); c.HLine(18, 31, 12, WhSnow); c.HLine(18, 31, 22, WhSnow); }
            if (up) { c.Rect(14, 0, 4, 10, WhWood); c.VLine(14, 0, 9, WhWoodL); }
            c.Rect(12, 8, 8, 24, WhWood);
            c.VLine(18, 10, 31, WhWoodD);
            c.VLine(12, 8, 31, WhWoodL);
            c.Rect(12, 4, 8, 4, WhSnow); c.Ellipse(16, 4, 5, 2, WhSnow); c.Set(15, 2, WhCrest);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }

        /// <summary>Raised garden bed under snow with winter cabbages peeking out.</summary>
        static PixelCanvas WinterHdGardenBed(int v)
        {
            var c = WHd(32, 28);
            c.Rect(2, 10, 28, 16, WhWoodD); c.HLine(2, 29, 10, WhWood);
            c.Rect(4, 12, 24, 12, PixelCanvas.Hex("#6a5048"));
            c.Rect(4, 10, 24, 6, WhSnow); c.HLine(4, 27, 16, WhSnowS);
            var cab = PixelCanvas.Hex("#7fae7a");
            var cabP = PixelCanvas.Hex("#a07bb0");
            c.Circle(10 + v % 2, 16, 4.4f, v % 2 == 0 ? cab : cabP); c.Set(10 + v % 2, 14, WhSnow);
            c.Circle(22 - v % 2, 17, 4f, v % 2 == 0 ? cabP : cab); c.Set(22 - v % 2, 15, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }
    }
}
