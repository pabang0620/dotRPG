// 별의 결정 지갑·원장의 유일한 쓰기 경로(0044, Docs/PLAN_CASH_ITEM_VALUE.md 3-1, 4).
// 호출 쪽은 이미 트랜잭션 안이어야 하고, 이 모듈이 결정 지갑 행을 FOR UPDATE로 잠근다(락 순서: 별조각 지갑 -> 결정 지갑 -> 결정 로트).
// 불변식: balance >= 0, 0 <= paid_balance <= balance, paid_balance = SUM(crystal_paid_lots.remaining), debt >= 0.
// 결정은 별조각·골드로 되돌릴 수 없고 거래할 수 없다(환전 금지). 유료 별조각에서 나온 몫은 결제 주문별 로트로 나눠 둔다.
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';

export interface CrystalWallet {
  balance: number;
  paidBalance: number;
  debt: number;
}

type Row = { balance: string; paid_balance: string; debt: string };
const of = (r: Row | undefined): CrystalWallet => ({
  balance: Number(r?.balance ?? 0),
  paidBalance: Number(r?.paid_balance ?? 0),
  debt: Number(r?.debt ?? 0),
});

export async function lockCrystals(db: Queryable, accountId: number): Promise<CrystalWallet> {
  await db.query('INSERT INTO crystal_wallets (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
  const r = await db.query<Row>('SELECT balance, paid_balance, debt FROM crystal_wallets WHERE account_id = $1 FOR UPDATE', [accountId]);
  return of(r.rows[0]);
}

export async function readCrystals(db: Queryable, accountId: number): Promise<CrystalWallet> {
  const r = await db.query<Row>('SELECT balance, paid_balance, debt FROM crystal_wallets WHERE account_id = $1', [accountId]);
  return of(r.rows[0]);
}

interface Line {
  accountId: number;
  delta: number;
  paidDelta: number;
  debtDelta: number;
  reason: string;
  ref: string | null;
  requestId: string | null;
}

async function apply(db: Queryable, l: Line): Promise<{ ledgerId: number } & CrystalWallet> {
  const w = await db.query<Row>(
    `UPDATE crystal_wallets SET balance = balance + $2, paid_balance = paid_balance + $3, debt = debt + $4, updated_at = now()
      WHERE account_id = $1 RETURNING balance, paid_balance, debt`,
    [l.accountId, l.delta, l.paidDelta, l.debtDelta],
  );
  const after = of(w.rows[0]);
  const ins = await db.query<{ id: string }>(
    `INSERT INTO crystal_ledger (account_id, delta, balance_after, paid_delta, debt_delta, debt_after, reason, ref, request_id)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9) RETURNING id`,
    [l.accountId, l.delta, after.balance, l.paidDelta, l.debtDelta, after.debt, l.reason, l.ref, l.requestId],
  );
  return { ledgerId: Number((ins.rows[0] as { id: string }).id), ...after };
}

/**
 * 결정 amount 중 유료 몫을 주문별로 나눈다: 이 계정이 뽑기·확정 교환에 쓴 별조각 중 아직 환불되지 않은 각 결제 주문의
 * 비율만큼(가장 큰 나머지 방식). 무료 별조각만 쓴 계정은 전부 무료 몫이다
 */
async function paidShares(db: Queryable, accountId: number, amount: number): Promise<{ orderId: number; n: number }[]> {
  const total = await db.query<{ spent: string }>(
    "SELECT COALESCE(-SUM(delta), 0) AS spent FROM star_ledger WHERE account_id = $1 AND reason IN ('gacha', 'exchange')",
    [accountId],
  );
  const spent = Number(total.rows[0]?.spent ?? 0);
  if (spent <= 0 || amount <= 0) return [];
  const byOrder = await db.query<{ order_id: string; stars: string }>(
    `SELECT a.order_id, SUM(a.stars) AS stars
       FROM star_spend_allocs a
       JOIN star_ledger l ON l.id = a.ledger_id
       JOIN star_paid_lots p ON p.order_id = a.order_id
      WHERE l.account_id = $1 AND l.reason IN ('gacha', 'exchange') AND p.revoked_at IS NULL
      GROUP BY a.order_id ORDER BY a.order_id`,
    [accountId],
  );
  const raw = byOrder.rows.map((r) => ({ orderId: Number(r.order_id), exact: (amount * Number(r.stars)) / spent }));
  const out = raw.map((r) => ({ orderId: r.orderId, n: Math.floor(r.exact), frac: r.exact - Math.floor(r.exact) }));
  const want = Math.min(amount, Math.round(raw.reduce((a, r) => a + r.exact, 0)));
  let left = want - out.reduce((a, r) => a + r.n, 0);
  for (const r of [...out].sort((a, b) => b.frac - a.frac || a.orderId - b.orderId)) {
    if (left <= 0) break;
    r.n += 1;
    left -= 1;
  }
  return out.filter((r) => r.n > 0).map(({ orderId, n }) => ({ orderId, n }));
}

/** 분해 입금. 유료 몫은 주문별 로트에 쌓고, 부채가 있으면 같은 트랜잭션에서 먼저 갚는다(무료 몫부터) */
export async function creditCrystals(
  db: Queryable,
  o: { accountId: number; amount: number; reason: 'dismantle_cosmetic' | 'dismantle_gear' | 'admin_grant' | 'test_grant'; ref: string | null; requestId: string | null },
): Promise<CrystalWallet & { settled: number; paid: number }> {
  const w = await lockCrystals(db, o.accountId);
  if (o.amount <= 0) return { ...w, settled: 0, paid: 0 };
  const shares = o.reason === 'admin_grant' || o.reason === 'test_grant' ? [] : await paidShares(db, o.accountId, o.amount);
  const paid = shares.reduce((a, s) => a + s.n, 0);
  let now = await apply(db, { accountId: o.accountId, delta: o.amount, paidDelta: paid, debtDelta: 0, reason: o.reason, ref: o.ref, requestId: o.requestId });
  for (const s of shares) {
    await db.query(
      `INSERT INTO crystal_paid_lots (account_id, order_id, granted, remaining) VALUES ($1, $2, $3, $3)
       ON CONFLICT (order_id) DO UPDATE SET granted = crystal_paid_lots.granted + EXCLUDED.granted, remaining = crystal_paid_lots.remaining + EXCLUDED.remaining`,
      [o.accountId, s.orderId, s.n],
    );
  }
  let settled = 0;
  if (now.debt > 0) {
    settled = Math.min(now.debt, o.amount);
    const fromFree = Math.min(settled, o.amount - paid);
    let fromPaid = settled - fromFree;
    const takes: { orderId: number; n: number }[] = [];
    for (const s of shares) {
      if (fromPaid <= 0) break;
      const n = Math.min(s.n, fromPaid);
      takes.push({ orderId: s.orderId, n });
      fromPaid -= n;
    }
    const paidTaken = takes.reduce((a, t) => a + t.n, 0);
    const a = await apply(db, { accountId: o.accountId, delta: -settled, paidDelta: -paidTaken, debtDelta: -settled, reason: 'debt_settle', ref: o.ref, requestId: null });
    for (const t of takes) {
      await db.query('UPDATE crystal_paid_lots SET remaining = remaining - $2 WHERE order_id = $1', [t.orderId, t.n]);
      await db.query('INSERT INTO crystal_spend_allocs (ledger_id, order_id, crystals) VALUES ($1, $2, $3)', [a.ledgerId, t.orderId, t.n]);
    }
    now = a;
  }
  return { balance: now.balance, paidBalance: now.paidBalance, debt: now.debt, settled, paid };
}

/** 환불로 갚지 않은 결정이 있으면 교환소를 막는다 */
export function assertNoCrystalDebt(w: CrystalWallet): void {
  if (w.debt > 0) throw new AppError(403, '환불된 결제분에서 나온 별의 결정이 남아 있어 교환소를 사용할 수 없습니다.', 'CRYSTAL_DEBT', { debt: w.debt });
}

/** 교환소 소비: 무료 몫 먼저, 유료 몫은 오래된 주문 로트부터. 모자라거나 부채가 있으면 던진다 */
export async function debitCrystals(
  db: Queryable,
  o: { accountId: number; price: number; ref: string | null; requestId: string | null },
): Promise<CrystalWallet> {
  const w = await lockCrystals(db, o.accountId);
  assertNoCrystalDebt(w);
  if (w.balance < o.price) throw new AppError(422, '별의 결정이 모자랍니다.', 'NOT_ENOUGH_CRYSTALS', { need: o.price, have: w.balance });
  const fromFree = Math.min(w.balance - w.paidBalance, o.price);
  let fromPaid = o.price - fromFree;
  const takes: { orderId: number; n: number }[] = [];
  if (fromPaid > 0) {
    const lots = await db.query<{ order_id: string; remaining: string }>(
      'SELECT order_id, remaining FROM crystal_paid_lots WHERE account_id = $1 AND remaining > 0 ORDER BY order_id FOR UPDATE',
      [o.accountId],
    );
    const sum = lots.rows.reduce((a, r) => a + Number(r.remaining), 0);
    if (sum !== w.paidBalance) {
      logger.error({ account: o.accountId, lots: sum, paid_balance: w.paidBalance }, 'crystal_wallet_invariant');
      throw new AppError(500, '별의 결정 정합성 오류가 발견되어 처리를 중단했습니다.', 'CRYSTAL_INVARIANT');
    }
    for (const l of lots.rows) {
      if (fromPaid <= 0) break;
      const n = Math.min(Number(l.remaining), fromPaid);
      takes.push({ orderId: Number(l.order_id), n });
      fromPaid -= n;
    }
  }
  const paidTaken = takes.reduce((a, t) => a + t.n, 0);
  const a = await apply(db, { accountId: o.accountId, delta: -o.price, paidDelta: -paidTaken, debtDelta: 0, reason: 'exchange_buy', ref: o.ref, requestId: o.requestId });
  for (const t of takes) {
    await db.query('UPDATE crystal_paid_lots SET remaining = remaining - $2 WHERE order_id = $1', [t.orderId, t.n]);
    await db.query('INSERT INTO crystal_spend_allocs (ledger_id, order_id, crystals) VALUES ($1, $2, $3)', [a.ledgerId, t.orderId, t.n]);
  }
  return { balance: a.balance, paidBalance: a.paidBalance, debt: a.debt };
}

/**
 * 환불·차지백 회수: 그 주문에서 나온 결정 중 남은 몫을 걷고, 이미 교환소에 쓴 몫은 부채로 남긴다. 주문당 1번.
 * 별조각 회수(starWallet.reversePaid)와 같은 트랜잭션에서 부른다
 */
export async function reverseCrystalsForOrder(
  db: Queryable,
  o: { accountId: number; orderId: number; orderUuid: string; kind: 'refund' | 'chargeback' },
): Promise<{ taken: number; owed: number }> {
  await lockCrystals(db, o.accountId);
  const lot = await db.query<{ granted: string; remaining: string; revoked_at: Date | null }>(
    'SELECT granted, remaining, revoked_at FROM crystal_paid_lots WHERE order_id = $1 FOR UPDATE',
    [o.orderId],
  );
  const row = lot.rows[0];
  if (!row || row.revoked_at) return { taken: 0, owed: 0 };
  const taken = Number(row.remaining);
  const owed = Number(row.granted) - taken;
  if (taken > 0 || owed > 0) {
    await apply(db, {
      accountId: o.accountId,
      delta: -taken,
      paidDelta: -taken,
      debtDelta: owed,
      reason: o.kind === 'refund' ? 'refund_revoke' : 'chargeback_revoke',
      ref: o.orderUuid,
      requestId: null,
    });
  }
  await db.query('UPDATE crystal_paid_lots SET remaining = 0, revoked_at = now() WHERE order_id = $1', [o.orderId]);
  return { taken, owed };
}
