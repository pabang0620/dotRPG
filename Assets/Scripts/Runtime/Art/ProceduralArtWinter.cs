using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Winter forest village art (keys "snow_*"). Warm white snow with pastel teal shadows, grey-violet
    /// and beige cliff strata, warm brown wood, a clear mid-blue river, dark grey-brown outlines.
    /// Ground and path come from large seamless patterns so no tile border or repeated square shows;
    /// small marks on the snow are sparse decorations placed by the world builder.
    /// </summary>
    public static partial class ProceduralArt
    {
        // ---------- Palette ----------

        static readonly Color32 SnowW = PixelCanvas.Hex("#f7f8f3");   // warm white
        static readonly Color32 SnowL = PixelCanvas.Hex("#e8f2f4");   // very light sky
        static readonly Color32 SnowS = PixelCanvas.Hex("#cde5e5");   // pastel teal shadow
        static readonly Color32 SnowSD = PixelCanvas.Hex("#a9ccd2");  // deeper shadow
        static readonly Color32 SnowBG = PixelCanvas.Hex("#b7c5d4");  // light blue-grey
        static readonly Color32 IceC = PixelCanvas.Hex("#d8f1f8");
        static readonly Color32 IceD = PixelCanvas.Hex("#a6d4e7");
        static readonly Color32 CliffP = PixelCanvas.Hex("#9a8ea6");  // grey-violet
        static readonly Color32 CliffPL = PixelCanvas.Hex("#b2a7bd");
        static readonly Color32 CliffPD = PixelCanvas.Hex("#7a6e88");
        static readonly Color32 CliffPDD = PixelCanvas.Hex("#5e536b");
        static readonly Color32 CliffBe = PixelCanvas.Hex("#d6c5aa");  // beige strata
        static readonly Color32 CliffBeD = PixelCanvas.Hex("#b6a48b");
        static readonly Color32 RiverB = PixelCanvas.Hex("#4f9fdc");
        static readonly Color32 RiverL = PixelCanvas.Hex("#80c2ee");
        static readonly Color32 RiverD = PixelCanvas.Hex("#3d85c3");
        static readonly Color32 RiverFoam = PixelCanvas.Hex("#e4f6fc");
        static readonly Color32 WWood = PixelCanvas.Hex("#a8704a");
        static readonly Color32 WWoodL = PixelCanvas.Hex("#c99068");
        static readonly Color32 WWoodD = PixelCanvas.Hex("#7a4e34");
        static readonly Color32 WWoodDD = PixelCanvas.Hex("#583726");
        static readonly Color32 Leaf1 = PixelCanvas.Hex("#6fbcab");
        static readonly Color32 Leaf2 = PixelCanvas.Hex("#519f91");
        static readonly Color32 Leaf3 = PixelCanvas.Hex("#3b8078");
        static readonly Color32 Pine1 = PixelCanvas.Hex("#5aa58e");
        static readonly Color32 Pine2 = PixelCanvas.Hex("#408975");
        static readonly Color32 Pine3 = PixelCanvas.Hex("#2f6c61");
        static readonly Color32 CobS = PixelCanvas.Hex("#b7bbc8");
        static readonly Color32 CobL = PixelCanvas.Hex("#d3d6df");
        static readonly Color32 CobD = PixelCanvas.Hex("#9195a6");
        static readonly Color32 CobGap = PixelCanvas.Hex("#e0e7ee");
        static readonly Color32 WOutline = PixelCanvas.Hex("#443a3e");
        static readonly Color32 LampG = PixelCanvas.Hex("#3f7d5c");
        static readonly Color32 LampGD = PixelCanvas.Hex("#2d5c44");
        static readonly Color32 Warm = PixelCanvas.Hex("#ffd98a");
        static readonly Color32 WarmL = PixelCanvas.Hex("#fff1c4");
        static readonly Color32 Terracotta = PixelCanvas.Hex("#c7764a");
        static readonly Color32 BarnWall = PixelCanvas.Hex("#c9a27a");
        static readonly Color32 BarnWallD = PixelCanvas.Hex("#a3805c");
        static readonly Color32 Cream = PixelCanvas.Hex("#f1e6cc");
        static readonly Color32 Hay = PixelCanvas.Hex("#e6c060");
        static readonly Color32 HayD = PixelCanvas.Hex("#c49a3e");

        static int Mod(int a, int n) => (a % n + n) % n;

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        static Color32 Mix(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));
        }

        /// <summary>Smooth bump for irregular edges: 0 at both tile ends, so neighbouring tiles join.</summary>
        static int EdgeBump(int i, float seed, float amp)
        {
            float t = i / 15f;
            return Mathf.RoundToInt(amp * Mathf.Sin(t * Mathf.PI) * (0.55f + 0.45f * Mathf.Sin(seed + i * 1.13f)));
        }

        // ---------- Seamless patterns ----------

        const int SnowPatternSize = 128;
        static Color32[] snowField, cobbles;

        /// <summary>
        /// 128x128 snow field: warm white with a few very soft, wide drift rims (light sky blue) and
        /// rare sparkles. Low contrast and much larger than a tile, so the ground reads as one surface.
        /// </summary>
        static Color32[] SnowField()
        {
            if (snowField != null) return snowField;
            const int N = SnowPatternSize;
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = SnowW;
            var rng = new System.Random(4471);
            for (int k = 0; k < 6; k++)
            {
                int cx = rng.Next(0, N), cy = rng.Next(0, N);
                float rx = rng.Next(10, 22), ry = rng.Next(5, 9);
                for (int y = -(int)ry; y <= ry; y++)
                    for (int x = -(int)rx; x <= rx; x++)
                    {
                        float d = x * x / (rx * rx) + y * y / (ry * ry);
                        if (d > 1f || d < 0.8f || y <= ry * 0.35f) continue; // only a short lower rim of each drift
                        px[Mod(cy + y, N) * N + Mod(cx + x, N)] = SnowL;
                    }
            }
            for (int k = 0; k < 18; k++) px[rng.Next(0, N) * N + rng.Next(0, N)] = White;
            snowField = px;
            return px;
        }

        /// <summary>
        /// 128x128 cobblestone path: round stones of different sizes and tones packed irregularly,
        /// light snow showing between them, some stones with a little snow on top.
        /// </summary>
        static Color32[] CobblePattern()
        {
            if (cobbles != null) return cobbles;
            const int N = SnowPatternSize;
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = CobGap;
            var rng = new System.Random(911);
            const int cell = 8;
            for (int gy = 0; gy < N / cell; gy++)
                for (int gx = 0; gx < N / cell; gx++)
                {
                    bool big = rng.NextDouble() < 0.22;
                    float cx = gx * cell + 4 + (gy % 2) * 4 + rng.Next(-2, 3);
                    float cy = gy * cell + 4 + rng.Next(-2, 3);
                    float rx = big ? 4.6f + (float)rng.NextDouble() * 1.2f : 2.7f + (float)rng.NextDouble() * 1.3f;
                    float ry = big ? 3.6f + (float)rng.NextDouble() * 1.0f : 2.3f + (float)rng.NextDouble() * 1.0f;
                    float tone = 0.9f + (float)rng.NextDouble() * 0.16f;
                    Cobble(px, N, cx, cy, rx, ry, tone, rng.NextDouble() < 0.28);
                }
            cobbles = px;
            return px;
        }

        static void Cobble(Color32[] px, int n, float cx, float cy, float rx, float ry, float tone, bool snowCap)
        {
            var baseC = PixelCanvas.Shade(CobS, tone);
            var light = PixelCanvas.Shade(CobL, tone);
            var dark = PixelCanvas.Shade(CobD, tone);
            for (int y = Mathf.FloorToInt(cy - ry - 1); y <= Mathf.CeilToInt(cy + ry + 1); y++)
                for (int x = Mathf.FloorToInt(cx - rx - 1); x <= Mathf.CeilToInt(cx + rx + 1); x++)
                {
                    float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                    float d = nx * nx + ny * ny;
                    if (d > 1f) continue;
                    var col = baseC;
                    if (ny < -0.3f && nx < 0.45f) col = light;   // lit from the upper left
                    if (d > 0.6f && ny > 0.15f) col = dark;      // shaded lower rim
                    if (snowCap && ny < -0.6f) col = SnowL;      // a little snow on top
                    px[Mod(y, n) * n + Mod(x, n)] = col;
                }
        }

        // ---------- Dispatch ----------

        /// <summary>snow_* keys: tiles, decorations and props of the winter village.</summary>
        static PixelCanvas DrawSnow(string[] p)
        {
            int Arg(int i, int fallback = 0) => p.Length > i ? int.Parse(p[i]) : fallback;
            switch (p[1])
            {
                case "ground": return SnowGroundTile(Arg(2), Arg(3));
                case "path": return PatternTile(CobblePattern(), Arg(2), Arg(3));
                case "pathedge": return DrawPathEdge(Arg(2));
                case "cliff": { var c = new PixelCanvas(16, 16); DrawSnowCliff(c, Arg(2), Arg(3), Arg(4)); return c; }
                case "water": { var c = new PixelCanvas(16, 16); DrawSnowWater(c, Arg(2), Arg(3)); return c; }
                case "fall": return DrawWaterfall(Arg(2), Arg(3), Arg(4));
                case "bridge": return DrawSnowBridge(Arg(2));
                case "stairs": return DrawSnowStairs(Arg(2));
                case "speck": return DrawSnowSpeck(Arg(2));
                case "tree": return DrawSnowTree(Arg(2), Arg(3));
                case "pine": return DrawSnowPine(Arg(2), Arg(3));
                case "bush": return DrawSnowBush(Arg(2));
                case "grass": return DrawSnowGrass(Arg(2));
                case "rock": return DrawSnowRock(Arg(2));
                case "ice": return DrawIceChunk(Arg(2));
                case "house": return DrawSnowHouse();
                case "barn": return DrawBarn();
                case "hay": return DrawHay();
                case "well": return DrawSnowWell();
                case "bench": return DrawSnowBench();
                case "fire": return DrawCampfire(Arg(2));
                case "log": return DrawLogSeat();
                case "workbench": return DrawWorkbench();
                case "lamp": return DrawLamp();
                case "mailbox": return DrawMailbox();
                case "pot": return DrawFlowerPot();
                case "woodpile": return DrawWoodpile();
                case "barrel": return DrawSnowBarrel();
                case "crate": return DrawSnowCrate();
                case "sign": return DrawSnowSign();
                case "gate": return DrawSnowGate();
                case "fence": return DrawSnowFence(Arg(2));
                case "bed": return DrawGardenBed(Arg(2));
            }
            return null;
        }

        // ---------- Ground ----------

        static PixelCanvas PatternTile(Color32[] pattern, int ix, int iy)
        {
            var c = new PixelCanvas(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    c.Pixels[y * 16 + x] = pattern[(iy * 16 + y) * SnowPatternSize + ix * 16 + x];
            return c;
        }

        static PixelCanvas SnowGroundTile(int ix, int iy) => PatternTile(SnowField(), ix, iy);

        /// <summary>
        /// Snow creeping over the edge of the stone path from snowy neighbours: an irregular white rim
        /// with a soft teal shadow on the stones, so no straight square boundary shows.
        /// mask: 1=N 2=E 4=S 8=W, 16=NE 32=SE 64=SW 128=NW (diagonal only counts when both sides are path).
        /// </summary>
        static PixelCanvas DrawPathEdge(int mask)
        {
            var c = new PixelCanvas(16, 16);
            void Side(int side)
            {
                for (int i = 0; i < 16; i++)
                {
                    int depth = 2 + EdgeBump(i, side * 1.7f + mask * 0.13f, 2.4f);
                    for (int k = 0; k <= depth; k++)
                    {
                        int x, y;
                        switch (side)
                        {
                            case 1: x = i; y = k; break;
                            case 4: x = i; y = 15 - k; break;
                            case 8: x = k; y = i; break;
                            default: x = 15 - k; y = i; break;
                        }
                        var col = k < depth - 1 ? SnowW : k == depth - 1 ? SnowL : PixelCanvas.WithAlpha(SnowSD, 140);
                        c.Set(x, y, col);
                    }
                }
            }
            if ((mask & 1) != 0) Side(1);
            if ((mask & 4) != 0) Side(4);
            if ((mask & 8) != 0) Side(8);
            if ((mask & 2) != 0) Side(2);
            void Corner(float cx, float cy)
            {
                c.Circle(cx, cy, 3.4f, SnowW);
                c.Circle(cx, cy, 2.2f, SnowW);
            }
            if ((mask & 16) != 0) Corner(15.5f, 0.5f);
            if ((mask & 32) != 0) Corner(15.5f, 15.5f);
            if ((mask & 64) != 0) Corner(0.5f, 15.5f);
            if ((mask & 128) != 0) Corner(0.5f, 0.5f);
            return c;
        }

        /// <summary>Rare small marks on the snow (placed sparsely, never on every tile).</summary>
        static PixelCanvas DrawSnowSpeck(int k)
        {
            var c = new PixelCanvas(16, 16);
            var rng = new System.Random(k * 71 + 5);
            int x = rng.Next(3, 11), y = rng.Next(4, 11);
            switch (k % 6)
            {
                case 0: // small drift bump
                    c.Ellipse(x + 2, y + 2, 3.5f, 1.2f, PixelCanvas.WithAlpha(SnowS, 170));
                    c.Ellipse(x + 2, y + 1, 3f, 1.2f, White);
                    break;
                case 1: // ice pellets
                    c.Set(x, y, IceD); c.Set(x + 3, y + 1, IceC); c.Set(x + 1, y + 3, IceD);
                    break;
                case 2: // little paw prints
                    c.Set(x, y, SnowSD); c.Set(x + 2, y + 1, SnowSD); c.Set(x + 1, y + 4, SnowSD); c.Set(x + 3, y + 5, SnowSD);
                    break;
                case 3: // sparkle
                    c.Set(x, y, White); c.Set(x - 1, y, SnowL); c.Set(x + 1, y, SnowL); c.Set(x, y - 1, SnowL); c.Set(x, y + 1, SnowL);
                    break;
                case 4: // twig
                    c.Line(x, y + 2, x + 3, y, WWoodD); c.Set(x + 2, y + 2, WWood);
                    break;
                default: // half-buried pebble
                    c.Ellipse(x + 1, y + 1, 1.8f, 1.1f, SnowBG); c.HLine(x, x + 2, y, SnowW);
                    break;
            }
            return c;
        }

        // ---------- Cliffs ----------

        /// <summary>
        /// Low winter cliff in the 3/4 view: grey-violet and beige strata whose lines sit at fixed rows
        /// so they continue across tiles, a thick snow lip with icicles on top, and snow piled at the foot
        /// (the ground below starts with the same white, so the joint has no gap).
        /// d = rows down to lower ground (1 = foot, 3 = top of a face, 4+ = rock top), flags 1 = plateau
        /// above, 2 = open left, 4 = open right.
        /// </summary>
        static void DrawSnowCliff(PixelCanvas c, int d, int flags, int v)
        {
            var rng = new System.Random(v * 613 + d * 37 + flags * 11);
            if (d >= 4 && (flags & 1) == 0)
            {
                c.Rect(0, 0, 16, 16, SnowW);
                c.Ellipse(rng.Next(3, 13), rng.Next(4, 12), 4f, 1.4f, SnowL);
                return;
            }
            // Strata: violet rock courses separated by thin beige layers. Each boundary wobbles but meets
            // the same height at both tile edges, so layers run on across the whole cliff.
            int[] seams = d >= 4 ? new[] { 12 } : d == 3 ? new[] { 3, 11 } : d == 2 ? new[] { 6, 14 } : new[] { 5 };
            var seamY = new int[seams.Length, 16];
            for (int s = 0; s < seams.Length; s++)
                for (int x = 0; x < 16; x++) seamY[s, x] = seams[s] + EdgeBump(x, v * 1.7f + s * 2.3f + d, 1.4f);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    bool beige = false;
                    for (int s = 0; s < seams.Length; s++) if (y >= seamY[s, x] && y <= seamY[s, x] + 1) beige = true;
                    c.Set(x, y, beige ? CliffBe : CliffP);
                }
            for (int s = 0; s < seams.Length; s++)
                for (int x = 0; x < 16; x++)
                {
                    c.Set(x, seamY[s, x] - 1, CliffPD);    // shaded underside of the course above
                    c.Set(x, seamY[s, x] + 1, CliffBeD);
                    c.Set(x, seamY[s, x] + 2, CliffPL);    // lit top of the course below
                }
            // Rock blocks: vertical joints with a lit left edge, and a rounded highlight per block.
            for (int x = rng.Next(0, 4); x < 16; x += rng.Next(4, 7))
            {
                int y0 = rng.Next(0, 8), y1 = Mathf.Min(15, y0 + rng.Next(4, 9));
                for (int y = y0; y <= y1; y++)
                {
                    if (Same(c.Get(x, y), CliffP) || Same(c.Get(x, y), CliffPL))
                    {
                        c.Set(x, y, CliffPDD);
                        if (x + 1 < 16 && Same(c.Get(x + 1, y), CliffP)) c.Set(x + 1, y, CliffPL);
                    }
                }
                int hx = x + 2, hy = y0 + 1;
                if (hx < 15 && Same(c.Get(hx, hy), CliffP)) { c.Set(hx, hy, CliffPL); c.Set(hx + 1, hy, CliffPL); }
            }
            for (int k = 0; k < 2; k++)
            {
                int px = rng.Next(1, 15), py = rng.Next(2, 14);
                if (Same(c.Get(px, py), CliffP)) c.Set(px, py, CliffPD);
            }
            // A little snow lying on the ledges.
            for (int s = 0; s < seams.Length; s++)
                for (int x = 0; x < 16; x++)
                    if (Mod(x * 7 + v * 5 + s * 3 + d, 9) < 2)
                    {
                        c.Set(x, seamY[s, x] + 1, SnowW);
                        c.Set(x, seamY[s, x] + 2, SnowL);
                    }
            if (d == 1)
            {
                // Foot: pastel shadow, then snow heaped against the wall (same white as the ground below).
                for (int x = 0; x < 16; x++)
                {
                    int h = 4 + EdgeBump(x, v * 2.1f, 2f);
                    c.Set(x, 15 - h - 1, CliffPDD);
                    c.Set(x, 15 - h - 2, PixelCanvas.WithAlpha(CliffPDD, 130));
                    for (int k = 0; k <= h; k++) c.Set(x, 15 - k, k == h ? SnowS : k == h - 1 ? SnowL : SnowW);
                }
            }
            if ((flags & 1) != 0)
            {
                // Plateau edge: a thick, rounded snow cap overhanging the face, with icicles.
                for (int x = 0; x < 16; x++)
                {
                    int h = 7 + EdgeBump(x, v * 1.3f + 0.7f, 2.2f);
                    for (int k = 0; k <= h; k++)
                    {
                        var col = k < h - 3 ? SnowW : k < h - 1 ? SnowL : SnowS;
                        if (k == 0) col = White;
                        c.Set(x, k, col);
                    }
                    c.Set(x, h + 1, CliffPDD);
                    c.Set(x, h + 2, PixelCanvas.WithAlpha(CliffPDD, 120));
                    if ((x * 7 + v * 3) % 5 == 0) { c.Set(x, h + 1, IceC); c.Set(x, h + 2, IceD); if ((x + v) % 2 == 0) c.Set(x, h + 3, IceD); }
                }
            }
            if ((flags & 2) != 0)
            {
                // Open left: rounded rock edge with snow clinging to it.
                c.VLine(0, 0, 15, CliffPL);
                c.VLine(1, 0, 15, CliffP);
                for (int y = 2; y < 16; y += 4) { c.Set(0, y, SnowW); c.Set(1, y, SnowL); }
            }
            if ((flags & 4) != 0)
            {
                c.VLine(15, 0, 15, CliffPDD);
                c.VLine(14, 0, 15, CliffPD);
                for (int y = 4; y < 16; y += 5) c.Set(15, y, SnowS);
            }
        }

        // ---------- River ----------

        /// <summary>
        /// Winter river: clear mid-blue with low-contrast ripples that never form a regular pattern, and
        /// snowy banks with a soft shadow line on the water. mask: 1 = land above, 2 = land right,
        /// 4 = land left, 8 = cliff above, 16 = land below, 32 = bridge above, 64 = waterfall above.
        /// Bends: 128/256/512/1024 = this cell is the corner of a one-tile step in the bank with land on
        /// N+W / N+E / S+W / S+E, drawn as one S-shaped bank; 2048/4096/8192/16384 = the S-curve of the
        /// neighbouring bend cell reaches into this cell (bend to the left / right / left / right).
        /// </summary>
        static void DrawSnowWater(PixelCanvas c, int mask, int v)
        {
            c.Rect(0, 0, 16, 16, RiverB);
            var rng = new System.Random(v * 977 + mask * 13 + 5);
            int ripples = rng.Next(1, 4);
            for (int i = 0; i < ripples; i++)
            {
                int len = rng.Next(2, 6), x = rng.Next(0, 16 - len), y = rng.Next(3, 14);
                c.HLine(x, x + len - 1, y, PixelCanvas.WithAlpha(RiverL, 120));
                if (len > 3) c.Set(x + len, y - 1, PixelCanvas.WithAlpha(RiverL, 70));
            }
            if (rng.NextDouble() < 0.4) c.HLine(rng.Next(0, 8), rng.Next(8, 16), rng.Next(3, 14), PixelCanvas.WithAlpha(RiverD, 110));
            int bend = (mask >> 7) & 15;
            if (bend != 0)
            {
                DrawBank(c, (x, y) => BendLand(bend, x, y));
                return;
            }
            if ((mask & 8) != 0 || (mask & 32) != 0)
            {
                c.Rect(0, 0, 16, 3, PixelCanvas.WithAlpha(RiverD, 180));
                for (int x = 0; x < 16; x++) if ((x * 3 + v) % 5 < 2) c.Set(x, 3, PixelCanvas.WithAlpha(RiverFoam, 110));
            }
            if ((mask & 64) != 0)
            {
                // Splash below the waterfall.
                for (int k = 0; k < 7; k++) c.Ellipse(rng.Next(0, 16), rng.Next(0, 6), rng.Next(2, 4), 1.4f, RiverFoam);
                for (int x = 0; x < 16; x++) if ((x + v) % 3 == 0) c.Set(x, rng.Next(6, 9), PixelCanvas.WithAlpha(White, 170));
            }
            if ((mask & 1) != 0)
            {
                // North bank: its snow edge and a thin grey-violet face dropping into the water.
                for (int x = 0; x < 16; x++)
                {
                    int h = 3 + EdgeBump(x, v * 1.9f, 1.5f);
                    for (int k = 0; k < h; k++) c.Set(x, k, k < h - 1 ? SnowW : SnowL);
                    c.Set(x, h, CliffPD);
                    c.Set(x, h + 1, PixelCanvas.WithAlpha(RiverD, 170));
                    if ((x * 5 + v) % 7 < 2) c.Set(x, h + 2, PixelCanvas.WithAlpha(RiverFoam, 120));
                }
            }
            if ((mask & 4) != 0)
            {
                for (int y = 0; y < 16; y++)
                {
                    int w = 2 + EdgeBump(y, v * 2.3f + 1f, 1.5f);
                    for (int k = 0; k < w; k++) c.Set(k, y, k < w - 1 ? SnowW : SnowL);
                    c.Set(w, y, PixelCanvas.WithAlpha(CliffPD, 200));
                    c.Set(w + 1, y, PixelCanvas.WithAlpha(RiverD, 140));
                }
            }
            if ((mask & 2) != 0)
            {
                for (int y = 0; y < 16; y++)
                {
                    int w = 2 + EdgeBump(y, v * 1.7f + 2f, 1.5f);
                    for (int k = 0; k < w; k++) c.Set(15 - k, y, k < w - 1 ? SnowW : SnowS);
                    c.Set(15 - w, y, PixelCanvas.WithAlpha(RiverD, 150));
                }
            }
            if ((mask & 16) != 0)
            {
                // South bank: its snowy top edge overlaps the water.
                for (int x = 0; x < 16; x++)
                {
                    int h = 2 + EdgeBump(x, v * 2.9f + 3f, 1.4f);
                    for (int k = 0; k < h; k++) c.Set(x, 15 - k, k < h - 1 ? SnowW : SnowL);
                    c.Set(x, 15 - h, PixelCanvas.WithAlpha(RiverFoam, 150));
                }
            }
            int spill = (mask >> 11) & 15;
            if (spill != 0) DrawBank(c, (x, y) => SpillLand(spill, x, y));
        }

        /// <summary>Snow lip width of the straight side banks at a cell edge, where a bend joins them.</summary>
        const float BankLip = 2f;

        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Land test for a bend cell (pixel centre, y down; may lie outside the cell). The bank leaves the
        /// top edge in line with the bank above and meets the bottom edge in line with the bank below,
        /// easing in and out so both ends are vertical: an S-curve instead of a square step.
        /// </summary>
        static bool BendLand(int bend, float x, float y)
        {
            float s = Ease(y / 16f);
            switch (bend)
            {
                case 1: return x < BankLip + 16f * (1f - s);       // land N+W: bank moves one cell left going down
                case 2: return x > -BankLip + 16f * s;             // land N+E: bank moves one cell right
                case 4: return x < BankLip + 16f * s;              // land S+W: bank moves one cell right
                default: return x > 16f - BankLip - 16f * s;       // land S+E: bank moves one cell left
            }
        }

        /// <summary>The part of a neighbouring bend cell's S-curve that reaches into this cell.</summary>
        static bool SpillLand(int spill, float x, float y)
        {
            return ((spill & 1) != 0 && BendLand(1, x + 16f, y))
                || ((spill & 2) != 0 && BendLand(2, x - 16f, y))
                || ((spill & 4) != 0 && BendLand(4, x + 16f, y))
                || ((spill & 8) != 0 && BendLand(8, x - 16f, y));
        }

        /// <summary>
        /// A snowy bank of any shape (given by a land test on pixel centres) over the water, in the same
        /// style as the straight banks: snow with a lighter lip, then where the water meets it a grey-violet
        /// face (bank to the north or west), a darker water line (bank to the east) or foam (bank to the
        /// south), and a soft shadow beyond the face.
        /// </summary>
        static void DrawBank(PixelCanvas c, System.Func<float, float, bool> land)
        {
            bool L(int x, int y) => land(x + 0.5f, y + 0.5f);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    bool up = L(x, y - 1), down = L(x, y + 1), left = L(x - 1, y), right = L(x + 1, y);
                    if (L(x, y))
                    {
                        bool inner = up && down && left && right;
                        c.Set(x, y, inner ? SnowW : !left ? SnowS : SnowL);
                        continue;
                    }
                    if (up) c.Set(x, y, CliffPD);
                    else if (left) c.Set(x, y, PixelCanvas.WithAlpha(CliffPD, 200));
                    else if (right) c.Set(x, y, PixelCanvas.WithAlpha(RiverD, 150));
                    else if (down) c.Set(x, y, PixelCanvas.WithAlpha(RiverFoam, 150));
                    else if (L(x - 1, y - 1) || L(x, y - 2) || L(x - 2, y)) c.Set(x, y, PixelCanvas.WithAlpha(RiverD, 150));
                }
        }

        /// <summary>
        /// Waterfall over the cliff: part 0 = lip at the top, 1 = falling sheet, 2 = foot with foam.
        /// sides: 1 = rock on the left, 2 = rock on the right (edge drawn only there, so the sheet is seamless).
        /// </summary>
        static PixelCanvas DrawWaterfall(int part, int v, int sides)
        {
            var c = new PixelCanvas(16, 16);
            c.Rect(0, 0, 16, 16, RiverL);
            for (int x = 0; x < 16; x++)
            {
                int k = (x * 7 + v * 3) % 5;
                if (k == 0) c.VLine(x, 0, 15, PixelCanvas.WithAlpha(White, 190));
                else if (k == 3) c.VLine(x, 0, 15, RiverB);
                else if (k == 4 && (x + v) % 2 == 0) c.VLine(x, (v * 5) % 8, 15, PixelCanvas.WithAlpha(RiverFoam, 150));
            }
            if (part == 0)
            {
                c.Rect(0, 0, 16, 3, RiverB);
                c.HLine(0, 15, 3, RiverFoam);
                for (int x = 1; x < 16; x += 3) c.Set(x, 4, White);
            }
            if (part == 2)
            {
                for (int k = 0; k < 6; k++) c.Ellipse(k * 3 + (v % 3), 13f + (k % 2), 2.6f, 2f, White);
                c.HLine(0, 15, 15, RiverFoam);
            }
            if ((sides & 1) != 0) { c.VLine(0, 0, 15, CliffPDD); c.VLine(1, 0, 15, PixelCanvas.WithAlpha(CliffPDD, 120)); }
            if ((sides & 2) != 0) { c.VLine(15, 0, 15, CliffPDD); c.VLine(14, 0, 15, PixelCanvas.WithAlpha(CliffPDD, 120)); }
            return c;
        }

        /// <summary>Wooden bridge / deck: planks run north-south; snow-capped rails on open north/south edges.</summary>
        static PixelCanvas DrawSnowBridge(int part)
        {
            var c = new PixelCanvas(16, 16);
            c.Rect(0, 0, 16, 16, WWoodL);
            for (int x = 0; x < 16; x += 4)
            {
                c.VLine(x + 3, 0, 15, WWoodD);
                c.VLine(x, 0, 15, PixelCanvas.Shade(WWoodL, 1.06f));
            }
            c.Set(1, 5, WWoodD); c.Set(9, 11, WWoodD); c.Set(6, 2, WWoodD); c.Set(13, 8, WWoodD);
            if ((part & 1) != 0)
            {
                c.Rect(0, 0, 16, 4, WWood);
                c.HLine(0, 15, 0, SnowW); c.HLine(0, 15, 1, SnowL);
                c.HLine(0, 15, 4, WWoodDD);
                c.Rect(1, 0, 2, 5, WWoodD); c.Rect(13, 0, 2, 5, WWoodD);
            }
            if ((part & 2) != 0)
            {
                c.Rect(0, 11, 16, 5, WWood);
                c.HLine(0, 15, 11, SnowW); c.HLine(0, 15, 12, SnowL);
                c.HLine(0, 15, 15, WWoodDD);
                c.Rect(1, 11, 2, 5, WWoodD); c.Rect(13, 11, 2, 5, WWoodD);
            }
            return c;
        }

        /// <summary>Stone steps with snow on the tread edges. sides: 1 = open left, 2 = open right (snow banks).</summary>
        static PixelCanvas DrawSnowStairs(int sides)
        {
            var c = new PixelCanvas(16, 16);
            var tread = PixelCanvas.Hex("#c9c6d2");
            var riser = PixelCanvas.Hex("#9a97a8");
            for (int y = 0; y < 16; y++)
            {
                int k = y % 8;
                var col = k == 0 ? PixelCanvas.Shade(tread, 1.08f) : k < 5 ? tread : k == 5 ? PixelCanvas.Hex("#7f7c8e") : riser;
                c.HLine(0, 15, y, col);
            }
            for (int x = 2; x < 16; x += 5) { c.Set(x, 1, SnowW); c.Set(x + 1, 1, SnowL); c.Set(x + 2, 9, SnowW); }
            c.VLine(5, 6, 7, PixelCanvas.Hex("#7f7c8e")); c.VLine(11, 14, 15, PixelCanvas.Hex("#7f7c8e"));
            if ((sides & 1) != 0)
                for (int y = 0; y < 16; y++) { int w = 2 + ((y / 3) % 2); for (int k = 0; k < w; k++) c.Set(k, y, k < w - 1 ? SnowW : SnowS); }
            if ((sides & 2) != 0)
                for (int y = 0; y < 16; y++) { int w = 2 + ((y / 3 + 1) % 2); for (int k = 0; k < w; k++) c.Set(15 - k, y, k < w - 1 ? SnowW : SnowS); }
            return c;
        }

        // ---------- Trees and plants ----------

        /// <summary>Round broadleaf tree with a thick snow cap over teal foliage. size 0 = large, 1 = medium, 2 = small.</summary>
        static PixelCanvas DrawSnowTree(int size, int variant)
        {
            int w = size == 0 ? 46 : size == 1 ? 36 : 28, h = size == 0 ? 56 : size == 1 ? 46 : 36;
            var c = new PixelCanvas(w, h);
            float cx = w / 2f;
            int ground = h - 3;
            c.Ellipse(cx, ground, w * 0.36f, 2.8f, PixelCanvas.WithAlpha(SnowSD, 150));
            // Trunk with root flares.
            int tw = size == 0 ? 6 : size == 1 ? 5 : 4, th = size == 0 ? 13 : size == 1 ? 11 : 8;
            int tx = Mathf.RoundToInt(cx - tw / 2f), ty = ground - th;
            c.Rect(tx, ty, tw, th + 1, WWood);
            c.VLine(tx, ty, ground, WWoodL);
            c.VLine(tx + tw - 1, ty, ground, WWoodD);
            c.Set(tx - 1, ground, WWoodD); c.Set(tx + tw, ground, WWoodD); c.Set(tx - 2, ground, WWoodD);
            // Canopy mask from overlapping circles.
            float r = w * 0.38f, cy = ty - r * 0.5f;
            var rng = new System.Random(size * 101 + variant * 17 + 3);
            var blobs = new List<Vector3>
            {
                new Vector3(cx, cy, r),
                new Vector3(cx - r * 0.62f, cy + r * 0.28f, r * 0.6f),
                new Vector3(cx + r * 0.6f, cy + r * 0.24f, r * 0.62f),
                new Vector3(cx + (variant % 2 == 0 ? -1f : 1f) * r * 0.28f, cy - r * 0.5f, r * 0.6f),
            };
            if (size == 0) blobs.Add(new Vector3(cx + (variant % 2 == 0 ? 1f : -1f) * r * 0.45f, cy - r * 0.2f, r * 0.55f));
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
                int snow = Mathf.RoundToInt(span * 0.56f + 1.6f * Mathf.Sin(x * 0.85f + variant * 1.7f) + (rng.NextDouble() < 0.12 ? 2 : 0));
                for (int y = top; y <= bottom; y++)
                {
                    Color32 col;
                    int k = y - top;
                    if (k <= snow)
                    {
                        float t = k / (float)Mathf.Max(1, snow);
                        col = t < 0.55f ? SnowW : t < 0.85f ? SnowL : SnowS;
                        if (x > cx + r * 0.35f && t > 0.3f) col = Mix(col, SnowS, 0.5f); // shaded right side
                    }
                    else if (k == snow + 1) col = Leaf3;                                   // shadow under the snow
                    else
                    {
                        float t = (y - top - snow) / (float)Mathf.Max(1, bottom - top - snow);
                        col = t > 0.72f ? Leaf3 : x < cx - r * 0.2f ? Leaf1 : Leaf2;
                        if ((x * 3 + y * 5 + variant) % 11 == 0) col = Leaf1;              // leaf texture
                    }
                    c.Set(x, y, col);
                }
            }
            c.Outline(WOutline);
            return c.WithPivot(cx, 3f);
        }

        /// <summary>Conifer with snow on every tier. size 0 = large, 1 = small.</summary>
        static PixelCanvas DrawSnowPine(int size, int variant)
        {
            int w = size == 0 ? 32 : 22, h = size == 0 ? 58 : 40;
            var c = new PixelCanvas(w, h);
            float cx = w / 2f;
            int ground = h - 3;
            c.Ellipse(cx, ground, w * 0.42f, 2.6f, PixelCanvas.WithAlpha(SnowSD, 150));
            int tw = size == 0 ? 4 : 3, th = size == 0 ? 7 : 5;
            int tx = Mathf.RoundToInt(cx - tw / 2f);
            c.Rect(tx, ground - th, tw, th + 1, WWood);
            c.VLine(tx + tw - 1, ground - th, ground, WWoodD);
            int tiers = size == 0 ? 4 : 3;
            float tierH = (ground - th - 2) / (float)tiers * 1.3f;
            for (int i = 0; i < tiers; i++)
            {
                float bottom = ground - th + 1 - i * tierH * 0.72f;
                float half = (w / 2f - 1f) * (1f - i * 0.19f);
                float top = bottom - tierH;
                float snowLine = 0.42f + 0.04f * ((variant + i) % 3);
                for (int y = Mathf.FloorToInt(top); y <= Mathf.FloorToInt(bottom); y++)
                {
                    float t = (y - top) / tierH;
                    if (t < 0f) continue;
                    float hw = half * Mathf.Pow(Mathf.Clamp01(t), 0.85f);
                    for (int x = Mathf.FloorToInt(cx - hw); x <= Mathf.CeilToInt(cx + hw) - 1; x++)
                    {
                        float wave = 0.05f * Mathf.Sin(x * 1.3f + i * 2f + variant);
                        Color32 col;
                        if (t < snowLine + wave) col = x > cx + hw * 0.35f ? SnowL : SnowW;
                        else if (t < snowLine + wave + 0.08f) col = SnowS;
                        else col = t > 0.86f ? Pine3 : x < cx - hw * 0.15f ? Pine1 : Pine2;
                        c.Set(x, y, col);
                    }
                }
            }
            c.Set(Mathf.FloorToInt(cx), 0, SnowW); c.Set(Mathf.FloorToInt(cx) - 1, 1, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(cx, 3f);
        }

        static PixelCanvas DrawSnowBush(int v)
        {
            var c = new PixelCanvas(22, 16);
            c.Ellipse(11, 13, 9, 2, PixelCanvas.WithAlpha(SnowSD, 140));
            c.Circle(7, 10, 5, Leaf2); c.Circle(14, 10, 5.5f, Leaf2); c.Circle(11, 7, 5, Leaf2);
            c.PaintEllipse(11, 12, 9, 2.5f, Leaf3);
            c.PaintEllipse(6 + v % 2, 9, 2, 1.5f, Leaf1);
            for (int x = 2; x < 20; x++)
            {
                int top = -1;
                for (int y = 0; y < 16; y++) if (c.Get(x, y).a > 0 && c.Get(x, y).g > 120) { top = y; break; }
                if (top < 0) continue;
                int depth = 3 + EdgeBump(x - 2, v * 1.3f, 1.4f);
                for (int y = top; y < top + depth; y++) c.Set(x, y, y < top + depth - 1 ? SnowW : SnowS);
            }
            c.Outline(WOutline);
            return c.WithPivot(11, 2.5f);
        }

        static PixelCanvas DrawSnowGrass(int v)
        {
            var c = new PixelCanvas(9, 8);
            var blade = PixelCanvas.Hex("#8cc9b4");
            var bladeD = PixelCanvas.Hex("#66a893");
            c.Ellipse(4.5f, 6.5f, 4f, 1.4f, SnowL);
            c.Line(2, 6, 1, 2, blade); c.Line(4, 6, 4, 1 + v % 2, bladeD); c.Line(6, 6, 7, 3, blade);
            if (v % 2 == 0) c.Line(5, 6, 6, 2, bladeD);
            c.HLine(1, 7, 6, SnowW);
            return c.WithPivot(4.5f, 1f);
        }

        static PixelCanvas DrawSnowRock(int v)
        {
            var c = new PixelCanvas(18, 14);
            var stone = PixelCanvas.Hex("#a3a9ba");
            var stoneD = PixelCanvas.Hex("#80869a");
            c.Ellipse(9, 12, 8, 1.6f, PixelCanvas.WithAlpha(SnowSD, 150));
            c.Ellipse(9, 8.5f, 7.5f - v % 2, 5, stone);
            c.PaintEllipse(11, 10, 6, 3, stoneD);
            c.PaintEllipse(6, 6, 2.5f, 1.5f, PixelCanvas.Hex("#c2c7d4"));
            c.Ellipse(8.5f, 4.8f, 6f - v % 2, 2.4f, SnowW);
            c.HLine(4, 13 - v % 2, 6, SnowL);
            c.Outline(WOutline);
            return c.WithPivot(9, 2f);
        }

        static PixelCanvas DrawIceChunk(int v)
        {
            var c = new PixelCanvas(14, 8);
            c.Ellipse(7, 4, 6f - v % 2, 2.5f, IceC);
            c.HLine(2, 11 - v % 2, 5, IceD);
            c.HLine(3, 9, 2, White);
            c.Set(4 + v % 3, 3, White);
            return c;
        }

        // ---------- Buildings ----------

        /// <summary>
        /// Snowy roof seen from the front: shingles in the roof colour, a thick snow layer covering most
        /// of it with a wavy lower edge, dark bargeboards on the slopes, a wooden fascia and icicles.
        /// </summary>
        static void SnowRoof(PixelCanvas c, int cx, int top, int eave, int topHalf, int eaveHalf, Color32 roof)
        {
            var roofDark = PixelCanvas.Shade(roof, 0.72f);
            var roofLight = PixelCanvas.Shade(roof, 1.18f);
            int HalfAt(int y) => Mathf.RoundToInt(Mathf.Lerp(topHalf, eaveHalf, (y - top) / (float)Mathf.Max(1, eave - top)));
            for (int y = top; y <= eave; y++)
            {
                int hw = HalfAt(y);
                for (int x = cx - hw; x <= cx + hw; x++)
                {
                    int row = (y - top) % 3;
                    var col = row == 2 ? roofDark : roof;
                    if (row == 0 && Mod(x + (y / 3) * 2, 5) == 0) col = roofLight;
                    c.Set(x, y, col);
                }
            }
            // Snow over the upper ~75%, a little thinner towards the right where the light falls away.
            int snowTo = top + Mathf.RoundToInt((eave - top) * 0.74f);
            for (int x = cx - eaveHalf; x <= cx + eaveHalf; x++)
            {
                int bottom = snowTo + EdgeBump(Mod(x, 16), 0.9f, 2.4f) - (x > cx + eaveHalf / 2 ? 1 : 0);
                for (int y = top; y <= bottom && y <= eave; y++)
                {
                    if (Mathf.Abs(x - cx) > HalfAt(y)) continue;
                    var col = x > cx + 2 ? SnowL : SnowW;
                    if (y >= bottom - 1) col = SnowS;
                    if (y == top) col = White;
                    c.Set(x, y, col);
                }
            }
            // Bargeboards along both slopes.
            for (int y = top; y <= eave; y++)
            {
                int hw = HalfAt(y);
                c.Set(cx - hw, y, WWoodDD); c.Set(cx + hw, y, WWoodDD);
            }
            // Fascia, a soft snow drip over it, icicles below.
            c.HLine(cx - eaveHalf, cx + eaveHalf, eave + 1, WWood);
            c.HLine(cx - eaveHalf, cx + eaveHalf, eave + 2, WWoodDD);
            for (int x = cx - eaveHalf; x <= cx + eaveHalf; x++)
            {
                if (Mod(x * 3, 7) < 3) c.Set(x, eave + 1, SnowW);
                if (Mod(x * 5, 11) == 0) { c.Set(x, eave + 3, IceC); c.Set(x, eave + 4, IceD); }
            }
        }

        /// <summary>Small log cabin (3 tiles wide): warm log walls, glowing windows, wreath, deep snow roof and chimney.</summary>
        static PixelCanvas DrawSnowHouse()
        {
            var c = new PixelCanvas(52, 64);
            int x0 = 4, x1 = 47, y0 = 34, y1 = 58;
            for (int y = y0; y <= y1; y++)
            {
                int k = (y - y0) % 4;
                c.HLine(x0, x1, y, k == 0 ? WWoodL : k == 3 ? WWoodD : WWood);
            }
            for (int y = y0; y <= y1; y += 4)
            {
                c.Rect(x0 - 2, y, 3, 3, WWoodL); c.Set(x0 - 1, y + 1, WWoodD);
                c.Rect(x1, y, 3, 3, WWoodL); c.Set(x1 + 1, y + 1, WWoodD);
            }
            c.Rect(x0 - 1, 58, x1 - x0 + 3, 3, PixelCanvas.Hex("#9aa0b0"));
            c.HLine(x0 - 1, x1 + 1, 58, PixelCanvas.Hex("#c2c7d4"));
            // Door with a wreath, stone step.
            c.Rect(21, 42, 10, 17, WWoodDD);
            c.VLine(26, 42, 58, PixelCanvas.Hex("#3e2618"));
            c.HLine(21, 30, 42, WWoodD);
            c.Set(29, 51, Gold);
            c.Circle(26, 46, 2.6f, Pine2); c.Circle(26, 46, 1.3f, WWoodDD);
            c.Set(24, 45, Red); c.Set(28, 47, Red); c.Set(26, 48, Red);
            c.Rect(19, 59, 14, 3, PixelCanvas.Hex("#b4b8c6")); c.HLine(19, 32, 59, SnowW);
            // Windows with warm light and snowy sills.
            foreach (int wx in new[] { 8, 36 })
            {
                c.Rect(wx, 40, 9, 9, Warm);
                c.Rect(wx + 1, 41, 3, 3, WarmL);
                c.VLine(wx + 4, 40, 48, WWoodDD); c.HLine(wx, wx + 8, 44, WWoodDD);
                c.Rect(wx - 1, 49, 11, 2, SnowW); c.HLine(wx - 1, wx + 9, 50, SnowS);
            }
            // Lantern beside the door.
            c.Rect(33, 43, 3, 4, Warm); c.Set(34, 42, LampGD);
            // Chimney (behind the roof) with a snow cap and a curl of smoke.
            c.Rect(35, 5, 7, 14, PixelCanvas.Hex("#8e93a3"));
            c.VLine(41, 5, 18, PixelCanvas.Hex("#6e7384"));
            c.Rect(34, 3, 9, 3, SnowW); c.HLine(34, 42, 5, SnowS);
            c.Circle(38, 1.5f, 1.5f, PixelCanvas.WithAlpha(SnowBG, 190));
            c.Circle(41, 0.5f, 1f, PixelCanvas.WithAlpha(SnowBG, 140));
            // Roof.
            SnowRoof(c, 26, 9, 33, 8, 26, PixelCanvas.Hex("#a44a3a"));
            c.HLine(x0, x1, 37, WWoodDD); // eave shadow on the wall
            // Flower boxes under the windows.
            foreach (int wx in new[] { 8, 36 }) { c.Rect(wx - 1, 51, 11, 2, WWoodD); c.Set(wx + 2, 50, Red); c.Set(wx + 6, 50, Red); }
            c.Outline(WOutline);
            return c.WithPivot(26, 3.5f);
        }

        /// <summary>Small farm building (5 tiles wide): board walls, big braced doors, hay loft, snowy gambrel roof, weathervane.</summary>
        static PixelCanvas DrawBarn()
        {
            var c = new PixelCanvas(84, 82);
            int x0 = 4, x1 = 79, y0 = 38, y1 = 77;
            for (int x = x0; x <= x1; x++)
            {
                var col = (x - x0) % 6 == 0 ? BarnWallD : (x - x0) % 6 == 1 ? PixelCanvas.Shade(BarnWall, 1.08f) : BarnWall;
                c.VLine(x, y0, y1, col);
            }
            c.Rect(x0 - 1, 77, x1 - x0 + 3, 3, PixelCanvas.Hex("#9aa0b0"));
            // Big double door with X braces.
            c.Rect(28, 52, 28, 26, WWoodD);
            c.VLine(41, 52, 77, WWoodDD); c.VLine(42, 52, 77, WWoodDD);
            c.Line(29, 53, 40, 76, Cream); c.Line(29, 76, 40, 53, Cream);
            c.Line(43, 53, 54, 76, Cream); c.Line(43, 76, 54, 53, Cream);
            c.Rect(28, 52, 28, 2, Cream); c.Rect(28, 76, 28, 2, Cream);
            c.VLine(28, 52, 77, Cream); c.VLine(55, 52, 77, Cream);
            // Hay loft.
            c.Rect(34, 40, 16, 9, WWoodDD);
            c.Rect(35, 44, 14, 5, Hay); c.HLine(35, 48, 44, PixelCanvas.Shade(Hay, 1.1f)); c.Set(38, 45, HayD); c.Set(44, 46, HayD);
            c.Rect(33, 39, 18, 1, Cream); c.VLine(33, 39, 49, Cream); c.VLine(50, 39, 49, Cream);
            // Small windows.
            foreach (int wx in new[] { 10, 66 })
            {
                c.Rect(wx, 56, 8, 8, Warm);
                c.VLine(wx + 4, 56, 63, WWoodDD); c.HLine(wx, wx + 7, 60, WWoodDD);
                c.Rect(wx - 1, 64, 10, 2, SnowW);
            }
            // Roof: dark red shingles under a thick snow layer.
            SnowRoof(c, 42, 9, 36, 12, 41, PixelCanvas.Hex("#8a3b32"));
            c.HLine(x0, x1, 40, PixelCanvas.WithAlpha(WWoodDD, 200));
            // Weathervane.
            c.VLine(42, 1, 8, WOutline);
            c.HLine(39, 45, 3, WOutline);
            c.Rect(43, 0, 3, 2, PixelCanvas.Hex("#d9a441"));
            c.Outline(WOutline);
            return c.WithPivot(42, 3.5f);
        }

        // ---------- Props ----------

        static PixelCanvas DrawHay()
        {
            var c = new PixelCanvas(18, 16);
            c.Ellipse(9, 14, 8, 1.6f, PixelCanvas.WithAlpha(SnowSD, 150));
            c.Rect(1, 5, 16, 9, Hay);
            for (int x = 2; x < 17; x += 3) c.VLine(x, 7, 13, HayD);
            c.HLine(1, 16, 9, WWoodD);
            c.Rect(1, 3, 16, 3, SnowW); c.HLine(1, 16, 5, SnowS);
            c.Set(3, 2, SnowW); c.Set(12, 2, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(9, 2.5f);
        }

        static PixelCanvas DrawSnowWell()
        {
            var c = new PixelCanvas(32, 38);
            var stone = PixelCanvas.Hex("#a39bb0");
            var stoneD = PixelCanvas.Hex("#7e7690");
            c.Ellipse(16, 34, 13, 2.5f, PixelCanvas.WithAlpha(SnowSD, 150));
            c.Ellipse(16, 29, 13, 7, stoneD);
            c.Ellipse(16, 28, 12, 6, stone);
            c.Ellipse(16, 27, 8, 3.5f, RiverD);
            c.PaintEllipse(14, 26, 3, 1, RiverL);
            for (int x = 5; x < 28; x += 5) c.Set(x, 31, stoneD);
            c.Ellipse(16, 23.5f, 12, 1.6f, SnowW);
            c.Rect(4, 7, 2, 21, WWoodD); c.Rect(26, 7, 2, 21, WWoodD);
            c.HLine(6, 25, 13, WWoodDD);
            c.VLine(16, 13, 19, PixelCanvas.Hex("#c8b89a"));
            c.Rect(14, 19, 5, 4, WWood); c.HLine(14, 18, 19, SnowW);
            // Little snowy roof.
            for (int y = 2; y <= 9; y++)
            {
                int hw = 6 + (y - 2) * 10 / 7;
                c.HLine(16 - hw, 15 + hw, y, y > 7 ? SnowS : y > 4 ? SnowL : SnowW);
            }
            c.HLine(1, 30, 10, WWood);
            c.Outline(WOutline);
            return c.WithPivot(16, 3.5f);
        }

        static PixelCanvas DrawSnowBench()
        {
            var c = new PixelCanvas(32, 18);
            c.Rect(2, 5, 28, 3, WWood); c.HLine(2, 29, 5, WWoodL);
            c.Rect(2, 10, 28, 3, WWood); c.HLine(2, 29, 12, WWoodD);
            c.Rect(4, 13, 2, 4, WWoodD); c.Rect(26, 13, 2, 4, WWoodD);
            c.HLine(2, 29, 4, SnowW); c.HLine(3, 27, 9, SnowW); c.HLine(4, 26, 10, SnowL);
            c.Outline(WOutline);
            return c.WithPivot(16, 1.5f);
        }

        /// <summary>Campfire: stone ring with snow caps, crossed logs, flames (frame 0-2 for the flicker).</summary>
        static PixelCanvas DrawCampfire(int frame)
        {
            var c = new PixelCanvas(24, 24);
            var stone = PixelCanvas.Hex("#9ea3b3");
            c.Ellipse(12, 20, 10, 3, PixelCanvas.WithAlpha(SnowSD, 120));
            c.Ellipse(12, 19, 7, 2.4f, PixelCanvas.Hex("#5a4a44"));
            c.Line(6, 20, 17, 16, WWoodD); c.Line(6, 16, 17, 20, WWood);
            c.Line(7, 20, 18, 16, WWoodD);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float sx = 12 + Mathf.Cos(a) * 9, sy = 19 + Mathf.Sin(a) * 3.4f;
                c.Ellipse(sx, sy, 2.2f, 1.6f, stone);
                c.Set(Mathf.RoundToInt(sx) - 1, Mathf.RoundToInt(sy) - 1, SnowW);
            }
            // Flames.
            float sway = frame == 0 ? 0f : frame == 1 ? 1f : -1f;
            int fh = frame == 1 ? 12 : frame == 2 ? 10 : 11;
            for (int y = 0; y < fh; y++)
            {
                float t = y / (float)fh;                       // 0 at the base
                float hw = 4.5f * (1f - t) * (0.85f + 0.15f * Mathf.Sin(y + frame));
                float fx = 12 + sway * t * 2f;
                int yy = 17 - y;
                for (int x = Mathf.FloorToInt(fx - hw); x <= Mathf.CeilToInt(fx + hw); x++)
                {
                    float e = Mathf.Abs(x + 0.5f - fx) / Mathf.Max(0.5f, hw);
                    var col = e > 0.7f ? PixelCanvas.Hex("#ff7a24") : e > 0.35f || t > 0.6f ? PixelCanvas.Hex("#ffc84a") : PixelCanvas.Hex("#fff1b8");
                    c.Set(x, yy, col);
                }
            }
            c.Set(10 + frame, 17 - fh - 1, PixelCanvas.Hex("#ffc84a"));
            return c.WithPivot(12, 3.5f);
        }

        static PixelCanvas DrawLogSeat()
        {
            var c = new PixelCanvas(24, 14);
            c.Ellipse(12, 12, 10, 1.6f, PixelCanvas.WithAlpha(SnowSD, 140));
            c.Rect(4, 4, 18, 8, WWood);
            c.HLine(4, 21, 11, WWoodD);
            for (int x = 7; x < 21; x += 4) c.VLine(x, 6, 10, WWoodD);
            c.Ellipse(4, 8, 3, 4, PixelCanvas.Hex("#dcb488"));
            c.Ellipse(4, 8, 1.6f, 2.2f, PixelCanvas.Hex("#b88a5c"));
            c.Set(4, 8, WWoodD);
            c.Rect(5, 2, 17, 3, SnowW); c.HLine(6, 21, 4, SnowS);
            c.Outline(WOutline);
            return c.WithPivot(12, 2f);
        }

        static PixelCanvas DrawWorkbench()
        {
            var c = new PixelCanvas(36, 26);
            c.Rect(2, 8, 32, 5, WWoodL); c.HLine(2, 33, 8, PixelCanvas.Shade(WWoodL, 1.08f)); c.HLine(2, 33, 12, WWoodD);
            c.Rect(4, 13, 3, 11, WWoodD); c.Rect(29, 13, 3, 11, WWoodD);
            c.Rect(4, 19, 28, 2, WWood);
            // Saw, hammer and a vice.
            c.Rect(8, 5, 11, 3, PixelCanvas.Hex("#c2c7d4")); c.HLine(8, 18, 7, PixelCanvas.Hex("#8e93a3"));
            c.Rect(19, 5, 3, 3, WWood);
            c.Rect(24, 6, 6, 2, WWoodD); c.Rect(28, 3, 3, 4, PixelCanvas.Hex("#8e93a3"));
            c.Rect(2, 4, 4, 4, PixelCanvas.Hex("#6e7384"));
            c.Set(12, 13, WWoodL); c.Set(20, 14, WWoodL); // shavings
            c.HLine(2, 7, 7, SnowW); c.HLine(30, 33, 7, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(18, 2f);
        }

        /// <summary>Green iron street lamp with a warm lantern and a little snow on its cap.</summary>
        static PixelCanvas DrawLamp()
        {
            var c = new PixelCanvas(14, 46);
            c.Ellipse(7, 44, 4, 1.2f, PixelCanvas.WithAlpha(SnowSD, 150));
            c.Rect(4, 40, 6, 4, LampGD); c.HLine(4, 9, 40, LampG);
            c.Rect(6, 14, 2, 26, LampG); c.VLine(7, 14, 39, LampGD);
            c.Rect(5, 22, 4, 1, LampG);
            c.Rect(3, 12, 8, 2, LampGD);
            c.Rect(3, 5, 8, 7, Warm); c.Rect(4, 6, 3, 3, WarmL);
            c.VLine(3, 5, 11, LampGD); c.VLine(10, 5, 11, LampGD); c.VLine(7, 5, 11, LampGD);
            c.Rect(2, 2, 10, 3, LampG); c.HLine(4, 9, 1, LampGD);
            c.HLine(2, 11, 2, SnowW); c.HLine(4, 9, 1, SnowW); c.Set(6, 0, SnowW); c.Set(7, 0, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(7, 2f);
        }

        static PixelCanvas DrawMailbox()
        {
            var c = new PixelCanvas(14, 22);
            var red = PixelCanvas.Hex("#c8463c");
            c.Rect(6, 10, 2, 11, WWood); c.VLine(7, 10, 20, WWoodD);
            c.Rect(2, 4, 10, 7, red); c.HLine(2, 11, 10, PixelCanvas.Shade(red, 0.75f));
            c.Rect(3, 6, 5, 1, PixelCanvas.Shade(red, 0.7f));
            c.Rect(11, 3, 2, 4, Yellow);
            c.Rect(2, 2, 10, 2, SnowW); c.HLine(2, 11, 4, SnowS);
            c.Outline(WOutline);
            return c.WithPivot(7, 1.5f);
        }

        static PixelCanvas DrawFlowerPot()
        {
            var c = new PixelCanvas(14, 14);
            c.Rect(3, 8, 8, 5, Terracotta); c.Rect(2, 7, 10, 2, PixelCanvas.Shade(Terracotta, 1.12f));
            c.VLine(10, 8, 12, PixelCanvas.Shade(Terracotta, 0.78f));
            c.Circle(7, 5, 3.6f, Pine2); c.PaintEllipse(6, 4, 2, 1.5f, Pine1);
            c.Set(5, 5, Red); c.Set(8, 6, Red); c.Set(7, 3, Red);
            c.HLine(4, 9, 2, SnowW); c.Set(6, 1, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(7, 1.5f);
        }

        static PixelCanvas DrawWoodpile()
        {
            var c = new PixelCanvas(30, 20);
            var ring = PixelCanvas.Hex("#dcb488");
            c.Ellipse(15, 18, 13, 1.6f, PixelCanvas.WithAlpha(SnowSD, 140));
            for (int row = 0; row < 3; row++)
            {
                int n = 4 - row;
                for (int i = 0; i < n; i++)
                {
                    float x = 6 + i * 6 + row * 3, y = 15 - row * 5;
                    c.Circle(x, y, 3, WWood);
                    c.Circle(x, y, 2, ring);
                    c.Set(Mathf.RoundToInt(x), Mathf.RoundToInt(y), WWoodD);
                }
            }
            c.Ellipse(15, 5, 7, 2, SnowW); c.Ellipse(9, 10, 4, 1.2f, SnowW); c.Ellipse(22, 10, 4, 1.2f, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(15, 2f);
        }

        static PixelCanvas DrawSnowBarrel()
        {
            var c = new PixelCanvas(14, 18);
            c.Ellipse(7, 16, 6, 1.4f, PixelCanvas.WithAlpha(SnowSD, 140));
            c.Rect(2, 5, 10, 11, WWood);
            c.VLine(2, 6, 14, WWoodL); c.VLine(11, 6, 14, WWoodD);
            c.HLine(2, 11, 7, LampGD); c.HLine(2, 11, 13, LampGD);
            c.Ellipse(7, 5, 5, 1.8f, SnowW); c.HLine(3, 10, 6, SnowS);
            c.Outline(WOutline);
            return c.WithPivot(7, 2f);
        }

        static PixelCanvas DrawSnowCrate()
        {
            var c = new PixelCanvas(16, 16);
            c.Rect(1, 4, 14, 11, WWood);
            c.HLine(1, 14, 9, WWoodD); c.VLine(1, 4, 14, WWoodL); c.VLine(14, 4, 14, WWoodD);
            c.Line(2, 5, 13, 13, WWoodD);
            c.Rect(1, 2, 14, 3, SnowW); c.HLine(1, 14, 4, SnowS);
            c.Outline(WOutline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawSnowSign()
        {
            var c = new PixelCanvas(16, 18);
            c.Rect(7, 10, 2, 7, WWoodD);
            c.Rect(1, 3, 14, 8, WWood);
            c.HLine(1, 14, 10, WWoodDD);
            c.HLine(3, 12, 6, WWoodDD); c.HLine(3, 9, 8, WWoodDD);
            c.Rect(1, 1, 14, 3, SnowW); c.HLine(1, 14, 3, SnowS);
            c.Outline(WOutline);
            return c.WithPivot(8, 1.5f);
        }

        /// <summary>North exit: a snowy timber arch with lanterns, the way beyond closed by a drift and a barrier.</summary>
        static PixelCanvas DrawSnowGate()
        {
            var c = new PixelCanvas(64, 54);
            // Posts.
            c.Rect(6, 10, 6, 42, WWood); c.VLine(6, 10, 51, WWoodL); c.VLine(11, 10, 51, WWoodD);
            c.Rect(52, 10, 6, 42, WWood); c.VLine(52, 10, 51, WWoodL); c.VLine(57, 10, 51, WWoodD);
            // Beams.
            c.Rect(0, 6, 64, 5, WWood); c.HLine(0, 63, 10, WWoodDD);
            c.Rect(4, 15, 56, 3, WWoodD);
            c.Rect(0, 3, 64, 4, SnowW); c.HLine(0, 63, 6, SnowS);
            c.Rect(5, 14, 54, 1, SnowW);
            // Hanging lanterns.
            foreach (int lx in new[] { 18, 44 })
            {
                c.VLine(lx + 1, 18, 21, WOutline);
                c.Rect(lx, 22, 3, 4, Warm); c.Set(lx + 1, 23, WarmL);
            }
            // Barrier and drift closing the path for now.
            c.Rect(14, 38, 36, 3, WWoodL); c.Rect(14, 44, 36, 3, WWoodL);
            c.HLine(14, 49, 40, WWoodD); c.HLine(14, 49, 46, WWoodD);
            c.Ellipse(32, 50, 22, 4, SnowW); c.Ellipse(32, 51, 20, 2.5f, SnowL);
            c.Outline(WOutline);
            return c.WithPivot(32, 3.5f);
        }

        /// <summary>Wooden fence with snow on the rails and posts (mask 1 = left, 2 = right, 4 = up neighbour).</summary>
        static PixelCanvas DrawSnowFence(int mask)
        {
            var c = new PixelCanvas(16, 18);
            bool left = (mask & 1) != 0, right = (mask & 2) != 0, up = (mask & 4) != 0;
            if (left) { c.Rect(0, 7, 7, 2, WWood); c.Rect(0, 12, 7, 2, WWood); c.HLine(0, 6, 6, SnowW); c.HLine(0, 6, 11, SnowW); }
            if (right) { c.Rect(9, 7, 7, 2, WWood); c.Rect(9, 12, 7, 2, WWood); c.HLine(9, 15, 6, SnowW); c.HLine(9, 15, 11, SnowW); }
            if (up) { c.Rect(7, 0, 2, 5, WWood); c.VLine(7, 0, 4, WWoodL); }
            c.Rect(6, 4, 4, 12, WWood);
            c.VLine(9, 5, 15, WWoodD);
            c.HLine(6, 9, 4, WWoodL);
            c.Rect(6, 2, 4, 2, SnowW); c.Set(7, 1, SnowW); c.Set(8, 1, SnowW);
            c.Outline(WOutline);
            return c.WithPivot(8, 1.5f);
        }

        /// <summary>Raised garden bed under snow with winter cabbages peeking out.</summary>
        static PixelCanvas DrawGardenBed(int v)
        {
            var c = new PixelCanvas(16, 14);
            c.Rect(1, 5, 14, 8, WWoodD); c.HLine(1, 14, 5, WWood);
            c.Rect(2, 6, 12, 6, PixelCanvas.Hex("#6a5048"));
            c.Rect(2, 5, 12, 3, SnowW); c.HLine(2, 13, 8, SnowS);
            var cab = PixelCanvas.Hex("#7fae7a");
            var cabP = PixelCanvas.Hex("#a07bb0");
            c.Circle(5 + v % 2, 8, 2.2f, v % 2 == 0 ? cab : cabP); c.Set(5 + v % 2, 7, SnowW);
            c.Circle(11 - v % 2, 8.5f, 2f, v % 2 == 0 ? cabP : cab);
            c.Outline(WOutline);
            return c.WithPivot(8, 1.5f);
        }
    }
}
