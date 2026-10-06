"""
dotRPG 던전 소탕 이론 계산 (직접 S등급 대비 55~65% 목표 확인). Docs/server/phase10_sweep_mail.md 3.3절 표의 재검산.

서버 데이터(server/data/*.json)를 읽어 요일 던전 5종 x 난이도 4개에서 소탕 1회의 경험치와 직접 S등급 1판의 경험치를 비교한다.
서버는 이 스크립트나 표를 읽지 않는다(검증용). 소탕 값(sweep.json)이나 던전·몬스터·상점 값이 바뀌면 다시 돌려 문서 표를 갱신한다.

모델 (문서 3.3절과 같다)
- 소탕 경험치 = 클리어 기본값 x (1 + sweep.xpBonusPercent/100). 처치 경험치·드롭·대박 카드·보호권 외 장비 카드는 없다. 서버 코드(dungeonRules.clearXpBase/applyXpBonus)와 같은 float32 연산.
- 직접 S 경험치 = round(클리어 기본값 x 1.30) + 처치 경험치 합. 처치 경험치 = 몬스터 xpByLevel[레벨 - 1], 레벨 = 1 + 난이도 monsterLevel + 그룹 levelOffset(보스 오프셋은 데이터가 정한다).
- 직접 처치 드롭 가치(NPC 판매가, 골드는 액면): 몬스터 1마리당 기본 골드(fieldGold 평균) + 재료 기대값, 몬스터별 추가 골드(goldMin/goldMax 평균: 보스·황금 해골).
  빼고 센 것(직접 쪽에만 있는 이득이라 비율은 소탕에 유리하게 나온다): 장비 드롭, 황금 해골의 타격당 골드, 대박 카드, 장비 보호권.
- 카드 기대값: 비장비 항목의 (개수 평균 x 개수 배율 x 판매가) 가중 평균(개수는 반올림하지 않은 기대값. 서버 굴림은 개수마다 반올림하므로 실제와 1% 안쪽 차이). 소탕은 장비 확률 p가 0.7p가 되어 비장비가 (1 - 0.7p)/(1 - p)배로 늘어난다.
- 비율 두 가지: w=0 경험치만, w=1 경험치 + 드롭·카드 가치를 1:1로 환산.

사용: python3 Tools/balance/theory_sweep.py [--check]
  --check : 문서 3.3절 표(2026-10-06 기준)와 다른 칸이 있으면 출력하고 종료 코드 1.
"""
import json
import os
import struct
import sys

DATA = os.path.join(os.path.dirname(__file__), "..", "..", "server", "data")


def load(name):
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


def f32(x):
    return struct.unpack("f", struct.pack("f", x))[0]


def round_half_even(x):
    f = int(x // 1)
    d = x - f
    if d < 0.5:
        return f
    if d > 0.5:
        return f + 1
    return f if f % 2 == 0 else f + 1


DUNGEONS = load("dungeons.json")
MONSTERS = load("monsters.json")
SHOP = load("shop.json")
SWEEP = load("sweep.json")
MON = {m["id"]: m for m in MONSTERS["monsters"]}
SELL = {m["id"]: m["sellPrice"] for m in SHOP["materials"]}
SELL.update({s["id"]: s["sellPrice"] for s in SHOP["sellPrices"]})
GEAR_KEEP = SWEEP["gearKeepPercent"] / 100.0
TOP_RANK_BONUS = DUNGEONS["ranking"]["xpBonus"][2]  # S 등급(번호 2)
TICKET_ITEM = "ticket_protect"  # enhance.json ticketItem (보호권은 개수 배율을 곱하지 않는다)
GOLD_AVG = (MONSTERS["fieldGoldMin"] + MONSTERS["fieldGoldMaxExclusive"] - 1) / 2.0
MATERIAL_EV = sum(m["dropChance"] * (m["minDrop"] + m["maxDrop"]) / 2.0 * m["sellPrice"] for m in SHOP["materials"] if m["id"] in ("mat_bone", "mat_ore", "mat_essence"))


def clear_base(d, diff, tier):
    base = max(f32(d["clearXp"] * diff["rewardMul"]), d["clearXpFloor"][tier])
    return f32(base * d["xpMul"])


def apply_bonus(xp, pct):
    return round_half_even(f32(xp * f32(1 + pct / 100.0)))


def kill_xp_and_drops(d, diff):
    xp = 0
    value = 0.0
    for room in d["rooms"]:
        for g in room["groups"]:
            m = MON[g["monsterId"]]
            level = 1 + diff["monsterLevel"] + g["levelOffset"]
            xp += g["count"] * m["xpByLevel"][level - 1]
            extra = (m["goldMin"] + m["goldMax"]) / 2.0
            value += g["count"] * (GOLD_AVG + MATERIAL_EV + extra)
    return xp, value


def card_values(d, diff):
    table = list(d["rewards"])
    if diff["ticketWeight"] > 0:
        table.append({"itemId": TICKET_ITEM, "min": 1, "max": 1, "weight": diff["ticketWeight"]})
    total = sum(max(0, e["weight"]) for e in table)
    ev = 0.0
    gear_w = 0
    for e in table:
        if e["itemId"] == "gear":
            gear_w += e["weight"]
            continue
        counts = []
        for n in range(e["min"], e["max"] + 1):
            counts.append(n if e["itemId"] == TICKET_ITEM else n * diff["rewardMul"])
        avg = sum(counts) / len(counts)
        price = 1 if e["itemId"] == "gold" else SELL.get(e["itemId"], 0)
        ev += e["weight"] * avg * price
    ev /= total
    p = gear_w / total
    return ev, ev * (1 - GEAR_KEEP * p) / (1 - p) if p < 1 else ev


def compute():
    rows = []
    for d in DUNGEONS["dungeons"]:
        if d["isRaid"]:
            continue
        for i, diff in enumerate(DUNGEONS["difficulties"]):
            base = clear_base(d, diff, i)
            sweep_xp = apply_bonus(base, SWEEP["xpBonusPercent"])
            clear_s = round_half_even(sweep_xp * (1 + TOP_RANK_BONUS / 100.0))  # 문서 표의 방식: 반올림한 기본값에 S 보너스(서버는 float 그대로 곱해 최대 1 차이)
            kills, drops = kill_xp_and_drops(d, diff)
            direct = clear_s + kills
            ev_direct, ev_sweep = card_values(d, diff)
            rows.append({
                "dungeon": d["id"], "difficulty": i, "sweep_xp": sweep_xp, "direct_xp": direct, "clear_s": clear_s, "kill_xp": kills,
                "w0": sweep_xp / direct * 100.0,
                "w1": (sweep_xp + ev_sweep) / (direct + drops + ev_direct) * 100.0,
            })
    return rows


# 문서 3.3절 표(2026-10-06): 던전, 난이도 -> (소탕 경험치, 직접 S 경험치, 클리어, 처치, w=0 %, w=1 %)
DOC = {
    ("gold_vein", 0): (1102, 2329, 1433, 896, 47.3, 32.6), ("gold_vein", 1): (2771, 5054, 3602, 1452, 54.8, 45.1),
    ("gold_vein", 2): (5913, 9777, 7687, 2090, 60.5, 54.6), ("gold_vein", 3): (12474, 18862, 16216, 2646, 66.1, 62.6),
    ("smelter", 0): (1153, 2344, 1499, 845, 49.2, 38.9), ("smelter", 1): (2849, 5078, 3704, 1374, 56.1, 50.1),
    ("smelter", 2): (6036, 9814, 7847, 1967, 61.5, 58.0), ("smelter", 3): (12624, 18907, 16411, 2496, 66.8, 64.7),
    ("mana_graveyard", 0): (1270, 2513, 1651, 862, 50.5, 41.2), ("mana_graveyard", 1): (3077, 5395, 4000, 1395, 57.0, 51.7),
    ("mana_graveyard", 2): (6423, 10351, 8350, 2001, 62.1, 59.1), ("mana_graveyard", 3): (13308, 19832, 17300, 2532, 67.1, 65.4),
    ("training_forest", 0): (1911, 3342, 2484, 858, 57.2, 47.5), ("training_forest", 1): (4638, 7409, 6029, 1380, 62.6, 57.4),
    ("training_forest", 2): (9662, 14544, 12561, 1983, 66.4, 63.6), ("training_forest", 3): (20002, 28508, 26003, 2505, 70.2, 68.7),
    ("armory", 0): (1322, 2662, 1719, 943, 49.7, 40.5), ("armory", 1): (3198, 5679, 4157, 1522, 56.3, 51.0),
    ("armory", 2): (6663, 10845, 8662, 2183, 61.4, 58.4), ("armory", 3): (13796, 20699, 17935, 2764, 66.6, 64.9),
}


def main():
    rows = compute()
    print("던전              난이도  소탕 경험치  직접 S 경험치 (클리어+처치)   w=0     w=1")
    for r in rows:
        print("%-16s %4d %10d %10d (%d+%d) %7.1f%% %7.1f%%" % (r["dungeon"], r["difficulty"], r["sweep_xp"], r["direct_xp"], r["clear_s"], r["kill_xp"], r["w0"], r["w1"]))
    if "--check" in sys.argv:
        bad = 0
        for r in rows:
            doc = DOC[(r["dungeon"], r["difficulty"])]
            got = (r["sweep_xp"], r["direct_xp"], r["clear_s"], r["kill_xp"], r["w0"], r["w1"])
            # 경험치는 정수가 같아야 하고, 비율은 문서의 소수 첫째 자리 반올림 오차(0.15%p)를 허용한다
            if got[:4] != doc[:4] or abs(got[4] - doc[4]) > 0.15 or abs(got[5] - doc[5]) > 0.15:
                bad += 1
                print("차이", r["dungeon"], r["difficulty"], "문서", doc, "계산", tuple(round(x, 1) if isinstance(x, float) else x for x in got))
        print("문서 표와 같다" if bad == 0 else "문서 표와 다른 칸 %d개" % bad)
        sys.exit(0 if bad == 0 else 1)


if __name__ == "__main__":
    main()
