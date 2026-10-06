// 9단계 7절: 구매자 자격, 양방향 쌍 한도와 대칭 락, 상점 품목 상한, 의심 거래 표시(체결은 성공), 같은 기기 쌍 차단 옵션
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import { limitsOf, type Reference } from '../src/domains/auction/auctionPricing';
import { advance, bidReq, buyoutReq, expectConserved, flagKinds, HOUR, listIron, listReq, mk, resetClock } from './auctionHelpers';
import { get, seedGold, seedItem, seedLevel, type Hero } from './economyHelpers';
import { buildApp, createChar, randomLoginId, randomName, resetDb, shutdown, ver } from './helpers';

let app: Express;
beforeEach(async () => {
  resetClock();
  await resetDb();
  app = buildApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

/** 기기 해시를 가진 계정의 영웅(골드 지급) */
async function heroOn(a: Express, deviceHash: string | null, gold = 50_000): Promise<Hero> {
  const loginId = randomLoginId();
  const reg = await request(a).post('/auth/dev/register').set(ver()).send({ login_id: loginId, password: 'password-1234', ...(deviceHash ? { device: { install_id: randomUUID(), device_hash: deviceHash } } : {}) });
  const s = { loginId, password: 'password-1234', accountId: reg.body.data.account.id, access: reg.body.data.access_token, refresh: reg.body.data.refresh_token };
  const made = await createChar(a, s, randomName());
  const id = made.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  const h: Hero = { s, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' };
  if (gold > 0) await seedGold(h, gold);
  return h;
}
const flagsOf = async (): Promise<string[][]> =>
  (await getPool().query('SELECT array_agg(f.flag ORDER BY f.flag) AS flags FROM auction_trades t LEFT JOIN auction_trade_flags f ON f.trade_id = t.id GROUP BY t.id ORDER BY t.id')).rows.map((r) => (r.flags as (string | null)[]).filter((x): x is string => x !== null));

describe('7.1 구매자 자격(BUYER_GATE)', () => {
  it('레벨 9 또는 계정 6일은 즉시 구매·입찰이 422 BUYER_GATE(의심 기록 gate), 정확히 Lv10·7일은 통과', async () => {
    const strict = buildApp({ AUCTION_BUYER_MIN_LEVEL: '10', AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS: '7' });
    const seller = await mk(strict);
    const buyer = await mk(strict);
    const id = await listIron(strict, seller, { buyout: 1000, start_bid: 100, hours: 48 });
    const low = await buyoutReq(strict, buyer, id);
    expect(low.status).toBe(422);
    expect(low.body.errors).toMatchObject({ code: 'BUYER_GATE', need_level: 10, need_days: 7 });
    expect((await bidReq(strict, buyer, id, 100)).body.errors.code).toBe('BUYER_GATE');
    expect(await flagKinds(buyer)).toEqual(['gate', 'gate']);
    const detail = await getPool().query("SELECT detail FROM auction_flags WHERE kind = 'gate' LIMIT 1");
    expect(detail.rows[0]?.detail).toMatchObject({ side: 'buyer', level: 1 });
    await seedLevel(buyer, 9);
    await getPool().query("UPDATE accounts SET created_at = now() - interval '8 days' WHERE id = (SELECT account_id FROM characters WHERE id = $1)", [buyer.dbId]);
    expect((await buyoutReq(strict, buyer, id)).body.errors.code).toBe('BUYER_GATE');
    await seedLevel(buyer, 10);
    await getPool().query("UPDATE accounts SET created_at = now() - interval '6 days' WHERE id = (SELECT account_id FROM characters WHERE id = $1)", [buyer.dbId]);
    expect((await buyoutReq(strict, buyer, id)).body.errors.code).toBe('BUYER_GATE');
    await getPool().query("UPDATE accounts SET created_at = now() - interval '7 days 1 minute' WHERE id = (SELECT account_id FROM characters WHERE id = $1)", [buyer.dbId]);
    expect((await buyoutReq(strict, buyer, id)).status).toBe(200);
    await expectConserved();
  });

  it('입찰 당시 통과한 자격은 정산(틱)에서 다시 묻지 않는다', async () => {
    const strict = buildApp({ AUCTION_BUYER_MIN_LEVEL: '10', AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS: '0' });
    const seller = await mk(strict);
    const buyer = await mk(strict);
    await seedLevel(buyer, 10);
    const id = await listIron(strict, seller, { buyout: 3000, start_bid: 100, hours: 12 });
    expect((await bidReq(strict, buyer, id, 100)).status).toBe(200);
    await seedLevel(buyer, 1);
    advance(13 * HOUR);
    await runAuctionTick();
    expect((await getPool().query("SELECT status FROM auction_listings WHERE uuid = $1", [id])).rows[0]?.status).toBe('sold');
    await expectConserved();
  });
});

describe('7.2 쌍 한도 양방향', () => {
  it('A->B로 한도만큼 체결된 뒤 B->A 구매도 같은 한도를 쓴다(건수)', async () => {
    const tight = buildApp({ AUCTION_PAIR_DAILY_TRADES: '2' });
    const a = await mk(tight);
    const b = await mk(tight);
    for (let i = 0; i < 2; i++) expect((await buyoutReq(tight, b, await listIron(tight, a, { buyout: 1000 }))).status).toBe(200);
    const reverse = await listIron(tight, b, { buyout: 1000, hours: 48 });
    const res = await buyoutReq(tight, a, reverse);
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'PAIR_LIMIT', limit: { trades: 2 } });
    // 다음 게임 일에는 다시 가능
    advance(25 * HOUR);
    expect((await buyoutReq(tight, a, reverse)).status).toBe(200);
  });

  it('금액도 양방향 합산이다. 일일 골드 한도 기본값은 1천만(결정 5)', async () => {
    expect(getConfig().auction.pairDailyGold).toBe(10_000_000);
    const tight = buildApp({ AUCTION_PAIR_DAILY_GOLD: '2500' });
    const a = await mk(tight);
    const b = await mk(tight);
    for (let i = 0; i < 2; i++) expect((await buyoutReq(tight, b, await listIron(tight, a, { buyout: 1000 }))).status).toBe(200);
    const res = await buyoutReq(tight, a, await listIron(tight, b, { buyout: 1000, hours: 48 }));
    expect(res.body.errors.code).toBe('PAIR_LIMIT');
    expect(await flagKinds(a)).toEqual(['pair_limit']);
  });

  it('반대 방향 두 요청이 동시에 와도 대칭 락으로 직렬화되어 한도를 넘는 체결이 없다', async () => {
    const tight = buildApp({ AUCTION_PAIR_DAILY_TRADES: '1' });
    const a = await mk(tight);
    const b = await mk(tight);
    const fromA = await listIron(tight, a, { buyout: 1000, hours: 48 });
    const fromB = await listIron(tight, b, { buyout: 1000, hours: 48 });
    const res = await Promise.all([buyoutReq(tight, b, fromA), buyoutReq(tight, a, fromB)]);
    expect(res.map((r) => r.status).sort()).toEqual([200, 422]);
    expect(res.find((r) => r.status === 422)?.body.errors.code).toBe('PAIR_LIMIT');
    expect((await getPool().query('SELECT count(*) AS n FROM auction_trades')).rows[0]?.n).toBe('1');
    await expectConserved();
  });
});

describe('7.3 상점 판매 품목의 기준가 상한(상점가 x 3)', () => {
  const vendor = (unit: number): Reference => ({ source: 'vendor', medianMilli: null, vendorUnit: unit });
  const history = (unit: number): Reference => ({ source: 'history', medianMilli: unit * 10_000, vendorUnit: null });

  it('상점이 파는 +0 품목은 max = 상점가 x 개수 x 3, 강화된 장비·상점에서 팔지 않는 재료는 기존 규칙', () => {
    expect(limitsOf(vendor(15), 10, 'mat_bone')).toEqual({ min: 150, max: 450 });
    expect(limitsOf(vendor(15), 10)).toEqual({ min: 150, max: 15_000 });
    expect(limitsOf(vendor(200), 1, 'eq_sword_10_c')).toEqual({ min: 200, max: 600 });
    expect(limitsOf(vendor(250), 1, 'eq_sword_10_c+3')).toEqual({ min: 250, max: 25_000 });
    expect(limitsOf(vendor(3), 1, 'wood')).toEqual({ min: 3, max: 300 });
    expect(limitsOf(vendor(31), 1, 'eq_sword_10_u')).toEqual({ min: 31, max: 3100 });
  });

  it('상한 이후 min > max가 되면 min = max로 정리된다(체결 기록이 부풀려도 상점가의 3배를 못 넘는다)', () => {
    // 중앙값 1000 -> 하한 200, 상한 5000이지만 상점가 15의 3배 = 45
    expect(limitsOf(history(1000), 1, 'mat_bone')).toEqual({ min: 45, max: 45 });
  });

  it('통합: mat_bone 10개는 450까지만 등록할 수 있다(451은 PRICE_OUT_OF_RANGE)', async () => {
    const seller = await mk(app);
    await seedItem(seller, 'mat_bone', 10);
    const over = await listReq(app, seller, { item_key: 'mat_bone', count: 10, buyout: 451, hours: 12 });
    expect(over.status).toBe(422);
    expect(over.body.errors).toMatchObject({ code: 'PRICE_OUT_OF_RANGE', max: 450 });
    expect((await listReq(app, seller, { item_key: 'mat_bone', count: 10, buyout: 450, hours: 12 })).status).toBe(201);
    const p = await get(app, seller, '/auction/prices/mat_bone?count=10');
    expect(p.body.data.limits).toEqual({ min: 150, max: 450 });
  });
});

describe('7.4 의심 거래 표시와 같은 기기 쌍 차단 옵션', () => {
  it('상한가는 CEILING_PRICE, 새 계정 + 하한 이상은 NEW_BUYER, 같은 기기는 SAME_DEVICE, 같은 IP는 SAME_IP. 체결 자체는 성공한다', async () => {
    const lowFloor = buildApp({ AUCTION_FLAG_MIN_PRICE: '500' });
    const dev = 'cc'.repeat(32);
    const seller = await heroOn(lowFloor, dev);
    const buyer = await heroOn(lowFloor, dev);
    expect((await buyoutReq(lowFloor, buyer, await listIron(lowFloor, seller, { buyout: 3100 }))).status).toBe(200);
    expect((await buyoutReq(lowFloor, buyer, await listIron(lowFloor, seller, { buyout: 1000 }))).status).toBe(200);
    // 두 번째 거래: 천장가 아님(1000 < 0.9 x 상한), 새 계정 + 500 이상
    expect(await flagsOf()).toEqual([['CEILING_PRICE', 'NEW_BUYER', 'SAME_DEVICE', 'SAME_IP'], ['NEW_BUYER', 'SAME_DEVICE', 'SAME_IP']]);
    const weights = await getPool().query('SELECT flag, weight_pct FROM auction_trade_flags WHERE trade_id = (SELECT min(id) FROM auction_trades) ORDER BY flag');
    expect(weights.rows).toEqual([
      { flag: 'CEILING_PRICE', weight_pct: 150 },
      { flag: 'NEW_BUYER', weight_pct: 200 },
      { flag: 'SAME_DEVICE', weight_pct: 300 },
      { flag: 'SAME_IP', weight_pct: 150 },
    ]);
    // 가중 수입은 플래그 최댓값(300%): 3100 - 155 + 보증금 31 = 2976 x 3
    const w = await getPool().query('SELECT auction_in, auction_in_w FROM income_hourly WHERE character_id = $1', [seller.dbId]);
    expect(w.rows).toEqual([{ auction_in: String(2976 + 960), auction_in_w: String(2976 * 3 + 960 * 3) }]);
    await expectConserved();
  });

  it('같은 Steam 소유자는 SAME_STEAM, 같은 쌍의 반복 거래는 PAIR_REPEAT(중복 INSERT 없음)', async () => {
    const repeat = buildApp({ AUCTION_FLAG_PAIR_REPEAT: '2' });
    const seller = await heroOn(repeat, null);
    const buyer = await heroOn(repeat, null);
    for (const [h, subject, owner] of [[seller, '76561190000000011', '76561190000000099'], [buyer, '76561190000000012', '76561190000000099']] as const) {
      await getPool().query("INSERT INTO auth_identities (account_id, provider, subject, steam_owner_id) VALUES ((SELECT account_id FROM characters WHERE id = $1), 'steam', $2, $3)", [h.dbId, subject, owner]);
    }
    await buyoutReq(repeat, buyer, await listIron(repeat, seller, { buyout: 1000 }));
    await buyoutReq(repeat, buyer, await listIron(repeat, seller, { buyout: 1000 }));
    const flags = await flagsOf();
    expect(flags[0]).toEqual(['SAME_IP', 'SAME_STEAM']);
    expect(flags[1]).toEqual(['PAIR_REPEAT', 'SAME_IP', 'SAME_STEAM']);
  });

  it('입찰 낙찰 정산(틱)도 의심 표시가 남는다', async () => {
    const seller = await heroOn(app, 'dd'.repeat(32));
    const buyer = await heroOn(app, 'dd'.repeat(32));
    const id = await listIron(app, seller, { buyout: 3000, start_bid: 100, hours: 12 });
    expect((await bidReq(app, buyer, id, 100)).status).toBe(200);
    advance(13 * HOUR);
    await runAuctionTick();
    expect(await flagsOf()).toEqual([['SAME_DEVICE', 'SAME_IP']]);
    await expectConserved();
  });

  it('AUCTION_BLOCK_SAME_DEVICE=true: 같은 기기 쌍의 구매가 403 SAME_DEVICE_TRADE로 막힌다(기본은 기록만)', async () => {
    const strict = buildApp({ AUCTION_BLOCK_SAME_DEVICE: 'true' });
    const dev = 'ee'.repeat(32);
    const seller = await heroOn(strict, dev);
    const same = await heroOn(strict, dev);
    const other = await heroOn(strict, 'ff'.repeat(32));
    const id = await listIron(strict, seller, { buyout: 1000, hours: 48 });
    const blocked = await buyoutReq(strict, same, id);
    expect(blocked.status).toBe(403);
    expect(blocked.body.errors.code).toBe('SAME_DEVICE_TRADE');
    expect(await flagKinds(same)).toEqual(['self_account']);
    expect((await buyoutReq(strict, other, id)).status).toBe(200);
  });
});
