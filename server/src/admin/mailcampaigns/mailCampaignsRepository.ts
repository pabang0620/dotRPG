// 캠페인 관리자 SQL(MC1~MC6). 캠페인 행은 만든 뒤 내용이 바뀌지 않는다(DB 트리거). 상태 열만 바뀐다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import type { Attachment } from '../../domains/mail/mailAttachments';
import type { Bind } from '../../domains/economy/economyRepository';

export type CampaignStatus = 'pending' | 'active' | 'ended' | 'cancelled';

export interface CampaignRow {
  id: number;
  uuid: string;
  title: string;
  body: string;
  category: string;
  deliveryUnit: 'account' | 'character';
  target: Record<string, unknown>;
  mailDays: number;
  startsAt: Date;
  endsAt: Date;
  capCount: number;
  issuedCount: number;
  status: CampaignStatus;
  memo: string;
  createdBy: number;
  createdByName: string;
  approvedBy: number | null;
  approvedByName: string | null;
  approvedAt: Date | null;
  cancelledBy: number | null;
  cancelledByName: string | null;
  cancelledAt: Date | null;
  cancelReason: string | null;
  revokeRequested: boolean;
  revokeDoneAt: Date | null;
  revokedCount: number;
  createdAt: Date;
}

interface Raw {
  id: string;
  uuid: string;
  title: string;
  body: string;
  category: string;
  delivery_unit: 'account' | 'character';
  target: Record<string, unknown>;
  mail_days: number;
  starts_at: Date;
  ends_at: Date;
  cap_count: number;
  issued_count: number;
  status: CampaignStatus;
  memo: string;
  created_by: string;
  created_by_name: string;
  approved_by: string | null;
  approved_by_name: string | null;
  approved_at: Date | null;
  cancelled_by: string | null;
  cancelled_by_name: string | null;
  cancelled_at: Date | null;
  cancel_reason: string | null;
  revoke_requested: boolean;
  revoke_done_at: Date | null;
  revoked_count: number;
  created_at: Date;
}

const SELECT = `SELECT c.id, c.uuid, c.title, c.body, c.category, c.delivery_unit, c.target, c.mail_days, c.starts_at, c.ends_at,
       c.cap_count, c.issued_count, c.status, c.memo, c.created_by, cb.display_name AS created_by_name,
       c.approved_by, ab.display_name AS approved_by_name, c.approved_at,
       c.cancelled_by, xb.display_name AS cancelled_by_name, c.cancelled_at, c.cancel_reason,
       c.revoke_requested, c.revoke_done_at, c.revoked_count, c.created_at
  FROM mail_campaigns c
  JOIN admin_users cb ON cb.id = c.created_by
  LEFT JOIN admin_users ab ON ab.id = c.approved_by
  LEFT JOIN admin_users xb ON xb.id = c.cancelled_by`;

const toRow = (r: Raw): CampaignRow => ({
  id: Number(r.id),
  uuid: r.uuid,
  title: r.title,
  body: r.body,
  category: r.category,
  deliveryUnit: r.delivery_unit,
  target: r.target,
  mailDays: r.mail_days,
  startsAt: r.starts_at,
  endsAt: r.ends_at,
  capCount: r.cap_count,
  issuedCount: r.issued_count,
  status: r.status,
  memo: r.memo,
  createdBy: Number(r.created_by),
  createdByName: r.created_by_name,
  approvedBy: r.approved_by === null ? null : Number(r.approved_by),
  approvedByName: r.approved_by_name,
  approvedAt: r.approved_at,
  cancelledBy: r.cancelled_by === null ? null : Number(r.cancelled_by),
  cancelledByName: r.cancelled_by_name,
  cancelledAt: r.cancelled_at,
  cancelReason: r.cancel_reason,
  revokeRequested: r.revoke_requested,
  revokeDoneAt: r.revoke_done_at,
  revokedCount: r.revoked_count,
  createdAt: r.created_at,
});

export interface NewCampaign {
  title: string;
  body: string;
  category: string;
  deliveryUnit: 'account' | 'character';
  target: Record<string, unknown>;
  mailDays: number;
  startsAt: Date;
  endsAt: Date;
  capCount: number;
  memo: string;
  createdBy: number;
}

export async function insertCampaign(client: PoolClient, n: NewCampaign): Promise<{ id: number; uuid: string }> {
  const r = await client.query<{ id: string; uuid: string }>(
    `INSERT INTO mail_campaigns (title, body, category, delivery_unit, target, mail_days, starts_at, ends_at, cap_count, memo, created_by)
     VALUES ($1, $2, $3, $4, $5::jsonb, $6, $7, $8, $9, $10, $11) RETURNING id, uuid`,
    [n.title, n.body, n.category, n.deliveryUnit, JSON.stringify(n.target), n.mailDays, n.startsAt, n.endsAt, n.capCount, n.memo, n.createdBy],
  );
  const x = r.rows[0] as { id: string; uuid: string };
  return { id: Number(x.id), uuid: x.uuid };
}

export async function insertCampaignAttachments(client: PoolClient, campaignId: number, atts: Attachment[]): Promise<void> {
  for (const a of atts) {
    await client.query(
      'INSERT INTO mail_campaign_attachments (campaign_id, slot, kind, item_key, amount, bind) VALUES ($1, $2, $3, $4, $5, $6)',
      [campaignId, a.slot, a.kind, a.itemKey, a.amount, a.bind],
    );
  }
}

export async function campaignAttachments(db: Queryable, campaignId: number): Promise<Attachment[]> {
  const r = await db.query<{ slot: number; kind: Attachment['kind']; item_key: string | null; amount: string; bind: Bind | null }>(
    'SELECT slot, kind, item_key, amount, bind FROM mail_campaign_attachments WHERE campaign_id = $1 ORDER BY slot',
    [campaignId],
  );
  return r.rows.map((x) => ({ slot: x.slot, kind: x.kind, itemKey: x.item_key, amount: Number(x.amount), bind: x.bind }));
}

export async function findByUuid(db: Queryable, uuid: string): Promise<CampaignRow | null> {
  const r = await db.query<Raw>(`${SELECT} WHERE c.uuid = $1`, [uuid]);
  return r.rows[0] ? toRow(r.rows[0]) : null;
}

/** 락 순서 ⑤: 캠페인 행(관리자 상태 변경은 이 행만 잠근다) */
export async function lockByUuid(client: PoolClient, uuid: string): Promise<CampaignRow | null> {
  const lock = await client.query('SELECT id FROM mail_campaigns WHERE uuid = $1 FOR UPDATE', [uuid]);
  if ((lock.rowCount ?? 0) === 0) return null;
  return findByUuid(client, uuid);
}

export async function list(db: Queryable, status: CampaignStatus | undefined, limit: number, before: number | undefined): Promise<CampaignRow[]> {
  const r = await db.query<Raw>(
    `${SELECT} WHERE ($1::text IS NULL OR c.status = $1) AND ($2::bigint IS NULL OR c.id < $2) ORDER BY c.id DESC LIMIT $3`,
    [status ?? null, before ?? null, limit + 1],
  );
  return r.rows.map(toRow);
}

export async function existingAccountUuids(db: Queryable, uuids: string[]): Promise<Set<string>> {
  const r = await db.query<{ uuid: string }>('SELECT uuid FROM accounts WHERE uuid = ANY($1::uuid[])', [uuids]);
  return new Set(r.rows.map((x) => x.uuid));
}

export async function markApproved(client: PoolClient, id: number, adminId: number, at: Date): Promise<void> {
  await client.query("UPDATE mail_campaigns SET status = 'active', approved_by = $2, approved_at = $3 WHERE id = $1", [id, adminId, at]);
}

export async function markCancelled(client: PoolClient, id: number, adminId: number, at: Date, reason: string, revoke: boolean): Promise<void> {
  await client.query(
    "UPDATE mail_campaigns SET status = 'cancelled', cancelled_by = $2, cancelled_at = $3, cancel_reason = $4, revoke_requested = $5 WHERE id = $1",
    [id, adminId, at, reason, revoke],
  );
}

/** 이미 끝난(ended) 캠페인의 미수령 회수만 요청한다(상태는 그대로) */
export async function requestRevoke(client: PoolClient, id: number): Promise<void> {
  await client.query('UPDATE mail_campaigns SET revoke_requested = true WHERE id = $1', [id]);
}

/** 우편 상태 집계(설계 7.4 MC3). revoked = 회수로 기한이 앞당겨진 미수령 우편 */
const MAIL_STATE = `CASE
    WHEN m.claimed_at IS NOT NULL THEN 'claimed'
    WHEN m.expires_at < m.created_at + (c.mail_days * interval '1 day') AND (m.expired_at IS NOT NULL OR m.expires_at <= $2) THEN 'revoked'
    WHEN m.expired_at IS NOT NULL OR m.expires_at <= $2 THEN 'expired'
    ELSE 'open' END`;

export async function stats(db: Queryable, campaignId: number, now: Date) {
  const r = await db.query<{ state: string; n: string }>(
    `SELECT ${MAIL_STATE} AS state, count(*) AS n FROM mails m JOIN mail_campaigns c ON c.id = m.campaign_id
      WHERE m.campaign_id = $1 GROUP BY 1`,
    [campaignId, now],
  );
  const by = new Map(r.rows.map((x) => [x.state, Number(x.n)] as const));
  const sums = await db.query<{ kind: string; promised: string; claimed: string }>(
    `SELECT a.kind, coalesce(sum(a.amount), 0) AS promised, coalesce(sum(a.amount) FILTER (WHERE m.claimed_at IS NOT NULL), 0) AS claimed
       FROM mail_attachments a JOIN mails m ON m.id = a.mail_id
      WHERE m.campaign_id = $1 AND a.kind IN ('gold', 'sweep_ticket') GROUP BY a.kind`,
    [campaignId],
  );
  const sum = (kind: string, col: 'promised' | 'claimed'): number => Number(sums.rows.find((x) => x.kind === kind)?.[col] ?? 0);
  return {
    issued: [...by.values()].reduce((a, b) => a + b, 0),
    claimed: by.get('claimed') ?? 0,
    unclaimed_open: by.get('open') ?? 0,
    expired: by.get('expired') ?? 0,
    revoked: by.get('revoked') ?? 0,
    gold_promised: sum('gold', 'promised'),
    gold_claimed: sum('gold', 'claimed'),
    tickets_promised: sum('sweep_ticket', 'promised'),
    tickets_claimed: sum('sweep_ticket', 'claimed'),
  };
}

/** 회수 대기 중인 미수령 우편 수(MC3 revoke.remaining) */
export async function revokeRemaining(db: Queryable, campaignId: number, now: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    'SELECT count(*) AS n FROM mails WHERE campaign_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2',
    [campaignId, now],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export interface DeliveryRow {
  account_id: string;
  character_id: string;
  character_name: string;
  delivered_at: Date;
  state: string;
  claimed_at: Date | null;
  mail_row_id: string;
}

export async function deliveries(db: Queryable, campaignId: number, now: Date, state: string | undefined, limit: number, before: number | undefined): Promise<DeliveryRow[]> {
  const r = await db.query<DeliveryRow>(
    `SELECT * FROM (
       SELECT d.id AS mail_row_id, a.uuid AS account_id, ch.uuid AS character_id, ch.name AS character_name,
              m.created_at AS delivered_at, m.claimed_at, ${MAIL_STATE} AS state
         FROM mail_campaign_deliveries d
         JOIN mail_campaigns c ON c.id = d.campaign_id
         JOIN mails m ON m.id = d.mail_id
         JOIN accounts a ON a.id = d.account_id
         JOIN characters ch ON ch.id = d.character_id
        WHERE d.campaign_id = $1 AND ($4::bigint IS NULL OR d.id < $4)
     ) t WHERE ($3::text IS NULL OR t.state = $3) ORDER BY t.mail_row_id DESC LIMIT $5`,
    [campaignId, now, state ?? null, before ?? null, limit + 1],
  );
  return r.rows;
}
