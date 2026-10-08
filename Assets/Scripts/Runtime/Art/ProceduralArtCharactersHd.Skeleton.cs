using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
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
    }
}
