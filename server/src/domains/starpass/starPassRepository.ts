// 별빛 정기권 저장소(0034). 기간은 계정 단위(account_star_pass), 주문별 지급·회수와 일일 지급은 따로 기록한다.
import type { Queryable } from '../../db/pool';

export const DAY_MS = 86_400_000;
/** 남은 기간 상한: 재구매는 남은 기간에 더하되 60일을 넘기지 않는다 */
export const MAX_REMAINING_MS = 60 * DAY_MS;

export async function expiresAt(db: Queryable, accountId: number): Promise<Date | null> {
  const r = await db.query<{ expires_at: Date }>('SELECT expires_at FROM account_star_pass WHERE account_id = $1', [accountId]);
  return r.rows[0]?.expires_at ?? null;
}

export async function lockExpiresAt(db: Queryable, accountId: number): Promise<Date | null> {
  const r = await db.query<{ expires_at: Date }>('SELECT expires_at FROM account_star_pass WHERE account_id = $1 FOR UPDATE', [accountId]);
  return r.rows[0]?.expires_at ?? null;
}

export async function setExpiresAt(db: Queryable, accountId: number, at: Date): Promise<void> {
  await db.query(
    `INSERT INTO account_star_pass (account_id, expires_at, updated_at) VALUES ($1, $2, now())
     ON CONFLICT (account_id) DO UPDATE SET expires_at = EXCLUDED.expires_at, updated_at = now()`,
    [accountId, at],
  );
}

export async function grantOf(db: Queryable, orderId: number, kind: 'grant' | 'revoke'): Promise<{ seconds: number } | null> {
  const r = await db.query<{ seconds: string }>('SELECT seconds FROM star_pass_grants WHERE order_id = $1 AND kind = $2', [orderId, kind]);
  return r.rows[0] ? { seconds: Number(r.rows[0].seconds) } : null;
}

export async function insertGrant(
  db: Queryable,
  g: { accountId: number; orderId: number; kind: 'grant' | 'revoke'; seconds: number; expiresAfter: Date },
): Promise<void> {
  await db.query(
    'INSERT INTO star_pass_grants (account_id, order_id, kind, seconds, expires_after) VALUES ($1, $2, $3, $4, $5)',
    [g.accountId, g.orderId, g.kind, g.seconds, g.expiresAfter],
  );
}

export async function dailyOf(db: Queryable, accountId: number, dayStart: Date): Promise<{ stars: number; mail_id: number | null } | null> {
  const r = await db.query<{ stars: number; mail_id: string | null }>(
    'SELECT stars, mail_id FROM star_pass_daily WHERE account_id = $1 AND day_start = $2',
    [accountId, dayStart],
  );
  const x = r.rows[0];
  return x ? { stars: x.stars, mail_id: x.mail_id === null ? null : Number(x.mail_id) } : null;
}

export async function insertDaily(
  db: Queryable,
  d: { accountId: number; dayStart: Date; characterId: number; mailId: number | null; stars: number; requestId: string },
): Promise<void> {
  await db.query(
    'INSERT INTO star_pass_daily (account_id, day_start, character_id, mail_id, stars, request_id) VALUES ($1, $2, $3, $4, $5, $6)',
    [d.accountId, d.dayStart, d.characterId, d.mailId, d.stars, d.requestId],
  );
}
