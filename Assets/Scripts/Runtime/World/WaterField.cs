using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Exact painted water pixels, rather than square map cells. Shared by flow and fish navigation.</summary>
    public sealed class WaterField
    {
        public readonly int Width, Height;
        public readonly Color32[] Mask;
        public readonly List<Vector2> SwimPoints = new List<Vector2>();
        public int WetPixels { get; private set; }
        public int FallPixels { get; private set; }
        WaterField(int w, int h) { Width = w; Height = h; Mask = new Color32[w * h]; }

        public static WaterField Create(byte[] kinds, Color32[] art, int w, int h, int water, int fall = -1, Color32 ice = default)
        {
            var field = new WaterField(w, h);
            var distance = new ushort[w * h];
            for (int i = 0; i < art.Length; i++)
            {
                var c = art[i];
                // Exclude painted brown bank faces, lilies, bridge rails, and winter ice sheets.
                bool wet = (kinds[i] == water || kinds[i] == fall) && c.g > c.r + 9 && c.b > c.r + 9
                    && !(c.r == ice.r && c.g == ice.g && c.b == ice.b);
                if (!wet) continue;
                bool waterfall = kinds[i] == fall;
                field.Mask[i] = new Color32(255, 0, waterfall ? (byte)255 : (byte)0, 255);
                // Falls still animate, but count as boundaries for the fish's whole-body clearance.
                distance[i] = waterfall ? (ushort)0 : (ushort)255;
                field.WetPixels++;
                if (waterfall) field.FallPixels++;
            }
            // Manhattan distance is conservatively reduced when testing the fish's circular clearance.
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (distance[i] == 0) continue;
                distance[i] = (ushort)Mathf.Min(distance[i], Mathf.Min(x > 0 ? distance[i - 1] : 0, y > 0 ? distance[i - w] : 0) + 1);
            }
            for (int y = h - 1; y >= 0; y--) for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                if (distance[i] == 0) continue;
                distance[i] = (ushort)Mathf.Min(distance[i], Mathf.Min(x + 1 < w ? distance[i + 1] : 0, y + 1 < h ? distance[i + w] : 0) + 1);
                var c = field.Mask[i]; c.g = (byte)Mathf.Min(255, distance[i] * 4); field.Mask[i] = c;
            }
            for (int y = 16; y < h; y += 24) for (int x = 16; x < w; x += 24)
            {
                var p = new Vector2((x + .5f) / 32f, (y + .5f) / 32f);
                if (field.CanSwim(p, .8f)) field.SwimPoints.Add(p);
            }
            return field;
        }
        public bool CanSwim(Vector2 p, float radius)
        {
            int x = Mathf.FloorToInt(p.x * 32), y = Mathf.FloorToInt(p.y * 32);
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            var c = Mask[y * Width + x];
            return c.r > 0 && c.b == 0 && c.g / 4f >= radius * 32 * 1.42f + 1;
        }
        public bool ClearSegment(Vector2 a, Vector2 b, float radius)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 12);
            for (int s = 0; s <= steps; s++)
                if (!CanSwim(Vector2.Lerp(a, b, steps == 0 ? 0 : s / (float)steps), radius)) return false;
            return true;
        }
    }
}
