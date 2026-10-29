"""
dotRPG 요일 던전 악몽·지옥·심연 이론 계산 (2026-10-29, Docs/PLAN_WEEKDAY_TIERS_2026-10-29.md). 게임을 돌리지 않는다.

질문: 영웅(Lv27, Lv25 장비)과 군주(Lv45, 3장) 사이에 요일 던전 난이도를 넣어 Lv30·35·40 장비가 요일 던전에서 나오게 할 때,
난이도 수치와 드롭을 어떻게 잡아야 (1) 단계별로 어렵고 (2) 그 레벨 구간 안에 클리어 장비를 맞출 수 있으며 (3) 무자본 골드 비율
0.85~0.90(ROADMAP 3절)과 Lv1~40 플레이 시간(약 46시간)을 지키고 (4) 레이드(레전더리의 유일한 출처)를 쓸모없게 만들지 않는가.

모델
1. 난이도 지수(theory_combat의 플레이어 모델): 시간 지수 = hpMul x (1 + 0.12 x 몬스터 레벨) / 권장 레벨 4직업 평균 무리 화력,
   피해 지수 = damageMul x (1 + 0.08 x 몬스터 레벨) / 권장 레벨 메이지 체력. 영웅(27)과 군주(45)의 지수를 권장 레벨로 선형 보간해
   hpMul·damageMul을 푼다(클리어 시간은 영웅과 같은 120~300초 창, 기준 시간도 영웅 것).
2. 클리어 경험치 하한(HuntingGrounds.DungeonClearFloor): 기준 레벨을 영웅과 같은 36으로 두면 1판 총 경험치(처치 + 클리어)가 영웅과 같다.
   theory_progress는 하한(클리어 몫)만 보므로 새 난이도를 고르지 않고, Lv40 도달 시간이 그대로인지 확인한다.
3. 판당 기대 드롭(서버 dungeonRules 규칙 그대로): 운 카드 2장, 카드 장비 확률 pg = 장비 가중치 / (표 합 + 보호권 가중치),
   장비 중 유니크 비율 u = rollGear(최소 에픽: 드롭표 12회 시도 + 에픽·유니크 같은 가중 3의 풀), 대박 pj = 천분율/1000,
   유니크 보너스 판당 rb = 1 / E[판 수] (22%, 8판째 보장: E = (1 - 0.78^8) / 0.22).
   E[에픽] = 2(1-pj) pg (1-u) - rb pg (1-u),  E[유니크] = 2(pj + (1-pj) pg u) + rb (1 - pg u)
4. 클리어 한 벌: 그 단계 5부위(무기·상의·하의·목걸이·반지) 모두 에픽 이상. 유니크 한 벌: 5부위 모두 유니크.
   서버 굴림 순서(카드 2장 -> 대박 -> 유니크 보너스가 없는 부위 먼저, 마지막 대박 아닌 카드를 바꿈)를 몬테카를로 20,000회(seed 고정)로 돌린다.
5. 구간: theory_gold와 같이 레벨 >= 권장 - 2 인 가장 높은 난이도를 하루 3판 한다. 구간 판 수 = 3 x (구간 시간 / 하루 2시간).
6. 레이드 비교: 그라흐(최종, 주 1회) 카드 4장의 유니크·레전더리 기대값과 재료 -> 레이드 상점 레전더리 무기(60개).
7. 골드: theory_gold.budget()의 무자본 비율(Lv40). 새 난이도를 뺀 옛 표와 비교하고, rewardMul 사다리 후보를 훑는다.

사용: python3 Tools/balance/theory_weekday_tiers.py          표 전부(1~40 골드 포함)
      python3 Tools/balance/theory_weekday_tiers.py --ch3    3장 골드 비율(--to 50 --mini 20)도 옛 표와 비교
"""
import contextlib
import copy
import io
import math
import os
import random
import sys

ARGS = sys.argv[1:]
sys.argv = [sys.argv[0]] + (["--to", "50"] if "--ch3" in ARGS else [])
sys.path.insert(0, os.path.dirname(__file__))
import theory_combat as tc  # noqa: E402
import theory_gold as tg  # noqa: E402
import theory_progress as tp  # noqa: E402

DNG = copy.deepcopy(tg.DNG)
DIFFS = DNG["difficulties"]
if len(DIFFS) != 8:
    sys.exit("dungeons.json에 악몽·지옥·심연이 없습니다(난이도 8개 필요). Unity GameDataExport로 다시 내보내세요.")
HERO, NIGHTMARE, INFERNO, ABYSS, LORD = 3, 4, 5, 6, 7
NEW = (NIGHTMARE, INFERNO, ABYSS)
WEEKDAY = [d for d in DNG["dungeons"] if not d["isRaid"] and not d.get("always")]
EQ = tg.SHOP["equipment"]
RARITY = ["Common", "Uncommon", "Rare", "Epic", "Unique", "Legendary"]
TIERS = [1, 10, 15, 20, 25, 30, 35, 40, 45, 50]
SLOTS = 5
LUCK = 2
BONUS_P, BONUS_PITY = 0.22, 8
MC_TRIALS, MC_SEED = 20000, 2026


def tier_of(level):
    return max(i for i, t in enumerate(TIERS) if t <= level)


# ---------------- 1. 난이도 지수 ----------------
def pack_dps(lv):
    rates = [tc.rotation(lv, c, 20.0)[1] / 20 for n, c in tc.CAREERS.items() if n in ("파이터", "비숍", "메이지", "수호자")]
    return sum(rates) / len(rates)


def mage_hp(lv):
    return tc.player(lv, tc.CAREERS["메이지"])[1]


def time_index(d):
    return d["hpMul"] * (1 + tc.HP_PER_LEVEL * d["monsterLevel"]) / pack_dps(d["recommendedLevel"])


def dmg_index(d):
    return d["damageMul"] * (1 + tc.DMG_PER_LEVEL * d["monsterLevel"]) / mage_hp(d["recommendedLevel"])


def solve_combat():
    h, l = DIFFS[HERO], DIFFS[LORD]
    th, tl, dh, dl = time_index(h), time_index(l), dmg_index(h), dmg_index(l)
    rows = []
    for i in NEW:
        d = DIFFS[i]
        f = (d["recommendedLevel"] - h["recommendedLevel"]) / (l["recommendedLevel"] - h["recommendedLevel"])
        hp = (th + (tl - th) * f) * pack_dps(d["recommendedLevel"]) / (1 + tc.HP_PER_LEVEL * d["monsterLevel"])
        dm = (dh + (dl - dh) * f) * mage_hp(d["recommendedLevel"]) / (1 + tc.DMG_PER_LEVEL * d["monsterLevel"])
        rows.append((i, hp, dm))
    return (th, tl, dh, dl), rows


# ---------------- 2. 경험치 ----------------
def run_total_xp(d, i):
    """1판 총 경험치 = 하한(클리어 몫, 랭크 보너스 앞) + 처치 경험치(HuntingGrounds.DungeonClearFloor의 kills)."""
    diff = DIFFS[i]
    kills = 0
    for room in d["rooms"]:
        for g in room["groups"]:
            m = tg.MONSTERS and next((x for x in tg.MONSTERS["monsters"] if x["id"] == g["monsterId"]), None)
            if not m or m["noLoot"]:
                continue
            kills += g["count"] * round(m["xp"] * (1 + 0.1 * (diff["monsterLevel"] + g["levelOffset"])))
    clear = max(d["clearXp"] * diff["rewardMul"], d["clearXpFloor"][i]) * d["xpMul"]
    return clear, kills


# ---------------- 3. 판당 기대 드롭 ----------------
def unique_share(tier, min_rarity, cls="warrior"):
    """rollGear에서 카드 장비가 유니크일 확률(서버 rareChances와 같은 식)."""
    def usable(e):
        c = e.get("classes") or ([e["classOnly"]] if e["classOnly"] else None)
        return c is None or cls in c
    u = [e for e in EQ if not e["bossOnly"] and e["levelTier"] == tier and e["rarity"] != "Legendary" and usable(e)]
    mi = RARITY.index(min_rarity)
    drop = [e for e in u if e["dropWeight"] > 0]
    total = sum(e["dropWeight"] for e in drop)
    q_ok = sum(e["dropWeight"] for e in drop if RARITY.index(e["rarity"]) >= mi) / total
    q_uni = sum(e["dropWeight"] for e in drop if e["rarity"] == "Unique") / total
    hit = 1 - (1 - q_ok) ** DNG["cards"]["gearDropTries"] if q_ok > 0 else 0
    from_drop = q_uni / q_ok * hit if q_ok > 0 else 0
    pool = [(e["rarity"], e["dropWeight"] or DNG["cards"]["gearRareWeight"]) for e in u if not e["starter"] and RARITY.index(e["rarity"]) >= mi]
    from_pool = sum(w for r, w in pool if r == "Unique") / sum(w for _, w in pool)
    return 0.5 * from_drop + (0.5 + 0.5 * (1 - hit)) * from_pool


def bonus_rate():
    return BONUS_P / (1 - (1 - BONUS_P) ** BONUS_PITY)


def card_numbers(d, diff):
    table = list(d["rewards"]) + ([{"itemId": "ticket_protect", "min": 1, "max": 1, "weight": diff["ticketWeight"]}] if diff["ticketWeight"] > 0 else [])
    total = sum(r["weight"] for r in table)
    pg = sum(r["weight"] for r in table if r["itemId"] == "gear") / total
    pt = diff["ticketWeight"] / total
    gold = sum((r["min"] + r["max"]) / 2 * diff["rewardMul"] * r["weight"] for r in table if r["itemId"] == "gold") / total
    return pg, pt, gold


def per_run(i):
    diff = DIFFS[i]
    tier = tier_of(diff["recommendedLevel"])
    u = unique_share(tier, diff["minGearRarity"])
    pj = diff["jackpotPerMille"] / 1000
    rb = bonus_rate()
    out = {"epic": 0.0, "unique": 0.0, "ticket": 0.0, "gold": 0.0, "mat": 0.0, "pg": 0.0}
    for d in WEEKDAY:
        pg, pt, card_gold = card_numbers(d, diff)
        e = 2 * (1 - pj) * pg * (1 - u) - rb * pg * (1 - u)
        un = 2 * (pj + (1 - pj) * pg * u) + rb * (1 - pg * u)
        s = d["sure"]
        out["epic"] += e
        out["unique"] += un
        out["ticket"] += 2 * (1 - pj) * pt
        out["gold"] += (s["goldMin"] + s["goldMax"]) / 2 * diff["rewardMul"] + 2 * (1 - pj) * card_gold
        out["mat"] += (s["materialMin"] + s["materialMax"]) / 2 * diff["rewardMul"]
        out["pg"] += pg
    n = len(WEEKDAY)
    out = {k: v / n for k, v in out.items()}
    out.update(tier=tier, u=u, pj=pj, rb=rb)
    return out


# ---------------- 4. 한 벌까지 판 수(몬테카를로) ----------------
def simulate_kit(i, trials=MC_TRIALS, seed=MC_SEED):
    diff = DIFFS[i]
    u = unique_share(tier_of(diff["recommendedLevel"]), diff["minGearRarity"])
    pj = diff["jackpotPerMille"] / 1000
    rng = random.Random(seed + i)
    pgs = [card_numbers(d, diff)[0] for d in WEEKDAY]
    kit_runs, set_runs = [], []
    for _ in range(trials):
        epic, uniq = [False] * SLOTS, [False] * SLOTS
        misses, n, kit_at = 0, 0, None
        while not all(uniq) and n < 400:
            n += 1
            pg = pgs[(n - 1) % len(pgs)]  # 요일마다 다른 던전(주말은 평균과 비슷)
            cards = []  # (kind, slot, jackpot)
            for _c in range(LUCK):
                kind, slot = None, None
                if rng.random() < pg:
                    kind, slot = ("U" if rng.random() < u else "E"), rng.randrange(SLOTS)
                jack = rng.random() < pj
                if jack:
                    kind, slot = "U", rng.randrange(SLOTS)
                cards.append((kind, slot, jack))
            hit = rng.random() < BONUS_P or misses + 1 >= BONUS_PITY
            if hit:
                missing = [s for s in range(SLOTS) if not uniq[s]] or list(range(SLOTS))
                bonus = (("U", rng.choice(missing), True))
                for k in range(LUCK - 1, -1, -1):
                    if not cards[k][2]:
                        cards[k] = bonus
                        break
                else:
                    cards.append(bonus)
                misses = 0
            else:
                misses += 1
            for kind, slot, _j in cards:
                if kind is None:
                    continue
                epic[slot] = True
                if kind == "U":
                    uniq[slot] = True
            if kit_at is None and all(epic):
                kit_at = n
        kit_runs.append(kit_at or n)
        set_runs.append(n)
    kit_runs.sort()
    set_runs.sort()
    q = lambda a, p: a[min(len(a) - 1, int(p * len(a)))]  # noqa: E731
    return {"kit_mean": sum(kit_runs) / trials, "kit_p50": q(kit_runs, .5), "kit_p90": q(kit_runs, .9),
            "set_mean": sum(set_runs) / trials, "set_p50": q(set_runs, .5), "set_p90": q(set_runs, .9), "kit_runs": kit_runs}


# ---------------- 5. 구간 ----------------
def windows(reached):
    """레벨 >= 권장 - 2 규칙으로 각 난이도를 하는 구간(시작 Lv, 끝 Lv, 시간)."""
    out = {}
    starts = [(i, DIFFS[i]["recommendedLevel"] - 2) for i in (HERO,) + NEW]
    for k, (i, lv) in enumerate(starts):
        end = starts[k + 1][1] if k + 1 < len(starts) else tp.CH2_CAP
        out[i] = (lv, end, reached.get(end, reached[max(reached)]) - reached[lv])
    return out


# ---------------- 6. 레이드 ----------------
def raid_week(rid="raid_grah"):
    d = next(x for x in DNG["dungeons"] if x["id"] == rid)
    rr = d["raidReward"]
    total = sum(c["weight"] for c in rr["cards"])
    pg = sum(c["weight"] for c in rr["cards"] if c["itemId"] == "gear") / total
    w = rr["gearRarityWeights"]
    s = sum(w.values())
    mats = sum((c["min"] + c["max"]) / 2 * c["weight"] for c in rr["cards"] if c["itemId"] == rr["materialItem"]) / total
    n = DNG["cards"]["count"]
    shop = next(p for p in tg.load("raid_shop.json")["products"] if p["raidId"] == rid and "Legendary" in p["rarityWeights"])
    return {"unique": n * pg * w.get("Unique", 0) / s, "legend": n * pg * w.get("Legendary", 0) / s, "mats": n * mats,
            "shop_price": shop["price"], "shop_legend": n * mats / shop["price"]}


# ---------------- 7. 골드 ----------------
def old_table():
    """새 난이도를 뺀 2026-10-28 표(군주가 4번)."""
    old = copy.deepcopy(DNG)
    old["difficulties"] = DIFFS[:4] + DIFFS[LORD:]
    old["minClearSeconds"] = DNG["minClearSeconds"][:5]
    for d in old["dungeons"]:
        if len(d["referenceSeconds"]) == 8:
            d["referenceSeconds"] = d["referenceSeconds"][:4] + d["referenceSeconds"][LORD:]
            d["clearXpFloor"] = d["clearXpFloor"][:4] + d["clearXpFloor"][LORD:]
    return old


def with_table(table, fn, cap40=True):
    """표를 바꿔 fn을 돌린다. cap40이면 만렙 40(c2_end)으로 시뮬한다(--ch3로 --to 50을 줬어도 1~40 계산은 40 기준)."""
    keep_g, keep_p, keep_lv = tg.DNG, tp.DNG, (tp.MAX_LEVEL, tp.END_QUEST)
    tg.DNG, tp.DNG = table, table
    if cap40:
        tp.MAX_LEVEL, tp.END_QUEST = tp.CH2_CAP, tp.CH2_END
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            return fn()
    finally:
        tg.DNG, tp.DNG = keep_g, keep_p
        tp.MAX_LEVEL, tp.END_QUEST = keep_lv


def gold40(table):
    def go():
        ti, ts, inc, _ = tg.budget(verbose=False)
        return ti / ts, inc["요일 던전"], tg.LAST["reached"][tp.CH2_CAP]
    return with_table(table, go)


def ladder(muls):
    t = copy.deepcopy(DNG)
    for i, m in zip(NEW, muls):
        t["difficulties"][i]["rewardMul"] = m
    return t


def main():
    print("## 1. 난이도 사다리 (dungeons.json)")
    print("| 번호 | 이름 | 권장Lv | 장비 단계 | 몬스터Lv | hpMul | damageMul | 권장 전투력 | rewardMul | 최소 등급 | 보호권 | 대박‰ | 부활 |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for i, d in enumerate(DIFFS):
        print(f"| {i} | {d['name']} | {d['recommendedLevel']} | Lv{TIERS[tier_of(d['recommendedLevel'])]} | {d['monsterLevel']} | {d['hpMul']} | {d['damageMul']} | "
              f"{d['recommendedPower']:,} | {d['rewardMul']} | {d['minGearRarity']} | {d['ticketWeight']} | {d['jackpotPerMille']} | {d['revives']} |")

    (th, tl, dh, dl), rows = solve_combat()
    print(f"\n## 2. 난이도 지수 보간 (영웅 시간 {th:.4f} 피해 {dh:.4f} -> 군주 시간 {tl:.4f} 피해 {dl:.4f})")
    print("| 난이도 | 권장Lv | 무리 화력 | 풀이 hpMul | 데이터 | 풀이 damageMul | 데이터 | 시간 지수 | 피해 지수 |\n|---|---|---|---|---|---|---|---|---|")
    for i, hp, dm in rows:
        d = DIFFS[i]
        print(f"| {d['name']} | {d['recommendedLevel']} | {pack_dps(d['recommendedLevel']):.0f} | {hp:.2f} | {d['hpMul']} | {dm:.3f} | {d['damageMul']} | {time_index(d):.4f} | {dmg_index(d):.4f} |")

    print("\n## 3. 1판 경험치 (황금 광맥, 랭크 보너스 앞): 하한 + 처치 = 총")
    gv = next(d for d in WEEKDAY if d["id"] == "gold_vein")
    print("| 난이도 | 클리어 몫 | 처치 | 총 |\n|---|---|---|---|")
    for i in (HERO,) + NEW:
        c, k = run_total_xp(gv, i)
        print(f"| {DIFFS[i]['name']} | {c:,.0f} | {k:,} | {c + k:,.0f} |")

    print(f"\n## 4. 판당 기대값 (요일 던전 5곳 평균, 전사, 유니크 보너스 판당 {bonus_rate():.4f})")
    print("| 난이도 | 장비 단계 | 카드 장비 pg | 장비 중 유니크 u | 에픽/판 | 유니크/판 | 보호권/판 | 골드/판 | 확정 재료/판 |\n|---|---|---|---|---|---|---|---|---|")
    pr = {}
    for i in (HERO,) + NEW + (LORD,):
        r = pr[i] = per_run(i)
        print(f"| {DIFFS[i]['name']} | Lv{TIERS[r['tier']]} | {r['pg']:.3f} | {r['u']:.3f} | {r['epic']:.3f} | {r['unique']:.3f} | {r['ticket']:.3f} | {r['gold']:,.0f} | {r['mat']:.1f} |")

    def go_reached():
        tp.TRAVEL, tp.FIXED_KPM = 4.0, 15
        tp.MEASURED_KPM = tp.measured_kpm()
        return tp.simulate(0.35, tg.HOURS_PER_DAY, True, True)[0]
    reached = with_table(DNG, go_reached)
    win = windows(reached)
    print(f"\n## 5. 한 벌까지 판 수 (몬테카를로 {MC_TRIALS:,}회) 와 그 난이도를 하는 구간(하루 3판, 하루 {tg.HOURS_PER_DAY:.0f}시간)")
    print("| 난이도 | 구간 | 구간 시간 | 구간 판 수 | 에픽+ 한 벌 평균/중앙/90% | 구간 안 완성 확률 | 유니크 한 벌 평균/중앙/90% |\n|---|---|---|---|---|---|---|")
    for i in (HERO,) + NEW:
        k = simulate_kit(i)
        lv0, lv1, h = win[i]
        runs = 3 * h / tg.HOURS_PER_DAY
        within = sum(1 for x in k["kit_runs"] if x <= runs) / len(k["kit_runs"])
        span = f"Lv{lv0}~{lv1}" if i != ABYSS else f"Lv{lv0}~40 + 만렙"
        print(f"| {DIFFS[i]['name']} | {span} | {h:.1f}h | {runs:.1f} | {k['kit_mean']:.1f} / {k['kit_p50']} / {k['kit_p90']} | {within:.0%} | "
              f"{k['set_mean']:.1f} / {k['set_p50']} / {k['set_p90']} |")

    r = raid_week()
    wk = 3 * 7
    print("\n## 6. Lv40 단계: 요일 던전 심연 vs 최종 레이드 그라흐 (주당)")
    print("| 출처 | 입장/주 | 유니크/주 | 레전더리/주 | 레이드 재료/주 | 상점 레전더리 무기/주 |\n|---|---|---|---|---|---|")
    print(f"| 심연 하루 3판 | {wk} | {wk * pr[ABYSS]['unique']:.2f} | 0 | 0 | 0 |")
    print(f"| 그라흐 보통 1회 | 1 | {r['unique']:.3f} | {r['legend']:.4f} | {r['mats']:.2f} | {r['shop_legend']:.3f} ({r['shop_price']}개) |")
    print(f"입장 1회당 유니크: 심연 {pr[ABYSS]['unique']:.3f}, 그라흐 {r['unique']:.3f}. 레전더리(카드·상점·승급 핵)는 레이드에서만 나온다.")

    print("\n## 7. 무자본 골드 비율 (Lv40, theory_gold) 과 Lv40 도달 시간")
    print("| 표 | rewardMul 악몽/지옥/심연 | 요일 던전 골드 | 비율 | Lv40 시간 |\n|---|---|---|---|---|")
    old = old_table()
    for name, t in [("옛 표(영웅까지)", old), ("이번 표", DNG)] + [(f"후보 {m}", ladder(m)) for m in ((3.7, 4.0, 4.3), (3.5, 3.6, 3.7), (3.4, 3.5, 3.7), (3.5, 3.5, 3.5))]:
        ratio, dg, h40 = gold40(t)
        muls = "-" if t is old else "/".join(str(t["difficulties"][i]["rewardMul"]) for i in NEW)
        print(f"| {name} | {muls} | {dg:,.0f} | {ratio:.4f} | {h40:.1f}h |")

    if "--ch3" in ARGS:
        tp.MINI_KPM = 20
        print("\n## 8. 3장 골드 비율 (--to 50 --mini 20)")
        for name, t in (("옛 표", old), ("이번 표", DNG)):
            res = with_table(t, lambda: tg.chapter3(verbose=False), cap40=False)
            print(f"- {name}: 3장 비율 {res['ratio']:.3f}, 3장 공개 시점 잔고 {res['open3']:,.0f}, Lv40 만렙 하루 {res['life40']:,.0f}")


if __name__ == "__main__":
    main()
