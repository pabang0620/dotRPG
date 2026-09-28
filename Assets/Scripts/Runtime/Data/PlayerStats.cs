using UnityEngine;

namespace DotRPG
{
    /// <summary>Tunable player numbers. Edit the asset at Assets/Resources/Data/PlayerStats.asset.</summary>
    [CreateAssetMenu(menuName = "dotRPG/Player Stats", fileName = "PlayerStats")]
    public class PlayerStats : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("Units (tiles) per second.")]
        public float moveSpeed = 4.2f;
        [Tooltip("How fast the character reaches full speed. Higher = snappier.")]
        public float acceleration = 45f;

        [Header("Health (1 = half a heart)")]
        public int maxHealth = 6;
        public float invulnerableTime = 0.9f;
        public float knockbackSpeed = 7f;
        public float knockbackDuration = 0.15f;

        [Header("Attack")]
        public int attackDamage = 1;
        public float attackCooldown = 0.36f;
        public float attackDuration = 0.2f;
        [Tooltip("Distance from the body to the centre of the hit circle.")]
        public float attackReach = 0.7f;
        public float attackRadius = 0.62f;
        public float attackKnockback = 6f;

        [Header("Interaction & items")]
        public float interactRange = 1.25f;
        [Tooltip("Health restored by eating one carrot (1 = half a heart).")]
        public int carrotHealAmount = 2;
    }
}
