# -*- coding: utf-8 -*-
"""봉인된 상자(확률형)와 패스권의 기대 비용 계산 (Docs/PLAN_CASH_BOX_PASS.md).

별조각 300개 = 1,100원 기준. 상자 1회 = 별조각 100개.
부스터: 10회 열 때마다 다음 1회가 부스터(보상 수량 x2, 희귀 이상 확률 x2).
python3 Tools/balance/theory_cash.py
"""
import random

WON_PER_STAR = 1100 / 300
BOX_STARS = 100
BOOST_EVERY = 10

# (id, 기본 확률 %, 희귀 이상 여부) - 희귀 이상만 부스터에서 x2, 그만큼 일반 등급이 줄어든다.
TABLE = [
    ("potion_hi_x5", 36.158, False),     # 상급 물약 5개
    ("buff_power_30m", 27.624, False),   # 30분 공격 +15% 주문서
    ("essence_x10", 12.75, False),
    ("protect_x1", 8.5, False),
    ("box_stars", 4.6, False),         # 별조각 상자(sealed-2, 2026-10-29): 무료 별조각 100~3000, 기대 265
    ("protect_x3", 6.0, True),
    ("box10_30", 1.5, True),           # +10 강화권 상자(30%), sealed-1 4.5
    ("ticket10", 0.8, True),           # sealed-1 2.4
    ("box12_10", 1.2, True),           # +12 강화권 상자(10%)
    ("box12_50", 0.55, True),          # +12 강화권 상자(50%)
    ("ticket12", 0.2, True),
    ("ticket13", 0.08, True),
    ("box15_50", 0.03, True),          # +15 강화권 상자(50%)
    ("ticket15", 0.008, True),
]
assert abs(sum(r for _, r, _ in TABLE) - 100) < 1e-6, sum(r for _, r, _ in TABLE)

NESTED = {"box10_30": ("ticket10", .30), "box12_10": ("ticket12", .10), "box12_50": ("ticket12", .50), "box15_50": ("ticket15", .50)}


def rates(boost):
    if not boost:
        return [(i, r) for i, r, _ in TABLE]
    rare = sum(r for _, r, h in TABLE if h)
    common = 100 - rare
    scale = (common - rare) / common  # 희귀 이상을 x2 하고 일반을 비례 축소
    return [(i, r * 2 if h else r * scale) for i, r, h in TABLE]


def p_of(target, boost):
    p = 0.0
    for i, r in rates(boost):
        r /= 100
        if i == target:
            p += r * (2 if boost else 1)  # 부스터는 수량도 x2
        elif i in NESTED and NESTED[i][0] == target:
            p += r * NESTED[i][1] * (2 if boost else 1)
    return p


def per_box(target):
    return (p_of(target, False) * BOOST_EVERY + p_of(target, True)) / (BOOST_EVERY + 1)


# 별조각 상자 1개를 열면 나오는 무료 별조각 (수량, 확률 %), server/data/sealed_box.json starBox
STAR_BOX = [(100, 40), (200, 30), (300, 15), (500, 10), (1000, 4), (3000, 1)]
STAR_BOX_EV = sum(s * r / 100 for s, r in STAR_BOX)


def stars_per_box():
    """상자 1회(부스터 포함 평균)에서 돌아오는 무료 별조각 기대값"""
    def p(boost):
        return sum(r / 100 * (2 if boost else 1) for i, r in rates(boost) if i == "box_stars")
    return (p(False) * BOOST_EVERY + p(True)) / (BOOST_EVERY + 1) * STAR_BOX_EV


def won(boxes):
    return boxes * BOX_STARS * WON_PER_STAR


if __name__ == "__main__":
    print("상자 1회 = %d원 (별조각 %d)" % (won(1), BOX_STARS))
    rare = sum(r for _, r, h in TABLE if h)
    print("희귀 이상 등급 합: 기본 %.2f%% / 부스터 %.2f%%" % (rare, rare * 2))
    sp = stars_per_box()
    print("별조각 상자: 1개 기대 %.1f개, 상자 1회당 무료 별조각 %.2f개 = 가격의 %.1f%%" % (STAR_BOX_EV, sp, sp / BOX_STARS * 100))
    for t in ("ticket10", "ticket12", "ticket13", "ticket15"):
        p = per_box(t)
        print("%-9s 상자당 기대 %.4f%%  ->  평균 %6.0f회, 약 %s원" % (t, p * 100, 1 / p, format(int(won(1 / p)), ",")))
    # 최고점: 6부위 +15 (강화권만으로) 기대 비용
    p15 = per_box("ticket15")
    print("최고점(6부위 +15) 기대 비용 약 %s원" % format(int(won(6 / p15)), ","))
    # 편차: 3,000회(약 110만원) 열었을 때 +15 장수 분포
    random.seed(1)
    runs = []
    for _ in range(2000):
        n15 = 0
        for k in range(3000):
            boost = (k + 1) % (BOOST_EVERY + 1) == 0
            mult = 2 if boost else 1
            x = random.random() * 100
            for i, r in rates(boost):
                x -= r
                if x <= 0:
                    if i == "ticket15": n15 += mult
                    elif i == "box15_50" and random.random() < .5: n15 += mult
                    break
        runs.append(n15)
    runs.sort()
    print("3,000회 열 때 +15 강화권: 0장 %.0f%%, 1장 이상 %.0f%%, 상위 10%%는 %d장 이상" % (
        100 * runs.count(0) / len(runs), 100 * sum(1 for r in runs if r >= 1) / len(runs), runs[int(len(runs) * .9)]))
