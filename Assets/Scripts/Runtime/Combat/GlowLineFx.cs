using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>A glowing line that fades out: the molten fissure a ground slam leaves behind.</summary>
    public class GlowLineFx : MonoBehaviour
    {
        LineRenderer glow, core;
        Color color;
        float life, age, width;

        public static void Spawn(Vector2 from, Vector2 to, Color color, float width, float life, int order)
        {
            var go = new GameObject("GlowLine");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<GlowLineFx>();
            fx.color = color;
            fx.width = width;
            fx.life = Mathf.Max(0.05f, life);
            fx.glow = FxMaterials.NewLine(go.transform, FxMaterials.SoftAlpha, order);
            fx.core = FxMaterials.NewLine(go.transform, FxMaterials.SoftAdditive, order + 1);
            foreach (var line in new[] { fx.glow, fx.core })
            {
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }
            fx.Refresh(0f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            Refresh(t);
        }

        void Refresh(float t)
        {
            float k = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
            glow.widthMultiplier = width;
            core.widthMultiplier = width * 0.45f;
            var g = color;
            g.a *= k;
            glow.startColor = glow.endColor = g;
            var c = new Color(1f, 0.95f, 0.75f, color.a * k);
            core.startColor = core.endColor = c;
        }
    }
}
