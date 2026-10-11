"""
dotRPG 던전·레이드·필드 보스 이론 계산 (2026-10-11). theory_combat.py 의 플레이어 모델(연사기, 쿨기 x(1+0.15cd), 60초 버프,
성역 +50%, 수호자 감소)을 그대로 쓰고, 던전 방·보스·레이드 수치는 코드에서 읽는다.

모델
- 방 몬스터 체력 = 기본 hp x 난이도 hpMul x 파티 보정(PartyHpScale) x (1 + HpPerLevel (L-1)), L = 1 + 난이도 monsterLevel + 묶음 offset.
  사냥터 전용 배율(FieldHpMul)은 던전에 걸리지 않는다.
- 방 정리 = 방 체력 합 / 파티 무리 화력(theory_combat.rotation 의 무리 피해, 20초 정상 상태) + 이동 12초.
- 보스 = 보스 hp(같은 식, 보스 묶음 offset 3) / 파티 단일 화력 x 0.7(패턴 회피·이동으로 붙어 있는 시간 70%).
- 파티: 사람 N명(직업 4종 순서대로 채움) + AI 동료(사람의 65%)로 4칸을 채운 경우와 사람만 있는 경우.
  파티 보정 체력은 PartySize(사람 + AI) 기준.
- 생존: 방 몬스터가 동시에 공격하는 초당 피해를 파티원 수로 나눈 값으로 HP를 나눈 초(물약 없음).
- 전직 전(권장 Lv 5, 12) 은 theory_combat.pre_clear 의 화력(기본 공격 + 열린 슬롯)을 쓴다.

목표 (PLAN_DUNGEON_RAID, theory_balance)
- 요일 던전: 권장 레벨, AI 동료 3명과 120~300초. 사람 4명 파티는 그보다 빠르되 90초 이상.
- 레이드: 300~600초(사람 4명 또는 AI 포함 4명). 보스는 그중 40% 이상(수 초에 녹지 않는다).
- 필드 보스: 권장 레벨 혼자 30~90초.

사용: python3 Tools/balance/theory_dungeon.py [--before]
"""
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
import theory_combat as tc  # noqa: E402

RT = tc.RT
DDB = tc.read(RT, "Dungeon", "DungeonDatabase.cs")
MDB = tc.MDB

PARTY_HP = [float(x) for x in re.search(r"PartyHpScale = \{ ([^}]*) \}", DDB).group(1).replace("f", "").split(",")]
DIFFS = []
for m in re.finditer(r'id = DungeonDifficulty\.\w+, name = "([^"]+)", recommendedLevel = (\d+),.*?hpMul = ([0-9.]+)f, damageMul = ([0-9.]+)f,.*?monsterLevel = (\d+)', DDB):
    if m.group(1) == "레이드":
        continue
    DIFFS.append(dict(name=m.group(1), lv=int(m.group(2)), hp=float(m.group(3)), dmg=float(m.group(4)), mlv=int(m.group(5))))

BOSS = {}
for m in re.finditer(r'Boss\("(\w+)", "([^"]+)", [^,]+(?:\([^)]*\))?, hp: (\d+), dmg: (\d+)', MDB):
    BOSS[m.group(1)] = dict(name=m.group(2), hp=int(m.group(3)), dmg=int(m.group(4)))


def groups(text):
    return [(g.group(1), int(g.group(2)), int(g.group(3) or 0)) for g in re.finditer(r'new SpawnGroup\(\d+, "(\w+)", (\d+)(?:, (\d+))?\)', text)]


DUNGEONS = []
for d in re.finditer(r'new DungeonDef\s*\{\s*id = "(\w+)", name = "([^"]+)"(.*?)\n            \},', DDB, re.S):
    body = d.group(3)
    rooms = [groups(r.group(1)) for r in re.finditer(r"Room\([^,]+,(.*?)\)\),?\n|Room\([^,]+,(.*?)\)\)", body)]
    rooms = [groups(r) for r in re.findall(r"Room\(MapRegistry\.\w+,((?:\s*new SpawnGroup\([^)]*\),?)+)\)", body)]
    boss = re.search(r'Boss\(MapRegistry\.\w+, "(\w+)", "(\w+)", (\d+)\)', body)
    DUNGEONS.append(dict(id=d.group(1), name=d.group(2), rooms=rooms, boss=boss.group(1), add=boss.group(2), adds=int(boss.group(3))))

RAIDS = []
for d in re.finditer(r'static readonly DungeonDef (\w+) = new DungeonDef\s*\{(.*?)\n        \};', DDB, re.S):
    body = d.group(2)
    rn = re.search(r"RaidNumbers\((\d+), \d+, ([0-9.]+)f, ([0-9.]+)f, (\d+)", body)
    if not rn:
        continue
    rooms = [groups(r) for r in re.findall(r"Room\(MapRegistry\.\w+,((?:\s*new SpawnGroup\([^)]*\),?)+)\)", body)]
    boss = re.search(r'Boss\(MapRegistry\.\w+, "(\w+)", "(\w+)", (\d+)\)', body)
    RAIDS.append(dict(name=re.search(r'name = "([^"]+)"', body).group(1), lv=int(rn.group(1)), hp=float(rn.group(2)),
                      dmg=float(rn.group(3)), mlv=int(rn.group(4)), rooms=rooms, boss=boss.group(1)))

ORDER = ["파이터", "비숍", "메이지", "수호자"]
AI_SHARE = .65
ROOM_TRAVEL = 12.0
BOSS_UPTIME = .7


def lvl_hp(base, lv, mul):
    return base * mul * (1 + tc.HP_PER_LEVEL * (lv - 1))


def lvl_dmg(base, lv, mul):
    return base * mul * (1 + tc.DMG_PER_LEVEL * (lv - 1))


def career_rates(lv, name):
    """(pack DPS, single DPS) of one character at `lv`."""
    if lv < 15:
        A = tc.player(lv, tc.CAREERS["파이터"])[0] / 1.05
        slots = sum(1 for s in (2, 6, 12, 18) if lv >= s)
        pack = A * (2.5 * 1.5 + slots)
        return pack, pack / 1.6
    c = tc.CAREERS[name]
    single, pack = tc.rotation(lv, c, 20.0)
    return pack / 20.0, single / 20.0


def party(lv, humans, ai):
    names = [ORDER[i % 4] for i in range(humans + ai)]
    pack = single = 0.0
    for i, n in enumerate(names):
        p, s = career_rates(lv, n)
        share = 1.0 if i < humans else AI_SHARE
        pack += p * share
        single += s * share
    return pack, single, len(names)


def run(lv, mlv_base, hp_mul, dmg_mul, rooms, boss_id, adds, add_id, humans, ai):
    pack, single, size = party(lv, humans, ai)
    hp_mul_p = hp_mul * PARTY_HP[min(size, len(PARTY_HP)) - 1]
    t = 0.0
    worst_survive = 999.0
    for room in rooms:
        hp = 0.0
        incoming = 0.0
        for mid, count, off in room:
            L = 1 + mlv_base + off
            hp += count * lvl_hp(tc.MON[mid]["hp"], L, hp_mul_p)
            m = tc.MON[mid]
            cycle = (m["skill"] if m["skill"] > 0 else m["wind"] + m["rec"]) * tc.ATTACK_PACE
            incoming += count * lvl_dmg(m["dmg"], L, dmg_mul) / max(.5, cycle)
        t += hp / pack + ROOM_TRAVEL
        php = tc.player(lv, tc.CAREERS["메이지"])[1]
        worst_survive = min(worst_survive, php / (incoming * .9 / size))
    b = BOSS[boss_id]
    bL = 1 + mlv_base + 3
    boss_hp = lvl_hp(b["hp"], bL, hp_mul_p)
    if adds:
        boss_hp += adds * lvl_hp(tc.MON[add_id]["hp"], 1 + mlv_base, hp_mul_p)
    boss_t = boss_hp / (single * BOSS_UPTIME)
    return t + boss_t, boss_t, worst_survive


def dungeon_table():
    print("\n## 요일 던전 (권장 레벨, 초)")
    print("| 던전 | 난이도 | 권장Lv | AI 3명과(전체/보스) | 사람 2 + AI 2 | 사람 4 | 메이지 버팀(AI 3명과) |")
    print("|---|---|---|---|---|---|---|")
    out = []
    for d in DUNGEONS:
        for df in DIFFS:
            a = run(df["lv"], df["mlv"], df["hp"], df["dmg"], d["rooms"], d["boss"], d["adds"], d["add"], 1, 3)
            b = run(df["lv"], df["mlv"], df["hp"], df["dmg"], d["rooms"], d["boss"], d["adds"], d["add"], 2, 2)
            c = run(df["lv"], df["mlv"], df["hp"], df["dmg"], d["rooms"], d["boss"], d["adds"], d["add"], 4, 0)
            out.append(a[0])
            print(f"| {d['name']} | {df['name']} | {df['lv']} | {a[0]:.0f} / {a[1]:.0f} | {b[0]:.0f} | {c[0]:.0f} | {a[2]:.0f} |")
    return out


def raid_table():
    print("\n## 레이드 (권장 레벨, 초)")
    print("| 레이드 | 권장Lv | AI 3명과(전체/보스) | 사람 4 (전체/보스) | 메이지 버팀(사람 4) |")
    print("|---|---|---|---|---|")
    for r in RAIDS:
        a = run(r["lv"], r["mlv"], r["hp"], r["dmg"], r["rooms"], r["boss"], 0, None, 1, 3)
        c = run(r["lv"], r["mlv"], r["hp"], r["dmg"], r["rooms"], r["boss"], 0, None, 4, 0)
        print(f"| {r['name']} | {r['lv']} | {a[0]:.0f} / {a[1]:.0f} | {c[0]:.0f} / {c[1]:.0f} | {c[2]:.0f} |")


FIELD_BOSS = {"fboss_forest": "forest_depths", "fboss_canyon": "canyon_ridge", "fboss_winter": "winter_peak"}
FIELD_BOSS_MUL = float(re.search(r"FieldBossHpMul = ([0-9.]+)f", MDB).group(1)) if "FieldBossHpMul" in MDB and not tc.BEFORE else 1.0


def field_boss_table():
    print("\n## 필드 보스 (그 사냥터 권장 최대 레벨 혼자, 초)")
    print("| 보스 | 보스Lv | 체력 | 파이터 | 메이지 | 수호자 | 비숍 |")
    print("|---|---|---|---|---|---|---|")
    for bid, zone in FIELD_BOSS.items():
        z = next(x for x in tc.ZONES if x["id"] == zone)
        L = z["mlv"] + 2
        hp = lvl_hp(BOSS[bid]["hp"], L, FIELD_BOSS_MUL)
        cells = []
        for n in ("파이터", "메이지", "수호자", "비숍"):
            s = career_rates(z["hi"], n)[1]
            cells.append(f"{hp / (s * BOSS_UPTIME):.0f}")
        print(f"| {BOSS[bid]['name']} | {L} | {hp:.0f} | " + " | ".join(cells) + " |")


if __name__ == "__main__":
    dungeon_table()
    raid_table()
    field_boss_table()
