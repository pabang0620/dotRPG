using UnityEngine;

namespace DotRPG
{
    public static partial class WinterTerrain
    {
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
                    // Wobble borders so they never follow the tile grid; the river shore gets a longer,
                    // gentler wobble so its S-bends read as smooth curves.
                    const float amp = 0.2f;
                    float wu = u + amp * Noise(u * 0.85f, v * 0.85f, j.seed + 3) + 0.06f * Noise(u * 3.1f, v * 3.1f, j.seed + 5);
                    float wv = v + amp * Noise(u * 0.85f + 31.7f, v * 0.85f - 12.3f, j.seed + 4) + 0.06f * Noise(u * 3.1f + 9f, v * 3.1f, j.seed + 6);
                    Membership(j, wu, wv, weights, out byte best, out byte second, out float margin);
                    // Decks, stairs and waterfalls keep their own crisp cells (they are man-made / vertical).
                    if (own == Deck) { best = Deck; second = Water; margin = 1f; }
                    else if (best == Deck) { best = second == Deck ? Water : second; margin = 0.05f; }
                    if (own == Stairs) { best = Stairs; second = Cobble; margin = 1f; }
                    else if (best == Stairs && own != Stairs) { best = second == Stairs ? Cobble : second; margin = 0.05f; }
                    if (own == Fall) { best = Fall; second = Water; margin = 1f; }
                    else if (best == Fall && own != Fall) { best = second == Fall ? Water : second; margin = 0.05f; }
                    // A cliff cell is solid rock; keep it crisp so the snowy foot lands exactly at its edge.
                    if (own == Cliff) { best = Cliff; margin = 1f; }
                    else if (best == Cliff && own != Cliff) { best = second == Cliff ? own : second; margin = 0.1f; }
                    int i = py * j.pw + px;
                    j.kind[i] = best;
                    j.other[i] = second;
                    j.margin[i] = margin;
                }
            });
        }

        // ---------- 2. Base textures ----------

        static void PaintBase(Job j)
        {
            System.Threading.Tasks.Parallel.For(0, j.ph, py =>
            {
                for (int px = 0; px < j.pw; px++)
                {
                    int i = py * j.pw + px;
                    byte k = j.kind[i];
                    Color32 c;
                    switch (k)
                    {
                        case Cobble: c = CobbleAt(j, px, py, i); break;
                        case Cliff: c = j.fantasyField ? IceCliffAt(j,px,py,i) : CliffAt(j, px, py, i); break;
                        case Stairs: c = StairsAt(j, px, py); break;
                        case Deck: c = DeckAt(j, px, py); break;
                        case Water: c = WaterC[2]; break;
                        case Fall: c = WaterC[3]; break;
                        default: c = SnowAt(j, px, py, i); break;
                    }
                    j.px[i] = c;
                }
            });
        }

        static Color32 SnowAt(Job j, int px, int py, int i)
        {
            // Warm white with soft, wide, low-contrast drifts and a faint teal in the hollows.
            float n = Fbm(px / 40f, py / 40f, j.seed + 20);
            int tone = n < -0.34f ? 2 : n > 0.42f ? 4 : 3;
            float m = Noise(px / 7f, py / 7f, j.seed + 21);
            if (m > 0.9f) tone = Mathf.Min(4, tone + 1);
            else if (m < -0.9f) tone = Mathf.Max(1, tone - 1);
            byte other = j.other[i];
            float margin = j.margin[i];
            // Snow sits a touch lower right beside a path, deck or the water's lip: a thin teal shade line.
            if ((other == Cobble || other == Deck || other == Water) && margin < 0.09f) tone = Mathf.Max(1, tone - 1);
            // Shade cast by the cliff foot (handled again in PaintCliffFoot, but tint the approach here).
            if (other == Cliff && margin < 0.3f) tone = Mathf.Max(1, tone - (margin < 0.15f ? 2 : 1));
            return SnowC[tone];
        }

        // Round snow-dusted cobbles: a jittered grid of stone centres; each pixel belongs to the nearest.
        const int StoneCell = 13;

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
                    float fx = ix * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed) - 0.5f) * 5f + ((iy & 1) != 0 ? StoneCell * 0.5f : 0f);
                    float fy = iy * StoneCell + StoneCell * 0.5f + (Hash01(ix, iy, seed + 1) - 0.5f) * 4.5f;
                    float ex = px + 0.5f - fx, ey = py + 0.5f - fy;
                    float d = ex * ex + ey * ey;
                    if (d < d1) { d2 = d1; d1 = d; sx = ix; sy = iy; cxOut = fx; cyOut = fy; }
                    else if (d < d2) d2 = d;
                }
            d1 = Mathf.Sqrt(d1);
            d2 = Mathf.Sqrt(d2);
        }

        static Color32 CobbleAt(Job j, int px, int py, int i)
        {
            NearestStone(px, py, j.seed + 50, out float d1, out float d2, out int sx, out int sy, out float cx, out float cy);
            float radius = StoneCell * (0.5f + Hash01(sx, sy, j.seed + 52) * 0.12f);
            // Snow buried between the stones, whiter where snow lies next to the path.
            bool edge = j.margin[i] < 0.34f && j.other[i] == Snow;
            if (d2 - d1 < 1.4f || d1 > radius)
                return edge ? CobbleGapSnow : Hash01(px / 3, py / 3, j.seed + 58) < 0.35f ? CobbleGapSnow : CobbleGap;
            // A stone whose centre falls outside the path shows only its snowy shoulder.
            if (j.KindAt(Mathf.Clamp(Mathf.RoundToInt(cx), 0, j.pw - 1), Mathf.Clamp(Mathf.RoundToInt(cy), 0, j.ph - 1)) != Cobble)
                return CobbleGapSnow;
            float roll = Hash01(sx, sy, j.seed + 55);
            int baseTone = roll < 0.16f ? 1 : roll < 0.64f ? 2 : 3;
            float ox = (px + 0.5f - cx) / radius, oy = (py + 0.5f - cy) / radius;
            float lit = -ox * 0.55f + oy * 0.83f;   // light from the top-left (y is up here)
            int tone = baseTone;
            if (lit > 0.42f) tone = Mathf.Min(4, baseTone + 1);
            else if (lit < -0.45f) tone = Mathf.Max(0, baseTone - 1);
            if (d1 > radius - 1.3f && lit < 0.1f) tone = Mathf.Max(0, tone - 1);
            // A little snow cap on the up-light side of some stones.
            if (Hash01(sx, sy, j.seed + 56) < 0.3f && oy < -0.35f) return SnowC[4];
            return CobbleC[tone];
        }

        static Color32 IceCliffAt(Job j,int x,int y,int i){
            // Larger fractured masses preserve the original ledge lighting, with a glacial colour ramp.
            var rock=CliffAt(j,x/3,y/3,i);
            int r=Mathf.RoundToInt(rock.r*.67f),g=Mathf.RoundToInt(rock.g*.91f+8),b=Mathf.RoundToInt(rock.b*.98f+16);
            float vein=Noise(x/75f,y/170f,557);
            if(vein>.72f){r+=22;g+=29;b+=25;}
            return new Color32((byte)Mathf.Clamp(r,0,255),(byte)Mathf.Clamp(g,0,255),(byte)Mathf.Clamp(b,0,255),255);
        }

        static Color32 CliffAt(Job j, int px, int py, int i)
        {
            // A low grey-violet rock face in 3/4 view, lit from the top-left, built from stacked rounded
            // ledge/block volumes: each block is lighter on its top-left shoulder and darker along its lower
            // right, so the wall has depth instead of reading flat. Thin muted beige strata drift across it.
            float frac = j.cliffFrac[i];                       // 0 at the top edge, 1 at the foot
            // Overall body: lighter under the snow lip, markedly darker toward the shaded foot.
            float shade = 1.08f - frac * 0.34f;
            float grain = Noise(px / 7f, py / 9f, j.seed + 70);
            var baseC = grain > 0.35f ? CliffPL : grain < -0.4f ? CliffPD : CliffP;
            var c = PixelCanvas.Shade(baseC, shade);

            // Irregular layered rock. Rows are horizontal strata whose heights wobble and drift across the
            // face (uneven, never a ruled grid); within each stratum the rock is broken into slabs of varied
            // width by staggered vertical joints, and the odd wider boulder spans two rows.
            // 1) Which stratum (layer) this pixel is in - cumulative wobbling row heights.
            float rowWob = 2.5f * Noise(px / 34f, py / 40f, j.seed + 85);
            float layerH = 13f + 5f * Hash01(0, Mathf.FloorToInt(py / 15f), j.seed + 92);   // rows 13..18 px tall, varying
            float layerF = (py + rowWob) / layerH + 0.5f * Noise(px / 40f, 0f, j.seed + 86);
            int layer = Mathf.FloorToInt(layerF);
            float inLayer = layerF - layer;                    // 0 at the layer top .. 1 at its foot
            // 2) Slabs across this layer: cell width varies per layer, joints staggered per layer.
            float slabW = 16f + 10f * Hash01(0, layer, j.seed + 87);         // 16..26 px, different per layer
            float phase = Hash01(1, layer, j.seed + 88) * slabW;            // horizontal stagger per layer
            float jointWob = 3f * Noise(py / 6f, layer * 3f, j.seed + 89);
            float slabF = (px + phase + jointWob) / slabW;
            int slab = Mathf.FloorToInt(slabF);
            float inSlab = slabF - slab;                       // 0 at the slab left .. 1 at its right
            // 3) An occasional bigger boulder: merge some slabs, sit a touch prouder (lighter).
            bool boulder = Hash01(slab, layer, j.seed + 90) > 0.82f;

            // Rounded lit/shaded edges of each slab (top-left catches light, bottom-right in shadow).
            if (inLayer < 0.14f || inSlab < 0.12f) c = PixelCanvas.Shade(CliffPL, shade + (boulder ? 0.08f : 0.04f));
            else if (inLayer > 0.9f || inSlab > 0.9f) c = PixelCanvas.Shade(CliffPDD, shade);      // joint in shadow
            else if (inSlab > 0.74f) c = PixelCanvas.Shade(CliffPD, shade);                         // right shoulder
            else if (boulder) c = PixelCanvas.Shade(CliffPL, shade + 0.03f);

            // Beige strata seams running ALONG the layers (a soft muted band near some layer tops).
            float seamRoll = Hash01(2, layer, j.seed + 91);
            if (seamRoll > 0.6f && inLayer < 0.16f)
                c = PixelCanvas.Shade(WMix(CliffBe, CliffP, 0.4f), shade);
            else if (seamRoll > 0.6f && inLayer < 0.24f)
                c = PixelCanvas.Shade(CliffBeD, shade);

            return c;
        }
    }
}
