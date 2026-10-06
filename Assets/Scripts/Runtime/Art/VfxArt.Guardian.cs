using UnityEngine;

namespace DotRPG
{
    /// <summary>Guardian clips: teal hexagon barriers, steel and gold shields, chains of light.</summary>
    public static partial class VfxArt
    {
        static readonly Color32[] Teal = { Hex("#ffffff"), Hex("#dafff8"), Hex("#a4f2e6"), Hex("#62dccf"), Hex("#2eaaa8"), Hex("#1a7078"), Hex("#0e4450") };
        static readonly Color32[] GoldRamp = { Hex("#ffffff"), Hex("#fff3c0"), Hex("#ffe070"), Hex("#f0b840"), Hex("#c88820"), Hex("#8a5410") };

        /// <summary>Hex cells of the dome (pointy-top grid) inside a circle, ordered from the ground up.</summary>
        static System.Collections.Generic.List<Vector2> DomeCells(float cx, float cy, float radius, float cell)
        {
            var list = new System.Collections.Generic.List<Vector2>();
            float w = cell * 1.732f, h = cell * 1.5f;
            for (int row = -8; row <= 8; row++)
                for (int col = -8; col <= 8; col++)
                {
                    float x = cx + col * w + (row & 1) * w * 0.5f, y = cy + row * h;
                    if (Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) <= radius - cell * 0.6f) list.Add(new Vector2(x, y));
                }
            list.Sort((a, b) => b.y.CompareTo(a.y));
            return list;
        }

        static PixelCanvas GuardDome(int i)
        {
            // 강철의 보루: hex tiles light up from the ground to the top until they close a barrier sphere around the body.
            var c = C(160, 176);
            var cells = DomeCells(80f, 96f, 70f, 9f);
            int lit = Mathf.CeilToInt(cells.Count * Mathf.Min(1f, (i + 1) / 6f));
            for (int k = 0; k < lit; k++)
            {
                bool fresh = k >= lit - cells.Count / 6;
                Hexagon(c, cells[k].x, cells[k].y, 8.6f, 0.6f, fresh ? Teal[0] : Teal[3], true, A(Teal[fresh ? 2 : 4], fresh ? 0.45f : 0.22f));
            }
            if (i >= 5) Ring(c, 80f, 96f, 70f, i == 5 ? 3.5f : 2f, A(Teal[1], i == 7 ? 0.6f : 1f));
            return c;
        }

        static PixelCanvas GuardDomeLoop(int i)
        {
            // The held barrier: faint hex sphere with a bright band running up it.
            var c = C(160, 176);
            var cells = DomeCells(80f, 96f, 70f, 9f);
            float band = 166f - i * 20f;
            foreach (var p in cells)
            {
                float near = Mathf.Abs(p.y - band);
                bool hot = near < 12f;
                Hexagon(c, p.x, p.y, 8.6f, 0.5f, hot ? Teal[1] : A(Teal[3], 0.55f), hot, A(Teal[2], 0.3f));
            }
            Ring(c, 80f, 96f, 70f, 1.6f, A(Teal[2], 0.8f));
            return c;
        }

        static PixelCanvas HexBurst(int i)
        {
            // Hex shards thrown outward when the barrier shoves monsters away.
            var c = C(192, 192);
            float t = i / 6f;
            for (int k = 0; k < 16; k++)
            {
                float a = k * 22.5f + Hash(k, 0, 71) * 10f;
                float r = 28f + EaseOut(t) * (52f + Hash(k, 1, 71) * 16f);
                var p = At(96f, 96f, a, r);
                float size = Mathf.Lerp(7f, 2.5f, t);
                Hexagon(c, p.x, p.y, size, 0.6f, A(Teal[t < 0.5f ? 0 : 2], 1f - t * 0.6f), true, A(Teal[3], 0.5f - t * 0.4f));
            }
            if (i < 3) Ring(c, 96f, 96f, 26f + i * 14f, 3f - i, A(Teal[1], 1f - i * 0.3f));
            return c;
        }

        /// <summary>Front view of a heater shield with a crest (rx = half width, squashed for spin).</summary>
        static void ShieldShape(PixelCanvas c, float cx, float cy, float rx, float ry, bool front, Color32[] ramp)
        {
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float u = (y + 0.5f - (cy - ry)) / (ry * 2f);
                    if (u < 0f || u > 1f) continue;
                    float half = (u < 0.5f ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - (u - 0.5f) / 0.5f))) * Mathf.Abs(rx);
                    float dx = x + 0.5f - cx;
                    if (Mathf.Abs(dx) > half) continue;
                    bool rim = Mathf.Abs(dx) > half - 2.2f || u < 0.06f;
                    float lit = 0.5f - dx / Mathf.Max(1f, Mathf.Abs(rx) * 2f) * (front ? 1f : -1f) + u * 0.25f;
                    Color32 col = rim ? ramp[front ? 1 : 3] : Pick(ramp, (front ? 0.3f : 0.6f) + lit * 0.4f, x, y);
                    if (front && Mathf.Abs(rx) > 6f && (Mathf.Abs(dx) < Mathf.Abs(rx) * 0.12f && u > 0.12f && u < 0.85f || Mathf.Abs(u - 0.38f) < 0.05f && Mathf.Abs(dx) < half * 0.75f)) col = ramp[0];
                    Put(c, x, y, col);
                }
        }

        static PixelCanvas SpinShield(int i)
        {
            // Thrown shield spinning like a coin: width follows the cosine of the turn, back face darker, speed arcs.
            var c = C(72, 72);
            float th = i / 8f * Mathf.PI * 2f;
            float rx = 22f * Mathf.Cos(th);
            ShieldShape(c, 36f, 36f, Mathf.Abs(rx) < 2f ? 2f : rx, 26f, Mathf.Cos(th) >= 0f, Teal);
            for (int k = 0; k < 2; k++)
            {
                float a0 = i * 45f + k * 180f;
                for (float a = a0; a < a0 + 70f; a += 2f)
                {
                    var p = At(36f, 36f, a, 33f);
                    if (On(1f - (a - a0) / 70f, (int)p.x, (int)p.y)) Put(c, (int)p.x, (int)p.y, A(Teal[2], 0.85f));
                }
            }
            return c;
        }

        static PixelCanvas Clang(int i)
        {
            // Blunt metal hit: a sharp eight-point star, then fragments flying and a ring.
            var c = C(64, 64);
            float[] size = { 10f, 26f, 22f, 14f, 6f, 0f };
            if (size[i] > 0f)
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float d = Polar(x, y, 32f, 32f, out float ang);
                        float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f * Mathf.Deg2Rad)), 6f);
                        float r = size[i] * (0.28f + 0.72f * spike * (Mathf.Repeat(ang + 22.5f, 90f) < 45f ? 1f : 0.6f));
                        if (d > r) continue;
                        Put(c, x, y, d < r * 0.45f ? Teal[0] : Pick(Teal, 0.2f + d / r * 0.5f, x, y));
                    }
            if (i >= 2)
            {
                Ring(c, 32f, 32f, 8f + i * 4.5f, 1.6f, A(Teal[2], 1f - i * 0.15f));
                for (int k = 0; k < 7; k++)
                {
                    float a = k * 51f + 10f;
                    var p = At(32f, 32f, a, 10f + i * 5f);
                    var q = At(p.x, p.y, a, 3f);
                    Line(c, p.x, p.y, q.x, q.y, 0.9f, Grey(0.85f));
                }
            }
            return c;
        }

        static PixelCanvas WardRing(int i)
        {
            // 수호의 맹세 ground ward (drawn round, squashed by the player): a ring of hexes lit in a chase, inner dashed ring.
            var c = C(256, 256);
            for (int k = 0; k < 12; k++)
            {
                var p = At(128f, 128f, k * 30f, 108f);
                int lag = (i - k + 12) % 12;
                bool hot = lag < 3;
                Hexagon(c, p.x, p.y, 10f, 0.8f, hot ? Teal[lag == 0 ? 0 : 1] : A(Teal[3], 0.7f), true, A(Teal[hot ? 2 : 4], hot ? 0.5f - lag * 0.12f : 0.18f));
            }
            for (float a = 0f; a < 360f; a += 1.2f)
                if (Mathf.Repeat(a + i * 5f, 20f) < 11f)
                {
                    var p = At(128f, 128f, a, 90f);
                    Put(c, (int)p.x, (int)p.y, A(Teal[2], 0.85f));
                    Put(c, (int)p.x, (int)p.y + 1, A(Teal[4], 0.6f));
                }
            Ring(c, 128f, 128f, 122f, 1.4f, A(Teal[3], 0.6f));
            return c;
        }

        static PixelCanvas RoarWave(int i)
        {
            // 대지의 호령: three sound-wave arcs rolling forward from the mouth, each thinner and fainter as it grows.
            var c = C(224, 192);
            for (int k = 0; k < 3; k++)
            {
                float r = 18f + i * 13f - k * 26f;
                if (r < 10f) continue;
                float fade = 1f - r / 200f;
                for (int y = 0; y < 192; y++)
                    for (int x = 0; x < 224; x++)
                    {
                        float d = Polar(x, y, 30f, 96f, out float ang);
                        if (Mathf.Abs(ang) > 60f) continue;
                        float w = Mathf.Lerp(7f, 2f, r / 190f) * (1f - Mathf.Abs(ang) / 75f);
                        float dd = Mathf.Abs(d - r);
                        if (dd > w) continue;
                        if (On(fade * 1.4f * (1f - Mathf.Abs(ang) / 70f), x, y)) Put(c, x, y, dd < w * 0.4f ? Grey(1f, 1f) : Grey(0.85f, 0.75f));
                    }
            }
            return c.WithPivot(30f, 96f);
        }

        static PixelCanvas ShieldBash(int i)
        {
            // 방패 강타: the shield thrusts forward with speed lines, then a star of impact at its face.
            var c = C(144, 112);
            float[] x = { 34f, 52f, 70f, 78f, 78f, 78f };
            ShieldShape(c, x[i], 56f, 26f, 34f, true, Teal);
            for (int k = 0; k < 5; k++)
            {
                float yy = 30f + k * 13f;
                float len = 20f + Hash(k, i, 81) * 20f;
                if (i < 4) Line(c, x[i] - 30f - len, yy, x[i] - 30f, yy, 0.6f, A(Teal[2], 0.8f));
            }
            if (i >= 2)
            {
                float s = i == 2 ? 14f : i == 3 ? 30f : i == 4 ? 22f : 10f;
                for (int y = 0; y < 112; y++)
                    for (int xx = 0; xx < 144; xx++)
                    {
                        float d = Polar(xx, y, 112f, 56f, out float ang);
                        float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f * Mathf.Deg2Rad)), 5f);
                        if (d < s * (0.3f + 0.7f * spike)) Put(c, xx, y, d < s * 0.3f ? Teal[0] : Teal[2]);
                    }
            }
            return c.WithPivot(40f, 56f);
        }

        static PixelCanvas Chain(int i)
        {
            // 천쇄: a chain of light bursts out of the ground and pulls taut (pivot at the ground).
            var c = C(48, 176);
            float h = Mathf.Min(160f, 30f + i * 26f);
            int links = Mathf.FloorToInt(h / 14f);
            for (int k = 0; k < links; k++)
            {
                float cy = 168f - k * 14f - 7f;
                bool side = (k & 1) == 1;
                float rx = side ? 3f : 7f, ry = 8f;
                for (int y = Mathf.FloorToInt(cy - ry - 1); y <= cy + ry + 1; y++)
                    for (int x = 14; x < 34; x++)
                    {
                        float dx = (x + 0.5f - 24f) / rx, dy = (y + 0.5f - cy) / ry;
                        float e = dx * dx + dy * dy;
                        if (e > 1f || (!side && e < 0.35f)) continue;
                        bool glint = i >= 5 && Mathf.Abs(cy - (168f - (i - 5) * 50f)) < 10f;
                        Put(c, x, y, glint ? GoldRamp[0] : Pick(GoldRamp, 0.2f + (x + 0.5f - 24f) / 14f + 0.2f, x, y));
                    }
            }
            if (i < 3) Ring(c, 24f, 168f, 10f + i * 5f, 2f, A(GoldRamp[1], 1f - i * 0.3f), 0.4f);
            return c.WithPivot(24f, 8f);
        }
    }
}
