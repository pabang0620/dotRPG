using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution (density 2, 32px per tile) characters and monsters. Same world size as the old
    /// 16x20 art — a frame is 32x40 with doubled pivot — but twice the detail: eyes with highlights,
    /// hair strands and shine, cloth folds, armour plates and rivets, belts and buckles, a clean 1px
    /// dark outline. Five views (down, up, side, downside, upside; left = flipped by the renderer) x
    /// eight frames (idle0, idle1, walk0-3, attack, hurt). Also the held weapons wpn_*, held tools
    /// tool_*, damage digits num_*, monster HP bars hpbar_* and the ground shadow.
    ///
    /// The old 16px functions stay in ProceduralArt.cs / ProceduralArtCharacters.cs untouched; the
    /// DrawCharacter and DrawCharacterFamily routers point here.
    /// </summary>
    public static partial class ProceduralArt
    {
        // ---------- Palette helpers (light from the top-left, soft ramps) ----------

        // Frame is 32 wide x 40 tall. Old layout x2: head rows 4..23, body 24..31, legs 32..37.
        const int W = 32, HGT = 40;

        static readonly Color32 ChSkinShade = PixelCanvas.Hex("#00000000"); // unused placeholder
        static readonly Color32 Blush = PixelCanvas.Hex("#f0968a");
        static readonly Color32 EyeWhite = PixelCanvas.Hex("#f7f2e8");
        static readonly Color32 ShoeCol = PixelCanvas.Hex("#4a3226");
        static readonly Color32 ShoeDark = PixelCanvas.Hex("#33231b");
        static readonly Color32 RobeShoe = PixelCanvas.Hex("#3a2a40");

        static Color32 Lit(Color32 c, float f) => PixelCanvas.Shade(c, f);

        /// <summary>Fills a solid vertical-lit column block: darker at the bottom/right edges.</summary>
        static void Body3(PixelCanvas c, int x, int y, int w, int h, Color32 col)
        {
            var dark = Lit(col, 0.8f);
            var light = Lit(col, 1.15f);
            c.Rect(x, y, w, h, col);
            c.VLine(x, y, y + h - 1, dark);           // left shade
            c.HLine(x, x + w - 1, y + h - 1, dark);    // bottom shade
            c.HLine(x, x + w - 1, y, light);           // top light
        }

        // ---------- Router entry points (called by ProceduralArt.Draw) ----------

        static PixelCanvas DrawCharacterHd(CharacterLook look, string dir, string frame)
        {
            var c = Hd(W, HGT);
            var v = new View(dir);
            if (look.body == BodyKind.Skeleton) DrawSkeletonBodyHd(c, look, v, frame);
            else DrawHumanBodyHd(c, look, v, frame);

            if (look.body == BodyKind.Human && look.hat == HatKind.Wizard)
            {
                // Pointed hat does not fit; move up into a taller canvas (pivot from the bottom, so the
                // feet stay put) and add the cone. 10 extra rows (5 old rows x2).
                const int extra = 10;
                var tall = Hd(W, HGT + extra);
                for (int y = 0; y < HGT; y++)
                    for (int x = 0; x < W; x++)
                        tall.Pixels[(y + extra) * W + x] = c.Pixels[y * W + x];
                FrameInfo(frame, out int bob, out _);
                DrawWizardConeHd(tall, look, v, 6, 4 + bob * 2 + extra);
                c = tall;
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        static PixelCanvas DrawCharacterFamilyHd(string key, string[] parts)
        {
            switch (parts[0])
            {
                case "wpn": return DrawWeaponHd(parts[1], parts.Length > 2 ? int.Parse(parts[2]) : 0);
                case "tool": return DrawToolHd(parts[1]);
                case "num": return DrawDigitHd(int.Parse(parts[1]));
                case "hpbar": return DrawHpBarHd(parts[1]);
                case "shadow": return DrawShadowHd();
            }
            return null;
        }

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

        // ---------- Hair ----------

        static void DrawHairHd(PixelCanvas c, CharacterLook L, View v, int hx, int hy, Color32 hairDark)
        {
            var h = L.hair;
            var hLight = Lit(h, 1.2f);
            bool up = v.back && !v.diag, upSide = v.back && v.diag, downSide = v.front && v.diag;

            if (L.hairStyle == HairStyle.Bald)
            {
                DrawBaldHd(c, L, v, hx, hy, h, hairDark);
                return;
            }

            // Common cap of hair over the top of the head.
            c.HLine(hx + 4, hx + 15, hy, h);
            c.HLine(hx + 2, hx + 17, hy + 1, h);
            c.Rect(hx, hy + 2, 20, 4, h);
            c.HLine(hx + 5, hx + 13, hy, hLight); // top shine

            var hMid = Lit(h, 0.9f);
            if (up)
            {
                // Rounded back-of-head hair mass with a curved hairline (the nape shows below the centre).
                c.Rect(hx, hy + 2, 20, 13, h);
                c.HLine(hx + 1, hx + 18, hy + 1, h);              // crown curve
                c.HLine(hx + 3, hx + 16, hy, h);
                // Side locks fall past a lifted centre hairline, so a lit nape shows between them.
                c.HLine(hx + 1, hx + 6, hy + 15, h); c.HLine(hx + 13, hx + 18, hy + 15, h);
                c.VLine(hx + 1, hy + 15, hy + 16, h); c.VLine(hx + 18, hy + 15, hy + 16, h);
                c.HLine(hx + 2, hx + 5, hy + 16, hairDark); c.HLine(hx + 14, hx + 17, hy + 16, hairDark);
                // Clumps: alternating mid/dark blocks give a "several locks" read.
                for (int x = hx + 1; x < hx + 19; x += 3)
                {
                    c.VLine(x, hy + 7, hy + 13, hMid);
                    c.VLine(x + 1, hy + 9, hy + 13, hairDark);
                }
                c.HLine(hx + 2, hx + 17, hy + 3, hLight);        // highlight band near the crown
                c.HLine(hx + 4, hx + 13, hy + 2, hLight);
                c.HLine(hx + 6, hx + 13, hy + 14, hairDark);     // shade just above the nape
            }
            else if (upSide)
            {
                // Turned back: mass shifted, ear side shorter, same clump + highlight treatment.
                c.Rect(hx, hy + 2, 16, 13, h);
                c.HLine(hx + 1, hx + 14, hy + 1, h);
                c.HLine(hx + 1, hx + 5, hy + 15, h); c.VLine(hx + 1, hy + 15, hy + 16, h); // far side lock
                for (int x = hx + 1; x < hx + 15; x += 3)
                {
                    c.VLine(x, hy + 7, hy + 13, hMid);
                    c.VLine(x + 1, hy + 9, hy + 13, hairDark);
                }
                c.HLine(hx + 2, hx + 13, hy + 3, hLight);        // highlight band
                c.HLine(hx + 4, hx + 12, hy + 14, hairDark);     // nape shade
                c.VLine(hx + 15, hy + 6, hy + 14, hairDark);     // fall past the ear
            }
            else if (v.side)
            {
                c.Rect(hx, hy + 2, 10, 12, h);
                c.Rect(hx + 10, hy + 2, 8, 4, h);
                c.Set(hx + 16, hy + 6, h); c.Set(hx + 17, hy + 6, h);
                c.VLine(hx + 8, hy + 6, hy + 13, hairDark);
                c.VLine(hx + 2, hy + 4, hy + 11, hLight);
            }
            else if (downSide)
            {
                c.Rect(hx, hy + 2, 6, 10, h);
                c.HLine(hx + 6, hx + 8, hy + 6, h); c.HLine(hx + 12, hx + 15, hy + 6, h); // swept fringe
                c.VLine(hx + 18, hy + 6, hy + 10, h);
                c.HLine(hx + 4, hx + 16, hy + 5, hairDark);
                c.VLine(hx + 4, hy + 6, hy + 12, hairDark);
                c.VLine(hx + 2, hy + 4, hy + 10, hLight);
            }
            else
            {
                // Front fringe with a parting.
                c.HLine(hx, hx + 3, hy + 6, h); c.HLine(hx + 8, hx + 11, hy + 6, h); c.HLine(hx + 16, hx + 19, hy + 6, h);
                c.VLine(hx, hy + 6, hy + 11, h);
                c.VLine(hx + 19, hy + 6, hy + 11, h);
                c.HLine(hx + 1, hx + 18, hy + 5, hairDark);
                c.Set(hx + 4, hy + 6, hairDark); c.Set(hx + 15, hy + 6, hairDark);
                c.HLine(hx + 5, hx + 12, hy + 1, hLight);
            }

            switch (L.hairStyle)
            {
                case HairStyle.Spiky:
                    c.Rect(hx + 2, hy - 2, 2, 2, h); c.Rect(hx + 8, hy - 3, 2, 3, h); c.Rect(hx + 14, hy - 2, 2, 2, h);
                    c.Set(hx + 10, hy - 4, h);
                    c.Set(hx + 3, hy - 2, hLight); c.Set(hx + 9, hy - 3, hLight);
                    if (v.front && !v.side && !v.diag) { c.Rect(hx + 4, hy + 6, 2, 2, h); c.Rect(hx + 14, hy + 6, 2, 2, h); }
                    break;
                case HairStyle.Long:
                    if (v.side) c.Rect(hx - 2, hy + 6, 5, 16, h);
                    else if (v.diag) { c.Rect(hx - 2, hy + 6, 5, 16, h); c.Rect(hx + 18, hy + 8, 2, v.back ? 8 : 12, h); }
                    else { c.Rect(hx - 2, hy + 8, 3, 14, h); c.Rect(hx + 19, hy + 8, 3, 14, h); }
                    if (v.back) c.Rect(hx, hy + 16, v.diag ? 16 : 20, 6, h);
                    // strand shine
                    if (!v.back) { c.VLine(hx - 1, hy + 8, hy + 18, hLight); if (!v.side && !v.diag) c.VLine(hx + 20, hy + 8, hy + 18, hLight); }
                    break;
                case HairStyle.Bun:
                    c.Ellipse(hx + (v.diag ? 8 : 10), hy - 2, 4.5f, 3.5f, h);
                    c.PaintEllipse(hx + (v.diag ? 7 : 9), hy - 3, 2f, 1.5f, hLight);
                    if (v.side || v.diag) { c.Ellipse(hx + 2, hy + 2, 4f, 3.5f, h); c.Ellipse(hx + (v.diag ? 8 : 10), hy - 2, 4.5f, 3.5f, h); }
                    break;
                case HairStyle.Curly:
                    for (int i = 0; i < 5; i++)
                    {
                        int cx = hx + 2 + i * 4;
                        c.Ellipse(cx, hy - 1, 2.2f, 2f, h);
                    }
                    c.Set(hx - 2, hy + 4, h); c.Set(hx + 21, hy + 4, h);
                    if (!v.side && !v.diag) { c.VLine(hx - 2, hy + 5, hy + 12, h); c.VLine(hx + 21, hy + 5, hy + 12, h); }
                    else c.VLine(hx - 2, hy + 5, hy + 14, h);
                    for (int i = 0; i < 3; i++) c.Set(hx + 4 + i * 6, hy - 2, hLight);
                    break;
            }
        }

        static void DrawBaldHd(PixelCanvas c, CharacterLook L, View v, int hx, int hy, Color32 h, Color32 hairDark)
        {
            bool up = v.back && !v.diag, upSide = v.back && v.diag, downSide = v.front && v.diag;
            if (up) c.Rect(hx, hy + 6, 20, 9, h);
            else if (upSide) c.Rect(hx, hy + 6, 16, 9, h);
            else if (v.side) c.Rect(hx, hy + 5, 6, 9, h);
            else if (downSide)
            {
                c.Rect(hx, hy + 5, 4, 9, h);
                // Beard, turned with the face.
                c.Rect(hx + 6, hy + 14, 12, 6, h);
                c.HLine(hx + 8, hx + 15, hy + 20, h);
                c.Set(hx + 10, hy + 14, Lit(L.skin, 0.8f)); c.Set(hx + 12, hy + 14, Lit(L.skin, 0.8f)); // mouth gap
                c.VLine(hx + 2, hy + 6, hy + 12, Lit(h, 1.15f));
            }
            else
            {
                c.Rect(hx, hy + 5, 2, 9, h); c.Rect(hx + 18, hy + 5, 2, 9, h); // side tufts
                c.Rect(hx + 4, hy + 14, 12, 6, h);   // beard
                c.HLine(hx + 6, hx + 13, hy + 20, h);
                c.Set(hx + 8, hy + 14, Lit(L.skin, 0.8f)); c.Set(hx + 11, hy + 14, Lit(L.skin, 0.8f)); // mouth
            }
        }

        // ---------- Hats ----------

        static void DrawHatHd(PixelCanvas c, CharacterLook L, View v, int hx, int hy)
        {
            var col = L.hatColor;
            var dark = Lit(col, 0.76f);
            var light = Lit(col, 1.18f);
            switch (L.hat)
            {
                case HatKind.Straw:
                    c.Rect(hx - 4, hy + 4, 28, 3, col);           // wide brim
                    c.HLine(hx - 4, hx + 23, hy + 6, dark);
                    c.HLine(hx - 4, hx + 23, hy + 4, light);
                    c.Rect(hx + 2, hy - 2, 16, 6, col);           // crown
                    c.HLine(hx + 2, hx + 17, hy + 2, PixelCanvas.Hex("#c4553d")); // band
                    c.HLine(hx + 4, hx + 12, hy - 2, light);
                    break;
                case HatKind.Cap:
                    c.Rect(hx, hy - 2, 20, 7, col);
                    c.HLine(hx + 2, hx + 15, hy - 2, light);
                    c.VLine(hx, hy - 1, hy + 4, dark);
                    if (v.side) c.Rect(hx + 16, hy + 5, 8, 2, dark);            // brim to the right
                    else if (v.front && v.diag) c.Rect(hx + 10, hy + 5, 12, 2, dark);
                    else if (v.front) c.HLine(hx + 2, hx + 17, hy + 6, dark);
                    break;
                case HatKind.Bandana:
                    c.Rect(hx, hy + 2, 20, 4, col);
                    c.HLine(hx, hx + 19, hy + 5, dark);
                    c.HLine(hx + 2, hx + 17, hy + 2, light);
                    // Knot tails on the back-left.
                    if (!v.front || v.diag) { c.Rect(hx - 2, hy + 6, 2, 3, col); c.Set(hx - 2, hy + 8, dark); c.Set(hx - 3, hy + 9, col); }
                    // Small dots pattern.
                    c.Set(hx + 5, hy + 3, light); c.Set(hx + 11, hy + 4, light); c.Set(hx + 15, hy + 3, light);
                    break;
                case HatKind.Wizard:
                    // Wide brim + gold band; the cone above is added by DrawWizardConeHd.
                    c.Rect(hx + 2, hy - 2, 16, 6, col);
                    c.HLine(hx + 2, hx + 17, hy + 2, Gold);
                    c.Rect(hx - 4, hy + 4, 28, 3, col);
                    c.HLine(hx - 4, hx + 23, hy + 6, dark);
                    c.HLine(hx - 4, hx + 23, hy + 4, light);
                    c.Set(hx - 4, hy + 4, dark); c.Set(hx + 23, hy + 4, dark);
                    break;
            }
        }

        // ---------- Skeleton ----------

        static void DrawSkeletonBodyHd(PixelCanvas c, CharacterLook L, View v, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            var bone = L.skin;
            var boneDark = Lit(bone, 0.76f);
            var boneLight = Lit(bone, 1.12f);
            var socket = PixelCanvas.Hex("#241812");
            bool attack = frame == "attack";
            bool hurt = frame == "hurt";
            int o = bob * 2;

            // ---- Legs ----
            if (v.side)
            {
                int back = step == 0 ? 14 : (step < 0 ? 12 : 16);
                int front = step == 0 ? 16 : (step < 0 ? 18 : 14);
                c.Rect(back, 32, 2, 6, boneDark); c.Rect(back - 1, 37, 3, 1, boneDark);
                c.Rect(front, 32, 2, 6, bone); c.Rect(front, 37, 4, 1, bone);
            }
            else if (v.diag)
            {
                int l = step < 0 ? 2 : 0, r = step > 0 ? 2 : 0;
                c.Rect(12, 32, 2, 6 - l, boneDark); c.Rect(10, 37 - l, 3, 1, boneDark);
                c.Rect(16, 32, 2, 6 - r, bone); c.Rect(16, 37 - r, 4, 1, bone);
            }
            else
            {
                int lLo = step < 0 ? 2 : 0, rLo = step > 0 ? 2 : 0;
                // Femur + shin with a knee joint pip.
                c.Rect(12, 32, 2, 6 - lLo, bone); c.Set(11, 34, boneLight); c.Set(13, 35, boneDark); // knee
                c.Rect(10, 37 - lLo, 4, 1, bone);
                c.Rect(18, 32, 2, 6 - rLo, bone); c.Set(17, 34, boneLight); c.Set(19, 35, boneDark);
                c.Rect(18, 37 - rLo, 4, 1, bone);
                c.VLine(14, 30 + o, 33, boneDark); c.VLine(17, 30 + o, 33, boneDark); // pelvis legs
            }

            // ---- Pelvis + spine + ribs ----
            int hipY = 30 + o;
            c.Rect(12, hipY, 8, 2, bone); c.HLine(12, 19, hipY + 1, boneDark); // pelvis
            c.VLine(15, 24 + o, hipY, bone); c.VLine(16, 24 + o, hipY, boneDark); // spine
            // Rib cage.
            if (v.side)
            {
                for (int r = 0; r < 3; r++) c.HLine(12, 19, 25 + o + r * 2, r % 2 == 0 ? bone : boneDark);
                int armX = attack ? 20 : 14 + step * 2;
                if (attack) { c.HLine(18, 26, 25 + o, bone); c.Set(27, 25 + o, boneLight); }
                else c.VLine(armX, 24 + o, 31 + o, bone);
            }
            else if (v.diag)
            {
                for (int r = 0; r < 3; r++) c.HLine(10, 20, 25 + o + r * 2, r % 2 == 0 ? bone : boneDark);
                c.VLine(8, 24 + o, 30 + o, boneDark); // far arm
                if (attack) { c.HLine(20, 26, 25 + o, bone); c.Set(27, 24 + o, boneLight); }
                else c.VLine(20, 24 + o, 31 + o, bone);
            }
            else
            {
                // Curved rib cage: each rib tapers inward at the ends.
                for (int r = 0; r < 3; r++)
                {
                    int ry = 25 + o + r * 2;
                    var rc = r % 2 == 0 ? bone : boneDark;
                    c.HLine(11, 20, ry, rc);
                    c.Set(10, ry + 1, rc); c.Set(21, ry + 1, rc); // rib tips curl down
                }
                c.VLine(15, 25 + o, 29 + o, boneDark); c.VLine(16, 25 + o, 29 + o, boneDark); // sternum
                // Left arm (upper + fore) with an elbow pip and a knuckle.
                c.VLine(8, 24 + o, 31 + o, bone); c.Set(9, 27 + o, boneLight); c.Set(8, 31 + o, boneLight);
                if (attack) { c.VLine(22, 18 + o, 25 + o, bone); c.Set(23, 21 + o, boneLight); c.Set(22, 18 + o, boneLight); }
                else { c.VLine(22, 24 + o, 31 + o, bone); c.Set(23, 27 + o, boneLight); c.Set(22, 31 + o, boneLight); }
            }

            // ---- Skull (rows 6..21) ----
            int hx = 8, hy = 6 + o;
            c.HLine(hx + 3, hx + 12, hy, bone);
            c.Rect(hx + 1, hy + 1, 14, 2, bone);
            c.Rect(hx, hy + 3, 16, 8, bone);
            c.HLine(hx + 1, hx + 14, hy + 2, boneLight);
            c.Rect(hx + 2, hy + 11, 12, 4, bone);   // jaw
            c.HLine(hx + 4, hx + 11, hy + 15, boneDark);
            // Teeth.
            for (int t = hx + 4; t <= hx + 11; t += 2) c.VLine(t, hy + 12, hy + 14, boneDark);
            c.VLine(hx + 15, hy + 4, hy + 9, boneDark); // right shade

            if (v.back)
            {
                c.HLine(hx + 1, hx + (v.diag ? 11 : 13), hy + 8, boneDark);
                c.Rect(hx + 3, hy + 4, 10, 4, boneLight); // cranium seam sheen
                if (v.diag) c.Set(hx + 14, hy + 6, socket);
            }
            else if (v.side)
            {
                c.Rect(hx + 8, hy + 5, 4, 4, socket);
                c.Set(hx + 14, hy + 9, socket);
                c.Rect(hx + 9, hy + 12, 2, 2, socket);
                if (attack || hurt) c.Rect(hx + 8, hy + 6, 2, 2, Red);
            }
            else if (v.diag)
            {
                c.Rect(hx + 5, hy + 5, 3, 4, socket);
                c.Rect(hx + 11, hy + 5, 2, 4, socket);
                c.Set(hx + 9, hy + 9, socket);        // nose
                c.Set(hx + 10, hy + 9, socket);
                if (attack || hurt) { c.Rect(hx + 5, hy + 6, 2, 2, Red); c.Rect(hx + 11, hy + 6, 2, 2, Red); }
            }
            else
            {
                c.Rect(hx + 2, hy + 5, 4, 4, socket);
                c.Rect(hx + 10, hy + 5, 4, 4, socket);
                c.Set(hx + 2, hy + 5, Lit(socket, 1.6f)); c.Set(hx + 10, hy + 5, Lit(socket, 1.6f)); // socket rim light
                c.Rect(hx + 7, hy + 9, 2, 2, socket); // nose
                c.Set(hx + 1, hy + 9, boneDark); c.Set(hx + 14, hy + 9, boneDark); // cheekbones
                c.HLine(hx + 5, hx + 10, hy + 3, boneLight);                        // brow ridge sheen
                if (attack || hurt)
                {
                    c.Rect(hx + 2, hy + 6, 3, 3, Red); c.Set(hx + 3, hy + 6, RedLight);
                    c.Rect(hx + 10, hy + 6, 3, 3, Red); c.Set(hx + 11, hy + 6, RedLight);
                }
                else { c.Set(hx + 3, hy + 6, RedLight); c.Set(hx + 11, hy + 6, RedLight); } // faint glow
            }
        }

        // ---------- Held weapons ----------

        static PixelCanvas DrawWeaponHd(string kind, int tier)
        {
            var c = Hd(32, 36);
            var handle = PixelCanvas.Hex("#6b4226");
            var handleDark = PixelCanvas.Hex("#4a2c17");
            if (kind == "sword")
            {
                Color32 blade, edge, edgeLight, guard;
                switch (tier)
                {
                    case 0: blade = Wood; edge = WoodDark; edgeLight = WoodLight; guard = BarkDark; break;               // 나무 검
                    case 1: blade = Steel; edge = SteelDark; edgeLight = White; guard = Gold; break;                     // 철검
                    case 2: blade = PixelCanvas.Hex("#f0e8d4"); edge = PixelCanvas.Hex("#b8ac90"); edgeLight = White; guard = PixelCanvas.Hex("#8a7a60"); break; // 해골 대검
                    default: blade = PixelCanvas.Hex("#ff9a52"); edge = PixelCanvas.Hex("#c83a2a"); edgeLight = Yellow; guard = Gold; break; // 용골 대검
                }
                int top = tier >= 2 ? 0 : 4;      // greatswords are longer
                int width = tier >= 2 ? 6 : 4;
                int x = 16 - width / 2;
                c.Rect(x, top + 2, width, 22 - top, blade);
                // Pointed tip.
                c.Rect(x + 1, top, width - 2, 2, blade);
                c.Set(x + width / 2 - 1, top - 1, blade); c.Set(x + width / 2, top - 1, blade);
                c.VLine(x, top + 3, 23, edgeLight);          // lit left edge
                c.VLine(x + width - 1, top + 3, 23, edge);   // shaded right edge
                if (tier == 2) { c.Rect(x + width, 8, 1, 2, blade); c.Rect(x + width, 14, 1, 2, blade); } // bone notches
                if (tier == 3)
                {
                    c.VLine(x + 1, top + 3, 22, Yellow);
                    for (int y = top + 6; y < 22; y += 5) c.Set(x + 2, y, White); // runes
                }
                // Fuller groove.
                if (tier != 3) c.VLine(x + width / 2, top + 3, 22, edge);
                // Guard.
                c.Rect(x - 4, 24, width + 8, 2, guard);
                c.HLine(x - 4, x + width + 3, 24, Lit(guard, 1.2f));
                if (tier == 3) { c.Set(15, 24, Red); c.Set(16, 24, Red); }
                // Grip + pommel.
                c.Rect(14, 26, 4, 6, handle); c.VLine(17, 26, 31, handleDark);
                for (int y = 27; y < 32; y += 2) c.HLine(14, 17, y, handleDark); // wrap
                c.Rect(14, 32, 4, 2, guard);
            }
            else // staff
            {
                Color32 shaft = tier == 2 ? PixelCanvas.Hex("#c8d0e0") : tier == 3 ? Gold : Bark;
                Color32 shaftDark = Lit(shaft, 0.7f);
                Color32 shaftLight = Lit(shaft, 1.2f);
                c.Rect(14, 10, 4, 24, shaft);
                c.VLine(14, 11, 33, shaftLight);
                c.VLine(17, 11, 33, shaftDark);
                for (int y = 14; y < 32; y += 4) c.HLine(14, 17, y, shaftDark); // grain rings
                switch (tier)
                {
                    case 0: // 참나무 지팡이: gnarled wooden head with a leaf
                        c.Rect(11, 4, 8, 6, Bark);
                        c.Set(19, 2, Bark); c.Set(11, 2, Bark); c.Rect(11, 3, 8, 1, BarkDark);
                        c.Rect(20, 5, 2, 2, PixelCanvas.Hex("#6fbf4a")); c.Set(22, 4, PixelCanvas.Hex("#8fdc6a"));
                        c.Set(12, 5, Lit(Bark, 1.2f));
                        break;
                    case 1: // 수정 지팡이: blue crystal
                        c.Rect(12, 4, 2, 2, Gold); c.Rect(18, 4, 2, 2, Gold);
                        c.Rect(12, 1, 8, 7, MagicCore);
                        c.Rect(14, 0, 4, 1, PixelCanvas.Hex("#bff6ff"));
                        c.Rect(13, 2, 3, 3, White);       // facet highlight
                        c.VLine(18, 2, 6, Lit(MagicCore, 0.7f));
                        break;
                    case 2: // 달빛 지팡이: crescent moon
                        c.Rect(12, 4, 2, 2, Gold); c.Rect(18, 4, 2, 2, Gold);
                        c.Circle(16f, 4f, 5.2f, PixelCanvas.Hex("#fff2b0"));
                        c.Circle(18.4f, 3f, 4f, PixelCanvas.Clear);
                        c.Rect(12, 3, 2, 2, White);
                        break;
                    default: // 별의 지팡이: golden star
                        c.Rect(12, 4, 2, 2, Magic); c.Rect(18, 4, 2, 2, Magic);
                        c.VLine(15, 0, 8, Yellow); c.VLine(16, 0, 8, Yellow);
                        c.HLine(10, 21, 4, Yellow);
                        c.Rect(13, 2, 6, 4, Yellow);
                        c.Rect(14, 3, 3, 2, White);
                        c.Set(12, 1, White); c.Set(20, 1, White); c.Set(12, 7, White); c.Set(20, 7, White);
                        break;
                }
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        // ---------- Held tools ----------

        static PixelCanvas DrawToolHd(string kind)
        {
            if (kind == "crate") return DrawCrateHd();
            var c = Hd(32, 32);
            var handle = PixelCanvas.Hex("#6b4226");
            var handleDark = PixelCanvas.Hex("#4a2c17");
            var handleLight = PixelCanvas.Hex("#8a5c38");
            var metalReel = PixelCanvas.Hex("#8f99a3");
            switch (kind)
            {
                case "sword":
                    c.Rect(14, 2, 4, 20, Steel);
                    c.VLine(14, 3, 21, White); c.VLine(17, 3, 21, SteelDark);
                    c.Rect(15, 0, 2, 2, Steel);
                    c.VLine(15, 3, 20, SteelDark); // fuller
                    c.Rect(10, 22, 12, 2, Gold);   // guard
                    c.HLine(10, 21, 22, Lit(Gold, 1.2f));
                    c.Rect(14, 24, 4, 6, handle); for (int y = 25; y < 30; y += 2) c.HLine(14, 17, y, handleDark);
                    c.Rect(14, 30, 4, 2, Gold);
                    break;
                case "axe":
                    c.Rect(14, 6, 3, 24, handle); c.VLine(14, 7, 29, handleLight); c.VLine(16, 7, 29, handleDark);
                    c.Rect(6, 4, 10, 10, Steel);
                    c.VLine(6, 4, 13, SteelDark);
                    c.Rect(4, 6, 3, 6, Steel);      // blade edge
                    c.HLine(4, 15, 4, White);
                    c.Set(8, 6, White); c.Set(9, 7, White); // shine
                    break;
                case "pickaxe":
                    c.Rect(14, 6, 3, 24, handle); c.VLine(14, 7, 29, handleLight); c.VLine(16, 7, 29, handleDark);
                    c.HLine(4, 27, 6, Steel); c.HLine(5, 26, 5, Steel);
                    c.HLine(6, 25, 7, SteelDark);
                    c.Set(4, 8, SteelDark); c.Set(27, 8, SteelDark);
                    c.HLine(10, 20, 5, White);
                    break;
                case "can":
                {
                    var body = PixelCanvas.Hex("#4a90d9");
                    var bodyDk = PixelCanvas.Hex("#2f6bb0");
                    var bodyLt = PixelCanvas.Hex("#8cc4ff");
                    var metal = SteelDark;
                    // Tank body (rounded).
                    c.Rect(9, 13, 12, 13, body);
                    c.Rect(8, 15, 1, 9, body); c.Rect(21, 15, 1, 9, body);
                    c.HLine(9, 20, 13, bodyLt); c.VLine(9, 14, 24, bodyLt); // top+left light
                    c.VLine(20, 15, 24, bodyDk); c.HLine(9, 20, 25, bodyDk); // right+bottom shade
                    c.Set(11, 16, White);                                     // sheen
                    // Curved spout to the upper-right ending in a rose.
                    c.Line(21, 18, 27, 11, body); c.Line(21, 17, 27, 10, bodyLt); c.Line(22, 19, 28, 12, bodyDk);
                    c.Rect(26, 8, 5, 3, metal);        // rose head
                    c.HLine(26, 30, 8, Lit(metal, 1.3f));
                    c.Set(27, 9, White); c.Set(29, 9, White); // spray holes glint
                    // Arched carry handle over the top.
                    c.Line(11, 12, 14, 8, metal); c.Line(14, 8, 18, 8, metal); c.Line(18, 8, 20, 12, metal);
                    c.HLine(14, 17, 7, Lit(metal, 1.3f));
                    break;
                }
                case "rod":
                {
                    var pole = PixelCanvas.Hex("#7a4a24");
                    var poleLt = PixelCanvas.Hex("#a56d3a");
                    var poleDk = BarkDark;
                    var line = PixelCanvas.Hex("#e8e8e8");
                    // Thicker tapering pole from grip (bottom-left) to tip (top-right).
                    c.Line(11, 30, 25, 3, pole); c.Line(12, 30, 26, 3, poleLt); c.Line(10, 30, 24, 4, poleDk);
                    c.Set(25, 2, pole); c.Set(26, 2, pole);
                    // Cork grip at the base.
                    c.Rect(9, 27, 4, 5, PixelCanvas.Hex("#c79a5a")); c.VLine(9, 28, 31, PixelCanvas.Hex("#a87c40"));
                    // Reel (side disc) just above the grip.
                    c.Circle(15f, 24f, 3.2f, metalReel); c.Circle(15f, 24f, 1.6f, PixelCanvas.Hex("#c0c6cf"));
                    c.Set(14, 23, White);
                    c.Rect(15, 26, 1, 3, SteelDark);   // reel foot to the pole
                    // Line from the tip down to a float, with a small hook.
                    c.VLine(25, 3, 9, line); c.Line(25, 9, 22, 15, line);
                    c.Rect(21, 15, 2, 2, Red); c.Set(21, 15, RedLight); // float
                    c.Set(22, 18, SteelDark);                           // hook
                    break;
                }
                case "hammer":
                    c.Rect(14, 10, 4, 20, handle); for (int y = 12; y < 28; y += 3) c.HLine(14, 17, y, handleDark);
                    c.Rect(8, 4, 16, 8, SteelDark);
                    c.HLine(8, 23, 4, Steel); c.HLine(8, 23, 5, Steel);
                    c.Rect(9, 6, 4, 4, Lit(SteelDark, 1.3f)); // face highlight
                    break;
                case "staff":
                    c.Rect(14, 10, 4, 22, handle); c.VLine(14, 11, 31, handleLight); c.VLine(17, 11, 31, BarkDark);
                    c.Rect(12, 8, 2, 2, Gold); c.Rect(18, 8, 2, 2, Gold);
                    c.Rect(11, 5, 2, 2, Gold); c.Rect(19, 5, 2, 2, Gold);
                    c.Circle(15.5f, 4.5f, 4.5f, Magic);
                    c.Circle(15.5f, 4.5f, 2.8f, MagicCore);
                    c.Rect(13, 2, 2, 2, White);
                    break;
                default:
                    return null;
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        /// <summary>Carried supply crate (tool_crate) at density 2 — a wooden box of carrots.</summary>
        static PixelCanvas DrawCrateHd()
        {
            var c = Hd(32, 34);
            var wood = Wood; var wl = WoodLight; var wd = WoodDark;
            for (int i = 0; i < 4; i++)
            {
                int x = 6 + i * 6;
                c.Rect(x, 2, 2, 6, Leaf); c.Set(x - 1, 4, LeafLight);
                c.Rect(x - 2, 8, 5, 5, Carrot); c.Set(x - 2, 8, CarrotLight);
            }
            c.Rect(2, 12, 28, 20, wood);
            c.HLine(2, 29, 12, wl);
            c.HLine(2, 29, 22, wd);
            c.HLine(2, 29, 31, wd);
            c.VLine(2, 12, 31, wd); c.VLine(29, 12, 31, wd);
            c.Line(4, 14, 27, 20, wd);
            c.Line(4, 24, 27, 30, wd);
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        // ---------- Damage digits, HP bar, shadow ----------

        static readonly string[] DigitShapesHd =
        {
            // 5x7 glyphs upscaled to crisp 6x9 forms.
            "011110|110011|110011|110011|110011|110011|011110",
            "001100|011100|001100|001100|001100|001100|011110",
            "011110|110011|000011|000110|011000|110000|111111",
            "011110|110011|000011|001110|000011|110011|011110",
            "000110|001110|011110|110110|111111|000110|000110",
            "111111|110000|111110|000011|000011|110011|011110",
            "001110|011000|110000|111110|110011|110011|011110",
            "111111|000011|000110|001100|011000|011000|011000",
            "011110|110011|110011|011110|110011|110011|011110",
            "011110|110011|110011|011111|000011|000110|011100",
        };

        static PixelCanvas DrawDigitHd(int digit)
        {
            var c = Hd(8, 11);
            var rows = DigitShapesHd[Mathf.Clamp(digit, 0, 9)].Split('|');
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '1') c.Set(x + 1, y + 1, White);
            c.Outline(Outline);
            return c;
        }

        /// <summary>Monster health bar at density 2. "bg" = dark frame (32px wide), "fill" = white strip, left pivot.</summary>
        static PixelCanvas DrawHpBarHd(string part)
        {
            var frame = PixelCanvas.Hex("#20140f");
            if (part == "bg")
            {
                var c = Hd(32, 8);
                // Rounded-end dark frame.
                c.Rect(1, 0, 30, 8, frame);
                c.Rect(0, 1, 32, 6, frame);
                c.Rect(0, 2, 1, 4, PixelCanvas.Clear); c.Rect(31, 2, 1, 4, PixelCanvas.Clear); // clip square corners
                // Empty channel with an inner bevel (dark bottom, faint top light).
                var empty = PixelCanvas.Hex("#3a2020");
                c.Rect(2, 2, 28, 4, empty);
                c.HLine(2, 29, 2, PixelCanvas.Hex("#4c2c2c"));   // bevel top light
                c.HLine(2, 29, 5, PixelCanvas.Hex("#281414"));   // bevel bottom shade
                return c;
            }
            // Fill: 28 wide, vertical gradient (light top, mid, shade bottom), left pivot so it scales from the left.
            var fill = Hd(28, 4);
            fill.HLine(0, 27, 0, White);
            fill.HLine(0, 27, 1, PixelCanvas.Hex("#f0f0f0"));
            fill.HLine(0, 27, 2, PixelCanvas.Hex("#dcdcdc"));
            fill.HLine(0, 27, 3, PixelCanvas.Hex("#c4c4c4"));
            return fill.WithPivot(0, 2);
        }

        static PixelCanvas DrawShadowHd()
        {
            var c = Hd(24, 10);
            c.Ellipse(12, 5, 11f, 4.4f, new Color32(20, 40, 20, 70));
            c.PaintEllipse(12, 5, 8f, 3f, new Color32(20, 40, 20, 55));
            return c;
        }
    }
}
