using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Forge sigils, converging light and result rings, on unscaled UI time.</summary>
    public class EnhanceFx : MonoBehaviour
    {
        static EnhanceFx host;
        static EnhanceFx Host
        {
            get
            {
                if (host == null) { host = new GameObject("EnhanceFx").AddComponent<EnhanceFx>(); DontDestroyOnLoad(host.gameObject); }
                return host;
            }
        }
        static bool Visible(RectTransform icon) => icon != null && icon.gameObject.activeInHierarchy;
        static RectTransform Root(RectTransform icon)
        {
            var root = UIFactory.Rect(icon.parent, "ForgeParticles");
            root.anchorMin = icon.anchorMin; root.anchorMax = icon.anchorMax;
            root.pivot = icon.pivot; root.anchoredPosition = icon.anchoredPosition; root.sizeDelta = Vector2.zero;
            return root;
        }
        static Image Piece(RectTransform root, string shape, Color color, float size)
        {
            var p = UIFactory.Image(root, "Forge_" + shape, ArcaneUiArt.Get(shape), color);
            UIFactory.Place(p.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, Vector2.one * size);
            return p;
        }
        static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);
        public static void Charge(RectTransform icon, float seconds) { if (Visible(icon)) Host.StartCoroutine(ChargeRoutine(icon, seconds)); }
        public static void Sparks(RectTransform icon, int count, Color color) { if (Visible(icon)) Host.StartCoroutine(SparkRoutine(icon, count, color, 90, .5f, false)); }
        public static void Burst(RectTransform icon, int count, Color color, bool great)
        {
            if (!Visible(icon)) return;
            Host.StartCoroutine(ResultRings(icon, great ? new Color(.35f, .75f, 1f) : color, great));
            Host.StartCoroutine(SparkRoutine(icon, count, color, great ? 230 : 150, great ? 1.15f : .8f, true));
        }
        public static void Fail(RectTransform icon, bool broken)
        {
            if (!Visible(icon)) return;
            Host.StartCoroutine(ResultRings(icon, new Color(1f, .3f, .2f), false));
            Host.StartCoroutine(Shake(icon, broken ? .45f : .28f, broken ? 8 : 4));
            Host.StartCoroutine(SparkRoutine(icon, broken ? 24 : 9, broken ? new Color(.65f, .75f, .85f) : new Color(1f, .42f, .15f), broken ? 180 : 70, .8f, false));
        }
        static IEnumerator ChargeRoutine(RectTransform icon, float seconds)
        {
            var root = Root(icon);
            root.SetSiblingIndex(icon.GetSiblingIndex());
            var halo = Piece(root, "halo", Color.clear, 220);
            var seal = Piece(root, "sigil", Color.clear, 155);
            var ring = Piece(root, "ring", Color.clear, 110);
            var lights = new Image[10];
            for (int i = 0; i < lights.Length; i++) lights[i] = Piece(root, "star", Color.white, 9);
            try
            {
                for (float t = 0; t < seconds && Visible(icon); t += Time.unscaledDeltaTime)
                {
                    float k = t / seconds;
                    halo.color = new Color(.16f, .5f, 1f, .25f + k * .45f);
                    seal.color = new Color(.4f, .76f, 1f, .3f + .5f * k);
                    seal.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 65);
                    ring.color = new Color(1f, .77f, .35f, .3f + k * .5f);
                    ring.rectTransform.localRotation = Quaternion.Euler(0, 0, -t * 40);
                    for (int i = 0; i < lights.Length; i++)
                    {
                        float f = Mathf.Repeat(k * 1.8f + i / 10f, 1);
                        float a = i * 2.4f + t * 1.4f, r = Mathf.Lerp(110, 24, f);
                        lights[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        lights[i].color = new Color(.7f, .88f, 1f, Mathf.Sin(f * Mathf.PI));
                    }
                    yield return null;
                }
            }
            finally { if (root != null) Destroy(root.gameObject); }
        }
        static IEnumerator ResultRings(RectTransform icon, Color color, bool great)
        {
            var root = Root(icon);
            var halo = Piece(root, "halo", color, 160);
            var ring = Piece(root, "ring", color, 80);
            var seal = Piece(root, "sigil", color, 100);
            var flash = Piece(root, "star", new Color(.85f, .94f, 1f), 90);
            float life = great ? 1.1f : .7f;
            try
            {
                for (float t = 0; t < life && Visible(icon); t += Time.unscaledDeltaTime)
                {
                    float k = t / life, ease = 1 - Mathf.Pow(1 - k, 3);
                    ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(45, great ? 265 : 205, ease);
                    seal.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(75, great ? 195 : 150, ease);
                    seal.rectTransform.localRotation = Quaternion.Euler(0, 0, k * 35);
                    ring.color = Fade(color, (1 - k) * .8f); seal.color = Fade(color, 1 - k);
                    halo.color = Fade(color, (1 - k) * .7f);
                    flash.color = new Color(.85f, .94f, 1f, Mathf.Max(0, 1 - k * 3));
                    yield return null;
                }
            }
            finally { if (root != null) Destroy(root.gameObject); }
        }
        static IEnumerator SparkRoutine(RectTransform icon, int count, Color color, float speed, float life, bool success)
        {
            var root = Root(icon); var parts = new Image[count]; var vel = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                parts[i] = Piece(root, i % 3 == 0 ? "star" : "gem", i % 4 == 0 ? new Color(.4f, .8f, 1f) : color, i % 3 == 0 ? 15 : 7);
                float a = i * Mathf.PI * 2 / count + Random.Range(-.12f, .12f);
                vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(speed * .5f, speed);
            }
            try
            {
                for (float t = 0; t < life && Visible(icon); t += Time.unscaledDeltaTime)
                {
                    float k = t / life;
                    for (int i = 0; i < count; i++)
                    {
                        parts[i].rectTransform.anchoredPosition += vel[i] * Time.unscaledDeltaTime;
                        vel[i] += Vector2.down * (success ? 85 : 260) * Time.unscaledDeltaTime;
                        parts[i].color = Fade(parts[i].color, Mathf.Pow(1 - k, 1.5f));
                        parts[i].rectTransform.localRotation = Quaternion.Euler(0, 0, t * (i % 2 == 0 ? 160 : -160));
                    }
                    yield return null;
                }
            }
            finally { if (root != null) Destroy(root.gameObject); }
        }
        static IEnumerator Shake(RectTransform icon, float seconds, float power)
        {
            var origin = icon.anchoredPosition;
            try
            {
                for (float t = 0; t < seconds && Visible(icon); t += Time.unscaledDeltaTime)
                {
                    float k = 1 - t / seconds;
                    icon.anchoredPosition = origin + new Vector2(Mathf.Sin(t * 95), Mathf.Cos(t * 77) * .5f) * power * k;
                    yield return null;
                }
            }
            finally { if (icon != null) icon.anchoredPosition = origin; }
        }
    }
}
