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

        // ---------- Trees ----------

        /// <summary>Round broadleaf tree. 0-2 town greens, 3-5 darker forest trees, 6 = the choppable tree.</summary>
        static PixelCanvas TownTree(int v)
        {
            bool forest = v >= 3 && v <= 5;
            var leaf = forest ? TLeafForest : TLeaf;
            int w = forest ? 90 : 80, h = forest ? 106 : 96;
            var c = Hd(w, h);
            float cx = w / 2f;
            int baseY = h - 6;
            c.Ellipse(cx + 4f, baseY - 0.5f, w * 0.34f, 6.5f, TShadow);
            float rx = forest ? 40f : 34f + (v % 3) * 1.5f, ry = forest ? 36f : 30f + (v % 3) * 2f;
            float cy = ry + 4f;
            Trunk(c, cx, (int)(cy + ry * 0.45f), baseY, forest ? 6f : 5f, forest ? 10.5f : 9f, TBark);
            // A branch fork showing under the crown.
            ThickLine(c, cx - 1f, cy + ry * 0.62f, cx - 9f, cy + ry * 0.35f, 2.2f, 1.4f, TBark[1]);
            ThickLine(c, cx + 1f, cy + ry * 0.66f, cx + 10f, cy + ry * 0.42f, 2.2f, 1.4f, TBark[0]);
            Canopy(c, cx, cy, rx, ry, leaf, 100 + v * 17, forest ? 16 : 14);
            if (v == 6)
            {
                // The choppable tree: an axe notch on the trunk.
                c.Rect((int)cx - 2, baseY - 12, 4, 3, TWood[4]);
                c.HLine((int)cx - 2, (int)cx + 1, baseY - 12, TWood[5]);
            }
            c.Outline(TOutlineLeaf);
            return c.WithPivot(cx, 6f);
        }

        static PixelCanvas FruitTree(int v)
        {
            var c = Hd(78, 92);
            float cx = 39f;
            int baseY = 86;
            c.Ellipse(cx + 4f, baseY - 0.5f, 26f, 6f, TShadow);
            float rx = 32f, ry = 28f, cy = 33f;
            Trunk(c, cx, (int)(cy + ry * 0.45f), baseY, 4.5f, 8.5f, TBark);
            Canopy(c, cx, cy, rx, ry, TLeafFruit, 300 + v * 13, 13);
            var rng = new System.Random(71 + v);
            var fruit = v == 1 ? PixelCanvas.Hex("#f59a2a") : PixelCanvas.Hex("#d9362d");
            var fruitLight = v == 1 ? PixelCanvas.Hex("#ffc867") : PixelCanvas.Hex("#ff8c72");
            for (int k = 0; k < 13; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.25f + (float)rng.NextDouble() * 0.62f;
                float fx = cx + Mathf.Cos(a) * rx * d, fy = cy + Mathf.Sin(a) * ry * d;
                c.Circle(fx, fy, 2.2f, fruit);
                c.Set(Mathf.FloorToInt(fx - 1f), Mathf.FloorToInt(fy - 1f), fruitLight);
                c.Set(Mathf.FloorToInt(fx), Mathf.FloorToInt(fy - 2.6f), PixelCanvas.Hex("#3b2a1a"));
            }
            c.Outline(TOutlineLeaf);
            return c.WithPivot(cx, 6f);
        }

        /// <summary>Conifer built from five scalloped tiers, lit from the left.</summary>
        static PixelCanvas TownPine(int v)
        {
            var c = Hd(62, 104);
            float cx = 31f;
            int baseY = 98;
            c.Ellipse(cx + 4f, baseY - 0.5f, 20f, 5.5f, TShadow);
            Trunk(c, cx, 80, baseY, 3.5f, 6.5f, TBark);
            float scale = 1f + (v % 2) * 0.06f;
            int[,] tiers = { { 4, 30, 10 }, { 16, 46, 15 }, { 30, 62, 20 }, { 44, 78, 25 }, { 58, 90, 29 } };
            for (int k = tiers.GetLength(0) - 1; k >= 0; k--)
            {
                int top = tiers[k, 0], bot = tiers[k, 1];
                float hwMax = tiers[k, 2] * scale;
                for (int y = top; y <= bot + 3; y++)
                {
                    float p = Mathf.Clamp01((y - top) / (float)(bot - top));
                    float hw = hwMax * (0.12f + 0.88f * p);
                    for (int x = Mathf.FloorToInt(cx - hw); x <= Mathf.CeilToInt(cx + hw); x++)
                    {
                        // Scalloped lower edge: points hanging below the tier.
                        if (y > bot)
                        {
                            float tooth = Mathf.Sin((x - cx) * 0.95f + k * 1.3f);
                            if (tooth < 0.35f + (y - bot) * 0.2f) continue;
                        }
                        float u = (x + 0.5f - (cx - hw)) / (2f * hw + 0.01f);
                        int idx = u < 0.28f ? 4 : u < 0.52f ? 3 : u < 0.78f ? 2 : 1;
                        if (p < 0.25f && u < 0.6f) idx = 5;
                        if (y >= bot - 1) idx = Mathf.Max(0, idx - 2);
                        if (((x * 3 + y * 2) % 9 == 0) && idx >= 2) idx = Mathf.Min(5, idx + 1);   // needle glints
                        c.Set(x, y, TPine[idx]);
                    }
                }
            }
            c.Outline(TOutlineLeaf);
            return c.WithPivot(cx, 6f);
        }

        /// <summary>Bare, grey dead tree with crooked branches (forest).</summary>
        static PixelCanvas DeadTree(int v)
        {
            var c = Hd(64, 92);
            float cx = 32f;
            int baseY = 86;
            c.Ellipse(cx + 4f, baseY - 0.5f, 18f, 5f, TShadow);
            Trunk(c, cx, 34, baseY, 4f, 8f, TBarkGrey);
            var rng = new System.Random(900 + v);
            void Branch(float x, float y, float ang, float len, float r, int depth)
            {
                float x1 = x + Mathf.Cos(ang) * len, y1 = y - Mathf.Sin(ang) * len;
                ThickLine(c, x, y, x1, y1, r, Mathf.Max(0.7f, r * 0.6f), TBarkGrey[depth == 0 ? 2 : 1]);
                if (depth >= 2) return;
                Branch(x1, y1, ang + 0.5f + (float)rng.NextDouble() * 0.3f, len * 0.6f, r * 0.6f, depth + 1);
                Branch(x1, y1, ang - 0.45f - (float)rng.NextDouble() * 0.3f, len * 0.55f, r * 0.6f, depth + 1);
            }
            Branch(cx, 40, Mathf.PI * 0.62f, 22f, 3f, 0);
            Branch(cx, 46, Mathf.PI * 0.3f, 20f, 2.6f, 0);
            Branch(cx, 36, Mathf.PI * 0.5f, 18f, 2.6f, 1);
            c.Outline(TOutline);
            return c.WithPivot(cx, 6f);
        }

        static PixelCanvas TownStump()
        {
            var c = Hd(36, 28);
            c.Ellipse(19f, 22f, 15f, 4.5f, TShadow);
            Trunk(c, 18f, 8, 23, 10f, 13f, TBark);
            c.Ellipse(18f, 9f, 10.5f, 4.5f, PixelCanvas.Hex("#d8b07c"));
            c.Ellipse(18f, 9f, 7f, 3f, PixelCanvas.Hex("#c8985f"));
            c.Ellipse(18f, 9f, 4f, 1.6f, PixelCanvas.Hex("#d8b07c"));
            c.Set(18, 9, PixelCanvas.Hex("#a97a48"));
            c.Outline(TOutline);
            return c.WithPivot(18f, 5f);
        }

        /// <summary>Mineable boulder (0-1) built from shaded lumps with moss on top.</summary>
        static PixelCanvas TownRock(int v)
        {
            var c = Hd(46, 38);
            c.Ellipse(24f, 32f, 20f, 5f, TShadow);
            if (v % 2 == 0)
            {
                ShadeBlob(c, 22f, 21f, 17f, 13f, TStone);
                ShadeBlob(c, 13f, 26f, 10f, 7.5f, TStone);
                ShadeBlob(c, 32f, 25f, 10f, 8f, TStone);
            }
            else
            {
                ShadeBlob(c, 23f, 18f, 13f, 15f, TStone);
                ShadeBlob(c, 13f, 26f, 9f, 7f, TStone);
                ShadeBlob(c, 33f, 27f, 8f, 6f, TStone);
            }
            // Cracks and moss.
            c.Line(20, 16, 24, 22, TStone[1]); c.Line(24, 22, 23, 27, TStone[1]);
            for (int x = 14; x < 30; x++)
            {
                int y = 9 + (int)(2f * Mathf.Sin(x * 0.7f)) + (v == 1 ? -3 : 0);
                if (!c.IsOpaque(x, y)) continue;
                c.Paint(x, y, TMoss); c.Paint(x, y + 1, TMoss);
                if (x % 3 == 0) c.Paint(x, y - 1, TMossLight);
            }
            c.Outline(TOutlineStone);
            return c.WithPivot(23f, 5f);
        }

        /// <summary>A few small decorative stones.</summary>
        static PixelCanvas TownPebbles(int v)
        {
            var c = Hd(30, 18);
            ShadeBlob(c, 10f, 10f, 6f, 4.5f, TStone);
            ShadeBlob(c, 19f, 12f, 4.5f, 3.5f, TStone);
            if (v == 1) ShadeBlob(c, 25f, 9f, 3f, 2.5f, TStone);
            c.Outline(TOutlineStone);
            return c.WithPivot(15f, 4f);
        }

        static PixelCanvas TownBush(int v)
        {
            var c = Hd(44, 34);
            c.Ellipse(23f, 29f, 18f, 4.5f, TShadow);
            var rng = new System.Random(500 + v);
            var parts = new List<Vector3> { new Vector3(14, 20, 10), new Vector3(29, 20, 10.5f), new Vector3(22, 14, 11), new Vector3(20, 23, 9), new Vector3(32, 25, 7), new Vector3(10, 25, 7) };
            parts.Sort((a, b) => a.y.CompareTo(b.y));
            foreach (var p in parts) ShadeBlob(c, p.x + (float)rng.NextDouble() * 2f, p.y, p.z, p.z * 0.85f, TBush, -(p.y - 20f) / 12f * 0.3f);
            if (v == 1)
            {
                // Berries.
                for (int k = 0; k < 9; k++)
                {
                    float bx = 10f + (float)rng.NextDouble() * 24f, by = 11f + (float)rng.NextDouble() * 14f;
                    if (!c.IsOpaque(Mathf.RoundToInt(bx), Mathf.RoundToInt(by))) continue;
                    c.Circle(bx, by, 1.6f, PixelCanvas.Hex("#c8323f"));
                    c.Set(Mathf.FloorToInt(bx - 0.5f), Mathf.FloorToInt(by - 0.5f), PixelCanvas.Hex("#ff8a8a"));
                }
            }
            else if (v == 2)
            {
                // Little white flowers.
                for (int k = 0; k < 8; k++)
                {
                    int bx = 10 + rng.Next(0, 24), by = 11 + rng.Next(0, 13);
                    if (!c.IsOpaque(bx, by)) continue;
                    c.Set(bx, by, White); c.Set(bx + 1, by, White); c.Set(bx, by + 1, PixelCanvas.Hex("#ffe066"));
                }
            }
            c.Outline(TOutlineLeaf);
            return c.WithPivot(22f, 5f);
        }

        // ---------- Fences ----------

        /// <summary>Wooden fence piece. mask: 1 = joins west, 2 = east, 4 = north, 8 = south.</summary>
        static PixelCanvas TownFence(int mask)
        {
            var c = Hd(32, 46);
            int baseY = 41;
            var w = TWood;
            void RailH(int x0, int x1, int y)
            {
                c.HLine(x0, x1, y, w[4]); c.HLine(x0, x1, y + 1, w[3]); c.HLine(x0, x1, y + 2, w[2]); c.HLine(x0, x1, y + 3, w[1]);
            }
            if ((mask & 1) != 0) { RailH(0, 15, baseY - 24); RailH(0, 15, baseY - 12); }
            if ((mask & 2) != 0) { RailH(16, 31, baseY - 24); RailH(16, 31, baseY - 12); }
            if ((mask & 4) != 0)
            {
                // Rails running north, seen from above: a lit top face and a darker side.
                c.Rect(14, 0, 2, baseY - 22, w[4]); c.Rect(16, 0, 2, baseY - 22, w[2]);
            }
            if ((mask & 8) != 0) { c.Rect(14, baseY - 22, 2, 22 + 4, w[4]); c.Rect(16, baseY - 22, 2, 22 + 4, w[2]); }
            // Post with a rounded cap.
            c.Rect(12, baseY - 30, 8, 30, w[2]);
            c.Rect(12, baseY - 30, 3, 30, w[3]);
            c.Rect(18, baseY - 30, 2, 30, w[1]);
            c.HLine(12, 19, baseY - 31, w[4]);
            c.HLine(13, 18, baseY - 32, w[5]);
            c.HLine(12, 19, baseY - 1, w[0]);
            c.Outline(TOutline);
            return c.WithPivot(16f, 5f);
        }

        // ---------- Street furniture and props ----------

        static PixelCanvas TownLamp()
        {
            var c = Hd(20, 76);
            int baseY = 72;
            var iron = PixelCanvas.Hex("#2c4a3c");
            var ironLight = PixelCanvas.Hex("#4d7a62");
            c.Ellipse(11f, baseY - 0.5f, 8f, 2.5f, TShadow);
            c.Rect(6, baseY - 5, 8, 5, iron); c.HLine(6, 13, baseY - 5, ironLight);             // foot
            c.Rect(9, 18, 3, baseY - 22, iron); c.VLine(9, 18, baseY - 6, ironLight);           // post
            c.Rect(8, 30, 5, 2, iron);                                                            // collar
            // Lantern: cap, warm glass in a cage, bottom plate.
            c.Rect(5, 4, 11, 3, iron); c.HLine(6, 14, 3, ironLight); c.Rect(9, 1, 3, 3, iron);
            c.Rect(6, 7, 9, 10, PixelCanvas.Hex("#ffd98a"));
            c.Rect(7, 8, 3, 7, PixelCanvas.Hex("#fff3c9"));
            c.VLine(6, 7, 16, iron); c.VLine(14, 7, 16, iron); c.VLine(10, 7, 16, iron);
            c.Rect(5, 17, 11, 2, iron);
            c.Outline(TOutline);
            return c.WithPivot(10.5f, 4f);
        }

        static PixelCanvas TownBench()
        {
            var c = Hd(66, 36);
            var w = TWood;
            c.Ellipse(34f, 31f, 28f, 4f, TShadow);
            // Back rest: two boards on posts.
            c.Rect(6, 4, 54, 5, w[3]); c.HLine(6, 59, 4, w[5]); c.HLine(6, 59, 8, w[1]);
            c.Rect(6, 11, 54, 4, w[3]); c.HLine(6, 59, 11, w[4]); c.HLine(6, 59, 14, w[1]);
            c.Rect(9, 4, 3, 16, w[2]); c.Rect(54, 4, 3, 16, w[2]);
            // Seat seen from above, then its front edge.
            c.Rect(4, 17, 58, 7, w[4]); c.HLine(4, 61, 17, w[5]); c.HLine(4, 61, 20, w[3]);
            c.Rect(4, 24, 58, 3, w[2]);
            // Legs with iron brackets.
            c.Rect(8, 27, 4, 6, w[1]); c.Rect(54, 27, 4, 6, w[1]);
            c.Rect(8, 24, 4, 2, TIron[2]); c.Rect(54, 24, 4, 2, TIron[2]);
            c.Outline(TOutline);
            return c.WithPivot(33f, 4f);
        }

        static PixelCanvas TownBarrel()
        {
            var c = Hd(28, 34);
            c.Ellipse(15f, 30f, 12f, 3.5f, TShadow);
            var w = TWood;
            for (int y = 5; y <= 30; y++)
            {
                float t = (y - 5) / 25f;
                float hw = 10f + 2.2f * Mathf.Sin(t * Mathf.PI);
                for (int x = Mathf.FloorToInt(14f - hw); x <= Mathf.CeilToInt(14f + hw) - 1; x++)
                {
                    float u = (x + 0.5f - (14f - hw)) / (2f * hw);
                    int idx = u < 0.25f ? 4 : u < 0.55f ? 3 : u < 0.8f ? 2 : 1;
                    if ((x - 2) % 5 == 0) idx = Mathf.Max(0, idx - 1);               // staves
                    c.Set(x, y, w[idx]);
                }
            }
            foreach (int hy in new[] { 9, 25 })
                for (int x = 1; x < 27; x++)
                    if (c.IsOpaque(x, hy)) { c.Set(x, hy, TIron[x < 9 ? 3 : 2]); c.Set(x, hy + 1, TIron[1]); }
            c.Ellipse(14f, 5.5f, 10f, 3.2f, w[4]);
            c.Ellipse(14f, 5.5f, 7.5f, 2f, w[3]);
            c.Outline(TOutline);
            return c.WithPivot(14f, 3f);
        }

        static PixelCanvas TownCrate(bool carrots)
        {
            var c = Hd(32, 32);
            var w = TWood;
            c.Ellipse(17f, 28f, 14f, 3.5f, TShadow);
            c.Rect(3, 12, 26, 16, w[2]);                                          // front
            c.Rect(3, 4, 26, 8, w[4]);                                            // top
            c.HLine(3, 28, 4, w[5]);
            c.HLine(3, 28, 12, w[1]);
            for (int y = 16; y < 28; y += 5) c.HLine(3, 28, y, w[1]);            // front boards
            c.Line(4, 13, 27, 27, w[3]); c.Line(5, 13, 28, 27, w[3]);             // brace
            c.Rect(3, 12, 3, 16, w[1]); c.Rect(26, 12, 3, 16, w[1]);
            if (carrots)
            {
                c.Rect(5, 6, 22, 5, PixelCanvas.Hex("#5a3a22"));
                for (int k = 0; k < 6; k++)
                {
                    int x = 6 + k * 4;
                    c.Rect(x, 6, 3, 3, PixelCanvas.Hex("#f28c28")); c.Set(x, 6, PixelCanvas.Hex("#ffb45a"));
                    c.Set(x + 1, 3, PixelCanvas.Hex("#4f9a3a")); c.Set(x, 4, PixelCanvas.Hex("#6fbb4a")); c.Set(x + 2, 4, PixelCanvas.Hex("#4f9a3a"));
                }
            }
            c.Outline(TOutline);
            return c.WithPivot(16f, 4f);
        }

        static PixelCanvas TownSacks()
        {
            var c = Hd(38, 30);
            var sack = Ramp("#8c7550", "#a88f63", "#c2a978", "#d8c192", "#e8d6ad");
            c.Ellipse(20f, 26f, 16f, 3.5f, TShadow);
            ShadeBlob(c, 12f, 17f, 9f, 9.5f, sack);
            ShadeBlob(c, 25f, 18f, 9.5f, 9f, sack);
            ShadeBlob(c, 19f, 11f, 8f, 7f, sack);
            c.Rect(17, 3, 4, 3, sack[1]); c.HLine(16, 21, 6, PixelCanvas.Hex("#6b4a2a"));
            c.Outline(TOutline);
            return c.WithPivot(19f, 4f);
        }

        static PixelCanvas TownHay()
        {
            var c = Hd(40, 32);
            var hay = Ramp("#a07a2a", "#c49a3e", "#dcb654", "#ecd07a", "#f7e4a4");
            c.Ellipse(21f, 28f, 17f, 3.5f, TShadow);
            for (int y = 6; y <= 27; y++)
                for (int x = 3; x <= 36; x++)
                {
                    float nx = (x - 19.5f) / 17f, ny = (y - 16.5f) / 11f;
                    if (Mathf.Pow(Mathf.Abs(nx), 4f) + Mathf.Pow(Mathf.Abs(ny), 4f) > 1f) continue;
                    float lit = -0.5f * nx - 0.7f * ny;
                    int idx = RampIndex(lit, hay.Length);
                    if ((x * 5 + y * 3) % 7 == 0) idx = Mathf.Max(0, idx - 1);  // straw lines
                    c.Set(x, y, hay[idx]);
                }
            c.VLine(12, 8, 26, hay[0]); c.VLine(27, 8, 26, hay[0]);             // twine
            c.Outline(TOutline);
            return c.WithPivot(20f, 4f);
        }

        static PixelCanvas TownWoodpile()
        {
            var c = Hd(66, 40);
            c.Ellipse(34f, 35f, 29f, 4f, TShadow);
            var endC = PixelCanvas.Hex("#d9ae78");
            var ring = PixelCanvas.Hex("#b8864f");
            void LogEnd(float x, float y, float r)
            {
                c.Circle(x, y, r, TBark[1]);
                c.Circle(x - 0.4f, y - 0.4f, r - 1.4f, endC);
                c.Circle(x - 0.4f, y - 0.4f, r * 0.45f, ring);
                c.Set(Mathf.FloorToInt(x), Mathf.FloorToInt(y), TBark[2]);
            }
            for (int k = 0; k < 5; k++) LogEnd(10f + k * 11.5f, 29f, 5.8f);
            for (int k = 0; k < 4; k++) LogEnd(15.5f + k * 11.5f, 19f, 5.8f);
            for (int k = 0; k < 3; k++) LogEnd(21f + k * 11.5f, 9.5f, 5.8f);
            c.Outline(TOutline);
            return c.WithPivot(33f, 4f);
        }

        static PixelCanvas TownPot()
        {
            var c = Hd(22, 28);
            var pot = Ramp("#8a4a2e", "#a85c38", "#c47448", "#dc9060");
            c.Ellipse(12f, 25f, 8f, 2.5f, TShadow);
            for (int y = 14; y <= 25; y++)
            {
                float hw = Mathf.Lerp(8f, 6f, (y - 14) / 11f);
                for (int x = Mathf.FloorToInt(11f - hw); x <= Mathf.CeilToInt(11f + hw) - 1; x++)
                {
                    float u = (x + 0.5f - (11f - hw)) / (2f * hw);
                    c.Set(x, y, pot[u < 0.3f ? 3 : u < 0.65f ? 2 : 1]);
                }
            }
            c.Rect(2, 12, 18, 3, pot[3]); c.HLine(2, 19, 14, pot[0]);
            ShadeBlob(c, 11f, 8f, 7f, 5f, TBush);
            foreach (var (x, y, col) in new[] { (7, 6, "#ff7fa8"), (12, 4, "#ffe066"), (15, 8, "#ff7fa8"), (9, 9, "#ffffff") })
            {
                var f = PixelCanvas.Hex(col);
                c.Set(x, y, f); c.Set(x + 1, y, f); c.Set(x, y + 1, f); c.Set(x + 1, y + 1, PixelCanvas.Shade(f, 0.8f));
            }
            c.Outline(TOutline);
            return c.WithPivot(11f, 3f);
        }

        static PixelCanvas TownPlanter()
        {
            var c = Hd(36, 26);
            var w = TWood;
            c.Ellipse(19f, 23f, 15f, 3f, TShadow);
            c.Rect(3, 12, 30, 10, w[2]); c.HLine(3, 32, 12, w[4]); c.HLine(3, 32, 21, w[0]);
            c.VLine(11, 13, 20, w[1]); c.VLine(23, 13, 20, w[1]);
            var rng = new System.Random(44);
            for (int k = 0; k < 7; k++) ShadeBlob(c, 7f + k * 3.8f, 9f + rng.Next(0, 3), 3.4f, 3f, TBush);
            string[] cols = { "#ff6f91", "#ffe066", "#ffffff", "#b99bff", "#ff9f43" };
            for (int k = 0; k < 9; k++)
            {
                int x = 5 + k * 3, y = 5 + rng.Next(0, 5);
                var f = PixelCanvas.Hex(cols[k % cols.Length]);
                c.Set(x, y, f); c.Set(x + 1, y, f); c.Set(x, y - 1, PixelCanvas.Shade(f, 1.1f));
            }
            c.Outline(TOutline);
            return c.WithPivot(18f, 3f);
        }

        static PixelCanvas TownMailbox()
        {
            var c = Hd(20, 40);
            var w = TWood;
            c.Ellipse(11f, 37f, 7f, 2f, TShadow);
            c.Rect(8, 16, 4, 22, w[2]); c.VLine(8, 16, 37, w[3]);
            var box = Ramp("#2f4f8a", "#3d65a8", "#5282c8", "#79a4e0");
            c.Rect(2, 4, 16, 12, box[2]);
            c.Ellipse(10f, 5f, 8f, 3.5f, box[3]);
            c.HLine(2, 17, 15, box[0]);
            c.Rect(3, 9, 9, 2, box[0]);
            c.Rect(16, 2, 2, 8, PixelCanvas.Hex("#d8323a")); c.Rect(16, 2, 4, 3, PixelCanvas.Hex("#d8323a"));
            c.Outline(TOutline);
            return c.WithPivot(10f, 3f);
        }

        /// <summary>Signpost: 0 = board sign, 1 = arrow signpost.</summary>
        static PixelCanvas TownSign(int v)
        {
            var c = Hd(34, 40);
            var w = TWood;
            c.Ellipse(18f, 37f, 9f, 2.5f, TShadow);
            c.Rect(15, 16, 4, 22, w[2]); c.VLine(15, 16, 37, w[4]);
            if (v == 1)
            {
                // Arrow boards pointing both ways.
                c.Rect(4, 5, 24, 7, w[4]); c.Set(28, 8, w[4]); c.Line(28, 5, 31, 8, w[4]); c.Line(28, 11, 31, 8, w[4]);
                c.Rect(6, 14, 22, 6, w[3]); c.Line(5, 16, 2, 17, w[3]); c.Line(5, 17, 2, 17, w[3]);
                c.HLine(7, 24, 8, w[1]); c.HLine(9, 24, 17, w[1]);
            }
            else
            {
                c.Rect(3, 4, 28, 16, w[3]);
                c.HLine(3, 30, 4, w[5]); c.HLine(3, 30, 19, w[1]);
                c.Rect(3, 4, 2, 16, w[4]);
                for (int y = 8; y <= 15; y += 3) c.HLine(7, 26 - (y % 2) * 3, y, w[0]);   // writing
            }
            c.Outline(TOutline);
            return c.WithPivot(17f, 3f);
        }

        static PixelCanvas TownBoard()
        {
            var c = Hd(66, 62);
            var w = TWood;
            c.Ellipse(34f, 58f, 28f, 3.5f, TShadow);
            c.Rect(8, 12, 4, 46, w[2]); c.Rect(54, 12, 4, 46, w[2]);
            c.VLine(8, 12, 57, w[4]); c.VLine(54, 12, 57, w[4]);
            // Little roof.
            c.Rect(3, 4, 60, 6, PixelCanvas.Hex("#a0473a")); c.HLine(3, 62, 4, PixelCanvas.Hex("#c8604a")); c.HLine(3, 62, 9, PixelCanvas.Hex("#6e2e26"));
            // Cork board with pinned notes.
            c.Rect(6, 12, 54, 30, PixelCanvas.Hex("#b98a55"));
            c.Rect(6, 12, 54, 2, w[1]);
            foreach (var (x, y, wd, ht, col) in new[] { (10, 16, 13, 16, "#f3ead0"), (26, 15, 12, 12, "#fff6d8"), (41, 17, 15, 11, "#e8f1ff"), (27, 29, 14, 10, "#ffe1d8"), (44, 30, 11, 9, "#f3ead0") })
            {
                var pcol = PixelCanvas.Hex(col);
                c.Rect(x, y, wd, ht, pcol);
                for (int yy = y + 3; yy < y + ht - 1; yy += 3) c.HLine(x + 2, x + wd - 3, yy, PixelCanvas.Hex("#8a7a6a"));
                c.Set(x + wd / 2, y, PixelCanvas.Hex("#d8323a"));
            }
            c.Outline(TOutline);
            return c.WithPivot(33f, 4f);
        }

        static PixelCanvas TownAnvil()
        {
            var c = Hd(46, 38);
            c.Ellipse(24f, 34f, 18f, 3.5f, TShadow);
            Trunk(c, 23f, 20, 34, 8f, 10f, TBark);
            c.Ellipse(23f, 20f, 8f, 3f, PixelCanvas.Hex("#c8985f"));
            // Steel anvil: horn on the left, face, waist and base.
            var st = TIron;
            c.Rect(9, 8, 28, 6, st[3]); c.HLine(9, 36, 8, st[4]); c.HLine(9, 36, 13, st[1]);
            c.Rect(3, 9, 7, 3, st[3]); c.Set(2, 10, st[3]); c.HLine(3, 9, 9, st[4]);
            c.Rect(16, 14, 14, 4, st[2]); c.Rect(13, 17, 20, 3, st[2]);
            // Hammer leaning on it and a glowing hot bar.
            c.Line(34, 3, 40, 12, TWood[3]); c.Line(35, 3, 41, 12, TWood[2]);
            c.Rect(31, 0, 8, 4, st[3]);
            c.Rect(14, 6, 10, 2, PixelCanvas.Hex("#ff9a3a")); c.HLine(15, 22, 6, PixelCanvas.Hex("#ffd66a"));
            c.Outline(TOutline);
            return c.WithPivot(23f, 4f);
        }

        /// <summary>Carrot plot: 0 = ready (leafy top), 1 = sprout, 2 = pulled (hole).</summary>
        static PixelCanvas TownCrop(int stage)
        {
            var c = Hd(28, 28);
            var leaf = Ramp("#2f6a2c", "#3f8a38", "#58a84a", "#7cc460");
            if (stage == 2)
            {
                c.Ellipse(14f, 20f, 7f, 3.5f, PixelCanvas.Hex("#4a2e1c"));
                c.Ellipse(14f, 19.5f, 5f, 2f, PixelCanvas.Hex("#301c10"));
                c.Ellipse(20f, 22f, 3f, 1.5f, PixelCanvas.Hex("#8a5a36"));
                return c.WithPivot(14f, 6f);
            }
            if (stage == 1)
            {
                c.Line(13, 20, 10, 14, leaf[2]); c.Line(14, 20, 17, 13, leaf[3]); c.Line(14, 20, 14, 15, leaf[1]);
                c.Ellipse(10f, 14f, 2.5f, 1.5f, leaf[3]); c.Ellipse(17f, 13f, 2.5f, 1.5f, leaf[2]);
                c.Outline(TOutlineLeaf);
                return c.WithPivot(14f, 6f);
            }
            c.Ellipse(14f, 21f, 5.5f, 2.2f, PixelCanvas.Hex("#e57a24"));
            c.Ellipse(13.5f, 20.5f, 3.5f, 1.2f, PixelCanvas.Hex("#ffa24a"));
            for (int k = 0; k < 5; k++)
            {
                float a = Mathf.Lerp(-2.4f, -0.7f, k / 4f);
                float x1 = 14f + Mathf.Cos(a) * 11f, y1 = 20f + Mathf.Sin(a) * 13f;
                ThickLine(c, 14f, 20f, x1, y1, 1.6f, 1.1f, leaf[k % 2 == 0 ? 2 : 1]);
                c.Ellipse(x1, y1, 2.6f, 2f, leaf[3]);
            }
            c.Outline(TOutlineLeaf);
            return c.WithPivot(14f, 6f);
        }

        static PixelCanvas TownChest(bool open)
        {
            var c = Hd(36, 34);
            var w = TWood;
            var gold = Ramp("#9a6a14", "#c8921e", "#e8b83a", "#ffe07a");
            c.Ellipse(19f, 30f, 15f, 3.5f, TShadow);
            c.Rect(4, 16, 28, 13, w[2]); c.HLine(4, 31, 28, w[0]);
            for (int y = 20; y < 28; y += 4) c.HLine(5, 30, y, w[1]);
            if (open)
            {
                c.Rect(4, 2, 28, 10, w[3]); c.HLine(4, 31, 2, w[4]); c.Rect(4, 10, 28, 2, w[1]);
                c.Rect(6, 12, 24, 5, PixelCanvas.Hex("#2a1a10"));
                c.Rect(10, 13, 16, 3, gold[3]);
                c.Set(14, 12, White); c.Set(20, 13, White);
            }
            else
            {
                c.Rect(4, 8, 28, 9, w[3]); c.HLine(4, 31, 8, w[5]); c.Ellipse(18f, 9f, 14f, 2.5f, w[4]);
                c.HLine(4, 31, 16, w[1]);
            }
            foreach (int x in new[] { 8, 26 }) { c.Rect(x, open ? 2 : 8, 3, open ? 10 : 21, gold[2]); c.VLine(x, open ? 2 : 8, 28, gold[3]); }
            c.Rect(8, 16, 3, 13, gold[2]); c.Rect(26, 16, 3, 13, gold[2]);
            c.Rect(16, 14, 5, 6, gold[2]); c.Set(18, 17, TOutline);
            c.Outline(TOutline);
            return c.WithPivot(18f, 5f);
        }

        // ---------- Forest props ----------

        static PixelCanvas TownGrave(int v)
        {
            var c = Hd(30, 38);
            c.Ellipse(16f, 34f, 12f, 3f, TShadow);
            if (v % 2 == 0)
            {
                // Rounded headstone with a carved cross, a little tilted.
                for (int y = 6; y <= 33; y++)
                    for (int x = 5; x <= 24; x++)
                    {
                        int tilt = (33 - y) / 9;
                        int xx = x + tilt;
                        if (y < 13 && (x - 14.5f) * (x - 14.5f) / 90f + (y - 13f) * (y - 13f) / 49f > 1f) continue;
                        float u = (x - 5) / 19f;
                        c.Set(xx, y, TStone[u < 0.3f ? 4 : u < 0.7f ? 3 : 2]);
                    }
                c.Rect(15, 13, 2, 11, TStone[1]); c.Rect(11, 16, 10, 2, TStone[1]);
            }
            else
            {
                // Stone cross.
                c.Rect(12, 4, 7, 30, TStone[3]); c.Rect(5, 11, 21, 6, TStone[3]);
                c.VLine(12, 4, 33, TStone[4]); c.HLine(5, 25, 11, TStone[4]);
                c.VLine(18, 4, 33, TStone[2]); c.HLine(5, 25, 16, TStone[2]);
            }
            for (int x = 6; x <= 25; x++) if (c.IsOpaque(x, 32)) { c.Set(x, 32, TMoss); if (x % 2 == 0) c.Set(x, 31, TMossLight); }
            c.Outline(TOutlineStone);
            return c.WithPivot(15f, 4f);
        }

        static PixelCanvas TownBones()
        {
            var c = Hd(36, 20);
            var bone = Ramp("#b8ad96", "#d9d0bc", "#f1ead9");
            // Skull.
            c.Ellipse(10f, 9f, 6f, 5f, bone[2]); c.Rect(7, 12, 7, 3, bone[1]);
            c.Set(8, 9, TOutline); c.Set(9, 9, TOutline); c.Set(12, 9, TOutline); c.Set(13, 9, TOutline);
            c.Set(10, 11, TOutline);
            // Scattered bones.
            void Bone(int x0, int y0, int x1, int y1)
            {
                c.Line(x0, y0, x1, y1, bone[2]); c.Line(x0, y0 + 1, x1, y1 + 1, bone[1]);
                c.Circle(x0, y0, 1.4f, bone[2]); c.Circle(x1, y1, 1.4f, bone[2]);
            }
            Bone(18, 12, 30, 9); Bone(20, 16, 31, 16); Bone(4, 16, 9, 17);
            c.Outline(TOutline);
            return c.WithPivot(18f, 3f);
        }

        static PixelCanvas TownLog()
        {
            var c = Hd(66, 30);
            c.Ellipse(34f, 26f, 30f, 3.5f, TShadow);
            for (int y = 8; y <= 24; y++)
            {
                float v = (y - 8) / 16f;
                int idx = v < 0.2f ? 4 : v < 0.5f ? 3 : v < 0.8f ? 2 : 1;
                c.HLine(8, 57, y, TBark[idx]);
            }
            for (int x = 10; x < 56; x += 6) c.VLine(x, 12, 22, TBark[1]);
            c.Ellipse(58f, 16f, 5.5f, 8.5f, TBark[1]);
            c.Ellipse(58f, 16f, 4f, 6.8f, PixelCanvas.Hex("#c99a64"));
            c.Ellipse(58f, 16f, 2f, 3.5f, PixelCanvas.Hex("#a97a48"));
            for (int x = 10; x < 50; x++) if ((x * 7) % 5 < 3) c.Set(x, 8 + (x % 3 == 0 ? 0 : 1), x % 4 == 0 ? TMossLight : TMoss);
            c.Circle(20f, 7f, 2.5f, PixelCanvas.Hex("#c8323f")); c.Set(19, 6, White); c.VLine(20, 8, 9, PixelCanvas.Hex("#efe6d0"));
            c.Outline(TOutline);
            return c.WithPivot(33f, 4f);
        }

        static PixelCanvas TownShrooms()
        {
            var c = Hd(28, 24);
            var stem = PixelCanvas.Hex("#efe6d0");
            void Shroom(float x, float y, float r, Color32 cap, Color32 capLight, bool dots)
            {
                c.Rect(Mathf.RoundToInt(x - 1.5f), Mathf.RoundToInt(y), 3, Mathf.RoundToInt(r * 1.4f), stem);
                ShadeBlob(c, x, y, r, r * 0.65f, new[] { PixelCanvas.Shade(cap, 0.7f), cap, cap, capLight });
                if (dots) { c.Set(Mathf.RoundToInt(x - r * 0.4f), Mathf.RoundToInt(y - 1), White); c.Set(Mathf.RoundToInt(x + r * 0.3f), Mathf.RoundToInt(y - 2), White); }
            }
            Shroom(9f, 11f, 6f, PixelCanvas.Hex("#c8323f"), PixelCanvas.Hex("#ff6a5a"), true);
            Shroom(19f, 14f, 4.5f, PixelCanvas.Hex("#9a6a3a"), PixelCanvas.Hex("#c8945a"), false);
            Shroom(15f, 18f, 3f, PixelCanvas.Hex("#c8323f"), PixelCanvas.Hex("#ff6a5a"), true);
            c.Outline(TOutline);
            return c.WithPivot(14f, 3f);
        }

        static PixelCanvas TownRuin()
        {
            var c = Hd(32, 56);
            c.Ellipse(17f, 52f, 13f, 3.5f, TShadow);
            for (int y = 8; y <= 50; y++)
            {
                int top = 8 + (int)(4f * Mathf.Abs(Mathf.Sin(y)));
                for (int x = 6; x <= 25; x++)
                {
                    if (y < 16 && y < 8 + ((x * 5) % 9)) continue;     // broken top
                    float u = (x - 6) / 19f;
                    int idx = u < 0.25f ? 4 : u < 0.55f ? 3 : u < 0.8f ? 2 : 1;
                    if (y % 12 == 0) idx = 1;                           // stone drums
                    c.Set(x, y, TStone[idx]);
                }
            }
            for (int y = 18; y < 48; y += 2) { c.Set(9 + (y % 5), y, TMoss); c.Set(10 + (y % 5), y + 1, TMossLight); }
            c.Rect(3, 48, 26, 4, TStone[2]); c.HLine(3, 28, 48, TStone[4]);
            c.Outline(TOutlineStone);
            return c.WithPivot(16f, 4f);
        }

        /// <summary>Construction-site material piles: 0 = logs, 1 = stones.</summary>
        static PixelCanvas TownPile(bool stone)
        {
            var c = Hd(42, 30);
            c.Ellipse(22f, 26f, 18f, 3.5f, TShadow);
            if (stone)
            {
                ShadeBlob(c, 12f, 20f, 8f, 6f, TStone); ShadeBlob(c, 26f, 21f, 9f, 6.5f, TStone); ShadeBlob(c, 19f, 13f, 8f, 6f, TStone);
                ShadeBlob(c, 32f, 15f, 5f, 4f, TStone);
                c.Outline(TOutlineStone);
            }
            else
            {
                for (int k = 0; k < 3; k++) { c.Rect(4, 18 - k * 6 + (k % 2), 34, 6, TBark[2 + (k % 2)]); c.HLine(4, 37, 18 - k * 6 + (k % 2), TBark[4]); }
                for (int k = 0; k < 3; k++) c.Ellipse(38f, 21f - k * 6f + (k % 2), 3f, 3f, PixelCanvas.Hex("#d9ae78"));
                c.Outline(TOutline);
            }
            return c.WithPivot(21f, 4f);
        }
    }
}
