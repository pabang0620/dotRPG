"""
dotRPG 무자본 골드 수지 (2026-10-16). 게임을 돌리지 않는 이론 계산.
"무자본은 돈이 조금 모자라야 한다"(사용자, 던전앤파이터식): 만렙·레이드 클리어 장비(강화 목표 한 벌)에 드는 골드와 재료를
Lv1~40 동안 무자본으로 버는 골드와 비교한다. 수입이 필요 지출의 85~90%가 되게 상수를 잡는다(딱 맞지 않고 조금 부족).

수입 (theory_progress.py 설계 기준 시나리오: 분당 15마리, 하루 2시간, 친구와 레이드)
- 처치: 몬스터 레벨별 처치 수(사냥 + 퀘스트 처치) x 기본 골드 더미 평균. 재료(뼈·강화석·정수)는 강화에 먼저 쓰고 남는 것만 판다.
  장비 드롭(equipmentDropChance)은 그 단계 드롭 풀의 가중 평균 판매가로 판다(쓸 것은 한 벌뿐이라 대부분 판다).
- 요일 던전 하루 3판(Lv5부터): 확정 골드 x rewardMul + 운 카드 2장의 골드 기대값. 난이도는 레벨 >= 권장 - 2 중 가장 높은 것.
- 일일 의뢰 하루 3개(Lv15부터), 주간 3개: daily_quests.json bands 골드.
- 레이드: 해골왕 주 3회(Lv20부터, 보상 하루 1회), 그라흐 Lv40 뒤 1회. raidReward 골드 평균.
- 시작 골드 100.

지출 (클리어에 "필요한" 한 벌, 레전더리·유니크 운은 없다고 본다)
- 강화: 아래 KIT 목표까지 기대 골드(실패 포함 마르코프 기대값. +10·+11 무기 실패는 -3, 방어구 +10 위는 파괴라 목표에 넣지 않는다).
- 재료: 강화에 필요한 뼈·강화석·정수 기대 수량. 드롭으로 모자라면 잡화점에서 산다(지출), 남으면 판다(수입).
- 물약: 사냥 1분당 POTIONS_PER_MIN개(실측 평균 1.0, analysis/player-sim 2026-10-13) x 물약값. 귀환 주문서 30분당 1장.
- 승급·보호권·경매 수수료는 선택 지출이라 넣지 않는다(있으면 더 부족해진다).

사용: python3 Tools/balance/theory_gold.py            현재 데이터로 수지표
      python3 Tools/balance/theory_gold.py --search   강화 골드 배율 후보를 훑어 목표 비율에 맞는 값을 찾는다
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import theory_progress as tp  # noqa: E402

DATA = tp.DATA


def load(name):
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


SHOP = load("shop.json")
MONSTERS = load("monsters.json")
DNG = load("dungeons.json")
DAILY = load("daily_quests.json")
STARTER = load("starter.json")
ENH = load("enhance.json")
EQ = {e["id"]: e for e in SHOP["equipment"]}
BUY = {s["id"]: s["buyPrice"] for s in SHOP["stock"]}
SELL = {m["id"]: m["sellPrice"] for m in SHOP["materials"]}
SELL.update({s["id"]: s["sellPrice"] for s in SHOP["sellPrices"]})
MAT = {m["id"]: m for m in SHOP["materials"]}
TIERS = [1, 10, 15, 20, 25, 30, 35, 40]

HOURS_PER_DAY = 2.0
POTIONS_PER_MIN = 1.0          # 실측 평균(사냥 1분당 HP+MP 물약)
SCROLLS_PER_HOUR = 2.0
FIELD_BOSS_PER_HOUR = 1.0
TARGET_RATIO = (0.85, 0.90)    # 수입 / 필요 지출

# 클리어 한 벌(무자본 목표). (레벨, 장비 id, 목표 강화). 전사 기준(마법사도 같은 값).
# 근거: theory_balance.py BUILDS - 왕(Lv20) 무기 +10, 영웅(Lv27) 무기 +12. 최종 레이드는 Lv40 에픽 무기 +11이면 이론 클리어
# 480초 안팎(목표 300~600)이라 +11을 "필요"로, +12부터는 운·과금의 몫으로 본다.
KIT = [
    (10, "eq_sword_10_r", 7),
    (15, "eq_sword_15_r", 7), (15, "eq_plate_15_r", 5), (15, "eq_greaves_15_r", 5),
    (20, "eq_sword_20_r", 10), (20, "eq_plate_20_r", 7), (20, "eq_greaves_20_r", 7), (20, "eq_neck_20_r", 5), (20, "eq_ring_20_r", 5),
    (30, "eq_sword_30_r", 10), (30, "eq_plate_30_r", 7), (30, "eq_greaves_30_r", 7),
    (40, "eq_sword_40_e", 11), (40, "eq_plate_40_e", 10), (40, "eq_greaves_40_e", 10), (40, "eq_neck_40_r", 8), (40, "eq_ring_40_r", 8),
]


def tier_of(level):
    return max(i for i, t in enumerate(TIERS) if t <= level)


# ---------------- 강화 기대 비용 (마르코프) ----------------

def expected_attempts(item_id, target, gold_scale=1.0):
    """+0에서 target까지 가는 동안 각 레벨에서 시도하는 기대 횟수 n[L]. Keep은 그 자리, Drop3는 L-3으로, 파괴는 목표에 없다."""
    levels = {l["from"]: l for l in ENH["steps"][[s["id"] for s in ENH["steps"]].index(item_id)]["levels"]}
    # 기대 방문 횟수: 선형 방정식을 값 반복으로 푼다. n[L] = 1회 진입당 시도 수 ... 간단히 상태 체인 기대값을 반복 계산
    n = [0.0] * (target + 1)
    # E[L] = 목표까지 기대 "시도 벡터"가 아니라, 레벨 L에 한 번 도착했을 때 이후 각 레벨 시도 횟수 v[L][k]
    v = [[0.0] * target for _ in range(target + 1)]
    for _ in range(400):
        new = [[0.0] * target for _ in range(target + 1)]
        for L in range(target - 1, -1, -1):
            row = levels[L]
            p = row["successPercent"] / 100.0
            if row["pity"]:
                p = min(1.0, p + 0.02)  # 천장 +1%p/실패의 평균 효과
            fail_to = L if row["failure"] == "Keep" else max(0, L - 3) if row["failure"] == "Drop3" else None
            assert fail_to is not None, f"{item_id} +{L} 실패가 파괴: 무자본 목표에 넣지 않는다"
            cur = [0.0] * target
            cur[L] = 1.0
            for k in range(target):
                cur[k] += p * v[L + 1][k] + (1 - p) * v[fail_to][k]
            new[L] = cur
        v = new
    return v[0]


def enhance_cost(item_id, target, gold_scale=1.0):
    levels = {l["from"]: l for l in ENH["steps"][[s["id"] for s in ENH["steps"]].index(item_id)]["levels"]}
    n = expected_attempts(item_id, target)
    gold_scale = gold_scale * ENH_SCALE_BY_TIER[EQ[item_id]["levelTier"]]
    gold = sum(n[L] * levels[L]["gold"] * gold_scale for L in range(target))
    mats = {k: sum(n[L] * levels[L][k] for L in range(target)) for k in ("bone", "ore", "essence")}
    return gold, mats, sum(n)


# ---------------- 수입 ----------------

GEAR_SELL_SCALE = 1.0
MAT_SELL_SCALE = 1.0
ENH_SCALE_BY_TIER = [1.0] * 8


def gold_per_kill_gear(tier):
    pool = [e for e in SHOP["equipment"] if e["dropWeight"] > 0 and not e["bossOnly"] and e["levelTier"] == tier]
    total = sum(e["dropWeight"] for e in pool)
    avg = sum(e["sellPrice"] * e["dropWeight"] for e in pool) / total if total else 0
    return MONSTERS["equipmentDropChance"] * avg * GEAR_SELL_SCALE


def base_pile():
    return (MONSTERS["fieldGoldMin"] + MONSTERS["fieldGoldMaxExclusive"] - 1) / 2


def dungeon_gold(level):
    """그 레벨에서 하루 3판 요일 던전 1판의 골드 기대값(확정 + 운 카드)."""
    diffs = DNG["difficulties"]
    tier = max([i for i, d in enumerate(diffs) if level >= d["recommendedLevel"] - 2] or [0])
    diff = diffs[tier]
    vals = []
    for d in DNG["dungeons"]:
        if d["isRaid"] or d.get("always") or not d.get("sure"):
            continue
        sure = (d["sure"]["goldMin"] + d["sure"]["goldMax"]) / 2 * diff["rewardMul"]
        w = sum(r["weight"] for r in d["rewards"])
        card = sum((r["min"] + r["max"]) / 2 * diff["rewardMul"] * r["weight"] / w for r in d["rewards"] if r["itemId"] == "gold")
        vals.append(sure + 2 * card)
    return sum(vals) / len(vals)


def daily_gold(level):
    for b in DAILY["bands"]:
        if b["minLevel"] <= level <= b["maxLevel"]:
            return b["gold"]
    return DAILY.get("maxLevelGold", 0) if level >= 40 else 0


def weekly_gold(level):
    w = DAILY.get("weekly", {})
    for b in w.get("bands", []):
        if b["minLevel"] <= level <= b["maxLevel"]:
            return b.get("gold", 0)
    return w.get("gold", 0)


def raid_gold(rid):
    rr = next(d for d in DNG["dungeons"] if d["id"] == rid)["raidReward"]
    return (rr["goldMin"] + rr["goldMax"]) / 2


def budget(gold_scale=1.0, potion_price=None, verbose=True):
    tp.TRAVEL, tp.FIXED_KPM = 4.0, 15
    reached, events, claimed, days, st = tp.simulate(0.35, HOURS_PER_DAY, True, True)
    hours = reached[40]
    potion_price = BUY["potion_hp"] if potion_price is None else potion_price

    def level_at(h):
        lv = 1
        for L, t in reached.items():
            if t <= h:
                lv = max(lv, L)
        return lv

    # ---- 수입 ----
    kills_by_tier = {}
    for ml, n in st["kills"].items():
        kills_by_tier[tier_of(ml)] = kills_by_tier.get(tier_of(ml), 0) + n
    total_kills = sum(kills_by_tier.values())
    inc = {"시작": STARTER["gold"], "처치 골드": total_kills * base_pile(), "장비 판매": sum(n * gold_per_kill_gear(t) for t, n in kills_by_tier.items())}
    drops = {m: total_kills * MAT[m]["dropChance"] * (MAT[m]["minDrop"] + MAT[m]["maxDrop"]) / 2 for m in MAT}
    dng, dly, wk, raid = 0.0, 0.0, 0.0, 0.0
    day_count = int(hours / HOURS_PER_DAY)
    for day in range(day_count):
        lv = level_at(day * HOURS_PER_DAY)
        if lv >= 5:
            dng += 3 * dungeon_gold(lv)
        if lv >= 15 and lv < 40:
            dly += 3 * daily_gold(lv)
            if day % 7 == 0:
                wk += 3 * weekly_gold(lv)
        if lv >= 20 and day % 7 in (2, 5, 6):  # 수·토·일 해골왕
            raid += raid_gold("raid_skeleton_king")
    raid += raid_gold("raid_grah")
    # 필드 보스: 15분 창마다 가능하지만 보통 한 시간에 한 번 잡는다고 본다(처치 골드 400~700 평균)
    fb = [m for m in MONSTERS["monsters"] if m["id"].startswith("fboss_")]
    fboss = hours * FIELD_BOSS_PER_HOUR * sum((m["goldMin"] + m["goldMax"]) / 2 for m in fb) / len(fb)
    inc.update({"요일 던전": dng, "일일 의뢰": dly, "주간 의뢰": wk, "레이드": raid, "필드 보스": fboss})

    # ---- 지출 ----
    enh_gold, need = 0.0, {"bone": 0.0, "ore": 0.0, "essence": 0.0}
    rows = []
    for lv, item, target in KIT:
        g, mats, tries = enhance_cost(item, target, gold_scale)
        enh_gold += g
        for k in need:
            need[k] += mats[k]
        rows.append((lv, item, target, g, tries, mats))
    kill_min = st["hunt_min"] + st["quest_kill_min"]
    potions = kill_min * POTIONS_PER_MIN * potion_price
    scrolls = hours * SCROLLS_PER_HOUR * BUY["scroll_town"]
    mat_buy, mat_sell = 0.0, 0.0
    for m in MAT:
        short = need[m.replace("mat_", "")] - drops[m]
        if short > 0:
            mat_buy += short * BUY[m]
        else:
            mat_sell += -short * SELL[m] * MAT_SELL_SCALE
    inc["재료 판매(남는 것)"] = mat_sell
    spend = {"강화 골드": enh_gold, "재료 구매(모자란 것)": mat_buy, "물약": potions, "귀환 주문서": scrolls}
    ti, ts = sum(inc.values()), sum(spend.values())
    if verbose:
        print(f"Lv40 {hours:.1f}시간 ({day_count}일), 처치 {total_kills:,.0f}마리, 사냥 분 {kill_min:,.0f}")
        print("| 수입 | 골드 |\n|---|---|")
        for k, v in inc.items():
            print(f"| {k} | {v:,.0f} |")
        print(f"| 합계 | {ti:,.0f} |")
        print("\n| 지출(클리어 한 벌) | 골드 |\n|---|---|")
        for k, v in spend.items():
            print(f"| {k} | {v:,.0f} |")
        print(f"| 합계 | {ts:,.0f} |")
        print(f"\n수입/지출 = {ti / ts:.2f} (목표 {TARGET_RATIO[0]:.2f}~{TARGET_RATIO[1]:.2f})")
        print("재료 필요/드롭: " + ", ".join(f"{m} {need[m.replace('mat_', '')]:,.0f}/{drops[m]:,.0f}" for m in MAT))
        # 체크포인트: 그 레벨에 도달할 때까지의 누적 수입(처치·던전·의뢰·레이드·판매)과 그 레벨까지의 한 벌 지출
        print("\n| 체크포인트 | 도달 시간 | 누적 수입 | 누적 지출 | 비율 |\n|---|---|---|---|---|")
        for ck in (10, 15, 20, 30, 40):
            h = reached[ck]
            frac = h / hours
            # 그 시각까지의 처치 수(사냥 + 퀘스트 처치, 시뮬레이션 시각 기준)
            k_ck = sum(n for t, _, n in st["kills_t"] if t <= h)
            k_frac = min(1.0, k_ck / total_kills)
            ci = STARTER["gold"] + (inc["처치 골드"] + inc["장비 판매"] + inc["재료 판매(남는 것)"]) * k_frac + (dng + dly + wk + fboss) * frac + (raid * frac if ck >= 20 else 0)
            cs = sum(g for lv, _, _, g, _, _ in rows if lv <= ck) + (potions + scrolls) * k_frac
            print(f"| Lv{ck} | {h:.1f}h | {ci:,.0f} | {cs:,.0f} | {ci / cs:.2f} |")
        print("\n| 단계 | 장비 | 목표 | 기대 골드 | 기대 시도 | 뼈/강화석/정수 |\n|---|---|---|---|---|---|")
        for lv, item, target, g, tries, mats in rows:
            print(f"| {lv} | {item} | +{target} | {g:,.0f} | {tries:.1f} | {mats['bone']:.0f}/{mats['ore']:.0f}/{mats['essence']:.0f} |")
    return ti, ts, inc, spend


if __name__ == "__main__":
    if "--search" in sys.argv:
        import itertools
        for gs, ms, es in itertools.product((1.0, 0.5, 0.3), (1.0, 0.5), (8, 10, 12, 15)):
            GEAR_SELL_SCALE, MAT_SELL_SCALE = gs, ms
            ti, ts, _, _ = budget(es, verbose=False)
            print(f"장비판매 x{gs} 재료판매 x{ms} 강화골드 x{es}: 수입 {ti:,.0f} / 지출 {ts:,.0f} = {ti / ts:.2f}")
    else:
        budget()
