using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Stone stairs for the painted 32px maps (canyon, winter village). A stairwell is a rectangle of
    /// stair cells climbing north, with a cut-stone cheek wall on each side. Every 32px tile holds two
    /// steps: a 5px riser (the front face, turned away from the north-west sun, with its darkest line
    /// right under the lit nosing) and an 11px tread (lit top, bright front edge, its back in the shade
    /// of the next riser up). Each step is laid from one to three large slabs whose joints never line up
    /// with the step above or below, and each slab carries its own tone, rounded front corners and the
    /// odd chip or hairline crack, so a flight reads as masonry instead of stripes.
    /// <para>The painter only decides the shade (an index into an 8-tone stone ramp, 0 = deepest shadow,
    /// 7 = nosing highlight) and where a pixel sits; each map applies its own colours and dressing
    /// (sand and moss in the canyon, snow in the winter village).</para>
    /// </summary>
    internal static class StairPainter
    {
        /// <summary>One step every 16px: two per 32px tile.</summary>
        public const int StepPx = 16;
        /// <summary>Rows 0..4 of a step are its riser; row 5 is the nosing; rows 6..15 the rest of the tread.</summary>
        public const int RiserPx = 5;
        /// <summary>Width of the cheek wall on each side of the flight.</summary>
        public const int WallPx = 8;
        /// <summary>Rows at the foot of the flight where the cheek walls show their south end face.</summary>
        public const int FaceRows = 6;

        public enum Part : byte { Tread, Riser, Wall, WallFace }

        /// <summary>Pixel extent of one stairwell, cheek walls included (inclusive, y up = north).</summary>
        public struct Well
        {
            public int x0, x1, y0, y1;
        }

        public struct Sample
        {
            public Part part;
            /// <summary>Shade: 0 deepest shadow .. 7 nosing highlight.</summary>
            public int tone;
            /// <summary>Step index from the bottom of the flight.</summary>
            public int step;
            /// <summary>Row inside the step: 0..4 riser (0 = its foot), 5 = nosing, 15 = back of the tread.</summary>
            public int cyc;
            /// <summary>Treads / risers: pixels from the left / right cheek wall (0 = touching it).</summary>
            public int fromL, fromR;
            /// <summary>Walls: 0 = outer edge .. WallPx - 1 = inner edge above the steps.</summary>
            public int wallD;
            public bool leftWall;
            /// <summary>0 beside the walls .. 1 in the middle of the flight, where feet wear the stone.</summary>
            public float path;
            /// <summary>The uppermost step: a landing whose back meets the upper paving.</summary>
            public bool top;
            /// <summary>The flat back half of a mid-flight landing (no riser in front of this row).</summary>
            public bool landing;
            /// <summary>A gap between two slabs.</summary>
            public bool joint;
            /// <summary>Slab (or coping stone) index within its step / wall.</summary>
            public int slab;
            /// <summary>Random 0..1 per pixel and per slab, for the map's dressing.</summary>
            public float rnd, slabRnd;
            /// <summary>Colour multiplier of this pixel's slab (about 0.93..1.07; 1 on edges and highlights).</summary>
            public float shade;
        }

        static float Hash01(int x, int y, int s) => TownTerrain.Hash01(x, y, s);
        static float Noise(float x, float y, int s) => TownTerrain.Noise(x, y, s);

        public static Sample At(in Well w, int px, int py, int seed)
        {
            var s = new Sample();
            int lx = px - w.x0, ly = py - w.y0;
            int width = w.x1 - w.x0 + 1, height = w.y1 - w.y0 + 1;
            // 16px rows from the bottom. A long flight (10+ rows) gets a landing halfway up: one extra
            // row of flat paving with no riser in front of it, so the climb pauses instead of repeating.
            int units = Mathf.Max(1, height / StepPx);
            int landing = units >= 10 ? units / 2 : -1;
            s.step = Mathf.Min(ly / StepPx, units - 1);
            s.cyc = ly - s.step * StepPx;
            s.top = s.step == units - 1;
            s.landing = s.step == landing;
            bool openBack = s.top || s.step == landing - 1;   // no riser rises behind this tread
            s.rnd = Hash01(px, py, seed + 1);
            // The foot of the flight sits deepest in its cutting and gets a little less light than the top.
            float depthShade = Mathf.Lerp(0.94f, 1.03f, ly / (float)Mathf.Max(1, height - 1));
            s.shade = depthShade;

            // ----- Cheek walls -----
            int dl = lx, dr = width - 1 - lx;
            if (dl < WallPx || dr < WallPx)
            {
                s.leftWall = dl < WallPx;
                s.wallD = s.leftWall ? dl : dr;
                if (ly < FaceRows)
                {
                    s.part = Part.WallFace;
                    s.tone = WallFaceTone(s.wallD, ly);
                }
                else
                {
                    s.part = Part.Wall;
                    s.tone = WallTone(ref s, ly, height, seed);
                }
                return s;
            }

            int tx0 = w.x0 + WallPx, tx1 = w.x1 - WallPx, tw = tx1 - tx0 + 1;
            s.fromL = px - tx0;
            s.fromR = tx1 - px;
            s.path = Mathf.Clamp01(1f - Mathf.Abs(px - (tx0 + tx1) * 0.5f) / (tw * 0.5f));

            // ----- Which slab of this step, and how far to its edges -----
            int n = Joints(s.step, tx0, tw, seed, out int ja, out int jb);
            int edgeL = tx0 - 1, edgeR = tx1 + 1;
            if (n >= 1)
            {
                if (px == ja) s.joint = true;
                else if (px > ja) { edgeL = ja; s.slab = 1; }
                else edgeR = ja;
            }
            if (n >= 2 && !s.joint)
            {
                if (px == jb) s.joint = true;
                else if (px > jb) { edgeL = jb; s.slab = 2; }
                else if (px > ja) edgeR = jb;
            }
            int dL = px - edgeL, dR = edgeR - px; // 1 = the slab's outermost pixel on that side
            s.slabRnd = Hash01(s.step * 5 + s.slab, 17, seed);

            int tone;
            float slabShade = depthShade * (1f + (s.slabRnd - 0.5f) * 0.14f);
            if (s.landing)
            {
                s.part = Part.Tread;
                tone = LandingTone(in s, px, py, dL, dR, edgeL, edgeR, seed);
                if (tone >= 4 && tone <= 6 && s.cyc > 0 && (s.top || s.cyc < StepPx - 2)) s.shade = slabShade;
            }
            else if (s.cyc < RiserPx)
            {
                s.part = Part.Riser;
                tone = RiserTone(in s, dL);
                if (tone >= 1 && tone <= 3 && !s.joint) s.shade = slabShade;
            }
            else
            {
                s.part = Part.Tread;
                tone = TreadTone(in s, px, py, dL, dR, edgeL, edgeR, seed, openBack);
                if (tone >= 4 && tone <= 6 && s.cyc > RiserPx + 1 && (openBack || s.cyc < StepPx - 2)) s.shade = slabShade;
            }
            // The left wall throws a short shadow onto the steps beside it (sun in the north-west);
            // the right wall only darkens its own corner.
            if (s.fromL < 3 || s.fromR < 2) tone--;
            s.tone = Mathf.Clamp(tone, 0, 7);
            return s;
        }

        // ---------- Slab joints ----------

        /// <summary>
        /// Joint x positions of one step (count 0..2, ja &lt; jb). Even steps keep their random joints;
        /// odd steps move theirs away from both even neighbours, so no joint continues into the next step.
        /// </summary>
        static int Joints(int step, int tx0, int tw, int seed, out int ja, out int jb)
        {
            int n = RawJoints(step, tx0, tw, seed, out ja, out jb);
            if ((step & 1) == 0 || n == 0) return n;
            int pn = RawJoints(step - 1, tx0, tw, seed, out int pa, out int pb);
            int qn = RawJoints(step + 1, tx0, tw, seed, out int qa, out int qb);
            ja = Dodge(ja, tx0, tw, pn, pa, pb, qn, qa, qb, n == 2 ? jb : int.MinValue);
            if (n == 2)
            {
                jb = Dodge(jb, tx0, tw, pn, pa, pb, qn, qa, qb, ja);
                if (jb < ja) { int t = ja; ja = jb; jb = t; }
            }
            return n;
        }

        static int RawJoints(int step, int tx0, int tw, int seed, out int ja, out int jb)
        {
            ja = jb = int.MinValue;
            if (step < 0 || tw < 36) return 0;               // a narrow flight: one slab per step
            if (tw >= 88 && Hash01(step, 7, seed) >= 0.45f)   // wide flights: 2 or 3 slabs
            {
                ja = tx0 + Mathf.RoundToInt((0.33f + (Hash01(step, 11, seed) - 0.5f) * 0.16f) * tw);
                jb = tx0 + Mathf.RoundToInt((0.67f + (Hash01(step, 13, seed) - 0.5f) * 0.16f) * tw);
                return 2;
            }
            ja = tx0 + Mathf.RoundToInt((0.5f + (Hash01(step, 11, seed) - 0.5f) * 0.44f) * tw);
            return 1;
        }

        static readonly int[] DodgeShifts = { 0, 11, -11, 17, -17, 23, -23 };

        /// <summary>Moves joint x at least 9px away from the neighbouring steps' joints (and 10px from the walls / its sibling).</summary>
        static int Dodge(int x, int tx0, int tw, int pn, int pa, int pb, int qn, int qa, int qb, int sibling)
        {
            foreach (int shift in DodgeShifts)
            {
                int c = x + shift;
                if (c < tx0 + 10 || c > tx0 + tw - 11) continue;
                if (sibling != int.MinValue && Mathf.Abs(c - sibling) < 12) continue;
                if (Near(c, pn, pa, pb) || Near(c, qn, qa, qb)) continue;
                return c;
            }
            return x;
        }

        static bool Near(int x, int n, int a, int b) => (n >= 1 && Mathf.Abs(x - a) < 9) || (n >= 2 && Mathf.Abs(x - b) < 9);

        // ---------- Steps ----------

        static int RiserTone(in Sample s, int dL)
        {
            // A vertical face turned away from the sun: darkest right under the overhanging nosing,
            // lifting towards its foot where the lit step below bounces a little light back.
            if (s.cyc == RiserPx - 1) return 0;
            if (s.joint) return 0;
            int tone = s.cyc == RiserPx - 2 ? 1 : s.cyc == 0 ? 1 : 2;
            if (dL == 1 && s.cyc > 0) tone++;                 // the block's lit west edge
            return tone;
        }

        static int TreadTone(in Sample s, int px, int py, int dL, int dR, int edgeL, int edgeR, int seed, bool openBack)
        {
            int c = s.cyc;
            if (s.joint) return c == RiserPx ? 3 : 2;         // gap between slabs (a small notch at the nosing)

            bool front = c <= RiserPx + 1;
            bool back = !openBack && c >= StepPx - 2;
            int tone;
            if (c == RiserPx) tone = s.path > 0.55f ? 6 : 7;  // nosing; the worn middle is rounded off, less glint
            else if (c == RiserPx + 1) tone = 6;
            else if (back) tone = 4;                          // soft shade at the foot of the next riser up
            else tone = SlabBody(in s, px, py, dL, dR, edgeL, edgeR, seed);
            if (front)
            {
                if (dL == 1 || dR == 1) tone = 5;             // rounded front corners of each slab
                if (Chip(s.step, px, seed)) tone = c == RiserPx ? 3 : 5;
            }
            if (s.top && c == StepPx - 1) tone = 3;           // landing meets the upper paving
            return tone;
        }

        /// <summary>The flat row of paving that makes up the back half of a landing (no riser, no nosing).</summary>
        static int LandingTone(in Sample s, int px, int py, int dL, int dR, int edgeL, int edgeR, int seed)
        {
            int c = s.cyc;
            if (c == 0) return 3;                             // joint between the two rows of landing slabs
            if (s.joint) return 2;
            if (!s.top && c >= StepPx - 2) return 4;          // shade at the foot of the next riser up
            if (s.top && c == StepPx - 1) return 3;
            return c == 1 ? 6 : SlabBody(in s, px, py, dL, dR, edgeL, edgeR, seed);   // lit north edge of the slab row
        }

        /// <summary>Body of a slab: bevelled edges, soft mottling, a few pits and the odd hairline crack.</summary>
        static int SlabBody(in Sample s, int px, int py, int dL, int dR, int edgeL, int edgeR, int seed)
        {
            int tone = 5;
            if (dL == 1) tone++;
            else if (dR == 1) tone--;
            float m = Noise(px / 4.5f, py / 4.5f, seed + 5);
            if (m > 0.6f) tone++;
            else if (m < -0.6f) tone--;
            if (s.rnd > 0.965f) tone--;
            tone = Mathf.Clamp(tone, 4, 6);
            if (Crack(in s, px, edgeL, edgeR, seed)) tone = 2;
            return tone;
        }

        /// <summary>About one slab in four carries a hairline crack from the back towards the front.</summary>
        static bool Crack(in Sample s, int px, int edgeL, int edgeR, int seed)
        {
            int span = edgeR - edgeL - 1;
            if (span < 14) return false;
            int id = s.step * 7 + s.slab;
            if (Hash01(id, 29, seed) > 0.26f) return false;
            int c0 = StepPx - 4;                                          // starts at the back of the tread
            int c1 = RiserPx + 2 + (int)(Hash01(id, 41, seed) * 3f);      // ends 7..9 rows up from the step foot
            if (s.cyc > c0 || s.cyc < c1) return false;
            float start = edgeL + 5 + Hash01(id, 31, seed) * (span - 10);
            float slope = (Hash01(id, 37, seed) - 0.5f) * 1.6f;           // -0.8 .. 0.8 px per row
            return px == Mathf.RoundToInt(start + (c0 - s.cyc) * slope);
        }

        /// <summary>A few chipped bits along the nosing.</summary>
        static bool Chip(int step, int px, int seed)
        {
            int cell = Mathf.FloorToInt(px / 19f);
            if (Hash01(cell, step, seed + 43) > 0.3f) return false;
            int start = cell * 19 + 3 + (int)(Hash01(cell, step, seed + 47) * 12f);
            int len = Hash01(cell, step, seed + 53) > 0.6f ? 3 : 2;
            return px >= start && px < start + len;
        }

        // ---------- Cheek walls ----------

        /// <summary>Coping stones of irregular length laid along the top of the wall.</summary>
        static int WallTone(ref Sample s, int ly, int height, int seed)
        {
            int d = s.wallD;
            if (d == 0) return 2;                                         // outer edge against the rock / paving
            if (d == WallPx - 1) return s.leftWall ? 0 : 1;               // inner drop to the steps (left in shade)
            int salt = s.leftWall ? 3 : 5;
            int y = FaceRows, k = 0, sy0 = FaceRows, sy1 = height - 1;
            while (y < height)
            {
                int len = 11 + (int)(Hash01(k, salt, seed + 59) * 13f);  // 11..23 px long
                if (ly < y + len) { sy0 = y; sy1 = Mathf.Min(height - 1, y + len - 1); break; }
                y += len;
                k++;
            }
            s.slab = k;
            s.slabRnd = Hash01(k, salt + 4, seed + 61);
            if (ly == sy0 && k > 0) return 2;                             // joint between two stones
            int tone = 5;
            if (s.slabRnd < 0.3f) tone--;
            if (ly == sy1) tone++;                                        // north edge catches the light
            else if (ly == sy0 + 1 || (k == 0 && ly == sy0)) tone--;      // south edge
            if (d == 1) tone += 2;                                        // lit outer bevel
            else if (d == WallPx - 2) tone -= 2;                          // inner bevel in shade
            if (s.rnd > 0.96f) tone--;
            return Mathf.Clamp(tone, 2, 7);
        }

        /// <summary>The south end face of a cheek wall at the foot of the flight.</summary>
        static int WallFaceTone(int d, int ly)
        {
            if (ly == FaceRows - 1) return d == 0 ? 3 : 6;               // lit top edge of the wall's end
            if (ly == 0) return 0;                                        // foot
            if (d == 0) return 1;
            return ly >= 3 ? 2 : 1;
        }
    }
}
