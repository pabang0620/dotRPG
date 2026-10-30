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

export async function grantOf(db: Queryable, orderId: number, kind: 'grant' | 'revoke'): Promise<{ seconds: number; createdAt: Date | null; expiresAfter: Date | null } | null> {
  const r = await db.query<{ seconds: string; created_at: Date; expires_after: Date }>(
    'SELECT seconds, created_at, expires_after FROM star_pass_grants WHERE order_id = $1 AND kind = $2',
    [orderId, kind],
  );
  const x = r.rows[0];
  return x ? { seconds: Number(x.seconds), createdAt: x.created_at, expiresAfter: x.expires_after } : null;
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

/** 그 주문 기간(지급 시각 이후, 만료 전 일자)에 받았고 아직 회수되지 않은 일일 지급 */
export async function unreclaimedDailies(
  db: Queryable,
  accountId: number,
  since: Date,
  until: Date,
): Promise<{ day_start: Date; mail_id: number | null; stars: number }[]> {
  const r = await db.query<{ day_start: Date; mail_id: string | null; stars: number }>(
    `SELECT day_start, mail_id, stars FROM star_pass_daily
      WHERE account_id = $1 AND reclaimed_order_id IS NULL AND created_at >= $2 AND day_start <= $3 ORDER BY day_start FOR UPDATE`,
    [accountId, since, until],
  );
  return r.rows.map((x) => ({ day_start: x.day_start, mail_id: x.mail_id === null ? null : Number(x.mail_id), stars: x.stars }));
}

export async function markReclaimed(db: Queryable, accountId: number, dayStarts: Date[], orderId: number): Promise<void> {
  if (dayStarts.length === 0) return;
  await db.query('UPDATE star_pass_daily SET reclaimed_order_id = $3 WHERE account_id = $1 AND day_start = ANY($2::timestamptz[])', [accountId, dayStarts, orderId]);
}

/** 받지 않은 일일 상자 우편을 폐기한다(받아 간 것은 owner 회수 도구가 가방에서 걷는다). 폐기한 통수 */
export async function expireUnclaimedMails(db: Queryable, mailIds: number[], now: Date): Promise<number> {
  if (mailIds.length === 0) return 0;
  const r = await db.query('UPDATE mails SET expired_at = $2 WHERE id = ANY($1::bigint[]) AND claimed_at IS NULL AND expired_at IS NULL', [mailIds, now]);
  return r.rowCount ?? 0;
}
