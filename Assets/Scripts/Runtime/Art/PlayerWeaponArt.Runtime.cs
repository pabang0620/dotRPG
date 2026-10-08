using UnityEngine;

namespace DotRPG
{
    public static partial class PlayerWeaponArt
    {
        public static Sprite Get(string key)
        {
            if (!TryKey(key, out int tier)) return null;
            // Dedicated art family only. Item IDs, icon artwork, combat and pose rig stay intact.
            // Artist-authored replacements can still be placed at this explicit Resources path.
            var supplied = Resources.Load<Sprite>(ResourceFolder + key);
            if (supplied != null) return supplied;
            var c = Draw(key);
            var texture = new Texture2D(c.Width, c.Height, TextureFormat.RGBA32, false)
            {
                name = "player_weapon_v3_" + key,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(c.ToTexturePixels());
            // Enhancement masks sample the alpha silhouette; keep the new source readable.
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, c.Width, c.Height),
                new Vector2(c.PivotX / c.Width, c.PivotY / c.Height), KatanaArt.Ppu,
                0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return sprite;
        }
    }
}
