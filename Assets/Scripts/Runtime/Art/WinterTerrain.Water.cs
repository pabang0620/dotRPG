using UnityEngine;

namespace DotRPG
{
    public static partial class WinterTerrain
    {
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
    }
}
