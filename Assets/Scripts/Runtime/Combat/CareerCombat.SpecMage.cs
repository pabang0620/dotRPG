using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3 SPEC] Mage and bishop specializations (Docs/ch3/SKILL_SPECIALIZATION.md 3): the shared helpers and the
    /// spec-only behaviours the 메이지 / 비숍 cast functions branch into. Without a spec (<c>n.spec == '\0'</c>) none of this runs.
    /// </summary>
    public sealed partial class CareerCombat
    {
        // 원소 집중: landings of the same element in a row. 직격 뇌전 / 심판의 대창: hits on one target in a row.
        int sameElementRun, stormRun, lightRun, flowCasts;
        Vector2? iceLandedAt;
        int feathersInRain;
        EnemyController stormTarget, lightTarget;
        // 이중 도약: where the first leap started and until when the way back is open.
        Vector2 veilAnchor;
        float veilReturnUntil, veilFirstCast;
        // 집중 치유: the cast whose first heal got the bonus.
        object mercyRun;
        /// <summary>The cast's duration: the spec's when it sets one, else the skill's own.</summary>
        static float Dur(Run c) => float.IsNaN(c.n.duration) || c.n.duration <= 0f ? c.s.duration : c.n.duration;

        /// <summary>The cast's hit count: the spec's (through the gem) when set, else the skill's own.</summary>
        static int Hits(Run c) => c.n.hits > 0 ? c.n.hits : c.s.hits;

        /// <summary>Clip tint for a spec cast (crimson / silver), none without a spec.</summary>
        static Color? SpecTint(Run c) => c.n.spec == 'a' ? SkillSpecs.ColorA : c.n.spec == 'b' ? SkillSpecs.ColorB : (Color?)null;

        int Attack => owner.Data.Stats.AttackDamage(owner.Class);

        void GainManaShare(float share)
        {
            if (owner.NetPuppet) return;
            owner.Data.Mana = Mathf.Min(owner.Data.Mana + owner.MaxMana * share, owner.MaxMana);
        }

        // ---------- 메이지 ----------

        /// <summary>원소 공명 (m_elements B): a different element landing bursts for 0.2 x attack within 2 m.</summary>
        void ElementBurst(Vector2 at, string element)
        {
            var tone = element == "fire" ? FireColor : element == "ice" ? IceColor : StormColor;
            SkillVisuals.Flash(at, new Color(tone.r, tone.g, tone.b, .7f), 2f, .2f);
            SkillVisuals.Sparks(at, SkillSpecs.ColorB, 6, 3.5f, .3f);
            int damage = Mathf.Max(1, Mathf.RoundToInt(Attack * .2f));
            foreach (var e in Enemies(at, 2f)) SecondaryHit(e, damage);
        }

        /// <summary>홍련 연화 (m_fire B): burns stack on one monster up to three at once, each 0.3 x attack a second for 3 s.</summary>
        readonly Dictionary<EnemyController, int> fireStacks = new Dictionary<EnemyController, int>();

        IEnumerator StackBurn(EnemyController e, int value, string map)
        {
            fireStacks.TryGetValue(e, out int stacks);
            if (stacks >= 3) yield break;
            fireStacks[e] = stacks + 1;
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(1f);
                if (e == null || e.IsDead || !Valid(map)) break;
                SecondaryHit(e, value, true);
                SkillFx.Spawn("fx_flame", e.Center + Random.insideUnitCircle * .2f, SkillSpecs.ColorB, .35f, SkillFx.TopOrder + 2).Move(Vector2.up * 1.5f).Scale(1.6f, .4f);
            }
            if (e != null && fireStacks.TryGetValue(e, out int left))
            {
                if (left <= 1) fireStacks.Remove(e);
                else fireStacks[e] = left - 1;
            }
        }

        /// <summary>직격 뇌전 (m_storm A): the third hit in a row on the same monster adds a 1.0 x attack shock.</summary>
        void StormDirect(Run c, EnemyController e)
        {
            if (!c.authority || e == null) return;
            stormRun = e == stormTarget ? stormRun + 1 : 1;
            stormTarget = e;
            if (stormRun < 3 || e.IsDead) return;
            stormRun = 0;
            Bolt(e.Center + Vector2.up * 1.6f, e.Center, true);
            SkillVisuals.Sparks(e.Center, SkillSpecs.ColorA, 10, 4f, .3f);
            SecondaryHit(e, Attack);
        }

        /// <summary>이중 도약 (m_veil B): the first leap opens a 4 s window in which the skill leaps straight back.</summary>
        bool VeilReturn(Run c)
        {
            if (Time.time >= veilReturnUntil) return false;
            veilReturnUntil = 0f;
            Vector2 to = veilAnchor - owner.Center;
            if (!owner.NetPuppet && to.sqrMagnitude > .01f) owner.SkillBlink(to.normalized, to.magnitude);
            CareerFx.Gen("m2_blink", "m_portal", owner.Center, Vector2.zero, 1.6f, 1.1f, 24f, VfxLayer.Top, false, SkillSpecs.ColorB);
            Sound("c_arcane", .6f);
            owner.GetComponent<SkillCaster>()?.SpecSetReady(c.s.id, veilFirstCast + c.n.cooldown);
            return true;
        }

        void VeilOpen(Run c, Vector2 origin)
        {
            veilAnchor = origin;
            veilFirstCast = Time.time;
            veilReturnUntil = Time.time + 4f;
            var caster = owner.GetComponent<SkillCaster>();
            caster?.SpecSetReady(c.s.id, Time.time + .3f);
            StartCoroutine(VeilClose(c, caster));
        }

        IEnumerator VeilClose(Run c, SkillCaster caster)
        {
            float first = veilFirstCast;
            yield return new WaitForSeconds(4f);
            if (veilReturnUntil > 0f && Mathf.Approximately(first, veilFirstCast))
            {
                veilReturnUntil = 0f;
                caster?.SpecSetReady(c.s.id, first + c.n.cooldown); // the way back was not used: the full cooldown runs
            }
        }

        /// <summary>공유 배리어 (m_barrier B): when the mage's own barrier breaks early, monsters within 3 m slow for 2 s.</summary>
        IEnumerator BarrierBreak(float until)
        {
            yield return new WaitForSeconds(.2f);
            while (owner != null && !owner.IsDead && Time.time < until)
            {
                if (Shield <= 0)
                {
                    SkillVisuals.Sparks(owner.Center, SkillSpecs.ColorB, 12, 4f, .4f);
                    foreach (var e in Enemies(owner.Center, 3f)) e.Slow(.4f, 2f);
                    yield break;
                }
                yield return null;
            }
        }

        // ---------- 비숍 ----------

        /// <summary>The ally with the lowest HP share within <paramref name="radius"/> (the bishop included).</summary>
        PlayerController Weakest(float radius)
        {
            PlayerController best = null;
            float low = float.MaxValue;
            foreach (var p in Allies(owner.Center, radius))
            {
                float share = p.Health.Current / (float)Mathf.Max(1, p.Health.Max);
                if (share < low) { low = share; best = p; }
            }
            return best ?? owner;
        }

        /// <summary>큰 깃 (b_heal A): the part of the heal above full HP becomes a shield (up to 20% of max HP, 10 s).</summary>
        void OverhealShield(Run c, PlayerController p, int value)
        {
            if (!c.authority || p == null || p.IsDead) return;
            int over = value - (p.Health.Max - p.Health.Current);
            if (over <= 0) return;
            For(p).AddShield(Mathf.Min(over, Mathf.RoundToInt(p.Health.Max * .2f)), 10f, Career.Bishop);
        }

        /// <summary>나눔의 그릇 (b_grace B): when a shield breaks early, allies within 3 m take 20% of it.</summary>
        IEnumerator GraceShare(PlayerController p, int amount, float duration)
        {
            float until = Time.time + duration;
            yield return new WaitForSeconds(.2f);
            while (p != null && !p.IsDead && Time.time < until)
            {
                if (For(p).Shield <= 0)
                {
                    int share = Mathf.Max(1, Mathf.RoundToInt(amount * .2f));
                    foreach (var other in Allies(p.Center, 3f))
                        if (other != p) { For(other).AddShield(share, 5f, Career.Bishop); CareerFx.Bless(other.Center, SkillSpecs.ColorB, false); }
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>집중 치유 (b_mercy A): +20% on the first heal of each cast.</summary>
        float MercyFirst(Run c)
        {
            if (ReferenceEquals(mercyRun, c)) return 1f;
            mercyRun = c;
            return 1.2f;
        }

        /// <summary>축복의 선택 (b_bless A): while the blessing lasts, a monster dying near the blessed ally gives the bishop 5% MP.</summary>
        IEnumerator BlessedKills(PlayerController target, float duration)
        {
            float until = Time.time + duration, next = 0f;
            void OnKilled(EnemyController e, int xp)
            {
                if (target == null || target.IsDead || e == null || Time.time < next) return;
                if (Vector2.Distance(e.Center, target.Center) > 2.5f) return;
                next = Time.time + .5f;
                GainManaShare(.05f);
            }
            EnemyController.Killed += OnKilled;
            try
            {
                while (Time.time < until && owner != null && !owner.IsDead && target != null && !target.IsDead) yield return null;
            }
            finally { EnemyController.Killed -= OnKilled; }
        }

        /// <summary>천상의 대행진 (b_awake B): monsters in the sanctum take 45% (of the march's 450%) every second for 10 s.</summary>
        IEnumerator MarchTicks(Run c)
        {
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * .45f / 4.5f));
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForSeconds(1f);
                if (!Live(c)) yield break;
                foreach (var e in Enemies(owner.Center, SanctumRadius))
                {
                    DawnPillar(e.Position, .6f, .45f, 26f, VfxLayer.AtFeet);
                    Strike(c, e, damage, owner.Center, 0f, 0, "c_holy");
                }
            }
        }
    }
}
