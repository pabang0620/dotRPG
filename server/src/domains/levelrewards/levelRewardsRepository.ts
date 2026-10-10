// SQL만. 호출 쪽이 트랜잭션을 연다.
import type { Queryable } from '../../db/pool';

export async function lockAccount(db: Queryable, accountId: number): Promise<void> {
  await db.query('SELECT id FROM accounts WHERE id = $1 FOR UPDATE', [accountId]);
}

/** 삭제되지 않은 캐릭터 중 최고 레벨(없으면 0)과 그 캐릭터 */
export async function topCharacter(db: Queryable, accountId: number): Promise<{ id: number; level: number } | null> {
  const r = await db.query<{ id: string; level: number }>(
    'SELECT id, level FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY level DESC, id LIMIT 1',
    [accountId],
  );
  const row = r.rows[0];
  return row ? { id: Number(row.id), level: row.level } : null;
}

export async function claimedLevels(db: Queryable, accountId: number): Promise<number[]> {
  const r = await db.query<{ level: number }>('SELECT level FROM account_level_rewards WHERE account_id = $1', [accountId]);
  return r.rows.map((x) => x.level);
}

export async function byRequest(db: Queryable, accountId: number, requestId: string): Promise<{ level: number; stars: number; characterId: number | null } | null> {
  const r = await db.query<{ level: number; stars: number; character_id: string | null }>(
    'SELECT level, stars, character_id FROM account_level_rewards WHERE account_id = $1 AND request_id = $2',
    [accountId, requestId],
  );
  const row = r.rows[0];
  return row ? { level: row.level, stars: row.stars, characterId: row.character_id === null ? null : Number(row.character_id) } : null;
}

/** 그때 지급 원장의 이후 잔액(재전송 응답용) */
export async function ledgerBalanceAfter(db: Queryable, accountId: number, level: number): Promise<number | null> {
  const r = await db.query<{ balance_after: string }>(
    "SELECT balance_after FROM star_ledger WHERE account_id = $1 AND reason = 'level_reward' AND ref = $2",
    [accountId, `level:${level}`],
  );
  return r.rows[0] ? Number(r.rows[0].balance_after) : null;
}

export async function insertClaim(db: Queryable, o: { accountId: number; level: number; stars: number; requestId: string; characterId: number }): Promise<void> {
  await db.query(
    'INSERT INTO account_level_rewards (account_id, level, stars, request_id, character_id) VALUES ($1, $2, $3, $4, $5)',
    [o.accountId, o.level, o.stars, o.requestId, o.characterId],
  );
}

// ---------------- 성장 패스(14단계) ----------------

export async function passOwned(db: Queryable, accountId: number): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM account_growth_pass WHERE account_id = $1', [accountId]);
  return (r.rowCount ?? 0) > 0;
}

export async function passByRequest(db: Queryable, accountId: number, requestId: string): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM account_growth_pass WHERE account_id = $1 AND request_id = $2', [accountId, requestId]);
  return (r.rowCount ?? 0) > 0;
}

export async function insertPass(db: Queryable, o: { accountId: number; stars: number; requestId: string }): Promise<void> {
  await db.query('INSERT INTO account_growth_pass (account_id, stars, request_id) VALUES ($1, $2, $3)', [o.accountId, o.stars, o.requestId]);
}

/** 패스 구매 원장의 이후 잔액(재전송 응답용) */
export async function passLedgerBalanceAfter(db: Queryable, accountId: number, requestId: string): Promise<number | null> {
  const r = await db.query<{ balance_after: string }>("SELECT balance_after FROM star_ledger WHERE account_id = $1 AND reason = 'pass_buy' AND request_id = $2", [accountId, requestId]);
  return r.rows[0] ? Number(r.rows[0].balance_after) : null;
}

export async function claimedPassLevels(db: Queryable, accountId: number): Promise<number[]> {
  const r = await db.query<{ level: number }>('SELECT level FROM account_pass_claims WHERE account_id = $1', [accountId]);
  return r.rows.map((x) => x.level);
}

export async function insertPassClaim(db: Queryable, o: { accountId: number; level: number; requestId: string; characterId: number }): Promise<void> {
  await db.query('INSERT INTO account_pass_claims (account_id, level, request_id, character_id) VALUES ($1, $2, $3, $4)', [o.accountId, o.level, o.requestId, o.characterId]);
}
