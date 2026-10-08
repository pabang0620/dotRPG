using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Forest props ----------

        static PixelCanvas TownGrave(int v)
        {
            var c = Hd(30, 38);
            c.Ellipse(16f, 34f, 12f, 3f, TShadow);
            if (v % 2 == 0)
            {
                // Rounded headstone with a carved cross, a little tilted.
                for (int y = 6; y <= 33; y++)
                    for (int x = 5; x <= 24; x++)
                    {
                        int tilt = (33 - y) / 9;
                        int xx = x + tilt;
                        if (y < 13 && (x - 14.5f) * (x - 14.5f) / 90f + (y - 13f) * (y - 13f) / 49f > 1f) continue;
                        float u = (x - 5) / 19f;
                        c.Set(xx, y, TStone[u < 0.3f ? 4 : u < 0.7f ? 3 : 2]);
                    }
                c.Rect(15, 13, 2, 11, TStone[1]); c.Rect(11, 16, 10, 2, TStone[1]);
            }
            else
            {
                // Stone cross.
                c.Rect(12, 4, 7, 30, TStone[3]); c.Rect(5, 11, 21, 6, TStone[3]);
                c.VLine(12, 4, 33, TStone[4]); c.HLine(5, 25, 11, TStone[4]);
                c.VLine(18, 4, 33, TStone[2]); c.HLine(5, 25, 16, TStone[2]);
            }
            for (int x = 6; x <= 25; x++) if (c.IsOpaque(x, 32)) { c.Set(x, 32, TMoss); if (x % 2 == 0) c.Set(x, 31, TMossLight); }
            c.Outline(TOutlineStone);
            return c.WithPivot(15f, 4f);
        }

        static PixelCanvas TownBones()
        {
            var c = Hd(36, 20);
            var bone = Ramp("#b8ad96", "#d9d0bc", "#f1ead9");
            // Skull.
            c.Ellipse(10f, 9f, 6f, 5f, bone[2]); c.Rect(7, 12, 7, 3, bone[1]);
            c.Set(8, 9, TOutline); c.Set(9, 9, TOutline); c.Set(12, 9, TOutline); c.Set(13, 9, TOutline);
            c.Set(10, 11, TOutline);
            // Scattered bones.
            void Bone(int x0, int y0, int x1, int y1)
            {
                c.Line(x0, y0, x1, y1, bone[2]); c.Line(x0, y0 + 1, x1, y1 + 1, bone[1]);
                c.Circle(x0, y0, 1.4f, bone[2]); c.Circle(x1, y1, 1.4f, bone[2]);
            }
            Bone(18, 12, 30, 9); Bone(20, 16, 31, 16); Bone(4, 16, 9, 17);
            c.Outline(TOutline);
            return c.WithPivot(18f, 3f);
        }

        static PixelCanvas TownLog()
        {
            var c = Hd(66, 30);
            c.Ellipse(34f, 26f, 30f, 3.5f, TShadow);
            for (int y = 8; y <= 24; y++)
            {
                float v = (y - 8) / 16f;
                int idx = v < 0.2f ? 4 : v < 0.5f ? 3 : v < 0.8f ? 2 : 1;
                c.HLine(8, 57, y, TBark[idx]);
            }
            for (int x = 10; x < 56; x += 6) c.VLine(x, 12, 22, TBark[1]);
            c.Ellipse(58f, 16f, 5.5f, 8.5f, TBark[1]);
            c.Ellipse(58f, 16f, 4f, 6.8f, PixelCanvas.Hex("#c99a64"));
            c.Ellipse(58f, 16f, 2f, 3.5f, PixelCanvas.Hex("#a97a48"));
            for (int x = 10; x < 50; x++) if ((x * 7) % 5 < 3) c.Set(x, 8 + (x % 3 == 0 ? 0 : 1), x % 4 == 0 ? TMossLight : TMoss);
            c.Circle(20f, 7f, 2.5f, PixelCanvas.Hex("#c8323f")); c.Set(19, 6, White); c.VLine(20, 8, 9, PixelCanvas.Hex("#efe6d0"));
            c.Outline(TOutline);
            return c.WithPivot(33f, 4f);
        }

        static PixelCanvas TownShrooms()
        {
            var c = Hd(28, 24);
            var stem = PixelCanvas.Hex("#efe6d0");
            void Shroom(float x, float y, float r, Color32 cap, Color32 capLight, bool dots)
            {
                c.Rect(Mathf.RoundToInt(x - 1.5f), Mathf.RoundToInt(y), 3, Mathf.RoundToInt(r * 1.4f), stem);
                ShadeBlob(c, x, y, r, r * 0.65f, new[] { PixelCanvas.Shade(cap, 0.7f), cap, cap, capLight });
                if (dots) { c.Set(Mathf.RoundToInt(x - r * 0.4f), Mathf.RoundToInt(y - 1), White); c.Set(Mathf.RoundToInt(x + r * 0.3f), Mathf.RoundToInt(y - 2), White); }
            }
            Shroom(9f, 11f, 6f, PixelCanvas.Hex("#c8323f"), PixelCanvas.Hex("#ff6a5a"), true);
            Shroom(19f, 14f, 4.5f, PixelCanvas.Hex("#9a6a3a"), PixelCanvas.Hex("#c8945a"), false);
            Shroom(15f, 18f, 3f, PixelCanvas.Hex("#c8323f"), PixelCanvas.Hex("#ff6a5a"), true);
            c.Outline(TOutline);
            return c.WithPivot(14f, 3f);
        }

        static PixelCanvas TownRuin()
        {
            var c = Hd(32, 56);
            c.Ellipse(17f, 52f, 13f, 3.5f, TShadow);
            for (int y = 8; y <= 50; y++)
            {
                int top = 8 + (int)(4f * Mathf.Abs(Mathf.Sin(y)));
                for (int x = 6; x <= 25; x++)
                {
                    if (y < 16 && y < 8 + ((x * 5) % 9)) continue;     // broken top
                    float u = (x - 6) / 19f;
                    int idx = u < 0.25f ? 4 : u < 0.55f ? 3 : u < 0.8f ? 2 : 1;
                    if (y % 12 == 0) idx = 1;                           // stone drums
                    c.Set(x, y, TStone[idx]);
                }
            }
            for (int y = 18; y < 48; y += 2) { c.Set(9 + (y % 5), y, TMoss); c.Set(10 + (y % 5), y + 1, TMossLight); }
            c.Rect(3, 48, 26, 4, TStone[2]); c.HLine(3, 28, 48, TStone[4]);
            c.Outline(TOutlineStone);
            return c.WithPivot(16f, 4f);
        }

        /// <summary>Construction-site material piles: 0 = logs, 1 = stones.</summary>
        static PixelCanvas TownPile(bool stone)
        {
            var c = Hd(42, 30);
            c.Ellipse(22f, 26f, 18f, 3.5f, TShadow);
            if (stone)
            {
                ShadeBlob(c, 12f, 20f, 8f, 6f, TStone); ShadeBlob(c, 26f, 21f, 9f, 6.5f, TStone); ShadeBlob(c, 19f, 13f, 8f, 6f, TStone);
                ShadeBlob(c, 32f, 15f, 5f, 4f, TStone);
                c.Outline(TOutlineStone);
            }
            else
            {
                for (int k = 0; k < 3; k++) { c.Rect(4, 18 - k * 6 + (k % 2), 34, 6, TBark[2 + (k % 2)]); c.HLine(4, 37, 18 - k * 6 + (k % 2), TBark[4]); }
                for (int k = 0; k < 3; k++) c.Ellipse(38f, 21f - k * 6f + (k % 2), 3f, 3f, PixelCanvas.Hex("#d9ae78"));
                c.Outline(TOutline);
            }
            return c.WithPivot(21f, 4f);
        }
    }
}
