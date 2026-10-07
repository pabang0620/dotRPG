// chat_messages와 채팅이 쓰는 조회 SQL
import type { Queryable } from '../../db/pool';
import { query } from '../../db/pool';

export interface StoredLine {
  id: number;
  channel: 'general' | 'party' | 'whisper';
  sender_account_id: number;
  sender_character_uuid: string;
  sender_name: string;
  sender_title: string | null;
  recipient_character_uuid: string | null;
  recipient_name: string | null;
  text: string;
  filtered: boolean;
  created_at: Date;
}

interface RawLine extends Omit<StoredLine, 'id' | 'sender_account_id'> {
  id: string;
  sender_account_id: string;
}
const toLine = (r: RawLine): StoredLine => ({ ...r, id: Number(r.id), sender_account_id: Number(r.sender_account_id) });

const LINE_SELECT = `SELECT m.id, m.channel, m.sender_account_id, sc.uuid AS sender_character_uuid, m.sender_name, m.sender_title,
       rc.uuid AS recipient_character_uuid, m.recipient_name, m.text, m.filtered, m.created_at
  FROM chat_messages m
  JOIN characters sc ON sc.id = m.sender_character_id
  LEFT JOIN characters rc ON rc.id = m.recipient_character_id`;

export interface NewMessage {
  /** 장착한 칭호 이름(없으면 null) */
  senderTitle?: string | null;
  channel: 'general' | 'party' | 'whisper';
  shard: number | null;
  partyId: number | null;
  senderAccountId: number;
  senderCharacterId: number;
  senderName: string;
  recipientAccountId: number | null;
  recipientCharacterId: number | null;
  recipientName: string | null;
  text: string;
  filtered: boolean;
  clientMsgId: string;
}

/** 같은 (보낸 계정, cid)가 이미 있으면 null(재전송) */
export async function insertMessage(m: NewMessage): Promise<{ id: number; createdAt: Date } | null> {
  const r = await query<{ id: string; created_at: Date }>(
    `INSERT INTO chat_messages (channel, shard, party_id, sender_account_id, sender_character_id, sender_name,
                                recipient_account_id, recipient_character_id, recipient_name, text, filtered, client_msg_id, sender_title)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
     ON CONFLICT ON CONSTRAINT chat_messages_client_uq DO NOTHING
     RETURNING id, created_at`,
    [m.channel, m.shard, m.partyId, m.senderAccountId, m.senderCharacterId, m.senderName, m.recipientAccountId,
      m.recipientCharacterId, m.recipientName, m.text, m.filtered, m.clientMsgId, m.senderTitle ?? null],
  );
  const row = r.rows[0];
  return row ? { id: Number(row.id), createdAt: row.created_at } : null;
}

export async function findByCid(accountId: number, cid: string): Promise<StoredLine | null> {
  const r = await query<RawLine>(`${LINE_SELECT} WHERE m.sender_account_id = $1 AND m.client_msg_id = $2`, [accountId, cid]);
  return r.rows[0] ? toLine(r.rows[0]) : null;
}

export async function maxSeq(db: Queryable): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT COALESCE(max(id), 0) AS n FROM chat_messages');
  return Number((r.rows[0] as { n: string }).n);
}

/** 일반 채널 따라잡기: 내 shard, since 이후, cursor 이하, 최근 N분. limit+1줄을 읽어 넘침을 알린다 */
export async function generalBacklog(shard: number, since: number, cursor: number, minutes: number, limit: number): Promise<StoredLine[]> {
  const r = await query<RawLine>(
    `${LINE_SELECT} WHERE m.channel = 'general' AND m.shard = $1 AND m.id > $2 AND m.id <= $3
        AND m.created_at >= now() - ($4::int * interval '1 minute') ORDER BY m.id DESC LIMIT $5`,
    [shard, since, cursor, minutes, limit + 1],
  );
  return r.rows.map(toLine);
}

/** 파티 따라잡기: 지금 속한 파티에서 내 가입 시각 이후 */
export async function partyBacklog(characterId: number, since: number, cursor: number, limit: number): Promise<StoredLine[]> {
  const r = await query<RawLine>(
    `${LINE_SELECT}
      JOIN party_members pm ON pm.party_id = m.party_id AND pm.character_id = $1 AND pm.left_at IS NULL
      WHERE m.channel = 'party' AND m.id > $2 AND m.id <= $3 AND m.created_at >= pm.joined_at
      ORDER BY m.id DESC LIMIT $4`,
    [characterId, since, cursor, limit + 1],
  );
  return r.rows.map(toLine);
}

export async function whisperBacklog(characterId: number, since: number, cursor: number, minutes: number, limit: number): Promise<StoredLine[]> {
  const r = await query<RawLine>(
    `${LINE_SELECT} WHERE m.channel = 'whisper' AND m.recipient_character_id = $1 AND m.id > $2 AND m.id <= $3
        AND m.created_at >= now() - ($4::int * interval '1 minute') ORDER BY m.id DESC LIMIT $5`,
    [characterId, since, cursor, minutes, limit + 1],
  );
  return r.rows.map(toLine);
}

export interface TargetCharacter {
  id: number;
  uuid: string;
  account_id: number;
  name: string;
  deleted: boolean;
}
interface RawTarget extends Omit<TargetCharacter, 'id' | 'account_id' | 'deleted'> {
  id: string;
  account_id: string;
  deleted_at: Date | null;
}
const toTarget = (r: RawTarget): TargetCharacter => ({
  id: Number(r.id),
  uuid: r.uuid,
  account_id: Number(r.account_id),
  name: r.name,
  deleted: r.deleted_at !== null,
});

/** 삭제된 캐릭터도 찾는다(차단·신고·친구 요청 대상 지목) */
export async function findCharacterByUuid(db: Queryable, uuid: string): Promise<TargetCharacter | null> {
  const r = await db.query<RawTarget>('SELECT id, uuid, account_id, name, deleted_at FROM characters WHERE uuid = $1', [uuid]);
  return r.rows[0] ? toTarget(r.rows[0]) : null;
}

export async function findAliveByName(name: string): Promise<TargetCharacter | null> {
  const r = await query<RawTarget>(
    'SELECT id, uuid, account_id, name, deleted_at FROM characters WHERE lower(name) = lower($1) AND deleted_at IS NULL LIMIT 1',
    [name],
  );
  return r.rows[0] ? toTarget(r.rows[0]) : null;
}

/** 지금 속한 파티와 가입 시각, 파티원 캐릭터 id */
export async function activeParty(characterId: number): Promise<{ partyId: number; memberIds: number[] } | null> {
  const r = await query<{ party_id: string; character_id: string }>(
    `SELECT m.party_id, m.character_id FROM party_members m
      WHERE m.left_at IS NULL AND m.party_id = (SELECT party_id FROM party_members WHERE character_id = $1 AND left_at IS NULL)`,
    [characterId],
  );
  const first = r.rows[0];
  if (!first) return null;
  return { partyId: Number(first.party_id), memberIds: r.rows.map((x) => Number(x.character_id)) };
}

/** blocker가 blocked 계정을 차단 중인가 */
export async function isBlockedBy(blocker: number, blocked: number): Promise<boolean> {
  const r = await query('SELECT 1 FROM blocks WHERE blocker_account_id = $1 AND blocked_account_id = $2 AND deleted_at IS NULL', [blocker, blocked]);
  return r.rows.length > 0;
}
