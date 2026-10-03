// 이상 기록·경매 플래그·의심 계정 순위 SQL(WT1~WT3). 계정 상세의 점수도 여기서 계산한다.
import type { Queryable } from '../../db/pool';

const SCORE_SQL = `
  WITH acct AS (
    SELECT a.id AS account_id, a.uuid,
           (SELECT max(created_at) FROM admin_account_notes n WHERE n.account_id = a.id AND n.kind = 'review_ack') AS acked_at
      FROM accounts a
     WHERE ($1::bigint IS NULL OR a.id = $1)
  ), ev AS (
    SELECT l.account_id, 'anomaly'::text AS src, l.kind, CASE l.severity WHEN 1 THEN 1 WHEN 2 THEN 3 ELSE 10 END AS pts, l.created_at
      FROM anomaly_log l
     WHERE l.created_at >= now() - ($2::int * interval '1 hour') AND ($1::bigint IS NULL OR l.account_id = $1)
    UNION ALL
    SELECT f.account_id, 'auction_flag', f.kind, CASE f.severity WHEN 1 THEN 1 WHEN 2 THEN 3 ELSE 10 END, f.created_at
      FROM auction_flags f
     WHERE f.created_at >= now() - ($2::int * interval '1 hour') AND ($1::bigint IS NULL OR f.account_id = $1)
    UNION ALL
    SELECT c.account_id, 'held_run', 'held_run', 10, d.ended_at
      FROM dungeon_runs d JOIN characters c ON c.id = d.character_id
     WHERE d.state = 'held' AND d.ended_at >= now() - ($2::int * interval '1 hour') AND ($1::bigint IS NULL OR c.account_id = $1)
  )
  SELECT acct.uuid AS account, acct.acked_at,
         sum(ev.pts)::int AS score, max(ev.created_at) AS last_at,
         (SELECT jsonb_object_agg(k, n) FROM (
            SELECT e2.src || ':' || e2.kind AS k, count(*) AS n FROM ev e2
             WHERE e2.account_id = acct.account_id AND (acct.acked_at IS NULL OR e2.created_at > acct.acked_at)
             GROUP BY 1) q) AS counts,
         EXISTS (SELECT 1 FROM account_sanctions s WHERE s.account_id = acct.account_id AND s.revoked_at IS NULL
                   AND (s.ends_at IS NULL OR s.ends_at > now()) AND s.kind IN ('ban', 'chat_mute')) AS has_sanction
    FROM acct JOIN ev ON ev.account_id = acct.account_id AND (acct.acked_at IS NULL OR ev.created_at > acct.acked_at)
   GROUP BY acct.account_id, acct.uuid, acct.acked_at
  HAVING sum(ev.pts) >= $3
   ORDER BY score DESC, last_at DESC
   LIMIT $4`;

export interface WatchEntry {
  account: string;
  score: number;
  counts: Record<string, number>;
  last_at: string;
  has_sanction: boolean;
  last_ack_at: string | null;
}

/** 점수 = 심각도 1 -> 1점, 2 -> 3점, 3 -> 10점, 보류 판 1건 -> 10점. 계정의 마지막 review_ack 이후 기록만 센다 */
export async function watchlist(db: Queryable, window_hours: number, minScore: number, limit: number, accountId: number | null = null): Promise<WatchEntry[]> {
  const r = await db.query<{ account: string; acked_at: Date | null; score: number; last_at: Date; counts: Record<string, number> | null; has_sanction: boolean }>(
    SCORE_SQL,
    [accountId, window_hours, minScore, limit],
  );
  return r.rows.map((x) => ({
    account: x.account,
    score: x.score,
    counts: x.counts ?? {},
    last_at: x.last_at.toISOString(),
    has_sanction: x.has_sanction,
    last_ack_at: x.acked_at ? x.acked_at.toISOString() : null,
  }));
}

export interface AnomalyQuery {
  accountId: number | null;
  kind: string | null;
  minSeverity: number | null;
  since: string | null;
  before: number | null;
  limit: number;
}

export async function anomalies(db: Queryable, q: AnomalyQuery) {
  const r = await db.query<{ id: string; account: string; character: string | null; kind: string; severity: number; detail: unknown; created_at: Date }>(
    `SELECT l.id, a.uuid AS account, c.uuid AS character, l.kind, l.severity, l.detail, l.created_at
       FROM anomaly_log l JOIN accounts a ON a.id = l.account_id LEFT JOIN characters c ON c.id = l.character_id
      WHERE ($1::bigint IS NULL OR l.account_id = $1) AND ($2::text IS NULL OR l.kind = $2)
        AND ($3::int IS NULL OR l.severity >= $3) AND ($4::timestamptz IS NULL OR l.created_at >= $4)
        AND ($5::bigint IS NULL OR l.id < $5)
      ORDER BY l.id DESC LIMIT $6`,
    [q.accountId, q.kind, q.minSeverity, q.since, q.before, q.limit + 1],
  );
  return r.rows;
}

export async function auctionFlags(db: Queryable, q: Omit<AnomalyQuery, 'minSeverity'>) {
  const r = await db.query<{ id: string; account: string; character: string | null; kind: string; severity: number; detail: unknown; created_at: Date }>(
    `SELECT l.id, a.uuid AS account, c.uuid AS character, l.kind, l.severity, l.detail, l.created_at
       FROM auction_flags l JOIN accounts a ON a.id = l.account_id LEFT JOIN characters c ON c.id = l.character_id
      WHERE ($1::bigint IS NULL OR l.account_id = $1) AND ($2::text IS NULL OR l.kind = $2)
        AND ($3::timestamptz IS NULL OR l.created_at >= $3) AND ($4::bigint IS NULL OR l.id < $4)
      ORDER BY l.id DESC LIMIT $5`,
    [q.accountId, q.kind, q.since, q.before, q.limit + 1],
  );
  return r.rows;
}
