"""챕터 3 특화 기대값 점검 (Docs/ch3/SKILL_SPECIALIZATION.md 1절 기대값 원칙, 7절 검증 도구).

server/data/skill_specs.json(Unity 내보내기)의 146개 특화(전사·마법사 96 + 도적 50)를 축별 기준 상황으로 비교한다.
- 집중·확산: A 보스 1체 한 번 시전 = B 묶음 3체 한 번 시전 합
- 폭발·지속: 보스 1체 60초 총 피해
- 공격·기동: B 피해 = A의 60%(나머지는 방어·기동으로 돌려받음, 측정 봇 피격 수로 검증)
- 회복·제어·버프: A 1명(1체) = B 3명(3체) 합
A·B 차이가 10%를 넘는 줄에 표시한다. 그리고 특화별 "보스 방 상한 대비 비율"을 낸다
(attemptDamageCap = 화력 상한 x 180초 x DAMAGE_SLACK, 티어 10 클리어 장비 기준). 0.8(HIGH_DAMAGE_RATIO)에 닿는 줄을 표시한다.
보스 방 비율은 상한 쪽 추정이다: 실측 전사 피크 비율 0.37(티어 8, PROGRESSION_AND_GEAR.md 7절. 상한도 attackCap에 비례하므로
티어 10에서도 같다) x 직업 배율 뒤 화력(파이터 = 1, PLAN_CLASS_ROGUE.md 3.2절) x 특화 이득(집중·폭발·공격 A는 최대 1.5배, 그 밖 1.0).

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
    # 마법사 계열(마법사 기본·메이지·비숍, 2026-10-22). 축별 기준은 위 설명과 같다
    "frostorb": (3.2 / 6 * 60, 0.8 / 1.5 * 60),        # 폭발·지속: 60초(쿨 6 / 1.5)
    "nova": (5.0, 1.6 * 3),                               # 제어: 빙결 5.0초 1체 / 1.6초 x 3체
    "firefield": (1.2 * 6, 0.4 * 6 * 3),
    "meteor": (2.5 * (22.4 + 0.5 * 6), 5 * 8 * 1.6),      # 60초(쿨 24 / 12), 균열 6초 포함
    "m_fire": (40 * 4.0, 180 * 0.9),                      # 60초: 쿨 1.5초 40회 / 시전 0.33초 180회(화상 제외)
    "m_ice": (7.8, 1.3 * 2 * 3),
    "m_storm": (3.9, 1.1 * 3 + 1.1 * 0.5),                # 3체 연쇄 + 첫 대상 복귀 50%
    "m_orbit": (6.0, 3.6), "m_veil": (5.0, 3.0), "b_cleanse": (3.2, 2.0),
    "m_rift": (60 / 18 * 14.0, 60 / 10 * (8 * 0.8 + 1.2)),
    "m_awake": (12 * 0.075 * 16, 288 * 0.05),
    "m_barrier": (0.55, 0.18 * 3),                        # 보호막 총량: 1명 / 3명
    "b_heal": (5.6, 1.9 * 3), "b_bloom": (1.6 * 6, 0.55 * 6 * 3), "b_light": (2.5, 0.85 * 3),
    "b_wing": (2.5, 0.8 * 3),                             # 무적 초: 1명 / 3명
    "b_bless": (35, (9 + 3) * 3),                         # 버프 %: 1명 / 3명 연결
    "b_awake": (6.0, 1.5 + 0.45 * 10),
    # 전사 계열(전사 기본·파이터·수호자). 집중·확산: A 1체 / B 3체 합
    "crush": (4.0, 0.67 * 2 * 3), "f_break": (2.8, 0.7 * 2 * 2), "g_wall": (3.5, 0.75 * 5), "f_cross": (3.4, 1.1 * 3),
    # 폭발·지속: 보스 1체 60초 총량
    "blades": (2.5 * 28.8, 5 * 1.2 * 12), "f_focus": (3 * 8.8, 6 * (2.2 + 2.2)), "f_awake": (2200, 700 + 150 * 10),
    # 공격·기동: B = A x 0.6
    "charge": (4.0, 2.4), "f_rush": (4.5, 2.7), "whirl": (2.9, 1.7), "f_execute": (7.0, 4.2), "f_stance": (1.6, 1.0), "g_bash": (5.5, 3.3),
    # 회복·제어·버프: A 1명(1체) = B 3명(3체) 합
    "cry": (2.4, 0.8 * 3), "f_flurry": (35, 12 * 3), "g_guard": (2.0 * 100, 20 * 3 + 50 * 3), "g_oath": (45 + 25, (15 + 10) * 3),
    "g_taunt": (8, 2.7 * 3), "g_counter": (400, 25 * 5 * 3), "g_awake": (40 * 12, 20 * 8 * 3),
    # 도적 계열(도적 기본·어쌔신·닌자, 2026-10-23). 집중·확산: A 1체 / B 3체 합
    "stab": (3.6, 1.2 * 3), "a_phantom": (3 * 3.5, 1 * 3.5 * 3),  # 2026-10-23 환영 삼격(쌍인참 대체): A 한 줄 3번, B 적마다 1번
    "a_rend": (1.1 * 2, 0.37 * 2 * 3),
    "n_volley": (0.64 * 3, 0.64 * 3), "n_recall": (6 * 1.0, 6 * 0.33 * 3),                  # 회수: 쿠나이 6개 기준
    # 폭발·지속: 보스 1체 60초 총량(쿨: 연쇄 도약 3 / 0.5(2026-10-23 연사기), 급소 파열 14 / 7, 풍마 13 / 6.5, 각성 60)
    "leap": (20 * 6.0, 120 * (0.7 + 0.15 * 2)), "dance": (2.5 * 3 * 8.8, 5 * 12 * 1.1),
    "a_rupture": (60 / 14 * 6.94, 60 / 7 * 3.47),
    # 2026-10-23 변신술(천영참 대체): 한 번 시전 합은 theory_combat.py 각성기 표(A 2초 +70% / B 12초 +9%·속도 +15%·폭발 절반)
    "a_awake": (19.29, 19.27),
    "n_fuma": (60 / 13 * 3 * 3.08, 60 / 6.5 * 12 * 0.385), "n_awake": (12 * 16.0 / 12, 36 * 16.0 / 36),  # 2026-10-23 표창 난사(분신 난무 대체): A 12개 / B 36개, 표창마다 24/개수 배
    # 공격·기동: B = A x 0.6
    "raid": (4.6, 2.76), "n_veil": (4.0, 2.4), "a_assassinate": (3.6, 2.16), "a_kunai": (3.2, 1.92),  # 2026-10-23 습격 x1.15, 쿠나이↔연막 자리 교환
    # 회복·제어·버프: A 1명(1체) = B 3명(3체) 합
    "backstab": (1.6, 0.55 * 3), "scarecrow": (6.0, 2.0 * 3), "a_venom": (36, 12 * 3), "a_stealth": (90, 30 * 3), "n_clone": (60 * 2, 60 * 3 * 2 / 3),  # 2026-10-23 분신 60% 고정: A 1명 x 지속 2배, B 3명 x 지속 2/3
    "n_bomb": (3.0, 1.0 * 3),
}
# 특화 id -> 보스 방 3분 평균 초당 피해(측정 또는 계산). 있으면 아래 추정 대신 쓴다
BOSS_DPS = {}

# 보스 방 비율 추정(위 설명). 직업 배율 뒤 화력: PLAN_CLASS_ROGUE.md 3.2절(닌자는 원거리 집중 상시 0.92)
PEAK_RATIO = 0.37
CLASS_REL = {"warrior": 1.0, "mage": 0.83, "rogue": 0.99,
             "f": 1.0, "g": 0.77, "m": 0.83, "b": 0.70, "a": 0.96, "n": 0.92}  # 2026-10-23 환영 삼격·분신 버프 뒤: 어쌔신 1.01 x 0.95, 닌자 0.71 x 1.15 x 1.12
BASE_CLASS = {"crush": "warrior", "whirl": "warrior", "cry": "warrior", "charge": "warrior", "blades": "warrior",
              "lance": "mage", "frostorb": "mage", "nova": "mage", "firefield": "mage", "meteor": "mage",
              "stab": "rogue", "leap": "rogue", "backstab": "rogue", "raid": "rogue", "dance": "rogue", "scarecrow": "rogue"}
SPEC_GAIN = 1.5


def boss_ratio(spec):
    skill = spec["skill"]
    line = BASE_CLASS.get(skill) or skill.split("_")[0]
    gain = SPEC_GAIN if spec["side"] == "a" and spec["axis"] in ("focus", "burst", "attack") else 1.0
    return PEAK_RATIO * CLASS_REL[line] * gain


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
    count = sum(len(v) for v in pairs.values())
    if DPS_CAP_T10 and BOSS_DPS:
        cap = DPS_CAP_T10 * ATTEMPT_SECONDS * DAMAGE_SLACK
        print(f"\n보스 방 상한(티어 10): {cap:,.0f}")
        for sid, dps in sorted(BOSS_DPS.items()):
            r = dps * ATTEMPT_SECONDS / cap
            print(f"  {sid:<16}{r:6.2f}{'  상한 근접' if r >= HIGH_DAMAGE_RATIO else ''}")
    worst = {}
    near = 0
    for skill, sides in pairs.items():
        for spec in sides.values():
            r = boss_ratio(spec)
            line = BASE_CLASS.get(skill) or skill.split("_")[0]
            if r > worst.get(line, (0, ""))[0]:
                worst[line] = (r, f"{skill}_{spec['side']}")
            if r >= HIGH_DAMAGE_RATIO:
                near += 1
                print(f"  상한 근접: {skill}_{spec['side']} {r:.2f}")
    print(f"\n보스 방 상한 비율 추정(특화 {count}개, {HIGH_DAMAGE_RATIO} 이상 {near}개). 계열별 최대:")
    print("  " + ", ".join(f"{k} {v[0]:.2f}({v[1]})" for k, v in worst.items()))
    bad += near
    if strict and bad:
        sys.exit(1)


if __name__ == "__main__":
    main()
