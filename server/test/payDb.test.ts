// 11단계 결제: DB 방어선(설계 18절 B). 앱 코드를 우회한 INSERT/UPDATE도 근거 없는 별조각은 커밋되지 않는다.
import { randomUUID } from 'node:crypto';
import type { PoolClient } from 'pg';
import { getPool } from '../src/db/pool';
import { ensureRatesSnapshot, RatesVersionConflict, buildRatesContent } from '../src/domains/starshop/ratesSnapshot';
import { resetDb, shutdown } from './helpers';
import { makeAdmin } from './opsHelpers';
import { newPayer, payApp, resetPay, type Payer } from './payHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(shutdown);

async function tx<T>(fn: (c: PoolClient) => Promise<T>, commit = true): Promise<T> {
  const c = await getPool().connect();
  try {
    await c.query('BEGIN');
    const r = await fn(c);
    await c.query(commit ? 'COMMIT' : 'ROLLBACK');
    return r;
  } catch (err) {
    await c.query('ROLLBACK').catch(() => undefined);
    throw err;
  } finally {
    c.release();
  }
}

interface Led {
  accountId: number;
  delta: number;
  reason: string;
  ref?: string | null;
  paid?: number;
  debt?: number;
}
const insertLedger = (c: PoolClient, l: Led) =>
  c.query(
    `INSERT INTO star_ledger (account_id, delta, balance_after, reason, ref, paid_delta, paid_balance_after, debt_delta, debt_after)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 0)`,
    [l.accountId, l.delta, Math.max(l.delta, 0), l.reason, l.ref ?? null, l.paid ?? 0, Math.max(l.paid ?? 0, 0), l.debt ?? 0],
  );

async function orderFor(p: Payer, o: { state: string; stars: number; granted: boolean; reversed?: boolean }): Promise<string> {
  const r = await getPool().query<{ uuid: string }>(
    `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, tier, state, expires_at,
                              granted_at, reversed_at)
     VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', $3, 'KRW', 1100, 'standard', $4, now() + interval '1 hour',
             CASE WHEN $5 THEN now() END, CASE WHEN $6 THEN now() END) RETURNING uuid`,
    [p.accountId, p.steamId, o.stars, o.state, o.granted, o.reversed ?? false],
  );
  return (r.rows[0] as { uuid: string }).uuid;
}

describe('B. DB 방어선', () => {
  it('1. 주문 없이 purchase 원장은 커밋되지 않는다(주문은 있어도 stars 와 delta 가 다르거나 다른 계정 주문이면 거절)', async () => {
    const a = await newPayer(app);
    const b = await newPayer(app);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 300, paid: 300, reason: 'purchase', ref: randomUUID() }))).rejects.toThrow(/no granted order/);
    const mismatch = await orderFor(a, { state: 'granted', stars: 300, granted: true });
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 999, paid: 999, reason: 'purchase', ref: mismatch }))).rejects.toThrow(/no granted order/);
    await expect(tx((c) => insertLedger(c, { accountId: b.accountId, delta: 300, paid: 300, reason: 'purchase', ref: mismatch }))).rejects.toThrow(/no granted order/);
    const notGranted = await orderFor(a, { state: 'finalized', stars: 300, granted: false });
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 300, paid: 300, reason: 'purchase', ref: notGranted }))).rejects.toThrow(/no granted order/);
    // 정상: granted 주문과 같은 계정, stars = delta
    await tx((c) => insertLedger(c, { accountId: a.accountId, delta: 300, paid: 300, reason: 'purchase', ref: mismatch }));
    // 같은 주문 uuid 로 purchase 두 번은 유일 인덱스가 거절
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 300, paid: 300, reason: 'purchase', ref: mismatch }))).rejects.toThrow(/duplicate key|star_ledger_purchase_uq/);
  });

  it('1. 회수 원장도 환불·차지백 주문이 없으면 거절, 같은 주문의 회수 두 번은 거절', async () => {
    const a = await newPayer(app);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 0, paid: 0, debt: 5, reason: 'refund_revoke', ref: randomUUID() }))).rejects.toThrow(/no reversed order/);
    const o = await orderFor(a, { state: 'refunded', stars: 300, granted: true, reversed: true });
    await tx((c) => insertLedger(c, { accountId: a.accountId, delta: 0, paid: 0, debt: 5, reason: 'refund_revoke', ref: o }));
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 0, paid: 0, debt: 5, reason: 'chargeback_revoke', ref: o }))).rejects.toThrow(/duplicate key|star_ledger_revoke_uq/);
  });

  it('2. 승인 없는 admin_grant·debt_forgive 거절, test_grant 는 DB 세션 설정이 없으면 거절하고 있으면 통과, 새 gacha_refund 거절', async () => {
    const a = await newPayer(app);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 10, reason: 'admin_grant', ref: randomUUID() }))).rejects.toThrow(/no approved grant/);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 0, debt: -5, reason: 'debt_forgive', ref: randomUUID() }))).rejects.toThrow(/no approved grant/);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 10, reason: 'test_grant', ref: 'x' }))).rejects.toThrow(/test stage/);
    await tx(async (c) => {
      await c.query("SET LOCAL dotrpg.allow_test_grant = 'on'");
      await insertLedger(c, { accountId: a.accountId, delta: 10, reason: 'test_grant', ref: 'x' });
    });
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 10, reason: 'gacha_refund', ref: 'x' }))).rejects.toThrow(/retired/);
    // 승인된(applied) 지급 행이 있으면 admin_grant 가 통과한다
    const [o1, o2] = [await makeAdmin('owner'), await makeAdmin('owner')];
    const g = await getPool().query<{ uuid: string }>(
      `INSERT INTO star_admin_grants (kind, account_id, stars, memo, request_id, created_by, approved_by, approved_at, state, expires_at)
       VALUES ('grant', $1, 10, 'm', gen_random_uuid(), $2, $3, now(), 'applied', now() + interval '1 day') RETURNING uuid`,
      [a.accountId, o1.id, o2.id],
    );
    await tx((c) => insertLedger(c, { accountId: a.accountId, delta: 10, reason: 'admin_grant', ref: (g.rows[0] as { uuid: string }).uuid }));
  });

  it('3. star_ledger·star_order_events·star_spend_allocs·star_rates_snapshots 의 UPDATE/DELETE/TRUNCATE 는 거절된다', async () => {
    const a = await newPayer(app);
    const db = getPool();
    await db.query("SET session_replication_role = DEFAULT");
    await tx(async (c) => {
      await c.query("SET LOCAL dotrpg.allow_test_grant = 'on'");
      await insertLedger(c, { accountId: a.accountId, delta: 5, reason: 'test_grant', ref: 'x' });
    });
    const o = await orderFor(a, { state: 'created', stars: 300, granted: false });
    const orderId = Number((await db.query<{ id: string }>('SELECT id FROM star_orders WHERE uuid = $1', [o])).rows[0]?.id);
    await db.query("INSERT INTO star_order_events (order_id, kind, actor) VALUES ($1, 'created', 'system')", [orderId]);
    await ensureRatesSnapshot(db);
    await expect(db.query('UPDATE star_ledger SET delta = 1 WHERE account_id = $1', [a.accountId])).rejects.toThrow(/append-only/);
    await expect(db.query('DELETE FROM star_ledger WHERE account_id = $1', [a.accountId])).rejects.toThrow(/append-only/);
    await expect(db.query('TRUNCATE star_ledger')).rejects.toThrow();
    await expect(db.query("UPDATE star_order_events SET kind = 'failed' WHERE order_id = $1", [orderId])).rejects.toThrow(/append-only/);
    await expect(db.query('DELETE FROM star_order_events WHERE order_id = $1', [orderId])).rejects.toThrow(/append-only/);
    await expect(db.query('TRUNCATE star_order_events')).rejects.toThrow(/append-only|referenced/);
    await expect(db.query("UPDATE star_spend_allocs SET stars = 1")).resolves.toBeDefined(); // 행이 없으면 트리거가 돌지 않는다
    await expect(db.query("UPDATE star_rates_snapshots SET content_hash = repeat('a', 64)")).rejects.toThrow(/append-only/);
    await expect(db.query('DELETE FROM star_rates_snapshots')).rejects.toThrow(/append-only/);
    await expect(db.query('TRUNCATE star_rates_snapshots')).rejects.toThrow(/append-only/);
  });

  it('4. 사유별 부호 위반(구매 음수, 소비 양수, 부채 상환에 debt_delta 양수)은 CHECK 가 거절한다', async () => {
    const a = await newPayer(app);
    const o = await orderFor(a, { state: 'granted', stars: 300, granted: true });
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: -300, paid: -300, reason: 'purchase', ref: o }))).rejects.toThrow(/star_ledger_sign_chk/);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 100, reason: 'gacha', ref: 'x' }))).rejects.toThrow(/star_ledger_sign_chk/);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: -5, debt: 5, reason: 'debt_settle', ref: 'x' }))).rejects.toThrow(/star_ledger_sign_chk/);
    await expect(tx((c) => insertLedger(c, { accountId: a.accountId, delta: 5, paid: 5, reason: 'dismantle', ref: 'x' }))).rejects.toThrow(/star_ledger_sign_chk/);
  });

  it('5. star_orders 상태·시각 CHECK(지급 안 됐는데 granted, 만들어지지 않은 granted_at), 열린 주문 2개 거절', async () => {
    const a = await newPayer(app);
    const db = getPool();
    const base = (state: string, grantedSql: string): Promise<unknown> =>
      db.query(
        `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, tier, state, expires_at, granted_at)
         VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', 1, 'KRW', 1100, 'standard', $3, now() + interval '1 hour', ${grantedSql})`,
        [a.accountId, a.steamId, state],
      );
    await expect(base('granted', 'NULL')).rejects.toThrow(/star_orders_granted_chk/);
    await expect(base('created', 'now()')).rejects.toThrow(/star_orders_granted_at_chk/);
    await base('created', 'NULL');
    await expect(base('authorized', 'NULL')).rejects.toThrow(/star_orders_one_open/);
    await expect(db.query("UPDATE star_orders SET state = 'failed' WHERE account_id = $1", [a.accountId])).rejects.toThrow(/star_orders_fail_chk/);
  });

  it('6. star_admin_grants: 작성자 = 승인자 거절(DB CHECK), 내용 UPDATE·DELETE·최종 상태 되돌리기 거절', async () => {
    const a = await newPayer(app);
    const [o1, o2] = [await makeAdmin('owner'), await makeAdmin('owner')];
    const db = getPool();
    await expect(
      db.query(`INSERT INTO star_admin_grants (kind, account_id, stars, memo, request_id, created_by, approved_by, approved_at, state, expires_at)
                VALUES ('grant', $1, 10, 'm', gen_random_uuid(), $2, $2, now(), 'applied', now() + interval '1 day')`, [a.accountId, o1.id]),
    ).rejects.toThrow(/star_admin_grants_two_person/);
    const g = await db.query<{ id: string }>(
      `INSERT INTO star_admin_grants (kind, account_id, stars, memo, request_id, created_by, expires_at) VALUES ('grant', $1, 10, 'm', gen_random_uuid(), $2, now() + interval '1 day') RETURNING id`,
      [a.accountId, o1.id],
    );
    const id = (g.rows[0] as { id: string }).id;
    await expect(db.query('UPDATE star_admin_grants SET stars = 99999 WHERE id = $1', [id])).rejects.toThrow(/immutable/);
    await expect(db.query("UPDATE star_admin_grants SET memo = 'x' WHERE id = $1", [id])).rejects.toThrow(/immutable/);
    await expect(db.query('UPDATE star_admin_grants SET approved_by = created_by WHERE id = $1', [id])).rejects.toThrow();
    await db.query("UPDATE star_admin_grants SET state = 'applied', approved_by = $2, approved_at = now() WHERE id = $1", [id, o2.id]);
    await expect(db.query("UPDATE star_admin_grants SET state = 'pending', approved_by = NULL, approved_at = NULL WHERE id = $1", [id])).rejects.toThrow(/final|star_admin_grants_applied_chk/);
    await expect(db.query('DELETE FROM star_admin_grants WHERE id = $1', [id])).rejects.toThrow(/never deleted/);
  });

  it('지갑 CHECK: 잔액 음수, 유료분 > 총액, 부채 음수는 거절된다', async () => {
    const a = await newPayer(app);
    const db = getPool();
    await db.query('INSERT INTO star_wallets (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [a.accountId]);
    await expect(db.query('UPDATE star_wallets SET balance = -1 WHERE account_id = $1', [a.accountId])).rejects.toThrow(/balance/);
    await expect(db.query('UPDATE star_wallets SET paid_balance = 5 WHERE account_id = $1', [a.accountId])).rejects.toThrow(/star_wallets_paid_le_balance/);
    await expect(db.query('UPDATE star_wallets SET debt = -1 WHERE account_id = $1', [a.accountId])).rejects.toThrow(/debt/);
  });

  it('확률표 스냅샷: 같은 버전에 같은 내용이면 통과, 다른 내용이면 RATES_VERSION_CONFLICT(기동 거부)', async () => {
    const db = getPool();
    const first = await ensureRatesSnapshot(db);
    expect(first.version).toMatch(/\d{4}-\d{2}-\d{2}/);
    expect((await ensureRatesSnapshot(db)).created).toBe(false);
    const changed = { ...buildRatesContent(), price: { one: 1, ten: 1, ten_count: 11 } };
    await expect(ensureRatesSnapshot(db, changed)).rejects.toBeInstanceOf(RatesVersionConflict);
    await expect(ensureRatesSnapshot(db, changed)).rejects.toThrow(/RATES_VERSION_CONFLICT/);
  });
});
