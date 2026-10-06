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
    public static class CanyonTerrain
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
                    case Flag:
                        if(j.fieldVariant>=0){c=FieldSand(j,px,py);if(j.shade[i]<.999f)c=PixelCanvas.Shade(c,j.shade[i]);break;}
                        c = StoneOrNull(j, px, py, out bool stone);
                        if (!stone)
                            c = edge && other == Grass ? GrassAt(j, px, py, i)
                                : other == Grass && j.margin[i] < 0.6f && Hash01(px, py, j.seed + 53) < 0.4f ? FlagMortarMoss : FlagMortar;
                        if (j.shade[i] < 0.999f) c = PixelCanvas.Shade(c, j.shade[i]);
                        break;
                    case Grass:
                        c = GrassAt(j, px, py, i);
                        if (j.shade[i] < 0.999f) c = PixelCanvas.Shade(c, j.shade[i]);
                        break;
                    case Cliff: c = CliffAt(j, px, py); break;
                    case Stairs:
                        c = StairsAt(j, px, py);
                        if (j.shade[i] < 0.999f) c = PixelCanvas.Shade(c, Mathf.Lerp(1f, j.shade[i], 0.5f));
                        break;
                    case Deck: c = DeckAt(j, px, py); break;
                    case Water: c = WaterC[4]; break;
                    default: c = GrassAt(j, px, py, i); break;
                }
                j.px[i] = c;
            }
        }

        // Broad dusty rock planes, broken paving patches and wind bands replace the town's uniform cobbles.
        static readonly Color32[] FieldSandRamp={new Color32(132,104,77,255),new Color32(149,116,84,255),new Color32(165,130,92,255),new Color32(181,146,105,255),new Color32(193,160,118,255)};
        static Color32 FieldSand(Job j,int x,int y){
            float broad=Noise(x/150f,y/130f,321+j.fieldVariant*13);
            int tone=Mathf.Clamp((int)((broad+1)*2.5f),0,4);

            // Retain scattered ancient paving instead of covering the entire canyon in a city floor.
            if(Noise(x/110f,y/95f,714+j.fieldVariant)> .5f){var stone=StoneOrNull(j,x,y,out bool yes);if(yes)return PixelCanvas.Shade(stone,.91f);}
            var c=FieldSandRamp[tone];int band=(y+(int)(10*Noise(x/82f,y/140f,457)))%39;
            if(band<2&&Noise(x/31f,y/39f,42)>.15f)c=PixelCanvas.Shade(c,.88f);
            return c;
        }

        static Color32 GrassAt(Job j, int px, int py, int i)
        {
            var g = GrassC;
            float n = Fbm(px / 34f, py / 34f, j.seed + 20);
            int tone = n < -0.3f ? 1 : n > 0.36f ? 3 : 2;
            float m = Noise(px / 6.5f, py / 6.5f, j.seed + 21);
            if (m > 0.86f) tone = Mathf.Min(4, tone + 1);
            else if (m < -0.88f) tone = Mathf.Max(0, tone - 1);
            byte other = j.other[i];
            float margin = j.margin[i];
            if (other == Flag && margin < 0.07f) tone = Mathf.Max(0, tone - 1);
            return g[tone];
        }

        // Warm sandstone flagstones: a jittered grid of paver centres; each pixel belongs to the nearest
        // paver, mortar where two pavers meet or beyond a paver's radius.
        const int StoneCell = 15;

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
                    float fx = ix * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed) - 0.5f) * 6f + ((iy & 1) != 0 ? StoneCell * 0.5f : 0f);
                    float fy = iy * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed + 1) - 0.5f) * 5.5f;
                    float ex = px + 0.5f - fx, ey = py + 0.5f - fy;
                    float d = ex * ex + ey * ey;
                    if (d < d1) { d2 = d1; d1 = d; sx = ix; sy = iy; cxOut = fx; cyOut = fy; }
                    else if (d < d2) d2 = d;
                }
            d1 = Mathf.Sqrt(d1);
            d2 = Mathf.Sqrt(d2);
        }

        static Color32 StoneOrNull(Job j, int px, int py, out bool stone)
        {
            NearestStone(px, py, j.seed + 50, out float d1, out float d2, out int sx, out int sy, out float cx, out float cy);
            float radius = StoneCell * (0.54f + Hash01(sx, sy, j.seed + 52) * 0.12f);
            stone = false;
            if (d2 - d1 < 1.5f || d1 > radius) return FlagMortar;
            if (j.KindAt(Mathf.Clamp(Mathf.RoundToInt(cx), 0, j.pw - 1), Mathf.Clamp(Mathf.RoundToInt(cy), 0, j.ph - 1)) != Flag) return FlagMortar;
            stone = true;
            var ramp = Hash01(sx, sy, j.seed + 54) < 0.3f ? FlagStoneCool : FlagStone;
            float roll = Hash01(sx, sy, j.seed + 55);
            int baseTone = roll < 0.14f ? 1 : roll < 0.62f ? 2 : 3;
            float ox = (px + 0.5f - cx) / radius, oy = (py + 0.5f - cy) / radius;
            float lit = -ox * 0.55f + oy * 0.83f; // light from the top-left (y is up here)
            int tone = baseTone;
            if (lit > 0.42f) tone = Mathf.Min(4, baseTone + 1);
            else if (lit < -0.45f) tone = Mathf.Max(0, baseTone - 1);
            if (d1 > radius - 1.5f && lit < 0.1f) tone = Mathf.Max(0, tone - 1);
            return ramp[tone];
        }

        // ---------- Cliffs: 3/4-view - rocky plateau tops + sedimentary south-facing walls ----------

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

        static bool BordersWater(Job j, int px, int py)
        {
            return j.KindAt(px - 1, py) == Water || j.KindAt(px + 1, py) == Water
                || j.KindAt(px, py - 1) == Water || j.KindAt(px, py + 1) == Water
                || j.KindAt(px - 1, py - 1) == Water || j.KindAt(px + 1, py - 1) == Water
                || j.KindAt(px - 1, py + 1) == Water || j.KindAt(px + 1, py + 1) == Water;
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

        /// <summary>Is there a water pixel within <paramref name="reach"/> steps in direction (dx,dy)?</summary>
        static bool NearWater(Job j, int px, int py, int dx, int dy, int reach)
        {
            for (int s = 1; s <= reach; s++)
            {
                byte kk = j.KindAt(px + dx * s, py + dy * s);
                if (kk == 255) return false;
                if (kk == Water) return true;
                if (kk == Cliff || kk == Deck) return false;
            }
            return false;
        }

        static int IHashB(int a, int b, int s) => ((a * 73856093) ^ (b * 19349663) ^ (s * 83492791)) & 0x7fffffff;

        static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
                (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
                (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
                255);
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
            for (int by = 0; by < j.ph; by += 6)
                for (int bx = 0; bx < j.pw; bx += 6)
                {
                    int x = bx + rng.Next(0, 6), y = by + rng.Next(0, 6);
                    double r = rng.NextDouble();
                    int i = Mathf.Clamp(y, 0, j.ph - 1) * j.pw + Mathf.Clamp(x, 0, j.pw - 1);
                    byte k = j.kind[i];
                    if (k == Grass)
                    {
                        if (r < 0.035) Flower(j, x, y);
                        else if (r < 0.34) Tuft(j, x, y, 2 + rng.Next(0, 2));
                    }
                    else if (k == Flag)
                    {
                        if (r < 0.10) Pebble(j, x, y, Flag, rng.NextDouble() < 0.5);
                        else if (r > 0.985) MossSpeck(j, x, y);
                    }
                    else if (k == Water)
                    {
                        if (r < 0.05) Ripple(j, x, y, 3 + rng.Next(0, 5));
                    }
                    else if (k == Cliff && r < 0.05)
                    {
                        PlateauDetail(j, x, y, rng);
                    }
                }
        }

        /// <summary>On a plateau top: a moss/grass tuft or a pebble clump clinging to the rock.</summary>
        static void PlateauDetail(Job j, int x, int y, System.Random rng)
        {
            // Only on plateau tops (a cliff pixel with a cliff cell above and below - not on a wall face).
            int cx = x / Px, cy = y / Px;
            if (WallDepth(j, cx, cy) <= 3) return;
            if (rng.NextDouble() < 0.5)
            {
                Set(j, x, y, Cliff, GrassC[1]);
                Set(j, x, y - 1, Cliff, GrassC[2]);
                Set(j, x + 1, y, Cliff, GrassC[2]);
            }
            else
            {
                Set(j, x, y, Cliff, PlateauC[0]);
                Set(j, x + 1, y, Cliff, PlateauC[1]);
                Set(j, x, y + 1, Cliff, PlateauC[3]);
            }
        }

        static void Tuft(Job j, int x, int y, int height)
        {
            Set(j, x + 1, y, Grass, BladeDark);
            for (int k = 1; k <= height; k++)
            {
                Set(j, x, y + k, Grass, k == height ? Blade : GrassC[3]);
                Set(j, x + 2 + (k > 1 ? 1 : 0), y + k, Grass, k == height ? Blade : GrassC[3]);
            }
            Set(j, x + 1, y + 1, Grass, GrassC[3]);
        }

        static readonly Color32[] Petals = { H("#ffe066"), H("#ff9ec4"), H("#ffffff"), H("#ff7f6a") };

        static void Flower(Job j, int x, int y)
        {
            var petal = Petals[(x * 7 + y * 3) % Petals.Length];
            var stem = BladeDark;
            Set(j, x, y - 1, Grass, stem);
            Set(j, x, y + 1, Grass, petal);
            Set(j, x - 1, y, Grass, petal);
            Set(j, x + 1, y, Grass, petal);
            Set(j, x, y, Grass, H("#ffd23f"));
        }

        static void MossSpeck(Job j, int x, int y)
        {
            Set(j, x, y, Flag, FlagMortarMoss);
            Set(j, x + 1, y, Flag, FlagMortarMoss);
            Set(j, x, y + 1, Flag, PixelCanvas.Shade(FlagMortarMoss, 1.15f));
        }

        static void Pebble(Job j, int x, int y, byte onKind, bool big)
        {
            var dark = H("#6f5a41");
            var mid = H("#94795b");
            var light = H("#b89c78");
            Set(j, x, y, onKind, dark); Set(j, x + 1, y, onKind, dark);
            Set(j, x, y + 1, onKind, mid); Set(j, x + 1, y + 1, onKind, light);
            if (big) { Set(j, x + 2, y, onKind, dark); Set(j, x + 2, y + 1, onKind, mid); Set(j, x + 1, y + 2, onKind, light); }
        }

        static void Ripple(Job j, int x, int y, int len)
        {
            int i = y * j.pw + x;
            if (y < 0 || y >= j.ph || x < 0 || x >= j.pw) return;
            // Only on open water (not the wet line / coping / shallow rim).
            byte r = j.px[i].r;
            bool onWater = r == WaterC[0].r || r == WaterC[1].r || r == WaterC[2].r || r == WaterC[3].r;
            if (!onWater) return;
            for (int k = 0; k < len; k++) Set(j, x + k, y, Water, k == 0 || k == len - 1 ? WaterC[3] : WaterC[5]);
        }
    }
}
