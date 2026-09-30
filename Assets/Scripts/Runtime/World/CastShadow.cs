using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The sun shadow of one object: a child renderer drawn flat on the ground, under every object and
    /// character (it is not depth-sorted, see <see cref="YSort"/>). It follows the owner's sprite, so a
    /// felled tree casts its stump's shadow, an opened chest its lid's, and a hidden object none.
    /// </summary>
    public sealed class CastShadow : MonoBehaviour
    {
        /// <summary>Above the painted ground and ground decorations, below every depth-sorted object.</summary>
        public const int SortingOrder = -19500;

        SpriteRenderer owner, shadow;
        SunShadowStyle style;
        Sprite lastSprite;
        bool lastFlip, lastVisible;

        public static CastShadow Attach(SpriteRenderer owner, SunShadowStyle style)
        {
            if (owner == null) return null;
            var go = new GameObject("SunShadow");
            go.transform.SetParent(owner.transform, false);
            var cast = go.AddComponent<CastShadow>();
            cast.owner = owner;
            cast.style = style;
            cast.shadow = go.AddComponent<SpriteRenderer>();
            cast.Refresh();
            return cast;
        }

        void LateUpdate()
        {
            if (owner == null) return;
            bool visible = owner.enabled && !owner.forceRenderingOff;
            if (owner.sprite != lastSprite || owner.flipX != lastFlip || visible != lastVisible) Refresh();
        }

        void Refresh()
        {
            lastSprite = owner.sprite;
            lastFlip = owner.flipX;
            lastVisible = owner.enabled && !owner.forceRenderingOff;
            var sprite = lastVisible ? SunShadows.For(lastSprite, lastFlip, style) : null;
            shadow.sprite = sprite;
            shadow.enabled = sprite != null;
            shadow.sortingOrder = SortingOrder;
            if (sprite != null && sprite.pixelsPerUnit > 16.5f && FxMaterials.Sharp != null) shadow.sharedMaterial = FxMaterials.Sharp;
        }
    }
}
