using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Paints the ground of the high-resolution maps (village town, forest hunting ground) as one
    /// continuous picture at 32 pixels per tile, instead of stamping 16px tiles. Terrain borders are
    /// found by blending the text map's cells and wobbling the result with noise, so paths, shores
    /// and forest edges are organic curves with no square tile edges; textures (grass tones, round
    /// cobblestones, packed earth, furrows, planks, water, a forest canopy seen from above) are sampled
    /// at each pixel's world position, so nothing repeats per tile.
    ///
    /// Cell codes: . grass  : flower meadow  ; tall grass / ferns  , cobble  = dirt  # soil  ~ water
    /// d wooden deck  % dense forest canopy. The result is laid out bottom-up (Unity texture order).
    /// </summary>
    public static class TownTerrain
    {
        public const int Px = 32;

        const byte Grass = 0, Cobble = 1, Dirt = 2, Soil = 3, Water = 4, Deck = 5, Thicket = 6, KindCount = 7;

        // ---------- Palettes ----------

        struct Palette
        {
            public Color32[] grass;      // dark → light (5)
            public Color32[] canopy;     // darkest gap → highlight (6)
            public Color32[] dirt;       // dark rim → light (4)
            public Color32 blade, bladeDark;
        }

        static readonly Palette TownPal = new Palette
        {
            grass = new[] { H("#4a9a40"), H("#56a947"), H("#61b44e"), H("#6ebf57"), H("#8fd46c") },
            // Match the village's imported teal / olive trees at the continuous forest boundary.
            canopy = new[] { H("#0b2029"), H("#123a3b"), H("#205044"), H("#35633c"), H("#52743d"), H("#7b9246") },
            dirt = new[] { H("#9a6a43"), H("#b98553"), H("#cc9a64"), H("#ddb27c") },
            blade = H("#a3e07e"), bladeDark = H("#3e8a3a"),
        };

        static readonly Palette ForestPal = new Palette
        {
            grass = new[] { H("#2d5d35"), H("#34683a"), H("#3c7440"), H("#458147"), H("#62a05a") },
            canopy = new[] { H("#0f2418"), H("#16321f"), H("#1e4428"), H("#285731"), H("#336a3a"), H("#447f45") },
            dirt = new[] { H("#7a5639"), H("#94694a"), H("#a87b56"), H("#bb8f67") },
            blade = H("#79b86b"), bladeDark = H("#27513a"),
        };

        static readonly Color32[] Stone = { H("#8f897f"), H("#a39c92"), H("#b2aba0"), H("#c0b9ad"), H("#d0cabe") };
        static readonly Color32[] StoneWarm = { H("#908275"), H("#a69683"), H("#b5a591"), H("#c3b39e"), H("#d4c5b0") };
        static readonly Color32 Mortar = H("#857f77"), MortarMoss = H("#6f8d50");
        static readonly Color32[] SoilC = { H("#5c3a22"), H("#734a2c"), H("#8a5a36"), H("#a06b41"), H("#b47d4f") };
        static readonly Color32[] WaterC = { H("#2b64a8"), H("#3478bf"), H("#428ed3"), H("#58a5e0"), H("#7cc0ec"), H("#a9dcf5") };
        static readonly Color32 Foam = H("#e6f6fd");
        static readonly Color32[] BankC = { H("#4a3222"), H("#5e402b"), H("#735036"), H("#8a6243") };
        static readonly Color32[] Plank = { H("#6e4428"), H("#8e5a35"), H("#a86d42"), H("#c28551"), H("#d69c66") };

        static Color32 H(string hex) => PixelCanvas.Hex(hex);

        // ---------- Noise (shared with the other 32px terrain painters) ----------

        internal static float Hash01(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 982451653);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Smooth value noise in [-1, 1].</summary>
        internal static float Noise(float x, float y, int s)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash01(x0, y0, s), b = Hash01(x0 + 1, y0, s), c = Hash01(x0, y0 + 1, s), d = Hash01(x0 + 1, y0 + 1, s);
            return (a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy) * 2f - 1f;
        }

        internal static float Fbm(float x, float y, int s) => Noise(x, y, s) * 0.62f + Noise(x * 2.07f, y * 2.07f, s + 7) * 0.28f + Noise(x * 4.3f, y * 4.3f, s + 13) * 0.1f;

        // ---------- State of one paint job ----------

        sealed class Job
        {
            public int w, h, pw, ph;
            public byte[] cells;      // per cell kind
            public byte[] flags;      // 1 = flowers, 2 = tall grass
            public byte[] kind;       // per pixel kind
            public byte[] other;      // per pixel second kind
            public float[] margin;    // per pixel best - second membership
            public Color32[] px;
            public Palette pal;
            public bool forest;
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
                case '=': return Dirt;
                case '#': return Soil;
                case '~': return Water;
                case 'd': return Deck;
                case '%': return Thicket;
                default: return Grass;
            }
        }

        /// <summary>Paints a map. <paramref name="ground"/>[x, y] is the ground code of each cell (y = 0 at the bottom).</summary>
        public static Color32[] Paint(char[,] ground, int w, int h, bool forest, out int pw, out int ph, System.Action<WaterField> waterReady = null)
        {
            var j = new Job { w = w, h = h, pw = w * Px, ph = h * Px, forest = forest, pal = forest ? ForestPal : TownPal, seed = forest ? 53 : 17 };
            pw = j.pw;
            ph = j.ph;
            j.cells = new byte[w * h];
            j.flags = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = ground[x, y];
                    j.cells[y * w + x] = KindOf(c);
                    j.flags[y * w + x] = (byte)(c == ':' ? 1 : c == ';' ? 2 : 0);
                }
            int n = j.pw * j.ph;
            j.kind = new byte[n];
            j.other = new byte[n];
            j.margin = new float[n];
            j.px = new Color32[n];

            Classify(j);
            PaintBase(j);
            PaintWater(j);
            Stamps(j);
            waterReady?.Invoke(WaterField.Create(j.kind, j.px, pw, ph, Water));
            return j.px;
        }

        // ---------- 1. Which terrain is at each pixel ----------

        static void Membership(Job j, float u, float v, float[] weights, out byte best, out byte second, out float margin)
        {
            for (int k = 0; k < KindCount; k++) weights[k] = 0f;
            float gu = u - 0.5f, gv = v - 0.5f;
            int i0 = Mathf.FloorToInt(gu), j0 = Mathf.FloorToInt(gv);
            float fx = gu - i0, fy = gv - j0;
            // Smoothstep makes corners round instead of chamfered.
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
                    // Wobble the borders so they never follow the tile grid.
                    const float amp = 0.2f;
                    float wu = u + amp * Noise(u * 0.85f, v * 0.85f, j.seed + 3) + 0.06f * Noise(u * 3.1f, v * 3.1f, j.seed + 5);
                    float wv = v + amp * Noise(u * 0.85f + 31.7f, v * 0.85f - 12.3f, j.seed + 4) + 0.06f * Noise(u * 3.1f + 9f, v * 3.1f, j.seed + 6);
                    Membership(j, wu, wv, weights, out byte best, out byte second, out float margin);
                    // Farm soil and wooden decks stay tidy: they follow their own cells exactly.
                    if (own == Deck) { best = Deck; second = Water; margin = 1f; }
                    else if (best == Deck) { best = second == Deck ? Water : second; margin = 0.05f; }
                    if (own == Soil || best == Soil)
                    {
                        Membership(j, u, v, weights, out best, out second, out margin);
                        if (best == Deck) best = Water;
                    }
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
            System.Threading.Tasks.Parallel.For(0, j.ph, py => PaintBaseRow(j, py));
        }

        static void PaintBaseRow(Job j, int py)
        {
                for (int px = 0; px < j.pw; px++)
                {
                    int i = py * j.pw + px;
                    byte k = j.kind[i];
                    byte other = j.other[i];
                    bool edge = j.margin[i] < 0.34f;
                    Color32 c;
                    switch (k)
                    {
                        case Cobble:
                            c = StoneOrNull(j, px, py, true, out bool stone);
                            if (!stone)
                                c = edge && other == Grass ? GrassAt(j, px, py, i)
                                    : other == Grass && j.margin[i] < 0.6f && Hash01(px, py, j.seed + 53) < 0.4f ? MortarMoss : Mortar;
                            break;
                        case Dirt: c = DirtAt(j, px, py, i); break;
                        case Soil: c = SoilAt(j, px, py, i); break;
                        case Deck: c = DeckAt(j, px, py); break;
                        case Thicket:
                            if (!CrownAt(j, px, py, out c))
                            {
                                // Between crowns: forest floor near open ground, deep shade inside the forest.
                                int cx = px / Px, cy = py / Px;
                                bool rim = j.Cell(cx - 1, cy) != Thicket || j.Cell(cx + 1, cy) != Thicket || j.Cell(cx, cy - 1) != Thicket || j.Cell(cx, cy + 1) != Thicket;
                                c = rim || (edge && other == Grass) ? PixelCanvas.Shade(j.pal.grass[0], 0.82f) : j.pal.canopy[0];
                            }
                            break;
                        case Water: c = WaterC[3]; break;
                        default:
                            c = GrassAt(j, px, py, i);
                            // Stones of the street / plaza that poke out past the smooth border.
                            if (edge && other == Cobble)
                            {
                                var s = StoneOrNull(j, px, py, false, out bool isStone);
                                if (isStone) c = s;
                            }
                            break;
                    }
                    j.px[i] = c;
                }
        }

        static Color32 GrassAt(Job j, int px, int py, int i)
        {
            var g = j.pal.grass;
            float n = Fbm(px / 34f, py / 34f, j.seed + 20);
            int tone = n < -0.3f ? 1 : n > 0.36f ? 3 : 2;
            float m = Noise(px / 6.5f, py / 6.5f, j.seed + 21);
            if (m > 0.86f) tone = Mathf.Min(4, tone + 1);
            else if (m < -0.88f) tone = Mathf.Max(0, tone - 1);
            byte other = j.other[i];
            float margin = j.margin[i];
            // Grass right next to a path, street or field sits a touch lower: a thin shade line.
            if ((other == Dirt || other == Cobble || other == Soil) && margin < 0.07f) tone = Mathf.Max(0, tone - 1);
            // Shade cast by the forest canopy.
            if (other == Thicket && margin < 0.34f) tone = Mathf.Max(0, tone - (margin < 0.16f ? 2 : 1));
            return g[tone];
        }

        static Color32 DirtAt(Job j, int px, int py, int i)
        {
            var d = j.pal.dirt;
            float n = Fbm(px / 22f, py / 22f, j.seed + 30);
            int tone = n < -0.35f ? 1 : n > 0.42f ? 3 : 2;
            float m = Noise(px / 4f, py / 4f, j.seed + 31);
            if (m > 0.82f) tone = 3;
            if (j.margin[i] < 0.08f && j.other[i] != Dirt) tone = 0;
            else if (j.margin[i] < 0.15f && j.other[i] == Grass) tone = Mathf.Min(tone, 1);
            return d[tone];
        }

        static Color32 SoilAt(Job j, int px, int py, int i)
        {
            if (j.margin[i] < 0.1f && j.other[i] != Soil) return SoilC[0];
            int row = Mod(py + 3, 8);
            int tone = row == 0 ? 0 : row == 1 ? 1 : row == 7 ? 4 : 3;
            float n = Noise(px / 5f, py / 3f, j.seed + 40);
            if (n > 0.7f && tone >= 3) tone = 2;
            if (n < -0.82f && tone == 3) tone = 4;
            return SoilC[tone];
        }

        static int Mod(int a, int m) => (a % m + m) % m;

        // Round cobblestones: a jittered grid of stone centres; each pixel belongs to the nearest stone,
        // with mortar where two stones meet or beyond a stone's radius.
        const int StoneCell = 14;

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
                    float fx = ix * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed) - 0.5f) * 5.5f + ((iy & 1) != 0 ? StoneCell * 0.5f : 0f);
                    float fy = iy * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed + 1) - 0.5f) * 5f;
                    float ex = px + 0.5f - fx, ey = py + 0.5f - fy;
                    float d = ex * ex + ey * ey;
                    if (d < d1) { d2 = d1; d1 = d; sx = ix; sy = iy; cxOut = fx; cyOut = fy; }
                    else if (d < d2) d2 = d;
                }
            d1 = Mathf.Sqrt(d1);
            d2 = Mathf.Sqrt(d2);
        }

        /// <summary>
        /// The cobblestone covering a pixel, if the stone's centre lies on the street / plaza (so borders
        /// run along whole stones). <paramref name="stone"/> is false for mortar or stones that belong
        /// outside. <paramref name="insideCobble"/> tells whether the pixel itself is street.
        /// </summary>
        static Color32 StoneOrNull(Job j, int px, int py, bool insideCobble, out bool stone)
        {
            NearestStone(px, py, j.seed + 50, out float d1, out float d2, out int sx, out int sy, out float cx, out float cy);
            float radius = StoneCell * (0.52f + Hash01(sx, sy, j.seed + 52) * 0.12f);
            stone = false;
            if (d2 - d1 < 1.4f || d1 > radius) return Mortar;
            if (j.KindAt(Mathf.Clamp(Mathf.RoundToInt(cx), 0, j.pw - 1), Mathf.Clamp(Mathf.RoundToInt(cy), 0, j.ph - 1)) != Cobble) return Mortar;
            stone = true;
            var ramp = Hash01(sx, sy, j.seed + 54) < 0.25f ? StoneWarm : Stone;
            float roll = Hash01(sx, sy, j.seed + 55);
            int baseTone = roll < 0.12f ? 1 : roll < 0.62f ? 2 : 3;
            float ox = (px + 0.5f - cx) / radius, oy = (py + 0.5f - cy) / radius;
            float lit = -ox * 0.55f + oy * 0.83f; // light from the top-left (y is up here)
            int tone = baseTone;
            if (lit > 0.42f) tone = Mathf.Min(4, baseTone + 1);
            else if (lit < -0.45f) tone = Mathf.Max(0, baseTone - 1);
            if (d1 > radius - 1.4f && lit < 0.1f) tone = Mathf.Max(0, tone - 1);
            return ramp[tone];
        }

        /// <summary>Tree crown of the forest roof covering a pixel (only crowns rooted in forest cells).</summary>
        static bool CrownAt(Job j, int px, int py, out Color32 color)
        {
            var c = j.pal.canopy;
            int gx = Mathf.FloorToInt((float)px / CrownCell), gy = Mathf.FloorToInt((float)py / CrownCell);
            float bestH = -1f, bestLit = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = gx + dx, iy = gy + dy;
                    float fx = ix * CrownCell + CrownCell * 0.5f + (Hash01(ix, iy, j.seed + 60) - 0.5f) * 11f;
                    float fy = iy * CrownCell + CrownCell * 0.5f + (Hash01(ix, iy, j.seed + 61) - 0.5f) * 11f;
                    float r = CrownCell * (0.62f + Hash01(ix, iy, j.seed + 62) * 0.22f);
                    float ex = (px + 0.5f - fx) / r, ey = (py + 0.5f - fy) / r;
                    float d = ex * ex + ey * ey;
                    if (d >= 1f) continue;
                    if (j.KindAt(Mathf.Clamp(Mathf.RoundToInt(fx), 0, j.pw - 1), Mathf.Clamp(Mathf.RoundToInt(fy), 0, j.ph - 1)) != Thicket) continue;
                    float hgt = Mathf.Sqrt(1f - d) + Hash01(ix, iy, j.seed + 63) * 0.35f; // higher crowns win overlaps
                    if (hgt <= bestH) continue;
                    bestH = hgt;
                    float nz = Mathf.Sqrt(1f - d);
                    bestLit = -ex * 0.5f + ey * 0.6f + nz * 0.62f;
                }
            if (bestH < 0f) { color = c[0]; return false; }
            int tone = bestLit > 0.95f ? 5 : bestLit > 0.72f ? 4 : bestLit > 0.45f ? 3 : bestLit > 0.15f ? 2 : 1;
            // A few leaf clusters catching the light.
            if (tone >= 3 && Noise(px / 3.2f, py / 3.2f, j.seed + 64) > 0.72f) tone = Mathf.Min(5, tone + 1);
            color = c[tone];
            return true;
        }

        // Forest roof: overlapping round crowns lit from the top-left, dark gaps between them.
        const int CrownCell = 19;

        static Color32 DeckAt(Job j, int px, int py)
        {
            int cx = px / Px, cy = py / Px;
            bool vertical = j.Cell(cx, cy + 1) == Deck || j.Cell(cx, cy - 1) == Deck;
            int along = vertical ? py : px;     // boards are stacked along the dock
            int across = vertical ? px : py;
            int board = Mathf.FloorToInt(along / 7f);
            int inBoard = Mod(along, 7);
            if (inBoard == 0) return Plank[0];
            // Edge beams along both sides of the dock.
            bool sideA = vertical ? j.Cell(cx - 1, cy) != Deck : j.Cell(cx, cy - 1) != Deck;
            bool sideB = vertical ? j.Cell(cx + 1, cy) != Deck : j.Cell(cx, cy + 1) != Deck;
            int local = Mod(across, Px);
            if ((sideA && local < 3) || (sideB && local > Px - 4)) return inBoard == 1 ? Plank[2] : Plank[1];
            int tone = 2 + (int)(Hash01(board, cx + cy * 7, j.seed + 70) * 2.99f); // 2..4
            if (inBoard == 1) tone = Mathf.Min(4, tone + 1);
            if (inBoard == 6) tone = Mathf.Max(1, tone - 1);
            // Nails near the beams.
            if (inBoard == 3 && ((sideA && local == 4) || (sideB && local == Px - 5))) return Plank[0];
            return Plank[Mathf.Min(4, tone)];
        }

        // ---------- 3. Water: depth, banks, foam ----------

        static void PaintWater(Job j)
        {
            int pw = j.pw, ph = j.ph;
            // Distance to the shore (chamfer transform over water pixels, in pixels).
            var dist = new float[pw * ph];
            const float Far = 999f;
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

            System.Threading.Tasks.Parallel.For(0, ph, py =>
            {
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    if (j.kind[i] != Water) continue;
                    // What lies directly north (above) within 8 px: a bank face of earth, or a dock.
                    int up = 0;
                    byte upKind = Water;
                    for (int k = 1; k <= 8; k++)
                    {
                        byte kk = j.KindAt(px, py + k);
                        if (kk == 255) break;
                        if (kk != Water) { up = k; upKind = kk; break; }
                    }
                    Color32 c;
                    if (up > 0 && upKind == Deck)
                    {
                        // Front beam of the dock, then its shadow on the water.
                        c = up <= 3 ? Plank[up == 1 ? 1 : 0] : up <= 7 ? Darken(WaterC[1], 0.8f) : WaterC[2];
                    }
                    else if (up > 0 && up <= 5)
                    {
                        // Earth bank under the land edge, darker towards the water.
                        float n = Noise(px / 3f, py / 5f, j.seed + 80);
                        int tone = up <= 1 ? 3 : up <= 3 ? 2 : 1;
                        if (n > 0.55f) tone = Mathf.Min(3, tone + 1);
                        if (up == 5) tone = 0;
                        c = BankC[tone];
                    }
                    else if (up == 6)
                    {
                        c = Foam;
                    }
                    else
                    {
                        float d = dist[i];
                        if (d <= 1.5f) c = Foam;                       // lapping at the other shores
                        else if (d <= 3.5f || up == 7) c = WaterC[4];  // shallow rim
                        else
                        {
                            float depth = Mathf.Clamp01((d - 3.5f) / 26f) + Noise(px / 18f, py / 12f, j.seed + 81) * 0.18f;
                            c = depth > 0.72f ? WaterC[0] : depth > 0.45f ? WaterC[1] : depth > 0.2f ? WaterC[2] : WaterC[3];
                        }
                    }
                    j.px[i] = c;
                }
            });

            // Grass lip along the top edge of every bank (land pixel with water right below it).
            for (int py = 1; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    int i = py * pw + px;
                    byte k = j.kind[i];
                    if (k == Water || k == Deck || j.kind[i - pw] != Water) continue;
                    if (k == Grass) j.px[i] = j.pal.grass[4];
                    else if (k == Thicket) j.px[i] = j.pal.canopy[2];
                    else j.px[i] = Lighten(j.px[i], 1.15f);
                }
        }

        static Color32 Darken(Color32 c, float f) => PixelCanvas.Shade(c, f);
        static Color32 Lighten(Color32 c, float f) => PixelCanvas.Shade(c, f);

        // ---------- 4. Small details stamped on top ----------

        static void Set(Job j, int x, int y, byte onlyKind, Color32 c)
        {
            if (x < 0 || y < 0 || x >= j.pw || y >= j.ph) return;
            int i = y * j.pw + x;
            if (j.kind[i] != onlyKind) return;
            j.px[i] = c;
        }

        static byte FlagAt(Job j, int px, int py) => j.flags[Mathf.Clamp(py / Px, 0, j.h - 1) * j.w + Mathf.Clamp(px / Px, 0, j.w - 1)];

        static readonly Color32[] Petals = { H("#ffffff"), H("#ffe066"), H("#ff9ec4"), H("#b9a6ff"), H("#ff7f6a"), H("#8fd0ff") };

        static void Stamps(Job j)
        {
            var rng = new System.Random(j.seed * 31 + 7);
            var pal = j.pal;
            // Grass tufts and flowers, one candidate per 6x6 block.
            for (int by = 0; by < j.ph; by += 6)
                for (int bx = 0; bx < j.pw; bx += 6)
                {
                    int x = bx + rng.Next(0, 6), y = by + rng.Next(0, 6);
                    double r = rng.NextDouble();
                    int i = Mathf.Clamp(y, 0, j.ph - 1) * j.pw + Mathf.Clamp(x, 0, j.pw - 1);
                    byte k = j.kind[i];
                    byte flag = FlagAt(j, x, y);
                    if (k == Grass)
                    {
                        double tuft = flag == 2 ? 0.75 : j.forest ? 0.3 : 0.2;
                        double flower = flag == 1 ? 0.55 : j.forest ? 0.006 : 0.022;
                        if (r < flower) Flower(j, x, y, Petals[rng.Next(0, j.forest ? 3 : Petals.Length)]);
                        else if (r < flower + tuft) Tuft(j, x, y, pal, flag == 2 ? 3 : 2 + rng.Next(0, 2));
                        else if (j.forest && r > 0.975) Leaf(j, x, y, rng.NextDouble() < 0.5 ? H("#b07a3a") : H("#c9963e"));
                        else if (!j.forest && r > 0.985) Clover(j, x, y, pal);
                    }
                    else if (k == Dirt)
                    {
                        if (r < 0.16) Pebble(j, x, y, Dirt, rng.NextDouble() < 0.5);
                        else if (j.forest && r > 0.96) Leaf(j, x, y, H("#8a6035"));
                    }
                    else if (k == Water)
                    {
                        if (r < 0.05) Ripple(j, x, y, 3 + rng.Next(0, 5));
                        else if (!j.forest && r > 0.994) LilyPad(j, x, y, rng.NextDouble() < 0.4);
                    }
                    else if (k == Thicket && r < 0.05)
                    {
                        Set(j, x, y, Thicket, pal.canopy[5]);
                    }
                }
        }

        static void Tuft(Job j, int x, int y, Palette pal, int height)
        {
            // A few blades in a "v": light tips, a dark root.
            Set(j, x + 1, y, Grass, pal.bladeDark);
            for (int k = 1; k <= height; k++)
            {
                Set(j, x, y + k, Grass, k == height ? pal.blade : pal.grass[3]);
                Set(j, x + 2 + (k > 1 ? 1 : 0), y + k, Grass, k == height ? pal.blade : pal.grass[3]);
            }
            Set(j, x + 1, y + 1, Grass, pal.grass[3]);
        }

        static void Flower(Job j, int x, int y, Color32 petal)
        {
            var stem = j.pal.bladeDark;
            Set(j, x, y - 1, Grass, stem);
            Set(j, x, y + 1, Grass, petal);
            Set(j, x - 1, y, Grass, petal);
            Set(j, x + 1, y, Grass, petal);
            Set(j, x, y + 2, Grass, PixelCanvas.Shade(petal, 0.9f));
            Set(j, x, y, Grass, H("#ffd23f"));
        }

        static void Clover(Job j, int x, int y, Palette pal)
        {
            var c = pal.grass[0];
            Set(j, x, y, Grass, c); Set(j, x + 1, y, Grass, c); Set(j, x, y + 1, Grass, c);
            Set(j, x + 1, y + 1, Grass, pal.grass[3]);
        }

        static void Leaf(Job j, int x, int y, Color32 c)
        {
            byte k = j.KindAt(x, y);
            if (k != Grass && k != Dirt) return;
            Set(j, x, y, k, c);
            Set(j, x + 1, y, k, PixelCanvas.Shade(c, 0.85f));
        }

        static void Pebble(Job j, int x, int y, byte onKind, bool big)
        {
            var dark = H("#7d6a58");
            var mid = H("#a8957f");
            var light = H("#cbb9a1");
            Set(j, x, y, onKind, dark); Set(j, x + 1, y, onKind, dark);
            Set(j, x, y + 1, onKind, mid); Set(j, x + 1, y + 1, onKind, light);
            if (big) { Set(j, x + 2, y, onKind, dark); Set(j, x + 2, y + 1, onKind, mid); Set(j, x + 1, y + 2, onKind, light); }
        }

        static void Ripple(Job j, int x, int y, int len)
        {
            int i = y * j.pw + x;
            if (y < 0 || y >= j.ph || x < 0 || x >= j.pw) return;
            // Only on open water (not on the bank or foam).
            if (j.px[i].r != WaterC[1].r && j.px[i].r != WaterC[2].r && j.px[i].r != WaterC[0].r && j.px[i].r != WaterC[3].r) return;
            for (int k = 0; k < len; k++) Set(j, x + k, y, Water, k == 0 || k == len - 1 ? WaterC[3] : WaterC[4]);
        }

        static void LilyPad(Job j, int x, int y, bool flower)
        {
            var pad = H("#4f9a4a");
            var padLight = H("#6fbb5c");
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (dx * dx / 9f + dy * dy / 4f > 1f) continue;
                    if (dx >= 0 && dy == 0 && dx <= 2) continue; // the notch
                    Set(j, x + dx, y + dy, Water, dy > 0 ? padLight : pad);
                }
            if (flower) { Set(j, x - 1, y + 1, Water, H("#ff9ec4")); Set(j, x, y + 1, Water, H("#ffd0e4")); Set(j, x - 1, y + 2, Water, H("#ff9ec4")); }
        }
    }
}
