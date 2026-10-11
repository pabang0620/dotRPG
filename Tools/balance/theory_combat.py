"""
dotRPG 전투 이론 계산 (게임을 돌리지 않는 밸런스 점검, 2026-10-11).

사냥터(HuntingGrounds) 대역마다 전직 4종의 지속 화력·처치 시간·무리 정리 시간·버티는 시간을 계산하고 목표와 비교한다.
수치는 코드와 데이터에서 읽는다: CareerData.cs S(...) 줄(+ S()의 보정 규칙), MonsterDatabase.cs, HuntingGrounds.cs,
server/data/shop.json(장비), SkillData.cs(HP·MP 식). 결과는 Docs/BALANCE_COMBAT_2026-10-11.md 에 옮긴다.

목표 (너무 쉬우면 안 된다)
- T1 같은 레벨 일반 몬스터 1마리, 집중 공격으로 2.0~3.5초 (한 방이 아니라 연사기 4~8번)
- T2 같은 레벨 3마리 무리 정리 4~7초(평균 5초): 이동 4초를 더하면 분당 15~20마리 = theory_progress 의 40시간 설계 기준(분당 15)
- T3 같은 레벨 무리 3마리 한가운데서 물약 없이 버티는 시간: 파이터 10~16초, 수호자 25초 이상, 메이지·비숍 6~10초
     (물약·회피 없이 서 있는 최악의 경우. 실제 전투는 무리 정리 시간 안에 끝나야 하므로 T3 > T2 이면 물약 없이도 사냥 가능,
      T3 가 T2 의 1.0~1.6배면 "가끔 물약", 1.0 미만이면 "물약 필수")
- T4 10레벨 높은 캐릭터(장비·패시브가 그만큼 좋은)로 옛 사냥터 몬스터 1마리 1.2초 이하(연사기 2~3번)

가정 (이름 있는 값)
- 장비: 그 레벨 단계의 희귀(Rare) 무기 +5, 목걸이·반지·상의·하의 희귀. 패시브는 포인트 절반을 피해 증가(점당 5.5%)에.
- 전직 기술은 추천 배치 키, 연사기와 주력 쿨기 3단계(위력 x1.24). 버프는 60초라 상시 유지로 본다.
- MP: 무리 하나(정리 시간 + 이동 4초) 동안의 회복량만큼 연사기를 쓰고 나머지 시간은 기본 공격(장기 사냥의 정상 상태).
- 무리 3마리는 붙어 있다: 연사기 평균 타격 수 = SPLASH 표.
- 받는 피해: 근접은 (windup+recover)x2 초마다, 원거리는 skillInterval x2 초마다 1회(MonsterDatabase.AttackPace 2).
  모든 직업 기본 감소 10%(GuardPassive), 수호자 보루 30% + 방진 40% + 강철의 심장 10% (최대 65%).

사용: python3 Tools/balance/theory_combat.py [--hp-per-level X] [--dmg-per-level Y] [--hp-mul Z] [--dmg-mul W]
"""
import json
import math
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
RT = os.path.join(ROOT, "Assets", "Scripts", "Runtime")


def read(*p):
    with open(os.path.join(*p), encoding="utf-8-sig") as f:
        return f.read()


def arg(name, default):
    if name in sys.argv:
        return float(sys.argv[sys.argv.index(name) + 1])
    return default


# ---------------- monsters ----------------
MDB = read(RT, "Enemies", "MonsterDatabase.cs")
HP_PER_LEVEL = arg("--hp-per-level", float(re.search(r"HpPerLevel = ([0-9.]+)f", MDB).group(1)))
DMG_PER_LEVEL = arg("--dmg-per-level", float(re.search(r"DamagePerLevel = ([0-9.]+)f", MDB).group(1)))
HP_MUL, DMG_MUL = arg("--hp-mul", 1.0), arg("--dmg-mul", 1.0)
# Field-only multipliers (MonsterDatabase.FieldHpBase/FieldHpSlope/FieldDamageMul). --before turns them off.
BEFORE = "--before" in sys.argv
FIELD_HP_BASE = 1.0 if BEFORE else float(re.search(r"FieldHpBase = ([0-9.]+)f", MDB).group(1))
FIELD_HP_SLOPE = HP_PER_LEVEL if BEFORE else float(re.search(r"FieldHpSlope = ([0-9.]+)f", MDB).group(1))
FIELD_DMG_MAX = 1.0 if BEFORE else float(re.search(r"FieldDamageMax = ([0-9.]+)f", MDB).group(1))


def field_dmg(lv):
    return 1 + (FIELD_DMG_MAX - 1) * min(1.0, max(0.0, (lv - 1) / 14))
ATTACK_PACE = 2.0

MON = {}
for m in re.finditer(r'id = ("[a-z_]+"|Warrior)(.{0,700}?)\}', MDB, re.S):
    mid = "skel_warrior" if m.group(1) == "Warrior" else m.group(1).strip('"')
    body = m.group(2)

    def g(k, d):
        x = re.search(r"\b" + k + r"\s*=\s*([0-9.]+)f?", body)
        return float(x.group(1)) if x else d
    if "boss = true" in body:
        continue
    MON[mid] = dict(hp=g("hp", 30), dmg=g("damage", 10), wind=g("windup", .5), rec=g("recover", .7),
                    skill=g("skillInterval", 0), ranged="keepDistance" in body or mid in ("skel_archer", "skel_necro", "hollow_hexer"))
MON["skeleton"] = MON["skel_warrior"]


def mon_hp(mid, lv):
    return MON[mid]["hp"] * HP_MUL * FIELD_HP_BASE * (1 + FIELD_HP_SLOPE * (lv - 1))


def mon_dps(mid, lv):
    m = MON[mid]
    dmg = m["dmg"] * DMG_MUL * field_dmg(lv) * (1 + DMG_PER_LEVEL * (lv - 1))
    cycle = (m["skill"] if m["skill"] > 0 else m["wind"] + m["rec"]) * ATTACK_PACE
    return dmg / max(.5, cycle) if dmg > 0 else 0.0


# ---------------- zones ----------------
ZONES = []
for z in re.finditer(r'new HuntingZone\("(\w+)", "([^"]+)", [^,]+, MapTheme\.\w+, (\d+), (\d+), (\d+), \d+, ([^)]*)\)', read(RT, "World", "HuntingGrounds.cs")):
    ZONES.append(dict(id=z.group(1), name=z.group(2), lo=int(z.group(3)), hi=int(z.group(4)), mlv=int(z.group(5)),
                      mons=[x.strip().strip('"') for x in z.group(6).split(",")]))

# ---------------- gear ----------------
SHOP = json.load(open(os.path.join(ROOT, "server", "data", "shop.json"), encoding="utf-8"))
TIER_LEVELS = [1, 10, 15, 20, 25, 30, 35, 40]
ENH = [0, 1.1, 2.2, 3.3, 4.5, 5.7]


def tier_of(lv):
    return max(i for i, l in enumerate(TIER_LEVELS) if l <= lv)


def gear(lv, rarity="Rare", plus=5):
    t = tier_of(lv)
    best = {}
    for e in SHOP["equipment"]:
        if e["starter"] or e["bossOnly"] or e["levelTier"] != t or e["rarity"] != rarity:
            continue
        c = e["category"]
        a, h = e["attack"], e["maxHealth"]
        if c not in best or (a, h) > best[c]:
            best[c] = (a, h)
    atk = sum(v[0] for v in best.values()) + round(.135 * (t + 1) * ENH[plus])
    hp = sum(v[1] for v in best.values())
    return atk, hp


# ---------------- career skills ----------------
CD = read(RT, "Progression", "CareerData.cs")
HEAL, BUFF = {"heal", "bloom", "cleanse"}, {"guard", "oath", "bless", "counter", "frenzy"}
SK = {}
for m in re.finditer(r'S\(([fgmb]),(\d+),"(\w+)","([^"]+)","[^"]*","(\w+)",([^)]*)\)', CD):
    nums = [x.strip().rstrip("f") for x in m.group(6).split(",")]
    i, eff = int(m.group(2)), m.group(5)
    if i in (0, 4):
        continue
    power, mp, cd, rng, rad, cast = (float(nums[k]) for k in range(6))
    hits = int(nums[7]) if len(nums) > 7 and nums[7].isdigit() else 1
    awaken = i == 8
    mp = max(1, round(mp * (.75 if awaken else .65)))
    signature = cd <= 0 or eff == "frenzy"
    if not signature:
        cd = max(.5, round(cd * (1.25 if awaken else 1.6 if eff in ("guard", "oath", "bless", "counter") else .7) * 2) / 2)
    if eff in HEAL:
        power *= .8
    elif eff not in BUFF and not awaken:
        power *= .85
        if cd > 0:
            power *= min(2.5, 1 + .15 * cd)
    SK[m.group(3)] = dict(eff=eff, power=power, mp=mp, cd=cd, cast=cast, hits=hits, rad=rad * (1.3 if awaken else 1.5))

RECOVERY = {"cross": .1, "light": .1, "heal": .1, "guard": .1, "blink": .1, "frenzy": .1, "nebula": .1, "sanctuary": .1,
            "break": .15, "fire": .15, "ice": .15, "storm": .15, "orbit": .15, "taunt": .15, "flurry": .7, "iaido": .22,
            "bash": .22, "shieldthrow": .14, "rush": .25, "execute": .32}
RANK3 = 1.24

# spam skill, its single-target hits per cast, average targets in a 3-pack, extra cooldown skills on keys
CAREERS = {
    "파이터": dict(cls="w", hpmul=1.1, mpcls="w", spam="f_break", spam_hits=1, splash=2.0,
                 cds=["f_cross", "f_rush", "f_focus"], cd_hits={"f_cross": 2}, cd_splash=1.6, more=1.3 * 1.15, dr=0),
    "수호자": dict(cls="w", hpmul=1.3, mpcls="w", spam="g_wall", spam_hits=2, splash=2.0,
                 cds=["g_taunt", "g_bash"], cd_hits={}, cd_splash=2.0, more=1.0, dr=65),
    "메이지": dict(cls="m", hpmul=1.0, mpcls="m", spam="m_fire", spam_hits=1, splash=2.6,
                 cds=["m_ice", "m_orbit"], cd_hits={"m_ice": 1, "m_orbit": 2.2}, cd_splash=2.4, more=1.0, dr=0),
    "비숍": dict(cls="m", hpmul=1.05, mpcls="m", spam="b_light", spam_hits=1, splash=2.0,
                cds=[], cd_hits={}, cd_splash=1.0, more=1.18, dr=0),
}
TRAVEL = 4.0
# EnemyController.LevelGapMul: player damage on monsters this many levels below (+15%/level beyond 3, max x3). --before = off.
def gap_mul(gap):
    return 1.0 if BEFORE else min(3.0, 1 + .15 * max(0, gap - 3))


def player(lv, c):
    atk, ghp = gear(lv)
    inc = (lv - 1) / 2 * 5.5 / 100 + (.05 if c["cls"] == "w" else 0)
    A = (10 + atk) * (1 + inc)
    warrior = c["cls"] == "w"
    hp = (60 + (50 if warrior else 0) + (lv - 1) * (8 + (6 if warrior else 0)) + ghp) * c["hpmul"]
    mp = (40 + (10 if warrior else 40) + (lv - 1) * (4 + (0 if warrior else 3))) * ({"비숍": 1.25, "메이지": 1.2}.get(next(k for k, v in CAREERS.items() if v is c), 1))
    return A, hp, mp


def rotation(lv, c, window):
    """Damage done to ONE target and to the pack in `window` seconds, MP-limited (regen over the window + travel)."""
    A, hp, mp = player(lv, c)
    regen = mp * .05
    s = SK[c["spam"]]
    cycle = s["cast"] + RECOVERY.get(s["eff"], .15) + .08 - (.08 if s["eff"] == "shieldthrow" else 0)
    per_cast = A * s["power"] * RANK3 * c["more"] * c["spam_hits"]
    single = pack = 0.0
    busy = 0.0
    budget = regen * (window + TRAVEL)
    for cid in c["cds"]:
        k = SK[cid]
        n = window / k["cd"]
        busy += n * (k["cast"] + RECOVERY.get(k["eff"], .15) + .08)
        budget -= n * k["mp"]
        d = n * A * k["power"] * RANK3 * c["more"] * c["cd_hits"].get(cid, 1)
        single += d
        pack += d * c["cd_splash"]
    left = max(0.0, window - busy)
    casts = min(left / cycle, max(0.0, budget) / s["mp"])
    single += casts * per_cast
    pack += casts * per_cast * c["splash"]
    basic_time = left - casts * cycle
    basic = A * .8 / (.3 if c["cls"] == "w" else .38) * basic_time * (1.0 if c is not CAREERS["비숍"] else 1)
    single += basic
    pack += basic * (1.3 if c["cls"] == "w" else 1.0)
    return single, pack


def solve_clear(lv, c, pack_hp):
    """Seconds to clear `pack_hp` with the rotation (bisection on the window)."""
    lo, hi = .1, 120.0
    for _ in range(40):
        mid = (lo + hi) / 2
        if rotation(lv, c, mid)[1] >= pack_hp:
            hi = mid
        else:
            lo = mid
    return hi


def pre_clear(lv, hp):
    """Before the Lv15 career: theory_progress pack_rate (basic attack x1.5 splash + 1.0 per open skill slot)."""
    slots = sum(1 for s in (2, 6, 12, 18) if lv >= s)
    A = player(lv, CAREERS["파이터"])[0] / 1.05
    return hp / (A * (2.5 * 1.5 + slots))


def summary():
    print("\n## 요약 (같은 레벨 사냥터, 직업 4종 기하평균)")
    print("| 사냥터 | 몹Lv | 무리 정리(초) | 1마리(초) | 파이터 버팀(초) | 메이지 버팀(초) | 수호자 버팀(초) | +10Lv 캐릭터 1마리(초) |")
    print("|---|---|---|---|---|---|---|---|")
    for z in ZONES:
        lv = z["mlv"]
        hps = [mon_hp(m, lv) for m in z["mons"]]
        incoming = sum(mon_dps(m, lv) for m in z["mons"]) * .9
        if lv < 15:
            clear = pre_clear(lv, sum(hps))
            one = clear / 3
            low = pre_clear(lv + 10, sum(hps)) / 3 / gap_mul(10)
            def base_hp(warrior):
                atk, ghp = gear(lv)
                return 60 + (50 if warrior else 0) + (lv - 1) * (8 + (6 if warrior else 0)) + ghp
            surv = [f"{base_hp(True) / incoming:.0f}(전사)", f"{base_hp(False) / incoming:.0f}(마법사)", "-"]
        else:
            cl, ones, lows = [], [], []
            for c in CAREERS.values():
                cl.append(solve_clear(lv, c, sum(hps)))
                d = rotation(lv, c, 3.0)[0] / 3.0
                ones.append(sum(hps) / 3 / d)
                lows.append(sum(hps) / 3 / (rotation(lv + 10, c, 3.0)[0] / 3.0) / gap_mul(10))
            geo = lambda v: math.exp(sum(map(math.log, v)) / len(v))
            clear, one, low = geo(cl), geo(ones), geo(lows)
            surv = [f"{player(lv, CAREERS[n])[1] / (incoming * (1 - CAREERS[n]['dr'] / 100)):.0f}" for n in ("파이터", "메이지", "수호자")]
        print(f"| {z['name']} | {lv} | {clear:.1f} | {one:.1f} | {surv[0]} | {surv[1]} | {surv[2]} | {low:.2f} |")


def report():
    print(f"HpPerLevel {HP_PER_LEVEL}  DamagePerLevel {DMG_PER_LEVEL}  hp x{HP_MUL}  dmg x{DMG_MUL}\n")
    print("| 사냥터 | 몹Lv | 직업 | 1마리(초) | 무리 정리(초) | 버팀(초) | 버팀/정리 | +10Lv 캐릭터 1마리(초) |")
    print("|---|---|---|---|---|---|---|---|")
    rows = []
    for z in ZONES:
        if z["mlv"] < 15:
            continue
        lv = z["mlv"]
        hps = [mon_hp(m, z["mlv"]) for m in z["mons"]]
        incoming = sum(mon_dps(m, z["mlv"]) for m in z["mons"])
        for name, c in CAREERS.items():
            A, hp, mp = player(lv, c)
            one_window = 3.0
            single3 = rotation(lv, c, one_window)[0] / one_window
            ttk = (sum(hps) / len(hps)) / single3
            clear = solve_clear(lv, c, sum(hps))
            taken = incoming * .9 * (1 - c["dr"] / 100)
            survive = hp / taken if taken > 0 else 999
            low = (sum(hps) / len(hps)) / (rotation(lv + 10, c, one_window)[0] / one_window) / gap_mul(10)
            rows.append((z, name, ttk, clear, survive))
            print(f"| {z['name']} | {z['mlv']} | {name} | {ttk:.1f} | {clear:.1f} | {survive:.0f} | {survive / clear:.1f} | {low:.2f} |")
    return rows


if __name__ == "__main__":
    if "--detail" in sys.argv:
        report()
    summary()
