using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [SKILL v1] The skill set before the 2026-10-03 rework, kept so it can be restored:
    /// warrior 회전 베기 · 대지 강타 · 검기 · 전쟁 함성 · 천검 강림, mage 번개 사슬 · 서리 폭발 · 빙뢰구 · 낙뢰 · 메테오,
    /// slots opening at Lv 2/4/6/8/10. Switch <see cref="SkillGems.UseLegacy"/> to true to play with it again
    /// (cast code for every v1 skill is still in SkillCaster). The whole v1 game is also tagged <c>skills-v1</c> in git.
    /// </summary>
    public static class SkillGemsLegacy
    {
        public static readonly int[] SlotLevels = { 2, 4, 6, 8, 10 };

        static SkillGem A(string id, string name, CharacterClass cls, int slot, string desc)
            => new SkillGem { id = id, name = name, icon = "gem_" + id, kind = GemKind.Active, classOnly = cls, slot = slot, unlockLevel = SlotLevels[slot], description = desc };

        public static List<SkillGem> Create() => new List<SkillGem>
        {
            // Warrior.
            A("whirl", "회전 베기", CharacterClass.Warrior, 0, "제자리에서 한 바퀴 돌며 주변의 모든 적을 벤다.").With(g => { g.damageMult = 1.5f; g.cooldown = 0.9f; g.radius = 1.7f; g.manaCost = 12; }),
            A("slam", "대지 강타", CharacterClass.Warrior, 1, "땅을 내려쳐 조준 방향으로 갈라지는 충격파를 보낸다.").With(g => { g.damageMult = 2.2f; g.cooldown = 1.4f; g.radius = 0.85f; g.range = 3.4f; g.manaCost = 16; }),
            A("wave", "검기", CharacterClass.Warrior, 2, "조준 방향으로 검기를 날려 지나가는 길의 모든 적을 벤다.").With(g => { g.damageMult = 1.8f; g.cooldown = 1.2f; g.radius = 0.7f; g.range = 6.5f; g.manaCost = 14; }),
            A("cry", "전쟁 함성", CharacterClass.Warrior, 3, "함성으로 주변 적을 기절시키고, 잠시 동안 모든 피해가 증가한다.").With(g => { g.damageMult = 0.8f; g.cooldown = 9f; g.radius = 2.6f; g.stun = 1.5f; g.buffPct = 25; g.buffTime = 6f; g.manaCost = 22; }),
            A("blades", "천검 강림", CharacterClass.Warrior, 4, "하늘에서 거대한 검을 떨어뜨려 주변의 적을 꿰뚫는다.").With(g => { g.damageMult = 2.6f; g.cooldown = 30f; g.radius = 1.1f; g.range = 4.8f; g.hits = 10; g.manaCost = 40; }),
            // Mage.
            A("arc", "번개 사슬", CharacterClass.Mage, 0, "가장 가까운 적에게 번개를 쏘고, 주변 적에게 연쇄된다.").With(g => { g.damageMult = 1.2f; g.cooldown = 0.7f; g.range = 6.5f; g.radius = 3.2f; g.chains = 3; g.manaCost = 10; }),
            A("nova", "서리 폭발", CharacterClass.Mage, 1, "주변에 냉기를 터뜨려 적을 얼린다 (1.5초 동안 행동 불가).").With(g => { g.damageMult = 1.1f; g.cooldown = 2.5f; g.radius = 2.3f; g.freeze = 1.5f; g.manaCost = 18; }),
            A("frostorb", "빙뢰구", CharacterClass.Mage, 2, "번개를 두른 얼음 구체를 날린다. 날아가는 동안 가까운 적에게 번개를 튀기고, 부딪히면 얼음 파편이 터지며 주변 적을 얼린다.").With(g => { g.damageMult = 2.2f; g.cooldown = 1.6f; g.radius = 1.5f; g.range = 7.5f; g.chains = 2; g.freeze = 1.2f; g.manaCost = 17; }),
            A("thunder", "낙뢰", CharacterClass.Mage, 3, "하늘에서 번개를 내리쳐 주변의 적 여럿을 동시에 공격하고 잠시 기절시킨다.").With(g => { g.damageMult = 1.9f; g.cooldown = 3.5f; g.radius = 0.9f; g.range = 6.5f; g.chains = 3; g.stun = 0.5f; g.manaCost = 22; }),
            A("meteor", "메테오", CharacterClass.Mage, 4, "거대한 운석을 연달아 떨어뜨려 넓은 지역을 불태운다.").With(g => { g.damageMult = 3f; g.cooldown = 30f; g.radius = 1.5f; g.range = 5.5f; g.hits = 7; g.manaCost = 45; }),

            new SkillGem { id = "sup_dmg", name = "추가 피해", icon = "gem_sup_dmg", kind = GemKind.Support, unlockLevel = 3, moreDamage = 35, manaMult = 1.3f,
                description = "연결된 스킬의 피해 35% 증폭. MP 소모 30% 증가." },
            new SkillGem { id = "sup_aoe", name = "범위 확대", icon = "gem_sup_aoe", kind = GemKind.Support, unlockLevel = 4, moreAoe = 35, manaMult = 1.15f,
                description = "연결된 스킬의 범위 35% 증폭. MP 소모 15% 증가." },
            new SkillGem { id = "sup_multi", name = "연속 시전", icon = "gem_sup_multi", kind = GemKind.Support, unlockLevel = 6, repeats = 1, moreDamage = -30, manaMult = 1.4f,
                description = "연결된 스킬이 한 번 더 발동한다. 피해 30% 감소, MP 소모 40% 증가." },
            new SkillGem { id = "sup_eff", name = "마력 효율", icon = "gem_sup_eff", kind = GemKind.Support, unlockLevel = 7, manaMult = 0.7f,
                description = "연결된 스킬의 MP 소모 30% 감소." },
            new SkillGem { id = "sup_leech", name = "흡혈", icon = "gem_sup_leech", kind = GemKind.Support, unlockLevel = 8, leechPct = 10,
                description = "연결된 스킬이 입힌 피해의 10%만큼 HP를 회복한다." },
            new SkillGem { id = "sup_chain", name = "추가 연쇄", icon = "gem_sup_chain", kind = GemKind.Support, unlockLevel = 9, extraChains = 2, moreDamage = 10,
                description = "번개 사슬 연쇄 +2, 낙뢰 +2회, 빙뢰구 번개 대상 +2. 모든 스킬 피해 10% 증폭." },
        };
    }
}
