using UnityEngine;

namespace DotRPG
{
    public static partial class CanyonTerrain
    {
        /// <summary>Rows of cliff below this cell down to lower ground (1..4; 4 = deep rock mass / plateau).</summary>
        static int WallDepth(Job j, int cx, int cy)
        {
            int d = 1;
            while (d < 4 && j.CellKind(cx, cy - d) == Cliff) d++;
            byte below = j.CellKind(cx, cy - d);
            if (below == 255 || below == Cliff) return 4;   // sits on the map edge / more rock: deep mass
            return d;
        }

        static Color32 CliffAt(Job j, int px, int py)
        {
            int cx = px / Px, cy = py / Px;
            int localX = px - cx * Px;               // 0..31 within cell, x right
            int localTop = py - cy * Px;             // 0..31 within cell, y up
            int fromTop = Px - 1 - localTop;         // distance down from the cell's top edge

            int d = WallDepth(j, cx, cy);
            bool openAbove = j.CellKind(cx, cy + 1) != Cliff && j.CellKind(cx, cy + 1) != 255;
            bool openBelow = j.CellKind(cx, cy - 1) != Cliff && j.CellKind(cx, cy - 1) != 255;
            bool openLeft = j.CellKind(cx - 1, cy) != Cliff;
            bool openRight = j.CellKind(cx + 1, cy) != Cliff;

            // A cell is a WALL (south-facing face) when it has open lower ground within 3 rows below it.
            bool isWall = d <= 3;

            Color32 c = isWall ? WallPixel(j, px, py, cx, cy, localX, localTop, fromTop, d, openLeft, openRight)
                               : PlateauPixel(j, px, py, cx, cy, localX, localTop);

            // Lit rim / lip along every plateau edge (top surface open to the sky).
            if (openAbove)
            {
                // Small irregular overhang: the lip juts down a pixel or two here and there.
                int lip = 1 + (int)(1.5f * (0.5f + 0.5f * Noise(px / 5f, cy, j.seed + 210)));
                if (fromTop == 0) c = RockRimHi;
                else if (fromTop <= lip) c = RockRim;
                else if (fromTop == lip + 1) c = PixelCanvas.Shade(RockRim, 0.8f);
            }

            // Shadowed foot + darkening toward the base of a wall.
            if (openBelow)
            {
                if (localTop == 0) c = RockFoot;
                else if (localTop == 1) c = PixelCanvas.Shade(RockA[0], 0.75f);
                else if (localTop <= 3) c = PixelCanvas.Shade(c, 0.8f);
            }
            // Side faces where a wall ends (seen a little side-on): darker on the right, lighter on the left.
            if (openRight && localX >= Px - 3) c = PixelCanvas.Shade(c, 0.72f);
            if (openLeft && localX <= 2) c = PixelCanvas.Shade(c, 1.12f);
            return c;
        }

        /// <summary>
        /// Sunlit rocky plateau shelf: broken rock slabs (a jittered Voronoi of large cells) with dark
        /// cracks along the joints, per-slab tone, a few loose stones, and slightly darker sheltered rock
        /// near the rim. Stays clearly lighter than the wall face below.
        /// </summary>
        static Color32 PlateauPixel(Job j, int px, int py, int cx, int cy, int localX, int localTop)
        {
            // Nearest slab centre (Voronoi) on a ~18px jittered grid, plus the distance to the 2nd centre
            // so we can darken the crack between slabs.
            const int SlabCell = 18;
            int gx = Mathf.FloorToInt((float)px / SlabCell), gy = Mathf.FloorToInt((float)py / SlabCell);
            float d1 = 1e9f, d2 = 1e9f; int sxi = 0, syi = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = gx + dx, iy = gy + dy;
                    float fx = ix * SlabCell + SlabCell * 0.5f + (Hash01(ix, iy, j.seed + 223) - 0.5f) * 11f;
                    float fy = iy * SlabCell + SlabCell * 0.5f + (Hash01(ix, iy, j.seed + 224) - 0.5f) * 11f;
                    float ex = px + 0.5f - fx, ey = py + 0.5f - fy;
                    float dd = ex * ex + ey * ey;
                    if (dd < d1) { d2 = d1; d1 = dd; sxi = ix; syi = iy; }
                    else if (dd < d2) d2 = dd;
                }
            float crackGap = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);

            // Per-slab base tone (each broken slab a slightly different lightness), lit top-left within it.
            int slabRoll = (int)(Hash01(sxi, syi, j.seed + 225) * 3f);   // 0..2
            int tone = slabRoll == 0 ? 2 : slabRoll == 1 ? 3 : 4;
            // Coarse mottling inside the slab so it is not dead flat.
            float mott = Fbm(px / 12f, py / 12f, j.seed + 226);
            if (mott > 0.4f) tone = Mathf.Min(4, tone + 1);
            else if (mott < -0.45f) tone = Mathf.Max(1, tone - 1);
            // Loose pebbly speckle.
            float sp = Noise(px / 2.6f, py / 2.6f, j.seed + 221);
            if (sp > 0.9f) tone = Mathf.Min(4, tone + 1);
            else if (sp < -0.92f) tone = Mathf.Max(0, tone - 1);

            // Cracks along the slab joints: a dark line, with a lit lip on the upper side of the crack.
            if (crackGap < 1.4f) tone = 0;
            else if (crackGap < 2.6f) tone = Mathf.Min(4, tone + 1);

            // Slightly darker, sheltered rock near the rim (top of the plateau, before it drops to the wall).
            int fromTopOfCell = Px - 1 - localTop;
            bool openAbove = j.CellKind(cx, cy + 1) != Cliff && j.CellKind(cx, cy + 1) != 255;
            if (!openAbove)
            {
                // How many rows down from the plateau's own top surface (rim) are we?
                int rimDrop = 0;
                for (int u = 1; u <= Px; u++) { if (j.KindAt(px, py + u) != Cliff) { rimDrop = u; break; } if (u == Px) rimDrop = Px; }
                if (rimDrop <= 6) tone = Mathf.Max(0, tone - 1);
            }
            return PlateauC[Mathf.Clamp(tone, 0, 4)];
        }

        /// <summary>
        /// Vertical wall: chunky warm sedimentary strata whose wobbly boundaries continue across cells,
        /// each band a different warmth and lightness, carrying rounded ledge volumes lit from the
        /// top-left with a shadowed underside and a shelf highlight where one band overhangs the next.
        /// </summary>
        static Color32 WallPixel(Job j, int px, int py, int cx, int cy, int localX, int localTop, int fromTop, int d, bool openLeft, bool openRight)
        {
            // Wobbly world-y for the strata: bands drift up/down with world-x so a layer flows across cells.
            float wob = 3.2f * Noise(px / 34f, 0f, j.seed + 230) + 1.4f * Noise(px / 12f, 0f, j.seed + 231);
            float sy = py + wob;
            // Uneven band heights (9-14 px): find which stratum this sy falls in by walking a hashed height list.
            int stratum; float bandTop, bandBot;
            StratumAt(sy, j.seed + 232, out stratum, out bandTop, out bandBot);
            float bandH = Mathf.Max(1f, bandBot - bandTop);
            float withinBand = (sy - bandTop) / bandH;      // 0 top → 1 bottom of the band

            // Each band: warm vs cool ramp + its own base lightness, so adjacent layers clearly differ.
            bool warm = (stratum & 1) == 0;
            var ramp = warm ? RockA : RockB;
            int bandRoll = (int)(Hash01(stratum, 3, j.seed + 233) * 3f);   // 0..2
            int baseTone = bandRoll == 0 ? 2 : bandRoll == 1 ? 3 : 4;

            // Rounded ledge blocks along the band: variable width, jittered, lit top-left.
            float blockW = 16f + 8f * Hash01(stratum, 7, j.seed + 234);
            float bxRaw = px + 5f * Noise(px / 30f, stratum, j.seed + 235);
            int blockIdx = Mathf.FloorToInt(bxRaw / blockW);
            float bx = blockIdx * blockW + blockW * 0.5f + (Hash01(blockIdx, stratum, j.seed + 236) - 0.5f) * 5f;
            float bcy = (bandTop + bandBot) * 0.5f;
            float ex = (px + 0.5f - bx) / (blockW * 0.5f);
            float ey = (sy - bcy) / (bandH * 0.5f);
            float dd = ex * ex + ey * ey * 0.85f;

            int tone = baseTone;
            if (dd < 1f)
            {
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - dd));
                float lit = -ex * 0.55f - ey * 0.5f + nz * 0.45f;
                if (lit > 0.5f) tone = baseTone + 2;
                else if (lit > 0.15f) tone = baseTone + 1;
                else if (lit < -0.45f) tone = baseTone - 2;
                else if (lit < -0.15f) tone = baseTone - 1;
            }
            else tone = baseTone - 2;   // recessed gap between ledge blocks

            // Strata joints: a dark shadow line at each band top, a lit shelf just under it (overhang).
            if (withinBand < 0.10f) tone -= 2;
            else if (withinBand < 0.22f) tone += 1;

            // Faint pitting so flat faces are not dead-flat (coarse, not single-pixel noise).
            float pit = Noise(px / 3.4f, sy / 3.4f, j.seed + 237);
            if (pit > 0.9f) tone += 1; else if (pit < -0.92f) tone -= 1;

            // The whole wall darkens toward its foot.
            float depthT = fromTop / (float)Mathf.Max(1, Px * d);
            if (depthT > 0.55f) tone -= 1;
            if (depthT > 0.8f) tone -= 1;

            return ramp[Mathf.Clamp(tone, 0, ramp.Length - 1)];
        }

        /// <summary>Finds the sedimentary band containing world-y <paramref name="sy"/>: uneven 9-14px heights.</summary>
        static void StratumAt(float sy, int seed, out int stratum, out float top, out float bot)
        {
            // Deterministic walk from a fixed origin so bands are stable and continuous.
            int guess = Mathf.FloorToInt(sy / 11.5f) - 2;
            float edge = guess * 11.5f;
            stratum = guess;
            // Re-derive exact edges by accumulating hashed heights around the guess.
            top = edge;
            for (int s = guess; s < guess + 6; s++)
            {
                float hgt = 9f + 5f * Hash01(s, 1, seed);
                if (sy < top + hgt) { stratum = s; bot = top + hgt; return; }
                top += hgt;
            }
            bot = top + 11f;
        }

        // ---------- Stairs: stone steps cut into the cliff ----------
    }
}
