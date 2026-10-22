"""
dotRPG 장비 계승 이론 계산 (2026-10-22, Docs/PLAN_GEAR_INHERIT_2026-10-22.md 2절 표를 다시 낸다). 게임을 돌리지 않는다.

규칙(C# InheritRules, 서버 enhance.json inheritTiers와 같다)
- 경로 Lv10 -> 20 -> 30 -> 40 (-> 50은 챕터 3). 유지 확률은 계승하는 강화 단계 N 기준, 실패하면 -1.
- 이미 계승한 횟수 k가 1 이상이면 결과에서 2단계를 더 깎는다(k=0은 0).
- 골드 = 0.25 x 새 레벨대 무기 강화 기본 골드 x (단위(N) - 단위(10)), N <= 10이면 0.
  단위(N) = sum(i<N) GoldMul[i] x 100 / Chance[i] (실패 하락·파괴는 빼고 시도 횟수만 센 하한).

표·상수는 C# 소스(EnhanceRules.cs, InheritRules.cs)에서 읽는다. Lv50 기본 골드 10,000은 챕터 3 설계값(이 브랜치 표에는 없다).
시뮬레이션은 시드 고정(SEED)이라 실행할 때마다 같은 결과가 나온다.

사용: python3 Tools/balance/theory_inherit.py [시행 횟수, 기본 60000]
"""
import json
import os
import random
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Scripts", "Runtime", "Items")
DATA = os.path.join(ROOT, "server", "data")
SEED = 20261022
LV50_BASE = 10000  # 챕터 3 설계값


def _array(text, name):
    m = re.search(name + r"\s*=\s*\{([^}]*)\}", text)
    return [float(x) for x in m.group(1).split(",") if x.strip()]


with open(os.path.join(SRC, "EnhanceRules.cs"), encoding="utf-8") as f:
    _enh = f.read()
with open(os.path.join(SRC, "InheritRules.cs"), encoding="utf-8") as f:
    _inh = f.read()

CHANCE = _array(_enh, "Chance")
GOLD_MUL = _array(_enh, "GoldMul")
GOLD_BY_TIER = _array(_enh, "GoldByTier")  # Lv1, 10, 15, 20, 25, 30, 35, 40
KEEP = [int(x) for x in _array(_inh, "KeepTable")]
PENALTY = [int(x) for x in _array(_inh, "PenaltyTable")]
FAIL_DROP = int(re.search(r"FailDrop\s*=\s*(\d+)", _inh).group(1))
SHARE = float(re.search(r"CostShare\s*=\s*([\d.]+)", _inh).group(1))
FREE = int(re.search(r"FreeLevel\s*=\s*(\d+)", _inh).group(1))

BASE = {10: GOLD_BY_TIER[1], 20: GOLD_BY_TIER[3], 30: GOLD_BY_TIER[5], 40: GOLD_BY_TIER[7], 50: LV50_BASE}
NEXT = {10: 20, 20: 30, 30: 40, 40: 50}


def units(n):
    return sum(GOLD_MUL[i] * 100.0 / CHANCE[i] for i in range(n))


def round_away(x):
    return int(x + 0.5)


def inherit_gold(to_level, n):
    return 0 if n <= FREE else round_away(SHARE * BASE[to_level] * (units(n) - units(FREE)))


def penalty(k):
    return PENALTY[min(k, len(PENALTY) - 1)]


def result_level(n, k, kept):
    return max(0, (n if kept else n - FAIL_DROP) - penalty(k))


def chain(rng, level, n, steps, k=0):
    """level 장비 +n을 steps번 계승: (최종 단계, 계승 골드 합)"""
    gold = 0
    for _ in range(steps):
        to = NEXT[level]
        gold += inherit_gold(to, n)
        kept = rng.randrange(100) < KEEP[min(n, len(KEEP) - 1)]
        n = result_level(n, k, kept)
        k += 1
        level = to
    return n, gold


def simulate(level, n, steps, runs):
    rng = random.Random(SEED)
    out = [chain(rng, level, n, steps) for _ in range(runs)]
    finals = [x[0] for x in out]
    dist = {}
    for f in finals:
        dist[f] = dist.get(f, 0) + 1
    mode = max(dist, key=lambda v: (dist[v], v))
    return {
        "best": max(finals),
        "mode": mode,
        "mean": sum(finals) / runs,
        "gold": sum(x[1] for x in out) / runs,
        "dist": {v: dist[v] / runs for v in sorted(dist, reverse=True)},
    }


def man(x):
    return f"{x / 10000:,.0f}만"


def check_export():
    """server/data/enhance.json inheritTiers가 이 계산과 같은지(내보내기가 최신인지) 확인"""
    path = os.path.join(DATA, "enhance.json")
    try:
        with open(path, encoding="utf-8") as f:
            tiers = json.load(f).get("inheritTiers")
    except OSError:
        return "enhance.json 없음"
    if not tiers:
        return "enhance.json에 inheritTiers 없음(내보내기 필요)"
    bad = 0
    for row in tiers["rows"]:
        to = int(row["to"].split("_")[2])
        weapon = row["to"].split("_")[1] in ("sword", "staff")
        for n, g in enumerate(row["gold"]):
            want = 0 if n <= FREE else round_away(SHARE * BASE[to] * (1.0 if weapon else 0.8) * (units(n) - units(FREE)))
            if want != g:
                bad += 1
    if tiers["keepPercent"] != KEEP or tiers["stackPenalty"] != PENALTY:
        bad += 1
    return f"enhance.json inheritTiers {len(tiers['rows'])}줄, 불일치 {bad}"


def main():
    runs = int(sys.argv[1]) if len(sys.argv) > 1 else 60000
    print(f"장비 계승 이론 계산 (시뮬레이션 {runs:,}번, 시드 {SEED})")
    print(f"유지 확률 {KEEP[11:]} (+11~+20), 누적 깎임 {PENALTY}, 실패 -{FAIL_DROP}")
    print(check_export())
    print()

    a = simulate(10, 18, 4, runs)
    enh = units(18) * BASE[10]
    print(f"Lv10 +18 -> Lv50 네 번: 최고 +{a['best']}, 보통 +{a['mode']}, 평균 +{a['mean']:.1f}")
    print(f"  골드: 강화 {man(enh)} + 계승 {man(a['gold'])} = {man(enh + a['gold'])}, Lv50 +10 정상 강화 {man(units(10) * BASE[50])}")
    for n in (12, 15, 18):
        r = simulate(40, n, 1, runs)
        dist = ", ".join(f"{p * 100:.0f}% +{v}" for v, p in r["dist"].items())
        print(f"처음 계승하는 Lv40 +{n} -> Lv50: {dist} | 계승 {man(inherit_gold(50, n))} (새로 +{n}은 {man(units(n) * BASE[50])})")
    b = simulate(30, 15, 2, runs)
    print(f"Lv30 +15 -> Lv50 두 번: 평균 +{b['mean']:.1f} | 계승 합 {man(b['gold'])}")
    print()
    print("이 브랜치(출시용, Lv40까지) 경로")
    for lv, n in ((10, 12), (10, 15), (20, 12), (20, 15), (30, 12), (30, 15)):
        to = NEXT[lv]
        print(f"  Lv{lv} +{n} -> Lv{to}: 유지 {KEEP[n]}% +{n} / 실패 +{n - FAIL_DROP}, 계승 {inherit_gold(to, n):,}골드 (Lv{to} 새로 +{n}은 {round_away(units(n) * BASE[to]):,})")


if __name__ == "__main__":
    main()
