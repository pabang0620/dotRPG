using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution skill effect sprites: the same keys, world sizes and pivots as the classic 16px effects, drawn
    /// four times as dense (Density 4, 64 art pixels per tile). Still pixel art: hard pixels, stepped tones and ordered
    /// dithering instead of smooth blur, but with highlights, facets, fine lines and detail the 16px set cannot hold.
    /// Most sprites stay white/grey so the skill code can tint them; ice, fire, rock, gold and lightning keep their own palette.
    /// </summary>
    public static partial class ProceduralArt
    {
        /// <summary>Draw skill effects in high resolution (false = the classic 16px set, kept for comparison).</summary>
        public static bool HdEffects = true;

        const int K = 4; // art pixels per classic pixel

        static PixelCanvas FxHd(int w, int h) => new PixelCanvas(w, h) { Density = K };

        static readonly int[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        static float Bayer(int x, int y) => (Bayer4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

        /// <summary>A 0..1 level stepped to <paramref name="levels"/> tones with ordered dithering (the pixel-art gradient).</summary>
        static float Step(float v, int x, int y, int levels = 6)
        {
            v = Mathf.Clamp01(v) * levels;
            int b = (int)v;
            if (v - b > Bayer(x, y)) b++;
            return Mathf.Clamp01(b / (float)levels);
        }

        static Color32 Wd(float a, int x, int y, int levels = 6) => Whiteish(Step(a, x, y, levels));

        /// <summary>Grey tone (for white sprites with shading) with alpha.</summary>
        static Color32 Grey(float tone, float alpha) =>
            new Color32((byte)(255 * tone), (byte)(255 * tone), (byte)(255 * Mathf.Min(1f, tone * 1.02f)), (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));

        static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));

        /// <summary>Picks a colour from a ramp with dithering between neighbours (no smooth blends).</summary>
        static Color32 Ramp(Color32[] ramp, float t, int x, int y)
        {
            float v = Mathf.Clamp01(t) * (ramp.Length - 1);
            int i = (int)v;
            if (i < ramp.Length - 1 && v - i > Bayer(x, y)) i++;
            return ramp[i];
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffff) / 65535f;
            }
        }

        static float Noise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Mathf.Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), fx);
            float b = Mathf.Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        static void Dot(PixelCanvas c, float x, float y, float r, Color32 col)
        {
            int x0 = Mathf.FloorToInt(x - r), x1 = Mathf.CeilToInt(x + r), y0 = Mathf.FloorToInt(y - r), y1 = Mathf.CeilToInt(y + r);
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - x, dy = py + 0.5f - y;
                    if (dx * dx + dy * dy <= r * r) c.Set(px, py, col);
                }
        }

        static void Thick(PixelCanvas c, float x0, float y0, float x1, float y1, float r, Color32 col)
        {
            float len = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            int n = Mathf.Max(1, Mathf.CeilToInt(len * 2));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                if (r <= 0.6f) c.Set(Mathf.FloorToInt(Mathf.Lerp(x0, x1, t)), Mathf.FloorToInt(Mathf.Lerp(y0, y1, t)), col);
                else Dot(c, Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), r, col);
            }
        }

        /// <summary>A jagged electric bolt from (x0,y0) to (x1,y1) as a list of points.</summary>
        static Vector2[] Jag(float x0, float y0, float x1, float y1, int segments, float wiggle, int seed)
        {
            var pts = new Vector2[segments + 1];
            var d = new Vector2(x1 - x0, y1 - y0);
            var n = new Vector2(-d.y, d.x).normalized;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float off = i == 0 || i == segments ? 0f : (Hash(i, seed, 91) - 0.5f) * 2f * wiggle * Mathf.Sin(t * Mathf.PI);
                pts[i] = new Vector2(x0, y0) + d * t + n * off;
            }
            return pts;
        }

        static void Poly(PixelCanvas c, Vector2[] pts, float r, Color32 col)
        {
            for (int i = 0; i < pts.Length - 1; i++) Thick(c, pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, r, col);
        }

        // =====================================================================================================

        static PixelCanvas DrawSkillFxHd(string kind)
        {
            switch (kind)
            {
                case "glow": return HdGlow();
                case "shock": return HdShock();
                case "rune": return HdRune();
                case "swoosh": return HdSwoosh();
                case "zap": return HdZap();
                case "blade": return HdBlade();
                case "frost": return HdFrost();
                case "crack": return HdCrack();
                case "spike": return HdSpike();
                case "ice": return HdIce();
                case "shard": return HdShard();
                case "snow": return HdSnow();
                case "spark": return HdSpark();
                case "streak": return HdStreak();
                case "cut": return HdCut();
                case "crescent": return HdCrescent(false);
                case "arc": return HdCrescent(true);
                case "bigsword": return HdBigSword();
                case "frostorb": return HdFrostOrb();
                case "flame": return HdFlame();
                case "meteor": return HdMeteor();
                case "scorch": return HdScorch();
                case "star": return HdStar();
                case "aegis": return HdAegis();
                case "feather": return HdFeather();
                case "holy": return HdHoly();
            }
            return null;
        }

        /// <summary>The fx sprites drawn in ProceduralArt.DrawFx (basic attack and pickup effects).</summary>
        static PixelCanvas DrawFxHd(string kind)
        {
            switch (kind)
            {
                case "ring": return HdRing();
                case "slash": return HdSlash();
                case "sparkle": return HdSparkle();
                case "bolt": return HdBolt();
                case "magic": return HdMagicMote();
                case "dust": return HdDust();
            }
            return null;
        }
    }
}
