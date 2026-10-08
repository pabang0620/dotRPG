// 14단계: 환불·차지백 결과물 회수(PA13)가 봉인된 상자 뽑기(sealed_pull)와 성장 패스(pass_buy)를 덮는다
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { setRng } from '../src/utils/rng';
import { adminApp, adminPost, makeAdmin, rid } from './opsHelpers';
import { countOf, post, seedLevel } from './economyHelpers';
import { auth, resetDb, shutdown } from './helpers';
import { heroOf, mock, newPayer, payApp, purchase, resetPay, sync } from './payHelpers';
import { rollOf, scriptRng } from './sealedBoxHelpers';

const app = payApp({ PAY_LIMIT_NEW_DAILY_STARS: '20000', PAY_LIMIT_NEW_MONTHLY_STARS: '50000', PAY_LIMIT_DAILY_STARS: '30000', PAY_LIMIT_MONTHLY_STARS: '90000' });
const admin = adminApp();
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

const inc = { cosmetics: true, gear: true, gauge: true };

async function refundAndRevoke(owner: Awaited<ReturnType<typeof makeAdmin>>, p: Awaited<ReturnType<typeof newPayer>>, o: { id: string; steam_order_id: string }) {
  mock.setStatus(o.steam_order_id, 'Refunded');
  await sync(app, p, o.id);
  const path = `/admin/payments/orders/${o.id}/revoke-outcomes`;
  const pv = await adminPost(admin, owner, path, { mode: 'preview', include: inc, note: '확정 부정' });
  expect(pv.status).toBe(200);
  const ap = await adminPost(admin, owner, path, { mode: 'apply', include: inc, preview_hash: pv.body.data.preview_hash, note: '확정 부정' }, rid());
  expect(ap.status).toBe(200);
  return { preview: pv.body.data.targets, applied: ap.body.data };
}

describe('결과물 회수 14단계', () => {
  it('봉인된 상자 뽑기: 남은 아이템을 걷고, 이미 쓴 만큼은 모자란 수량으로 보고한다', async () => {
    const owner = await makeAdmin('owner');
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const o = await purchase(app, p, 'stars_1000');
    scriptRng([...Array<number>(10).fill(rollOf('potion_hi_x5', false)), rollOf('potion_hi_x5', true)]);
    const pull = await post(app, hero, '/starshop/sealed/pull', { count: 11 });
    expect(pull.status).toBe(200);
    expect(await countOf(hero, 'potion_hp_hi')).toBe(60);
    // 일부를 이미 썼다: 상급 체력 물약 5개
    await getPool().query("UPDATE character_items SET count = count - 5 WHERE character_id = $1 AND item_key = 'potion_hp_hi'", [hero.dbId]);
    const r = await refundAndRevoke(owner, p, o);
    expect(r.preview.sweep_tickets).toBe(0);
    expect(r.applied).toMatchObject({ gear_removed: 115, gear_shortfall: 5, sweep_tickets_removed: 0, sweep_shortfall: 0 });
    expect(await countOf(hero, 'potion_hp_hi')).toBe(0);
    expect(await countOf(hero, 'potion_mp_hi')).toBe(0);
  });

  it('성장 패스: 보유·수령 기록을 지우고 받은 보상을 걷는다(쓴 만큼은 모자란 수량)', async () => {
    const owner = await makeAdmin('owner');
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const orders = [await purchase(app, p, 'stars_3000'), await purchase(app, p, 'stars_3000'), await purchase(app, p, 'stars_3000')];
    const buy = await request(app).post('/level-rewards/pass/buy').set(auth({ access: p.access } as never)).send({ request_id: randomUUID() });
    expect(buy.status).toBe(200);
    await seedLevel(hero, 10);
    expect((await post(app, hero, '/level-rewards/pass/claim', { level: 5 })).status).toBe(200);
    expect((await post(app, hero, '/level-rewards/pass/claim', { level: 10 })).status).toBe(200);
    expect(await countOf(hero, 'box_sealed')).toBe(10);
    await getPool().query("UPDATE character_items SET count = count - 2 WHERE character_id = $1 AND item_key = 'box_sealed'", [hero.dbId]);
    const r = await refundAndRevoke(owner, p, orders[0]!);
    expect(r.preview.pass).toBe(true);
    expect(r.applied).toMatchObject({ pass_removed: true, gear_removed: 9, gear_shortfall: 2 });
    expect(await countOf(hero, 'box_sealed')).toBe(0);
    expect(await countOf(hero, 'ticket_enh10')).toBe(0);
    for (const t of ['account_growth_pass', 'account_pass_claims']) {
      expect((await getPool().query(`SELECT count(*) AS n FROM ${t} WHERE account_id = $1`, [p.accountId])).rows[0]).toMatchObject({ n: '0' });
    }
  });
});
