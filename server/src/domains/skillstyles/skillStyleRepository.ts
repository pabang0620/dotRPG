import type { Queryable } from '../../db/pool';

export async function style3Owned(db: Queryable, accountId: number): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM skill_style_unlocks WHERE account_id = $1', [accountId]);
  return (r.rowCount ?? 0) > 0;
}

export async function unlockByRequest(db: Queryable, accountId: number, requestId: string): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM skill_style_unlocks WHERE account_id = $1 AND request_id = $2', [accountId, requestId]);
  return (r.rowCount ?? 0) > 0;
}

export async function insertUnlock(db: Queryable, o: { accountId: number; stars: number; requestId: string }): Promise<void> {
  await db.query('INSERT INTO skill_style_unlocks (account_id, stars, request_id) VALUES ($1, $2, $3)', [o.accountId, o.stars, o.requestId]);
}

export async function ledgerBalanceAfter(db: Queryable, accountId: number, requestId: string): Promise<number | null> {
  const r = await db.query<{ balance_after: string }>(
    "SELECT balance_after FROM star_ledger WHERE account_id = $1 AND reason = 'skill_style_buy' AND request_id = $2",
    [accountId, requestId],
  );
  return r.rows[0] ? Number(r.rows[0].balance_after) : null;
}

export async function topCharacterId(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ id: string }>(
    'SELECT id FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY level DESC, id LIMIT 1',
    [accountId],
  );
  return r.rows[0] ? Number(r.rows[0].id) : 0;
}
