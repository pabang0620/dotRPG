using UnityEngine;

namespace DotRPG
{
    public partial class EnemyController
    {
        float slowUntil, slowFactor = 1f;

        /// <summary>
        /// [CH3 SPEC] Slowed: walks at (1 - fraction) of its speed for a while. The strongest running slow wins; a weaker
        /// one never stretches it. Bosses take half the slow.
        /// </summary>
        public void Slow(float fraction, float seconds)
        {
            if (state == State.Dead || seconds <= 0f || fraction <= 0f || Puppet) return;
            if (IsBoss) fraction *= .5f;
            float factor = Mathf.Clamp(1f - fraction, .2f, 1f);
            bool running = Time.time < slowUntil;
            if (running && factor > slowFactor) return;
            slowFactor = factor;
            slowUntil = Mathf.Max(running ? slowUntil : 0f, Time.time + seconds);
        }

        /// <summary>Movement multiplier from <see cref="Slow"/> (1 when not slowed).</summary>
        float SlowMul => Time.time < slowUntil ? slowFactor : 1f;
    }
}
