using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] A ring on the ground for the awakenings (Docs/PLAN_SKILL_VFX.md): a code-drawn circle (soft colour glow
    /// plus a crisp bright core, readable on any ground) seen in top-down perspective, with an optional generated
    /// picture (a sigil or a rune ring) laid flat inside it. Everything is driven from outside each frame:
    /// <see cref="Radius"/>, <see cref="Reveal"/> (how much of the circle is drawn, for "drawing" it),
    /// <see cref="Spin"/> (degrees, turns the picture), <see cref="Tint"/>, <see cref="Brightness"/>, <see cref="Alpha"/>.
    /// </summary>
    public class RingFx : MonoBehaviour
    {
        const int Points = 72;

        LineRenderer glow, core;
        Transform flat, art;
        SpriteRenderer picture;
        float pictureWorld = 1f;

        public Vector2 Center;
        public float Radius = 1f;
        public float Reveal = 1f;
        public float Spin;
        public float Width = .08f;
        public Color Tint = Color.white;
        public float Brightness = 1f;
        public float Alpha = 1f;
        /// <summary>0 = a circle; 3 or more = a regular polygon with that many corners (the guardian's hexagon).</summary>
        public int Sides;
        /// <summary>Turns the polygon (degrees); a circle ignores it.</summary>
        public float Corner;
        /// <summary>Picture size relative to the ring's diameter (1 = the picture's width equals the circle's).</summary>
        public float PictureScale = 1f;
        /// <summary>Ground perspective (.7, as the skill range ellipses); 1 = a true circle, matching a world-space radius.</summary>
        public float Squash = .7f;

        public static RingFx Spawn(Vector2 center, float radius, Color tint, int order, string picture = null)
        {
            var go = new GameObject("Ring");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<RingFx>();
            fx.Center = center;
            fx.Radius = radius;
            fx.Tint = tint;
            fx.glow = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 1);
            fx.core = FxMaterials.NewLine(go.transform, FxMaterials.Alpha, order + 2);
            if (picture != null && VfxLibrary.Has(picture))
            {
                var frames = VfxLibrary.Get(picture);
                if (frames.Length > 0)
                {
                    fx.flat = new GameObject("Flat").transform;
                    fx.flat.SetParent(go.transform, false);
                    fx.art = new GameObject("Art").transform;
                    fx.art.SetParent(fx.flat, false);
                    fx.picture = fx.art.gameObject.AddComponent<SpriteRenderer>();
                    fx.picture.sprite = frames[0];
                    fx.picture.sortingOrder = order;
                    if (VfxLibrary.Strip(picture).additive && FxMaterials.Additive != null) fx.picture.sharedMaterial = FxMaterials.Additive;
                    fx.pictureWorld = Mathf.Max(.01f, frames[0].bounds.size.x);
                }
            }
            fx.Refresh();
            return fx;
        }

        public void Kill() => Destroy(gameObject);

        void LateUpdate() => Refresh();

        /// <summary>Distance to a regular polygon's edge at angle a, relative to its corner radius (1 for a circle).</summary>
        float PolygonScale(float a)
        {
            if (Sides < 3) return 1f;
            float sector = Mathf.PI * 2f / Sides;
            float local = Mathf.Repeat(a - Corner * Mathf.Deg2Rad, sector) - sector * .5f;
            return Mathf.Cos(sector * .5f) / Mathf.Cos(local);
        }

        void Refresh()
        {
            float reveal = Mathf.Clamp01(Reveal);
            int count = Mathf.Max(0, Mathf.CeilToInt(Points * reveal) + 1);
            if (reveal <= 0f) count = 0;
            foreach (var line in new[] { glow, core })
            {
                line.positionCount = count;
                for (int i = 0; i < count; i++)
                {
                    // Drawn from the top, clockwise, so "drawing" the ring reads like a pen going round.
                    float a = Mathf.PI * .5f - Mathf.Min(i / (float)Points, reveal) * Mathf.PI * 2f;
                    float r = Radius * PolygonScale(a);
                    line.SetPosition(i, Center + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * Squash));
                }
            }
            float b = Mathf.Max(0f, Brightness);
            glow.widthMultiplier = Width * 3.2f * Mathf.Lerp(1f, 1.6f, Mathf.Clamp01(b - 1f));
            glow.startColor = glow.endColor = new Color(Tint.r, Tint.g, Tint.b, Mathf.Clamp01(.55f * b) * Alpha);
            core.widthMultiplier = Width * .5f;
            var white = Color.Lerp(Tint, Color.white, .7f);
            core.startColor = core.endColor = new Color(white.r, white.g, white.b, Mathf.Clamp01(.95f * Mathf.Min(1f, b)) * Alpha);
            if (picture != null)
            {
                flat.position = Center;
                flat.localScale = new Vector3(1f, Squash, 1f);
                float s = Radius * 2f * PictureScale / pictureWorld;
                art.localScale = new Vector3(s, s, 1f);
                art.localRotation = Quaternion.Euler(0f, 0f, Spin);
                var c = Color.Lerp(Tint, Color.white, .35f);
                picture.color = new Color(c.r * Mathf.Min(1f, b), c.g * Mathf.Min(1f, b), c.b * Mathf.Min(1f, b), Alpha * Mathf.Clamp01(reveal * 1.3f - .3f));
            }
        }
    }
}
