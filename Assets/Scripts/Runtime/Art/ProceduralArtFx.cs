using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Skill effect sprites: soft glow, shock ring, magic circle, whirlwind trail, ground frost and
    /// cracks, rock spikes, ice crystals and shards, snow, sparks and slash marks.
    /// Most are white so the effect code can tint them; the ice and rock pieces carry their own colours.
    /// Sizes are chosen so each sprite is drawn at (or scaled up from) its natural size, never shrunk,
    /// which keeps the pixel lines crisp.
    /// </summary>
    public static partial class ProceduralArt
    {
        static readonly Color32 IceDeep = PixelCanvas.Hex("#3a78d8");
        static readonly Color32 IceMid = PixelCanvas.Hex("#79c2ff");
        static readonly Color32 IceLight = PixelCanvas.Hex("#c4ecff");
        static readonly Color32 IceWhite = PixelCanvas.Hex("#f4fcff");
        static readonly Color32 IceOutline = PixelCanvas.Hex("#23458f");

        static Color32 Whiteish(float alpha) =>
            new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));

        /// <summary>Distance from a pixel centre to the canvas centre, plus its angle in degrees (0 = right, counter-clockwise, y up).</summary>
        static float Polar(PixelCanvas c, int x, int y, out float angle)
        {
            float dx = x + 0.5f - c.Width * 0.5f;
            float dy = c.Height * 0.5f - (y + 0.5f);
            angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Writes a pixel without blending.</summary>
        static void Put(PixelCanvas c, int x, int y, Color32 color) => c.Pixels[y * c.Width + x] = color;

        /// <summary>One ice crystal from a base point to a tip, lit from the left.</summary>
        static void Crystal(PixelCanvas c, float bx, float by, float tx, float ty, float halfWidth)
        {
            float ax = tx - bx, ay = ty - by;
            float len = Mathf.Sqrt(ax * ax + ay * ay);
            ax /= len; ay /= len;
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    float px = x + 0.5f - bx, py = y + 0.5f - by;
                    float along = px * ax + py * ay;   // 0 at the base, len at the tip
                    float side = px * -ay + py * ax;   // signed distance across the crystal
                    if (along < 0f || along > len) continue;
                    float u = along / len;
                    float w = u < 0.68f ? halfWidth : halfWidth * (1f - u) / 0.32f;
                    if (Mathf.Abs(side) > w) continue;
                    var col = side < -w * 0.3f ? IceLight : side > w * 0.35f ? IceDeep : IceMid;
                    if (Mathf.Abs(side + w * 0.5f) < 0.5f && u > 0.15f && u < 0.85f) col = IceWhite;
                    c.Set(x, y, col);
                }
        }
    }
}
