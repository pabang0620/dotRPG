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
    MON[mid] = dict(hp=g("hp", 30), dmg=g("damage", 10), wind=g("windup", .5), rec=g("recover", .7), armor=g("armor", 0),
                    skill=g("skillInterval", 0), ranged="keepDistance" in body or mid in ("skel_archer", "skel_necro", "hollow_hexer"))
MON["skeleton"] = MON["skel_warrior"]


SANCTUM_MUL = 1.0 if BEFORE else float((re.search(r"SanctumFieldHpMul = ([0-9.]+)f", MDB) or re.search("(1)", "1")).group(1))


def zone_mul(z):
    """[2026-10-12] MonsterDatabase.SanctumFieldHpMul on the sanctum hunting grounds."""
    return SANCTUM_MUL if z.get("theme") == "SanctumField" else 1.0


# [ARMOR 2026-10-12] Armour cuts direct hits; burns and zones go through it. Modelled as more effective HP, with
# DIRECT_SHARE of a class's damage being direct hits (the rest damage over time). --no-armor shows the old numbers.
ARMOR = "--no-armor" not in sys.argv
DIRECT_SHARE = .85


def mon_hp(mid, lv):
    hp = MON[mid]["hp"] * HP_MUL * FIELD_HP_BASE * (1 + FIELD_HP_SLOPE * (lv - 1))
    a = MON[mid].get("armor", 0) if ARMOR and lv >= 15 else 0  # EnemyController.ArmorFromLevel
    return hp / (1 - a * DIRECT_SHARE)


def mon_dps(mid, lv):
    m = MON[mid]
    dmg = m["dmg"] * DMG_MUL * field_dmg(lv) * (1 + DMG_PER_LEVEL * (lv - 1))
    cycle = (m["skill"] if m["skill"] > 0 else m["wind"] + m["rec"]) * ATTACK_PACE
    return dmg / max(.5, cycle) if dmg > 0 else 0.0


# ---------------- zones ----------------
ZONES = []
for z in re.finditer(r'new HuntingZone\("(\w+)", (?:L\.T\()?"([^"]+)"\)?, [^,]+, MapTheme\.(\w+), (\d+), (\d+), (\d+), \d+, ([^)]*)\)', read(RT, "World", "HuntingGrounds.cs")):
    ZONES.append(dict(id=z.group(1), name=z.group(2), theme=z.group(3), lo=int(z.group(4)), hi=int(z.group(5)), mlv=int(z.group(6)),
                      mons=[x.strip().strip('"') for x in z.group(7).split(",")]))

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
# cooldown damage bonus (CareerData S(): +slope per second of cooldown, capped); --cd-slope / --cd-cap to test
_m = re.search(r"Mathf\.Min\(([0-9.]+)f, 1f \+ \.?([0-9.]+)f \* cd\)", CD)
CD_CAP = arg("--cd-cap", float(_m.group(1)) if _m else 2.5)
CD_SLOPE = arg("--cd-slope", float("0." + _m.group(2).lstrip(".")) if _m and not _m.group(2).startswith("0") else (float(_m.group(2)) if _m else .15))
HEAL, BUFF = {"heal", "bloom", "cleanse"}, {"guard", "oath", "bless", "counter", "frenzy", "venom", "clone", "stealth"}
# [ROGUE 2026-10-23] Assassin (a) and ninja (n) rows from Docs/PLAN_CLASS_ROGUE.md 2.3/2.4 in the CareerData S() format.
# Used only until CareerData.cs carries them (7절 5단계); once it does, the rows are read from the code like the others.
ROGUE_ROWS = """
S(a,1,"a_twin","쌍인참","","twin",1.0f,10,3,1.6f,0,.08f,0,4),
S(a,2,"a_veil","연막 도약","","veil",1.7f,14,12,4,2.5f,.05f,2),
S(a,3,"a_venom","맹독 도포","","venom",20,20,62,0,0,.1f,120),
S(a,5,"a_rend","연환 베기","","rend",.8f,18,0,2,.9f,.1f,0,2),
S(a,6,"a_rupture","급소 파열","","rupture",1.2f,22,10,2,0,.2f),
S(a,7,"a_assassinate","암살","","assassinate",1.85f,26,12,1.8f,0,.2f),
S(a,8,"a_awake","천영참","","shadowdance",.8f,60,48,0,5.4f,0f,2,12),
S(a,9,"a_stealth","은신","","stealth",50,20,20,0,0,.1f,6),
S(n,1,"n_kunai","쿠나이 순간이동","","kunai",1.6f,14,8,10,.4f,.08f),
S(n,2,"n_clone","분신술","","clone",25,20,24,0,0,.1f,8),
S(n,3,"n_recall","회수","","recall",.6f,16,8,8,0,.15f),
S(n,5,"n_volley","연발 쿠나이","","volley",.75f,18,0,8,.35f,.1f,0,2),
S(n,6,"n_fuma","풍마 수리검","","fuma",.55f,22,9,7,.7f,.2f,0,6),
S(n,7,"n_bomb","기폭 쿠나이","","bomb",1.4f,26,12,7,1.2f,.25f,0,5),
S(n,8,"n_awake","분신 난무","","clonestorm",.55f,60,48,0,6.2f,0f,2,16),
"""
SK = {}
_ROWS = CD if 'S(a,1,"a_twin"' in CD else CD + ROGUE_ROWS
for m in re.finditer(r'S\(([fgmban]),(\d+),"(\w+)","([^"]+)","[^"]*","(\w+)",([^)]*)\)', _ROWS):
    nums = [x.strip().rstrip("f") for x in m.group(6).split(",")]
    i, eff = int(m.group(2)), m.group(5)
    if i in (0, 4):
        continue
    power, mp, cd, rng, rad, cast = (float(nums[k]) for k in range(6))
    hits = int(nums[7]) if len(nums) > 7 and nums[7].isdigit() else 1
    awaken = i == 8
    mp = max(1, round(mp * (.75 if awaken else .65)))
    signature = cd <= 0 or eff in ("frenzy", "venom")
    if not signature:
        cd = max(.5, round(cd * (1.25 if awaken else 1.6 if eff in ("guard", "oath", "bless", "counter") else .7) * 2) / 2)
    if eff in HEAL:
        power *= .8
    elif eff not in BUFF and not awaken:
        power *= .85
        if cd > 0:
            power *= min(CD_CAP, 1 + CD_SLOPE * cd)
    SK[m.group(3)] = dict(eff=eff, power=power, mp=mp, cd=cd, cast=cast, hits=hits, rad=rad * (1.3 if awaken else 1.5))

RECOVERY = {"cross": .1, "light": .09, "heal": .1, "guard": .1, "blink": .1, "frenzy": .1, "nebula": .1, "sanctuary": .1,
            "break": .15, "fire": .15, "ice": .15, "storm": .15, "orbit": .15, "taunt": .15, "flurry": .7, "iaido": .22,
            "bash": .22, "shieldthrow": .14, "rush": .25, "execute": .32}
RANK3 = 1.24
MP_SHARE = {k: float(v) for k, v in re.findall(r'id == "(\w+)" \? \.?([0-9.]+)f', CD)} if "MainHunterMpShare" in CD else {}
MP_SHARE = {k: (v if v < 1 else v / 10000) for k, v in MP_SHARE.items()}
MP_SHARE.setdefault("a_rend", .0228)  # [ROGUE] PLAN_CLASS_ROGUE 2.3: .09 / 1.3 / 3.03 per second at full speed
MP_SHARE.setdefault("n_volley", .0228)
# [ROGUE] Rupture and assassinate are counted with every condition met (5 marks, from behind): 3.47 and 7.86 (2.3절).
COND_MUL = {"a_rupture": 2.0, "a_assassinate": 1.5 * 1.8}

# spam skill, its single-target hits per cast, average targets in a 3-pack, extra cooldown skills on keys
CAREERS = {
    # [2026-10-12] 8 skill keys: the A/S cooldown skills join the rotation (fighter 일섬·단죄, mage 중력 균열·차원도약,
    # bishop 정화의 종); shield = share of max HP kept up (mage 매직 배리어 35% per 62 s, guardian 회귀의 방패 18%).
    "파이터": dict(cls="w", hpmul=1.1, mpcls="w", spam="f_break", spam_hits=1, splash=2.0,
                 cds=["f_cross", "f_rush", "f_focus", "f_execute"], cd_hits={"f_cross": 2}, cd_splash=1.6, more=1.2 * 1.15, dr=0, shield=0),
    "수호자": dict(cls="w", hpmul=1.3, mpcls="w", spam="g_wall", spam_hits=2, splash=2.0,
                 cds=["g_taunt", "g_bash"], cd_hits={}, cd_splash=2.0, more=1.0, dr=65, shield=.18),
    "메이지": dict(cls="m", hpmul=1.0, mpcls="m", spam="m_storm", spam_hits=1, splash=2.6,
                 cds=["m_ice", "m_orbit", "m_rift", "m_veil"], cd_hits={"m_ice": 1, "m_orbit": 2.2, "m_rift": 6}, cd_splash=2.4, more=1.0, dr=0, shield=.35),
    "비숍": dict(cls="m", hpmul=1.05, mpcls="m", spam="b_light", spam_hits=1, splash=2.0,
                cds=["b_cleanse"], cd_hits={}, cd_splash=2.0, more=1.18, dr=0, shield=0),
    # [ROGUE 2026-10-23] PLAN_CLASS_ROGUE 3절: assassin more = venom 1.2 x marks 1.15 x ambush 1.05, ninja = clones 1.235 x .96.
    "어쌔신": dict(cls="r", hpmul=1.15, mpcls="r", spam="a_rend", spam_hits=2, splash=1.4,
                 cds=["a_twin", "a_veil", "a_rupture", "a_assassinate"], cd_hits={"a_twin": 4}, cd_splash=1.0, more=1.2 * 1.15 * 1.05, dr=0, shield=0,
                 basic=(.24, 1.1)),
    "닌자": dict(cls="r", hpmul=1.0, mpcls="r", spam="n_volley", spam_hits=2, splash=1.6,
                cds=["n_kunai", "n_fuma", "n_bomb", "n_recall"], cd_hits={"n_fuma": 6, "n_bomb": 3, "n_recall": 6}, cd_splash=2.0, more=1.235 * .96, dr=0, shield=0,
                basic=(.3, 1.0)),
}
# --old-mp: the warrior MP formula this tool used before 2026-10-23 (40 + 10, 4 per level). The code (CharacterStatsCalc) is
# 40 + 40, 4 + 2 per level for warriors and rogues; the 3.1 table in PLAN_CLASS_ROGUE.md was made with the old one.
OLD_MP = "--old-mp" in sys.argv
TRAVEL = 4.0
# EnemyController.LevelGapMul: player damage on monsters this many levels below (+15%/level beyond 3, max x3). --before = off.
def gap_mul(gap):
    return 1.0 if BEFORE else min(3.0, 1 + .15 * max(0, gap - 3))


def player(lv, c):
    atk, ghp = gear(lv)
    inc = (lv - 1) / 2 * 5.5 / 100 + (.05 if c["cls"] == "w" else 0)
    A = (10 + atk) * (1 + inc)
    warrior, rogue = c["cls"] == "w", c["cls"] == "r"
    hp = (60 + (50 if warrior else 25 if rogue else 0) + (lv - 1) * (8 + (6 if warrior else 3 if rogue else 0)) + ghp) * c["hpmul"]
    if rogue or (warrior and not OLD_MP):
        mp_base = 40 + 40 + (lv - 1) * (4 + 2)
    else:
        mp_base = 40 + (10 if warrior else 40) + (lv - 1) * (4 + (0 if warrior else 3))
    mp = mp_base * ({"비숍": 1.25, "메이지": 1.2}.get(next(k for k, v in CAREERS.items() if v is c), 1))
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
        d = n * A * k["power"] * COND_MUL.get(cid, 1) * RANK3 * c["more"] * c["cd_hits"].get(cid, 1)
        single += d
        pack += d * c["cd_splash"]
    left = max(0.0, window - busy)
    share = MP_SHARE.get(c["spam"])
    spam_mp = mp * share * 1.3 if share else s["mp"]  # [2026-10-12] CareerSkill.MainHunterMpShare x one support gem (x1.3)
    casts = min(left / cycle, max(0.0, budget) / spam_mp)
    single += casts * per_cast
    pack += casts * per_cast * c["splash"]
    basic_time = left - casts * cycle
    if "basic" in c:  # [ROGUE] dagger .65 / .24 s (pack x1.1), ninja kunai .65 / .3 s
        basic = A * .65 / c["basic"][0] * basic_time
        single += basic
        pack += basic * c["basic"][1]
        return single, pack
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


def career_table():
    """[ROGUE 2026-10-23] PLAN_CLASS_ROGUE 3.1/3.2: per-career averages over the Lv15+ grounds and the Lv40 boss model."""
    print("\n## 전직 6종 사냥 평균 (Lv15+ 사냥터, PLAN_CLASS_ROGUE 3.1)" + (" [--old-mp]" if OLD_MP else ""))
    print("| 직업 | 1마리 평균(초) | 1마리 범위 | 무리 3마리 평균(초) | 범위 | 버팀(초) |")
    print("|---|---|---|---|---|---|")
    zs = [z for z in ZONES if z["mlv"] >= 15]
    for name, c in CAREERS.items():
        ones, clears, survs = [], [], []
        for z in zs:
            lv = z["mlv"]
            hps = [mon_hp(m, lv) * zone_mul(z) for m in z["mons"]]
            incoming = sum(mon_dps(m, lv) for m in z["mons"]) * .9 * (1 - c["dr"] / 100)
            ones.append((sum(hps) / len(hps)) / (rotation(lv, c, 3.0)[0] / 3.0))
            clears.append(solve_clear(lv, c, sum(hps)))
            survs.append(player(lv, c)[1] * (1 + c["shield"]) / incoming)
        avg = lambda v: sum(v) / len(v)
        print(f"| {name} | {avg(ones):.2f} | {min(ones):.1f}~{max(ones):.1f} | {avg(clears):.2f} | {min(clears):.1f}~{max(clears):.1f} | {min(survs):.0f}~{max(survs):.0f} |")
    print("\n## Lv40 보스 1체 60초 피해 (파이터 = 1, 이동 없음, PLAN_CLASS_ROGUE 3.2)")
    base = rotation(40, CAREERS["파이터"], 60.0)[0]
    print("| 직업 | 모델 |")
    print("|---|---|")
    for name, c in CAREERS.items():
        print(f"| {name} | {rotation(40, c, 60.0)[0] / base:.2f} |")


def summary():
    print("\n## 요약 (같은 레벨 사냥터, 직업 4종 기하평균)")
    print("| 사냥터 | 몹Lv | 무리 정리(초) | 1마리(초) | 파이터 버팀(초) | 메이지 버팀(초) | 수호자 버팀(초) | +10Lv 캐릭터 1마리(초) |")
    print("|---|---|---|---|---|---|---|---|")
    for z in ZONES:
        lv = z["mlv"]
        hps = [mon_hp(m, lv) * zone_mul(z) for m in z["mons"]]
        incoming = sum(mon_dps(m, lv) for m in z["mons"]) * .9
        if lv < 15:
            clear = pre_clear(lv, sum(hps))
            one = clear / 3
            low = pre_clear(lv + 10, sum(hps)) / 3 / gap_mul(10)
            def base_hp(warrior):
                atk, ghp = gear(lv)
                return 60 + (50 if warrior else 0) + (lv - 1) * (8 + (6 if warrior else 0)) + ghp
            rogue_hp = 60 + 25 + (lv - 1) * (8 + 3) + gear(lv)[1]  # [ROGUE] HP +25, +3 per level
            surv = [f"{base_hp(True) / incoming:.1f}(전사)", f"{base_hp(False) / incoming:.1f}(마법사)", f"{rogue_hp / incoming:.1f}(도적)"]
        else:
            cl, ones, lows = [], [], []
            for n4 in ("파이터", "수호자", "메이지", "비숍"):  # [ROGUE] the summary stays the 4-career baseline
                c = CAREERS[n4]
                cl.append(solve_clear(lv, c, sum(hps)))
                d = rotation(lv, c, 3.0)[0] / 3.0
                ones.append(sum(hps) / 3 / d)
                lows.append(sum(hps) / 3 / (rotation(lv + 10, c, 3.0)[0] / 3.0) / gap_mul(10))
            geo = lambda v: math.exp(sum(map(math.log, v)) / len(v))
            clear, one, low = geo(cl), geo(ones), geo(lows)
            surv = [f"{player(lv, CAREERS[n])[1] * (1 + CAREERS[n]['shield']) / (incoming * (1 - CAREERS[n]['dr'] / 100)):.0f}" for n in ("파이터", "메이지", "수호자")]
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
        hps = [mon_hp(m, z["mlv"]) * zone_mul(z) for m in z["mons"]]
        incoming = sum(mon_dps(m, z["mlv"]) for m in z["mons"])
        for name, c in CAREERS.items():
            A, hp, mp = player(lv, c)
            one_window = 3.0
            single3 = rotation(lv, c, one_window)[0] / one_window
            ttk = (sum(hps) / len(hps)) / single3
            clear = solve_clear(lv, c, sum(hps))
            taken = incoming * .9 * (1 - c["dr"] / 100)
            survive = hp * (1 + c["shield"]) / taken if taken > 0 else 999
            low = (sum(hps) / len(hps)) / (rotation(lv + 10, c, one_window)[0] / one_window) / gap_mul(10)
            rows.append((z, name, ttk, clear, survive))
            print(f"| {z['name']} | {z['mlv']} | {name} | {ttk:.1f} | {clear:.1f} | {survive:.0f} | {survive / clear:.1f} | {low:.2f} |")
    return rows


if __name__ == "__main__":
    if "--detail" in sys.argv:
        report()
    summary()
    career_table()
