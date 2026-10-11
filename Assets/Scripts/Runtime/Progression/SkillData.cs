using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    // =============================== Skill gems ===============================

    public enum GemKind { Active, Support }

    /// <summary>
    /// Skill gems. Each class has one active skill per slot (slot 5 = awakening ultimate),
    /// unlocked when the slot opens. Support gems linked into a slot change how its skill works
    /// (more damage, bigger area, casting twice...). Supports unlock as the character levels up.
    /// </summary>
    public sealed class SkillGem
    {
        public string id, name, icon, description;
        /// <summary>[UI] One line on when the gem pays off (gem guide in the skill window).</summary>
        public string tip;
        public GemKind kind;
        public CharacterClass? classOnly;
        public int unlockLevel;
        // Active gems.
        public int slot = -1;
        public float damageMult = 1f, cooldown = 1f, radius = 1f, range = 6f;
        public int manaCost;
        public int chains;
        public float freeze, stun;
        public int hits = 1;          // strikes of an ultimate / volley
        public int buffPct;           // war cry: % increased damage
        public float buffTime;
        public int guardPct;          // [SKILL v2] 철벽 / 마나 보호막: % less damage taken
        public float guardTime;
        // Support gems.
        public int moreDamage;        // % more damage (can be negative)
        public int moreAoe;           // % more area
        public float manaMult = 1f;
        public int repeats;           // extra casts
        public int extraChains;
        public int leechPct;          // % of damage dealt returned as HP
        public int bossDamage;        // [SKILL v2] % more damage against bosses

        public bool IsUltimate => slot == SkillGems.UltimateSlot;
        public bool UsableBy(CharacterClass cls) => classOnly == null || classOnly == cls;
    }

    public static class SkillGems
    {
        public const int Slots = 5, SupportsPerSlot = 2, UltimateSlot = 4;

        /// <summary>
        /// Level at which each skill slot (and its skill) opens. Slot 5 is the awakening skill.
        /// v2 spreads them over the climb to the first mid raid (Lv22, about 20 hours): about 0 / 0.6 / 4 / 11 / 20 hours.
        /// </summary>
        public static readonly int[] SlotLevels = { 2, 6, 12, 18, 15 };

        static SkillGem Active(string id, string name, CharacterClass cls, int slot, string desc)
            => new SkillGem { id = id, name = name, icon = "gem_" + id, kind = GemKind.Active, classOnly = cls, slot = slot, unlockLevel = SlotLevels[slot], description = desc };

        static readonly List<SkillGem> gems = CreateV2();

        /// <summary>
        /// [SKILL v2] One job per slot (Tools/balance/theory_skills.py): 1 single-target main attack, 2 the one
        /// area skill, 3 control, 4 a second attack (dash strike / fire field; the old defence skills are class
        /// passives now, see CharacterData), 5 awakening. Areas were widened ~25% with the denser fields. Area skills hit each target for about 60% of the
        /// single-target skill, so they pay off from three monsters up; bosses are the single skill's job.
        /// </summary>
        static List<SkillGem> CreateV2() => new List<SkillGem>
        {
            // Warrior.
            // [BALANCE 2026-10-06] Cheaper, quicker, wider: about 1.2x damage per second, used far more often.
            Active("crush", "파쇄 일격", CharacterClass.Warrior, 0, "눈앞의 적 하나를 온 힘으로 내려친다. 주변에 충격파가 퍼진다. 보스와 강적을 상대하는 주력기.").With(g => { g.damageMult = 2.7f; g.cooldown = 1.6f; g.range = 2.2f; g.radius = 1.3f; g.manaCost = 4; }),
            Active("whirl", "회전 베기", CharacterClass.Warrior, 1, "제자리에서 한 바퀴 돌며 주변의 모든 적을 벤다. 적이 셋 이상 몰렸을 때 쓴다.").With(g => { g.damageMult = 1.7f; g.cooldown = 3f; g.radius = 3.2f; g.manaCost = 8; }),
            Active("cry", "전쟁 함성", CharacterClass.Warrior, 2, "함성으로 주변 적을 기절시키고 어그로를 끈다.").With(g => { g.damageMult = 0.5f; g.cooldown = 10f; g.radius = 4.2f; g.stun = 1.3f; g.manaCost = 12; }),
            Active("charge", "돌진 베기", CharacterClass.Warrior, 3, "겨눈 방향으로 돌진하며 길목의 적을 모두 베고 밀어낸다. 무리 속으로 파고들 때 쓴다.").With(g => { g.damageMult = 2.5f; g.cooldown = 4.5f; g.range = 5f; g.radius = 1.6f; g.manaCost = 9; }),
            Active("blades", "천검 강림", CharacterClass.Warrior, 4, "하늘에서 거대한 검을 떨어뜨려 주변의 적을 꿰뚫는다.").With(g => { g.damageMult = 2.4f; g.cooldown = 24f; g.radius = 2f; g.range = 6f; g.hits = 12; g.manaCost = 30; }),
            // Mage.
            Active("lance", "번개 창", CharacterClass.Mage, 0, "가장 가까운 적 하나에게 굵은 번개를 내리꽂는다. 맞은 자리 주변에도 전기가 튄다. 보스와 강적을 상대하는 주력기.").With(g => { g.damageMult = 2.5f; g.cooldown = 1.6f; g.range = 8f; g.radius = 1.3f; g.manaCost = 4; }),
            Active("frostorb", "빙뢰구", CharacterClass.Mage, 1, "얼음 구체를 날려 부딪힌 자리의 적들을 얼음 파편으로 터뜨린다. 적이 셋 이상 몰렸을 때 쓴다.").With(g => { g.damageMult = 1.6f; g.cooldown = 3f; g.radius = 3.1f; g.range = 8f; g.manaCost = 8; }),
            Active("nova", "서리 폭발", CharacterClass.Mage, 2, "주변에 냉기를 터뜨려 다가온 적을 1.4초 동안 얼린다.").With(g => { g.damageMult = 0.5f; g.cooldown = 10f; g.radius = 4.1f; g.freeze = 1.4f; g.manaCost = 12; }),
            Active("firefield", "화염 장판", CharacterClass.Mage, 3, "적이 몰린 자리에 불길을 깔아 3초 동안 6번 태운다. 무리를 묶어 둘 때 쓴다.").With(g => { g.damageMult = 0.4f; g.cooldown = 6f; g.range = 7.5f; g.radius = 3.2f; g.hits = 6; g.manaCost = 10; }),
            Active("meteor", "메테오", CharacterClass.Mage, 4, "거대한 운석을 연달아 떨어뜨려 넓은 지역을 불태운다.").With(g => { g.damageMult = 2.8f; g.cooldown = 24f; g.radius = 2.8f; g.range = 7f; g.hits = 8; g.manaCost = 32; }),

            new SkillGem { id = "sup_dmg", tip = "보스·강적을 잡는 주력기(1번 스킬)에. MP 여유가 있을 때 가장 무난하다.", name = "추가 피해", icon = "gem_sup_dmg", kind = GemKind.Support, unlockLevel = 3, moreDamage = 35, manaMult = 1.3f,
                description = "연결된 스킬의 피해 35% 증폭. MP 소모 30% 증가." },
            new SkillGem { id = "sup_aoe", tip = "몰이 사냥용 범위 스킬(2번)에. 한 번에 더 많은 적을 맞힌다.", name = "범위 확대", icon = "gem_sup_aoe", kind = GemKind.Support, unlockLevel = 7, moreAoe = 35, manaMult = 1.15f,
                description = "연결된 스킬의 범위(면적) 35% 증폭. MP 소모 15% 증가." },
            new SkillGem { id = "sup_multi", tip = "쿨타임이 긴 범위기·각성에. 피해는 줄지만 두 번 터져 총량이 늘어난다.", name = "연속 시전", icon = "gem_sup_multi", kind = GemKind.Support, unlockLevel = 10, repeats = 1, moreDamage = -30, manaMult = 1.4f,
                description = "연결된 스킬이 한 번 더 발동한다. 피해 30% 감소, MP 소모 40% 증가." },
            new SkillGem { id = "sup_eff", tip = "자주 쓰는 스킬이나 MP가 모자랄 때. 다른 보조 젬의 MP 증가를 상쇄한다.", name = "마력 효율", icon = "gem_sup_eff", kind = GemKind.Support, unlockLevel = 5, manaMult = 0.7f,
                description = "연결된 스킬의 MP 소모 30% 감소." },
            new SkillGem { id = "sup_leech", tip = "물약을 아끼고 싶을 때, 솔로 사냥에. 여러 적을 때리는 스킬일수록 많이 회복한다.", name = "흡혈", icon = "gem_sup_leech", kind = GemKind.Support, unlockLevel = 8, leechPct = 10,
                description = "연결된 스킬이 입힌 피해의 10%만큼 HP를 회복한다." },
            // Same id as v1's 추가 연쇄 (saves keep their sockets); v2 has no chain skills, so it hunts bosses instead.
            new SkillGem { id = "sup_chain", tip = "보스전·레이드 전용 주력기에. 일반 몬스터에게는 효과가 없다.", name = "보스 사냥", icon = "gem_sup_boss", kind = GemKind.Support, unlockLevel = 14, bossDamage = 30,
                description = "연결된 스킬이 보스에게 주는 피해 30% 증폭." },
        };

        internal static SkillGem With(this SkillGem g, System.Action<SkillGem> set) { set(g); return g; }

        public static IReadOnlyList<SkillGem> All => gems;

        public static SkillGem Get(string id)
        {
            var career = CareerCatalog.Get(id); if(career != null) return career.Gem;
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var g in gems) if (g.id == id) return g;
            return null;
        }

        /// <summary>The class's skill in a slot (0-4), whether or not it is unlocked yet.</summary>
        public static SkillGem ForSlot(CharacterClass cls, int slot)
        {
            foreach (var g in gems) if (g.kind == GemKind.Active && g.slot == slot && g.UsableBy(cls)) return g;
            return null;
        }

        /// <summary>Key binding of a slot.</summary>
        public static GameAction ActionFor(int slot)
        {
            switch (slot)
            {
                case 0: return GameAction.Skill1;
                case 1: return GameAction.Skill2;
                case 2: return GameAction.Skill3;
                case 3: return GameAction.Skill4;
                default: return GameAction.Skill5;
            }
        }
    }

    /// <summary>Final numbers of one skill after supports and passives.</summary>
    public struct SkillNumbers
    {
        public float careerPotency;
        public int damage, manaCost, chains, repeats, leechPct, hits, buffPct, guardPct, bossPct;
        public float cooldown, radius, range, freeze, stun, buffTime, guardTime;
        public bool usesLife;
    }

    // =============================== Aggregated character stats ===============================

    /// <summary>
    /// Everything that makes one party member stronger, summed from base stats, level, passive tree and
    /// equipment of its <see cref="CharacterData"/>. Recomputed on demand (cheap), so there is no cached
    /// state to go stale. Every member has its own instance (<see cref="CharacterData.Stats"/>).
    /// </summary>
    public sealed class CharacterStatsCalc
    {
        public const int BaseMana = 40, HpPerLevel = 8, MpPerLevel = 4;

        readonly CharacterData data;

        public CharacterStatsCalc(CharacterData data) => this.data = data;

        Progression Prog => data.Progression;
        Equipment Eq => data.Equipment;
        int Level => Prog.Level;

        int Sum(PassiveStat stat, int slot = -2)
        {
            int total = 0;
            foreach (var id in Prog.LegacyTraining)
            {
                var n = PassiveTree.Get(id);
                if (n == null || (slot != -2 && n.skillSlot != slot)) continue;
                foreach (var (s, v) in n.stats) if (s == stat) total += v;
            }
            return total;
        }

        /// <summary>A Skill* stat summed for one skill slot.</summary>
        public int SlotStat(PassiveStat stat, int slot) => Sum(stat, slot);

        public bool Has(Keystone k)
        {
            foreach (var id in Prog.LegacyTraining)
            {
                var n = PassiveTree.Get(id);
                if (n != null && n.keystone == k) return true;
            }
            return false;
        }

        public int MaxHp
        {
            get
            {
                bool warrior = data.Class == CharacterClass.Warrior;
                float flat = data.BaseMaxHp + (warrior ? WarriorHpBonus : 0) + (Level - 1) * (HpPerLevel + (warrior ? WarriorHpPerLevel : 0)) + Sum(PassiveStat.FlatHp) + Eq.MaxHealthBonus;
                float inc = Sum(PassiveStat.IncHp) + (Has(Keystone.GlassCannon) ? -25 : 0) + (Has(Keystone.BloodMagic) ? 30 : 0) + data.CollectionHealth;
                return Mathf.Max(1, Mathf.RoundToInt(flat * (1f + inc / 100f) * CareerHp(Prog.Career)));
            }
        }

        public int MaxMp
        {
            get
            {
                if (Has(Keystone.BloodMagic)) return 0;
                bool mage = data.Class == CharacterClass.Mage;
                float mp = BaseMana + (mage ? MageMpBonus : WarriorMpBonus) + (Level - 1) * (MpPerLevel + (mage ? MageMpPerLevel : 0)) + Sum(PassiveStat.FlatMp);
                return Mathf.RoundToInt(mp * CareerMp(Prog.Career));
            }
        }

        /// <summary>
        /// [BALANCE 2026-10-06] Class identity in the base numbers: warriors are the sturdy ones (more HP, more per
        /// level), mages carry the big mana pool; the career then leans further (guardian +30% HP, bishop +25% MP...).
        /// </summary>
        public const int WarriorHpBonus = 50, WarriorHpPerLevel = 6, WarriorMpBonus = 10, MageMpBonus = 40, MageMpPerLevel = 3;
        public static float CareerHp(Career c) => c == Career.Guardian ? 1.3f : c == Career.Fighter ? 1.1f : c == Career.Bishop ? 1.05f : 1f;
        public static float CareerMp(Career c) => c == Career.Bishop ? 1.25f : c == Career.Arcanist ? 1.2f : 1f;

        /// <summary>MP per second: 5% of max MP, increased by the tree.</summary>
        public float ManaRegen => MaxMp * 0.05f * (1f + Sum(PassiveStat.ManaRegen) / 100f);

        public int IncDamage => Sum(PassiveStat.IncDamage) + (Has(Keystone.GlassCannon) ? 40 : 0) + data.BuffDamage + data.CosmeticDamage;

        /// <summary>Class base damage + weapon/gear attack, before % increases (scaled for companions).</summary>
        float BaseAttack(CharacterClass cls)
        {
            var info = CharacterClassInfo.Get(cls);
            int baseDmg = info.ranged ? info.damage : Game.Config.playerStats.attackDamage;
            return (baseDmg + Eq.AttackBonus) * data.DamageScale;
        }

        /// <summary>Class base damage + weapon/gear attack, then % increased damage.</summary>
        public int AttackDamage(CharacterClass cls) => Mathf.Max(1, Mathf.RoundToInt(BaseAttack(cls) * (1f + IncDamage / 100f)));

        public int Block => Mathf.Clamp(Eq.BlockChance + Sum(PassiveStat.Block) + (Has(Keystone.Unwavering) ? 15 : 0), 0, 75);
        public int SpeedBonus => Eq.SpeedBonus + Sum(PassiveStat.Speed) + (Has(Keystone.Unwavering) ? -10 : 0);
        public float SpeedMultiplier => Mathf.Max(0.5f, 1f + SpeedBonus / 100f);
        public int AttackSpeed => Sum(PassiveStat.AttackSpeed) + CareerCombat.SpeedFor(data);
        public float CooldownMultiplier => 1f / (1f + AttackSpeed / 100f);
        public int Aoe => Sum(PassiveStat.Aoe) + Eq.AoeBonus;
        public int ManaCostReduction => Mathf.Min(80, Sum(PassiveStat.ManaCost));
        public int LifeOnKill => Sum(PassiveStat.LifeOnKill);
        public int ManaOnKill => Sum(PassiveStat.ManaOnKill);

        /// <summary>전투력 shown on the character card.</summary>
        public int Power(CharacterClass cls) =>
            AttackDamage(cls) * 10 + MaxHp * 5 + MaxMp * 3 + Block * 20 + SpeedBonus * 10 + Level * 50;

        /// <summary>A slotted skill's final numbers (supports + global and per-slot passives applied).</summary>
        public SkillNumbers Skill(CharacterClass cls, int slot, SkillGem active, IEnumerable<SkillGem> supports)
        {
            float more = 1f, moreAoe = 1f, mana = 1f;
            var n = new SkillNumbers
            {
                chains = active.chains, freeze = active.freeze, stun = active.stun, range = active.range,
                hits = active.hits, buffPct = active.buffPct, buffTime = active.buffTime,
                guardPct = active.guardPct, guardTime = active.guardTime,
            };
            foreach (var s in supports)
            {
                if (s == null) continue;
                more *= 1f + s.moreDamage / 100f;
                moreAoe *= 1f + s.moreAoe / 100f;
                mana *= s.manaMult;
                n.repeats += s.repeats;
                n.chains += active.chains > 0 ? s.extraChains : 0;
                n.leechPct += s.leechPct;
                n.bossPct += s.bossDamage;
            }
            var career=CareerCatalog.Get(active.id);
            float rankScale=career==null?1:1+.12f*Mathf.Max(0,Prog.Rank(active.id)-1);
            float inc = IncDamage + (career==null?SlotStat(PassiveStat.SkillDamage, slot):0);
            float area = (1f + (Aoe + SlotStat(PassiveStat.SkillArea, slot)) / 100f) * moreAoe;
            n.damage = Mathf.Max(1, Mathf.RoundToInt(BaseAttack(cls) * (1f + inc / 100f) * active.damageMult * more * rankScale));
            // [SKILL v2] Area bonuses grow the AREA (radius by the square root); v1 multiplied the radius,
            // which squared every bonus (+35% and +25% made a 2.85x area).
            float grow = Mathf.Sqrt(area);
            n.radius = active.radius * grow;
            // An ultimate's target area grows with its area bonuses too.
            if (active.IsUltimate) n.range = active.range * grow;
            n.cooldown = active.cooldown * CooldownMultiplier * Mathf.Max(0.3f, 1f - SlotStat(PassiveStat.SkillCooldown, slot) / 100f);
            n.repeats += SlotStat(PassiveStat.SkillRepeat, slot);
            if (active.guardPct > 0)
            {
                // [SKILL v2] Defence skills turn the slot's passives into their own terms:
                // damage nodes -> stronger reduction (a quarter of the %), area nodes -> longer duration.
                n.guardPct = Mathf.Min(70, active.guardPct + Mathf.RoundToInt(SlotStat(PassiveStat.SkillDamage, slot) / 4f));
                n.guardTime = active.guardTime * area;
                n.repeats = 0;
            }
            n.usesLife = Has(Keystone.BloodMagic);
            n.manaCost = Mathf.Max(1, Mathf.RoundToInt(active.manaCost * mana * (1f - ManaCostReduction / 100f)));
            if(career!=null) {
                n.careerPotency=rankScale*more;
                n.repeats=0; n.leechPct=0; n.bossPct=0;
                if(Prog.Rank("m_flow")>0) n.manaCost=Mathf.Max(1,Mathf.RoundToInt(n.manaCost*(1-(10+3*(Prog.Rank("m_flow")-1))/100f)));
            }
            return n;
        }
    }

    /// <summary>
    /// Static facade over the LOCAL member's stats (<see cref="CharacterData.Session"/>, which wraps
    /// <see cref="GameSession"/>), so the character, skill and equipment screens keep working unchanged.
    /// Gameplay code of a party member uses its own <see cref="CharacterData.Stats"/> instead.
    /// </summary>
    public static class CharacterStats
    {
        public const int BaseMana = CharacterStatsCalc.BaseMana, HpPerLevel = CharacterStatsCalc.HpPerLevel, MpPerLevel = CharacterStatsCalc.MpPerLevel;

        static CharacterStatsCalc L => CharacterData.Session.Stats;

        public static int SlotStat(PassiveStat stat, int slot) => L.SlotStat(stat, slot);
        public static bool Has(Keystone k) => L.Has(k);

        // ---------- Temporary buff (전쟁 함성) of the local member ----------

        public static int BuffDamage => CharacterData.Session.BuffDamage;

        public static int MaxHp => L.MaxHp;
        public static int MaxMp => L.MaxMp;
        public static float ManaRegen => L.ManaRegen;
        public static int IncDamage => L.IncDamage;
        public static int AttackDamage(CharacterClass cls) => L.AttackDamage(cls);
        public static int Block => L.Block;
        public static int SpeedBonus => L.SpeedBonus;
        public static float SpeedMultiplier => L.SpeedMultiplier;
        public static int AttackSpeed => L.AttackSpeed;
        public static float CooldownMultiplier => L.CooldownMultiplier;
        public static int Aoe => L.Aoe;
        public static int ManaCostReduction => L.ManaCostReduction;
        public static int LifeOnKill => L.LifeOnKill;
        public static int ManaOnKill => L.ManaOnKill;
        public static int Power(CharacterClass cls) => L.Power(cls);

        public static SkillNumbers Skill(CharacterClass cls, int slot, SkillGem active, IEnumerable<SkillGem> supports)
            => L.Skill(cls, slot, active, supports);
    }
}
