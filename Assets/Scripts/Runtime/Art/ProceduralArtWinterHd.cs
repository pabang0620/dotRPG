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
    }
}
