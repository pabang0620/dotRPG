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
    }
}
