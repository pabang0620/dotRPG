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

        // ---------- Light and rings ----------

        static PixelCanvas HdGlow()
        {
            // Smooth light (the one bilinear fx sprite): a soft body and a slightly brighter core.
            var c = FxHd(128, 128);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float d = Polar(c, x, y, out _) / 64f;
                    if (d >= 1f) continue;
                    float a = Mathf.Pow(1f - d, 1.7f) * 0.92f + Mathf.Pow(Mathf.Max(0f, 1f - d * 3f), 2f) * 0.08f;
                    Put(c, x, y, Whiteish(a));
                }
            return c;
        }

        static PixelCanvas HdShock()
        {
            // Shock ring (radius 1 unit): hard bright rim with an inner highlight line, broken energy wisps behind it.
            var c = FxHd(128, 128);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float d = Polar(c, x, y, out float ang);
                    if (d >= 64f || d < 26f) continue;
                    float wisp = Noise(ang * 0.18f, d * 0.12f, 3);
                    float a;
                    if (d >= 61.5f) a = 1f;
                    else if (d >= 59f) a = 0.62f + 0.3f * wisp;
                    else if (d >= 56.5f) a = d < 57.5f ? 0.85f : 0.4f;
                    else a = 0.34f * Mathf.Pow((d - 26f) / 30.5f, 1.5f) * (0.45f + 0.9f * wisp);
                    if (a <= 0.02f) continue;
                    Put(c, x, y, Wd(a, x, y, 7));
                }
            // Small chips flung off the rim.
            for (int k = 0; k < 14; k++)
            {
                float a = Hash(k, 1, 5) * Mathf.PI * 2f, r = 60f + Hash(k, 2, 5) * 3f;
                Dot(c, 64f + Mathf.Cos(a) * r, 64f - Mathf.Sin(a) * r, 1.2f, Whiteish(0.9f));
            }
            return c;
        }

        static PixelCanvas HdRing()
        {
            // Thin ring (pickup / target marks): crisp band, faint inner fill.
            var c = FxHd(128, 128);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float d = Polar(c, x, y, out _);
                    if (d > 62f) continue;
                    if (d >= 58f) Put(c, x, y, Whiteish(1f));
                    else if (d >= 55.5f) Put(c, x, y, Whiteish(0.7f));
                    else if (d >= 50f) Put(c, x, y, Wd(0.43f * (d - 44f) / 11.5f, x, y));
                    else if (d >= 44f) Put(c, x, y, Wd(0.2f, x, y, 5));
                    else Put(c, x, y, Wd(0.1f, x, y, 5));
                }
            return c;
        }

        /// <summary>A small rune glyph made of strokes on a 3x4 lattice.</summary>
        static void Glyph(PixelCanvas c, int cx, int cy, int seed, Color32 col)
        {
            var lattice = new Vector2[12];
            for (int i = 0; i < 12; i++) lattice[i] = new Vector2(cx - 3 + (i % 3) * 3, cy - 4.5f + (i / 3) * 3);
            int strokes = 3 + (int)(Hash(seed, 7, 11) * 3);
            for (int s = 0; s < strokes; s++)
            {
                int a = (int)(Hash(seed, s, 13) * 12) % 12, b = (int)(Hash(seed, s, 17) * 12) % 12;
                if (a == b) b = (b + 4) % 12;
                Thick(c, lattice[a].x, lattice[a].y, lattice[b].x, lattice[b].y, 0.5f, col);
            }
        }

        static PixelCanvas HdRune()
        {
            // Magic circle: double outer ring, a band of rune glyphs, a ticked ring, a hexagram and a sealed core.
            var c = FxHd(128, 128);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float d = Polar(c, x, y, out float ang);
                    if (d >= 60.5f && d < 63f) Put(c, x, y, Whiteish(1f));
                    else if (d >= 57.6f && d < 58.8f) Put(c, x, y, Whiteish(0.75f));
                    else if (d >= 45f && d < 47.4f) Put(c, x, y, Whiteish(0.9f));
                    else if (d >= 47.4f && d < 50.5f && Mathf.Repeat(ang, 7.5f) < 1.1f) Put(c, x, y, Whiteish(0.65f));
                    else if (d >= 18f && d < 19.6f) Put(c, x, y, Whiteish(0.85f));
                    else if (d >= 12f && d < 13f) Put(c, x, y, Whiteish(0.6f));
                    else if (d < 57.6f && d > 50.5f) Put(c, x, y, Wd(0.12f, x, y, 4));
                }
            for (int k = 0; k < 16; k++)
            {
                float a = (k * 22.5f + 11.25f) * Mathf.Deg2Rad;
                Glyph(c, Mathf.RoundToInt(64f + Mathf.Cos(a) * 53.8f), Mathf.RoundToInt(64f - Mathf.Sin(a) * 53.8f), k + 3, Whiteish(0.95f));
            }
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad;
                Dot(c, 64f + Mathf.Cos(a) * 61.8f, 64f - Mathf.Sin(a) * 61.8f, 2.6f, White);
            }
            for (int t = 0; t < 2; t++)
                for (int k = 0; k < 3; k++)
                {
                    float a0 = (90f + t * 180f + k * 120f) * Mathf.Deg2Rad, a1 = (90f + t * 180f + (k + 1) * 120f) * Mathf.Deg2Rad;
                    Thick(c, 64f + Mathf.Cos(a0) * 43f, 64f - Mathf.Sin(a0) * 43f, 64f + Mathf.Cos(a1) * 43f, 64f - Mathf.Sin(a1) * 43f, 0.9f, Whiteish(0.8f));
                    Dot(c, 64f + Mathf.Cos(a0) * 43f, 64f - Mathf.Sin(a0) * 43f, 2f, White);
                }
            // Core seal: a diamond inside the inner ring.
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 64f), dy = Mathf.Abs(y + 0.5f - 64f);
                    float m = dx + dy;
                    if (m < 9f && m >= 7.5f) Put(c, x, y, Whiteish(0.95f));
                    else if (m < 4f) Put(c, x, y, Whiteish(0.8f));
                }
            return c;
        }

        // ---------- Blades and arcs ----------

        static PixelCanvas HdSwoosh()
        {
            // Whirlwind blade (white): sharp leading edge, then several thin speed lines that thin out along the trail.
            var c = FxHd(224, 224);
            const float outer = 108f, sweep = 300f;
            for (int y = 0; y < 224; y++)
                for (int x = 0; x < 224; x++)
                {
                    float d = Polar(c, x, y, out float ang);
                    if (ang > sweep || d > outer) continue;
                    float s = ang / sweep;
                    float thick = Mathf.Lerp(26f, 4f, s);
                    float fade = Mathf.Pow(1f - s, 1.4f);
                    float edge = outer - thick;
                    float a;
                    if (d >= outer - thick * 0.3f) a = fade;
                    else if (d >= edge)
                    {
                        float lines = 0.55f + 0.45f * Mathf.Sin(d * 1.25f + s * 9f);
                        a = fade * (0.45f + 0.4f * lines);
                    }
                    else if (d >= edge - 28f) a = fade * 0.24f * (d - (edge - 28f)) / 28f * (0.6f + 0.8f * Noise(ang * 0.25f, d * 0.2f, 9));
                    else continue;
                    if (a > 0.03f) Put(c, x, y, Wd(a, x, y, 7));
                }
            return c;
        }

        static PixelCanvas HdBlade()
        {
            // Gold whirlwind blade with a baked palette, now with a white leading edge, a hard specular line,
            // dithered tone steps and a dark rim. Same shape as fx_swoosh.
            var c = FxHd(224, 224);
            var ramp = new[] { PixelCanvas.Hex("#fffdf2"), PixelCanvas.Hex("#fff0b0"), PixelCanvas.Hex("#ffe070"), PixelCanvas.Hex("#ffc23a"), PixelCanvas.Hex("#ffa526"), PixelCanvas.Hex("#e8761c"), PixelCanvas.Hex("#b8521a") };
            var rim = PixelCanvas.Hex("#6e2a0e");
            const float outer = 108f, sweep = 290f;
            for (int y = 0; y < 224; y++)
                for (int x = 0; x < 224; x++)
                {
                    float d = Polar(c, x, y, out float ang);
                    if (ang > sweep || d > outer) continue;
                    float s = ang / sweep;
                    float thick = Mathf.Lerp(28f, 5f, s);
                    float fade = Mathf.Pow(1f - s, 1.2f);
                    float inner = outer - thick;
                    if (d < inner - 24f) continue;
                    if (d < inner)
                    {
                        float a = fade * 0.3f * (d - (inner - 24f)) / 24f;
                        if (Step(a, x, y, 5) > 0f) Put(c, x, y, PixelCanvas.WithAlpha(ramp[4], (byte)(255 * Step(a, x, y, 5))));
                        continue;
                    }
                    float u = (outer - d) / thick;
                    Color32 col = d > outer - 2.5f ? rim : Mathf.Abs(u - 0.18f) < 0.05f && s < 0.6f ? ramp[0] : Ramp(ramp, u * 0.85f + s * 0.45f, x, y);
                    float alpha = fade * (d > outer - 2.5f ? 0.8f : 1f);
                    byte al = (byte)(255 * Step(alpha, x, y, 8));
                    if (al > 0) Put(c, x, y, PixelCanvas.WithAlpha(col, al));
                }
            return c;
        }

        static PixelCanvas HdCrescent(bool white)
        {
            // Flying sword wave: a crescent bulging to the right (direction of travel). A hard cutting edge, tone
            // bands, a specular streak and speed lines dragging behind; tips fade with dithering.
            var c = FxHd(80, 128);
            var gold = new[] { PixelCanvas.Hex("#fffbe8"), PixelCanvas.Hex("#fff0b0"), PixelCanvas.Hex("#ffe070"), PixelCanvas.Hex("#ffc23a"), PixelCanvas.Hex("#ffa526"), PixelCanvas.Hex("#e8761c") };
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 80; x++)
                {
                    float ox = (x + 0.5f - 16f) / 60f, oy = (y + 0.5f - 64f) / 62f;
                    float ix = (x + 0.5f + 4f) / 52f, iy = (y + 0.5f - 64f) / 54f;
                    float outer = ox * ox + oy * oy;
                    if (outer > 1f || ix * ix + iy * iy <= 1f) continue;
                    float depth = 1f - Mathf.Sqrt(outer);           // 0 at the cutting edge
                    float tip = Mathf.Abs(y + 0.5f - 64f) / 64f;    // 0 middle, 1 tips
                    float a = tip > 0.72f ? 1f - (tip - 0.72f) / 0.28f : 1f;
                    float lines = (Mathf.Repeat(y * 0.55f, 6f) < 1f && depth > 0.12f) ? 0.55f : 1f;
                    if (white)
                    {
                        float tone = depth < 0.035f ? 1f : depth < 0.07f ? 0.92f : Mathf.Lerp(0.85f, 0.55f, depth / 0.3f);
                        float al = (depth < 0.07f ? 1f : Mathf.Lerp(0.85f, 0.35f, depth / 0.3f)) * a * lines;
                        Put(c, x, y, Grey(tone, Step(al, x, y, 7)));
                    }
                    else
                    {
                        var col = depth < 0.03f ? gold[0] : Ramp(gold, depth / 0.3f, x, y);
                        float al = a * lines;
                        Put(c, x, y, PixelCanvas.WithAlpha(col, (byte)(255 * Step(al, x, y, 7))));
                    }
                }
            return c;
        }

        static PixelCanvas HdSlash()
        {
            // Basic-attack crescent (white), facing right.
            var c = FxHd(80, 96);
            for (int y = 0; y < 96; y++)
                for (int x = 0; x < 80; x++)
                {
                    float ox = (x + 0.5f - 32f) / 44f, oy = (y + 0.5f - 48f) / 44f;
                    float ix = (x + 0.5f - 16f) / 40f, iy = (y + 0.5f - 48f) / 42f;
                    float o = ox * ox + oy * oy;
                    if (o > 1f || ix * ix + iy * iy <= 1f) continue;
                    float depth = 1f - Mathf.Sqrt(o);
                    float tip = Mathf.Abs(y + 0.5f - 48f) / 48f;
                    float a = (tip > 0.7f ? 1f - (tip - 0.7f) / 0.3f : 1f) * (depth < 0.05f ? 1f : Mathf.Lerp(0.9f, 0.4f, depth / 0.35f));
                    Put(c, x, y, Grey(depth < 0.05f ? 1f : 0.88f, Step(a, x, y, 7)));
                }
            return c;
        }

        static PixelCanvas HdCut()
        {
            // Lens-shaped hit slash: bright core line, a mid band and dithered feathered edges.
            var c = FxHd(88, 28);
            for (int x = 0; x < 88; x++)
            {
                float u = (x + 0.5f) / 88f;
                float h = 13.2f * Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.8f);
                for (int y = 0; y < 28; y++)
                {
                    float dy = Mathf.Abs(y + 0.5f - 14f);
                    if (dy > h) continue;
                    float t = dy / Mathf.Max(0.01f, h);
                    float a = t < 0.22f ? 1f : t < 0.5f ? 0.8f : 0.55f * (1f - (t - 0.5f) / 0.5f) + 0.15f;
                    Put(c, x, y, Wd(a, x, y, 7));
                }
            }
            return c;
        }

        static PixelCanvas HdStreak()
        {
            // Motion streak: tapered, bright head on the right.
            var c = FxHd(32, 12);
            for (int x = 0; x < 32; x++)
            {
                float t = (x + 1) / 32f;
                float half = 0.6f + 5f * Mathf.Pow(t, 1.6f) * (t > 0.88f ? (1f - t) / 0.12f + 0.2f : 1f);
                for (int y = 0; y < 12; y++)
                {
                    float dy = Mathf.Abs(y + 0.5f - 6f);
                    if (dy > half) continue;
                    float a = (0.15f + 0.85f * t) * (dy < half * 0.4f ? 1f : 0.5f);
                    Put(c, x, y, Wd(a, x, y, 6));
                }
            }
            return c;
        }

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

        // ---------- Fire, rock and ground ----------

        static PixelCanvas HdFlame()
        {
            // Fire puff: a licking flame shape in four bands from dark red to a white-hot core.
            var c = FxHd(32, 32);
            var ramp = new[] { PixelCanvas.Hex("#fffbe0"), PixelCanvas.Hex("#ffe070"), PixelCanvas.Hex("#ffb030"), PixelCanvas.Hex("#ff7a1c"), PixelCanvas.Hex("#e8401c"), PixelCanvas.Hex("#a82010") };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x + 0.5f - 16f) / 15f, dy = (y + 0.5f - 18f) / 14f;
                    // Teardrop pointing up, with a wavy edge.
                    float r = Mathf.Sqrt(dx * dx * (1f + Mathf.Max(0f, -dy) * 1.8f) + dy * dy);
                    float edge = 1f + 0.12f * Mathf.Sin(Mathf.Atan2(dy, dx) * 5f + 1f);
                    if (r > edge) continue;
                    float t = r / edge + (dy < 0 ? -dy * 0.15f : 0f);
                    var col = Ramp(ramp, t, x, y);
                    byte a = (byte)(t > 0.85f ? 200 : 255);
                    Put(c, x, y, PixelCanvas.WithAlpha(col, a));
                }
            return c;
        }

        static PixelCanvas HdMeteor()
        {
            // Burning rock: lit from the upper left, pitted surface, glowing lava fissures and an ember rim.
            var c = FxHd(56, 56);
            var rock = new[] { PixelCanvas.Hex("#9a7a60"), PixelCanvas.Hex("#7a5a44"), PixelCanvas.Hex("#5a4032"), PixelCanvas.Hex("#3e2c22"), PixelCanvas.Hex("#2a1d16") };
            for (int y = 0; y < 56; y++)
                for (int x = 0; x < 56; x++)
                {
                    float dx = x + 0.5f - 28f, dy = y + 0.5f - 28f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) + (Noise(x * 0.25f, y * 0.25f, 12) - 0.5f) * 4f;
                    if (d > 24f) continue;
                    float lit = 0.5f + (dx + dy) / 48f + (Noise(x * 0.35f, y * 0.35f, 13) - 0.5f) * 0.5f;
                    var col = Ramp(rock, lit, x, y);
                    if (d > 21.5f && dx + dy > 4f) col = PixelCanvas.Hex("#ff8a24");     // ember rim on the trailing side
                    Put(c, x, y, col);
                }
            var lava = PixelCanvas.Hex("#ff8a24");
            var hot = PixelCanvas.Hex("#ffe070");
            foreach (var (x0, y0, x1, y1) in new[] { (12f, 28f, 27f, 24f), (27f, 24f, 40f, 36f), (27f, 24f, 31f, 9f), (20f, 40f, 27f, 24f) })
            {
                Thick(c, x0, y0, x1, y1, 1.6f, lava);
                Thick(c, x0, y0, x1, y1, 0.6f, hot);
            }
            c.Outline(PixelCanvas.Hex("#1e140f"));
            return c;
        }

        static PixelCanvas HdScorch()
        {
            // Burnt ground (squashed like the ground plane): charred core, ragged edge, glowing embers.
            var c = FxHd(128, 80);
            for (int y = 0; y < 80; y++)
                for (int x = 0; x < 128; x++)
                {
                    float nx = (x + 0.5f - 64f) / 60f, ny = (y + 0.5f - 40f) / 36f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny) + (Noise(x * 0.12f, y * 0.12f, 14) - 0.5f) * 0.3f;
                    if (d > 1f) continue;
                    float a = d < 0.45f ? 0.8f : 0.8f * (1f - (d - 0.45f) / 0.55f);
                    var col = d < 0.3f ? new Color32(22, 16, 14, 255) : new Color32(38, 28, 24, 255);
                    Put(c, x, y, PixelCanvas.WithAlpha(col, (byte)(255 * Step(a, x, y, 5))));
                }
            for (int k = 0; k < 18; k++)
            {
                float px = 32f + Hash(k, 1, 15) * 64f, py = 20f + Hash(k, 2, 15) * 40f;
                Dot(c, px, py, 1.6f, PixelCanvas.Hex("#ff8a24", 220));
                if (k % 3 == 0) Dot(c, px, py, 0.7f, PixelCanvas.Hex("#ffe070"));
            }
            return c;
        }

        static PixelCanvas HdCrack()
        {
            // Ground cracks (squashed like the ground plane): branching fissures with a dark core, lit lower lip and pebbles.
            var c = FxHd(128, 96);
            var dark = PixelCanvas.Hex("#2a1e15", 240);
            var mid = PixelCanvas.Hex("#4a3626", 200);
            var lit = PixelCanvas.Hex("#e2cfa4", 170);
            var rng = new System.Random(7);
            void Fissure(float x, float y, float ang, int steps, float width, int depth)
            {
                for (int s = 0; s < steps; s++)
                {
                    float a2 = ang + (float)(rng.NextDouble() - 0.5) * 0.9f;
                    float nx = x + Mathf.Cos(a2) * 9f, ny = y - Mathf.Sin(a2) * 9f * 0.75f;
                    float w = Mathf.Lerp(width, 0.5f, s / (float)steps);
                    Thick(c, x, y + 1.5f, nx, ny + 1.5f, w + 0.4f, lit);
                    Thick(c, x, y, nx, ny, w + 1.2f, mid);
                    Thick(c, x, y, nx, ny, w, dark);
                    if (depth > 0 && rng.NextDouble() < 0.35) Fissure(nx, ny, a2 + (rng.NextDouble() < 0.5 ? 0.8f : -0.8f), steps - s - 1, w * 0.7f, depth - 1);
                    x = nx; y = ny;
                }
            }
            for (int k = 0; k < 6; k++) Fissure(64f, 48f, (k * 60f + rng.Next(-15, 15)) * Mathf.Deg2Rad, 5 + rng.Next(0, 3), 2.4f, 1);
            Dot(c, 64f, 48f, 5f, dark);
            for (int k = 0; k < 10; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 8f + (float)rng.NextDouble() * 20f;
                float px = 64f + Mathf.Cos(a) * r, py = 48f - Mathf.Sin(a) * r * 0.75f;
                Dot(c, px, py, 1.6f, PixelCanvas.Hex("#6b5a48"));
                c.Set(Mathf.FloorToInt(px - 1), Mathf.FloorToInt(py - 1), PixelCanvas.Hex("#b8a488"));
            }
            return c;
        }

        static PixelCanvas HdSpike()
        {
            // Rock spike bursting out of the ground (pivot at its base): three faces, strata lines, chipped highlights.
            var c = FxHd(48, 64);
            var ramp = new[] { PixelCanvas.Hex("#e0e6ec"), StoneLight, PixelCanvas.Hex("#b0b8c2"), Stone, PixelCanvas.Hex("#80888f"), StoneDark, PixelCanvas.Hex("#4e555e") };
            for (int y = 2; y < 64; y++)
            {
                float t = (y - 2) / 61f;
                float w = Mathf.Lerp(2f, 18.5f, Mathf.Pow(t, 0.9f));
                float lean = (1f - t) * 3f;
                for (int x = 0; x < 48; x++)
                {
                    float dx = x + 0.5f - 24f - lean;
                    if (Mathf.Abs(dx) > w) continue;
                    float s = dx / w;
                    float tone = s < -0.3f ? 0.15f + (s + 1f) * 0.2f : s < 0.35f ? 0.45f : 0.7f + s * 0.3f;
                    if (Mathf.Abs(Mathf.Repeat(y + dx * 0.4f, 11f) - 5.5f) < 0.6f && Mathf.Abs(s) < 0.85f) tone += 0.18f; // strata
                    c.Set(x, y, Ramp(ramp, tone, x, y));
                }
            }
            foreach (var (hx, hy) in new[] { (19, 18), (17, 30), (21, 44), (15, 52) }) { c.Set(hx, hy, ramp[0]); c.Set(hx + 1, hy, ramp[0]); }
            c.Outline(Outline);
            var p = c.WithBottomPivot();
            p.PivotY = 2f;
            return p;
        }

        // ---------- Weapons and holy ----------

        static PixelCanvas HdBigSword()
        {
            // Giant falling sword, tip down (pivot at the tip): gem-set guard, wrapped grip, fullered blade with runes.
            var c = FxHd(56, 152);
            var steel = new[] { PixelCanvas.Hex("#ffffff"), PixelCanvas.Hex("#e8f0ff"), PixelCanvas.Hex("#c8d4ea"), PixelCanvas.Hex("#9fb0cc"), PixelCanvas.Hex("#7486a8") };
            var gold = new[] { PixelCanvas.Hex("#fff3b0"), PixelCanvas.Hex("#f0c850"), PixelCanvas.Hex("#d8a030"), PixelCanvas.Hex("#a87018") };
            var leather = new[] { PixelCanvas.Hex("#8a5a32"), PixelCanvas.Hex("#6b4226"), PixelCanvas.Hex("#4a2c18") };
            // Pommel.
            for (int y = 2; y < 12; y++)
                for (int x = 22; x < 34; x++)
                {
                    float dx = x + 0.5f - 28f, dy = y + 0.5f - 7f;
                    if (dx * dx + dy * dy > 30f) continue;
                    c.Set(x, y, Ramp(gold, 0.4f + (dx + dy) / 14f, x, y));
                }
            Dot(c, 26.5f, 5.5f, 1.2f, gold[0]);
            // Grip with a diagonal wrap.
            for (int y = 12; y < 32; y++)
                for (int x = 24; x < 32; x++)
                {
                    bool band = Mathf.Repeat(y + x * 0.7f, 5f) < 1.4f;
                    float s = (x - 24) / 7f;
                    c.Set(x, y, band ? leather[2] : Ramp(leather, s * 0.9f, x, y));
                }
            // Crossguard with a bevel and a centre gem.
            for (int y = 32; y < 41; y++)
                for (int x = 3; x < 53; x++)
                {
                    float taper = Mathf.Abs(x + 0.5f - 28f) / 25f;
                    if (y < 32 + taper * 2f || y > 40 - taper * 2f) continue;
                    float tone = y < 34 ? 0.1f : y > 38 ? 0.85f : 0.45f + taper * 0.2f;
                    c.Set(x, y, Ramp(gold, tone, x, y));
                }
            Dot(c, 28f, 36.5f, 3.6f, PixelCanvas.Hex("#c02a2a"));
            Dot(c, 27f, 35.5f, 1.6f, PixelCanvas.Hex("#ff7a6a"));
            c.Set(26, 34, White);
            // Blade.
            for (int y = 41; y < 150; y++)
            {
                float half = y < 122 ? 9.5f : Mathf.Max(0.5f, 9.5f * (149f - y) / 27f);
                for (int x = 0; x < 56; x++)
                {
                    float dx = x + 0.5f - 28f;
                    if (Mathf.Abs(dx) > half) continue;
                    float s = dx / half;
                    float tone;
                    if (s < -0.82f) tone = 0f;                       // lit left edge
                    else if (s < -0.25f) tone = 0.25f;
                    else if (s < 0.25f) tone = y < 120 ? 0.75f : 0.45f;   // fuller groove
                    else if (s < 0.82f) tone = 0.55f;
                    else tone = 0.95f;                              // shaded right edge
                    c.Set(x, y, Ramp(steel, tone, x, y));
                }
                if (y > 46 && y < 116 && (y - 46) % 14 < 3) { c.Set(28, y, gold[1]); c.Set(27, y, gold[0]); }   // runes in the fuller
            }
            c.Outline(PixelCanvas.Hex("#22304a"));
            return c.WithPivot(28f, 2f);
        }

        static PixelCanvas HdAegis()
        {
            // Guardian crest (white for tinting): a heater shield with a bevelled rim, a cross emblem in a ring,
            // rivets and a faint field so it reads as a crest when tinted.
            var c = FxHd(68, 76);
            for (int y = 0; y < 76; y++)
            {
                float half = y < 36 ? 31f : 31f * Mathf.Sqrt(Mathf.Max(0f, 1f - (y - 35f) / 40f));
                for (int x = 0; x < 68; x++)
                {
                    float dx = x + 0.5f - 34f, ax = Mathf.Abs(dx);
                    if (ax > half || y < 2) continue;
                    float rimW = 4.5f;
                    bool rim = ax > half - rimW || y < 2 + rimW;
                    bool innerLine = !rim && (ax > half - rimW - 1.4f || y < 2 + rimW + 1.4f);
                    if (rim)
                    {
                        float bevel = dx < 0 || y < 6 ? 1f : 0.78f;
                        Put(c, x, y, Grey(bevel, 1f));
                    }
                    else if (innerLine) Put(c, x, y, Grey(0.6f, 0.9f));
                    else
                    {
                        float shade = 0.55f - y / 76f * 0.15f + (dx < 0 ? 0.05f : -0.03f);
                        Put(c, x, y, Grey(0.75f, Step(0.32f + shade * 0.15f, x, y, 5)));
                    }
                }
            }
            // Emblem: ring and cross.
            for (int y = 0; y < 76; y++)
                for (int x = 0; x < 68; x++)
                {
                    float dx = x + 0.5f - 34f, dy = y + 0.5f - 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = d > 13f && d < 16f;
                    bool cross = (Mathf.Abs(dx) < 2.6f && Mathf.Abs(dy) < 20f) || (Mathf.Abs(dy) < 2.6f && Mathf.Abs(dx) < 20f);
                    if (y > 66) cross = false;
                    if (ring || cross) Put(c, x, y, Grey(dx < 0 ? 1f : 0.88f, 1f));
                }
            foreach (var (rx, ry) in new[] { (12f, 10f), (56f, 10f), (12f, 34f), (56f, 34f), (34f, 64f) }) { Dot(c, rx, ry, 2f, Grey(0.95f, 1f)); c.Set((int)rx - 1, (int)ry - 1, White); }
            return c;
        }

        static PixelCanvas HdFeather()
        {
            // Feather: a curved rachis, a narrow left vane and a wide right vane, barbs slanting to the tip,
            // a couple of splits in the vane, fluffy down at the base and a bare quill.
            var c = FxHd(28, 60);
            for (int y = 0; y < 60; y++)
                for (int x = 0; x < 28; x++)
                {
                    float u = y / 59f;                                   // 0 tip .. 1 quill end
                    float spine = 14f + 3.2f * Mathf.Sin(u * Mathf.PI * 0.9f) - 1.6f;
                    float dx = x + 0.5f - spine;
                    if (u > 0.86f)
                    {
                        if (Mathf.Abs(dx) < 1.1f) Put(c, x, y, Grey(0.95f, 1f));
                        continue;
                    }
                    float body = Mathf.Sin(Mathf.Clamp01(u / 0.86f) * Mathf.PI * 0.92f + 0.08f);
                    float half = dx < 0 ? 6.5f * body : 11f * body;
                    float tipCut = u < 0.08f ? u / 0.08f : 1f;
                    half *= tipCut;
                    float ax = Mathf.Abs(dx);
                    if (ax > half) continue;
                    if (ax < 0.9f) { Put(c, x, y, Grey(1f, 1f)); continue; }
                    // Splits: thin gaps running along the barb direction.
                    float barbLine = y + ax * 0.75f;
                    if ((dx > 0 && Mathf.Abs(barbLine - 30f) < 0.7f && ax > 3f) || (dx < 0 && Mathf.Abs(barbLine - 21f) < 0.7f && ax > 2f)) continue;
                    bool down = u > 0.72f && ax > half * 0.45f;           // fluffy base
                    float stripe = Mathf.Repeat(barbLine, 3f);
                    float tone = down ? 0.92f : stripe < 1f ? 0.66f : (dx < 0 ? 0.92f : 0.82f);
                    float a = ax > half - 1.2f ? (down ? 0.45f : 0.75f) : 1f;
                    if (down && Hash(x, y, 23) < 0.35f) continue;
                    Put(c, x, y, Grey(tone, a));
                }
            return c;
        }

        static PixelCanvas HdHoly()
        {
            // Holy cross: a four-point light cross (long lower arm), hard bright core and stepped halo pixels.
            var c = FxHd(44, 68);
            for (int y = 0; y < 68; y++)
                for (int x = 0; x < 44; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 22f), dy = y + 0.5f - 22f;
                    float vertical = dy < 0 ? 6.4f * (1f + dy / 22f) : 6.4f * (1f - dy / 46f);
                    float horizontal = 6.4f * (1f - dx / 22f);
                    bool on = dx < vertical || Mathf.Abs(dy) < horizontal;
                    float halo = Mathf.Sqrt(dx * dx + dy * dy * 0.6f);
                    if (on)
                    {
                        bool core = dx < 1.6f || Mathf.Abs(dy) < 1.6f;
                        Put(c, x, y, Whiteish(core ? 1f : dx < vertical * 0.5f || Mathf.Abs(dy) < horizontal * 0.5f ? 0.82f : 0.6f));
                    }
                    else if (halo < 16f) Put(c, x, y, Wd(0.35f * (1f - halo / 16f), x, y, 4));
                }
            return c;
        }
    }
}
