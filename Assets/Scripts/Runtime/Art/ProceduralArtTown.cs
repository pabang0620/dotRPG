using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution art for the village town and the forest hunting ground (keys "town_*").
    /// Drawn at 32 pixels per tile (<see cref="PixelCanvas.Density"/> = 2), twice the detail of the
    /// original 16px art: leafy tree crowns built from many shaded clusters, round stones, planks with
    /// grain, one-pixel outlines in dark local colours, light from the top-left everywhere.
    /// Buildings live in ProceduralArtTownBuildings.cs.
    /// </summary>
    public static partial class ProceduralArt
    {
        static PixelCanvas Hd(int w, int h) => new PixelCanvas(w, h) { Density = 2 };

        static Color32[] Ramp(params string[] hex)
        {
            var r = new Color32[hex.Length];
            for (int i = 0; i < hex.Length; i++) r[i] = PixelCanvas.Hex(hex[i]);
            return r;
        }

        // ---------- Palette (dark → light ramps) ----------

        static readonly Color32[] TLeaf = Ramp("#1f4d2b", "#2d6a36", "#3f8a43", "#56a64f", "#74c162", "#9ad57a");
        static readonly Color32[] TLeafForest = Ramp("#10281a", "#183a23", "#224f2e", "#2e663a", "#3f7e46", "#5a9a58");
        static readonly Color32[] TLeafFruit = Ramp("#2a5a2a", "#3b7a36", "#4f9a44", "#68b453", "#88cc68", "#ace285");
        static readonly Color32[] TPine = Ramp("#123528", "#1b4a36", "#276245", "#377c56", "#4f9a69", "#70b884");
        static readonly Color32[] TBush = Ramp("#224f2c", "#316a37", "#438846", "#5ba454", "#7cbf68");
        static readonly Color32[] TBark = Ramp("#3a2415", "#55361f", "#72482a", "#8e5c37", "#a87349");
        static readonly Color32[] TBarkGrey = Ramp("#35302e", "#4f4741", "#6b6158", "#877b70", "#a2968a");
        static readonly Color32[] TWood = Ramp("#4a2e1e", "#684127", "#875633", "#a56d41", "#c28954", "#daa56f");
        static readonly Color32[] TStone = Ramp("#4c4847", "#68635f", "#86807a", "#a49d94", "#c1baaf", "#dbd5ca");
        static readonly Color32[] TIron = Ramp("#22262c", "#343a42", "#4b535d", "#69737e", "#8f99a3");
        static readonly Color32 TOutline = PixelCanvas.Hex("#2a1e18");
        static readonly Color32 TOutlineLeaf = PixelCanvas.Hex("#132b1b");
        static readonly Color32 TOutlineStone = PixelCanvas.Hex("#302c2b");
        static readonly Color32 TShadow = new Color32(18, 30, 16, 78);
        static readonly Color32 TMoss = PixelCanvas.Hex("#5f8f43");
        static readonly Color32 TMossLight = PixelCanvas.Hex("#7fb05a");

        static int RampIndex(float lit, int n)
        {
            float t = Mathf.Clamp01((lit + 0.25f) / 1.3f);
            return Mathf.Min(n - 1, (int)(t * n));
        }

        /// <summary>Ellipse shaded like a ball lit from the top-left, quantised to a colour ramp.</summary>
        static void ShadeBlob(PixelCanvas c, float cx, float cy, float rx, float ry, Color32[] ramp, float bias = 0f, bool rim = true)
        {
            int x0 = Mathf.FloorToInt(cx - rx) - 1, x1 = Mathf.CeilToInt(cx + rx) + 1;
            int y0 = Mathf.FloorToInt(cy - ry) - 1, y1 = Mathf.CeilToInt(cy + ry) + 1;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                    float d = nx * nx + ny * ny;
                    if (d > 1f) continue;
                    float nz = Mathf.Sqrt(1f - d);
                    float lit = -0.5f * nx - 0.6f * ny + 0.62f * nz + bias;
                    int idx = RampIndex(lit, ramp.Length);
                    if (rim && d > 0.8f && ny > 0f) idx = Mathf.Max(0, idx - 1);
                    c.Set(x, y, ramp[idx]);
                }
        }

        /// <summary>
        /// Leafy crown: a dark mass, bumpy clusters around the rim and more clusters inside, each lit
        /// from the top-left; upper clusters a little brighter. Drawn top to bottom so lower clusters overlap.
        /// </summary>
        static void Canopy(PixelCanvas c, float cx, float cy, float rx, float ry, Color32[] ramp, int seed, int inner = 14)
        {
            var rng = new System.Random(seed);
            ShadeBlob(c, cx, cy + ry * 0.04f, rx * 0.95f, ry * 0.93f, new[] { ramp[0], ramp[1], ramp[1], ramp[2] }, 0f, false);
            var clumps = new List<Vector3>();
            int rimN = 10 + seed % 3;
            for (int k = 0; k < rimN; k++)
            {
                float a = (k + (float)rng.NextDouble() * 0.6f) / rimN * Mathf.PI * 2f;
                float r = Mathf.Min(rx, ry) * (0.3f + 0.1f * (float)rng.NextDouble());
                clumps.Add(new Vector3(cx + Mathf.Cos(a) * (rx - r * 0.85f), cy + Mathf.Sin(a) * (ry - r * 0.85f), r));
            }
            for (int k = 0; k < inner; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rng.NextDouble()) * 0.58f;
                float r = Mathf.Min(rx, ry) * (0.3f + 0.15f * (float)rng.NextDouble());
                clumps.Add(new Vector3(cx + Mathf.Cos(a) * rx * d, cy + Mathf.Sin(a) * ry * d, r));
            }
            clumps.Sort((p, q) => p.y.CompareTo(q.y));
            foreach (var p in clumps) ShadeBlob(c, p.x, p.y, p.z, p.z * 0.9f, ramp, -(p.y - cy) / ry * 0.28f);
            // Tiny sparkles of light on the upper-left clusters.
            for (int k = 0; k < 18; k++)
            {
                int x = Mathf.RoundToInt(cx + ((float)rng.NextDouble() - 0.7f) * rx * 1.2f);
                int y = Mathf.RoundToInt(cy + ((float)rng.NextDouble() - 0.75f) * ry * 1.1f);
                if (c.IsOpaque(x, y) && c.IsOpaque(x + 1, y)) { c.Set(x, y, ramp[ramp.Length - 1]); }
            }
        }

        /// <summary>Tapered trunk, lit on its left, flaring into roots at the bottom.</summary>
        static void Trunk(PixelCanvas c, float cx, int top, int bottom, float halfTop, float halfBottom, Color32[] bark)
        {
            for (int y = top; y <= bottom; y++)
            {
                float t = (y - top) / (float)Mathf.Max(1, bottom - top);
                float hw = Mathf.Lerp(halfTop, halfBottom, t * t * t);
                int xa = Mathf.FloorToInt(cx - hw), xb = Mathf.CeilToInt(cx + hw) - 1;
                for (int x = xa; x <= xb; x++)
                {
                    float u = (x + 0.5f - (cx - hw)) / (2f * hw);
                    int idx = u < 0.22f ? 3 : u < 0.5f ? 2 : u < 0.78f ? 1 : 0;
                    if ((x * 7 + (y / 4) * 3) % 11 == 0) idx = Mathf.Max(0, idx - 1);   // bark grain
                    c.Set(x, y, bark[Mathf.Min(bark.Length - 1, idx)]);
                }
            }
        }

        static void ThickLine(PixelCanvas c, float x0, float y0, float x1, float y1, float r0, float r1, Color32 col)
        {
            float len = Mathf.Max(1f, Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)));
            int steps = Mathf.CeilToInt(len * 1.5f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                c.Circle(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), Mathf.Lerp(r0, r1, t), col);
            }
        }

        // ---------- Dispatch ----------

        /// <summary>town_* keys: the 32px town / forest objects.</summary>
        static PixelCanvas DrawTown(string[] p)
        {
            int Arg(int i, int fallback = 0) => p.Length > i ? int.Parse(p[i]) : fallback;
            switch (p[1])
            {
                case "tree": return TownTree(Arg(2));
                case "fruit": return FruitTree(Arg(2));
                case "pine": return TownPine(Arg(2));
                case "dead": return DeadTree(Arg(2));
                case "chop": return TownTree(6);
                case "stump": return TownStump();
                case "rock": return TownRock(Arg(2));
                case "pebbles": return TownPebbles(Arg(2));
                case "bush": return TownBush(Arg(2));
                case "fence": return TownFence(Arg(2));
                case "lamp": return TownLamp();
                case "bench": return TownBench();
                case "barrel": return TownBarrel();
                case "crate": return TownCrate(false);
                case "ccrate": return TownCrate(true);
                case "sacks": return TownSacks();
                case "hay": return TownHay();
                case "woodpile": return TownWoodpile();
                case "pot": return TownPot();
                case "planter": return TownPlanter();
                case "mailbox": return TownMailbox();
                case "sign": return TownSign(Arg(2));
                case "board": return TownBoard();
                case "anvil": return TownAnvil();
                case "crop": return TownCrop(Arg(2));
                case "chest": return TownChest(Arg(2) == 1);
                case "grave": return TownGrave(Arg(2));
                case "bones": return TownBones();
                case "log": return TownLog();
                case "shroom": return TownShrooms();
                case "ruin": return TownRuin();
                case "pile": return TownPile(Arg(2) == 1);
                case "house": return TownHouse(Arg(2));
                case "hall": return TownHall();
                case "store": return TownStore();
                case "smithy": return TownSmithy();
                case "warehouse": return TownWarehouse();
                case "site": return Arg(2) == 1 ? TownWorkshop() : TownBlueprint();
                case "fountain": return TownFountain(Arg(2));
                case "well": return TownWell();
            }
            return null;
        }
    }
}
