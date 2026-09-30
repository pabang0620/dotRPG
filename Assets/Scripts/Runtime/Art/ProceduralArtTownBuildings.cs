using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution town buildings (keys "town_house_*", "town_hall", "town_store", "town_smithy",
    /// "town_warehouse", "town_site_*", "town_fountain", "town_well"). Every building is seen in the
    /// same 3/4 view: a shingled roof slope on top, the front wall below with a door in the middle, a
    /// shadow falling to the lower right. The pivot is the middle of the front wall's base line.
    /// </summary>
    public static partial class ProceduralArt
    {
        static int IHash(int a, int b, int s) => ((a * 73856093) ^ (b * 19349663) ^ (s * 83492791)) & 0x7fffffff;

        static readonly Color32[] TRoofRed = Ramp("#6e2a22", "#8e3a2c", "#b04c36", "#cc6444", "#e2825c");
        static readonly Color32[] RoofBlue = Ramp("#243c62", "#30507e", "#3d669a", "#5282b8", "#74a2d2");
        static readonly Color32[] RoofGreen = Ramp("#2a4a2a", "#386238", "#487c44", "#5f9855", "#7fb46c");
        static readonly Color32[] RoofSlate = Ramp("#2c3038", "#3a3f49", "#4b515c", "#606874", "#7c8591");
        static readonly Color32[] RoofBrown = Ramp("#3e2a1c", "#553a26", "#6e4c31", "#89613f", "#a67a52");
        static readonly Color32[] RoofOrange = Ramp("#7a3a18", "#9c4c20", "#bf6428", "#dc8034", "#ef9e52");
        static readonly Color32[] Plaster = Ramp("#bda983", "#d6c39d", "#e9dcbf", "#f5ecd8");
        static readonly Color32[] Beam = Ramp("#3e2616", "#56361f", "#6e472a", "#8a5c37");
        static readonly Color32 Glass = PixelCanvas.Hex("#78b2dc");
        static readonly Color32 GlassLight = PixelCanvas.Hex("#bfe4f7");
        static readonly Color32 GlassDark = PixelCanvas.Hex("#4a7aa8");
        static readonly Color32 BuildShadow = new Color32(16, 26, 14, 70);

        // ---------- Pieces ----------

        /// <summary>Shingled roof slope, narrower at the top (hipped), with a ridge cap and dark eave.</summary>
        static void Shingles(PixelCanvas c, int x0, int x1, int top, int bottom, int hip, Color32[] r, int seed)
        {
            int h = Mathf.Max(1, bottom - top);
            for (int y = top; y <= bottom; y++)
            {
                float p = (y - top) / (float)h;
                int inset = Mathf.RoundToInt(hip * (1f - p));
                int xa = x0 + inset, xb = x1 - inset;
                int row = (y - top) / 7, inRow = (y - top) % 7;
                for (int x = xa; x <= xb; x++)
                {
                    int sx = x - x0 + (row % 2) * 5;
                    int colIndex = sx / 10, within = sx % 10;
                    int hsh = IHash(colIndex, row, seed);
                    int tone = 2 + (hsh % 3 == 0 ? 1 : 0) - (hsh % 5 == 0 ? 1 : 0);
                    Color32 col;
                    if (inRow == 6) col = r[0];
                    else if (inRow == 0) col = r[Mathf.Min(4, tone + 1)];
                    else if (inRow == 5) col = r[Mathf.Max(0, tone - 1)];
                    else if (within == 0) col = r[Mathf.Max(0, tone - 1)];
                    else col = r[tone];
                    if (x - xa < 2 || xb - x < 2) col = x - xa < 2 ? r[3] : r[1];     // verge boards
                    if (y >= bottom - 1) col = r[0];                                  // eave
                    c.Set(x, y, col);
                }
            }
            // Ridge cap.
            int ra = x0 + hip, rb = x1 - hip;
            c.HLine(ra, rb, top, r[4]);
            c.HLine(ra, rb, top + 1, r[3]);
            c.HLine(ra, rb, top + 2, r[1]);
        }

        static void PlasterWall(PixelCanvas c, int x0, int x1, int top, int bottom, int seed)
        {
            for (int y = top; y <= bottom; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int hsh = IHash(x, y, seed);
                    c.Set(x, y, hsh % 29 == 0 ? Plaster[1] : hsh % 37 == 0 ? Plaster[3] : Plaster[2]);
                }
        }

        static void BeamRect(PixelCanvas c, int x, int y, int w, int h)
        {
            c.Rect(x, y, w, h, Beam[2]);
            if (w >= h) c.HLine(x, x + w - 1, y, Beam[3]); else c.VLine(x, y, y + h - 1, Beam[3]);
            if (w >= h) c.HLine(x, x + w - 1, y + h - 1, Beam[0]); else c.VLine(x + w - 1, y, y + h - 1, Beam[0]);
        }

        static void StoneWall(PixelCanvas c, int x0, int x1, int top, int bottom, Color32[] r, int seed, int rowH = 7)
        {
            for (int y = top; y <= bottom; y++)
            {
                int row = (y - top) / rowH, inRow = (y - top) % rowH;
                for (int x = x0; x <= x1; x++)
                {
                    int off = (row % 2) * 7;
                    int bw = 12 + IHash(row, (x - x0 + off) / 13, seed) % 3;
                    int within = (x - x0 + off) % 13;
                    int hsh = IHash((x - x0 + off) / 13, row, seed + 1);
                    int tone = 2 + (hsh % 3 == 0 ? 1 : 0) - (hsh % 4 == 0 ? 1 : 0);
                    Color32 col = inRow == rowH - 1 || within == 0 ? r[0] : inRow == 0 ? r[Mathf.Min(r.Length - 1, tone + 1)] : r[tone];
                    if (within == 1 && inRow != rowH - 1) col = r[Mathf.Min(r.Length - 1, tone + 1)];
                    _ = bw;
                    c.Set(x, y, col);
                }
            }
        }

        static void PlankWall(PixelCanvas c, int x0, int x1, int top, int bottom, Color32[] r, int seed)
        {
            for (int x = x0; x <= x1; x++)
            {
                int board = (x - x0) / 7, within = (x - x0) % 7;
                int tone = 2 + (IHash(board, 0, seed) % 3 == 0 ? 1 : 0) - (IHash(board, 1, seed) % 4 == 0 ? 1 : 0);
                for (int y = top; y <= bottom; y++)
                {
                    Color32 col = within == 0 ? r[0] : within == 1 ? r[Mathf.Min(r.Length - 1, tone + 1)] : r[tone];
                    if (IHash(board, y / 9, seed + 2) % 23 == 0 && within == 3) col = r[0];   // knots
                    c.Set(x, y, col);
                }
            }
        }

        /// <summary>Window with frame, cross bars, sky reflection and (optionally) a flower box under it.</summary>
        static void TWindow(PixelCanvas c, int x, int y, int w, int h, Color32 frame, bool flowers, int seed)
        {
            c.Rect(x - 2, y - 2, w + 4, h + 4, frame);
            c.Rect(x, y, w, h, Glass);
            for (int k = 0; k < w; k++)
            {
                int yy = y + h - 1 - k;
                if (yy >= y) { c.Set(x + k, yy, GlassLight); if (yy - 1 >= y) c.Set(x + k, yy - 1, GlassLight); }
            }
            c.HLine(x, x + w - 1, y + h - 1, GlassDark);
            c.VLine(x + w / 2, y, y + h - 1, frame);
            c.HLine(x, x + w - 1, y + h / 2, frame);
            c.HLine(x - 3, x + w + 2, y + h + 2, PixelCanvas.Shade(frame, 0.8f));      // sill
            if (!flowers) return;
            c.Rect(x - 2, y + h + 3, w + 4, 4, Beam[2]);
            c.HLine(x - 2, x + w + 1, y + h + 3, Beam[3]);
            string[] cols = { "#ff6f91", "#ffe066", "#ffffff", "#ff9f43", "#b99bff" };
            for (int k = 0; k < w + 2; k += 2)
            {
                var leaf = (k / 2) % 2 == 0 ? PixelCanvas.Hex("#3f8a38") : PixelCanvas.Hex("#58a84a");
                c.Set(x - 1 + k, y + h + 2, leaf);
                c.Set(x + k, y + h + 1, leaf);
                if (IHash(k, seed, 3) % 3 != 0) c.Set(x - 1 + k, y + h + 1, PixelCanvas.Hex(cols[IHash(k, seed, 4) % cols.Length]));
            }
        }

        /// <summary>Plank door (optionally double and arched) with a frame, handle and stone step.</summary>
        static void Door(PixelCanvas c, int cx, int bottom, int w, int h, bool arched, bool dbl)
        {
            int x0 = cx - w / 2, top = bottom - h;
            float archCy = top + w / 2f;
            bool Inside(int x, int y, float r)
            {
                if (!arched || y >= archCy) return true;
                float dx = (x + 0.5f - cx) / r, dy = (y + 0.5f - archCy) / r;
                return dx * dx + dy * dy <= 1f;
            }
            for (int y = top - 3; y < bottom; y++)
                for (int x = x0 - 3; x < x0 + w + 3; x++)
                    if (Inside(x, y, w * 0.5f + 3f)) c.Set(x, y, Beam[1]);
            for (int y = top; y < bottom; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (!Inside(x, y, w * 0.5f)) continue;
                    int board = (x - x0) / 4;
                    c.Set(x, y, (x - x0) % 4 == 0 ? TWood[1] : TWood[board % 2 == 0 ? 3 : 2]);
                }
            if (dbl) c.VLine(cx, top, bottom - 1, TWood[0]);
            c.HLine(x0, x0 + w - 1, top + h / 3, TWood[1]);
            c.HLine(x0, x0 + w - 1, top + 2 * h / 3, TWood[1]);
            c.Set(dbl ? cx - 3 : x0 + w - 4, top + h / 2, PixelCanvas.Hex("#e8c050"));
            if (dbl) c.Set(cx + 2, top + h / 2, PixelCanvas.Hex("#e8c050"));
            c.Rect(x0 - 4, bottom, w + 8, 3, TStone[3]);
            c.HLine(x0 - 4, x0 + w + 3, bottom, TStone[5]);
        }

        static void Chimney(PixelCanvas c, int x, int top, int bottom, bool smoke)
        {
            var brick = Ramp("#5a2e24", "#7a3e2e", "#96503a", "#b06448");
            for (int y = top; y <= bottom; y++)
                for (int xx = x; xx < x + 12; xx++)
                {
                    int row = (y - top) / 4;
                    bool mortar = (y - top) % 4 == 3 || (xx - x + (row % 2) * 3) % 6 == 0;
                    c.Set(xx, y, mortar ? brick[0] : xx < x + 3 ? brick[3] : brick[2]);
                }
            c.Rect(x - 2, top - 3, 16, 4, TStone[2]);
            c.HLine(x - 2, x + 13, top - 3, TStone[4]);
            if (!smoke) return;
            var puff = new Color32(236, 236, 236, 150);
            c.Circle(x + 7f, top - 8f, 3.5f, puff);
            c.Circle(x + 10f, top - 14f, 4.5f, new Color32(236, 236, 236, 110));
            c.Circle(x + 14f, top - 21f, 5f, new Color32(236, 236, 236, 70));
        }

        /// <summary>Wooden sign hanging from an iron bracket, with a little picture on it.</summary>
        static void HangingSign(PixelCanvas c, int x, int y, string icon)
        {
            c.HLine(x - 2, x + 15, y - 5, TIron[1]); c.Set(x - 2, y - 4, TIron[1]);
            c.VLine(x + 1, y - 4, y - 1, TIron[2]); c.VLine(x + 12, y - 4, y - 1, TIron[2]);
            c.Rect(x - 2, y, 18, 15, TWood[1]);
            c.Rect(x - 1, y + 1, 16, 13, TWood[4]);
            c.HLine(x - 1, x + 14, y + 1, TWood[5]);
            int cx = x + 7, cy = y + 7;
            switch (icon)
            {
                case "potion":
                    c.Circle(cx + 0.5f, cy + 1.5f, 4f, PixelCanvas.Hex("#e0413a"));
                    c.Rect(cx - 1, cy - 5, 3, 3, PixelCanvas.Hex("#d8eef6"));
                    c.Set(cx - 1, cy, PixelCanvas.Hex("#ff9a8a"));
                    break;
                case "anvil":
                    c.Rect(cx - 5, cy - 2, 11, 3, TIron[2]); c.Rect(cx - 2, cy + 1, 5, 2, TIron[1]); c.Rect(cx - 4, cy + 3, 9, 2, TIron[2]);
                    c.Set(cx - 6, cy - 1, TIron[2]);
                    break;
                case "box":
                    c.Rect(cx - 4, cy - 3, 9, 8, TWood[2]); c.HLine(cx - 4, cx + 4, cy - 3, TWood[3]); c.Line(cx - 4, cy - 3, cx + 4, cy + 4, TWood[1]);
                    break;
                case "saw":
                    c.Rect(cx - 5, cy - 1, 10, 3, TIron[3]);
                    for (int k = 0; k < 10; k += 2) c.Set(cx - 5 + k, cy + 2, TIron[2]);
                    c.Rect(cx + 4, cy - 2, 3, 5, TWood[2]);
                    break;
                default:
                    c.Circle(cx + 0.5f, cy + 0.5f, 3.5f, PixelCanvas.Hex("#e8c050"));
                    break;
            }
        }

        static void Awning(PixelCanvas c, int x0, int x1, int y, int h, Color32 a, Color32 b)
        {
            for (int x = x0; x <= x1; x++)
            {
                bool stripe = ((x - x0) / 6) % 2 == 0;
                var col = stripe ? a : b;
                int scallop = (int)(2f * Mathf.Abs(Mathf.Sin((x - x0) * Mathf.PI / 6f)));
                for (int yy = y; yy < y + h + scallop; yy++)
                {
                    var cc = yy == y ? PixelCanvas.Shade(col, 1.12f) : yy >= y + h - 1 ? PixelCanvas.Shade(col, 0.82f) : col;
                    c.Set(x, yy, cc);
                }
            }
            // Its shadow on the wall.
            for (int x = x0; x <= x1; x++) c.Set(x, y + h + 3, new Color32(0, 0, 0, 50));
        }

        /// <summary>Soft shadow on the ground to the lower right of a building's footprint.</summary>
        static void GroundShadow(PixelCanvas c, int x0, int x1, int wallTop, int baseY)
        {
            for (int y = wallTop + 8; y <= baseY + 3; y++)
                for (int x = x1 + 1; x <= x1 + 7; x++)
                    if (x - x1 < 7 - (baseY + 3 - y) / 6) c.Set(x, y, BuildShadow);
            for (int x = x0 + 2; x <= x1 + 6; x++) { c.Set(x, baseY + 1, BuildShadow); c.Set(x, baseY + 2, new Color32(16, 26, 14, 40)); }
        }

        /// <summary>Stone footing along the bottom of a wall.</summary>
        static void Footing(PixelCanvas c, int x0, int x1, int bottom, int h)
        {
            StoneWall(c, x0, x1, bottom - h + 1, bottom, TStone, 77, 4);
        }

        // ---------- Buildings ----------

        /// <summary>Timber-framed cottage (3x3): 0 red roof, 1 blue, 2 green.</summary>
        static PixelCanvas TownHouse(int v)
        {
            var roof = v == 1 ? RoofBlue : v == 2 ? RoofGreen : TRoofRed;
            const int footW = 96, wallH = 42, roofH = 52;
            int W = footW + 14, Hh = roofH + wallH + 16;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, wallTop, baseY);
            PlasterWall(c, x0, x1, wallTop, baseY, 11 + v);
            Footing(c, x0, x1, baseY, 6);
            // Timber frame.
            BeamRect(c, x0, wallTop, footW, 4);
            BeamRect(c, x0, baseY - 9, footW, 3);
            foreach (int bx in new[] { x0, x0 + 30, x1 - 32, x1 - 2 }) BeamRect(c, bx, wallTop, 3, wallH - 6);
            c.Line(x0 + 3, baseY - 10, x0 + 29, wallTop + 4, Beam[1]);
            c.Line(x1 - 3, baseY - 10, x1 - 31, wallTop + 4, Beam[1]);
            TWindow(c, x0 + 8, wallTop + 11, 14, 14, Beam[1], true, 3 + v);
            TWindow(c, x1 - 22, wallTop + 11, 14, 14, Beam[1], true, 5 + v);
            Door(c, cx, baseY - 5, 16, 28, true, false);
            int roofBottom = wallTop + 5, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 8, roofBottom, 10, roof, 200 + v);
            Chimney(c, x1 - 22, roofTop + 4, roofTop + 26, true);
            // The eave shades the wall right under it.
            for (int x = x0; x <= x1; x++) for (int y = roofBottom + 1; y <= roofBottom + 2; y++) c.Set(x, y, new Color32(0, 0, 0, 45));
            // Little round attic window in the roof.
            c.Circle(cx + 0.5f, roofTop + 30f, 5f, Beam[1]);
            c.Circle(cx + 0.5f, roofTop + 30f, 3.6f, Glass);
            c.Set(cx - 1, roofTop + 28, GlassLight); c.Set(cx, roofTop + 28, GlassLight);
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>Chief's hall (5x4): stone ground floor, blue slate roof with a front gable and a bell.</summary>
        static PixelCanvas TownHall()
        {
            const int footW = 160, wallH = 52, roofH = 64;
            int W = footW + 14, Hh = roofH + wallH + 22;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, wallTop, baseY);
            StoneWall(c, x0, x1, wallTop, baseY, TStone, 31);
            BeamRect(c, x0, wallTop, footW, 4);
            for (int k = 0; k < 4; k++)
            {
                int wx = k < 2 ? x0 + 12 + k * 30 : x1 - 26 - (3 - k) * 30;
                TWindow(c, wx, wallTop + 14, 14, 18, TStone[1], false, k);
            }
            // Porch: two pillars and a little roof over the double door.
            Door(c, cx, baseY - 3, 26, 34, true, true);
            foreach (int px in new[] { cx - 22, cx + 18 })
            {
                c.Rect(px, wallTop + 6, 5, wallH - 8, TStone[4]);
                c.VLine(px, wallTop + 6, baseY - 3, TStone[5]);
                c.VLine(px + 4, wallTop + 6, baseY - 3, TStone[2]);
                c.Rect(px - 1, wallTop + 5, 7, 2, TStone[3]);
            }
            // Plaque over the door.
            c.Rect(cx - 12, wallTop + 7, 24, 7, PixelCanvas.Hex("#e8c050"));
            c.HLine(cx - 12, cx + 11, wallTop + 7, PixelCanvas.Hex("#ffe07a"));
            c.HLine(cx - 8, cx + 7, wallTop + 10, PixelCanvas.Hex("#9a6a14"));
            int roofBottom = wallTop + 5, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 10, roofBottom, 14, RoofBlue, 300);
            Chimney(c, x0 + 22, roofTop + 6, roofTop + 30, true);
            // Front gable with a round window and a bell.
            int gTop = roofTop, gBase = roofBottom - 2, gHalf = 34;
            for (int y = gTop + 6; y <= gBase; y++)
            {
                float p = (y - gTop - 6) / (float)(gBase - gTop - 6);
                int hw = Mathf.RoundToInt(gHalf * p);
                for (int x = cx - hw; x <= cx + hw; x++)
                {
                    bool edge = Mathf.Abs(x - cx) > hw - 5;
                    c.Set(x, y, edge ? (x < cx ? RoofBlue[3] : RoofBlue[1]) : Plaster[2]);
                }
            }
            c.Circle(cx + 0.5f, gBase - 16f, 7f, Beam[1]);
            c.Circle(cx + 0.5f, gBase - 16f, 5.3f, PixelCanvas.Hex("#e8c050"));
            c.Rect(cx - 2, gBase - 18, 5, 5, PixelCanvas.Hex("#9a6a14"));
            c.Set(cx - 1, gBase - 19, PixelCanvas.Hex("#ffe07a"));
            // Banners.
            foreach (int bx in new[] { x0 + 50, x1 - 56 })
            {
                c.Rect(bx, wallTop + 4, 8, 16, PixelCanvas.Hex("#3d669a"));
                c.Set(bx + 3, wallTop + 11, PixelCanvas.Hex("#e8c050")); c.Set(bx + 4, wallTop + 11, PixelCanvas.Hex("#e8c050"));
                c.Set(bx, wallTop + 20, PixelCanvas.Hex("#3d669a")); c.Set(bx + 7, wallTop + 20, PixelCanvas.Hex("#3d669a"));
            }
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>General store (4x3): green roof, striped awning over a window full of potions, potion sign.</summary>
        static PixelCanvas TownStore()
        {
            const int footW = 128, wallH = 46, roofH = 50;
            int W = footW + 14, Hh = roofH + wallH + 16;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1;
            GroundShadow(c, x0, x1, wallTop, baseY);
            PlasterWall(c, x0, x1, wallTop, baseY, 41);
            Footing(c, x0, x1, baseY, 6);
            BeamRect(c, x0, wallTop, footW, 4);
            BeamRect(c, x0, wallTop, 3, wallH - 6); BeamRect(c, x1 - 2, wallTop, 3, wallH - 6);
            // Big shop window with shelves of potions.
            int sx = x0 + 10, sy = wallTop + 16, sw = 62, sh = 20;
            c.Rect(sx - 3, sy - 3, sw + 6, sh + 6, Beam[1]);
            c.Rect(sx, sy, sw, sh, PixelCanvas.Hex("#3a2e28"));
            string[] bottle = { "#e0413a", "#3f7ee8", "#5fbf4a", "#e8c050", "#b77bff", "#ff8a3d" };
            for (int shelf = 0; shelf < 2; shelf++)
            {
                int by = sy + 3 + shelf * 9;
                c.HLine(sx, sx + sw - 1, by + 6, TWood[3]);
                for (int k = 0; k < 9; k++)
                {
                    var col = PixelCanvas.Hex(bottle[(k + shelf * 2) % bottle.Length]);
                    int bx = sx + 3 + k * 7;
                    c.Rect(bx, by + 1, 4, 5, col); c.Set(bx + 1, by, PixelCanvas.Hex("#d8eef6")); c.Set(bx, by + 2, PixelCanvas.Shade(col, 1.35f));
                }
            }
            for (int k = 0; k < sw; k += 3) c.Set(sx + k, sy + sh - 1 - (k % 7 == 0 ? 1 : 0), new Color32(255, 255, 255, 60));
            Awning(c, sx - 6, sx + sw + 5, wallTop + 5, 8, PixelCanvas.Hex("#3f9a5a"), PixelCanvas.Hex("#f1ead6"));
            Door(c, x1 - 30, baseY - 5, 18, 30, false, false);
            HangingSign(c, x1 - 12, wallTop + 10, "potion");
            int roofBottom = wallTop + 5, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 8, roofBottom, 12, RoofGreen, 410);
            for (int x = x0; x <= x1; x++) c.Set(x, roofBottom + 1, new Color32(0, 0, 0, 45));
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>Smithy (4x3): stone walls, slate roof, an open forge bay glowing orange, anvil sign.</summary>
        static PixelCanvas TownSmithy()
        {
            const int footW = 128, wallH = 46, roofH = 46;
            int W = footW + 14, Hh = roofH + wallH + 26;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1;
            GroundShadow(c, x0, x1, wallTop, baseY);
            StoneWall(c, x0, x1, wallTop, baseY, TStone, 51);
            BeamRect(c, x0, wallTop, footW, 4);
            // Open forge bay: dark inside, framed by a stone arch.
            var bayColor = PixelCanvas.Hex("#231a16");
            int bx = x0 + 10, bw = 56, bTop = wallTop + 10;
            float archR = bw / 2f * 0.45f;
            bool InBay(int x, int y)
            {
                if (x < bx || x >= bx + bw || y < bTop || y >= baseY - 2) return false;
                if (y >= bTop + archR) return true;
                float dx = (x + 0.5f - (bx + bw / 2f)) / (bw / 2f), dy = (y + 0.5f - (bTop + archR)) / archR;
                return dx * dx + dy * dy <= 1f;
            }
            for (int y = bTop - 4; y < baseY - 2; y++)
                for (int x = bx - 4; x < bx + bw + 4; x++)
                {
                    if (InBay(x, y)) c.Set(x, y, bayColor);
                    else if (InBay(x + 3, y) || InBay(x - 3, y) || InBay(x, y + 3) || InBay(x + 2, y + 2) || InBay(x - 2, y + 2))
                        c.Set(x, y, x < bx + bw / 2 ? TStone[5] : TStone[3]);
                }
            // Forge: brick hearth with coals and flames, lighting up the bay.
            for (int y = bTop; y < baseY - 2; y++)
                for (int x = bx; x < bx + bw; x++)
                {
                    if (!InBay(x, y)) continue;
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(bx + 20, baseY - 20));
                    if (d < 24f) c.Set(x, y, Mix(bayColor, PixelCanvas.Hex("#7a3418"), 1f - d / 24f));
                }
            c.Rect(bx + 8, baseY - 16, 24, 14, PixelCanvas.Hex("#6e3a2a"));
            c.HLine(bx + 8, bx + 31, baseY - 16, PixelCanvas.Hex("#8e4e36"));
            c.Rect(bx + 11, baseY - 20, 18, 5, PixelCanvas.Hex("#ff7a2a"));
            c.Rect(bx + 13, baseY - 23, 14, 3, PixelCanvas.Hex("#ffb54a"));
            c.Rect(bx + 16, baseY - 26, 8, 3, PixelCanvas.Hex("#ffe28a"));
            // Tools on the back wall.
            c.Line(bx + 40, bTop + 12, bx + 40, bTop + 28, TWood[3]); c.Rect(bx + 37, bTop + 10, 7, 3, TIron[3]);
            c.Line(bx + 47, bTop + 12, bx + 47, bTop + 30, TWood[2]); c.Rect(bx + 45, bTop + 30, 5, 4, TIron[2]);
            Door(c, x1 - 24, baseY - 3, 16, 30, false, false);
            HangingSign(c, x1 - 50, wallTop + 10, "anvil");
            int roofBottom = wallTop + 5, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 8, roofBottom, 12, RoofSlate, 520);
            // Stone chimney with dark smoke.
            int chx = x0 + 24;
            StoneWall(c, chx, chx + 13, roofTop - 10, roofTop + 22, TStone, 61, 5);
            c.Rect(chx - 2, roofTop - 13, 18, 4, TStone[1]);
            var smoke = new Color32(90, 90, 96, 150);
            c.Circle(chx + 8f, roofTop - 17f, 4f, smoke);
            c.Circle(chx + 12f, roofTop - 23f, 4.5f, new Color32(110, 110, 116, 110));
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>Warehouse (4x3): plank walls, brown roof with a loft door, big braced double doors, box sign.</summary>
        static PixelCanvas TownWarehouse()
        {
            const int footW = 128, wallH = 48, roofH = 52;
            int W = footW + 14, Hh = roofH + wallH + 16;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, wallTop, baseY);
            PlankWall(c, x0, x1, wallTop, baseY, TWood, 71);
            Footing(c, x0, x1, baseY, 5);
            BeamRect(c, x0, wallTop, footW, 4);
            BeamRect(c, x0, wallTop, 4, wallH - 5); BeamRect(c, x1 - 3, wallTop, 4, wallH - 5);
            // Double barn doors with X braces.
            int dw = 44, dh = 36, dx0 = cx - dw / 2, dTop = baseY - 4 - dh;
            c.Rect(dx0 - 3, dTop - 3, dw + 6, dh + 3, Beam[1]);
            PlankWall(c, dx0, dx0 + dw - 1, dTop, baseY - 5, Ramp("#5a3a24", "#7a5030", "#96663c", "#b07c4c"), 72);
            foreach (int half in new[] { 0, 1 })
            {
                int hx = dx0 + half * (dw / 2);
                c.Line(hx + 1, dTop + 1, hx + dw / 2 - 2, baseY - 6, Beam[3]); c.Line(hx + dw / 2 - 2, dTop + 1, hx + 1, baseY - 6, Beam[3]);
                c.Line(hx + 2, dTop + 1, hx + dw / 2 - 1, baseY - 6, Beam[1]);
            }
            c.VLine(cx, dTop, baseY - 5, Beam[0]);
            c.Rect(dx0 - 4, baseY - 4, dw + 8, 3, TStone[3]);
            TWindow(c, x0 + 12, wallTop + 12, 12, 12, Beam[1], false, 2);
            TWindow(c, x1 - 23, wallTop + 12, 12, 12, Beam[1], false, 4);
            HangingSign(c, x0 + 10, wallTop + 30, "box");
            int roofBottom = wallTop + 5, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 8, roofBottom, 12, RoofBrown, 610);
            // Hay loft door in the roof, with a little hay poking out.
            c.Rect(cx - 10, roofTop + 22, 20, 20, Beam[1]);
            c.Rect(cx - 8, roofTop + 24, 16, 18, PixelCanvas.Hex("#2a1c14"));
            c.Rect(cx - 7, roofTop + 36, 14, 6, PixelCanvas.Hex("#dcb654"));
            c.HLine(cx - 7, cx + 6, roofTop + 36, PixelCanvas.Hex("#f7e4a4"));
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>The finished village workshop (quest reward building, 3x3).</summary>
        static PixelCanvas TownWorkshop()
        {
            const int footW = 96, wallH = 40, roofH = 46;
            int W = footW + 14, Hh = roofH + wallH + 16;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, wallTop, baseY);
            PlankWall(c, x0, x1, wallTop, baseY, Ramp("#6e4a2e", "#8e6238", "#b07c48", "#caa068", "#e0bc86"), 81);
            Footing(c, x0, x1, baseY, 5);
            BeamRect(c, x0, wallTop, footW, 4);
            TWindow(c, x0 + 10, wallTop + 12, 14, 12, Beam[1], true, 9);
            Door(c, cx + 14, baseY - 4, 18, 28, false, false);
            HangingSign(c, cx - 22, wallTop + 10, "saw");
            int roofBottom = wallTop + 5, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 8, roofBottom, 10, RoofOrange, 710);
            Chimney(c, x0 + 14, roofTop + 4, roofTop + 24, true);
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>Unfinished workshop: stone footing, timber frame, a ladder and a plank stack (3x3).</summary>
        static PixelCanvas TownBlueprint()
        {
            const int footW = 96;
            int W = footW + 14, Hh = 100;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, x0 = 7, x1 = 7 + footW - 1;
            // Trodden earth and the stone footing outline.
            c.Ellipse(W / 2f, baseY - 16f, 52f, 20f, new Color32(120, 84, 52, 90));
            StoneWall(c, x0, x1, baseY - 6, baseY, TStone, 91, 3);
            StoneWall(c, x0 + 6, x1 - 6, baseY - 42, baseY - 38, TStone, 92, 3);
            // Timber frame: posts and beams.
            foreach (int px in new[] { x0 + 2, x0 + 32, x1 - 34, x1 - 4 })
            {
                BeamRect(c, px, baseY - 64, 4, 58);
            }
            BeamRect(c, x0, baseY - 66, footW, 4);
            BeamRect(c, x0 + 2, baseY - 36, footW - 4, 3);
            c.Line(x0 + 6, baseY - 8, x0 + 30, baseY - 34, Beam[2]);
            c.Line(x1 - 6, baseY - 8, x1 - 32, baseY - 34, Beam[2]);
            // Roof rafters, not yet covered.
            for (int k = 0; k < 6; k++)
            {
                int rx = x0 + 6 + k * 17;
                c.Line(rx, baseY - 66, rx + 8, baseY - 88, Beam[2]); c.Line(rx + 1, baseY - 66, rx + 9, baseY - 88, Beam[3]);
            }
            BeamRect(c, x0 + 12, baseY - 90, footW - 22, 3);
            // Ladder and a plank stack.
            c.Line(x1 - 12, baseY - 2, x1 - 2, baseY - 52, TWood[3]); c.Line(x1 - 6, baseY - 2, x1 + 4, baseY - 52, TWood[3]);
            for (int k = 1; k < 9; k++) c.Line(x1 - 12 + k * 10 / 9, baseY - 2 - k * 6, x1 - 6 + k * 10 / 9, baseY - 2 - k * 6, TWood[2]);
            for (int k = 0; k < 4; k++) { c.Rect(x0 + 14, baseY - 14 + k * 3, 40, 3, TWood[3 + (k % 2)]); c.HLine(x0 + 14, x0 + 53, baseY - 14 + k * 3, TWood[5]); }
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>Plaza fountain (3x3): round stone basin, water with ripples, a spouting bowl in the middle. v = animation frame.</summary>
        static PixelCanvas TownFountain(int frame)
        {
            int W = 104, Hh = 90;
            var c = Hd(W, Hh);
            float cx = W / 2f;
            int baseY = Hh - 5;
            c.Ellipse(cx + 4f, baseY - 8f, 48f, 16f, BuildShadow);
            float by = baseY - 22f; // basin centre
            // Front wall of the basin.
            for (int y = (int)by; y <= baseY - 2; y++)
                for (int x = 4; x < W - 4; x++)
                {
                    float nx = (x + 0.5f - cx) / 46f, ny = (y + 0.5f - by) / 19f;
                    if (nx * nx > 1f) continue;
                    float edgeY = by + 19f * Mathf.Sqrt(1f - nx * nx);
                    if (y > edgeY + 12f) continue;
                    float u = (x - 4f) / (W - 8f);
                    int idx = u < 0.25f ? 4 : u < 0.6f ? 3 : u < 0.85f ? 2 : 1;
                    if (((y - (int)by) % 6 == 5) || ((x + (y / 6) * 5) % 13 == 0)) idx = 1;
                    c.Set(x, y, TStone[idx]);
                }
            // Rim and water.
            ShadeBlob(c, cx, by, 46f, 19f, new[] { TStone[3], TStone[4], TStone[5], TStone[5] }, 0.3f, false);
            c.Ellipse(cx, by + 0.5f, 40f, 15f, FWaterDeep);
            c.Ellipse(cx, by + 1.5f, 38f, 13.5f, FWaterMid);
            var rng = new System.Random(5 + frame);
            for (int k = 0; k < 16; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.35f + (float)rng.NextDouble() * 0.55f;
                int rx0 = Mathf.RoundToInt(cx + Mathf.Cos(a) * 36f * d), ry0 = Mathf.RoundToInt(by + 1f + Mathf.Sin(a) * 12f * d);
                c.HLine(rx0, rx0 + 3 + rng.Next(0, 4), ry0, FWaterLight);
            }
            // Ring of ripples where the spouts land.
            for (int k = 0; k < 24; k++)
            {
                float a = k / 24f * Mathf.PI * 2f + frame * 0.13f;
                c.Set(Mathf.RoundToInt(cx + Mathf.Cos(a) * 17f), Mathf.RoundToInt(by + 1f + Mathf.Sin(a) * 6f), Foam);
            }
            // Pedestal, upper bowl and water arcs.
            c.Rect((int)cx - 5, (int)by - 30, 10, 30, TStone[3]);
            c.VLine((int)cx - 5, (int)by - 30, (int)by - 1, TStone[5]);
            c.VLine((int)cx + 4, (int)by - 30, (int)by - 1, TStone[1]);
            ShadeBlob(c, cx, by - 30f, 15f, 5.5f, new[] { TStone[2], TStone[3], TStone[4], TStone[5] }, 0.2f, false);
            c.Ellipse(cx, by - 30.5f, 11f, 3.2f, FWaterMid);
            c.Rect((int)cx - 1, (int)by - 42, 3, 12, FWaterLight);
            c.Set((int)cx, (int)by - 43, White);
            foreach (int side in new[] { -1, 1 })
                for (int k = 0; k <= 16; k++)
                {
                    float t = k / 16f;
                    float x = cx + side * (6f + t * 12f);
                    float y = by - 42f + t * t * 36f + (1f - t) * 4f - 4f * Mathf.Sin(t * Mathf.PI);
                    c.Set(Mathf.RoundToInt(x), Mathf.RoundToInt(y), (k + frame) % 4 == 0 ? White : FWaterLight);
                    c.Set(Mathf.RoundToInt(x), Mathf.RoundToInt(y) + 1, FWaterMid);
                }
            c.Outline(TOutlineStone);
            return c.WithPivot(W / 2f, 5f);
        }

        static readonly Color32 FWaterDeep = PixelCanvas.Hex("#2f6fb4");
        static readonly Color32 FWaterMid = PixelCanvas.Hex("#4c95d8");
        static readonly Color32 FWaterLight = PixelCanvas.Hex("#a9dcf5");

        /// <summary>Stone well with a little shingled roof and a bucket (2x2).</summary>
        static PixelCanvas TownWell()
        {
            int W = 68, Hh = 78;
            var c = Hd(W, Hh);
            float cx = W / 2f;
            int baseY = Hh - 5;
            c.Ellipse(cx + 3f, baseY - 5f, 28f, 8f, BuildShadow);
            float by = baseY - 17f;
            for (int y = (int)by; y <= baseY - 2; y++)
                for (int x = 6; x < W - 6; x++)
                {
                    float nx = (x + 0.5f - cx) / 26f;
                    if (nx * nx > 1f) continue;
                    float edgeY = by + 10f * Mathf.Sqrt(1f - nx * nx);
                    if (y > edgeY + 8f) continue;
                    float u = (x - 6f) / (W - 12f);
                    int idx = u < 0.25f ? 4 : u < 0.6f ? 3 : u < 0.85f ? 2 : 1;
                    if (((y - (int)by) % 5 == 4) || ((x + (y / 5) * 4) % 9 == 0)) idx = 1;
                    c.Set(x, y, TStone[idx]);
                }
            ShadeBlob(c, cx, by, 26f, 10f, new[] { TStone[3], TStone[4], TStone[5], TStone[5] }, 0.3f, false);
            c.Ellipse(cx, by + 0.5f, 20f, 7f, PixelCanvas.Hex("#1a2a3a"));
            c.Ellipse(cx, by + 2f, 16f, 4.5f, PixelCanvas.Hex("#2a4a6a"));
            // Posts, crank, rope and bucket.
            BeamRect(c, 9, 14, 4, (int)by - 12);
            BeamRect(c, W - 13, 14, 4, (int)by - 12);
            c.HLine(12, W - 12, 24, TWood[2]); c.HLine(12, W - 12, 25, TWood[1]);
            c.VLine((int)cx, 26, (int)by - 6, PixelCanvas.Hex("#c8a878"));
            c.Rect((int)cx - 5, (int)by - 8, 10, 8, TWood[3]); c.HLine((int)cx - 5, (int)cx + 4, (int)by - 8, TIron[3]);
            c.HLine((int)cx - 5, (int)cx + 4, (int)by - 3, TIron[2]);
            c.Line(W - 10, 24, W - 4, 30, TIron[2]);
            Shingles(c, 2, W - 3, 2, 18, 8, TRoofRed, 810);
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }
    }
}
