using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// One short-lived sprite effect. <see cref="Spawn"/> creates it; the chainable setters add
    /// scaling, spin, drift, a start delay, additive glow, and an "erupt from the ground" pop.
    /// </summary>
    public class SkillFx : MonoBehaviour
    {
        /// <summary>On the ground: over tiles and decorations, under every character.</summary>
        public const int GroundOrder = -19000;

        /// <summary>In the air: over everything in the world.</summary>
        public const int TopOrder = 21000;

        /// <summary>Sorted together with characters standing at <paramref name="y"/>.</summary>
        public static int At(float y, int boost = 40) => YSort.OrderFor(y) + boost;

        SpriteRenderer sr;
        float life, age, delay, spin, drag, swingDeg, swingHz;
        Vector3 scaleFrom = Vector3.one, scaleTo = Vector3.one;
        Vector2 velocity;
        bool pop, faceMotion;
        Color color;
        FxFade fade = FxFade.Linear;

        /// <summary>[VFX] The generated picture (Art/FxImg/&lt;img&gt;) when it exists, else the procedural sprite.</summary>
        public static string Pick(string img, string fallback) => Game.Art != null && Game.Art.Optional("FxImg/" + img) != null ? "FxImg/" + img : fallback;

        /// <summary>
        /// [VFX] Cracked ground under a heavy blow, sized to <paramref name="radius"/>: the drawn crack decal (thin
        /// fissures, see-through between them) when it exists, else the old procedural crack kept small. Short-lived so
        /// it reads as the ground splitting, not as a patch over the screen.
        /// </summary>
        public static void Crack(Vector2 ground, float radius, float life)
        {
            bool flip = Random.value < 0.5f;
            if (HasImage("fxi_crack"))
            {
                float k = radius * 0.62f;
                Spawn("FxImg/fxi_crack", ground, Color.white, life * 0.8f, GroundOrder + 2).Scale(new Vector2(k * 0.55f, k * 0.55f), new Vector2(k, k)).Flip(flip).Fade(FxFade.Late);
                return;
            }
            Spawn("fx_crack", ground, new Color(1f, 1f, 1f, 0.85f), life * 0.7f, GroundOrder + 2).Scale(radius * 0.55f, radius * 0.75f).Flip(flip).Fade(FxFade.Late);
        }

        /// <summary>True when the generated picture is there (callers then skip the procedural flipbook it replaces).</summary>
        public static bool HasImage(string img) => Game.Art != null && Game.Art.Optional("FxImg/" + img) != null;

        public static SkillFx Spawn(string sprite, Vector2 position, Color color, float life, int order)
        {
            var go = new GameObject("SkillFx");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = position;
            var fx = go.AddComponent<SkillFx>();
            fx.sr = go.AddComponent<SpriteRenderer>();
            fx.sr.sprite = Game.Art.Get(sprite);
            fx.sr.sortingOrder = order;
            fx.color = color;
            fx.life = Mathf.Max(0.02f, life);
            fx.Apply(0f);
            return fx;
        }

        public SkillFx Additive() { sr.sharedMaterial = FxMaterials.Additive; return this; }

        public SkillFx Scale(float from, float to) => Scale(new Vector2(from, from), new Vector2(to, to));

        public SkillFx Scale(Vector2 from, Vector2 to)
        {
            scaleFrom = new Vector3(from.x, from.y, 1f);
            scaleTo = new Vector3(to.x, to.y, 1f);
            Apply(0f);
            return this;
        }

        /// <summary>Bursts up out of the ground (with a small overshoot), holds, then sinks back. Uses the target scale.</summary>
        public SkillFx Pop() { pop = true; Apply(0f); return this; }

        /// <summary>Degrees per second; negative = clockwise.</summary>
        public SkillFx Spin(float degreesPerSecond) { spin = degreesPerSecond; return this; }

        /// <summary>Rocks back and forth around the pivot (a ringing bell): <paramref name="degrees"/> either side, dying out.</summary>
        public SkillFx Swing(float degrees, float hz) { swingDeg = degrees; swingHz = hz; return this; }

        public SkillFx Rotate(float degrees) { transform.rotation = Quaternion.Euler(0f, 0f, degrees); return this; }

        public SkillFx Move(Vector2 speed, float dragPerSecond = 0f) { velocity = speed; drag = dragPerSecond; return this; }

        /// <summary>Keeps the sprite's +x axis pointing along its motion (sparks, shards).</summary>
        public SkillFx FaceMotion() { faceMotion = true; Face(); return this; }

        public SkillFx Fade(FxFade mode) { fade = mode; Apply(0f); return this; }

        public SkillFx Flip(bool x, bool y = false) { sr.flipX = x; sr.flipY = y; return this; }

        /// <summary>Stays hidden for <paramref name="seconds"/>, then plays.</summary>
        public SkillFx Delay(float seconds) { delay = Mathf.Max(0f, seconds); sr.enabled = delay <= 0f; return this; }

        void Face()
        {
            if (velocity.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age < delay) return;
            if (!sr.enabled) sr.enabled = true;
            float t = (age - delay) / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            if (velocity.sqrMagnitude > 0f)
            {
                transform.position += (Vector3)(velocity * dt);
                if (drag > 0f) velocity *= Mathf.Max(0f, 1f - drag * dt);
                if (faceMotion) Face();
            }
            if (spin != 0f) transform.Rotate(0f, 0f, spin * dt);
            if (swingDeg != 0f) transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(age * swingHz * Mathf.PI * 2f) * swingDeg * (1f - Mathf.Clamp01(age / life)));
            Apply(t);
        }

        void Apply(float t)
        {
            if (pop)
            {
                float k = t < 0.18f ? EaseOutBack(t / 0.18f) : t > 0.72f ? Mathf.Clamp01(1f - (t - 0.72f) / 0.28f) : 1f;
                transform.localScale = new Vector3(scaleTo.x * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(k)), scaleTo.y * k, 1f);
            }
            else
            {
                transform.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, 1f - (1f - t) * (1f - t));
            }

            float a;
            switch (fade)
            {
                case FxFade.Quick: a = (1f - t) * (1f - t); break;
                case FxFade.Late: a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f; break;
                case FxFade.InOut: a = t < 0.15f ? t / 0.15f : t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f; break;
                case FxFade.None: a = 1f; break;
                default: a = 1f - t; break;
            }
            var c = color;
            c.a *= Mathf.Clamp01(a);
            sr.color = c;
        }

        /// <summary>Removes the effect now (a projectile that hit something).</summary>
        public void Kill()
        {
            if (this != null) Destroy(gameObject);
        }

        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float m = x - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }
    }
}
