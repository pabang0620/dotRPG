// 14단계: 확률 상자·봉인된 상자 아이템 열기, 새 소모품 사용
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { setRng } from '../src/utils/rng';
import * as sealed from '../src/domains/sealedbox/sealedBoxService';
import { countOf, expectLedgerConsistent, post, seedItem } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { payApp, resetPay } from './payHelpers';
import { newPlayer, openApi, rollOf, scriptRng, sealedInfo } from './sealedBoxHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterEach(() => setRng(null));
afterAll(async () => {
  setRng(null);
  await shutdown();
});

describe('확률 상자 열기', () => {
  it('성공(굴림 < chance): +10 강화권 1장, 상자 1개 소모, 기록', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_enh10_30', 2);
    scriptRng([0], 29); // 29 < 30
    const r = await openApi(app, pl, 'box_enh10_30');
    expect(r.status).toBe(200);
    expect(r.body.data.item_key).toBe('box_enh10_30');
    expect(r.body.data.results).toEqual([expect.objectContaining({ item_key: 'ticket_enh10', count: 1, boosted: false })]);
    expect(r.body.data.booster).toBeUndefined();
    expect(await countOf(pl.hero, 'box_enh10_30')).toBe(1);
    expect(await countOf(pl.hero, 'ticket_enh10')).toBe(1);
    const log = await getPool().query("SELECT source, item_key, rate FROM sealed_pulls WHERE account_id = $1", [pl.p.accountId]);
    expect(log.rows).toEqual([{ source: 'luck_box', item_key: 'ticket_enh10', rate: 30 }]);
    await expectLedgerConsistent(pl.hero);
  });

  it('실패(굴림 >= chance): 마력 정수 20개', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_enh12_10', 1);
    scriptRng([0], 10); // 10 < 10 아님
    const r = await openApi(app, pl, 'box_enh12_10');
    expect(r.status).toBe(200);
    expect(r.body.data.results[0]).toMatchObject({ item_key: 'mat_essence', count: 20 });
    expect(await countOf(pl.hero, 'box_enh12_10')).toBe(0);
    expect(await countOf(pl.hero, 'mat_essence')).toBe(20);
    expect(await countOf(pl.hero, 'ticket_enh12')).toBe(0);
  });

  it('상자가 없으면 NOT_ENOUGH_ITEMS, 열 수 없는 아이템은 NOT_OPENABLE, 잘못된 입력은 422', async () => {
    const pl = await newPlayer(app);
    expect((await openApi(app, pl, 'box_enh10_30')).body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    await seedItem(pl.hero, 'potion_hp', 1);
    expect((await openApi(app, pl, 'potion_hp')).body.errors.code).toBe('NOT_OPENABLE');
    expect((await post(app, pl.hero, '/items/open', { item_key: 'box_sealed', count: 9 })).status).toBe(400);
  });

  it('같은 request_id 재전송은 한 번만 소모, 동시 열기는 상자 1개로 한 번만 성공', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_enh10_30', 1);
    scriptRng([0], 0);
    const rid = randomUUID();
    const a = await openApi(app, pl, 'box_enh10_30', rid);
    const b = await openApi(app, pl, 'box_enh10_30', rid);
    expect(b.body.data).toEqual(a.body.data);
    expect(await countOf(pl.hero, 'ticket_enh10')).toBe(1);

    await seedItem(pl.hero, 'box_enh15_50', 1);
    const rs = await Promise.allSettled([
      sealed.openItem(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), item_key: 'box_enh15_50' }),
      sealed.openItem(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), item_key: 'box_enh15_50' }),
    ]);
    expect(rs.filter((r) => r.status === 'fulfilled')).toHaveLength(1);
    expect((rs.find((r) => r.status === 'rejected') as PromiseRejectedResult).reason).toMatchObject({ code: 'NOT_ENOUGH_ITEMS' });
    expect(await countOf(pl.hero, 'box_enh15_50')).toBe(0);
  });
});

describe('봉인된 상자 아이템', () => {
  it('열면 같은 표로 굴리고 계정 부스터 게이지가 1 오른다, 아이템 1개 소모', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_sealed', 3);
    scriptRng([rollOf('essence_x10', false)]);
    const r = await openApi(app, pl, 'box_sealed');
    expect(r.status).toBe(200);
    expect(r.body.data.results[0]).toMatchObject({ item_key: 'mat_essence', count: 10, boosted: false });
    expect(r.body.data.booster).toEqual({ gauge: 1, next_boosted: false });
    expect(await countOf(pl.hero, 'box_sealed')).toBe(2);
    expect(await countOf(pl.hero, 'mat_essence')).toBe(10);
    expect((await sealedInfo(app, pl)).body.data.booster.gauge).toBe(1);
    await expectLedgerConsistent(pl.hero);
  });

  it('게이지가 가득 차면(캐시샵 뽑기와 같은 게이지) 다음 열기가 부스터', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_sealed', 12);
    scriptRng([rollOf('protect_x1', false)]);
    for (let i = 0; i < 10; i++) await sealed.openItem(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), item_key: 'box_sealed' });
    scriptRng([rollOf('protect_x1', true)]);
    const r = await openApi(app, pl, 'box_sealed');
    expect(r.body.data.results[0]).toMatchObject({ boosted: true, count: 2 });
    expect(r.body.data.booster).toEqual({ gauge: 0, next_boosted: false });
  });

  it('/items/use 로는 열 수 없다(USE_OPEN_INSTEAD), 상자는 그대로', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_sealed', 1);
    const r = await post(app, pl.hero, '/items/use', { item_id: 'box_sealed' });
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('USE_OPEN_INSTEAD');
    expect(await countOf(pl.hero, 'box_sealed')).toBe(1);
  });
});

describe('새 소모품 사용', () => {
  it('투지의 주문서·상급 물약은 1개 소모, 강화권은 사용 불가', async () => {
    const pl = await newPlayer(app);
    for (const k of ['scroll_power', 'potion_hp_hi', 'potion_mp_hi', 'ticket_enh10']) await seedItem(pl.hero, k, 2);
    for (const k of ['scroll_power', 'potion_hp_hi', 'potion_mp_hi']) {
      const r = await post(app, pl.hero, '/items/use', { item_id: k });
      expect(r.status).toBe(200);
      expect(await countOf(pl.hero, k)).toBe(1);
    }
    const t = await post(app, pl.hero, '/items/use', { item_id: 'ticket_enh10' });
    expect(t.status).toBe(422);
    expect(t.body.errors.code).toBe('NOT_USABLE');
    expect(await countOf(pl.hero, 'ticket_enh10')).toBe(2);
  });
});
