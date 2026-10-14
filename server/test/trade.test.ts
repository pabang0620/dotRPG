// 1:1 교환: 요청 -> 수락 -> 제안 -> 잠금 -> 확정. 사기 방지(제안 변경 시 잠금 해제, 양쪽 확정, 재검증)와 이중 지급 없음.
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { advance, buildApp, goldNow, mk, resetClock, secondChar, MIN } from './auctionHelpers';
import { countOf, seedItem, type Hero } from './economyHelpers';
import { auth, resetDb, shutdown } from './helpers';

const SWORD = 'eq_sword_10_u';
const ORE = 'mat_ore';

let app: Express;
beforeEach(async () => {
  resetClock();
  await resetDb();
  await getPool().query('DELETE FROM trade_items');
  await getPool().query('DELETE FROM trades');
  app = buildApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const base = (h: Hero) => `/characters/${h.id}/trades`;
const send = (h: Hero, method: 'post' | 'put', path: string, body: Record<string, unknown> = {}) =>
  request(app)[method](`${base(h)}${path}`).set(auth(h.s)).send(body);
const current = (h: Hero) => request(app).get(`${base(h)}/current`).set(auth(h.s));

/** 캐릭터를 온라인으로 만든다(교환 요청은 온라인 상대에게만) */
async function online(h: Hero): Promise<void> {
  await getPool().query(
    `INSERT INTO online_sessions (account_id, character_id, family_id, map_id, map_since, map_changed_at, last_seen_at)
     VALUES ((SELECT account_id FROM characters WHERE id = $1), $1, $2, 'village', now(), now(), now())
     ON CONFLICT (account_id) DO UPDATE SET character_id = $1, ended_at = NULL, end_reason = NULL, last_seen_at = now()`,
    [h.dbId, randomUUID()],
  );
}
const nameOf = async (h: Hero): Promise<string> =>
  ((await getPool().query<{ name: string }>('SELECT name FROM characters WHERE id = $1', [h.dbId])).rows[0] as { name: string }).name;
const stackUuid = async (h: Hero, key: string, bind = 'none'): Promise<string> =>
  ((await getPool().query<{ uuid: string }>("SELECT uuid FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2 AND bind = $3", [h.dbId, key, bind])).rows[0] as { uuid: string }).uuid;

async function pair(goldA = 10_000, goldB = 10_000): Promise<{ a: Hero; b: Hero }> {
  const a = await mk(app, goldA);
  const b = await mk(app, goldB);
  await online(a);
  await online(b);
  return { a, b };
}

/** a가 b에게 요청하고 b가 수락한 거래 uuid */
async function open(a: Hero, b: Hero): Promise<string> {
  const c = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
  expect(c.status).toBe(201);
  const id = c.body.data.trade.id as string;
  expect((await send(b, 'post', `/${id}/accept`)).status).toBe(200);
  return id;
}

const offer = (h: Hero, id: string, version: number, gold: number, items: { item_uuid: string; count: number }[] = [], rid = randomUUID()) =>
  send(h, 'put', `/${id}/offer`, { request_id: rid, version, gold, items });
const lock = (h: Hero, id: string, version: number) => send(h, 'post', `/${id}/lock`, { version });
const confirm = (h: Hero, id: string, version: number, rid = randomUUID()) => send(h, 'post', `/${id}/confirm`, { request_id: rid, version });

/** a: 검 1 + 골드 500, b: 광석 3 + 골드 200 를 올리고 양쪽 잠금까지. version을 돌려준다 */
async function setupLocked(a: Hero, b: Hero, id: string): Promise<number> {
  await seedItem(a, SWORD, 1);
  await seedItem(b, ORE, 5);
  const o1 = await offer(a, id, 0, 500, [{ item_uuid: await stackUuid(a, SWORD), count: 1 }]);
  expect(o1.status).toBe(200);
  const o2 = await offer(b, id, 1, 200, [{ item_uuid: await stackUuid(b, ORE), count: 3 }]);
  expect(o2.status).toBe(200);
  expect((await lock(a, id, 2)).status).toBe(200);
  const l2 = await lock(b, id, 2);
  expect(l2.status).toBe(200);
  return l2.body.data.trade.version as number;
}

describe('정상 교환', () => {
  it('아이템과 골드가 양방향으로 한 번에 옮겨지고 원장이 남는다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    const v = await setupLocked(a, b, id);
    expect(v).toBe(2);

    const cur = await current(b);
    expect(cur.body.data.trade).toMatchObject({ id, state: 'active', role: 'target', version: 2 });
    expect(cur.body.data.trade.me).toMatchObject({ gold: 200, locked: true, confirmed: false });
    expect(cur.body.data.trade.other.items).toEqual([expect.objectContaining({ item_key: SWORD, count: 1 })]);

    const first = await confirm(a, id, v);
    expect(first.status).toBe(200);
    expect(first.body.data.trade.state).toBe('active');
    expect(first.body.data.delta).toBeUndefined();
    expect(await goldNow(a)).toBe(10_000);

    const done = await confirm(b, id, v);
    expect(done.status).toBe(200);
    expect(done.body.data.trade.state).toBe('completed');
    expect(done.body.data.delta.gold).toBe(10_000 + 500 - 200);

    expect(await goldNow(a)).toBe(10_000 - 500 + 200);
    expect(await goldNow(b)).toBe(10_000 + 500 - 200);
    expect(await countOf(a, SWORD)).toBe(0);
    expect(await countOf(b, SWORD)).toBe(1);
    expect(await countOf(a, ORE)).toBe(3);
    expect(await countOf(b, ORE)).toBe(2);

    const g = await getPool().query<{ reason: string; n: string; s: string }>(
      "SELECT reason, count(*) AS n, sum(delta) AS s FROM gold_ledger WHERE reason IN ('trade_out', 'trade_in') GROUP BY reason ORDER BY reason",
    );
    expect(g.rows).toEqual([{ reason: 'trade_in', n: '2', s: '700' }, { reason: 'trade_out', n: '2', s: '-700' }]);
    const it = await getPool().query<{ reason: string; n: string; s: string }>(
      "SELECT reason, count(*) AS n, sum(delta) AS s FROM item_ledger WHERE reason IN ('trade_out', 'trade_in') GROUP BY reason ORDER BY reason",
    );
    expect(it.rows).toEqual([{ reason: 'trade_in', n: '2', s: '4' }, { reason: 'trade_out', n: '2', s: '-4' }]);
    const refs = await getPool().query("SELECT DISTINCT ref FROM item_ledger WHERE reason IN ('trade_out', 'trade_in')");
    expect(refs.rows).toEqual([{ ref: id }]);

    // 상대가 확정해 끝난 결과는 폴링의 last_closed로 알려 준다
    const after = await current(a);
    expect(after.body.data.trade).toBeNull();
    expect(after.body.data.last_closed).toMatchObject({ id, state: 'completed' });
    // 끝난 뒤에는 새 거래를 열 수 있다
    expect((await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) })).status).toBe(201);
  });

  it('쌓이는 아이템은 상대 가방의 스택에 합쳐지고, 같은 request_id 확정 재전송은 한 번만 실행된다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    await seedItem(a, ORE, 4);
    await seedItem(b, ORE, 1);
    expect((await offer(a, id, 0, 0, [{ item_uuid: await stackUuid(a, ORE), count: 4 }])).status).toBe(200);
    expect((await lock(a, id, 1)).status).toBe(200);
    expect((await lock(b, id, 1)).status).toBe(200);
    const rid = randomUUID();
    expect((await confirm(a, id, 1)).status).toBe(200);
    const r1 = await confirm(b, id, 1, rid);
    const r2 = await confirm(b, id, 1, rid);
    expect(r1.status).toBe(200);
    expect(r2.status).toBe(200);
    expect(r2.headers['idempotent-replay']).toBe('true');
    expect(r2.body.data).toEqual(r1.body.data);
    expect(await countOf(b, ORE)).toBe(5);
    expect(await countOf(a, ORE)).toBe(0);
    const rows = await getPool().query("SELECT count(*) AS n FROM character_items WHERE character_id = $1 AND item_key = $2 AND location = 'bag'", [b.dbId, ORE]);
    expect(rows.rows[0]).toEqual({ n: '1' });
    // 다른 request_id로 다시 보낸 확정은 결과만 알려 주고 다시 옮기지 않는다
    const again = await confirm(b, id, 1);
    expect(again.status).toBe(200);
    expect(await countOf(b, ORE)).toBe(5);
  });
});

describe('사기 방지', () => {
  it('제안이 바뀌면 양쪽 잠금·확정이 풀리고 version이 오르며, 옛 version의 잠금·확정은 거절된다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    const v = await setupLocked(a, b, id);
    expect((await confirm(a, id, v)).status).toBe(200);
    // b가 몰래 제안을 바꾼다
    const changed = await offer(b, id, v, 1, []);
    expect(changed.status).toBe(200);
    expect(changed.body.data.trade).toMatchObject({ version: v + 1 });
    expect(changed.body.data.trade.me).toMatchObject({ locked: false, confirmed: false });
    expect(changed.body.data.trade.other).toMatchObject({ locked: false, confirmed: false });
    // a가 옛 version으로 확정하려 하면 막힌다
    const stale = await confirm(a, id, v);
    expect(stale.status).toBe(409);
    expect(stale.body.errors.code).toBe('TRADE_VERSION_MISMATCH');
    const stale2 = await lock(a, id, v);
    expect(stale2.status).toBe(409);
    // 오래된 version의 제안 교체도 거절
    expect((await offer(a, id, v, 0)).status).toBe(409);
    expect(await goldNow(a)).toBe(10_000);
    expect(await goldNow(b)).toBe(10_000);
  });

  it('한쪽만 확정하면 아무것도 옮겨지지 않고, 잠그기 전에는 확정할 수 없다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    await seedItem(a, SWORD, 1);
    expect((await offer(a, id, 0, 300, [{ item_uuid: await stackUuid(a, SWORD), count: 1 }])).status).toBe(200);
    const early = await confirm(a, id, 1);
    expect(early.status).toBe(409);
    expect(early.body.errors.code).toBe('TRADE_NOT_LOCKED');
    expect((await lock(a, id, 1)).status).toBe(200);
    expect((await confirm(a, id, 1)).body.errors.code).toBe('TRADE_NOT_LOCKED');
    expect((await lock(b, id, 1)).status).toBe(200);
    const one = await confirm(a, id, 1);
    expect(one.status).toBe(200);
    expect(one.body.data.trade).toMatchObject({ state: 'active', me: { confirmed: true }, other: { confirmed: false } });
    expect(await goldNow(a)).toBe(10_000);
    expect(await goldNow(b)).toBe(10_000);
    expect(await countOf(a, SWORD)).toBe(1);
    expect(await countOf(b, SWORD)).toBe(0);
  });

  it('확정 때 재검증: 그새 아이템을 써 버렸으면 실패하고 거래가 취소되며 아무것도 옮겨지지 않는다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    const v = await setupLocked(a, b, id);
    // a가 잠근 뒤에 검을 팔아 버렸다(가방에서 사라짐)
    await getPool().query("DELETE FROM character_items WHERE character_id = $1 AND item_key = $2", [a.dbId, SWORD]);
    expect((await confirm(a, id, v)).status).toBe(200);
    const res = await confirm(b, id, v);
    expect(res.status).toBe(409);
    expect(res.body.errors).toMatchObject({ code: 'TRADE_INVALID', who: 'other' });
    const row = await getPool().query("SELECT state, close_reason FROM trades WHERE uuid = $1", [id]);
    expect(row.rows[0]).toEqual({ state: 'failed', close_reason: 'items_changed' });
    expect(await goldNow(a)).toBe(10_000);
    expect(await goldNow(b)).toBe(10_000);
    expect(await countOf(b, ORE)).toBe(5);
    expect(await countOf(b, SWORD)).toBe(0);
    const led = await getPool().query("SELECT count(*) AS n FROM item_ledger WHERE reason IN ('trade_out', 'trade_in')");
    expect(led.rows[0]).toEqual({ n: '0' });
    // 닫힌 거래라 더 할 수 없고, 새 거래는 열 수 있다
    expect((await confirm(b, id, v)).status).toBe(409);
    expect((await current(b)).body.data.last_closed).toMatchObject({ id, state: 'failed' });
  });

  it('확정 때 재검증: 그새 골드를 써 버렸으면 실패한다', async () => {
    const { a, b } = await pair(1_000, 10_000);
    const id = await open(a, b);
    expect((await offer(a, id, 0, 800)).status).toBe(200);
    expect((await lock(a, id, 1)).status).toBe(200);
    expect((await lock(b, id, 1)).status).toBe(200);
    await getPool().query('UPDATE characters SET gold = 100 WHERE id = $1', [a.dbId]);
    expect((await confirm(b, id, 1)).status).toBe(200);
    const res = await confirm(a, id, 1);
    expect(res.status).toBe(409);
    expect(res.body.errors).toMatchObject({ code: 'TRADE_INVALID', who: 'me' });
    expect(await goldNow(b)).toBe(10_000);
    expect(await goldNow(a)).toBe(100);
  });

  it('귀속 아이템·귀속 하한이 있는 아이템·남의 아이템은 올릴 수 없다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    await seedItem(a, SWORD, 1, 'bag', 'account');
    const bound = await offer(a, id, 0, 0, [{ item_uuid: await stackUuid(a, SWORD, 'account'), count: 1 }]);
    expect(bound.status).toBe(422);
    expect(bound.body.errors.code).toBe('ITEM_BOUND');
    await seedItem(a, 'eq_sword_wood', 1, 'bag', 'none');
    const floor = await offer(a, id, 0, 0, [{ item_uuid: await stackUuid(a, 'eq_sword_wood'), count: 1 }]);
    expect(floor.status).toBe(422);
    expect(floor.body.errors.code).toBe('ITEM_BOUND');
    await seedItem(b, ORE, 2);
    const theirs = await offer(a, id, 0, 0, [{ item_uuid: await stackUuid(b, ORE), count: 1 }]);
    expect(theirs.status).toBe(404);
    expect(theirs.body.errors.code).toBe('ITEM_NOT_FOUND');
    await seedItem(a, ORE, 2);
    const many = await offer(a, id, 0, 0, [{ item_uuid: await stackUuid(a, ORE), count: 3 }]);
    expect(many.status).toBe(422);
    expect(many.body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    const gold = await offer(a, id, 0, 10_001);
    expect(gold.status).toBe(422);
    expect(gold.body.errors.code).toBe('NOT_ENOUGH_GOLD');
  });
});

describe('동시 요청', () => {
  it('양쪽이 동시에 확정해도 교환은 한 번만 실행된다(이중 지급 없음)', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    const v = await setupLocked(a, b, id);
    const res = await Promise.all([confirm(a, id, v), confirm(b, id, v)]);
    expect(res.map((r) => r.status)).toEqual([200, 200]);
    expect(res.filter((r) => r.body.data.trade.state === 'completed')).toHaveLength(1);
    expect(await goldNow(a)).toBe(10_000 - 500 + 200);
    expect(await goldNow(b)).toBe(10_000 + 500 - 200);
    expect(await countOf(a, SWORD)).toBe(0);
    expect(await countOf(b, SWORD)).toBe(1);
    expect(await countOf(a, ORE)).toBe(3);
    const led = await getPool().query("SELECT count(*) AS n FROM gold_ledger WHERE reason IN ('trade_out', 'trade_in')");
    expect(led.rows[0]).toEqual({ n: '4' });
  });

  it('같은 확정을 여러 번 동시에 보내도(서로 다른 request_id) 한 번만 실행된다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    const v = await setupLocked(a, b, id);
    expect((await confirm(a, id, v)).status).toBe(200);
    const res = await Promise.all([confirm(b, id, v), confirm(b, id, v), confirm(a, id, v)]);
    for (const r of res) expect(r.status).toBe(200);
    expect(await goldNow(a)).toBe(10_000 - 500 + 200);
    expect(await goldNow(b)).toBe(10_000 + 500 - 200);
    const led = await getPool().query("SELECT count(*) AS n FROM gold_ledger WHERE reason IN ('trade_out', 'trade_in')");
    expect(led.rows[0]).toEqual({ n: '4' });
  });

  it('한 캐릭터가 동시에 두 요청을 보내도 열린 거래는 하나다', async () => {
    const { a, b } = await pair();
    const c = await mk(app);
    await online(c);
    const res = await Promise.all([
      send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) }),
      send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(c) }),
    ]);
    expect(res.map((r) => r.status).sort()).toEqual([201, 409]);
    const open = await getPool().query("SELECT count(*) AS n FROM trades WHERE state = 'pending'");
    expect(open.rows[0]).toEqual({ n: '1' });
  });
});

describe('요청 규칙', () => {
  it('요청은 60초 뒤 만료되어 수락할 수 없고 상태가 expired로 남는다', async () => {
    const { a, b } = await pair();
    const c = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    const id = c.body.data.trade.id as string;
    expect((await current(b)).body.data.trade).toMatchObject({ id, state: 'pending', role: 'target' });
    advance(61_000);
    const late = await send(b, 'post', `/${id}/accept`);
    expect(late.status).toBe(410);
    expect(late.body.errors.code).toBe('TRADE_EXPIRED');
    const row = await getPool().query('SELECT state, close_reason FROM trades WHERE uuid = $1', [id]);
    expect(row.rows[0]).toEqual({ state: 'expired', close_reason: 'timeout' });
    const cur = await current(a);
    expect(cur.body.data.trade).toBeNull();
    // 만료된 뒤에는 둘 다 새 거래를 열 수 있다(온라인 신호는 시계와 무관하게 방금 갱신)
    await online(a);
    await online(b);
    expect((await send(b, 'post', '', { request_id: randomUUID(), target_name: await nameOf(a) })).status).toBe(201);
  });

  it('요청 만료는 폴링만 해도 닫힌다', async () => {
    const { a, b } = await pair();
    await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    advance(2 * MIN);
    expect((await current(b)).body.data.trade).toBeNull();
    expect((await current(a)).body.data.last_closed).toMatchObject({ state: 'expired' });
  });

  it('자기 계정 캐릭터, 오프라인, 없는 이름, 이미 거래 중인 상대, 차단 관계는 거절된다', async () => {
    const { a, b } = await pair();
    const mine = await secondChar(app, a);
    await online(mine);
    const self = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(mine) });
    expect(self.status).toBe(422);
    expect(self.body.errors.code).toBe('CANNOT_TARGET_SELF');
    expect((await send(a, 'post', '', { request_id: randomUUID(), target_name: '없는사람이름' })).body.errors.code).toBe('PLAYER_NOT_FOUND');

    const off = await mk(app);
    const offline = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(off) });
    expect(offline.status).toBe(422);
    expect(offline.body.errors.code).toBe('TARGET_OFFLINE');

    // 상대가 나를 차단: 오프라인과 같은 응답
    await getPool().query("INSERT INTO blocks (blocker_account_id, blocked_account_id, blocked_character_id, blocked_name) VALUES ((SELECT account_id FROM characters WHERE id = $1), (SELECT account_id FROM characters WHERE id = $2), $2, 'x')", [b.dbId, a.dbId]);
    const blockedByThem = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    expect(blockedByThem.body.errors.code).toBe('TARGET_OFFLINE');
    // 내가 차단한 사람
    const blockedByMe = await send(b, 'post', '', { request_id: randomUUID(), target_name: await nameOf(a) });
    expect(blockedByMe.status).toBe(409);
    expect(blockedByMe.body.errors.code).toBe('YOU_BLOCKED_TARGET');
    await getPool().query('DELETE FROM blocks');

    const c = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    expect(c.status).toBe(201);
    const x = await mk(app);
    await online(x);
    expect((await send(x, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) })).body.errors.code).toBe('TARGET_BUSY');
    expect((await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(x) })).body.errors.code).toBe('TRADE_ALREADY_OPEN');
  });

  it('수락·거절·취소: 받은 사람만 수락·거절, 둘 다 취소 가능, 남의 거래는 404', async () => {
    const { a, b } = await pair();
    const outsider = await mk(app);
    const c = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    const id = c.body.data.trade.id as string;
    expect((await send(a, 'post', `/${id}/accept`)).status).toBe(403);
    expect((await send(outsider, 'post', `/${id}/accept`)).status).toBe(404);
    expect((await send(b, 'post', `/${id}/decline`)).body.data.trade.state).toBe('declined');
    expect((await send(a, 'post', `/${id}/accept`)).status).toBe(403);
    expect((await send(b, 'post', `/${id}/accept`)).status).toBe(409);

    const id2 = await open(a, b);
    expect((await offer(a, id2, 0, 100)).status).toBe(200);
    const cancelled = await send(b, 'post', `/${id2}/cancel`);
    expect(cancelled.body.data.trade).toMatchObject({ state: 'cancelled', close_reason: 'target_cancel' });
    expect((await offer(a, id2, 1, 100)).status).toBe(409);
  });

  it('입력 오류: 8칸 초과, 잘못된 uuid, 알 수 없는 필드, 수락 전 제안은 거절된다', async () => {
    const { a, b } = await pair();
    const c = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    const id = c.body.data.trade.id as string;
    const early = await offer(a, id, 0, 0);
    expect(early.status).toBe(409);
    expect(early.body.errors.code).toBe('TRADE_NOT_ACTIVE');
    expect((await send(b, 'post', `/${id}/accept`)).status).toBe(200);
    const nine = Array.from({ length: 9 }, () => ({ item_uuid: randomUUID(), count: 1 }));
    expect((await offer(a, id, 0, 0, nine)).status).toBe(400);
    expect((await send(a, 'put', `/${id}/offer`, { request_id: randomUUID(), version: 0, gold: -1, items: [] })).status).toBe(400);
    expect((await send(a, 'put', `/${id}/offer`, { request_id: randomUUID(), version: 0, gold: 0, items: [], extra: 1 })).status).toBe(400);
    expect((await send(a, 'post', '/not-a-uuid/lock', { version: 0 })).status).toBe(400);
    expect((await send(a, 'post', '', { target_name: 'x' })).status).toBe(400);
    const empty = await lock(a, id, 0);
    expect(empty.status).toBe(422);
    expect(empty.body.errors.code).toBe('TRADE_EMPTY');
  });

  it('제안 교체 재전송(같은 request_id)은 같은 응답을 돌려주고 version을 또 올리지 않는다', async () => {
    const { a, b } = await pair();
    const id = await open(a, b);
    const rid = randomUUID();
    const r1 = await offer(a, id, 0, 50, [], rid);
    const r2 = await offer(a, id, 0, 50, [], rid);
    expect(r1.status).toBe(200);
    expect(r2.headers['idempotent-replay']).toBe('true');
    expect(r2.body.data).toEqual(r1.body.data);
    expect((await current(a)).body.data.trade.version).toBe(1);
  });
});
