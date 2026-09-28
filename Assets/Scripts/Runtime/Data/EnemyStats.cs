using UnityEngine;

namespace DotRPG
{
    /// <summary>Tunable enemy numbers. One asset per enemy type (the slice has "skeleton").</summary>
    [CreateAssetMenu(menuName = "dotRPG/Enemy Stats", fileName = "EnemyStats")]
    public class EnemyStats : ScriptableObject
    {
        public string enemyId = "skeleton";
        public string displayName = "해골";

        [Header("Health")]
        public int maxHealth = 3;
        public float hurtStunTime = 0.3f;
        public float invulnerableTime = 0.15f;
        public float knockbackSpeed = 6f;

        [Header("Movement")]
        public float wanderSpeed = 1.1f;
        public float chaseSpeed = 2.3f;
        public float wanderRadius = 2.5f;
        public Vector2 wanderPauseRange = new Vector2(1.0f, 2.5f);

        [Header("Senses")]
        public float detectRadius = 4.5f;
        public float loseInterestRadius = 7.5f;
        [Tooltip("Maximum distance from its spawn point before it gives up and walks home.")]
        public float leashRadius = 10f;

        [Header("Attack")]
        public int attackDamage = 1;
        public float attackRange = 0.95f;
        public float windupTime = 0.5f;
        public float recoverTime = 0.7f;
        public float attackKnockback = 7f;

        [Header("Spawning")]
        public float respawnDelay = 25f;
        [Tooltip("Enemies never respawn while the player is closer than this.")]
        public float respawnMinPlayerDistance = 9f;
    }
}
