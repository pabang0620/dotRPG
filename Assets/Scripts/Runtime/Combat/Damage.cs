using UnityEngine;

namespace DotRPG
{
    public enum Team
    {
        Player,
        Enemy,
        Neutral,
    }

    public struct DamageInfo
    {
        public int amount;
        public Vector2 sourcePosition;
        public float knockback;
        public Team team;
        // [MONSTER] Boss telegraphs / charges: the target's block roll is skipped.
        public bool unblockable;

        public DamageInfo(int amount, Vector2 sourcePosition, float knockback, Team team)
        {
            this.amount = amount;
            this.sourcePosition = sourcePosition;
            this.knockback = knockback;
            this.team = team;
        }
    }

    /// <summary>Anything that reacts to attacks: enemies, the player, trees, rocks.</summary>
    public interface IDamageable
    {
        /// <returns>True if the hit landed (used for hit-stop, sounds, and "hit once per swing").</returns>
        bool TakeDamage(DamageInfo info);
    }
}
