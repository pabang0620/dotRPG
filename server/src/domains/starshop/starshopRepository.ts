import type { Queryable } from '../../db/pool';

export interface Wallet {
  balance: number;
  pity: number;
  skinPity: number;
  /** 등급별 연속 합성 실패 수(천장): 희귀·에픽·유니크로 가는 합성 */
  synthFail: { rare: number; epic: number; unique: number };
}

type WalletRow = { balance: string; pity: number; skin_pity: number; synth_fail_rare: number; synth_fail_epic: number; synth_fail_unique: number };
const WALLET_COLS = 'balance, pity, skin_pity, synth_fail_rare, synth_fail_epic, synth_fail_unique';
function walletOf(row: WalletRow | undefined): Wallet {
  return {
    balance: Number(row?.balance ?? 0),
    pity: row?.pity ?? 0,
    skinPity: row?.skin_pity ?? 0,
    synthFail: { rare: row?.synth_fail_rare ?? 0, epic: row?.synth_fail_epic ?? 0, unique: row?.synth_fail_unique ?? 0 },
  };
}

/** 지갑 행을 만들고(없으면) 잠근다. 같은 계정의 다른 캐릭터 요청도 여기서 줄을 선다 */
export async function lockWallet(db: Queryable, accountId: number): Promise<Wallet> {
  await db.query('INSERT INTO star_wallets (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
  const r = await db.query<WalletRow>(`SELECT ${WALLET_COLS} FROM star_wallets WHERE account_id = $1 FOR UPDATE`, [accountId]);
  return walletOf(r.rows[0]);
}

export async function readWallet(db: Queryable, accountId: number): Promise<Wallet> {
  const r = await db.query<WalletRow>(`SELECT ${WALLET_COLS} FROM star_wallets WHERE account_id = $1`, [accountId]);
  return walletOf(r.rows[0]);
}

/** 잔액을 바꾸고 원장에 남긴다. 잔액 CHECK(>= 0)가 마지막 안전장치다 */
export async function changeBalance(
  db: Queryable,
  accountId: number,
  delta: number,
  reason: string,
  ref: string | null,
  requestId: string | null,
): Promise<number> {
  const r = await db.query<{ balance: string }>(
    'UPDATE star_wallets SET balance = balance + $2, updated_at = now() WHERE account_id = $1 RETURNING balance',
    [accountId, delta],
  );
  const after = Number(r.rows[0]?.balance ?? 0);
  await db.query(
    `INSERT INTO star_ledger (account_id, delta, balance_after, reason, ref, request_id)
     VALUES ($1, $2, $3, $4, $5, $6)`,
    [accountId, delta, after, reason, ref, requestId],
  );
  return after;
}

export async function setPity(db: Queryable, accountId: number, pity: number, skin = false): Promise<void> {
  await db.query(`UPDATE star_wallets SET ${skin ? 'skin_pity' : 'pity'} = $2, updated_at = now() WHERE account_id = $1`, [accountId, pity]);
}

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

export async function setSynthFail(db: Queryable, accountId: number, tier: 'rare' | 'epic' | 'unique', fails: number): Promise<void> {
  await db.query(`UPDATE star_wallets SET synth_fail_${tier} = $2, updated_at = now() WHERE account_id = $1`, [accountId, fails]);
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
