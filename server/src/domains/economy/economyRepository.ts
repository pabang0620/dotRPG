// 3단계 공통 SQL. 재화 변경은 호출하는 Service가 characters 행 잠금(lockCharacter) 아래에서만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface LockedChar {
  id: number;
  uuid: string;
  accountId: number;
  class: 'warrior' | 'mage';
  level: number;
  xp: number;
  gold: number;
}

export type StackLocation = 'bag' | 'storage';
export type Bind = 'none' | 'account' | 'character';

interface RawLocked {
  id: string;
  uuid: string;
  account_id: string;
  class: 'warrior' | 'mage';
  level: number;
  xp: number;
  gold: string;
}

/** 3단계 모든 경제 요청의 첫 문장: 캐릭터 행을 잠근다. 남의 것·삭제됨·없음은 null */
export async function lockCharacter(
  client: PoolClient,
  accountId: number,
  uuid: string,
): Promise<LockedChar | null> {
  const r = await client.query<RawLocked>(
    `SELECT id, uuid, account_id, class, level, xp, gold FROM characters
      WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL FOR UPDATE`,
    [uuid, accountId],
  );
  const row = r.rows[0];
  return row ? toLocked(row) : null;
}

const toLocked = (row: RawLocked): LockedChar => ({
  id: Number(row.id),
  uuid: row.uuid,
  accountId: Number(row.account_id),
  class: row.class,
  level: row.level,
  xp: row.xp,
  gold: Number(row.gold),
});

/** 4단계: 여러 캐릭터를 id 오름차순으로 한 번에 잠근다(교착 방지의 유일한 순서). 삭제된 캐릭터는 빠진다 */
export async function lockCharacters(client: PoolClient, ids: number[]): Promise<LockedChar[]> {
  const r = await client.query<RawLocked>(
    `SELECT id, uuid, account_id, class, level, xp, gold FROM characters
      WHERE id = ANY($1::bigint[]) AND deleted_at IS NULL ORDER BY id FOR UPDATE`,
    [ids],
  );
  return r.rows.map(toLocked);
}

/** 귀속 강도(강할수록 큼). 소모는 강한 쪽부터, 귀속 하한은 약한 쪽으로 풀리지 않는다 */
const BIND_RANK: Record<Bind, number> = { none: 0, account: 1, character: 2 };
export const strongerBind = (a: Bind, b: Bind): Bind => (BIND_RANK[a] >= BIND_RANK[b] ? a : b);

/** 한 키의 모든 귀속 행 수량 합(6단계: 재고 키가 (키, 귀속)이라 합산해서 쓴다). bind를 주면 그 귀속만 */
export async function stackCount(
  client: Queryable,
  characterId: number,
  location: StackLocation,
  itemKey: string,
  bind?: Bind,
): Promise<number> {
  const r = await client.query<{ n: string }>(
    `SELECT coalesce(sum(count), 0) AS n FROM character_items
      WHERE character_id = $1 AND location = $2 AND item_key = $3 AND ($4::text IS NULL OR bind = $4)`,
    [characterId, location, itemKey, bind ?? null],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function upsertStack(
  client: PoolClient,
  characterId: number,
  location: StackLocation,
  itemKey: string,
  add: number,
  bind: Bind,
): Promise<number> {
  const r = await client.query<{ count: number }>(
    `INSERT INTO character_items (character_id, item_key, count, location, bind)
     VALUES ($1, $3, $4, $2, $5)
     ON CONFLICT (character_id, location, item_key, bind) WHERE location IN ('bag', 'storage')
     DO UPDATE SET count = character_items.count + EXCLUDED.count, version = character_items.version + 1
     RETURNING count`,
    [characterId, location, itemKey, add, bind],
  );
  return (r.rows[0] as { count: number }).count;
}

export interface Consumed {
  bind: Bind;
  n: number;
  /** 그 귀속 행에 남은 수량(0이면 행이 지워졌다) */
  left: number;
}

/**
 * 수량 n을 뺀다. 귀속이 강한 행(character -> account -> none)부터 소모하고 0이 된 행은 지운다.
 * 모자라면 null(아무것도 바꾸지 않는다). 소모한 귀속별 수량을 돌려준다.
 */
export async function decStack(
  client: PoolClient,
  characterId: number,
  location: StackLocation,
  itemKey: string,
  n: number,
  onlyBind?: Bind,
): Promise<Consumed[] | null> {
  const r = await client.query<{ id: string; bind: Bind; count: number }>(
    `SELECT id, bind, count FROM character_items
      WHERE character_id = $1 AND location = $2 AND item_key = $3 AND ($4::text IS NULL OR bind = $4)
      ORDER BY CASE bind WHEN 'character' THEN 0 WHEN 'account' THEN 1 ELSE 2 END
      FOR UPDATE`,
    [characterId, location, itemKey, onlyBind ?? null],
  );
  const have = r.rows.reduce((a, x) => a + x.count, 0);
  if (have < n) return null;
  const out: Consumed[] = [];
  let need = n;
  for (const row of r.rows) {
    if (need === 0) break;
    const take = Math.min(need, row.count);
    if (take === row.count) {
      await client.query('DELETE FROM character_items WHERE id = $1', [row.id]);
    } else {
      await client.query('UPDATE character_items SET count = count - $2, version = version + 1 WHERE id = $1', [
        row.id,
        take,
      ]);
    }
    out.push({ bind: row.bind, n: take, left: row.count - take });
    need -= take;
  }
  return out;
}

export async function getWornBind(client: Queryable, characterId: number, slot: number): Promise<Bind | null> {
  const r = await client.query<{ bind: Bind }>(
    "SELECT bind FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2",
    [characterId, slot],
  );
  return r.rows[0]?.bind ?? null;
}

export async function getWornKey(client: Queryable, characterId: number, slot: number): Promise<string | null> {
  const r = await client.query<{ item_key: string }>(
    "SELECT item_key FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2",
    [characterId, slot],
  );
  return r.rows[0]?.item_key ?? null;
}

export async function listWornKeys(client: Queryable, characterId: number): Promise<string[]> {
  const r = await client.query<{ item_key: string }>(
    "SELECT item_key FROM character_items WHERE character_id = $1 AND location = 'worn'",
    [characterId],
  );
  return r.rows.map((x) => x.item_key);
}

export async function insertWorn(
  client: PoolClient,
  characterId: number,
  slot: number,
  itemKey: string,
  bind: Bind,
): Promise<void> {
  await client.query(
    `INSERT INTO character_items (character_id, item_key, count, location, slot, bind)
     VALUES ($1, $2, 1, 'worn', $3, $4)`,
    [characterId, itemKey, slot, bind],
  );
}

/** 착용 행을 지우고 그 귀속을 돌려준다(없으면 none) */
export async function deleteWorn(client: PoolClient, characterId: number, slot: number): Promise<Bind> {
  const r = await client.query<{ bind: Bind }>(
    "DELETE FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2 RETURNING bind",
    [characterId, slot],
  );
  return r.rows[0]?.bind ?? 'none';
}

export async function updateWornKey(
  client: PoolClient,
  characterId: number,
  slot: number,
  itemKey: string,
): Promise<void> {
  await client.query(
    `UPDATE character_items SET item_key = $3, version = version + 1
      WHERE character_id = $1 AND location = 'worn' AND slot = $2`,
    [characterId, slot, itemKey],
  );
}

export async function updateGold(client: PoolClient, characterId: number, balance: number): Promise<void> {
  await client.query('UPDATE characters SET gold = $2 WHERE id = $1', [characterId, balance]);
}

export async function updateLevelXp(
  client: PoolClient,
  characterId: number,
  level: number,
  xp: number,
): Promise<void> {
  await client.query('UPDATE characters SET level = $2, xp = $3 WHERE id = $1', [characterId, level, xp]);
}

export async function insertGoldLedger(
  client: PoolClient,
  characterId: number,
  delta: number,
  balanceAfter: number,
  reason: string,
  ref: string,
  requestId: string,
): Promise<void> {
  await client.query(
    `INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref, request_id)
     VALUES ($1, $2, $3, $4, $5, $6)`,
    [characterId, delta, balanceAfter, reason, ref, requestId],
  );
}

export async function insertItemLedger(
  client: PoolClient,
  characterId: number,
  itemKey: string,
  delta: number,
  balanceAfter: number,
  location: string,
  reason: string,
  ref: string,
  requestId: string | null,
): Promise<void> {
  await client.query(
    `INSERT INTO item_ledger (character_id, item_key, delta, balance_after, location, reason, ref, request_id)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
    [characterId, itemKey, delta, balanceAfter, location, reason, ref, requestId],
  );
}

export async function insertXpLedger(
  client: PoolClient,
  characterId: number,
  delta: number,
  levelAfter: number,
  xpAfter: number,
  reason: string,
  ref: string,
  requestId: string,
): Promise<void> {
  await client.query(
    `INSERT INTO xp_ledger (character_id, delta, level_after, xp_after, reason, ref, request_id)
     VALUES ($1, $2, $3, $4, $5, $6, $7)`,
    [characterId, delta, levelAfter, xpAfter, reason, ref, requestId],
  );
}

// ---------- 이상 기록 ----------

export type AnomalyKind =
  | 'kill_target'
  | 'kill_rate'
  | 'kill_supply'
  | 'kill_power'
  | 'gather_node'
  | 'gather_early'
  | 'gather_rate'
  | 'drop_foreign'
  | 'quest_denied'
  | 'chest_unknown'
  | 'dungeon_enter'
  | 'dungeon_result'
  | 'party_result'
  | 'party_host'
  | 'raid_enter'
  | 'field_uncredited'
  | 'field_host'
  | 'relay_abuse';

/** 롤백되는 트랜잭션 밖에서도 남기려고 풀에서 직접 쓴다(호출 쪽이 선택) */
export async function insertAnomaly(
  db: Queryable,
  accountId: number,
  characterId: number | null,
  kind: AnomalyKind,
  severity: 1 | 2 | 3,
  detail: Record<string, unknown>,
): Promise<void> {
  await db.query(
    `INSERT INTO anomaly_log (account_id, character_id, kind, severity, detail)
     VALUES ($1, $2, $3, $4, $5::jsonb)`,
    [accountId, characterId, kind, severity, JSON.stringify(detail)],
  );
}

export async function countRecentKillAnomalies(client: Queryable, characterId: number, since: Date): Promise<number> {
  const r = await client.query<{ n: string }>(
    `SELECT count(*) AS n FROM anomaly_log
      WHERE character_id = $1 AND created_at > $2 AND kind LIKE 'kill\\_%' AND severity >= 2`,
    [characterId, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}

// ---------- 캐릭터 상세 확장(GET /characters/:uuid) ----------

export interface EconomyDetailRows {
  bonusMaxHealth: number;
  claimedQuests: string[];
  openedChests: string[];
  pity: { item_key: string; pity: number }[];
  deliveries: { site_id: string; item_key: string; delivered: number }[];
}

export async function readEconomyDetail(db: Queryable, characterId: number): Promise<EconomyDetailRows> {
  const claims = await db.query<{ quest_id: string; max_health: string | null }>(
    `SELECT quest_id, reward->>'max_health' AS max_health FROM quest_claims
      WHERE character_id = $1 ORDER BY claimed_at, quest_id`,
    [characterId],
  );
  const chests = await db.query<{ chest_id: string }>(
    'SELECT chest_id FROM character_chests WHERE character_id = $1 ORDER BY opened_at, chest_id',
    [characterId],
  );
  const pity = await db.query<{ item_key: string; pity: number }>(
    'SELECT item_key, pity FROM character_enhance_pity WHERE character_id = $1 ORDER BY item_key',
    [characterId],
  );
  const del = await db.query<{ site_id: string; item_key: string; delivered: number }>(
    'SELECT site_id, item_key, delivered FROM site_deliveries WHERE character_id = $1',
    [characterId],
  );
  return {
    bonusMaxHealth: claims.rows.reduce((a, r) => a + Number(r.max_health ?? 0), 0),
    claimedQuests: claims.rows.map((r) => r.quest_id),
    openedChests: chests.rows.map((r) => r.chest_id),
    pity: pity.rows,
    deliveries: del.rows,
  };
}
