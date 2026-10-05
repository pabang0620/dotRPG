using UnityEngine;

namespace DotRPG
{
    /// <summary>Purely visual. No colliders, combat state or random-number consumption.</summary>
    public sealed class CosmeticAura : MonoBehaviour
    {
        static Sprite sprite;
        static Sprite starSprite;
        // The ring lies on the ground around the feet: its far half is drawn behind the body, its near half in
        // front of it (one renderer behind would let a robe hem hide the near arc and the ring looks displaced).
        SpriteRenderer aura, auraFront;
        CosmeticStore store;
        static readonly System.Collections.Generic.Dictionary<(Sprite, bool), Sprite> halves =
            new System.Collections.Generic.Dictionary<(Sprite, bool), Sprite>();

        /// <summary>The far (upper) or near (lower) half of an aura sprite, both meeting at the ring's centre.</summary>
        static Sprite Half(Sprite full, bool near)
        {
            if (full == null) return null;
            if (halves.TryGetValue((full, near), out var cached)) return cached;
            var r = full.textureRect;
            float h = r.height / 2f;
            var rect = near ? new Rect(r.x, r.y, r.width, h) : new Rect(r.x, r.y + h, r.width, h);
            var half = UnityEngine.Sprite.Create(full.texture, rect, new Vector2(0.5f, near ? 1f : 0f), full.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            half.name = full.name + (near ? "_near" : "_far");
            halves[(full, near)] = half;
            return half;
        }

        /// <summary>The same artwork is used by the wardrobe preview and the world renderer.</summary>
        public static Sprite ForProduct(CosmeticProduct product)
        {
            if (product == null || product.IsFree || product.Rarity == CosmeticRarity.Common) return Sprite;
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
            view.auraFront = new GameObject("CosmeticAuraFront").AddComponent<SpriteRenderer>();
            view.auraFront.transform.SetParent(player.transform, false);
            view.auraFront.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            view.auraFront.sortingOrder = 1;
            HdMaterial.Apply(view.auraFront);
            player.GetComponent<YSort>().Refresh();
            store.Changed += view.Refresh;
            view.Refresh();
        }

        void Refresh()
        {
            var product = store.Equipped;
            var full = ForProduct(product);
            aura.sprite = Half(full, false);
            auraFront.sprite = Half(full, true);
            aura.color = auraFront.color = product.Color;
            aura.enabled = auraFront.enabled = aura.color.a > 0;
        }

        void Update()
        {
            var product = store?.Equipped;
            if (product != null && product.Effect != CosmeticEffect.None && aura.enabled) aura.color = auraFront.color = product.ColorAt(Time.time);
        }

        void OnDestroy()
        {
            if (store != null) store.Changed -= Refresh;
        }
    }
}
