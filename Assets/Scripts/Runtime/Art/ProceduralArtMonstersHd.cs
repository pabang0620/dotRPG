using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution (density 2, 32px per tile) dungeon monsters, bosses and the necro totem, matching
    /// the merged HD character art. Small monsters stand on the exact HD field-skeleton body
    /// (DrawSkeletonBodyHd: 32x40 canvas, pivot (16, 3), 1px outline), with their gear authored on the
    /// 16px grid, edge-smoothed 2x (EPX) and bevel-lit at the HD resolution, then a few HD-only details
    /// (rivets, sparkles, plume strands, soul glow). Bosses and the totem upscale their 16px-grid art the
    /// same way: 48x56 → 96x112 boss canvases (same world size as before, so MonsterDef.artScale = 2 and
    /// size scaling are unchanged). Frame set and keys are untouched (5 views × idle0/1, walk0-3,
    /// attack, hurt).
    /// </summary>
    public static partial class ProceduralArt
    {
        // ---------- Tuning ----------
        const float MonBevelLight = 1.18f;  // top-edge highlight on gear
        const float MonBevelDark = 0.80f;   // bottom-edge shade on gear
        const byte MonGlowInner = 150, MonGlowOuter = 70; // soul glow halo alpha (1px / 2px away)

        /// <summary>HD router for BodyKind.Monster looks (called by DrawCharacter).</summary>
        static PixelCanvas DrawMonsterCharacterHd(CharacterLook look, string dir, string frame)
        {
            var v = new View(dir);
            if (look.id == "totem") return DrawTotemHd(frame);
            if (look.id.StartsWith("boss_")) return DrawBossHd(look, v, frame);
            return DrawSmallMonsterHd(look, v, frame);
        }

        // ---------- Small monsters ----------

        static PixelCanvas DrawSmallMonsterHd(CharacterLook L, View v, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            bool attack = frame == "attack";
            int o = bob, hx = 4, hy = 3 + o;          // old 16px skull origin (HD skull = x2)
            var g = SmallGrip(v, attack, o, step);
            bool behindTool = v.back && !attack;

            // Gear layers on the 16px grid.
            var back = new PixelCanvas(16, 20);
            var front = new PixelCanvas(16, 20);
            switch (L.id)
            {
                case "skel_gold": GoldSack(back, v, o); break;
                case "skel_archer": ArcherQuiver(back, v, o); break;
                case "skel_miner": if (behindTool) SmallPickaxe(back, g); break;
                case "skel_knight": if (behindTool) SmallSword(back, g, true); break;
            }
            switch (L.id)
            {
                case "skel_miner":
                    SmallHelmet(front, v, hx, hy, MonHelm, MonHelmDark, MonHelmLight, true);
                    if (!behindTool) SmallPickaxe(front, g);
                    break;
                case "skel_necro":
                    NecroRobe(front, v, o, step);
                    NecroHood(front, v, hx, hy, attack);
                    SmallStaff(front, v, g, attack, o);
                    break;
                case "skel_archer":
                    ArcherHood(front, v, hx, hy);
                    SmallBow(front, v, g, attack, o);
                    break;
                case "skel_shield":
                    SmallHelmet(front, v, hx, hy, MonIron, MonIronDark, MonIronLight, false);
                    if (!v.back) SmallShortSword(front, v, g);
                    TowerShieldSmall(front, v, o);
                    break;
                case "skel_knight":
                    KnightArmor(front, v, o);
                    KnightHelm(front, v, hx, hy);
                    if (!behindTool) SmallSword(front, g, false);
                    break;
            }

            var c = Hd(W, HGT);
            c.Blit(Bevel(Scale2x(back)), 0, 0);
            DrawSkeletonBodyHd(c, L, v, frame);
            if (L.id == "skel_necro") c.Rect(6, 32, 22, 6, PixelCanvas.Clear); // robe hides the legs
            c.Blit(Bevel(Scale2x(front)), 0, 0);
            SmallMonsterDetailsHd(c, L.id, v, attack, o * 2, step);
            c.Outline(Outline);
            SoulGlow(c);
            return c.WithPivot(16, 3f);
        }

        /// <summary>HD-only touches drawn after the gear (HD coordinates; o = doubled bob).</summary>
        static void SmallMonsterDetailsHd(PixelCanvas c, string id, View v, bool attack, int o, int step)
        {
            int hx = 8, hy = 6 + o; // HD skull origin
            switch (id)
            {
                case "skel_gold":
                    // Gilded bones: sparkles on the skull, ribs and pelvis.
                    c.Paint(hx + 4, hy + 2, White); c.Paint(hx + 5, hy + 1, MonHelmLight); c.Paint(hx + 3, hy + 1, MonHelmLight);
                    c.Paint(13, 25 + o, MonHelmLight); c.Paint(18, 27 + o, White); c.Paint(14, 30 + o, MonHelmLight);
                    if (!v.back) { c.Paint(hx + 12, hy + 12, MonCoin); c.Paint(hx + 13, hy + 11, White); }
                    break;
                case "skel_miner":
                    // Helmet rim rivets.
                    for (int x = hx + 1; x <= hx + 14; x += 4) c.Paint(x, hy + 4, MonHelmLight);
                    break;
                case "skel_shield":
                    for (int x = hx + 1; x <= hx + 14; x += 4) c.Paint(x, hy + 4, MonIronLight);
                    break;
                case "skel_knight":
                    // Plume strands and a gold crest band.
                    if (!v.side)
                    {
                        c.Paint(hx + 7, hy - 5, RedLight); c.Paint(hx + 9, hy - 4, RedLight);
                        c.Paint(hx + 8, hy - 2, PixelCanvas.Shade(Red, 0.7f));
                    }
                    c.Paint(hx + 2, hy + 1, Gold); c.Paint(hx + 13, hy + 1, Gold);
                    break;
            }
        }

        // ---------- Bosses ----------

        static PixelCanvas DrawBossHd(CharacterLook L, View v, string frame)
        {
            var c = Bevel(Scale2x(DrawBossRaw(L, v, frame)));
            BossDetailsHd(c, L.id, v, frame);
            c.Outline(Outline);
            SoulGlow(c);
            return c.WithPivot(BossW, 3f);
        }

        /// <summary>HD-only boss touches: gem glints on crowns/gold, pulsing eye highlights.</summary>
        static void BossDetailsHd(PixelCanvas c, string id, View v, string frame)
        {
            // Every gold pixel next to a highlight gets a 1px white glint on its top-left corner, so
            // crowns, buckles and trims sparkle at the HD resolution.
            if (id == "boss_skeleton_king" || id == "boss_gold_foreman" || id == "boss_armory_warden")
            {
                var src = (Color32[])c.Pixels.Clone();
                int n = 0;
                for (int y = 1; y < c.Height; y++)
                    for (int x = 1; x < c.Width; x++)
                    {
                        var p = src[y * c.Width + x];
                        if (!Same(p, Gold) && !Same(p, MonCoin) && !Same(p, MonHelmLight)) continue;
                        if (src[(y - 1) * c.Width + x].a != 0 && src[y * c.Width + x - 1].a != 0) continue;
                        if ((n++ % 3) == 0) c.Set(x, y, White);
                    }
            }
        }

        // ---------- Totem ----------

        static PixelCanvas DrawTotemHd(string frame)
        {
            var c = Bevel(Scale2x(DrawTotemRaw(frame)));
            c.Outline(Outline);
            SoulGlow(c);
            return c.WithPivot(16, 3f);
        }

        // ---------- HD helpers ----------

        /// <summary>
        /// EPX / Scale2x upscale of 16px-grid art into a density-2 canvas: same world size, diagonal
        /// edges smoothed into 1px steps instead of 2x2 blocks.
        /// </summary>
        static PixelCanvas Scale2x(PixelCanvas s)
        {
            var d = Hd(s.Width * 2, s.Height * 2);
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    var p = s.Get(x, y);
                    var a = s.Get(x, y - 1); var b = s.Get(x + 1, y);
                    var cl = s.Get(x - 1, y); var dn = s.Get(x, y + 1);
                    var e0 = Same(cl, a) && !Same(cl, dn) && !Same(a, b) ? a : p;
                    var e1 = Same(a, b) && !Same(a, cl) && !Same(b, dn) ? b : p;
                    var e2 = Same(dn, cl) && !Same(dn, b) && !Same(cl, a) ? cl : p;
                    var e3 = Same(b, dn) && !Same(b, a) && !Same(dn, cl) ? dn : p;
                    int i = (y * 2) * d.Width + x * 2;
                    d.Pixels[i] = e0; d.Pixels[i + 1] = e1;
                    d.Pixels[i + d.Width] = e2; d.Pixels[i + d.Width + 1] = e3;
                }
            return d;
        }

        /// <summary>Thin HD rim light on top edges and shade on bottom edges of every opaque shape.</summary>
        static PixelCanvas Bevel(PixelCanvas c)
        {
            var src = (Color32[])c.Pixels.Clone();
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    var p = src[y * c.Width + x];
                    if (p.a == 0) continue;
                    bool topOpen = y == 0 || src[(y - 1) * c.Width + x].a == 0;
                    bool botOpen = y == c.Height - 1 || src[(y + 1) * c.Width + x].a == 0;
                    if (topOpen) c.Pixels[y * c.Width + x] = PixelCanvas.Shade(p, MonBevelLight);
                    else if (botOpen) c.Pixels[y * c.Width + x] = PixelCanvas.Shade(p, MonBevelDark);
                }
            return c;
        }

        /// <summary>Soft translucent halo (outside the outline) around soul-green pixels: staffs, eyes, runes.</summary>
        static void SoulGlow(PixelCanvas c)
        {
            var src = (Color32[])c.Pixels.Clone();
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    var p = src[y * c.Width + x];
                    if (!Same(p, MonSoul) && !Same(p, MonSoulLight)) continue;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (!c.InBounds(nx, ny)) continue;
                            int dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                            if (dist < 2) continue; // 1px ring is the outline
                            byte alpha = dist == 2 ? MonGlowInner : MonGlowOuter;
                            if (c.Pixels[ny * c.Width + nx].a >= alpha) continue;
                            c.Pixels[ny * c.Width + nx] = PixelCanvas.WithAlpha(MonSoul, alpha);
                        }
                }
        }
    }
}
