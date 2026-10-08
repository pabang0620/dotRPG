using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
