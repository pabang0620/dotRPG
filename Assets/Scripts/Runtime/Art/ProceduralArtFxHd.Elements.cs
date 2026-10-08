using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
