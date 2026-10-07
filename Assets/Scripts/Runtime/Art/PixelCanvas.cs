using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Tiny CPU-side pixel buffer used to author placeholder art in code.
    /// Coordinates are top-down (y = 0 is the top row) because that is how pixel art is usually
    /// described; <see cref="ToTexturePixels"/> flips to Unity's bottom-up layout.
    /// Only uses <see cref="Color32"/> from Unity so it can also run in offline preview tools.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        /// <summary>Pivot in pixels measured from the bottom-left corner (Unity convention).</summary>
        public float PivotX;
        public float PivotY;

        /// <summary>Optional 9-slice border (left, bottom, right, top) in pixels for UI sprites.</summary>
        public int BorderLeft, BorderBottom, BorderRight, BorderTop;

        /// <summary>
        /// Art pixels per 16px game pixel. 1 = the classic 16px-per-tile art; 2 = the high-resolution
        /// town/forest art (32px per tile), which the sprite library imports at twice the pixels per unit.
        /// </summary>
        public int Density = 1;

        public static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
            PivotX = width * 0.5f;
            PivotY = height * 0.5f;
        }

        public PixelCanvas WithPivot(float x, float y)
        {
            PivotX = x;
            PivotY = y;
            return this;
        }

        public PixelCanvas WithBottomPivot()
        {
            PivotX = Width * 0.5f;
            PivotY = 0.5f;
            return this;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public Color32 Get(int x, int y) => InBounds(x, y) ? Pixels[y * Width + x] : Clear;

        public bool IsOpaque(int x, int y) => InBounds(x, y) && Pixels[y * Width + x].a > 0;

        public void Set(int x, int y, Color32 c)
        {
            if (!InBounds(x, y)) return;
            if (c.a == 255 || c.a == 0)
            {
                Pixels[y * Width + x] = c;
                return;
            }
            // Alpha blend over what is already there.
            var d = Pixels[y * Width + x];
            float a = c.a / 255f;
            float da = d.a / 255f;
            float outA = a + da * (1f - a);
            if (outA <= 0f)
            {
                Pixels[y * Width + x] = Clear;
                return;
            }
            byte Mix(byte s, byte t) => (byte)Math.Round((s * a + t * da * (1f - a)) / outA);
            Pixels[y * Width + x] = new Color32(Mix(c.r, d.r), Mix(c.g, d.g), Mix(c.b, d.b), (byte)Math.Round(outA * 255f));
        }

        /// <summary>Only paints where the canvas is already opaque (for shading / details).</summary>
        public void Paint(int x, int y, Color32 c)
        {
            if (IsOpaque(x, y)) Set(x, y, c);
        }

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Set(xx, yy, c);
        }

        public void HLine(int x0, int x1, int y, Color32 c)
        {
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Set(x, y, c);
        }

        public void VLine(int x, int y0, int y1, Color32 c)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Set(x, y, c);
        }

        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Filled ellipse centred on (cx, cy) using pixel centres.</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            int x0 = (int)Math.Floor(cx - rx), x1 = (int)Math.Ceiling(cx + rx);
            int y0 = (int)Math.Floor(cy - ry), y1 = (int)Math.Ceiling(cy + ry);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) / rx;
                    float ny = (y + 0.5f - cy) / ry;
                    if (nx * nx + ny * ny <= 1f) Set(x, y, c);
                }
        }

        public void Circle(float cx, float cy, float r, Color32 c) => Ellipse(cx, cy, r, r, c);

        /// <summary>Paints (only on opaque pixels) the part of an ellipse - used for highlights/shadows.</summary>
        public void PaintEllipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            int x0 = (int)Math.Floor(cx - rx), x1 = (int)Math.Ceiling(cx + rx);
            int y0 = (int)Math.Floor(cy - ry), y1 = (int)Math.Ceiling(cy + ry);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) / rx;
                    float ny = (y + 0.5f - cy) / ry;
                    if (nx * nx + ny * ny <= 1f) Paint(x, y, c);
                }
        }

        /// <summary>Adds a 1px outline around every opaque shape (4-neighbourhood).</summary>
        public void Outline(Color32 c)
        {
            var copy = (Color32[])Pixels.Clone();
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (copy[y * Width + x].a > 0) continue;
                    bool edge = Opaque(copy, x - 1, y) || Opaque(copy, x + 1, y) || Opaque(copy, x, y - 1) || Opaque(copy, x, y + 1);
                    if (edge) Pixels[y * Width + x] = c;
                }
        }

        bool Opaque(Color32[] buffer, int x, int y) => InBounds(x, y) && buffer[y * Width + x].a > 128;

        /// <summary>Copies another canvas on top (alpha blended) at the given top-left offset.</summary>
        public void Blit(PixelCanvas src, int ox, int oy)
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var c = src.Pixels[y * src.Width + x];
                    if (c.a > 0) Set(ox + x, oy + y, c);
                }
        }

        /// <summary>Pixels in Unity texture order (row 0 = bottom).</summary>
        public Color32[] ToTexturePixels()
        {
            var result = new Color32[Pixels.Length];
            for (int y = 0; y < Height; y++)
                Array.Copy(Pixels, y * Width, result, (Height - 1 - y) * Width, Width);
            return result;
        }

        public static Color32 Hex(string hex, byte alpha = 255)
        {
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            int v = Convert.ToInt32(hex, 16);
            return new Color32((byte)((v >> 16) & 255), (byte)((v >> 8) & 255), (byte)(v & 255), alpha);
        }

        public static Color32 Shade(Color32 c, float factor)
        {
            byte S(byte v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v * factor)));
            return new Color32(S(c.r), S(c.g), S(c.b), c.a);
        }

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);
    }
}
