import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export async function queue(db: Queryable, states: string[], reason: string | null) {
  const r = await db.query<{ uuid: string; reason: string; target_name: string; line_count: number; created_at: Date; state: string; same_target_open: string }>(
    `SELECT r.uuid, r.reason, r.target_name, r.line_count, r.created_at, r.state,
            (SELECT count(*) FROM reports o WHERE o.target_account_id = r.target_account_id AND o.state IN ('open', 'reviewing')) AS same_target_open
       FROM reports r WHERE r.state = ANY($1::text[]) AND ($2::text IS NULL OR r.reason = $2)
      ORDER BY r.id LIMIT 50`,
    [states, reason],
  );
  return r.rows;
}

export interface ReportFull {
  id: number;
  uuid: string;
  state: string;
  reason: string;
  target_account_id: number;
  target_account_uuid: string;
  target_character_uuid: string;
  target_name: string;
  reporter_account_id: number;
  line_count: number;
  created_at: Date;
  handled_at: Date | null;
  handled_by: string | null;
  note: string | null;
}

export async function findByUuid(db: Queryable, uuid: string, forUpdate = false): Promise<ReportFull | null> {
  const r = await db.query<Record<string, string | number | Date | null>>(
    `SELECT r.id, r.uuid, r.state, r.reason, r.target_account_id, ta.uuid AS target_account_uuid, tc.uuid AS target_character_uuid,
            r.target_name, r.reporter_account_id, r.line_count, r.created_at, r.handled_at, r.handled_by, r.note
       FROM reports r JOIN accounts ta ON ta.id = r.target_account_id JOIN characters tc ON tc.id = r.target_character_id
      WHERE r.uuid = $1${forUpdate ? ' FOR UPDATE OF r' : ''}`,
    [uuid],
  );
  const x = r.rows[0];
  if (!x) return null;
  return {
    ...(x as unknown as ReportFull),
    id: Number(x.id),
    target_account_id: Number(x.target_account_id),
    reporter_account_id: Number(x.reporter_account_id),
  };
}

export async function lines(db: Queryable, reportId: number) {
  const r = await db.query<{ seq: string; channel: string; sender_name: string; recipient_name: string | null; text: string; sent_at: Date; is_target: boolean }>(
    'SELECT seq, channel, sender_name, recipient_name, text, sent_at, is_target FROM report_lines WHERE report_id = $1 ORDER BY seq',
    [reportId],
  );
  return r.rows;
}

export async function targetSanctions(db: Queryable, accountId: number) {
  const r = await db.query<{ uuid: string; kind: string; reason_code: string; starts_at: Date; ends_at: Date | null; revoked_at: Date | null; created_by: string }>(
    'SELECT uuid, kind, reason_code, starts_at, ends_at, revoked_at, created_by FROM account_sanctions WHERE account_id = $1 ORDER BY id DESC LIMIT 20',
    [accountId],
  );
  return r.rows;
}

export async function otherReports(db: Queryable, targetAccountId: number, exceptId: number) {
  const r = await db.query<{ uuid: string; reason: string; state: string; created_at: Date }>(
    `SELECT uuid, reason, state, created_at FROM reports
      WHERE target_account_id = $1 AND id <> $2 AND created_at >= now() - interval '30 days' ORDER BY id DESC LIMIT 20`,
    [targetAccountId, exceptId],
  );
  return r.rows;
}

export async function reporterRatio(db: Queryable, accountId: number) {
  const r = await db.query<{ total: string; dismissed: string }>(
    `SELECT count(*) AS total, count(*) FILTER (WHERE state = 'dismissed') AS dismissed FROM reports WHERE reporter_account_id = $1`,
    [accountId],
  );
  return r.rows[0] as { total: string; dismissed: string };
}

export async function markReviewing(db: Queryable, id: number, by: string): Promise<void> {
  await db.query("UPDATE reports SET state = 'reviewing', handled_by = $2 WHERE id = $1", [id, by]);
}

/** 같은 대상·같은 사유의 다른 열린 신고를 같은 결과로 닫는다(병합 표시). 닫은 수를 돌려준다 */
export async function closeSimilar(client: PoolClient, r: ReportFull, state: 'actioned' | 'dismissed', by: string): Promise<number> {
  const res = await client.query(
    `UPDATE reports SET state = $5, handled_at = now(), handled_by = $4, note = left('병합: ' || $3::text, 500)
      WHERE target_account_id = $1 AND reason = $2 AND state IN ('open', 'reviewing') AND id <> $6`,
    [r.target_account_id, r.reason, r.uuid, by, state, r.id],
  );
  return res.rowCount ?? 0;
}
