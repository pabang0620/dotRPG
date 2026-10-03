// 신고와 증거 SQL. 증거는 서버가 접수 순간 chat_messages에서 복사한다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface EvidenceLine {
  id: number;
  channel: 'general' | 'party' | 'whisper';
  sender_account_id: number;
  sender_character_id: number;
  sender_name: string;
  recipient_name: string | null;
  text: string;
  created_at: Date;
}
interface Raw extends Omit<EvidenceLine, 'id' | 'sender_account_id' | 'sender_character_id'> {
  id: string;
  sender_account_id: string;
  sender_character_id: string;
}
const to = (r: Raw): EvidenceLine => ({
  ...r,
  id: Number(r.id),
  sender_account_id: Number(r.sender_account_id),
  sender_character_id: Number(r.sender_character_id),
});

// 내가 볼 수 있는 줄: 내 shard의 일반, 내가 속했던 파티의 채팅, 나와 대상 사이의 귓속말(제3자와의 귓속말은 제외)
const VISIBLE = `
  m.created_at >= $4::timestamptz
  AND (
    (m.channel = 'general' AND m.shard = $3::int AND m.created_at >= $7::timestamptz)
    OR (m.channel = 'party' AND EXISTS (
          SELECT 1 FROM party_members pm
           WHERE pm.party_id = m.party_id AND pm.account_id = $1 AND pm.joined_at <= m.created_at
             AND (pm.left_at IS NULL OR pm.left_at >= m.created_at)))
    OR (m.channel = 'whisper' AND ((m.sender_account_id = $1 AND m.recipient_account_id = $2)
                                OR (m.sender_account_id = $2 AND m.recipient_account_id = $1)))
  )`;

const SELECT_LINE = `SELECT m.id, m.channel, m.sender_account_id, m.sender_character_id, m.sender_name, m.recipient_name, m.text, m.created_at
  FROM chat_messages m`;

/** 대상이 최근 contextSince 안에 내가 볼 수 있는 줄을 남겼는가 */
export async function targetSpokeInSight(
  db: Queryable,
  me: number,
  target: number,
  shard: number | null,
  since: Date,
  connectedAt: Date,
): Promise<boolean> {
  const r = await db.query(`SELECT 1 FROM chat_messages m WHERE m.sender_account_id = $2 AND ${VISIBLE}
        AND ($5::bigint[] IS NULL OR true) AND ($6::int IS NULL OR true) LIMIT 1`, // $5, $6은 증거 수집 쿼리와 번호를 맞추려고 자리만 채운다
    [me, target, shard, since, null, null, connectedAt]);
  return r.rows.length > 0;
}

export async function sharedParty(db: Queryable, me: number, target: number, since: Date): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM party_members a JOIN party_members b ON a.party_id = b.party_id
      WHERE a.account_id = $1 AND b.account_id = $2
        AND (a.left_at IS NULL OR a.left_at > $3) AND (b.left_at IS NULL OR b.left_at > $3) LIMIT 1`,
    [me, target, since],
  );
  return r.rows.length > 0;
}

export async function areFriends(db: Queryable, me: number, target: number): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM friendships WHERE state = 'accepted'
        AND ((requester_account_id = $1 AND target_account_id = $2) OR (requester_account_id = $2 AND target_account_id = $1))`,
    [me, target],
  );
  return r.rows.length > 0;
}

export async function blockedAccounts(db: Queryable, me: number): Promise<number[]> {
  const r = await db.query<{ a: string }>('SELECT blocked_account_id AS a FROM blocks WHERE blocker_account_id = $1 AND deleted_at IS NULL', [me]);
  return r.rows.map((x) => Number(x.a));
}

export async function countReports(db: Queryable, reporter: number, windowSql: '1 hour' | '1 day'): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM reports WHERE reporter_account_id = $1 AND created_at > now() - interval '${windowSql}'`,
    [reporter],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function findOpen(db: Queryable, reporter: number, target: number, reason: string) {
  const r = await db.query<{ uuid: string; line_count: number; state: string }>(
    `SELECT uuid, line_count, state FROM reports
      WHERE reporter_account_id = $1 AND target_account_id = $2 AND reason = $3 AND state IN ('open', 'reviewing')`,
    [reporter, target, reason],
  );
  return r.rows[0] ?? null;
}

/** (a) 내가 볼 수 있던 최근 N줄 + (b) 그 안에서 대상이 한 최근 M줄. 차단한 계정(대상 제외)의 줄은 뺀다 */
export async function collectEvidence(
  db: Queryable,
  me: number,
  target: number,
  shard: number | null,
  since: Date,
  recent: number,
  targetLines: number,
  blocked: number[],
  connectedAt: Date,
): Promise<EvidenceLine[]> {
  const others = blocked.filter((b) => b !== target);
  const a = await db.query<Raw>(
    `${SELECT_LINE} WHERE ${VISIBLE} AND NOT (m.sender_account_id = ANY($5::bigint[])) ORDER BY m.id DESC LIMIT $6`,
    [me, target, shard, since, others, recent, connectedAt],
  );
  const b = await db.query<Raw>(
    `${SELECT_LINE} WHERE m.sender_account_id = $2 AND ${VISIBLE} AND NOT (m.sender_account_id = ANY($5::bigint[])) ORDER BY m.id DESC LIMIT $6`,
    [me, target, shard, since, others, targetLines, connectedAt],
  );
  const byId = new Map<number, EvidenceLine>();
  for (const r of [...a.rows, ...b.rows].map(to)) byId.set(r.id, r);
  return [...byId.values()].sort((x, y) => x.id - y.id);
}

export async function insertReport(
  client: PoolClient,
  p: { reporter: number; reporterChar: number; target: number; targetChar: number; targetName: string; reason: string; lineCount: number },
): Promise<{ id: number; uuid: string }> {
  const r = await client.query<{ id: string; uuid: string }>(
    `INSERT INTO reports (reporter_account_id, reporter_character_id, target_account_id, target_character_id, target_name, reason, line_count)
     VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id, uuid`,
    [p.reporter, p.reporterChar, p.target, p.targetChar, p.targetName, p.reason, p.lineCount],
  );
  const row = r.rows[0] as { id: string; uuid: string };
  return { id: Number(row.id), uuid: row.uuid };
}

export async function insertLines(client: PoolClient, reportId: number, lines: EvidenceLine[], targetAccount: number): Promise<void> {
  for (const l of lines) {
    await client.query(
      `INSERT INTO report_lines (report_id, seq, channel, sender_account_id, sender_character_id, sender_name, recipient_name, text, sent_at, is_target)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)`,
      [reportId, l.id, l.channel, l.sender_account_id, l.sender_character_id, l.sender_name, l.recipient_name, l.text, l.created_at, l.sender_account_id === targetAccount],
    );
  }
}
