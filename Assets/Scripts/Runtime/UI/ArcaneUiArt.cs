using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Shared code-drawn ornaments for nodes and forge effects. No external textures required.</summary>
    public static class ArcaneUiArt
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        public static Sprite Get(string key)
        {
            if (cache.TryGetValue(key, out var sprite)) return sprite;
            const int size = 96;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float px = (x + .5f - size / 2f) / (size / 2f), py = (y + .5f - size / 2f) / (size / 2f);
                float r = Mathf.Sqrt(px * px + py * py), a = Mathf.Atan2(py, px);
                float alpha = 0f, shade = 1f;
                switch (key)
                {
                    case "halo": alpha = Mathf.Pow(Mathf.Clamp01(1f - r), 2f); break;
                    case "ring":
                        alpha = r > .83f && r < .94f || r > .70f && r < .73f ? 1f : 0f;
                        shade = Mathf.Clamp01(.7f + py * .3f - px * .15f); break;
                    case "sigil":
                        bool rune = r > .77f && r < .87f && Mathf.Cos(a * 12f) > .83f;
                        alpha = r > .9f && r < .925f || r > .68f && r < .7f || rune ? 1f : 0f;
                        break;
                    case "gem":
                        alpha = Mathf.Abs(px) + Mathf.Abs(py) < .81f ? 1f : 0f;
                        shade = py > px ? (py > -px ? 1f : .72f) : (py > -px ? .86f : .42f);
                        break;
                    case "star":
                        alpha = Mathf.Abs(px) * Mathf.Abs(py) < .025f && Mathf.Abs(px) + Mathf.Abs(py) < .9f ? Mathf.Clamp01((1f - r) * 2) : 0f;
                        break;
                }
                pixels[y * size + x] = new Color(shade, shade, shade, alpha);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "arcane_" + key, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels); tex.Apply(false, true);
            sprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * .5f, 96);
            sprite.name = tex.name; cache[key] = sprite; return sprite;
        }
    }
}
