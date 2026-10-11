using UnityEngine;

namespace DotRPG
{
    public partial class PlayerController
    {
        /// <summary>[GUARDIAN] A guardian takes no damage from monsters this many levels (or more) below.</summary>
        public const int GuardianImmuneGap = 25;

        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead || info.team == Team.Player) return false;
            if (NetPuppet || (IsLocal && PartyNet.IsMember)) return false; // [PARTY NET] the host decides HP
            // Party members never hurt each other, whatever team the hit claims.
            if (info.AttackerMember != null) return false;
            // [GUARDIAN] Monsters 25 or more levels below a guardian can't hurt them at all.
            if (Data.Progression.Career == Career.Guardian && info.attacker != null && info.attacker.TryGetComponent<EnemyController>(out var foe)
                && foe.Level <= Data.Progression.Level - GuardianImmuneGap) return false;
            // Armour / rings: a chance to shrug the hit off completely.
            if (!info.unblockable && !health.IsInvulnerable && Random.Range(0, 100) < Data.Stats.Block) // [MONSTER] unblockable skips the roll
            {
                Fx.Sparkle(Center + Vector2.up * 0.3f, 3, 0.35f);
                Game.Audio.PlaySfx("mine", IsLocal ? 1f : 0.5f);
                if (IsLocal) GameEvents.RaiseToast("막았습니다!");
                return false;
            }
            int guard = Data.GuardReduction; // class passive (was 철벽 / 마나 보호막)
            if (guard > 0) info.amount = Mathf.Max(1, Mathf.RoundToInt(info.amount * (1f - guard / 100f)));
            if(health.IsInvulnerable) return false;
            info.amount = CareerCombat.For(this).Absorb(info.amount);
            if(info.amount<=0) return true;
            if (!health.TryDamage(info)) return false;
            CancelMobility();
            Game.Party?.RecordDamageTaken(this, info);

            Vector2 away = Position - info.sourcePosition;
            if (away.sqrMagnitude < 0.0001f) away = -Facing.ToVector();
            knockbackVelocity = away.normalized * Mathf.Max(info.knockback, stats.knockbackSpeed);
            knockbackUntil = Time.time + stats.knockbackDuration;
            combat.Cancel();

            flash.Flash(0.1f);
            flash.Blink(stats.invulnerableTime);
            Game.Audio.PlaySfx("hurt", IsLocal ? 1f : 0.5f);
            if (IsLocal) Game.Camera?.Shake(0.12f, 0.18f);

            if (health.IsDead) Die();
            return true;
        }

        void Die()
        {
            CancelMobility();
            IsDead = true;
            desiredVelocity = Vector2.zero;
            animator.Play(CharacterAnim.Hurt, Facing);
            visual.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Game.Audio.PlaySfx("player_down", IsLocal ? 1f : 0.6f);
            Game.Party?.NotifyDowned(this);
            // The local player's death is the game's business (game over, or a dungeon's revive rules).
            if (IsLocal) Game.Flow.OnPlayerDied();
        }

        public void HealFull() => health.Heal(health.Max);

        public void AddMaxHealth(int amount)
        {
            Data.BaseMaxHp += amount;
            RefreshStats();
        }

        void OnHealthChanged(int current, int max)
        {
            if (!IsLocal) return;
            Game.Session.PlayerHealth = current;
            GameEvents.RaisePlayerHealthChanged(current, max);
        }

        public void FaceTowards(Vector2 point)
        {
            Facing = FacingExtensions.FromVector(point - Position, Facing);
            AimDirection = SnapTo8(point - Position);
            animator.Play(CharacterAnim.Idle, Facing);
        }
    }
}
