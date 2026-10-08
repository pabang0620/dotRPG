using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
