using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public partial class SkillCaster
    {
        /// <summary>
        /// [CH3 SPEC] Sets when a skill is ready again (mage specs: 이중 도약 gives a second cast for a few seconds, then the
        /// full cooldown). Reads the same per-skill ready time the HUD and <see cref="IsReady"/> use.
        /// </summary>
        public void SpecSetReady(string skillId, float at)
        {
            if (string.IsNullOrEmpty(skillId)) return;
            skillReady[skillId] = at;
        }

        /// <summary>[CH3 SPEC] When a skill is ready again (0 when it never ran).</summary>
        public float SpecReadyAt(string skillId) => !string.IsNullOrEmpty(skillId) && skillReady.TryGetValue(skillId, out var t) ? t : 0f;

        /// <summary>[CH3 SPEC] Clip tint of a mage cast: the spec colour, or none (the art's own colour) without a spec.</summary>
        static Color? MageTint(SkillNumbers n) => n.spec == 'a' ? SkillSpecs.ColorA : n.spec == 'b' ? SkillSpecs.ColorB : (Color?)null;

        /// <summary>[CH3 SPEC] 절대 영도: when the ice on a monster breaks (thaw or death), it bursts for 1.0 x attack around it.</summary>
        IEnumerator NovaShatter(EnemyController e, SkillNumbers n, float freeze)
        {
            float until = Time.time + freeze + 0.5f;
            yield return new WaitForSeconds(0.1f);
            while (e != null && !e.IsDead && e.IsFrozen && Time.time < until) yield return null;
            if (e == null || owner == null) yield break;
            Vector2 at = e.Center;
            var burst = n;
            burst.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * 2f)); // 서리 폭발 is 0.5 x attack: the burst is 1.0 x
            CareerFx.Clip("m_icebloom", e.Position, Vector2.zero, 1.2f, 22f, VfxLayer.AtFeet, false, MageTint(n));
            SkillVisuals.Sparks(at, SkillSpecs.ColorA, 8, 4f, 0.3f);
            foreach (var t in EnemiesInRadius(at, 1.6f)) Hit(t, burst, at, 3f);
        }

        /// <summary>[CH3 SPEC] 빙뢰 파편: six ice shards fly out of the burst; everything within 1.6x the burst radius is slowed 20% for 2 s.</summary>
        void FrostShards(Vector2 pos, SkillNumbers n)
        {
            float reach = n.radius * 1.6f;
            for (int k = 0; k < 6; k++)
            {
                float a = (k * 60f + 30f) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f);
                SkillFx.Spawn("fx_streak", pos + d * 0.4f, SkillSpecs.ColorB, 0.35f, SkillFx.TopOrder).Move(d * reach / 0.35f, 1f).FaceMotion().Scale(1.1f, 0.35f);
                CareerFx.Clip("m_icebloom", pos + d * reach * 0.85f, Vector2.zero, 0.6f, 24f, VfxLayer.AtFeet, false, MageTint(n));
            }
            foreach (var e in EnemiesInRadius(pos, reach)) e.Slow(0.2f, 2f);
        }

        /// <summary>
        /// [CH3 SPEC] 대운석: a warning ring on the target spot for 1.5 s, one rock that lands at once (the whole meteor in one
        /// hit), then a burning crack that ticks 0.5 x attack a second for 6 s.
        /// </summary>
        IEnumerator BigMeteor(SkillNumbers n)
        {
            Vector2 p = NextTarget(n.range, 0, 1);
            const float warn = 1.5f, fall = 0.38f;
            SkillFx.Spawn("fx_ring", p, new Color(1f, 0.2f, 0.27f, 0.85f), warn, SkillFx.GroundOrder + 14).Scale(new Vector2(n.radius * 0.3f, n.radius * 0.21f), new Vector2(n.radius, n.radius * 0.7f)).Fade(FxFade.Late);
            yield return new WaitForSeconds(warn - fall);
            SkillVisuals.MeteorFall(p, fall, n.radius * 1.3f);
            yield return new WaitForSeconds(fall);
            if (owner == null || owner.IsDead) yield break;
            SkillVisuals.MeteorImpact(p, n.radius * 1.2f);
            CareerFx.Clip("m_explode", p + Vector2.up * 0.3f, Vector2.zero, n.radius / 0.9f, 22f, VfxLayer.Top, false, MageTint(n));
            Game.Audio.PlaySfx("rock_break");
            Shake(0.22f, 0.3f);
            foreach (var e in EnemiesInRadius(p + Vector2.up * 0.3f, n.radius)) Hit(e, n, p, 10f);
            var crack = n;
            crack.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * 0.5f / 22.4f));
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(1f);
                if (owner == null || owner.IsDead) yield break;
                SkillVisuals.Flash(p, new Color(1f, 0.25f, 0.3f, 0.45f), n.radius, 0.3f);
                foreach (var e in EnemiesInRadius(p, n.radius)) Hit(e, crack, p, 0f);
            }
        }
    }
}
