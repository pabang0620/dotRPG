"""
dotRPG 진행 시뮬레이션 (게임을 돌리지 않는 이론 계산).

서버 데이터(server/data/*.json)를 그대로 읽어 하루 단위로 플레이를 흉내 낸다.
- 레벨업 요구량: progression.json xpToNext
- 필드 사냥: maps.json fieldSpawns(지점 수, 몬스터 레벨, 처치 XP = 서버 xpOverride), monsters.json 체력, 체력 성장 0.12/레벨
- 요일 던전: dungeons.json (하루 3회, 평일은 그날 던전 하나, 주말은 전부 열림), 클리어 XP = max(clearXp x rewardMul, 하한) x xpMul x 등급 보너스
- 레이드: 해골왕 수·토·일(보상 하루 1회, 2인 이상), 그라흐 일요일(보상 주 1회, 열쇠 60개, 2인 이상)
  퀘스트 판정은 연습 클리어도 인정한다. 열쇠는 2인 이상 해골왕 보상에서만 나온다.
- 퀘스트: quest_index.json (선행 퀘스트, 최소 레벨, 레벨 조건, 처치 대상이 나오는 사냥터, 던전·레이드 조건)
- 플레이어 화력: 공격 10 + 장비(그 레벨에 낄 수 있는 가장 높은 단계의 레어, 무기 +5) x (1 + 패시브 절반 x 5.5%)

가정 (실측이 아니다, 아래 상수로 조절)
- EFF: 이론 DPS 대비 실제로 넣는 비율(이동·회피·자원 포함). 기본 0.35, 느린 플레이 0.2, 숙련 0.5
- 사냥 시간의 80%가 실제 사냥(나머지 마을·정비)
- 무리 사이 이동 3초, 퀘스트 처리 메인 5분·서브 3분, 던전 1판 = 기준 시간 + 1분

- 미니 던전(--mini): 하루 30분, 메인 1-11(c1s_depths) 완료 후 Lv10~39. 처치당 입장 레벨 막대 / 8000, 다음 메인 퀘스트가
  레벨 때문에 막혔으면 / 800(따라잡기 x10). 막힘은 처치마다 다시 판정한다(서버 miniDungeonRules.ts와 같은 규칙)

- 챕터 3(--to 50, 2026-10-23 7단계): Lv40에서 c2_end를 끝내기 전에는 40에 멈춘다(CHAPTER3_OPEN 전 서버 levelCap 40, 넘친 XP는 버린다).
  3장 시작 = Lv40 + c2_end. 미니 던전 티어 40은 하루 10분(miniDungeonRules DAILY_LIMIT_MS_TIER40). 지바크(c3_xibak 수락 = c3_record 완료 + Lv45)
  수·토·일 보상 하루 1회, 카마츠(c3_kamatz 수락 = c3_trace 완료 + Lv50) 일요일 열쇠 60. 요일 던전 군주(권장 45)는 floor 칸이 있는 던전만.
  Lv41+ 실측 속도: 지상(dry_*) = 그 직업 Lv36~40 실측 중앙값, 지하(hollow_*) = Lv40 캐릭터의 지하 실측 중앙값(장비 티어 8 기준이라 보수적).
  고유 몬스터(사냥터에 없는 정찰대장 등)는 1마리 2분.
- --career <id>: 실측 속도를 그 직업 행만으로 낸다(fighter·guardian·arcanist·bishop). assassin·ninja는 실측이 없어 theory_combat.py 무리 3마리
  처치 시간 비로 실측 4직업에서 환산한다(PLAN_CLASS_ROGUE 3.1).

사용: python3 Tools/balance/theory_progress.py [--measured] [--mini [분당 처치, 기본 20]] [--pace Lv16+ 배율] [--max-level 40 | --to 50] [--career fighter]
"""
import json
import math
import os
import sys

DATA = os.path.join(os.path.dirname(__file__), "..", "..", "server", "data")


def load(name):
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


PROG = load("progression.json")["xpToNext"]
# 만렙(챕터 3: --max-level 50). 기본 40. progression.json 이 그만큼 칸을 가져야 한다(xpToNext 길이 >= MAX_LEVEL-1)
_LV_ARG = "--to" if "--to" in sys.argv else "--max-level"
MAX_LEVEL = int(sys.argv[sys.argv.index(_LV_ARG) + 1]) if _LV_ARG in sys.argv else 40
CH2_CAP, CH2_END = 40, "c2_end"  # 3장 문이 열리기 전 상한과 그 문을 여는 퀘스트
END_QUEST = "c3_end" if MAX_LEVEL > CH2_CAP else CH2_END
CAREER = sys.argv[sys.argv.index("--career") + 1] if "--career" in sys.argv else None
# theory_combat.py "전직 6종 사냥 평균" 무리 3마리 평균(초). 도적 두 전직의 실측 대용 환산에만 쓴다
PACK_SECONDS = {"fighter": 4.09, "guardian": 7.24, "arcanist": 4.96, "bishop": 5.87, "assassin": 5.88, "ninja": 5.54}
assert len(PROG) >= MAX_LEVEL - 1, f"progression.json xpToNext {len(PROG)}칸, --max-level {MAX_LEVEL}에 모자람"
MON = {m["id"]: m for m in load("monsters.json")["monsters"]}
MON["skeleton"] = load("monsters.json")["fieldSkeleton"]
ZONES = []
for m in load("maps.json")["maps"]:
    for f in m["fieldSpawns"]:
        ZONES.append((m["id"], f["monsterId"], f["points"], f["level"], f["xp"]))
ZONE_IDS = sorted({z[0] for z in ZONES}, key=lambda i: min(z[3] for z in ZONES if z[0] == i))
DNG = load("dungeons.json")
QUESTS = load("quest_index.json")["quests"]
DAILY = load("daily_quests.json")  # 일일 의뢰(서버 전용 데이터, Docs/server/phase14_daily_quests.md)
ENH = {s["id"]: s["levels"] for s in load("enhance.json")["steps"]}
TIERS = [1, 10, 15, 20, 25, 30, 35, 40]
DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]


def gear_attack(item, plus):
    levels = ENH.get(item)
    return levels[min(plus, len(levels) - 1)]["statsAttack"] if levels else 0


def player_attack(level):
    tier = max(t for t in TIERS if t <= level)
    plus = 3 if tier == 1 else 5
    atk = 10 + gear_attack(f"eq_sword_{tier}_r", plus) + gear_attack(f"eq_neck_{tier}_r", 0) + gear_attack(f"eq_ring_{tier}_r", 0)
    return atk * (1 + (level - 1) / 2 * 0.055)


def pack_rate(level):
    """공격력 배수/초, 3마리 무리 기준(범위 공격 포함). 기본 공격 0.4초, 열린 스킬 칸당 +1.0, 전직 후 +20%."""
    slots = sum(1 for s in (2, 6, 12, 18) if level >= s)
    rate = 2.5 * 1.5 + slots * 1.0
    return rate * (1.2 if level >= 15 else 1.0)


TRAVEL = 4.0
# [BALANCE 2026-10-11] Field monsters: HP x FIELD_HP_BASE x (1 + FIELD_HP_SLOPE (L-1)) / (1 + 0.12 (L-1)) on top of the
# normal level growth (MonsterDatabase.FieldHpMul). Docs/BALANCE_COMBAT_2026-10-11.md.
FIELD_HP_BASE, FIELD_HP_SLOPE = 3.0, 0.5


def field_hp_mul(level):
    return FIELD_HP_BASE * (1 + FIELD_HP_SLOPE * (level - 1)) / (1 + 0.12 * (level - 1))
# 조정 시험용 덮어쓰기 (데이터에 반영한 뒤에는 비워 둔다)
QUEST_SCALE = 1.0
MINI_KPM = 0  # 미니 던전 분당 처치(0 = 안 함). 서버 상한은 2초에 1마리 = 분당 30
MINI_MINUTES, MINI_BASE, MINI_STUCK, MINI_UNLOCK = 30, 8000, 800, "c1s_depths"


def _mini_tier40_minutes():
    """3장 티어 40(Lv40~49) 하루 상한(분): 서버 miniDungeonRules.ts DAILY_LIMIT_MS_TIER40을 읽는다. 그날 쓴 시간은 티어 공통."""
    import re
    try:
        with open(os.path.join(DATA, "..", "src", "domains", "minidungeon", "miniDungeonRules.ts"), encoding="utf-8") as f:
            return int(re.search(r"DAILY_LIMIT_MS_TIER40\s*=\s*([\d_]+)", f.read()).group(1).replace("_", "")) / 60000
    except (OSError, AttributeError):
        return 10


MINI_MINUTES_TIER40 = _mini_tier40_minutes()
MIN_LEVEL = {}
XP_MUL = {}
FIXED_KPM = None
# 실측 속도(2026-10-16, Builds/WindowsQA -dotrpgMeasure, 자동 사냥 드라이버): 레벨 5구간별 분당 처치 중앙값. Lv1~15는 설계값 15 유지.
# `python3 theory_progress.py --measured` 또는 MEASURED_KPM = measured_kpm() 로 켠다. 파일이 없으면 None.
MEASURED_FILE = os.path.join(os.path.dirname(__file__), "..", "..", "..", "analysis", "player-sim", "data", "dotrpg-measured.json")
MEASURED_KPM = None


def _career_rows(rows, career):
    """그 직업의 실측 행. 도적 전직(실측 없음)은 실측 4직업 행의 속도를 무리 처치 시간 비로 환산해 만든다."""
    if career is None:
        return rows
    own = [r for r in rows if r.get("career") == career]
    if career not in PACK_SECONDS or any(r.get("mlv", 0) <= CH2_CAP for r in own):
        return own
    # 3장 실측만 있는 직업(도적 전직, 2026-10-24): Lv40 이하는 4직업 환산, Lv41+는 자기 실측
    conv = [dict(r, kills_per_min=r["kills_per_min"] * PACK_SECONDS[r["career"]] / PACK_SECONDS[career])
            for r in rows if r.get("career") in PACK_SECONDS and r.get("career") != career and (not own or r.get("mlv", 0) <= CH2_CAP)]
    return conv + own


MEASURED_UNDER = None  # Lv41+ 지하(hollow_*) 분당 처치(실측 중앙값). measured_kpm()이 채운다


def measured_kpm(path=None):
    """{레벨: 분당 처치}: 실측 사냥 행(driver auto, 몬스터 Lv40 이하)을 5레벨 구간 중앙값으로 묶고, Lv1~15는 15로 둔다.
    Lv41+(챕터 3)는 Lv36~40 구간 값을 지상에 쓰고, 지하는 몬스터 Lv40 초과 행의 중앙값을 MEASURED_UNDER에 둔다."""
    global MEASURED_UNDER
    path = path or (sys.argv[sys.argv.index("--measured-file") + 1] if "--measured-file" in sys.argv else MEASURED_FILE)
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        rows = _career_rows([r for r in json.load(f).get("hunting", []) if r.get("valid", True)], CAREER)
    bands, under = {}, []
    for r in rows:
        if r.get("kills_per_min", 0) <= 0 or r.get("driver") != "auto":
            continue
        if r.get("mlv", 0) > 40:
            # 지하(hollow_*)는 MEASURED_UNDER, 3장 지상(dry_*, 2026-10-24 실측)은 Lv41~50 구간(8)
            if str(r.get("zone", "")).startswith("hollow_"):
                under.append(r["kills_per_min"])
            else:
                bands.setdefault(8, []).append(r["kills_per_min"])
            continue
        bands.setdefault(min(8, (r["level"] - 1) // 5), []).append(r["kills_per_min"])
    med = {k: sorted(v)[len(v) // 2] for k, v in bands.items()}
    MEASURED_UNDER = sorted(under)[len(under) // 2] if under else None
    return {lv: 15.0 if lv <= 15 else med.get(min(8, (lv - 1) // 5), med.get(7, 15.0)) for lv in range(1, MAX_LEVEL + 1)}


def zone_xp_per_min(zone, level, eff):
    rows = [z for z in ZONES if z[0] == zone]
    points = sum(z[2] for z in rows)
    hp = sum(MON[z[1]]["hp"] * z[2] for z in rows) / points
    mlevel = rows[0][3]
    xp = sum(z[4] * z[2] for z in rows) / points  # maps.json fieldSpawns xp (FIELD_XP_MUL already applied there)
    ehp = hp * (1 + 0.12 * (mlevel - 1)) * field_hp_mul(mlevel)
    dps = player_attack(level) * pack_rate(level) * eff
    seconds = 3 * ehp / dps + TRAVEL
    kills_per_min = (MEASURED_KPM[level] if MEASURED_KPM else None) or FIXED_KPM or 3 * 60 / seconds
    # 3장 구간(Lv41+)만. Lv40 이하 결과(1~40 수지·시간)는 2026-10-16 기준 그대로 둔다
    if MEASURED_KPM and MEASURED_UNDER and level > CH2_CAP and mlevel > CH2_CAP and zone.startswith("hollow_"):
        kills_per_min = MEASURED_UNDER
    return kills_per_min * xp, kills_per_min, mlevel


def kill_cost(mid, count, level, eff):
    """퀘스트 처치 목표: 그 몬스터가 나오는 사냥터 중 가장 높은 곳(레벨+2 이하)에서 잡는 시간(분)과 그동안 얻는 처치 XP."""
    zones = [z for z in ZONES if z[1] == mid and z[3] <= level + 2] or [z for z in ZONES if z[1] == mid]
    if not zones:  # 사냥터에 없는 고유 몬스터(3-11 정찰대장 등): 찾아가 잡는 데 1마리 2분, 처치 XP는 무시
        return 2.0 * count, 0.0
    zid = max(zones, key=lambda z: z[3])[0]
    rows = [z for z in ZONES if z[0] == zid]
    share = sum(z[2] for z in rows if z[1] == mid) / sum(z[2] for z in rows)
    rate, kpm, _ = zone_xp_per_min(zid, level, eff)
    minutes = count / (kpm * share) / 0.8
    return minutes, rate * minutes * 0.8


def best_zone(level, eff):
    best = None
    for zid in ZONE_IDS:
        rate, kpm, ml = zone_xp_per_min(zid, level, eff)
        if ml > level + 2:
            continue
        if best is None or rate > best[1]:
            best = (zid, rate, kpm, ml)
    return best


def monster_zone_level(mid):
    levels = [z[3] for z in ZONES if z[1] == mid]
    return min(levels) if levels else MON.get(mid, {}).get("level", 1)


def dungeon_run(day, level, done_counts):
    """그날 할 수 있는 가장 좋은 요일 던전 1판: (XP, 초)."""
    best = None
    for d in DNG["dungeons"]:
        if d["isRaid"]:
            continue
        if day not in d["openDays"] and not (DNG["weekendOpensAll"] and day in ("Saturday", "Sunday")):
            continue
        for tier, diff in enumerate(DNG["difficulties"]):
            if tier >= len(d["clearXpFloor"]) or level < diff["recommendedLevel"] - 2:  # 군주(3장)는 요일 던전만 칸이 있다
                continue
            base = max(d["clearXp"] * diff["rewardMul"], d["clearXpFloor"][tier])
            xp = base * XP_MUL.get(d["id"], d["xpMul"]) * 1.2  # A등급 보너스 20%
            secs = d["referenceSeconds"][tier] + 60
            if best is None or xp / secs > best[0] / best[1]:
                best = (xp, secs, d["id"], tier)
    return best


def rebuild_prog(pace_late):
    """Progression.XpToNext를 Lv16+ 배율만 바꿔 다시 계산한다(Unity 내보내기 전에 배율을 시험할 때)."""
    def scale(lv):
        early = 4 if lv <= 10 else 36 if lv >= 26 else 4 + 32 * (lv - 10) / 16
        trim = 1 if lv <= 15 else 0.8 if lv >= 18 else 1 - 0.2 * (lv - 15) / 3
        pace = 1 if lv <= 15 else pace_late if lv >= 20 else 1 + (pace_late - 1) * (lv - 15) / 5
        return early * trim * pace
    return [round((40 + (lv - 1) * 30 + (lv - 1) ** 2 * 5) * scale(lv)) for lv in range(1, len(PROG) + 1)]


def main_level_gate(level, claimed):
    """다음 메인 퀘스트가 레벨 때문에 막혔으면 그 레벨, 아니면 0 (서버 mainQuestLevelGate와 같은 규칙)."""
    gate = 0
    for q in QUESTS:
        if q["kind"] != "main" or q["id"] in claimed or any(r not in claimed for r in q["requires"]):
            continue
        need = max(MIN_LEVEL.get(q["id"], q["minLevel"]), q["objectives"]["levelNeed"])
        if need <= level:
            return 0
        gate = need if gate == 0 else min(gate, need)
    return gate


def simulate(eff=0.35, hours_per_day=2.0, friend=True, quests=True):
    level, xp, total_min, day = 1, 0, 0.0, 0
    claimed, dungeon_clears, raid_clears, keys = set(), 0, {}, 0
    sk_claim_day, grah_claim_week = -1, -1
    events, reached, done_raid = [], {}, {}
    stats = {"quest_xp": 0, "quest_kill_min": 0.0, "dungeon_xp": 0, "dungeon_min": 0.0, "raid_xp": 0, "hunt_xp": 0, "hunt_min": 0.0, "daily_xp": 0,
             # 골드 계산용(theory_gold.py): 몬스터 레벨별 처치 수, 던전 판(id, 난이도), 보상 받은 레이드(id), 일일·주간 의뢰 청구(레벨)
             "kills": {}, "kills_t": [], "dungeon_runs": [], "raid_rewards": [], "daily_claims": [], "weekly_claims": [],
             "mini_xp": 0, "mini_days": 0, "mini_stuck_days": 0, "mini_stuck_kills": 0, "mini_kills": 0,
             # 챕터 3 골드 구간 나누기용: (시각, 레이드 id), (시각, 처치 분)
             "raid_t": [], "kill_min_t": []}

    def gain(amount):
        nonlocal level, xp
        xp += amount
        cap = MAX_LEVEL if MAX_LEVEL <= CH2_CAP or CH2_END in claimed else CH2_CAP
        if level >= cap:
            xp = 0  # 서버 levelCap에서 멈춘다(넘친 XP는 쌓이지 않는다)
            return
        while level < cap and xp >= PROG[level - 1]:
            xp -= PROG[level - 1]
            level += 1
            reached[level] = total_min / 60

    def raids():
        nonlocal total_min, keys, sk_claim_day, grah_claim_week
        weekday = DAYS[day % 7]
        if level >= 20 and "c1_stronger" in claimed and weekday in ("Wednesday", "Saturday", "Sunday") and done_raid.get("sk") != day:
            done_raid["sk"] = day
            total_min += 10
            raid_clears["raid_skeleton_king"] = raid_clears.get("raid_skeleton_king", 0) + 1
            if friend:
                gain(6728 * 1.2)
                stats["raid_xp"] += 6728 * 1.2
                stats["raid_rewards"].append("raid_skeleton_king")
                stats["raid_t"].append((total_min / 60, "raid_skeleton_king"))
                keys += 35
            try_quests()
        if level >= 40 and "c2_north" in claimed and weekday == "Sunday" and done_raid.get("grah") != day // 7:
            # 열쇠가 모자라면 연습 입장: 클리어는 퀘스트에 인정되고 보상은 없다
            done_raid["grah"] = day // 7
            total_min += 12
            raid_clears["raid_grah"] = raid_clears.get("raid_grah", 0) + 1
            if friend and keys >= 60:
                keys -= 60
                gain(27900 * 1.2)
                stats["raid_rewards"].append("raid_grah")
                stats["raid_t"].append((total_min / 60, "raid_grah"))
            try_quests()
        if MAX_LEVEL <= CH2_CAP:
            return
        # 챕터 3: 지바크(중간, 수·토·일, 보상 하루 1회, 열쇠 조각 20~50), 카마츠(최종, 일요일, 열쇠 60)
        if level >= 45 and "c3_record" in claimed and weekday in ("Wednesday", "Saturday", "Sunday") and done_raid.get("xibak") != day:
            done_raid["xibak"] = day
            total_min += 11
            raid_clears["raid_xibak"] = raid_clears.get("raid_xibak", 0) + 1
            if friend:
                gain(30892 * 1.2)
                stats["raid_xp"] += 30892 * 1.2
                stats["raid_rewards"].append("raid_xibak")
                stats["raid_t"].append((total_min / 60, "raid_xibak"))
                keys += 35
            try_quests()
        if level >= 50 and "c3_trace" in claimed and weekday == "Sunday" and done_raid.get("kamatz") != day // 7:
            done_raid["kamatz"] = day // 7
            total_min += 12
            raid_clears["raid_kamatz"] = raid_clears.get("raid_kamatz", 0) + 1
            if friend and keys >= 60:
                keys -= 60
                gain(42956 * 1.2)
                stats["raid_rewards"].append("raid_kamatz")
                stats["raid_t"].append((total_min / 60, "raid_kamatz"))
            try_quests()

    # 퀘스트 처치 목표의 남은 수. 처치는 하루 예산 안에서만 하고 모자라면 다음 날 이어 잡는다
    # (2026-10-21: 전에는 하루에 다 잡아 날짜가 실제의 절반으로 잡혔고, 그만큼 미니 던전·의뢰 같은 하루 단위 수입이 적게 계산됐다)
    kill_left = {}
    day_start = 0.0
    day_budget = hours_per_day * 60

    def quest_kills(q):
        """오늘 남은 예산 안에서 이 퀘스트의 처치 목표를 잡는다. 다 잡았으면 True."""
        nonlocal total_min
        for mid, count in q["objectives"]["killNeeds"].items():
            left = kill_left.get((q["id"], mid), count)
            if left <= 0:
                continue
            remaining = day_budget - (total_min - day_start)
            if remaining <= 0.01:
                return False
            minutes, xp_gain = kill_cost(mid, left, level, eff)
            n = left
            if minutes > remaining:
                n = int(left * remaining / minutes)
                if n <= 0:
                    return False
                minutes, xp_gain = kill_cost(mid, n, level, eff)
            total_min += minutes
            stats["quest_kill_min"] += minutes
            stats["kill_min_t"].append((total_min / 60, minutes))
            ml = monster_zone_level(mid)
            stats["kills"][ml] = stats["kills"].get(ml, 0) + n
            stats["kills_t"].append((total_min / 60, ml, n))
            gain(xp_gain)
            kill_left[(q["id"], mid)] = left - n
            if left - n > 0:
                return False
        return True

    def try_quests():
        nonlocal total_min
        progress = True
        while progress:
            progress = False
            for q in QUESTS:
                if not quests and q["kind"] == "sub":
                    continue
                if q["id"] in claimed:
                    continue
                o = q["objectives"]
                if any(r not in claimed for r in q["requires"]):
                    continue
                if level < MIN_LEVEL.get(q["id"], q["minLevel"]) or level < o["levelNeed"]:
                    continue
                if any(monster_zone_level(m) > level + 2 for m in o["killNeeds"]):
                    continue
                need_dng = sum(n["count"] for n in o["dungeonNeeds"])
                if need_dng and dungeon_clears < need_dng:
                    continue
                if any(raid_clears.get(r["target"], 0) < r["count"] for r in o["raidNeeds"]):
                    continue
                if "career_promoted" in o["flagNeeds"] and level < 15:
                    continue
                if not quest_kills(q):
                    continue
                claimed.add(q["id"])
                total_min += 5 if q["kind"] == "main" else 3
                stats["quest_xp"] += q["reward"]["xp"] * QUEST_SCALE
                gain(q["reward"]["xp"] * QUEST_SCALE)
                events.append((total_min / 60, day, level, q["id"], q["reward"]["xp"], q["kind"]))
                progress = True

    while day < 400 and not (END_QUEST in claimed and level >= MAX_LEVEL):
        weekday = DAYS[day % 7]
        budget = day_budget
        day_start = start = total_min
        try_quests()
        raids()
        # 일일·주간 의뢰: 처치 목표는 지금 사냥터 사냥과 겹치므로 수락·보고 시간만 더하고, 보상은 레벨 막대 x xpShare
        if DAILY["unlockQuest"] in claimed and level < MAX_LEVEL and level >= min(b["minLevel"] for b in DAILY["bands"]):
            for _ in range(DAILY["perDay"]):
                total_min += 2
                stats["daily_xp"] += DAILY["xpShare"] * PROG[level - 1]
                stats["daily_claims"].append(level)
                gain(DAILY["xpShare"] * PROG[level - 1])
            try_quests()
            # 주간 의뢰: 한 주 동안 나눠 하지만 계산은 주 첫날(목요일)에 한 번에 받는다고 본다
            if weekday == "Thursday" or "weekly_first" not in stats:
                stats["weekly_first"] = 1
                for _ in range(DAILY["weekly"]["perWeek"]):
                    total_min += 3
                    stats["daily_xp"] += DAILY["weekly"]["xpShare"] * PROG[level - 1]
                    stats["weekly_claims"].append(level)
                    gain(DAILY["weekly"]["xpShare"] * PROG[level - 1])
                try_quests()
        # 미니 던전 하루 30분 (입장 1분 포함). 처치마다 막힘을 다시 본다
        mini_open = level < CH2_CAP or (level < MAX_LEVEL and CH2_END in claimed)
        if MINI_KPM > 0 and MINI_UNLOCK in claimed and 10 <= level < MAX_LEVEL and mini_open:
            entry, stuck_today = level, False
            minutes = MINI_MINUTES_TIER40 if level >= CH2_CAP else MINI_MINUTES
            total_min += 1
            for _ in range(int(MINI_KPM * minutes)):
                if level >= MAX_LEVEL:
                    break
                stuck = main_level_gate(level, claimed) > 0
                amount = math.ceil(PROG[entry - 1] / (MINI_STUCK if stuck else MINI_BASE))
                stats["mini_xp"] += amount
                stats["mini_kills"] += 1
                if entry >= CH2_CAP:
                    stats["mini_kills_ch3"] = stats.get("mini_kills_ch3", 0) + 1
                stats["mini_stuck_kills"] += stuck
                stuck_today = stuck_today or stuck
                gain(amount)
            total_min += minutes
            stats["mini_days"] += 1
            stats["mini_stuck_days"] += stuck_today
            try_quests()
            raids()
        # 요일 던전 3회
        for _ in range(DNG["dailyEntries"]):
            run = dungeon_run(weekday, level, dungeon_clears)
            if run is None:
                break
            total_min += run[1] / 60
            dungeon_clears += 1
            stats["dungeon_runs"].append((run[2], run[3]))
            if level < MAX_LEVEL:
                stats["dungeon_xp"] += run[0]
                stats["dungeon_min"] += run[1] / 60
            gain(run[0])
            try_quests()
            raids()
        # 남은 시간 사냥 (10분 단위로 퀘스트 확인)
        while total_min - start < budget:
            zone = best_zone(level, eff)
            step = min(10, budget - (total_min - start))
            if step < 0.01:
                break
            total_min += step
            gain(zone[1] * step * 0.8)
            stats["kills"][zone[3]] = stats["kills"].get(zone[3], 0) + zone[2] * step * 0.8
            stats["kills_t"].append((total_min / 60, zone[3], zone[2] * step * 0.8))
            if level < MAX_LEVEL:
                stats["hunt_xp"] += zone[1] * step * 0.8
                stats["hunt_min"] += step
            stats["kill_min_t"].append((total_min / 60, step))
            try_quests()
            raids()
            if level >= MAX_LEVEL and END_QUEST in claimed:
                break
        day += 1
    return reached, events, claimed, day, stats


def ch3_start(reached, events):
    """3장 시작 시각: Lv40 도달과 c2_end 완료 중 늦은 쪽(그 전에는 서버 상한 40)."""
    t_end = next((h for h, _, _, qid, *_ in events if qid == CH2_END), 0.0)
    return max(reached.get(CH2_CAP, 0.0), t_end)


def report(eff, hours, friend, quests=True):
    reached, events, claimed, days, st = simulate(eff, hours, friend, quests)
    tag = f"EFF {eff}, 하루 {hours}시간, {'친구와 레이드' if friend else '혼자'}"
    print(f"\n## {tag}")
    print("| 레벨 | 플레이 시간 |")
    print("|---|---|")
    for lv in (5, 10, 15, 20, 22, 25, 30, 35, 40) + tuple(range(41, MAX_LEVEL + 1)):
        if lv in reached:
            h = reached[lv]
            print(f"| {lv} | {h:.1f}시간 |")
        else:
            print(f"| {lv} | 도달 못 함 | |")
    print("\n주요 메인 퀘스트 완료:")
    for h, d, lv, qid, x, kind in events:
        if qid in ("c1_stronger", "c1_fortress", "c1_road", "c2_trial", "c2_growth", "c2_golem", "c2_north", "c2_grah", "c2_end") or (kind == "main" and qid.startswith("c3")):
            print(f"- {qid}: {h:.1f}시간 ({DAYS[d % 7]}), Lv{lv}")
    if MAX_LEVEL > CH2_CAP and MAX_LEVEL in reached:
        t0 = ch3_start(reached, events)
        print(f"\n챕터 3: 시작(Lv{CH2_CAP} + {CH2_END}) {t0:.1f}시간 -> Lv{MAX_LEVEL} {reached[MAX_LEVEL]:.1f}시간 = {reached[MAX_LEVEL] - t0:.1f}시간 (목표 22~27), "
              f"미니 티어 40 처치 {st.get('mini_kills_ch3', 0):,}마리, 직업 {CAREER or '4직업 합산'}"
              + (f", 지하 실측 분당 {MEASURED_UNDER:.1f}" if MEASURED_UNDER else ""))
    if st["mini_days"]:
        print(f"미니 던전: {st['mini_days']}일, 따라잡기 받은 날 {st['mini_stuck_days']}일, 처치 {st['mini_kills']:,}마리 중 따라잡기 {st['mini_stuck_kills']:,}마리, 경험치 {st['mini_xp']:,.0f}")
    print(f"XP 출처(Lv40까지 대략): 퀘스트 {st['quest_xp']:,.0f} / 사냥 {st['hunt_xp']:,.0f} ({st['hunt_min'] / 60:.1f}시간) / 퀘스트 처치 {st['quest_kill_min'] / 60:.1f}시간 / 던전 {st['dungeon_xp']:,.0f} ({st['dungeon_min'] / 60:.1f}시간) / 레이드 {st['raid_xp']:,.0f} / 일일·주간 의뢰 {st['daily_xp']:,.0f}")
    gaps = []
    for (h0, _, lv0, q0, *_), (h1, _, lv1, q1, *_) in zip(events, events[1:]):
        if h1 - h0 >= 4:
            gaps.append(f"{h0:.0f}~{h1:.0f}시간 Lv{lv0}~{lv1} ({q0} 다음 {q1})")
    print("퀘스트 공백 4시간 이상:", "; ".join(gaps) if gaps else "없음")
    missing = [q["id"] for q in QUESTS if q["id"] not in claimed]
    if missing:
        print("미완료:", ", ".join(missing))


if __name__ == "__main__":
    import sys
    if "--pace" in sys.argv:
        PROG[:] = rebuild_prog(float(sys.argv[sys.argv.index("--pace") + 1]))
    if "--mini" in sys.argv:
        i = sys.argv.index("--mini")
        MINI_KPM = float(sys.argv[i + 1]) if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("--") else 20
    total = sum(PROG[:MAX_LEVEL - 1])
    quest_xp = sum(q["reward"]["xp"] for q in QUESTS)
    print(f"Lv1->{MAX_LEVEL} 필요 XP {total:,}, 퀘스트 XP 합 {quest_xp:,} ({quest_xp / total:.0%})")
    print("\n## 레벨별 최적 사냥터 (EFF 0.35, 이동 4초)")
    print("| 레벨 | 사냥터(몬스터Lv) | 공격력 | 분당 처치 | XP/시간 | 다음 레벨까지 사냥만 |")
    print("|---|---|---|---|---|---|")
    for lv in (1, 5, 10, 15, 20, 25, 30, 35, MAX_LEVEL - 1):
        z = best_zone(lv, 0.35)
        print(f"| {lv} | {z[0]}({z[3]}) | {player_attack(lv):.0f} | {z[2]:.1f} | {z[1] * 60:,.0f} | {PROG[lv - 1] / (z[1] * 0.8):.0f}분 |")
    if "--measured" in sys.argv:
        MEASURED_KPM = measured_kpm()
        print("\n# 실측 속도 (자동 사냥, 2026-10-16): " + ", ".join(f"Lv{lv} {MEASURED_KPM[lv]:.0f}" for lv in (16, 21, 26, 31, 36)))
        report(0.35, 2.0, True, True)
        sys.exit(0)
    scenarios = [("설계 기준: 분당 15마리 고정", 0.35, 4.0, 15, True),
                 ("코드 계산 보수: EFF 0.2, 이동 6초", 0.2, 6.0, None, True),
                 ("코드 계산 기본: EFF 0.35, 이동 4초", 0.35, 4.0, None, True),
                 ("혼자(레이드 보상 없음), 보수", 0.2, 6.0, None, False),
                 ("비교: 서브 퀘스트 없이(메인만), 기본", 0.35, 4.0, None, True)]
    for i, (name, eff, travel, kpm, friend) in enumerate(scenarios):
        TRAVEL, FIXED_KPM = travel, kpm
        print(f"\n# {name}")
        report(eff, 2.0, friend, i != 4)
    # 연쇄 해금: c2_trial을 Lv23에 완료한 순간 처치 시간 없이 연달아 받을 수 있는 퀘스트 XP
    lv, xp, got = 23, 0, set(q["id"] for q in QUESTS if q["id"] in ("c2_trial",))
    done = {"c1_morning","c1_festival","c1_burning","c1_ashes","c1_rise","c1_rebuild","c1_trail","c1_stronger","c1_fortress","c1_bargas","c1_road","c2_canyon","c2_trial"}
    chain = []
    changed = True
    while changed:
        changed = False
        for q in QUESTS:
            if q["id"] in done or q["kind"] != "sub" or any(r not in done for r in q["requires"]) or q["minLevel"] > lv:
                continue
            done.add(q["id"]); xp += q["reward"]["xp"]; chain.append((q["id"], q["minLevel"], q["reward"]["xp"]))
            while lv < MAX_LEVEL and xp >= PROG[lv - 1]:
                xp -= PROG[lv - 1]; lv += 1
            changed = True
    print("\n## 연쇄 해금 (c2_trial 직후, 처치 목표 무시)")
    print(", ".join(f"{i}(Lv{m}+, {x:,})" for i, m, x in chain))
    print(f"-> Lv23에서 Lv{lv}까지 오른다")
