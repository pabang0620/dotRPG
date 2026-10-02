using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Small sprite particles (leaves, stone chips, dust, sparkles). Simulated with a fake height so
    /// pieces hop and land on the ground plane like in top-down pixel games.
    /// Swap for a ParticleSystem/VFX later without touching callers.
    /// </summary>
    public static class Fx
    {
        public static Transform Root;
        static readonly System.Collections.Generic.Stack<FxParticle> Pool = new System.Collections.Generic.Stack<FxParticle>();

        /// <summary>A particle that finished goes back to the pool.</summary>
        public static void Release(FxParticle p)
        {
            p.gameObject.SetActive(false);
            if (Pool.Count < 256) Pool.Push(p);
            else Object.Destroy(p.gameObject);
        }

        public static FxParticle Spawn(string spriteKey, Vector2 position, Vector2 velocity, float upSpeed, float lifetime,
            float gravity = 18f, int orderBoost = 8)
        {
            if (Game.Art == null) return null;
            // [P5] Reuse finished particles: a fight spawns hundreds and each new GameObject was garbage.
            FxParticle p = null;
            while (Pool.Count > 0 && p == null) p = Pool.Pop(); // entries die with the old map's root
            SpriteRenderer sr;
            if (p != null)
            {
                sr = p.GetComponent<SpriteRenderer>();
                if (Root != null && p.transform.parent != Root) p.transform.SetParent(Root, false);
                p.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("fx");
                if (Root != null) go.transform.SetParent(Root, false);
                sr = go.AddComponent<SpriteRenderer>();
                p = go.AddComponent<FxParticle>();
            }
            sr.sprite = Game.Art.Get(spriteKey);
            p.Init(sr, position, velocity, upSpeed, lifetime, gravity, orderBoost);
            return p;
        }

        public static void Burst(string spriteKey, Vector2 position, int count, float speed, float lifetime = 0.6f)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle.normalized * Random.Range(speed * 0.4f, speed);
                Spawn(spriteKey, position, dir, Random.Range(2f, 4.5f), lifetime * Random.Range(0.75f, 1.15f));
            }
        }

        public static void Sparkle(Vector2 position, int count = 3, float spread = 0.6f)
        {
            for (int i = 0; i < count; i++)
            {
                var offset = Random.insideUnitCircle * spread;
                var p = Spawn("fx_sparkle", position + offset, Vector2.zero, 0f, Random.Range(0.35f, 0.55f), 0f, 40);
                if (p != null) p.PopScale = true;
            }
        }

        public static void Dust(Vector2 position)
        {
            var p = Spawn("fx_dust", position + new Vector2(Random.Range(-0.1f, 0.1f), 0.05f),
                new Vector2(Random.Range(-0.3f, 0.3f), 0.2f), 0f, 0.35f, 0f, -2);
            if (p != null) p.PopScale = true;
        }
    }

    public class FxParticle : MonoBehaviour
    {
        public bool PopScale;

        SpriteRenderer sr;
        Vector2 ground;
        Vector2 velocity;
        float height, upSpeed, gravity, lifetime, age;
        int orderBoost;

        public void Init(SpriteRenderer renderer, Vector2 position, Vector2 vel, float up, float life, float g, int boost)
        {
            sr = renderer;
            ground = position;
            velocity = vel;
            upSpeed = up;
            lifetime = Mathf.Max(0.05f, life);
            gravity = g;
            orderBoost = boost;
            height = 0.25f;
            age = 0f;
            PopScale = false;
            transform.localScale = Vector3.one;
            Apply();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            ground += velocity * dt;
            velocity *= 1f - Mathf.Clamp01(3f * dt);
            if (gravity > 0f)
            {
                upSpeed -= gravity * dt;
                height += upSpeed * dt;
                if (height < 0f)
                {
                    height = 0f;
                    upSpeed = -upSpeed * 0.3f;
                    velocity *= 0.5f;
                }
            }
            if (age >= lifetime)
            {
                Fx.Release(this);
                return;
            }
            Apply();
        }

        void Apply()
        {
            transform.position = new Vector3(ground.x, ground.y + height, 0f);
            sr.sortingOrder = YSort.OrderFor(ground.y) + orderBoost;
            float t = age / lifetime;
            var c = sr.color;
            c.a = t < 0.65f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.65f) / 0.35f);
            sr.color = c;
            if (PopScale) transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.2f, Mathf.Sin(t * Mathf.PI));
        }
    }
}
