using UnityEngine;

namespace DotRPG
{
    public static partial class CanyonTerrain
    {
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
