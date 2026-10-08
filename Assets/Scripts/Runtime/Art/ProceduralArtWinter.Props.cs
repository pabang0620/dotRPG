using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
