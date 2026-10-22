using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3 SPEC] State and hooks of the warrior line's specializations (Docs/ch3/SKILL_SPECIALIZATION.md 3): extra
    /// damage taken marks, haste, the passives' A/B rules, 응보의 방진's block count, 검기 연성's shards. Without a spec
    /// (Progression.SpecOf '\0', chapter 3 closed) every helper returns the old value, so Lv1~40 play is unchanged.
    /// </summary>
    public sealed partial class CareerCombat
    {
        /// <summary>Extra damage a monster takes from this owner: 위압의 함성 A, 표적 호령 A, 천쇄 일점 A.</summary>
        readonly Dictionary<EnemyController, (float until, int pct)> specMarks = new Dictionary<EnemyController, (float, int)>();
        /// <summary>기선 제압 B: monsters already opened (staggered once).</summary>
        readonly HashSet<EnemyController> edgeOpened = new HashSet<EnemyController>();
        float hasteEnd, shardUntil, wallReflectEnd;
        int hastePct;
        bool frenzyBossOnly, frenzyExtend;
        char counterSpec;

        char Spec(string skill) => Prog.SpecOf(skill);

        /// <summary>Spec colour of a cast (crimson A, silver B), else <paramref name="own"/>.</summary>
        static Color Tint(Run c, Color own) => SkillSpecs.Tint(c.n, own);

        void MarkTaken(EnemyController e, int pct, float seconds)
        {
            if (e == null || e.IsDead) return;
            specMarks[e] = (Time.time + seconds, specMarks.TryGetValue(e, out var m) && m.until > Time.time ? Mathf.Max(m.pct, pct) : pct);
            SkillFx.Spawn("fx_alert", e.Center + Vector2.up * .9f, SkillSpecs.ColorA, .5f, SkillFx.TopOrder + 5).Pop().Fade(FxFade.Late);
        }

        /// <summary>위압의 함성 A (a base skill cast by SkillCaster) marks through the caster's CareerCombat.</summary>
        public void MarkFromCaster(EnemyController e, int pct, float seconds) => MarkTaken(e, pct, seconds);

        float SpecTakenMul(EnemyController e) => specMarks.TryGetValue(e, out var m) && Time.time < m.until ? 1 + m.pct / 100f : 1f;

        /// <summary>전장의 함성 B: allies walk faster for a while.</summary>
        public void AddHaste(int pct, float seconds)
        {
            if (Time.time < hasteEnd && pct < hastePct) return;
            hastePct = pct;
            hasteEnd = Time.time + seconds;
        }

        float SpecMoveBonus => Time.time < hasteEnd ? 1 + hastePct / 100f : 1f;

        // ---------- Passives ----------

        /// <summary>파죽지세: base +15% on HP 80%+; A 결정타 +25% on career-skill hits only; B 기선 제압 +10% on everything.</summary>
        float EdgeMul(EnemyController enemy)
        {
            int edge = Prog.Rank("f_edge");
            if (edge <= 0) return 1f;
            char sp = Spec("f_edge");
            if (sp == 'b') return 1.10f;
            bool fresh = enemy.Health.Current >= enemy.Health.Max * .8f;
            if (sp == 'a') return fresh && !string.IsNullOrEmpty(currentSkill) ? 1.25f : 1f;
            return fresh ? 1 + (15 + 5 * (edge - 1)) / 100f : 1f;
        }

        /// <summary>수호의 반향 after a blow: base +20% once; A 분노의 반향 +40% once; B 울림의 반향 +10% for the whole 4 s.</summary>
        float RetalMul()
        {
            int rank = Prog.Rank("g_retal");
            char sp = Spec("g_retal");
            if (sp == 'b') return 1.10f;
            retalEnd = 0;
            if (sp == 'a') return 1.40f;
            return 1 + (20 + 5 * (rank - 1)) / 100f;
        }

        /// <summary>강철의 심장: base -6%; A 철벽 -12% while a boss is near, else 0; B 강철 파동 -6% and a shared -3%.</summary>
        int SteelReduction()
        {
            int steel = Prog.Rank("g_steel");
            if (steel <= 0) return 0;
            int base6 = 4 + steel * 2;
            if (Spec("g_steel") != 'a') return base6;
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.IsBoss && Vector2.Distance(e.Center, owner.Center) < 8f) return base6 * 2;
            return 0;
        }

        /// <summary>A blow got through: 강철 파동 B shares -3% with the allies within 2 m; 수호의 벽 B answers it.</summary>
        void SpecOnHurt(int blocked)
        {
            if (Prog.Rank("g_steel") > 0 && Spec("g_steel") == 'b')
                foreach (var p in AlliesNear(owner.Center, 2f)) if (p != owner) For(p).AddGuard(3, 3f);
            if (Time.time < wallReflectEnd && blocked > 0)
            {
                EnemyController near = null;
                float best = 3f;
                foreach (var e in EnemyController.Active)
                {
                    if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                    float d = Vector2.Distance(e.Center, owner.Center);
                    if (d < best) { best = d; near = e; }
                }
                if (near != null)
                {
                    SecondaryHit(near, Mathf.Max(1, blocked / 2));
                    CareerFx.Clip("g_clang", near.Center, Vector2.zero, .8f, 30f, VfxLayer.Top, false, SkillSpecs.ColorB);
                }
            }
        }

        List<PlayerController> AlliesNear(Vector2 at, float radius)
        {
            var list = new List<PlayerController>();
            void Add(PlayerController p) { if (p != null && !p.IsDead && !list.Contains(p) && Vector2.Distance(p.Center, at) <= radius) list.Add(p); }
            Add(owner);
            if (Game.Party != null) foreach (var p in Game.Party.Members) Add(p);
            return list;
        }

        /// <summary>검기 연성: base +20% on the next Fighter skill. A 검기 응축 +35% on a single-target skill only (kept for one);
        /// B 검기 파급 +20% and every hit of that cast throws shards.</summary>
        float SpendRhythm(string skill = null)
        {
            int rank = Prog.Rank("f_rhythm");
            if (rank <= 0 || ComboStacks < 3) return 1f;
            char sp = Spec("f_rhythm");
            if (sp == 'a' && !SingleTarget(skill)) return 1f;
            stacks = 0;
            rhythmEnd = 0;
            if (sp == 'a') return 1.35f;
            if (sp == 'b') shardUntil = Time.time + 2.5f;
            return 1.2f + .05f * (rank - 1);
        }

        bool SingleTarget(string skill) => skill == "f_execute" || skill == "f_focus" || !string.IsNullOrEmpty(skill) && Spec(skill) == 'a';

        /// <summary>검기 파급: a hit of the boosted cast bursts into shards on the monsters within 1.5 m (30%).</summary>
        void RhythmShards(EnemyController hit, int damage)
        {
            if (Time.time >= shardUntil || secondaryDamage) return;
            int shard = Mathf.Max(1, Mathf.RoundToInt(damage * .3f));
            foreach (var e in Enemies(hit.Center, 1.5f))
            {
                if (e == hit) continue;
                SecondaryHit(e, shard);
                SkillVisuals.Sparks(e.Center, SkillSpecs.ColorB, 3, 2.5f, .2f);
            }
        }

        /// <summary>Every landed hit of the owner: kills extend 귀검 난무 B and reset 단죄 도약 B; 기선 제압 B opens once;
        /// 수호 태세 B gathers its shield.</summary>
        void SpecLanded(EnemyController enemy)
        {
            if (enemy == null) return;
            if (Prog.Rank("f_edge") > 0 && Spec("f_edge") == 'b' && edgeOpened.Add(enemy) && !enemy.IsDead) enemy.Stagger(.3f);
            if (currentSkill == "f_stance" && Spec("f_stance") == 'b')
            {
                int add = Mathf.Max(1, Mathf.RoundToInt(owner.Health.Max * .01f)), cap = Mathf.RoundToInt(owner.Health.Max * .1f);
                AddShield(Mathf.Min(cap, Shield + add), 6f, Career.Fighter);
            }
            if (!enemy.IsDead) return;
            if (frenzyExtend && Time.time < frenzyEnd) frenzyEnd += 2f;
            if (currentSkill == "f_execute" && Spec("f_execute") == 'b') owner.Skills?.ResetSkillCooldown("f_execute");
        }

        /// <summary>응보의 방진 power of one answered block: base 100% then 60%; A one 400% blow; B 25% (and a 5% shield).</summary>
        float CounterPower()
        {
            if (counterSpec == 'a') return 4f;
            if (counterSpec == 'b')
            {
                AddShield(Shield + Mathf.RoundToInt(owner.Health.Max * .05f), 6f, Career.Guardian);
                return .25f;
            }
            return counterCharges == CounterBlocks - 1 ? 1f : .6f;
        }

        void ResetSpecWarrior()
        {
            specMarks.Clear();
            edgeOpened.Clear();
            hasteEnd = shardUntil = wallReflectEnd = 0f;
            hastePct = 0;
            frenzyBossOnly = frenzyExtend = false;
            counterSpec = '\0';
        }
    }
}
