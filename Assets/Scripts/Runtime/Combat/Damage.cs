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
        /// <summary>
        /// Who caused the hit (a party member's or a monster's GameObject; null = unknown / environment).
        /// Used for kill credit, threat and the per-member damage meters.
        /// </summary>
        public GameObject attacker;
        // [MONSTER] Boss telegraphs / charges: the target's block roll is skipped.
        public bool unblockable;

        public DamageInfo(int amount, Vector2 sourcePosition, float knockback, Team team)
            : this(amount, sourcePosition, knockback, team, null) { }

        public DamageInfo(int amount, Vector2 sourcePosition, float knockback, Team team, GameObject attacker)
        {
            this.amount = amount;
            this.sourcePosition = sourcePosition;
            this.knockback = knockback;
            this.team = team;
            this.attacker = attacker;
            unblockable = false; // [MONSTER]
        }

        /// <summary>The party member that dealt the hit, or null.</summary>
        public PlayerController AttackerMember => attacker != null ? attacker.GetComponent<PlayerController>() : null;
    }

    /// <summary>Anything that reacts to attacks: enemies, the player, trees, rocks.</summary>
    public interface IDamageable
    {
        /// <returns>True if the hit landed (used for hit-stop, sounds, and "hit once per swing").</returns>
        bool TakeDamage(DamageInfo info);
    }
}
