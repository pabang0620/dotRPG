using UnityEngine;

namespace DotRPG
{
    /// <summary>Fighter clips: steel-blue sword energy with white cutting edges.</summary>
    public static partial class VfxArt
    {
        static readonly Color32[] Steel = { Hex("#ffffff"), Hex("#e4f4ff"), Hex("#b4dcff"), Hex("#7cbcff"), Hex("#4a8cf0"), Hex("#2a5cc8"), Hex("#1a3a8a") };

        /// <summary>
        /// One frame of a sweeping sword arc around (cx, cy): a crescent smear covering the angles from <paramref name="head"/>
        /// (the blade tip, sharp) back to <paramref name="tail"/>. White cutting edge on the outside, blue body fading toward
        /// the inside and toward the tail, a faint outer glow, and a dissolve as <paramref name="fade"/> drops.
        /// </summary>
        static void SwordArc(PixelCanvas c, float cx, float cy, float R, float head, float tail, float fade, float thick, int seed)
        {
            float span = Mathf.Max(1f, tail - head);
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float d = Polar(x, y, cx, cy, out float ang);
                    if (ang < head || ang > tail) continue;
                    float s = Mathf.Clamp01((ang - head) / span);           // 0 tip .. 1 tail
                    float tipRamp = Mathf.Clamp01((ang - head) / 9f);       // sharp point at the tip
                    float band = thick * Mathf.Sqrt(tipRamp) * (1f - 0.72f * s);
                    float keep = fade - s * 0.45f + (Hash(x, y, seed) - 0.5f) * 0.2f;
                    if (keep <= 0f) continue;
                    if (d <= R + 1.5f && d >= R - band)
                    {
                        float u = (R + 1.5f - d) / Mathf.Max(1f, band + 1.5f);   // 0 edge .. 1 inner
                        float cover = (u < 0.45f ? 1f : Mathf.Pow((1f - u) / 0.55f, 0.7f)) * Mathf.Min(1f, keep * 1.5f);
                        if (u > 0.1f && !On(cover, x, y)) continue;
                        Put(c, x, y, u < 0.1f ? Steel[0] : Pick(Steel, 0.12f + u * 0.85f + s * 0.25f, x, y));
                    }
                    else if (d > R + 1.5f && d < R + 5f && s < 0.6f && On((1f - s) * 0.5f * keep, x, y)) Put(c, x, y, A(Steel[2], 0.8f));
                }
            if (fade > 0.95f)
            {
                var a = At(cx, cy, head + 1f, R - 1f); var b = At(cx, cy, head + 14f, R - 1f);
                Line(c, a.x, a.y, b.x, b.y, 1.2f, Steel[0]);
                for (int k = 0; k < 3; k++)
                {
                    var p = At(cx, cy, head + 4f + k * 10f, R + 4f + Hash(k, seed, 3) * 5f);
                    Twinkle(c, p.x, p.y, 2f, Steel[1]);
                }
            }
        }

        static PixelCanvas FighterArc(int i, int n)
        {
            // Downward cut in front of the body: the tip travels from high front (70 deg) to low front (-75 deg).
            var c = C(192, 192);
            float[] head = { 40f, 0f, -45f, -75f, -75f, -75f, -75f };
            float[] tail = { 78f, 78f, 72f, 45f, 5f, -35f, -60f };
            float[] fade = { 1f, 1f, 1f, 1f, 0.8f, 0.55f, 0.3f };
            SwordArc(c, 96f, 96f, 80f, head[i], tail[i], fade[i], 46f, 11);
            return c;
        }

        static PixelCanvas FighterVerticalSlash(int i)
        {
            // A huge overhead cut: the arc centre is behind the swing, the tip falls from above onto the impact point
            // (pivot at the impact point near the bottom right).
            var c = C(208, 272);
            float[] head = { 62f, 40f, 18f, 2f, 2f, 2f, 2f, 2f };
            float[] tail = { 88f, 88f, 86f, 74f, 52f, 30f, 14f, 6f };
            float[] fade = { 1f, 1f, 1f, 1f, 0.85f, 0.6f, 0.4f, 0.2f };
            SwordArc(c, 24f, 262f, 150f, head[i], tail[i], fade[i], 64f, 17);
            float ix = 24f + 150f, iy = 262f - 4f;
            if (i >= 3 && i <= 6)
            {
                float r = 10f + (i - 3) * 9f;
                Ring(c, ix, iy, r, 3f, A(Steel[1], 1f - (i - 3) * 0.25f), 0.45f);
                for (int k = 0; k < 7; k++)
                {
                    var p = At(ix, iy, 15f + k * 25f, r + 8f + Hash(k, i, 5) * 12f);
                    Line(c, ix, iy - 2f, p.x, p.y, 0.8f, A(Steel[2], 0.9f));
                }
            }
            return c.WithPivot(ix, 272f - iy);
        }

        static PixelCanvas FighterX(int i)
        {
            // Hit mark: a first cut, then a crossing cut, a flash at the crossing, then fragments.
            var c = C(64, 64);
            void Cut(float x0, float y0, float x1, float y1, float w, float fade)
            {
                float len = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
                var d = new Vector2(x1 - x0, y1 - y0) / len;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float px = x + 0.5f - x0, py = y + 0.5f - y0;
                        float along = px * d.x + py * d.y;
                        if (along < 0f || along > len) continue;
                        float side = Mathf.Abs(px * -d.y + py * d.x);
                        float half = w * Mathf.Pow(Mathf.Sin(along / len * Mathf.PI), 0.7f);
                        if (side > half + 2f) continue;
                        float u = side / Mathf.Max(0.5f, half);
                        if (!On(fade * 1.3f, x, y)) continue;
                        Put(c, x, y, side > half ? A(Steel[4], 0.7f) : Pick(Steel, u * 0.6f, x, y));
                    }
            }
            float[] w1 = { 2f, 6f, 4f, 2f, 0f, 0f };
            float[] w2 = { 0f, 0f, 6f, 4f, 2f, 0f };
            if (w1[i] > 0f) Cut(8f, 12f, 56f, 52f, w1[i], i == 3 ? 0.6f : 1f);
            if (w2[i] > 0f) Cut(8f, 52f, 56f, 12f, w2[i], i == 4 ? 0.6f : 1f);
            if (i == 2 || i == 3) Dot(c, 32f, 32f, i == 2 ? 7f : 4f, Steel[0]);
            if (i >= 3)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 45f + 20f;
                    var p = At(32f, 32f, a, 10f + (i - 3) * 8f + Hash(k, i, 7) * 4f);
                    Dot(c, p.x, p.y, i == 5 ? 0.8f : 1.4f, Steel[i == 5 ? 3 : 1]);
                }
            return c;
        }

        static PixelCanvas FighterWave(int i)
        {
            // Flying crescent of sword energy (bulges to the right), shimmering edge and moving inner streaks.
            var c = C(96, 160);
            for (int y = 0; y < 160; y++)
                for (int x = 0; x < 96; x++)
                {
                    float ox = (x + 0.5f - 18f) / 74f, oy = (y + 0.5f - 80f) / 78f;
                    float ix = (x + 0.5f - 4f) / 64f, iy = (y + 0.5f - 80f) / 68f;
                    float o = ox * ox + oy * oy;
                    if (o > 1f || ix * ix + iy * iy <= 1f) continue;
                    float depth = 1f - Mathf.Sqrt(o);
                    float tip = Mathf.Abs(y + 0.5f - 80f) / 80f;
                    float a = tip > 0.7f ? 1f - (tip - 0.7f) / 0.3f : 1f;
                    float streak = Mathf.Repeat(y * 0.35f + i * 2.5f, 7f) < 1.4f ? 0.55f : 1f;
                    if (!On(a, x, y)) continue;
                    Put(c, x, y, depth < 0.035f ? Steel[0] : Pick(Steel, depth * 3.2f * streak + 0.1f, x, y));
                }
            for (int k = 0; k < 4; k++)
            {
                float t = Mathf.Repeat(Hash(k, i, 9) + i * 0.13f, 1f);
                float yy = 20f + t * 120f;
                float ox = 18f + 74f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((yy - 80f) / 78f, 2f)));
                Twinkle(c, ox - 2f, yy, 2.5f, Steel[1]);
            }
            return c;
        }

        static PixelCanvas FighterLine(int i)
        {
            // 일섬: a hairline flickers, then a blade of light splits the screen and breaks apart (pivot at the start).
            var c = C(384, 48);
            float[] core = { 1f, 5f, 9f, 4f, 0f };
            float[] glow = { 0f, 12f, 20f, 10f, 6f };
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 384; x++)
                {
                    float dy = Mathf.Abs(y + 0.5f - 24f);
                    float u = x / 384f;
                    float taper = Mathf.Min(1f, u * 12f) * Mathf.Min(1f, (1f - u) * 6f);
                    bool broken = i >= 3 && Noise(x * 0.06f, i, 21) < (i == 3 ? 0.35f : 0.7f);
                    if (broken) continue;
                    if (i == 0) { if (dy < 1f && (x / 6) % 2 == 0) Put(c, x, y, Steel[1]); continue; }
                    if (dy < core[i] * 0.5f * taper) Put(c, x, y, dy < core[i] * 0.2f ? Steel[0] : Steel[1]);
                    else if (dy < glow[i] * 0.5f * taper && On(0.55f * (1f - dy / (glow[i] * 0.5f)), x, y)) Put(c, x, y, A(Steel[3], 0.85f));
                }
            if (i == 2 || i == 3)
                for (int k = 0; k < 14; k++)
                {
                    float x = Hash(k, i, 23) * 380f;
                    Twinkle(c, x, 24f + (Hash(k, i, 29) - 0.5f) * 30f, 2f, Steel[1]);
                }
            return c.WithPivot(0f, 24f);
        }

        static PixelCanvas FighterCut(int i)
        {
            // A delayed cut mark on a monster: a single slash that flares and breaks into sparks.
            var c = C(64, 64);
            float[] w = { 1.5f, 5f, 6f, 3f, 0f };
            if (w[i] > 0f)
            {
                var a = new Vector2(10f, 50f); var b = new Vector2(54f, 14f);
                float len = Vector2.Distance(a, b); var d = (b - a) / len;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float px = x + 0.5f - a.x, py = y + 0.5f - a.y;
                        float along = px * d.x + py * d.y;
                        if (along < 0 || along > len) continue;
                        float side = Mathf.Abs(px * -d.y + py * d.x);
                        float half = w[i] * Mathf.Pow(Mathf.Sin(along / len * Mathf.PI), 0.6f);
                        if (side <= half) Put(c, x, y, Pick(Steel, side / Mathf.Max(0.5f, half) * 0.5f + (i == 3 ? 0.3f : 0f), x, y));
                        else if (side <= half + 3f && i <= 2 && On(0.4f, x, y)) Put(c, x, y, A(Steel[4], 0.7f));
                    }
            }
            if (i >= 2)
                for (int k = 0; k < 6; k++)
                {
                    float t = Hash(k, 1, 31);
                    var p = Vector2.Lerp(new Vector2(10f, 50f), new Vector2(54f, 14f), t);
                    p += new Vector2(Hash(k, 2, 31) - 0.5f, Hash(k, 3, 31) - 0.5f) * (6f + (i - 2) * 8f);
                    Dot(c, p.x, p.y, i == 4 ? 0.8f : 1.3f, Steel[i == 4 ? 3 : 1]);
                }
            return c;
        }

        static PixelCanvas FighterShatter(int i)
        {
            // 파쇄 표식: glass-like cracks spread over the monster, then shards pop out.
            var c = C(64, 64);
            float grow = Mathf.Min(1f, (i + 1) / 4f);
            float fade = i < 4 ? 1f : 1f - (i - 3) / 4f;
            for (int k = 0; k < 7; k++)
            {
                float a = k * 51.4f + Hash(k, 0, 41) * 20f;
                float len = (14f + Hash(k, 1, 41) * 12f) * grow;
                var mid = At(32f, 32f, a + (Hash(k, 2, 41) - 0.5f) * 30f, len * 0.5f);
                var end = At(32f, 32f, a, len);
                if (On(fade * 1.2f, k, 0) || fade > 0.5f)
                {
                    Line(c, 32f, 32f, mid.x, mid.y, 0.9f, A(Steel[0], fade));
                    Line(c, mid.x, mid.y, end.x, end.y, 0.6f, A(Steel[2], fade));
                    if (grow >= 1f) { var br = At(mid.x, mid.y, a + 50f, 6f); Line(c, mid.x, mid.y, br.x, br.y, 0.5f, A(Steel[3], fade)); }
                }
            }
            Dot(c, 32f, 32f, 2.5f * fade + 0.5f, Steel[0]);
            if (i >= 4)
                for (int k = 0; k < 9; k++)
                {
                    float a = k * 40f + 10f;
                    var p = At(32f, 32f, a, 12f + (i - 3) * 6f);
                    var q = At(p.x, p.y, a + 90f, 2.5f);
                    Line(c, p.x, p.y, q.x, q.y, 0.9f, A(Steel[1], fade + 0.2f));
                }
            return c;
        }

        static PixelCanvas FighterSwordSpark(int i)
        {
            // A falling sword hits: a vertical spike of light, a flat light ring and flying sparks (pivot at the ground).
            var c = C(96, 128);
            float t = i / 5f;
            float spikeH = i < 2 ? 100f : 100f * (1f - (i - 1) / 5f);
            float spikeW = i < 2 ? 5f : 3f;
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 96; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 48f), h = 116f - (y + 0.5f);
                    if (h < 0f || h > spikeH) continue;
                    float w = spikeW * (1f - h / spikeH);
                    if (dx < w) Put(c, x, y, dx < w * 0.4f ? Steel[0] : Steel[2]);
                    else if (dx < w + 4f && On(0.4f * (1f - h / spikeH), x, y)) Put(c, x, y, A(Steel[3], 0.8f));
                }
            if (i >= 1) Ring(c, 48f, 116f, 8f + i * 7f, 2.5f, A(Steel[1], 1f - t * 0.8f), 0.4f);
            for (int k = 0; k < 8; k++)
            {
                float a = 15f + k * 21f;
                var p = At(48f, 116f, a, 6f + i * 8f + Hash(k, 0, 51) * 6f);
                if (i > 0) Dot(c, p.x, p.y, i > 4 ? 0.8f : 1.3f, Steel[i > 3 ? 3 : 1]);
            }
            return c.WithPivot(48f, 12f);
        }

        static PixelCanvas HeavyImpact(int i)
        {
            // Heavy ground impact (white, tinted by the caller; pivot centre, drawn as seen on the ground):
            // a flash, an expanding shock ring, dust puffs riding the ring and flying debris.
            var c = C(256, 160);
            float t = i / 8f;
            float r = 18f + EaseOut(t) * 100f;
            if (i == 0) Dot(c, 128f, 80f, 26f, Grey(1f, 0.9f));
            for (int y = 0; y < 160; y++)
                for (int x = 0; x < 256; x++)
                {
                    float dx = x + 0.5f - 128f, dy = (y + 0.5f - 80f) / 0.6f;
                    float dd = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    float w = Mathf.Lerp(10f, 2.5f, t);
                    if (dd > 1f || dd < -w * 2.2f) continue;
                    if (dd > -w) Put(c, x, y, Grey(dd > -w * 0.4f ? 1f : 0.88f, 1f - t * 0.6f));
                    else if (On((1f - (-dd - w) / (w * 1.2f)) * 0.5f * (1f - t), x, y)) Put(c, x, y, Grey(0.8f, 0.7f));
                }
            if (i >= 1) Ring(c, 128f, 80f, r * 0.72f, Mathf.Lerp(4f, 1f, t), Grey(0.9f, 0.6f - t * 0.5f), 0.6f);
            for (int k = 0; k < 14; k++)
            {
                float a = k * 25.7f + Hash(k, 0, 61) * 12f;
                var p = new Vector2(128f + Mathf.Cos(a * Mathf.Deg2Rad) * r, 80f - Mathf.Sin(a * Mathf.Deg2Rad) * r * 0.6f);
                float pr = (5f + Hash(k, 1, 61) * 4f) * (1f - t * 0.6f);
                for (int y = Mathf.FloorToInt(p.y - pr); y <= p.y + pr; y++)
                    for (int x = Mathf.FloorToInt(p.x - pr); x <= p.x + pr; x++)
                    {
                        float dd = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), p) / pr;
                        if (dd > 1f) continue;
                        if (On((1f - dd) * (1f - t) * 1.3f, x, y)) Put(c, x, y, Grey(dd < 0.5f ? 0.92f : 0.78f, 0.85f));
                    }
            }
            for (int k = 0; k < 10; k++)
            {
                float a = 10f + k * 16f;
                float dist = 10f + t * (60f + Hash(k, 2, 61) * 40f);
                float lift = Mathf.Sin(Mathf.Min(1f, t * 1.3f) * Mathf.PI) * (14f + Hash(k, 3, 61) * 16f);
                var p = new Vector2(128f + Mathf.Cos(a * Mathf.Deg2Rad) * dist, 80f - Mathf.Sin(a * Mathf.Deg2Rad) * dist * 0.6f - lift);
                if (i < 8) Dot(c, p.x, p.y, 1.6f, Grey(0.7f, 1f));
            }
            return c;
        }
    }
}
