using UnityEngine;

namespace DotRPG
{
    public static partial class WinterTerrain
    {
        // ---------- River shape: smooth bank curves instead of per-cell steps ----------

        /// <summary>
        /// Rebuilds the river as two continuous bank curves. Water and waterfall cells define, per pixel
        /// row, the raw left/right edge of the channel (in pixels); those edges are low-pass filtered along
        /// y (a moving average about 2.5 tiles wide) and lightly noise-wobbled, then every pixel between the
        /// two smoothed curves becomes Water (or Fall, where the row's channel sits in a waterfall cell).
        /// The result is a smoothly meandering river with no one-cell stair steps; the painted bank is
        /// clamped so it never strays more than ~0.35 tile from the cell edge, keeping it honest against the
        /// per-cell colliders, the bridge ends and the waterfall.
        /// </summary>
        static void ShapeRiver(Job j)
        {
            int pw = j.pw, ph = j.ph, w = j.w;
            j.bankL = new float[ph];
            j.bankR = new float[ph];
            var rawL = new float[ph];
            var rawR = new float[ph];
            var hasFall = new bool[ph];   // this row's channel lies in waterfall cells
            const float None = -1f;

            for (int py = 0; py < ph; py++)
            {
                int cy = py / Px;
                int left = -1, right = -1;
                bool fall = false;
                // Extent of the true water/fall cells.
                for (int cx = 0; cx < w; cx++)
                {
                    byte k = j.cells[cy * w + cx];
                    if (k == Water || k == Fall)
                    {
                        if (left < 0) left = cx;
                        right = cx;
                        if (k == Fall) fall = true;
                    }
                }
                // A bridge deck spans the channel: extend the raw span through any deck cells that are
                // flanked by water (so the bank curve carries on smoothly across the bridge instead of
                // breaking, which used to leave a one-cell step just past the deck).
                for (int cx = 0; cx < w; cx++)
                {
                    if (j.cells[cy * w + cx] != Deck) continue;
                    bool wetL = cx > 0 && (j.cells[cy * w + cx - 1] == Water || j.cells[cy * w + cx - 1] == Fall);
                    bool wetR = cx < w - 1 && (j.cells[cy * w + cx + 1] == Water || j.cells[cy * w + cx + 1] == Fall);
                    // Also treat a deck cell whose column is water on other rows as spanning the channel.
                    if (!wetL && !wetR) continue;
                    if (left < 0 || cx < left) left = cx;
                    if (cx > right) right = cx;
                }
                if (left < 0) { rawL[py] = None; rawR[py] = None; continue; }
                rawL[py] = left * Px;              // left edge of the leftmost channel cell
                rawR[py] = (right + 1) * Px;       // right edge of the rightmost channel cell
                hasFall[py] = fall;
            }

            // Fill short gaps (fully-decked rows) by interpolating the raw edges, so the moving average is
            // never interrupted across the bridge.
            for (int py = 0; py < ph; py++)
            {
                if (rawL[py] >= 0f) continue;
                int a = py - 1; while (a >= 0 && rawL[a] < 0f) a--;
                int b = py + 1; while (b < ph && rawL[b] < 0f) b++;
                if (a < 0 || b >= ph) continue;               // only bridge interior gaps
                if (b - a > Px) continue;                     // don't span a real dry stretch
                float t = (py - a) / (float)(b - a);
                rawL[py] = Mathf.Lerp(rawL[a], rawL[b], t);
                rawR[py] = Mathf.Lerp(rawR[a], rawR[b], t);
            }

            // Low-pass the raw edges along y (moving average, ~1.6 tiles), skipping rows with no water.
            int radius = Mathf.RoundToInt(Px * 1.6f);
            for (int py = 0; py < ph; py++)
            {
                if (rawL[py] < 0f) { j.bankL[py] = None; j.bankR[py] = None; continue; }
                float sumL = 0f, sumR = 0f;
                int nL = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int qy = py + dy;
                    if (qy < 0 || qy >= ph || rawL[qy] < 0f) continue;
                    sumL += rawL[qy];
                    sumR += rawR[qy];
                    nL++;
                }
                float mL = sumL / nL, mR = sumR / nL;
                // Gentle meander so the banks are never perfectly straight.
                float ny = py / (float)Px;
                mL += 3.2f * Noise(ny * 0.42f, 0f, j.seed + 200) + 1.4f * Noise(ny * 1.5f, 7f, j.seed + 201);
                mR += 3.2f * Noise(ny * 0.42f, 13f, j.seed + 202) + 1.4f * Noise(ny * 1.5f, 21f, j.seed + 203);
                // Clamp so the smoothed bank never strays more than 0.35 tile from the raw cell edge.
                float lim = 0.35f * Px;
                mL = Mathf.Clamp(mL, rawL[py] - lim, rawL[py] + lim);
                mR = Mathf.Clamp(mR, rawR[py] - lim, rawR[py] + lim);
                if (mR < mL + 2f) mR = mL + 2f;
                j.bankL[py] = mL;
                j.bankR[py] = mR;
            }

            // Reclassify the channel region: pixels between the smoothed banks are water (or fall),
            // pixels just outside them revert to the land the cell says they are.
            System.Threading.Tasks.Parallel.For(0, ph, py =>
            {
                float bl = j.bankL[py], br = j.bankR[py];
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    byte cellK = j.cells[(py / Px) * w + px / Px];
                    byte cur = j.kind[i];
                    bool cellWet = cellK == Water || cellK == Fall;
                    bool nearChannel = bl >= 0f && px >= bl - Px && px <= br + Px;
                    if (!cellWet && !nearChannel) continue;         // far from the river: leave as-is
                    // Never disturb the man-made / solid cells that sit in the channel area.
                    if (cur == Deck || cur == Stairs || cellK == Cliff) continue;
                    if (bl < 0f) continue;
                    float fx = px + 0.5f;
                    if (fx >= bl && fx <= br)
                    {
                        j.kind[i] = hasFall[py] && cellK == Fall ? Fall : Water;
                        j.other[i] = Snow;
                        j.margin[i] = 1f;
                    }
                    else
                    {
                        // Outside the smoothed bank: it is land. Take the underlying land cell's kind
                        // (snow or cobble), so the shore reads correctly next to walkable ground.
                        byte land = cellK == Water || cellK == Fall ? Snow : cellK;
                        if (land == Water || land == Fall) land = Snow;
                        j.kind[i] = land == Cliff || land == Deck || land == Stairs ? Snow : land;
                        j.other[i] = Water;
                        float dist = fx < bl ? bl - fx : fx - br;
                        j.margin[i] = Mathf.Clamp01(dist / Px);
                    }
                }
            });
        }

        /// <summary>Per cliff pixel: how far down the rock mass it sits (0 = top edge, 1 = foot), for shading.</summary>
        static void MeasureCliffs(Job j)
        {
            int pw = j.pw, ph = j.ph;
            for (int px = 0; px < pw; px++)
            {
                int y = 0;
                while (y < ph)
                {
                    if (j.kind[y * pw + px] != Cliff) { y++; continue; }
                    int top = y;
                    while (y < ph && j.kind[y * pw + px] == Cliff) y++;
                    int bot = y - 1;
                    float span = Mathf.Max(1, bot - top);
                    for (int yy = top; yy <= bot; yy++) j.cliffFrac[yy * pw + px] = (bot - yy) / span;
                }
            }
        }

        /// <summary>
        /// Per pixel column, the outer top and bottom y of the contiguous deck run, so the deck can be
        /// painted as ONE continuous surface: rails and edges land only on the true outer silhouette, never
        /// on the internal cell seams that used to draw faint lines across the middle of the bridge.
        /// </summary>
        static void MeasureDecks(Job j)
        {
            int pw = j.pw, ph = j.ph;
            j.deckTop = new int[pw];
            j.deckBot = new int[pw];
            for (int px = 0; px < pw; px++)
            {
                j.deckTop[px] = -1;
                j.deckBot[px] = -1;
                for (int py = 0; py < ph; py++)
                {
                    if (j.kind[py * pw + px] != Deck) continue;
                    if (j.deckBot[px] < 0) j.deckBot[px] = py;   // lowest (screen-bottom) deck pixel
                    j.deckTop[px] = py;                          // highest (screen-top) deck pixel so far
                }
            }
        }
    }
}
