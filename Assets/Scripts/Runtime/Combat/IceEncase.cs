using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Ice crystals around a frozen monster's feet. They shatter when the freeze ends or the monster dies.</summary>
    public class IceEncase : MonoBehaviour
    {
        EnemyController enemy;
        readonly List<Transform> crystals = new List<Transform>();
        readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        readonly List<float> sizes = new List<float>();
        SpriteRenderer glow;
        float age;

        public static void Attach(EnemyController enemy)
        {
            if (enemy == null || enemy.IsDead || enemy.GetComponent<IceEncase>() != null) return;
            enemy.gameObject.AddComponent<IceEncase>().Build(enemy);
        }

        void Build(EnemyController target)
        {
            enemy = target;
            AddCrystal(new Vector2(-0.3f, 0.06f), 0.8f, 14f, false);
            AddCrystal(new Vector2(0.32f, 0.08f), 0.9f, -12f, true);
            AddCrystal(new Vector2(0.03f, -0.06f), 0.62f, 0f, false);
            var g = new GameObject("IceGlow");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            g.transform.localScale = Vector3.one * 0.9f;
            glow = g.AddComponent<SpriteRenderer>();
            glow.sprite = Game.Art.Get("fx_glow");
            glow.sharedMaterial = FxMaterials.Additive;
            LateUpdate();
        }

        void AddCrystal(Vector2 offset, float size, float tilt, bool flip)
        {
            var go = new GameObject("Ice");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            go.transform.localScale = new Vector3(size, 0f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get("fx_ice");
            sr.flipX = flip;
            crystals.Add(go.transform);
            renderers.Add(sr);
            sizes.Add(size);
        }

        void LateUpdate()
        {
            if (enemy == null) { Destroy(this); return; }
            if (enemy.IsDead || !enemy.IsFrozen) { Shatter(); return; }
            age += Time.deltaTime;
            float grow = SkillFx.EaseOutBack(Mathf.Clamp01(age / 0.16f));
            int order = YSort.OrderFor(enemy.Position.y) + 8;
            for (int i = 0; i < crystals.Count; i++)
            {
                crystals[i].localScale = new Vector3(sizes[i], sizes[i] * grow, 1f);
                renderers[i].sortingOrder = order + (crystals[i].localPosition.y < 0f ? 1 : 0);
            }
            glow.sortingOrder = order + 2;
            glow.color = new Color(0.55f, 0.85f, 1f, 0.3f + 0.1f * Mathf.Sin(age * 8f));
        }

        void Shatter()
        {
            Vector2 c = (Vector2)transform.position + new Vector2(0f, 0.3f);
            int order = SkillFx.At(transform.position.y, 30);
            for (int i = 0; i < 9; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                SkillFx.Spawn("fx_shard", c + dir * 0.15f, Color.white, Random.Range(0.3f, 0.45f), order)
                    .Move(dir * Random.Range(2.5f, 4.5f) + Vector2.up * 0.8f, 4f).FaceMotion().Scale(0.9f, 0.6f).Fade(FxFade.Late);
            }
            SkillFx.Spawn("fx_glow", c, new Color(0.7f, 0.92f, 1f, 0.6f), 0.2f, order + 1).Additive().Scale(0.4f, 1.2f).Fade(FxFade.Quick);
            foreach (var t in crystals) if (t != null) Destroy(t.gameObject);
            if (glow != null) Destroy(glow.gameObject);
            Destroy(this);
        }
    }
}
