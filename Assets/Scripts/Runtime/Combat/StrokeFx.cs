using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// A sword cut that splits the air: a bright blade tip runs from one point to another and the line it leaves opens
    /// into a slit (two hair-thin edges with light pouring out between them). <see cref="Split"/> sets how wide the slit
    /// is, <see cref="Offset"/> moves it (trembling), <see cref="Explode"/> blows the two edges apart and fades it out.
    /// </summary>
    public class StrokeFx : MonoBehaviour
    {
        const float ExplodeTime = .3f;

        LineRenderer inner, edgeA, edgeB;
        SpriteRenderer tip;
        Vector2 from, to, normal;
        Color color;
        float draw, age, width, explodeAt = -1f;

        public Vector2 Offset;
        /// <summary>Slit width in world units (0 = a closed line).</summary>
        public float Split;

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
            fx.tip = new GameObject("Tip").AddComponent<SpriteRenderer>();
            fx.tip.transform.SetParent(go.transform, false);
            fx.tip.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.tip.sharedMaterial = FxMaterials.Additive;
            fx.tip.sortingOrder = order + 2;
            fx.Refresh();
            return fx;
        }

        /// <summary>The slit blows open: the edges fly apart and everything fades over 0.3 s.</summary>
        public void Explode() { if (explodeAt < 0f) explodeAt = age; }

        /// <summary>Removes it at once (the skill was interrupted).</summary>
        public void Kill() => Destroy(gameObject);

        void Update()
        {
            age += Time.deltaTime;
            if (explodeAt >= 0f && age - explodeAt >= ExplodeTime) { Destroy(gameObject); return; }
            Refresh();
        }

        void Refresh()
        {
            // Ease-out cut: quick, settling as it reaches the end.
            float t = Mathf.Clamp01(age / draw);
            float k = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector2 a = from + Offset, b = Vector2.Lerp(from, to, k) + Offset;
            float split = Split, fade = 1f, flare = 1f;
            if (explodeAt >= 0f)
            {
                float e = (age - explodeAt) / ExplodeTime;
                split += e * e * 1.6f;          // the halves fly apart
                fade = 1f - e;
                flare = 1f + 2f * (1f - e);     // light bursts out of the opening
            }
            Vector2 half = normal * (split * .5f);
            edgeA.SetPosition(0, a + half); edgeA.SetPosition(1, b + half);
            edgeB.SetPosition(0, a - half); edgeB.SetPosition(1, b - half);
            inner.SetPosition(0, a); inner.SetPosition(1, b);
            // Vivid from the first frame: a solid white core with a strong blue glow (thin, not faint).
            edgeA.widthMultiplier = edgeB.widthMultiplier = width * .4f;
            inner.widthMultiplier = width + split * 1.2f;
            var edge = new Color(1f, 1f, 1f, fade);
            edgeA.startColor = edgeA.endColor = edgeB.startColor = edgeB.endColor = edge;
            inner.startColor = inner.endColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * flare) * fade);
            // The blade tip: bright while cutting, gone once the cut is complete.
            float tipA = t < 1f ? 1f : Mathf.Max(0f, 1f - (age - draw) / .12f);
            tip.transform.position = b;
            tip.transform.localScale = Vector3.one * (width * 9f);
            tip.color = new Color(.85f, .95f, 1f, tipA * fade);
        }
    }
}
