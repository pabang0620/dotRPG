using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Sparks, snow and stars ----------

        static PixelCanvas HdSpark()
        {
            // Four-point spark: white core, tapered arms, faint diagonal glints.
            var c = FxHd(20, 20);
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 10f), dy = Mathf.Abs(y + 0.5f - 10f);
                    float arm = Mathf.Min(dx, dy), len = Mathf.Max(dx, dy);
                    float a = 0f;
                    if (len < 10f && arm < 1.6f * (1f - len / 10f) + 0.4f) a = 1f - len / 12f;
                    if (Mathf.Abs(dx - dy) < 0.8f && len < 5f) a = Mathf.Max(a, 0.55f);
                    if (dx * dx + dy * dy < 7f) a = 1f;
                    if (a > 0f) Put(c, x, y, Wd(a, x, y, 5));
                }
            return c;
        }

        static PixelCanvas HdSnow()
        {
            // Six-arm snowflake with side branches.
            var c = FxHd(20, 20);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                float ex = 10f + Mathf.Cos(a) * 9f, ey = 10f - Mathf.Sin(a) * 9f;
                Thick(c, 10f, 10f, ex, ey, 0.6f, Whiteish(0.95f));
                float mx = 10f + Mathf.Cos(a) * 5.5f, my = 10f - Mathf.Sin(a) * 5.5f;
                Thick(c, mx, my, mx + Mathf.Cos(a + 0.8f) * 2.6f, my - Mathf.Sin(a + 0.8f) * 2.6f, 0.5f, Whiteish(0.7f));
                Thick(c, mx, my, mx + Mathf.Cos(a - 0.8f) * 2.6f, my - Mathf.Sin(a - 0.8f) * 2.6f, 0.5f, Whiteish(0.7f));
            }
            Dot(c, 10f, 10f, 1.8f, White);
            return c;
        }

        static PixelCanvas HdStar()
        {
            // Stun star: gold four-point star with a lit face and a brown outline.
            var c = FxHd(28, 28);
            var ramp = new[] { PixelCanvas.Hex("#fff6c0"), PixelCanvas.Hex("#ffe070"), PixelCanvas.Hex("#ffd34a"), PixelCanvas.Hex("#f0a830"), PixelCanvas.Hex("#c87818") };
            for (int y = 0; y < 28; y++)
                for (int x = 0; x < 28; x++)
                {
                    float dx = x + 0.5f - 14f, dy = y + 0.5f - 14f;
                    float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                    float lim = 12f - Mathf.Min(ax, ay) * 2.4f;
                    if (Mathf.Max(ax, ay) > lim || Mathf.Min(ax, ay) > 5f) continue;
                    float light = (dx + dy) / 24f + 0.5f;     // lit from the upper left
                    Put(c, x, y, Ramp(ramp, light, x, y));
                }
            Dot(c, 12f, 12f, 2.2f, PixelCanvas.Hex("#ffffff"));
            c.Outline(PixelCanvas.Hex("#7a3a0c"));
            return c;
        }

        static PixelCanvas HdSparkle()
        {
            // Pickup sparkle: yellow star with a white heart.
            var c = FxHd(44, 44);
            var ramp = new[] { PixelCanvas.Hex("#ffffff"), PixelCanvas.Hex("#fff3a0"), PixelCanvas.Hex("#ffd34a"), PixelCanvas.Hex("#f0a830") };
            for (int y = 0; y < 44; y++)
                for (int x = 0; x < 44; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 22f), dy = Mathf.Abs(y + 0.5f - 22f);
                    float len = Mathf.Max(dx, dy), arm = Mathf.Min(dx, dy);
                    bool cross = len < 21f && arm < 3.2f * (1f - len / 22f) + 0.6f;
                    bool diag = Mathf.Abs(dx - dy) < 1.2f && len < 11f;
                    if (!cross && !diag) continue;
                    Put(c, x, y, Ramp(ramp, len / 21f, x, y));
                }
            return c;
        }

        static PixelCanvas HdMagicMote()
        {
            var c = FxHd(20, 20);
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 10f), dy = Mathf.Abs(y + 0.5f - 10f);
                    if (Mathf.Min(dx, dy) > 1.6f || Mathf.Max(dx, dy) > 9.5f) continue;
                    Put(c, x, y, Mathf.Max(dx, dy) < 3f ? MagicLight : Magic);
                }
            return c;
        }

        static PixelCanvas HdBolt()
        {
            // Mage bolt: violet sphere with a bright core, rim light and orbiting motes.
            var c = FxHd(48, 48);
            var ramp = new[] { PixelCanvas.Hex("#ffffff"), PixelCanvas.Hex("#d9ccff"), PixelCanvas.Hex("#b39bff"), PixelCanvas.Hex("#9b7bff"), PixelCanvas.Hex("#6a4be0") };
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    float dx = x + 0.5f - 24f, dy = y + 0.5f - 24f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 22f) continue;
                    if (d > 16f) { Put(c, x, y, PixelCanvas.WithAlpha(Magic, (byte)(255 * Step(0.55f * (22f - d) / 6f, x, y, 4)))); continue; }
                    float lit = d / 16f - (-dx - dy) / 60f;
                    Put(c, x, y, Ramp(ramp, lit, x, y));
                }
            foreach (var (mx, my) in new[] { (10f, 9f), (38f, 13f), (8f, 34f), (36f, 37f) }) Dot(c, mx, my, 1.4f, MagicCore);
            return c;
        }

        static PixelCanvas HdDust()
        {
            var c = FxHd(24, 24);
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                {
                    float dx = x + 0.5f - 12f, dy = y + 0.5f - 12f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) + (Noise(x * 0.4f, y * 0.4f, 21) - 0.5f) * 3f;
                    if (d > 10.5f) continue;
                    var col = dx + dy > 4f ? PixelCanvas.Hex("#e0d8c8") : PixelCanvas.Hex("#fffaf0");
                    Put(c, x, y, PixelCanvas.WithAlpha(col, (byte)(230 * Step(1f - d / 12f + 0.3f, x, y, 4))));
                }
            return c;
        }

        // ---------- Lightning ----------

        static PixelCanvas HdZap()
        {
            // Electric burst: jagged forked rays (white core, cyan body, violet tips) with a dark outline.
            var c = FxHd(96, 96);
            var cyan = PixelCanvas.Hex("#8ff0ff");
            var violet = PixelCanvas.Hex("#a58bff");
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad + (k % 2 == 0 ? 0f : 0.14f);
                float len = k % 2 == 0 ? 44f : 28f;
                var pts = Jag(48f, 48f, 48f + Mathf.Cos(a) * len, 48f - Mathf.Sin(a) * len, 6, 6f, k + 1);
                Poly(c, pts, 3.6f, violet);
                Poly(c, pts, 2.5f, cyan);
                for (int i = 0; i < pts.Length - 2; i++) Thick(c, pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, 1.1f, White);
                // A fork from the middle of the long rays.
                if (k % 2 == 0)
                {
                    var m = pts[3];
                    float fa = a + (Hash(k, 3, 7) < 0.5f ? 0.6f : -0.6f);
                    var fork = Jag(m.x, m.y, m.x + Mathf.Cos(fa) * 14f, m.y - Mathf.Sin(fa) * 14f, 3, 3f, k + 20);
                    Poly(c, fork, 2f, violet);
                    Poly(c, fork, 1.2f, cyan);
                }
            }
            Dot(c, 48f, 48f, 8.5f, cyan);
            Dot(c, 48f, 48f, 6f, White);
            c.Outline(PixelCanvas.Hex("#3a2a8a"));
            return c;
        }
    }
}
