import type { PoolClient } from 'pg';
import type { CareerState } from './careerRules';
import { getPool, query, type Queryable } from '../../db/pool';

export interface CharacterRow {
  id: number;
  uuid: string;
  account_id: number;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
  xp: number;
  gold: number;
  created_at: Date;
  deleted_at: Date | null;
}

export interface QuestSave {
  id: string;
  status: number;
  step: number;
  counts: number[];
}
export interface GemSave {
  active?: string | null;
  slot: number;
  supports: (string | null)[];
}

export interface StateRow {
  career?: CareerState | null;
  map_id: string;
  pos_x: number | null;
  pos_y: number | null;
  facing: number;
  quests: QuestSave[];
  story_flags: string[];
  tracked_quest: string;
  passives: string[];
  skill_gems: GemSave[];
  version: number;
  updated_at: Date;
}

export interface ItemRow {
  uuid: string;
  item_key: string;
  count: number;
  location: string;
  slot: number | null;
}

interface RawChar extends Omit<CharacterRow, 'id' | 'account_id' | 'gold'> {
  id: string;
  account_id: string;
  gold: string;
}
const toChar = (r: RawChar): CharacterRow => ({
  ...r,
  id: Number(r.id),
  account_id: Number(r.account_id),
  gold: Number(r.gold),
});

const CHAR_COLS = 'id, uuid, account_id, name, class, level, xp, gold, created_at, deleted_at';

export async function lockAccount(client: PoolClient, accountId: number): Promise<void> {
  await client.query('SELECT id FROM accounts WHERE id = $1 FOR UPDATE', [accountId]);
}

export async function countAlive(client: PoolClient, accountId: number): Promise<number> {
  const r = await client.query<{ n: string }>(
    'SELECT count(*) AS n FROM characters WHERE account_id = $1 AND deleted_at IS NULL',
    [accountId],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function insertCharacter(
  client: PoolClient,
  accountId: number,
  name: string,
  cls: string,
): Promise<CharacterRow> {
  const r = await client.query<RawChar>(
    `INSERT INTO characters (account_id, name, class) VALUES ($1, $2, $3) RETURNING ${CHAR_COLS}`,
    [accountId, name, cls],
  );
  return toChar(r.rows[0] as RawChar);
}

export async function insertInitialState(
  client: PoolClient,
  characterId: number,
  mapId: string,
): Promise<void> {
  await client.query(
    `INSERT INTO character_state (character_id, map_id, pos_x, pos_y, facing, version)
     VALUES ($1, $2, NULL, NULL, 0, 0)`,
    [characterId, mapId],
  );
}

/** 골드 변경은 행 잠금 후 원장과 함께만 */
export async function lockCharacterGold(client: PoolClient, characterId: number): Promise<number> {
  const r = await client.query<{ gold: string }>(
    'SELECT gold FROM characters WHERE id = $1 FOR UPDATE',
    [characterId],
  );
  return Number((r.rows[0] as { gold: string }).gold);
}

export async function updateGold(
  client: PoolClient,
  characterId: number,
  newBalance: number,
): Promise<void> {
  await client.query('UPDATE characters SET gold = $2 WHERE id = $1', [characterId, newBalance]);
}

export async function insertGoldLedger(
  client: PoolClient,
  characterId: number,
  delta: number,
  balanceAfter: number,
  reason: string,
  ref: string,
): Promise<void> {
  await client.query(
    `INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref)
     VALUES ($1, $2, $3, $4, $5)`,
    [characterId, delta, balanceAfter, reason, ref],
  );
}

export async function insertItem(
  client: PoolClient,
  characterId: number,
  itemKey: string,
  count: number,
  location: 'bag' | 'worn',
  slot: number | null,
  bind: 'none' | 'account' | 'character',
): Promise<void> {
  await client.query(
    `INSERT INTO character_items (character_id, item_key, count, location, slot, bind)
     VALUES ($1, $2, $3, $4, $5, $6)`,
    [characterId, itemKey, count, location, slot, bind],
  );
}

export async function insertItemLedger(
  client: PoolClient,
  characterId: number,
  itemKey: string,
  delta: number,
  reason: string,
  ref: string,
  location: 'bag' | 'worn',
  balanceAfter: number,
): Promise<void> {
  // 아이템 원장은 (캐릭터, 위치, 키)별 합이 character_items.count와 같다(0002). 시작 장비는 worn.
  await client.query(
    `INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after)
     VALUES ($1, $2, $3, $4, $5, $6, $7)`,
    [characterId, itemKey, delta, reason, ref, location, balanceAfter],
  );
}

/** 소유자와 살아 있음을 함께 확인한다. 남의 것·삭제됨·없음은 모두 null */
export async function findOwnedAlive(
  db: Queryable,
  accountId: number,
  uuid: string,
): Promise<CharacterRow | null> {
  const r = await db.query<RawChar>(
    `SELECT ${CHAR_COLS} FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL`,
    [uuid, accountId],
  );
  const row = r.rows[0];
  return row ? toChar(row) : null;
}

export async function softDelete(
  accountId: number,
  uuid: string,
  db: Queryable = getPool(),
): Promise<'deleted' | 'already' | 'missing'> {
  const upd = await db.query(
    `UPDATE characters SET deleted_at = now()
      WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL`,
    [uuid, accountId],
  );
  if ((upd.rowCount ?? 0) > 0) return 'deleted';
  const ex = await db.query('SELECT 1 FROM characters WHERE uuid = $1 AND account_id = $2', [
    uuid,
    accountId,
  ]);
  return (ex.rowCount ?? 0) > 0 ? 'already' : 'missing';
}

export async function listAlive(
  accountId: number,
): Promise<(CharacterRow & { map_id: string; pos_x: number | null; pos_y: number | null; updated_at: Date })[]> {
  const r = await query<
    RawChar & { map_id: string; pos_x: number | null; pos_y: number | null; updated_at: Date }
  >(
    `SELECT c.id, c.uuid, c.account_id, c.name, c.class, c.level, c.xp, c.gold, c.created_at, c.deleted_at,
            s.map_id, s.pos_x, s.pos_y, s.updated_at
       FROM characters c JOIN character_state s ON s.character_id = c.id
      WHERE c.account_id = $1 AND c.deleted_at IS NULL
      ORDER BY c.created_at, c.id`,
    [accountId],
  );
  return r.rows.map((row) => ({ ...toChar(row), map_id: row.map_id, pos_x: row.pos_x, pos_y: row.pos_y, updated_at: row.updated_at }));
}

export async function getState(db: Queryable, characterId: number): Promise<StateRow> {
  const r = await db.query<StateRow>(
    `SELECT map_id, pos_x, pos_y, facing, quests, story_flags, tracked_quest, passives,
            skill_gems, career, version, updated_at
       FROM character_state WHERE character_id = $1`,
    [characterId],
  );
  return r.rows[0] as StateRow;
}

export async function getStateForUpdate(client: PoolClient, characterId: number): Promise<StateRow> {
  const r = await client.query<StateRow>(
    `SELECT map_id, pos_x, pos_y, facing, quests, story_flags, tracked_quest, passives,
            skill_gems, career, version, updated_at
       FROM character_state WHERE character_id = $1 FOR UPDATE`,
    [characterId],
  );
  return r.rows[0] as StateRow;
}

export async function getItems(db: Queryable, characterId: number): Promise<ItemRow[]> {
  const r = await db.query<ItemRow>(
    `SELECT uuid, item_key, count, location, slot FROM character_items
      WHERE character_id = $1 ORDER BY location, slot NULLS LAST, id`,
    [characterId],
  );
  return r.rows;
}

export interface NewState {
  mapId: string;
  posX: number | null;
  posY: number | null;
  facing: number;
  quests: QuestSave[];
  storyFlags: string[];
  trackedQuest: string;
  passives: string[];
  skillGems: GemSave[];
  career?: CareerState | null;
}

/** 낙관적 잠금: version이 맞을 때만 갱신. 0행이면 null */
export async function updateStateIfVersion(
  client: PoolClient,
  characterId: number,
  expected: number,
  s: NewState,
): Promise<{ version: number; updated_at: Date } | null> {
  const r = await client.query<{ version: number; updated_at: Date }>(
    `UPDATE character_state
        SET map_id = $3, pos_x = $4, pos_y = $5, facing = $6, quests = $7::jsonb,
            story_flags = $8, tracked_quest = $9, passives = $10, skill_gems = $11::jsonb, career = $12::jsonb,
            version = version + 1, updated_at = now()
      WHERE character_id = $1 AND version = $2
      RETURNING version, updated_at`,
    [
      characterId,
      expected,
      s.mapId,
      s.posX,
      s.posY,
      s.facing,
      JSON.stringify(s.quests),
      s.storyFlags,
      s.trackedQuest,
      s.passives,
      JSON.stringify(s.skillGems),
      JSON.stringify(s.career ?? null),
    ],
  );
  return r.rows[0] ?? null;
}
