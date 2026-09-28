using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Sword swing: cooldown, arc animation of the weapon sprite, slash effect and hit detection.
    /// The same swing damages enemies and harvests trees/rocks (anything implementing IDamageable).
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        const float SwingArc = 150f;

        PlayerController owner;
        PlayerStats stats;
        SpriteRenderer weapon;
        SpriteRenderer slash;
        YSort ySort;

        float attackStart = -10f;
        float attackEnd = -10f;
        float nextAttackTime;
        Facing attackFacing;
        bool hitResolved;

        readonly List<Collider2D> overlap = new List<Collider2D>();
        readonly HashSet<IDamageable> hitThisSwing = new HashSet<IDamageable>();
        ContactFilter2D filter;

        public bool IsAttacking => Time.time < attackEnd;

        public void Setup(PlayerController player, Transform visualRoot)
        {
            owner = player;
            stats = player.Stats;

            weapon = new GameObject("Weapon").AddComponent<SpriteRenderer>();
            weapon.transform.SetParent(visualRoot, false);
            weapon.sprite = Game.Art.Get("tool_sword");
            weapon.sortingOrder = 1;
            weapon.enabled = false;

            slash = new GameObject("Slash").AddComponent<SpriteRenderer>();
            slash.transform.SetParent(visualRoot, false);
            slash.sprite = Game.Art.Get("fx_slash");
            slash.sortingOrder = 3;
            slash.enabled = false;

            filter = new ContactFilter2D { useTriggers = true };
        }

        public void Cancel()
        {
            attackEnd = -10f;
            if (weapon != null) weapon.enabled = false;
            if (slash != null) slash.enabled = false;
        }

        public void TryAttack()
        {
            if (Time.time < nextAttackTime) return;
            attackStart = Time.time;
            attackEnd = attackStart + stats.attackDuration;
            nextAttackTime = attackStart + stats.attackCooldown;
            attackFacing = owner.Facing;
            hitResolved = false;
            hitThisSwing.Clear();

            weapon.enabled = true;
            slash.enabled = true;
            if (ySort == null) ySort = GetComponent<YSort>();
            // Weapon behind the head when swinging upwards.
            ySort?.SetLocalOrder(weapon, attackFacing == Facing.Up ? -1 : 1);
            Game.Audio.PlaySfx("swing");
        }

        void Update()
        {
            if (!IsAttacking)
            {
                if (weapon.enabled) Cancel();
                return;
            }

            float t = Mathf.Clamp01((Time.time - attackStart) / stats.attackDuration);
            Vector2 dir = attackFacing.ToVector();
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            // Sweep from one side of the facing direction to the other (ease-out).
            float eased = 1f - (1f - t) * (1f - t);
            float angle = baseAngle + Mathf.Lerp(SwingArc * 0.5f, -SwingArc * 0.5f, eased);
            weapon.transform.localPosition = new Vector3(0f, 0.42f, 0f) + (Vector3)(dir * 0.12f);
            weapon.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);

            slash.transform.localPosition = new Vector3(0f, 0.42f, 0f) + (Vector3)(dir * 0.35f);
            slash.transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle);
            var c = slash.color;
            c.a = t < 0.25f ? 0f : Mathf.Lerp(0.95f, 0f, (t - 0.25f) / 0.75f);
            slash.color = c;

            // Resolve hits once the blade is roughly in front of the player.
            if (!hitResolved && t >= 0.3f)
            {
                hitResolved = true;
                ResolveHits(dir);
            }
        }

        void ResolveHits(Vector2 dir)
        {
            Vector2 center = owner.Center + dir * stats.attackReach;
            overlap.Clear();
            Physics2D.OverlapCircle(center, stats.attackRadius, filter, overlap);

            bool landed = false;
            foreach (var col in overlap)
            {
                if (col == null || col.attachedRigidbody != null && col.attachedRigidbody.gameObject == owner.gameObject) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || ReferenceEquals(target, owner) || hitThisSwing.Contains(target)) continue;
                hitThisSwing.Add(target);
                var info = new DamageInfo(stats.attackDamage, owner.Center, stats.attackKnockback, Team.Player);
                landed |= target.TakeDamage(info);
            }
            if (landed) Game.Camera?.Shake(0.06f, 0.1f);
        }

        void OnDrawGizmosSelected()
        {
            if (owner == null || stats == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(owner.Center + owner.Facing.ToVector() * stats.attackReach, stats.attackRadius);
        }
    }
}
