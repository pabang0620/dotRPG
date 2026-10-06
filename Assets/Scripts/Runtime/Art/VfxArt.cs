using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Procedural frame-by-frame skill effects (see Docs/PLAN_SKILL_VFX.md). Every clip is drawn facing right in high
    /// resolution (Density 4: 64 art pixels per tile) with hard pixels, stepped tones and ordered dithering.
    /// White-based clips are tinted by the caller; coloured ones carry their own palette.
    /// </summary>
    public static partial class VfxArt
    {
        /// <summary>[PERF] Every clip name, so the loading screen can build them all before play.</summary>
        public static readonly string[] AllClips = { "f_arc", "f_x", "f_wave", "f_line", "f_cut", "f_shatter", "f_vslash", "f_spark", "impact", "g_dome", "g_domeloop", "g_hexburst", "g_spin", "g_clang", "g_ward", "g_roar", "g_bash", "g_chain", "m_fireball", "m_explode", "m_icebloom", "m_spark", "m_star", "m_starburst", "m_portal", "m_vortex", "m_collapse", "m_hit", "b_feather", "b_heal", "b_lotus", "b_bell", "b_spear", "b_cross", "b_wings", "b_pillar" };

        public const int Density = 4;
        public const float Ppu = 16f * Density;

        public static PixelCanvas[] Frames(string name)
        {
            switch (name)
            {
                // Fighter
                case "f_arc": return Frames(7, i => FighterArc(i, 7));
                case "f_x": return Frames(6, FighterX);
                case "f_wave": return Frames(4, FighterWave);
                case "f_line": return Frames(5, FighterLine);
                case "f_cut": return Frames(5, FighterCut);
                case "f_shatter": return Frames(7, FighterShatter);
                case "f_vslash": return Frames(8, FighterVerticalSlash);
                case "f_spark": return Frames(6, FighterSwordSpark);
                case "impact": return Frames(9, HeavyImpact);
                // Guardian
                case "g_dome": return Frames(8, GuardDome);
                case "g_domeloop": return Frames(8, GuardDomeLoop);
                case "g_hexburst": return Frames(7, HexBurst);
                case "g_spin": return Frames(8, SpinShield);
                case "g_clang": return Frames(6, Clang);
                case "g_ward": return Frames(12, WardRing);
                case "g_roar": return Frames(8, RoarWave);
                case "g_bash": return Frames(6, ShieldBash);
                case "g_chain": return Frames(8, Chain);
                // Arcanist
                case "m_fireball": return Frames(8, Fireball);
                case "m_explode": return Frames(12, Explosion);
                case "m_icebloom": return Frames(7, IceBloom);
                case "m_spark": return Frames(6, ElectricSpark);
                case "m_star": return Frames(4, StarShot);
                case "m_starburst": return Frames(7, StarBurst);
                case "m_portal": return Frames(9, Portal);
                case "m_vortex": return Frames(12, Vortex);
                case "m_collapse": return Frames(10, Collapse);
                case "m_hit": return Frames(6, ArcaneHit);
                // Bishop
                case "b_feather": return Frames(6, FeatherFlutter);
                case "b_heal": return Frames(9, HealRise);
                case "b_lotus": return Frames(10, Lotus);
                case "b_bell": return Frames(10, Bell);
                case "b_spear": return Frames(4, LightSpear);
                case "b_cross": return Frames(6, HolyCrossHit);
                case "b_wings": return Frames(8, Wings);
                case "b_pillar": return Frames(8, LightPillar);
            }
            return null;
        }

        static PixelCanvas[] Frames(int count, System.Func<int, PixelCanvas> draw)
        {
            var list = new PixelCanvas[count];
            for (int i = 0; i < count; i++) list[i] = draw(i);
            return list;
        }

        // ---------------------------------------------------------------- helpers

        static PixelCanvas C(int w, int h) => new PixelCanvas(w, h) { Density = Density };

        static readonly int[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        static float Bayer(int x, int y) => (Bayer4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

        /// <summary>True when a pixel at coverage <paramref name="v"/> (0..1) is drawn, with ordered dithering.</summary>
        static bool On(float v, int x, int y) => v > 0f && v >= Bayer(x, y);

        /// <summary>0..1 stepped to tones with ordered dithering.</summary>
        static float Step(float v, int x, int y, int levels = 6)
        {
            v = Mathf.Clamp01(v) * levels;
            int b = (int)v;
            if (v - b > Bayer(x, y)) b++;
            return Mathf.Clamp01(b / (float)levels);
        }

        static Color32 Pick(Color32[] ramp, float t, int x, int y)
        {
            float v = Mathf.Clamp01(t) * (ramp.Length - 1);
            int i = (int)v;
            if (i < ramp.Length - 1 && v - i > Bayer(x, y)) i++;
            return ramp[i];
        }

        static Color32 A(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(Mathf.RoundToInt(a * c.a), 0, 255));
        static Color32 Grey(float v, float a = 1f) => new Color32((byte)(255 * v), (byte)(255 * v), (byte)(255 * v), (byte)Mathf.Clamp(Mathf.RoundToInt(255 * a), 0, 255));
        static Color32 Hex(string h, byte a = 255) => PixelCanvas.Hex(h, a);

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

        /// <summary>Draws over (alpha blend); fully transparent colours are skipped instead of erasing.</summary>
        static void Put(PixelCanvas c, int x, int y, Color32 col)
        {
            if (col.a == 0 || !c.InBounds(x, y)) return;
            c.Set(x, y, col);
        }

        /// <summary>Keeps the brighter of the existing and the new pixel (light layers).</summary>
        static void Light(PixelCanvas c, int x, int y, Color32 col)
        {
            if (col.a == 0 || !c.InBounds(x, y)) return;
            var d = c.Pixels[y * c.Width + x];
            if (d.a == 0 || col.a >= d.a && (col.r + col.g + col.b) >= (d.r + d.g + d.b) - 30) c.Pixels[y * c.Width + x] = col;
            else c.Set(x, y, col);
        }

        static void Dot(PixelCanvas c, float x, float y, float r, Color32 col)
        {
            int x0 = Mathf.FloorToInt(x - r), x1 = Mathf.CeilToInt(x + r), y0 = Mathf.FloorToInt(y - r), y1 = Mathf.CeilToInt(y + r);
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - x, dy = py + 0.5f - y;
                    if (dx * dx + dy * dy <= r * r) Put(c, px, py, col);
                }
        }

        static void Line(PixelCanvas c, float x0, float y0, float x1, float y1, float r, Color32 col)
        {
            float len = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            int n = Mathf.Max(1, Mathf.CeilToInt(len * 2));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                if (r <= 0.6f) Put(c, Mathf.FloorToInt(Mathf.Lerp(x0, x1, t)), Mathf.FloorToInt(Mathf.Lerp(y0, y1, t)), col);
                else Dot(c, Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), r, col);
            }
        }

        /// <summary>Polar coordinates around (cx, cy), angle in degrees, 0 = right, counter-clockwise (canvas y is down).</summary>
        static float Polar(float x, float y, float cx, float cy, out float deg)
        {
            float dx = x + 0.5f - cx, dy = cy - (y + 0.5f);
            deg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Vector2 At(float cx, float cy, float deg, float r) =>
            new Vector2(cx + Mathf.Cos(deg * Mathf.Deg2Rad) * r, cy - Mathf.Sin(deg * Mathf.Deg2Rad) * r);

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        /// <summary>A sparkle: plus shape with a bright centre.</summary>
        static void Twinkle(PixelCanvas c, float x, float y, float size, Color32 col)
        {
            int s = Mathf.Max(1, Mathf.RoundToInt(size));
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            for (int k = -s; k <= s; k++) { Put(c, ix + k, iy, A(col, 1f - Mathf.Abs(k) / (s + 1f))); Put(c, ix, iy + k, A(col, 1f - Mathf.Abs(k) / (s + 1f))); }
            Put(c, ix, iy, new Color32(255, 255, 255, 255));
        }

        /// <summary>A ring band with dithered soft edges.</summary>
        static void Ring(PixelCanvas c, float cx, float cy, float r, float width, Color32 col, float squash = 1f)
        {
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float dx = x + 0.5f - cx, dy = (y + 0.5f - cy) / squash;
                    float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r);
                    if (d > width) continue;
                    float v = 1f - d / width;
                    if (On(v * 1.4f, x, y)) Put(c, x, y, A(col, d < width * 0.35f ? 1f : 0.7f));
                }
        }

        /// <summary>A flat hexagon outline (pointy-top) at (cx, cy) with circumradius r.</summary>
        static void Hexagon(PixelCanvas c, float cx, float cy, float r, float thick, Color32 col, bool fill = false, Color32 fillCol = default)
        {
            var pts = new Vector2[6];
            for (int k = 0; k < 6; k++) pts[k] = new Vector2(cx + Mathf.Cos((60f * k + 30f) * Mathf.Deg2Rad) * r, cy + Mathf.Sin((60f * k + 30f) * Mathf.Deg2Rad) * r);
            if (fill)
                for (int y = Mathf.FloorToInt(cy - r); y <= cy + r; y++)
                    for (int x = Mathf.FloorToInt(cx - r); x <= cx + r; x++)
                        if (InHex(x + 0.5f - cx, y + 0.5f - cy, r * 0.92f)) Put(c, x, y, fillCol);
            for (int k = 0; k < 6; k++) Line(c, pts[k].x, pts[k].y, pts[(k + 1) % 6].x, pts[(k + 1) % 6].y, thick, col);
        }

        static bool InHex(float dx, float dy, float r)
        {
            dx = Mathf.Abs(dx); dy = Mathf.Abs(dy);
            return dx <= r * 0.866f && 0.5f * dx + 0.866f * dy <= r * 0.866f;
        }
    }
}
