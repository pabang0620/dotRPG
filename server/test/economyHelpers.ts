import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import type { Rng } from '../src/utils/rng';
import { auth, createChar, randomName, registerAccount, type Session } from './helpers';

export interface Hero {
  s: Session;
  id: string; // 캐릭터 uuid
  dbId: number;
  cls: 'warrior' | 'mage';
}

export async function newHero(app: Express, cls: 'warrior' | 'mage' = 'warrior'): Promise<Hero> {
  const s = await registerAccount(app);
  const res = await createChar(app, s, randomName(), cls);
  if (res.status !== 201) throw new Error(`createChar failed ${res.status}`);
  const id = res.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { s, id, dbId: Number((r.rows[0] as { id: string }).id), cls };
}

export const post = (app: Express, h: Hero, path: string, body: Record<string, unknown>, requestId?: string) =>
  request(app)
    .post(`/characters/${h.id}${path}`)
    .set(auth(h.s))
    .send({ request_id: requestId ?? randomUUID(), ...body });

export const get = (app: Express, h: Hero, path: string) =>
  request(app).get(`/characters/${h.id}${path}`).set(auth(h.s));

// ---------- 시드(테스트 준비): 원장에도 같이 남겨 "원장 합 = 잔액" 검증을 유지한다 ----------

export async function seedItem(
  h: Hero,
  key: string,
  count: number,
  location: 'bag' | 'storage' = 'bag',
  bind: 'none' | 'account' | 'character' = 'none',
): Promise<void> {
  const db = getPool();
  await db.query(
    `INSERT INTO character_items (character_id, item_key, count, location, bind) VALUES ($1, $2, $3, $4, $5)
     ON CONFLICT (character_id, location, item_key, bind) WHERE location IN ('bag', 'storage')
     DO UPDATE SET count = character_items.count + EXCLUDED.count`,
    [h.dbId, key, count, location, bind],
  );
  const total = await db.query<{ n: string }>(
    'SELECT sum(count) AS n FROM character_items WHERE character_id = $1 AND item_key = $2 AND location = $3',
    [h.dbId, key, location],
  );
  await db.query(
    `INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after)
     VALUES ($1, $2, $3, 'drop_claim', $4, $5, $6)`,
    [h.dbId, key, count, randomUUID(), location, Number((total.rows[0] as { n: string }).n)],
  );
}

export async function seedWorn(h: Hero, slot: number, key: string): Promise<void> {
  const db = getPool();
  const old = await db.query<{ item_key: string }>(
    "DELETE FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2 RETURNING item_key",
    [h.dbId, slot],
  );
  for (const o of old.rows) {
    await db.query(
      `INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after)
       VALUES ($1, $2, -1, 'drop_claim', $3, 'worn', 0)`,
      [h.dbId, o.item_key, randomUUID()],
    );
  }
  await db.query(
    "INSERT INTO character_items (character_id, item_key, count, location, slot) VALUES ($1, $2, 1, 'worn', $3)",
    [h.dbId, key, slot],
  );
  await db.query(
    `INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after)
     VALUES ($1, $2, 1, 'drop_claim', $3, 'worn', 1)`,
    [h.dbId, key, randomUUID()],
  );
}

export async function seedGold(h: Hero, amount: number): Promise<void> {
  const db = getPool();
  const r = await db.query<{ gold: string }>('UPDATE characters SET gold = gold + $2 WHERE id = $1 RETURNING gold', [
    h.dbId,
    amount,
  ]);
  await db.query(
    `INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref) VALUES ($1, $2, $3, 'drop_claim', $4)`,
    [h.dbId, amount, Number((r.rows[0] as { gold: string }).gold), randomUUID()],
  );
}

export async function seedLevel(h: Hero, level: number): Promise<void> {
  await getPool().query('UPDATE characters SET level = $2, xp = 0 WHERE id = $1', [h.dbId, level]);
}

export async function seedClaims(h: Hero, questIds: string[]): Promise<void> {
  for (const q of questIds) {
    await getPool().query("INSERT INTO quest_claims (character_id, quest_id, reward) VALUES ($1, $2, '{}'::jsonb)", [
      h.dbId,
      q,
    ]);
  }
}

// ---------- 조회 ----------

export async function goldOf(h: Hero): Promise<number> {
  const r = await getPool().query<{ gold: string }>('SELECT gold FROM characters WHERE id = $1', [h.dbId]);
  return Number((r.rows[0] as { gold: string }).gold);
}

export async function countOf(h: Hero, key: string, location = 'bag'): Promise<number> {
  const r = await getPool().query<{ count: number }>(
    'SELECT coalesce(sum(count), 0)::int AS count FROM character_items WHERE character_id = $1 AND item_key = $2 AND location = $3',
    [h.dbId, key, location],
  );
  return r.rows[0]?.count ?? 0;
}

export async function wornOf(h: Hero, slot: number): Promise<string | null> {
  const r = await getPool().query<{ item_key: string }>(
    "SELECT item_key FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2",
    [h.dbId, slot],
  );
  return r.rows[0]?.item_key ?? null;
}

export async function anomalyKinds(h: Hero): Promise<string[]> {
  const r = await getPool().query<{ kind: string }>('SELECT kind FROM anomaly_log WHERE character_id = $1 ORDER BY id', [
    h.dbId,
  ]);
  return r.rows.map((x) => x.kind);
}

/** 검증 쿼리(mapping 7절): 재고 = 원장 합, 골드 = 원장 합이고 마지막 balance_after와 같다 */
export async function expectLedgerConsistent(h: Hero): Promise<void> {
  const db = getPool();
  const items = await db.query<{ item_key: string; location: string; led: string; cnt: string }>(
    `SELECT k.item_key, k.location, coalesce(l.s, 0) AS led, coalesce(c.count, 0) AS cnt
       FROM (SELECT item_key, location FROM item_ledger WHERE character_id = $1 AND location IN ('bag', 'storage', 'worn')
             UNION SELECT item_key, location FROM character_items WHERE character_id = $1) k
       LEFT JOIN (SELECT item_key, location, sum(delta) AS s FROM item_ledger WHERE character_id = $1
                  GROUP BY item_key, location) l USING (item_key, location)
       LEFT JOIN (SELECT item_key, location, sum(count) AS count FROM character_items WHERE character_id = $1
                  GROUP BY item_key, location) c USING (item_key, location)`,
    [h.dbId],
  );
  for (const r of items.rows) {
    if (Number(r.led) !== Number(r.cnt)) {
      throw new Error(`아이템 원장 불일치 ${r.location}/${r.item_key}: 원장 ${r.led} 재고 ${r.cnt}`);
    }
  }
  const g = await db.query<{ gold: string; led: string; last: string | null }>(
    `SELECT c.gold, coalesce((SELECT sum(delta) FROM gold_ledger WHERE character_id = c.id), 0) AS led,
            (SELECT balance_after FROM gold_ledger WHERE character_id = c.id ORDER BY id DESC LIMIT 1) AS last
       FROM characters c WHERE c.id = $1`,
    [h.dbId],
  );
  const row = g.rows[0] as { gold: string; led: string; last: string | null };
  if (Number(row.gold) !== Number(row.led)) throw new Error(`골드 원장 불일치: 잔액 ${row.gold} 원장 ${row.led}`);
  if (row.last !== null && Number(row.last) !== Number(row.gold)) throw new Error('마지막 balance_after 불일치');
}

/** 난수 주입: unit은 [0,1] 값 목록(소진 후 마지막 값 반복), int는 함수 */
export function fakeRng(opts: { unit?: number | number[]; int?: (min: number, max: number) => number }): Rng {
  const units = Array.isArray(opts.unit) ? [...opts.unit] : null;
  const fixed = typeof opts.unit === 'number' ? opts.unit : 1;
  return {
    unit: () => (units ? (units.length > 1 ? (units.shift() as number) : (units[0] as number)) : fixed),
    int: opts.int ?? ((min) => min),
  };
}

/** 새 캐릭터가 받는 시작 지급 수량(starter.json). 시작 수량에 기대는 검사는 숫자 대신 이것을 더한다 */
export function starterCount(key: string): number {
  return getGameData().starter.items.find((i) => i.itemKey === key)?.count ?? 0;
}
