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
    }
}
