using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Generated transparent winter atlas. Rectangles are measured on a 1280-unit canvas.</summary>
    public static class WinterVillageArt
    {
        public const string Prefix = "winter_village_";
        static Texture2D atlas;
        static bool attempted;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public static Sprite Get(string key)
        {
            if (!key.StartsWith(Prefix)) return null;
            if (sprites.TryGetValue(key, out var cached)) return cached;
            if (!attempted)
            {
                attempted = true;
                var path = Path.Combine(Application.streamingAssetsPath, "Art", "winter_village_atlas.png");
                if (StreamingFiles.Exists(path))
                {
                    atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!atlas.LoadImage(StreamingFiles.ReadAllBytes(path))) { Object.Destroy(atlas); atlas = null; }
                    else { atlas.filterMode = FilterMode.Point; atlas.wrapMode = TextureWrapMode.Clamp; }
                }
            }
            if (atlas == null) return null;
            Rect rect; float worldWidth; float ground;
            switch (key.Substring(Prefix.Length))
            {
                case "house": rect = new Rect(0, 0, 393, 446); worldWidth = 4.6f; ground = 434; break;
                case "barn": rect = new Rect(394, 0, 482, 446); worldWidth = 6.1f; ground = 431; break;
                case "gate": rect = new Rect(881, 0, 399, 446); worldWidth = 4.8f; ground = 428; break;
                case "broadleaf": rect = new Rect(0, 447, 415, 465); worldWidth = 4.2f; ground = 889; break;
                case "fir": rect = new Rect(444, 447, 374, 473); worldWidth = 3.5f; ground = 895; break;
                case "pine": rect = new Rect(827, 440, 453, 483); worldWidth = 4.2f; ground = 908; break;
                case "fence_h": rect = new Rect(0, 941, 404, 280); worldWidth = 1.13f; ground = 1193; break;
                case "fence_v": rect = new Rect(516, 922, 218, 340); worldWidth = .7f; ground = 1244; break;
                case "rock": rect = new Rect(826, 935, 454, 315); worldWidth = 1.3f; ground = 1225; break;
                default: return null;
            }
            float scale = atlas.width / 1280f;
            float pivotY = (rect.yMax - ground) / rect.height;
            rect = new Rect(rect.x * scale, atlas.height - rect.yMax * scale, rect.width * scale, rect.height * scale);
            var sprite = Sprite.Create(atlas, rect, new Vector2(.5f, pivotY), rect.width / worldWidth, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            sprites[key] = sprite;
            return sprite;
        }
    }
}
