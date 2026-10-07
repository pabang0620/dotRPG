// 9단계 감사 반영: 기기 키 대체, 채집·상자·드롭 프레즌스, 회수 보정(구간 정확도, 판매 대금 이중 회수, 아이템 상한, shadow 거절)
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { humanGroups } from '../src/domains/antiabuse/contribution';
import { hmacDevice } from '../src/domains/antiabuse/deviceRecords';
import { resetClock, setNowAt, advance } from './auctionHelpers';
import { buyoutReq, listIron } from './auctionHelpers';
import { newHero, post, seedGold, seedItem, anomalyKinds, type Hero } from './economyHelpers';
import { auth, buildApp, createChar, randomLoginId, randomName, resetDb, shutdown, ver, type Session } from './helpers';
import { adminApp, adminPost, makeAdmin, type TestAdmin } from './opsHelpers';

const T0 = '2026-10-07T10:00:05Z';
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

interface Player {
  s: Session;
  id: string;
  dbId: number;
}
async function player(a: Express, dev?: object): Promise<Player> {
  const loginId = randomLoginId();
  const reg = await request(a).post('/auth/dev/register').set(ver()).send({ login_id: loginId, password: 'password-1234', ...(dev ? { device: dev } : {}) });
  const s: Session = { loginId, password: 'password-1234', accountId: reg.body.data.account.id, access: reg.body.data.access_token, refresh: reg.body.data.refresh_token };
  const c = await createChar(a, s, randomName());
  const id = c.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { s, id, dbId: Number((r.rows[0] as { id: string }).id) };
}
const hero = (p: Player): Hero => ({ s: p.s, id: p.id, dbId: p.dbId, cls: 'warrior' });
const beat = (a: Express, p: Player, body: Record<string, unknown> = {}) =>
  request(a).post(`/characters/${p.id}/presence`).set(auth(p.s)).send({ map_id: 'village', auto_play: false, input_recent: true, ...body });

describe('1. device_hash 없이 install_id만 보내도 기기 한도와 사람 수가 적용된다', () => {
  it('같은 install_id로 device_hash 없이 세 계정이 접속하면 enforce에서 세 번째가 409, new_device 플래그와 기기 키 기록', async () => {
    const strict = buildApp({ DEVICE_LIMIT_MODE: 'enforce' });
    const install = randomUUID();
    const [a, b, c] = [await player(strict, { install_id: install }), await player(strict, { install_id: install }), await player(strict, { install_id: install })] as [Player, Player, Player];
    expect((await beat(strict, a)).body.data.device).toEqual({ online: 1, max: 2 });
    expect((await beat(strict, b)).status).toBe(200);
    const third = await beat(strict, c);
    expect(third.status).toBe(409);
    expect(third.body.errors.code).toBe('DEVICE_LIMIT');
    const key = hmacDevice(`install:${install}`);
    const rows = await getPool().query('SELECT DISTINCT device_hash FROM login_events WHERE device_hash IS NOT NULL');
    expect(rows.rows).toEqual([{ device_hash: key }]);
    const flags = await getPool().query("SELECT flags FROM login_events WHERE kind = 'register' LIMIT 1");
    expect(flags.rows[0]?.flags).toContain('device_missing');
  });

  it('이미 기기가 있는 계정이 처음 보는 기기 키로 로그인하면 new_device 플래그, 같은 기기면 없다', async () => {
    const dev1 = { install_id: randomUUID(), device_hash: createHash('sha256').update(randomBytes(8)).digest('hex') };
    const p = await player(app, dev1);
    const login = (device: object) => request(app).post('/auth/dev/login').set(ver()).send({ login_id: p.s.loginId, password: p.s.password, device });
    expect((await login(dev1)).status).toBe(200);
    expect((await login({ install_id: dev1.install_id, device_hash: createHash('sha256').update(randomBytes(8)).digest('hex') })).status).toBe(200);
    const ev = await getPool().query("SELECT flags FROM login_events WHERE kind = 'login' ORDER BY id");
    expect(ev.rows.map((r) => (r.flags as string[]).includes('new_device'))).toEqual([false, true]);
  });

  it('humanGroups: 기기 해시가 달라도 install_id가 같으면 한 사람, 둘 다 다르면 두 사람', () => {
    const k = (d: string | null, i: string | null) => ({ deviceHash: d, steamKey: null, installId: i });
    expect(humanGroups([k('a', 'x'), k('b', 'x')])).toHaveLength(1);
    expect(humanGroups([k('a', 'x'), k('b', 'y')])).toHaveLength(2);
    expect(humanGroups([k(null, null), k(null, null)])).toHaveLength(2);
  });
});

describe('2. 채집·상자·드롭 줍기의 프레즌스 검사', () => {
  const rock = { map_id: 'forest', node_id: 'forest:43:13' };
  const chest = { chest_id: 'canyon:46:40' };

  it('enforce: 프레즌스 없으면 409, 신호를 보내면 통과, 다른 맵으로 채집·상자는 422', async () => {
    const strict = buildApp({ PRESENCE_KILL_MODE: 'enforce' });
    const p = await player(strict, { install_id: randomUUID() });
    expect((await post(strict, hero(p), '/gathers', rock)).body.errors.code).toBe('PRESENCE_REQUIRED');
    expect((await post(strict, hero(p), '/chests/open', chest)).body.errors.code).toBe('PRESENCE_REQUIRED');
    expect((await post(strict, hero(p), '/drops/claim', { drop_ids: [randomUUID()] })).body.errors.code).toBe('PRESENCE_REQUIRED');
    await beat(strict, p, { map_id: 'village' });
    const wrongMap = await post(strict, hero(p), '/gathers', rock);
    expect(wrongMap.status).toBe(422);
    expect(wrongMap.body.errors.code).toBe('PRESENCE_MAP_MISMATCH');
    expect((await post(strict, hero(p), '/chests/open', chest)).body.errors.code).toBe('PRESENCE_MAP_MISMATCH');
    advance(2_000);
    await beat(strict, p, { map_id: 'forest' });
    expect((await post(strict, hero(p), '/gathers', rock)).status).toBe(200);
    advance(2_000);
    await beat(strict, p, { map_id: 'canyon' });
    expect((await post(strict, hero(p), '/chests/open', chest)).status).toBe(200);
    expect((await post(strict, hero(p), '/drops/claim', { drop_ids: [randomUUID()] })).status).toBe(200);
  });

  it('log(기본): 프레즌스 없이도 통과하고, 맵이 다르면 이상 기록만 남긴다', async () => {
    const p = await player(app, { install_id: randomUUID() });
    expect((await post(app, hero(p), '/chests/open', chest)).status).toBe(200);
    await beat(app, p, { map_id: 'village' });
    advance(2_000);
    const res = await post(app, hero(p), '/gathers', rock);
    expect(res.status).toBe(200);
    expect(await anomalyKinds(hero(p))).toContain('kill_presence');
  });
});

describe('3~7. 회수(H4) 보정', () => {
  let adm: Express;
  let owner: TestAdmin;
  beforeEach(async () => {
    resetClock();
    adm = adminApp();
    owner = await makeAdmin('owner');
  });
  const accountOf = async (h: Hero): Promise<number> => Number(((await getPool().query('SELECT account_id FROM characters WHERE id = $1', [h.dbId])).rows[0] as { account_id: string }).account_id);
  async function hold(h: Hero, state: string, windowStart: string): Promise<string> {
    const r = await getPool().query<{ uuid: string }>(
      `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence, reviewed_at)
       VALUES ($1, $2, 'velocity', $3, '24h', now() - $4::interval, now() + interval '1 hour', '{"metric":"xp","value":5,"cap":1}'::jsonb, CASE WHEN $3 IN ('shadow','active') THEN NULL ELSE now() END) RETURNING uuid`,
      [await accountOf(h), h.dbId, state, windowStart],
    );
    return (r.rows[0] as { uuid: string }).uuid;
  }
  const claw = (id: string, body: Record<string, unknown> = {}) =>
    adminPost(adm, owner, `/admin/economy/holds/${id}/clawback`, { note: '확정', gold: true, items: true, void_mail: true, ...body }, randomUUID());

  it('4. 골드 회수는 구간 안의 정확한 시각만 센다(구간 직전의 같은 시간대 수입은 회수하지 않는다)', async () => {
    const h = await newHero(app);
    const db = getPool();
    await db.query('UPDATE characters SET gold = 5000 WHERE id = $1', [h.dbId]);
    await db.query("INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref, created_at) VALUES ($1, 1000, 5000, 'drop_claim', $2, now() - interval '90 minutes')", [h.dbId, randomUUID()]);
    await db.query("INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref, created_at) VALUES ($1, 700, 5000, 'drop_claim', $2, now() - interval '20 minutes')", [h.dbId, randomUUID()]);
    const res = await claw(await hold(h, 'active', '1 hour'), { items: false, void_mail: false });
    expect(res.status).toBe(200);
    expect(res.body.data.gold_clawed).toBe(700);
  });

  it('3. 미수령 판매 대금 우편을 폐기하면 같은 대금을 지갑에서 또 걷지 않는다', async () => {
    const seller = await newHero(app);
    const buyer = await newHero(app);
    await seedGold(buyer, 5000);
    await seedGold(seller, 5000);
    const id = await listIron(app, seller, { buyout: 1000 });
    expect((await buyoutReq(app, buyer, id)).status).toBe(200);
    const payout = Number(((await getPool().query("SELECT gold FROM mails WHERE character_id = $1 AND kind = 'sold'", [seller.dbId])).rows[0] as { gold: string }).gold);
    expect(payout).toBeGreaterThan(0);
    // 구간 획득 = seedGold 5000 + 판매 대금. 대금은 우편 폐기로 회수하므로 지갑에서는 5000만 걷는다
    const res = await claw(await hold(seller, 'active', '2 hours'), { items: false });
    expect(res.status).toBe(200);
    expect(res.body.data.mails_voided).toBe(1);
    expect(res.body.data.gold_clawed).toBe(5000);
  });

  it('5. 아이템 회수는 구간 안 획득에서 구간 안 사용을 뺀 수량까지만(구간 전 보유분은 건드리지 않는다)', async () => {
    const h = await newHero(app);
    const db = getPool();
    await seedItem(h, 'mat_ore', 6); // 가방 6 (원장 +6 drop_claim, 지금)
    await db.query("INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after, created_at) VALUES ($1, 'mat_ore', 4, 'drop_claim', $2, 'bag', 4, now() - interval '5 hours')", [h.dbId, randomUUID()]);
    await db.query("INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after) VALUES ($1, 'mat_ore', -4, 'shop_sell', $2, 'bag', 6)", [h.dbId, randomUUID()]);
    // 구간(1시간) 안: +6 획득, -4 사용 = 순증가 2. 가방 6개 중 2개만 회수
    const res = await claw(await hold(h, 'active', '1 hour'), { gold: false, void_mail: false });
    expect(res.status).toBe(200);
    expect(res.body.data.items).toEqual([{ item_key: 'mat_ore', count: 2 }]);
    expect(Number(((await db.query("SELECT count FROM character_items WHERE character_id = $1 AND item_key = 'mat_ore'", [h.dbId])).rows[0] as { count: string }).count)).toBe(4);
  });

  it('5. 구간 안에서 얻은 다른 강화 단계 키만 정확히 회수하고 구간 전 단계 개체는 건드리지 않는다', async () => {
    const h = await newHero(app);
    const db = getPool();
    await seedItem(h, 'eq_sword_10_u+3', 1);
    await seedItem(h, 'eq_sword_10_u', 1);
    await db.query("INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after, created_at) VALUES ($1, 'eq_sword_10_u', 1, 'drop_claim', $2, 'bag', 2, now() - interval '5 hours')", [h.dbId, randomUUID()]);
    await seedItem(h, 'eq_sword_10_u', 1); // 구간 밖에서 얻은 1개 + 구간 안 +1 = 가방 3개, 구간 안 순증가 2(seed 두 번)
    const res = await claw(await hold(h, 'active', '1 hour'), { gold: false, void_mail: false });
    expect(res.status).toBe(200);
    const taken = Object.fromEntries((res.body.data.items as { item_key: string; count: number }[]).map((x) => [x.item_key, x.count]));
    expect(taken['eq_sword_10_u+3']).toBe(1);
    expect(taken['eq_sword_10_u']).toBe(2);
  });

  it('6. shadow(log_only) 정지는 회수할 수 없다(409 HOLD_SHADOW), 상태는 그대로', async () => {
    const h = await newHero(app);
    await seedGold(h, 500);
    const id = await hold(h, 'shadow', '1 hour');
    const res = await claw(id);
    expect(res.status).toBe(409);
    expect(res.body.errors.code).toBe('HOLD_SHADOW');
    expect(((await getPool().query('SELECT state FROM economy_holds WHERE uuid = $1', [id])).rows[0] as { state: string }).state).toBe('shadow');
  });

  it('동시에 같은 정지를 두 번 회수해도 한 번만 걷는다', async () => {
    const h = await newHero(app);
    await seedGold(h, 900);
    const id = await hold(h, 'active', '1 hour');
    const res = await Promise.all([claw(id), claw(id)]);
    expect(res.map((r) => r.status).sort()).toEqual([200, 409]);
    const g = await getPool().query("SELECT count(*) AS n FROM gold_ledger WHERE reason = 'admin_clawback' AND character_id = $1", [h.dbId]);
    expect((g.rows[0] as { n: string }).n).toBe('1');
  });
});
