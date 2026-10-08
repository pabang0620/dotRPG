using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
