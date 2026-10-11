using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [RAID] Player stun (raid roar penalty): no move, attack, skill, mobility or item keys until it ends; the body
    /// shows the hurt pose with sparkles over the head, "기절" pops up, and the local player's buff row counts it down.
    /// </summary>
    public partial class PlayerController
    {
        float stunnedUntil, nextStunSparkle;

        public bool IsStunned => Time.time < stunnedUntil;
        public float StunnedLeft => Mathf.Max(0f, stunnedUntil - Time.time);

        public void Stun(float seconds)
        {
            if (IsDead || seconds <= 0f) return;
            bool fresh = !IsStunned;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            CancelMobility();
            combat.Cancel();
            desiredVelocity = Vector2.zero;
            if (body != null) body.SetVelocity(Vector2.zero);
            if (fresh) WorldPopupText.Show(transform, "기절", new Color(1f, .85f, .35f), seconds);
            if (IsLocal) Game.Camera?.Shake(.15f, .25f);
        }

        /// <summary>While stunned: hold still in the hurt pose with dizzy sparkles.</summary>
        void StunFrame()
        {
            desiredVelocity = Vector2.zero;
            body.SetVelocity(Vector2.zero);
            animator.Play(CharacterAnim.Hurt, Facing);
            if (Time.time >= nextStunSparkle)
            {
                nextStunSparkle = Time.time + .3f;
                Fx.Sparkle(Position + new Vector2(0f, 1.25f), 2, .4f);
            }
        }
    }
}
