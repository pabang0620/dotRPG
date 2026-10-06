using UnityEngine;

namespace DotRPG
{
    /// <summary>Bishop clips: gold holy light, feathers, flowers, a bell and wings.</summary>
    public static partial class VfxArt
    {
        static readonly Color32[] Holy = { Hex("#ffffff"), Hex("#fffbe4"), Hex("#fff0b0"), Hex("#ffdc78"), Hex("#f4b848"), Hex("#c8862a"), Hex("#8a5418") };
        static readonly Color32[] Life = { Hex("#ffffff"), Hex("#f0ffe0"), Hex("#c8f8a8"), Hex("#90e878"), Hex("#58c058"), Hex("#2e8a3e") };
        static readonly Color32[] Petal = { Hex("#ffffff"), Hex("#fff4f8"), Hex("#ffd8e8"), Hex("#ffb8d4"), Hex("#f090b8"), Hex("#c86090") };

        /// <summary>A feather at an angle (deg) with a width squash, white/gold, centred at (cx, cy).</summary>
        static void FeatherAt(PixelCanvas c, float cx, float cy, float len, float width, float deg, float squash, Color32[] ramp)
        {
            float ca = Mathf.Cos(deg * Mathf.Deg2Rad), sa = Mathf.Sin(deg * Mathf.Deg2Rad);
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float px = x + 0.5f - cx, py = cy - (y + 0.5f);
                    float u = px * ca + py * sa;              // along the shaft (+ = tip)
                    float v = -px * sa + py * ca;             // across
                    float t = u / len + 0.5f;                 // 0 quill .. 1 tip
                    if (t < 0f || t > 1f) continue;
                    float half = Mathf.Sin(Mathf.Clamp01(t * 1.05f) * Mathf.PI * 0.95f) * width * (v > 0 ? 0.65f : 1f) * squash;
                    if (t < 0.12f) half = 0.8f;
                    if (Mathf.Abs(v) > half + 0.3f) continue;
                    float barb = Mathf.Repeat(u + Mathf.Abs(v) * 0.8f, 3.2f);
                    Color32 col = Mathf.Abs(v) < 0.9f ? ramp[0] : barb < 1f ? ramp[3] : ramp[Mathf.Abs(v) > half - 1.2f ? 2 : 1];
                    Put(c, x, y, col);
                }
        }

        static PixelCanvas FeatherFlutter(int i)
        {
            // A feather drifting through the air: it rocks and turns its face (looped).
            var c = C(48, 56);
            float rock = Mathf.Sin(i / 6f * Mathf.PI * 2f);
            FeatherAt(c, 24f, 28f, 44f, 9f, 70f + rock * 25f, 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(i / 6f * Mathf.PI)), Holy);
            return c;
        }

        static PixelCanvas HealRise(int i)
        {
            // Healing lands: a soft green-gold ring at the feet and little crosses floating up (pivot at the feet).
            var c = C(80, 112);
            float t = i / 8f;
            if (i < 6) Ring(c, 40f, 104f, 8f + i * 5f, 2.2f, A(Life[2], 1f - i * 0.15f), 0.4f);
            for (int k = 0; k < 7; k++)
            {
                float start = Hash(k, 0, 61) * 0.4f;
                float u = (t - start) / 0.6f;
                if (u < 0f || u > 1f) continue;
                float x = 14f + Hash(k, 1, 61) * 52f + Mathf.Sin(u * 6f + k) * 3f;
                float y = 100f - u * 84f;
                int s = u < 0.8f ? 3 : 2;
                var col = k % 3 == 0 ? Holy[2] : Life[2];
                for (int d = -s; d <= s; d++) { Put(c, (int)x + d, (int)y, col); Put(c, (int)x, (int)y + d, col); }
                Put(c, (int)x, (int)y, Life[0]);
            }
            return c.WithPivot(40f, 8f);
        }

        static PixelCanvas Lotus(int i)
        {
            // 생명의 파문 ground flower (drawn round, squashed by the player): eight petals open, then the heart glows.
            var c = C(256, 256);
            float open = Mathf.Min(1f, (i + 1) / 7f);
            for (int layer = 0; layer < 2; layer++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 45f + layer * 22.5f;
                    float len = (layer == 0 ? 92f : 64f) * EaseOut(open);
                    float wid = layer == 0 ? 26f : 20f;
                    for (int y = 0; y < 256; y++)
                        for (int x = 0; x < 256; x++)
                        {
                            float px = x + 0.5f - 128f, py = 128f - (y + 0.5f);
                            float ca = Mathf.Cos(a * Mathf.Deg2Rad), sa = Mathf.Sin(a * Mathf.Deg2Rad);
                            float u = (px * ca + py * sa) / Mathf.Max(1f, len), v = -px * sa + py * ca;
                            if (u < 0.1f || u > 1f) continue;
                            float half = Mathf.Sin(u * Mathf.PI) * wid * (u > 0.75f ? (1f - u) / 0.25f * 0.8f + 0.2f : 1f);
                            if (Mathf.Abs(v) > half) continue;
                            bool edge = Mathf.Abs(v) > half - 2f;
                            Put(c, x, y, edge ? Petal[layer == 0 ? 4 : 3] : Pick(Petal, 0.1f + Mathf.Abs(v) / half * 0.4f + (1f - u) * 0.2f + layer * 0.15f, x, y));
                        }
                }
            float heart = i >= 7 ? 18f + (i - 7) * 4f : 14f * open;
            Dot(c, 128f, 128f, heart, Holy[2]);
            Dot(c, 128f, 128f, heart * 0.55f, Holy[0]);
            if (i >= 7) Ring(c, 128f, 128f, 100f + (i - 7) * 10f, 2f, A(Life[2], 1f - (i - 7) * 0.3f));
            return c;
        }

        static PixelCanvas Bell(int i)
        {
            // 정화의 종: a bell of light swings and rings; sound arcs leave both sides on each swing.
            var c = C(128, 128);
            float swing = Mathf.Sin(i / 10f * Mathf.PI * 2f) * 16f;
            float ca = Mathf.Cos(swing * Mathf.Deg2Rad), sa = Mathf.Sin(swing * Mathf.Deg2Rad);
            const float px0 = 64f, py0 = 22f;   // hanging point
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float dx = x + 0.5f - px0, dy = y + 0.5f - py0;
                    float lx = dx * ca + dy * sa, ly = -dx * sa + dy * ca;   // bell-local (ly down)
                    if (ly < 4f || ly > 70f) continue;
                    float u = (ly - 4f) / 66f;
                    float half = 9f + 15f * Mathf.Pow(u, 1.6f) + (u > 0.85f ? 6f * (u - 0.85f) / 0.15f : 0f);
                    if (Mathf.Abs(lx) > half) continue;
                    bool rim = u > 0.9f || Mathf.Abs(lx) > half - 2f;
                    float lit = 0.5f - lx / (half * 2f) * 0.9f;
                    Put(c, x, y, rim ? Holy[3] : Pick(Holy, 0.15f + (1f - lit) * 0.6f, x, y));
                }
            Dot(c, px0 + sa * 74f * -1f + 0f, py0 + ca * 74f, 4f, Holy[2]);   // clapper
            Dot(c, px0, py0, 3f, Holy[4]);
            int phase = i % 5;
            for (int k = 0; k < 2; k++)
            {
                float r = 20f + phase * 9f + k * 12f;
                for (int side = -1; side <= 1; side += 2)
                    for (float a = -35f; a <= 35f; a += 1.5f)
                    {
                        var p = At(64f, 70f, (side < 0 ? 180f : 0f) + a, 44f + r);
                        if (On(1f - phase / 5f, (int)p.x, (int)p.y)) Put(c, (int)p.x, (int)p.y, Holy[1]);
                    }
            }
            return c;
        }

        static PixelCanvas LightSpear(int i)
        {
            // 심판의 광창: a long lance of light (point to the right), pulsing tip, sparks trailing (looped).
            var c = C(176, 48);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 176; x++)
                {
                    float u = x / 176f;
                    float half = u < 0.78f ? 2.5f + u * 4f : 5.6f * (1f - (u - 0.78f) / 0.22f) + 0.3f;
                    if (u > 0.68f && u < 0.78f) half += 3f * Mathf.Sin((u - 0.68f) / 0.1f * Mathf.PI);
                    float dy = Mathf.Abs(y + 0.5f - 24f);
                    if (dy < half) Put(c, x, y, dy < half * 0.4f ? Holy[0] : Pick(Holy, 0.2f + dy / half * 0.4f + (1f - u) * 0.3f, x, y));
                    else if (dy < half + 4f && On((0.5f - (dy - half) / 8f) * u, x, y)) Put(c, x, y, A(Holy[3], 0.8f));
                }
            float pulse = i % 2 == 0 ? 9f : 6f;
            Twinkle(c, 164f, 24f, pulse * 0.8f, Holy[0]);
            for (int k = 0; k < 5; k++) Twinkle(c, 10f + Hash(k, i, 63) * 60f, 24f + (Hash(k, i + 4, 63) - 0.5f) * 18f, 1.6f, Holy[2]);
            return c.WithPivot(120f, 24f);
        }

        static PixelCanvas HolyCrossHit(int i)
        {
            // Holy contact: a gold cross of light bursts, thins out and leaves sparkles.
            var c = C(64, 72);
            float[] arm = { 12f, 28f, 26f, 18f, 10f, 0f };
            float[] wid = { 3f, 5f, 4f, 2.5f, 1.5f, 0f };
            if (arm[i] > 0f)
                for (int y = 0; y < 72; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float dx = Mathf.Abs(x + 0.5f - 32f), dy = y + 0.5f - 32f;
                        bool v = dx < wid[i] * (1f - Mathf.Abs(dy) / (dy > 0 ? arm[i] * 1.3f : arm[i])) + 0.6f && Mathf.Abs(dy) < (dy > 0 ? arm[i] * 1.3f : arm[i]);
                        bool h = Mathf.Abs(dy) < wid[i] * (1f - dx / arm[i]) + 0.6f && dx < arm[i];
                        if (v || h) Put(c, x, y, dx < 1.2f || Mathf.Abs(dy) < 1.2f ? Holy[0] : Holy[2]);
                    }
            if (i >= 2)
                for (int k = 0; k < 6; k++)
                {
                    var p = At(32f, 32f, k * 60f + 30f, 8f + i * 4f);
                    Twinkle(c, p.x, p.y, i < 5 ? 2f : 1f, Holy[1]);
                }
            return c;
        }

        static PixelCanvas Wings(int i)
        {
            // 천사의 품 / 천상의 행진: a pair of wings spreads sideways and up from the back (shoulders at the centre),
            // long primaries low and outward, coverts shorter and higher; they open, hold and settle a little.
            var c = C(256, 160);
            float[] open = { 0.05f, 0.3f, 0.6f, 0.88f, 1f, 1f, 0.94f, 0.85f };
            float o = open[i];
            const float sx = 128f, sy = 92f;
            for (int side = -1; side <= 1; side += 2)
                for (int row = 2; row >= 0; row--)
                {
                    int n = 6 - row;
                    for (int k = n - 1; k >= 0; k--)
                    {
                        float spread = Mathf.Lerp(-60f, -8f + k * (78f / n) + row * 10f, o);   // degrees above horizontal
                        float a = side > 0 ? spread : 180f - spread;
                        float len = (row == 0 ? 104f : row == 1 ? 74f : 46f) * (1f - k * 0.06f) * (0.6f + 0.4f * o);
                        var root = new Vector2(sx + side * (4f + k * 2f), sy - row * 5f);
                        var mid = At(root.x, root.y, a, len * 0.5f);
                        FeatherAt(c, mid.x, mid.y, len, 13f + row, a, 1f, row == 0 ? Holy : new[] { Holy[0], Holy[1], Holy[1], Holy[2] });
                    }
                }
            return c;
        }

        static PixelCanvas LightPillar(int i)
        {
            // A column of light comes down from the sky, widens with sparkles and fades (pivot at the ground).
            var c = C(96, 288);
            float[] reach = { 90f, 190f, 280f, 280f, 280f, 280f, 280f, 280f };
            float[] wid = { 5f, 7f, 10f, 16f, 18f, 14f, 9f, 4f };
            float bottom = 280f;
            for (int y = 0; y < 288; y++)
                for (int x = 0; x < 96; x++)
                {
                    float fromTop = y + 0.5f;
                    if (fromTop > reach[i]) continue;
                    float dx = Mathf.Abs(x + 0.5f - 48f);
                    float w = wid[i] * (0.7f + 0.3f * fromTop / bottom);
                    if (dx < w * 0.35f) Put(c, x, y, Holy[0]);
                    else if (dx < w) Put(c, x, y, Pick(Holy, 0.2f + dx / w * 0.5f, x, y));
                    else if (dx < w + 8f && On(0.4f * (1f - (dx - w) / 8f), x, y)) Put(c, x, y, A(Holy[3], 0.8f));
                }
            if (i >= 2 && i <= 6) Ring(c, 48f, 280f, 10f + (i - 2) * 8f, 2.5f, A(Holy[1], 1f - (i - 2) * 0.2f), 0.35f);
            if (i >= 3)
                for (int k = 0; k < 8; k++)
                    Twinkle(c, 48f + (Hash(k, i, 67) - 0.5f) * 40f, 40f + Hash(k, i + 3, 67) * 230f, 2f, Holy[1]);
            return c.WithPivot(48f, 8f);
        }
    }
}
