using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Weapon-local light, after the eight-way hand pose and depth sort have been applied.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class WeaponEnhanceVfx : MonoBehaviour
    {
        struct Masks { public Sprite edge, aura; }
        static readonly Dictionary<Sprite, Masks> cache = new Dictionary<Sprite, Masks>();
        PlayerController owner;
        SpriteRenderer source, edge, aura;
        readonly SpriteRenderer[] motes = new SpriteRenderer[4];
        Sprite lastSprite;
        Vector2[] points;
        public int Tier { get; private set; }
        public bool LightVisible => edge != null && edge.enabled;
        public bool AuraVisible => aura != null && aura.enabled;
        public static int TierFor(int level) => level < 7 ? 0 : level < 11 ? 1 : 2;

        public void Setup(PlayerController player, SpriteRenderer weapon)
        {
            owner = player; source = weapon;
            edge = Layer("WeaponBlueLight"); aura = Layer("WeaponBlueAura");
            for (int i = 0; i < motes.Length; i++)
            {
                motes[i] = Layer("WeaponAuraSpark" + i);
                motes[i].sprite = ArcaneUiArt.Get("star");
            }
        }
        SpriteRenderer Layer(string name)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(source.transform, false);
            sr.sharedMaterial = FxMaterials.Additive; sr.enabled = false;
            return sr;
        }
        void LateUpdate()
        {
            if (source == null || owner == null || owner.Data == null) return;
            Tier = TierFor(EquipmentDatabase.LevelOfKey(owner.Data.Equipment[EquipSlot.Weapon]));
            bool show = Tier > 0 && source.enabled && source.sprite != null && !owner.IsDead;
            edge.enabled = show; aura.enabled = show && Tier == 2;
            foreach (var p in motes) p.enabled = show && Tier == 2;
            if (!show) return;
            if (lastSprite != source.sprite)
            {
                lastSprite = source.sprite;
                var masks = GetMasks(source.sprite, out points);
                edge.sprite = masks.edge; aura.sprite = masks.aura;
            }
            float t = Time.time;
            float pulse = .82f + .18f * Mathf.Sin(t * 2.4f);
            Follow(edge); Follow(aura);
            edge.color = new Color(.12f, .55f, 1f, (Tier == 2 ? .9f : .64f) * pulse * source.color.a);
            aura.color = new Color(.06f, .38f, 1f, .75f * pulse * source.color.a);
            // Silhouette stays fixed to the blade. Only the small lights flow along its opaque pixels.
            for (int i = 0; i < motes.Length; i++)
            {
                var p = motes[i]; Follow(p);
                float phase = Mathf.Repeat(t * .32f + i * .25f, 1f);
                int index = Mathf.Min(points.Length - 1, (int)(phase * points.Length));
                var at = points[index];
                at.x *= source.flipX ? -1 : 1; at.y *= source.flipY ? -1 : 1;
                p.transform.localPosition = at;
                p.transform.localScale = Vector3.one * (.055f + .06f * Mathf.Sin(phase * Mathf.PI));
                p.color = new Color(.48f, .85f, 1f, Mathf.Sin(phase * Mathf.PI) * source.color.a);
            }
        }
        void Follow(SpriteRenderer sr)
        {
            sr.sortingLayerID = source.sortingLayerID; sr.sortingOrder = source.sortingOrder;
            sr.flipX = source.flipX; sr.flipY = source.flipY;
            sr.maskInteraction = source.maskInteraction;
        }
        static Masks GetMasks(Sprite sprite, out Vector2[] anchors)
        {
            // All game weapon sheets are readable (also used by the hit-flash silhouette system).
            const int pad = 7;
            int w = (int)sprite.rect.width, h = (int)sprite.rect.height, tw = w + pad * 2, th = h + pad * 2;
            var tex = sprite.texture;
            var src = tex.GetPixels32();
            var mask = new bool[tw * th];
            var path = new List<Vector2>();
            int ox = (int)sprite.rect.x, oy = (int)sprite.rect.y;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (src[(oy + y) * tex.width + ox + x].a > 100)
                {
                    mask[(y + pad) * tw + x + pad] = true;
                    if (y > sprite.pivot.y + 3) path.Add((new Vector2(x + .5f, y + .5f) - sprite.pivot) / sprite.pixelsPerUnit);
                }
            if (path.Count == 0) path.Add(Vector2.zero);
            anchors = path.ToArray();
            if (cache.TryGetValue(sprite, out var ready)) return ready;
            var rim = new Color32[tw * th]; var haze = new Color32[tw * th];
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
            {
                int nearest = pad + 1;
                for (int dy = -pad; dy <= pad; dy++) for (int dx = -pad; dx <= pad; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= tw || yy >= th || !mask[yy * tw + xx]) continue;
                    nearest = Mathf.Min(nearest, Mathf.Abs(dx) + Mathf.Abs(dy));
                }
                bool inside = mask[y * tw + x];
                rim[y * tw + x] = new Color(1, 1, 1, inside ? .28f : nearest == 1 ? .85f : nearest == 2 ? .28f : 0f);
                haze[y * tw + x] = new Color(1, 1, 1, inside ? .08f : Mathf.Pow(Mathf.Clamp01(1f - nearest / 7f), 2f) * .7f);
            }
            Sprite Make(Color32[] pixels, string name)
            {
                var texture = new Texture2D(tw, th, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(pixels); texture.Apply(false, true);
                var pivot = new Vector2((sprite.pivot.x + pad) / tw, (sprite.pivot.y + pad) / th);
                return Sprite.Create(texture, new Rect(0, 0, tw, th), pivot, sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            ready = new Masks { edge = Make(rim, "weapon_blue_rim"), aura = Make(haze, "weapon_blue_aura") };
            cache[sprite] = ready; return ready;
        }
    }
}
