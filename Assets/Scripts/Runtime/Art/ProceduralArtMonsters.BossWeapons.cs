using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Boss weapons (always drawn pointing up from the grip) ----------

        static void BossWeapon(PixelCanvas c, string id, BigPose p)
        {
            int gx = p.gx, gy = p.gy;
            switch (id)
            {
                case "boss_gold_foreman":
                {
                    // Golden pickaxe.
                    c.Rect(gx - 1, gy - 17, 2, 20, Wood);
                    c.VLine(gx, gy - 17, gy + 2, WoodDark);
                    c.HLine(gx - 7, gx + 6, gy - 19, Gold);
                    c.HLine(gx - 6, gx + 5, gy - 18, Gold);
                    c.HLine(gx - 3, gx + 2, gy - 20, Yellow);
                    c.Set(gx - 8, gy - 17, Gold); c.Set(gx - 8, gy - 16, PixelCanvas.Shade(Gold, 0.7f));
                    c.Set(gx + 7, gy - 17, Gold); c.Set(gx + 7, gy - 16, PixelCanvas.Shade(Gold, 0.7f));
                    c.HLine(gx - 5, gx + 4, gy - 18, PixelCanvas.Shade(Gold, 0.8f));
                    c.Set(gx - 2, gy - 19, White);
                    break;
                }
                case "boss_mine_captain":
                {
                    // Sledgehammer.
                    c.Rect(gx - 1, gy - 15, 2, 18, Bark);
                    c.VLine(gx, gy - 15, gy + 2, BarkDark);
                    c.Rect(gx - 5, gy - 23, 10, 8, MonIronDark);
                    c.HLine(gx - 5, gx + 4, gy - 23, MonIron);
                    c.VLine(gx - 5, gy - 23, gy - 16, MonIron);
                    c.HLine(gx - 5, gx + 4, gy - 17, PixelCanvas.Shade(MonIronDark, 0.7f));
                    c.HLine(gx - 5, gx + 4, gy - 20, MonLeather);
                    c.Set(gx - 3, gy - 22, MonIronLight);
                    break;
                }
                case "boss_lich":
                {
                    // Staff with a glowing skull orb.
                    var orb = p.attack ? MonSoulLight : MonSoul;
                    c.Rect(gx - 1, gy - 18, 2, 22, BarkDark);
                    c.VLine(gx, gy - 18, gy + 3, PixelCanvas.Shade(BarkDark, 0.7f));
                    c.Circle(gx, gy - 21.5f, 3.6f, orb);
                    c.Rect(gx - 2, gy - 23, 4, 3, PixelCanvas.Hex("#e6ecd8"));
                    c.Set(gx - 1, gy - 22, MonSocket); c.Set(gx + 1, gy - 22, MonSocket);
                    c.Set(gx - 3, gy - 18, Bark); c.Set(gx + 2, gy - 18, Bark);
                    if (p.attack) { c.Set(gx - 5, gy - 22, MonSoul); c.Set(gx + 5, gy - 24, MonSoul); c.Set(gx, gy - 27, MonSoulLight); }
                    break;
                }
                case "boss_archer_chief":
                {
                    // Longbow: arc bulging right, string on the grip line.
                    int top = gy - 20, bottom = gy + 4;
                    c.VLine(gx, top + 1, bottom - 1, FlagLight);
                    c.Set(gx, top, Wood); c.Set(gx, bottom, Wood);
                    c.VLine(gx + 1, top, top + 2, Wood); c.VLine(gx + 1, bottom - 2, bottom, Wood);
                    c.VLine(gx + 2, top + 3, bottom - 3, Wood);
                    c.VLine(gx + 3, top + 7, bottom - 7, WoodDark);
                    c.Rect(gx + 1, gy - 9, 3, 3, MonLeather);
                    if (p.attack)
                    {
                        c.VLine(gx - 1, gy - 18, gy - 1, WoodLight);
                        c.Set(gx - 1, gy - 19, Steel); c.Set(gx - 1, gy - 20, Steel);
                        c.Set(gx - 2, gy, Red); c.Set(gx, gy, Red);
                    }
                    break;
                }
                case "boss_armory_warden":
                {
                    // Halberd.
                    c.Rect(gx - 1, gy - 22, 2, 26, Bark);
                    c.VLine(gx, gy - 22, gy + 3, BarkDark);
                    c.VLine(gx - 1, gy - 27, gy - 23, Steel); c.Set(gx - 1, gy - 28, White);
                    c.Rect(gx + 1, gy - 22, 5, 5, Steel);
                    c.VLine(gx + 6, gy - 21, gy - 18, Steel);
                    c.VLine(gx + 5, gy - 22, gy - 17, SteelDark);
                    c.Rect(gx - 4, gy - 21, 3, 2, SteelDark);
                    c.Set(gx - 1, gy - 20, Gold);
                    break;
                }
                case "boss_skeleton_king":
                {
                    // Bone greatsword with a red gem.
                    var blade = PixelCanvas.Hex("#e8e2d2");
                    var edge = PixelCanvas.Hex("#b8ae98");
                    c.Rect(gx - 1, gy - 1, 2, 4, handleColor);
                    c.Rect(gx - 5, gy - 3, 10, 2, Gold);
                    c.Set(gx - 1, gy - 3, Red); c.Set(gx, gy - 3, Red);
                    c.Rect(gx - 2, gy - 23, 4, 20, blade);
                    c.VLine(gx + 1, gy - 22, gy - 4, edge);
                    c.HLine(gx - 1, gx, gy - 24, blade); c.Set(gx - 1, gy - 25, blade);
                    for (int y = gy - 20; y <= gy - 6; y += 5) c.Set(gx + 2, y, blade);
                    c.VLine(gx - 1, gy - 20, gy - 6, MonCapeDark);
                    break;
                }
            }
        }

        // =====================================================================================
        // Keys "mon_*": projectiles, telegraphs, summon circle
        // =====================================================================================

        static PixelCanvas DrawMonsterKey(string[] p)
        {
            switch (p[1])
            {
                case "arrow": return DrawMonArrow();
                case "bolt": return DrawMonBolt(p.Length > 2 && p[2] == "big");
                case "summon": return DrawSummonCircle();
                case "tele":
                {
                    var t = DrawTelegraph(p);
                    if (t != null && p[2] != "rect" && p[2] != "rectfill") t.Density = 2;
                    return t;
                }
            }
            return null;
        }

        static PixelCanvas DrawMonArrow()
        {
            var c = new PixelCanvas(12, 5);
            c.HLine(2, 9, 2, WoodLight);
            c.Set(10, 2, Steel); c.Set(11, 2, White); c.Set(10, 1, SteelDark); c.Set(10, 3, SteelDark);
            c.Set(0, 1, Red); c.Set(1, 1, Red); c.Set(0, 3, Red); c.Set(1, 3, Red); c.Set(1, 2, White);
            c.Outline(PixelCanvas.WithAlpha(Outline, 200));
            return c.WithPivot(6, 2.5f);
        }

        static PixelCanvas DrawMonBolt(bool big)
        {
            int s = big ? 12 : 9;
            var c = new PixelCanvas(s, s);
            float m = s / 2f;
            c.Circle(m, m, m - 0.5f, PixelCanvas.WithAlpha(MonSoul, 120));
            c.Circle(m, m, m - 2f, MonSoul);
            c.Circle(m - 0.5f, m - 0.5f, m - 3.3f, MonSoulLight);
            c.Set((int)m - 1, (int)m - 1, White);
            return c;
        }

        static PixelCanvas DrawSummonCircle()
        {
            var c = new PixelCanvas(32, 32);
            var col = new Color32(190, 120, 255, 230);
            var dim = new Color32(150, 80, 230, 150);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = x + 0.5f - 16f, dy = y + 0.5f - 16f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 14.6f && d <= 15.8f) c.Set(x, y, col);
                    else if (d > 11.4f && d <= 12.2f) c.Set(x, y, dim);
                }
            // Five-pointed star.
            var pts = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI * 2f / 5f;
                pts[i] = new Vector2(16f + Mathf.Cos(a) * 12f, 16f - Mathf.Sin(a) * 12f);
            }
            for (int i = 0; i < 5; i++)
            {
                var a = pts[i];
                var b = pts[(i + 2) % 5];
                c.Line(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y), Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y), col);
            }
            // Rune ticks on the outer ring.
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                c.Set(Mathf.RoundToInt(16f + Mathf.Cos(a) * 13.5f), Mathf.RoundToInt(16f + Mathf.Sin(a) * 13.5f), White);
            }
            return c;
        }

        /// <summary>
        /// Telegraph textures, white so the telegraph can tint them. mon_tele_ring (outline + faint
        /// fill), mon_tele_disc (solid), mon_tele_rect / mon_tele_rectfill (9-slice), mon_tele_cone_{deg}
        /// and mon_tele_conefill_{deg} (apex at the sprite centre, opening towards +x), mon_tele_donut_{innerPct}.
        /// All 128px at density 2 = 4 world units across.
        /// </summary>
        static PixelCanvas DrawTelegraph(string[] p)
        {
            const int S = 128; // density 2: still 4 world units across, smooth edges when scaled up
            var edge = new Color32(255, 255, 255, 255);
            var fill = new Color32(255, 255, 255, 90);
            switch (p[2])
            {
                case "ring":
                case "disc":
                {
                    bool ring = p[2] == "ring";
                    var c = new PixelCanvas(S, S);
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d > S / 2f) continue;
                            if (!ring) c.Set(x, y, edge);
                            else c.Set(x, y, d > S / 2f - 3.5f ? edge : fill);
                        }
                    return c;
                }
                case "donutfill":
                {
                    float inner = (p.Length > 3 ? int.Parse(p[3]) : 40) / 100f * S / 2f;
                    var c = new PixelCanvas(S, S);
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d <= S / 2f && d >= inner) c.Set(x, y, edge);
                        }
                    return c;
                }
                case "donut":
                {
                    float inner = (p.Length > 3 ? int.Parse(p[3]) : 40) / 100f * S / 2f;
                    var c = new PixelCanvas(S, S);
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d > S / 2f || d < inner) continue;
                            c.Set(x, y, d > S / 2f - 3.5f || d < inner + 3.5f ? edge : fill);
                        }
                    return c;
                }
                case "rect":
                case "rectfill":
                {
                    var c = new PixelCanvas(16, 16);
                    bool frame = p[2] == "rect";
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            bool border = x < 2 || y < 2 || x > 13 || y > 13;
                            if (!frame) c.Set(x, y, edge);
                            else c.Set(x, y, border ? edge : fill);
                        }
                    c.BorderLeft = c.BorderRight = c.BorderTop = c.BorderBottom = 3;
                    return c;
                }
                case "cone":
                case "conefill":
                {
                    bool frame = p[2] == "cone";
                    float half = (p.Length > 3 ? int.Parse(p[3]) : 90) * 0.5f * Mathf.Deg2Rad;
                    var c = new PixelCanvas(S, S);
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            // Apex at the centre; the cone opens to the right (+x).
                            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d > S / 2f) continue;
                            float a = Mathf.Abs(Mathf.Atan2(dy, dx));
                            if (a > half) continue;
                            if (!frame) { c.Set(x, y, edge); continue; }
                            bool border = d > S / 2f - 3.5f || (half - a) * d < 3f;
                            c.Set(x, y, border ? edge : fill);
                        }
                    return c;
                }
            }
            return null;
        }
    }
}
