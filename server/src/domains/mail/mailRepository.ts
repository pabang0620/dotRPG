// 우편 SQL. 우편은 서버만 만든다(플레이어 간 우편 없음).
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import type { Bind } from '../economy/economyRepository';
import { announceMail, type MailKind } from './mailNotify';
import { insertItemLedger } from '../economy/economyRepository';

export type SystemCode = 'compensation' | 'event' | 'refund' | 'notice' | 'maintenance' | 'apology' | 'attendance' | 'other';

export interface NewMail {
  characterId: number;
  kind: MailKind;
  /** kind=system일 때만(0008 mails_system_chk) */
  systemCode?: SystemCode | null;
  listingId: number | null;
  bidId: number | null;
  refItemKey: string | null;
  refCount: number | null;
  itemKey: string | null;
  count: number | null;
  bind: Bind | null;
  gold: number;
  createdAt: Date;
  expiresAt: Date;
  /** 10단계 캠페인 우편: 제목·본문·캠페인·첨부 수(첨부가 있으면 item_key는 NULL, gold는 0) */
  title?: string | null;
  body?: string | null;
  campaignId?: number | null;
  attachN?: number;
}

export async function insertMail(
  client: PoolClient,
  m: NewMail,
): Promise<{ id: number; uuid: string; characterUuid: string }> {
  const r = await client.query<{ id: string; uuid: string; char_uuid: string }>(
    `WITH ins AS (
       INSERT INTO mails (character_id, kind, listing_id, bid_id, ref_item_key, ref_count, item_key, count, bind, gold,
                          created_at, expires_at, system_code, title, body, campaign_id, attach_n)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)
       RETURNING id, uuid)
     SELECT ins.id, ins.uuid, (SELECT uuid FROM characters WHERE id = $1) AS char_uuid FROM ins`,
    [
      m.characterId, m.kind, m.listingId, m.bidId, m.refItemKey, m.refCount, m.itemKey, m.count, m.bind, m.gold,
      m.createdAt, m.expiresAt, m.systemCode ?? null, m.title ?? null, m.body ?? null, m.campaignId ?? null, m.attachN ?? 0,
    ],
  );
  const row = r.rows[0] as { id: string; uuid: string; char_uuid: string };
  return { id: Number(row.id), uuid: row.uuid, characterUuid: row.char_uuid };
}

/**
 * D6 시스템 우편: 운영 지급이 쓰는 유일한 경로. 골드는 우편이 들고 있다가 수령 때 gold_ledger(mail_claim)로 들어가고,
 * 아이템은 item_ledger('admin_grant', mail +n)로 남긴다. 알림은 커밋 뒤에 나간다(announceMail).
 */
export async function createSystemMail(
  client: PoolClient,
  m: {
    characterId: number;
    systemCode: SystemCode;
    gold: number;
    item: { itemKey: string; count: number; bind: Bind } | null;
    now: Date;
    expiresAt: Date;
    requestId: string;
  },
): Promise<{ id: number; uuid: string; characterUuid: string }> {
  const mail = await insertMail(client, {
    characterId: m.characterId,
    kind: 'system',
    systemCode: m.systemCode,
    listingId: null,
    bidId: null,
    refItemKey: m.item?.itemKey ?? null,
    refCount: m.item?.count ?? null,
    itemKey: m.item?.itemKey ?? null,
    count: m.item?.count ?? null,
    bind: m.item?.bind ?? null,
    gold: m.gold,
    createdAt: m.now,
    expiresAt: m.expiresAt,
  });
  if (m.item) {
    await insertItemLedger(client, m.characterId, m.item.itemKey, m.item.count, m.item.count, 'mail', 'admin_grant', mail.uuid, m.requestId);
  }
  announceMail(client, {
    characterUuid: mail.characterUuid,
    mailUuid: mail.uuid,
    kind: 'system',
    refItemKey: m.item?.itemKey ?? null,
    gold: m.gold,
    at: m.now,
  });
  return mail;
}

export interface MailRow {
  id: number;
  uuid: string;
  characterId: number;
  kind: MailKind;
  systemCode: SystemCode | null;
  listingId: number | null;
  refItemKey: string | null;
  refCount: number | null;
  itemKey: string | null;
  count: number | null;
  bind: Bind | null;
  gold: number;
  createdAt: Date;
  expiresAt: Date;
  claimedAt: Date | null;
  expiredAt: Date | null;
  /** 10단계: 옛 우편은 null / 0 */
  title: string | null;
  body: string | null;
  campaignId: number | null;
  attachN: number;
}

interface RawMail {
  id: string;
  uuid: string;
  character_id: string;
  kind: MailKind;
  system_code: SystemCode | null;
  listing_id: string | null;
  ref_item_key: string | null;
  ref_count: number | null;
  item_key: string | null;
  count: number | null;
  bind: Bind | null;
  gold: string;
  created_at: Date;
  expires_at: Date;
  claimed_at: Date | null;
  expired_at: Date | null;
  title: string | null;
  body: string | null;
  campaign_id: string | null;
  attach_n: number;
}

const MAIL_COLS = `id, uuid, character_id, kind, system_code, listing_id, ref_item_key, ref_count, item_key, count, bind, gold,
  created_at, expires_at, claimed_at, expired_at, title, body, campaign_id, attach_n`;

const toMail = (r: RawMail): MailRow => ({
  id: Number(r.id),
  uuid: r.uuid,
  characterId: Number(r.character_id),
  kind: r.kind,
  systemCode: r.system_code,
  listingId: r.listing_id === null ? null : Number(r.listing_id),
  refItemKey: r.ref_item_key,
  refCount: r.ref_count,
  itemKey: r.item_key,
  count: r.count,
  bind: r.bind,
  gold: Number(r.gold),
  createdAt: r.created_at,
  expiresAt: r.expires_at,
  claimedAt: r.claimed_at,
  expiredAt: r.expired_at,
  title: r.title,
  body: r.body,
  campaignId: r.campaign_id === null ? null : Number(r.campaign_id),
  attachN: r.attach_n,
});

/** 한 통을 잠그고 읽는다(받는 사람 확인은 호출 쪽) */
export async function lockMailByUuid(client: PoolClient, uuid: string): Promise<MailRow | null> {
  const r = await client.query<RawMail>(`SELECT ${MAIL_COLS} FROM mails WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? toMail(r.rows[0]) : null;
}

/** 받을 수 있는 우편(미수령, 미폐기, 기한 안)을 오래된 순으로 잠그고 읽는다 */
export async function lockOpenMails(client: PoolClient, characterId: number, now: Date, limit: number): Promise<MailRow[]> {
  const r = await client.query<RawMail>(
    `SELECT ${MAIL_COLS} FROM mails
      WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2
      ORDER BY id LIMIT $3 FOR UPDATE`,
    [characterId, now, limit],
  );
  return r.rows.map(toMail);
}

export async function countOpenMails(db: Queryable, characterId: number, now: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM mails
      WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2`,
    [characterId, now],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 조건부 UPDATE로 한 번만 수령한다. 영향 행이 없으면 false */
export async function markClaimed(client: PoolClient, mailId: number, now: Date): Promise<boolean> {
  const r = await client.query(
    'UPDATE mails SET claimed_at = $2 WHERE id = $1 AND claimed_at IS NULL AND expired_at IS NULL',
    [mailId, now],
  );
  return (r.rowCount ?? 0) === 1;
}

export async function listOpenMails(
  db: Queryable,
  characterId: number,
  now: Date,
  tab: 'all' | 'gold' | 'item',
  limit: number,
  offset: number,
): Promise<{ rows: MailRow[]; total: number }> {
  // 첨부 표가 있는 우편(10단계)은 첨부 종류로 탭을 가른다: 골드 탭 = 골드 첨부, 아이템 탭 = 아이템·클리어권 첨부
  const filter =
    tab === 'gold'
      ? `AND (gold > 0 OR EXISTS (SELECT 1 FROM mail_attachments a WHERE a.mail_id = mails.id AND a.kind = 'gold'))`
      : tab === 'item'
        ? `AND (item_key IS NOT NULL OR EXISTS (SELECT 1 FROM mail_attachments a WHERE a.mail_id = mails.id AND a.kind IN ('item', 'sweep_ticket')))`
        : '';
  const base = `FROM mails WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2 ${filter}`;
  const rows = await db.query<RawMail>(
    `SELECT ${MAIL_COLS} ${base} ORDER BY created_at DESC, id DESC LIMIT $3 OFFSET $4`,
    [characterId, now, limit, offset],
  );
  const total = await db.query<{ n: string }>(`SELECT count(*) AS n ${base}`, [characterId, now]);
  return { rows: rows.rows.map(toMail), total: Number((total.rows[0] as { n: string }).n) };
}

export async function summaryOf(
  db: Queryable,
  characterId: number,
  now: Date,
  since: Date | null,
): Promise<{ unclaimed: number; latestAt: Date | null; fresh: MailRow[] }> {
  const agg = await db.query<{ n: string; latest: Date | null }>(
    `SELECT count(*) AS n, max(created_at) AS latest FROM mails
      WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2`,
    [characterId, now],
  );
  const a = agg.rows[0] as { n: string; latest: Date | null };
  let fresh: MailRow[] = [];
  if (since) {
    const r = await db.query<RawMail>(
      `SELECT ${MAIL_COLS} FROM mails
        WHERE character_id = $1 AND created_at > $3 AND expired_at IS NULL AND expires_at > $2
        ORDER BY created_at DESC, id DESC LIMIT 20`,
      [characterId, now, since],
    );
    fresh = r.rows.map(toMail);
  }
  return { unclaimed: Number(a.n), latestAt: a.latest, fresh };
}

export async function expiredMailIds(db: Queryable, now: Date, limit: number): Promise<number[]> {
  const r = await db.query<{ id: string }>(
    `SELECT id FROM mails WHERE claimed_at IS NULL AND expired_at IS NULL AND expires_at <= $1 ORDER BY expires_at LIMIT $2`,
    [now, limit],
  );
  return r.rows.map((x) => Number(x.id));
}

/** 기한 폐기 대상 한 통을 잠근다(사용 중이면 건너뜀) */
export async function lockExpiredMail(client: PoolClient, id: number, now: Date): Promise<MailRow | null> {
  const r = await client.query<RawMail>(
    `SELECT ${MAIL_COLS} FROM mails
      WHERE id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at <= $2 FOR UPDATE SKIP LOCKED`,
    [id, now],
  );
  return r.rows[0] ? toMail(r.rows[0]) : null;
}

export async function markExpired(client: PoolClient, mailId: number, now: Date): Promise<void> {
  await client.query('UPDATE mails SET expired_at = $2 WHERE id = $1', [mailId, now]);
}

/** 이 캐릭터가 받을 우편(첨부·골드)이 남았는가: 캐릭터 삭제 거절 판정 */
export async function hasOpenMail(db: Queryable, characterId: number): Promise<boolean> {
  const r = await db.query(
    'SELECT 1 FROM mails WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL LIMIT 1',
    [characterId],
  );
  return (r.rowCount ?? 0) > 0;
}

export async function findMailOfListing(
  db: Queryable,
  listingId: number,
  characterId: number,
  kind: MailKind,
): Promise<string | null> {
  const r = await db.query<{ uuid: string }>(
    'SELECT uuid FROM mails WHERE listing_id = $1 AND character_id = $2 AND kind = $3',
    [listingId, characterId, kind],
  );
  return r.rows[0]?.uuid ?? null;
}
