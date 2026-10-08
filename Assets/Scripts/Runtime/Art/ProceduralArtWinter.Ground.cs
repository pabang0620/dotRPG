using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
