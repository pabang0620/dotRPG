using UnityEngine;

namespace DotRPG
{
    /// <summary>Arcanist clips: fire, ice, lightning and violet arcane force.</summary>
    public static partial class VfxArt
    {
        static readonly Color32[] Fire = { Hex("#ffffff"), Hex("#fff6c8"), Hex("#ffe070"), Hex("#ffb030"), Hex("#ff7a1c"), Hex("#e8401c"), Hex("#a82010"), Hex("#5a1408") };
        static readonly Color32[] Smoke = { Hex("#8a7c74"), Hex("#6a5e58"), Hex("#4a423e"), Hex("#2e2a28") };
        static readonly Color32[] Ice = { Hex("#ffffff"), Hex("#e4f8ff"), Hex("#b8e8ff"), Hex("#84ccff"), Hex("#5aa0f0"), Hex("#3a70d0"), Hex("#22448a") };
        static readonly Color32[] Arcane = { Hex("#ffffff"), Hex("#f0e4ff"), Hex("#d4bcff"), Hex("#b08cff"), Hex("#8a5cf0"), Hex("#6034c8"), Hex("#3a1a88"), Hex("#1e0c48") };
        static readonly Color32[] Volt = { Hex("#ffffff"), Hex("#e8fcff"), Hex("#9ff0ff"), Hex("#5ad4ff"), Hex("#8a7cff") };

        static PixelCanvas Fireball(int i)
        {
            // Flaming orb flying right: a white-hot core, tongues of flame licking backwards, flickering every frame.
            var c = C(112, 80);
            const float cx = 76f, cy = 40f;
            for (int y = 0; y < 80; y++)
                for (int x = 0; x < 112; x++)
                {
                    float d = Polar(x, y, cx, cy, out float ang);
                    float back = Mathf.Clamp01((Mathf.Abs(ang) - 90f) / 90f);     // 0 front .. 1 straight behind
                    float tongue = Noise(ang * 0.07f + i * 0.9f, d * 0.08f - i * 0.6f, 31);
                    float r = 17f + back * back * (40f + 18f * tongue) + (1f - back) * 3f * tongue;
                    if (d > r) continue;
                    float t = d / r;
                    t += back * 0.25f + (tongue - 0.5f) * 0.2f;
                    Put(c, x, y, Pick(Fire, t * 1.15f, x, y));
                }
            for (int k = 0; k < 5; k++)
            {
                float px = cx - 30f - Hash(k, i, 33) * 40f, py = cy + (Hash(k, i + 9, 33) - 0.5f) * 30f;
                Dot(c, px, py, 1.3f, Fire[Hash(k, i, 35) < 0.5f ? 2 : 3]);
            }
            return c.WithPivot(cx, cy);
        }

        static PixelCanvas Explosion(int i)
        {
            // A real explosion: white flash, fireball blooming in hot-to-cool bands, breaking into smoke and embers.
            var c = C(192, 192);
            const float cx = 96f, cy = 100f;
            if (i == 0) { Dot(c, cx, cy, 22f, Fire[0]); Dot(c, cx, cy, 30f, A(Fire[1], 0.5f)); return c; }
            float t = (i - 1) / 10f;
            float r = 30f + EaseOut(Mathf.Min(1f, t * 2.2f)) * 52f;
            for (int y = 0; y < 192; y++)
                for (int x = 0; x < 192; x++)
                {
                    float d = Polar(x, y, cx, cy, out float ang);
                    float n = Noise(x * 0.07f, y * 0.07f + t * 3f, 37);
                    float rr = r * (0.85f + 0.3f * n);
                    if (d > rr) continue;
                    float u = d / rr;
                    if (i <= 4)
                    {
                        // Fireball: hot core shrinking as it cools.
                        float heat = u * 0.9f + t * 1.6f + (n - 0.5f) * 0.3f;
                        Put(c, x, y, Pick(Fire, heat, x, y));
                    }
                    else
                    {
                        // Breaking up: smoke puffs with fire seams, holes opening from the middle.
                        float hole = (t - 0.35f) * 1.6f;
                        if (u < hole * (0.8f + 0.4f * n)) continue;
                        bool seam = n > 0.72f && t < 0.45f && u > 0.6f;
                        if (!On(1.2f - t * 0.9f + n * 0.3f, x, y)) continue;
                        Put(c, x, y, seam ? Pick(Fire, 0.45f + t * 0.4f, x, y) : Pick(Smoke, u * 0.6f + t * 0.6f + (n - 0.5f) * 0.3f, x, y));
                    }
                }
            if (i >= 3)
                for (int k = 0; k < 12; k++)
                {
                    float a = k * 30f + Hash(k, 0, 39) * 20f;
                    var p = At(cx, cy, a, r * (0.9f + t * 0.8f));
                    if (i < 11) Dot(c, p.x, p.y - t * 20f, 1.4f, Fire[i < 7 ? 2 : 4]);
                }
            return c;
        }

        static PixelCanvas IceBloom(int i)
        {
            // Frozen monster: ice crystals grow up around the feet, then glint (pivot at the base).
            var c = C(80, 96);
            float g = Mathf.Min(1f, (i + 1) / 4f);
            float[,] spikes = { { 40f, 0f, 46f, 9f }, { 24f, -28f, 30f, 7f }, { 56f, 26f, 32f, 7f }, { 14f, -48f, 20f, 5f }, { 66f, 44f, 22f, 5f }, { 33f, -10f, 36f, 6f }, { 48f, 14f, 38f, 6f } };
            for (int k = 0; k < spikes.GetLength(0); k++)
            {
                float bx = spikes[k, 0], lean = spikes[k, 1], len = spikes[k, 2] * g, hw = spikes[k, 3];
                var b = new Vector2(bx, 92f);
                var tip = b + new Vector2(Mathf.Sin(lean * Mathf.Deg2Rad), -Mathf.Cos(lean * Mathf.Deg2Rad)) * len;
                float L = Mathf.Max(1f, len);
                var dir = (tip - b) / L;
                for (int y = 0; y < 96; y++)
                    for (int x = 0; x < 80; x++)
                    {
                        float px = x + 0.5f - b.x, py = y + 0.5f - b.y;
                        float along = px * dir.x + py * dir.y;
                        if (along < 0f || along > L) continue;
                        float side = px * -dir.y + py * dir.x;
                        float w = (along / L < 0.7f ? hw : hw * (1f - along / L) / 0.3f) * g;
                        if (Mathf.Abs(side) > w) continue;
                        float s = side / Mathf.Max(0.5f, w);
                        Put(c, x, y, Mathf.Abs(s + 0.1f) < 0.15f ? Ice[0] : Pick(Ice, s < 0f ? 0.15f + (s + 1f) * 0.25f : 0.5f + s * 0.4f, x, y));
                    }
            }
            if (i >= 4) Twinkle(c, 30f + (i - 4) * 9f, 50f - (i - 4) * 6f, 3f, Ice[1]);
            return c.WithPivot(40f, 4f);
        }

        static PixelCanvas ElectricSpark(int i)
        {
            // Lightning contact: forked bolts snapping out in new directions every frame, a hot core, then crackles.
            var c = C(80, 80);
            int bolts = i < 4 ? 6 : 3;
            float reach = i < 3 ? 22f + i * 8f : 34f - (i - 3) * 6f;
            for (int k = 0; k < bolts; k++)
            {
                float a = Hash(k, i, 43) * 360f;
                var p = new Vector2(40f, 40f);
                int segs = 5;
                for (int s = 1; s <= segs; s++)
                {
                    var q = At(40f, 40f, a + (Hash(k * 7 + s, i, 45) - 0.5f) * 40f, reach * s / segs);
                    Line(c, p.x, p.y, q.x, q.y, i < 4 ? 1.4f : 0.8f, Volt[4]);
                    Line(c, p.x, p.y, q.x, q.y, 0.6f, Volt[i < 4 ? 0 : 2]);
                    p = q;
                }
            }
            if (i < 4) { Dot(c, 40f, 40f, 7f - i, Volt[2]); Dot(c, 40f, 40f, 4f - i * 0.5f, Volt[0]); }
            return c;
        }

        static PixelCanvas StarShot(int i)
        {
            // 성운탄 bullet: a four-point star turning and twinkling inside a soft violet halo.
            var c = C(56, 56);
            float rot = i * 22.5f;
            for (int y = 0; y < 56; y++)
                for (int x = 0; x < 56; x++)
                {
                    float d = Polar(x, y, 28f, 28f, out float ang);
                    float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos((ang - rot) * 2f * Mathf.Deg2Rad)), 8f);
                    float r = 6f + 18f * spike;
                    if (d < r) Put(c, x, y, d < 5f ? Arcane[0] : Pick(Arcane, 0.15f + d / r * 0.4f, x, y));
                    else if (d < 15f && On(0.5f * (1f - d / 15f), x, y)) Put(c, x, y, A(Arcane[3], 0.8f));
                }
            return c;
        }

        static PixelCanvas StarBurst(int i)
        {
            // Star impact: rays shoot out, a ring opens and sparkles scatter.
            var c = C(112, 112);
            float t = i / 6f;
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f + 10f;
                float r0 = 6f + t * 20f, r1 = 16f + EaseOut(t) * 40f;
                var p0 = At(56f, 56f, a, r0); var p1 = At(56f, 56f, a, r1 * (k % 2 == 0 ? 1f : 0.65f));
                if (i < 6) Line(c, p0.x, p0.y, p1.x, p1.y, k % 2 == 0 ? 1.6f : 1f, Arcane[k % 2 == 0 ? 1 : 3]);
            }
            Ring(c, 56f, 56f, 10f + EaseOut(t) * 38f, Mathf.Lerp(4f, 1f, t), A(Arcane[2], 1f - t * 0.8f));
            if (i < 2) Dot(c, 56f, 56f, 10f - i * 3f, Arcane[0]);
            for (int k = 0; k < 6; k++)
            {
                var p = At(56f, 56f, Hash(k, 0, 47) * 360f, 20f + t * 30f);
                if (i >= 2) Twinkle(c, p.x, p.y, 2f, Arcane[1]);
            }
            return c;
        }

        static PixelCanvas Portal(int i)
        {
            // 차원도약 gate: a standing oval rift spirals open, churns, then snaps shut.
            var c = C(96, 128);
            float[] open = { 0.15f, 0.45f, 0.8f, 1f, 1f, 1f, 0.7f, 0.35f, 0.08f };
            float o = open[i];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 96; x++)
                {
                    float dx = (x + 0.5f - 48f) / (36f * o), dy = (y + 0.5f - 64f) / 56f;
                    float e = dx * dx + dy * dy;
                    if (e > 1f) continue;
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float swirl = Mathf.Repeat(ang + Mathf.Sqrt(e) * 260f - i * 40f, 90f) / 90f;
                    float rim = e > 0.78f ? 1f : 0f;
                    Color32 col = rim > 0f ? Pick(Arcane, 0.1f + (1f - e) * 2f, x, y) : Pick(Arcane, 0.55f + swirl * 0.35f + (1f - e) * 0.15f, x, y);
                    if (e < 0.15f) col = Arcane[7];
                    Put(c, x, y, col);
                }
            return c;
        }

        static PixelCanvas Vortex(int i)
        {
            // 중력 균열 (looped, drawn round and squashed by the player): three spiral arms around a dark core, specks spiralling in.
            var c = C(208, 208);
            const float cx = 104f, cy = 104f;
            float spin = i * 30f;
            for (int y = 0; y < 208; y++)
                for (int x = 0; x < 208; x++)
                {
                    float d = Polar(x, y, cx, cy, out float ang);
                    if (d > 100f) continue;
                    float u = d / 100f;
                    float arm = Mathf.Repeat(ang - spin + Mathf.Log(1f + d) * 95f, 120f) / 120f;
                    float armV = Mathf.Pow(1f - Mathf.Abs(arm - 0.5f) * 2f, 3f);
                    if (d < 16f) { Put(c, x, y, d < 11f ? Arcane[7] : Arcane[6]); continue; }
                    float v = armV * (1f - u) * 1.6f;
                    if (!On(v, x, y)) { if (On(0.18f * (1f - u), x, y)) Put(c, x, y, A(Arcane[6], 0.8f)); continue; }
                    Put(c, x, y, Pick(Arcane, 0.15f + u * 0.5f + (1f - armV) * 0.3f, x, y));
                }
            for (int k = 0; k < 10; k++)
            {
                float a = Hash(k, 0, 53) * 360f - spin * 1.5f;
                float r = Mathf.Repeat(Hash(k, 1, 53) * 90f - i * 7f, 90f) + 14f;
                var p = At(cx, cy, a, r);
                Dot(c, p.x, p.y, 1.6f, Smoke[0]);
            }
            Ring(c, cx, cy, 98f, 1.5f, A(Arcane[3], 0.7f));
            return c;
        }

        static PixelCanvas Collapse(int i)
        {
            // 천체 붕괴 finale: a great arcane sphere sucks inward, flashes white, then bursts in a ring of shards.
            var c = C(224, 224);
            const float cx = 112f, cy = 112f;
            if (i <= 4)
            {
                float r = Mathf.Lerp(80f, 14f, i / 4f);
                for (int y = 0; y < 224; y++)
                    for (int x = 0; x < 224; x++)
                    {
                        float d = Polar(x, y, cx, cy, out float ang);
                        if (d > r + 26f) continue;
                        if (d <= r) Put(c, x, y, Pick(Arcane, 0.1f + d / r * 0.55f, x, y));
                        else
                        {
                            float streak = Mathf.Repeat(ang * 0.5f + d * 0.4f + i * 9f, 12f) < 2f ? 1f : 0f;
                            if (streak > 0f && On(1f - (d - r) / 26f, x, y)) Put(c, x, y, Arcane[2]);
                        }
                    }
                return c;
            }
            if (i == 5) { Dot(c, cx, cy, 46f, Arcane[0]); Dot(c, cx, cy, 70f, A(Arcane[1], 0.45f)); return c; }
            float t = (i - 5) / 4f;
            Ring(c, cx, cy, 30f + EaseOut(t) * 80f, Mathf.Lerp(10f, 2f, t), A(Arcane[1], 1f - t * 0.6f));
            for (int k = 0; k < 16; k++)
            {
                float a = k * 22.5f + Hash(k, 0, 57) * 10f;
                var p = At(cx, cy, a, 30f + EaseOut(t) * (70f + Hash(k, 1, 57) * 20f));
                var q = At(p.x, p.y, a, 6f * (1f - t) + 2f);
                Line(c, p.x, p.y, q.x, q.y, 1.4f, Arcane[t < 0.5f ? 1 : 3]);
            }
            return c;
        }

        static PixelCanvas ArcaneHit(int i)
        {
            // Spell contact: a violet burst ring with diamond shards.
            var c = C(64, 64);
            float t = i / 5f;
            if (i < 2) Dot(c, 32f, 32f, 9f - i * 3f, Arcane[1]);
            Ring(c, 32f, 32f, 6f + EaseOut(t) * 22f, Mathf.Lerp(4f, 1f, t), A(Arcane[2], 1f - t * 0.7f));
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f + 15f;
                var p = At(32f, 32f, a, 8f + t * 20f);
                for (int y = -3; y <= 3; y++)
                    for (int x = -3; x <= 3; x++)
                        if (Mathf.Abs(x) + Mathf.Abs(y) <= 3 - (int)(t * 2f)) Put(c, (int)p.x + x, (int)p.y + y, Arcane[Mathf.Abs(x) + Mathf.Abs(y) <= 1 ? 1 : 3]);
            }
            return c;
        }
    }
}
