// SQL만. 호출 쪽이 트랜잭션을 연다.
import type { Queryable } from '../../db/pool';

export interface BoosterState {
  gauge: number;
  totalOpens: number;
}

/** 부스터 행을 만들고(없으면) 잠근다. 락 순서: 캐릭터 -> 계정 -> 별조각 지갑 -> 이 행 */
export async function lockState(db: Queryable, accountId: number): Promise<BoosterState> {
  await db.query('INSERT INTO account_sealed_state (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
  const r = await db.query<{ gauge: number; total_opens: string }>('SELECT gauge, total_opens FROM account_sealed_state WHERE account_id = $1 FOR UPDATE', [accountId]);
  const row = r.rows[0];
  return { gauge: row?.gauge ?? 0, totalOpens: Number(row?.total_opens ?? 0) };
}

export async function readState(db: Queryable, accountId: number): Promise<BoosterState> {
  const r = await db.query<{ gauge: number; total_opens: string }>('SELECT gauge, total_opens FROM account_sealed_state WHERE account_id = $1', [accountId]);
  const row = r.rows[0];
  return { gauge: row?.gauge ?? 0, totalOpens: Number(row?.total_opens ?? 0) };
}

export async function saveState(db: Queryable, accountId: number, s: BoosterState): Promise<void> {
  await db.query('UPDATE account_sealed_state SET gauge = $2, total_opens = $3, updated_at = now() WHERE account_id = $1', [accountId, s.gauge, s.totalOpens]);
}

export interface PullLogRow {
  seq: number;
  source: 'shop' | 'sealed_item' | 'luck_box';
  rowId: string;
  itemKey: string;
  count: number;
  tier: 'common' | 'rare';
  boosted: boolean;
  rate: number;
  gaugeBefore: number | null;
  gaugeAfter: number | null;
}

export async function insertPulls(db: Queryable, o: { accountId: number; characterId: number; requestId: string; version: string; rows: PullLogRow[] }): Promise<void> {
  for (const p of o.rows) {
    await db.query(
      `INSERT INTO sealed_pulls (account_id, character_id, request_id, seq, source, rates_version, row_id, item_key, count, tier, boosted, rate, gauge_before, gauge_after)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)`,
      [o.accountId, o.characterId, o.requestId, p.seq, p.source, o.version, p.rowId, p.itemKey, p.count, p.tier, p.boosted, p.rate, p.gaugeBefore, p.gaugeAfter],
    );
  }
}
