using UnityEngine;

namespace DotRPG
{
    public static partial class WinterTerrain
    {
        /// <summary>
        /// Granite steps climbing north (see StairPainter): large staggered slabs, a lit nosing over a shaded
        /// riser, coping-stone cheek walls capped with snow, snow banks along both walls and a swept stone
        /// path up the middle (StairSnowAt).
        /// </summary>
        static Color32 StairsAt(Job j, int px, int py)
        {
            var w = StairWell(j, px / Px, py / Px);
            var s = StairPainter.At(w, px, py, j.seed + 70);
            var c = StairRamp[s.tone];
            switch (s.part)
            {
                case StairPainter.Part.Wall:
                    // Snow on the coping; the outer edge and the inner drop stay stone.
                    if (s.wallD >= 1 && s.wallD <= StairPainter.WallPx - 2)
                    {
                        int t = s.wallD == StairPainter.WallPx - 2 ? 1 : s.wallD == 1 ? 4 : 3;
                        if (s.rnd > 0.94f) t = Mathf.Max(1, t - 1);
                        c = SnowC[t];
                    }
                    return c;
                case StairPainter.Part.WallFace:
                    // Snow cap on the wall's end, with a shaded lip just below it.
                    if (s.cyc == StairPainter.FaceRows - 1 && s.wallD > 0) c = SnowC[3];
                    else if (s.cyc == StairPainter.FaceRows - 2 && s.wallD > 0 && s.rnd < 0.6f) c = SnowC[1];
                    return c;
                case StairPainter.Part.Riser:
                case StairPainter.Part.Tread:
                    return StairSnowAt(w, s, px, py, c, j.seed + 71);
            }
            return c;
        }

        /// <summary>
        /// Snow on the steps: a bank along each wall (12-26px wide, changing from step to step) buries
        /// the treads and risers there into a soft white slope where each step only shows as a pale teal
        /// front face; the swept path up the middle keeps the stone steps clear, with a thin edge of
        /// snow, a few icicles where the bank overhangs a riser, and the odd flake on the stone.
        /// </summary>
        static Color32 StairSnowAt(in StairPainter.Well w, in StairPainter.Sample s, int px, int py, Color32 stone, int seed)
        {
            if (s.shade != 1f) stone = PixelCanvas.Shade(stone, s.shade);
            bool riser = s.part == StairPainter.Part.Riser;
            // Drifts lie deepest at the back of each step, against the riser above, and thin out towards
            // its front edge, so every step's pile has its own scalloped outline.
            float into = riser ? 0f : s.landing ? 1f : Mathf.Clamp01((s.cyc - StairPainter.RiserPx) / 10f);
            float widen = 0.55f + 0.45f * (1f - (1f - into) * (1f - into));
            int depthL = Mathf.RoundToInt(StairBank(s.step, 0, py, seed) * widen) - s.fromL;   // > 0 inside the left drift
            int depthR = Mathf.RoundToInt(StairBank(s.step, 1, py, seed) * widen) - s.fromR;
            int depth = Mathf.Max(depthL, depthR);
            if (depth <= 0)
            {
                // Swept stone. Just beside a drift the riser carries a little lip of snow and an icicle or two.
                if (depth > -3 && riser)
                {
                    if (s.cyc == StairPainter.RiserPx - 1) return SnowC[2];
                    float ice = Hash01(px, s.step, seed + 4);
                    if (s.cyc == StairPainter.RiserPx - 2 && ice < 0.35f) return StairIce;
                    if (s.cyc == StairPainter.RiserPx - 3 && ice < 0.15f) return StairIce;
                }
                if (!riser && !s.joint && s.cyc > StairPainter.RiserPx + 1 && s.rnd > 0.99f) return SnowC[2];
                return stone;
            }
            bool nearWallL = depthL >= depthR && s.fromL < 3;
            bool nearWallR = depthR > depthL && s.fromR < 2;
            Color32 c;
            if (riser || (!s.landing && s.cyc == StairPainter.RiserPx && Noise(px / 6f, s.step * 3.3f, seed + 2) < -0.2f))
            {
                // Shaded front face of the drift, hanging over the riser (its rounded top edge wanders a row).
                int f = s.cyc == 0 ? 0 : s.cyc >= StairPainter.RiserPx - 1 ? 2 : 1;
                c = f == 0 ? StairSnowDeep : f == 1 ? SnowC[0] : SnowC[1];
            }
            else
            {
                int t = !s.landing && s.cyc <= StairPainter.RiserPx + 1 ? 2 : (s.cyc >= StairPainter.StepPx - 4 || s.landing) && !s.top ? 4 : 3;
                c = t >= 4 && s.rnd > 0.9f ? SnowSpark : SnowC[t];
            }
            if (depth <= 2) c = Blend(c, SnowC[1], 0.5f);                            // thin edge toward the path
            if (nearWallL) c = Blend(c, StairSnowDeep, 0.55f);                        // in the left wall's shade
            else if (nearWallR) c = Blend(c, SnowC[0], 0.4f);
            if (s.rnd < 0.03f) c = Blend(c, SnowC[0], 0.5f);
            return c;
        }

        static Color32 Blend(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

        /// <summary>Width in px of the snow drift on one side (0 = left, 1 = right) of a step at its back, gently wobbling.</summary>
        static int StairBank(int step, int side, int py, int seed)
        {
            float wv = 0.5f + 0.5f * Noise(step * 1.37f, side * 7.1f, seed);
            return 12 + Mathf.RoundToInt(13f * wv + 1.5f * Noise(side * 3.7f, py / 4f, seed + 1));
        }

        /// <summary>The rectangle of stair cells containing (cx, cy), in pixels.</summary>
        static StairPainter.Well StairWell(Job j, int cx, int cy)
        {
            int l = cx, r = cx, b = cy, t = cy;
            while (l > 0 && j.cells[cy * j.w + l - 1] == Stairs) l--;
            while (r < j.w - 1 && j.cells[cy * j.w + r + 1] == Stairs) r++;
            while (b > 0 && j.cells[(b - 1) * j.w + cx] == Stairs) b--;
            while (t < j.h - 1 && j.cells[(t + 1) * j.w + cx] == Stairs) t++;
            return new StairPainter.Well { x0 = l * Px, x1 = r * Px + Px - 1, y0 = b * Px, y1 = t * Px + Px - 1 };
        }

        static Color32 DeckAt(Job j, int px, int py)
        {
            // Bridge / lookout deck as ONE continuous surface: north-south boards (vertical, seen from above)
            // with 1px gaps, no per-cell interior lines. The measured outer extent gives the true north
            // (screen-top) and south (screen-bottom) edges; the timber rails sit only there.
            int top = j.deckTop != null ? j.deckTop[px] : -1;    // screen-top edge (north)
            int bot = j.deckBot != null ? j.deckBot[px] : -1;    // screen-bottom edge (south)
            int fromN = top >= 0 ? top - py : 999;               // pixels in from the north edge
            int fromS = bot >= 0 ? py - bot : 999;               // pixels in from the south edge

            // Continuous plank field: a board every 6 px across x, 1px dark seam between boards.
            int inBoard = Mod(px, 6);
            int board = px / 6;
            Color32 plankCol;
            if (inBoard == 0) plankCol = Plank[0];
            else
            {
                int tone = 2 + (int)(Hash01(board, 0, j.seed + 78) * 2.99f);
                if (inBoard == 1) tone = Mathf.Min(4, tone + 1);
                else if (inBoard == 5) tone = Mathf.Max(1, tone - 1);
                if (Noise(px / 3f, py / 9f, j.seed + 79) > 0.72f) tone = Mathf.Min(4, tone + 1);
                plankCol = Plank[Mathf.Clamp(tone, 0, 4)];
                // A couple of cross-batten shadows a fixed distance in from each outer edge (not cell seams).
                if (inBoard == 3 && (fromN == 4 || fromS == 4)) plankCol = Plank[1];
            }

            bool post;
            // North rail (screen-top outer edge, faces the water): snow-capped timber rail on posts.
            post = px % 12 < 3;
            if (fromN == 0) return post ? SnowSpark : WhSnowCap(px);        // snow crest along the rail
            if (fromN == 1) return SnowC[3];
            if (fromN == 2) return post ? PlankPost : Lighten(PlankRail, 1.25f);  // lit top cap of the rail
            if (fromN == 3) return post ? PlankPost : PlankRail;                  // rail body
            if (fromN == 4) return post ? PlankPost : Darken(PlankRail, 0.72f);   // shaded underside of the rail
            if (fromN == 5) return SnowC[2];                                      // shadow the rail casts on the deck
            // South rail (screen-bottom outer edge).
            post = (px + 6) % 12 < 3;
            if (fromS == 0) return Plank[0];
            if (fromS == 1) return post ? PlankPost : Lighten(PlankRail, 1.15f);
            if (fromS == 2) return post ? PlankPost : PlankRail;
            if (fromS == 3) return post ? PlankPost : Darken(PlankRail, 0.72f);
            if (fromS == 4) return SnowC[2];                                      // soft shadow on the deck

            // Snow caught on the plank ends just inside the north rail.
            if (fromN >= 5 && fromN <= 8 && Noise(px / 4f, 0f, j.seed + 83) > 0.5f) return WMix(plankCol, SnowC[3], 0.55f);
            return plankCol;
        }

        static Color32 WhSnowCap(int px) => Noise(px / 4f, 0f, 321) > 0f ? SnowSpark : SnowC[4];
    }
}
