using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Props ----------

        static PixelCanvas WinterHdHay()
        {
            var c = WHd(36, 32);
            c.Ellipse(18, 28, 16, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Rect(2, 10, 32, 18, WhHay);
            for (int x = 4; x < 34; x += 6) c.VLine(x, 14, 26, WhHayD);
            c.HLine(2, 33, 18, WhWoodD);
            c.Rect(2, 6, 32, 6, WhSnow); c.HLine(2, 33, 10, WhSnowS);
            c.Set(6, 4, WhCrest); c.Set(24, 4, WhCrest);
            c.Outline(WhOutline);
            return c.WithPivot(18, 5f);
        }

        static PixelCanvas WinterHdWell()
        {
            var c = WHd(64, 76);
            c.Ellipse(32, 68, 26, 5, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Ellipse(32, 58, 26, 14, WhStoneD);
            c.Ellipse(32, 56, 24, 12, WhStone);
            c.Ellipse(32, 54, 16, 7, PixelCanvas.Hex("#3d85c3"));
            c.PaintEllipse(28, 52, 6, 2, PixelCanvas.Hex("#80c2ee"));
            for (int a = 0; a < 12; a++) { double ang = a / 12.0 * 6.28; c.Set(32 + (int)(20 * Mathf.Cos((float)ang)), 58 + (int)(9 * Mathf.Sin((float)ang)), WhStoneD); }
            c.Ellipse(32, 47, 24, 3.2f, WhSnow);
            // Posts + winch.
            c.Rect(8, 14, 4, 42, WhWoodD); c.Rect(52, 14, 4, 42, WhWoodD);
            c.HLine(12, 51, 26, WhWoodDD);
            c.VLine(32, 26, 38, PixelCanvas.Hex("#c8b89a"));
            c.Rect(28, 38, 10, 8, WhWood); c.HLine(28, 37, 38, WhSnow);
            // Little snowy roof.
            for (int y = 4; y <= 18; y++)
            {
                int hw = 12 + (y - 4) * 20 / 14;
                c.HLine(32 - hw, 31 + hw, y, y > 14 ? WhSnowS : y > 8 ? WhSnowL : WhSnow);
            }
            c.HLine(2, 61, 20, WhWood);
            for (int x = 6; x < 60; x += 8) { c.Set(x, 22, WhIce); c.Set(x, 23, WhIceD); }
            c.Outline(WhOutline);
            return c.WithPivot(32, 7f);
        }

        static PixelCanvas WinterHdBench()
        {
            var c = WHd(64, 36);
            c.Ellipse(32, 32, 26, 3, PixelCanvas.WithAlpha(WhSnowSD, 130));
            c.Rect(4, 10, 56, 6, WhWood); c.HLine(4, 59, 10, WhWoodL);
            c.Rect(4, 20, 56, 6, WhWood); c.HLine(4, 59, 25, WhWoodD);
            c.Rect(8, 26, 4, 8, WhWoodD); c.Rect(52, 26, 4, 8, WhWoodD);
            c.Rect(4, 6, 56, 4, WhSnow); c.HLine(4, 59, 9, WhSnowS);
            c.Rect(6, 18, 52, 2, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(32, 3f);
        }

        /// <summary>Campfire: stone ring with snow caps, crossed logs, flames (frame 0-2).</summary>
        static PixelCanvas WinterHdCampfire(int frame)
        {
            var c = WHd(48, 48);
            var stone = PixelCanvas.Hex("#9ea3b3");
            var stoneD = PixelCanvas.Hex("#787d8d");
            c.Ellipse(24, 40, 20, 6, PixelCanvas.WithAlpha(WhSnowSD, 120));
            c.Ellipse(24, 38, 14, 4.8f, PixelCanvas.Hex("#5a4a44"));
            c.Line(12, 40, 34, 32, WhWoodD); c.Line(12, 32, 34, 40, WhWood);
            c.Line(14, 40, 36, 32, WhWoodD);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                float sx = 24 + Mathf.Cos(a) * 18, sy = 38 + Mathf.Sin(a) * 6.8f;
                c.Ellipse(sx, sy, 4.4f, 3.2f, stone);
                c.PaintEllipse(sx - 1, sy - 1.5f, 2.5f, 1.5f, WhStoneL);
                c.PaintEllipse(sx + 1, sy + 1.5f, 2.5f, 1.5f, stoneD);
                c.Ellipse(sx, sy - 2.5f, 2.5f, 1.2f, WhSnow);
            }
            float sway = frame == 0 ? 0f : frame == 1 ? 2f : -2f;
            int fh = frame == 1 ? 24 : frame == 2 ? 20 : 22;
            for (int y = 0; y < fh; y++)
            {
                float t = y / (float)fh;
                float hw = 9f * (1f - t) * (0.85f + 0.15f * Mathf.Sin(y * 0.5f + frame));
                float fx = 24 + sway * t * 2f;
                int yy = 34 - y;
                for (int x = Mathf.FloorToInt(fx - hw); x <= Mathf.CeilToInt(fx + hw); x++)
                {
                    float e = Mathf.Abs(x + 0.5f - fx) / Mathf.Max(0.5f, hw);
                    var col = e > 0.7f ? PixelCanvas.Hex("#ff7a24") : e > 0.35f || t > 0.6f ? PixelCanvas.Hex("#ffc84a") : PixelCanvas.Hex("#fff1b8");
                    c.Set(x, yy, col);
                }
            }
            c.Set(22 + frame, 34 - fh - 1, PixelCanvas.Hex("#ffc84a"));
            return c.WithPivot(24, 7f);
        }

        static PixelCanvas WinterHdLogSeat()
        {
            var c = WHd(48, 28);
            c.Ellipse(24, 24, 20, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            c.Rect(8, 8, 36, 16, WhWood);
            c.HLine(8, 43, 22, WhWoodD);
            for (int x = 14; x < 42; x += 8) c.VLine(x, 12, 20, WhWoodD);
            c.Ellipse(8, 16, 6, 8, PixelCanvas.Hex("#dcb488"));
            c.Ellipse(8, 16, 3.2f, 4.4f, PixelCanvas.Hex("#b88a5c"));
            c.Set(8, 16, WhWoodD);
            c.Rect(10, 4, 34, 6, WhSnow); c.HLine(12, 43, 8, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(24, 4f);
        }

        static PixelCanvas WinterHdWorkbench()
        {
            var c = WHd(72, 52);
            c.Rect(4, 16, 64, 10, WhWoodL); c.HLine(4, 67, 16, PixelCanvas.Shade(WhWoodL, 1.08f)); c.HLine(4, 67, 25, WhWoodD);
            c.Rect(8, 26, 6, 22, WhWoodD); c.Rect(58, 26, 6, 22, WhWoodD);
            c.Rect(8, 38, 56, 4, WhWood);
            // Saw, hammer, plane, vice.
            c.Rect(16, 10, 22, 6, PixelCanvas.Hex("#c2c7d4")); c.HLine(16, 36, 14, PixelCanvas.Hex("#8e93a3"));
            for (int x = 18; x < 36; x += 3) c.Set(x, 16, PixelCanvas.Hex("#8e93a3"));
            c.Rect(38, 10, 6, 6, WhWood); c.Rect(40, 6, 3, 6, PixelCanvas.Hex("#8e93a3"));
            c.Rect(48, 12, 12, 4, WhWoodD); c.Rect(56, 6, 6, 8, PixelCanvas.Hex("#8e93a3"));
            c.Rect(4, 8, 8, 8, PixelCanvas.Hex("#6e7384"));
            c.Set(24, 26, WhWoodL); c.Set(40, 28, WhWoodL);
            c.HLine(4, 14, 14, WhSnow); c.HLine(60, 67, 14, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(36, 4f);
        }

        static PixelCanvas WinterHdLamp()
        {
            var c = WHd(28, 92);
            c.Ellipse(14, 88, 8, 2.6f, PixelCanvas.WithAlpha(WhSnowSD, 150));
            c.Rect(8, 80, 12, 8, WhLampD); c.HLine(8, 19, 80, WhLamp);
            c.Rect(12, 28, 4, 52, WhLamp); c.VLine(15, 28, 79, WhLampD);
            c.Rect(10, 44, 8, 2, WhLamp);
            c.Rect(6, 24, 16, 4, WhLampD);
            c.Rect(6, 10, 16, 14, WhWarm); c.Rect(8, 12, 6, 6, WhWarmL);
            c.VLine(6, 10, 23, WhLampD); c.VLine(21, 10, 23, WhLampD); c.VLine(14, 10, 23, WhLampD);
            c.Rect(4, 4, 20, 6, WhLamp); c.HLine(8, 19, 2, WhLampD);
            c.HLine(4, 23, 4, WhSnow); c.HLine(8, 19, 2, WhSnow); c.Set(12, 0, WhCrest); c.Set(14, 0, WhCrest); c.Set(13, 1, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(14, 4f);
        }

        static PixelCanvas WinterHdMailbox()
        {
            var c = WHd(28, 44);
            var red = PixelCanvas.Hex("#c8463c");
            c.Rect(12, 20, 4, 22, WhWood); c.VLine(15, 20, 41, WhWoodD);
            c.Rect(4, 8, 20, 14, red); c.Ellipse(14, 8, 10, 4, red);
            c.HLine(4, 23, 8, PixelCanvas.Shade(red, 0.75f));
            c.Rect(6, 12, 10, 2, PixelCanvas.Shade(red, 0.7f));
            c.Rect(22, 6, 4, 8, Yellow);
            c.Rect(4, 4, 20, 4, WhSnow); c.Ellipse(14, 5, 10, 3, WhSnow); c.HLine(4, 23, 8, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(14, 3f);
        }

        static PixelCanvas WinterHdPot()
        {
            var c = WHd(28, 28);
            c.Rect(6, 16, 16, 10, WhTerracotta); c.Rect(4, 14, 20, 4, PixelCanvas.Shade(WhTerracotta, 1.12f));
            c.VLine(20, 16, 24, PixelCanvas.Shade(WhTerracotta, 0.78f));
            c.Circle(14, 10, 7.2f, WhPine2); c.PaintEllipse(12, 8, 4, 3, WhPine1);
            c.Set(10, 10, Red); c.Set(16, 12, Red); c.Set(14, 6, Red);
            c.HLine(8, 19, 4, WhSnow); c.Set(12, 2, WhCrest); c.Set(14, 3, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(14, 3f);
        }

        static PixelCanvas WinterHdWoodpile()
        {
            var c = WHd(60, 40);
            var ring = PixelCanvas.Hex("#dcb488");
            var ringD = PixelCanvas.Hex("#b88a5c");
            c.Ellipse(30, 36, 26, 3.2f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            for (int row = 0; row < 3; row++)
            {
                int n = 4 - row;
                for (int i = 0; i < n; i++)
                {
                    float x = 12 + i * 12 + row * 6, y = 30 - row * 10;
                    c.Circle(x, y, 6, WhWood);
                    c.Circle(x, y, 4, ring);
                    c.Circle(x, y, 1.6f, ringD);
                    c.PaintEllipse(x - 1.5f, y - 1.5f, 2, 1.4f, PixelCanvas.Hex("#eccca0"));
                }
            }
            c.Ellipse(30, 10, 14, 4, WhSnow); c.Ellipse(18, 20, 8, 2.4f, WhSnow); c.Ellipse(44, 20, 8, 2.4f, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(30, 4f);
        }

        static PixelCanvas WinterHdBarrel()
        {
            var c = WHd(28, 36);
            c.Ellipse(14, 32, 12, 2.8f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            c.Rect(4, 10, 20, 22, WhWood);
            c.VLine(4, 12, 30, WhWoodL); c.VLine(23, 12, 30, WhWoodD);
            c.HLine(4, 23, 14, WhLampD); c.HLine(4, 23, 26, WhLampD);
            c.Rect(6, 10, 16, 4, WhWoodD);
            c.Ellipse(14, 10, 10, 3.6f, WhSnow); c.HLine(6, 21, 12, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(14, 4f);
        }

        static PixelCanvas WinterHdCrate()
        {
            var c = WHd(32, 32);
            c.Rect(2, 8, 28, 22, WhWood);
            c.HLine(2, 29, 18, WhWoodD); c.VLine(2, 8, 29, WhWoodL); c.VLine(29, 8, 29, WhWoodD);
            c.Rect(2, 8, 28, 2, WhWoodL); c.Rect(2, 28, 28, 2, WhWoodD);
            c.Line(4, 10, 27, 27, WhWoodD); c.Line(27, 10, 4, 27, WhWoodD);
            c.Rect(2, 4, 28, 5, WhSnow); c.HLine(2, 29, 8, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }

        static PixelCanvas WinterHdSign()
        {
            var c = WHd(32, 36);
            c.Rect(14, 20, 4, 14, WhWoodD);
            c.Rect(2, 6, 28, 16, WhWood);
            c.HLine(2, 29, 20, WhWoodDD);
            c.HLine(6, 25, 12, WhWoodDD); c.HLine(6, 19, 16, WhWoodDD);
            c.Rect(2, 6, 28, 2, WhWoodL);
            c.Rect(2, 2, 28, 5, WhSnow); c.HLine(2, 29, 6, WhSnowS);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }

        /// <summary>North exit: a snowy timber arch with lanterns, closed by a drift and a barrier.</summary>
        static PixelCanvas WinterHdGate()
        {
            var c = WHd(128, 108);
            c.Rect(12, 20, 12, 84, WhWood); c.VLine(12, 20, 103, WhWoodL); c.VLine(23, 20, 103, WhWoodD);
            c.Rect(104, 20, 12, 84, WhWood); c.VLine(104, 20, 103, WhWoodL); c.VLine(115, 20, 103, WhWoodD);
            c.Rect(0, 12, 128, 10, WhWood); c.HLine(0, 127, 20, WhWoodDD);
            c.Rect(8, 30, 112, 6, WhWoodD);
            c.Rect(0, 6, 128, 8, WhSnow); c.HLine(0, 127, 12, WhSnowS);
            c.Rect(10, 28, 108, 2, WhSnow);
            foreach (int lx in new[] { 36, 88 })
            {
                c.VLine(lx + 1, 36, 42, WhOutline);
                c.Rect(lx - 3, 44, 8, 8, WhWarm); c.Set(lx, 46, WhWarmL); c.Rect(lx - 3, 44, 8, 1, WhLampD);
            }
            // Barrier and drift closing the path for now.
            c.Rect(28, 76, 72, 6, WhWoodL); c.Rect(28, 88, 72, 6, WhWoodL);
            c.HLine(28, 99, 80, WhWoodD); c.HLine(28, 99, 92, WhWoodD);
            c.Ellipse(64, 100, 44, 8, WhSnow); c.Ellipse(64, 102, 40, 5, WhSnowL); c.PaintEllipse(64, 98, 30, 4, WhCrest);
            c.Outline(WhOutline);
            return c.WithPivot(64, 7f);
        }

        /// <summary>Wooden fence with snow on rails and posts (mask 1=left,2=right,4=up neighbour).</summary>
        static PixelCanvas WinterHdFence(int mask)
        {
            var c = WHd(32, 36);
            bool left = (mask & 1) != 0, right = (mask & 2) != 0, up = (mask & 4) != 0;
            if (left) { c.Rect(0, 14, 15, 4, WhWood); c.Rect(0, 24, 15, 4, WhWood); c.HLine(0, 13, 12, WhSnow); c.HLine(0, 13, 22, WhSnow); }
            if (right) { c.Rect(17, 14, 15, 4, WhWood); c.Rect(17, 24, 15, 4, WhWood); c.HLine(18, 31, 12, WhSnow); c.HLine(18, 31, 22, WhSnow); }
            if (up) { c.Rect(14, 0, 4, 10, WhWood); c.VLine(14, 0, 9, WhWoodL); }
            c.Rect(12, 8, 8, 24, WhWood);
            c.VLine(18, 10, 31, WhWoodD);
            c.VLine(12, 8, 31, WhWoodL);
            c.Rect(12, 4, 8, 4, WhSnow); c.Ellipse(16, 4, 5, 2, WhSnow); c.Set(15, 2, WhCrest);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }

        /// <summary>Raised garden bed under snow with winter cabbages peeking out.</summary>
        static PixelCanvas WinterHdGardenBed(int v)
        {
            var c = WHd(32, 28);
            c.Rect(2, 10, 28, 16, WhWoodD); c.HLine(2, 29, 10, WhWood);
            c.Rect(4, 12, 24, 12, PixelCanvas.Hex("#6a5048"));
            c.Rect(4, 10, 24, 6, WhSnow); c.HLine(4, 27, 16, WhSnowS);
            var cab = PixelCanvas.Hex("#7fae7a");
            var cabP = PixelCanvas.Hex("#a07bb0");
            c.Circle(10 + v % 2, 16, 4.4f, v % 2 == 0 ? cab : cabP); c.Set(10 + v % 2, 14, WhSnow);
            c.Circle(22 - v % 2, 17, 4f, v % 2 == 0 ? cabP : cab); c.Set(22 - v % 2, 15, WhSnow);
            c.Outline(WhOutline);
            return c.WithPivot(16, 3f);
        }
    }
}
