using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Dungeon monsters and bosses (stage 3). Small monsters reuse the 16x20 skeleton body and add
    /// helmets, tools, robes and shields; bosses are drawn on a 48x56 canvas (body 2x the normal
    /// skeleton, room above the head for raised weapons) with the same outline and palette. All
    /// frames use the CharacterAnimator frame set (5 views × idle0/1, walk0-3, attack, hurt), so the
    /// same animator drives them. Also: the necro totem, projectiles, telegraph textures and the
    /// summon circle ("mon_*" keys).
    /// </summary>
    public static partial class ProceduralArt
    {
        // ---------- Palette ----------
        static readonly Color32 MonHelm = PixelCanvas.Hex("#e8b83a");
        static readonly Color32 MonHelmDark = PixelCanvas.Hex("#a8781c");
        static readonly Color32 MonHelmLight = PixelCanvas.Hex("#ffe488");
        static readonly Color32 MonIron = PixelCanvas.Hex("#8c96a4");
        static readonly Color32 MonIronDark = PixelCanvas.Hex("#5a6270");
        static readonly Color32 MonIronLight = PixelCanvas.Hex("#c4ccd6");
        static readonly Color32 MonPlate = PixelCanvas.Hex("#4e5566");
        static readonly Color32 MonPlateDark = PixelCanvas.Hex("#343947");
        static readonly Color32 MonPlateLight = PixelCanvas.Hex("#7a8398");
        static readonly Color32 MonRobe = PixelCanvas.Hex("#4a2a6a");
        static readonly Color32 MonRobeDark = PixelCanvas.Hex("#321a4a");
        static readonly Color32 MonRobeLight = PixelCanvas.Hex("#6e44a0");
        static readonly Color32 MonSoul = PixelCanvas.Hex("#7dff8a");
        static readonly Color32 MonSoulLight = PixelCanvas.Hex("#d4ffd8");
        static readonly Color32 MonHood = PixelCanvas.Hex("#3f7a3a");
        static readonly Color32 MonHoodDark = PixelCanvas.Hex("#2a5428");
        static readonly Color32 MonHoodLight = PixelCanvas.Hex("#5e9e4e");
        static readonly Color32 MonLeather = PixelCanvas.Hex("#7a4a2a");
        static readonly Color32 MonLeatherDark = PixelCanvas.Hex("#52301a");
        static readonly Color32 MonSack = PixelCanvas.Hex("#a8804e");
        static readonly Color32 MonSackDark = PixelCanvas.Hex("#7a5a34");
        static readonly Color32 MonCoin = PixelCanvas.Hex("#ffd84a");
        static readonly Color32 MonCape = PixelCanvas.Hex("#b0283a");
        static readonly Color32 MonCapeDark = PixelCanvas.Hex("#761a28");
        static readonly Color32 MonCapeLight = PixelCanvas.Hex("#dc4a52");
        static readonly Color32 MonFur = PixelCanvas.Hex("#f4efe6");
        static readonly Color32 MonSocket = PixelCanvas.Hex("#2b1d16");

        /// <summary>Monster frame dispatch (CharacterLook.body == Monster, keyed by look.id).</summary>
        static PixelCanvas DrawMonsterCharacter(CharacterLook look, string dir, string frame)
        {
            var v = new View(dir);
            if (look.id == "totem") return DrawTotem(frame);
            if (look.id.StartsWith("boss_")) return DrawBoss(look, v, frame);
            return DrawSmallMonster(look, v, frame);
        }

        // =====================================================================================
        // Small monsters (16x20, same body as the field skeleton)
        // =====================================================================================

        /// <summary>Where a small monster holds its tool: grip pixel and whether it is raised.</summary>
        struct Grip
        {
            public int x, y;
            public bool raised, forward;
        }

        static Grip SmallGrip(View v, bool attack, int o, int step)
        {
            if (v.side) return attack ? new Grip { x = 13, y = 12 + o, forward = true } : new Grip { x = 9 + step, y = 14 + o };
            if (v.diag) return attack ? new Grip { x = 13, y = 12 + o, forward = true } : new Grip { x = 11, y = 14 + o };
            return attack ? new Grip { x = 12, y = 9 + o, raised = true } : new Grip { x = 12, y = 14 + o };
        }

        static PixelCanvas DrawSmallMonster(CharacterLook L, View v, string frame)
        {
            var c = new PixelCanvas(16, 20);
            FrameInfo(frame, out int bob, out int step);
            bool attack = frame == "attack";
            int o = bob, hx = 4, hy = 3 + o;
            var g = SmallGrip(v, attack, o, step);
            bool behindTool = v.back && !attack;

            // Behind the body.
            switch (L.id)
            {
                case "skel_gold": GoldSack(c, v, o); break;
                case "skel_archer": ArcherQuiver(c, v, o); break;
                case "skel_miner": if (behindTool) SmallPickaxe(c, g); break;
                case "skel_knight": if (behindTool) SmallSword(c, g, true); break;
            }

            DrawSkeletonBody(c, L, v, frame);

            switch (L.id)
            {
                case "skel_gold":
                    // Gold glints on the skull and a coin in the hand.
                    if (!v.back) c.Set(hx + 2, hy + 1, MonHelmLight);
                    c.Set(hx + 5, hy, White);
                    if (v.front && !v.side) c.Set(v.diag ? 10 : 11, 15 + o, MonCoin);
                    break;
                case "skel_miner":
                    SmallHelmet(c, v, hx, hy, MonHelm, MonHelmDark, MonHelmLight, true);
                    if (!behindTool) SmallPickaxe(c, g);
                    break;
                case "skel_necro":
                    NecroRobe(c, v, o, step);
                    NecroHood(c, v, hx, hy, attack);
                    SmallStaff(c, v, g, attack, o);
                    break;
                case "skel_archer":
                    ArcherHood(c, v, hx, hy);
                    SmallBow(c, v, g, attack, o);
                    break;
                case "skel_shield":
                    SmallHelmet(c, v, hx, hy, MonIron, MonIronDark, MonIronLight, false);
                    if (!v.back) SmallShortSword(c, v, g);
                    TowerShieldSmall(c, v, o);
                    break;
                case "skel_knight":
                    KnightArmor(c, v, o);
                    KnightHelm(c, v, hx, hy);
                    if (!behindTool) SmallSword(c, g, false);
                    break;
            }
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static void SmallHelmet(PixelCanvas c, View v, int hx, int hy, Color32 col, Color32 dark, Color32 light, bool lamp)
        {
            c.HLine(hx + 2, hx + 5, hy - 1, col);
            c.HLine(hx + 1, hx + 6, hy, col);
            c.HLine(hx, hx + 7, hy + 1, col);
            c.Set(hx + 2, hy, light); c.Set(hx + 3, hy - 1, light);
            if (v.side)
            {
                c.HLine(hx, hx + 8, hy + 2, dark);
                if (lamp) { c.Set(hx + 7, hy, White); c.Set(hx + 7, hy + 1, Yellow); }
            }
            else
            {
                c.HLine(hx - 1, hx + 8, hy + 2, dark);
                if (lamp && !v.back)
                {
                    int lx = v.diag ? hx + 5 : hx + 3;
                    c.Set(lx, hy, White); c.Set(lx + 1, hy, White);
                    c.Set(lx, hy + 1, Yellow); c.Set(lx + 1, hy + 1, Yellow);
                }
                if (!lamp && !v.back) c.VLine(v.diag ? hx + 5 : hx + 3, hy + 2, hy + 4, dark); // nose guard
            }
        }

        static void SmallPickaxe(PixelCanvas c, Grip g)
        {
            if (g.forward)
            {
                c.HLine(g.x - 1, g.x + 1, g.y, Wood);
                c.VLine(g.x + 2, g.y - 2, g.y + 2, Steel);
                c.Set(g.x + 2, g.y + 2, SteelDark); c.Set(g.x + 1, g.y - 2, SteelDark);
                return;
            }
            c.VLine(g.x, g.y - 5, g.y + 1, Wood);
            c.Set(g.x, g.y + 1, WoodDark);
            c.HLine(g.x - 2, g.x + 2, g.y - 6, Steel);
            c.Set(g.x - 2, g.y - 5, SteelDark); c.Set(g.x + 2, g.y - 5, SteelDark);
            c.Set(g.x + 1, g.y - 6, White);
        }

        static void SmallSword(PixelCanvas c, Grip g, bool dim)
        {
            var blade = dim ? SteelDark : Steel;
            if (g.forward)
            {
                c.Set(g.x - 1, g.y, handleColor);
                c.VLine(g.x, g.y - 1, g.y + 1, Gold);
                c.HLine(g.x + 1, g.x + 2, g.y, blade);
                return;
            }
            c.Set(g.x, g.y + 1, handleColor);
            c.HLine(g.x - 1, g.x + 1, g.y, Gold);
            c.VLine(g.x, g.y - 6, g.y - 1, blade);
            c.Set(g.x, g.y - 6, White);
        }

        static void SmallShortSword(PixelCanvas c, View v, Grip g)
        {
            if (g.forward) { c.Set(g.x - 1, g.y, handleColor); c.HLine(g.x, g.x + 2, g.y, Steel); return; }
            c.Set(g.x, g.y + 1, handleColor);
            c.VLine(g.x, g.y - 3, g.y, Steel);
        }

        static void TowerShieldSmall(PixelCanvas c, View v, int o)
        {
            if (v.side)
            {
                c.Rect(11, 10 + o, 3, 8, MonIron);
                c.VLine(13, 10 + o, 17 + o, MonIronDark);
                c.VLine(11, 10 + o, 17 + o, MonIronLight);
                c.Set(12, 13 + o, Gold);
                return;
            }
            if (v.back)
            {
                c.Rect(1, 11 + o, 3, 7, MonIronDark);
                c.VLine(1, 11 + o, 17 + o, MonIron);
                return;
            }
            int x0 = v.diag ? 8 : 2;
            c.Rect(x0, 10 + o, 6, 8, MonIron);
            c.HLine(x0, x0 + 5, 10 + o, MonIronLight);
            c.VLine(x0 + 5, 11 + o, 17 + o, MonIronDark);
            c.HLine(x0, x0 + 5, 17 + o, MonIronDark);
            c.Set(x0, 17 + o, PixelCanvas.Clear); c.Set(x0 + 5, 17 + o, PixelCanvas.Clear);
            // Red cross emblem with a gold boss.
            c.VLine(x0 + 2, 11 + o, 16 + o, Red);
            c.HLine(x0 + 1, x0 + 4, 13 + o, Red);
            c.Set(x0 + 2, 13 + o, Gold);
        }

        static void GoldSack(PixelCanvas c, View v, int o)
        {
            if (v.side)
            {
                c.Ellipse(4.5f, 12.5f + o, 2.6f, 3f, MonSack);
                c.PaintEllipse(3.5f, 13.5f + o, 1.5f, 2f, MonSackDark);
                c.Set(4, 9 + o, MonCoin); c.Set(5, 9 + o, MonCoin);
            }
            else if (v.back)
            {
                c.Ellipse(8f, 13f + o, 3.5f, 3.2f, MonSack);
                c.PaintEllipse(9f, 14f + o, 2f, 2f, MonSackDark);
                c.HLine(7, 9, 10 + o, MonCoin);
            }
            else
            {
                c.Ellipse(v.diag ? 3.5f : 2.8f, 11.5f + o, 2.4f, 2.8f, MonSack);
                c.PaintEllipse(2.5f, 12.5f + o, 1.4f, 1.6f, MonSackDark);
                c.Set(2, 9 + o, MonCoin); c.Set(3, 8 + o, MonCoin);
            }
        }

        static void ArcherQuiver(PixelCanvas c, View v, int o)
        {
            int x = v.side ? 4 : v.back ? 9 : 10;
            c.Rect(x, 9 + o, 2, 6, MonLeather);
            c.VLine(x + 1, 9 + o, 14 + o, MonLeatherDark);
            c.Set(x, 8 + o, Red); c.Set(x + 1, 7 + o, Red); c.Set(x - 1, 8 + o, White);
        }

        static void ArcherHood(PixelCanvas c, View v, int hx, int hy)
        {
            c.HLine(hx + 2, hx + 5, hy - 1, MonHood);
            c.HLine(hx + 1, hx + 6, hy, MonHood);
            c.HLine(hx, hx + 7, hy + 1, MonHood);
            c.Set(hx + 3, hy - 1, MonHoodLight);
            if (v.back) { c.Rect(hx, hy + 2, 8, 5, MonHood); c.HLine(hx, hx + 7, hy + 6, MonHoodDark); }
            else if (v.side) { c.Rect(hx - 1, hy + 1, 4, 6, MonHood); c.VLine(hx + 2, hy + 2, hy + 6, MonHoodDark); c.Set(hx - 2, hy + 3, MonHood); }
            else { c.VLine(hx, hy + 2, hy + 5, MonHood); c.VLine(hx + 7, hy + 2, hy + 5, MonHoodDark); c.HLine(hx + 1, hx + 6, hy + 2, MonHoodDark); }
            // Scarf over the neck.
            c.HLine(hx + 1, hx + 6, hy + 8, MonHoodDark);
        }

        static void SmallBow(PixelCanvas c, View v, Grip g, bool attack, int o)
        {
            if (v.side && attack)
            {
                // Drawn bow pointing forward with a nocked arrow.
                c.Set(14, 8 + o, Wood); c.VLine(15, 9 + o, 15 + o, Wood); c.Set(14, 16 + o, Wood);
                c.Line(14, 8 + o, 11, 12 + o, FlagLight); c.Line(11, 12 + o, 14, 16 + o, FlagLight);
                c.HLine(10, 15, 12 + o, WoodLight);
                c.Set(15, 12 + o, Steel);
                return;
            }
            if (v.back) return;
            int x = v.side ? 10 + (attack ? 0 : 0) : v.diag ? 12 : 2;
            int top = 8 + o, bottom = 16 + o;
            bool bulgeRight = !(v.front && !v.side && !v.diag);
            int bx = bulgeRight ? x + 1 : x - 1;
            c.VLine(x, top + 1, bottom - 1, FlagLight);         // string
            c.Set(x, top, Wood); c.Set(x, bottom, Wood);
            c.VLine(bx, top + 1, bottom - 1, Wood);
            c.Set(bx, (top + bottom) / 2, WoodDark);
            if (attack) { c.HLine(x - 1, x + 2, 12 + o, WoodLight); c.Set(x + (bulgeRight ? 2 : -2), 12 + o, Steel); }
        }

        static void SmallStaff(PixelCanvas c, View v, Grip g, bool attack, int o)
        {
            int x = v.side ? 10 : v.diag ? 12 : v.back ? 12 : 3;
            if (v.back) { x = 12; }
            int top = attack ? 3 + o : 5 + o;
            c.VLine(x, top + 2, 17 + o, BarkDark);
            c.Set(x, 17 + o, Bark);
            var orb = attack ? MonSoulLight : MonSoul;
            c.Rect(x - 1, top, 3, 2, orb);
            c.Set(x, top - 1, orb);
            c.Set(x - 1, top + 2, Bark); c.Set(x + 1, top + 2, Bark);
            if (attack) { c.Set(x - 2, top, MonSoul); c.Set(x + 2, top, MonSoul); c.Set(x, top - 2, MonSoul); }
        }

        static void NecroRobe(PixelCanvas c, View v, int o, int step)
        {
            // Covers the legs and torso; sleeves over the arms, bony hands show.
            c.Rect(3, 16, 11, 3, PixelCanvas.Clear);
            int sway = step;
            if (v.side)
            {
                c.Rect(5, 11 + o, 6, 5, MonRobe);
                c.Rect(4 + (sway < 0 ? 1 : 0), 16, 7, 2, MonRobe);
                c.HLine(4 + (sway < 0 ? 1 : 0), 10 + (sway > 0 ? 1 : 0), 18, MonRobeDark);
                c.VLine(5, 11 + o, 17, MonRobeDark);
                c.Set(9, 18, MonSoul);
                c.Rect(8, 12 + o, 2, 3, MonRobeLight); // sleeve
                return;
            }
            c.Rect(4, 11 + o, 8, 5, MonRobe);
            c.Rect(4, 16, 8, 2, MonRobe);
            c.HLine(3 + (sway < 0 ? 1 : 0), 12 - (sway > 0 ? 1 : 0), 18, MonRobeDark);
            c.VLine(4, 11 + o, 17, MonRobeDark);
            c.VLine(11, 11 + o, 17, MonRobeDark);
            if (!v.back)
            {
                // Ribs peek through the open front, green trim.
                c.VLine(v.diag ? 8 : 7, 12 + o, 17, MonRobeDark);
                c.Set(5, 18, MonSoul); c.Set(10, 18, MonSoul);
                c.HLine(5, 10, 15 + o, MonRobeLight);
            }
            // Sleeves.
            c.Rect(3, 12 + o, 1, 3, MonRobeLight);
            c.Rect(12, 12 + o, 1, 3, MonRobeLight);
        }

        static void NecroHood(PixelCanvas c, View v, int hx, int hy, bool attack)
        {
            var eye = attack ? MonSoulLight : MonSoul;
            c.HLine(hx + 2, hx + 5, hy - 1, MonRobe);
            c.Set(hx + 3, hy - 2, MonRobe);
            c.HLine(hx + 1, hx + 6, hy, MonRobe);
            c.HLine(hx, hx + 7, hy + 1, MonRobe);
            c.Set(hx + 2, hy, MonRobeLight);
            if (v.back)
            {
                c.Rect(hx - 1, hy + 1, 10, 8, MonRobe);
                c.VLine(hx + 3, hy + 2, hy + 7, MonRobeDark);
                return;
            }
            if (v.side)
            {
                c.Rect(hx - 1, hy + 1, 5, 8, MonRobe);
                c.VLine(hx + 3, hy + 2, hy + 8, MonRobeDark);
                c.Rect(hx + 4, hy + 3, 2, 2, MonSocket);
                c.Set(hx + 5, hy + 3, eye);
                return;
            }
            c.VLine(hx - 1, hy + 2, hy + 8, MonRobe);
            c.VLine(hx, hy + 2, hy + 7, MonRobeDark);
            c.VLine(hx + 7, hy + 2, hy + 7, MonRobeDark);
            c.VLine(hx + 8, hy + 2, hy + 8, MonRobe);
            if (v.diag) { c.Set(hx + 4, hy + 4, eye); c.Set(hx + 6, hy + 4, eye); }
            else { c.Set(hx + 2, hy + 4, eye); c.Set(hx + 6, hy + 4, eye); }
        }

        static void KnightArmor(PixelCanvas c, View v, int o)
        {
            if (v.side)
            {
                c.Rect(6, 11 + o, 4, 4, MonPlate);
                c.VLine(6, 11 + o, 14 + o, MonPlateDark);
                c.Set(9, 11 + o, MonPlateLight);
                c.Rect(7, 10 + o, 3, 2, MonPlateLight);
                c.HLine(6, 9, 15 + o, MonLeatherDark);
                return;
            }
            c.Rect(5, 11 + o, 6, 4, MonPlate);
            c.HLine(5, 10, 14 + o, MonPlateDark);
            c.HLine(5, 10, 15 + o, MonLeatherDark);
            if (!v.back) { c.Set(6, 12 + o, MonPlateLight); c.Set(7, 11 + o, MonPlateLight); c.Set(v.diag ? 9 : 8, 15 + o, Gold); }
            // Pauldrons.
            c.Rect(3, 10 + o, 3, 2, MonPlateLight); c.Rect(10, 10 + o, 3, 2, MonPlateLight);
            c.HLine(3, 5, 11 + o, MonPlateDark); c.HLine(10, 12, 11 + o, MonPlateDark);
        }

        static void KnightHelm(PixelCanvas c, View v, int hx, int hy)
        {
            c.HLine(hx + 2, hx + 5, hy - 1, MonPlate);
            c.HLine(hx + 1, hx + 6, hy, MonPlate);
            c.Rect(hx, hy + 1, 8, 6, MonPlate);
            c.Set(hx + 2, hy, MonPlateLight); c.Set(hx + 1, hy + 1, MonPlateLight);
            c.VLine(hx + 7, hy + 1, hy + 6, MonPlateDark);
            // Red plume.
            if (v.side) { c.HLine(hx - 1, hx + 3, hy - 2, Red); c.Set(hx - 2, hy - 1, Red); c.Set(hx + 4, hy - 2, RedLight); }
            else { c.VLine(hx + 3, hy - 3, hy - 1, Red); c.VLine(hx + 4, hy - 3, hy - 2, RedLight); c.Set(hx + 2, hy - 2, Red); }
            if (v.back) return;
            // Visor slit with red eyes.
            if (v.side) { c.HLine(hx + 4, hx + 7, hy + 3, MonSocket); c.Set(hx + 6, hy + 3, Red); }
            else if (v.diag) { c.HLine(hx + 2, hx + 7, hy + 3, MonSocket); c.Set(hx + 3, hy + 3, Red); c.Set(hx + 6, hy + 3, Red); }
            else { c.HLine(hx + 1, hx + 6, hy + 3, MonSocket); c.Set(hx + 2, hy + 3, Red); c.Set(hx + 5, hy + 3, Red); c.VLine(hx + 3, hy + 4, hy + 5, MonPlateDark); }
        }

        // =====================================================================================
        // Totem (사령 토템)
        // =====================================================================================

        static PixelCanvas DrawTotem(string frame) => DrawTotemRaw(frame).OutlinedWithPivot(8, 1.5f);

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

        static PixelCanvas DrawBoss(CharacterLook L, View v, string frame) => DrawBossRaw(L, v, frame).OutlinedWithPivot(BossW / 2f, 1.5f);

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

        // ---------- Boss gear ----------

        static void BigDomeHelmet(PixelCanvas c, BigPose p, Color32 col, Color32 dark, Color32 light, bool lamp)
        {
            int hx = p.hx, hy = p.hy;
            c.HLine(hx + 4, hx + 11, hy - 3, col);
            c.HLine(hx + 2, hx + 13, hy - 2, col);
            c.HLine(hx + 1, hx + 14, hy - 1, col);
            c.Rect(hx, hy, 16, 3, col);
            c.HLine(hx + 4, hx + 8, hy - 2, light); c.HLine(hx + 2, hx + 5, hy - 1, light);
            c.VLine(hx + 15, hy, hy + 2, dark);
            bool side = p.v.side;
            c.HLine(side ? hx - 1 : hx - 2, side ? hx + 19 : hx + 17, hy + 3, dark);
            c.HLine(side ? hx - 1 : hx - 2, side ? hx + 19 : hx + 17, hy + 4, PixelCanvas.Shade(dark, 0.8f));
            if (p.v.back) { c.HLine(hx, hx + 15, hy + 1, dark); return; }
            if (lamp)
            {
                int lx = side ? hx + 15 : p.v.diag ? hx + 9 : hx + 7;
                c.Rect(lx - 1, hy - 1, 4, 4, MonIronDark);
                c.Rect(lx, hy, 2, 2, White);
                c.Set(lx - 1, hy - 2, Yellow); c.Set(lx + 2, hy - 2, Yellow);
            }
            else
            {
                // Visor band with rivets.
                c.HLine(hx, hx + 15, hy + 2, Red);
                for (int x = hx + 1; x <= hx + 14; x += 4) c.Set(x, hy + 1, MonIronLight);
            }
        }

        static void BigHorns(PixelCanvas c, BigPose p)
        {
            var horn = PixelCanvas.Hex("#e8dcc0");
            var hornDark = PixelCanvas.Shade(horn, 0.75f);
            int hx = p.hx, hy = p.hy;
            if (p.v.side)
            {
                c.Line(hx + 3, hy - 2, hx, hy - 6, horn); c.Line(hx + 4, hy - 2, hx + 1, hy - 6, hornDark);
                return;
            }
            c.Line(hx, hy - 1, hx - 3, hy - 5, horn); c.Line(hx + 1, hy - 1, hx - 2, hy - 5, hornDark);
            c.Line(hx + 15, hy - 1, hx + 18, hy - 5, horn); c.Line(hx + 14, hy - 1, hx + 17, hy - 5, hornDark);
            c.Set(hx - 3, hy - 6, horn); c.Set(hx + 18, hy - 6, horn);
        }

        static void BigBelt(PixelCanvas c, BigPose p, Color32 col, Color32 buckle)
        {
            int y = p.T + 9;
            if (p.v.side) { c.HLine(p.hx + 4, p.hx + 11, y, col); c.HLine(p.hx + 4, p.hx + 11, y + 1, PixelCanvas.Shade(col, 0.75f)); return; }
            c.HLine(p.hx + 2, p.hx + 13, y, col);
            c.HLine(p.hx + 2, p.hx + 13, y + 1, PixelCanvas.Shade(col, 0.75f));
            if (!p.v.back) { c.Rect(p.hx + 7, y, 2, 2, buckle); }
        }

        static void BigStrap(PixelCanvas c, BigPose p)
        {
            BigBelt(c, p, MonLeather, Steel);
            if (p.v.side) return;
            c.Line(p.hx + 2, p.T + 1, p.hx + 13, p.T + 8, MonLeather);
            c.Line(p.hx + 3, p.T + 1, p.hx + 14, p.T + 8, MonLeatherDark);
        }

        static void BigPauldrons(PixelCanvas c, BigPose p, Color32 col, Color32 dark, Color32 light)
        {
            int T = p.T;
            if (p.v.side)
            {
                c.Rect(p.hx + 5, T - 1, 7, 4, col);
                c.HLine(p.hx + 5, p.hx + 11, T + 2, dark);
                c.HLine(p.hx + 6, p.hx + 9, T - 1, light);
                return;
            }
            int l = p.hx - 3, r = p.hx + 12;
            c.Rect(l, T - 1, 7, 4, col); c.Rect(r, T - 1, 7, 4, col);
            c.HLine(l, l + 6, T + 2, dark); c.HLine(r, r + 6, T + 2, dark);
            c.HLine(l + 1, l + 4, T - 1, light); c.HLine(r + 1, r + 4, T - 1, light);
            c.Set(l + 3, T + 1, MonIronLight); c.Set(r + 3, T + 1, MonIronLight);
        }

        static void BigPlate(PixelCanvas c, BigPose p, Color32 col, Color32 dark, Color32 light, Color32 trim)
        {
            int T = p.T;
            if (p.v.side)
            {
                c.Rect(p.hx + 5, T + 1, 8, 8, col);
                c.VLine(p.hx + 5, T + 1, T + 8, dark);
                c.VLine(p.hx + 12, T + 2, T + 7, light);
                c.HLine(p.hx + 5, p.hx + 12, T + 9, trim);
            }
            else
            {
                c.Rect(p.hx + 2, T + 1, 12, 8, col);
                c.VLine(p.hx + 13, T + 1, T + 8, dark);
                c.HLine(p.hx + 2, p.hx + 13, T + 8, dark);
                c.HLine(p.hx + 2, p.hx + 13, T + 9, trim);
                if (!p.v.back)
                {
                    c.VLine(p.hx + 7, T + 2, T + 7, dark);
                    c.HLine(p.hx + 3, p.hx + 5, T + 2, light); c.Set(p.hx + 3, T + 3, light);
                    c.Set(p.hx + 7, T + 4, trim); c.Set(p.hx + 8, T + 4, trim);
                }
            }
            BigPauldrons(c, p, col, dark, light);
            // Greaves / tassets.
            if (!p.v.side) { c.Rect(p.hx + 3, T + 10, 4, 3, dark); c.Rect(p.hx + 9, T + 10, 4, 3, dark); }
        }

        static void BigFurCollar(PixelCanvas c, BigPose p)
        {
            int T = p.T;
            int x0 = p.v.side ? p.hx + 3 : p.hx - 1, x1 = p.v.side ? p.hx + 13 : p.hx + 16;
            c.HLine(x0, x1, T - 1, MonFur);
            c.HLine(x0 - 1, x1 + 1, T, MonFur);
            c.HLine(x0, x1, T + 1, PixelCanvas.Shade(MonFur, 0.85f));
            for (int x = x0 + 1; x < x1; x += 3) c.Set(x, T, MonSocket);
            if (!p.v.back && !p.v.side) { c.Set(p.hx + 7, T + 1, Red); c.Set(p.hx + 8, T + 1, Gold); }
        }

        static void BigCrown(PixelCanvas c, BigPose p)
        {
            int hx = p.hx, hy = p.hy;
            int x0 = p.v.side ? hx + 3 : hx + 2, x1 = p.v.side ? hx + 14 : hx + 13;
            c.Rect(x0, hy - 2, x1 - x0 + 1, 3, Gold);
            c.HLine(x0, x1, hy, PixelCanvas.Shade(Gold, 0.75f));
            for (int x = x0; x <= x1; x += 3) { c.VLine(x, hy - 5, hy - 3, Gold); c.Set(x, hy - 6, Yellow); }
            c.HLine(x0 + 1, x1 - 1, hy - 2, Yellow);
            if (!p.v.back)
            {
                int mid = (x0 + x1) / 2;
                c.Rect(mid, hy - 2, 2, 2, Red); c.Set(mid, hy - 2, RedLight);
                c.Set(x0 + 2, hy - 1, MagicCore); c.Set(x1 - 2, hy - 1, MagicCore);
            }
        }

        static void BigBoneCrown(PixelCanvas c, BigPose p)
        {
            var bone = PixelCanvas.Hex("#d8d0b8");
            int hx = p.hx, hy = p.hy;
            for (int i = 0; i < 5; i++)
            {
                int x = hx + 2 + i * 3;
                int h = i == 2 ? 6 : i % 2 == 0 ? 4 : 5;
                c.VLine(x, hy - h, hy, bone);
                c.Set(x + 1, hy - 1, PixelCanvas.Shade(bone, 0.75f));
            }
            c.HLine(hx + 1, hx + 14, hy + 1, MonRobeLight);
            if (!p.v.back) c.Set(hx + 8, hy - 3, MonSoul);
        }

        static void BigHood(PixelCanvas c, BigPose p)
        {
            int hx = p.hx, hy = p.hy;
            c.HLine(hx + 4, hx + 11, hy - 2, MonHood);
            c.HLine(hx + 2, hx + 13, hy - 1, MonHood);
            c.Rect(hx, hy, 16, 4, MonHood);
            c.HLine(hx + 4, hx + 9, hy - 2, MonHoodLight);
            if (p.v.back)
            {
                c.Rect(hx - 1, hy + 3, 18, 11, MonHood);
                c.VLine(hx + 7, hy + 1, hy + 12, MonHoodDark);
                c.Set(hx + 8, hy - 3, MonHood); c.Set(hx + 8, hy - 4, MonHoodDark);
                return;
            }
            if (p.v.side)
            {
                c.Rect(hx - 1, hy + 2, 8, 12, MonHood);
                c.VLine(hx + 6, hy + 3, hy + 13, MonHoodDark);
                c.Set(hx - 2, hy + 1, MonHood); c.Set(hx - 3, hy, MonHoodDark);
                // Mask over the jaw.
                c.Rect(hx + 12, hy + 10, 10, 4, MonHoodDark);
                return;
            }
            c.Rect(hx - 1, hy + 3, 2, 11, MonHood); c.Rect(hx + 15, hy + 3, 2, 11, MonHoodDark);
            c.HLine(hx + 1, hx + 14, hy + 3, MonHoodDark);
            // Mask over the jaw.
            c.Rect(hx + 2, hy + 10, 12, 4, MonHoodDark);
            c.HLine(hx + 2, hx + 13, hy + 10, MonHood);
        }

        static void BigGreatHelm(PixelCanvas c, BigPose p)
        {
            int hx = p.hx, hy = p.hy;
            c.HLine(hx + 3, hx + 12, hy - 2, MonIron);
            c.HLine(hx + 1, hx + 14, hy - 1, MonIron);
            c.Rect(hx - 1, hy, 18, 14, MonIron);
            c.VLine(hx + 16, hy, hy + 13, MonIronDark);
            c.HLine(hx - 1, hx + 16, hy + 13, MonIronDark);
            c.HLine(hx + 3, hx + 8, hy - 1, MonIronLight); c.VLine(hx, hy + 1, hy + 6, MonIronLight);
            // Gold ridge.
            if (!p.v.side) c.VLine(hx + 7, hy - 2, hy + 4, Gold);
            if (p.v.back) { c.HLine(hx, hx + 15, hy + 8, MonIronDark); return; }
            if (p.v.side)
            {
                c.HLine(hx + 9, hx + 16, hy + 6, MonSocket);
                c.Set(hx + 12, hy + 6, Red); c.Set(hx + 13, hy + 6, RedLight);
                for (int y = hy + 8; y <= hy + 11; y += 2) c.Set(hx + 15, y, MonSocket);
                return;
            }
            int s = p.v.diag ? 1 : 0;
            c.HLine(hx + 1 + s, hx + 14 + s, hy + 6, MonSocket);
            c.VLine(hx + 7 + s, hy + 6, hy + 11, MonSocket);
            c.Set(hx + 4 + s, hy + 6, Red); c.Set(hx + 11 + s, hy + 6, Red);
            for (int y = hy + 8; y <= hy + 11; y += 2) { c.Set(hx + 4 + s, y, MonSocket); c.Set(hx + 10 + s, y, MonSocket); }
        }

        static void BigRobe(PixelCanvas c, BigPose p)
        {
            int T = p.T, hx = p.hx;
            int sway = p.step;
            int bottom = BodyOY + 38;
            bool side = p.v.side;
            int x0 = side ? hx + 3 : hx, x1 = side ? hx + 13 : hx + 15;
            for (int y = T + 1; y <= bottom; y++)
            {
                int widen = (y - T) / 4;
                int a = x0 - widen + (y > bottom - 3 ? (sway < 0 ? 1 : 0) : 0);
                int b = x1 + widen - (y > bottom - 3 ? (sway > 0 ? 1 : 0) : 0);
                c.HLine(a, b, y, MonRobe);
                c.Set(a, y, MonRobeDark); c.Set(b, y, MonRobeDark);
            }
            c.HLine(x0 - 4, x1 + 4, bottom, MonSoul);
            c.HLine(x0 - 3, x1 + 3, bottom - 1, MonRobeDark);
            if (!p.v.back && !side)
            {
                // Open front with the ribs showing, gold clasp, green trim.
                c.Rect(hx + 5, T + 1, 6, 6, PixelCanvas.Clear);
                for (int y = T + 2; y <= T + 6; y += 2) c.HLine(hx + 5, hx + 10, y, PixelCanvas.Hex("#e6ecd8"));
                c.VLine(hx + 4, T + 1, bottom - 2, MonSoul);
                c.VLine(hx + 11, T + 1, bottom - 2, MonSoul);
                c.Rect(hx + 7, T + 7, 2, 2, Gold);
            }
            else if (side) c.VLine(x1 - 2, T + 2, bottom - 2, MonRobeLight);
            // High collar.
            if (!side) { c.Rect(hx - 2, T - 3, 3, 5, MonRobeLight); c.Rect(hx + 15, T - 3, 3, 5, MonRobeLight); }
            else c.Rect(hx + 1, T - 3, 3, 5, MonRobeLight);
        }

        static void BigCloak(PixelCanvas c, BigPose p, Color32 col, Color32 dark)
        {
            int T = p.T, hx = p.hx;
            int bottom = BodyOY + 37;
            if (p.v.side)
            {
                for (int y = T; y <= bottom; y++)
                {
                    int w = 4 + (y - T) / 3 + (p.step != 0 ? 1 : 0);
                    c.HLine(hx + 4 - w, hx + 5, y, col);
                    c.Set(hx + 4 - w, y, dark);
                }
                return;
            }
            for (int y = T; y <= bottom; y++)
            {
                int widen = (y - T) / 3;
                c.HLine(hx - 1 - widen, hx + 16 + widen, y, p.v.back ? col : dark);
            }
            if (p.v.back)
            {
                for (int x = hx + 1; x < hx + 16; x += 4) c.VLine(x, T + 3, bottom - 1, dark);
                c.HLine(hx - 8, hx + 23, bottom, dark);
            }
        }

        static void BigGoldSack(PixelCanvas c, BigPose p)
        {
            float cx = p.v.side ? p.hx + 1 : p.v.back ? p.hx + 8 : p.hx - 2;
            float cy = p.T + 4;
            c.Ellipse(cx, cy, 6f, 7f, MonSack);
            c.PaintEllipse(cx + 2f, cy + 2f, 3.5f, 4f, MonSackDark);
            c.HLine((int)cx - 3, (int)cx + 2, (int)cy - 7, MonSackDark);
            // Coins spilling from the top.
            c.Set((int)cx - 2, (int)cy - 8, MonCoin); c.Set((int)cx, (int)cy - 9, MonCoin); c.Set((int)cx + 1, (int)cy - 8, Yellow);
            c.Set((int)cx - 1, (int)cy - 9, White);
        }

        static void BigQuiver(PixelCanvas c, BigPose p)
        {
            int x = p.v.side ? p.hx + 2 : p.v.back ? p.hx + 10 : p.hx + 13;
            int y = p.T - 4;
            c.Rect(x, y, 4, 13, MonLeather);
            c.VLine(x + 3, y, y + 12, MonLeatherDark);
            c.HLine(x, x + 3, y + 3, Gold);
            for (int i = 0; i < 3; i++) { c.VLine(x + i, y - 4 + i % 2, y - 1, WoodLight); c.Set(x + i, y - 5 + i % 2, Red); }
        }

        static void BigTowerShield(PixelCanvas c, BigPose p)
        {
            int T = p.T;
            if (p.v.back)
            {
                c.Rect(p.hx - 5, T + 1, 4, 17, MonIronDark);
                c.VLine(p.hx - 5, T + 1, T + 17, MonIron);
                return;
            }
            if (p.v.side)
            {
                int x = p.hx + 17;
                c.Rect(x, T - 2, 4, 20, MonIron);
                c.VLine(x, T - 2, T + 17, MonIronLight);
                c.VLine(x + 3, T - 2, T + 17, MonIronDark);
                c.Rect(x + 1, T + 6, 2, 3, Gold);
                return;
            }
            int x0 = p.v.diag ? p.hx + 7 : p.hx - 6, w = 12, top = T - 1, h = 21;
            c.Rect(x0, top, w, h, MonIron);
            c.HLine(x0, x0 + w - 1, top, MonIronLight);
            c.VLine(x0, top, top + h - 1, MonIronLight);
            c.VLine(x0 + w - 1, top + 1, top + h - 1, MonIronDark);
            c.HLine(x0, x0 + w - 1, top + h - 1, MonIronDark);
            c.Set(x0, top + h - 1, PixelCanvas.Clear); c.Set(x0 + w - 1, top + h - 1, PixelCanvas.Clear);
            // Rivets and a skull emblem.
            for (int y = top + 2; y < top + h - 2; y += 4) { c.Set(x0 + 1, y, MonIronDark); c.Set(x0 + w - 2, y, MonIronDark); }
            int ex = x0 + w / 2 - 2, ey = top + 6;
            c.Rect(ex, ey, 4, 3, Gold); c.Rect(ex + 1, ey + 3, 2, 2, Gold);
            c.Set(ex + 1, ey + 1, MonSocket); c.Set(ex + 2, ey + 1, MonSocket);
            c.HLine(x0 + 2, x0 + w - 3, top + h - 5, Red);
        }

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
