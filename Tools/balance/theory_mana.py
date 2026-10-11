"""
dotRPG MP (마나) 이론 계산. 게임을 돌리지 않는다. 직업별·레벨별로 "주력 연사기만 계속 누를 때 MP가 바닥나는 시간"과
"보통 사냥(이동 + 전투)에서 분당 마나 물약 수"를 비교한다. 결과와 목표는 Docs/BALANCE_COMBAT_2026-10-11.md 의 MP 절.

출처(코드 값 그대로)
- 최대 MP: CharacterStatsCalc.MaxMp = (BaseMana 40 + 직업 보너스 + (Lv-1) x (MpPerLevel 4 + 직업 추가)) x 전직 배율
  (메이지 계열 +40·레벨당 +3, 아르카니스트 x1.2, 비숍 x1.25). 패시브 FlatMp 는 넣지 않는다(보수적).
- 회복: 초당 최대 MP의 5% (ManaRegen, 패시브 제외).
- 물약: 마나 물약 최대 MP의 50%, 상급 70% (ConsumableDatabase power, HealMp 는 %).
- 연사기 MP: 이전은 CareerData S() 의 mp x 0.65 반올림, 지금은 최대 MP x MainHunterMpShare. 보조 젬 manaMult(추가 피해 1.3) 하나를 낀 것으로 본다.
- 연사 간격: 시전 + CareerMoves.Recovery + 0.08. 회귀의 방패는 동시에 2+단계(최대 5)개, 왕복 1.15초라 초당 개수 / 1.15 가 상한.
- 보통 사냥: 전투 비율 0.55 (무리 정리 4~7초 + 이동 4초, theory_combat 목표) 동안 연사기, 그 밖에 쿨 스킬 4초마다 MP 10,
  버프 62초마다 MP 15 를 더한다.
사용: python3 Tools/balance/theory_mana.py [--before]
"""
import sys

BASE, MP_LV = 40, 4
DUTY, CD_SKILL_MP_PER_S, BUFF_MP_PER_S = .55, 10 / 4, 15 / 62
SUPPORT = 1.3

BEFORE = dict(war_bonus=10, war_lv=0, mage_bonus=40, mage_lv=3,
              spam={"fighter": 18, "guardian": 18, "arcanist": 18, "bishop": 3})
# 2026-10-12: warrior MP 40 + 6/Lv, main hunting skills cost a share of max MP (CareerSkill.MainHunterMpShare)
AFTER = dict(war_bonus=40, war_lv=2, mage_bonus=40, mage_lv=3,
             share={"fighter": .0263, "guardian": .0199, "arcanist": .0333, "bishop": .0111})

CAREERS = {  # mage?, career MP x, spam skill, casts per second at full speed
    "fighter": (False, 1.0, "파쇄 검기", 1 / (.15 + .15 + .08)),
    "guardian": (False, 1.0, "회귀의 방패(2단계, 4개)", min(1 / (.08 + .14 - .04 + .08), 4 / 1.15)),
    "arcanist": (True, 1.2, "연쇄전격", 1 / (.25 + .15 + .08)),
    "bishop": (True, 1.25, "심판의 광창", 1 / (.04 + .04 + .08)),
}


def max_mp(p, mage, mul, lv):
    bonus, per = (p["mage_bonus"], p["mage_lv"]) if mage else (p["war_bonus"], p["war_lv"])
    return round((BASE + bonus + (lv - 1) * (MP_LV + per)) * mul)


def table(p, title):
    print(f"\n## {title}")
    print("| 직업 | Lv | 최대 MP | 회복/초 | 연사기 MP(1회) | 연사 MP/초 | 연사만 누를 때 바닥(초) | 보통 사냥 물약/분 |")
    print("|---|---|---|---|---|---|---|---|")
    for c, (mage, mul, name, rate) in CAREERS.items():
        for lv in (18, 20, 30, 40):
            m = max_mp(p, mage, mul, lv)
            cost = (m * p["share"][c] if "share" in p else max(1, round(p["spam"][c] * .65))) * SUPPORT
            regen = m * .05
            spam = cost * rate
            empty = m / (spam - regen) if spam > regen else float("inf")
            net = DUTY * spam + CD_SKILL_MP_PER_S + BUFF_MP_PER_S - regen
            pots = max(0.0, net) * 60 / (m * .5)
            e = "무한" if empty == float("inf") else f"{empty:.0f}"
            print(f"| {c} {name} | {lv} | {m} | {regen:.1f} | {cost:.1f} | {spam:.1f} | {e} | {pots:.2f} |")


if __name__ == "__main__":
    if "--before" in sys.argv:
        table(BEFORE, "이전 (2026-10-11 마나 조정 전)")
    table(AFTER, "현재 (마나 조정 후)")
