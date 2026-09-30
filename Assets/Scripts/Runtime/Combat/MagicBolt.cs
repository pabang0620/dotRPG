using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Mage projectile: flies straight, damages the first IDamageable it touches (enemies, trees,
    /// rocks) and fizzles on walls, cliffs and props. Water does not stop it, so the mage can
    /// shoot across the canyon channel.
    /// </summary>
    public class MagicBolt : MonoBehaviour
    {
        GameObject owner;
        EnemyController target;
        const float TurnRateDegrees = 540f;
        Vector2 direction;
        float speed, range, radius, travelled;
        int damage;
        float knockback;
        SpriteRenderer sr;
        SpriteRenderer glow;
        float sparkleTimer, trailTimer;

        readonly List<Collider2D> overlap = new List<Collider2D>();
        ContactFilter2D filter;

        public static MagicBolt Fire(GameObject owner, Vector2 origin, Vector2 direction, CharacterClassInfo info, EnemyController target = null, int damage = -1)
        {
            var go = new GameObject("MagicBolt");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = origin;
            var bolt = go.AddComponent<MagicBolt>();
            bolt.owner = owner;
            bolt.target = target;
            // A homing bolt may curve a little, so give it some extra reach.
            if (target != null) bolt.range = info.boltRange + 1.5f;
            bolt.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            bolt.speed = info.boltSpeed;
            if (target == null) bolt.range = info.boltRange;
            bolt.radius = info.boltRadius;
            bolt.damage = damage >= 0 ? damage : info.damage;
            bolt.knockback = info.knockback;
            bolt.sr = go.AddComponent<SpriteRenderer>();
            bolt.sr.sprite = Game.Art.Get("fx_bolt");
            // Soft violet light around the bolt.
            var glow = new GameObject("Glow");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localScale = Vector3.one * 0.75f;
            bolt.glow = glow.AddComponent<SpriteRenderer>();
            bolt.glow.sprite = Game.Art.Get("fx_glow");
            bolt.glow.sharedMaterial = FxMaterials.Additive;
            bolt.glow.color = new Color(0.62f, 0.5f, 1f, 0.6f);
            bolt.filter = new ContactFilter2D { useTriggers = true };
            bolt.UpdateOrder();
            return bolt;
        }

        void Update()
        {
            if (Game.State != null && Game.State.Current != GameState.Playing) return;
            if (target != null)
            {
                if (target.IsDead || !target.isActiveAndEnabled) target = null;
                else
                {
                    // Turn towards the monster (limited turn rate so it curves instead of snapping).
                    Vector2 want = (target.Center - (Vector2)transform.position).normalized;
                    float maxTurn = TurnRateDegrees * Mathf.Deg2Rad * Time.deltaTime;
                    direction = Vector3.RotateTowards(direction, want, maxTurn, 0f);
                }
            }
            float step = speed * Time.deltaTime;
            transform.position += (Vector3)(direction * step);
            travelled += step;
            transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 30f) * 0.08f);
            UpdateOrder();

            sparkleTimer -= Time.deltaTime;
            if (sparkleTimer <= 0f)
            {
                sparkleTimer = 0.05f;
                Fx.Spawn("fx_magic", (Vector2)transform.position - direction * 0.2f + Random.insideUnitCircle * 0.1f,
                    -direction * 0.5f, 0f, 0.3f, 0f, 30);
            }
            trailTimer -= Time.deltaTime;
            if (trailTimer <= 0f)
            {
                trailTimer = 0.025f;
                SkillFx.Spawn("fx_glow", transform.position, new Color(0.55f, 0.45f, 1f, 0.45f), 0.16f, sr.sortingOrder - 2)
                    .Additive().Scale(0.5f, 0.15f);
            }

            if (CheckHit()) return;
            if (travelled >= range) Explode(false);
        }

        void UpdateOrder()
        {
            // The bolt flies at chest height; sort it a bit in front of whatever is at its feet.
            sr.sortingOrder = YSort.OrderFor(transform.position.y - 0.45f) + 20;
            if (glow != null) glow.sortingOrder = sr.sortingOrder - 1;
        }

        bool CheckHit()
        {
            overlap.Clear();
            Physics2D.OverlapCircle(transform.position, radius, filter, overlap);
            foreach (var col in overlap)
            {
                if (col == null) continue;
                if (col.attachedRigidbody != null && col.attachedRigidbody.gameObject == owner) continue;
                if (owner != null && col.transform.IsChildOf(owner.transform)) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && !(target is PlayerController))
                {
                    var info = new DamageInfo(damage, (Vector2)transform.position - direction, knockback, Team.Player, owner);
                    bool landed = target.TakeDamage(info);
                    // Only the local player's hits shake the screen.
                    if (landed && owner != null && Game.Player != null && owner == Game.Player.gameObject) Game.Camera?.Shake(0.05f, 0.08f);
                    Explode(landed);
                    return true;
                }
                // Solid scenery stops the bolt; triggers (portals, interact zones) and water do not.
                if (!col.isTrigger && col.attachedRigidbody == null && col.gameObject.name != "Water")
                {
                    Explode(false);
                    return true;
                }
            }
            return false;
        }

        void Explode(bool landed)
        {
            Fx.Burst("fx_magic", transform.position, landed ? 8 : 5, 2.5f, 0.4f);
            if (landed) Fx.Sparkle(transform.position, 2, 0.25f);
            SkillVisuals.BoltImpact(transform.position, landed, sr.sortingOrder + 1);
            Destroy(gameObject);
        }
    }
}
