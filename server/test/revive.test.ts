// 12단계: 부활 코인(GET/POST /characters/{uuid}/revive)
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { resetClock, setNowAt } from './auctionHelpers';
import { newHero, type Hero } from './economyHelpers';
import { auth, buildApp, resetDb, shutdown } from './helpers';

const T0 = '2026-10-07T10:00:05Z'; // 게임 일자 2026-10-07 (06:00 KST 이후)
let app: Express;
beforeEach(async () => {
  await resetDb();
  resetClock();
  setNowAt(new Date(T0));
  app = buildApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const setLevel = (h: Hero, level: number) => getPool().query('UPDATE characters SET level = $2 WHERE id = $1', [h.dbId, level]);
const setCoins = (h: Hero, coins: number, day: string | null) =>
  getPool().query('UPDATE characters SET revive_coins = $2, revive_coin_day = $3::date WHERE id = $1', [h.dbId, coins, day]);
const state = async (h: Hero) =>
  (await getPool().query<{ revive_coins: number }>('SELECT revive_coins FROM characters WHERE id = $1', [h.dbId])).rows[0]!.revive_coins;
const logCount = async (h: Hero) =>
  Number((await getPool().query<{ n: string }>('SELECT count(*) AS n FROM revive_log WHERE character_id = $1', [h.dbId])).rows[0]!.n);
const use = (h: Hero, body: Record<string, unknown> = {}, id: string = h.id) =>
  request(app).post(`/characters/${id}/revive`).set(auth(h.s)).send({ request_id: randomUUID(), context: 'dungeon', ...body });
const get = (h: Hero, id: string = h.id) => request(app).get(`/characters/${id}/revive`).set(auth(h.s));

describe('부활 코인', () => {
  it('새 캐릭터는 코인 1개로 시작하고 GET이 상태를 돌려준다', async () => {
    const h = await newHero(app, 'warrior');
    const r = await get(h);
    expect(r.status).toBe(200);
    expect(r.body.success).toBe(true);
    expect(r.body.data).toEqual({ coins: 1, max: 5, free: true, free_until_level: 10, next_grant_at: '2026-10-07T21:00:00.000Z' });
  });

  it('Lv10 이하는 무료: 코인이 줄지 않고 로그만 남는다', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 10);
    const r = await use(h);
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ free: true, coins: 1, max: 5 });
    expect(await state(h)).toBe(1);
    expect(await logCount(h)).toBe(1);
  });

  it('Lv11은 코인 1개를 쓰고, 0이면 409 NO_REVIVE_COIN', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 11);
    const r = await use(h, { context: 'dungeon', map_id: 'dungeon_x' });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ free: false, coins: 0 });
    const r2 = await use(h);
    expect(r2.status).toBe(409);
    expect(r2.body.errors.code).toBe('NO_REVIVE_COIN');
    expect(await state(h)).toBe(0);
    expect(await logCount(h)).toBe(1);
  });

  it('사냥터(필드) 부활은 레벨과 상관없이 무료이고 코인을 쓰지 않는다', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 30);
    const r = await use(h, { context: 'field', map_id: 'forest' });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ free: true, coins: 1 });
    expect(await state(h)).toBe(1);
  });

  it('지난 게임 일수만큼 지급하고, 상한 5에서 버려진다', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 20);
    await setCoins(h, 0, '2026-10-05'); // 2일 지남
    expect((await get(h)).body.data.coins).toBe(2);
    await setCoins(h, 3, '2026-10-06'); // 1일 지남
    expect((await get(h)).body.data.coins).toBe(4);
    await setCoins(h, 4, '2026-09-01'); // 오래 지남 -> 상한
    expect((await get(h)).body.data.coins).toBe(5);
    await setCoins(h, 5, '2026-10-06');
    const r = await use(h);
    expect(r.body.data.coins).toBe(4);
    // 같은 게임 일자에는 다시 지급되지 않는다
    expect((await get(h)).body.data.coins).toBe(4);
  });

  it('같은 request_id를 두 번 보내면 한 번만 쓰고 같은 응답을 돌려준다', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 15);
    await setCoins(h, 3, '2026-10-07');
    const rid = randomUUID();
    const a = await use(h, { request_id: rid });
    const b = await use(h, { request_id: rid });
    expect(a.body.data).toEqual(b.body.data);
    expect(a.body.data.coins).toBe(2);
    expect(await state(h)).toBe(2);
    expect(await logCount(h)).toBe(1);
  });

  it('동시 요청 2개(같은 request_id)도 한 번만 쓴다', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 15);
    await setCoins(h, 3, '2026-10-07');
    const rid = randomUUID();
    const [a, b] = await Promise.all([use(h, { request_id: rid }), use(h, { request_id: rid })]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect(await state(h)).toBe(2);
    expect(await logCount(h)).toBe(1);
  });

  it('동시 요청 2개(다른 request_id)에 코인 1개면 하나만 성공한다', async () => {
    const h = await newHero(app, 'warrior');
    await setLevel(h, 15);
    const rs = await Promise.all([use(h), use(h)]);
    expect(rs.map((r) => r.status).sort()).toEqual([200, 409]);
    expect(await state(h)).toBe(0);
  });

  it('남의 캐릭터는 404, 잘못된 입력은 400', async () => {
    const a = await newHero(app, 'warrior');
    const b = await newHero(app, 'warrior');
    expect((await use(a, {}, b.id)).status).toBe(404);
    expect((await get(a, b.id)).status).toBe(404);
    expect((await use(a, { context: 'town' })).status).toBe(400);
    expect((await use(a, { coins: 99 })).status).toBe(400);
    expect((await use(a, { request_id: 'x' })).status).toBe(400);
  });
});
