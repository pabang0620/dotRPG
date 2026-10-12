using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// A hair-thin sword stroke drawn smoothly from one point to another: the line grows every frame behind a bright
    /// blade tip, then stays until <see cref="Burst"/> or its life ends. Needle-thin ends, glow plus a white core.
    /// <see cref="Offset"/> moves the whole stroke (used to make the finished cross tremble).
    /// </summary>
    public class StrokeFx : MonoBehaviour
    {
        LineRenderer glow, core;
        SpriteRenderer tip;
        Vector2 from, to;
        Color color;
        float draw, life, age, width;

        public Vector2 Offset;

        public static StrokeFx Spawn(Vector2 from, Vector2 to, Color color, float width, float draw, float life, int order)
        {
            var go = new GameObject("Stroke");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<StrokeFx>();
            fx.from = from;
            fx.to = to;
            fx.color = color;
            fx.width = width;
            fx.draw = Mathf.Max(.02f, draw);
            fx.life = Mathf.Max(fx.draw, life);
            fx.glow = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order);
            fx.core = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 1);
            var taper = new AnimationCurve(new Keyframe(0f, .05f), new Keyframe(.12f, 1f), new Keyframe(.88f, 1f), new Keyframe(1f, .05f));
            foreach (var line in new[] { fx.glow, fx.core }) { line.positionCount = 2; line.widthCurve = taper; }
            fx.tip = new GameObject("Tip").AddComponent<SpriteRenderer>();
            fx.tip.transform.SetParent(go.transform, false);
            fx.tip.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.tip.sharedMaterial = FxMaterials.Additive;
            fx.tip.sortingOrder = order + 2;
            fx.Refresh();
            return fx;
        }

        /// <summary>Ends the stroke now (the burst art takes over).</summary>
        public void Burst() => Destroy(gameObject);

        void Update()
        {
            age += Time.deltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            Refresh();
        }

        void Refresh()
        {
            // Ease-out: a quick cut that settles as it reaches the end.
            float t = Mathf.Clamp01(age / draw);
            float k = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector2 a = from + Offset, b = Vector2.Lerp(from, to, k) + Offset;
            float fade = age > life - .15f ? (life - age) / .15f : 1f;
            glow.SetPosition(0, a); glow.SetPosition(1, b);
            core.SetPosition(0, a); core.SetPosition(1, b);
            glow.widthMultiplier = width;
            core.widthMultiplier = width * .35f;
            glow.startColor = glow.endColor = new Color(color.r, color.g, color.b, color.a * .8f * fade);
            core.startColor = core.endColor = new Color(1f, 1f, 1f, fade);
            // The blade tip: bright while cutting, gone once the stroke is complete.
            float tipA = t < 1f ? 1f : Mathf.Max(0f, 1f - (age - draw) / .12f);
            tip.transform.position = b;
            tip.transform.localScale = Vector3.one * (width * 9f);
            tip.color = new Color(.85f, .95f, 1f, tipA);
        }
    }
}
