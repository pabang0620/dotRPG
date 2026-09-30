using UnityEngine;

namespace DotRPG
{
    /// <summary>Flickering campfire: cycles the flame frames, pulses a warm glow and lets embers rise.</summary>
    public class CampfireFx : MonoBehaviour
    {
        SpriteRenderer sr, glow;
        Sprite[] frames;
        float t, nextEmber;

        void Start()
        {
            sr = GetComponent<SpriteRenderer>();
            frames = new[] { Game.Art.Get("snow_fire_0"), Game.Art.Get("snow_fire_1"), Game.Art.Get("snow_fire_2") };
            var go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            glow = go.AddComponent<SpriteRenderer>();
            glow.sprite = Game.Art.Get("fx_glow");
            glow.sharedMaterial = FxMaterials.Additive;
            t = Random.value * 3f;
        }

        void Update()
        {
            t += Time.deltaTime;
            sr.sprite = frames[Mathf.FloorToInt(t * 8f) % frames.Length];
            float pulse = 0.88f + 0.12f * Mathf.Sin(t * 11f) * Mathf.Sin(t * 3.7f);
            glow.color = new Color(1f, 0.62f, 0.25f, 0.4f * pulse);
            glow.transform.localScale = Vector3.one * (2.3f * pulse);
            glow.sortingOrder = sr.sortingOrder + 1;
            if (t >= nextEmber)
            {
                nextEmber = t + Random.Range(0.12f, 0.3f);
                SkillFx.Spawn("fx_spark", (Vector2)transform.position + new Vector2(Random.Range(-0.2f, 0.2f), 0.55f),
                        new Color(1f, Random.Range(0.55f, 0.8f), 0.3f, 1f), Random.Range(0.5f, 0.9f), sr.sortingOrder + 2)
                    .Move(new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.8f, 1.4f)), 0.5f).Scale(0.9f, 0.3f);
            }
        }
    }

    /// <summary>Soft warm light around a lantern or window, sorted just above its owner.</summary>
    public class WarmGlow : MonoBehaviour
    {
        SpriteRenderer owner, glow;
        float baseAlpha, phase;

        public static void Attach(Transform target, Vector2 offset, float size, float alpha)
        {
            var go = new GameObject("WarmGlow");
            go.transform.SetParent(target, false);
            go.transform.localPosition = offset;
            go.transform.localScale = Vector3.one * size;
            var w = go.AddComponent<WarmGlow>();
            w.owner = target.GetComponent<SpriteRenderer>();
            w.glow = go.AddComponent<SpriteRenderer>();
            w.glow.sprite = Game.Art.Get("fx_glow");
            w.glow.sharedMaterial = FxMaterials.Additive;
            w.baseAlpha = alpha;
            w.phase = Random.value * 6f;
            w.LateUpdate();
        }

        void LateUpdate()
        {
            float a = baseAlpha * (0.92f + 0.08f * Mathf.Sin(Time.time * 2.3f + phase));
            glow.color = new Color(1f, 0.82f, 0.5f, a);
            if (owner != null) glow.sortingOrder = owner.sortingOrder + 1;
        }
    }
}
