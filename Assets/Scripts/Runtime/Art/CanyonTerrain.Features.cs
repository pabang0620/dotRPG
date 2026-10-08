using UnityEngine;

namespace DotRPG
{
    public static partial class CanyonTerrain
    {
        /// <summary>
        /// Cut sandstone steps (see StairPainter): large staggered slabs, a lit nosing over a shaded riser,
        /// coping-stone cheek walls. Wind-blown sand gathers in the back corners and along the walls while
        /// the worn middle stays clean, and a few shaded corners hold a tuft of moss.
        /// </summary>
        static Color32 StairsAt(Job j, int px, int py)
        {
            var s = StairPainter.At(StairWell(j, px / Px, py / Px), px, py, j.seed + 90);
            var c = StairRamp[s.tone];
            if (s.shade != 1f) c = PixelCanvas.Shade(c, s.shade);
            if (s.part != StairPainter.Part.Tread || s.joint || s.cyc <= StairPainter.RiserPx + 1) return c;
            int edge = Mathf.Min(s.fromL, s.fromR);
            bool back = !s.top && s.cyc >= StairPainter.StepPx - 3;
            float grit = edge < 6 ? 0.36f - edge * 0.06f : 0f;
            if (back && edge < 10) grit += 0.12f;
            if (grit > 0f && s.rnd < grit * 0.5f) c = s.rnd < grit * 0.18f ? StairSand[1] : StairSand[0];
            if (back && edge < 6 && Hash01(s.step, s.fromL < s.fromR ? 1 : 2, j.seed + 97) < 0.3f)
            {
                float m = Noise(px / 2.2f, py / 2.2f, j.seed + 98) + (6 - edge) * 0.12f;
                if (m > 0.4f) c = StairMoss[m > 0.8f ? 2 : m > 0.6f ? 1 : 0];
            }
            return c;
        }

        /// <summary>The rectangle of stair cells containing (cx, cy), in pixels.</summary>
        static StairPainter.Well StairWell(Job j, int cx, int cy)
        {
            int l = cx, r = cx, b = cy, t = cy;
            while (j.CellKind(l - 1, cy) == Stairs) l--;
            while (j.CellKind(r + 1, cy) == Stairs) r++;
            while (j.CellKind(cx, b - 1) == Stairs) b--;
            while (j.CellKind(cx, t + 1) == Stairs) t++;
            return new StairPainter.Well { x0 = l * Px, x1 = r * Px + Px - 1, y0 = b * Px, y1 = t * Px + Px - 1 };
        }

        // ---------- Deck / bridge planks with rails ----------

        static int Mod(int a, int m) => (a % m + m) % m;

        static Color32 DeckAt(Job j, int px, int py)
        {
            int cx = px / Px, cy = py / Px;
            int local = Mod(px, Px);
            // Which edges of this deck cell are open (no neighbouring deck) - rails go there.
            bool openN = j.CellKind(cx, cy + 1) != Deck;
            bool openS = j.CellKind(cx, cy - 1) != Deck;
            bool openW = j.CellKind(cx - 1, cy) != Deck;
            bool openE = j.CellKind(cx + 1, cy) != Deck;

            int localTop = py - cy * Px;
            int fromTop = Px - 1 - localTop;

            // Rails on the open edges. A bridge crossing the N-S channel has WATER on its long E/W
            // sides, so the guard rails belong there (that is where you would fall in); its N/S ends
            // meet land with a low beam. A lookout deck rails every open side the same way.
            if (openW && local <= 3) return RailFace(py, cy, local, true);
            if (openE && local >= Px - 4) return RailFace(py, cy, Px - 1 - local, true);
            if (openN && fromTop <= 2) return RailFace(px, cx, fromTop, false);
            if (openS && localTop <= 2) return RailFace(px, cx, localTop, false);

            // Planks run NORTH-SOUTH (vertical boards seen from above): board index by x, grain along y.
            int board = px / 7;
            int inBoard = Mod(px, 7);
            if (inBoard == 0) return Plank[0];                       // plank gap
            int tone = 2 + (int)(Hash01(board, 0, j.seed + 100) * 2.99f); // 2..4 per board
            // Wood grain streaks along the board length.
            float grain = Noise(px / 2.2f, py / 9f, j.seed + 101);
            if (grain > 0.7f) tone = Mathf.Min(4, tone + 1);
            else if (grain < -0.75f) tone = Mathf.Max(1, tone - 1);
            if (inBoard == 1) tone = Mathf.Min(4, tone + 1);         // lit left edge of each board
            if (inBoard == 6) tone = Mathf.Max(1, tone - 1);         // shaded right edge
            // Nail dots near plank ends.
            if ((inBoard == 2 || inBoard == 5) && (Mod(py, 26) == 4 || Mod(py, 26) == 22)) return Plank[0];
            return Plank[Mathf.Min(4, tone)];
        }

        /// <summary>A wooden guard rail along a deck edge: a lit top cap, a shadowed under-rail, and posts.</summary>
        static Color32 RailFace(int along, int cell, int fromEdge, bool vertical)
        {
            bool post = Mod(along, 9) < 3;              // a post every ~9px
            if (fromEdge == 0) return post ? RailC[0] : RailC[1];   // outer edge / post shadow
            if (fromEdge == 1) return post ? RailC[3] : RailC[3];   // lit top rail cap
            if (fromEdge == 2) return post ? RailC[2] : RailC[1];   // under-rail
            return post ? RailC[1] : RailC[0];                       // inner shadow onto the deck
        }

        // ---------- 3. Water: coping, wet line, depth gradient, ripples ----------

        static void PaintWater(Job j)
        {
            int pw = j.pw, ph = j.ph;
            // Distance from each water pixel to the nearest shore (chamfer transform).
            var dist = new float[pw * ph];
            const float Far = 9999f;
            for (int i = 0; i < dist.Length; i++) dist[i] = j.kind[i] == Water ? Far : 0f;
            for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    if (dist[i] == 0f) continue;
                    float d = dist[i];
                    if (px > 0) d = Mathf.Min(d, dist[i - 1] + 1f);
                    if (py > 0) d = Mathf.Min(d, dist[i - pw] + 1f);
                    if (px > 0 && py > 0) d = Mathf.Min(d, dist[i - pw - 1] + 1.41f);
                    if (px < pw - 1 && py > 0) d = Mathf.Min(d, dist[i - pw + 1] + 1.41f);
                    dist[i] = d;
                }
            for (int py = ph - 1; py >= 0; py--)
                for (int px = pw - 1; px >= 0; px--)
                {
                    int i = py * pw + px;
                    if (dist[i] == 0f) continue;
                    float d = dist[i];
                    if (px < pw - 1) d = Mathf.Min(d, dist[i + 1] + 1f);
                    if (py < ph - 1) d = Mathf.Min(d, dist[i + pw] + 1f);
                    if (px < pw - 1 && py < ph - 1) d = Mathf.Min(d, dist[i + pw + 1] + 1.41f);
                    if (px > 0 && py < ph - 1) d = Mathf.Min(d, dist[i + pw - 1] + 1.41f);
                    dist[i] = d;
                }

            // Water body: depth gradient toward the middle, wet line just inside the shore, ripples.
            System.Threading.Tasks.Parallel.For(0, ph, py =>
            {
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    if (j.kind[i] != Water) continue;
                    float d = dist[i];
                    // Is a deck directly overhead (bridge)? Cast its shadow on the water.
                    int up = 0; byte upKind = Water;
                    for (int k = 1; k <= 8; k++) { byte kk = j.KindAt(px, py + k); if (kk == 255) break; if (kk != Water) { up = k; upKind = kk; break; } }
                    Color32 c;
                    if (d <= 1.5f) c = WetLine;                         // dark wet line hugging the coping
                    else if (up > 0 && upKind == Deck && up <= 6) c = PixelCanvas.Shade(WaterC[2], 0.82f); // bridge shadow
                    else
                    {
                        float depth = Mathf.Clamp01((d - 1.5f) / 22f) + Noise(px / 18f, py / 12f, j.seed + 111) * 0.16f;
                        c = depth > 0.74f ? WaterC[0] : depth > 0.52f ? WaterC[1] : depth > 0.3f ? WaterC[2] : depth > 0.12f ? WaterC[3] : WaterC[4];
                    }
                    j.px[i] = c;
                }
            });

            // Cut-stone coping: a band of 6-8px stone blocks along every land pixel bordering water.
            PaintCoping(j, dist);

            // Ripples on open water.
            // (kept in Stamps for the deterministic scatter)
        }

        static void PaintCoping(Job j, float[] dist)
        {
            int pw = j.pw, ph = j.ph;
            // Land distance to water (how deep into the land a pixel is), for the coping band width.
            var land = new float[pw * ph];
            const float Far = 9999f;
            for (int i = 0; i < land.Length; i++) land[i] = (j.kind[i] == Water) ? 0f : Far;
            // Only need a few px of reach - do a small BFS-ish sweep both directions.
            for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    if (land[i] == 0f) continue;
                    float d = land[i];
                    if (px > 0) d = Mathf.Min(d, land[i - 1] + 1f);
                    if (py > 0) d = Mathf.Min(d, land[i - pw] + 1f);
                    land[i] = d;
                }
            for (int py = ph - 1; py >= 0; py--)
                for (int px = pw - 1; px >= 0; px--)
                {
                    int i = py * pw + px;
                    if (land[i] == 0f) continue;
                    float d = land[i];
                    if (px < pw - 1) d = Mathf.Min(d, land[i + 1] + 1f);
                    if (py < ph - 1) d = Mathf.Min(d, land[i + pw] + 1f);
                    land[i] = d;
                }

            for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    byte k = j.kind[i];
                    if (k == Water || k == Deck || k == Cliff) continue;
                    float ld = land[i];
                    if (ld > 8.5f) continue;                 // beyond the ~8px coping band
                    int depth = Mathf.RoundToInt(ld);        // 1 = touching water … up to 8 = inner edge

                    // Which way is the water? Used to place the dark front face and to orient the blocks.
                    bool waterN = NearWater(j, px, py, 0, 1, 8);
                    bool waterS = NearWater(j, px, py, 0, -1, 8);
                    bool waterE = NearWater(j, px, py, 1, 0, 8);
                    bool waterW = NearWater(j, px, py, -1, 0, 8);
                    bool horizShore = waterN || waterS;      // shore runs east-west (water above/below)
                    bool vertShore = waterE || waterW;
                    bool corner = horizShore && vertShore;

                    // Blocks 12-16px long run ALONG the shore; joints across them.
                    int alongAxis = horizShore && !corner ? px : vertShore && !corner ? py : px + py;
                    int blkLen = 12 + (IHashB(alongAxis / 14, 1, j.seed + 251) % 5); // 12..16
                    int block = alongAxis / blkLen;
                    int within = alongAxis - block * blkLen;
                    bool joint = within == 0 || within == blkLen - 1;

                    int tone;
                    if (depth <= 1) tone = 0;                // dark FRONT FACE dropping into the water
                    else if (depth == 2) tone = joint ? 1 : 4; // top of the front lip, brightest (top-lit)
                    else if (depth >= 7) tone = 1;           // inner edge fading to ground
                    else tone = joint ? 1 : 2 + (IHashB(block, 0, j.seed + 250) % 3); // block top face

                    // Corners: a clean mitred block - diagonal joint, still lit on top.
                    if (corner)
                    {
                        int diagJoint = ((px + py) % 13);
                        if (diagJoint == 0) tone = 1;
                        if (depth <= 1) tone = 0;
                    }

                    Color32 c = CopingC[Mathf.Clamp(tone, 0, 4)];
                    if (depth <= 1) c = PixelCanvas.Shade(c, 0.72f);        // deepen the vertical front face
                    // Fade the innermost row softly into the ground so the band is not a hard rectangle.
                    if (depth >= 7) c = Lerp(c, j.px[i], 0.5f);
                    j.px[i] = c;
                }
        }
    }
}
