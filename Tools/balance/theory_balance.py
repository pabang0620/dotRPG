"""
dotRPG 난이도 이론 계산 (게임을 돌리지 않는 밸런스 점검).

게임 데이터에서 옮긴 수치로 난이도별 "권장 레벨의 전형적인 캐릭터"와 몬스터를 비교한다.
- 클리어 시간 = 일반 실측 기준 시간 x (몬스터 실효 체력 비) / (파티 화력 비)
- 생존 = 플레이어 체력 / 몬스터 실효 피해 (일반 대비 비율)

출처
- 몬스터 레벨 성장: MonsterDatabase HpPerLevel 0.12, DamagePerLevel 0.08, 레벨 = 1 + monsterLevel
- 난이도 배율: DungeonDatabase.Difficulties, 레이드: RaidNumbers(...)
- 플레이어: PlayerStats attackDamage 10, maxHealth 60, 레벨당 체력 +8 (CharacterStatsCalc)
- 장비: EquipmentDatabase W(...), 강화 = 씨앗 x EnhanceCoef[레벨], 무기 씨앗 0.135 x (티어 + 1)
- 패시브: 레벨당 1점, 절반을 피해 증가(점당 약 5.5%)에 쓴다고 가정
- 일반 기준 시간 150초: 2026-10-02 -dotrpgBalance 일반 5종 실측 87~180초의 대표값

사용: python3 Tools/balance/theory_balance.py
"""

ENHANCE = [0, 1.1, 2.2, 3.3, 4.5, 5.7, 7.0, 8.3, 11.11, 14.7, 18.9, 27.25, 37.13]
HP_PER_LV, DMG_PER_LV = 0.12, 0.08
NORMAL_SECONDS = 150.0
TARGET = (120.0, 300.0)          # PLAN_DUNGEON_RAID 목표 (요일 던전)
RAID_TARGET = (300.0, 600.0)


def weapon(atk, tier, plus):
    return atk + round(0.135 * (tier + 1) * ENHANCE[plus])


# 권장 레벨에서 손에 들어오는 전형적인 장비 (드롭 등급·강화 단계)
BUILDS = {
    #            레벨  무기 공격            장신구 공격  방어구·장신구 체력
    "일반":   (5,  weapon(10, 1, 0),   0,  20 + 10 + 20 + 10),          # 철검, 천 상하의, 잎 목걸이, 구리 반지
    "모험":   (12, weapon(10, 1, 7),  10,  20 + 20 + 10 + 20),          # 철검+7, 뼈 목걸이, 가죽 상하의
    "왕":     (20, weapon(20, 2, 10), 20,  10 + 40 + 20 + 10 + 10),     # 해골 대검+10, 뼈 목걸이, 루비 반지, 철 흉갑
    "영웅":   (27, weapon(30, 3, 12), 20,  20 + 20 + 10 + 40 + 20 + 15),  # 용골 대검+12, 해골왕 목걸이, 루비 반지
}
RAID_BUILDS = {22: "왕", 24: "왕", 28: "영웅", 31: "영웅"}


def player(level, wpn, acc_atk, gear_hp):
    inc = (level - 1) / 2 * 5.5 / 100
    atk = 10 + wpn + acc_atk
    return atk * (1 + inc), 60 + (level - 1) * 8 + gear_hp


def monster(hp_mul, dmg_mul, mlevel):
    lv = 1 + mlevel
    return hp_mul * (1 + HP_PER_LV * (lv - 1)), dmg_mul * (1 + DMG_PER_LV * (lv - 1))


def table(diffs, title):
    base_dps, base_hp = player(*BUILDS["일반"])
    base_mhp, base_mdmg = monster(*diffs["일반"])
    print(f"\n## {title}")
    print("| 난이도 | 권장Lv | 화력 | 체력 | 몬스터 실효 체력 | 몬스터 실효 피해 | 예상 클리어 | 생존(일반=1.00) | 판정 |")
    print("|---|---|---|---|---|---|---|---|---|")
    for name, numbers in diffs.items():
        lv, wpn, acc, ghp = BUILDS[name]
        dps, hp = player(lv, wpn, acc, ghp)
        mhp, mdmg = monster(*numbers)
        t = NORMAL_SECONDS * (mhp / base_mhp) / (dps / base_dps)
        surv = (hp / mdmg) / (base_hp / base_mdmg)
        ok = TARGET[0] <= t <= TARGET[1] and surv >= 0.55
        print(f"| {name} | {lv} | {dps:.0f} | {hp:.0f} | x{mhp:.2f} | x{mdmg:.2f} | {t:.0f}초 | {surv:.2f} | {'통과' if ok else '조정 필요'} |")


CURRENT = {"일반": (1.0, 1.0, 0), "모험": (2.2, 1.6, 7), "왕": (4.0, 2.4, 15), "영웅": (6.5, 3.4, 22)}
# 조정안: 몬스터 레벨 성장(체력 +12%/Lv, 피해 +8%/Lv)이 이미 크므로 배율을 그만큼 낮춘다.
# 목표: 클리어 시간이 일반의 1.2 / 1.4 / 1.6배, 생존이 일반의 0.8 / 0.7 / 0.6배.
PROPOSED = {"일반": (1.0, 1.0, 0), "모험": (1.2, 1.05, 7), "왕": (2.0, 1.25, 15), "영웅": (2.7, 1.6, 22)}

RAIDS = {  # 이름: (권장Lv, 보스 체력, hpMul, dmgMul, monsterLevel) - DungeonDatabase RaidNumbers / MonsterDatabase Boss
    "해골왕(중간, 챕터1)": (22, 3200, 2.6, 1.4, 14),
    "바르가스(최종, 챕터1)": (24, 4200, 3.0, 1.6, 17),
    "골렘(중간, 챕터2)": (28, 5200, 3.4, 1.8, 22),
    "그라흐(최종, 챕터2)": (31, 6400, 3.8, 2.0, 25),
}


# 조정안: 해골왕(실측 490~600초와 계산 494초가 일치)을 기준으로 뒤 레이드가 400~560초에 들도록 체력 배율을 낮춘다.
RAIDS_PROPOSED = {
    "해골왕(중간, 챕터1)": (22, 3200, 2.6, 1.4, 14),
    "바르가스(최종, 챕터1)": (24, 4200, 2.1, 1.6, 17),
    "골렘(중간, 챕터2)": (28, 5200, 2.2, 1.8, 22),
    "그라흐(최종, 챕터2)": (31, 6400, 1.9, 1.7, 25),
}


def raids(data, title):
    base_dps, base_hp = player(*BUILDS["일반"])
    # 일반 던전 보스(약 950)를 일반 시간의 약 40%에 잡는다는 기준으로, 레이드는 보스 체력 비를 곱한다.
    print(f"\n## {title}")
    print("| 레이드 | 권장Lv | 실효 체력 | 실효 피해 | 예상 클리어 | 생존 | 판정 |")
    print("|---|---|---|---|---|---|---|")
    for name, (lv, boss_hp, hm, dm, ml) in data.items():
        _, wpn, acc, ghp = BUILDS[RAID_BUILDS[lv]]
        dps, hp = player(lv, wpn, acc, ghp)
        mhp, mdmg = monster(hm, dm, ml)
        t = NORMAL_SECONDS * 0.6 * mhp / (dps / base_dps) + NORMAL_SECONDS * 0.4 * mhp * (boss_hp / 950) / (dps / base_dps)
        surv = (hp / mdmg) / (base_hp / 1.0)
        ok = RAID_TARGET[0] <= t <= RAID_TARGET[1] and surv >= 0.45
        print(f"| {name} | {lv} | x{mhp:.2f} | x{mdmg:.2f} | {t:.0f}초 | {surv:.2f} | {'통과' if ok else '조정 필요'} |")


# ---------- 장비 개편(2026-10-05, Docs/PLAN_GEAR_RENEWAL.md): GearCatalog 공식 ----------
TIER_LEVELS = [1, 10, 15, 20, 25, 30, 35, 40]
W_BASE = [6, 10, 14, 18, 23, 28, 34, 40]
H_BASE = [20, 40, 55, 70, 90, 110, 135, 160]
GRADE = {"c": 0.6, "u": 0.8, "r": 1.0, "e": 1.25, "un": 2.3, "l": 3.2}


def tier_of(level):
    t = 0
    for i, lv in enumerate(TIER_LEVELS):
        if lv <= level:
            t = i
    return t


def gear_build(level, grade="r", plus=10):
    """그 레벨 단계의 한 등급 장비 한 벌(검, 갑옷, 각반, 목걸이, 반지 2) + 무기 강화"""
    t, m = tier_of(level), GRADE[grade]
    seed = 0.135 * (1 + 0.5 * t)
    wpn = round(W_BASE[t] * m) + round(seed * ENHANCE[min(plus, 12)])
    acc = round(W_BASE[t] * 0.3 * m) + 2 * round(W_BASE[t] * 0.35 * m)
    hp = round(H_BASE[t] * m) + round(H_BASE[t] * 0.6 * m) + round(H_BASE[t] * 0.3 * m)
    return (level, wpn, acc, hp)


NEW_BUILDS = {
    "일반": gear_build(5, "r", 0),
    "모험": gear_build(12, "r", 7),
    "왕": gear_build(20, "r", 10),
    "영웅": gear_build(27, "e", 12),
}
NEW_RAIDS = {  # 2026-10-05 현재 수치: 해골왕 Lv.20, 그라흐 Lv.40
    "해골왕(Lv.20)": (20, 3200, 2.6, 1.4, 12, gear_build(20, "e", 10)),
    "그라흐(Lv.40)": (40, 6400, 1.9, 1.7, 35, gear_build(40, "e", 12)),
}


def new_tables():
    global BUILDS
    old = BUILDS
    BUILDS = NEW_BUILDS
    print("\n## 장비 개편 후 빌드:", {k: v for k, v in NEW_BUILDS.items()})
    table(PROPOSED, "요일 던전 - 장비 개편 후(현재 난이도 배율)")
    base_dps, base_hp = player(*NEW_BUILDS["일반"])
    print("\n## 레이드 - 장비 개편 후")
    print("| 레이드 | 권장Lv | 실효 체력 | 실효 피해 | 예상 클리어 | 생존 | 판정 |")
    print("|---|---|---|---|---|---|---|")
    for name, (lv, boss_hp, hm, dm, ml, build) in NEW_RAIDS.items():
        dps, hp = player(*build)
        mhp, mdmg = monster(hm, dm, ml)
        tt = NORMAL_SECONDS * 0.6 * mhp / (dps / base_dps) + NORMAL_SECONDS * 0.4 * mhp * (boss_hp / 950) / (dps / base_dps)
        surv = (hp / mdmg) / (base_hp / 1.0)
        ok = RAID_TARGET[0] <= tt <= RAID_TARGET[1] and surv >= 0.45
        print(f"| {name} | {lv} | x{mhp:.2f} | x{mdmg:.2f} | {tt:.0f}초 | {surv:.2f} | {'통과' if ok else '조정 필요'} |")
    BUILDS = old


if __name__ == "__main__":
    table(CURRENT, "요일 던전 - 현재 수치")
    table(PROPOSED, "요일 던전 - 조정안")
    raids(RAIDS, "레이드 - 현재 수치")
    raids(RAIDS_PROPOSED, "레이드 - 조정안")
    new_tables()
