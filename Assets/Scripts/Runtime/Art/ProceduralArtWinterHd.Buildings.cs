using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Buildings ----------

        /// <summary>
        /// Snowy front-facing roof: shingles, a thick snow layer with a wavy lower edge, dark bargeboards,
        /// a wooden fascia and icicles.
        /// </summary>
        static void WSnowRoof(PixelCanvas c, int cx, int top, int eave, int topHalf, int eaveHalf, Color32 roof)
        {
            var roofDark = PixelCanvas.Shade(roof, 0.72f);
            var roofLight = PixelCanvas.Shade(roof, 1.18f);
            int HalfAt(int y) => Mathf.RoundToInt(Mathf.Lerp(topHalf, eaveHalf, (y - top) / (float)Mathf.Max(1, eave - top)));
            for (int y = top; y <= eave; y++)
            {
                int hw = HalfAt(y);
                for (int x = cx - hw; x <= cx + hw; x++)
                {
                    int row = (y - top) % 4;
                    var col = row == 3 ? roofDark : roof;
                    if (row == 0 && ((x + (y / 4) * 2) % 6 == 0)) col = roofLight;
                    c.Set(x, y, col);
                }
            }
            int snowTo = top + Mathf.RoundToInt((eave - top) * 0.72f);
            for (int x = cx - eaveHalf; x <= cx + eaveHalf; x++)
            {
                int bottom = snowTo + Mathf.RoundToInt(3.5f * Mathf.Sin(x * 0.5f)) - (x > cx + eaveHalf / 2 ? 2 : 0);
                for (int y = top; y <= bottom && y <= eave; y++)
                {
                    if (Mathf.Abs(x - cx) > HalfAt(y)) continue;
                    var col = x > cx + 3 ? WhSnowL : WhSnow;
                    if (y >= bottom - 2) col = WhSnowS;
                    if (y <= top + 1) col = WhCrest;
                    c.Set(x, y, col);
                }
            }
            for (int y = top; y <= eave; y++)
            {
                int hw = HalfAt(y);
                c.Set(cx - hw, y, WhWoodDD); c.Set(cx - hw - 1, y, WhWoodDD);
                c.Set(cx + hw, y, WhWoodDD); c.Set(cx + hw + 1, y, WhWoodDD);
            }
            c.HLine(cx - eaveHalf - 1, cx + eaveHalf + 1, eave + 1, WhWood);
            c.HLine(cx - eaveHalf - 1, cx + eaveHalf + 1, eave + 2, WhWoodDD);
            for (int x = cx - eaveHalf; x <= cx + eaveHalf; x++)
            {
                if ((x * 3) % 7 < 3) c.Set(x, eave + 1, WhSnow);
                if ((x * 5) % 13 == 0) { c.Set(x, eave + 3, WhIce); c.Set(x, eave + 4, WhIce); c.Set(x, eave + 5, WhIceD); }
            }
        }

        /// <summary>Log cabin (3 tiles wide): warm log walls, glowing windows, wreath, deep snow roof, chimney.</summary>
        static PixelCanvas WinterHdHouse()
        {
            var c = WHd(104, 128);
            int x0 = 8, x1 = 95, y0 = 68, y1 = 116;
            for (int y = y0; y <= y1; y++)
            {
                int k = (y - y0) % 8;
                c.HLine(x0, x1, y, k < 2 ? WhWoodL : k >= 6 ? WhWoodD : WhWood);
            }
            // corner log ends
            for (int y = y0; y <= y1; y += 8)
            {
                c.Rect(x0 - 4, y, 6, 6, WhWoodL); c.Set(x0 - 2, y + 2, WhWoodD); c.Ellipse(x0 - 1, y + 2, 2, 2, WhWoodD);
                c.Rect(x1 - 1, y, 6, 6, WhWoodL); c.Ellipse(x1 + 2, y + 2, 2, 2, WhWoodD);
            }
            c.Rect(x0 - 2, 116, x1 - x0 + 5, 6, PixelCanvas.Hex("#9aa0b0"));
            c.HLine(x0 - 2, x1 + 2, 116, PixelCanvas.Hex("#c2c7d4"));
            // Door with a wreath and a stone step.
            c.Rect(42, 84, 20, 34, WhWoodDD);
            c.VLine(52, 84, 116, PixelCanvas.Hex("#3e2618"));
            c.HLine(42, 61, 84, WhWoodD);
            c.Set(58, 102, Gold); c.Set(58, 103, Gold);
            c.Circle(52, 92, 5.4f, WhPine2); c.Circle(52, 92, 2.6f, WhWoodDD);
            c.Set(48, 90, Red); c.Set(56, 94, Red); c.Set(52, 96, Red); c.Set(50, 95, Red);
            c.Rect(38, 118, 28, 6, PixelCanvas.Hex("#b4b8c6")); c.HLine(38, 65, 118, WhSnow);
            // Windows with warm light and snowy sills.
            foreach (int wx in new[] { 16, 72 })
            {
                c.Rect(wx, 80, 18, 18, WhWarm);
                c.Rect(wx + 2, 82, 6, 6, WhWarmL);
                c.VLine(wx + 8, 80, 97, WhWoodDD); c.HLine(wx, wx + 17, 88, WhWoodDD);
                c.Rect(wx - 2, 98, 22, 4, WhSnow); c.HLine(wx - 2, wx + 19, 100, WhSnowS);
                // flower box
                c.Rect(wx - 2, 102, 22, 4, WhWoodD); c.Set(wx + 4, 100, Red); c.Set(wx + 12, 100, Red); c.Set(wx + 8, 100, WhWarm);
            }
            // Lantern beside the door.
            c.Rect(66, 86, 6, 8, WhWarm); c.Set(68, 84, WhLampD); c.Rect(66, 86, 6, 1, WhLampD);
            // Chimney behind the roof with a snow cap and a curl of smoke.
            c.Rect(70, 10, 14, 28, PixelCanvas.Hex("#8e93a3"));
            c.VLine(82, 10, 37, PixelCanvas.Hex("#6e7384"));
            c.Rect(68, 6, 18, 6, WhSnow); c.HLine(68, 85, 10, WhSnowS);
            c.Circle(76, 3f, 3f, PixelCanvas.WithAlpha(WhSnowSD, 190));
            c.Circle(82, 1f, 2f, PixelCanvas.WithAlpha(WhSnowSD, 140));
            // Roof.
            WSnowRoof(c, 52, 18, 66, 16, 52, PixelCanvas.Hex("#a44a3a"));
            c.HLine(x0, x1, 74, WhWoodDD); // eave shadow on the wall
            c.Outline(WhOutline);
            return c.WithPivot(52, 7f);
        }

        /// <summary>Farm building (5 tiles wide): board walls, big braced doors, hay loft, snowy gambrel roof, weathervane.</summary>
        static PixelCanvas WinterHdBarn()
        {
            var c = WHd(168, 164);
            int x0 = 8, x1 = 159, y0 = 76, y1 = 154;
            for (int x = x0; x <= x1; x++)
            {
                var col = (x - x0) % 12 == 0 ? WhBarnD : (x - x0) % 12 == 1 ? PixelCanvas.Shade(WhBarn, 1.08f) : WhBarn;
                c.VLine(x, y0, y1, col);
            }
            c.Rect(x0 - 2, 154, x1 - x0 + 5, 6, PixelCanvas.Hex("#9aa0b0"));
            // Big double door with X braces.
            c.Rect(56, 104, 56, 52, WhWoodD);
            c.VLine(82, 104, 154, WhWoodDD); c.VLine(84, 104, 154, WhWoodDD);
            c.Line(58, 106, 80, 152, WhCream); c.Line(58, 152, 80, 106, WhCream);
            c.Line(86, 106, 108, 152, WhCream); c.Line(86, 152, 108, 106, WhCream);
            c.Rect(56, 104, 56, 4, WhCream); c.Rect(56, 152, 56, 4, WhCream);
            c.VLine(56, 104, 154, WhCream); c.VLine(111, 104, 154, WhCream);
            // Hay loft.
            c.Rect(68, 80, 32, 18, WhWoodDD);
            c.Rect(70, 88, 28, 10, WhHay); c.HLine(70, 96, 88, PixelCanvas.Shade(WhHay, 1.1f)); c.Set(76, 90, WhHayD); c.Set(88, 92, WhHayD);
            c.Rect(66, 78, 36, 2, WhCream); c.VLine(66, 78, 98, WhCream); c.VLine(100, 78, 98, WhCream);
            // Small windows.
            foreach (int wx in new[] { 20, 132 })
            {
                c.Rect(wx, 112, 16, 16, WhWarm);
                c.VLine(wx + 8, 112, 127, WhWoodDD); c.HLine(wx, wx + 15, 120, WhWoodDD);
                c.Rect(wx - 2, 128, 20, 4, WhSnow); c.HLine(wx - 2, wx + 17, 130, WhSnowS);
            }
            // Roof: dark red shingles under a thick snow layer.
            WSnowRoof(c, 84, 18, 72, 24, 82, PixelCanvas.Hex("#8a3b32"));
            c.HLine(x0, x1, 80, PixelCanvas.WithAlpha(WhWoodDD, 200));
            // Weathervane.
            c.VLine(84, 2, 16, WhOutline);
            c.HLine(78, 90, 6, WhOutline);
            c.Rect(86, 0, 6, 4, PixelCanvas.Hex("#d9a441"));
            c.Outline(WhOutline);
            return c.WithPivot(84, 7f);
        }
    }
}
