using UnityEngine;

namespace DotRPG
{
    /// <summary>Purely visual. No colliders, combat state or random-number consumption.</summary>
    public sealed class CosmeticAura : MonoBehaviour
    {
        static Sprite sprite;
        static Sprite starSprite;
        SpriteRenderer aura;
        CosmeticStore store;

        /// <summary>The same artwork is used by the wardrobe preview and the world renderer.</summary>
        public static Sprite ForProduct(CosmeticProduct product)
        {
            if (product == null || product.IsFree) return Sprite;
            if (starSprite == null) starSprite = Resources.Load<Sprite>("Art/fx_cosmetic_stars");
            return starSprite != null ? starSprite : Sprite;
        }

        public static Sprite Sprite
        {
            get
            {
                if (sprite != null) return sprite;
                const int width = 64, height = 32;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.name = "CosmeticAura";
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = (x + 0.5f - width / 2f) / (width / 2f - 2f);
                    float dy = (y + 0.5f - height / 2f) / (height / 2f - 2f);
                    float radius = dx * dx + dy * dy;
                    pixels[y * width + x] = radius >= 0.72f && radius <= 1f
                        ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 64f);
                return sprite;
            }
        }

        public static void Attach(PlayerController player, CosmeticStore store)
        {
            var view = player.gameObject.AddComponent<CosmeticAura>();
            view.store = store;
            view.aura = new GameObject("CosmeticAura").AddComponent<SpriteRenderer>();
            view.aura.transform.SetParent(player.transform, false);
            view.aura.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            view.aura.sortingOrder = -1;
            view.aura.sprite = Sprite;
            HdMaterial.Apply(view.aura);
            player.GetComponent<YSort>().Refresh();
            store.Changed += view.Refresh;
            view.Refresh();
        }

        void Refresh()
        {
            var product = store.Equipped;
            aura.sprite = ForProduct(product);
            aura.color = product.Color;
            aura.enabled = aura.color.a > 0;
        }

        void OnDestroy()
        {
            if (store != null) store.Changed -= Refresh;
        }
    }
}
