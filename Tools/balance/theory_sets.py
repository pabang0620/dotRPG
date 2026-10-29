"""
dotRPG 장비 세트 효과 이론 계산 (Docs/PLAN_GEAR_SETS_2026-10-29.md).

세트 효과는 장비 합계에 곱한다: 장비 공격력 합 x (1 + 공격%), 장비 체력 합 x (1 + 체력%).
캐릭터 기본 공격력(10)·레벨 체력은 늘지 않으므로 실제 피해 증가는 공격% x 장비 몫이다.

확인하는 것
- Lv40·Lv50의 측정 장비 세 벌(사냥 레어 +5, 레이드 에픽 +5, 클리어 KIT)에서 피해·체력 증가
- 요일 던전 군주 실측 최단 133초가 목표 하한 120초 밑으로 내려가지 않는지
- 사냥 무리 처치 시간(전투 + 이동 4초, theory_progress.py)의 단축 = 플레이 시간 단축
- 서버 화력 상한 사용률(실측 최대 0.55, 경고 0.8): 상한도 같은 배율로 넓히므로 변하지 않는다

사용: python3 Tools/balance/theory_sets.py
"""
import json
import os

DATA = os.path.join(os.path.dirname(__file__), "..", "..", "server", "data")
STEPS = {s["id"]: s["levels"] for s in json.load(open(os.path.join(DATA, "enhance.json"), encoding="utf-8"))["steps"]}
EQUIP = {e["id"]: e for e in json.load(open(os.path.join(DATA, "shop.json"), encoding="utf-8"))["equipment"]}

# Items/GearSets.cs와 같은 표: 가장 낮은 등급 -> (2세트 체력, 3세트 공격, 5세트 공격, 5세트 체력) %, 레어 이하는 체력만
SETS = {
    "low": {2: (0, 3), 3: (0, 3), 5: (0, 4)},
    "e": {2: (0, 5), 3: (5, 0), 5: (5, 5)},
    "un": {2: (0, 6), 3: (6, 0), 5: (6, 6)},
    "l": {2: (0, 7), 3: (7, 0), 5: (7, 7)},
}


def stat(key, plus, field):
    levels = STEPS.get(key)
    if levels:
        v = levels[min(plus, len(levels) - 1)].get(field)
        if v is not None:
            return v
    return EQUIP[key]["attack" if field == "statsAttack" else "maxHealth"]


def kit(level, pieces):
    """pieces: [(종류, 등급, 강화)] -> 장비 공격 합, 장비 체력 합"""
    atk = hp = 0
    for kind, grade, plus in pieces:
        key = f"eq_{kind}_{level}_{grade}"
        atk += stat(key, plus, "statsAttack")
        hp += stat(key, plus, "statsHealth")
    return atk, hp


def totals(group, count):
    a = h = 0
    for step, (sa, sh) in SETS[group].items():
        if count >= step:
            a, h = a + sa, h + sh
    return a, h


def player_hp(level):
    # 전사 기준(CharacterStatsCalc): 60 + 50 + (L - 1) x (8 + 6), 파이터 x1.1
    return 60 + 50 + (level - 1) * 14


KITS = {
    "사냥(레어 +5)": ("low", [("sword", "r", 5), ("plate", "r", 0), ("greaves", "r", 0), ("neck", "r", 0), ("ring", "r", 0), ("ring", "r", 0)]),
    "클리어 KIT(에픽 +11/+10, 장신구 레어 +8)": ("low", [("sword", "e", 11), ("plate", "e", 10), ("greaves", "e", 10), ("neck", "r", 8), ("ring", "r", 8)]),
    "레이드(에픽 +5)": ("e", [("sword", "e", 5), ("plate", "e", 5), ("greaves", "e", 5), ("neck", "e", 5), ("ring", "e", 5), ("ring", "e", 5)]),
    "유니크 한 벌 +10": ("un", [("sword", "un", 10), ("plate", "un", 10), ("greaves", "un", 10), ("neck", "un", 10), ("ring", "un", 10), ("ring", "un", 10)]),
    "레전더리 한 벌 +10": ("l", [("sword", "l", 10), ("plate", "l", 10), ("greaves", "l", 10), ("neck", "l", 10), ("ring", "l", 10), ("ring", "l", 10)]),
}


def main():
    print("| Lv | 장비 | 세트(공/체 %) | 장비 공격 | 피해 증가 | 장비 체력 | 최대 체력 증가 |")
    print("|---|---|---|---|---|---|---|")
    worst = {}
    for level in (40, 50):
        for name, (group, pieces) in KITS.items():
            ga, gh = kit(level, pieces)
            sa, sh = totals(group, 5)
            dmg = (10 + ga * (1 + sa / 100)) / (10 + ga) - 1
            hp_total = (player_hp(level) + gh) * 1.1
            hp_gain = gh * sh / 100 * 1.1 / hp_total
            worst[name] = max(worst.get(name, 0), dmg)
            print(f"| {level} | {name} | {sa}/{sh} | {ga} | +{dmg * 100:.1f}% | {gh} | +{hp_gain * 100:.1f}% |")

    lord = 133 / (1 + worst["레이드(에픽 +5)"])
    print(f"\n요일 던전 군주 최단 133초(에픽 한 벌로 보면): {lord:.0f}초 (목표 하한 120초)")
    # 사냥 무리 하나: Lv41+ 지상 실측 18.5킬/분(파이터 자동) = 3마리에 9.7초, 그중 이동 4초
    pack, travel = 3 * 60 / 18.5, 4.0
    for name in ("사냥(레어 +5)", "레이드(에픽 +5)"):
        d = worst[name]
        faster = 1 - (travel + (pack - travel) / (1 + d)) / pack
        print(f"사냥 무리 처치 시간 단축({name}): {faster * 100:.1f}%")


if __name__ == "__main__":
    main()
