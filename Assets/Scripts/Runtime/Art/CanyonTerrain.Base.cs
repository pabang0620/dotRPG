using UnityEngine;

namespace DotRPG
{
    public static partial class CanyonTerrain
    {
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
    }
}
