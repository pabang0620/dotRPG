using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE SPEC] The rogue base skills' specializations (Docs/ch3/SKILL_SPECIALIZATION.md 3.6). Damage numbers come in
    /// through the gem (SkillSpecs.Rogue); these are the behaviours and timings the gem fields cannot hold.
    /// </summary>
    public partial class SkillCaster
    {
        /// <summary>심장 꿰기 A: x1.25 on a monster under half health.</summary>
        const float StabHeartLow = 1.25f;
        /// <summary>그림자 일격 도약 A: the last monster cut is staggered.</summary>
        const float LeapHeavyStagger = .5f;
        /// <summary>잔영 도약 B: the shadow left on each monster cuts twice, 1 s and 1.5 s later, 0.15 each (of the 0.7 cut).</summary>
        const float LeapTrailShare = .15f / .7f, LeapTrailFirst = 1f, LeapTrailGap = .5f;
        /// <summary>급습 A: a backstab on the raided monster counts as from behind for 2 s.</summary>
        const float RaidOpenWindow = 2f;
        /// <summary>그림자 귀환 B: 0.5 s untouchable, back to the start spot 1 s after the cut.</summary>
        const float RaidReturnGuard = .5f, RaidReturnDelay = 1f;
        /// <summary>일도난무 A: three heavy cuts 0.25 s apart, the last one staggers for 1 s.</summary>
        const float DanceHeavyGap = .25f, DanceHeavyStagger = 1f;

        /// <summary>부채 찌르기 B: every monster in a 90° fan in front (up to 4), the aimed one first.</summary>
        void StabFan(SkillNumbers n, EnemyController first)
        {
            int cap = n.targets > 0 ? n.targets : 4;
            float half = (float.IsNaN(n.spreadDeg) ? 90f : n.spreadDeg) * .5f;
            CareerFx.Clip("f_arc", owner.Center, castAim, n.range / 1.35f, 40f, VfxLayer.Top, true, SpecTint(n, RogueGreen));
            int count = 0;
            if (first != null) { Hit(first, n, owner.Center, 3f); count++; }
            foreach (var e in EnemiesInRadius(owner.Center, n.range + .35f))
            {
                if (count >= cap) break;
                if (e == first || Vector2.Angle(castAim, e.Center - owner.Center) > half) continue;
                Hit(e, n, owner.Center, 3f);
                count++;
            }
            Shake(0.05f, 0.08f);
        }

        IEnumerator LeapTrail(SkillNumbers n, EnemyController e)
        {
            int cut = Mathf.Max(1, Mathf.RoundToInt(n.damage * LeapTrailShare));
            var shade = new Color(SkillSpecs.ColorB.r, SkillSpecs.ColorB.g, SkillSpecs.ColorB.b, .7f);
            yield return new WaitForSeconds(LeapTrailFirst);
            for (int i = 0; i < 2; i++)
            {
                if (e == null || e.IsDead || owner == null || owner.IsDead) yield break;
                var trail = n;
                trail.damage = cut;
                CareerFx.Clip("f_x", e.Center, Random.insideUnitCircle.normalized, .7f, 30f, VfxLayer.Top, true, shade);
                CareerCombat.For(owner).Uncopied(() => HitRapid(e, trail, e.Center, 0f)); // [NINJA] part of 연쇄 도약: not copied
                if (i == 0) yield return new WaitForSeconds(LeapTrailGap);
            }
        }

        /// <summary>회전 백어택 B: up to 3 monsters within reach; each one I stand behind is stunned 0.55 s, the rest get the shallow stab.</summary>
        void BackstabSpin(SkillNumbers n)
        {
            int cap = n.targets > 0 ? n.targets : 3;
            float stun = float.IsNaN(n.duration) ? n.stun : n.duration;
            CareerFx.Clip("f_arc", owner.Center, castAim, n.range / 1.35f, 40f, VfxLayer.Top, true, SpecTint(n, RogueGreen));
            var front = n;
            front.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * BackstabFrontShare));
            int count = 0;
            foreach (var e in EnemiesInRadius(owner.Center, n.range + .35f))
            {
                if (count++ >= cap) break;
                bool behind = e.IsBehind(owner.Position) || (e == raidMark && Time.time < raidMarkUntil);
                if (!behind) { Hit(e, front, owner.Center, 3f); continue; }
                CareerFx.Gen("r_backstab", "f_x", e.Center, (e.Center - owner.Center).normalized, 1.1f, 1f, tint: SpecTint(n, RogueGreen));
                Hit(e, n, owner.Center, 4f);
                e.Stun(stun);
                StunStars.Attach(e);
            }
            Shake(0.08f, 0.12f);
        }

        IEnumerator RaidReturn(Vector2 origin)
        {
            owner.Health.SetInvulnerable(RaidReturnGuard);
            yield return new WaitForSeconds(RaidReturnDelay);
            if (owner == null || owner.IsDead || owner.NetPuppet) yield break;
            CareerFx.Ghost(owner, ShadowTint);
            owner.SkillLeapTo(origin, origin + castAim);
        }
    }
}
