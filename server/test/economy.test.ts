import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { loadGameData } from '../src/gamedata/loader';
import { gameWeekday, resetBoundaries } from '../src/utils/resetBoundaries';
import { DATA_DIR, auth, buildApp, resetDb, shutdown } from './helpers';
import { expectLedgerConsistent, goldOf, newHero, post, seedGold } from './economyHelpers';

let app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

describe('resetBoundaries: 현재 구간의 시작', () => {
  it('일일 시작은 직전 06:00 KST(경계 직전은 전날, 정각은 당일)', () => {
    expect(resetBoundaries(new Date('2026-10-04T20:59:59Z')).dailyStartAt).toBe('2026-10-03T21:00:00.000Z');
    expect(resetBoundaries(new Date('2026-10-04T21:00:00Z')).dailyStartAt).toBe('2026-10-04T21:00:00.000Z');
    const b = resetBoundaries(new Date('2026-10-04T21:00:00Z'));
    expect(b.nextDailyAt).toBe('2026-10-05T21:00:00.000Z');
  });

  it('주간 시작은 직전 목요일 06:00 KST', () => {
    expect(resetBoundaries(new Date('2026-10-07T20:59:59Z')).weeklyStartAt).toBe('2026-09-30T21:00:00.000Z');
    expect(resetBoundaries(new Date('2026-10-07T21:00:00Z')).weeklyStartAt).toBe('2026-10-07T21:00:00.000Z');
    expect(resetBoundaries(new Date('2026-10-05T03:00:00Z')).weeklyStartAt).toBe('2026-09-30T21:00:00.000Z');
  });

  it('게임 요일은 06:00 전에는 전날', () => {
    expect(gameWeekday(new Date('2026-10-05T20:59:59Z'))).toBe('Monday'); // 화요일 05:59 KST
    expect(gameWeekday(new Date('2026-10-05T21:00:00Z'))).toBe('Tuesday');
    expect(gameWeekday(new Date('2026-10-10T03:00:00Z'))).toBe('Saturday');
  });
});

describe('시작 지급 원장(0002 전제)', () => {
  it('item_ledger에 위치와 잔액이 맞게 기록된다(장비는 worn)', async () => {
    const h = await newHero(app, 'warrior');
    const l = await getPool().query(
      'SELECT item_key, location, balance_after, delta FROM item_ledger WHERE character_id = $1 ORDER BY id',
      [h.dbId],
    );
    expect(l.rows).toEqual([
      { item_key: 'potion_hp', location: 'bag', balance_after: 3, delta: 3 },
      { item_key: 'potion_mp', location: 'bag', balance_after: 2, delta: 2 },
      { item_key: 'scroll_town', location: 'bag', balance_after: 1, delta: 1 },
      { item_key: 'eq_sword_wood', location: 'worn', balance_after: 1, delta: 1 },
    ]);
    const bind = await getPool().query("SELECT bind FROM character_items WHERE character_id = $1 AND location = 'worn'", [h.dbId]);
    expect(bind.rows[0].bind).toBe('character');
    await expectLedgerConsistent(h);
  });
});

describe('멱등성 공통 규칙', () => {
  it('4xx 실패는 request_log에 남지 않아 고쳐서 같은 request_id로 다시 보낼 수 있다', async () => {
    const h = await newHero(app); // 골드 100
    const rid = randomUUID();
    const body = { item_id: 'potion_hp', count: 5 }; // 150 필요
    const no = await post(app, h, '/shop/buy', body, rid);
    expect(no.status).toBe(422);
    const n = await getPool().query('SELECT count(*)::int AS n FROM request_log WHERE request_id = $1', [rid]);
    expect(n.rows[0].n).toBe(0);
    await seedGold(h, 100);
    const ok = await post(app, h, '/shop/buy', body, rid);
    expect(ok.status).toBe(200);
    expect(await goldOf(h)).toBe(50);
  });

  it('다른 캐릭터에 같은 request_id를 재사용하면 불일치', async () => {
    const a = await newHero(app);
    const rid = randomUUID();
    // 같은 계정의 두 번째 캐릭터
    const second = await request(app)
      .post('/characters')
      .set(auth(a.s))
      .send({ request_id: randomUUID(), name: '둘째용사', class: 'mage' });
    expect(second.status).toBe(201);
    const otherId = second.body.data.character.id as string;
    await post(app, a, '/items/use', { item_id: 'potion_hp' }, rid);
    const res = await request(app)
      .post(`/characters/${otherId}/items/use`)
      .set(auth(a.s))
      .send({ request_id: rid, item_id: 'potion_hp' });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });

  it('버전 헤더가 없으면 3단계 경로도 400, 오래된 데이터 버전은 426', async () => {
    const h = await newHero(app);
    const res = await request(app)
      .post(`/characters/${h.id}/shop/buy`)
      .set({ Authorization: `Bearer ${h.s.access}` })
      .send({ request_id: randomUUID(), item_id: 'potion_hp', count: 1 });
    expect(res.status).toBe(400);
    const old = await request(app)
      .post(`/characters/${h.id}/shop/buy`)
      .set({ ...auth(h.s), 'X-Data-Version': '0000000000000000' })
      .send({ request_id: randomUUID(), item_id: 'potion_hp', count: 1 });
    expect(old.status).toBe(426);
  });

  it('인증이 없으면 401', async () => {
    const h = await newHero(app);
    const res = await request(app).get(`/characters/${h.id}/dungeons`).set({ 'X-Client-Version': '0.2.0' });
    expect([400, 401]).toContain(res.status);
  });
});

describe('속도 제한', () => {
  it('3단계 경로는 IP 한도를 따로 쓴다(일반 한도에 걸리지 않는다)', async () => {
    // 일반 경로(회원가입·캐릭터 생성)가 한도에 걸리지 않도록 캐릭터를 먼저 만든 뒤 한도를 낮춘다
    app = buildApp();
    const hero = await newHero(app);
    app = buildApp({ RATE_GENERAL_IP_MAX: '1', RATE_ECONOMY_IP_MAX: '3' });
    const send = () => post(app, hero, '/items/use', { item_id: 'potion_hp' });
    const statuses = [(await send()).status, (await send()).status, (await send()).status, (await send()).status];
    expect(statuses).toEqual([200, 200, 200, 429]);
    app = buildApp();
  });

  it('캐릭터별 한도: 강화는 초당 N회, 초과하면 429와 Retry-After', async () => {
    app = buildApp({ RATE_ENHANCE_PER_SEC: '1' });
    const h = await newHero(app);
    const a = await post(app, h, '/enhance', { target: { worn_slot: 0 } });
    expect(a.status).not.toBe(429);
    const b = await post(app, h, '/enhance', { target: { worn_slot: 0 } });
    expect(b.status).toBe(429);
    expect(b.headers['retry-after']).toBeDefined();
    expect(b.body.errors.code).toBe('RATE_LIMITED');
    app = buildApp();
  });
});

describe('게임 데이터 로더(3단계 필드)', () => {
  function copyData(mutate: (dir: string) => void): string {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd3-'));
    for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
    mutate(dir);
    return dir;
  }
  const edit = (dir: string, file: string, fn: (j: Record<string, unknown>) => void) => {
    const p = path.join(dir, file);
    const j = JSON.parse(fs.readFileSync(p, 'utf8'));
    fn(j);
    fs.writeFileSync(p, JSON.stringify(j));
  };

  it('실제 데이터에서 필드 해골(fieldSkeleton)과 던전을 읽는다', () => {
    const eco = loadGameData(DATA_DIR).economy;
    expect(eco.monsters.get('skeleton')?.respawnSeconds).toBe(25);
    expect(eco.mapExtra.get('forest')?.fieldSpawns).toEqual([{ monsterId: 'skeleton', points: 15, monsterLevel: 1 }]);
    expect(eco.dungeons.byId.get('gold_vein')?.rooms).toHaveLength(4);
    expect(eco.enhance.steps.get('eq_sword_wood')).toHaveLength(20);
  });

  it('알 수 없는 필드는 무시한다', () => {
    const dir = copyData((d) => edit(d, 'monsters.json', (j) => ((j as { futureField?: number }).futureField = 1)));
    expect(() => loadGameData(dir)).not.toThrow();
  });

  it('필수 필드(fieldSkeleton)가 없거나 던전이 없는 몬스터를 가리키면 기동 실패', () => {
    const a = copyData((d) => edit(d, 'monsters.json', (j) => delete j.fieldSkeleton));
    expect(() => loadGameData(a)).toThrow(/monsters\.json/);
    const b = copyData((d) =>
      edit(d, 'dungeons.json', (j) => {
        const dg = (j.dungeons as { rooms: { groups: { monsterId: string }[] }[] }[])[0];
        (dg as { rooms: { groups: { monsterId: string }[] }[] }).rooms[0]!.groups[0]!.monsterId = 'ghost';
      }),
    );
    expect(() => loadGameData(b)).toThrow(/ghost/);
  });
});
