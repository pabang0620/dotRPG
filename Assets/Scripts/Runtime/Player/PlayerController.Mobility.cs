using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class PlayerController
    {
        public const float DashDistance = 2.8f, DashDuration = .18f, DashCooldown = 1.4f;
        public const float BlinkDistance = 3.5f, BlinkCooldown = 1.5f;
        /// <summary>[ROGUE] 그림자 질주: the dash at 120% distance and the same speed (3.36 m in 0.216 s), cooldown of a damaging
        /// mobility (dash + 0.2 s, like 섬광보 on Shift). Its hits are SkillCaster.ShadowDash.</summary>
        public const float ShadowDashDistance = DashDistance * 1.2f, ShadowDashCooldown = DashCooldown + .2f;
        const float MobilitySkin = .03f;
        readonly List<RaycastHit2D> mobilityHits = new List<RaycastHit2D>(16);
        float mobilityReadyAt, dashRemaining, nextDashDust;
        Vector2 dashDirection;
        bool mobilityBraking;

        public bool IsDashing => dashRemaining > 0f;
        public float MobilityCooldownRemaining => Mathf.Max(0f, mobilityReadyAt - Time.time);
        public string MobilityName => Class == CharacterClass.Mage ? L.T("텔레포트") : Class == CharacterClass.Rogue ? L.T("그림자 질주") : skills != null && skills.RushOnMobility ? L.T("섬광보") : L.T("대시");

        /// <summary>One key-down, current movement or last facing, without MP cost or invulnerability.</summary>
        public bool TryMobility(Vector2 movement)
        {
            if (!isActiveAndEnabled || IsDead || !Game.IsWorldRunning || (IsLocal && !(QuestAutoPilot.Drives(this) && QuestAutoPilot.WindowOpenInRunningWorld) && (!Game.IsPlaying || Game.State.ChangedThisFrame)) ||
                IsDashing || MobilityCooldownRemaining > 0f || Time.time < knockbackUntil ||
                Time.time < lockedUntil || skills.IsCasting) return false;

            Vector2 direction = SnapTo8(movement.sqrMagnitude > .01f ? movement : AimDirection);
            // [FIGHTER] 섬광보 learned: Shift casts it (toward the move direction) instead of the plain dash.
            if (Class == CharacterClass.Warrior && skills.RushOnMobility)
            {
                combat.Cancel();
                Facing = FacingExtensions.FromVector(direction, Facing);
                AimDirection = direction;
                if (!skills.TryCastRush(out float rushCooldown)) return false;
                mobilityReadyAt = Time.time + rushCooldown;
                return true;
            }
            bool blink = Class == CharacterClass.Mage, shadow = Class == CharacterClass.Rogue;
            float distance = MobilityClearance(direction, blink ? BlinkDistance : shadow ? ShadowDashDistance : DashDistance);
            if (distance < .08f) return false;

            combat.Cancel();
            Facing = FacingExtensions.FromVector(direction, Facing);
            AimDirection = direction;
            desiredVelocity = Vector2.zero;
            body.SetVelocity(Vector2.zero);
            mobilityReadyAt = Time.time + (blink ? BlinkCooldown : shadow ? ShadowDashCooldown : DashCooldown);
            if (blink)
            {
                BlinkEffect(Position);
                Vector2 destination = Position + direction * distance;
                body.position = destination;
                transform.position = destination;
                Physics2D.SyncTransforms();
                BlinkEffect(destination);
                animator.Play(CharacterAnim.Idle, Facing);
            }
            else
            {
                dashDirection = direction;
                dashRemaining = distance;
                nextDashDust = 0f;
                animator.Play(CharacterAnim.Walk, Facing);
                animator.SpeedMultiplier = 1.8f;
                Fx.Dust(Position);
                if (shadow) skills.ShadowDash(direction); // [ROGUE] the path is cut 0.6 s later
            }
            return true;
        }

        /// <summary>[ROGUE] 연쇄 도약: no knockback (damage still lands) until this time.</summary>
        float steadyUntil;
        public bool Steady => Time.time < steadyUntil;
        public void HoldSteady(float seconds) => steadyUntil = Mathf.Max(steadyUntil, Time.time + seconds);

        /// <summary>
        /// [ROGUE] 연쇄 도약 / 그림자 습격: jump straight to <paramref name="destination"/>, stopping before walls (monsters and
        /// players do not block), facing <paramref name="faceTo"/>. No mobility cooldown. Returns false when it cannot move.
        /// </summary>
        public bool SkillLeapTo(Vector2 destination, Vector2 faceTo)
        {
            if (IsDead) return false;
            Vector2 delta = destination - Position;
            float dist = delta.magnitude;
            if (dist > .02f)
            {
                Vector2 dir = delta / dist;
                float clear = MobilityClearance(dir, dist);
                if (clear < .02f) return false;
                CancelMobility();
                desiredVelocity = Vector2.zero;
                body.SetVelocity(Vector2.zero);
                Vector2 to = Position + dir * clear;
                body.position = to;
                transform.position = to;
                Physics2D.SyncTransforms();
            }
            Vector2 look = faceTo - Position;
            if (look.sqrMagnitude > .0001f) { Facing = FacingExtensions.FromVector(look, Facing); AimDirection = look.normalized; }
            return true;
        }

        /// <summary>
        /// [SKILL] 돌진 베기: a skill-driven dash of up to <paramref name="distance"/> along <paramref name="direction"/>
        /// (stops at walls, no mobility cooldown). Returns the distance it will cover.
        /// </summary>
        public float SkillDash(Vector2 direction, float distance)
        {
            if (IsDead || direction.sqrMagnitude < .0001f) return 0f;
            direction = direction.normalized;
            float clear = MobilityClearance(direction, distance);
            if (clear < .08f) return 0f;
            Facing = FacingExtensions.FromVector(direction, Facing);
            AimDirection = direction;
            desiredVelocity = Vector2.zero;
            body.SetVelocity(Vector2.zero);
            dashDirection = direction;
            dashRemaining = clear;
            nextDashDust = 0f;
            animator.Play(CharacterAnim.Walk, Facing);
            animator.SpeedMultiplier = 2.2f;
            Fx.Dust(Position);
            return clear;
        }

        /// <summary>
        /// [CAREER] 차원도약: a skill-driven teleport of up to <paramref name="distance"/> (stops before walls, no mobility
        /// cooldown). Returns the distance covered.
        /// </summary>
        public float SkillBlink(Vector2 direction, float distance)
        {
            if (IsDead || direction.sqrMagnitude < .0001f) return 0f;
            direction = direction.normalized;
            float clear = MobilityClearance(direction, distance);
            if (clear < .08f) return 0f;
            Facing = FacingExtensions.FromVector(direction, Facing);
            AimDirection = direction;
            desiredVelocity = Vector2.zero;
            body.SetVelocity(Vector2.zero);
            BlinkEffect(Position);
            Vector2 destination = Position + direction * clear;
            body.position = destination;
            transform.position = destination;
            Physics2D.SyncTransforms();
            BlinkEffect(destination);
            return clear;
        }

        /// <summary>[CAREER] How far a skill dash along <paramref name="direction"/> could go before a wall, without moving.</summary>
        public float SkillClearance(Vector2 direction, float distance) =>
            direction.sqrMagnitude < .0001f ? 0f : MobilityClearance(direction.normalized, distance);

        // Sweep the actual feet collider, including its offset and the physics collision mask.
        // Triggers (pickups / door prompts) are not walls; solids cannot be tunneled through.
        float MobilityClearance(Vector2 direction, float distance)
        {
            Physics2D.SyncTransforms();
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            int count = BodyCollider.Cast(direction, filter, mobilityHits, distance + MobilitySkin);
            float safe = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = mobilityHits[i];
                if (hit.collider == null || hit.rigidbody == body || Physics2D.GetIgnoreCollision(BodyCollider, hit.collider)) continue;
                // Dash, teleport and the dash skills of every class go through monsters, players and NPCs; only walls stop them.
                if (IsActor(hit.collider)) continue;
                // A touching surface only blocks motion into it, allowing escape or travel along it.
                if (Vector2.Dot(direction, hit.normal) >= -.001f) continue;
                safe = Mathf.Min(safe, Mathf.Max(0f, hit.distance - MobilitySkin));
            }
            return safe;
        }

        static bool IsActor(Collider2D c) =>
            c.GetComponentInParent<EnemyController>() != null || c.GetComponentInParent<PlayerController>() != null || c.GetComponentInParent<NpcController>() != null;

        readonly List<Collider2D> passedThrough = new List<Collider2D>(8);
        readonly List<RaycastHit2D> passHits = new List<RaycastHit2D>(16);

        // Stop colliding with the actors in the next dash step so the body slides through them instead of shoving them.
        void PassThroughActors(Vector2 direction, float distance)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            int count = BodyCollider.Cast(direction, filter, passHits, distance + .3f);
            for (int i = 0; i < count; i++)
            {
                var c = passHits[i].collider;
                if (c == null || passHits[i].rigidbody == body || !IsActor(c) || Physics2D.GetIgnoreCollision(BodyCollider, c)) continue;
                Physics2D.IgnoreCollision(BodyCollider, c, true);
                passedThrough.Add(c);
            }
            if (passedThrough.Count > 0 && restoreRoutine == null) restoreRoutine = StartCoroutine(RestoreAfterPass());
        }

        Coroutine restoreRoutine;

        // Collide with them again once the dash is over and the bodies no longer overlap (at most 2 s later).
        System.Collections.IEnumerator RestoreAfterPass()
        {
            float giveUp = Time.time + 2f;
            while (passedThrough.Count > 0)
            {
                yield return new WaitForFixedUpdate();
                if (IsDashing) continue;
                for (int i = passedThrough.Count - 1; i >= 0; i--)
                {
                    var c = passedThrough[i];
                    if (c == null || !c.enabled || BodyCollider == null) { passedThrough.RemoveAt(i); continue; }
                    if (Time.time < giveUp && Physics2D.Distance(BodyCollider, c).isOverlapped) continue;
                    Physics2D.IgnoreCollision(BodyCollider, c, false);
                    passedThrough.RemoveAt(i);
                }
            }
            restoreRoutine = null;
        }

        /// <summary>Largest monster size a walking character can push aside.</summary>
        const float ShoveMaxSize = 1.3f;
        readonly List<RaycastHit2D> shoveHits = new List<RaycastHit2D>(8);

        // Walking into a regular monster pushes it along instead of being body-blocked by it. Bosses, super armor and
        // big monsters hold their ground. A party member PC cannot move the host's monsters, so it walks through the
        // same ones instead of being body-blocked (collision comes back once they no longer overlap).
        void ShoveMonsters(Vector2 velocity)
        {
            if (BodyCollider == null) return;
            bool member = PartyNet.IsMember;
            Vector2 dir = velocity.normalized;
            float step = velocity.magnitude * Time.fixedDeltaTime;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            int count = BodyCollider.Cast(dir, filter, shoveHits, step + .06f);
            for (int i = 0; i < count; i++)
            {
                var hit = shoveHits[i];
                if (hit.collider == null || hit.rigidbody == null || hit.rigidbody == body) continue;
                var enemy = hit.collider.GetComponentInParent<EnemyController>();
                if (enemy == null || enemy.IsDead || enemy.IsBoss || enemy.SuperArmorActive) continue;
                if (enemy.Def != null && enemy.Def.size > ShoveMaxSize) continue;
                if (!member) { hit.rigidbody.position += dir * step; continue; }
                if (passedThrough.Contains(hit.collider)) continue;
                Physics2D.IgnoreCollision(BodyCollider, hit.collider, true);
                passedThrough.Add(hit.collider);
            }
            if (member && passedThrough.Count > 0 && restoreRoutine == null) restoreRoutine = StartCoroutine(RestoreAfterPass());
        }

        void StepDash()
        {
            float requested = Mathf.Min(dashRemaining, DashDistance / DashDuration * Time.fixedDeltaTime);
            PassThroughActors(dashDirection, requested);
            float step = MobilityClearance(dashDirection, requested);
            body.SetVelocity(Vector2.zero);
            body.MovePosition(Position + dashDirection * step);
            mobilityBraking = true;
            dashRemaining = Mathf.Max(0f, dashRemaining - step);
            if (Time.time >= nextDashDust)
            {
                Fx.Dust(Position - dashDirection * .12f);
                nextDashDust = Time.time + .045f;
            }
            if (step < requested - .001f || dashRemaining < .001f) CancelMobility();
        }

        /// <summary>Collide again with everything a dash went through (OnDisable stops the restore coroutine).</summary>
        void RestorePassThrough()
        {
            foreach (var c in passedThrough)
                if (c != null && BodyCollider != null) Physics2D.IgnoreCollision(BodyCollider, c, false);
            passedThrough.Clear();
            restoreRoutine = null;
        }

        void CancelMobility()
        {
            dashRemaining = 0f;
            if (animator != null) animator.SpeedMultiplier = 1f;
        }

        static void BlinkEffect(Vector2 feet)
        {
            SkillVisuals.CastCircle(feet, SkillVisuals.MageViolet);
            SkillVisuals.Flash(feet + Vector2.up * .45f, SkillVisuals.FrostBlue, 1.3f, .22f);
            SkillVisuals.Sparks(feet + Vector2.up * .45f, SkillVisuals.MageViolet, 8, 3f, .3f);
        }
    }
}
