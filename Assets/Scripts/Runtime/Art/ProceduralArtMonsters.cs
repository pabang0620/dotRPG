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
        static readonly Color32 MonFur = PixelCanvas.Hex("#f4efe6");
        static readonly Color32 MonSocket = PixelCanvas.Hex("#2b1d16");

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
    }
}
