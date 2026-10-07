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
        /// Purely cosmetic - the real damage values and the balance are unchanged.
        /// </summary>
        public const int DisplayScale = 1;

        SpriteRenderer[] digits, rims;
        Vector3 start;
        Vector2 drift;
        float age;

        /// <summary>Companion hits are drawn white so the player can tell their own numbers apart.</summary>
        static readonly Color32 CompanionColor = new Color32(255, 255, 255, 235);

        public static void Show(Vector2 position, int amount, bool big = false) => Show(position, amount, big, false);

        // [P5] Reused numbers: every hit used to build a GameObject per digit plus strings.
        const int MaxDigits = 7;
        static readonly System.Collections.Generic.Stack<DamageNumber> Pool = new System.Collections.Generic.Stack<DamageNumber>();
        static readonly string[] DigitKeys = { "num_0", "num_1", "num_2", "num_3", "num_4", "num_5", "num_6", "num_7", "num_8", "num_9" };
        int shown;

        public static void Show(Vector2 position, int amount, bool big, bool companion)
        {
            if (Game.Art == null || amount <= 0) return;
            DamageNumber number = null;
            while (Pool.Count > 0 && number == null) number = Pool.Pop(); // entries die with the old map's root
            if (number == null)
            {
                var go = new GameObject("DamageNumber");
                number = go.AddComponent<DamageNumber>();
                number.digits = new SpriteRenderer[MaxDigits];
                number.rims = new SpriteRenderer[MaxDigits];
                for (int i = 0; i < MaxDigits; i++)
                {
                    var sr = new GameObject("d").AddComponent<SpriteRenderer>();
                    sr.transform.SetParent(go.transform, false);
                    sr.sortingOrder = 20010;
                    HdMaterial.Apply(sr);
                    number.digits[i] = sr;
                    // [FEEL] A black rim one pixel wide behind each digit so numbers read on any ground.
                    var rim = new GameObject("r").AddComponent<SpriteRenderer>();
                    rim.transform.SetParent(sr.transform, false);
                    rim.sortingOrder = 20009;
                    HdMaterial.Apply(rim);
                    number.rims[i] = rim;
                }
            }
            if (Fx.Root != null && number.transform.parent != Fx.Root) number.transform.SetParent(Fx.Root, false);
            number.gameObject.SetActive(true);
            number.age = 0f;
            // [FEEL] Hits landing on the same spot within a moment stack upward instead of piling on one another.
            var cell = new Vector2Int(Mathf.RoundToInt(position.x * 2f), Mathf.RoundToInt(position.y * 2f));
            int stack = 0;
            if (Stacks.TryGetValue(cell, out var last) && Time.time - last.at < 0.45f) stack = Mathf.Min(last.count + 1, 5);
            Stacks[cell] = (Time.time, stack);
            if (Stacks.Count > 256) Stacks.Clear();
            number.big = big;
            number.start = position + new Vector2(Random.Range(-0.08f, 0.08f), stack * 0.34f);
            number.drift = new Vector2(Random.Range(-0.1f, 0.1f), 0.55f);
            number.transform.position = number.start;
            number.transform.localScale = Vector3.one;

            int value = amount * DisplayScale, count = 0;
            for (int v = value; v > 0 && count < MaxDigits; v /= 10) count++;
            number.shown = count;
            float width = (count - 1) * DigitSpacing;
            var color = companion ? CompanionColor : big ? new Color32(255, 120, 80, 255) : new Color32(255, 214, 64, 255);
            for (int i = 0; i < MaxDigits; i++)
            {
                var sr = number.digits[i];
                bool used = i < count;
                sr.enabled = used;
                if (!used) continue;
                int digit = value / Pow10(count - 1 - i) % 10;
                sr.transform.localPosition = new Vector3(i * DigitSpacing - width * 0.5f, 0f, 0f);
                sr.sprite = Game.Art.Get(DigitKeys[digit]);
                sr.color = color;
                var rim = number.rims[i];
                rim.sprite = Game.Art.GetOutline(sr.sprite);
                rim.enabled = rim.sprite != null;
                rim.color = new Color(0.05f, 0.04f, 0.08f, 1f);
            }
        }

        static readonly System.Collections.Generic.Dictionary<Vector2Int, (float at, int count)> Stacks = new System.Collections.Generic.Dictionary<Vector2Int, (float, int)>();
        bool big;

        static int Pow10(int n) { int r = 1; while (n-- > 0) r *= 10; return r; }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / Lifetime;
            if (t >= 1f)
            {
                gameObject.SetActive(false);
                if (Pool.Count < 64) Pool.Push(this); else Destroy(gameObject);
                return;
            }
            // Quick pop, then float upwards while slowing down.
            float rise = 1f - (1f - t) * (1f - t);
            transform.position = start + (Vector3)(drift * rise * 0.6f);
            // [FEEL] Bigger and held longer in place (critical hits a third larger); little drift so a burst stays readable.
            float k = big ? 1.35f : 1f;
            float scale = t < 0.1f ? Mathf.Lerp(1.4f, 3.4f, t / 0.1f) : Mathf.Lerp(3.4f, 2.7f, Mathf.Clamp01((t - 0.1f) / 0.15f));
            transform.localScale = Vector3.one * scale * k;
            float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            for (int i = 0; i < shown; i++)
            {
                var sr = digits[i];
                var c = sr.color;
                c.a = alpha;
                sr.color = c;
                var r = rims[i].color;
                r.a = alpha;
                rims[i].color = r;
            }
        }
    }
}
