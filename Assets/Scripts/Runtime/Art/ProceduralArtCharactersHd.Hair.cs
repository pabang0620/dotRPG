using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
