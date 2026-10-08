using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
