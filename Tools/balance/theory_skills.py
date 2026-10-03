"""
dotRPG 스킬 이론 분석 (게임을 돌리지 않는 수치 점검).

출처: Progression/SkillData.cs SkillGems (damageMult, cooldown, radius, range, chains, freeze, stun, manaCost, hits),
PlayerStats attackCooldown 0.36 / attackRadius 0.62 (전사 기본 공격), CharacterClassInfo cooldown 0.5 (마법사 구슬),
CharacterStatsCalc: MP = 40 + 4/Lv, 초당 회복 = 최대 MP의 5%. 범위 보너스는 반지름에 곱해진다(면적은 제곱).

가정
- 무리 밀도: 던전 방 한 무리 5마리가 반지름 약 2.5 칸 안에 모여 있다 -> 0.25마리/칸^2, 최대 5마리.
- 선형 스킬(검기·대지 강타)의 면적 = 사거리 x 폭(반지름 x 2).
- 연쇄 스킬은 첫 대상 + 연쇄 수 (무리 크기 한도).
사용: python3 Tools/balance/theory_skills.py
"""
import math

DENSITY, PACK = 0.25, 5
LEVEL = 10
MP = 40 + 4 * (LEVEL - 1)
REGEN = MP * 0.05

BASIC = {  # 기본 공격: (피해 배율, 간격, 맞는 마리 수)
    "전사": (1.0, 0.36, 1 + DENSITY * math.pi * 0.62 ** 2 / 2),   # 반원 휘두르기
    "마법사": (1.0, 0.5, 1.0),
}

# name, class, slot, unlock Lv, dmgMult, cooldown, shape, radius, range, chains, hits, cc(sec), mana
SKILLS = [
    ("회전 베기", "전사", 1, 2, 1.5, 0.9, "circle", 1.7, 0, 0, 1, 0.0, 12),
    ("대지 강타", "전사", 2, 4, 2.2, 1.4, "line", 0.85, 3.4, 0, 1, 0.0, 16),
    ("검기", "전사", 3, 6, 1.8, 1.2, "line", 0.7, 6.5, 0, 1, 0.0, 14),
    ("전쟁 함성", "전사", 4, 8, 0.8, 9.0, "circle", 2.6, 0, 0, 1, 1.5, 22),
    ("천검 강림(각성)", "전사", 5, 10, 2.6, 30.0, "circle", 1.1, 4.8, 0, 10, 0.0, 40),
    ("번개 사슬", "마법사", 1, 2, 1.2, 0.7, "chain", 3.2, 6.5, 3, 1, 0.0, 10),
    ("서리 폭발", "마법사", 2, 4, 1.1, 2.5, "circle", 2.3, 0, 0, 1, 1.5, 18),
    ("빙뢰구", "마법사", 3, 6, 2.2, 1.6, "circle+chain", 1.5, 7.5, 2, 1, 1.2, 17),
    ("낙뢰", "마법사", 4, 8, 1.9, 3.5, "chain", 0.9, 6.5, 3, 1, 0.5, 22),
    ("메테오(각성)", "마법사", 5, 10, 3.0, 30.0, "circle", 1.5, 5.5, 0, 7, 0.0, 45),
]


# v2 (2026-10-03 개편): 슬롯마다 역할 하나. 1 단일 주력 / 2 범위 / 3 제어 / 4 방어 / 5 각성. 해금 Lv2·6·12·18·22.
SKILLS_V2 = [
    ("파쇄 일격", "전사", 1, 2, 3.2, 2.5, "single", 0, 1.6, 0, 1, 0.0, 6),
    ("회전 베기", "전사", 2, 6, 2.0, 5.0, "circle", 1.6, 0, 0, 1, 0.0, 14),
    ("전쟁 함성", "전사", 3, 12, 0.4, 14.0, "circle", 2.4, 0, 0, 1, 1.0, 18),
    ("철벽", "전사", 4, 18, 0.0, 16.0, "none", 0, 0, 0, 1, 0.0, 12),
    ("천검 강림(각성)", "전사", 5, 22, 2.6, 30.0, "circle", 1.1, 4.8, 0, 10, 0.0, 40),
    ("번개 창", "마법사", 1, 2, 3.0, 2.5, "single", 0, 7.0, 0, 1, 0.0, 6),
    ("빙뢰구", "마법사", 2, 6, 1.9, 5.0, "circle", 1.6, 7.5, 0, 1, 0.0, 14),
    ("서리 폭발", "마법사", 3, 12, 0.4, 14.0, "circle", 2.3, 0, 0, 1, 1.2, 18),
    ("마나 보호막", "마법사", 4, 18, 0.0, 16.0, "none", 0, 0, 0, 1, 0.0, 12),
    ("메테오(각성)", "마법사", 5, 22, 3.0, 30.0, "circle", 1.5, 5.5, 0, 7, 0.0, 45),
]


def targets(shape, radius, rng, chains):
    if shape == "circle":
        return min(PACK, 1 + DENSITY * math.pi * radius ** 2)
    if shape == "line":
        return min(PACK, 1 + DENSITY * rng * radius * 2)
    if shape == "chain":
        return min(PACK, 1 + chains)
    if shape == "single":
        return 1
    if shape == "none":
        return 0
    if shape == "circle+chain":
        return min(PACK, 1 + DENSITY * math.pi * radius ** 2 + chains)
    return 1


def table(skills, title):
    print(f"\n## {title} (Lv{LEVEL}, MP {MP}, 초당 회복 {REGEN:.1f}) - 기본 공격에 더해지는 초당 피해")
    print("| 스킬 | 직업 | 해금 | 1마리 상대(기본 공격 대비 추가) | 맞는 마리 | 무리 상대(기본 공격 대비 추가) | 제어 가동률 | 초당 MP |")
    print("|---|---|---|---|---|---|---|---|")
    for name, cls, slot, lv, dm, cd, shape, r, rng, ch, hits, cc, mana in skills:
        b_dm, b_cd, b_t = BASIC[cls]
        b_single = b_dm / b_cd
        t = targets(shape, r, rng, ch)
        single = dm * hits / cd if t else 0.0
        pack = single * t
        uptime = min(1.0, cc / cd) if cc else 0.0
        print(f"| {name} | {cls} | Lv{lv} | +{single / b_single:.0%} | {t:.1f} | +{pack / (b_single * b_t):.0%} | {uptime:.0%} | {mana / cd:.1f} |")


def main():
    table(SKILLS, "개편 전 (skills-v1)")
    table(SKILLS_V2, "개편 후 (v2)")
    print("\n## 범위 보너스가 겹칠 때")
    for label, mult in (("보조 젬 범위 확대(+35%)", 1.35), ("+ 패시브 범위 확장(+25%)", 1.35 * 1.25)):
        print(f"- {label}: v1 반지름 x{mult:.2f} -> 면적 x{mult ** 2:.2f} / v2 면적 x{mult:.2f} (반지름 x{mult ** 0.5:.2f})")
    print("\n- v2 범위 손익분기: 회전 베기(2.0, 5초)는 3마리 이상일 때 파쇄 일격(3.2, 2.5초)보다 초당 피해가 많다")
    print("- v2 제어: 일반 몬스터도 6초 안에 다시 걸리면 지속시간이 절반씩 준다. 보스는 x0.3 유지")


if __name__ == "__main__":
    main()
