using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE SPEC] 어쌔신·닌자 specializations (Docs/ch3/SKILL_SPECIALIZATION.md 3.6). Career skills keep their built
    /// numbers; these ratios turn the built damage into the design's A / B values (e.g. 환영 일점 3.5 of the 2.74 cut).
    /// With no side chosen ('\0', or chapter 3 closed) every helper returns the old value, so the base kit is unchanged.
    /// </summary>
    public sealed partial class CareerCombat
    {
        // ---------- 어쌔신 ----------
        /// <summary>환영 삼격 A/B (2026-10-23, replaced 쌍인참): each cut 3.5 of the built 2.74 (A three on one line, B one per monster).</summary>
        const float PhantomSpecMul = 3.5f / 2.74f;
        const float RendPointMul = 1.1f / .68f, RendWhirlMul = .37f / .68f;
        const float VeilThroughMul = 4.0f / 2.67f, VeilDoubleShare = 2.4f / 2.67f, VeilThroughHalf = .6f, VeilPierceRange = 8f;
        const int VeilDoubleGuard = 30;
        const float VeilDoubleGuardSeconds = 1.5f, VeilDoubleStun = 1f;
        const float VenomBossScale = 1.8f, VenomSpreadScale = .6f, VenomSpreadRadius = 2f;
        const float RuptureBleedShare = .4f;
        const float AssassinExecMul = 3.6f / 2.91f, AssassinExecLow = .3f, AssassinExecBonus = 1.5f, AssassinBlinkMul = 2.16f / 2.91f, AssassinBlinkGuard = .3f;
        const float StealthDeepScale = 1.8f, StealthThreeScale = .6f;
        const int StealthThreeHits = 3;
        const int VitalCapA = 3, VitalPctA = 7;
        const float VitalLifeA = 8f, VitalPassRadius = 3f;
        const float AmbushSkillMul = 1.25f, AmbushAllMul = 1.08f, OpenStagger = .3f;
        const float FarSnipeDistance = 8f, FarSnipeMul = 1.2f, FarNearDistance = 4f, FarNearMul = 1.08f;

        // ---------- 닌자 ----------
        const float ThrowHeavyGap = .4f, ThrowHeavyScale = 1.33f, ThrowQuickGap = .24f, ThrowQuickScale = .8f;
        /// <summary>분신술 A/B (2026-10-29, user: always up, damage adjusted): the time is the same for every spec; the clones
        /// together keep the base total (2 x 12% = 24%): one clone 2x the share (24%), three clones 2/3 of it (8% each).</summary>
        const float CloneOneShare = 2f, CloneThreeShare = 2f / 3f;
        const float PortPierceMul = 3.2f / 2.11f, PortAnchorMul = 1.92f / 2.11f, PortAnchorGuardSeconds = 1.5f;
        const int PortAnchorGuard = 40;
        const float RecallTargetMul = 1.0f / .79f, RecallWideMul = .33f / .79f, RecallWideHalf = 1.2f;
        const float FumaBigMul = 3.08f / .77f, FumaHoverMul = .385f / .77f;
        const float BombFieldRing = 3f, BombOneMul = 5f, BombFieldMul = .625f;

        readonly HashSet<EnemyController> ambushOpened = new HashSet<EnemyController>();
        readonly HashSet<EnemyController> farOpened = new HashSet<EnemyController>();
        bool pendingFarOpen;

        void RogueSpecReset()
        {
            ambushOpened.Clear();
            farOpened.Clear();
            stealthHitsLeft = 1;
        }

        // ---------- Passives ----------

        /// <summary>급소 간파: 5 marks of +4% (rank +1); 급소 각인 A: 3 marks of +7% that last 8 s.</summary>
        int MarkCap => Spec("a_vital") == 'a' ? VitalCapA : MaxMarks;
        float MarkLife => Spec("a_vital") == 'a' ? VitalLifeA : MarkSeconds;
        int VitalPct(int rank) => Spec("a_vital") == 'a' ? VitalPctA : 4 + (rank - 1);

        void AddMark(EnemyController e, int count)
        {
            if (e == null || e.IsDead) return;
            marks[e] = Mathf.Min(MarkCap, Marks(e) + count);
            marksUntil[e] = Time.time + MarkLife;
        }

        /// <summary>급소 전이 B: a marked monster's marks pass to the nearest other monster within 3 m when it dies.</summary>
        void PassMarks(EnemyController dead)
        {
            if (Spec("a_vital") != 'b') return;
            int n = Marks(dead);
            if (n <= 0) return;
            EnemyController next = null;
            float best = float.MaxValue;
            foreach (var e in Enemies(dead.Center, VitalPassRadius))
            {
                if (e == dead) continue;
                float d = (e.Center - dead.Center).sqrMagnitude;
                if (d < best) { best = d; next = e; }
            }
            if (next == null) return;
            AddMark(next, n);
            CareerFx.Line(dead.Center, next.Center, new Color(SkillSpecs.ColorB.r, SkillSpecs.ColorB.g, SkillSpecs.ColorB.b, .7f), .06f, .2f);
        }

        /// <summary>기습: base +15% from behind (rank +5); 치명 기습 A +25% from behind on career skills only; 그림자 기습 B +8% always.</summary>
        float AmbushMul(EnemyController enemy, bool behind)
        {
            int rank = Prog.Rank("a_ambush");
            if (rank <= 0) return 1f;
            char sp = Spec("a_ambush");
            if (sp == 'a') return behind && !string.IsNullOrEmpty(currentSkill) ? AmbushSkillMul : 1f;
            if (sp == 'b') return AmbushAllMul;
            return behind ? 1f + (15 + 5 * (rank - 1)) / 100f : 1f;
        }

        /// <summary>원거리 집중: base +12% at 6 m+ (rank +4); 저격 A +20% at 8 m+; 산개 집중 B +8% at 4 m+ (and a stagger at 6 m+).</summary>
        float FarMul(EnemyController enemy)
        {
            pendingFarOpen = false;
            int rank = Prog.Rank("n_far");
            if (rank <= 0) return 1f;
            float d = Vector2.Distance(owner.Center, enemy.Center);
            char sp = Spec("n_far");
            if (sp == 'a') return d >= FarSnipeDistance ? FarSnipeMul : 1f;
            if (sp == 'b') { pendingFarOpen = d >= FarDistance; return d >= FarNearDistance ? FarNearMul : 1f; }
            return d >= FarDistance ? 1f + (12 + 4 * (rank - 1)) / 100f : 1f;
        }

        /// <summary>The first hit on a monster from behind (그림자 기습 B) or from 6 m+ (산개 집중 B) staggers it once.</summary>
        void RogueSpecLanded(EnemyController enemy)
        {
            if (enemy == null || enemy.IsDead) return;
            if (pendingBehind && Prog.Rank("a_ambush") > 0 && Spec("a_ambush") == 'b' && ambushOpened.Add(enemy)) enemy.Stagger(OpenStagger);
            if (pendingFarOpen && farOpened.Add(enemy)) enemy.Stagger(OpenStagger);
            pendingFarOpen = false;
        }

        // ---------- 어쌔신 casts ----------

        int PhantomDamage(Run c) => Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec != '\0' ? PhantomSpecMul : 1f)));

        /// <summary>연환 일점 A: two cuts on the one monster in front. 연환 선풍 B: two full turns around me, up to 5 monsters.</summary>
        IEnumerator RendSpec(Run c)
        {
            bool point = c.n.spec == 'a';
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (point ? RendPointMul : RendWhirlMul)));
            int cap = c.n.targets > 0 ? c.n.targets : 5;
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                Vector2 from = owner.Center;
                Pose(.1f, i % 2);
                Sound("c_slash", .5f);
                if (point)
                {
                    var target = Target(c.dir, c.n.range, out Vector2 at);
                    CareerFx.Clip("f_x", target != null ? target.Center : at, CareerFx.Tilt(c.dir, i == 0 ? 20f : -20f), 1.25f, 30f, VfxLayer.Top, true, Tint(c, AssassinPink)); // [VFX 2026-10-29] was .9
                    if (target != null) Strike(c, target, damage, from, 2f, 0, "c_slash");
                }
                else
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (k * 90f + i * 45f) * Mathf.Deg2Rad;
                        CareerFx.Clip("f_arc", from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), c.n.range / 1.3f, 32f, VfxLayer.Top, true, Tint(c, AssassinPink)).FlipY(k % 2 == 1); // [VFX 2026-10-29] was range/1.45 at 40 fps (as Rend)
                    }
                    int count = 0;
                    foreach (var e in Enemies(from, c.n.range))
                    {
                        if (count++ >= cap) break;
                        Strike(c, e, damage, from, 2f, 0, "c_slash");
                    }
                }
                if (i < c.s.hits - 1) yield return new WaitForSeconds(CutGap);
            }
        }

        /// <summary>
        /// 연막 관통탄 A (2026-10-23 ranged, was 연막 돌파's dash): no leap; a smoke kunai flies 8 m forward through the
        /// monsters (4.0 each) and its smoke bursts where it stops (no damage, can't attack 2 s).
        /// </summary>
        IEnumerator SmokeThrough(Run c)
        {
            Vector2 from = owner.Center;
            float length = owner.SkillClearance(c.dir, VeilPierceRange);
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * VeilThroughMul));
            var k = KunaiProjectile.Spawn(owner, from, c.dir, ThrowsStars, Tint(c, Color.white));
            Sound("swing", .8f);
            var hit = new HashSet<EnemyController>();
            float travelled = 0f;
            while (travelled < length)
            {
                if (!Live(c) || k == null) { if (k != null) k.Vanish(); yield break; }
                float step = Mathf.Min(VolleySpeed * Time.deltaTime, length - travelled);
                Vector2 a = from + c.dir * travelled, b = a + c.dir * step;
                foreach (var e in Corridor(a, b, VeilThroughHalf)) if (hit.Add(e)) Strike(c, e, damage, a, 2f, 1, "c_slash");
                travelled += step;
                k.Place(b);
                yield return null;
            }
            Vector2 spot = from + c.dir * length;
            if (k != null) k.Vanish();
            CareerFx.Gen("a_smoke", null, spot, Vector2.zero, c.n.radius * 2f, 0f, 14f, VfxLayer.Top, false, SmokeGray);
            foreach (var e in Enemies(spot, c.n.radius)) Stun(c, e, c.s.duration);
        }

        /// <summary>이중 연막 B: a second smoke where I land (1 s no attacking, no damage) and -30% damage taken for 1.5 s.</summary>
        void SmokeArrival(Run c)
        {
            Vector2 at = owner.Center;
            CareerFx.Gen("a_smoke", null, at, Vector2.zero, c.n.radius * 1.6f, 0f, 14f, VfxLayer.Top, false, Tint(c, SmokeGray));
            foreach (var e in Enemies(at, c.n.radius * .8f)) Stun(c, e, VeilDoubleStun);
            if (c.authority) AddGuard(VeilDoubleGuard, VeilDoubleGuardSeconds);
        }

        /// <summary>맹독 전염 B: the same poison goes to up to 2 more monsters within 2 m.</summary>
        void SpreadPoison(EnemyController from, int poison)
        {
            int left = venomSpread;
            foreach (var e in Enemies(from.Center, VenomSpreadRadius))
            {
                if (left <= 0) break;
                if (e == from || e.IsDead) continue;
                StartCoroutine(Poison(e, poison));
                left--;
            }
        }

        /// <summary>출혈 파열 B: the held-back 40% bleeds over the next seconds, once a second, through armour.</summary>
        IEnumerator Bleed(EnemyController e, int total, float seconds)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(seconds));
            int left = total;
            for (int i = 0; i < ticks; i++)
            {
                yield return new WaitForSeconds(1f);
                if (e == null || e.IsDead || owner == null || owner.IsDead) yield break;
                int tick = i == ticks - 1 ? left : Mathf.Max(1, total / ticks);
                left -= tick;
                if (tick <= 0) yield break;
                SecondaryHit(e, tick, true);
                SkillVisuals.Sparks(e.Center, SkillSpecs.ColorB, 3, 2f, .2f);
            }
        }

        /// <summary>그림자 암살 B: blink behind the target before the stab, untouchable for a moment.</summary>
        void BlinkBehind(Run c, EnemyController target)
        {
            CareerFx.Ghost(owner, AssassinShade, .3f);
            Vector2 back = target.NetFacing.ToVector();
            Vector2 landing = target.Position - back * (.3f + .35f * target.Size);
            if (!owner.NetPuppet) owner.SkillLeapTo(landing, target.Position);
            if (c.authority) owner.Health.SetInvulnerable(AssassinBlinkGuard);
        }

        /// <summary>처형 A: 3.6 (of 2.91), x1.5 under 30% health. 그림자 암살 B: 2.16.</summary>
        float AssassinateSpecMul(Run c, EnemyController target)
        {
            if (c.n.spec == 'a') return AssassinExecMul * (target.Health.Current < target.Health.Max * AssassinExecLow ? AssassinExecBonus : 1f);
            if (c.n.spec == 'b') return AssassinBlinkMul;
            return 1f;
        }

        // ---------- 닌자 casts ----------

        float ThrowGap() { char sp = Spec("n_throw"); return sp == 'a' ? ThrowHeavyGap : sp == 'b' ? ThrowQuickGap : ThrowInterval; }
        float ThrowHitScale() { char sp = Spec("n_throw"); return sp == 'a' ? ThrowHeavyScale : sp == 'b' ? ThrowQuickScale : 1f; }
        int ThrowPierce() => Spec("n_throw") == 'b' ? 1 : 0;

        /// <summary>부채 연발 B: three kunai at once (-15°, 0, +15°) that pierce.</summary>
        void VolleyFan(Run c)
        {
            float spread = float.IsNaN(c.n.spreadDeg) ? 15f : c.n.spreadDeg;
            int pierce = c.n.pierce > 0 ? c.n.pierce : 9;
            Pose(.12f, 1);
            Sound("swing", .6f);
            for (int k = -1; k <= 1; k++)
            {
                Vector2 dir = CareerFx.Tilt(c.dir, k * spread);
                StartCoroutine(Throw(c, owner.Center + dir * .3f, dir, c.n.range, VolleySpeed, c.n.damage, true, null, pierce));
                ForClones(clone => StartCoroutine(Throw(c, clone.Center + dir * .3f, dir, c.n.range, VolleySpeed, CloneDamage(c.n.damage), false, clone)));
            }
        }

        /// <summary>표적 회수 A: the kunai first flies through the marked monster (one cut), then back to my hand.</summary>
        IEnumerator RecallVia(Run c, KunaiProjectile k, EnemyController mark, int damage)
        {
            Vector2 at = k.Position;
            for (float guard = 0f; guard < 2f && k != null; guard += Time.deltaTime)
            {
                if (!Live(c) || mark == null || mark.IsDead) yield break;
                Vector2 to = mark.Center - at;
                float step = RecallSpeed * Time.deltaTime;
                Vector2 next = to.magnitude <= step ? mark.Center : at + to.normalized * step;
                k.Aim(to);
                k.Place(next);
                at = next;
                if (next == mark.Center)
                {
                    Strike(c, mark, damage, at, 1.5f, 0, "c_slash");
                    yield break;
                }
                yield return null;
            }
        }
    }
}
