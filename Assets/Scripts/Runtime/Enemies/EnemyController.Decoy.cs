using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE] 허수아비 (Docs/PLAN_CLASS_ROGUE.md 2.2): a lured monster walks to the scarecrow and swings at it instead of
    /// following its threat target, so it turns its back on whoever stands on the far side. Runs on the machine that owns
    /// the monster (puppets on a member PC never get here). Bosses are lured for a short time only (ScarecrowDecoy).
    /// </summary>
    public partial class EnemyController
    {
        ScarecrowDecoy decoy;
        float decoyUntil, decoyNextSwing, decoySwingEnd;

        /// <summary>Makes this monster go for <paramref name="scarecrow"/> for <paramref name="seconds"/>.</summary>
        public void Lure(ScarecrowDecoy scarecrow, float seconds)
        {
            if (scarecrow == null || IsDead || seconds <= 0f) return;
            decoy = scarecrow;
            decoyUntil = Time.time + seconds;
            if (state == State.Idle || state == State.Wander || state == State.ReturnHome) EnterState(State.Chase);
        }

        /// <summary>True while this monster is after <paramref name="scarecrow"/>.</summary>
        public bool LuredBy(ScarecrowDecoy scarecrow) => decoy != null && decoy == scarecrow && Time.time < decoyUntil;

        /// <summary>Owns the frame while lured: close in, then swing at the scarecrow every windup + recover seconds.</summary>
        bool DecoyTick()
        {
            if (decoy == null) return false;
            if (!decoy.Standing || Time.time >= decoyUntil) { decoy = null; return false; }
            Vector2 to = decoy.Position - Position;
            if (to.sqrMagnitude > .0001f) facing = FacingExtensions.FromVector(to, facing);
            if (to.magnitude > stats.attackRange)
            {
                MoveTowards(decoy.Position, stats.chaseSpeed);
                if (state != State.Chase) EnterState(State.Chase);
                animator.Play(desiredVelocity.sqrMagnitude > 0.05f ? CharacterAnim.Walk : CharacterAnim.Idle, facing);
                return true;
            }
            desiredVelocity = Vector2.zero;
            if (Time.time >= decoyNextSwing)
            {
                decoyNextSwing = Time.time + stats.windupTime + stats.recoverTime;
                decoySwingEnd = Time.time + .25f;
                Game.Audio.PlaySfx("enemy_attack");
                decoy.TakeHit(stats.attackDamage);
            }
            animator.Play(Time.time < decoySwingEnd ? CharacterAnim.Attack : CharacterAnim.Idle, facing);
            return true;
        }
    }
}
