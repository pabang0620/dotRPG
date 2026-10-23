using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [NINJA 2026-10-23] The promoted ninja's thrown versions of the rogue base attacks (Docs/PLAN_CLASS_ROGUE.md 2.4).
    /// User: "닌자는 전부 원거리 ... 다 던지는 것만". Same gems, numbers, cooldowns and MP as the dagger versions; only the
    /// delivery changes: 급소 투척 (one heavy kunai), 튕기는 쿠나이 (a kunai bouncing over up to 5 monsters), 등 뒤 저격
    /// (a kunai that stuns when it lands on a monster's back), 그림자 표창 (a star flying at the nearest monster).
    /// They are attacks, not moves, so the 분신술 clones copy them (CareerCombat.CloneEcho). Kunai that stop stay 4 s for 회수.
    /// </summary>
    public partial class SkillCaster
    {
        const float NinjaThrowSpeed = 18f, NinjaKunaiHalf = .3f, NinjaStarSpeed = 16f;
        /// <summary>How long a thrown kunai stays where it stopped (the basic throw's 4 s), for 회수.</summary>
        const float NinjaKunaiStay = 4f;
        /// <summary>급소 투척 throws a bigger kunai (the picture only).</summary>
        const float NinjaHeavyScale = 1.35f;
        /// <summary>그림자 귀환 (raid B) on the ninja: a 2 m hop back with the throw instead of a blink and a return.</summary>
        const float NinjaRaidHop = 2f;

        /// <summary>True when this cast of <paramref name="gemId"/> is the ninja's thrown version.</summary>
        bool NinjaThrowCast(string gemId) => owner.Class == CharacterClass.Rogue && SkillGems.NinjaThrows(gemId) && CareerCombat.For(owner).ThrowsKunai;

        float NinjaReach => CareerCombat.For(owner).ThrowReach;

        bool NinjaStars => CareerCombat.For(owner).ThrowsStars;

        void NinjaPose() => owner.GetComponent<CharacterAnimator>()?.BeginCareerPose(.16f, 0, 0, WarriorAttackMotion.Contact);

        /// <summary>The monster nearest <paramref name="a"/> on the segment a-b (a kunai's step).</summary>
        static EnemyController FirstOnSegment(Vector2 a, Vector2 b)
        {
            EnemyController best = null;
            float bestDist = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                if (DistanceToSegment(e.Center, a, b) > NinjaKunaiHalf + .25f * e.Size) continue;
                float d = (e.Center - a).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = e; }
            }
            return best;
        }

        /// <summary>
        /// One kunai along <paramref name="dir"/>: <paramref name="onHit"/> runs on the first monster it meets. It stops at
        /// walls and stays where it stops, like the basic throw.
        /// </summary>
        IEnumerator ThrowLine(Vector2 dir, float range, float scale, System.Action<EnemyController> onHit)
        {
            Vector2 at = owner.Center + dir * .3f;
            var k = KunaiProjectile.Spawn(owner, at, dir, NinjaStars, null, scale);
            float travelled = 0f;
            while (travelled < range)
            {
                if (k == null || owner == null || owner.IsDead) { if (k != null) k.Vanish(); yield break; }
                float step = Mathf.Min(NinjaThrowSpeed * Time.deltaTime, range - travelled);
                Vector2 next = at + dir * step;
                if (Blocked(next)) break;
                var victim = FirstOnSegment(at, next);
                if (victim != null)
                {
                    k.Place(victim.Center);
                    onHit(victim);
                    break;
                }
                k.Place(next);
                at = next;
                travelled += step;
                yield return null;
            }
            if (k != null) k.Stick(NinjaKunaiStay, true);
        }

        /// <summary>Moves <paramref name="k"/> from <paramref name="from"/> onto <paramref name="target"/> in <paramref name="seconds"/>, following it.</summary>
        static IEnumerator FlyTo(KunaiProjectile k, Vector2 from, EnemyController target, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (k == null || target == null || target.IsDead) yield break;
                Vector2 to = target.Center;
                k.Aim(to - k.Position);
                k.Place(Vector2.Lerp(from, to, t / seconds));
                yield return null;
            }
            if (k != null && target != null) k.Place(target.Center);
        }

        /// <summary>A kunai (or star) thrown at <paramref name="target"/>; <paramref name="onHit"/> runs when it gets there.</summary>
        IEnumerator ThrowAt(EnemyController target, float speed, bool star, System.Action<EnemyController> onHit)
        {
            if (target == null) yield break;
            Vector2 from = owner.Center;
            var k = KunaiProjectile.Spawn(owner, from, target.Center - from, star || NinjaStars);
            yield return FlyTo(k, from, target, Mathf.Max(.05f, Vector2.Distance(from, target.Center) / speed));
            if (k == null) yield break;
            if (target != null && !target.IsDead && owner != null && !owner.IsDead) onHit(target);
            k.Stick(NinjaKunaiStay, true);
        }

        // ---------- 급소 투척 (급소 찌르기) ----------

        void NinjaStab(SkillNumbers n)
        {
            Game.Audio.PlaySfx("swing");
            NinjaPose();
            Vector2 aim = castAim;
            if (n.spec == 'b') { NinjaStabFan(n, aim); return; } // [ROGUE SPEC] 부채 찌르기: a fan of kunai
            StartCoroutine(ThrowLine(aim, NinjaReach, NinjaHeavyScale, e =>
            {
                var m = n;
                if (m.spec == 'a' && e.Health.Current < e.Health.Max * .5f) m.damage = Mathf.RoundToInt(m.damage * StabHeartLow); // [ROGUE SPEC] 심장 꿰기
                CareerFx.Clip("f_x", e.Center, aim, 1f, 30f, VfxLayer.Top, true, m.spec != '\0' ? SpecTint(m, RogueGreen) : (Color?)null);
                Hit(e, m, owner.Center, 5f);
                Shake(0.06f, 0.1f);
            }));
        }

        /// <summary>부채 찌르기 B thrown: up to 4 kunai spread over the 90° fan, each sticks in the first monster.</summary>
        void NinjaStabFan(SkillNumbers n, Vector2 aim)
        {
            int count = n.targets > 0 ? n.targets : 4;
            float spread = float.IsNaN(n.spreadDeg) ? 90f : n.spreadDeg;
            for (int i = 0; i < count; i++)
            {
                float a = count == 1 ? 0f : -spread * .5f + spread * i / (count - 1);
                StartCoroutine(ThrowLine(CareerFx.Tilt(aim, a), NinjaReach, 1f, e => Hit(e, n, owner.Center, 3f)));
            }
        }

        // ---------- 튕기는 쿠나이 (연쇄 도약) ----------

        /// <summary>
        /// One kunai jumps from monster to monster around the cast spot (nearest not hit yet, each once, up to 5), with the
        /// leap's timing (0.1 s a hop + 0.02 s), so the 0.5 s recharge and the MP share pace it the same way. The body stays.
        /// </summary>
        IEnumerator NinjaLeap(SkillNumbers n)
        {
            Vector2 searchFrom = owner.Center;
            int planned = Mathf.Clamp(EnemiesInRadius(searchFrom, n.radius).Count, 1, LeapMaxTargets);
            HoldCast(planned * (LeapJump + LeapCut) + LeapReturn + .05f);
            Game.Audio.PlaySfx("swing");
            NinjaPose();
            var done = new HashSet<EnemyController>();
            EnemyController last = null;
            KunaiProjectile k = null;
            Vector2 at = owner.Center;
            for (int i = 0; i < planned; i++)
            {
                if (owner == null || owner.IsDead) break;
                EnemyController next = null;
                float best = float.MaxValue;
                foreach (var e in EnemiesInRadius(searchFrom, n.radius))
                {
                    if (done.Contains(e)) continue;
                    float d = Vector2.Distance(at, e.Center);
                    if (d < best) { best = d; next = e; }
                }
                if (next == null) break;
                done.Add(next);
                if (k == null) k = KunaiProjectile.Spawn(owner, at, next.Center - at, NinjaStars);
                yield return FlyTo(k, at, next, LeapJump);
                if (k == null) yield break;
                if (next != null && !next.IsDead)
                {
                    Vector2 dir = next.Center - at;
                    dir = dir.sqrMagnitude > .0001f ? dir.normalized : castAim;
                    CareerFx.Clip("f_x", next.Center, dir, .8f, 30f, VfxLayer.Top, true, n.spec != '\0' ? SpecTint(n, RogueGreen) : (Color?)null);
                    Hit(next, n, at, 3f);
                    if (n.spec == 'b') StartCoroutine(LeapTrail(n, next)); // [ROGUE SPEC] 잔영 도약
                    last = next;
                    at = next.Center;
                }
                else at = k.Position;
                yield return new WaitForSeconds(LeapCut);
            }
            if (n.spec == 'a' && last != null && !last.IsDead) last.Stagger(LeapHeavyStagger); // [ROGUE SPEC] 그림자 일격 도약
            if (k != null) k.Stick(NinjaKunaiStay, true);
        }

        // ---------- 등 뒤 저격 (백어택) ----------

        void NinjaBackstab(SkillNumbers n)
        {
            Game.Audio.PlaySfx("swing");
            NinjaPose();
            if (n.spec == 'b') { NinjaBackstabSpin(n); return; } // [ROGUE SPEC] 회전 백어택: three kunai
            float stun = n.spec == 'a' && !float.IsNaN(n.duration) ? n.duration : n.stun; // [ROGUE SPEC] 심장 뜯기 1.6 s
            StartCoroutine(ThrowLine(castAim, NinjaReach, 1f, e => BackstabLanded(e, n, stun)));
        }

        /// <summary>회전 백어택 B thrown: a kunai at each of the 3 nearest monsters in reach, each judged on its own.</summary>
        void NinjaBackstabSpin(SkillNumbers n)
        {
            int cap = n.targets > 0 ? n.targets : 3;
            float stun = float.IsNaN(n.duration) ? n.stun : n.duration;
            int count = 0;
            foreach (var e in EnemiesByDistance(NinjaReach))
            {
                if (count++ >= cap) break;
                StartCoroutine(ThrowAt(e, NinjaThrowSpeed, false, hit => BackstabLanded(hit, n, stun)));
            }
        }

        /// <summary>The kunai of 등 뒤 저격 landed: on the monster's back the full hit and the stun (the usual crowd-control rules), else 40%.</summary>
        void BackstabLanded(EnemyController e, SkillNumbers n, float stun)
        {
            Vector2 dir = e.Center - owner.Center;
            dir = dir.sqrMagnitude > .0001f ? dir.normalized : castAim;
            bool behind = e.IsBehind(owner.Position) || (e == raidMark && Time.time < raidMarkUntil);
            if (behind)
            {
                CareerFx.Gen("r_backstab", "f_x", e.Center, dir, 1.2f, 1.1f, tint: n.spec != '\0' ? SpecTint(n, RogueGreen) : (Color?)null);
                Hit(e, n, owner.Center, 4f);
                e.Stun(stun);
                StunStars.Attach(e);
                Shake(0.1f, 0.14f);
                return;
            }
            var front = n;
            front.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * BackstabFrontShare));
            CareerFx.Clip("f_x", e.Center, dir, .8f, 30f, VfxLayer.Top, true);
            Hit(e, front, owner.Center, 3f);
        }

        // ---------- 그림자 표창 (그림자 습격) ----------

        /// <summary>A star flies at the nearest monster in range; a 등 뒤 저격 on it counts as from behind for 1 s after it lands.</summary>
        void NinjaRaid(SkillNumbers n)
        {
            var target = RaidTarget(n);
            if (target == null) return;
            Game.Audio.PlaySfx("swing");
            NinjaPose();
            if (n.spec == 'b') // [ROGUE SPEC] 그림자 귀환: the ninja hops back 2 m, 0.5 s untouchable
            {
                owner.Health.SetInvulnerable(RaidReturnGuard);
                CareerFx.Ghost(owner, ShadowTint);
                if (!owner.NetPuppet) owner.SkillDash(-castAim, NinjaRaidHop);
            }
            float window = n.spec == 'a' ? RaidOpenWindow : RaidBehindWindow; // [ROGUE SPEC] 급습 2 s
            StartCoroutine(ThrowAt(target, NinjaStarSpeed, true, e =>
            {
                Vector2 dir = e.Center - owner.Center;
                CareerFx.Clip("f_x", e.Center, dir.sqrMagnitude > .0001f ? dir.normalized : castAim, 1.1f, 30f, VfxLayer.Top, true);
                Hit(e, n, owner.Center, 4f);
                raidMark = e;
                raidMarkUntil = Time.time + window;
                Shake(0.08f, 0.12f);
            }));
        }
    }
}
