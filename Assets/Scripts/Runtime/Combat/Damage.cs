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
        /// <summary>Damage over time (burn ticks): it does not start the target's hit invulnerability, so it never eats a real blow.</summary>
        public bool noHitInvulnerability;
        /// <summary>[ARMOR] Damage over time (burns, the mage's zones): it goes through a monster's armour.</summary>
        public bool dot;
        /// <summary>[RAID] Instant-death gimmick (파멸의 의식, 운명의 낙인): goes through invulnerability and shields.</summary>
        public bool lethal;

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
            noHitInvulnerability = false;
            dot = false; // [ARMOR]
            lethal = false; // [RAID]
        }

        /// <summary>The party member that dealt the hit, or null.</summary>
        public PlayerController AttackerMember => attacker != null ? attacker.GetComponent<PlayerController>() : null;
    }

    /// <summary>Anything that reacts to attacks: enemies and the player.</summary>
    public interface IDamageable
    {
        /// <returns>True if the hit landed (used for hit-stop, sounds, and "hit once per swing").</returns>
        bool TakeDamage(DamageInfo info);
    }
}
