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

사용: python3 Tools/balance/theory_progress.py
"""
import json
import math
import os

DATA = os.path.join(os.path.dirname(__file__), "..", "..", "server", "data")


def load(name):
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


PROG = load("progression.json")["xpToNext"]
MON = {m["id"]: m for m in load("monsters.json")["monsters"]}
MON["skeleton"] = load("monsters.json")["fieldSkeleton"]
ZONES = []
for m in load("maps.json")["maps"]:
    for f in m["fieldSpawns"]:
        ZONES.append((m["id"], f["monsterId"], f["points"], f["level"], f["xp"]))
ZONE_IDS = sorted({z[0] for z in ZONES}, key=lambda i: min(z[3] for z in ZONES if z[0] == i))
DNG = load("dungeons.json")
QUESTS = load("quest_index.json")["quests"]
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
FIXED_KPM = None


def zone_xp_per_min(zone, level, eff):
    rows = [z for z in ZONES if z[0] == zone]
    points = sum(z[2] for z in rows)
    hp = sum(MON[z[1]]["hp"] * z[2] for z in rows) / points
    mlevel = rows[0][3]
    xp = sum(z[4] * z[2] for z in rows) / points
    ehp = hp * (1 + 0.12 * (mlevel - 1))
    dps = player_attack(level) * pack_rate(level) * eff
    seconds = 3 * ehp / dps + TRAVEL
    kills_per_min = FIXED_KPM or 3 * 60 / seconds
    return kills_per_min * xp, kills_per_min, mlevel


def kill_cost(mid, count, level, eff):
    """퀘스트 처치 목표: 그 몬스터가 나오는 사냥터 중 가장 높은 곳(레벨+2 이하)에서 잡는 시간(분)과 그동안 얻는 처치 XP."""
    zones = [z for z in ZONES if z[1] == mid and z[3] <= level + 2] or [z for z in ZONES if z[1] == mid]
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
    return min(levels) if levels else 1


def dungeon_run(day, level, done_counts):
    """그날 할 수 있는 가장 좋은 요일 던전 1판: (XP, 초)."""
    best = None
    for d in DNG["dungeons"]:
        if d["isRaid"]:
            continue
        if day not in d["openDays"] and not (DNG["weekendOpensAll"] and day in ("Saturday", "Sunday")):
            continue
        for tier, diff in enumerate(DNG["difficulties"]):
            if level < diff["recommendedLevel"] - 2:
                continue
            base = max(d["clearXp"] * diff["rewardMul"], d["clearXpFloor"][tier])
            xp = base * d["xpMul"] * 1.2  # A등급 보너스 20%
            secs = d["referenceSeconds"][tier] + 60
            if best is None or xp / secs > best[0] / best[1]:
                best = (xp, secs)
    return best


def simulate(eff=0.35, hours_per_day=2.0, friend=True, quests=True):
    level, xp, total_min, day = 1, 0, 0.0, 0
    claimed, dungeon_clears, raid_clears, keys = set(), 0, {}, 0
    sk_claim_day, grah_claim_week = -1, -1
    events, reached, done_raid = [], {}, {}
    stats = {"quest_xp": 0, "quest_kill_min": 0.0, "dungeon_xp": 0, "dungeon_min": 0.0, "raid_xp": 0, "hunt_xp": 0, "hunt_min": 0.0}

    def gain(amount):
        nonlocal level, xp
        xp += amount
        while level < 40 and xp >= PROG[level - 1]:
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
                keys += 35
            try_quests()
        if level >= 40 and "c2_north" in claimed and weekday == "Sunday" and done_raid.get("grah") != day // 7:
            if keys >= 60:
                done_raid["grah"] = day // 7
                total_min += 12
                raid_clears["raid_grah"] = raid_clears.get("raid_grah", 0) + 1
                keys -= 60
                gain(27900 * 1.2)
                try_quests()

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
                if level < q["minLevel"] or level < o["levelNeed"]:
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
                claimed.add(q["id"])
                total_min += 5 if q["kind"] == "main" else 3
                for mid, count in o["killNeeds"].items():
                    minutes, xp_gain = kill_cost(mid, count, level, eff)
                    total_min += minutes
                    stats["quest_kill_min"] += minutes
                    gain(xp_gain)
                stats["quest_xp"] += q["reward"]["xp"]
                gain(q["reward"]["xp"])
                if q["kind"] == "main":
                    events.append((total_min / 60, day, level, q["id"], q["reward"]["xp"]))
                progress = True

    while day < 400 and not ("c2_end" in claimed and level >= 40):
        weekday = DAYS[day % 7]
        budget = hours_per_day * 60
        start = total_min
        try_quests()
        raids()
        # 요일 던전 3회
        for _ in range(DNG["dailyEntries"]):
            run = dungeon_run(weekday, level, dungeon_clears)
            if run is None:
                break
            total_min += run[1] / 60
            dungeon_clears += 1
            if level < 40:
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
            if level < 40:
                stats["hunt_xp"] += zone[1] * step * 0.8
                stats["hunt_min"] += step
            try_quests()
            raids()
            if level >= 40 and "c2_end" in claimed:
                break
        day += 1
    return reached, events, claimed, day, stats


def report(eff, hours, friend, quests=True):
    reached, events, claimed, days, st = simulate(eff, hours, friend, quests)
    tag = f"EFF {eff}, 하루 {hours}시간, {'친구와 레이드' if friend else '혼자'}"
    print(f"\n## {tag}")
    print("| 레벨 | 플레이 시간 |")
    print("|---|---|")
    for lv in (5, 10, 15, 20, 22, 25, 30, 35, 40):
        if lv in reached:
            h = reached[lv]
            print(f"| {lv} | {h:.1f}시간 |")
        else:
            print(f"| {lv} | 도달 못 함 | |")
    print("\n주요 메인 퀘스트 완료:")
    for h, d, lv, qid, x in events:
        if qid in ("c1_stronger", "c1_fortress", "c1_road", "c2_trial", "c2_growth", "c2_golem", "c2_north", "c2_grah", "c2_end"):
            print(f"- {qid}: {h:.1f}시간 ({DAYS[d % 7]}), Lv{lv}")
    print(f"XP 출처(Lv40까지 대략): 퀘스트 {st['quest_xp']:,.0f} / 사냥 {st['hunt_xp']:,.0f} ({st['hunt_min'] / 60:.1f}시간) / 퀘스트 처치 {st['quest_kill_min'] / 60:.1f}시간 / 던전 {st['dungeon_xp']:,.0f} ({st['dungeon_min'] / 60:.1f}시간) / 레이드 {st['raid_xp']:,.0f}")
    missing = [q["id"] for q in QUESTS if q["id"] not in claimed]
    if missing:
        print("미완료:", ", ".join(missing))


if __name__ == "__main__":
    total = sum(PROG[:39])
    quest_xp = sum(q["reward"]["xp"] for q in QUESTS)
    print(f"Lv1->40 필요 XP {total:,}, 퀘스트 XP 합 {quest_xp:,} ({quest_xp / total:.0%})")
    print("\n## 레벨별 최적 사냥터 (EFF 0.35, 이동 4초)")
    print("| 레벨 | 사냥터(몬스터Lv) | 공격력 | 분당 처치 | XP/시간 | 다음 레벨까지 사냥만 |")
    print("|---|---|---|---|---|---|")
    for lv in (1, 5, 10, 15, 20, 25, 30, 35, 39):
        z = best_zone(lv, 0.35)
        print(f"| {lv} | {z[0]}({z[3]}) | {player_attack(lv):.0f} | {z[2]:.1f} | {z[1] * 60:,.0f} | {PROG[lv - 1] / (z[1] * 0.8):.0f}분 |")
    import sys
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
            while lv < 40 and xp >= PROG[lv - 1]:
                xp -= PROG[lv - 1]; lv += 1
            changed = True
    print("\n## 연쇄 해금 (c2_trial 직후, 처치 목표 무시)")
    print(", ".join(f"{i}(Lv{m}+, {x:,})" for i, m, x in chain))
    print(f"-> Lv23에서 Lv{lv}까지 오른다")
