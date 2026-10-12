// 천장 게이지(0033, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A). SQL만, 호출 쪽이 트랜잭션을 연다.
// 락 순서: 이 행은 항상 마지막에 잠근다(캐릭터 -> 계정 -> 별조각 지갑 -> 부스터 행 -> 이 행).
import type { Queryable } from '../../db/pool';

export type CeilingKind = 'sealed' | 'gear';

export interface Ceilings {
  sealed: number;
  gear: number;
}

const COLUMN: Record<CeilingKind, string> = { sealed: 'sealed_gauge', gear: 'gear_gauge' };

export async function readCeilings(db: Queryable, accountId: number): Promise<Ceilings> {
  const r = await db.query<{ sealed_gauge: number; gear_gauge: number }>('SELECT sealed_gauge, gear_gauge FROM account_ceilings WHERE account_id = $1', [accountId]);
  const row = r.rows[0];
  return { sealed: row?.sealed_gauge ?? 0, gear: row?.gear_gauge ?? 0 };
}

/** 행을 만들고(없으면) 잠근다 */
export async function lockCeilings(db: Queryable, accountId: number): Promise<Ceilings> {
  await db.query('INSERT INTO account_ceilings (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
  const r = await db.query<{ sealed_gauge: number; gear_gauge: number }>(
    'SELECT sealed_gauge, gear_gauge FROM account_ceilings WHERE account_id = $1 FOR UPDATE',
    [accountId],
  );
  const row = r.rows[0];
  return { sealed: row?.sealed_gauge ?? 0, gear: row?.gear_gauge ?? 0 };
}

/** 게이지를 n만큼 올리고 새 값을 돌려준다(행이 없으면 만든다) */
export async function addGauge(db: Queryable, accountId: number, kind: CeilingKind, n: number): Promise<number> {
  const col = COLUMN[kind];
  const r = await db.query<{ v: number }>(
    `INSERT INTO account_ceilings (account_id, ${col}) VALUES ($1, $2)
     ON CONFLICT (account_id) DO UPDATE SET ${col} = account_ceilings.${col} + EXCLUDED.${col}, updated_at = now()
     RETURNING ${col} AS v`,
    [accountId, n],
  );
  return r.rows[0]?.v ?? n;
}

export async function setGauge(db: Queryable, accountId: number, kind: CeilingKind, value: number): Promise<void> {
  await db.query(`UPDATE account_ceilings SET ${COLUMN[kind]} = $2, updated_at = now() WHERE account_id = $1`, [accountId, value]);
}

export async function insertClaim(
  db: Queryable,
  o: { accountId: number; characterId: number; requestId: string; kind: CeilingKind; itemKey: string; count: number; before: number; after: number },
): Promise<void> {
  await db.query(
    `INSERT INTO ceiling_claims (account_id, character_id, request_id, kind, item_key, count, gauge_before, gauge_after)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
    [o.accountId, o.characterId, o.requestId, o.kind, o.itemKey, o.count, o.before, o.after],
  );
}
