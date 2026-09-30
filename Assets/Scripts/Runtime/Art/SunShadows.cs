using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Colour and strength of the sun shadows on one map (see <see cref="MapInfo.sunShadow"/>).</summary>
    public readonly struct SunShadowStyle
    {
        public readonly Color32 tint;
        /// <summary>Opacity of the shadow core, 0-1.</summary>
        public readonly float strength;

        public SunShadowStyle(Color32 tint, float strength)
        {
            this.tint = tint;
            this.strength = strength;
        }

        /// <summary>Shadow colour per map palette: green-grey on grass, warm brown in the canyon, cool blue on snow.</summary>
        public static SunShadowStyle For(MapInfo map)
        {
            float s = map != null ? map.sunShadow : 0f;
            switch (map != null ? map.theme : MapTheme.Town)
            {
                case MapTheme.Canyon: return new SunShadowStyle(new Color32(64, 38, 24, 255), s);
                case MapTheme.Winter: return new SunShadowStyle(new Color32(52, 78, 122, 255), s);
                case MapTheme.Forest: return new SunShadowStyle(new Color32(12, 28, 22, 255), s);
                default: return new SunShadowStyle(new Color32(20, 40, 34, 255), s);
            }
        }
    }

    /// <summary>
    /// Ground shadows cast by a sun fixed in the top-left of the sky. Each solid pixel of an object's
    /// sprite is projected onto the ground by its height above the sprite's base (the pivot): it moves
    /// right by <see cref="Shear"/> and down by <see cref="Drop"/> per pixel of height, so a tree's crown
    /// lands as a flattened, slanted patch to the lower right of its trunk and a house throws a short
    /// band along its right side. Shadows are one flat translucent tint with a softer rim, generated
    /// once per sprite and cached.
    /// </summary>
    public static class SunShadows
    {
        /// <summary>Sideways shift per pixel of height (sun on the left).</summary>
        public const float Shear = 0.5f;
        /// <summary>Downward shift per pixel of height (sun toward the top, fairly high).</summary>
        public const float Drop = 0.14f;

        static readonly Dictionary<(Sprite, bool, Color32, float), Sprite> cache = new Dictionary<(Sprite, bool, Color32, float), Sprite>();

        /// <summary>The shadow of <paramref name="source"/> (mirrored when the object is flipped), or null when it casts none.</summary>
        public static Sprite For(Sprite source, bool flipX, SunShadowStyle style)
        {
            if (source == null || style.strength <= 0f) return null;
            var key = (source, flipX, style.tint, style.strength);
            if (cache.TryGetValue(key, out var shadow)) return shadow;
            shadow = Build(source, flipX, style);
            cache[key] = shadow;
            return shadow;
        }

        static Sprite Build(Sprite src, bool flipX, SunShadowStyle style)
        {
            var tex = src.texture;
            if (tex == null || !tex.isReadable) return null;
            var rect = src.textureRect;
            int rx = Mathf.RoundToInt(rect.x), ry = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            var px = tex.GetPixels32();
            int tw = tex.width;
            float pivX = src.pivot.x, pivY = src.pivot.y;

            // Only solid art casts a shadow (baked contact shadows and glows are translucent).
            int top = -1;
            for (int y = h - 1; y >= 0 && top < 0; y--)
                for (int x = 0; x < w; x++)
                    if (px[(ry + y) * tw + rx + x].a >= 128) { top = y; break; }
            if (top < 0) return null;
            float maxH = Mathf.Max(0f, top - pivY);
            // Flat things (crop holes, pebbles) do not need a sun shadow.
            float unit = src.pixelsPerUnit;
            if (maxH < unit * 0.3f) return null;

            int extraX = Mathf.CeilToInt(maxH * Shear) + 2;
            int minY = Mathf.FloorToInt(pivY - maxH * Drop) - 2;
            int maxY = Mathf.CeilToInt(pivY) + 2;
            int sw = w + extraX, sh = maxY - minY + 1;
            var mask = new bool[sw * sh];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[(ry + y) * tw + rx + x].a < 128) continue;
                    int sx = flipX ? w - 1 - x : x;
                    float height = Mathf.Max(0f, y - pivY);
                    float dx = sx + height * Shear;
                    float dy = (height > 0f ? pivY - height * Drop : y) - minY;
                    int ix = Mathf.FloorToInt(dx), iy = Mathf.FloorToInt(dy);
                    if (ix < 0 || iy < 0 || ix >= sw || iy >= sh) continue;
                    mask[iy * sw + ix] = true;
                    // Cover the pixel the slanted edge reaches into, so the patch has no pinholes.
                    if (dx - ix > 0.5f && ix + 1 < sw) mask[iy * sw + ix + 1] = true;
                }
            // Close single-pixel holes.
            var closed = (bool[])mask.Clone();
            for (int y = 1; y < sh - 1; y++)
                for (int x = 1; x < sw - 1; x++)
                {
                    int i = y * sw + x;
                    if (mask[i]) continue;
                    int n = (mask[i - 1] ? 1 : 0) + (mask[i + 1] ? 1 : 0) + (mask[i - sw] ? 1 : 0) + (mask[i + sw] ? 1 : 0);
                    if (n >= 3) closed[i] = true;
                }

            // Flat tint, softer rim. Every pixel carries the tint colour so bilinear edges stay clean.
            byte core = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(style.strength));
            byte rim = (byte)Mathf.RoundToInt(core * 0.55f);
            var outPx = new Color32[sw * sh];
            var t = style.tint;
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    int i = y * sw + x;
                    byte a = 0;
                    if (closed[i])
                    {
                        bool edge = x == 0 || y == 0 || x == sw - 1 || y == sh - 1
                            || !closed[i - 1] || !closed[i + 1] || !closed[i - sw] || !closed[i + sw];
                        a = edge ? rim : core;
                    }
                    outPx[i] = new Color32(t.r, t.g, t.b, a);
                }

            bool smooth = src.pixelsPerUnit > 16.5f && FxMaterials.Sharp != null;
            var shadowTex = new Texture2D(sw, sh, TextureFormat.RGBA32, false)
            {
                name = src.name + "_sunshadow",
                filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            shadowTex.SetPixels32(outPx);
            shadowTex.Apply(false, true);
            float pivotX = flipX ? w - pivX : pivX;
            var pivot = new Vector2(pivotX / sw, (pivY - minY) / sh);
            var sprite = Sprite.Create(shadowTex, new Rect(0, 0, sw, sh), pivot, src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = shadowTex.name;
            return sprite;
        }
    }
}
