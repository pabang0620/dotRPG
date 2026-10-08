using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Jagged, flickering lightning between two points: a coloured aura, a bright glow, a white core
    /// and two small forks. The path is re-randomised every few frames so it crackles.
    /// </summary>
    public class LightningFx : MonoBehaviour
    {
        class Strand
        {
            public LineRenderer line;
            public float width;
            public Color color;
            public int path; // 0 = main bolt, 1/2 = forks
        }

        readonly List<Strand> strands = new List<Strand>();
        Vector3[] main, forkA, forkB;
        Vector2 from, to;
        float life, age, nextShape, amplitude, flicker = 1f;
        int segments;

        public static LightningFx Strike(Vector2 from, Vector2 to, Color glow, Color aura, float life = 0.3f, float thickness = 1f)
        {
            var go = new GameObject("Lightning");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<LightningFx>();
            fx.from = from;
            fx.to = to;
            fx.life = Mathf.Max(0.05f, life);
            float len = Vector2.Distance(from, to);
            fx.segments = Mathf.Clamp(Mathf.RoundToInt(len / 0.28f), 3, 18);
            fx.amplitude = Mathf.Clamp(len * 0.12f, 0.06f, 0.38f) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(thickness));
            fx.main = new Vector3[fx.segments + 1];
            fx.forkA = new Vector3[4];
            fx.forkB = new Vector3[4];
            fx.Add(FxMaterials.SoftAlpha, 0.62f * thickness, aura, 0, 0);
            fx.Add(FxMaterials.SoftAdditive, 0.3f * thickness, glow, 0, 1);
            fx.Add(FxMaterials.Alpha, 0.11f * thickness, Color.white, 0, 2);
            if (len > 0.8f)
                for (int f = 1; f <= 2; f++)
                {
                    fx.Add(FxMaterials.SoftAlpha, 0.3f * thickness, aura, f, 0);
                    fx.Add(FxMaterials.Alpha, 0.06f * thickness, Color.white, f, 2);
                }
            fx.Reshape();
            fx.Refresh(0f);
            return fx;
        }

        void Add(Material material, float width, Color color, int path, int orderOffset)
        {
            var line = FxMaterials.NewLine(transform, material, SkillFx.TopOrder + orderOffset);
            strands.Add(new Strand { line = line, width = width, color = color, path = path });
        }

        void Reshape()
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            Vector2 dir = len > 0.0001f ? d / len : Vector2.right;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float off = i == 0 || i == segments ? 0f : Random.Range(-amplitude, amplitude) * Mathf.Lerp(0.45f, 1f, Mathf.Sin(t * Mathf.PI));
                main[i] = from + d * t + perp * off;
            }
            Fork(forkA, dir, len, 1f);
            Fork(forkB, dir, len, -1f);
            flicker = Random.Range(0.7f, 1f);
        }

        void Fork(Vector3[] points, Vector2 dir, float len, float side)
        {
            Vector2 p = main[Random.Range(1, Mathf.Max(2, segments - 1))];
            float ang = side * Random.Range(25f, 55f) * Mathf.Deg2Rad;
            var fdir = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
            var fperp = new Vector2(-fdir.y, fdir.x);
            float step = Mathf.Min(len, 2.5f) * Random.Range(0.1f, 0.16f);
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = p;
                p += fdir * step + fperp * (Random.Range(-step, step) * 0.6f);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            if (age >= nextShape)
            {
                nextShape = age + 0.045f;
                Reshape();
            }
            Refresh(t);
        }

        void Refresh(float t)
        {
            float k = 1f - t;
            foreach (var s in strands)
            {
                var points = s.path == 0 ? main : s.path == 1 ? forkA : forkB;
                s.line.positionCount = points.Length;
                s.line.SetPositions(points);
                s.line.widthMultiplier = s.width * (0.55f + 0.45f * k);
                var c = s.color;
                c.a *= k * flicker;
                s.line.startColor = c;
                s.line.endColor = s.path == 0 ? c : new Color(c.r, c.g, c.b, 0f);
            }
        }
    }
}
