import type { Queryable } from '../../db/pool';

// 지갑(star_wallets)·원장(star_ledger) SQL은 starWallet.ts 한 파일에만 둔다(11단계 4절)

export async function ownedOf(db: Queryable, accountId: number): Promise<Set<string>> {
  const r = await db.query<{ item_id: string }>('SELECT item_id FROM account_cosmetics WHERE account_id = $1', [accountId]);
  return new Set(r.rows.map((x) => x.item_id));
}

export async function addCosmetic(db: Queryable, accountId: number, itemId: string, source: string): Promise<void> {
  await db.query(
    'INSERT INTO account_cosmetics (account_id, item_id, source) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING',
    [accountId, itemId, source],
  );
}

export interface PullRow {
  seq: number;
  rarity: string;
  itemId: string;
  pityBefore: number;
  pityAfter: number;
  byPity: boolean;
  duplicate: boolean;
  refund: number;
  kind: 'cosmetic' | 'gear';
}

export async function insertPulls(db: Queryable, accountId: number, requestId: string, version: string, banner: string, rows: PullRow[]): Promise<void> {
  for (const p of rows) {
    await db.query(
      `INSERT INTO gacha_pulls (account_id, request_id, seq, rates_version, rarity, item_id, pity_before, pity_after, by_pity, duplicate, refund, kind, banner)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)`,
      [accountId, requestId, p.seq, version, p.rarity, p.itemId, p.pityBefore, p.pityAfter, p.byPity, p.duplicate, p.refund, p.kind, banner],
    );
  }
}

// ---------------- 여분·합성·컬렉션 ----------------

/** 가진 외형별 여분 수 */
export async function copiesOf(db: Queryable, accountId: number): Promise<Map<string, number>> {
  const r = await db.query<{ item_id: string; copies: number }>('SELECT item_id, copies FROM account_cosmetics WHERE account_id = $1', [accountId]);
  return new Map(r.rows.map((x) => [x.item_id, x.copies] as const));
}

/** 이미 가진 외형에 여분을 더한다(가지지 않았으면 아무것도 하지 않는다) */
export async function addCopies(db: Queryable, accountId: number, itemId: string, n: number): Promise<void> {
  await db.query('UPDATE account_cosmetics SET copies = copies + $3 WHERE account_id = $1 AND item_id = $2', [accountId, itemId, n]);
}

/** 여분을 뺀다. 모자라면 false(CHECK copies >= 0이 마지막 안전장치) */
export async function takeCopies(db: Queryable, accountId: number, itemId: string, n: number): Promise<boolean> {
  const r = await db.query('UPDATE account_cosmetics SET copies = copies - $3 WHERE account_id = $1 AND item_id = $2 AND copies >= $3', [accountId, itemId, n]);
  return (r.rowCount ?? 0) > 0;
}

export interface SynthLogRow {
  seq: number;
  fromRarity: string;
  inputs: string[];
  success: boolean;
  byPity: boolean;
  resultItem: string;
  failsBefore: number;
  failsAfter: number;
}

export async function insertSynthLog(db: Queryable, accountId: number, requestId: string, rows: SynthLogRow[]): Promise<void> {
  for (const s of rows) {
    await db.query(
      `INSERT INTO star_synth_log (account_id, request_id, seq, from_rarity, inputs, success, by_pity, result_item, fails_before, fails_after)
       VALUES ($1, $2, $3, $4, $5::jsonb, $6, $7, $8, $9, $10)`,
      [accountId, requestId, s.seq, s.fromRarity, JSON.stringify(s.inputs), s.success, s.byPity, s.resultItem, s.failsBefore, s.failsAfter],
    );
  }
}

export async function collectionsOf(db: Queryable, accountId: number): Promise<Set<string>> {
  const r = await db.query<{ set_id: string }>('SELECT set_id FROM account_collections WHERE account_id = $1', [accountId]);
  return new Set(r.rows.map((x) => x.set_id));
}

export async function addCollection(db: Queryable, accountId: number, setId: string): Promise<boolean> {
  const r = await db.query('INSERT INTO account_collections (account_id, set_id) VALUES ($1, $2) ON CONFLICT DO NOTHING', [accountId, setId]);
  return (r.rowCount ?? 0) > 0;
}
