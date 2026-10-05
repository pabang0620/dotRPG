using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Monster arrow / bolt: flies straight at chest height, hits the first alive party member it touches
    /// (through <see cref="MonsterHits"/>), stops on walls and props, fades at the end of its range.
    /// </summary>
    public sealed class MonsterProjectile : MonoBehaviour
    {
        EnemyController owner;
        GameObject ownerObject;
        Vector2 direction;
        float speed, range, radius, travelled, knockback;
        int damage;
        bool unblockable;
        SpriteRenderer sr, glow;
        Color trail;
        float trailTimer;

        readonly List<Collider2D> overlap = new List<Collider2D>();
        ContactFilter2D filter;

        /// <summary>Every projectile in flight (tests count them).</summary>
        public static readonly List<MonsterProjectile> Active = new List<MonsterProjectile>();

        public EnemyController Owner => owner;

        /// <param name="sprite">"mon_arrow", "mon_bolt", "mon_bolt_big".</param>
        public static MonsterProjectile Fire(EnemyController owner, string sprite, Vector2 origin, Vector2 dir, float speed, float range, float radius,
            int damage, float knockback, Color glowColor, bool unblockable = false)
        {
            var go = new GameObject("MonsterProjectile");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = origin;
            var p = go.AddComponent<MonsterProjectile>();
            p.owner = owner;
            p.ownerObject = owner != null ? owner.gameObject : null;
            p.direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
            p.speed = speed;
            p.range = range;
            p.radius = radius;
            p.damage = damage;
            p.knockback = knockback;
            p.unblockable = unblockable;
            p.trail = glowColor;
            p.sr = go.AddComponent<SpriteRenderer>();
            p.sr.sprite = RegionalMonsterArt.Projectile(owner != null ? owner.Def : null) ?? Game.Art.Get(sprite);
            if (sprite == "mon_arrow") go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(p.direction.y, p.direction.x) * Mathf.Rad2Deg);
            if (glowColor.a > 0f)
            {
                var g = new GameObject("Glow");
                g.transform.SetParent(go.transform, false);
                g.transform.localScale = Vector3.one * 0.7f;
                p.glow = g.AddComponent<SpriteRenderer>();
                p.glow.sprite = Game.Art.Get("fx_glow");
                p.glow.sharedMaterial = FxMaterials.Additive;
                p.glow.color = glowColor;
            }
            p.filter = new ContactFilter2D { useTriggers = false };
            p.UpdateOrder();
            Active.Add(p);
            return p;
        }

        void OnDestroy() => Active.Remove(this);

        void Update()
        {
            if (!Game.IsWorldRunning) return;
            float step = speed * Time.deltaTime;
            transform.position += (Vector3)(direction * step);
            travelled += step;
            UpdateOrder();
            trailTimer -= Time.deltaTime;
            if (trailTimer <= 0f && trail.a > 0f)
            {
                trailTimer = 0.04f;
                SkillFx.Spawn("fx_glow", transform.position, new Color(trail.r, trail.g, trail.b, trail.a * 0.6f), 0.18f, sr.sortingOrder - 2).Additive().Scale(0.4f, 0.1f);
            }
            var hits = MonsterHits.InCircle(transform.position, radius + 0.25f);
            if (hits.Count > 0)
            {
                // The closest member takes it.
                PlayerController best = hits[0];
                foreach (var m in hits)
                    if (Vector2.Distance(m.Center, transform.position) < Vector2.Distance(best.Center, transform.position)) best = m;
                MonsterHits.Apply(new[] { best }, damage, (Vector2)transform.position - direction, knockback, unblockable, ownerObject);
                Burst(true);
                return;
            }
            if (HitsWall())
            {
                Burst(false);
                return;
            }
            if (travelled >= range)
            {
                Burst(false);
            }
        }

        bool HitsWall()
        {
            overlap.Clear();
            // Walls sit at the feet line; the projectile flies ~0.45 above it.
            Physics2D.OverlapCircle((Vector2)transform.position - new Vector2(0f, 0.4f), 0.08f, filter, overlap);
            foreach (var col in overlap)
            {
                if (col == null || col.isTrigger) continue;
                if (col.attachedRigidbody != null) continue; // bodies (monsters, members) do not stop it
                if (col.gameObject.name == "Water") continue;
                return true;
            }
            return false;
        }

        void UpdateOrder()
        {
            sr.sortingOrder = YSort.OrderFor(transform.position.y - 0.45f) + 20;
            if (glow != null) glow.sortingOrder = sr.sortingOrder - 1;
        }

        void Burst(bool landed)
        {
            if (trail.a > 0f) SkillVisuals.Sparks(transform.position, trail, landed ? 6 : 3, 2.5f);
            else Fx.Burst("fx_dust", transform.position, landed ? 3 : 2, 1.5f, 0.3f);
            Destroy(gameObject);
        }
    }
}
