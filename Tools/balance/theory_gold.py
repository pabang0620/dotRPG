"""
dotRPG 무자본 골드 수지 (2026-10-16). 게임을 돌리지 않는 이론 계산.
"무자본은 돈이 조금 모자라야 한다"(사용자, 던전앤파이터식): 만렙·레이드 클리어 장비(강화 목표 한 벌)에 드는 골드와 재료를
Lv1~40 동안 무자본으로 버는 골드와 비교한다. 수입이 필요 지출의 85~90%가 되게 상수를 잡는다(딱 맞지 않고 조금 부족).

수입 (theory_progress.py: 실측 처치 속도(2026-10-16 자동 사냥, Lv1~15는 설계값 15), 하루 2시간, 친구와 레이드)
- 처치: 몬스터 레벨별 처치 수(사냥 + 퀘스트 처치) x 기본 골드 더미 평균. 재료(뼈·강화석·정수)는 강화에 먼저 쓰고 남는 것만 판다.
  장비 드롭(equipmentDropChance)은 그 단계 드롭 풀의 가중 평균 판매가로 판다(쓸 것은 한 벌뿐이라 대부분 판다).
- 요일 던전 하루 3판(Lv5부터): 확정 골드 x rewardMul + 운 카드 2장의 골드 기대값. 난이도는 레벨 >= 권장 - 2 중 가장 높은 것.
- 일일 의뢰 하루 3개(Lv15부터), 주간 3개: daily_quests.json bands 골드.
- 완료 상자(2026-10-24, daily_quests.json chests): 매일 연속(7일 주기 단계)·매주 연다고 본다. 골드로 바꿀 수 있는 것만 판매가로 센다:
  강화석·마력 정수(재료는 이미 남아서 판다), 강화 재료 상자(마력 정수 수량 기대값), 보너스 굴림 기대값.
  보호권 조각·장비 보호권·레이드 열쇠 조각·고대의 핵·스킨 게이지는 판매가 0이고 필요 지출(KIT, +11까지)에도 없어 비율에 넣지 않는다.
- 레이드: 해골왕 주 3회(Lv20부터, 보상 하루 1회), 그라흐 Lv40 뒤 1회. raidReward 골드 평균.
- 퀘스트 보상 골드(quest_index.json reward.gold, 스토리·서브 합계 약 42만). 시작 골드 100.

지출 (클리어에 "필요한" 한 벌, 레전더리·유니크 운은 없다고 본다)
- 강화: 아래 KIT 목표까지 기대 골드(실패 포함 마르코프 기대값. +10·+11 무기 실패는 -3, 방어구 +10 위는 파괴라 목표에 넣지 않는다).
- 재료: 강화에 필요한 뼈·강화석·정수 기대 수량. 드롭으로 모자라면 잡화점에서 산다(지출), 남으면 판다(수입).
- 물약: 사냥 1분당 POTIONS_PER_MIN개(실측 평균 1.0, analysis/player-sim 2026-10-13) x 물약값. 귀환 주문서 30분당 1장.
- 승급·보호권·경매 수수료는 선택 지출이라 넣지 않는다(있으면 더 부족해진다).

사용: python3 Tools/balance/theory_gold.py            현재 데이터로 수지표
      python3 Tools/balance/theory_gold.py --mini [분당 처치]   미니 던전을 매일 30분 하는 경로(기본 20마리)
      python3 Tools/balance/theory_gold.py --search   강화 골드 배율 후보를 훑어 목표 비율에 맞는 값을 찾는다
      --max-level N 을 붙이면 만렙 N 기준(theory_progress.MAX_LEVEL, 챕터 3은 50)
      python3 Tools/balance/theory_gold.py --to 50 --mini 20 [--after-days N] [--window-days 60] [--career fighter]
          챕터 3 수지(2026-10-23, ch3 7단계). 1~40 구간은 위 계산 그대로(만렙 40 시뮬)이고, 3장 구간은 Lv40 + c2_end부터 Lv50까지.
          비율 = (3장 공개 시점 잔고 + 3장 수입) / (Lv50 한 벌 + 계승 +12 무기 + 아카브 교환 4회)  (ROADMAP 3절 경제)
          3장 공개 시점 잔고 = 1~40 수입 - 1~40 한 벌 지출 + (공개까지 남은 날 x Lv40 만렙 일상 하루 수입)
          4장 공개 시점 잔고 = 3장 공개 시점 잔고 + 3장 수입 - 3장 지출 + (--after-days, 기본 = 창 60일 - 3장 날짜) x Lv50 만렙 일상 하루 수입
          만렙 일상 = 요일 던전 3판 + 일일 의뢰 만렙 골드 3개 + 주간 의뢰 만렙 골드 3개 + 그 장 레이드 두 곳 + 월드·길드보스(주 80,000). 사냥은 넣지 않는다(보수)
          1인 결정론 값이다(20명 시뮬 중앙값이 아니다)
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
TIERS = [1, 10, 15, 20, 25, 30, 35, 40, 45, 50]  # levelTier 인덱스(8 = Lv45, 9 = Lv50은 챕터 3)

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
QUEST_GOLD_SCALE = 1.0
# 미니 던전 처치 골드(2026-10-21): 처치마다 고정 골드, 드롭·재료 없음(사냥 처치 더미 평균 12의 3배: 드롭·재료 판매가 없는 대신).
# 클라 MiniDungeonCatalog.GoldPerKill, 서버 miniDungeonRules.MINI_GOLD_PER_KILL과 같은 값
MINI_GOLD_PER_KILL = 36
SRC = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Scripts", "Runtime", "Items")


def enh_scale_by_tier():
    """EnhanceRules.cs의 GoldByTier가 아직 enhance.json(내보내기 산출물)에 반영되지 않았으면 그 차이만큼 배율을 둔다.
    내보내기 뒤에는 1.0이 된다(소스와 데이터가 같아짐)."""
    import re
    try:
        with open(os.path.join(SRC, "EnhanceRules.cs"), encoding="utf-8") as f:
            text = f.read()
        tiers = [float(x) for x in re.search(r"GoldByTier\s*=\s*\{([^}]*)\}", text).group(1).split(",")]
        mul0 = float(re.search(r"GoldMul\s*=\s*\{\s*([^,]*),", text).group(1))
    except (OSError, AttributeError, ValueError):
        return [1.0] * len(TIERS)
    scale = []
    for i, lv in enumerate(TIERS):
        step = next((s for s in ENH["steps"] if s["id"] == f"eq_sword_{lv}_r"), None)
        data = step["levels"][0]["gold"] if step else 0
        scale.append(tiers[i] * mul0 / data if data else 1.0)
    return scale


def mat_sell_scale():
    """ConsumableDatabase.SellPrice의 재료 판매가가 shop.json(내보내기 산출물)과 다르면 그 차이(재료별). 내보낸 뒤에는 1.0."""
    import re
    try:
        with open(os.path.join(SRC, "ConsumableDatabase.cs"), encoding="utf-8") as f:
            text = f.read()
        body = text[text.index("public static int SellPrice"):]
        prices = {m: int(g) for m, g in re.findall(r'case "(mat_\w+)": return (\d+);', body)}
    except (OSError, ValueError):
        return {}
    return {m: prices[m] / SELL[m] for m in MAT if m in prices and SELL.get(m)}


ENH_SCALE_BY_TIER = enh_scale_by_tier()
MAT_SELL_SCALE_BY_MAT = mat_sell_scale()


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
    return DAILY.get("maxLevelGold", 0) if level >= tp.MAX_LEVEL else 0


def weekly_gold(level):
    w = DAILY.get("weekly", {})
    for b in w.get("bands", []):
        if b["minLevel"] <= level <= b["maxLevel"]:
            return b.get("gold", 0)
    return w.get("gold", 0)


CH = DAILY.get("chests")


def _items_gold(items):
    """아이템 묶음의 판매 환산 골드(재료 판매가, 강화 재료 상자는 마력 정수 기대 수량)"""
    g = 0.0
    for r in items:
        k, n = r["item_key"], r["count"]
        if CH and k == CH["matBox"]["item"]:
            b = CH["matBox"]["bands"]
            w = sum(x["weight"] for x in b)
            ev = sum((x["min"] + x["max"]) / 2 * x["weight"] / w for x in b)
            g += n * ev * SELL.get(CH["matBox"]["reward"], 0) * MAT_SELL_SCALE
        elif k in MAT:
            g += n * SELL[k] * MAT_SELL_SCALE * MAT_SELL_SCALE_BY_MAT.get(k, 1.0)
    return g


def _chest_gold(step, at_max):
    if not CH:
        return 0.0
    w = sum(b["weight"] for b in CH["bonus"])
    bonus = sum(_items_gold(b["rewards"]) * b["weight"] / w for b in CH["bonus"])
    return _items_gold(step["rewards"]) + (_items_gold(step["maxLevel"]) if at_max else 0) + step["bonusRolls"] * bonus


def chest_day_gold(streak, at_max=False):
    """연속 streak일째 일일 완료 상자의 판매 환산 골드"""
    if not CH:
        return 0.0
    return _chest_gold(CH["day"][(streak - 1) % len(CH["day"])], at_max)


def chest_week_gold(at_max=False):
    return _chest_gold(CH["week"], at_max) if CH else 0.0


def chest_day_avg(at_max=False):
    return sum(chest_day_gold(i + 1, at_max) for i in range(len(CH["day"]))) / len(CH["day"]) if CH else 0.0


def raid_gold(rid):
    rr = next(d for d in DNG["dungeons"] if d["id"] == rid)["raidReward"]
    return (rr["goldMin"] + rr["goldMax"]) / 2


LAST = {}


def budget(gold_scale=1.0, potion_price=None, verbose=True):
    # 2026-10-16: 실측 속도(자동 사냥, Lv16+ 분당 20~25)가 있으면 그것으로, 없으면 설계값 15
    tp.TRAVEL, tp.FIXED_KPM = 4.0, 15
    tp.MEASURED_KPM = tp.measured_kpm()
    reached, events, claimed, days, st = tp.simulate(0.35, HOURS_PER_DAY, True, True)
    LAST.update(reached=reached, events=events, st=st)
    hours = reached[tp.MAX_LEVEL]
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
    cday = cweek = 0.0
    streak = 0
    day_count = int(hours / HOURS_PER_DAY)
    for day in range(day_count):
        lv = level_at(day * HOURS_PER_DAY)
        if lv >= 5:
            dng += 3 * dungeon_gold(lv)
        if lv >= 15 and lv < tp.MAX_LEVEL:
            dly += 3 * daily_gold(lv)
            streak += 1
            cday += chest_day_gold(streak)
            if day % 7 == 0:
                wk += 3 * weekly_gold(lv)
                cweek += chest_week_gold()
        if lv >= 20 and day % 7 in (2, 5, 6):  # 수·토·일 해골왕
            raid += raid_gold("raid_skeleton_king")
    raid += raid_gold("raid_grah")
    # 필드 보스: 15분 창마다 가능하지만 보통 한 시간에 한 번 잡는다고 본다(처치 골드 400~700 평균)
    fb = [m for m in MONSTERS["monsters"] if m["id"].startswith("fboss_")]
    fboss = hours * FIELD_BOSS_PER_HOUR * sum((m["goldMin"] + m["goldMax"]) / 2 for m in fb) / len(fb)
    # 퀘스트 보상 골드(quest_index.json reward.gold): 청구한 퀘스트의 합. Lv40 도달 전에 받은 것만 체크포인트에 넣는다
    qgold = {q["id"]: q["reward"]["gold"] for q in tp.QUESTS}
    quest_gold_t = [(h, qgold.get(qid, 0) * QUEST_GOLD_SCALE) for h, _, _, qid, _, _ in events]
    quest = sum(g for _, g in quest_gold_t)
    inc.update({"요일 던전": dng, "일일 의뢰": dly, "주간 의뢰": wk, "일일 완료 상자": cday, "주간 완료 상자": cweek, "레이드": raid, "필드 보스": fboss, "퀘스트 골드": quest,
                "미니 던전 처치 골드": st["mini_kills"] * MINI_GOLD_PER_KILL})

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
            mat_sell += -short * SELL[m] * MAT_SELL_SCALE * MAT_SELL_SCALE_BY_MAT.get(m, 1.0)
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
        for ck in (10, 15, 20, 30, 40) + tuple(range(45, tp.MAX_LEVEL + 1, 5)):
            h = reached[ck]
            frac = h / hours
            # 그 시각까지의 처치 수(사냥 + 퀘스트 처치, 시뮬레이션 시각 기준)
            k_ck = sum(n for t, _, n in st["kills_t"] if t <= h)
            k_frac = min(1.0, k_ck / total_kills)
            ci = STARTER["gold"] + (inc["처치 골드"] + inc["장비 판매"] + inc["재료 판매(남는 것)"]) * k_frac + (dng + dly + wk + cday + cweek + fboss + inc["미니 던전 처치 골드"]) * frac + (raid * frac if ck >= 20 else 0) + sum(g for t, g in quest_gold_t if t <= h)
            cs = sum(g for lv, _, _, g, _, _ in rows if lv <= ck) + (potions + scrolls) * k_frac
            print(f"| Lv{ck} | {h:.1f}h | {ci:,.0f} | {cs:,.0f} | {ci / cs:.2f} |")
        print("\n| 단계 | 장비 | 목표 | 기대 골드 | 기대 시도 | 뼈/강화석/정수 |\n|---|---|---|---|---|---|")
        for lv, item, target, g, tries, mats in rows:
            print(f"| {lv} | {item} | +{target} | {g:,.0f} | {tries:.1f} | {mats['bone']:.0f}/{mats['ore']:.0f}/{mats['essence']:.0f} |")
    return ti, ts, inc, spend


# ---------------- 챕터 3 (--to 50) ----------------
# Lv50 한 벌: Lv40 한 벌(KIT 마지막 5줄)과 같은 부위·목표를 티어 10으로(PROGRESSION_AND_GEAR 5절 "Lv40 한 벌 x GoldByTier 비")
KIT50 = [(50, "eq_sword_50_e", 11), (50, "eq_plate_50_e", 10), (50, "eq_greaves_50_e", 10), (50, "eq_neck_50_r", 8), (50, "eq_ring_50_r", 8)]
INHERIT_PLUS = 12            # 계승하는 무기 강화(ROADMAP 3절 "계승 +12 무기")
EXCHANGES = 4                # 아카브 교환(주 2회 x 2주)
WORLD_GUILD_PER_WEEK = 80000  # 월드보스 참가 30,000 + 격파 30,000 + 길드보스 20,000(PLAN_GUILD_WORLDBOSS 3·4절, Lv40 입장)
WINDOW_DAYS = 60             # 장 공개부터 다음 장 공개까지(약 2달, ROADMAP 3절)
BAL40_SIM = 910000           # 옛 기준: 출시 20명 시뮬 Lv40 잔고 중앙값(BALANCE_MEASURE_2026-10-16 5절)
CH4_TARGET = (1329500, 1444100)  # PLAN_CHAPTER4 9.3절: 이 범위면 4장 비율 0.85~0.90
LIFE_RAIDS = {40: ("raid_skeleton_king", 3, "raid_grah", 1), 50: ("raid_xibak", 3, "raid_kamatz", 1)}


def seal_exchange_gold():
    import re
    try:
        with open(os.path.join(DATA, "..", "src", "domains", "shop", "sealExchange.ts"), encoding="utf-8") as f:
            return int(re.search(r"gold:\s*([\d_]+)", f.read()).group(1).replace("_", ""))
    except (OSError, AttributeError):
        return 20000


def max_level_day(level):
    """만렙(서버 상한)에서 하루 일상 골드: 요일 던전 3판 + 일일 의뢰 만렙 골드 + 완료 상자(일 평균 + 주/7) + 주간 의뢰/7 + 레이드/7 + 월드·길드보스/7."""
    r1, n1, r2, n2 = LIFE_RAIDS[level]
    return (3 * dungeon_gold(level) + DAILY["perDay"] * DAILY["maxLevelGold"] + DAILY["weekly"]["perWeek"] * DAILY["weekly"]["maxLevelGold"] / 7
            + chest_day_avg(True) + chest_week_gold(True) / 7
            + (n1 * raid_gold(r1) + n2 * raid_gold(r2)) / 7 + WORLD_GUILD_PER_WEEK / 7)


def chapter3(after_days=None, window=WINDOW_DAYS, verbose=True):
    import math
    import theory_inherit as ti
    max_level = tp.MAX_LEVEL
    # 1~40: 만렙 40 시뮬 그대로(챕터 3 시뮬도 c2_end 전까지는 40에 멈춰 같은 길을 간다)
    tp.MAX_LEVEL, tp.END_QUEST = tp.CH2_CAP, tp.CH2_END
    inc_a, sp_a, _, _ = budget(verbose=False)
    tp.MAX_LEVEL, tp.END_QUEST = max_level, "c3_end"
    tp.TRAVEL, tp.FIXED_KPM = 4.0, 15
    tp.MEASURED_KPM = tp.measured_kpm()
    reached, events, claimed, days, st = tp.simulate(0.35, HOURS_PER_DAY, True, True)
    t0, t1 = tp.ch3_start(reached, events), reached[max_level]
    hours = t1 - t0
    days_b = hours / HOURS_PER_DAY

    def level_at(h):
        return max([L for L, t in reached.items() if t <= h] or [1])

    # ---- 3장 수입 ----
    kills = {}
    for t, ml, n in st["kills_t"]:
        if t0 < t <= t1:
            kills[tier_of(ml)] = kills.get(tier_of(ml), 0) + n
    total_kills = sum(kills.values())
    inc = {"처치 골드": total_kills * base_pile(), "장비 판매": sum(n * gold_per_kill_gear(t) for t, n in kills.items())}
    drops = {m: total_kills * MAT[m]["dropChance"] * (MAT[m]["minDrop"] + MAT[m]["maxDrop"]) / 2 for m in MAT}
    dng = dly = wk = cday = cweek = 0.0
    for d in range(math.ceil(days_b)):
        frac = min(1.0, days_b - d)
        lv = level_at(t0 + d * HOURS_PER_DAY)
        dng += 3 * dungeon_gold(lv) * frac
        dly += 3 * daily_gold(lv) * frac
        cday += chest_day_gold(d + 1) * frac
        if d % 7 == 0:
            wk += 3 * weekly_gold(lv)
            cweek += chest_week_gold()
    raid = sum(raid_gold(rid) for t, rid in st["raid_t"] if t0 < t <= t1)
    fb = [m for m in MONSTERS["monsters"] if m["id"].startswith("fboss_")]
    fboss = hours * FIELD_BOSS_PER_HOUR * sum((m["goldMin"] + m["goldMax"]) / 2 for m in fb) / len(fb)
    qgold = {q["id"]: q["reward"]["gold"] for q in tp.QUESTS}
    quest = sum(qgold.get(qid, 0) * QUEST_GOLD_SCALE for h, _, _, qid, _, _ in events if t0 < h <= t1)
    inc.update({"요일 던전(군주)": dng, "일일 의뢰": dly, "주간 의뢰": wk, "일일 완료 상자": cday, "주간 완료 상자": cweek, "레이드": raid, "월드·길드보스": WORLD_GUILD_PER_WEEK * days_b / 7,
                "필드 보스": fboss, "퀘스트 골드": quest, "미니 던전 40 처치 골드": st.get("mini_kills_ch3", 0) * MINI_GOLD_PER_KILL})
    # ---- 3장 지출 ----
    enh_gold, need, rows = 0.0, {"bone": 0.0, "ore": 0.0, "essence": 0.0}, []
    for lv, item, target in KIT50:
        g, mats, tries = enhance_cost(item, target)
        enh_gold += g
        for k in need:
            need[k] += mats[k]
        rows.append((item, target, g, tries))
    kill_min = sum(m for t, m in st["kill_min_t"] if t0 < t <= t1)
    mat_buy = mat_sell = 0.0
    for m in MAT:
        short = need[m.replace("mat_", "")] - drops[m]
        if short > 0:
            mat_buy += short * BUY[m]
        else:
            mat_sell += -short * SELL[m] * MAT_SELL_SCALE * MAT_SELL_SCALE_BY_MAT.get(m, 1.0)
    inc["재료 판매(남는 것)"] = mat_sell
    spend = {"Lv50 한 벌 강화 골드": enh_gold, "재료 구매(모자란 것)": mat_buy, f"계승 +{INHERIT_PLUS} 무기": ti.inherit_gold(50, INHERIT_PLUS),
             f"아카브 교환 {EXCHANGES}회": EXCHANGES * seal_exchange_gold(),
             "물약": kill_min * POTIONS_PER_MIN * BUY["potion_hp"], "귀환 주문서": hours * SCROLLS_PER_HOUR * BUY["scroll_town"]}
    ib, sb = sum(inc.values()), sum(spend.values())
    # ---- 공개 시점 잔고 ----
    life40, life50 = max_level_day(tp.CH2_CAP), max_level_day(max_level)
    days_a = t0 / HOURS_PER_DAY
    after_a = max(0.0, window - days_a)
    after_b = max(0.0, window - days_b) if after_days is None else after_days
    bal40 = inc_a - sp_a
    open3 = bal40 + after_a * life40
    open4 = open3 + ib - sb + after_b * life50
    res = {"hours": hours, "days": days_b, "income": ib, "spend": sb, "bal40": bal40, "open3": open3, "open4": open4,
           "ratio": (open3 + ib) / sb, "ratio_old": (BAL40_SIM + ib) / sb, "life40": life40, "life50": life50, "after_a": after_a, "after_b": after_b}
    if verbose:
        print(f"## 챕터 3 골드 (Lv40+c2_end {t0:.1f}h -> Lv{max_level} {t1:.1f}h = {hours:.1f}시간, {days_b:.1f}일, 직업 {tp.CAREER or '4직업 합산'})")
        print("| 3장 수입 | 골드 |\n|---|---|")
        for k, v in inc.items():
            print(f"| {k} | {v:,.0f} |")
        print(f"| 합계 | {ib:,.0f} |\n\n| 3장 지출 | 골드 |\n|---|---|")
        for k, v in spend.items():
            print(f"| {k} | {v:,.0f} |")
        print(f"| 합계 | {sb:,.0f} |")
        print("\n| Lv50 한 벌 | 목표 | 기대 골드 | 기대 시도 |\n|---|---|---|---|")
        for item, target, g, tries in rows:
            print(f"| {item} | +{target} | {g:,.0f} | {tries:.1f} |")
        print(f"\n1~40: 수입 {inc_a:,.0f} / 지출 {sp_a:,.0f} = {inc_a / sp_a:.2f}, Lv40 잔고 {bal40:,.0f}")
        print(f"만렙 일상 하루: Lv40 {life40:,.0f}, Lv50 {life50:,.0f}")
        print(f"3장 공개 시점 잔고 = {bal40:,.0f} + {after_a:.1f}일 x {life40:,.0f} = {open3:,.0f}")
        print(f"3장 비율(공개 시점 기준) = ({open3:,.0f} + {ib:,.0f}) / {sb:,.0f} = {res['ratio']:.3f} (목표 {TARGET_RATIO[0]:.2f}~{TARGET_RATIO[1]:.2f})")
        print(f"3장 비율(옛 기준, Lv40 잔고 {BAL40_SIM:,}) = {res['ratio_old']:.3f}")
        print(f"4장 공개 시점 잔고 = {open3:,.0f} + {ib:,.0f} - {sb:,.0f} + {after_b:.1f}일 x {life50:,.0f} = {open4:,.0f} "
              f"(4장 0.85~0.90 범위 {CH4_TARGET[0]:,}~{CH4_TARGET[1]:,})")
    return res


if __name__ == "__main__":
    if "--mini" in sys.argv:
        i = sys.argv.index("--mini")
        tp.MINI_KPM = float(sys.argv[i + 1]) if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("--") else 20
    if "--search" in sys.argv:
        import itertools
        for gs, ms, es in itertools.product((1.0, 0.5, 0.3), (1.0, 0.5), (8, 10, 12, 15)):
            GEAR_SELL_SCALE, MAT_SELL_SCALE = gs, ms
            ti, ts, _, _ = budget(es, verbose=False)
            print(f"장비판매 x{gs} 재료판매 x{ms} 강화골드 x{es}: 수입 {ti:,.0f} / 지출 {ts:,.0f} = {ti / ts:.2f}")
    elif tp.MAX_LEVEL > tp.CH2_CAP:
        ad = float(sys.argv[sys.argv.index("--after-days") + 1]) if "--after-days" in sys.argv else None
        wd = float(sys.argv[sys.argv.index("--window-days") + 1]) if "--window-days" in sys.argv else WINDOW_DAYS
        chapter3(ad, wd)
    else:
        budget()
