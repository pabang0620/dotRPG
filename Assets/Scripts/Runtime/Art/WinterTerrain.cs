using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Paints the ground of the 32px winter village as one continuous picture at 32 pixels per tile,
    /// the same way <see cref="TownTerrain"/> paints the village and forest. Terrain borders come from
    /// blending the text map's cells and wobbling the result with noise, so the snow field, the cobble
    /// plaza and the river shore are organic curves with no square tile edges; textures (warm-white snow
    /// with soft teal drifts, round snow-dusted cobbles, a clear blue river with depth and foam, packed
    /// stone stairs, snowy plank decks) are sampled at each pixel's world position, so nothing repeats
    /// per tile.
    ///
    /// Cell codes: '.' snow  ',' cobble path  'W' cliff (solid, drawn by the object layer's colliders but
    /// its snowy foot is painted here)  'L' stone stairs  '~' river  'V' waterfall  'd' wooden bridge/deck.
    /// The river's banks are found by the same smooth membership blend as every other border, so the
    /// S-bends the map draws as one-tile steps read as continuous curves. The result is laid out bottom-up
    /// (Unity texture order).
    /// </summary>
    public static class WinterTerrain
    {
        public const int Px = 32;

        const byte Snow = 0, Cobble = 1, Cliff = 2, Stairs = 3, Water = 4, Fall = 5, Deck = 6, KindCount = 7;

        // ---------- Palette ----------

        static readonly Color32[] SnowC =
        {
            H("#c7dbe4"), // deepest teal shadow
            H("#d8e8ec"), // teal shadow
            H("#e8f2f4"), // light sky
            H("#f2f6f4"), // warm white
            H("#fbfcfa"), // brightest crest
        };
        static readonly Color32 SnowSpark = H("#ffffff");

        static readonly Color32[] CobbleC = { H("#8f93a4"), H("#a3a7b6"), H("#b7bbc8"), H("#cbcedb"), H("#dde0ea") };
        static readonly Color32 CobbleGap = H("#e2e9ef");
        static readonly Color32 CobbleGapSnow = H("#f2f6f6");

        static readonly Color32[] WaterC =
        {
            H("#2c6ba8"), // deep
            H("#3a83c6"), // mid-deep
            H("#4f9fdc"), // mid (the river base)
            H("#69b6ea"), // shallow
            H("#8fcdf3"), // rim
            H("#c2e6f8"), // near-shore
        };
        static readonly Color32 Foam = H("#e6f6fd");
        static readonly Color32 IceSheet = H("#bfe6f2");

        // Snowy banks: a lit lip, a grey-violet face where the water undercuts the north/west edge.
        static readonly Color32 BankFace = H("#7a6e88");
        static readonly Color32 BankFaceD = H("#5e536b");

        static readonly Color32 CliffP = H("#9a8ea6");   // grey-violet rock
        static readonly Color32 CliffPL = H("#b2a7bd");
        static readonly Color32 CliffPD = H("#7a6e88");
        static readonly Color32 CliffPDD = H("#5e536b");
        static readonly Color32 CliffBe = H("#d6c5aa");  // beige strata
        static readonly Color32 CliffBeD = H("#b6a48b");

        // Stairs: grey-violet granite, 8 tones for StairPainter (deepest shadow → nosing highlight).
        static readonly Color32[] StairRamp = { H("#3a3844"), H("#4c4a5a"), H("#605d70"), H("#767388"), H("#8a8799"), H("#9e9bad"), H("#b7b4c4"), H("#d2d0dc") };
        static readonly Color32 StairIce = H("#cfe9f5");
        static readonly Color32 StairSnowDeep = H("#b3c6d4");

        static readonly Color32[] Plank = { H("#6e4428"), H("#8e5a35"), H("#a86d42"), H("#c28551"), H("#d69c66") };
        static readonly Color32 PlankRail = H("#7a4e34");
        static readonly Color32 PlankPost = H("#583726");

        static Color32 H(string hex) => PixelCanvas.Hex(hex);

        // ---------- Noise (shared with TownTerrain) ----------

        static float Hash01(int x, int y, int s) => TownTerrain.Hash01(x, y, s);
        static float Noise(float x, float y, int s) => TownTerrain.Noise(x, y, s);
        static float Fbm(float x, float y, int s) => TownTerrain.Fbm(x, y, s);

        static int Mod(int a, int m) => (a % m + m) % m;

        // ---------- State of one paint job ----------

        sealed class Job
        {
            public int w, h, pw, ph;
            public byte[] cells;      // per cell kind
            public byte[] kind;       // per pixel kind
            public byte[] other;      // per pixel second kind
            public float[] margin;    // per pixel best - second membership
            public float[] cliffFrac; // per pixel: 0 at the rock top, 1 at its foot (only meaningful on Cliff)
            public float[] bankL, bankR;   // per pixel-row: smoothed left / right river-bank x (-1 = no water in row)
            public int[] deckTop, deckBot; // per pixel-column: outer top / bottom y of the contiguous deck run (-1 = none)
            public Color32[] px;
            public int seed;

            public byte Cell(int x, int y)
            {
                x = Mathf.Clamp(x, 0, w - 1);
                y = Mathf.Clamp(y, 0, h - 1);
                return cells[y * w + x];
            }

            public byte KindAt(int x, int y)
            {
                if (x < 0 || y < 0 || x >= pw || y >= ph) return 255;
                return kind[y * pw + x];
            }
        }

        static byte KindOf(char c)
        {
            switch (c)
            {
                case ',': return Cobble;
                case 'W': return Cliff;
                case 'L': return Stairs;
                case '~': return Water;
                case 'V': return Fall;
                case 'd': return Deck;
                default: return Snow;
            }
        }

        /// <summary>Paints the winter map. <paramref name="ground"/>[x, y] is the ground code of each cell (y = 0 at the bottom).</summary>
        public static Color32[] Paint(char[,] ground, int w, int h, out int pw, out int ph)
        {
            var j = new Job { w = w, h = h, pw = w * Px, ph = h * Px, seed = 91 };
            pw = j.pw;
            ph = j.ph;
            j.cells = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    j.cells[y * w + x] = KindOf(ground[x, y]);
            int n = j.pw * j.ph;
            j.kind = new byte[n];
            j.other = new byte[n];
            j.margin = new float[n];
            j.cliffFrac = new float[n];
            j.px = new Color32[n];

            Classify(j);
            ShapeRiver(j);
            MeasureCliffs(j);
            MeasureDecks(j);
            PaintBase(j);
            PaintWater(j);
            PaintCliffFoot(j);
            Stamps(j);
            return j.px;
        }

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

        // ---------- 1. Which terrain is at each pixel ----------
        static void Membership(Job j, float u, float v, float[] weights, out byte best, out byte second, out float margin)
        {
            for (int k = 0; k < KindCount; k++) weights[k] = 0f;
            float gu = u - 0.5f, gv = v - 0.5f;
            int i0 = Mathf.FloorToInt(gu), j0 = Mathf.FloorToInt(gv);
            float fx = gu - i0, fy = gv - j0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            weights[j.Cell(i0, j0)] += (1f - fx) * (1f - fy);
            weights[j.Cell(i0 + 1, j0)] += fx * (1f - fy);
            weights[j.Cell(i0, j0 + 1)] += (1f - fx) * fy;
            weights[j.Cell(i0 + 1, j0 + 1)] += fx * fy;
            best = 0; second = 0;
            float b = -1f, s = -1f;
            for (byte k = 0; k < KindCount; k++)
            {
                float wk = weights[k];
                if (wk > b) { s = b; second = best; b = wk; best = k; }
                else if (wk > s) { s = wk; second = k; }
            }
            margin = b - Mathf.Max(0f, s);
        }

        static void Classify(Job j)
        {
            System.Threading.Tasks.Parallel.For(0, j.ph, py =>
            {
                var weights = new float[KindCount];
                for (int px = 0; px < j.pw; px++)
                {
                    float u = (px + 0.5f) / Px, v = (py + 0.5f) / Px;
                    int cx = px / Px, cy = py / Px;
                    byte own = j.cells[cy * j.w + cx];
                    // Wobble borders so they never follow the tile grid; the river shore gets a longer,
                    // gentler wobble so its S-bends read as smooth curves.
                    const float amp = 0.2f;
                    float wu = u + amp * Noise(u * 0.85f, v * 0.85f, j.seed + 3) + 0.06f * Noise(u * 3.1f, v * 3.1f, j.seed + 5);
                    float wv = v + amp * Noise(u * 0.85f + 31.7f, v * 0.85f - 12.3f, j.seed + 4) + 0.06f * Noise(u * 3.1f + 9f, v * 3.1f, j.seed + 6);
                    Membership(j, wu, wv, weights, out byte best, out byte second, out float margin);
                    // Decks, stairs and waterfalls keep their own crisp cells (they are man-made / vertical).
                    if (own == Deck) { best = Deck; second = Water; margin = 1f; }
                    else if (best == Deck) { best = second == Deck ? Water : second; margin = 0.05f; }
                    if (own == Stairs) { best = Stairs; second = Cobble; margin = 1f; }
                    else if (best == Stairs && own != Stairs) { best = second == Stairs ? Cobble : second; margin = 0.05f; }
                    if (own == Fall) { best = Fall; second = Water; margin = 1f; }
                    else if (best == Fall && own != Fall) { best = second == Fall ? Water : second; margin = 0.05f; }
                    // A cliff cell is solid rock; keep it crisp so the snowy foot lands exactly at its edge.
                    if (own == Cliff) { best = Cliff; margin = 1f; }
                    else if (best == Cliff && own != Cliff) { best = second == Cliff ? own : second; margin = 0.1f; }
                    int i = py * j.pw + px;
                    j.kind[i] = best;
                    j.other[i] = second;
                    j.margin[i] = margin;
                }
            });
        }

        // ---------- 2. Base textures ----------

        static void PaintBase(Job j)
        {
            System.Threading.Tasks.Parallel.For(0, j.ph, py =>
            {
                for (int px = 0; px < j.pw; px++)
                {
                    int i = py * j.pw + px;
                    byte k = j.kind[i];
                    Color32 c;
                    switch (k)
                    {
                        case Cobble: c = CobbleAt(j, px, py, i); break;
                        case Cliff: c = CliffAt(j, px, py, i); break;
                        case Stairs: c = StairsAt(j, px, py); break;
                        case Deck: c = DeckAt(j, px, py); break;
                        case Water: c = WaterC[2]; break;
                        case Fall: c = WaterC[3]; break;
                        default: c = SnowAt(j, px, py, i); break;
                    }
                    j.px[i] = c;
                }
            });
        }

        static Color32 SnowAt(Job j, int px, int py, int i)
        {
            // Warm white with soft, wide, low-contrast drifts and a faint teal in the hollows.
            float n = Fbm(px / 40f, py / 40f, j.seed + 20);
            int tone = n < -0.34f ? 2 : n > 0.42f ? 4 : 3;
            float m = Noise(px / 7f, py / 7f, j.seed + 21);
            if (m > 0.9f) tone = Mathf.Min(4, tone + 1);
            else if (m < -0.9f) tone = Mathf.Max(1, tone - 1);
            byte other = j.other[i];
            float margin = j.margin[i];
            // Snow sits a touch lower right beside a path, deck or the water's lip: a thin teal shade line.
            if ((other == Cobble || other == Deck || other == Water) && margin < 0.09f) tone = Mathf.Max(1, tone - 1);
            // Shade cast by the cliff foot (handled again in PaintCliffFoot, but tint the approach here).
            if (other == Cliff && margin < 0.3f) tone = Mathf.Max(1, tone - (margin < 0.15f ? 2 : 1));
            return SnowC[tone];
        }

        // Round snow-dusted cobbles: a jittered grid of stone centres; each pixel belongs to the nearest.
        const int StoneCell = 13;

        static void NearestStone(int px, int py, int seed, out float d1, out float d2, out int sx, out int sy, out float cxOut, out float cyOut)
        {
            int gx = Mathf.FloorToInt((float)px / StoneCell), gy = Mathf.FloorToInt((float)py / StoneCell);
            d1 = d2 = float.MaxValue;
            sx = sy = 0;
            cxOut = cyOut = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = gx + dx, iy = gy + dy;
                    float fx = ix * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed) - 0.5f) * 5f + ((iy & 1) != 0 ? StoneCell * 0.5f : 0f);
                    float fy = iy * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed + 1) - 0.5f) * 4.5f;
                    float ex = px + 0.5f - fx, ey = py + 0.5f - fy;
                    float d = ex * ex + ey * ey;
                    if (d < d1) { d2 = d1; d1 = d; sx = ix; sy = iy; cxOut = fx; cyOut = fy; }
                    else if (d < d2) d2 = d;
                }
            d1 = Mathf.Sqrt(d1);
            d2 = Mathf.Sqrt(d2);
        }

        static Color32 CobbleAt(Job j, int px, int py, int i)
        {
            NearestStone(px, py, j.seed + 50, out float d1, out float d2, out int sx, out int sy, out float cx, out float cy);
            float radius = StoneCell * (0.5f + Hash01(sx, sy, j.seed + 52) * 0.12f);
            // Snow buried between the stones, whiter where snow lies next to the path.
            bool edge = j.margin[i] < 0.34f && j.other[i] == Snow;
            if (d2 - d1 < 1.4f || d1 > radius)
                return edge ? CobbleGapSnow : Hash01(px / 3, py / 3, j.seed + 58) < 0.35f ? CobbleGapSnow : CobbleGap;
            // A stone whose centre falls outside the path shows only its snowy shoulder.
            if (j.KindAt(Mathf.Clamp(Mathf.RoundToInt(cx), 0, j.pw - 1), Mathf.Clamp(Mathf.RoundToInt(cy), 0, j.ph - 1)) != Cobble)
                return CobbleGapSnow;
            float roll = Hash01(sx, sy, j.seed + 55);
            int baseTone = roll < 0.16f ? 1 : roll < 0.64f ? 2 : 3;
            float ox = (px + 0.5f - cx) / radius, oy = (py + 0.5f - cy) / radius;
            float lit = -ox * 0.55f + oy * 0.83f;   // light from the top-left (y is up here)
            int tone = baseTone;
            if (lit > 0.42f) tone = Mathf.Min(4, baseTone + 1);
            else if (lit < -0.45f) tone = Mathf.Max(0, baseTone - 1);
            if (d1 > radius - 1.3f && lit < 0.1f) tone = Mathf.Max(0, tone - 1);
            // A little snow cap on the up-light side of some stones.
            if (Hash01(sx, sy, j.seed + 56) < 0.3f && oy < -0.35f) return SnowC[4];
            return CobbleC[tone];
        }

        static Color32 CliffAt(Job j, int px, int py, int i)
        {
            // A low grey-violet rock face in 3/4 view, lit from the top-left, built from stacked rounded
            // ledge/block volumes: each block is lighter on its top-left shoulder and darker along its lower
            // right, so the wall has depth instead of reading flat. Thin muted beige strata drift across it.
            float frac = j.cliffFrac[i];                       // 0 at the top edge, 1 at the foot
            // Overall body: lighter under the snow lip, markedly darker toward the shaded foot.
            float shade = 1.08f - frac * 0.34f;
            float grain = Noise(px / 7f, py / 9f, j.seed + 70);
            var baseC = grain > 0.35f ? CliffPL : grain < -0.4f ? CliffPD : CliffP;
            var c = PixelCanvas.Shade(baseC, shade);

            // Irregular layered rock. Rows are horizontal strata whose heights wobble and drift across the
            // face (uneven, never a ruled grid); within each stratum the rock is broken into slabs of varied
            // width by staggered vertical joints, and the odd wider boulder spans two rows.
            // 1) Which stratum (layer) this pixel is in — cumulative wobbling row heights.
            float rowWob = 2.5f * Noise(px / 34f, py / 40f, j.seed + 85);
            float layerH = 13f + 5f * Hash01(0, Mathf.FloorToInt(py / 15f), j.seed + 92);   // rows 13..18 px tall, varying
            float layerF = (py + rowWob) / layerH + 0.5f * Noise(px / 40f, 0f, j.seed + 86);
            int layer = Mathf.FloorToInt(layerF);
            float inLayer = layerF - layer;                    // 0 at the layer top .. 1 at its foot
            // 2) Slabs across this layer: cell width varies per layer, joints staggered per layer.
            float slabW = 16f + 10f * Hash01(0, layer, j.seed + 87);         // 16..26 px, different per layer
            float phase = Hash01(1, layer, j.seed + 88) * slabW;            // horizontal stagger per layer
            float jointWob = 3f * Noise(py / 6f, layer * 3f, j.seed + 89);
            float slabF = (px + phase + jointWob) / slabW;
            int slab = Mathf.FloorToInt(slabF);
            float inSlab = slabF - slab;                       // 0 at the slab left .. 1 at its right
            // 3) An occasional bigger boulder: merge some slabs, sit a touch prouder (lighter).
            bool boulder = Hash01(slab, layer, j.seed + 90) > 0.82f;

            // Rounded lit/shaded edges of each slab (top-left catches light, bottom-right in shadow).
            if (inLayer < 0.14f || inSlab < 0.12f) c = PixelCanvas.Shade(CliffPL, shade + (boulder ? 0.08f : 0.04f));
            else if (inLayer > 0.9f || inSlab > 0.9f) c = PixelCanvas.Shade(CliffPDD, shade);      // joint in shadow
            else if (inSlab > 0.74f) c = PixelCanvas.Shade(CliffPD, shade);                         // right shoulder
            else if (boulder) c = PixelCanvas.Shade(CliffPL, shade + 0.03f);

            // Beige strata seams running ALONG the layers (a soft muted band near some layer tops).
            float seamRoll = Hash01(2, layer, j.seed + 91);
            if (seamRoll > 0.6f && inLayer < 0.16f)
                c = PixelCanvas.Shade(WMix(CliffBe, CliffP, 0.4f), shade);
            else if (seamRoll > 0.6f && inLayer < 0.24f)
                c = PixelCanvas.Shade(CliffBeD, shade);

            return c;
        }

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

        // ---------- 3. Water: depth, banks, foam, waterfall, ice ----------

        static void PaintWater(Job j)
        {
            int pw = j.pw, ph = j.ph;
            var dist = new float[pw * ph];
            const float Far = 999f;
            for (int i = 0; i < dist.Length; i++)
                dist[i] = j.kind[i] == Water || j.kind[i] == Fall ? Far : 0f;
            // Two-pass chamfer distance to the nearest shore.
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

            System.Threading.Tasks.Parallel.For(0, ph, py =>
            {
                float bl = j.bankL != null ? j.bankL[py] : -1f;
                float br = j.bankR != null ? j.bankR[py] : -1f;
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    byte k = j.kind[i];
                    if (k != Water && k != Fall) continue;
                    // What lies directly north (above, screen-up) within 9 px: a bank face, deck, or cliff.
                    int up = 0;
                    byte upKind = Water;
                    for (int t = 1; t <= 9; t++)
                    {
                        byte kk = j.KindAt(px, py + t);
                        if (kk == 255) break;
                        if (kk != Water && kk != Fall) { up = t; upKind = kk; break; }
                    }
                    // Horizontal distance to the nearest west/east bank curve (screen-left / right shore).
                    float dxWest = bl >= 0f ? (px + 0.5f) - bl : 999f;   // >0 inside, measured from left bank
                    float dxEast = br >= 0f ? br - (px + 0.5f) : 999f;   // >0 inside, measured from right bank
                    Color32 c;
                    float d = dist[i];
                    if (k == Fall)
                    {
                        c = WaterfallAt(j, px, py, up);
                    }
                    else if (up > 0 && upKind == Deck)
                    {
                        c = up <= 3 ? Plank[up == 1 ? 1 : 0] : up <= 7 ? Darken(WaterC[1], 0.82f) : WaterC[2];
                    }
                    else if (up > 0 && upKind == Cliff && up <= 6)
                    {
                        // Splash / churn at the cliff foot where the fall lands.
                        c = up <= 2 ? Foam : up <= 4 ? WaterC[4] : WaterC[3];
                        if (Noise(px / 3f, py / 3f + j.seed, j.seed + 82) > 0.5f && up <= 3) c = Foam;
                    }
                    else if (up > 0 && up <= 4 && (upKind != Snow && upKind != Cobble))
                    {
                        // Bank face beneath a non-snow land edge to the north.
                        c = up <= 1 ? BankFace : up <= 2 ? Darken(BankFace, 0.92f) : BankFaceD;
                    }
                    else if (dxWest >= 0f && dxWest < 3.5f)
                    {
                        // West (screen-left) shore: a lit snowy lip then a grey-violet undercut face.
                        c = dxWest < 1.4f ? BankFace : dxWest < 2.6f ? BankFaceD : WaterC[5];
                    }
                    else if (dxEast >= 0f && dxEast < 3.5f)
                    {
                        // East (screen-right) shore: catches less light, a soft shaded rim.
                        c = dxEast < 1.4f ? Darken(WaterC[0], 0.9f) : dxEast < 2.6f ? WaterC[4] : WaterC[5];
                    }
                    else if (up > 0 && up <= 2 && (upKind == Snow || upKind == Cobble))
                    {
                        c = up <= 1 ? WaterC[5] : WaterC[4];
                    }
                    else if (d <= 1.6f) c = Foam;
                    else if (d <= 3.5f) c = WaterC[4];
                    else
                    {
                        float depth = Mathf.Clamp01((d - 3.5f) / 22f) + Noise(px / 16f, py / 11f, j.seed + 81) * 0.16f;
                        c = depth > 0.72f ? WaterC[0] : depth > 0.46f ? WaterC[1] : depth > 0.22f ? WaterC[2] : WaterC[3];
                    }
                    // Low-contrast ripples on open water only, biased into gentle horizontal arcs.
                    if (k == Water && up == 0 && d > 4f && dxWest > 3.5f && dxEast > 3.5f)
                    {
                        float r = Noise(px / 2.6f, py / 6f, j.seed + 90);
                        if (r > 0.82f) c = Lighten(c, 1.12f);
                        else if (r < -0.86f) c = Darken(c, 0.92f);
                    }
                    j.px[i] = c;
                }
            });

            // Splash pool: where the fall lands in the river, a churned foamy zone with a few ripple rings.
            for (int px = 0; px < pw; px++)
            {
                // Find a fall column and the first open water just below its foot.
                for (int py = 1; py < ph; py++)
                {
                    if (j.kind[py * pw + px] != Fall) continue;
                    if (IsFall(j, px, py - 1)) continue;             // only the bottom row of the fall
                    // Paint a splash apron into the water below.
                    for (int t = 1; t <= 26; t++)
                    {
                        int y = py - t; if (y < 0) break;
                        if (j.kind[y * pw + px] != Water) break;
                        float fall = t / 26f;
                        float n = Noise(px / 3f, y / 3f, j.seed + 120);
                        Color32 c;
                        if (t <= 4) c = n > -0.2f ? Foam : WaterC[5];                 // roaring foam at the impact
                        else if (n > 0.72f - fall * 0.4f) c = PixelCanvas.WithAlpha(Foam, 180);
                        else if (n > 0.4f - fall * 0.3f) c = WaterC[5];
                        else continue;
                        j.px[y * pw + px] = c;
                    }
                    break;
                }
            }
            // Ripple rings radiating from the impact point of each fall column.
            for (int px = 0; px < pw; px++)
                for (int py = 1; py < ph; py++)
                {
                    if (j.kind[py * pw + px] != Fall || IsFall(j, px, py - 1)) continue;
                    // Only stamp one set per contiguous fall column (leftmost pixel).
                    if (IsFall(j, px - 1, py)) break;
                    int cxp = px, cyp = py - 6;
                    for (int ring = 1; ring <= 3; ring++)
                    {
                        float r = ring * 6f;
                        for (int a = 0; a < 40; a++)
                        {
                            float ang = a / 40f * Mathf.PI * 2f;
                            int rx = cxp + Mathf.RoundToInt(Mathf.Cos(ang) * r * 1.6f);
                            int ry = cyp + Mathf.RoundToInt(Mathf.Sin(ang) * r * 0.7f);
                            if (rx < 0 || ry < 0 || rx >= pw || ry >= ph) continue;
                            if (j.kind[ry * pw + rx] == Water && ry < py - 3)
                                j.px[ry * pw + rx] = PixelCanvas.WithAlpha(WaterC[5], 150);
                        }
                    }
                    break;
                }

            // Snow lip along every bank edge (a land pixel touching water on any side): the bright, lit
            // rim that makes the smooth shore read. Following j.kind, this traces the curved banks exactly.
            for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    byte k = j.kind[i];
                    if (k == Water || k == Fall || k == Deck) continue;
                    bool touch = IsWet(j, px, py - 1) || IsWet(j, px, py + 1) || IsWet(j, px - 1, py) || IsWet(j, px + 1, py);
                    if (!touch) continue;
                    bool lit = IsWet(j, px, py - 1) || IsWet(j, px - 1, py);   // north/west edges catch the light
                    if (k == Snow || k == Cobble) j.px[i] = lit ? SnowSpark : SnowC[4];
                    else j.px[i] = Lighten(j.px[i], 1.12f);
                }
        }

        static bool IsWet(Job j, int x, int y)
        {
            byte k = j.KindAt(x, y);
            return k == Water || k == Fall;
        }

        static Color32 WaterfallAt(Job j, int px, int py, int up)
        {
            // Falling sheet: vertical white streaks over blue, brightest at the lip. The left/right edges
            // fade into misty spray (so the column has no hard rectangular sides) and the foot churns to foam.
            // Distance to the fall's side edges (in pixels) and to its top/bottom within this column.
            int edgeL = 0; while (edgeL < 12 && IsFall(j, px - edgeL - 1, py)) edgeL++;
            int edgeR = 0; while (edgeR < 12 && IsFall(j, px + edgeR + 1, py)) edgeR++;
            int fromTop = 0; while (fromTop < 40 && IsFall(j, px, py + fromTop + 1)) fromTop++;
            int fromBot = 0; while (fromBot < 40 && IsFall(j, px, py - fromBot - 1)) fromBot++;
            bool atSide = edgeL < 3 || edgeR < 3;

            float streak = Noise(px / 2f, py / 5f - j.seed * 0.1f, j.seed + 100);
            int col = Mod(px, 5);
            Color32 c = WaterC[3];
            if (col == 0 || streak > 0.5f) c = PixelCanvas.WithAlpha(SnowSpark, 235);
            else if (col == 2) c = WaterC[1];
            else if (col == 4 && streak < -0.3f) c = Foam;

            // Bright, foamy lip at the very top of the fall.
            if (fromTop <= 2) c = fromTop == 0 ? Foam : WaterC[5];
            // Churning foam where the sheet lands at the foot.
            if (fromBot <= 3) c = (px + py + j.seed) % 2 == 0 ? Foam : WaterC[5];

            // Misty spray along the side edges: fade toward the neighbouring water / bank.
            if (atSide)
            {
                float mist = Noise(px / 2.5f, py / 3f, j.seed + 101);
                int edge = Mathf.Min(edgeL, edgeR);
                if (edge == 0) c = mist > 0.2f ? PixelCanvas.WithAlpha(Foam, 150) : WaterC[4];
                else if (edge == 1) c = mist > 0f ? PixelCanvas.WithAlpha(SnowSpark, 120) : c;
                else if (mist > 0.5f) c = PixelCanvas.WithAlpha(SnowSpark, 90);
            }
            return c;
        }

        static bool IsFall(Job j, int x, int y) => j.KindAt(x, y) == Fall;

        // ---------- 3b. Cliff top overhang and foot drift: break the straight silhouettes ----------

        static void PaintCliffFoot(Job j)
        {
            int pw = j.pw, ph = j.ph;

            // Foot: a heaped, irregular snow drift along the bottom edge. It bulges DOWN past the cell line
            // (painted onto the snow below) and UP onto the rock, so the base silhouette is wavy, not ruled.
            for (int px = 0; px < pw; px++)
                for (int py = 0; py < ph; py++)
                {
                    int i = py * pw + px;
                    if (j.kind[i] != Cliff) continue;
                    byte below = j.KindAt(px, py - 1);
                    if (below == Cliff || below == 255) continue;
                    if (below == Water || below == Fall) continue;   // river / fall foot handled by the water pass
                    // Drift profile: two octaves of noise so the heap rises and falls along the wall.
                    float prof = 0.6f * Noise(px / 13f, 0f, j.seed + 110) + 0.4f * Noise(px / 5f, 3f, j.seed + 111);
                    int up = 9 + Mathf.RoundToInt(6f * (prof + 1f));      // how far the drift climbs the rock
                    int down = 3 + Mathf.RoundToInt(4f * (prof + 1f));    // how far it spills below the cell line
                    for (int t = 0; t < up; t++)
                    {
                        int y = py + t; if (y >= ph) break;
                        if (j.kind[y * pw + px] != Cliff) break;
                        j.px[y * pw + px] = t >= up - 1 ? SnowC[1] : t >= up - 2 ? SnowC[2] : t < 2 ? SnowSpark : SnowC[3];
                    }
                    for (int t = 1; t <= down; t++)
                    {
                        int y = py - t; if (y < 0) break;
                        byte kk = j.kind[y * pw + px];
                        if (kk != Snow && kk != Cobble) break;
                        j.px[y * pw + px] = t >= down - 1 ? SnowC[2] : t < 2 ? SnowSpark : SnowC[3];   // rounded toe of the drift
                    }
                    // Soft shadow the wall casts just above the drift.
                    int sy = py + up;
                    if (sy < ph && j.kind[sy * pw + px] == Cliff) j.px[sy * pw + px] = PixelCanvas.Shade(CliffPDD, 1.0f);
                }

            // Top: a thick snow overhang with rounded scallops draping over the edge. It bulges UP past the
            // cell line (painted onto the snow plateau above) and DOWN onto the rock, with icicles below.
            for (int px = 0; px < pw; px++)
                for (int py = ph - 1; py >= 0; py--)
                {
                    int i = py * pw + px;
                    if (j.kind[i] != Cliff) continue;
                    byte above = j.KindAt(px, py + 1);
                    if (above == Cliff) continue;
                    if (above == 255 || above == Water || above == Fall) break;
                    // Scalloped thickness: a broad lobe plus a finer wobble, so the lip has rounded bulges.
                    float lobe = Mathf.Sin(px / 9f + 2f * Noise(px / 30f, 0f, j.seed + 113));
                    float fine = Noise(px / 5f, 5f, j.seed + 112);
                    int drapeDown = 12 + Mathf.RoundToInt(5f * lobe + 3f * fine);   // onto the rock
                    int bulgeUp = 2 + Mathf.RoundToInt(2.5f * (lobe + 1f));         // over the plateau above
                    for (int t = 0; t < drapeDown; t++)
                    {
                        int y = py - t; if (y < 0) break;
                        if (j.kind[y * pw + px] != Cliff) break;
                        j.px[y * pw + px] = t == 0 ? SnowSpark : t < drapeDown - 3 ? SnowC[4] : t < drapeDown - 1 ? SnowC[3] : SnowC[1];
                    }
                    for (int t = 1; t <= bulgeUp; t++)
                    {
                        int y = py + t; if (y >= ph) break;
                        byte kk = j.kind[y * pw + px];
                        if (kk != Snow && kk != Cobble) break;
                        j.px[y * pw + px] = t >= bulgeUp ? SnowC[3] : SnowSpark;   // rounded crest above the lip
                    }
                    // Icicles hanging from the lobes down the face.
                    if (lobe < -0.3f && Mod(px, 4) == 0)
                    {
                        int y = py - drapeDown;
                        int len = 3 + Mod(px * 7, 5);
                        for (int t = 0; t < len && y - t >= 0 && j.kind[(y - t) * pw + px] == Cliff; t++)
                            j.px[(y - t) * pw + px] = t < len - 1 ? IceSheet : IceD2;
                    }
                    break;
                }
        }

        static readonly Color32 IceD2 = H("#a6d4e7");

        static Color32 Darken(Color32 c, float f) => PixelCanvas.Shade(c, f);
        static Color32 Lighten(Color32 c, float f) => PixelCanvas.Shade(c, f);

        static Color32 WMix(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));
        }

        // ---------- 4. Small details stamped on top ----------

        static void Set(Job j, int x, int y, byte onlyKind, Color32 c)
        {
            if (x < 0 || y < 0 || x >= j.pw || y >= j.ph) return;
            int i = y * j.pw + x;
            if (j.kind[i] != onlyKind) return;
            j.px[i] = c;
        }

        static void Stamps(Job j)
        {
            var rng = new System.Random(j.seed * 31 + 7);
            // Sparse sparkles and tiny drift bumps on the open snow, one candidate per 7x7 block.
            for (int by = 0; by < j.ph; by += 7)
                for (int bx = 0; bx < j.pw; bx += 7)
                {
                    int x = bx + rng.Next(0, 7), y = by + rng.Next(0, 7);
                    if (x >= j.pw || y >= j.ph) continue;
                    int i = y * j.pw + x;
                    if (j.kind[i] != Snow) continue;
                    double r = rng.NextDouble();
                    if (r < 0.03) Sparkle(j, x, y);
                    else if (r < 0.09) DriftBump(j, x, y);
                }
            // A very occasional ice sheen on the open river.
            for (int by = 0; by < j.ph; by += 10)
                for (int bx = 0; bx < j.pw; bx += 10)
                {
                    int x = bx + rng.Next(0, 10), y = by + rng.Next(0, 10);
                    if (x >= j.pw || y >= j.ph) continue;
                    if (j.kind[y * j.pw + x] != Water) continue;
                    if (rng.NextDouble() < 0.05) IceSheen(j, x, y, rng.Next(3, 6));
                }
        }

        static void Sparkle(Job j, int x, int y)
        {
            Set(j, x, y, Snow, SnowSpark);
            Set(j, x - 1, y, Snow, SnowC[4]);
            Set(j, x + 1, y, Snow, SnowC[4]);
            Set(j, x, y - 1, Snow, SnowC[4]);
            Set(j, x, y + 1, Snow, SnowC[4]);
        }

        static void DriftBump(Job j, int x, int y)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                int h = Mathf.RoundToInt(1.6f * Mathf.Cos(dx / 3f * 1.5f));
                for (int dy = 0; dy <= h; dy++)
                {
                    Set(j, x + dx, y - dy, Snow, dy == h ? SnowC[4] : SnowC[3]);
                }
                Set(j, x + dx, y + 1, Snow, SnowC[2]);
            }
        }

        static void IceSheen(Job j, int x, int y, int len)
        {
            for (int k = 0; k < len; k++)
                Set(j, x + k, y, Water, k == 0 || k == len - 1 ? WaterC[4] : IceSheet);
        }
    }
}
