using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[CH3 SPEC] Helpers of the warrior line's specializations that need the caster's own state.</summary>
    public partial class SkillCaster
    {
        /// <summary>단죄 도약 B: a kill by the skill makes it ready again at once.</summary>
        public void ResetSkillCooldown(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return;
            skillReady.Remove(skillId);
            for (int slot = 0; slot < readyAt.Length; slot++)
            {
                var g = Prog.Active(slot);
                if (g != null && g.id == skillId) readyAt[slot] = 0f;
            }
            if (owner.IsLocal) SkillVisuals.Flash(owner.Center, new Color(SkillSpecs.ColorB.r, SkillSpecs.ColorB.g, SkillSpecs.ColorB.b, .6f), 1.4f, .2f);
        }

        /// <summary>The spec colour of a base skill's effect, else <paramref name="own"/>.</summary>
        static Color SpecTint(SkillNumbers n, Color own) => SkillSpecs.Tint(n, own);

        /// <summary>[CH3 SPEC] 파쇄 연파: the first wave skips the struck target (it was just hit), the second takes it too.</summary>
        IEnumerator CrushWaves(SkillNumbers n, EnemyController struck, Vector2 ground)
        {
            int cap = n.targets > 0 ? n.targets : 5;
            for (int wave = 0; wave < 2; wave++)
            {
                if (wave == 1) yield return new WaitForSeconds(0.17f); // past the monsters' hit invulnerability
                if (owner == null || owner.IsDead) yield break;
                Vector2 at = struck != null && !struck.IsDead ? struck.Center : ground;
                CareerFx.Clip("impact", at + Vector2.down * 0.3f, Vector2.zero, n.radius / 1.4f, 26f, VfxLayer.Ground, false, SkillSpecs.ColorB);
                int count = 0;
                foreach (var e in EnemiesInRadius(at, n.radius))
                {
                    if (wave == 0 && e == struck) { count++; continue; }
                    if (count++ >= cap) break;
                    Hit(e, n, at, 4f);
                }
                Game.Audio.PlaySfx("rock_break", 0.5f);
            }
        }

        void Spin(SkillNumbers n, Vector2 c, Color? tint)
        {
            Game.Audio.PlaySfx("swing");
            // [VFX] Four crescents around the body make one full turn of the blade.
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad;
                CareerFx.Clip("f_arc", c, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), n.radius / 1.35f, 40f, VfxLayer.Top, true, tint).FlipY(k % 2 == 1);
            }
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 7f);
                CareerFx.Hit(e.Center, (e.Center - c).normalized, Career.Fighter, 0);
            }
            Shake(0.06f, 0.12f);
        }

        IEnumerator WhirlSpins(SkillNumbers n)
        {
            bool pull = n.spec == 'a';
            int spins = Mathf.Max(1, n.hits);
            if (pull) owner.LockMovement(0.9f);
            else CareerCombat.For(owner).AddGuard(25, 1f);
            for (int i = 0; i < spins; i++)
            {
                if (owner == null || owner.IsDead) yield break;
                Vector2 c = owner.Center;
                if (pull)
                    foreach (var e in EnemiesInRadius(c, n.radius * 1.8f)) e.PullTowards(owner.Position, 6f, 0.3f);
                Spin(n, c, SpecTint(n, Color.white));
                yield return new WaitForSeconds(0.3f);
            }
        }
    }
}
