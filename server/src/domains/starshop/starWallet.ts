// 별조각 지갑·원장의 유일한 쓰기 경로(Docs/server/phase11_payments.md 4절, 8절).
// INSERT INTO star_ledger 와 UPDATE star_wallets 는 이 파일에만 있다(test/starWalletOnly.test.ts 가 검사한다).
// 호출 쪽은 이미 트랜잭션 안이어야 하고, 이 모듈이 지갑 행을 FOR UPDATE로 잠근다(락 순서 ④ 지갑 -> ⑤ 유료 로트).
// 불변식: balance >= 0, 0 <= paid_balance <= balance, paid_balance = SUM(star_paid_lots.remaining), debt >= 0.
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';

export interface Wallet {
  balance: number;
  /** 그중 유료분(결제로 산 별조각). 무료분 = balance - paidBalance */
  paidBalance: number;
  /** 환불·차지백된 결제분 중 이미 소비된 별조각. > 0 이면 소비 차단, 모든 입금이 먼저 상환한다 */
  debt: number;
  pity: number;
  skinPity: number;
  /** 등급별 연속 합성 실패 수(천장): 희귀·에픽·유니크로 가는 합성 */
  synthFail: { rare: number; epic: number; unique: number };
}

type WalletRow = {
  balance: string;
  paid_balance: string;
  debt: string;
  pity: number;
  skin_pity: number;
  synth_fail_rare: number;
  synth_fail_epic: number;
  synth_fail_unique: number;
};
const WALLET_COLS = 'balance, paid_balance, debt, pity, skin_pity, synth_fail_rare, synth_fail_epic, synth_fail_unique';
function walletOf(row: WalletRow | undefined): Wallet {
  return {
    balance: Number(row?.balance ?? 0),
    paidBalance: Number(row?.paid_balance ?? 0),
    debt: Number(row?.debt ?? 0),
    pity: row?.pity ?? 0,
    skinPity: row?.skin_pity ?? 0,
    synthFail: { rare: row?.synth_fail_rare ?? 0, epic: row?.synth_fail_epic ?? 0, unique: row?.synth_fail_unique ?? 0 },
  };
}

/** 지갑 행을 만들고(없으면) 잠근다. 같은 계정의 다른 캐릭터 요청도 여기서 줄을 선다 */
export async function lockWallet(db: Queryable, accountId: number): Promise<Wallet> {
  await db.query('INSERT INTO star_wallets (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
  const r = await db.query<WalletRow>(`SELECT ${WALLET_COLS} FROM star_wallets WHERE account_id = $1 FOR UPDATE`, [accountId]);
  return walletOf(r.rows[0]);
}

export async function readWallet(db: Queryable, accountId: number): Promise<Wallet> {
  const r = await db.query<WalletRow>(`SELECT ${WALLET_COLS} FROM star_wallets WHERE account_id = $1`, [accountId]);
  return walletOf(r.rows[0]);
}

export async function setPity(db: Queryable, accountId: number, pity: number, skin = false): Promise<void> {
  await db.query(`UPDATE star_wallets SET ${skin ? 'skin_pity' : 'pity'} = $2, updated_at = now() WHERE account_id = $1`, [accountId, pity]);
}

export async function setSynthFail(db: Queryable, accountId: number, tier: 'rare' | 'epic' | 'unique', fails: number): Promise<void> {
  await db.query(`UPDATE star_wallets SET synth_fail_${tier} = $2, updated_at = now() WHERE account_id = $1`, [accountId, fails]);
}

/** 부채가 있으면 별조각 소비·선택·합성을 막는다. 지갑 행을 이미 잠근 상태에서 부른다(11.2) */
export function assertNoStarDebt(w: Wallet): void {
  if (w.debt > 0) {
    throw new AppError(403, '환불된 결제분이 있어 별조각을 사용할 수 없습니다.', 'STAR_DEBT', { debt: w.debt });
  }
}

/** 오늘(since 이후) gacha·exchange 로 쓴 별조각 합계. 지갑 행을 잠근 상태에서 읽어 경쟁이 없다 */
export async function spentSince(db: Queryable, accountId: number, since: Date): Promise<number> {
  const r = await db.query<{ s: string }>(
    "SELECT coalesce(-sum(delta), 0) AS s FROM star_ledger WHERE account_id = $1 AND reason IN ('gacha', 'exchange') AND created_at >= $2",
    [accountId, since],
  );
  return Number(r.rows[0]?.s ?? 0);
}

// ---------------- 원장 한 줄 + 지갑 갱신 ----------------

interface Line {
  accountId: number;
  delta: number;
  paidDelta: number;
  debtDelta: number;
  reason: string;
  ref: string | null;
  requestId: string | null;
}
interface Applied {
  ledgerId: number;
  balance: number;
  paid: number;
  debt: number;
}

async function apply(db: Queryable, l: Line): Promise<Applied> {
  const w = await db.query<{ balance: string; paid_balance: string; debt: string }>(
    `UPDATE star_wallets SET balance = balance + $2, paid_balance = paid_balance + $3, debt = debt + $4, updated_at = now()
      WHERE account_id = $1 RETURNING balance, paid_balance, debt`,
    [l.accountId, l.delta, l.paidDelta, l.debtDelta],
  );
  const row = w.rows[0];
  if (!row) throw new Error('star wallet row missing');
  const after = { balance: Number(row.balance), paid: Number(row.paid_balance), debt: Number(row.debt) };
  const ins = await db.query<{ id: string }>(
    `INSERT INTO star_ledger (account_id, delta, balance_after, reason, ref, request_id, paid_delta, paid_balance_after, debt_delta, debt_after)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10) RETURNING id`,
    [l.accountId, l.delta, after.balance, l.reason, l.ref, l.requestId, l.paidDelta, after.paid, l.debtDelta, after.debt],
  );
  return { ledgerId: Number((ins.rows[0] as { id: string }).id), ...after };
}

async function alloc(db: Queryable, ledgerId: number, orderId: number, stars: number): Promise<void> {
  if (stars <= 0) return;
  await db.query('INSERT INTO star_spend_allocs (ledger_id, order_id, stars) VALUES ($1, $2, $3)', [ledgerId, orderId, stars]);
}

/** 입금 직후 같은 트랜잭션에서 부채를 먼저 갚는다(8.5). 유료 입금이면 방금 만든 로트에서 깎는다 */
async function settleDebt(db: Queryable, accountId: number, credited: number, now: Applied, paidLot: { orderId: number } | null, ref: string | null): Promise<{ applied: number; applied_state: Applied }> {
  const applied = Math.min(now.debt, credited);
  if (applied <= 0) return { applied: 0, applied_state: now };
  const a = await apply(db, { accountId, delta: -applied, paidDelta: paidLot ? -applied : 0, debtDelta: -applied, reason: 'debt_settle', ref, requestId: null });
  if (paidLot) {
    await db.query('UPDATE star_paid_lots SET remaining = remaining - $2 WHERE order_id = $1', [paidLot.orderId, applied]);
    await alloc(db, a.ledgerId, paidLot.orderId, applied);
  }
  return { applied, applied_state: a };
}

export interface CreditResult {
  balance: number;
  paidBalance: number;
  debt: number;
  /** 이 입금이 갚은 부채 */
  settled: number;
}

/** 결제 주문 지급(주문당 1회, 유료분). 호출 전에 주문 행을 잠그고 state='finalized'를 확인한 쪽이 책임진다 */
export async function creditPaid(db: Queryable, o: { accountId: number; orderId: number; orderUuid: string; stars: number }): Promise<CreditResult> {
  await lockWallet(db, o.accountId);
  const a = await apply(db, { accountId: o.accountId, delta: o.stars, paidDelta: o.stars, debtDelta: 0, reason: 'purchase', ref: o.orderUuid, requestId: null });
  await db.query('INSERT INTO star_paid_lots (order_id, account_id, granted, remaining) VALUES ($1, $2, $3, $3)', [o.orderId, o.accountId, o.stars]);
  const s = await settleDebt(db, o.accountId, o.stars, a, { orderId: o.orderId }, o.orderUuid);
  const f = s.applied_state;
  return { balance: f.balance, paidBalance: f.paid, debt: f.debt, settled: s.applied };
}

/** 무료 입금: 분해, 승인된 운영 지급, 시험 지급. 환불 대상이 아니다 */
export async function creditFree(
  db: Queryable,
  o: { accountId: number; reason: 'dismantle' | 'admin_grant' | 'test_grant' | 'level_reward'; amount: number; ref: string | null; requestId: string | null },
): Promise<CreditResult> {
  await lockWallet(db, o.accountId);
  const a = await apply(db, { accountId: o.accountId, delta: o.amount, paidDelta: 0, debtDelta: 0, reason: o.reason, ref: o.ref, requestId: o.requestId });
  const s = await settleDebt(db, o.accountId, o.amount, a, null, o.ref);
  const f = s.applied_state;
  return { balance: f.balance, paidBalance: f.paid, debt: f.debt, settled: s.applied };
}

/** 소비(뽑기·교환): 무료분 먼저, 유료분은 오래된 로트부터(8.3). 모자라거나 부채가 있으면 던진다 */
export async function debit(
  db: Queryable,
  o: { accountId: number; reason: 'gacha' | 'exchange'; price: number; ref: string | null; requestId: string | null },
): Promise<{ balance: number; paidBalance: number }> {
  const w = await lockWallet(db, o.accountId);
  assertNoStarDebt(w);
  if (w.balance < o.price) throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: o.price, have: w.balance });
  const fromFree = Math.min(w.balance - w.paidBalance, o.price);
  const fromPaid = o.price - fromFree;
  const takes: { orderId: number; n: number }[] = [];
  if (fromPaid > 0) {
    const lots = await db.query<{ order_id: string; remaining: number }>(
      'SELECT order_id, remaining FROM star_paid_lots WHERE account_id = $1 AND remaining > 0 ORDER BY created_at, order_id FOR UPDATE',
      [o.accountId],
    );
    const sum = lots.rows.reduce((a, r) => a + r.remaining, 0);
    if (sum !== w.paidBalance) {
      logger.error({ account: o.accountId, lots: sum, paid_balance: w.paidBalance }, 'star_wallet_invariant');
      throw new AppError(500, '별조각 정합성 오류가 발견되어 처리를 중단했습니다.', 'STAR_INVARIANT');
    }
    let left = fromPaid;
    for (const l of lots.rows) {
      if (left <= 0) break;
      const n = Math.min(l.remaining, left);
      takes.push({ orderId: Number(l.order_id), n });
      left -= n;
    }
  }
  const a = await apply(db, { accountId: o.accountId, delta: -o.price, paidDelta: -fromPaid, debtDelta: 0, reason: o.reason, ref: o.ref, requestId: o.requestId });
  for (const t of takes) {
    await db.query('UPDATE star_paid_lots SET remaining = remaining - $2 WHERE order_id = $1', [t.orderId, t.n]);
    await alloc(db, a.ledgerId, t.orderId, t.n);
  }
  return { balance: a.balance, paidBalance: a.paid };
}

/** 환불·차지백 회수(9.3): 그 주문 로트의 남은 분을 걷고, 이미 쓴 분은 부채로 남긴다. 주문당 1번(회수 원장 유일 인덱스) */
export async function reversePaid(
  db: Queryable,
  o: { accountId: number; orderId: number; orderUuid: string; kind: 'refund' | 'chargeback' },
): Promise<{ taken: number; owed: number; already: boolean }> {
  await lockWallet(db, o.accountId);
  const lot = await db.query<{ granted: number; remaining: number; revoked_at: Date | null }>(
    'SELECT granted, remaining, revoked_at FROM star_paid_lots WHERE order_id = $1 FOR UPDATE',
    [o.orderId],
  );
  const row = lot.rows[0];
  if (!row) throw new Error(`star_paid_lots row missing for order ${o.orderId}`);
  if (row.revoked_at) return { taken: 0, owed: 0, already: true };
  const taken = row.remaining;
  const owed = row.granted - row.remaining;
  const a = await apply(db, {
    accountId: o.accountId,
    delta: -taken,
    paidDelta: -taken,
    debtDelta: owed,
    reason: o.kind === 'refund' ? 'refund_revoke' : 'chargeback_revoke',
    ref: o.orderUuid,
    requestId: null,
  });
  await db.query('UPDATE star_paid_lots SET remaining = 0, revoked_at = now() WHERE order_id = $1', [o.orderId]);
  await alloc(db, a.ledgerId, o.orderId, taken);
  return { taken, owed, already: false };
}

/** 승인된 부채 탕감(전액만). 부채가 없으면 0 */
export async function forgiveDebt(db: Queryable, o: { accountId: number; ref: string }): Promise<number> {
  const w = await lockWallet(db, o.accountId);
  if (w.debt <= 0) return 0;
  await apply(db, { accountId: o.accountId, delta: 0, paidDelta: 0, debtDelta: -w.debt, reason: 'debt_forgive', ref: o.ref, requestId: null });
  return w.debt;
}
