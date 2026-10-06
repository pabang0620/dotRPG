"""
(2026-10-06부터 성장 기준은 퀘스트·던전·레이드를 포함한 theory_progress.py다. 이 파일은 사냥만 본 옛 계산이다.)
dotRPG 성장 속도 이론 계산 (게임을 돌리지 않는 수치 점검).

목표: 중간 레이드(Lv22)까지 플레이 약 20시간 (STORY.md S7, MASTER_CHECKLIST J10).
출처: Progression.BaseXpToNext / CurveScale, HuntingGrounds.XpAt / PackSize, 사냥터 권장 레벨 표(HUNTING_BALANCE.md).

가정 (실측이 아니다)
- 필드 처치 분당 15마리 (3마리 무리라 기존 기준 12보다 조금 높게)
- 플레이 시간의 75%를 사냥, 나머지는 마을·이동·대화
- 하루 3회 요일던전이 필드 대비 경험치를 약 25% 보탠다
사용: python3 Tools/balance/theory_growth.py
"""

CURVE_SCALE = 13.4
PACK = 3
KILLS_PER_MIN, FIELD_SHARE, DUNGEON_BONUS = 15, 0.75, 1.25
ZONES = [(1, 4, 1), (5, 8, 6), (9, 12, 10), (13, 16, 14), (17, 20, 18), (21, 24, 22), (25, 28, 26), (29, 33, 31), (34, 40, 36)]


def base_need(level):
    return 40 + 30 * (level - 1) + 5 * (level - 1) ** 2


def xp_at(level):
    level = max(1, min(40, level))
    return 20 + (base_need(level) - base_need(1) + 40 - 1) // 40


def kill_xp(level):
    for lo, hi, monster_level in ZONES:
        if lo <= level <= hi:
            return max(1, (xp_at(monster_level) + PACK // 2) // PACK)
    return 1


def hours_to(target, scale=CURVE_SCALE):
    minutes = 0.0
    for level in range(1, target):
        rate = KILLS_PER_MIN * kill_xp(level) * FIELD_SHARE * DUNGEON_BONUS
        minutes += base_need(level) * scale / rate
    return minutes / 60


if __name__ == "__main__":
    print(f"곡선 배율 {CURVE_SCALE} (배율 1이면 Lv22까지 {hours_to(22, 1):.1f}시간)")
    for lv in (6, 12, 18, 22, 28, 31, 40):
        print(f"Lv{lv}: 약 {hours_to(lv):.1f}시간")
