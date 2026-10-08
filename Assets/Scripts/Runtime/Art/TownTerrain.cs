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
    public static partial class TownTerrain
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
    }
}
