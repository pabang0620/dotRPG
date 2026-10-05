import type { Queryable } from '../../db/pool';

export interface Wallet {
  balance: number;
  pity: number;
}

/** 지갑 행을 만들고(없으면) 잠근다. 같은 계정의 다른 캐릭터 요청도 여기서 줄을 선다 */
export async function lockWallet(db: Queryable, accountId: number): Promise<Wallet> {
  await db.query('INSERT INTO star_wallets (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
  const r = await db.query<{ balance: string; pity: number }>(
    'SELECT balance, pity FROM star_wallets WHERE account_id = $1 FOR UPDATE',
    [accountId],
  );
  const row = r.rows[0];
  return { balance: Number(row?.balance ?? 0), pity: row?.pity ?? 0 };
}

export async function readWallet(db: Queryable, accountId: number): Promise<Wallet> {
  const r = await db.query<{ balance: string; pity: number }>('SELECT balance, pity FROM star_wallets WHERE account_id = $1', [accountId]);
  const row = r.rows[0];
  return { balance: Number(row?.balance ?? 0), pity: row?.pity ?? 0 };
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

export async function setPity(db: Queryable, accountId: number, pity: number): Promise<void> {
  await db.query('UPDATE star_wallets SET pity = $2, updated_at = now() WHERE account_id = $1', [accountId, pity]);
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
