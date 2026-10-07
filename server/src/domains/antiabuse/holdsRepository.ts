// 경제 정지 SQL(economy_holds). 막는 상태는 active와 clawed_back이다(부분 인덱스 economy_holds_account_active).
import type { Queryable } from '../../db/pool';

export interface HoldRow {
  id: number;
  uuid: string;
  account_id: number;
  character_id: number | null;
  kind: 'velocity' | 'auction' | 'linked' | 'manual' | 'payment';
  state: 'shadow' | 'active' | 'released' | 'clawed_back';
  origin_hold_id: number | null;
  window_kind: string | null;
  window_start: Date | null;
  window_end: Date | null;
  evidence: Record<string, unknown>;
  created_at: Date;
  reviewed_by: string | null;
  reviewed_at: Date | null;
  released_at: Date | null;
  note: string | null;
  clawback: Record<string, unknown> | null;
}

interface RawHold extends Omit<HoldRow, 'id' | 'account_id' | 'character_id' | 'origin_hold_id'> {
  id: string;
  account_id: string;
  character_id: string | null;
  origin_hold_id: string | null;
}

export const HOLD_COLS = `id, uuid, account_id, character_id, kind, state, origin_hold_id, window_kind, window_start, window_end, evidence,
  created_at, reviewed_by, reviewed_at, released_at, note, clawback`;

export const toHold = (r: RawHold): HoldRow => ({
  ...r,
  id: Number(r.id),
  account_id: Number(r.account_id),
  character_id: r.character_id === null ? null : Number(r.character_id),
  origin_hold_id: r.origin_hold_id === null ? null : Number(r.origin_hold_id),
});

/** 차단 판정(요청마다, 부분 인덱스 한 번): 이 계정 전체 정지이거나 이 캐릭터의 정지 */
export async function findBlocking(db: Queryable, accountId: number, characterId: number): Promise<{ created_at: Date; character_id: number | null } | null> {
  const r = await db.query<{ created_at: Date; character_id: string | null }>(
    `SELECT created_at, character_id FROM economy_holds
      WHERE account_id = $1 AND state IN ('active', 'clawed_back') AND (character_id IS NULL OR character_id = $2)
      ORDER BY created_at LIMIT 1`,
    [accountId, characterId],
  );
  const row = r.rows[0];
  return row ? { created_at: row.created_at, character_id: row.character_id === null ? null : Number(row.character_id) } : null;
}

/** 같은 (계정, 범위)의 막는 정지(범위 0 = 계정 전체) */
export async function blockingInScope(db: Queryable, accountId: number, characterId: number | null): Promise<HoldRow | null> {
  const r = await db.query<RawHold>(
    `SELECT ${HOLD_COLS} FROM economy_holds WHERE account_id = $1 AND scope_char = $2 AND state IN ('active', 'clawed_back') LIMIT 1`,
    [accountId, characterId ?? 0],
  );
  return r.rows[0] ? toHold(r.rows[0]) : null;
}

export async function recentShadow(db: Queryable, accountId: number, characterId: number | null, since: Date): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM economy_holds WHERE account_id = $1 AND scope_char = $2 AND state = 'shadow' AND created_at > $3 LIMIT 1`,
    [accountId, characterId ?? 0, since],
  );
  return r.rows.length > 0;
}

/** 해제 기준선: 이 계정의 해제된 정지 중 가장 늦은 released_at(계정 전체 정지이거나 이 캐릭터의 정지) */
export async function baselineOf(db: Queryable, accountId: number, characterId: number | null): Promise<Date | null> {
  const r = await db.query<{ t: Date | null }>(
    `SELECT max(released_at) AS t FROM economy_holds
      WHERE account_id = $1 AND released_at IS NOT NULL AND ($2::bigint IS NULL OR character_id IS NULL OR character_id = $2)`,
    [accountId, characterId],
  );
  return r.rows[0]?.t ?? null;
}

export interface NewHold {
  accountId: number;
  characterId: number | null;
  kind: HoldRow['kind'];
  state: 'shadow' | 'active';
  originHoldId?: number | null;
  windowKind?: string | null;
  windowStart?: Date | null;
  windowEnd?: Date | null;
  evidence: Record<string, unknown>;
  createdAt: Date;
  note?: string | null;
}

/** 막는 정지를 만들 때 같은 범위에 이미 있으면 유일 인덱스(economy_holds_one_active)가 막는다: 호출 쪽이 isUniqueViolation으로 처리한다 */
export async function insertHold(db: Queryable, n: NewHold): Promise<{ id: number; uuid: string }> {
  const r = await db.query<{ id: string; uuid: string }>(
    `INSERT INTO economy_holds (account_id, character_id, kind, state, origin_hold_id, window_kind, window_start, window_end, evidence, created_at, note,
                                reviewed_by, reviewed_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9::jsonb, $10, $11, NULL, NULL)
     RETURNING id, uuid`,
    [n.accountId, n.characterId, n.kind, n.state, n.originHoldId ?? null, n.windowKind ?? null, n.windowStart ?? null, n.windowEnd ?? null, JSON.stringify(n.evidence), n.createdAt, n.note ?? null],
  );
  const row = r.rows[0] as { id: string; uuid: string };
  return { id: Number(row.id), uuid: row.uuid };
}

export async function holdById(db: Queryable, id: number): Promise<HoldRow | null> {
  const r = await db.query<RawHold>(`SELECT ${HOLD_COLS} FROM economy_holds WHERE id = $1`, [id]);
  return r.rows[0] ? toHold(r.rows[0]) : null;
}

export async function lockHoldByUuid(db: Queryable, uuid: string): Promise<HoldRow | null> {
  const r = await db.query<RawHold>(`SELECT ${HOLD_COLS} FROM economy_holds WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? toHold(r.rows[0]) : null;
}

/** 해제: shadow·active·clawed_back 모두 풀 수 있다. 이미 해제면 false(멱등) */
export async function release(db: Queryable, id: number, by: string, note: string, now: Date): Promise<boolean> {
  const r = await db.query(
    `UPDATE economy_holds SET state = 'released', released_at = $3, reviewed_by = $2, reviewed_at = $3, note = $4
      WHERE id = $1 AND state <> 'released'`,
    [id, by, now, note],
  );
  return (r.rowCount ?? 0) > 0;
}

/** 연결 정지(origin_hold_id = id) 중 풀 수 있는 것의 id */
export async function linkedOf(db: Queryable, originId: number): Promise<number[]> {
  const r = await db.query<{ id: string }>("SELECT id FROM economy_holds WHERE origin_hold_id = $1 AND state <> 'released' ORDER BY id", [originId]);
  return r.rows.map((x) => Number(x.id));
}

// ---------- 전파(12.7) ----------

/** 이 계정이 since 이후 쓴 기기와, 각 기기를 쓴 서로 다른 계정 수 */
export async function devicesWithCounts(db: Queryable, accountId: number, since: Date): Promise<{ device_hash: string; accounts: number }[]> {
  const r = await db.query<{ device_hash: string; n: string }>(
    `SELECT d.device_hash, (SELECT count(DISTINCT x.account_id) FROM account_devices x WHERE x.device_hash = d.device_hash AND x.last_seen_at > $2) AS n
       FROM account_devices d WHERE d.account_id = $1 AND d.last_seen_at > $2`,
    [accountId, since],
  );
  return r.rows.map((x) => ({ device_hash: x.device_hash, accounts: Number(x.n) }));
}

export async function accountsOnDevice(db: Queryable, deviceHash: string, since: Date): Promise<number[]> {
  const r = await db.query<{ account_id: string }>('SELECT DISTINCT account_id FROM account_devices WHERE device_hash = $1 AND last_seen_at > $2', [deviceHash, since]);
  return r.rows.map((x) => Number(x.account_id));
}

export async function steamKeyOf(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ k: string }>("SELECT COALESCE(steam_owner_id, subject) AS k FROM auth_identities WHERE account_id = $1 AND provider = 'steam'", [accountId]);
  return r.rows[0]?.k ?? null;
}

export async function accountsOfSteamKey(db: Queryable, key: string): Promise<number[]> {
  const r = await db.query<{ account_id: string }>("SELECT account_id FROM auth_identities WHERE provider = 'steam' AND COALESCE(steam_owner_id, subject) = $1", [key]);
  return r.rows.map((x) => Number(x.account_id));
}

/** 최근 7일 이 계정과 체결한 상대 중 의심 플래그가 걸린 거래의 상대 계정 */
export async function flaggedPartners(db: Queryable, accountId: number, since: Date): Promise<number[]> {
  const r = await db.query<{ other: string }>(
    `SELECT DISTINCT CASE WHEN t.seller_account_id = $1 THEN t.buyer_account_id ELSE t.seller_account_id END AS other
       FROM auction_trades t JOIN auction_trade_flags f ON f.trade_id = t.id
      WHERE (t.seller_account_id = $1 OR t.buyer_account_id = $1) AND t.traded_at > $2
        AND f.flag IN ('SAME_DEVICE', 'SAME_STEAM', 'NEW_BUYER', 'CEILING_PRICE')`,
    [accountId, since],
  );
  return r.rows.map((x) => Number(x.other));
}

/** 캐릭터 정보(평가용) */
export async function characterBrief(db: Queryable, characterId: number): Promise<{ account_id: number; level: number } | null> {
  const r = await db.query<{ account_id: string; level: number }>('SELECT account_id, level FROM characters WHERE id = $1 AND deleted_at IS NULL', [characterId]);
  return r.rows[0] ? { account_id: Number(r.rows[0].account_id), level: r.rows[0].level } : null;
}

export async function charactersOfAccount(db: Queryable, accountId: number): Promise<{ id: number; level: number }[]> {
  const r = await db.query<{ id: string; level: number }>('SELECT id, level FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY id', [accountId]);
  return r.rows.map((x) => ({ id: Number(x.id), level: x.level }));
}

export async function ownedCharacter(db: Queryable, accountId: number, uuid: string): Promise<{ id: number } | null> {
  const r = await db.query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL', [uuid, accountId]);
  return r.rows[0] ? { id: Number(r.rows[0].id) } : null;
}
