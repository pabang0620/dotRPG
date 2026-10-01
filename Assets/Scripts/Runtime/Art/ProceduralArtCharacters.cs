using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Chibi character generator. Five views cover the eight facings: down, up, side, downside
    /// (3/4 front) and upside (3/4 back); the left-facing ones are the right-facing art flipped by the
    /// renderer. Frames: idle0, idle1, walk0..walk3, attack, hurt. Worn gear shows on the body:
    /// vest / leather / plate tops, trouser colours and robe colours. Also draws the held weapons.
    /// </summary>
    public static partial class ProceduralArt
    {
        /// <summary>Which way a view looks, decoded from its sprite key.</summary>
        readonly struct View
        {
            public readonly bool front, back, side, diag;

            public View(string dir)
            {
                side = dir == "side";
                diag = dir == "downside" || dir == "upside";
                back = dir == "up" || dir == "upside";
                front = !side && !back;
            }
        }

        // ---------- Characters ----------

        /// <summary>
        /// Sprite keys that belong with the characters: held weapons (wpn_*), held tools (tool_*), damage
        /// digits (num_*), monster HP bars (hpbar_*) and the ground shadow, so they can be swapped in one place.
        /// </summary>
        static PixelCanvas DrawCharacterFamily(string key, string[] parts)
        {
            // Routed to the 32px (density 2) art in ProceduralArtCharactersHd.cs. The old density-1
            // versions (DrawWeapon/DrawTool/DrawDigit/DrawHpBar/DrawShadow) stay below for reference.
            return DrawCharacterFamilyHd(key, parts);
        }

        /// <summary>
        /// Draws one frame of a chibi character at 32px (density 2). Right-facing views face right;
        /// the renderer flips them for left. Delegates to the HD art; the old 16px body drawing
        /// (DrawCharacterLegacy) is kept below untouched.
        /// </summary>
        public static PixelCanvas DrawCharacter(CharacterLook look, string dir, string frame)
        {
            // [MONSTER] Dungeon monsters / bosses (bigger canvases for bosses).
            if (look.body == BodyKind.Monster) return DrawMonsterCharacter(look, dir, frame);
            return DrawCharacterHd(look, dir, frame);
        }

        /// <summary>Original 16px character frame (kept for reference; no longer routed).</summary>
        static PixelCanvas DrawCharacterLegacy(CharacterLook look, string dir, string frame)
        {
            var c = new PixelCanvas(16, 20);
            var v = new View(dir);
            if (look.body == BodyKind.Skeleton) DrawSkeletonBody(c, look, v, frame);
            else DrawHumanBody(c, look, v, frame);
            if (look.body == BodyKind.Human && look.hat == HatKind.Wizard)
            {
                // A pointed hat does not fit the 16x20 frame: move the character into a taller
                // canvas (pivot is measured from the bottom, so the feet stay put) and add the cone.
                const int extra = 5;
                var tall = new PixelCanvas(16, 20 + extra);
                for (int y = 0; y < 20; y++)
                    for (int x = 0; x < 16; x++)
                        tall.Pixels[(y + extra) * 16 + x] = c.Pixels[y * 16 + x];
                FrameInfo(frame, out int bob, out _);
                DrawWizardCone(tall, look, v, 3, 2 + bob + extra);
                c = tall;
            }
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        /// <summary>Cone of the mage hat above the brim (hx/hy = head origin in the tall canvas).</summary>
        static void DrawWizardCone(PixelCanvas c, CharacterLook L, View v, int hx, int hy)
        {
            var col = L.hatColor;
            var dark = PixelCanvas.Shade(col, 0.75f);
            var light = PixelCanvas.Shade(col, 1.25f);
            int bend = v.side || v.diag ? -1 : 1; // turned views: the tip droops backwards
            c.Rect(hx + 2, hy - 1, 6, 1, col);
            c.Rect(hx + 2, hy - 2, 5, 1, col);
            c.Rect(hx + 3, hy - 3, 4, 1, col);
            c.Rect(hx + 3 + (bend > 0 ? 1 : 0), hy - 4, 2, 1, col);
            c.Set(hx + 5 + bend * 2, hy - 5, col);
            c.Set(hx + 5 + bend * 3, hy - 5, dark);
            c.VLine(hx + 7, hy - 2, hy - 1, dark);
            c.Set(hx + 3, hy - 2, light);
            if (!v.back) c.Set(hx + (v.diag ? 5 : 4), hy - 2, Yellow); // star
        }

        static void FrameInfo(string frame, out int bob, out int step)
        {
            bob = 0;
            step = 0; // -1 left foot forward, +1 right foot forward
            switch (frame)
            {
                case "idle1": bob = 1; break;
                case "walk0": step = -1; break;
                case "walk1": bob = 1; break;
                case "walk2": step = 1; break;
                case "walk3": bob = 1; break;
                case "attack": step = 1; break;
                case "hurt": bob = 1; break;
            }
        }

        static void DrawHumanBody(PixelCanvas c, CharacterLook L, View v, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            var shoe = PixelCanvas.Hex("#4a3226");
            var pantsDark = PixelCanvas.Shade(L.pants, 0.8f);
            var shirtDark = PixelCanvas.Shade(L.shirt, 0.78f);
            var skinDark = PixelCanvas.Shade(L.skin, 0.85f);
            var hairDark = PixelCanvas.Shade(L.hair, 0.78f);
            var blush = PixelCanvas.Hex("#f29a8a");
            bool attack = frame == "attack";

            // Legs (rows 16-18). Walking lifts one foot.
            if (v.side)
            {
                int back = step == 0 ? 6 : (step < 0 ? 5 : 7);
                int front = step == 0 ? 8 : (step < 0 ? 9 : 7);
                c.Rect(back, 16, 2, 2, pantsDark); c.Rect(back, 18, 2, 1, shoe);
                c.Rect(front, 16, 2, 2, L.pants); c.Rect(front, 18, 3, 1, shoe);
            }
            else if (v.diag)
            {
                // 3/4 view: legs closer together, the near (right) foot pointing to the side.
                int lLift = step < 0 ? 1 : 0, rLift = step > 0 ? 1 : 0;
                c.Rect(5, 16, 2, 2 - lLift, pantsDark); c.Rect(5, 18 - lLift, 2, 1, shoe);
                c.Rect(8, 16, 2, 2 - rLift, L.pants); c.Rect(8, 18 - rLift, v.back ? 2 : 3, 1, shoe);
                c.Set(7, 16, pantsDark);
            }
            else
            {
                int lLift = step < 0 ? 1 : 0, rLift = step > 0 ? 1 : 0;
                c.Rect(5, 16, 2, 2 - lLift, L.pants); c.Rect(5, 18 - lLift, 2, 1, shoe);
                c.Rect(9, 16, 2, 2 - rLift, L.pants); c.Rect(9, 18 - rLift, 2, 1, shoe);
                c.Rect(7, 16, 2, 1, pantsDark);
            }

            int o = bob; // body/head vertical offset
            // Body (rows 12-15).
            if (v.side)
            {
                c.Rect(5, 12 + o, 6, 4, L.shirt);
                c.HLine(5, 10, 15 + o, L.pants);
                c.VLine(5, 12 + o, 14 + o, shirtDark);
                // Arm swings with steps.
                int armX = attack ? 10 : 7 + step;
                if (attack)
                {
                    c.Rect(10, 12 + o, 3, 2, L.shirt);
                    c.Rect(13, 12 + o, 1, 2, L.skin);
                }
                else
                {
                    c.Rect(armX, 12 + o, 2, 2, shirtDark);
                    c.Rect(armX, 14 + o, 2, 1, L.skin);
                }
            }
            else if (v.diag)
            {
                c.Rect(4, 12 + o, 7, 4, L.shirt);
                c.HLine(4, 10, 15 + o, L.pants);
                c.VLine(4, 12 + o, 14 + o, shirtDark);
                c.HLine(5, 10, 14 + o, shirtDark);
                if (!v.back) { c.Set(8, 12 + o, shirtDark); c.Set(9, 12 + o, shirtDark); }
                // Far arm peeks out behind the body; the near arm swings.
                c.Set(3, 13 + o, shirtDark);
                if (attack && !v.back) { c.Rect(11, 12 + o, 2, 2, L.shirt); c.Set(13, 13 + o, L.skin); }
                else if (attack) { c.Rect(11, 10 + o, 1, 3, L.shirt); c.Set(11, 9 + o, L.skin); }
                else
                {
                    c.Rect(11, 12 + o, 1, 2, L.shirt); c.Set(11, 14 + o, L.skin);
                    if (step != 0) c.Set(step > 0 ? 11 : 3, 15 + o, L.skin);
                }
            }
            else
            {
                c.Rect(4, 12 + o, 8, 4, L.shirt);
                c.HLine(4, 11, 15 + o, L.pants);
                c.HLine(4, 11, 14 + o, shirtDark);
                if (!v.back) { c.Set(7, 12 + o, shirtDark); c.Set(8, 12 + o, shirtDark); }
                // Arms.
                c.Rect(3, 12 + o, 1, 2, L.shirt); c.Set(3, 14 + o, L.skin);
                if (attack && !v.back) { c.Rect(12, 12 + o, 1, 3, L.shirt); c.Rect(12, 15 + o, 1, 1, L.skin); }
                else if (attack) { c.Rect(12, 10 + o, 1, 3, L.shirt); c.Set(12, 9 + o, L.skin); }
                else { c.Rect(12, 12 + o, 1, 2, L.shirt); c.Set(12, 14 + o, L.skin); }
                // Walk arm swing (one shade darker arm moves).
                if (step != 0 && !attack) { c.Set(step < 0 ? 3 : 12, 15 + o, L.skin); }
            }

            if (L.robe) DrawRobe(c, L, v, step, bob, shirtDark);
            if (L.armor != ArmorStyle.None) DrawArmor(c, L, v, bob);

            // Head (rows 2-11), 10px wide rounded.
            int hx = 3; // head left edge
            int hy = 2 + o;
            c.HLine(hx + 2, hx + 7, hy, L.skin);
            c.HLine(hx + 1, hx + 8, hy + 1, L.skin);
            c.Rect(hx, hy + 2, 10, 6, L.skin);
            c.HLine(hx + 1, hx + 8, hy + 8, L.skin);
            c.HLine(hx + 2, hx + 7, hy + 9, skinDark);

            // Face.
            if (!v.back)
            {
                if (v.side)
                {
                    c.Rect(hx + 7, hy + 5, 1, 2, Eye);
                    c.Set(hx + 9, hy + 6, skinDark);
                    c.Set(hx + 6, hy + 7, blush);
                }
                else if (v.diag)
                {
                    // Turned towards the right: both eyes shifted, the far one near the middle.
                    c.Rect(hx + 4, hy + 5, 1, 2, Eye);
                    c.Rect(hx + 8, hy + 5, 1, 2, Eye);
                    c.Set(hx + 3, hy + 7, blush);
                    c.Set(hx + 9, hy + 7, blush);
                    if (frame == "hurt") { c.Set(hx + 4, hy + 5, L.skin); c.Set(hx + 8, hy + 5, L.skin); }
                }
                else
                {
                    c.Rect(hx + 2, hy + 5, 1, 2, Eye);
                    c.Rect(hx + 7, hy + 5, 1, 2, Eye);
                    c.Set(hx + 1, hy + 7, blush);
                    c.Set(hx + 8, hy + 7, blush);
                    if (frame == "hurt") { c.Set(hx + 2, hy + 5, L.skin); c.Set(hx + 7, hy + 5, L.skin); }
                }
            }
            else if (v.diag)
            {
                c.Set(hx + 8, hy + 6, skinDark); // ear, seen from behind
            }

            DrawHair(c, L, v, hx, hy, hairDark);
            DrawHat(c, L, v, hx, hy);
        }

        /// <summary>Long mage robe: covers the legs, sways with the walk cycle, gold belt and hem.</summary>
        static void DrawRobe(PixelCanvas c, CharacterLook L, View v, int step, int bob, Color32 shirtDark)
        {
            var shoe = PixelCanvas.Hex("#3a2a40");
            var robe = L.robeColor.a > 0 ? L.robeColor : L.shirt;
            var robeDark = L.robeColor.a > 0 ? PixelCanvas.Shade(robe, 0.78f) : shirtDark;
            int o = bob;
            // Clear the legs first so the robe silhouette is clean.
            c.Rect(3, 16, 11, 3, PixelCanvas.Clear);
            if (v.side)
            {
                c.Rect(5, 15, 6, 2, robe);
                c.Rect(4 + (step < 0 ? 1 : 0), 17, 7, 1, robe);
                c.HLine(4 + (step < 0 ? 1 : 0), 10 + (step > 0 ? 1 : 0), 18, Gold);
                c.VLine(5, 15, 17, robeDark);
                if (step != 0) c.Set(step > 0 ? 11 : 4, 18, shoe);
            }
            else
            {
                c.Rect(4, 15, 8, 2, robe);
                c.Rect(3, 17, 10, 1, robe);
                c.HLine(3, 12, 18, Gold);
                c.VLine(4, 15, 17, robeDark);
                c.VLine(11, 15, 17, robeDark);
                if (!v.back) c.VLine(v.diag ? 9 : 8, 15, 17, robeDark); // front opening
                // Feet peek out under the hem while walking.
                if (step < 0) c.Rect(5, 18, 2, 1, shoe);
                else if (step > 0) c.Rect(9, 18, 2, 1, shoe);
                // Belt.
                c.HLine(4, 11, 14 + o, Gold);
            }
        }

        /// <summary>
        /// Worn top over the torso: a cloth vest (shirt showing in the middle), leather armour with a
        /// chest strap and belt, or an iron breastplate with shoulder plates.
        /// </summary>
        static void DrawArmor(PixelCanvas c, CharacterLook L, View v, int bob)
        {
            int o = bob;
            var col = L.armorColor;
            var dark = PixelCanvas.Shade(col, 0.72f);
            var light = PixelCanvas.Shade(col, 1.2f);
            int x0 = v.side ? 5 : 4, x1 = v.side || v.diag ? 10 : 11;
            switch (L.armor)
            {
                case ArmorStyle.Vest:
                    for (int y = 12; y <= 14; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            bool gap = v.front && !v.side && (v.diag ? x == 8 || x == 9 : x == 7 || x == 8);
                            if (!gap) c.Set(x, y + o, col);
                        }
                    c.VLine(x0, 12 + o, 14 + o, dark);
                    if (v.front && !v.side) { c.Set(v.diag ? 7 : 6, 13 + o, dark); c.Set(v.diag ? 10 : 9, 13 + o, dark); }
                    break;
                case ArmorStyle.Leather:
                    c.Rect(x0, 12 + o, x1 - x0 + 1, 3, col);
                    c.VLine(x0, 12 + o, 14 + o, dark);
                    if (!v.side) c.Line(x0 + 1, 12 + o, x1 - 1, 14 + o, dark); // chest strap
                    c.HLine(x0, x1, 15 + o, PixelCanvas.Hex("#4a2e1a"));        // belt
                    if (!v.back) c.Set(v.side ? x1 - 1 : (x0 + x1 + 1) / 2, 15 + o, Gold);
                    c.Set(x1, 12 + o, light);
                    break;
                case ArmorStyle.Plate:
                    c.Rect(x0, 12 + o, x1 - x0 + 1, 3, col);
                    c.HLine(x0, x1, 14 + o, dark);
                    c.VLine(x0, 12 + o, 14 + o, dark);
                    if (!v.back) { c.Set(x0 + 2, 12 + o, light); c.Set(x0 + 3, 12 + o, light); c.Set(x0 + 2, 13 + o, light); }
                    // Shoulder plates.
                    if (v.side)
                    {
                        c.Rect(6, 11 + o, 4, 2, col);
                        c.HLine(6, 9, 12 + o, dark);
                        c.Set(7, 11 + o, light);
                    }
                    else
                    {
                        int l = v.diag ? 3 : 2, r = v.diag ? 10 : 11;
                        c.Rect(l, 11 + o, 3, 2, col); c.Rect(r, 11 + o, 3, 2, col);
                        c.HLine(l, l + 2, 12 + o, dark); c.HLine(r, r + 2, 12 + o, dark);
                        c.Set(l + 1, 11 + o, light); c.Set(r + 1, 11 + o, light);
                    }
                    break;
            }
        }

        static void DrawHair(PixelCanvas c, CharacterLook L, View v, int hx, int hy, Color32 hairDark)
        {
            var h = L.hair;
            bool up = v.back && !v.diag, upSide = v.back && v.diag, downSide = v.front && v.diag;
            switch (L.hairStyle)
            {
                case HairStyle.Bald:
                    if (up) { c.Rect(hx, hy + 4, 10, 4, h); }
                    else if (upSide) { c.Rect(hx, hy + 4, 8, 4, h); }
                    else if (v.side) { c.Rect(hx, hy + 3, 3, 4, h); }
                    else if (downSide)
                    {
                        c.Rect(hx, hy + 3, 2, 4, h);
                        // Beard for elders, turned with the face.
                        c.Rect(hx + 3, hy + 7, 6, 3, h);
                        c.HLine(hx + 4, hx + 7, hy + 10, h);
                        c.Set(hx + 5, hy + 7, PixelCanvas.Shade(L.skin, 0.8f));
                        c.Set(hx + 6, hy + 7, PixelCanvas.Shade(L.skin, 0.8f));
                    }
                    else
                    {
                        c.Rect(hx, hy + 3, 1, 4, h); c.Rect(hx + 9, hy + 3, 1, 4, h);
                        c.Rect(hx + 2, hy + 7, 6, 3, h);
                        c.HLine(hx + 3, hx + 6, hy + 10, h);
                        c.Set(hx + 4, hy + 7, PixelCanvas.Shade(L.skin, 0.8f));
                        c.Set(hx + 5, hy + 7, PixelCanvas.Shade(L.skin, 0.8f));
                    }
                    return;
            }

            // Common cap of hair over the top of the head.
            c.HLine(hx + 2, hx + 7, hy, h);
            c.HLine(hx + 1, hx + 8, hy + 1, h);
            c.Rect(hx, hy + 2, 10, 2, h);
            c.HLine(hx + 3, hx + 6, hy, PixelCanvas.Shade(h, 1.15f));

            if (up)
            {
                c.Rect(hx, hy + 2, 10, 7, h);
                c.HLine(hx + 1, hx + 8, hy + 8, hairDark);
            }
            else if (upSide)
            {
                // Back of the head turned right: the ear and cheek show on the right.
                c.Rect(hx, hy + 2, 8, 7, h);
                c.HLine(hx + 1, hx + 7, hy + 8, hairDark);
                c.VLine(hx + 7, hy + 4, hy + 8, hairDark);
            }
            else if (v.side)
            {
                c.Rect(hx, hy + 2, 5, 6, h);
                c.Rect(hx + 5, hy + 2, 5, 2, h);
                c.Set(hx + 8, hy + 4, h); c.Set(hx + 9, hy + 4, h);
                c.VLine(hx + 4, hy + 4, hy + 7, hairDark);
            }
            else if (downSide)
            {
                // Fringe swept to the right, more hair on the far (left) side of the head.
                c.Rect(hx, hy + 2, 3, 5, h);
                c.Set(hx + 3, hy + 4, h); c.Set(hx + 6, hy + 4, h); c.Set(hx + 7, hy + 4, h);
                c.VLine(hx + 9, hy + 4, hy + 5, h);
                c.HLine(hx + 2, hx + 8, hy + 3, hairDark);
                c.VLine(hx + 2, hy + 4, hy + 6, hairDark);
            }
            else
            {
                // Fringe.
                c.Set(hx, hy + 4, h); c.Set(hx + 1, hy + 4, h); c.Set(hx + 4, hy + 4, h);
                c.Set(hx + 5, hy + 4, h); c.Set(hx + 8, hy + 4, h); c.Set(hx + 9, hy + 4, h);
                c.VLine(hx, hy + 5, hy + 6, h);
                c.VLine(hx + 9, hy + 5, hy + 6, h);
                c.HLine(hx + 1, hx + 8, hy + 3, hairDark);
            }

            switch (L.hairStyle)
            {
                case HairStyle.Spiky:
                    c.Set(hx + 1, hy - 1, h); c.Set(hx + 4, hy - 1, h); c.Set(hx + 7, hy - 1, h);
                    c.Set(hx + 5, hy - 2, h);
                    if (v.front && !v.side && !v.diag) { c.Set(hx + 2, hy + 5, h); c.Set(hx + 7, hy + 5, h); }
                    if (downSide) c.Set(hx + 3, hy + 5, h);
                    break;
                case HairStyle.Long:
                    if (v.side) c.Rect(hx - 1, hy + 3, 3, 8, h);
                    else if (v.diag) { c.Rect(hx - 1, hy + 3, 3, 8, h); c.Rect(hx + 9, hy + 4, 1, v.back ? 4 : 6, h); }
                    else { c.Rect(hx - 1, hy + 4, 2, 7, h); c.Rect(hx + 9, hy + 4, 2, 7, h); }
                    if (v.back) c.Rect(hx, hy + 8, v.diag ? 8 : 10, 3, h);
                    break;
                case HairStyle.Bun:
                    c.Ellipse(hx + (v.diag ? 4 : 5), hy - 1, 2.5f, 2f, h);
                    if (v.side || v.diag) c.Ellipse(hx + 1, hy + 1, 2.2f, 2f, h);
                    break;
                case HairStyle.Curly:
                    c.Set(hx - 1, hy + 2, h); c.Set(hx + 10, hy + 2, h);
                    c.Set(hx + 2, hy - 1, h); c.Set(hx + 5, hy - 1, h); c.Set(hx + 8, hy - 1, h);
                    if (!v.side && !v.diag) { c.VLine(hx - 1, hy + 3, hy + 6, h); c.VLine(hx + 10, hy + 3, hy + 6, h); }
                    else c.VLine(hx - 1, hy + 3, hy + 7, h);
                    break;
            }
        }

        static void DrawHat(PixelCanvas c, CharacterLook L, View v, int hx, int hy)
        {
            var col = L.hatColor;
            var dark = PixelCanvas.Shade(col, 0.78f);
            switch (L.hat)
            {
                case HatKind.Straw:
                    c.Rect(hx - 2, hy + 2, 14, 2, col);
                    c.HLine(hx - 2, hx + 11, hy + 3, dark);
                    c.Rect(hx + 1, hy - 1, 8, 3, col);
                    c.HLine(hx + 1, hx + 8, hy + 1, PixelCanvas.Hex("#c4553d"));
                    break;
                case HatKind.Cap:
                    c.Rect(hx, hy - 1, 10, 4, col);
                    if (v.side) c.Rect(hx + 8, hy + 2, 4, 1, dark);
                    else if (v.front && v.diag) c.Rect(hx + 5, hy + 2, 6, 1, dark);
                    else if (v.front) c.HLine(hx + 1, hx + 8, hy + 3, dark);
                    break;
                case HatKind.Bandana:
                    c.Rect(hx, hy + 1, 10, 2, col);
                    c.HLine(hx, hx + 9, hy + 2, dark);
                    if (!v.front || v.diag) { c.Set(hx - 1, hy + 3, col); c.Set(hx - 1, hy + 4, dark); }
                    break;
                case HatKind.Wizard:
                    // Wide brim + gold band; the cone above is added by DrawWizardCone.
                    c.Rect(hx + 1, hy - 1, 8, 3, col);
                    c.HLine(hx + 1, hx + 8, hy + 1, Gold);
                    c.Rect(hx - 2, hy + 2, 14, 2, col);
                    c.HLine(hx - 2, hx + 11, hy + 3, dark);
                    c.Set(hx - 2, hy + 2, dark); c.Set(hx + 11, hy + 2, dark);
                    break;
            }
        }

        static void DrawSkeletonBody(PixelCanvas c, CharacterLook L, View v, string frame)
        {
            FrameInfo(frame, out int bob, out int step);
            var bone = L.skin;
            var boneDark = PixelCanvas.Shade(bone, 0.78f);
            var socket = PixelCanvas.Hex("#2b1d16");
            bool attack = frame == "attack";
            int o = bob;

            // Legs.
            if (v.side)
            {
                int back = step == 0 ? 7 : (step < 0 ? 6 : 8);
                int front = step == 0 ? 8 : (step < 0 ? 9 : 7);
                c.VLine(back, 16, 18, boneDark);
                c.VLine(front, 16, 18, bone); c.Set(front + 1, 18, bone);
            }
            else if (v.diag)
            {
                int l = step < 0 ? 1 : 0, r = step > 0 ? 1 : 0;
                c.VLine(6, 16, 18 - l, boneDark); c.Set(5, 18 - l, boneDark);
                c.VLine(8, 16, 18 - r, bone); c.Set(9, 18 - r, bone);
            }
            else
            {
                c.VLine(6, 16, 18 - (step < 0 ? 1 : 0), bone); c.Set(5, 18 - (step < 0 ? 1 : 0), bone);
                c.VLine(9, 16, 18 - (step > 0 ? 1 : 0), bone); c.Set(10, 18 - (step > 0 ? 1 : 0), bone);
            }
            // Pelvis + spine + ribs.
            c.HLine(6, 9, 15 + o, bone);
            c.VLine(7, 11 + o, 15 + o, bone);
            c.VLine(8, 11 + o, 15 + o, boneDark);
            if (v.side)
            {
                c.HLine(6, 9, 12 + o, bone);
                c.HLine(6, 9, 14 + o, boneDark);
                int armX = attack ? 10 : 8 + step;
                if (attack) c.HLine(9, 12, 12 + o, bone); else c.VLine(armX, 12 + o, 15 + o, bone);
            }
            else if (v.diag)
            {
                c.HLine(5, 9, 12 + o, bone);
                c.HLine(5, 9, 14 + o, boneDark);
                c.Set(5, 13 + o, boneDark); c.Set(9, 13 + o, bone);
                c.VLine(4, 12 + o, 14 + o, boneDark); // far arm
                if (attack) c.HLine(10, 12, 12 + o, bone); else c.VLine(10, 12 + o, 15 + o, bone);
            }
            else
            {
                c.HLine(5, 10, 12 + o, bone);
                c.HLine(5, 10, 14 + o, boneDark);
                c.Set(5, 13 + o, bone); c.Set(10, 13 + o, bone);
                // Arms.
                c.VLine(4, 12 + o, 15 + o, bone);
                if (attack) c.VLine(11, 9 + o, 12 + o, bone); else c.VLine(11, 12 + o, 15 + o, bone);
            }

            // Skull (rows 2-10).
            int hx = 4, hy = 3 + o;
            c.HLine(hx + 2, hx + 5, hy, bone);
            c.Rect(hx + 1, hy + 1, 6, 1, bone);
            c.Rect(hx, hy + 2, 8, 4, bone);
            c.Rect(hx + 1, hy + 6, 6, 2, bone);
            c.HLine(hx + 2, hx + 5, hy + 8, boneDark);
            if (v.back)
            {
                c.HLine(hx + 1, hx + (v.diag ? 5 : 6), hy + 5, boneDark);
                if (v.diag) c.Set(hx + 7, hy + 4, socket); // edge of the eye socket, seen from behind
            }
            else if (v.side)
            {
                c.Rect(hx + 4, hy + 3, 2, 2, socket);
                c.Set(hx + 7, hy + 5, socket);
                c.Set(hx + 5, hy + 7, socket);
            }
            else if (v.diag)
            {
                c.Rect(hx + 3, hy + 3, 2, 2, socket);
                c.Rect(hx + 6, hy + 3, 1, 2, socket);
                c.Set(hx + 5, hy + 5, socket);
                c.Set(hx + 4, hy + 7, socket); c.Set(hx + 6, hy + 7, socket);
                if (attack || frame == "hurt") { c.Set(hx + 4, hy + 4, Red); c.Set(hx + 6, hy + 4, Red); }
            }
            else
            {
                c.Rect(hx + 1, hy + 3, 2, 2, socket);
                c.Rect(hx + 5, hy + 3, 2, 2, socket);
                c.Set(hx + 3, hy + 5, socket); c.Set(hx + 4, hy + 5, socket);
                c.Set(hx + 2, hy + 7, socket); c.Set(hx + 4, hy + 7, socket);
                if (attack || frame == "hurt")
                {
                    c.Set(hx + 2, hy + 4, Red);
                    c.Set(hx + 6, hy + 4, Red);
                }
            }
        }

        // ---------- Held weapons ----------

        /// <summary>
        /// Held weapon for the player, drawn upright (blade/orb up, pivot at the grip end) so it can be
        /// rotated into any pose. kind: sword / staff; tier 0-3 follows the item's icon tier.
        /// </summary>
        static PixelCanvas DrawWeapon(string kind, int tier)
        {
            var c = new PixelCanvas(16, 18);
            var handle = PixelCanvas.Hex("#6b4226");
            if (kind == "sword")
            {
                Color32 blade, edge, guard;
                switch (tier)
                {
                    case 0: blade = WoodLight; edge = Wood; guard = BarkDark; break;                                   // 나무 검
                    case 1: blade = Steel; edge = SteelDark; guard = Gold; break;                                       // 철검
                    case 2: blade = PixelCanvas.Hex("#f0e8d4"); edge = PixelCanvas.Hex("#c8bca0"); guard = PixelCanvas.Hex("#8a7a60"); break; // 해골 대검
                    default: blade = PixelCanvas.Hex("#ff9a52"); edge = PixelCanvas.Hex("#c83a2a"); guard = Gold; break; // 용골 대검
                }
                int top = tier >= 2 ? 0 : 2; // greatswords are longer
                int width = tier >= 2 ? 3 : 2;
                int x = 8 - width / 2 - (width % 2 == 0 ? 0 : 0);
                c.Rect(x, top + 1, width, 11 - top, blade);
                c.VLine(x + width - 1, top + 2, 11, edge);
                c.Set(x, top, blade);
                if (width == 3) c.Set(x + 1, top, blade);
                if (tier == 2) { c.Set(x + width, 4, blade); c.Set(x + width, 7, blade); } // bone notches
                if (tier == 3)
                {
                    c.VLine(x, top + 2, 10, Gold);                 // gilded edge
                    c.Set(x + 1, 5, Yellow); c.Set(x + 1, 8, Yellow);
                }
                c.Rect(x - 2, 12, width + 4, 1, guard);
                if (tier == 3) c.Set(8, 12, Red);
                c.Rect(7, 13, 2, 3, handle);
                c.Set(7, 16, guard); c.Set(8, 16, guard);
            }
            else
            {
                Color32 shaft = tier == 2 ? PixelCanvas.Hex("#c8d0e0") : tier == 3 ? Gold : Bark;
                Color32 shaftDark = PixelCanvas.Shade(shaft, 0.72f);
                c.Rect(7, 5, 2, 12, shaft);
                c.VLine(8, 6, 16, shaftDark);
                switch (tier)
                {
                    case 0: // 참나무 지팡이: curled wooden head with a leaf
                        c.Rect(6, 2, 4, 3, Bark);
                        c.Set(9, 1, Bark); c.Set(6, 1, Bark);
                        c.Set(10, 3, PixelCanvas.Hex("#6fbf4a")); c.Set(11, 2, PixelCanvas.Hex("#6fbf4a"));
                        break;
                    case 1: // 수정 지팡이: blue crystal
                        c.Set(6, 5, Gold); c.Set(9, 5, Gold);
                        c.Rect(6, 1, 4, 4, MagicCore);
                        c.Set(7, 0, PixelCanvas.Hex("#bff6ff")); c.Set(8, 0, MagicCore);
                        c.Set(7, 2, White);
                        break;
                    case 2: // 달빛 지팡이: crescent moon
                        c.Set(6, 5, Gold); c.Set(9, 5, Gold);
                        c.Circle(8f, 2.5f, 2.6f, PixelCanvas.Hex("#fff2b0"));
                        c.Circle(9.2f, 2f, 2f, PixelCanvas.Clear);
                        c.Set(6, 2, White);
                        break;
                    default: // 별의 지팡이: golden star
                        c.Set(6, 5, Magic); c.Set(9, 5, Magic);
                        c.VLine(7, 0, 4, Yellow); c.VLine(8, 0, 4, Yellow); c.HLine(5, 10, 2, Yellow);
                        c.Rect(6, 1, 4, 3, Yellow);
                        c.Set(7, 2, White);
                        break;
                }
            }
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }
    }
}
