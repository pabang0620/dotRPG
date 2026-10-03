import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import {
  anomalyKinds,
  countOf,
  expectLedgerConsistent,
  fakeRng,
  get,
  goldOf,
  newHero,
  post,
  seedWorn,
} from './economyHelpers';

let app = buildApp();
let fixed = new Date('2026-10-05T03:00:00Z');
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeAll(resetDb);
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});
beforeEach(() => {
  fixed = new Date('2026-10-05T03:00:00Z');
  setClockOverride(() => fixed);
  // 재료·장비가 나오지 않게 u=1(모두 실패), 정수는 최솟값
  setRng(fakeRng({ unit: 1 }));
});

const forestKill = { map_id: 'forest', monster_id: 'skeleton' };

describe('POST /characters/:id/kills', () => {
  it('정상: 경험치와 드롭을 서버가 정한다(골드 1더미, 잔액은 줍기 전까지 그대로)', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(200);
    expect(res.body.data.granted_xp).toBe(20);
    expect(res.body.data.delta).toEqual({ level: 1, xp: 20 });
    expect(res.body.data.drops).toHaveLength(1);
    expect(res.body.data.drops[0]).toMatchObject({ item_key: 'gold', count: 8 });
    expect(await goldOf(h)).toBe(100);

    const db = getPool();
    const stats = await db.query('SELECT kills FROM kill_stats WHERE character_id = $1 AND monster_id = $2', [h.dbId, 'skeleton']);
    expect(Number(stats.rows[0].kills)).toBe(1);
    const xl = await db.query('SELECT delta, reason, ref FROM xp_ledger WHERE character_id = $1', [h.dbId]);
    expect(xl.rows).toEqual([{ delta: 20, reason: 'kill', ref: 'skeleton' }]);
  });

  it('레벨업: 경험치가 한 번에 여러 레벨을 올린다', async () => {
    const h = await newHero(app);
    // xp 38 + 20 = 58 >= 40 -> 레벨 2, 남은 18
    await getPool().query('UPDATE characters SET xp = 38 WHERE id = $1', [h.dbId]);
    const res = await post(app, h, '/kills', forestKill);
    expect(res.body.data.leveled_up).toBe(true);
    expect(res.body.data.delta).toEqual({ level: 2, xp: 18 });
  });

  it('입력 오류: 경험치·드롭을 보내거나 run_id만 보내면 400', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/kills', { ...forestKill, xp: 9999 })).status).toBe(400);
    expect((await post(app, h, '/kills', { ...forestKill, drops: [] })).status).toBe(400);
    expect((await post(app, h, '/kills', { ...forestKill, run_id: randomUUID() })).status).toBe(400);
    expect((await post(app, h, '/kills', { ...forestKill, hits: 61 })).status).toBe(400);
    expect((await post(app, h, '/kills', { ...forestKill }, 'not-a-uuid')).status).toBe(400);
    const res = await request(app).post(`/characters/${h.id}/kills`).set(auth(h.s)).send({ ...forestKill });
    expect(res.status).toBe(400);
  });

  it('재전송: 같은 request_id는 같은 드롭 id를 돌려주고 이중 지급이 없다', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, h, '/kills', forestKill, rid);
    const b = await post(app, h, '/kills', forestKill, rid);
    expect(b.status).toBe(200);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    const n = await getPool().query('SELECT count(*)::int AS n FROM kill_log WHERE character_id = $1', [h.dbId]);
    expect(n.rows[0].n).toBe(1);
    const x = await getPool().query('SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1', [h.dbId]);
    expect(x.rows[0].n).toBe(1);
  });

  it('같은 request_id에 다른 본문이면 422 IDEMPOTENCY_MISMATCH', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    await post(app, h, '/kills', forestKill, rid);
    const res = await post(app, h, '/kills', { map_id: 'village', monster_id: 'skel_warrior' }, rid);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });

  it('동시 요청: 같은 request_id 두 개는 한 번만 처리되고, 다른 id 두 개는 둘 다 처리된다', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const [a, b] = await Promise.all([post(app, h, '/kills', forestKill, rid), post(app, h, '/kills', forestKill, rid)]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect(a.body).toEqual(b.body);
    const n1 = await getPool().query('SELECT count(*)::int AS n FROM kill_log WHERE character_id = $1', [h.dbId]);
    expect(n1.rows[0].n).toBe(1);

    const [c, d] = await Promise.all([post(app, h, '/kills', forestKill), post(app, h, '/kills', forestKill)]);
    expect([c.status, d.status]).toEqual([200, 200]);
    const n2 = await getPool().query('SELECT count(*)::int AS n FROM kill_log WHERE character_id = $1', [h.dbId]);
    expect(n2.rows[0].n).toBe(3);
    const xp = await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [h.dbId]);
    expect(xp.rows[0]).toEqual({ level: 2, xp: 20 }); // 20 x 3 = 60 = 40 + 20
  });

  it('그럴듯함: 맵에 없는 몬스터는 거절하고 이상 기록을 남긴다', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skel_gold' });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('KILL_REJECTED');
    expect(await anomalyKinds(h)).toEqual(['kill_target']);
    // 보상은 없다
    const n = await getPool().query('SELECT count(*)::int AS n FROM kill_log WHERE character_id = $1', [h.dbId]);
    expect(n.rows[0].n).toBe(0);
    // 던전 방 맵, 마을 보스, 없는 맵도 거절
    expect((await post(app, h, '/kills', { map_id: 'dgn_canyon_1', monster_id: 'skel_gold' })).status).toBe(422);
    expect((await post(app, h, '/kills', { map_id: 'village', monster_id: 'skeleton' })).status).toBe(422);
    expect((await post(app, h, '/kills', { map_id: 'nowhere', monster_id: 'skeleton' })).status).toBe(422);
  });

  it('몬스터 id 오류: 모르는 id, 보상 없는 토템, 레이드 보스', async () => {
    const h = await newHero(app);
    const a = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'dragon' });
    expect(a.body.errors.code).toBe('MONSTER_UNKNOWN');
    const b = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'totem' });
    expect(b.body.errors.code).toBe('MONSTER_NO_REWARD');
    const c = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'boss_skeleton_king' });
    expect(c.body.errors.code).toBe('RAID_NOT_AVAILABLE');
  });

  it('속도: 1초 창에 4마리를 넘기면 429 KILL_RATE_LIMITED와 이상 기록', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 4; i++) expect((await post(app, h, '/kills', forestKill)).status).toBe(200);
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(429);
    expect(res.body.errors.code).toBe('KILL_RATE_LIMITED');
    expect(res.headers['retry-after']).toBe('1');
    expect(await anomalyKinds(h)).toEqual(['kill_rate']);
    // 1초 뒤에는 다시 받아들인다
    advance(2);
    expect((await post(app, h, '/kills', forestKill)).status).toBe(200);
  });

  it('공급: 리스폰 창 안에 스폰점 수 x 1.1(올림)을 넘으면 거절한다', async () => {
    const h = await newHero(app);
    const rows = Array.from({ length: 17 }, () => randomUUID());
    for (const rid of rows) {
      await getPool().query(
        `INSERT INTO kill_log (character_id, map_id, monster_id, monster_level, context, hits, xp_granted, request_id, created_at)
         VALUES ($1, 'forest', 'skeleton', 1, 'field', 0, 0, $2, $3)`,
        [h.dbId, rid, new Date(fixed.getTime() - 5000)],
      );
    }
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('KILL_REJECTED');
    expect(await anomalyKinds(h)).toEqual(['kill_supply']);
    advance(30); // 리스폰 창(25초)이 지나면 다시 받는다
    expect((await post(app, h, '/kills', forestKill)).status).toBe(200);
  });

  it('연출 스폰: 마을 해골 전사는 평생 5마리까지', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 5; i++) {
      advance(2);
      const res = await post(app, h, '/kills', { map_id: 'village', monster_id: 'skel_warrior' });
      expect(res.status).toBe(200);
      expect(res.body.data.granted_xp).toBe(20);
    }
    advance(2);
    const res = await post(app, h, '/kills', { map_id: 'village', monster_id: 'skel_warrior' });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('KILL_REJECTED');
  });

  it('가벼운 이상(severity 1, 1초 창 초과)만으로는 차단하지 않는다', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 25; i++) {
      await getPool().query(
        "INSERT INTO anomaly_log (account_id, character_id, kind, severity, created_at) VALUES ((SELECT account_id FROM characters WHERE id = $1), $1, 'kill_rate', 1, $2)",
        [h.dbId, new Date(fixed.getTime() - 60_000)],
      );
    }
    expect((await post(app, h, '/kills', forestKill)).status).toBe(200);
  });

  it('이상이 반복되면 일시 차단(403 KILL_BLOCKED)', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 20; i++) {
      await getPool().query(
        "INSERT INTO anomaly_log (account_id, character_id, kind, severity, created_at) VALUES ((SELECT account_id FROM characters WHERE id = $1), $1, 'kill_supply', 2, $2)",
        [h.dbId, new Date(fixed.getTime() - 60_000)],
      );
    }
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(403);
    expect(res.body.errors.code).toBe('KILL_BLOCKED');
    advance(11 * 60); // 10분이 지나면 풀린다
    expect((await post(app, h, '/kills', forestKill)).status).toBe(200);
  });

  it('난수 주입: 재료와 장비 드롭은 u <= 확률일 때만, 장비는 직업이 쓰는 것만', async () => {
    const h = await newHero(app, 'mage');
    setRng(fakeRng({ unit: 0, int: (min) => min }));
    const res = await post(app, h, '/kills', forestKill);
    const keys = res.body.data.drops.map((d: { item_key: string }) => d.item_key);
    // 골드, 뼈 1개, 강화석 1개, 마력 정수 1개, 장비 1개(마법사 가중 목록의 첫 장비)
    expect(keys).toEqual(['gold', 'mat_bone', 'mat_ore', 'mat_essence', 'eq_staff_crystal']);
  });

  it('미수령 드롭이 상한이면 경험치만 인정하고 드롭 굴림은 건너뛴다', async () => {
    const small = buildApp({ DROP_OPEN_PER_CHARACTER: '1' });
    const h = await newHero(small);
    const a = await post(small, h, '/kills', forestKill);
    expect(a.body.data.drops.length).toBeGreaterThan(0);
    advance(2);
    const b = await post(small, h, '/kills', forestKill);
    expect(b.status).toBe(200);
    expect(b.body.data.drops).toEqual([]);
    expect(b.body.data.granted_xp).toBe(20);
    app = buildApp();
  });

  it('GET 상세에 3단계 필드가 있다', async () => {
    const h = await newHero(app);
    const res = await get(app, h, '');
    const c = res.body.data.character;
    expect(c).toMatchObject({
      bonus_max_health: 0,
      claimed_quests: [],
      opened_chests: [],
      enhance_pity: [],
      storage_capacity: 48,
    });
    expect(c.deliveries[0]).toMatchObject({ site_id: 'workshop' });
    expect(c.deliveries[0].items).toEqual([
      { item_key: 'wood', delivered: 0, required: 6 },
      { item_key: 'stone', delivered: 0, required: 4 },
    ]);
  });
});

describe('POST /characters/:id/drops/claim', () => {
  async function killAndDrops(h: Awaited<ReturnType<typeof newHero>>) {
    const res = await post(app, h, '/kills', forestKill);
    return res.body.data.drops as { id: string; item_key: string; count: number }[];
  }

  it('정상: 골드가 늘고 드롭마다 원장 한 줄(ref = 드롭 id)', async () => {
    const h = await newHero(app);
    const [drop] = await killAndDrops(h);
    const res = await post(app, h, '/drops/claim', { drop_ids: [(drop as { id: string }).id] });
    expect(res.status).toBe(200);
    expect(res.body.data.results).toEqual([{ id: (drop as { id: string }).id, result: 'claimed' }]);
    expect(res.body.data.delta.gold).toBe(108);
    expect(await goldOf(h)).toBe(108);
    const l = await getPool().query("SELECT ref, delta FROM gold_ledger WHERE character_id = $1 AND reason = 'drop_claim'", [h.dbId]);
    expect(l.rows).toEqual([{ ref: (drop as { id: string }).id, delta: '8' }]);
    await expectLedgerConsistent(h);
  });

  it('아이템 드롭은 가방 스택으로 들어간다', async () => {
    const h = await newHero(app);
    setRng(fakeRng({ unit: 0 }));
    const drops = await killAndDrops(h);
    const res = await post(app, h, '/drops/claim', { drop_ids: drops.map((d) => d.id) });
    expect(res.body.data.results.every((r: { result: string }) => r.result === 'claimed')).toBe(true);
    expect(await countOf(h, 'mat_bone')).toBe(1);
    expect(await countOf(h, 'eq_sword_iron')).toBe(1);
    expect(res.body.data.delta.stacks).toEqual(
      expect.arrayContaining([{ item_key: 'mat_bone', location: 'bag', count: 1 }]),
    );
    await expectLedgerConsistent(h);
  });

  it('두 번 줍기: 두 번째는 already_claimed이고 잔액이 늘지 않는다', async () => {
    const h = await newHero(app);
    const [drop] = await killAndDrops(h);
    const id = (drop as { id: string }).id;
    await post(app, h, '/drops/claim', { drop_ids: [id] });
    const again = await post(app, h, '/drops/claim', { drop_ids: [id] });
    expect(again.status).toBe(200);
    expect(again.body.data.results).toEqual([{ id, result: 'already_claimed' }]);
    expect(await goldOf(h)).toBe(108);
    await expectLedgerConsistent(h);
  });

  it('재전송: 같은 request_id는 같은 응답이고 한 번만 지급', async () => {
    const h = await newHero(app);
    const [drop] = await killAndDrops(h);
    const rid = randomUUID();
    const a = await post(app, h, '/drops/claim', { drop_ids: [(drop as { id: string }).id] }, rid);
    const b = await post(app, h, '/drops/claim', { drop_ids: [(drop as { id: string }).id] }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await goldOf(h)).toBe(108);
  });

  it('동시 요청: 같은 드롭을 두 요청이 보내도 한쪽만 claimed', async () => {
    const h = await newHero(app);
    const [drop] = await killAndDrops(h);
    const id = (drop as { id: string }).id;
    const [a, b] = await Promise.all([
      post(app, h, '/drops/claim', { drop_ids: [id] }),
      post(app, h, '/drops/claim', { drop_ids: [id] }),
    ]);
    const results = [a, b].map((r) => r.body.data.results[0].result).sort();
    expect(results).toEqual(['already_claimed', 'claimed']);
    expect(await goldOf(h)).toBe(108);
    await expectLedgerConsistent(h);
  });

  it('남의 드롭: not_found로 알리고 이상 기록, 남의 잔액은 그대로', async () => {
    const owner = await newHero(app);
    const thief = await newHero(app);
    const [drop] = await killAndDrops(owner);
    const res = await post(app, thief, '/drops/claim', { drop_ids: [(drop as { id: string }).id] });
    expect(res.status).toBe(200);
    expect(res.body.data.results[0].result).toBe('not_found');
    expect(await goldOf(thief)).toBe(100);
    expect(await anomalyKinds(thief)).toEqual(['drop_foreign']);
    // 주인은 여전히 줍을 수 있다
    const mine = await post(app, owner, '/drops/claim', { drop_ids: [(drop as { id: string }).id] });
    expect(mine.body.data.results[0].result).toBe('claimed');
  });

  it('만료된 드롭은 expired, 모르는 id는 not_found', async () => {
    const h = await newHero(app);
    const [drop] = await killAndDrops(h);
    const id = (drop as { id: string }).id;
    await getPool().query("UPDATE drops SET expires_at = $2 WHERE uuid = $1", [id, new Date(fixed.getTime() - 1000)]);
    const unknown = randomUUID();
    const res = await post(app, h, '/drops/claim', { drop_ids: [id, unknown] });
    expect(res.body.data.results).toEqual([
      { id, result: 'expired' },
      { id: unknown, result: 'not_found' },
    ]);
    expect(await goldOf(h)).toBe(100);
  });

  it('만료 시각은 서버 시계로 판정: 5분 뒤에는 못 줍는다', async () => {
    const h = await newHero(app);
    const [drop] = await killAndDrops(h);
    advance(301);
    const res = await post(app, h, '/drops/claim', { drop_ids: [(drop as { id: string }).id] });
    expect(res.body.data.results[0].result).toBe('expired');
  });

  it('입력 오류: 빈 목록, uuid 아님, 51개, 금액 필드', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/drops/claim', { drop_ids: [] })).status).toBe(400);
    expect((await post(app, h, '/drops/claim', { drop_ids: ['abc'] })).status).toBe(400);
    expect((await post(app, h, '/drops/claim', { drop_ids: Array.from({ length: 51 }, () => randomUUID()) })).status).toBe(400);
    expect((await post(app, h, '/drops/claim', { drop_ids: [randomUUID()], gold: 99999 })).status).toBe(400);
  });

  it('남의 캐릭터 경로는 404', async () => {
    const a = await newHero(app);
    const b = await newHero(app);
    const res = await request(app)
      .post(`/characters/${a.id}/drops/claim`)
      .set(auth(b.s))
      .send({ request_id: randomUUID(), drop_ids: [randomUUID()] });
    expect(res.status).toBe(404);
    expect(res.body.errors.code).toBe('CHARACTER_NOT_FOUND');
  });
});

describe('화력 상한 규칙(순수 함수)', () => {
  it('실효 HP 합이 상한 x 창 x AOE + 최대 HP를 넘으면 불허', async () => {
    const { loadGameData } = await import('../src/gamedata/loader');
    const { attackCap, powerAllows, effectiveHp } = await import('../src/domains/kills/killRules');
    const eco = loadGameData(buildDataDir()).economy;
    const pol = { powerPassivePerLevel: 0.11, powerSkillFactor: 2, powerAoeCap: 5 };
    const cap = attackCap(eco, pol, 1, ['eq_sword_wood']);
    expect(cap).toBe(10);
    const boss = effectiveHp(eco, eco.monsters.get('boss_grah') as never, 26, 2.7);
    expect(powerAllows(eco, pol, cap, 5, [boss])).toBe(true); // 한 마리는 항상 통과
    expect(powerAllows(eco, pol, cap, 5, [boss, boss, boss])).toBe(false); // 5초에 보스 셋은 불가능
    // 레벨과 장비가 오르면 상한이 오른다
    const strong = attackCap(eco, pol, 30, ['eq_sword_dragon+10']);
    expect(strong).toBeGreaterThan(cap * 10);
    expect(powerAllows(eco, pol, strong, 30, [boss, boss, boss])).toBe(true);
    expect(powerAllows(eco, pol, cap, 30, [boss, boss, boss])).toBe(false);
  });
});

function buildDataDir(): string {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  return require('node:path').resolve(__dirname, '..', 'data');
}

describe('착용 장비는 화력 상한에 반영된다', () => {
  it('착용 행이 있어도 처치는 정상 처리된다', async () => {
    const h = await newHero(app);
    await seedWorn(h, 1, 'eq_neck_bone');
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(200);
  });
});
