using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// A sword slash drawn from one point to another that stays where it was cut (천검귀일, Docs/PLAN_SKILL_VFX.md).
    /// The finished slash (a symmetric crescent: needle-thin ends, full in the middle) is revealed by a bright blade
    /// tip running along it, like space opening behind the blade, and stays exactly as drawn. Three layers: a wide soft halo, the blue-to-white slash and a white core.
    /// <see cref="Charge"/> swells and brightens it with a faster pulse, <see cref="Offset"/> moves it (trembling).
    /// </summary>
    public class StrokeFx : MonoBehaviour
    {
        const int Samples = 8;

        LineRenderer halo, slash, core;
        SpriteRenderer tip;
        Vector2 from, to;
        Color color;
        float draw, age, width;
        readonly Gradient slashColors = new Gradient();
        readonly GradientColorKey[] colorKeys = new GradientColorKey[3];
        readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[3];

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
            fx.color = color;
            fx.width = width;
            fx.draw = Mathf.Max(.02f, draw);
            fx.halo = Line(go.transform, order);
            fx.slash = Line(go.transform, order + 1);
            fx.core = Line(go.transform, order + 2);
            fx.tip = new GameObject("Tip").AddComponent<SpriteRenderer>();
            fx.tip.transform.SetParent(go.transform, false);
            fx.tip.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.tip.sharedMaterial = FxMaterials.Additive;
            fx.tip.sortingOrder = order + 3;
            fx.Refresh();
            return fx;
        }

        static LineRenderer Line(Transform parent, int order)
        {
            var line = FxMaterials.NewLine(parent, FxMaterials.SoftAdditive, order);
            line.positionCount = 2;
            return line;
        }

        /// <summary>Removes it at once (the skill was interrupted, or it bursts).</summary>
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
            float k = Mathf.Max(.001f, 1f - (1f - t) * (1f - t) * (1f - t));
            Vector2 a = from + Offset, b = Vector2.Lerp(from, to, k) + Offset;
            // The finished slash already exists along the whole line; the blade only reveals it. So the part drawn so
            // far always has its final shape (needle ends, full in the middle) and nothing changes once it is done.
            var shape = new AnimationCurve();
            for (int i = 0; i <= Samples; i++)
            {
                float u = i / (float)Samples;
                shape.AddKey(u, Crescent(u * k));
            }
            for (int i = 0; i < 3; i++)
            {
                float g = i * .5f;                     // global position: start, middle, end of the whole line
                float u = Mathf.Min(1f, g / k);        // where it sits on the part drawn so far
                float w = Crescent(g);
                colorKeys[i] = new GradientColorKey(Color.Lerp(color, new Color(.92f, .98f, 1f), w), u);
                alphaKeys[i] = new GradientAlphaKey(.65f + .35f * w, u);
            }
            // Charging: up to about 1.7x as thick, brighter, pulsing faster as the burst nears.
            float pulse = Charge > 0f ? .5f + .5f * Mathf.Sin(age * Mathf.Lerp(10f, 40f, Charge)) : 0f;
            float swell = 1f + Charge * (.45f + .25f * pulse);

            foreach (var line in new[] { halo, slash, core })
            {
                line.SetPosition(0, a);
                line.SetPosition(1, b);
                line.widthCurve = shape;
            }
            halo.widthMultiplier = width * 7f * swell;
            halo.startColor = halo.endColor = new Color(color.r, color.g, color.b, .22f + .25f * Charge * (.5f + .5f * pulse));
            slash.widthMultiplier = width * 3.2f * swell;
            slashColors.SetKeys(colorKeys, alphaKeys);
            slash.colorGradient = slashColors;
            core.widthMultiplier = width * .5f * swell;
            core.startColor = core.endColor = Color.white;
            // The blade tip: bright while cutting, gone shortly after the cut is complete.
            float tipA = t < 1f ? 1f : Mathf.Max(0f, 1f - (age - draw) / .15f);
            tip.transform.position = b;
            tip.transform.localScale = Vector3.one * (width * 6f);
            tip.color = new Color(.85f, .95f, 1f, tipA);
        }

        /// <summary>The slash's width along the whole line (0..1): needle-thin at both ends, full in the middle.</summary>
        static float Crescent(float u) => Mathf.Pow(Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI), .6f);
    }
}
