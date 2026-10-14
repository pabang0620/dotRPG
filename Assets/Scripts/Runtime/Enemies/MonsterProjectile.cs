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
        bool visualOnly;
        string spriteKey;
        static int nextVisualId;
        readonly int visualId = ++nextVisualId;

        internal void WriteVisual(System.IO.BinaryWriter w)
        {
            w.Write(visualId); w.Write(owner != null ? owner.NetId : 0); w.Write(spriteKey);
            PartyWire.WriteVec(w, transform.position); PartyWire.WriteVec(w, direction);
            w.Write(speed); w.Write(Mathf.Max(0f, range - travelled));
            w.Write(trail.r); w.Write(trail.g); w.Write(trail.b); w.Write(trail.a);
        }

        internal static MonsterProjectile ReadVisual(System.IO.BinaryReader r, MonsterProjectile previous,
            Dictionary<int, EnemyController> owners)
        {
            int ownerId = r.ReadInt32(); string sprite = r.ReadString();
            var pos = PartyWire.ReadVec(r); var dir = PartyWire.ReadVec(r);
            float speed = r.ReadSingle(), remaining = r.ReadSingle();
            var color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var p = previous;
            if (p == null)
            {
                owners.TryGetValue(ownerId, out var owner);
                p = Fire(owner, sprite, pos, dir, speed, remaining, 0f, 0, 0f, color);
                p.visualOnly = true;
                Active.Remove(p);
            }
            p.transform.position = pos;
            p.range = remaining; p.travelled = 0;
            return p;
        }

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
            p.spriteKey = sprite;
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
            if (visualOnly)
            {
                if (travelled >= range) Destroy(gameObject);
                return; // host snapshots own collisions, damage and disappearance
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
                // Characters do not stop a shot here; static composite tilemaps still must.
                if (col.attachedRigidbody != null && col.attachedRigidbody.bodyType != RigidbodyType2D.Static) continue;
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
