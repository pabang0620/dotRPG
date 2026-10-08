using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
