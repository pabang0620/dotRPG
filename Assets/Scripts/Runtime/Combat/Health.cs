using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Reusable hit points with invulnerability frames. 1 point = half a heart for the player.</summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] int max = 6;
        [SerializeField] int current = 6;
        [SerializeField] float invulnerableTime = 0.5f;

        float invulnerableUntil;

        public int Max => max;
        public int Current => current;
        public bool IsDead => current <= 0;
        public bool IsInvulnerable => Time.time < invulnerableUntil;

        public event Action<DamageInfo> Damaged;
        public event Action Died;
        /// <summary>(current, max)</summary>
        public event Action<int, int> Changed;

        public void Init(int maxHealth, int currentHealth, float invulnerability)
        {
            max = Mathf.Max(1, maxHealth);
            current = Mathf.Clamp(currentHealth, 0, max);
            invulnerableTime = invulnerability;
            invulnerableUntil = 0f;
            Changed?.Invoke(current, max);
        }

        /// <summary>No damage for a while (revive protection). Never shortens a running invulnerability.</summary>
        public void SetInvulnerable(float seconds) => invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);

        public bool TryDamage(DamageInfo info)
        {
            if (IsDead || IsInvulnerable || info.amount <= 0) return false;
            current = Mathf.Max(0, current - info.amount);
            if (!info.noHitInvulnerability) invulnerableUntil = Time.time + invulnerableTime;
            Damaged?.Invoke(info);
            Changed?.Invoke(current, max);
            if (current == 0) Died?.Invoke();
            return true;
        }

        /// <summary>Spends HP as a cost (never kills, no hit reaction).</summary>
        public void Drain(int amount)
        {
            if (IsDead || amount <= 0) return;
            current = Mathf.Max(1, current - amount);
            Changed?.Invoke(current, max);
        }

        public int Heal(int amount)
        {
            if (IsDead || amount <= 0) return 0;
            int before = current;
            current = Mathf.Min(max, current + amount);
            if (current != before) Changed?.Invoke(current, max);
            return current - before;
        }

        public void SetMax(int newMax, bool fill)
        {
            max = Mathf.Max(1, newMax);
            current = fill ? max : Mathf.Min(current, max);
            Changed?.Invoke(current, max);
        }
    }
}
