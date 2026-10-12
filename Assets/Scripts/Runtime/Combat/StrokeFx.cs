using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// A sword slash drawn from one point to another that stays where it was cut (천검귀일, Docs/PLAN_SKILL_VFX.md).
    /// While cutting, a bright blade tip runs ahead and the slash grows behind it, thickest just behind the tip; once
    /// the cut is complete the same slash settles into a symmetric crescent (needle-thin ends, full in the middle)
    /// and stays as the afterimage. Three layers: a wide soft halo, the blue-to-white slash and a white core.
    /// <see cref="Charge"/> swells and brightens it with a faster pulse, <see cref="Offset"/> moves it (trembling).
    /// </summary>
    public class StrokeFx : MonoBehaviour
    {
        const float Settle = .2f;

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
            float k = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector2 a = from + Offset, b = Vector2.Lerp(from, to, k) + Offset;
            // 0 while cutting (peak just behind the tip, blunt tip end), 1 once settled (symmetric crescent).
            float settled = Mathf.Clamp01((age - draw) / Settle);
            float peak = Mathf.Lerp(.72f, .5f, settled), tipEnd = Mathf.Lerp(.45f, 0f, settled);
            var shape = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(peak, 1f), new Keyframe(1f, tipEnd));
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
            // Blue at the ends, white where it is thickest (toward the tip while cutting, the middle once settled).
            var white = new Color(.92f, .98f, 1f);
            colorKeys[0] = new GradientColorKey(color, 0f);
            colorKeys[1] = new GradientColorKey(white, peak);
            colorKeys[2] = new GradientColorKey(Color.Lerp(white, color, settled), 1f);
            alphaKeys[0] = new GradientAlphaKey(.65f, 0f);
            alphaKeys[1] = new GradientAlphaKey(1f, peak);
            alphaKeys[2] = new GradientAlphaKey(Mathf.Lerp(1f, .65f, settled), 1f);
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
    }
}
