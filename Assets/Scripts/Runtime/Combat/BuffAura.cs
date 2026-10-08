using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Glow and rising embers around the player while a buff lasts (전쟁 함성).</summary>
    public class BuffAura : MonoBehaviour
    {
        SpriteRenderer glow;
        Color color;
        float until, next;

        public static void Attach(Transform target, Color color, float seconds)
        {
            var aura = target.GetComponent<BuffAura>();
            if (aura == null)
            {
                aura = target.gameObject.AddComponent<BuffAura>();
                var go = new GameObject("BuffGlow");
                go.transform.SetParent(target, false);
                go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                aura.glow = go.AddComponent<SpriteRenderer>();
                aura.glow.sprite = Game.Art.Get("fx_glow");
                aura.glow.sharedMaterial = FxMaterials.Additive;
            }
            aura.color = color;
            aura.until = Time.time + seconds;
        }

        void LateUpdate()
        {
            if (Time.time >= until)
            {
                if (glow != null) Destroy(glow.gameObject);
                Destroy(this);
                return;
            }
            float left = until - Time.time;
            float pulse = 0.55f + 0.2f * Mathf.Sin(Time.time * 7f);
            glow.color = new Color(color.r, color.g, color.b, pulse * Mathf.Clamp01(left / 0.5f) * 0.6f);
            glow.transform.localScale = Vector3.one * (1.1f + 0.08f * Mathf.Sin(Time.time * 7f));
            glow.sortingOrder = YSort.OrderFor(transform.position.y) - 3;
            if (Time.time >= next)
            {
                next = Time.time + 0.07f;
                Vector2 p = (Vector2)transform.position + new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(0.1f, 0.6f));
                SkillFx.Spawn("fx_spark", p, new Color(1f, Mathf.Lerp(0.5f, 0.85f, Random.value), 0.3f, 1f), Random.Range(0.35f, 0.55f), SkillFx.At(transform.position.y, 30))
                    .Move(new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(1.2f, 2f)), 1f).Scale(1f, 0.4f);
            }
        }
    }
}
