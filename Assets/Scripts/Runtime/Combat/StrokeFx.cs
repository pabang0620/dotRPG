using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// A laser-like sword cut: a bright blade tip with a swoosh tail runs from one point to another and leaves a glowing
    /// line (white core, blue glow) that stays as an afterimage. <see cref="Charge"/> makes the afterimage swell and
    /// pulse brighter (before it bursts), <see cref="Offset"/> moves it (trembling), <see cref="Kill"/> removes it.
    /// </summary>
    public class StrokeFx : MonoBehaviour
    {
        LineRenderer inner, edgeA, edgeB, swoosh;
        SpriteRenderer tip;
        Vector2 from, to, normal;
        Color color;
        float draw, age, width;

        public Vector2 Offset;
        /// <summary>0..1: the afterimage charges up (thicker, brighter, pulsing faster).</summary>
        public float Charge;

        public static StrokeFx Spawn(Vector2 from, Vector2 to, Color color, float width, float draw, int order)
        {
            var go = new GameObject("Stroke");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<StrokeFx>();
            fx.from = from;
            fx.to = to;
            var d = (to - from).normalized;
            fx.normal = new Vector2(-d.y, d.x);
            fx.color = color;
            fx.width = width;
            fx.draw = Mathf.Max(.02f, draw);
            fx.inner = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order);
            fx.edgeA = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 1);
            fx.edgeB = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 1);
            // Needle-thin ends, full width in the middle.
            var taper = new AnimationCurve(new Keyframe(0f, .02f), new Keyframe(.15f, 1f), new Keyframe(.85f, 1f), new Keyframe(1f, .02f));
            foreach (var line in new[] { fx.inner, fx.edgeA, fx.edgeB }) { line.positionCount = 2; line.widthCurve = taper; }
            // The swoosh: a bright, wide streak right behind the blade tip that thins out along its tail.
            fx.swoosh = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 2);
            fx.swoosh.positionCount = 2;
            fx.swoosh.widthCurve = new AnimationCurve(new Keyframe(0f, .1f), new Keyframe(.8f, 1f), new Keyframe(1f, .6f));
            fx.tip = new GameObject("Tip").AddComponent<SpriteRenderer>();
            fx.tip.transform.SetParent(go.transform, false);
            fx.tip.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.tip.sharedMaterial = FxMaterials.Additive;
            fx.tip.sortingOrder = order + 2;
            fx.Refresh();
            return fx;
        }


        /// <summary>Removes it at once (the skill was interrupted).</summary>
        public void Kill() => Destroy(gameObject);

        void Update()
        {
            age += Time.deltaTime;
            Refresh();
        }

        void Refresh()
        {
            // Ease-out cut: quick, settling as it reaches the end.
            float t = Mathf.Clamp01(age / draw);
            float k = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector2 a = from + Offset, b = Vector2.Lerp(from, to, k) + Offset;
            // Charging: up to twice as thick, pulsing faster and brighter as the burst nears.
            float pulse = Charge > 0f ? .5f + .5f * Mathf.Sin(age * Mathf.Lerp(10f, 40f, Charge)) : 0f;
            float swell = 1f + Charge * (.6f + .4f * pulse), fade = 1f;
            Vector2 half = Vector2.zero;
            edgeA.SetPosition(0, a + half); edgeA.SetPosition(1, b + half);
            edgeB.SetPosition(0, a - half); edgeB.SetPosition(1, b - half);
            inner.SetPosition(0, a); inner.SetPosition(1, b);
            // Vivid from the first frame: a solid white core with a strong blue glow.
            edgeA.widthMultiplier = edgeB.widthMultiplier = width * .4f * swell;
            inner.widthMultiplier = width * swell * (1f + Charge * .8f);
            var edge = new Color(1f, 1f, 1f, fade);
            edgeA.startColor = edgeA.endColor = edgeB.startColor = edgeB.endColor = edge;
            var glowColor = Color.Lerp(color, new Color(.85f, .97f, 1f, 1f), Charge * pulse * .7f);
            inner.startColor = inner.endColor = new Color(glowColor.r, glowColor.g, glowColor.b, color.a * fade);
            // The blade tip and its swoosh: bright while cutting, gone shortly after the cut is complete.
            float tipA = t < 1f ? 1f : Mathf.Max(0f, 1f - (age - draw) / .15f);
            Vector2 dir = (to - from).normalized;
            float tail = Mathf.Min((b - a).magnitude, (to - from).magnitude * .35f);
            swoosh.SetPosition(0, b - dir * tail);
            swoosh.SetPosition(1, b);
            swoosh.widthMultiplier = width * 3.2f;
            swoosh.startColor = new Color(color.r, color.g, color.b, 0f);
            swoosh.endColor = new Color(.95f, .99f, 1f, tipA * fade);
            tip.transform.position = b;
            tip.transform.localScale = Vector3.one * (width * 6f);
            tip.color = new Color(.85f, .95f, 1f, tipA * fade);
        }
    }
}
