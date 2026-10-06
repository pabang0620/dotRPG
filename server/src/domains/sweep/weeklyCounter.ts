// 계정 주간 카운터(설계 5.3, 5.4): sweep_buy(구매 장수), direct_clear(직접 클리어), activity_claim(보상 수령 표시).
// week_start는 resetBoundaries().weeklyStartAt(목요일 06:00 KST). 주가 바뀌면 새 행이 생겨 초기화 작업이 없다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import { resetBoundaries } from '../../utils/resetBoundaries';

export type CounterKind = 'sweep_buy' | 'direct_clear' | 'activity_claim';

/** 시각 at이 속한 주의 시작(날짜 경계 함수 한 곳) */
export const weekStartOf = (at: Date): Date => new Date(resetBoundaries(at).weeklyStartAt);

export async function readUsed(db: Queryable, accountId: number, weekStart: Date, kind: CounterKind): Promise<number> {
  const r = await db.query<{ used: number }>(
    'SELECT used FROM account_week_counters WHERE account_id = $1 AND week_start = $2 AND kind = $3',
    [accountId, weekStart, kind],
  );
  return r.rows[0]?.used ?? 0;
}

/** 요일 던전 직접 클리어(보상이 잠기지 않은 판) +1. 그 요청의 마지막 쓰기로 부른다 */
export async function bumpDirectClear(client: PoolClient, accountId: number, at: Date): Promise<void> {
  await client.query(
    `INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES ($1, $2, 'direct_clear', 1)
     ON CONFLICT (account_id, week_start, kind) DO UPDATE SET used = account_week_counters.used + 1, updated_at = now()`,
    [accountId, weekStartOf(at)],
  );
}

/** 구매 장수를 올린다. 한도를 넘으면 false(조건부 UPDATE, 계정 행 잠금 아래의 안전장치) */
export async function addBuy(client: PoolClient, accountId: number, weekStart: Date, count: number, limit: number): Promise<boolean> {
  if (count > limit) return false;
  const r = await client.query(
    `INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES ($1, $2, 'sweep_buy', $3)
     ON CONFLICT (account_id, week_start, kind) DO UPDATE SET used = account_week_counters.used + $3, updated_at = now()
       WHERE account_week_counters.used + $3 <= $4`,
    [accountId, weekStart, count, limit],
  );
  return (r.rowCount ?? 0) === 1;
}

/** 주간 보상 수령 표시. 이미 받았으면 false(PK가 이중 수령을 막는다) */
export async function markClaimed(client: PoolClient, accountId: number, weekStart: Date): Promise<boolean> {
  const r = await client.query(
    `INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES ($1, $2, 'activity_claim', 1) ON CONFLICT DO NOTHING`,
    [accountId, weekStart],
  );
  return (r.rowCount ?? 0) === 1;
}
