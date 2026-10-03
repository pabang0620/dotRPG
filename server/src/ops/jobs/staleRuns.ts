// 방치 상태 정리 `stale-runs`(phase7_ops.md 6.5, D7 sweepStale): 요청 때 지연 처리되던 전이를 일괄로 훑는다.
// 새 규칙을 만들지 않고 기존 함수(abandonRun, cancelRun, closeParty)를 그대로 쓴다.
import { getConfig } from '../../config/env';
import { getPool, withTransaction } from '../../db/pool';
import * as dungeonRepo from '../../domains/dungeons/dungeonRepository';
import * as partyRepo from '../../domains/party/partyRepository';
import * as partyRunRepo from '../../domains/partyruns/partyRunRepository';
import { getNow } from '../../utils/clock';
import type { JobCtx, JobResult } from '../jobRunner';

const LIMIT = 200;
/** 모으는 중 판이 마감을 지나 이만큼 더 지나도록 아무도 처리하지 않으면 취소한다 */
const GATHER_GRACE_MS = 10 * 60_000;

export interface SweepResult {
  abandonedRuns: number;
  cancelledPartyRuns: number;
  closedParties: number;
}

export async function sweepStale(ctx: JobCtx | null = null): Promise<SweepResult> {
  const cfg = getConfig();
  const pol = cfg.policy;
  const now = getNow();
  const db = getPool();
  const stop = (): boolean => (ctx ? ctx.shouldStop() : false);
  const out: SweepResult = { abandonedRuns: 0, cancelledPartyRuns: 0, closedParties: 0 };

  // 1. 오래 방치된 진행 중 던전 판 -> abandoned(보상 없음)
  const stale = await db.query<{ id: string }>(
    `SELECT id FROM dungeon_runs WHERE state = 'playing' AND started_at < $1 ORDER BY id LIMIT $2`,
    [new Date(now.getTime() - pol.runStaleSeconds * 1000), LIMIT],
  );
  for (const r of stale.rows) {
    if (stop()) break;
    await withTransaction(async (client) => {
      const lock = await client.query<{ id: string; party_run_id: string | null }>(
        `SELECT id, party_run_id FROM dungeon_runs WHERE id = $1 AND state = 'playing' AND started_at < $2 FOR UPDATE SKIP LOCKED`,
        [r.id, new Date(now.getTime() - pol.runStaleSeconds * 1000)],
      );
      const row = lock.rows[0];
      if (!row) return;
      await dungeonRepo.abandonRun(client, Number(row.id), now);
      out.abandonedRuns++;
      if (row.party_run_id !== null) {
        const pr = await partyRunRepo.getRunById(client, Number(row.party_run_id));
        if (pr) await partyRunRepo.endRunIfDone(client, pr, now);
      }
    });
  }

  // 2. 마감이 한참 지난 모으는 중 파티 판 -> 취소(timeout)하고 파티를 모집 상태로 되돌린다
  const gathering = await db.query<{ id: string; party_id: string }>(
    `SELECT id, party_id FROM party_runs WHERE state = 'gathering' AND gather_deadline_at < $1 ORDER BY id LIMIT $2`,
    [new Date(now.getTime() - GATHER_GRACE_MS), LIMIT],
  );
  for (const r of gathering.rows) {
    if (stop()) break;
    await withTransaction(async (client) => {
      const lock = await client.query(
        `SELECT id FROM party_runs WHERE id = $1 AND state = 'gathering' FOR UPDATE SKIP LOCKED`,
        [r.id],
      );
      if (lock.rows.length === 0) return;
      await partyRunRepo.cancelRun(client, Number(r.id), 'timeout', now);
      await partyRunRepo.setPartyState(client, Number(r.party_id), 'forming');
      out.cancelledPartyRuns++;
    });
  }

  // 3. 방치된 파티(마지막 활동 후 PARTY_IDLE_MINUTES, 또는 출발 기한 경과) -> 해산
  const idle = await db.query<{ id: string; why: 'idle_timeout' | 'start_timeout' }>(
    `SELECT id, CASE WHEN start_by IS NOT NULL AND start_by <= $1 THEN 'start_timeout' ELSE 'idle_timeout' END AS why
       FROM parties
      WHERE state = 'forming'
        AND (last_active_at < $2 OR (start_by IS NOT NULL AND start_by <= $1))
      ORDER BY id LIMIT $3`,
    [now, new Date(now.getTime() - pol.partyIdleMinutes * 60_000), LIMIT],
  );
  for (const r of idle.rows) {
    if (stop()) break;
    await withTransaction(async (client) => {
      const lock = await client.query(`SELECT id FROM parties WHERE id = $1 AND state = 'forming' FOR UPDATE SKIP LOCKED`, [r.id]);
      if (lock.rows.length === 0) return;
      await partyRepo.closeParty(client, Number(r.id), r.why, now);
      out.closedParties++;
    });
  }
  return out;
}

export async function staleRunsJob(ctx: JobCtx): Promise<JobResult> {
  const r = await sweepStale(ctx);
  return { rows: r.abandonedRuns + r.cancelledPartyRuns + r.closedParties, detail: { ...r } };
}
