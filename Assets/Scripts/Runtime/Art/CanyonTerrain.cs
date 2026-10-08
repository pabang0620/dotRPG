using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Paints the ground of the 32px canyon town as one continuous picture at 32 pixels per tile,
    /// the same way <see cref="TownTerrain"/> paints the village and forest. Nothing is stamped per
    /// tile: sandy flagstone plazas, moss-green grass, teal water walled by cut-stone coping, real
    /// 3/4-view cliffs (rocky plateau tops and south-facing sedimentary walls with a lit rim, block
    /// volumes and a rubble foot), stone stairs cut into the rock and a plank bridge with rails are all
    /// sampled at each pixel's world position, with noise-wobbled soft borders so no tile grid shows.
    ///
    /// Cell codes: , flagstone  . moss grass  ~ canyon water  W cliff (solid rock; plateau top or wall)
    /// L stairs  d wooden bridge / lookout deck. The result is laid out bottom-up (Unity texture order).
    /// Cliffs and water are drawn here for looks only; their colliders are placed per cell by WorldBuilder.
    /// </summary>
    public static partial class CanyonTerrain
    {
        public const int Px = 32;

        // Pixel kinds (what is painted at a pixel).
        const byte Flag = 0, Grass = 1, Water = 2, Cliff = 3, Stairs = 4, Deck = 5, KindCount = 6;

        static Color32 H(string hex) => PixelCanvas.Hex(hex);

        // ---------- Palette: warm sandy flagstone, moss grass, brown strata, teal water ----------

        // Flagstone: warm sandstone pavers, dark → light.
        static readonly Color32[] FlagStone = { H("#9c7a54"), H("#b28f64"), H("#c2a074"), H("#d2b487"), H("#e2c79b") };
        static readonly Color32[] FlagStoneCool = { H("#95795d"), H("#a98d6c"), H("#b89c7c"), H("#c9b090"), H("#dbc3a5") };
        static readonly Color32 FlagMortar = H("#6f5a41"), FlagMortarMoss = H("#7d7a45");

        // Moss grass: canyon greens, dark → light.
        static readonly Color32[] GrassC = { H("#4c6a34"), H("#587a3c"), H("#658945"), H("#749a50"), H("#8fb468") };
        static readonly Color32 Blade = H("#a6c877"), BladeDark = H("#3c5a2c");

        // Cliff: a warm sedimentary ramp (dark foot → light rim). Alternating strata pick two neighbouring
        // tones so bands read as browns of different warmth, not a flat camo.
        static readonly Color32[] RockA = { H("#3a2719"), H("#4d3423"), H("#63472f"), H("#7c5b3d"), H("#96714e"), H("#b28c66") };
        static readonly Color32[] RockB = { H("#42301f"), H("#573f28"), H("#6f5335"), H("#8a6842"), H("#a47e56"), H("#bf9a70") };
        // Plateau top: lighter, sunlit rock shelf.
        static readonly Color32[] PlateauC = { H("#7c6144"), H("#94764f"), H("#a98a60"), H("#bf9f73"), H("#d3b488") };
        static readonly Color32 RockRim = H("#e2c393"), RockRimHi = H("#f0d7a8");
        static readonly Color32 RockFoot = H("#241811");

        // Stairs: cut stone steps (8-tone ramp for StairPainter, deepest shadow → nosing highlight),
        // a touch greyer than the flagstone so the flight reads as its own cut-stone structure.
        static readonly Color32[] StairRamp = { H("#382d24"), H("#4b3e32"), H("#605041"), H("#796851"), H("#928069"), H("#a8967c"), H("#bfae92"), H("#d6c8ab") };
        static readonly Color32[] StairSand = { H("#c3a577"), H("#d6bc8d") };
        static readonly Color32[] StairMoss = { H("#55733a"), H("#6d8c46"), H("#89a85a") };

        // Water: teal canyon pool, deep → shallow, plus foam.
        static readonly Color32[] WaterC = { H("#0f4a4c"), H("#155a5a"), H("#1c7070"), H("#268a86"), H("#33a29a"), H("#4dbcb0"), H("#7fd6c8") };
        static readonly Color32 Foam = H("#dff7f0");
        static readonly Color32 WetLine = H("#0c3a3c");

        // Cut-stone coping around the water: warm grey blocks, top-lit.
        static readonly Color32[] CopingC = { H("#6a5942"), H("#867155"), H("#9d8768"), H("#b7a07e"), H("#cdb790") };

        // Wooden bridge / deck planks and rails.
        static readonly Color32[] Plank = { H("#5c3b23"), H("#7a4f30"), H("#96633e"), H("#b17c4f"), H("#c99765") };
        static readonly Color32[] RailC = { H("#3f2917"), H("#5c3d24"), H("#7a5232"), H("#976a44") };

        // ---------- Noise (reuses TownTerrain's shared hashes/noise) ----------

        static float Hash01(int x, int y, int s) => TownTerrain.Hash01(x, y, s);
        static float Noise(float x, float y, int s) => TownTerrain.Noise(x, y, s);
        static float Fbm(float x, float y, int s) => TownTerrain.Fbm(x, y, s);

        // ---------- State of one paint job ----------

        sealed class Job
        {
            public int w, h, pw, ph;
            public byte[] cells;      // per cell kind
            public byte[] kind;       // per pixel kind
            public byte[] other;      // per pixel second kind
            public float[] margin;    // per pixel best - second membership
            public float[] shade;     // per pixel soft shade multiplier (1 = none)
            public Color32[] px;
            public int seed;
            public int fieldVariant=-1;

            public byte Cell(int x, int y)
            {
                x = Mathf.Clamp(x, 0, w - 1);
                y = Mathf.Clamp(y, 0, h - 1);
                return cells[y * w + x];
            }

            public byte CellKind(int x, int y) => (x < 0 || y < 0 || x >= w || y >= h) ? (byte)255 : cells[y * w + x];

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
                case '.': return Grass;
                case '~': return Water;
                case 'W': return Cliff;
                case 'L': return Stairs;
                case 'd': return Deck;
                default: return Flag;   // ',' flagstone and anything else
            }
        }

        /// <summary>Paints the canyon. <paramref name="ground"/>[x, y] is the ground code of each cell (y = 0 at the bottom).</summary>
        public static Color32[] Paint(char[,] ground, int w, int h, out int pw, out int ph, System.Action<WaterField> waterReady = null, int fieldVariant = -1)
        {
            var j = new Job { w = w, h = h, pw = w * Px, ph = h * Px, seed = 131,fieldVariant=fieldVariant };
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
            j.shade = new float[n];
            j.px = new Color32[n];

            Classify(j);
            ComputeShadeBand(j);
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
                    // Wobble flagstone/grass borders so they curve. Cliffs wobble a little too (<0.25 tile)
                    // for an organic silhouette; water, stairs and decks keep true cell edges (cut/built).
                    const float amp = 0.19f;
                    float wu = u + amp * Noise(u * 0.85f, v * 0.85f, j.seed + 3) + 0.06f * Noise(u * 3.1f, v * 3.1f, j.seed + 5);
                    float wv = v + amp * Noise(u * 0.85f + 31.7f, v * 0.85f - 12.3f, j.seed + 4) + 0.06f * Noise(u * 3.1f + 9f, v * 3.1f, j.seed + 6);
                    Membership(j, wu, wv, weights, out byte best, out byte second, out float margin);

                    if (own == Water || own == Stairs || own == Deck)
                    {
                        Membership(j, u, v, weights, out best, out second, out margin);
                        if (best != own) { best = own; margin = 1f; }
                    }
                    else if (own == Cliff)
                    {
                        // Small silhouette wobble only, capped under a quarter tile, and never onto water/deck/stairs.
                        float cu = u + 0.16f * Noise(u * 1.1f, v * 1.1f, j.seed + 7);
                        float cv = v + 0.16f * Noise(u * 1.1f + 5f, v * 1.1f, j.seed + 8);
                        Membership(j, cu, cv, weights, out best, out second, out margin);
                        if (best != Cliff)
                        {
                            byte nb = j.CellKind(Mathf.RoundToInt(cu) - 0, Mathf.RoundToInt(cv) - 0);
                            if (best == Water || best == Deck || best == Stairs) { best = Cliff; margin = 1f; }
                        }
                    }
                    else if (best == Water || best == Stairs || best == Deck)
                    {
                        Membership(j, u, v, weights, out best, out second, out margin);
                    }
                    else if (best == Cliff)
                    {
                        // A soft kind (flag/grass) near a cliff: only become cliff strictly inside the cliff cell.
                        Membership(j, u, v, weights, out best, out second, out margin);
                    }

                    int i = py * j.pw + px;
                    j.kind[i] = best;
                    j.other[i] = second;
                    j.margin[i] = margin;
                }
            });
        }

        // ---------- Soft shade band cast on the ground along the whole cliff foot ----------

        static void ComputeShadeBand(Job j)
        {
            // Distance (in pixels) from each non-cliff ground pixel up to the nearest cliff foot directly
            // above it, then a smooth falloff over ~0.9 tile. Continuous along the base, no hard edges.
            int reach = Mathf.RoundToInt(Px * 0.95f);
            System.Threading.Tasks.Parallel.For(0, j.ph, py =>
            {
                for (int px = 0; px < j.pw; px++)
                {
                    int i = py * j.pw + px;
                    byte k = j.kind[i];
                    if (k == Cliff || k == Water) { j.shade[i] = 1f; continue; }
                    int up = -1;
                    for (int d = 1; d <= reach; d++)
                    {
                        byte kk = j.KindAt(px, py + d);
                        if (kk == 255) break;
                        if (kk == Cliff) { up = d; break; }
                        if (kk == Deck) break; // a deck above shades its own way
                    }
                    if (up < 0) { j.shade[i] = 1f; continue; }
                    float t = up / (float)reach;                 // 0 at foot → 1 far
                    float amt = (1f - t) * (1f - t);             // smooth quadratic falloff
                    // Slight lateral wobble so the band edge is soft, not a ruler line.
                    amt *= 0.55f + 0.45f * (0.5f + 0.5f * Noise(px / 22f, py / 30f, j.seed + 200));
                    j.shade[i] = Mathf.Lerp(1f, 0.62f, Mathf.Clamp01(amt));
                }
            });
        }

        // ---------- 2. Base textures ----------
    }
}
