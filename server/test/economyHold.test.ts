// 9단계 12절: 경제 속도 감시(시간별 집계, 상한 식, 평가, 모드, 정지 대상 7개 경로, 전파, 동시성)
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getConfig, loadConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { checkAuction, checkCharacter, createHold, propagate } from '../src/domains/antiabuse/holds';
import * as holdsRepo from '../src/domains/antiabuse/holdsRepository';
import { hourStart, HOUR_MS } from '../src/domains/antiabuse/incomeMeter';
import { runHoldCheck } from '../src/domains/antiabuse/holdSweep';
import { reconcileIncome } from '../src/domains/antiabuse/incomeReconcile';
import { auctionViolation, capsFor, evaluateBuckets, type Bucket } from '../src/domains/antiabuse/velocity';
import { getIncomeCaps } from '../src/gamedata/antiAbuseData';
import { setRng } from '../src/utils/rng';
import { auth, buildApp, createChar, randomName, resetDb, shutdown } from './helpers';
import { expectLedgerConsistent, fakeRng, get, newHero, post, seedItem, seedLevel, type Hero } from './economyHelpers';
import { listIron, mk, buyoutReq, listReq, cancelReq } from './auctionHelpers';

let app: Express = buildApp();
beforeEach(async () => {
  await resetDb();
  app = buildApp();
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setRng(null);
  await shutdown();
});

const accountOf = async (h: Hero): Promise<number> =>
  Number(((await getPool().query('SELECT account_id FROM characters WHERE id = $1', [h.dbId])).rows[0] as { account_id: string }).account_id);

interface SeedBucket {
  hoursAgo?: number;
  level?: number;
  xp?: number;
  gold?: number;
  itemValue?: number;
  ore?: number;
  essence?: number;
  active?: number;
  auctionInW?: number;
  auctionOut?: number;
}
/** 시간별 집계와 활동 시간을 직접 심는다(이 시각에서 hoursAgo시간 전 버킷) */
async function seedBucket(h: Hero, o: SeedBucket = {}): Promise<void> {
  const hour = hourStart(new Date(Date.now() - (o.hoursAgo ?? 0) * HOUR_MS));
  await getPool().query(
    `INSERT INTO income_hourly (character_id, hour_start, level_max, xp, gold_acq, item_value, ore, essence, auction_in_w, auction_out)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
     ON CONFLICT (character_id, hour_start) DO UPDATE SET xp = income_hourly.xp + EXCLUDED.xp`,
    [h.dbId, hour, o.level ?? 1, o.xp ?? 0, o.gold ?? 0, o.itemValue ?? 0, o.ore ?? 0, o.essence ?? 0, o.auctionInW ?? 0, o.auctionOut ?? 0],
  );
  if (o.active) await getPool().query('INSERT INTO play_time_hourly (character_id, hour_start, active_seconds) VALUES ($1, $2, $3)', [h.dbId, hour, o.active]);
}
/** 서버의 첫 버킷을 8일 전으로 만들어 3개 창이 모두 평가되게 한다(배포 직후 창 건너뜀 규칙) */
const anchor = async (h: Hero, daysAgo = 8): Promise<void> => seedBucket(h, { hoursAgo: daysAgo * 24 });
const holdsOf = async (h: Hero): Promise<{ state: string; kind: string; character_id: string | null }[]> =>
  (await getPool().query('SELECT state, kind, character_id::text FROM economy_holds WHERE account_id = $1 ORDER BY id', [await accountOf(h)])).rows;

async function insertHold(h: Hero, state = 'active', over: { scoped?: boolean; kind?: string; windowStart?: Date; windowEnd?: Date } = {}): Promise<string> {
  const r = await getPool().query<{ uuid: string }>(
    `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence, reviewed_at)
     VALUES ($1, $2, $3, $4, '24h', $5, $6, '{"metric":"xp","value":1,"cap":1}'::jsonb, CASE WHEN $4 IN ('shadow', 'active') THEN NULL ELSE now() END) RETURNING uuid`,
    [await accountOf(h), over.scoped === false ? null : h.dbId, over.kind ?? 'velocity', state, over.windowStart ?? new Date(Date.now() - 24 * HOUR_MS), over.windowEnd ?? new Date(Date.now() + HOUR_MS)],
  );
  return (r.rows[0] as { uuid: string }).uuid;
}

describe('시간별 집계(income_hourly)', () => {
  it('처치 경험치와 드롭 골드가 같은 트랜잭션에서 집계되고, 제외 사유(상점 판매 등)는 늘지 않는다', async () => {
    const h = await newHero(app);
    const k = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect(k.status).toBe(200);
    const drops = k.body.data.drops as { id: string }[];
    expect(drops.length).toBeGreaterThan(0);
    let row = (await getPool().query('SELECT xp, gold_acq, item_value, level_max FROM income_hourly WHERE character_id = $1', [h.dbId])).rows[0];
    expect(row).toMatchObject({ xp: '7', gold_acq: '0', item_value: '0' });
    expect((await post(app, h, '/drops/claim', { drop_ids: drops.map((d) => d.id) })).status).toBe(200);
    row = (await getPool().query('SELECT xp, gold_acq FROM income_hourly WHERE character_id = $1', [h.dbId])).rows[0];
    expect(row).toMatchObject({ xp: '7', gold_acq: '8' });
    // 상점 판매(shop_sell)는 제외: 획득 시점에 이미 환산했다
    await seedItem(h, 'potion_hp', 10);
    expect((await post(app, h, '/shop/sell', { item_key: 'potion_hp', count: 10 })).status).toBe(200);
    expect((await getPool().query('SELECT gold_acq, item_value FROM income_hourly WHERE character_id = $1', [h.dbId])).rows[0]).toMatchObject({ gold_acq: '8', item_value: '0' });
  });

  it('재료·장비 드롭은 상점 판매가로 환산되고 광석·정수 카운터가 오른다', async () => {
    setRng(fakeRng({ unit: 0, int: (min) => min }));
    const h = await newHero(app);
    const k = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    const ids = (k.body.data.drops as { id: string }[]).map((d) => d.id);
    expect((await post(app, h, '/drops/claim', { drop_ids: ids })).status).toBe(200);
    const row = (await getPool().query('SELECT ore, essence, item_value, gold_acq FROM income_hourly WHERE character_id = $1', [h.dbId])).rows[0];
    expect(row.ore).toBe(1);
    expect(row.essence).toBe(1);
    // 뼈 1 x 5 + 광석 20 + 정수 80 + 장비 판매가
    expect(Number(row.item_value)).toBeGreaterThanOrEqual(105);
    expect(Number(row.gold_acq)).toBeGreaterThan(0);
  });

  it('ECONOMY_HOLD_MODE=off: 정지는 만들지 않지만 버킷은 계속 쌓인다', async () => {
    const off = buildApp({ ECONOMY_HOLD_MODE: 'off' });
    const h = await newHero(off);
    await post(off, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect((await getPool().query('SELECT count(*) AS n FROM income_hourly WHERE character_id = $1', [h.dbId])).rows[0]?.n).toBe('1');
    await anchor(h);
    await seedBucket(h, { xp: 1_000_000 });
    expect((await checkCharacter(h.dbId)).violated).toBe(false);
    expect(await holdsOf(h)).toEqual([]);
    app = buildApp();
  });

  it('원장 대조(income-reconcile): 어긋난 버킷을 찾아 원장 값으로 고친다', async () => {
    const h = await newHero(app);
    await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect((await reconcileIncome({ fix: false })).mismatched).toBe(0);
    await getPool().query('UPDATE income_hourly SET xp = xp + 999 WHERE character_id = $1', [h.dbId]);
    const found = await reconcileIncome({ fix: false });
    expect(found).toMatchObject({ mismatched: 1 });
    expect(found.samples[0]).toMatchObject({ columns: ['xp'], ledger: { xp: 14 } });
    expect((await reconcileIncome({ fix: true })).fixed).toBe(1);
    expect((await getPool().query('SELECT xp FROM income_hourly WHERE character_id = $1', [h.dbId])).rows[0]?.xp).toBe('14');
    expect((await reconcileIncome({ fix: false })).mismatched).toBe(0);
  });
});

describe('상한 식 (12.2) 순수 함수', () => {
  const caps = () => getIncomeCaps() as NonNullable<ReturnType<typeof getIncomeCaps>>;
  const bucket = (o: Partial<Bucket> = {}): Bucket => ({
    hourStart: new Date(), levelMax: 18, activeSeconds: 3600, xp: 0, goldAcq: 0, itemValue: 0, ore: 0, essence: 0, epicPlus: 0, uniquePlus: 0, auctionIn: 0, auctionInW: 0, auctionOut: 0, ...o,
  });

  it('문서 예: 대역 5, 활동 4시간, 24시간 창의 XP 상한 3 x (55,200 x 4 + 2 x 43,477 + 2 x 10,092) = 983,814', () => {
    const buckets = [0, 1, 2, 3].map(() => bucket());
    const cap = capsFor(caps(), '24h', buckets, 18);
    expect(cap.xp).toBe(55_200 * 4 + 2 * 43_477 + 2 * 10_092);
    expect(3 * cap.xp).toBe(983_814);
    expect(evaluateBuckets(caps(), '24h', buckets.map((b, i) => (i === 0 ? { ...b, xp: 983_815 } : b)), 18, 3).over).toEqual(['xp']);
    expect(evaluateBuckets(caps(), '24h', buckets.map((b, i) => (i === 0 ? { ...b, xp: 983_814 } : b)), 18, 3).over).toEqual([]);
  });

  it('창 중간에 레벨이 올라도 버킷마다 그 시간의 대역을 쓴다. 일 단위 덩어리는 days_touched만큼', () => {
    const buckets = [bucket({ levelMax: 4 }), bucket({ levelMax: 18 })];
    const cap = capsFor(caps(), '1h', buckets, 18);
    expect(cap.xp).toBe(16_200 + 55_200 + 2 * 43_477 + 2 * 10_092);
    // 활동 시간 0이면 덩어리만 허용된다(HTTP 봇)
    expect(capsFor(caps(), '24h', [bucket({ activeSeconds: 0 })], 18).xp).toBe(2 * 43_477 + 2 * 10_092);
    expect(capsFor(caps(), '7d', [], 40).goldEq).toBe(8 * 12_240 + 3 * 4_500 + 2 * 18_000);
  });

  it('절대 하한: 광석 150은 임계를 넘어도 무시, 250은 위반. 경매 순유입은 비율 + 하한', () => {
    const none = bucket({ activeSeconds: 0 });
    expect(evaluateBuckets(caps(), '24h', [{ ...none, ore: 150 }], 18, 3).over).toEqual([]);
    expect(evaluateBuckets(caps(), '24h', [{ ...none, ore: 250 }], 18, 3).over).toEqual(['ore']);
    const v = auctionViolation([bucket({ auctionInW: 300_000, auctionOut: 10_000 })], 100_000, 0.5, 100_000);
    expect(v).toEqual({ net: 290_000, limit: 150_000, over: true });
    expect(auctionViolation([bucket({ auctionInW: 100_000 })], 100_000, 0.5, 100_000).over).toBe(false);
  });
});

describe('평가와 모드 (12.4)', () => {
  it('log_only(기본): 프레즌스 없는 캐릭터가 하한을 넘는 경험치를 얻으면 shadow 한 줄만 쓰고 막지 않는다. 같은 대상은 DEDUPE 시간당 한 줄', async () => {
    const h = await newHero(app);
    await seedLevel(h, 18);
    await anchor(h);
    await seedBucket(h, { level: 18, xp: 400_000 });
    const r = await checkCharacter(h.dbId);
    expect(r.violated).toBe(true);
    expect(r.created?.state).toBe('shadow');
    expect(await holdsOf(h)).toEqual([{ state: 'shadow', kind: 'velocity', character_id: String(h.dbId) }]);
    const ev = (await getPool().query("SELECT evidence, window_kind FROM economy_holds WHERE kind = 'velocity'")).rows[0];
    expect(ev.evidence).toMatchObject({ metric: 'xp', mult: 3, windows: { '1h': { over: ['xp'] } } });
    expect(ev.window_kind).toBe('1h');
    expect(await checkCharacter(h.dbId)).toMatchObject({ violated: true, created: null });
    expect(await holdsOf(h)).toHaveLength(1);
    // 막지 않는다
    await seedItem(h, 'potion_hp', 1);
    expect((await post(app, h, '/shop/sell', { item_key: 'potion_hp', count: 1 })).status).toBe(200);
  });

  it('정상 플레이(활동 시간이 충분하고 설계 속도)는 위반이 없다. 서버 첫 버킷이 창보다 새로우면 그 창은 건너뛴다', async () => {
    const h = await newHero(app);
    await anchor(h);
    await seedBucket(h, { level: 18, xp: 20_000, active: 3600 });
    expect((await checkCharacter(h.dbId)).violated).toBe(false);
    const young = await newHero(app);
    await getPool().query('DELETE FROM income_hourly');
    await seedBucket(young, { level: 18, xp: 900_000 });
    expect((await checkCharacter(young.dbId)).evals).toEqual([]);
    expect((await checkCharacter(young.dbId)).violated).toBe(false);
    // 첫 버킷이 2시간 전이면 1시간 창만 평가된다
    await seedBucket(young, { hoursAgo: 2 });
    const out = await checkCharacter(young.dbId);
    expect(out.evals.map((e) => e.window)).toEqual(['1h']);
    expect(out.violated).toBe(true);
  });

  it('enforce: active 정지가 생기고 정지 대상 경로가 403 ECONOMY_HOLD(수치 없음), 처치·줍기·취소는 정상', async () => {
    const strict = buildApp({ ECONOMY_HOLD_MODE: 'enforce' });
    const h = await mk(strict);
    const other = await mk(strict);
    const listed = await listIron(strict, h, { buyout: 1000, hours: 48 });
    const bought = await listIron(strict, other, { buyout: 1000, hours: 48 });
    await seedLevel(h, 18);
    await anchor(h);
    await seedBucket(h, { level: 18, xp: 400_000 });
    const r = await checkCharacter(h.dbId);
    expect(r.created?.state).toBe('active');

    const denied = async (res: request.Response): Promise<void> => {
      expect(res.status).toBe(403);
      expect(res.body.message).toBe('경제 활동이 일시적으로 제한되었습니다. 문의해 주세요.');
      expect(res.body.errors).toMatchObject({ code: 'ECONOMY_HOLD', scope: 'character' });
      expect(Object.keys(res.body.errors).sort()).toEqual(['code', 'scope', 'since']);
    };
    await denied(await post(strict, h, '/shop/sell', { item_key: 'potion_hp', count: 1 }));
    await denied(await post(strict, h, '/enhance', { target: { worn_slot: 0 } }));
    await denied(await post(strict, h, '/promote', { target: { worn_slot: 0 } }));
    await denied(await post(strict, h, '/starshop/pull', { count: 1 }));
    await denied(await post(strict, h, '/starshop/exchange', { item_id: 'aura_x' }));
    await denied(await post(strict, h, '/starshop/claim', { banner: 'aura', item_id: 'aura_x' }));
    await new Promise((r) => setTimeout(r, 1100)); // 별조각 경로는 캐릭터당 초당 3회로 고정되어 있다
    await denied(await post(strict, h, '/starshop/synth', { rarity: 'common', times: 1 }));
    await denied(await post(strict, h, `/mail/${randomUUID()}/claim`, {}));
    await denied(await post(strict, h, '/mail/claim-all', {}));
    await seedItem(h, 'eq_sword_10_u', 1);
    await denied(await listReq(strict, h, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 }));
    await denied(await buyoutReq(strict, h, bought));
    // 막지 않는 것: 처치, 드롭 줍기, 경매 취소(재료가 갇히지 않게)
    const k = await post(strict, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect(k.status).toBe(200);
    expect((await post(strict, h, '/drops/claim', { drop_ids: (k.body.data.drops as { id: string }[]).map((d) => d.id) })).status).toBe(200);
    expect((await cancelReq(strict, h, listed)).status).toBe(200);
    expect((await get(strict, h, '/economy-hold')).body.data).toMatchObject({ on_hold: true, scope: 'character' });
    await expectLedgerConsistent(h);
    app = buildApp();
  });

  it('P3: 정지가 없으면 on_hold=false, 남의 캐릭터는 404', async () => {
    const h = await newHero(app);
    const o = await newHero(app);
    expect((await get(app, h, '/economy-hold')).body.data).toEqual({ on_hold: false, since: null, scope: null });
    expect((await request(app).get(`/characters/${o.id}/economy-hold`).set(auth(h.s))).status).toBe(404);
    await insertHold(h, 'active', { scoped: false, kind: 'manual' });
    const on = (await get(app, h, '/economy-hold')).body.data;
    expect(on).toMatchObject({ on_hold: true, scope: 'account' });
    expect(Object.keys(on).sort()).toEqual(['on_hold', 'scope', 'since']);
  });

  it('경매 위반: 24시간 가중 순유입이 비율 + 하한을 넘으면 계정 단위 정지(kind=auction)', async () => {
    const strict = buildApp({ ECONOMY_HOLD_MODE: 'enforce' });
    const seller = await mk(strict);
    await anchor(seller);
    await seedBucket(seller, { level: 1, auctionInW: 900_000 });
    const hold = await checkAuction(await accountOf(seller));
    expect(hold?.state).toBe('active');
    expect(await holdsOf(seller)).toEqual([{ state: 'active', kind: 'auction', character_id: null }]);
    // 하한 안의 유입은 위반이 아니다
    const calm = await mk(strict);
    await seedBucket(calm, { level: 1, auctionInW: 50_000 });
    expect(await checkAuction(await accountOf(calm))).toBeNull();
    app = buildApp();
  });

  it('동시 평가: 같은 계정에 평가가 겹쳐도 막는 정지는 하나(유일 인덱스)', async () => {
    const strict = buildApp({ ECONOMY_HOLD_MODE: 'enforce' });
    const h = await newHero(strict);
    await seedLevel(h, 18);
    await anchor(h);
    await seedBucket(h, { level: 18, xp: 400_000 });
    const res = await Promise.all([checkCharacter(h.dbId), checkCharacter(h.dbId), createHold({ accountId: await accountOf(h), characterId: h.dbId, kind: 'velocity', evidence: {} })]);
    expect(res.every((x) => x !== null)).toBe(true);
    const active = (await holdsOf(h)).filter((x) => x.state === 'active');
    expect(active).toHaveLength(1);
    app = buildApp();
  });

  it('더티 워커: 소득이 바뀐 캐릭터만 평가한다(runHoldCheck)', async () => {
    const h = await newHero(app);
    await anchor(h);
    await seedBucket(h, { level: 1, xp: 400_000 });
    await runHoldCheck(); // 더티 집합 비움
    await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    const r = await runHoldCheck();
    expect(r).toEqual({ checked: 1, violated: 1 });
    expect(await holdsOf(h)).toHaveLength(1);
  });
});

describe('연결 계정으로 전파 (12.7)', () => {
  const sameDeviceAccounts = async (n: number, deviceHash: string, a: Express = app): Promise<Hero[]> => {
    const out: Hero[] = [];
    for (let i = 0; i < n; i++) {
      const loginId = `t${randomUUID().replace(/-/g, '').slice(0, 10)}`;
      const reg = await request(a).post('/auth/dev/register').set({ 'X-Client-Version': '0.2.0' }).send({ login_id: loginId, password: 'password-1234', device: { install_id: randomUUID(), device_hash: deviceHash } });
      const s = { loginId, password: 'password-1234', accountId: reg.body.data.account.id, access: reg.body.data.access_token, refresh: reg.body.data.refresh_token };
      const made = await createChar(a, s, randomName());
      const id = made.body.data.character.id as string;
      const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
      out.push({ s, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' });
    }
    return out;
  };

  it('같은 기기를 쓴 계정은 계정 단위 linked 정지(origin_hold_id), 계정이 너무 많은 공용 기기는 연결하지 않는다', async () => {
    const strict = buildApp({ ECONOMY_HOLD_MODE: 'enforce' });
    const dev = 'ab'.repeat(32);
    const [a, b, c] = await sameDeviceAccounts(3, dev, strict);
    await seedLevel(a as Hero, 18);
    await anchor(a as Hero);
    await seedBucket(a as Hero, { level: 18, xp: 400_000 });
    const r = await checkCharacter((a as Hero).dbId);
    expect(r.created?.created).toBe(true);
    for (const x of [b, c]) {
      const hs = (await getPool().query('SELECT kind, state, character_id, origin_hold_id::text FROM economy_holds WHERE account_id = $1', [await accountOf(x as Hero)])).rows;
      expect(hs).toEqual([{ kind: 'linked', state: 'active', character_id: null, origin_hold_id: String(r.created?.hold.id) }]);
    }
    // 7개 계정이 쓴 공용 기기(> HOLD_LINK_DEVICE_MAX_ACCOUNTS 6)는 연결하지 않는다
    await resetDb();
    const pc = await sameDeviceAccounts(7, 'cd'.repeat(32), strict);
    const first = pc[0] as Hero;
    await seedLevel(first, 18);
    await anchor(first);
    await seedBucket(first, { level: 18, xp: 400_000 });
    await checkCharacter(first.dbId);
    expect((await getPool().query("SELECT count(*) AS n FROM economy_holds WHERE kind = 'linked'")).rows[0]?.n).toBe('0');
    app = buildApp();
  });

  it('같은 Steam 소유자의 계정은 항상 연결되고, 전파는 HOLD_LINK_MAX_ACCOUNTS까지만', async () => {
    const strict = buildApp({ ECONOMY_HOLD_MODE: 'enforce', HOLD_LINK_MAX_ACCOUNTS: '2' });
    const heroes = await sameDeviceAccounts(4, 'ef'.repeat(32), strict);
    // 기기는 다르게 만들고(공용 기기 규칙과 무관), 같은 Steam 소유자로 묶는다
    await getPool().query('DELETE FROM account_devices');
    const owner = '76561190000000001';
    for (const [i, x] of heroes.entries()) {
      await getPool().query("INSERT INTO auth_identities (account_id, provider, subject, steam_owner_id) VALUES ($1, 'steam', $2, $3)", [await accountOf(x), `7656119000000010${i}`, i === 0 ? null : owner]);
    }
    // 소유자 본인(i=0)은 subject가 달라 키가 다르므로, 소유자 키를 맞춘다
    await getPool().query("UPDATE auth_identities SET subject = $1 WHERE account_id = $2 AND provider = 'steam'", [owner, await accountOf(heroes[0] as Hero)]);
    const first = heroes[1] as Hero;
    await seedLevel(first, 18);
    await anchor(first);
    await seedBucket(first, { level: 18, xp: 400_000 });
    const r = await checkCharacter(first.dbId);
    expect(r.created?.created).toBe(true);
    const linked = await getPool().query("SELECT account_id FROM economy_holds WHERE kind = 'linked'");
    expect(linked.rows).toHaveLength(2);
    app = buildApp();
  });

  it('해제 때 release_linked면 연결 정지도 함께 풀리고, 기준선 이전 버킷은 이후 평가에서 제외된다', async () => {
    const strict = buildApp({ ECONOMY_HOLD_MODE: 'enforce' });
    const [a, b] = await sameDeviceAccounts(2, '12'.repeat(32), strict);
    await seedLevel(a as Hero, 18);
    await anchor(a as Hero);
    await seedBucket(a as Hero, { level: 18, xp: 400_000 });
    const r = await checkCharacter((a as Hero).dbId);
    const origin = r.created?.hold as holdsRepo.HoldRow;
    expect(await propagate(origin)).toBe(0); // 이미 연결돼 있으므로 새로 만들지 않는다
    expect(await holdsRepo.linkedOf(getPool(), origin.id)).toHaveLength(1);
    const now = new Date();
    expect(await holdsRepo.release(getPool(), origin.id, 'tester', '확인함', now)).toBe(true);
    for (const id of await holdsRepo.linkedOf(getPool(), origin.id)) await holdsRepo.release(getPool(), id, 'tester', '확인함', now);
    expect(await holdsRepo.release(getPool(), origin.id, 'tester', '확인함', now)).toBe(false); // 멱등
    expect(await holdsRepo.baselineOf(getPool(), await accountOf(a as Hero), (a as Hero).dbId)).not.toBeNull();
    // 같은 버킷은 승인된 수입으로 보고 다시 정지하지 않는다
    expect((await checkCharacter((a as Hero).dbId)).violated).toBe(false);
    expect(b).toBeDefined();
    app = buildApp();
  });
});

describe('설정 가드', () => {
  it('ECONOMY_HOLD_MODE=enforce에는 income_caps.json이 필요하다(없는 경로면 기동 실패), 모드 값이 틀리면 실패', () => {
    const base = { DATABASE_URL: 'x', JWT_SECRET: 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw', MIN_CLIENT_VERSION: '0.1.0' };
    expect(() => loadConfig({ ...base, ECONOMY_HOLD_MODE: 'enforce', GAME_DATA_DIR: '/nonexistent-dir' })).toThrow(/income_caps/);
    expect(loadConfig({ ...base, ECONOMY_HOLD_MODE: 'log_only', GAME_DATA_DIR: '/nonexistent-dir' }).aa.hold.mode).toBe('log_only');
    expect(() => loadConfig({ ...base, ECONOMY_HOLD_MODE: 'maybe' })).toThrow(/ECONOMY_HOLD_MODE/);
    expect(() => loadConfig({ ...base, PRESENCE_KILL_MODE: 'strict' })).toThrow(/PRESENCE_KILL_MODE/);
    expect(() => loadConfig({ ...base, FIELD_CARRY_HARD_GAP: '5' })).toThrow(/FIELD_CARRY_HARD_GAP/);
    expect(() => loadConfig({ ...base, DEVICE_HASH_PEPPER: 'short' })).toThrow(/DEVICE_HASH_PEPPER/);
    expect(() => loadConfig({ ...base, DEVICE_HASH_PEPPER: base.JWT_SECRET })).toThrow(/JWT_SECRET/);
    expect(getConfig().aa.hold.mult).toBe(3);
  });
});
