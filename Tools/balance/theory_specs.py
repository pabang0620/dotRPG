"""챕터 3 특화 기대값 점검 (Docs/ch3/SKILL_SPECIALIZATION.md 1절 기대값 원칙, 7절 검증 도구).

server/data/skill_specs.json(Unity 내보내기)의 96개 특화를 축별 기준 상황으로 비교한다.
- 집중·확산: A 보스 1체 한 번 시전 = B 묶음 3체 한 번 시전 합
- 폭발·지속: 보스 1체 60초 총 피해
- 공격·기동: B 피해 = A의 60%(나머지는 방어·기동으로 돌려받음, 측정 봇 피격 수로 검증)
- 회복·제어·버프: A 1명(1체) = B 3명(3체) 합
A·B 차이가 10%를 넘는 줄에 표시한다. 그리고 특화별 "보스 방 상한 대비 비율"을 낸다
(attemptDamageCap = 화력 상한 x 180초 x DAMAGE_SLACK, 티어 10 클리어 장비 기준). 0.8(HIGH_DAMAGE_RATIO)에 닿는 줄을 표시한다.

EV 표는 스킬 구현 포크가 수치를 채운다: EV[skill] = (A 기준값, B 기준값). 비어 있으면 "미입력"으로 나온다.
보스 방 비율은 BOSS_DPS[spec_id](3분 평균 초당 피해, 측정 또는 계산)가 있을 때만 낸다.

  python3 Tools/balance/theory_specs.py            # 표
  python3 Tools/balance/theory_specs.py --strict   # 차이 10% 초과나 미입력이 있으면 종료 코드 1
"""
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SPECS = os.path.join(ROOT, "server", "data", "skill_specs.json")

TOLERANCE = 0.10          # A·B 기대값 허용 차이
MOBILITY_RATIO = 0.60     # 공격·기동 축: B 피해 = A x 0.6
HIGH_DAMAGE_RATIO = 0.8   # server bossRushService HIGH_DAMAGE_RATIO
DAMAGE_SLACK = 0.6        # server bossRushRules DAMAGE_SLACK
ATTEMPT_SECONDS = 180
# 티어 10 클리어 장비 화력 상한(초당). Docs/ch3/PROGRESSION_AND_GEAR.md 7절 값으로 채운다
DPS_CAP_T10 = None

# 축별 기준 기대값: skill -> (A, B). 설계 3절 "기대값" 열의 숫자를 구현과 함께 채운다(lance는 예시)
EV = {
    "lance": (1.5, 1.5),
}
# 특화 id -> 보스 방 3분 평균 초당 피해(측정 또는 계산)
BOSS_DPS = {}


def load_specs():
    with open(SPECS, encoding="utf-8") as f:
        data = json.load(f)
    pairs = {}
    for s in data["specs"]:
        pairs.setdefault(s["skill"], {})[s["side"]] = s
    return data["points"], pairs


def ratio_ok(axis_a, a, b):
    if axis_a == "attack":
        return abs(b - a * MOBILITY_RATIO) <= a * MOBILITY_RATIO * TOLERANCE
    return abs(a - b) <= max(a, b) * TOLERANCE


def main():
    strict = "--strict" in sys.argv
    points, pairs = load_specs()
    print(f"포인트: Lv{points['from']}~{points['to']} 레벨당 1, 액티브 {points['active']}점 · 패시브 {points['passive']}점")
    print(f"{'스킬':<12}{'축 A/B':<20}{'A 이름':<14}{'B 이름':<14}{'A':>8}{'B':>8}  판정")
    bad = 0
    for skill, sides in pairs.items():
        a, b = sides.get("a"), sides.get("b")
        axes = f"{a['axis']}/{b['axis']}"
        ev = EV.get(skill)
        if ev is None:
            verdict, va, vb = "미입력", "-", "-"
            bad += 1 if a["axis"] != "passive" else 0
        else:
            ok = ratio_ok(a["axis"], ev[0], ev[1])
            verdict, va, vb = ("통과" if ok else "차이 10% 초과"), f"{ev[0]:.2f}", f"{ev[1]:.2f}"
            bad += 0 if ok else 1
        print(f"{skill:<12}{axes:<20}{a['name']:<14}{b['name']:<14}{va:>8}{vb:>8}  {verdict}")
    if DPS_CAP_T10 and BOSS_DPS:
        cap = DPS_CAP_T10 * ATTEMPT_SECONDS * DAMAGE_SLACK
        print(f"\n보스 방 상한(티어 10): {cap:,.0f}")
        for sid, dps in sorted(BOSS_DPS.items()):
            r = dps * ATTEMPT_SECONDS / cap
            print(f"  {sid:<16}{r:6.2f}{'  상한 근접' if r >= HIGH_DAMAGE_RATIO else ''}")
    else:
        print("\n보스 방 상한 비율: DPS_CAP_T10·BOSS_DPS 미입력")
    if strict and bad:
        sys.exit(1)


if __name__ == "__main__":
    main()
