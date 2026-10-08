using System;
using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        static PixelCanvas DrawDeco(string[] p)
        {
            switch (p[1])
            {
                case "tuft":
                {
                    var c = new PixelCanvas(8, 6);
                    c.VLine(1, 3, 5, GrassDark); c.VLine(3, 1, 5, GrassDark); c.VLine(5, 2, 5, GrassDark); c.VLine(6, 4, 5, GrassDark);
                    c.Set(3, 0, GrassLight);
                    return c.WithBottomPivot();
                }
                case "flower":
                {
                    var c = new PixelCanvas(7, 7);
                    bool pink = p.Length > 2 && p[2] == "1";
                    var petal = pink ? PixelCanvas.Hex("#ffb3d1") : White;
                    c.Rect(2, 1, 3, 5, petal); c.Rect(1, 2, 5, 3, petal);
                    c.Rect(2, 2, 3, 3, Yellow);
                    c.Set(3, 3, PixelCanvas.Hex("#f0a020"));
                    c.Outline(PixelCanvas.WithAlpha(GrassDark, 160));
                    return c.WithBottomPivot();
                }
                case "grassedge": return DrawGrassEdge(int.Parse(p[2]));
                case "moss":
                {
                    var c = new PixelCanvas(8, 5);
                    c.Ellipse(4, 3, 3.5f, 1.8f, PixelCanvas.WithAlpha(MossA, 220));
                    c.PaintEllipse(3.5f, 2.5f, 1.8f, 1f, PixelCanvas.WithAlpha(GrassLight, 200));
                    return c.WithBottomPivot();
                }
                case "pebble":
                {
                    var c = new PixelCanvas(6, 4);
                    c.Ellipse(3, 2, 2.5f, 1.6f, StoneDark);
                    c.PaintEllipse(2.6f, 1.6f, 1.5f, 0.9f, Stone);
                    return c.WithBottomPivot();
                }
            }
            return null;
        }

        // ---------- Props ----------

        static PixelCanvas DrawTree(bool fruit)
        {
            var c = new PixelCanvas(32, 38);
            // Trunk and roots.
            c.Rect(13, 26, 6, 9, Bark);
            c.VLine(13, 26, 34, BarkDark);
            c.VLine(18, 28, 34, BarkDark);
            c.Rect(11, 33, 3, 2, Bark);
            c.Rect(18, 33, 3, 2, Bark);
            c.Set(15, 29, BarkDark); c.Set(16, 31, BarkDark);
            // Canopy: overlapping blobs, dark first.
            c.Ellipse(16, 14, 13, 12, LeafDark);
            c.Ellipse(8, 19, 7, 7, LeafDark);
            c.Ellipse(24, 19, 7, 7, LeafDark);
            c.Ellipse(16, 21, 9, 6, LeafDark);
            c.PaintEllipse(15, 12, 11, 9.5f, Leaf);
            c.PaintEllipse(8, 17, 5, 5, Leaf);
            c.PaintEllipse(23, 17, 5.5f, 5, Leaf);
            c.PaintEllipse(12, 8, 6, 4.5f, LeafLight);
            c.PaintEllipse(21, 11, 4, 3.5f, LeafLight);
            c.PaintEllipse(7, 15, 3, 2.5f, LeafLight);
            c.Set(10, 6, LeafShine); c.Set(11, 6, LeafShine); c.Set(20, 9, LeafShine); c.Set(6, 13, LeafShine);
            // Leaf texture flecks.
            var rng = new System.Random(fruit ? 91 : 17);
            for (int i = 0; i < 14; i++)
            {
                int x = rng.Next(5, 27), y = rng.Next(5, 24);
                c.Paint(x, y, LeafDark);
            }
            if (fruit)
            {
                int[,] spots = { { 9, 10 }, { 17, 7 }, { 22, 14 }, { 13, 16 }, { 6, 19 }, { 25, 20 }, { 19, 20 } };
                for (int i = 0; i < spots.GetLength(0); i++)
                {
                    int x = spots[i, 0], y = spots[i, 1];
                    c.Rect(x, y, 2, 2, Carrot);
                    c.Set(x, y, CarrotLight);
                }
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3.5f);
        }

        static PixelCanvas DrawStump()
        {
            var c = new PixelCanvas(18, 14);
            c.Rect(3, 5, 12, 6, Bark);
            c.Rect(1, 9, 4, 3, Bark);
            c.Rect(13, 9, 4, 3, Bark);
            c.VLine(6, 6, 10, BarkDark); c.VLine(11, 6, 11, BarkDark);
            c.Ellipse(9, 5, 6, 3, WoodLight);
            c.PaintEllipse(9, 5, 3.5f, 1.6f, Wood);
            c.PaintEllipse(9, 5, 1.5f, 0.8f, WoodLight);
            c.Outline(Outline);
            return c.WithPivot(9, 2.5f);
        }

        static PixelCanvas DrawRock()
        {
            var c = new PixelCanvas(18, 14);
            c.Ellipse(9, 8, 8, 5.5f, StoneDark);
            c.Ellipse(7, 6, 5, 3.5f, StoneDark);
            c.PaintEllipse(8.5f, 7, 7, 4.2f, Stone);
            c.PaintEllipse(6.5f, 5, 3.5f, 2.2f, StoneLight);
            c.Set(12, 9, StoneDark); c.Set(13, 8, StoneDark); c.Set(4, 10, StoneDark);
            c.Outline(PixelCanvas.Hex("#3f454f"));
            return c.WithPivot(9, 2.5f);
        }

        static PixelCanvas DrawBush()
        {
            var c = new PixelCanvas(18, 16);
            c.Ellipse(9, 9, 8, 6.5f, LeafDark);
            c.PaintEllipse(8.5f, 8, 7, 5.5f, Leaf);
            c.PaintEllipse(6, 6, 3.5f, 2.5f, LeafLight);
            int[,] berries = { { 5, 9 }, { 10, 6 }, { 13, 10 }, { 8, 12 }, { 12, 3 } };
            for (int i = 0; i < berries.GetLength(0); i++)
            {
                int x = berries[i, 0], y = berries[i, 1];
                c.Rect(x, y, 2, 2, Red);
                c.Set(x, y, RedLight);
            }
            c.Outline(Outline);
            return c.WithPivot(9, 2.5f);
        }

        /// <summary>mask: 1 = connects left, 2 = right, 4 = up, 8 = down.</summary>
        static PixelCanvas DrawFence(int mask)
        {
            var c = new PixelCanvas(16, 18);
            bool left = (mask & 1) != 0, right = (mask & 2) != 0, up = (mask & 4) != 0;
            if (left) { c.Rect(0, 7, 7, 2, Wood); c.Rect(0, 12, 7, 2, Wood); c.HLine(0, 6, 7, WoodLight); }
            if (right) { c.Rect(9, 7, 7, 2, Wood); c.Rect(9, 12, 7, 2, Wood); c.HLine(9, 15, 7, WoodLight); }
            if (up) { c.Rect(7, 0, 2, 5, Wood); c.VLine(7, 0, 4, WoodLight); }
            // Post.
            c.Rect(6, 4, 4, 12, Wood);
            c.VLine(9, 5, 15, WoodDark);
            c.HLine(6, 9, 4, WoodLight);
            c.Set(7, 3, WoodLight); c.Set(8, 3, WoodLight);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawSign()
        {
            var c = new PixelCanvas(16, 18);
            c.Rect(7, 9, 2, 8, WoodDark);
            c.Rect(1, 2, 14, 8, Wood);
            c.HLine(1, 14, 2, WoodLight);
            c.HLine(1, 14, 9, WoodDark);
            c.HLine(3, 12, 5, WoodDark);
            c.HLine(3, 9, 7, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }

        static PixelCanvas DrawCrate(bool carrots)
        {
            var c = new PixelCanvas(16, 18);
            if (carrots)
            {
                for (int i = 0; i < 4; i++)
                {
                    int x = 3 + i * 3;
                    c.Rect(x, 1, 1, 3, Leaf);
                    c.Set(x - 1, 2, LeafLight);
                    c.Rect(x - 1, 4, 3, 3, Carrot);
                    c.Set(x - 1, 4, CarrotLight);
                }
            }
            c.Rect(1, 6, 14, 11, Wood);
            c.HLine(1, 14, 6, WoodLight);
            c.HLine(1, 14, 11, WoodDark);
            c.HLine(1, 14, 16, WoodDark);
            c.VLine(1, 6, 16, WoodDark); c.VLine(14, 6, 16, WoodDark);
            c.Line(2, 7, 13, 10, WoodDark);
            c.Line(2, 12, 13, 15, WoodDark);
            c.Outline(Outline);
            return c.WithPivot(8, 1.5f);
        }
    }
}
