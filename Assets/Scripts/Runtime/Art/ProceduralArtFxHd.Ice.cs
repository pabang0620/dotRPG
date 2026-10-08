using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Ice ----------

        static readonly Color32[] IceRamp = { PixelCanvas.Hex("#f4fcff"), PixelCanvas.Hex("#c4ecff"), PixelCanvas.Hex("#9fdcff"), PixelCanvas.Hex("#79c2ff"), PixelCanvas.Hex("#5a9ef0"), PixelCanvas.Hex("#3a78d8") };

        static void FrostBranch(PixelCanvas c, float x, float y, float ang, float len, int depth, int seed)
        {
            float ex = x + Mathf.Cos(ang) * len, ey = y - Mathf.Sin(ang) * len;
            Thick(c, x, y, ex, ey, depth >= 2 ? 0.9f : 0.5f, PixelCanvas.WithAlpha(IceWhite, (byte)(depth >= 2 ? 220 : 170)));
            if (depth <= 0) return;
            for (int i = 1; i <= 2; i++)
            {
                float t = i / 3f;
                float bx = x + Mathf.Cos(ang) * len * t, by = y - Mathf.Sin(ang) * len * t;
                float side = (i % 2 == 0 ? 1f : -1f) * (0.75f + Hash(seed, i, 31) * 0.3f);
                FrostBranch(c, bx, by, ang + side, len * 0.42f, depth - 1, seed * 3 + i);
            }
        }

        static PixelCanvas HdFrost()
        {
            // Frosted ground (radius ~1.5 units): stepped ice tones, a glinting rim, fractal frost ferns and sparkles.
            var c = FxHd(192, 192);
            for (int y = 0; y < 192; y++)
                for (int x = 0; x < 192; x++)
                {
                    float d = Polar(c, x, y, out float ang);
                    float rad = ang * Mathf.Deg2Rad;
                    float r = 86f + 5f * Mathf.Sin(rad * 6f) + 3.5f * Mathf.Sin(rad * 11f + 0.7f) + 2f * Noise(ang * 0.3f, 0f, 4);
                    if (d > r) continue;
                    float t = d / r;
                    if (d > r - 4f) { Put(c, x, y, PixelCanvas.WithAlpha(IceWhite, 235)); continue; }
                    if (d > r - 10f) { Put(c, x, y, PixelCanvas.WithAlpha(IceRamp[1], 175)); continue; }
                    float n = Noise(x * 0.08f, y * 0.08f, 6);
                    var col = Ramp(IceRamp, 1.8f + (1f - t) * 2.2f + (n - 0.5f) * 1.2f, x, y);
                    Put(c, x, y, PixelCanvas.WithAlpha(col, (byte)(255 * Step(0.22f + 0.38f * t * t, x, y, 5))));
                }
            for (int k = 0; k < 9; k++)
            {
                float a = (k * 40f + (k % 3) * 7f) * Mathf.Deg2Rad;
                FrostBranch(c, 96f, 96f, a, 70f + (k % 2) * 10f, 2, k + 1);
            }
            for (int k = 0; k < 40; k++)
            {
                float a = Hash(k, 1, 8) * Mathf.PI * 2, r = Hash(k, 2, 8) * 80f;
                float px = 96f + Mathf.Cos(a) * r, py = 96f - Mathf.Sin(a) * r;
                Put(c, Mathf.FloorToInt(px), Mathf.FloorToInt(py), White);
                if (k % 3 == 0) { c.Set(Mathf.FloorToInt(px) + 1, Mathf.FloorToInt(py), Whiteish(0.6f)); c.Set(Mathf.FloorToInt(px), Mathf.FloorToInt(py) + 1, Whiteish(0.6f)); }
            }
            return c;
        }

        /// <summary>One faceted ice crystal: lit left face, deep right face, a bright ridge and inner refraction lines.</summary>
        static void HdCrystal(PixelCanvas c, float bx, float by, float tx, float ty, float halfWidth, int seed)
        {
            float ax = tx - bx, ay = ty - by;
            float len = Mathf.Sqrt(ax * ax + ay * ay);
            float ux = ax / len, uy = ay / len;
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float px = x + 0.5f - bx, py = y + 0.5f - by;
                    float along = px * ux + py * uy;
                    if (along < 0f || along > len) continue;
                    float u = along / len;
                    float side = px * -uy + py * ux;
                    float w = u < 0.7f ? halfWidth : halfWidth * (1f - u) / 0.3f;
                    if (Mathf.Abs(side) > w) continue;
                    float s = side / Mathf.Max(0.01f, w);   // -1 left .. 1 right
                    float tone = s < -0.15f ? 0.1f + (s + 1f) * 0.25f : s < 0.2f ? 0.35f + u * 0.1f : 0.65f + s * 0.35f;
                    var col = Ramp(IceRamp, tone * (IceRamp.Length - 1) / (IceRamp.Length - 1f), x, y);
                    if (Mathf.Abs(s + 0.05f) < 0.12f && u > 0.1f && u < 0.92f) col = IceWhite;       // ridge
                    if (Mathf.Abs(Mathf.Repeat(along + side * 0.8f + seed * 5f, 14f) - 7f) < 0.6f && s < 0.6f) col = Lerp(col, IceWhite, 0.6f); // refraction
                    c.Set(x, y, col);
                }
        }

        static PixelCanvas HdIce()
        {
            // Cluster of three ice crystals (pivot at the base), outlined.
            var c = FxHd(56, 72);
            HdCrystal(c, 20f, 70f, 6f, 28f, 7.2f, 1);
            HdCrystal(c, 36f, 70f, 50f, 34f, 6.8f, 2);
            HdCrystal(c, 28f, 70f, 28f, 4f, 9.6f, 3);
            c.Outline(IceOutline);
            var p = c.WithBottomPivot();
            p.PivotY = 2f;
            return p;
        }

        static PixelCanvas HdShard()
        {
            // Small ice shard pointing right: a faceted diamond with a bright upper face.
            var c = FxHd(40, 20);
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 40; x++)
                {
                    float u = (x + 0.5f) / 40f;
                    float half = u < 0.35f ? 9.5f * (u / 0.35f) : 9.5f * (1f - (u - 0.35f) / 0.65f);
                    float dy = y + 0.5f - 10f;
                    if (Mathf.Abs(dy) > half) continue;
                    float s = dy / Mathf.Max(0.01f, half);
                    var col = Mathf.Abs(s) < 0.12f ? IceWhite : s < 0f ? Ramp(IceRamp, 0.2f + (s + 1f) * 0.3f, x, y) : Ramp(IceRamp, 0.55f + s * 0.45f, x, y);
                    Put(c, x, y, col);
                }
            c.Outline(PixelCanvas.WithAlpha(IceOutline, 200));
            return c;
        }

        static PixelCanvas HdFrostOrb()
        {
            // Ice-lightning orb: cold halo, faceted crystal core with seams, a hot white heart and violet sparks.
            var c = FxHd(72, 72);
            for (int y = 0; y < 72; y++)
                for (int x = 0; x < 72; x++)
                {
                    float dx = x + 0.5f - 36f, dy = y + 0.5f - 36f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 33f) continue;
                    if (d > 25f) { Put(c, x, y, PixelCanvas.Hex("#7fd4ff", (byte)(255 * Step(0.45f * (33f - d) / 8f + 0.05f, x, y, 4)))); continue; }
                    if (d > 22.5f) { Put(c, x, y, PixelCanvas.Hex("#2f6fb8")); continue; }
                    float face = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 15f, 60f);
                    float lit = (dx + dy) / 44f + 0.5f + (face < 30f ? -0.1f : 0.1f);
                    var col = Ramp(IceRamp, lit * 0.9f, x, y);
                    if (face < 1.6f || face > 58.4f) col = IceWhite;                  // facet seams
                    if (d < 7.5f) col = d < 4.5f ? new Color32(255, 255, 255, 255) : IceRamp[0];
                    Put(c, x, y, col);
                }
            var spark = PixelCanvas.Hex("#c8b4ff");
            for (int k = 0; k < 10; k++)
            {
                float a = k * 36f * Mathf.Deg2Rad + Hash(k, 0, 3);
                Dot(c, 36f + Mathf.Cos(a) * 30f, 36f - Mathf.Sin(a) * 30f, 1.3f, spark);
            }
            return c;
        }
    }
}
