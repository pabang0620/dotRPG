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

        LineRenderer inner, edgeA, edgeB, swoosh;
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

        /// <summary>The slit blows open: the edges fly apart and everything fades over 0.3 s.</summary>
        public void Explode() { if (explodeAt < 0f) explodeAt = age; }

        /// <summary>
        /// The burst along a finished cut: thin splinters of the same line fly off both sides (and a few along it),
        /// so the explosion keeps the drawn line's thickness and length.
        /// </summary>
        public static void Shatter(Vector2 from, Vector2 to, Color color, float width, int count, int order)
        {
            Vector2 d = (to - from).normalized, n = new Vector2(-d.y, d.x);
            for (int i = 0; i < count; i++)
            {
                Vector2 at = Vector2.Lerp(from, to, Random.Range(.05f, .95f));
                float side = i % 2 == 0 ? 1f : -1f;
                Vector2 dir = i % 5 == 4 ? d * (Random.value < .5f ? 1f : -1f) : (n * side + d * Random.Range(-.45f, .45f)).normalized;
                Splinter.Spawn(at, dir, Random.Range(.5f, 1.3f), Random.Range(5f, 10f), color, width * Random.Range(.6f, 1f), Random.Range(.3f, .5f), order);
            }
        }

        /// <summary>One flying splinter: a short thin streak that shoots out, slows down and fades.</summary>
        sealed class Splinter : MonoBehaviour
        {
            LineRenderer line;
            Vector2 at, dir;
            Color color;
            float len, speed, width, life, age;

            public static void Spawn(Vector2 at, Vector2 dir, float len, float speed, Color color, float width, float life, int order)
            {
                var go = new GameObject("Splinter");
                if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
                var s = go.AddComponent<Splinter>();
                s.at = at; s.dir = dir; s.len = len; s.speed = speed; s.color = color; s.width = width; s.life = life;
                s.line = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order);
                s.line.positionCount = 2;
                s.line.widthCurve = new AnimationCurve(new Keyframe(0f, .05f), new Keyframe(.7f, 1f), new Keyframe(1f, .3f));
                s.Refresh();
            }

            void Update()
            {
                age += Time.deltaTime;
                if (age >= life) { Destroy(gameObject); return; }
                at += dir * speed * (1f - age / life) * Time.deltaTime;
                Refresh();
            }

            void Refresh()
            {
                float k = 1f - age / life;
                line.SetPosition(0, at - dir * len * (.4f + .6f * k));
                line.SetPosition(1, at);
                line.widthMultiplier = width;
                line.startColor = new Color(color.r, color.g, color.b, 0f);
                line.endColor = new Color(Mathf.Lerp(color.r, 1f, .6f), Mathf.Lerp(color.g, 1f, .6f), 1f, k);
            }
        }

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
