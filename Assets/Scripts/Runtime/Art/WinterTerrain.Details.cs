using UnityEngine;

namespace DotRPG
{
    public static partial class WinterTerrain
    {
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
