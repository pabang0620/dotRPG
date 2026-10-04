using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class PlayerController
    {
        public const float DashDistance = 2.8f, DashDuration = .18f, DashCooldown = 1.4f;
        public const float BlinkDistance = 3.5f, BlinkCooldown = 2f;
        const float MobilitySkin = .03f;
        readonly List<RaycastHit2D> mobilityHits = new List<RaycastHit2D>(16);
        float mobilityReadyAt, dashRemaining, nextDashDust;
        Vector2 dashDirection;
        bool mobilityBraking;

        public bool IsDashing => dashRemaining > 0f;
        public float MobilityCooldownRemaining => Mathf.Max(0f, mobilityReadyAt - Time.time);
        public string MobilityName => Class == CharacterClass.Mage ? "텔레포트" : "대시";

        /// <summary>One key-down, current movement or last facing, without MP cost or invulnerability.</summary>
        public bool TryMobility(Vector2 movement)
        {
            if (!isActiveAndEnabled || IsDead || !Game.IsWorldRunning || (IsLocal && (!Game.IsPlaying || Game.State.ChangedThisFrame)) ||
                IsDashing || MobilityCooldownRemaining > 0f || Time.time < knockbackUntil ||
                Time.time < lockedUntil || skills.IsCasting) return false;

            Vector2 direction = SnapTo8(movement.sqrMagnitude > .01f ? movement : AimDirection);
            bool blink = Class == CharacterClass.Mage;
            float distance = MobilityClearance(direction, blink ? BlinkDistance : DashDistance);
            if (distance < .08f) return false;

            combat.Cancel();
            Facing = FacingExtensions.FromVector(direction, Facing);
            AimDirection = direction;
            desiredVelocity = Vector2.zero;
            body.SetVelocity(Vector2.zero);
            mobilityReadyAt = Time.time + (blink ? BlinkCooldown : DashCooldown);
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
            }
            return true;
        }

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
                // A touching surface only blocks motion into it, allowing escape or travel along it.
                if (Vector2.Dot(direction, hit.normal) >= -.001f) continue;
                safe = Mathf.Min(safe, Mathf.Max(0f, hit.distance - MobilitySkin));
            }
            return safe;
        }

        void StepDash()
        {
            float requested = Mathf.Min(dashRemaining, DashDistance / DashDuration * Time.fixedDeltaTime);
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
