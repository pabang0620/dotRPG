using UnityEngine;

namespace DotRPG
{
    /// <summary>Purely visual. No colliders, combat state or random-number consumption.</summary>
    public sealed class CosmeticAura : MonoBehaviour
    {
        static Sprite sprite;
        SpriteRenderer aura;
        CosmeticStore store;

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
            aura.color = store.Equipped.Color;
            aura.enabled = aura.color.a > 0;
        }

        void OnDestroy()
        {
            if (store != null) store.Changed -= Refresh;
        }
    }
}
