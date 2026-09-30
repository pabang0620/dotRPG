using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Canyon town art: a seamless paving pattern that ignores tile borders, 3/4-view cliff faces,
    /// water that meets the rock wall directly (no bank strip), and the town's props.
    /// </summary>
    public static partial class ProceduralArt
    {
        public const int PaveSize = 128; // pattern repeats every 8x8 tiles
        static Color32[] paving;

        /// <summary>
        /// 128x128 paving, generated once. Stone courses have uneven heights (11-15 px) and stones
        /// uneven widths, so joints rarely line up with the 16px tile grid.
        /// </summary>
        static Color32[] PavingPattern()
        {
            if (paving != null) return paving;
            var px = new Color32[PaveSize * PaveSize];
            var rng = new System.Random(20260929);
            int[] courses = { 11, 13, 10, 15, 11, 9, 13, 11, 12, 10, 13 }; // sums to 128
            int top = 0;
            foreach (int h in courses)
            {
                int start = rng.Next(0, 24);
                int x = start;
                while (x < start + PaveSize)
                {
                    int w = rng.Next(13, 27);
                    if (start + PaveSize - (x + w) < 10) w = start + PaveSize - x; // close the loop cleanly
                    float tone = 0.9f + (float)rng.NextDouble() * 0.17f;
                    var stone = PixelCanvas.Shade(Flag, tone);
                    var light = PixelCanvas.Shade(FlagLight, tone);
                    var dark = PixelCanvas.Shade(Flag, tone * 0.87f);
                    int spots = rng.Next(1, 4);
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w; xx++)
                        {
                            int gx = (x + xx) % PaveSize, gy = (top + yy) % PaveSize;
                            Color32 col;
                            if (xx == 0 || yy == h - 1) col = FlagGrout;               // joints
                            else if (yy == 0) col = light;                             // lit top edge
                            else if (yy == h - 2 || xx == w - 1) col = dark;           // shaded lower edge
                            else col = stone;
                            // Rounded corners.
                            if ((xx == 1 && (yy == 0 || yy == h - 2)) || (xx == w - 1 && yy == 0)) col = PixelCanvas.Shade(FlagGrout, 1.15f);
                            px[gy * PaveSize + gx] = col;
                        }
                    for (int s = 0; s < spots; s++)
                    {
                        int sx = (x + rng.Next(2, Mathf.Max(3, w - 2))) % PaveSize, sy = (top + rng.Next(2, Mathf.Max(3, h - 3))) % PaveSize;
                        px[sy * PaveSize + sx] = FlagSpot;
                    }
                    if (rng.NextDouble() < 0.12)
                    {
                        // Hairline crack.
                        int cx = x + rng.Next(3, Mathf.Max(4, w - 3)), cy = top + 1;
                        for (int k = 0; k < h - 3; k++)
                        {
                            px[((cy + k) % PaveSize) * PaveSize + (cx % PaveSize)] = PixelCanvas.Shade(FlagGrout, 1.1f);
                            if (rng.NextDouble() < 0.4) cx += rng.Next(-1, 2);
                        }
                    }
                    x += w;
                }
                top += h;
            }
            paving = px;
            return px;
        }

        /// <summary>Rock wall seen from the 3/4 view: boulder mass on top, vertical face towards lower ground.</summary>
        static void DrawCliffTile(PixelCanvas c, int d, int flags, int v)
        {
            var rng = new System.Random(v * 7907 + d * 131 + flags * 17);
            if (d >= 4)
            {
                // Rock mass seen from above: stacked rounded boulders.
                c.Rect(0, 0, 16, 16, CliffDark);
                for (int i = 0; i < 3; i++)
                {
                    float cx = rng.Next(1, 15), cy = rng.Next(2, 14);
                    float rx = rng.Next(4, 7), ry = rng.Next(3, 6);
                    c.Ellipse(cx, cy, rx, ry, CliffDeep);
                    c.Ellipse(cx - 0.4f, cy - 0.6f, rx - 0.8f, ry - 0.8f, Cliff);
                    c.Ellipse(cx - 1.4f, cy - 1.6f, Mathf.Max(1f, rx - 2.6f), Mathf.Max(1f, ry - 2.4f), CliffLight);
                }
            }
            else
            {
                // Vertical face: columns of rock lit from the upper left, with horizontal strata.
                c.Rect(0, 0, 16, 16, Cliff);
                int x = -rng.Next(0, 5);
                while (x < 16)
                {
                    int w = rng.Next(4, 8);
                    for (int yy = 0; yy < 16; yy++)
                    {
                        c.Set(x, yy, CliffDeep);
                        c.Set(x + 1, yy, CliffLight);
                        c.Set(x + w - 1, yy, CliffDark);
                    }
                    if (rng.NextDouble() < 0.5) c.Set(x + 2, rng.Next(2, 14), CliffLight);
                    x += w;
                }
                int strata = rng.Next(4, 12);
                c.HLine(0, 15, strata, CliffDark);
                c.HLine(0, 15, strata - 1, PixelCanvas.Shade(CliffLight, 1.05f));
                if (d == 1)
                {
                    // Foot of the wall: darkens into a contact shadow.
                    for (int yy = 11; yy < 16; yy++)
                        for (int xx = 0; xx < 16; xx++)
                            c.Pixels[yy * 16 + xx] = PixelCanvas.Shade(c.Pixels[yy * 16 + xx], 1f - (yy - 10) * 0.1f);
                    c.HLine(0, 15, 15, CliffDeep);
                    c.Set(rng.Next(1, 15), 14, StoneDark);
                }
                else if (d == 3)
                {
                    // Top of the face catches more light.
                    c.HLine(0, 15, 0, CliffLight);
                }
            }
            if ((flags & 1) != 0)
            {
                // Plateau lip: the high ground's paving ends in a worn stone edge.
                c.Rect(0, 0, 16, 4, CliffRim);
                c.HLine(0, 15, 0, FlagLight);
                for (int xx = 0; xx < 16; xx++) c.Set(xx, 4 + (xx * 7 + v) % 3 / 2, CliffDeep);
                c.HLine(0, 15, 3, PixelCanvas.Shade(CliffRim, 0.85f));
            }
            if ((flags & 2) != 0) { c.VLine(0, 0, 15, CliffLight); c.VLine(1, 0, 15, PixelCanvas.Shade(CliffLight, 0.92f)); }
            if ((flags & 4) != 0) { c.VLine(15, 0, 15, CliffDeep); c.VLine(14, 0, 15, CliffDark); }
        }

        /// <summary>
        /// Canyon water. Where land is directly above, the rock face continues below the land surface
        /// and ripples only touch the wall's foot — no blue strip between land and rock.
        /// </summary>
        static void DrawCanyonWater(PixelCanvas c, int mask, int v)
        {
            c.Rect(0, 0, 16, 16, Teal);
            var rng = new System.Random(v * 1543 + mask * 7 + 11);
            // Low-contrast ripples of varying length and spacing.
            int ripples = rng.Next(1, 4);
            for (int i = 0; i < ripples; i++)
            {
                int len = rng.Next(2, 6), x = rng.Next(0, 16 - len), y = rng.Next(2, 15);
                c.HLine(x, x + len - 1, y, PixelCanvas.WithAlpha(TealLight, 110));
                if (len > 3) c.Set(x + len, y - 1, PixelCanvas.WithAlpha(TealLight, 70));
            }
            if (rng.NextDouble() < 0.5) c.HLine(rng.Next(0, 8), rng.Next(8, 16), rng.Next(3, 15), PixelCanvas.WithAlpha(TealDark, 120));

            if ((mask & 1) != 0)
            {
                // Rock face below the land edge, straight into the water.
                c.Rect(0, 0, 16, 8, Cliff);
                for (int x = rng.Next(0, 4); x < 16; x += rng.Next(4, 7))
                {
                    c.VLine(x, 0, 7, CliffDeep);
                    c.VLine(x + 1, 0, 7, CliffLight);
                }
                c.HLine(0, 15, 0, CliffRim);
                c.HLine(0, 15, 7, CliffDeep);
                c.HLine(0, 15, 8, PixelCanvas.WithAlpha(CliffDeep, 120));
                for (int x = 0; x < 16; x++)
                    if ((x * 5 + v) % 7 < 3) c.Set(x, 9, PixelCanvas.WithAlpha(TealFoam, 120));
            }
            else if ((mask & 8) != 0)
            {
                // A cliff tile above already drew the face: only its shadow and a faint foam line.
                c.Rect(0, 0, 16, 3, PixelCanvas.WithAlpha(CliffDeep, 110));
                for (int x = 0; x < 16; x++)
                    if ((x * 3 + v) % 5 < 2) c.Set(x, 3, PixelCanvas.WithAlpha(TealFoam, 110));
            }
            else if ((mask & 32) != 0)
            {
                c.Rect(0, 0, 16, 4, PixelCanvas.WithAlpha(TealDark, 170));
            }
            if ((mask & 4) != 0)
            {
                // Land to the west: the bank's rock side wall drops into the water.
                int y0 = (mask & 1) != 0 ? 8 : 0;
                c.Rect(0, y0, 5, 16, Cliff);
                c.VLine(0, y0, 15, CliffLight);
                c.VLine(1, y0, 15, PixelCanvas.Shade(CliffLight, 0.9f));
                c.VLine(4, y0, 15, CliffDeep);
                for (int y = y0 + 2 + v; y < 16; y += 5) c.HLine(1, 3, y, CliffDark);
                c.VLine(5, y0 + 1, 15, PixelCanvas.WithAlpha(TealFoam, 80));
            }
            if ((mask & 2) != 0)
            {
                int y0 = (mask & 1) != 0 ? 8 : 0;
                c.Rect(11, y0, 5, 16, CliffDark);
                c.VLine(11, y0, 15, CliffDeep);
                c.VLine(12, y0, 15, Cliff);
                for (int y = y0 + 3 + v; y < 16; y += 5) c.HLine(12, 14, y, CliffDeep);
                c.VLine(10, y0 + 1, 15, PixelCanvas.WithAlpha(TealFoam, 80));
            }
            if ((mask & 16) != 0)
            {
                // Land to the south sits higher: its paving lip casts a dark edge on the water.
                c.HLine(0, 15, 15, CliffDark);
                c.HLine(0, 15, 14, PixelCanvas.WithAlpha(TealDark, 160));
            }
        }

        /// <summary>Soft grass creeping onto paving from grass neighbours (mask 1=N 2=E 4=S 8=W).</summary>
        static PixelCanvas DrawGrassEdge(int mask)
        {
            var c = new PixelCanvas(16, 16);
            var rng = new System.Random(mask * 97 + 3);
            void Edge(bool horizontal, bool start)
            {
                for (int i = 0; i < 16; i++)
                {
                    int depth = rng.Next(1, 5);
                    for (int k = 0; k < depth; k++)
                    {
                        int along = i, across = start ? k : 15 - k;
                        int x = horizontal ? along : across, y = horizontal ? across : along;
                        c.Set(x, y, k == depth - 1 ? GrassDark : MossA);
                    }
                    if (rng.NextDouble() < 0.25)
                    {
                        int across = start ? depth : 15 - depth;
                        c.Set(horizontal ? i : across, horizontal ? across : i, PixelCanvas.WithAlpha(GrassLight, 200));
                    }
                }
            }
            if ((mask & 1) != 0) Edge(true, true);
            if ((mask & 4) != 0) Edge(true, false);
            if ((mask & 8) != 0) Edge(false, true);
            if ((mask & 2) != 0) Edge(false, false);
            c.WithPivot(8, 8);
            return c;
        }

        // ---------- Town props ----------

        static readonly Color32 RoofRed = PixelCanvas.Hex("#b8432f");
        static readonly Color32 RoofRedLight = PixelCanvas.Hex("#dd6a4c");
        static readonly Color32 RoofRedDark = PixelCanvas.Hex("#7e2a1e");
        static readonly Color32 WallStone = PixelCanvas.Hex("#b89a78");
        static readonly Color32 WallStoneDark = PixelCanvas.Hex("#8a6f55");
        static readonly Color32 Window = PixelCanvas.Hex("#ffd98a");

        /// <summary>Clay-tile roof rows (scalloped) over a rectangle.</summary>
        static void TileRoof(PixelCanvas c, int x0, int y0, int w, int h)
        {
            c.Rect(x0, y0, w, h, RoofRed);
            for (int y = y0 + 2; y < y0 + h; y += 4)
            {
                c.HLine(x0, x0 + w - 1, y, RoofRedDark);
                for (int x = x0 + ((y / 4) % 2) * 2; x < x0 + w; x += 4) c.Set(x, y - 1, RoofRedLight);
            }
            c.HLine(x0, x0 + w - 1, y0, RoofRedLight);
            c.HLine(x0, x0 + w - 1, y0 + h - 1, RoofRedDark);
        }

        /// <summary>Two-storey inn, the plaza landmark (5 tiles wide).</summary>
        static PixelCanvas DrawInn()
        {
            var c = new PixelCanvas(80, 76);
            // Stone ground floor.
            c.Rect(4, 46, 72, 26, WallStone);
            for (int y = 50; y < 72; y += 5)
                for (int x = 4 + (y / 5 % 2) * 4; x < 76; x += 8) { c.VLine(x, y - 4, y - 1, WallStoneDark); c.HLine(4, 75, y, WallStoneDark); }
            // Door with lantern and hanging sign board.
            c.Rect(34, 54, 12, 18, PixelCanvas.Hex("#4a2a16"));
            c.VLine(40, 54, 71, PixelCanvas.Hex("#2e1a0e"));
            c.HLine(33, 46, 53, WoodLight);
            c.Rect(48, 50, 3, 4, Yellow);
            c.Rect(18, 44, 12, 8, Wood); c.HLine(18, 29, 44, WoodLight); c.Rect(21, 46, 6, 3, WoodDark);
            // Windows.
            foreach (int wx in new[] { 10, 58, 66 }) { c.Rect(wx, 56, 7, 7, Window); c.VLine(wx + 3, 56, 62, WoodDark); c.HLine(wx, wx + 6, 59, WoodDark); c.HLine(wx - 1, wx + 7, 63, WoodDark); }
            // Timber upper floor + balcony.
            c.Rect(6, 30, 68, 16, PixelCanvas.Hex("#d9b27a"));
            for (int x = 6; x < 74; x += 10) c.VLine(x, 30, 45, WoodDark);
            c.HLine(6, 73, 30, WoodDark);
            foreach (int wx in new[] { 14, 34, 54 }) { c.Rect(wx, 34, 8, 7, Window); c.HLine(wx, wx + 7, 37, WoodDark); }
            c.Rect(4, 44, 72, 3, Wood); c.HLine(4, 75, 44, WoodLight);
            for (int x = 6; x < 76; x += 4) c.VLine(x, 41, 43, WoodDark);
            // Big clay-tile roof with a chimney.
            TileRoof(c, 0, 6, 80, 25);
            c.Rect(62, 0, 8, 10, WallStoneDark); c.Rect(61, 0, 10, 2, StoneDark);
            c.Rect(36, 10, 8, 6, PixelCanvas.Hex("#e8d8b8")); c.Rect(38, 12, 4, 3, Window);
            c.Outline(Outline);
            return c.WithPivot(40, 3.5f);
        }

        /// <summary>Market stall with a striped awning (3 tiles wide).</summary>
        static PixelCanvas DrawStall()
        {
            var c = new PixelCanvas(48, 40);
            c.Rect(4, 10, 2, 28, WoodDark); c.Rect(42, 10, 2, 28, WoodDark);
            // Counter with goods.
            c.Rect(3, 26, 42, 12, Wood); c.HLine(3, 44, 26, WoodLight); c.HLine(3, 44, 37, BarkDark);
            for (int x = 6; x < 42; x += 7) c.VLine(x, 28, 36, WoodDark);
            int[] goods = { 8, 16, 24, 32, 38 };
            Color32[] colors = { Carrot, Red, Leaf, Yellow, StoneLight };
            for (int i = 0; i < goods.Length; i++) { c.Ellipse(goods[i], 23, 3, 2.2f, colors[i]); c.Set(goods[i] - 1, 22, White); }
            // Awning.
            for (int x = 0; x < 48; x++)
                for (int y = 2; y < 12; y++)
                    c.Set(x, y, (x / 6) % 2 == 0 ? RoofRed : PixelCanvas.Hex("#f2e6c9"));
            for (int x = 0; x < 48; x += 6) { c.Set(x + 2, 12, RoofRedDark); c.Set(x + 3, 12, RoofRedDark); }
            c.HLine(0, 47, 2, RoofRedLight);
            c.Outline(Outline);
            return c.WithPivot(24, 3.5f);
        }

        static PixelCanvas DrawWell()
        {
            var c = new PixelCanvas(32, 34);
            c.Ellipse(16, 26, 13, 7, StoneDark);
            c.Ellipse(16, 25, 12, 6, Stone);
            c.Ellipse(16, 24, 8, 3.5f, Teal);
            c.PaintEllipse(14, 23, 3, 1, TealLight);
            for (int x = 5; x < 28; x += 5) c.Set(x, 28, StoneDark);
            c.Rect(4, 4, 2, 21, WoodDark); c.Rect(26, 4, 2, 21, WoodDark);
            TileRoof(c, 1, 1, 30, 7);
            c.HLine(6, 25, 10, BarkDark);
            c.VLine(16, 10, 16, PixelCanvas.Hex("#c8b89a"));
            c.Rect(14, 16, 5, 4, Wood);
            c.Outline(Outline);
            return c.WithPivot(16, 3.5f);
        }

        static PixelCanvas DrawBench()
        {
            var c = new PixelCanvas(32, 16);
            c.Rect(2, 4, 28, 3, Wood); c.HLine(2, 29, 4, WoodLight);
            c.Rect(2, 8, 28, 3, Wood); c.HLine(2, 29, 8, WoodLight); c.HLine(2, 29, 10, WoodDark);
            c.Rect(4, 11, 2, 4, WoodDark); c.Rect(26, 11, 2, 4, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(16, 1.5f);
        }

        static PixelCanvas DrawChest(bool open)
        {
            var c = new PixelCanvas(16, 16);
            c.Rect(1, 7, 14, 8, Wood); c.HLine(1, 14, 14, BarkDark);
            c.VLine(1, 7, 14, WoodDark); c.VLine(14, 7, 14, WoodDark);
            c.Rect(3, 7, 2, 8, Gold); c.Rect(11, 7, 2, 8, Gold);
            if (open)
            {
                c.Rect(1, 1, 14, 5, WoodDark); c.HLine(1, 14, 1, Wood);
                c.Rect(3, 6, 10, 2, PixelCanvas.Hex("#2a1d16"));
                c.Set(6, 6, Yellow); c.Set(9, 6, Yellow);
            }
            else
            {
                c.Rect(1, 3, 14, 5, WoodLight); c.HLine(1, 14, 3, PixelCanvas.Shade(WoodLight, 1.1f)); c.HLine(1, 14, 7, WoodDark);
                c.Rect(3, 3, 2, 5, Gold); c.Rect(11, 3, 2, 5, Gold);
                c.Rect(7, 6, 2, 3, Yellow);
            }
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        /// <summary>Closed stone gate to the next region (4 tiles wide).</summary>
        static PixelCanvas DrawGate()
        {
            var c = new PixelCanvas(64, 48);
            c.Rect(2, 8, 12, 38, WallStone); c.Rect(50, 8, 12, 38, WallStone);
            for (int y = 12; y < 46; y += 6) { c.HLine(2, 13, y, WallStoneDark); c.HLine(50, 61, y, WallStoneDark); }
            c.Rect(0, 2, 64, 8, WallStoneDark); c.HLine(0, 63, 2, WallStone);
            TileRoof(c, 0, 0, 64, 5);
            // Wooden bars.
            c.Rect(14, 10, 36, 36, PixelCanvas.Hex("#2a1d16"));
            for (int x = 16; x < 50; x += 5) c.Rect(x, 10, 2, 36, Wood);
            c.HLine(14, 49, 20, WoodDark); c.HLine(14, 49, 34, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(32, 3.5f);
        }
    }
}
