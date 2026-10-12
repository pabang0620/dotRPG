using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] A comet of the mage's 유성우 (Docs/PLAN_SKILL_VFX.md): a bright head with a long tail falling diagonally
    /// from the sky onto a point, drawn in code like the fighter's slash (white core, element-coloured glow, needle-thin
    /// tail end). Calls <c>landed</c> on impact and removes itself after a short afterglow.
    /// </summary>
    public class CometFx : MonoBehaviour
    {
        const int Points = 12;

        LineRenderer glow, core;
        SpriteRenderer head;
        Vector2 from, to;
        Color color;
        float fall, age, width, tail;
        Action landed;
        bool hit;

        /// <param name="size">1 = a normal comet; the finale's big ones are about 2.</param>
        public static CometFx Spawn(Vector2 target, Color color, float fall, float size, int order, Action landed)
        {
            var go = new GameObject("Comet");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<CometFx>();
            // Every comet comes in from the upper left, steeply, a little different each time.
            fx.from = target + new Vector2(-2.6f + UnityEngine.Random.Range(-.6f, .6f), 7f + UnityEngine.Random.Range(-.8f, .8f)) * Mathf.Lerp(1f, 1.3f, size - 1f);
            fx.to = target;
            fx.color = color;
            fx.fall = Mathf.Max(.05f, fall);
            fx.width = .16f * size;
            fx.tail = 2.6f * size;
            fx.landed = landed;
            fx.glow = Line(go.transform, FxMaterials.SoftAdditive, order);
            fx.core = Line(go.transform, FxMaterials.SoftAdditive, order + 1);
            var shape = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.85f, .8f), new Keyframe(1f, 1f)); // thin tail, full head
            fx.glow.widthCurve = fx.core.widthCurve = shape;
            fx.head = new GameObject("Head").AddComponent<SpriteRenderer>();
            fx.head.transform.SetParent(go.transform, false);
            fx.head.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.head.sharedMaterial = FxMaterials.Additive;
            fx.head.sortingOrder = order + 2;
            fx.Refresh();
            return fx;
        }

        static LineRenderer Line(Transform parent, Material material, int order)
        {
            var line = FxMaterials.NewLine(parent, material, order);
            line.positionCount = Points;
            return line;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (!hit && age >= fall)
            {
                hit = true;
                landed?.Invoke();
            }
            if (age >= fall + .18f) { Destroy(gameObject); return; }
            Refresh();
        }

        void Refresh()
        {
            // Accelerating fall; after the hit the tail drains into the ground for a moment.
            float t = Mathf.Clamp01(age / fall), k = t * t;
            Vector2 dir = (to - from).normalized;
            Vector2 headAt = Vector2.Lerp(from, to, k);
            float drain = age > fall ? Mathf.Clamp01((age - fall) / .18f) : 0f;
            float len = Mathf.Min(tail * (1f - drain), (headAt - from).magnitude);
            for (int i = 0; i < Points; i++)
            {
                Vector2 p = headAt - dir * len * (1f - i / (Points - 1f));
                glow.SetPosition(i, p);
                core.SetPosition(i, p);
            }
            float fade = 1f - drain;
            glow.widthMultiplier = width * 3f;
            glow.startColor = new Color(color.r, color.g, color.b, 0f);
            glow.endColor = new Color(color.r, color.g, color.b, .85f * fade);
            core.widthMultiplier = width * .8f;
            core.startColor = new Color(1f, 1f, 1f, 0f);
            core.endColor = new Color(1f, 1f, 1f, fade);
            head.transform.position = headAt;
            head.transform.localScale = Vector3.one * width * 7f;
            head.color = new Color(Mathf.Lerp(color.r, 1f, .5f), Mathf.Lerp(color.g, 1f, .5f), Mathf.Lerp(color.b, 1f, .5f), fade);
        }
    }
}
