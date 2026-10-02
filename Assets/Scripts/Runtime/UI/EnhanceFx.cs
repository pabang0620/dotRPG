using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [A] Enhancement presentation on the blacksmith window: a charge glow that grows while the hammer falls,
    /// a burst of sparks on success (bigger for +10 and up), a red flash and shake on failure, and the item
    /// shattering into shards when it breaks. Plain UI images on unscaled time (windows pause the game).
    /// </summary>
    public class EnhanceFx : MonoBehaviour
    {
        static EnhanceFx host;

        static EnhanceFx Host(RectTransform target)
        {
            if (host == null)
            {
                host = new GameObject("EnhanceFx").AddComponent<EnhanceFx>();
                DontDestroyOnLoad(host.gameObject);
            }
            return host;
        }

        static Image Piece(RectTransform parent, string sprite, Color color, Vector2 size)
        {
            var img = UIFactory.Image(parent.parent != null ? parent.parent : parent, "Fx", Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = parent.anchorMin;
            img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            img.rectTransform.anchoredPosition = parent.anchoredPosition;
            img.rectTransform.sizeDelta = size;
            return img;
        }

        public static void Charge(RectTransform icon, float seconds) => Host(icon).StartCoroutine(ChargeRoutine(icon, seconds));

        static IEnumerator ChargeRoutine(RectTransform icon, float seconds)
        {
            var glow = Piece(icon, "ui_circle", new Color(1f, 0.85f, 0.4f, 0f), new Vector2(60f, 60f));
            glow.transform.SetSiblingIndex(icon.GetSiblingIndex());
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = t / seconds;
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 18f);
                glow.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(70f, 170f, k);
                glow.color = new Color(1f, 0.85f, 0.4f, Mathf.Lerp(0.15f, 0.55f, k) * (0.7f + 0.3f * pulse));
                yield return null;
            }
            Destroy(glow.gameObject);
        }

        public static void Sparks(RectTransform icon, int count, Color color) => Host(icon).StartCoroutine(SparkRoutine(icon, count, color, 90f, 0.45f, 8f));

        public static void Burst(RectTransform icon, int count, Color color, bool great)
        {
            var h = Host(icon);
            h.StartCoroutine(Flash(icon, new Color(1f, 0.95f, 0.7f, 0.85f), great ? 0.5f : 0.3f, great ? 260f : 180f));
            h.StartCoroutine(SparkRoutine(icon, count, color, great ? 260f : 170f, great ? 1.1f : 0.7f, great ? 12f : 9f));
            if (great) h.StartCoroutine(SparkRoutine(icon, count / 2, new Color32(120, 220, 255, 255), 200f, 1.2f, 10f));
        }

        public static void Fail(RectTransform icon, bool broken)
        {
            var h = Host(icon);
            h.StartCoroutine(Flash(icon, new Color(1f, 0.2f, 0.15f, 0.7f), 0.35f, 170f));
            h.StartCoroutine(Shake(icon, broken ? 0.5f : 0.3f, broken ? 9f : 5f));
            if (broken) h.StartCoroutine(Shatter(icon));
        }

        static IEnumerator Flash(RectTransform icon, Color color, float seconds, float size)
        {
            var f = Piece(icon, "ui_circle", color, Vector2.one * 40f);
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = t / seconds;
                f.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(60f, size, k);
                f.color = new Color(color.r, color.g, color.b, color.a * (1f - k));
                yield return null;
            }
            Destroy(f.gameObject);
        }

        static IEnumerator SparkRoutine(RectTransform icon, int count, Color color, float speed, float life, float size)
        {
            var parts = new Image[count];
            var vel = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                parts[i] = Piece(icon, "ui_white", color, Vector2.one * Random.Range(size * 0.5f, size));
                float a = Random.value * Mathf.PI * 2f;
                vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(speed * 0.4f, speed);
            }
            for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
            {
                float k = t / life;
                for (int i = 0; i < count; i++)
                {
                    parts[i].rectTransform.anchoredPosition += vel[i] * Time.unscaledDeltaTime;
                    vel[i] += Vector2.down * 220f * Time.unscaledDeltaTime;
                    var c = parts[i].color;
                    parts[i].color = new Color(c.r, c.g, c.b, 1f - k);
                }
                yield return null;
            }
            foreach (var p in parts) Destroy(p.gameObject);
        }

        static IEnumerator Shake(RectTransform icon, float seconds, float power)
        {
            var origin = Vector2.zero;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = 1f - t / seconds;
                icon.anchoredPosition = origin + new Vector2(Random.Range(-power, power), Random.Range(-power, power)) * k;
                yield return null;
            }
            icon.anchoredPosition = origin;
        }

        static IEnumerator Shatter(RectTransform icon)
        {
            var img = icon.GetComponent<Image>();
            if (img == null || img.sprite == null) yield break;
            const int N = 4;
            var shards = new Image[N * N];
            var vel = new Vector2[N * N];
            float cell = icon.sizeDelta.x / N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    // Grey metal shards fly out where the item was.
                    var s = Piece(icon, "ui_white", new Color(0.75f, 0.75f, 0.8f, 1f), Vector2.one * cell);
                    s.rectTransform.anchoredPosition = icon.anchoredPosition + new Vector2((x + 0.5f - N / 2f) * cell, (y + 0.5f - N / 2f) * cell);
                    var dir = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).normalized;
                    vel[i] = dir * Random.Range(120f, 260f) + Vector2.up * 80f;
                    shards[i] = s;
                }
            img.enabled = false;
            for (float t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.9f;
                for (int i = 0; i < shards.Length; i++)
                {
                    shards[i].rectTransform.anchoredPosition += vel[i] * Time.unscaledDeltaTime;
                    vel[i] += Vector2.down * 520f * Time.unscaledDeltaTime;
                    shards[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 360f * (i % 2 == 0 ? 1f : -1f));
                    shards[i].color = new Color(0.75f, 0.75f, 0.8f, 1f - k);
                }
                yield return null;
            }
            foreach (var s in shards) Destroy(s.gameObject);
        }
    }
}
