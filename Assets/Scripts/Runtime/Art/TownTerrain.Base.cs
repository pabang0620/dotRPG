using UnityEngine;

namespace DotRPG
{
    public static partial class TownTerrain
    {
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
    }
}
