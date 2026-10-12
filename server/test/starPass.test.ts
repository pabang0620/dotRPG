// 별빛 정기권(Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md B): 결제 지급(별조각 300 + 30일), 일일 지급(별조각 30 + 봉인된 상자 우편),
// 기간 상한(60일), 환불 시 기간 회수. 가짜 Steam(mock)으로 결제한다.
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { post } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { authP, buy, expectWalletConsistent, heroOf, ledgerCount, mock, newPayer, offerOf, payApp, purchase, resetPay, sync, walletState } from './payHelpers';

const app = payApp();
const PASS = 'star_pass_30';
const DAY = 86_400_000;

beforeAll(resetDb);
beforeEach(() => resetPay());
afterEach(() => setClockOverride(null));
afterAll(shutdown);

const status = (p: Awaited<ReturnType<typeof newPayer>>) => request(app).get('/star-pass').set(authP(p));

async function boxMails(characterDbId: number): Promise<number> {
  const r = await getPool().query<{ n: string }>(
    "SELECT count(*) AS n FROM mails WHERE character_id = $1 AND kind = 'system' AND system_code = 'attendance' AND item_key = 'box_sealed'",
    [characterDbId],
  );
  return Number((r.rows[0] as { n: string }).n);
}

describe('별빛 정기권', () => {
  it('1. 구매: 별조각 300(유료) 즉시, 30일 활성. 상품 목록에 일수가 보인다', async () => {
    const p = await newPayer(app);
    const offer = await offerOf(app, p, PASS);
    expect(offer).toMatchObject({ product_id: PASS, stars: 300, purchasable: true });
    expect((offer as unknown as { pass_days: number }).pass_days).toBe(30);
    await purchase(app, p, PASS);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 300, paid: 300 });
    const s = await status(p);
    expect(s.status).toBe(200);
    expect(s.body.data).toMatchObject({ active: true, days_left: 30, today_claimed: false, daily: { stars: 30, item_key: 'box_sealed', count: 1 } });
    await expectWalletConsistent(p.accountId);
  });

  it('2. 일일 지급: 하루 한 번(같은 요청은 같은 응답, 새 요청은 granted=false), 다음 초기화 구간에 다시', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await purchase(app, p, PASS);
    const rid = randomUUID();
    const a = await post(app, hero, '/star-pass/daily', {}, rid);
    expect(a.status).toBe(200);
    expect(a.body.data).toMatchObject({ granted: true, stars: 30, item: { item_key: 'box_sealed', count: 1 }, balance: 330 });
    const replay = await post(app, hero, '/star-pass/daily', {}, rid);
    expect(replay.body.data).toEqual(a.body.data);
    const again = await post(app, hero, '/star-pass/daily', {});
    expect(again.body.data).toMatchObject({ granted: false, stars: 0, balance: 330 });
    expect(await ledgerCount(p.accountId, 'pass_daily')).toBe(1);
    expect(await boxMails(hero.dbId)).toBe(1);
    expect((await status(p)).body.data.today_claimed).toBe(true);

    const base = Date.now();
    setClockOverride(() => new Date(base + DAY));
    const next = await post(app, hero, '/star-pass/daily', {});
    expect(next.body.data).toMatchObject({ granted: true, balance: 360 });
    expect(await ledgerCount(p.accountId, 'pass_daily')).toBe(2);
    expect(await boxMails(hero.dbId)).toBe(2);
    await expectWalletConsistent(p.accountId);
  });

  it('3. 정기권 없으면 일일 지급 409 PASS_REQUIRED, 만료 뒤에도 같다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const none = await post(app, hero, '/star-pass/daily', {});
    expect(none.status).toBe(409);
    expect(none.body.errors.code).toBe('PASS_REQUIRED');
    await purchase(app, p, PASS);
    const base = Date.now();
    setClockOverride(() => new Date(base + 31 * DAY));
    const late = await post(app, hero, '/star-pass/daily', {});
    expect(late.status).toBe(409);
    expect((await status(p)).body.data).toMatchObject({ active: false, days_left: 0 });
  });

  it('4. 재구매는 남은 기간에 더한다(60일까지). 넘으면 목록에서 막히고 주문은 409 PASS_TOO_LONG', async () => {
    const p = await newPayer(app);
    await purchase(app, p, PASS);
    await purchase(app, p, PASS);
    expect((await status(p)).body.data.days_left).toBe(60);
    const offer = await offerOf(app, p, PASS);
    expect(offer).toMatchObject({ purchasable: false, block: 'PASS_TOO_LONG' });
    const res = await buy(app, p, PASS);
    expect(res.status).toBe(409);
    expect(res.body.errors.code).toBe('PASS_TOO_LONG');
    // 별조각 상품은 그대로 살 수 있다
    expect(await offerOf(app, p, 'stars_300')).toMatchObject({ purchasable: true });
  });

  it('5. 환불: 그 주문이 더한 기간만 뺀다(별조각 회수는 기존 경로), 받은 일일 지급은 그대로', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const first = await purchase(app, p, PASS);
    await purchase(app, p, PASS);
    expect((await post(app, hero, '/star-pass/daily', {})).body.data.granted).toBe(true);
    mock.setStatus(first.steam_order_id, 'Refunded');
    const r = await sync(app, p, first.id);
    expect(r.body.data.order).toMatchObject({ state: 'refunded' });
    expect((await status(p)).body.data).toMatchObject({ active: true, days_left: 30 });
    const g = await getPool().query<{ kind: string; n: string }>(
      'SELECT kind, count(*) AS n FROM star_pass_grants WHERE account_id = $1 GROUP BY kind ORDER BY kind',
      [p.accountId],
    );
    expect(g.rows.map((x) => [x.kind, Number(x.n)])).toEqual([['grant', 2], ['revoke', 1]]);
    expect(await ledgerCount(p.accountId, 'pass_daily')).toBe(1);
    // 같은 주문을 다시 확인해도 두 번 빼지 않는다
    await sync(app, p, first.id);
    expect((await status(p)).body.data.days_left).toBe(30);
    await expectWalletConsistent(p.accountId);
  });
});
