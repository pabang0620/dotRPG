using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Small pixel health bar above a monster. The coloured fill drops instantly on a hit and a pale
    /// "damage chunk" behind it drains a moment later, so the size of each hit is easy to read.
    /// Drawn above the whole world (not y-sorted) so trees and houses never hide it.
    /// </summary>
    public class EnemyHealthBar : MonoBehaviour
    {
        const int SortingOrder = 20000;
        static readonly Color High = new Color32(99, 199, 77, 255);
        static readonly Color Mid = new Color32(255, 211, 74, 255);
        static readonly Color Low = new Color32(228, 59, 68, 255);
        static readonly Color Chunk = new Color32(255, 240, 200, 255);

        Health health;
        Transform root;
        SpriteRenderer bg, chunk, fill;
        float shownRatio = 1f;   // coloured fill
        float chunkRatio = 1f;   // trailing pale part
        float chunkHoldUntil;

        public void Setup(Health target, float height)
        {
            health = target;
            root = new GameObject("HealthBar").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, height, 0f);
            bg = Part("Bg", "hpbar_bg", Color.white, 0, Vector3.zero);
            // Fill sprites have a left pivot; the bg is 16px wide with a 1px frame.
            var left = new Vector3(-7f / 16f, 0f, 0f);
            chunk = Part("Chunk", "hpbar_fill", Chunk, 1, left);
            fill = Part("Fill", "hpbar_fill", High, 2, left);
            health.Changed += OnChanged;
            OnChanged(health.Current, health.Max);
            chunkRatio = shownRatio;
            Apply();
        }

        SpriteRenderer Part(string name, string sprite, Color color, int order, Vector3 localPos)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            sr.transform.localPosition = localPos;
            sr.sprite = Game.Art.Get(sprite);
            sr.color = color;
            sr.sortingOrder = SortingOrder + order;
            HdMaterial.Apply(sr);
            return sr;
        }

        void OnDestroy()
        {
            if (health != null) health.Changed -= OnChanged;
        }

        void OnChanged(int current, int max)
        {
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            if (ratio < shownRatio) chunkHoldUntil = Time.time + 0.35f;
            shownRatio = ratio;
            if (chunkRatio < ratio) chunkRatio = ratio;
            root.gameObject.SetActive(current > 0);
        }

        void LateUpdate()
        {
            if (root == null || !root.gameObject.activeSelf) return;
            if (Time.time >= chunkHoldUntil && chunkRatio > shownRatio)
                chunkRatio = Mathf.MoveTowards(chunkRatio, shownRatio, Time.deltaTime * 1.5f);
            Apply();
        }

        void Apply()
        {
            fill.transform.localScale = new Vector3(shownRatio, 1f, 1f);
            chunk.transform.localScale = new Vector3(chunkRatio, 1f, 1f);
            fill.color = shownRatio > 0.5f ? Color.Lerp(Mid, High, (shownRatio - 0.5f) * 2f) : Color.Lerp(Low, Mid, shownRatio * 2f);
            fill.enabled = shownRatio > 0f;
            chunk.enabled = chunkRatio > 0f;
        }
    }

    /// <summary>Pixel-digit number that pops up where a monster was hit, floats up and fades out.</summary>
    public class DamageNumber : MonoBehaviour
    {
        const float Lifetime = 1.0f;
        const float DigitSpacing = 4f / 16f; // 3px digit + 1px gap
        /// <summary>
        /// Health points are tiny (a skeleton has 3), so numbers are shown ×10 ("10" instead of "1").
        /// Purely cosmetic — the real damage values and the balance are unchanged.
        /// </summary>
        public const int DisplayScale = 1;

        SpriteRenderer[] digits;
        Vector3 start;
        Vector2 drift;
        float age;

        public static void Show(Vector2 position, int amount, bool big = false)
        {
            if (Game.Art == null || amount <= 0) return;
            var go = new GameObject("DamageNumber");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var number = go.AddComponent<DamageNumber>();
            number.start = position + new Vector2(Random.Range(-0.12f, 0.12f), 0f);
            number.drift = new Vector2(Random.Range(-0.25f, 0.25f), 1.1f);
            go.transform.position = number.start;

            string text = (amount * DisplayScale).ToString();
            number.digits = new SpriteRenderer[text.Length];
            float width = (text.Length - 1) * DigitSpacing;
            var color = big ? new Color32(255, 120, 80, 255) : new Color32(255, 214, 64, 255);
            for (int i = 0; i < text.Length; i++)
            {
                var sr = new GameObject("d" + text[i]).AddComponent<SpriteRenderer>();
                sr.transform.SetParent(go.transform, false);
                sr.transform.localPosition = new Vector3(i * DigitSpacing - width * 0.5f, 0f, 0f);
                sr.sprite = Game.Art.Get("num_" + text[i]);
                sr.color = color;
                sr.sortingOrder = 20010;
                HdMaterial.Apply(sr);
                number.digits[i] = sr;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            // Quick pop, then float upwards while slowing down.
            float rise = 1f - (1f - t) * (1f - t);
            transform.position = start + (Vector3)(drift * rise * 0.6f);
            float scale = t < 0.12f ? Mathf.Lerp(1.2f, 2.8f, t / 0.12f) : Mathf.Lerp(2.8f, 2.2f, Mathf.Clamp01((t - 0.12f) / 0.2f));
            transform.localScale = Vector3.one * scale;
            float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            foreach (var sr in digits)
            {
                var c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
        }
    }
}
