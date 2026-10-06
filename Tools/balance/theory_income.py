"""
dotRPG 기대 수입 표 (경제 속도 정지의 상한 원본). Docs/server/phase9_anti_abuse.md 12.2절의 계산을 그대로 코드로 옮긴 것.

서버 데이터(server/data/*.json)를 읽어 "플레이 시간 1시간에 이론상 얻을 수 있는 최대치"와 일 단위·주 단위 덩어리(요일 던전, 레이드)를
권장 레벨 대역마다 계산하고 server/data/income_caps.json 으로 쓴다. 서버는 이 파일만 읽는다(문서의 표를 코드에 복사하지 않는다).
데이터(몬스터 경험치, 드롭률, 상점 판매가, 던전 보상)가 바뀌면 이 스크립트를 다시 돌려 JSON을 갱신한다.

가정 (이름 있는 값, 바꿀 수 있다)
- KILLS_PER_MIN 30: 3마리 무리를 6초(이동 4초 + 전투 2초, theory_progress.py 의 TRAVEL=4)마다 정리하는 숙련자. 설계 기준(분당 15마리)의 2배.
  한 시간 연속으로 유지할 수 있는 사람은 없는 값이라 "이론 최대"다. 서버 환경변수 INCOME_MAX_KILLS_PER_MIN 과 같은 값이어야 한다.
- 구간(대역)은 HUNTING_BALANCE 의 권장 레벨 구간. 각 대역에서 쓰는 사냥터는 몬스터 레벨 <= 대역 최대 레벨 + 2 중 처치 경험치가 가장 높은 곳.
- 처치 경험치는 maps.json fieldSpawns[].xp(서버가 지급하는 값 그대로). 골드 환산은 획득 시점에 상점 판매가로 한다(팔지 않고 모아 둬도 감시된다).
  기본 골드(monsters.json fieldGold) + 재료 판매가 기대값 + 장비 드롭(equipmentDropChance x 그 레벨 단계 드롭 풀의 가중 평균 판매가, 두 직업 풀 전체).
- 필드 보스: 15분 창당 캐릭터 1회 -> 시간당 4회. 보스 처치의 기대 골드 환산 = 기본 드롭 + 보스 추가 골드 + 강화석 3~5 + 마력 정수 1 + 보스 장비 확률 x 판매가.
- 요일 던전 하루 3판(dailyEntries), 클리어 경험치 = max(clearXpFloor[난이도] x xpMul) x 최고 등급 보너스(S, +50%). 카드 골드는 던전별 최댓값 x rewardMul.
- 레이드: 중간 레이드(Lv20+)는 하루 1회 보상, 최종 레이드(Lv40)는 주 1회. 경험치 = clearXpFloor x 1.5.
- 퀘스트 보상은 캐릭터당 1회 청구라 속도 감시에서 제외한다(서버 집계 사유 표 참조).
- 입장 가능 난이도는 대역 최소 레벨 >= 권장 레벨 - PARTY_MIN_LEVEL_SLACK(5)인 가장 높은 난이도(이전 난이도 클리어는 이미 했다고 본다).

사용: python3 Tools/balance/theory_income.py [--check]
  --check : 파일을 쓰지 않고 문서 12.2절 손계산 표와의 차이만 출력한다.
"""
import json
import math
import os
import sys

DATA = os.path.join(os.path.dirname(__file__), "..", "..", "server", "data")
OUT = os.path.join(DATA, "income_caps.json")

KILLS_PER_MIN = 30
BOSSES_PER_HOUR = 4          # 15분 창당 1회
PARTY_MIN_LEVEL_SLACK = 5
TIER_LEVELS = [1, 10, 15, 20, 25, 30, 35, 40]
BAND_RANGES = [(1, 4), (5, 8), (9, 12), (13, 16), (17, 20), (21, 24), (25, 28), (29, 33), (34, 40)]
TOP_RANK_XP_MUL = 1.5        # 최고 등급(S) 경험치 보너스 +50% (dungeons.json ranking.xpBonus[0])
UNIQUE_PLUS_PER_DAY = 4      # 던전 카드 대박 + 레이드 레전더리(필드 일반 드롭에는 유니크 이상이 없다)


def load(name):
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


MAPS = load("maps.json")["maps"]
MONSTERS = load("monsters.json")
MON = {m["id"]: m for m in MONSTERS["monsters"]}
SHOP = load("shop.json")
DUNGEONS = load("dungeons.json")
PROGRESSION = load("progression.json")
EQUIP = SHOP["equipment"]
EQUIP_BY_ID = {e["id"]: e for e in EQUIP}
MATERIALS = SHOP["materials"]
SELL = {m["id"]: m["sellPrice"] for m in MATERIALS}


def tier_of(level):
    t = 0
    for i, lv in enumerate(TIER_LEVELS):
        if lv <= level:
            t = i
    return t


def pool_stats(tier):
    """그 레벨 단계의 일반 장비 드롭 풀(dropWeight > 0, bossOnly 아님): (가중 평균 판매가, 에픽 이상 가중 비율)."""
    pool = [e for e in EQUIP if e["dropWeight"] > 0 and not e["bossOnly"] and e["levelTier"] == tier]
    total = sum(e["dropWeight"] for e in pool)
    if total <= 0:
        return 0.0, 0.0
    avg = sum(e["sellPrice"] * e["dropWeight"] for e in pool) / total
    epic = sum(e["dropWeight"] for e in pool if e["rarity"] in ("Epic", "Unique", "Legendary")) / total
    return avg, epic


def base_drop_gold_eq():
    """처치마다 나오는 기본 골드 평균 + 재료 판매가 기대값(뼈·광석·정수)."""
    gold = (MONSTERS["fieldGoldMin"] + MONSTERS["fieldGoldMaxExclusive"] - 1) / 2
    mats = 0.0
    for m in MATERIALS:
        avg_n = (m["minDrop"] + m["maxDrop"]) / 2
        mats += m["dropChance"] * avg_n * m["sellPrice"]
    return gold, mats


def zones():
    out = []
    for m in MAPS:
        spawns = m.get("fieldSpawns") or []
        if not spawns:
            continue
        points = sum(s["points"] for s in spawns)
        xp = sum(s["xp"] * s["points"] for s in spawns) / points
        out.append({"map": m["id"], "level": spawns[0]["level"], "xp": xp, "boss": m.get("fieldBoss")})
    return out


ZONES = zones()
BOSSES = [(z["boss"]["level"], z["boss"]["xp"], z["boss"]["monsterId"], z["map"]) for z in ZONES if z["boss"]]


def boss_gold_eq(monster_id, tier):
    """필드 보스 한 마리의 기대 골드 환산: 기본 드롭 + 추가 골드 + 강화석 3~5 + 마력 정수 1 + 보스 장비 확률."""
    b = MON[monster_id]
    gold, mats = base_drop_gold_eq()
    avg_eq, _ = pool_stats(tier)
    eq = MONSTERS["equipmentDropChance"] * avg_eq
    extra = (b["goldMin"] + b["goldMax"]) / 2
    ore = 4 * SELL["mat_ore"]      # rng.int(3, 6) 평균 4
    essence = 1 * SELL["mat_essence"]
    gear = 0.0
    if b.get("bossGear"):
        gear = b["bossGearPermille"] / 1000 * EQUIP_BY_ID[b["bossGear"]]["sellPrice"]
    return gold + mats + eq + extra + ore + essence + gear


def hour_rates(band_max_level):
    """한 대역의 시간당 이론 최대. 사냥터는 몬스터 레벨 <= 대역 최대 + 2 중 처치 경험치가 가장 높은 곳."""
    cands = [z for z in ZONES if z["level"] <= band_max_level + 2]
    zone = max(cands, key=lambda z: (z["xp"], z["level"]))
    bosses = [b for b in BOSSES if b[0] <= band_max_level + 2]
    boss = max(bosses, key=lambda b: b[1]) if bosses else None
    kills = KILLS_PER_MIN * 60
    tier = tier_of(zone["level"])
    gold, mats = base_drop_gold_eq()
    avg_eq, epic_share = pool_stats(tier)
    per_kill = gold + mats + MONSTERS["equipmentDropChance"] * avg_eq
    xp = kills * zone["xp"]
    gold_eq = kills * per_kill
    ore = kills * next(m["dropChance"] * (m["minDrop"] + m["maxDrop"]) / 2 for m in MATERIALS if m["id"] == "mat_ore")
    essence = kills * next(m["dropChance"] * (m["minDrop"] + m["maxDrop"]) / 2 for m in MATERIALS if m["id"] == "mat_essence")
    epic = kills * MONSTERS["equipmentDropChance"] * epic_share
    if boss:
        xp += BOSSES_PER_HOUR * boss[1]
        gold_eq += BOSSES_PER_HOUR * boss_gold_eq(boss[2], tier_of(boss[0]))
        ore += BOSSES_PER_HOUR * 4
        essence += BOSSES_PER_HOUR * 1
    return {
        "zone": zone["map"], "monsterLevel": zone["level"], "boss": boss[3] if boss else None,
        "xp": xp, "goldEq": gold_eq, "ore": ore, "essence": essence, "epicPlus": epic,
    }


def best_difficulty(band_min_level):
    """대역 최소 레벨로 입장 가능한 가장 높은 요일 던전 난이도(권장 레벨 - 여유)."""
    best = 0
    for i, d in enumerate(DUNGEONS["difficulties"]):
        if band_min_level >= d["recommendedLevel"] - PARTY_MIN_LEVEL_SLACK:
            best = i
    return best


def day_lumps(difficulty):
    """요일 던전 하루 dailyEntries판: 경험치 = 3 x max(던전별 clearXpFloor[난이도] x xpMul) x 최고 등급 보너스, 골드 = 3 x 카드 골드 최댓값 x rewardMul."""
    plain = [d for d in DUNGEONS["dungeons"] if not d["isRaid"]]
    runs = DUNGEONS["dailyEntries"]
    xp = runs * max(d["clearXpFloor"][difficulty] * d["xpMul"] for d in plain) * TOP_RANK_XP_MUL
    card_gold = max((r["max"] for d in plain for r in d["rewards"] if r["itemId"] == "gold"), default=0)
    gold = runs * card_gold * DUNGEONS["difficulties"][difficulty]["rewardMul"]
    return xp, gold


def raid_lumps(tier):
    for d in DUNGEONS["dungeons"]:
        if d["isRaid"] and d["raidTier"] == tier:
            xp = d["clearXpFloor"][0] * TOP_RANK_XP_MUL
            card_gold = max((r["max"] for r in d["rewards"] if r["itemId"] == "gold"), default=0)
            gold = card_gold * d["raidNumbers"]["rewardMul"]
            return d["raidNumbers"]["recommendedLevel"], xp, gold
    return None


def build():
    mid = raid_lumps("Mid")
    fin = raid_lumps("Final")
    bands = []
    for lo, hi in BAND_RANGES:
        r = hour_rates(hi)
        diff = best_difficulty(lo)
        d_xp, d_gold = day_lumps(diff)
        mid_xp = mid_gold = fin_xp = fin_gold = 0
        if mid and hi >= mid[0]:
            mid_xp, mid_gold = mid[1], mid[2]
        if fin and hi >= fin[0]:
            fin_xp, fin_gold = fin[1], fin[2]
        bands.append({
            "minLevel": lo, "maxLevel": hi,
            "zone": r["zone"], "monsterLevel": r["monsterLevel"], "fieldBoss": r["boss"], "dungeonDifficulty": diff,
            "perHour": {"xp": math.ceil(r["xp"]), "goldEq": round(r["goldEq"]), "ore": round(r["ore"], 2), "essence": round(r["essence"], 2), "epicPlus": round(r["epicPlus"], 2)},
            "perDay": {"dungeonXp": round(d_xp), "dungeonGoldEq": round(d_gold), "raidMidXp": round(mid_xp), "raidMidGoldEq": round(mid_gold)},
            "perWeek": {"raidFinalXp": round(fin_xp), "raidFinalGoldEq": round(fin_gold)},
        })
    boss_permille = max((MON[b[2]].get("bossGearPermille", 0) for b in BOSSES), default=0)
    return {
        "schema": 1,
        "generatedBy": "Tools/balance/theory_income.py",
        "killsPerMinute": KILLS_PER_MIN,
        "bossesPerHour": BOSSES_PER_HOUR,
        "bands": bands,
        "uniquePlus": {"perDay": UNIQUE_PLUS_PER_DAY, "perHour": round(BOSSES_PER_HOUR * boss_permille / 1000, 3)},
    }


# 문서 12.2절의 손계산 표(데이터 기준일 2026-10-06). 스크립트 결과와 대조해 차이를 출력한다.
DOC_HOURLY = {
    1: (16200, 77700, 630, 144), 2: (21600, 87200, 630, 144), 3: (31800, 90200, 646, 148), 4: (42600, 95800, 646, 148),
    5: (55200, 101500, 646, 148), 6: (73200, 106900, 646, 148), 7: (73200, 106900, 646, 148), 8: (96600, 112600, 646, 148),
    9: (150700, 123900, 646, 148),
}
DOC_DAY = {  # 대역 -> (던전 XP/일, 던전 골드/일, 중간 레이드 XP/일, 중간 레이드 골드/일)
    1: (8600, 3600, 0, 0), 2: (8600, 3600, 0, 0), 3: (20871, 5760, 0, 0), 4: (20871, 5760, 0, 0),
    5: (43477, 8640, 10092, 4500), 6: (43477, 8640, 10092, 4500), 7: (90011, 12240, 10092, 4500),
    8: (90011, 12240, 10092, 4500), 9: (90011, 12240, 10092, 4500),
}


def compare(data):
    print("대역  | 항목        | 스크립트    | 문서        | 차이")
    for i, b in enumerate(data["bands"], start=1):
        h = DOC_HOURLY[i]
        rows = [("xp/h", b["perHour"]["xp"], h[0]), ("goldEq/h", b["perHour"]["goldEq"], h[1]), ("ore/h", b["perHour"]["ore"], h[2]), ("essence/h", b["perHour"]["essence"], h[3])]
        d = DOC_DAY[i]
        rows += [("dungeonXp/d", b["perDay"]["dungeonXp"], d[0]), ("dungeonGold/d", b["perDay"]["dungeonGoldEq"], d[1]),
                 ("raidMidXp/d", b["perDay"]["raidMidXp"], d[2]), ("raidMidGold/d", b["perDay"]["raidMidGoldEq"], d[3])]
        for name, mine, doc in rows:
            if abs(mine - doc) > 0.5:
                print(f"{i:>4}  | {name:<12}| {mine:>11} | {doc:>11} | {mine - doc:+.1f}")
    first_epic = next(i for i, b in enumerate(data["bands"], start=1) if b["perHour"]["epicPlus"] > 0)
    print(f"에픽 이상 장비/h: 처음 나오는 대역 {first_epic} (문서 3), 값 {data['bands'][first_epic - 1]['perHour']['epicPlus']} (문서 약 31)")
    print(f"최종 레이드(대역 9): XP/주 {data['bands'][8]['perWeek']['raidFinalXp']} (문서 41850), 골드 {data['bands'][8]['perWeek']['raidFinalGoldEq']} (문서 18000)")
    print(f"유니크 이상: 하루 {data['uniquePlus']['perDay']}, 시간당 {data['uniquePlus']['perHour']} (문서 4, 0.12)")


if __name__ == "__main__":
    result = build()
    compare(result)
    if "--check" not in sys.argv:
        with open(OUT, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print(f"쓴 파일: {os.path.abspath(OUT)}")
