using UnityEngine;

namespace DotRPG
{
    public static partial class TownTerrain
    {
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
