using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Skill effect sprites: soft glow, shock ring, magic circle, whirlwind trail, ground frost and
    /// cracks, rock spikes, ice crystals and shards, snow, sparks and slash marks.
    /// Most are white so the effect code can tint them; the ice and rock pieces carry their own colours.
    /// Sizes are chosen so each sprite is drawn at (or scaled up from) its natural size, never shrunk,
    /// which keeps the pixel lines crisp.
    /// </summary>
    public static partial class ProceduralArt
    {
        static readonly Color32 IceDeep = PixelCanvas.Hex("#3a78d8");
        static readonly Color32 IceMid = PixelCanvas.Hex("#79c2ff");
        static readonly Color32 IceLight = PixelCanvas.Hex("#c4ecff");
        static readonly Color32 IceWhite = PixelCanvas.Hex("#f4fcff");
        static readonly Color32 IceOutline = PixelCanvas.Hex("#23458f");

        static Color32 Whiteish(float alpha) =>
            new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));

        /// <summary>Distance from a pixel centre to the canvas centre, plus its angle in degrees (0 = right, counter-clockwise, y up).</summary>
        static float Polar(PixelCanvas c, int x, int y, out float angle)
        {
            float dx = x + 0.5f - c.Width * 0.5f;
            float dy = c.Height * 0.5f - (y + 0.5f);
            angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Writes a pixel without blending.</summary>
        static void Put(PixelCanvas c, int x, int y, Color32 color) => c.Pixels[y * c.Width + x] = color;

        /// <summary>One ice crystal from a base point to a tip, lit from the left.</summary>
        static void Crystal(PixelCanvas c, float bx, float by, float tx, float ty, float halfWidth)
        {
            float ax = tx - bx, ay = ty - by;
            float len = Mathf.Sqrt(ax * ax + ay * ay);
            ax /= len; ay /= len;
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float px = x + 0.5f - bx, py = y + 0.5f - by;
                    float along = px * ax + py * ay;   // 0 at the base, len at the tip
                    float side = px * -ay + py * ax;   // signed distance across the crystal
                    if (along < 0f || along > len) continue;
                    float u = along / len;
                    float w = u < 0.68f ? halfWidth : halfWidth * (1f - u) / 0.32f;
                    if (Mathf.Abs(side) > w) continue;
                    var col = side < -w * 0.3f ? IceLight : side > w * 0.35f ? IceDeep : IceMid;
                    if (Mathf.Abs(side + w * 0.5f) < 0.5f && u > 0.15f && u < 0.85f) col = IceWhite;
                    c.Set(x, y, col);
                }
        }

        static PixelCanvas DrawSkillFx(string kind)
        {
            switch (kind)
            {
                case "glow":
                {
                    // Soft round light (drawn smooth, see SpriteLibrary). 2 units across at scale 1.
                    var c = new PixelCanvas(32, 32);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float d = Polar(c, x, y, out _) / 16f;
                            if (d >= 1f) continue;
                            Put(c, x, y, Whiteish(Mathf.Pow(1f - d, 1.7f)));
                        }
                    return c;
                }
                case "shock":
                {
                    // Shock-wave ring: bright outer edge with a faint trail inside. Radius 1 unit at scale 1.
                    var c = new PixelCanvas(32, 32);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float d = Polar(c, x, y, out _);
                            if (d >= 16f || d < 7f) continue;
                            float a = d >= 14.6f ? 1f : d >= 13.4f ? 0.6f : 0.28f * (d - 7f) / 6.4f;
                            Put(c, x, y, Whiteish(a));
                        }
                    return c;
                }
                case "rune":
                {
                    // Magic circle: two rings with rune beads between them, a hexagram and a small inner ring.
                    var c = new PixelCanvas(32, 32);
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float d = Polar(c, x, y, out _);
                            if (d >= 14.6f && d < 15.8f) Put(c, x, y, Whiteish(1f));
                            else if (d >= 11.2f && d < 12.1f) Put(c, x, y, Whiteish(0.85f));
                            else if (d >= 4.3f && d < 5.2f) Put(c, x, y, Whiteish(0.8f));
                        }
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k * 30f * Mathf.Deg2Rad;
                        int px = Mathf.FloorToInt(16f + Mathf.Cos(a) * 13.3f);
                        int py = Mathf.FloorToInt(16f - Mathf.Sin(a) * 13.3f);
                        if (k % 2 == 0) c.Rect(px, py, 2, 2, White);
                        else c.Set(px, py, Whiteish(0.9f));
                    }
                    for (int t = 0; t < 2; t++)
                        for (int k = 0; k < 3; k++)
                        {
                            float a0 = (90f + t * 180f + k * 120f) * Mathf.Deg2Rad;
                            float a1 = (90f + t * 180f + (k + 1) * 120f) * Mathf.Deg2Rad;
                            c.Line(Mathf.RoundToInt(15.5f + Mathf.Cos(a0) * 10.5f), Mathf.RoundToInt(15.5f - Mathf.Sin(a0) * 10.5f),
                                Mathf.RoundToInt(15.5f + Mathf.Cos(a1) * 10.5f), Mathf.RoundToInt(15.5f - Mathf.Sin(a1) * 10.5f), Whiteish(0.75f));
                        }
                    return c;
                }
                case "swoosh":
                {
                    // Whirlwind blade: sharp leading edge at angle 0, a tapering trail behind it
                    // (counter-clockwise), so it reads right when spun clockwise. Outer edge ~1.7 units.
                    var c = new PixelCanvas(56, 56);
                    const float outer = 27f, sweep = 300f;
                    for (int y = 0; y < 56; y++)
                        for (int x = 0; x < 56; x++)
                        {
                            float d = Polar(c, x, y, out float ang);
                            if (ang > sweep || d > outer) continue;
                            float s = ang / sweep;
                            float thick = Mathf.Lerp(6.5f, 1f, s);
                            float fade = Mathf.Pow(1f - s, 1.4f);
                            if (d >= outer - thick) Put(c, x, y, Whiteish(fade * (d >= outer - thick * 0.5f ? 1f : 0.7f)));
                            else if (d >= outer - thick - 7f) Put(c, x, y, Whiteish(fade * 0.22f * (d - (outer - thick - 7f)) / 7f));
                        }
                    return c;
                }
                case "zap":
                {
                    // Electric impact star: white core, cyan rays, violet tips and a dark outline so it
                    // reads on light ground as well as dark.
                    var c = new PixelCanvas(24, 24);
                    var cyan = PixelCanvas.Hex("#8ff0ff");
                    var violet = PixelCanvas.Hex("#9b7bff");
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * 45f * Mathf.Deg2Rad + (k % 2 == 0 ? 0f : 0.12f);
                        float len = k % 2 == 0 ? 10.5f : 6.5f;
                        for (float r = 0f; r <= len; r += 0.5f)
                        {
                            float jig = Mathf.Sin(r * 1.7f + k) * 0.8f; // zig-zag
                            float px = 12f + Mathf.Cos(a) * r + Mathf.Sin(a) * jig;
                            float py = 12f - Mathf.Sin(a) * r + Mathf.Cos(a) * jig;
                            var col = r < 2.5f ? White : r < len * 0.7f ? cyan : violet;
                            int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py);
                            c.Set(ix, iy, col);
                            if (r < len * 0.5f) c.Set(ix + 1, iy, col);
                        }
                    }
                    c.Circle(12f, 12f, 2.2f, White);
                    c.Outline(PixelCanvas.Hex("#3a2a8a"));
                    return c;
                }
                case "blade":
                {
                    // Gold whirlwind blade with a baked palette (white edge, gold body, orange tail and a
                    // thin dark rim) so it stays readable on light ground. Same shape as fx_swoosh.
                    var c = new PixelCanvas(56, 56);
                    var edge = PixelCanvas.Hex("#fffbe8");
                    var bright = PixelCanvas.Hex("#ffe070");
                    var gold = PixelCanvas.Hex("#ffb52a");
                    var orange = PixelCanvas.Hex("#e8761c");
                    var rim = PixelCanvas.Hex("#8a3a12");
                    const float outer = 27f, sweep = 290f;
                    for (int y = 0; y < 56; y++)
                        for (int x = 0; x < 56; x++)
                        {
                            float d = Polar(c, x, y, out float ang);
                            if (ang > sweep || d > outer) continue;
                            float s = ang / sweep;                  // 0 at the leading edge, 1 at the tail end
                            float thick = Mathf.Lerp(7f, 1.2f, s);
                            float fade = Mathf.Pow(1f - s, 1.2f);
                            float inner = outer - thick;
                            if (d < inner - 6f) continue;
                            if (d < inner)
                            {
                                // Faint motion blur inside the blade.
                                Put(c, x, y, PixelCanvas.WithAlpha(gold, (byte)(fade * 70f * (d - (inner - 6f)) / 6f)));
                                continue;
                            }
                            float u = (outer - d) / thick;          // 0 at the outer edge, 1 at the inner edge
                            Color32 col = d > outer - 1f ? rim
                                : s < 0.06f ? edge
                                : u < 0.3f ? (s < 0.35f ? edge : bright)
                                : u < 0.65f ? (s < 0.2f ? bright : gold)
                                : orange;
                            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * fade * (d > outer - 1f ? 0.75f : 1f)), 0, 255);
                            Put(c, x, y, PixelCanvas.WithAlpha(col, a));
                        }
                    return c;
                }
                case "frost":
                {
                    // Frosted ground patch with an uneven edge and crystal cracks. Radius ~1.5 units.
                    var c = new PixelCanvas(48, 48);
                    for (int y = 0; y < 48; y++)
                        for (int x = 0; x < 48; x++)
                        {
                            float d = Polar(c, x, y, out float ang);
                            float rad = ang * Mathf.Deg2Rad;
                            float r = 21.5f + 1.3f * Mathf.Sin(rad * 6f) + 0.9f * Mathf.Sin(rad * 11f + 0.7f);
                            if (d > r) continue;
                            float t = d / r;
                            Color32 col;
                            if (d > r - 1.3f) col = PixelCanvas.WithAlpha(IceWhite, 230);
                            else if (d > r - 3f) col = PixelCanvas.WithAlpha(IceLight, 170);
                            else col = PixelCanvas.WithAlpha(t < 0.5f ? IceMid : IceLight, (byte)(50 + 80 * t * t));
                            Put(c, x, y, col);
                        }
                    for (int k = 0; k < 9; k++)
                    {
                        float a = (k * 40f + (k % 3) * 7f) * Mathf.Deg2Rad;
                        float len = 17f + (k % 2) * 3f;
                        c.Line(24, 24, Mathf.RoundToInt(24 + Mathf.Cos(a) * len), Mathf.RoundToInt(24 - Mathf.Sin(a) * len), PixelCanvas.WithAlpha(IceWhite, 200));
                        float fa = a + (k % 2 == 0 ? 0.5f : -0.5f);
                        int mx = Mathf.RoundToInt(24 + Mathf.Cos(a) * len * 0.55f), my = Mathf.RoundToInt(24 - Mathf.Sin(a) * len * 0.55f);
                        c.Line(mx, my, Mathf.RoundToInt(mx + Mathf.Cos(fa) * 6f), Mathf.RoundToInt(my - Mathf.Sin(fa) * 6f), PixelCanvas.WithAlpha(IceWhite, 170));
                    }
                    return c;
                }
                case "crack":
                {
                    // Jagged ground cracks with a lit lower edge, squashed vertically like the ground plane.
                    var c = new PixelCanvas(32, 24);
                    var dark = PixelCanvas.Hex("#3b2a1e", 235);
                    var lit = PixelCanvas.Hex("#e2cfa4", 160);
                    var rng = new System.Random(7);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = (k * 60f + rng.Next(-15, 15)) * Mathf.Deg2Rad;
                        float px = 16f, py = 12f;
                        int steps = 4 + rng.Next(0, 3);
                        for (int s = 0; s < steps; s++)
                        {
                            float a2 = a + (float)(rng.NextDouble() - 0.5) * 0.9f;
                            float nx = px + Mathf.Cos(a2) * 2.6f, ny = py - Mathf.Sin(a2) * 2.6f * 0.75f;
                            c.Line(Mathf.RoundToInt(px), Mathf.RoundToInt(py) + 1, Mathf.RoundToInt(nx), Mathf.RoundToInt(ny) + 1, lit);
                            c.Line(Mathf.RoundToInt(px), Mathf.RoundToInt(py), Mathf.RoundToInt(nx), Mathf.RoundToInt(ny), dark);
                            px = nx; py = ny;
                        }
                    }
                    c.Rect(15, 11, 2, 2, dark); c.Set(14, 12, dark); c.Set(17, 11, dark);
                    return c;
                }
                case "spike":
                {
                    // Rock spike bursting out of the ground (pivot at its base).
                    var c = new PixelCanvas(12, 16);
                    for (int y = 1; y < 16; y++)
                    {
                        float w = Mathf.Lerp(0.6f, 4.6f, (y - 1) / 14f);
                        for (int x = 0; x < 12; x++)
                        {
                            float dx = x + 0.5f - 6f;
                            if (Mathf.Abs(dx) > w) continue;
                            c.Set(x, y, dx < -w * 0.35f ? StoneLight : dx > w * 0.4f ? StoneDark : Stone);
                        }
                    }
                    c.Set(5, 8, StoneDark); c.Set(6, 9, StoneDark); c.Set(6, 12, StoneDark); c.Set(7, 13, StoneDark);
                    c.Outline(Outline);
                    return c.WithBottomPivot();
                }
                case "ice":
                {
                    // Cluster of three ice crystals (pivot at the base).
                    var c = new PixelCanvas(14, 18);
                    Crystal(c, 5f, 17.5f, 1.5f, 7f, 1.8f);
                    Crystal(c, 9f, 17.5f, 12.5f, 8.5f, 1.7f);
                    Crystal(c, 7f, 17.5f, 7f, 1f, 2.4f);
                    c.Outline(IceOutline);
                    return c.WithBottomPivot();
                }
                case "shard":
                {
                    // Small ice shard pointing right (rotated along its flight at runtime).
                    var c = new PixelCanvas(10, 5);
                    c.HLine(3, 5, 0, IceLight);
                    c.HLine(1, 7, 1, IceLight);
                    c.HLine(0, 9, 2, IceMid);
                    c.HLine(4, 9, 2, IceWhite);
                    c.HLine(1, 7, 3, IceDeep);
                    c.HLine(3, 5, 4, IceDeep);
                    return c;
                }
                case "snow":
                {
                    var c = new PixelCanvas(5, 5);
                    c.VLine(2, 0, 4, Whiteish(0.95f)); c.HLine(0, 4, 2, Whiteish(0.95f));
                    c.Set(1, 1, Whiteish(0.6f)); c.Set(3, 1, Whiteish(0.6f)); c.Set(1, 3, Whiteish(0.6f)); c.Set(3, 3, Whiteish(0.6f));
                    c.Set(2, 2, White);
                    return c;
                }
                case "spark":
                {
                    var c = new PixelCanvas(5, 5);
                    c.Set(2, 0, Whiteish(0.55f)); c.Set(2, 4, Whiteish(0.55f)); c.Set(0, 2, Whiteish(0.55f)); c.Set(4, 2, Whiteish(0.55f));
                    c.Set(2, 1, White); c.Set(2, 3, White); c.Set(1, 2, White); c.Set(3, 2, White); c.Set(2, 2, White);
                    return c;
                }
                case "streak":
                {
                    // Motion streak, bright head on the right.
                    var c = new PixelCanvas(8, 3);
                    for (int x = 0; x < 8; x++)
                    {
                        float t = (x + 1) / 8f;
                        Put(c, x, 1, Whiteish(0.2f + 0.8f * t));
                        if (x >= 4) { Put(c, x, 0, Whiteish(0.25f * t)); Put(c, x, 2, Whiteish(0.25f * t)); }
                    }
                    return c;
                }
                case "cut":
                {
                    // Thin lens-shaped slash mark for melee hits.
                    var c = new PixelCanvas(22, 7);
                    for (int x = 0; x < 22; x++)
                    {
                        float h = 3.3f * Mathf.Pow(Mathf.Sin((x + 0.5f) / 22f * Mathf.PI), 0.8f);
                        for (int y = 0; y < 7; y++)
                        {
                            float dy = Mathf.Abs(y + 0.5f - 3.5f);
                            if (dy <= h) Put(c, x, y, Whiteish(dy < h * 0.45f ? 1f : 0.55f));
                        }
                    }
                    return c;
                }
                case "crescent":
                {
                    // Flying sword wave: a crescent bulging to the right (its direction of travel),
                    // white cutting edge, gold body, fading tips.
                    var c = new PixelCanvas(20, 32);
                    var edge = PixelCanvas.Hex("#fffbe8");
                    var light = PixelCanvas.Hex("#ffe9a0");
                    var gold = PixelCanvas.Hex("#ffb52a");
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 20; x++)
                        {
                            float ox = (x + 0.5f - 4f) / 15f, oy = (y + 0.5f - 16f) / 15.5f;
                            float ix = (x + 0.5f + 1f) / 13f, iy = (y + 0.5f - 16f) / 13.5f;
                            float outer = ox * ox + oy * oy;
                            if (outer > 1f || ix * ix + iy * iy <= 1f) continue;
                            float depth = 1f - Mathf.Sqrt(outer);
                            var col = depth < 0.07f ? edge : depth < 0.16f ? light : gold;
                            byte a = (byte)(Mathf.Abs(y + 0.5f - 16f) > 12f ? 170 : 255);
                            Put(c, x, y, PixelCanvas.WithAlpha(col, a));
                        }
                    return c;
                }
                case "bigsword":
                {
                    // Giant falling sword, tip down (pivot at the tip), for 천검 강림.
                    var c = new PixelCanvas(14, 38);
                    var steel = PixelCanvas.Hex("#e8f0ff");
                    var steelDark = PixelCanvas.Hex("#9fb0cc");
                    var goldDark = PixelCanvas.Hex("#b07818");
                    // Pommel, grip and crossguard at the top.
                    c.Rect(6, 1, 2, 2, Gold);
                    c.Rect(6, 3, 2, 5, PixelCanvas.Hex("#6b4226"));
                    c.Set(6, 4, PixelCanvas.Hex("#8a5a32")); c.Set(6, 6, PixelCanvas.Hex("#8a5a32"));
                    c.Rect(1, 8, 12, 2, Gold);
                    c.HLine(1, 12, 9, goldDark);
                    c.Rect(6, 8, 2, 2, PixelCanvas.Hex("#e0403a")); // gem in the guard
                    // Blade.
                    for (int y = 10; y < 37; y++)
                    {
                        int half = y < 31 ? 2 : Mathf.Max(0, 2 - (y - 30) / 2);
                        for (int x = 7 - half - 1; x <= 6 + half + 1; x++)
                        {
                            if (x < 0 || x > 13) continue;
                            bool left = x < 7;
                            c.Set(x, y, left ? steel : steelDark);
                        }
                        c.Set(7, y, White);
                    }
                    c.Set(6, 14, Gold); c.Set(7, 18, Gold); c.Set(6, 22, Gold); // runes
                    c.Outline(PixelCanvas.Hex("#2b3a5a"));
                    return c.WithPivot(7f, 0.5f);
                }
                case "frostorb":
                {
                    // Ice-lightning orb: faceted crystal core, cold halo, violet sparks on the rim.
                    var c = new PixelCanvas(18, 18);
                    var halo = PixelCanvas.Hex("#7fd4ff", 110);
                    var ice = PixelCanvas.Hex("#9fe3ff");
                    var iceLight = PixelCanvas.Hex("#e4fbff");
                    var iceDark = PixelCanvas.Hex("#4fa8e8");
                    var deep = PixelCanvas.Hex("#2f6fb8");
                    c.Circle(8.5f, 8.5f, 8.3f, halo);
                    c.Circle(8.5f, 8.5f, 6.2f, deep);
                    c.Circle(8.5f, 8.5f, 5.4f, ice);
                    // Facets: light upper-left, dark lower-right, a bright seam between them.
                    for (int y = 0; y < 18; y++)
                        for (int x = 0; x < 18; x++)
                        {
                            float dx = x + 0.5f - 8.5f, dy = y + 0.5f - 8.5f;
                            if (dx * dx + dy * dy > 5.4f * 5.4f) continue;
                            if (dx + dy < -2.5f) c.Set(x, y, iceLight);
                            else if (dx + dy > 3f) c.Set(x, y, iceDark);
                            else if (Mathf.Abs(dx - dy) < 0.8f) c.Set(x, y, iceLight);
                        }
                    c.Circle(8.5f, 8.5f, 1.9f, White);
                    c.Set(6, 5, White); c.Set(5, 6, White);
                    var spark = PixelCanvas.Hex("#c8b4ff");
                    foreach (var (x, y) in new[] { (1, 8), (16, 9), (8, 0), (9, 17), (3, 3), (14, 14) }) c.Set(x, y, spark);
                    return c;
                }
                case "flame":
                {
                    // Small fire puff (particle).
                    var c = new PixelCanvas(8, 8);
                    c.Circle(4f, 4f, 3.8f, PixelCanvas.Hex("#e8401c", 200));
                    c.Circle(4f, 4.3f, 2.8f, PixelCanvas.Hex("#ff8a24"));
                    c.Circle(4f, 4.6f, 1.6f, PixelCanvas.Hex("#ffe070"));
                    return c;
                }
                case "meteor":
                {
                    // Burning rock with glowing cracks.
                    var c = new PixelCanvas(14, 14);
                    c.Circle(7f, 7f, 6f, PixelCanvas.Hex("#5a4032"));
                    c.PaintEllipse(5f, 5f, 3.5f, 3f, PixelCanvas.Hex("#7a5a44"));
                    c.PaintEllipse(9f, 9.5f, 3f, 2.5f, PixelCanvas.Hex("#3a2a22"));
                    var lava = PixelCanvas.Hex("#ff8a24");
                    c.Line(3, 7, 7, 6, lava); c.Line(7, 6, 10, 9, lava); c.Line(7, 6, 8, 2, lava);
                    c.Set(5, 10, PixelCanvas.Hex("#ffd84a")); c.Set(9, 4, PixelCanvas.Hex("#ffd84a"));
                    c.Outline(PixelCanvas.Hex("#2b1d16"));
                    return c;
                }
                case "scorch":
                {
                    // Burnt ground left by fire and lightning, squashed like the ground plane.
                    var c = new PixelCanvas(32, 20);
                    var rng = new System.Random(3);
                    for (int y = 0; y < 20; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            float nx = (x + 0.5f - 16f) / 15f, ny = (y + 0.5f - 10f) / 9f;
                            float d = Mathf.Sqrt(nx * nx + ny * ny) + (float)(rng.NextDouble() - 0.5) * 0.18f;
                            if (d > 1f) continue;
                            byte a = (byte)(d < 0.5f ? 190 : 190 * (1f - (d - 0.5f) / 0.5f));
                            Put(c, x, y, new Color32(34, 26, 22, a));
                        }
                    for (int k = 0; k < 7; k++)
                        c.Set(8 + rng.Next(0, 16), 5 + rng.Next(0, 10), PixelCanvas.Hex("#ff8a24", 220));
                    return c;
                }
                case "star":
                {
                    // Stun star.
                    var c = new PixelCanvas(7, 7);
                    var y = PixelCanvas.Hex("#ffd34a");
                    c.VLine(3, 0, 6, y); c.HLine(0, 6, 3, y);
                    c.Rect(2, 2, 3, 3, y);
                    c.Set(3, 3, White);
                    c.Outline(PixelCanvas.Hex("#8a4a10"));
                    return c;
                }
            }
            return null;
        }
    }
}
