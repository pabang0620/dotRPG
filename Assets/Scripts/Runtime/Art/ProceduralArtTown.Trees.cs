using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
