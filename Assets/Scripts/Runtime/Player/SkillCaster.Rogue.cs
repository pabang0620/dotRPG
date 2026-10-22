using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE] The rogue's base skills and the Shadow Dash hits (Docs/PLAN_CLASS_ROGUE.md 2.2). Numbers come from the gems
    /// (SkillData) like every base skill; the constants below are the timings and shares the gem fields cannot hold.
    /// Leap, backstab, dance and the dash are the user's skills: their behaviour and numbers follow the plan exactly.
    /// </summary>
    public partial class SkillCaster
    {
        /// <summary>연쇄 도약: at most five monsters, each once; 0.1 s jump + cut per monster, back to the cast spot 0.12 s after the last.</summary>
        public const int LeapMaxTargets = 5;
        const float LeapJump = .1f, LeapCut = .02f, LeapReturn = .12f;
        /// <summary>백어택 from the front: 60% of a basic stab (0.39 of attack) = 0.4 of the behind value (0.975).</summary>
        public const float BackstabFrontShare = .4f;
        /// <summary>그림자 습격: for this long after arriving, a backstab on that monster counts as from behind.</summary>
        public const float RaidBehindWindow = 1f;
        /// <summary>그림자 질주: 4 hits of 0.2 x attack on the monsters passed, 0.6 / 0.77 / 0.94 / 1.11 s after passing them,
        /// along a 0.9 m wide path. The 0.17 s gap is longer than a monster's 0.15 s hit invulnerability.</summary>
        public const float ShadowDashHitScale = .2f, ShadowDashFirstHit = .6f, ShadowDashHitGap = .17f, ShadowDashHalfWidth = .45f;
        public const int ShadowDashHits = 4;
        /// <summary>천인난무: cut-in 0.35 s, dash in 0.2 s, 12 cuts 0.07 s apart, closing pose 0.45 s (1.77 s in all).</summary>
        const float DanceCutIn = .35f, DanceDashIn = .2f, DanceGap = .07f, DanceFinish = .45f;

        static readonly Color RogueGreen = new Color(.31f, .78f, .47f, 1f); // #50c878
        static readonly Color ShadowTint = new Color(.45f, .55f, 1f, .6f);

        EnemyController raidMark;
        float raidMarkUntil, shadowDashGuard;

        /// <summary>The monster in front within <paramref name="range"/> (nearest, leaning towards the aim), like 파쇄 일격.</summary>
        EnemyController FrontTarget(float range)
        {
            Vector2 c = owner.Center, aim = castAim;
            EnemyController target = null;
            float best = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                Vector2 to = e.Center - c;
                float d = to.magnitude;
                if (d > range + 0.35f * e.Size) continue;
                float score = d - Vector2.Dot(to.normalized, aim) * 0.6f; // in front first
                if (score < best) { best = score; target = e; }
            }
            return target;
        }

        /// <summary>A hit that leaves no hit invulnerability, so cuts closer than 0.15 s all land (천인난무).</summary>
        void HitRapid(EnemyController enemy, SkillNumbers n, Vector2 from, float knockback)
        {
            if (enemy == null || enemy.IsDead) return;
            int damage = BossScaled(enemy, n, n.damage);
            bool landed = enemy.TakeDamage(new DamageInfo(damage, from, knockback, Team.Player, owner.gameObject) { noHitInvulnerability = true });
            if (landed) SkillVisuals.Sparks(enemy.Center, SkillVisuals.WhirlGold, 3, 3f);
            if (landed && n.leechPct > 0) owner.Heal(Mathf.Max(1, damage * n.leechPct / 100));
        }

        void HoldCast(float seconds) => castEnd = Mathf.Max(castEnd, Time.time + seconds);

        /// <summary>급소 찌르기: one deep stab on the monster in front, no shockwave.</summary>
        void Stab(SkillNumbers n)
        {
            var target = FrontTarget(n.range);
            Game.Audio.PlaySfx("swing");
            if (target == null)
            {
                CareerFx.Clip("f_x", owner.Center + castAim * Mathf.Min(1f, n.range), castAim, .7f, 30f, VfxLayer.Top, true, RogueGreen);
                return;
            }
            if (n.spec == 'b') { StabFan(n, target); return; } // [ROGUE SPEC] 부채 찌르기
            if (n.spec == 'a' && target.Health.Current < target.Health.Max * .5f) n.damage = Mathf.RoundToInt(n.damage * StabHeartLow); // [ROGUE SPEC] 심장 꿰기
            CareerFx.Clip("f_x", target.Center, castAim, 1.1f, 30f, VfxLayer.Top, true, n.spec != '\0' ? SpecTint(n, RogueGreen) : (Color?)null);
            Hit(target, n, owner.Center, 5f);
            Shake(0.06f, 0.1f);
        }

        /// <summary>연쇄 도약: jump from monster to monster within reach of the cast spot, cutting each once, then come back.</summary>
        IEnumerator Leap(SkillNumbers n)
        {
            Vector2 origin = owner.Position, searchFrom = owner.Center;
            var done = new HashSet<EnemyController>();
            EnemyController last = null;
            float total = LeapMaxTargets * (LeapJump + LeapCut) + LeapReturn + .05f;
            HoldCast(total);
            owner.HoldSteady(total);
            Game.Audio.PlaySfx("swing");
            for (int k = 0; k < LeapMaxTargets; k++)
            {
                if (owner.IsDead) yield break;
                // Next: the nearest monster not cut yet, among those within the radius of the cast spot.
                EnemyController next = null;
                float best = float.MaxValue;
                foreach (var e in EnemiesInRadius(searchFrom, n.radius))
                {
                    if (done.Contains(e)) continue;
                    float d = Vector2.Distance(owner.Center, e.Center);
                    if (d < best) { best = d; next = e; }
                }
                if (next == null) break;
                done.Add(next);
                CareerFx.Ghost(owner, ShadowTint);
                Vector2 side = owner.Position - next.Position;
                if (side.sqrMagnitude < .0001f) side = -castAim;
                Vector2 landing = next.Position + side.normalized * (.3f + .35f * next.Size);
                if (!owner.NetPuppet) owner.SkillLeapTo(landing, next.Position);
                yield return new WaitForSeconds(LeapJump);
                if (next != null && !next.IsDead)
                {
                    Vector2 dir = (next.Center - owner.Center).normalized;
                    CareerFx.Clip("f_x", next.Center, dir, .9f, 30f, VfxLayer.Top, true, n.spec != '\0' ? SpecTint(n, RogueGreen) : (Color?)null);
                    Hit(next, n, owner.Center, 4f);
                    if (n.spec == 'b') StartCoroutine(LeapTrail(n, next)); // [ROGUE SPEC] 잔영 도약
                    last = next;
                }
                yield return new WaitForSeconds(LeapCut);
            }
            if (done.Count == 0) yield break;
            if (n.spec == 'a' && last != null && !last.IsDead) last.Stagger(LeapHeavyStagger); // [ROGUE SPEC] 그림자 일격 도약
            yield return new WaitForSeconds(LeapReturn);
            CareerFx.Ghost(owner, ShadowTint);
            if (!owner.NetPuppet && !owner.IsDead) owner.SkillLeapTo(origin, origin + castAim);
            Shake(0.06f, 0.12f);
        }

        /// <summary>백어택: from behind a 0.8 s stun (the usual crowd-control rules: repeats halve, bosses x0.3 then immune), else a shallow stab.</summary>
        void Backstab(SkillNumbers n)
        {
            var target = FrontTarget(n.range);
            Game.Audio.PlaySfx("swing");
            if (target == null)
            {
                CareerFx.Clip("f_x", owner.Center + castAim * Mathf.Min(1f, n.range), castAim, .6f, 30f, VfxLayer.Top, true, RogueGreen);
                return;
            }
            if (n.spec == 'b') { BackstabSpin(n); return; } // [ROGUE SPEC] 회전 백어택
            bool behind = target.IsBehind(owner.Position) || (target == raidMark && Time.time < raidMarkUntil);
            if (behind)
            {
                CareerFx.Gen("r_backstab", "f_x", target.Center, castAim, 1.4f, 1.3f);
                Hit(target, n, owner.Center, 4f);
                target.Stun(n.spec == 'a' && !float.IsNaN(n.duration) ? n.duration : n.stun); // [ROGUE SPEC] 심장 뜯기 1.6 s // not Stagger: bosses only get 0.24 s and then 8 s of immunity
                StunStars.Attach(target);
                Shake(0.1f, 0.14f);
                return;
            }
            var front = n;
            front.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * BackstabFrontShare));
            CareerFx.Clip("f_x", target.Center, castAim, .8f, 30f, VfxLayer.Top, true);
            Hit(target, front, owner.Center, 3f);
        }

        /// <summary>The monster 그림자 습격 would jump behind (nearest within the range), or null: then the skill is not cast.</summary>
        EnemyController RaidTarget(SkillNumbers n) => Nearest(owner.Center, n.range, null);

        /// <summary>그림자 습격: blink behind the nearest monster and cut it; a backstab on it counts as from behind for 1 s.</summary>
        void Raid(SkillNumbers n)
        {
            var target = RaidTarget(n);
            if (target == null) return;
            Vector2 origin = owner.Position;
            CareerFx.Ghost(owner, ShadowTint);
            Vector2 back = target.NetFacing.ToVector();
            Vector2 landing = target.Position - back * (.3f + .35f * target.Size);
            if (!owner.NetPuppet) owner.SkillLeapTo(landing, target.Position);
            CareerFx.Ghost(owner, ShadowTint);
            Game.Audio.PlaySfx("swing");
            Vector2 dir = (target.Center - owner.Center).sqrMagnitude > .0001f ? (target.Center - owner.Center).normalized : castAim;
            CareerFx.Clip("f_x", target.Center, dir, 1.1f, 30f, VfxLayer.Top, true);
            Hit(target, n, owner.Center, 4f);
            raidMark = target;
            raidMarkUntil = Time.time + (n.spec == 'a' ? RaidOpenWindow : RaidBehindWindow); // [ROGUE SPEC] 급습 2 s
            if (n.spec == 'b') StartCoroutine(RaidReturn(origin)); // [ROGUE SPEC] 그림자 귀환
            Shake(0.08f, 0.12f);
        }

        /// <summary>천인난무 (awakening): dash in and cut twelve times around the area, taking turns on the monsters in it.</summary>
        IEnumerator Dance(SkillNumbers n, bool cutIn)
        {
            float gap = n.spec == 'a' ? DanceHeavyGap : DanceGap; // [ROGUE SPEC] 일도난무: three slow heavy cuts
            HoldCast((cutIn ? DanceCutIn : 0f) + DanceDashIn + n.hits * gap + DanceFinish);
            if (cutIn)
            {
                Awaken(L.T("천인난무"), RogueGreen);
                yield return new WaitForSeconds(DanceCutIn);
            }
            Vector2 center = owner.Center;
            var first = Nearest(center, n.range, null);
            if (first != null && !owner.NetPuppet)
            {
                CareerFx.Ghost(owner, ShadowTint);
                Vector2 side = owner.Position - first.Position;
                if (side.sqrMagnitude < .0001f) side = -castAim;
                owner.SkillLeapTo(first.Position + side.normalized * (.3f + .35f * first.Size), first.Position);
            }
            yield return new WaitForSeconds(DanceDashIn);
            for (int i = 0; i < n.hits; i++)
            {
                if (owner.IsDead) yield break;
                var inArea = EnemiesInRadius(center, n.range);
                Vector2 at;
                if (inArea.Count > 0) at = inArea[i % inArea.Count].Center;
                else { float a = Random.Range(0f, Mathf.PI * 2f); at = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(.5f, Mathf.Max(.6f, n.range * .6f)); }
                float turn = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                CareerFx.Gen("r_dance_slash", "f_x", at, new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)), n.spec == 'a' ? 2.2f : 1.6f, 1f, tint: n.spec != '\0' ? SpecTint(n, RogueGreen) : (Color?)null);
                Game.Audio.PlaySfx("swing", 0.5f);
                bool lastCut = n.spec == 'a' && i == n.hits - 1;
                foreach (var e in EnemiesInRadius(at, n.radius))
                {
                    HitRapid(e, n, center, 1f);
                    if (lastCut && !e.IsDead) e.Stagger(DanceHeavyStagger);
                }
                if (i % 3 == 0 || n.spec == 'a') Shake(0.05f, 0.06f);
                yield return new WaitForSeconds(gap);
            }
            yield return new WaitForSeconds(DanceFinish);
            SkillVisuals.UltFinish(owner.Center, RogueGreen, n.range);
        }

        /// <summary>
        /// [ROGUE] 그림자 질주 started (PlayerController.TryMobility, or the host replaying a member's Shift): note every monster
        /// the body passes within half the path width, then cut each 4 times from 0.6 s after it was passed.
        /// </summary>
        public void ShadowDash(Vector2 direction)
        {
            if (!isActiveAndEnabled || owner == null || owner.IsDead || owner.NetPuppet || Time.time < shadowDashGuard) return;
            shadowDashGuard = Time.time + PlayerController.ShadowDashCooldown * .9f;
            StartCoroutine(ShadowDashRoutine(direction));
        }

        IEnumerator ShadowDashRoutine(Vector2 direction)
        {
            var passed = new Dictionary<EnemyController, float>();
            float speed = PlayerController.DashDistance / PlayerController.DashDuration;
            float track = PlayerController.ShadowDashDistance / speed + .06f;
            float end = Time.time + track;
            Vector2 last = owner.Center;
            var tint = new Color(ShadowTint.r, ShadowTint.g, ShadowTint.b, .45f);
            float ghost = 0f;
            while (Time.time < end || owner.IsDashing)
            {
                if (owner == null || owner.IsDead) yield break;
                Vector2 now = owner.Center;
                // The path between two frames counts too (a fast dash covers ~0.25 m a frame).
                foreach (var e in EnemyController.Active)
                {
                    if (e == null || e.IsDead || !e.isActiveAndEnabled || passed.ContainsKey(e)) continue;
                    if (DistanceToSegment(e.Center, last, now) <= ShadowDashHalfWidth + .2f * e.Size) passed[e] = Time.time;
                }
                last = now;
                ghost -= Time.deltaTime;
                if (ghost <= 0f) { ghost = .04f; CareerFx.Ghost(owner, tint); }
                if (Time.time > end + 1f) break; // never hang on a stuck dash
                yield return null;
            }
            foreach (var pair in passed) StartCoroutine(ShadowCuts(pair.Key, pair.Value, direction));
        }

        IEnumerator ShadowCuts(EnemyController e, float passedAt, Vector2 direction)
        {
            for (int k = 0; k < ShadowDashHits; k++)
            {
                float at = passedAt + ShadowDashFirstHit + k * ShadowDashHitGap;
                if (Time.time < at) yield return new WaitForSeconds(at - Time.time);
                if (e == null || e.IsDead || owner == null || owner.IsDead) yield break;
                int damage = Mathf.Max(1, Mathf.RoundToInt(owner.Data.Stats.AttackDamage(owner.Class) * ShadowDashHitScale));
                if (e.IsBoss) damage = Mathf.RoundToInt(damage * owner.Data.Stats.BossMul); // [LEGEND] 토벌형 weapon
                bool landed = e.TakeDamage(new DamageInfo(damage, e.Center - direction, 1f, Team.Player, owner.gameObject));
                if (landed) CareerFx.Clip("f_x", e.Center, k % 2 == 0 ? direction : -direction, .7f, 30f, VfxLayer.Top, true, ShadowTint);
            }
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < .0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
