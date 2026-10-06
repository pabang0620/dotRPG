// 클리어권 지갑(설계 5.2): 계정 소유 로트 + 추가 전용 원장. 호출하는 쪽이 계정 행을 먼저 잠근다(락 순서 ②).
// 일반 로트는 계정당 1행(누적), 이벤트 로트는 지급마다 1행(기한 있음). 원장 없이 remaining만 바꾸지 않는다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export type LotKind = 'normal' | 'event';
export type LedgerReason = 'shop_buy' | 'weekly_activity' | 'campaign_claim' | 'sweep_use' | 'expire';

export interface Lot {
  id: number;
  kind: LotKind;
  remaining: number;
  expiresAt: Date | null;
}

interface RawLot {
  id: string;
  kind: LotKind;
  remaining: number;
  expires_at: Date | null;
}
const toLot = (r: RawLot): Lot => ({ id: Number(r.id), kind: r.kind, remaining: r.remaining, expiresAt: r.expires_at });

const LIVE = '(remaining > 0 AND (expires_at IS NULL OR expires_at > $2))';

/** 락 순서 ②: 계정 행. 클리어권·주간 카운터·우편 수령을 만지는 모든 경로가 캐릭터 행 다음에 잠근다. 계정 uuid를 돌려준다 */
export async function lockAccount(client: PoolClient, accountId: number): Promise<string> {
  const r = await client.query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1 FOR UPDATE', [accountId]);
  return (r.rows[0] as { uuid: string }).uuid;
}

/** 쓸 수 있는 로트를 잠그고 읽는다: 만료가 가까운 이벤트 로트 먼저, 일반 로트는 마지막(id 순) */
export async function lockLive(client: PoolClient, accountId: number, now: Date): Promise<Lot[]> {
  const r = await client.query<RawLot>(
    `SELECT id, kind, remaining, expires_at FROM sweep_ticket_lots
      WHERE account_id = $1 AND ${LIVE}
      ORDER BY expires_at NULLS LAST, id FOR UPDATE`,
    [accountId, now],
  );
  return r.rows.map(toLot);
}

/** 읽기만(현황, 우편 요약) */
export async function readLive(db: Queryable, accountId: number, now: Date): Promise<Lot[]> {
  const r = await db.query<RawLot>(
    `SELECT id, kind, remaining, expires_at FROM sweep_ticket_lots
      WHERE account_id = $1 AND ${LIVE} ORDER BY expires_at NULLS LAST, id`,
    [accountId, now],
  );
  return r.rows.map(toLot);
}

export async function liveTotal(db: Queryable, accountId: number, now: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT coalesce(sum(remaining), 0) AS n FROM sweep_ticket_lots WHERE account_id = $1 AND ${LIVE}`,
    [accountId, now],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export interface TicketView {
  total: number;
  normal: number;
  event: { count: number; expires_at: string }[];
}

/** 응답의 tickets 블록(만료 가까운 순) */
export function viewOf(lots: Lot[]): TicketView {
  let normal = 0;
  const event: TicketView['event'] = [];
  for (const l of lots) {
    if (l.remaining <= 0) continue;
    if (l.kind === 'normal') normal += l.remaining;
    else event.push({ count: l.remaining, expires_at: (l.expiresAt as Date).toISOString() });
  }
  return { total: normal + event.reduce((a, e) => a + e.count, 0), normal, event };
}

async function writeLedger(
  client: PoolClient,
  o: { accountId: number; lotId: number; characterId: number | null; delta: number; reason: LedgerReason; ref: string; requestId: string | null; now: Date },
): Promise<void> {
  const balance = await liveTotal(client, o.accountId, o.now);
  await client.query(
    `INSERT INTO sweep_ticket_ledger (account_id, lot_id, character_id, delta, balance_after, reason, ref, request_id)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
    [o.accountId, o.lotId, o.characterId, o.delta, balance, o.reason, o.ref, o.requestId],
  );
}

export interface WalletActor {
  accountId: number;
  characterId: number | null;
  requestId: string | null;
  now: Date;
}

/** 일반권 n장 추가(구매, 주간 활동): 계정당 1행 upsert */
export async function addNormal(client: PoolClient, a: WalletActor, n: number, reason: 'shop_buy' | 'weekly_activity', ref: string): Promise<void> {
  const r = await client.query<{ id: string }>(
    `INSERT INTO sweep_ticket_lots (account_id, kind, granted, remaining) VALUES ($1, 'normal', $2, $2)
     ON CONFLICT (account_id) WHERE kind = 'normal'
     DO UPDATE SET granted = sweep_ticket_lots.granted + EXCLUDED.granted, remaining = sweep_ticket_lots.remaining + EXCLUDED.remaining
     RETURNING id`,
    [a.accountId, n],
  );
  await writeLedger(client, { ...a, lotId: Number((r.rows[0] as { id: string }).id), delta: n, reason, ref });
}

/** 이벤트권 n장 추가(캠페인 우편 수령): 새 로트 한 행, 기한은 호출 쪽이 정한다 */
export async function addEvent(client: PoolClient, a: WalletActor, n: number, expiresAt: Date, ref: string): Promise<void> {
  const r = await client.query<{ id: string }>(
    `INSERT INTO sweep_ticket_lots (account_id, kind, granted, remaining, expires_at) VALUES ($1, 'event', $2, $2, $3) RETURNING id`,
    [a.accountId, n, expiresAt],
  );
  await writeLedger(client, { ...a, lotId: Number((r.rows[0] as { id: string }).id), delta: n, reason: 'campaign_claim', ref });
}

/** 한 장 소모(동시 소모 방어: remaining >= 1 조건부 UPDATE). 성공하면 true */
export async function consumeOne(client: PoolClient, a: WalletActor, lot: Lot, ref: string): Promise<boolean> {
  const r = await client.query('UPDATE sweep_ticket_lots SET remaining = remaining - 1 WHERE id = $1 AND remaining >= 1', [lot.id]);
  if ((r.rowCount ?? 0) !== 1) return false;
  lot.remaining -= 1;
  await writeLedger(client, { ...a, lotId: lot.id, delta: -1, reason: 'sweep_use', ref });
  return true;
}

/** 작업 sweep_ticket_expire: 기한이 지난 이벤트 로트 한 개를 0으로(계정 행을 잠근 뒤 부른다). 만료시킨 장수를 돌려준다 */
export async function expireLot(client: PoolClient, lotId: number, now: Date): Promise<number> {
  const r = await client.query<{ account_id: string; remaining: number }>(
    `SELECT account_id, remaining FROM sweep_ticket_lots WHERE id = $1 AND kind = 'event' AND remaining > 0 AND expires_at <= $2 FOR UPDATE`,
    [lotId, now],
  );
  const row = r.rows[0];
  if (!row) return 0;
  await client.query('UPDATE sweep_ticket_lots SET remaining = 0 WHERE id = $1', [lotId]);
  await writeLedger(client, { accountId: Number(row.account_id), lotId, characterId: null, delta: -row.remaining, reason: 'expire', ref: String(lotId), requestId: null, now });
  return row.remaining;
}
