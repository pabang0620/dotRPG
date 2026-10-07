using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 32px (density 2) art of the canyon town: sprite keys "cyn_*". Only the canyon-specific
    /// landmarks live here - the inn, the market stall, the red- and green-roofed houses and the north
    /// gate. Everything else (barrel, crate, sign, fences, bushes, trees, rocks, well, bench, chest,
    /// anvil) reuses the shared "town_*" set, which already fits the canyon's warm palette.
    ///
    /// House style matches ProceduralArtTownBuildings.cs: a 3/4 view with a roof slope on top, the front
    /// wall below with a door in the middle and a shadow to the lower right; the pivot is the middle of
    /// the front wall's base line. All the shared helpers (Hd, Ramp, Shingles, PlasterWall, StoneWall,
    /// Door, TWindow, Awning, GroundShadow, Footing, Chimney, BeamRect, ShadeBlob...) belong to this same
    /// partial class, so they are called directly.
    /// </summary>
    public static partial class ProceduralArt
    {
        // Canyon-flavoured ramps: adobe / sandstone walls, terracotta and sun-baked green roofs.
        static readonly Color32[] CynAdobe = Ramp("#a9764a", "#c08c5b", "#d3a271", "#e3b98c", "#f0cda4");
        static readonly Color32[] CynSandstone = Ramp("#8c7150", "#a68a64", "#bda079", "#d0b590", "#e0c8a6");
        static readonly Color32[] CynRoofRed = Ramp("#7a2f1c", "#9c3f26", "#bd5433", "#d66e45", "#ea8c60");
        static readonly Color32[] CynRoofGreen = Ramp("#3a5230", "#4b6b3c", "#5f8449", "#79a05c", "#96bb74");
        static readonly Color32[] CynCanvas = Ramp("#b8a37a", "#cbb891", "#dccaa6", "#ece0c4");
        static readonly Color32 CynTerracotta = PixelCanvas.Hex("#c15a33");

        /// <summary>cyn_* keys: the canyon-specific 32px buildings.</summary>
        static PixelCanvas DrawCanyonHd(string[] p)
        {
            if (p.Length < 2) return null;
            switch (p[1])
            {
                case "inn": return CynInn();
                case "stall": return CynStall();
                case "house": return CynHouse(p.Length > 2 && p[2] == "green");
                case "gate": return CynGate();
            }
            return null;
        }

        // ---------- Houses (3x3): adobe walls, flat-ish tiled roof, warm canyon look ----------

        /// <summary>Canyon house (3x3). Red terracotta roof by default, sun-baked green when <paramref name="green"/>.</summary>
        static PixelCanvas CynHouse(bool green)
        {
            var roof = green ? CynRoofGreen : CynRoofRed;
            int seed = green ? 621 : 613;
            const int footW = 96, wallH = 46, roofH = 46;
            int W = footW + 14, Hh = roofH + wallH + 16;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, wallTop, baseY);
            AdobeWall(c, x0, x1, wallTop, baseY, seed);
            // Sandstone footing / plinth.
            for (int y = baseY - 5; y <= baseY; y++)
                for (int x = x0; x <= x1; x++)
                    c.Set(x, y, y == baseY - 5 ? CynSandstone[3] : CynSandstone[(x + y) % 2 == 0 ? 1 : 2]);
            // Exposed roof beams (vigas) poking out just under the roofline.
            for (int bx = x0 + 6; bx < x1 - 4; bx += 14)
            {
                c.Rect(bx, wallTop - 3, 4, 3, TBark[1]);
                c.Set(bx, wallTop - 3, TBark[3]);
            }
            // Windows and an arched door.
            CynWindow(c, x0 + 12, wallTop + 12, 13, 13);
            CynWindow(c, x1 - 25, wallTop + 12, 13, 13);
            Door(c, cx, baseY - 5, 16, 28, true, false);
            // Flat, gently sloped tiled roof with a thick parapet - desert style, low pitch.
            int roofBottom = wallTop + 4, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 16, roofBottom, 8, roof, seed + 40);
            // A rounded terracotta ridge cap and small vent.
            c.HLine(10, W - 11, roofTop + 16, roof[4]);
            c.HLine(10, W - 11, roofTop + 17, roof[3]);
            // Sun bleaches the left slope brighter.
            for (int x = 2; x < W / 2; x++)
                for (int y = roofTop + 18; y <= roofBottom; y++)
                    if (c.IsOpaque(x, y) && ((x + y) % 5 == 0)) c.Paint(x, y, PixelCanvas.Shade(c.Get(x, y), 1.08f));
            // Eave shadow on the wall.
            for (int x = x0; x <= x1; x++) c.Set(x, roofBottom + 1, new Color32(0, 0, 0, 45));
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        /// <summary>Adobe/mud-brick wall: warm plaster with faint horizontal courses and specks.</summary>
        static void AdobeWall(PixelCanvas c, int x0, int x1, int top, int bottom, int seed)
        {
            for (int y = top; y <= bottom; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int hsh = IHash(x, y, seed);
                    int tone = hsh % 23 == 0 ? 1 : hsh % 31 == 0 ? 4 : (y - top) % 12 == 0 ? 1 : 2 + (hsh % 7 == 0 ? 1 : 0);
                    c.Set(x, y, CynAdobe[Mathf.Clamp(tone, 0, 4)]);
                }
            // Left wall catches the light.
            for (int y = top; y <= bottom; y++) { c.Set(x0, y, CynAdobe[4]); c.Set(x0 + 1, y, CynAdobe[3]); }
            for (int y = top; y <= bottom; y++) { c.Set(x1, y, CynAdobe[0]); c.Set(x1 - 1, y, CynAdobe[1]); }
        }

        /// <summary>Small window sunk into an adobe wall: a wooden shutter frame and a dark reveal.</summary>
        static void CynWindow(PixelCanvas c, int x, int y, int w, int h)
        {
            c.Rect(x - 2, y - 2, w + 4, h + 4, TBark[2]);              // wooden frame
            c.HLine(x - 2, x + w + 1, y - 2, TBark[3]);
            c.Rect(x, y, w, h, PixelCanvas.Hex("#2a1d14"));           // dark interior
            // A little sky glint in the top-left of the reveal.
            c.Set(x + 1, y + 1, PixelCanvas.Hex("#6f8f8a"));
            c.Set(x + 2, y + 1, PixelCanvas.Hex("#557370"));
            c.VLine(x + w / 2, y, y + h - 1, TBark[1]);               // mullion
            c.HLine(x, x + w - 1, y + h / 2, TBark[1]);               // transom
            c.HLine(x - 3, x + w + 2, y + h + 2, CynSandstone[3]);    // sill
        }

        // ---------- Inn (5x4 landmark): two-storey adobe with a big terracotta roof and a hanging sign ----------

        static PixelCanvas CynInn()
        {
            const int footW = 160, wallH = 66, roofH = 60;
            int W = footW + 14, Hh = roofH + wallH + 22;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, wallTop = baseY - wallH, x0 = 7, x1 = 7 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, wallTop, baseY);
            AdobeWall(c, x0, x1, wallTop, baseY, 701);
            // Sandstone base course.
            for (int y = baseY - 6; y <= baseY; y++)
                for (int x = x0; x <= x1; x++)
                    c.Set(x, y, y == baseY - 6 ? CynSandstone[3] : CynSandstone[(x + y) % 2 == 0 ? 1 : 2]);
            // A string course dividing the two storeys.
            c.HLine(x0, x1, wallTop + 30, TBark[1]);
            c.HLine(x0, x1, wallTop + 31, CynSandstone[3]);
            // Upper-storey windows.
            for (int k = 0; k < 4; k++)
                CynWindow(c, x0 + 14 + k * 36, wallTop + 8, 14, 14);
            // Ground floor: two windows and a wide arched double door in the middle.
            CynWindow(c, x0 + 12, wallTop + 40, 15, 16);
            CynWindow(c, x1 - 27, wallTop + 40, 15, 16);
            Door(c, cx, baseY - 5, 28, 36, true, true);
            // Hanging inn sign (a bed/moon motif drawn by the shared helper as a picture-less board).
            HangingSign(c, cx + 30, wallTop + 40, "inn");
            // Big low terracotta roof with exposed rafters (vigas) along the eave.
            int roofBottom = wallTop + 4, roofTop = roofBottom - roofH;
            Shingles(c, 1, W - 2, roofTop + 18, roofBottom, 12, CynRoofRed, 740);
            for (int bx = x0 + 4; bx < x1; bx += 16) { c.Rect(bx, roofBottom - 1, 4, 4, TBark[1]); c.Set(bx, roofBottom - 1, TBark[3]); }
            // A canvas awning over the entrance.
            Awning(c, cx - 34, cx + 33, roofBottom + 2, 9, CynTerracotta, CynCanvas[3]);
            for (int x = x0; x <= x1; x++) c.Set(x, roofBottom + 1, new Color32(0, 0, 0, 40));
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        // ---------- Market stall (3x2): open trader stall with a striped canvas roof ----------

        static PixelCanvas CynStall()
        {
            const int footW = 92, postH = 40;
            int W = footW + 12, Hh = postH + 40;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, x0 = 6, x1 = 6 + footW - 1;
            // Ground shadow.
            c.Ellipse((x0 + x1) / 2f, baseY - 1f, footW * 0.5f, 5.5f, TShadow);
            // Four wooden posts.
            foreach (int pxp in new[] { x0, x0 + 26, x1 - 27, x1 - 3 })
            {
                c.Rect(pxp, baseY - postH, 4, postH, TWood[2]);
                c.VLine(pxp, baseY - postH, baseY - 1, TWood[4]);
                c.VLine(pxp + 3, baseY - postH, baseY - 1, TWood[0]);
            }
            // Counter with goods.
            int cty = baseY - 16;
            c.Rect(x0 + 2, cty, footW - 4, 12, TWood[2]);
            c.HLine(x0 + 2, x1 - 2, cty, TWood[4]);
            c.HLine(x0 + 2, x1 - 2, cty + 11, TWood[0]);
            // Baskets of fruit / pottery on the counter.
            string[] wares = { "#d9362d", "#f59a2a", "#5fbf4a", "#b06448", "#e0c060", "#c15a33" };
            for (int k = 0; k < 6; k++)
            {
                int wx = x0 + 8 + k * 13;
                var col = PixelCanvas.Hex(wares[k % wares.Length]);
                c.Ellipse(wx + 3f, cty - 2f, 5f, 4f, col);
                c.Set(wx + 1, cty - 4, PixelCanvas.Shade(col, 1.4f));
                c.Ellipse(wx + 3f, cty + 1f, 5.5f, 2.5f, TBark[1]);   // basket rim
            }
            // Striped canvas roof, slightly sagging.
            int rTop = baseY - postH - 4;
            for (int x = x0 - 4; x <= x1 + 4; x++)
            {
                float t = (x - (x0 - 4)) / (float)(footW + 8);
                int sag = Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * 4f);
                int top = rTop - 6 + Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * -3f);
                for (int y = top; y <= rTop + sag; y++)
                {
                    bool stripe = ((x / 8) % 2) == 0;
                    var col = stripe ? CynTerracotta : CynCanvas[3];
                    if (y == top) col = PixelCanvas.Shade(col, 1.15f);
                    if (y >= rTop + sag - 1) col = PixelCanvas.Shade(col, 0.8f);
                    c.Set(x, y, col);
                }
            }
            // Scalloped valance hanging off the front edge.
            for (int x = x0 - 4; x <= x1 + 4; x++)
            {
                float t = (x - (x0 - 4)) / (float)(footW + 8);
                int sag = Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * 4f);
                int yv = rTop + sag + 1;
                if ((x % 8) < 6) { c.Set(x, yv, ((x / 8) % 2) == 0 ? CynTerracotta : CynCanvas[2]); if ((x % 8) < 4) c.Set(x, yv + 1, CynCanvas[1]); }
            }
            c.Outline(TOutline);
            return c.WithPivot(W / 2f, 5f);
        }

        // ---------- North gate (4x2): a closed stone gateway in the canyon wall ----------

        static PixelCanvas CynGate()
        {
            const int footW = 124, wallH = 78;
            int W = footW + 12, Hh = wallH + 14;
            var c = Hd(W, Hh);
            int baseY = Hh - 5, top = baseY - wallH, x0 = 6, x1 = 6 + footW - 1, cx = W / 2;
            GroundShadow(c, x0, x1, top, baseY);
            // Two stone towers flanking the gateway.
            int towerW = 34;
            StoneWall(c, x0, x0 + towerW, top, baseY, TStone, 811);
            StoneWall(c, x1 - towerW, x1, top, baseY, TStone, 813);
            // Crenellations on top of each tower.
            foreach (int tx in new[] { x0, x1 - towerW })
                for (int m = 0; m <= towerW; m += 12)
                {
                    c.Rect(tx + m, top - 6, 7, 6, TStone[2]);
                    c.HLine(tx + m, tx + m + 6, top - 6, TStone[4]);
                }
            // The archway between the towers, filled with heavy timber doors (closed).
            int ax0 = x0 + towerW + 2, ax1 = x1 - towerW - 2, aw = ax1 - ax0;
            int archTop = top + 14;
            float archR = aw / 2f;
            // Stone arch voussoirs.
            for (int y = top; y < baseY; y++)
                for (int x = ax0 - 6; x <= ax1 + 6; x++)
                {
                    bool inside;
                    if (y >= archTop) inside = x >= ax0 && x <= ax1;
                    else { float dx = (x + 0.5f - cx) / archR, dy = (y + 0.5f - archTop) / archR; inside = dx * dx + dy * dy <= 1f; }
                    bool nearArch;
                    {
                        float dx = (x + 0.5f - cx) / (archR + 6f), dy = (y + 0.5f - archTop) / (archR + 6f);
                        nearArch = (y >= archTop ? (x >= ax0 - 6 && x <= ax1 + 6) : dx * dx + dy * dy <= 1f);
                    }
                    if (inside)
                    {
                        // Timber doors: vertical planks with iron bands.
                        int board = (x - ax0) / 8;
                        var wood = (x - ax0) % 8 == 0 ? TWood[0] : TWood[board % 2 == 0 ? 2 : 1];
                        c.Set(x, y, wood);
                        if (x == cx || x == cx - 1) c.Set(x, y, TWood[0]);               // centre gap
                        if (y == top + wallH / 3 || y == top + 2 * wallH / 3) c.Set(x, y, TIron[2]); // iron bands
                    }
                    else if (nearArch)
                    {
                        c.Set(x, y, x < cx ? TStone[4] : TStone[2]);                      // arch stones
                    }
                }
            // Iron ring handles.
            c.Circle(cx - 5f, top + wallH / 2f, 2.2f, TIron[3]);
            c.Circle(cx + 5f, top + wallH / 2f, 2.2f, TIron[3]);
            // A keystone at the crown of the arch.
            c.Rect(cx - 4, archTop - (int)archR - 3, 8, 8, TStone[3]);
            c.HLine(cx - 4, cx + 3, archTop - (int)archR - 3, TStone[5]);
            c.Outline(TOutlineStone);
            return c.WithPivot(W / 2f, 5f);
        }
    }
}
