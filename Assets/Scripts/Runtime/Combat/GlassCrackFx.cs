using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] 천검귀일: the view cracks like a struck pane of glass around the cross just before it breaks
    /// (Docs/PLAN_SKILL_VFX.md). Angular cracks run out from the centre, web rings join them, short cracks split off
    /// both slash lines; they spread in a moment, hold while it shakes, then fade as the shards fly (<see cref="Fade"/>).
    /// Crisp white cores (normal blending, readable on any ground) with a soft blue glow.
    /// </summary>
    public class GlassCrackFx : MonoBehaviour
    {
        const float GrowTime = .18f, FadeTime = .45f;
        const float CoreWidth = .05f, GlowWidth = .16f;

        sealed class Crack { public Vector2[] points; public float delay; public LineRenderer core, glow; }

        readonly List<Crack> cracks = new List<Crack>();
        Color color;
        float age, fadeAt = -1f;

        /// <param name="arm">Half the length of the slash lines: the cracks stay inside the cross.</param>
        public static GlassCrackFx Spawn(Vector2 center, float arm, Color color, int order)
        {
            var go = new GameObject("GlassCrack");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<GlassCrackFx>();
            fx.color = color;
            // Radial cracks: angular polylines from the centre, angles jittered around the circle.
            const int Rays = 12;
            var rays = new List<Vector2[]>();
            for (int r = 0; r < Rays; r++)
            {
                float angle = (r + Random.Range(-.3f, .3f)) * Mathf.PI * 2f / Rays;
                var path = Jagged(center, angle, arm * Random.Range(.65f, 1f), 5);
                rays.Add(path);
                fx.Add(go.transform, path, Random.Range(0f, .05f), order);
            }
            // Web rings: join neighbouring rays at two distances, like a struck screen.
            foreach (float ring in new[] { .3f, .6f })
                for (int r = 0; r < Rays; r++)
                {
                    if (Random.value < .25f) continue; // a few gaps keep it irregular
                    Vector2 p = Along(rays[r], ring), q = Along(rays[(r + 1) % Rays], ring * Random.Range(.9f, 1.1f));
                    Vector2 mid = (p + q) * .5f + (p - center).normalized * Random.Range(-.08f, .08f) * arm;
                    fx.Add(go.transform, new[] { p, mid, q }, .06f + ring * .1f, order);
                }
            // Short cracks splitting off both slash lines.
            foreach (var dir in new[] { Vector2.right, Vector2.up })
                for (int i = 0; i < 8; i++)
                {
                    Vector2 at = center + dir * Random.Range(-arm, arm) * .95f;
                    Vector2 side = new Vector2(-dir.y, dir.x) * (i % 2 == 0 ? 1f : -1f);
                    float angle = Mathf.Atan2(side.y, side.x) + Random.Range(-.6f, .6f);
                    fx.Add(go.transform, Jagged(at, angle, arm * Random.Range(.12f, .28f), 3), Random.Range(.02f, .1f), order);
                }
            fx.Refresh();
            return fx;
        }

        /// <summary>An angular crack: straight segments that turn a little at each joint.</summary>
        static Vector2[] Jagged(Vector2 from, float angle, float length, int segments)
        {
            var points = new Vector2[segments + 1];
            points[0] = from;
            for (int i = 1; i <= segments; i++)
            {
                angle += Random.Range(-.35f, .35f);
                points[i] = points[i - 1] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (length / segments);
            }
            return points;
        }

        /// <summary>The point at fraction t of a polyline's points (by index, good enough for evenly spaced cracks).</summary>
        static Vector2 Along(Vector2[] path, float t)
        {
            float f = t * (path.Length - 1);
            int i = Mathf.Min(path.Length - 2, Mathf.FloorToInt(f));
            return Vector2.Lerp(path[i], path[i + 1], f - i);
        }

        void Add(Transform parent, Vector2[] points, float delay, int order)
        {
            var c = new Crack { points = points, delay = delay };
            c.glow = FxMaterials.NewLine(parent, FxMaterials.SoftAdditive, order);
            c.core = FxMaterials.NewLine(parent, FxMaterials.Alpha, order + 1);
            var taper = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, .35f));
            c.glow.widthCurve = c.core.widthCurve = taper;
            cracks.Add(c);
        }

        /// <summary>The pane is broken: the cracks fade out over 0.45 s and the object removes itself.</summary>
        public void Fade() { if (fadeAt < 0f) fadeAt = age; }

        void Update()
        {
            age += Time.deltaTime;
            if (fadeAt >= 0f && age - fadeAt >= FadeTime) { Destroy(gameObject); return; }
            Refresh();
        }

        void Refresh()
        {
            float fade = fadeAt >= 0f ? 1f - (age - fadeAt) / FadeTime : 1f;
            foreach (var c in cracks)
            {
                // Grow point by point (each crack after its own small delay).
                float grow = Mathf.Clamp01((age - c.delay) / GrowTime) * (c.points.Length - 1);
                int full = Mathf.FloorToInt(grow);
                int count = grow <= 0f ? 0 : Mathf.Min(c.points.Length, full + 2);
                foreach (var line in new[] { c.core, c.glow })
                {
                    line.positionCount = count;
                    for (int k = 0; k < count; k++)
                        line.SetPosition(k, k <= full ? c.points[k] : Vector2.Lerp(c.points[k - 1], c.points[k], grow - full));
                }
                c.core.widthMultiplier = CoreWidth;
                c.core.startColor = c.core.endColor = new Color(1f, 1f, 1f, .95f * fade);
                c.glow.widthMultiplier = GlowWidth;
                c.glow.startColor = c.glow.endColor = new Color(color.r, color.g, color.b, .55f * fade);
            }
        }
    }
}
