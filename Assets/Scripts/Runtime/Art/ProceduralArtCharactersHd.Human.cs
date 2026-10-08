using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Human ----------

        static void DrawHumanBodyHd(PixelCanvas c, CharacterLook L, View v, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            int o = bob * 2; // vertical bob, doubled

            var pants = L.pants;
            var pantsDark = Lit(pants, 0.78f);
            var pantsLight = Lit(pants, 1.12f);
            var shirt = L.shirt;
            var shirtDark = Lit(shirt, 0.76f);
            var shirtLight = Lit(shirt, 1.14f);
            var skin = L.skin;
            var skinDark = Lit(skin, 0.84f);
            var skinLight = Lit(skin, 1.1f);
            var hair = L.hair;
            var hairDark = Lit(hair, 0.74f);
            bool attack = frame == "attack";

            DrawLegsHd(c, v, step, pants, pantsDark, pantsLight);

            // ---- Torso (rows 24..31) ----
            DrawTorsoHd(c, L, v, o, attack, step, shirt, shirtDark, shirtLight, skin, skinDark);

            if (L.robe) DrawRobeHd(c, L, v, step, o, shirtDark);
            if (L.armor != ArmorStyle.None) DrawArmorHd(c, L, v, o);

            // ---- Head (rows 4..23), 20px wide rounded ----
            int hx = 6;      // head left edge
            int hy = 4 + o;
            DrawHeadHd(c, v, hx, hy, skin, skinDark, skinLight, frame);
            DrawHairHd(c, L, v, hx, hy, hairDark);
            DrawHatHd(c, L, v, hx, hy);
        }

        static void DrawLegsHd(PixelCanvas c, View v, int step, Color32 pants, Color32 pantsDark, Color32 pantsLight)
        {
            // rows 32..37
            if (v.side)
            {
                int back = step == 0 ? 12 : (step < 0 ? 10 : 14);
                int front = step == 0 ? 16 : (step < 0 ? 18 : 14);
                c.Rect(back, 32, 4, 4, pantsDark); c.Rect(back, 36, 4, 2, ShoeCol); c.HLine(back, back + 3, 37, ShoeDark);
                c.Rect(front, 32, 4, 4, pants); c.HLine(front, front + 3, 32, pantsLight);
                c.Rect(front, 36, 6, 2, ShoeCol); c.HLine(front, front + 5, 37, ShoeDark);
            }
            else if (v.diag)
            {
                int lLift = step < 0 ? 2 : 0, rLift = step > 0 ? 2 : 0;
                c.Rect(10, 32, 4, 4 - lLift, pantsDark); c.Rect(10, 36 - lLift, 4, 2, ShoeCol); c.HLine(10, 13, 37 - lLift, ShoeDark);
                c.Rect(16, 32, 4, 4 - rLift, pants); c.HLine(16, 19, 32, pantsLight);
                c.Rect(16, 36 - rLift, v.back ? 4 : 6, 2, ShoeCol); c.HLine(16, 16 + (v.back ? 3 : 5), 37 - rLift, ShoeDark);
                c.VLine(14, 32, 35, pantsDark);
            }
            else
            {
                int lLift = step < 0 ? 2 : 0, rLift = step > 0 ? 2 : 0;
                c.Rect(10, 32, 4, 4 - lLift, pants); c.HLine(10, 13, 32, pantsLight);
                c.Rect(10, 36 - lLift, 4, 2, ShoeCol); c.HLine(10, 13, 37 - lLift, ShoeDark); c.Set(10, 36 - lLift, Lit(ShoeCol, 1.25f)); // toe cap
                c.Rect(18, 32, 4, 4 - rLift, pants); c.HLine(18, 21, 32, pantsLight);
                c.Rect(18, 36 - rLift, 4, 2, ShoeCol); c.HLine(18, 21, 37 - rLift, ShoeDark); c.Set(18, 36 - rLift, Lit(ShoeCol, 1.25f));
                c.Rect(14, 32, 4, 2, pantsDark); // gap between legs
                c.VLine(11, 33, 35, pantsDark); c.VLine(19, 33, 35, pantsDark); // inseam shade
            }
        }

        static void DrawTorsoHd(PixelCanvas c, CharacterLook L, View v, int o, bool attack, int step,
            Color32 shirt, Color32 shirtDark, Color32 shirtLight, Color32 skin, Color32 skinDark)
        {
            int top = 24 + o, bot = 31 + o;
            if (v.side)
            {
                c.Rect(10, top, 12, 8, shirt);
                c.HLine(10, 21, top, shirtLight);
                c.HLine(10, 20, 31 + o, L.pants);       // waist
                c.VLine(10, top, bot, shirtDark);
                c.Set(11, top + 1, shirtLight);
                // Arm.
                if (attack)
                {
                    c.Rect(20, top, 6, 4, shirt); c.HLine(20, 25, top, shirtLight);
                    c.Rect(26, top, 2, 4, skin); c.Set(26, top, Lit(skin, 1.1f));
                }
                else
                {
                    int armX = 14 + step * 2;
                    c.Rect(armX, top, 4, 4, shirtDark);
                    c.Rect(armX, top + 4, 4, 2, skin); c.HLine(armX, armX + 3, top + 5, skinDark);
                }
            }
            else if (v.diag)
            {
                c.Rect(8, top, 14, 8, shirt);
                c.HLine(8, 21, top, shirtLight);
                c.HLine(8, 20, 31 + o, L.pants);
                c.VLine(8, top, bot, shirtDark);
                if (!v.back) { c.VLine(16, top + 1, top + 5, shirtDark); c.VLine(17, top + 1, top + 5, shirtDark); } // front seam
                c.Set(6, top + 2, shirtDark); // far shoulder
                if (attack && !v.back) { c.Rect(22, top, 4, 4, shirt); c.Rect(26, top + 1, 2, 3, skin); }
                else if (attack) { c.Rect(22, top - 4, 3, 6, shirt); c.Rect(22, top - 6, 2, 2, skin); }
                else
                {
                    c.Rect(22, top, 3, 5, shirt); c.Rect(22, top + 5, 3, 2, skin); c.HLine(22, 24, top + 6, skinDark);
                    if (step != 0) c.Rect(step > 0 ? 22 : 6, top + 7, 2, 1, skin);
                }
            }
            else
            {
                c.Rect(8, top, 16, 8, shirt);
                c.HLine(8, 23, top, shirtLight);
                c.HLine(8, 23, 31 + o, L.pants);
                c.VLine(8, top, bot, shirtDark);
                c.VLine(23, top + 1, bot, shirtDark);            // right-side fold shade
                c.Set(10, top + 1, shirtLight); c.Set(11, top + 1, shirtLight); // top-left sheen
                if (!v.back) { c.VLine(15, top + 1, top + 5, shirtDark); c.VLine(16, top + 1, top + 5, shirtDark); } // collar/placket
                // Arms on both sides.
                c.Rect(6, top, 2, 5, shirt); c.Rect(6, top + 5, 2, 2, skin); c.HLine(6, 7, top + 6, skinDark);
                if (attack && !v.back) { c.Rect(24, top, 2, 6, shirt); c.Rect(24, top + 6, 2, 2, skin); }
                else if (attack) { c.Rect(24, top - 4, 2, 6, shirt); c.Rect(24, top - 6, 2, 2, skin); }
                else { c.Rect(24, top, 2, 5, shirt); c.Rect(24, top + 5, 2, 2, skin); c.HLine(24, 25, top + 6, skinDark); }
                if (step != 0 && !attack)
                {
                    // Swing: the forward arm lifts a touch.
                    int ax = step < 0 ? 6 : 24;
                    c.Rect(ax, top + 7, 2, 1, skin);
                }
                // Belt + buckle at the waist (skipped when a robe/armor will cover it).
                if (!L.robe && L.armor == ArmorStyle.None)
                {
                    c.HLine(8, 23, top + 7, PixelCanvas.Hex("#4a2e1a"));
                    c.Rect(15, top + 7, 2, 1, Gold);
                }
            }
        }

        static void DrawHeadHd(PixelCanvas c, View v, int hx, int hy, Color32 skin, Color32 skinDark, Color32 skinLight, string frame)
        {
            // 20px wide rounded head, rows hy..hy+19.
            c.HLine(hx + 4, hx + 15, hy, skin);
            c.HLine(hx + 2, hx + 17, hy + 1, skin);
            c.HLine(hx + 1, hx + 18, hy + 2, skin);
            c.Rect(hx, hy + 3, 20, 12, skin);
            c.HLine(hx + 1, hx + 18, hy + 15, skin);
            c.HLine(hx + 2, hx + 17, hy + 16, skin);
            c.HLine(hx + 4, hx + 15, hy + 17, skinDark);
            // Soft shading: right and bottom of the face slightly darker, top-left cheek lit.
            c.VLine(hx + 18, hy + 4, hy + 14, skinDark);
            c.HLine(hx + 3, hx + 16, hy + 16, skinDark);
            c.Set(hx + 2, hy + 3, skinLight); c.Set(hx + 3, hy + 3, skinLight);

            bool hurt = frame == "hurt";
            if (v.back)
            {
                // Back of the head: rounded skull, a lit nape below the hairline, ears on the sides.
                // (The hair mass is drawn on top by DrawHairHd; here we shape the neck/ears that show.)
                if (!v.diag)
                {
                    c.HLine(hx + 6, hx + 13, hy + 17, skin);       // nape
                    c.HLine(hx + 7, hx + 12, hy + 18, skinDark);
                    c.Set(hx, hy + 8, skinDark); c.Set(hx + 19, hy + 8, skinDark); // ear hints
                    c.Set(hx - 1, hy + 9, skin); c.Set(hx + 20, hy + 9, skin);
                }
                else
                {
                    c.HLine(hx + 4, hx + 12, hy + 17, skin);
                    c.Set(hx + 17, hy + 10, skinDark); c.Set(hx + 18, hy + 10, skin); // near ear
                    c.Set(hx + 17, hy + 11, skinDark);
                }
                return;
            }

            if (v.side)
            {
                // Profile: brow, one eye, nose bump, mouth, cheek blush.
                c.HLine(hx + 12, hx + 15, hy + 7, skinDark);          // brow
                c.Rect(hx + 13, hy + 9, 2, 4, Eye);
                c.Set(hx + 14, hy + 9, EyeWhite);                     // highlight
                c.Set(hx + 19, hy + 10, skinLight);                   // nose bridge
                c.Rect(hx + 19, hy + 11, 2, 2, skin); c.Set(hx + 20, hy + 12, skinDark);
                c.HLine(hx + 15, hx + 17, hy + 15, skinDark);         // mouth
                c.Rect(hx + 11, hy + 13, 2, 2, Blush);
                if (hurt) { c.Rect(hx + 13, hy + 9, 2, 2, skin); c.Set(hx + 13, hy + 10, Eye); c.Set(hx + 14, hy + 11, Eye); }
            }
            else if (v.diag)
            {
                c.HLine(hx + 7, hx + 9, hy + 7, skinDark); c.HLine(hx + 14, hx + 16, hy + 7, skinDark); // brows
                c.Rect(hx + 7, hy + 9, 2, 4, Eye); c.Set(hx + 8, hy + 9, EyeWhite);
                c.Rect(hx + 14, hy + 9, 2, 4, Eye); c.Set(hx + 15, hy + 9, EyeWhite);
                c.Set(hx + 11, hy + 12, skinDark); c.Set(hx + 11, hy + 13, skinLight); // nose (turned)
                c.HLine(hx + 9, hx + 13, hy + 15, skinDark);          // mouth
                c.Rect(hx + 5, hy + 13, 2, 2, Blush);
                c.Rect(hx + 16, hy + 13, 2, 2, Blush);
                if (hurt)
                {
                    c.Rect(hx + 7, hy + 9, 2, 2, skin); c.Rect(hx + 14, hy + 9, 2, 2, skin);
                    c.Set(hx + 7, hy + 10, Eye); c.Set(hx + 8, hy + 11, Eye);
                    c.Set(hx + 15, hy + 10, Eye); c.Set(hx + 14, hy + 11, Eye);
                }
            }
            else
            {
                // Front: brows, two eyes (2-3px w, 3-4px tall, 1px highlight), nose, mouth, blush.
                c.HLine(hx + 3, hx + 6, hy + 7, skinDark);  c.HLine(hx + 13, hx + 16, hy + 7, skinDark);  // brows
                c.Rect(hx + 3, hy + 9, 3, 4, Eye);  c.Set(hx + 4, hy + 9, EyeWhite);
                c.Rect(hx + 14, hy + 9, 3, 4, Eye); c.Set(hx + 15, hy + 9, EyeWhite);
                c.Set(hx + 9, hy + 12, skinDark); c.Set(hx + 10, hy + 12, skinDark); // nose
                c.HLine(hx + 8, hx + 11, hy + 15, skinDark);                          // mouth
                c.Rect(hx + 1, hy + 13, 2, 2, Blush);
                c.Rect(hx + 17, hy + 13, 2, 2, Blush);
                if (hurt)
                {
                    // Squeezed eyes ( >< ) + open mouth.
                    c.Rect(hx + 3, hy + 9, 3, 3, skin); c.Rect(hx + 14, hy + 9, 3, 3, skin);
                    c.Set(hx + 3, hy + 10, Eye); c.Set(hx + 4, hy + 11, Eye); c.Set(hx + 5, hy + 10, Eye);
                    c.Set(hx + 16, hy + 10, Eye); c.Set(hx + 15, hy + 11, Eye); c.Set(hx + 14, hy + 10, Eye);
                    c.Rect(hx + 9, hy + 15, 2, 2, PixelCanvas.Hex("#7a3b32"));
                }
            }
        }

        static void DrawWizardConeHd(PixelCanvas c, CharacterLook L, View v, int hx, int hy)
        {
            var col = L.hatColor;
            var dark = Lit(col, 0.72f);
            var light = Lit(col, 1.28f);
            int bend = v.side || v.diag ? -1 : 1;
            // Stacked cone, narrowing to the tip; hy is the top of the brim area.
            c.Rect(hx + 4, hy - 2, 12, 2, col);
            c.Rect(hx + 5, hy - 4, 10, 2, col);
            c.Rect(hx + 6, hy - 6, 8, 2, col);
            c.Rect(hx + 7 + (bend > 0 ? 1 : 0), hy - 8, 5, 2, col);
            c.Rect(hx + 9 + bend * 2, hy - 10, 3, 2, col);
            c.Set(hx + 10 + bend * 4, hy - 11, col);
            c.Set(hx + 11 + bend * 5, hy - 11, dark);
            // Shading down the right side, light streak on the left.
            c.VLine(hx + 14, hy - 4, hy - 1, dark);
            c.VLine(hx + 6, hy - 5, hy - 1, light);
            if (!v.back) { c.Set(hx + 8, hy - 3, Yellow); c.Set(hx + 9, hy - 3, Yellow); } // star
            c.Set(hx + 7, hy - 6, light);
        }

        static void DrawRobeHd(PixelCanvas c, CharacterLook L, View v, int step, int o, Color32 shirtDark)
        {
            var robe = L.robeColor.a > 0 ? L.robeColor : L.shirt;
            var robeDark = Lit(robe, 0.76f);
            var robeLight = Lit(robe, 1.12f);
            // Clear the legs so the robe silhouette is clean.
            c.Rect(6, 32, 22, 6, PixelCanvas.Clear);
            if (v.side)
            {
                c.Rect(10, 30, 12, 4, robe);
                c.Rect(8 + (step < 0 ? 2 : 0), 34, 14, 3, robe);
                c.HLine(8 + (step < 0 ? 2 : 0), 20 + (step > 0 ? 2 : 0), 37, Gold);
                c.VLine(10, 30, 36, robeDark);
                c.HLine(10, 21, 30, robeLight);
                if (step != 0) c.Rect(step > 0 ? 20 : 8, 36, 2, 2, RobeShoe);
            }
            else
            {
                c.Rect(8, 30, 16, 4, robe);
                c.Rect(6, 34, 20, 3, robe);
                c.HLine(6, 25, 37, Gold);         // hem trim
                c.VLine(8, 30, 36, robeDark); c.VLine(23, 30, 36, robeDark);
                c.HLine(8, 23, 30, robeLight);
                if (!v.back) c.VLine(v.diag ? 18 : 16, 30, 36, robeDark); // front opening fold
                // Feet peek out under the hem while walking.
                if (step < 0) c.Rect(10, 36, 4, 2, RobeShoe);
                else if (step > 0) c.Rect(18, 36, 4, 2, RobeShoe);
                c.HLine(8, 23, 30 + o, Gold);     // belt at the waist
            }
        }

        static void DrawArmorHd(PixelCanvas c, CharacterLook L, View v, int o)
        {
            var col = L.armorColor;
            var dark = Lit(col, 0.7f);
            var light = Lit(col, 1.22f);
            int top = 24 + o;
            int x0 = v.side ? 10 : 8, x1 = v.side || v.diag ? 21 : 23;
            switch (L.armor)
            {
                case ArmorStyle.Vest:
                    for (int y = top; y <= top + 6; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            bool gap = v.front && !v.side && (v.diag ? (x >= 15 && x <= 18) : (x >= 14 && x <= 17));
                            if (!gap) c.Set(x, y, col);
                        }
                    c.VLine(x0, top, top + 6, dark);
                    c.HLine(x0, x1, top, light);
                    if (v.front && !v.side)
                    {
                        int cx = (x0 + x1) / 2;
                        c.VLine(cx - 3, top + 1, top + 5, dark); c.VLine(cx + 3, top + 1, top + 5, dark);
                        c.Set(cx - 3, top + 3, Gold); c.Set(cx + 3, top + 3, Gold); // buttons
                    }
                    break;
                case ArmorStyle.Leather:
                    c.Rect(x0, top, x1 - x0 + 1, 6, col);
                    c.VLine(x0, top, top + 5, dark);
                    c.HLine(x0, x1, top, light);
                    if (!v.side) c.Line(x0 + 2, top, x1 - 2, top + 5, dark);        // chest strap
                    c.Rect(x0, top + 6, x1 - x0 + 1, 2, PixelCanvas.Hex("#4a2e1a")); // belt
                    if (!v.back) c.Rect((x0 + x1) / 2, top + 6, 2, 2, Gold);          // buckle
                    c.Set(x1 - 1, top, light);
                    break;
                case ArmorStyle.Plate:
                    c.Rect(x0, top, x1 - x0 + 1, 6, col);
                    c.HLine(x0, x1, top + 3, dark);        // chest split line
                    c.VLine(x0, top, top + 5, dark);
                    c.HLine(x0, x1, top, light);
                    if (!v.back) { c.Rect(x0 + 3, top + 1, 3, 2, light); c.Set(x0 + 4, top + 3, light); } // pec highlight
                    c.Set(x1 - 1, top + 4, Gold); // rivet
                    c.Set(x0 + 2, top + 5, dark); c.Set(x1 - 2, top + 5, dark);
                    // Shoulder plates.
                    if (v.side)
                    {
                        c.Rect(12, top - 2, 8, 3, col); c.HLine(12, 19, top, dark); c.Rect(13, top - 2, 2, 1, light);
                    }
                    else
                    {
                        int l = v.diag ? 6 : 4, r = v.diag ? 20 : 22;
                        c.Rect(l, top - 2, 5, 3, col); c.Rect(r, top - 2, 5, 3, col);
                        c.HLine(l, l + 4, top, dark); c.HLine(r, r + 4, top, dark);
                        c.Rect(l + 1, top - 2, 2, 1, light); c.Rect(r + 1, top - 2, 2, 1, light);
                    }
                    break;
            }
        }
    }
}
