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
    public static partial class WinterTerrain
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
            public bool fantasyField;

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
        public static Color32[] Paint(char[,] ground, int w, int h, out int pw, out int ph, System.Action<WaterField> waterReady = null, bool fantasyField=false)
        {
            var j = new Job { w = w, h = h, pw = w * Px, ph = h * Px, seed = 91,fantasyField=fantasyField };
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
            waterReady?.Invoke(WaterField.Create(j.kind, j.px, pw, ph, Water, Fall, IceSheet));
            return j.px;
        }
    }
}
