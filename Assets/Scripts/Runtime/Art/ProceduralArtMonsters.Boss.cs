using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // =====================================================================================
        // Totem (사령 토템)
        // =====================================================================================

        /// <summary>Totem frame before the outline (the HD path upscales this).</summary>
        static PixelCanvas DrawTotemRaw(string frame)
        {
            var c = new PixelCanvas(16, 28);
            bool bright = frame == "idle1" || frame == "attack" || frame == "walk1" || frame == "walk3";
            var glow = bright ? MonSoulLight : MonSoul;
            var bone = PixelCanvas.Hex("#e6dcc4");
            var boneDark = PixelCanvas.Shade(bone, 0.75f);
            // Stone base.
            c.Rect(2, 23, 12, 4, StoneDark);
            c.Rect(3, 22, 10, 1, Stone);
            c.HLine(2, 13, 26, TOutlineStone);
            c.Set(4, 24, StoneLight); c.Set(10, 25, Stone);
            // Stacked vertebrae.
            for (int y = 10; y < 22; y += 3)
            {
                c.Rect(5, y, 6, 2, bone);
                c.HLine(5, 10, y + 1, boneDark);
                c.Rect(6, y + 2, 4, 1, boneDark);
                c.Set(4, y, bone); c.Set(11, y, boneDark);
            }
            // Runes glowing on the spine.
            c.Set(7, 13, glow); c.Set(8, 16, glow); c.Set(7, 19, glow);
            // Skull on top with horns.
            c.HLine(5, 10, 2, bone);
            c.Rect(4, 3, 8, 5, bone);
            c.Rect(5, 8, 6, 2, bone);
            c.HLine(5, 10, 9, boneDark);
            c.Rect(5, 4, 2, 2, MonSocket); c.Rect(9, 4, 2, 2, MonSocket);
            c.Set(5, 4, glow); c.Set(10, 4, glow);
            c.Set(7, 7, MonSocket); c.Set(8, 7, MonSocket);
            c.Set(3, 2, boneDark); c.Set(2, 1, bone); c.Set(12, 2, boneDark); c.Set(13, 1, bone);
            // Floating soul wisps.
            if (bright) { c.Set(1, 12, MonSoul); c.Set(14, 16, MonSoul); c.Set(2, 18, MonSoulLight); }
            else { c.Set(1, 14, MonSoul); c.Set(14, 11, MonSoul); }
            if (frame == "hurt") c.Rect(5, 4, 6, 2, White);
            return c;
        }

        // =====================================================================================
        // Bosses (48x56 canvas, body 2x the field skeleton)
        // =====================================================================================

        const int BossW = 48, BossH = 56, BodyOX = 8, BodyOY = 16;

        struct BigPose
        {
            public View v;
            public int o, step;
            public bool attack, hurt;
            /// <summary>Skull top-left and torso top row, in boss canvas coordinates.</summary>
            public int hx, hy, T;
            /// <summary>Weapon grip in boss canvas coordinates; weapons always point up from it.</summary>
            public int gx, gy;
        }

        /// <summary>Boss frame before the outline (the HD path upscales this).</summary>
        static PixelCanvas DrawBossRaw(CharacterLook L, View v, string frame)
        {
            var big = new PixelCanvas(BossW, BossH);
            var body = new PixelCanvas(32, 40);
            string id = L.id;
            bool robe = id == "boss_lich";
            Color32 eye = id switch
            {
                "boss_gold_foreman" => Yellow,
                "boss_mine_captain" => PixelCanvas.Hex("#ff9a3a"),
                "boss_lich" => MonSoul,
                "boss_archer_chief" => MagicCore,
                _ => Red,
            };
            var p = DrawBigSkeleton(body, L.skin, eye, v, frame, !robe);

            // Behind the body.
            switch (id)
            {
                case "boss_gold_foreman": BigGoldSack(big, p); break;
                case "boss_archer_chief": BigCloak(big, p, MonHood, MonHoodDark); BigQuiver(big, p); break;
                case "boss_skeleton_king": BigCloak(big, p, MonCape, MonCapeDark); break;
                case "boss_lich": BigCloak(big, p, MonRobeDark, PixelCanvas.Shade(MonRobeDark, 0.8f)); break;
            }
            bool weaponBehind = v.back && !p.attack;
            if (weaponBehind) BossWeapon(big, id, p);

            big.Blit(body, BodyOX, BodyOY);

            switch (id)
            {
                case "boss_gold_foreman":
                    BigBelt(big, p, MonLeather, Gold);
                    BigDomeHelmet(big, p, MonHelm, MonHelmDark, MonHelmLight, true);
                    break;
                case "boss_mine_captain":
                    BigStrap(big, p);
                    BigPauldrons(big, p, MonIron, MonIronDark, MonIronLight);
                    BigDomeHelmet(big, p, MonIronDark, PixelCanvas.Shade(MonIronDark, 0.7f), MonIron, false);
                    BigHorns(big, p);
                    break;
                case "boss_lich":
                    BigRobe(big, p);
                    BigBoneCrown(big, p);
                    break;
                case "boss_archer_chief":
                    BigBelt(big, p, MonLeatherDark, Steel);
                    BigHood(big, p);
                    break;
                case "boss_armory_warden":
                    BigPlate(big, p, MonIron, MonIronDark, MonIronLight, Gold);
                    BigGreatHelm(big, p);
                    break;
                case "boss_skeleton_king":
                    BigPlate(big, p, MonPlate, MonPlateDark, MonPlateLight, Gold);
                    BigFurCollar(big, p);
                    BigCrown(big, p);
                    break;
            }
            if (!weaponBehind) BossWeapon(big, id, p);
            if (id == "boss_armory_warden") BigTowerShield(big, p);

            return big;
        }

        /// <summary>
        /// Big skeleton on a 32x40 canvas (feet at row 38). Returns the pose in boss canvas
        /// coordinates (offset by BodyOX/BodyOY).
        /// </summary>
        static BigPose DrawBigSkeleton(PixelCanvas c, Color32 bone, Color32 eye, View v, string frame, bool legs)
        {
            FrameInfo(frame, out int bob, out int step);
            bool attack = frame == "attack", hurt = frame == "hurt";
            var dark = PixelCanvas.Shade(bone, 0.78f);
            var deep = PixelCanvas.Shade(bone, 0.6f);
            var light = PixelCanvas.Shade(bone, 1.1f);
            int o = bob, T = 21 + o, hx = 8, hy = 4 + o;
            int gx, gy;

            // ----- Legs (rows 34..38, fixed so the feet stay on the ground) -----
            if (legs)
            {
                if (v.side)
                {
                    int backX = 13 - step * 2, frontX = 16 + step * 2;
                    BigLeg(c, backX, 0, dark, deep, 2);
                    BigLeg(c, frontX, 0, bone, dark, 2);
                }
                else
                {
                    int lx = v.diag ? 13 : 12, rx = v.diag ? 19 : 18;
                    int lLift = step < 0 ? 2 : 0, rLift = step > 0 ? 2 : 0;
                    BigLeg(c, lx, lLift, bone, dark, v.diag ? 0 : -2);
                    BigLeg(c, rx, rLift, bone, dark, 2);
                }
            }

            // ----- Far arm (behind the ribs) -----
            if (v.side) { c.Rect(12, T + 1, 2, 9, deep); c.Rect(11, T + 10, 3, 2, deep); }
            else if (v.diag) { c.Rect(8, T + 1, 2, 10, dark); c.Rect(7, T + 11, 3, 2, dark); }

            // ----- Pelvis -----
            if (v.side)
            {
                c.HLine(12, 18, T + 10, bone);
                c.HLine(12, 14, T + 11, dark); c.HLine(17, 18, T + 11, bone);
            }
            else
            {
                c.HLine(11, 20, T + 10, bone);
                c.HLine(10, 13, T + 11, bone); c.HLine(18, 21, T + 11, dark);
                c.HLine(15, 16, T + 11, dark);
                c.HLine(12, 13, T + 12, dark); c.HLine(18, 19, T + 12, dark);
            }

            // ----- Spine + ribs -----
            if (v.side)
            {
                c.VLine(13, T, T + 9, dark); c.VLine(14, T, T + 9, bone);
                c.HLine(13, 18, T, bone);
                for (int r = 0; r < 3; r++)
                {
                    int y = T + 2 + r * 2;
                    c.HLine(14, 19 - (r == 2 ? 1 : 0), y, r == 0 ? light : bone);
                    c.Set(19 - (r == 2 ? 1 : 0), y + 1, dark);
                }
                c.HLine(15, 17, T + 8, dark);
            }
            else if (v.back)
            {
                // Shoulder blades, back ribs, spine on top.
                c.HLine(9, 22, T, bone);
                c.Rect(10, T + 1, 4, 4, bone); c.Rect(18, T + 1, 4, 4, dark);
                c.HLine(10, 13, T + 4, dark);
                c.HLine(11, 20, T + 6, dark); c.HLine(12, 19, T + 8, dark);
                for (int y = T; y <= T + 9; y++)
                {
                    c.Set(15, y, y % 2 == 0 ? light : bone);
                    c.Set(16, y, y % 2 == 0 ? bone : dark);
                }
            }
            else
            {
                int s = v.diag ? 1 : 0;
                c.HLine(9 + s, 22, T, bone);
                c.HLine(10 + s, 21, T + 2, bone); c.Set(10 + s, T + 3, dark); c.Set(21, T + 3, dark);
                c.HLine(11 + s, 20, T + 4, bone); c.Set(11 + s, T + 5, dark); c.Set(20, T + 5, dark);
                c.HLine(12 + s, 19, T + 6, bone); c.Set(12 + s, T + 7, dark); c.Set(19, T + 7, dark);
                c.HLine(13 + s, 18, T + 8, dark);
                for (int y = T + 2; y <= T + 8; y += 2) c.HLine(17 + s, 21 - (y - T - 2) / 2, y, dark);
                c.HLine(11 + s, 14 + s, T + 2, light);
                c.VLine(15 + s, T, T + 9, bone); c.VLine(16 + s, T, T + 9, dark);
            }

            // ----- Near / right arm -----
            if (v.side)
            {
                if (attack)
                {
                    c.Line(15, T + 1, 19, T - 4, bone); c.Line(16, T + 1, 20, T - 4, dark);
                    c.Line(19, T - 4, 21, T - 9, bone); c.Line(20, T - 4, 22, T - 9, dark);
                    c.Rect(20, T - 11, 3, 2, bone);
                    gx = 22; gy = T - 11;
                }
                else
                {
                    int sw = step;
                    c.Rect(15, T + 1, 2, 6, bone); c.VLine(16, T + 1, T + 6, dark);
                    c.Rect(15 + sw, T + 7, 2, 5, bone); c.VLine(16 + sw, T + 7, T + 11, dark);
                    c.Rect(15 + sw, T + 12, 3, 2, bone); c.Set(17 + sw, T + 13, dark);
                    gx = 17 + sw; gy = T + 12;
                }
            }
            else
            {
                bool back = v.back;
                // Left arm (canvas left).
                if (!v.diag)
                {
                    c.Rect(7, T + 1, 2, 6, back ? dark : bone); c.VLine(8, T + 1, T + 6, dark);
                    c.Rect(6, T + 7, 2, 5, bone); c.VLine(7, T + 7, T + 11, dark);
                    c.Rect(5, T + 12, 4, 1, bone); c.Set(5, T + 13, bone); c.Set(7, T + 13, bone);
                }
                int ax = v.diag ? 22 : 23;
                if (attack)
                {
                    c.Rect(ax, T - 5, 2, 6, bone); c.VLine(ax + 1, T - 5, T, dark);
                    c.Rect(ax + 1, T - 10, 2, 5, bone); c.VLine(ax + 2, T - 10, T - 6, dark);
                    c.Rect(ax, T - 12, 4, 2, bone);
                    gx = ax + 2; gy = T - 12;
                }
                else
                {
                    c.Rect(ax, T + 1, 2, 6, bone); c.VLine(ax + 1, T + 1, T + 6, dark);
                    c.Rect(ax + 1, T + 7, 2, 5, bone); c.VLine(ax + 2, T + 7, T + 11, dark);
                    c.Rect(ax, T + 12, 4, 1, bone); c.Set(ax, T + 13, bone); c.Set(ax + 2, T + 13, bone);
                    gx = ax + 3; gy = T + 12;
                }
                c.Set(8, T + 1, light); c.Set(23, T + 1, bone);
            }

            // ----- Neck -----
            if (v.side) c.Rect(13, hy + 13, 2, T - hy - 13, dark);
            else c.Rect(15, hy + 14, 2, T - hy - 14, dark);

            // ----- Skull -----
            if (v.side)
            {
                c.HLine(11, 18, hy, bone);
                c.HLine(9, 20, hy + 1, bone);
                c.HLine(8, 21, hy + 2, bone);
                c.Rect(8, hy + 3, 15, 6, bone);
                c.HLine(9, 22, hy + 9, bone);
                c.HLine(11, 21, hy + 10, bone);
                c.HLine(13, 21, hy + 11, bone);
                c.HLine(14, 21, hy + 12, bone);
                c.HLine(15, 20, hy + 13, dark);
                c.HLine(9, 13, hy + 9, dark); c.HLine(11, 13, hy + 10, dark);
                c.HLine(11, 15, hy + 1, light); c.HLine(10, 13, hy + 2, light);
                // Eye socket, nose, teeth.
                c.Rect(17, hy + 4, 4, 4, MonSocket);
                c.Set(17, hy + 4, bone); c.Set(20, hy + 7, bone);
                c.Rect(18, hy + 5, 2, 2, eye);
                c.Set(22, hy + 8, MonSocket);
                c.HLine(16, 21, hy + 11, MonSocket);
                for (int x = 16; x <= 21; x += 2) c.Set(x, hy + 12, MonSocket);
                c.Set(15, hy + 9, deep);
            }
            else
            {
                c.HLine(12, 19, hy, bone);
                c.HLine(10, 21, hy + 1, bone);
                c.HLine(9, 22, hy + 2, bone);
                c.Rect(8, hy + 3, 16, 7, bone);
                c.HLine(9, 22, hy + 10, dark);
                c.Rect(10, hy + 11, 12, 3, bone);
                c.HLine(11, 20, hy + 14, dark);
                c.VLine(23, hy + 3, hy + 9, dark); c.VLine(22, hy + 2, hy + 2, dark);
                if (v.back)
                {
                    c.HLine(12, 19, hy + 1, light);
                    c.HLine(9, 22, hy + 9, dark);
                    c.Rect(10, hy + 11, 12, 3, dark);
                    c.Set(13, hy + 4, dark); c.Set(14, hy + 5, dark); // old crack
                    if (v.diag) c.Set(22, hy + 6, MonSocket);
                }
                else
                {
                    c.HLine(12, 16, hy + 1, light); c.HLine(10, 13, hy + 2, light);
                    int lx = v.diag ? 12 : 10, lw = v.diag ? 3 : 4, rx = v.diag ? 19 : 18;
                    c.Rect(lx, hy + 5, lw, 4, MonSocket);
                    c.Rect(rx, hy + 5, 4, 4, MonSocket);
                    c.Set(lx, hy + 5, bone); c.Set(rx + 3, hy + 5, bone);
                    c.Rect(lx + 1, hy + 6, 2, 2, eye);
                    c.Rect(rx + 1, hy + 6, 2, 2, eye);
                    if (attack) { c.Set(lx + 1, hy + 6, White); c.Set(rx + 1, hy + 6, White); }
                    if (hurt) { c.Rect(lx + 1, hy + 6, 2, 2, MonSocket); c.Rect(rx + 1, hy + 6, 2, 2, MonSocket); c.Set(lx + 1, hy + 7, eye); c.Set(rx + 2, hy + 7, eye); }
                    int nx = v.diag ? 17 : 15;
                    c.HLine(nx, nx + 1, hy + 9, MonSocket); c.Set(nx, hy + 8, MonSocket);
                    c.HLine(11 + (v.diag ? 1 : 0), 20 + (v.diag ? 1 : 0), hy + 11, MonSocket);
                    for (int x = 12 + (v.diag ? 1 : 0); x <= 20; x += 2) c.Set(x, hy + 12, MonSocket);
                    c.Set(9, hy + 9, deep); c.Set(22, hy + 9, deep);
                }
            }

            return new BigPose
            {
                v = v, o = o, step = step, attack = attack, hurt = hurt,
                hx = hx + BodyOX, hy = hy + BodyOY, T = T + BodyOY,
                gx = gx + BodyOX, gy = gy + BodyOY,
            };
        }

        static void BigLeg(PixelCanvas c, int x, int lift, Color32 col, Color32 dark, int footDir)
        {
            int bottom = 38 - lift;
            c.Rect(x, 34, 2, bottom - 34, col);
            c.VLine(x + 1, 34, bottom - 1, dark);
            c.Set(x, 36 - lift / 2, dark);
            if (footDir > 0) c.HLine(x, x + 1 + footDir, bottom, col);
            else if (footDir < 0) c.HLine(x + footDir, x + 1, bottom, col);
            else c.HLine(x, x + 2, bottom, col);
        }
    }
}
