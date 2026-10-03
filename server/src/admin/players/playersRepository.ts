// 플레이어 조회 SQL(PL1~PL4). 읽기 전용이고 외부에는 uuid만 내보낸다.
import type { Queryable } from '../../db/pool';

export interface AccountBrief {
  id: number;
  uuid: string;
  created_at: Date;
  last_login_at: Date | null;
  banned_until: Date | null;
  deleted_at: Date | null;
}

interface RawAcct extends Omit<AccountBrief, 'id'> {
  id: string;
}
const toAcct = (r: RawAcct): AccountBrief => ({ ...r, id: Number(r.id) });

const COLS = 'a.id, a.uuid, a.created_at, a.last_login_at, a.banned_until, a.deleted_at';

export async function findByAccountOrCharacterUuid(db: Queryable, uuid: string): Promise<AccountBrief[]> {
  const r = await db.query<RawAcct>(
    `SELECT ${COLS} FROM accounts a
      WHERE a.uuid = $1 OR a.id IN (SELECT account_id FROM characters WHERE uuid = $1) LIMIT 20`,
    [uuid],
  );
  return r.rows.map(toAcct);
}

export async function findByCharacterName(db: Queryable, name: string): Promise<AccountBrief[]> {
  const r = await db.query<RawAcct>(
    `SELECT DISTINCT ON (a.id) ${COLS} FROM accounts a JOIN characters c ON c.account_id = a.id
      WHERE lower(c.name) = lower($1) ORDER BY a.id LIMIT 20`,
    [name],
  );
  return r.rows.map(toAcct);
}

export async function findByIdentity(db: Queryable, provider: 'steam' | 'dev', subject: string): Promise<AccountBrief[]> {
  const r = await db.query<RawAcct>(
    `SELECT ${COLS} FROM accounts a JOIN auth_identities i ON i.account_id = a.id
      WHERE i.provider = $1 AND i.subject = $2 LIMIT 20`,
    [provider, subject],
  );
  return r.rows.map(toAcct);
}

export async function accountByUuid(db: Queryable, uuid: string): Promise<AccountBrief | null> {
  const r = await db.query<RawAcct>(`SELECT ${COLS} FROM accounts a WHERE a.uuid = $1`, [uuid]);
  return r.rows[0] ? toAcct(r.rows[0]) : null;
}

export async function accountByUuidForUpdate(db: Queryable, uuid: string): Promise<AccountBrief | null> {
  const r = await db.query<RawAcct>(`SELECT ${COLS} FROM accounts a WHERE a.uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? toAcct(r.rows[0]) : null;
}

export async function identitiesOf(db: Queryable, accountId: number) {
  const r = await db.query<{ provider: string; subject: string; created_at: Date }>(
    'SELECT provider, subject, created_at FROM auth_identities WHERE account_id = $1 ORDER BY id',
    [accountId],
  );
  return r.rows;
}

export async function charactersOf(db: Queryable, accountId: number) {
  const r = await db.query<{ uuid: string; name: string; class: string; level: number; gold: string; deleted_at: Date | null }>(
    'SELECT uuid, name, class, level, gold, deleted_at FROM characters WHERE account_id = $1 ORDER BY id',
    [accountId],
  );
  return r.rows;
}

export async function sanctionsOf(db: Queryable, accountId: number) {
  const r = await db.query<{
    uuid: string; kind: string; source: string; reason_code: string; starts_at: Date; ends_at: Date | null; created_by: string;
    revoked_at: Date | null; note: string | null; active: boolean;
  }>(
    `SELECT uuid, kind, source, reason_code, starts_at, ends_at, created_by, revoked_at, note,
            (revoked_at IS NULL AND (ends_at IS NULL OR ends_at > now()) AND kind <> 'warning') AS active
       FROM account_sanctions WHERE account_id = $1 ORDER BY id DESC LIMIT 20`,
    [accountId],
  );
  return r.rows;
}

export async function reportSummary(db: Queryable, accountId: number) {
  const against = await db.query<{ state: string; n: string }>(
    'SELECT state, count(*) AS n FROM reports WHERE target_account_id = $1 GROUP BY state',
    [accountId],
  );
  const recent = await db.query<{ uuid: string; reason: string; state: string; created_at: Date }>(
    'SELECT uuid, reason, state, created_at FROM reports WHERE target_account_id = $1 ORDER BY id DESC LIMIT 5',
    [accountId],
  );
  const by = await db.query<{ total: string; dismissed: string }>(
    `SELECT count(*) AS total, count(*) FILTER (WHERE state = 'dismissed') AS dismissed FROM reports WHERE reporter_account_id = $1`,
    [accountId],
  );
  return { against: against.rows, recent: recent.rows, by: by.rows[0] as { total: string; dismissed: string } };
}

export async function anomalySummary(db: Queryable, accountId: number) {
  const r = await db.query<{ kind: string; h24: string; d7: string; maxsev: number }>(
    `SELECT kind, count(*) FILTER (WHERE created_at >= now() - interval '24 hours') AS h24,
            count(*) AS d7, max(severity) AS maxsev
       FROM anomaly_log WHERE account_id = $1 AND created_at >= now() - interval '7 days' GROUP BY kind ORDER BY kind`,
    [accountId],
  );
  return r.rows;
}

export async function flagSummary(db: Queryable, accountId: number) {
  const r = await db.query<{ kind: string; n: string }>(
    `SELECT kind, count(*) AS n FROM auction_flags WHERE account_id = $1 AND created_at >= now() - interval '7 days' GROUP BY kind ORDER BY kind`,
    [accountId],
  );
  return r.rows;
}

export async function heldRunCount(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM dungeon_runs d JOIN characters c ON c.id = d.character_id
      WHERE c.account_id = $1 AND d.state = 'held'`,
    [accountId],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function tradeSummary(db: Queryable, accountId: number) {
  const r = await db.query<{ n: string; gold: string; partners: string }>(
    `SELECT count(*) AS n, coalesce(sum(price), 0) AS gold,
            count(DISTINCT CASE WHEN buyer_account_id = $1 THEN seller_account_id ELSE buyer_account_id END) AS partners
       FROM auction_trades WHERE (buyer_account_id = $1 OR seller_account_id = $1) AND traded_at >= now() - interval '7 days'`,
    [accountId],
  );
  return r.rows[0] as { n: string; gold: string; partners: string };
}

export async function notesOf(db: Queryable, accountId: number) {
  const r = await db.query<{ uuid: string; kind: string; note: string; created_at: Date; admin: string }>(
    `SELECT n.uuid, n.kind, n.note, n.created_at, u.login_id AS admin
       FROM admin_account_notes n JOIN admin_users u ON u.id = n.admin_id
      WHERE n.account_id = $1 ORDER BY n.id DESC LIMIT 10`,
    [accountId],
  );
  return r.rows;
}

export async function recentAdminActions(db: Queryable, accountUuid: string) {
  const r = await db.query<{ action: string; result: string; created_at: Date; admin: string | null }>(
    `SELECT l.action, l.result, l.created_at, u.login_id AS admin
       FROM admin_audit_log l LEFT JOIN admin_users u ON u.id = l.admin_id
      WHERE l.target_type = 'account' AND l.target_uuid = $1 ORDER BY l.id DESC LIMIT 5`,
    [accountUuid],
  );
  return r.rows;
}

export interface CharacterFull {
  id: number;
  uuid: string;
  account_uuid: string;
  name: string;
  class: string;
  level: number;
  xp: number;
  gold: number;
  created_at: Date;
  deleted_at: Date | null;
}

export async function characterByUuid(db: Queryable, uuid: string): Promise<CharacterFull | null> {
  const r = await db.query<{ id: string; uuid: string; account_uuid: string; name: string; class: string; level: number; xp: number; gold: string; created_at: Date; deleted_at: Date | null }>(
    `SELECT c.id, c.uuid, a.uuid AS account_uuid, c.name, c.class, c.level, c.xp, c.gold, c.created_at, c.deleted_at
       FROM characters c JOIN accounts a ON a.id = c.account_id WHERE c.uuid = $1`,
    [uuid],
  );
  const x = r.rows[0];
  return x ? { ...x, id: Number(x.id), gold: Number(x.gold) } : null;
}

export async function itemsOf(db: Queryable, characterId: number) {
  const r = await db.query<{ location: string; item_key: string; bind: string; count: number }>(
    'SELECT location, item_key, bind, count FROM character_items WHERE character_id = $1 ORDER BY location, item_key, bind',
    [characterId],
  );
  return r.rows;
}

export async function characterCounters(db: Queryable, characterId: number) {
  const r = await db.query<{ cleared: string; held: string; open_mails: string; listings: string }>(
    `SELECT (SELECT count(*) FROM dungeon_runs WHERE character_id = $1 AND state = 'cleared') AS cleared,
            (SELECT count(*) FROM dungeon_runs WHERE character_id = $1 AND state = 'held') AS held,
            (SELECT count(*) FROM mails WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL) AS open_mails,
            (SELECT count(*) FROM auction_listings WHERE seller_character_id = $1 AND status = 'active') AS listings`,
    [characterId],
  );
  return r.rows[0] as { cleared: string; held: string; open_mails: string; listings: string };
}

const LEDGER_TABLE = { gold: 'gold_ledger', item: 'item_ledger', xp: 'xp_ledger' } as const;
const LEDGER_EXTRA = {
  gold: 'NULL::text AS item_key, NULL::text AS location, NULL::int AS level_after',
  item: 'item_key, location, NULL::int AS level_after',
  xp: 'NULL::text AS item_key, NULL::text AS location, level_after',
} as const;

export async function ledgerRows(
  db: Queryable,
  kind: 'gold' | 'item' | 'xp',
  characterId: number,
  q: { reason: string | null; request: string | null; since: string | null; before: number | null; limit: number },
) {
  const r = await db.query<{
    id: string; delta: string; reason: string; ref: string | null; request_id: string | null; created_at: Date;
    item_key: string | null; location: string | null; level_after: number | null; balance_after: string | null;
  }>(
    `SELECT id, delta, reason, ref, request_id, created_at, ${LEDGER_EXTRA[kind]},
            ${kind === 'xp' ? 'xp_after::text' : 'balance_after::text'} AS balance_after
       FROM ${LEDGER_TABLE[kind]}
      WHERE character_id = $1 AND ($2::text IS NULL OR reason = $2) AND ($3::uuid IS NULL OR request_id = $3)
        AND ($4::timestamptz IS NULL OR created_at >= $4) AND ($5::bigint IS NULL OR id < $5)
      ORDER BY id DESC LIMIT $6`,
    [characterId, q.reason, q.request, q.since, q.before, q.limit + 1],
  );
  return r.rows;
}

export async function insertNote(db: Queryable, accountId: number, adminId: number, kind: string, note: string) {
  const r = await db.query<{ uuid: string }>(
    'INSERT INTO admin_account_notes (account_id, admin_id, kind, note) VALUES ($1, $2, $3, $4) RETURNING uuid',
    [accountId, adminId, kind, note],
  );
  return (r.rows[0] as { uuid: string }).uuid;
}

export async function devIdentityOf(db: Queryable, accountId: number): Promise<{ subject: string } | null> {
  const r = await db.query<{ subject: string }>("SELECT subject FROM auth_identities WHERE account_id = $1 AND provider = 'dev'", [accountId]);
  return r.rows[0] ?? null;
}

export async function setDevSecret(db: Queryable, accountId: number, hash: string): Promise<void> {
  await db.query("UPDATE auth_identities SET secret_hash = $2 WHERE account_id = $1 AND provider = 'dev'", [accountId, hash]);
  // 비밀번호를 바꾸면 기존 로그인 세션(갱신 토큰)을 모두 폐기한다
  await db.query('UPDATE refresh_tokens SET revoked_at = now() WHERE account_id = $1 AND revoked_at IS NULL', [accountId]);
}
