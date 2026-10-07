// 경제 정지 평가의 실행 시점(12.8): 더티 워커(30초), 전체 훑기 작업(10분), 일 1회 원장 대조 작업. 평가는 요청 경로에서 하지 않는다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { metrics } from '../../ops/metrics';
import type { JobCtx, JobResult } from '../../ops/jobRunner';
import { checkCharacter, propagate } from './holds';
import * as repo from './holdsRepository';
import { HOUR_MS, takeDirty } from './incomeMeter';
import { reconcileIncome } from './incomeReconcile';
import { sweepPresence } from './presenceService';

/** 더티 집합의 캐릭터를 한 번 평가한다(워커 틱이 부르고, 테스트도 직접 부른다) */
export async function runHoldCheck(): Promise<{ checked: number; violated: number }> {
  const ids = takeDirty();
  let violated = 0;
  for (const id of ids) {
    try {
      if ((await checkCharacter(id)).violated) violated++;
    } catch (err) {
      logger.error({ err, character_id: id }, 'hold check failed');
    }
  }
  return { checked: ids.length, violated };
}

/** 서버 기동 때 한 번: ECONOMY_HOLD_CHECK_SECONDS마다 더티 캐릭터를 평가한다. 돌려주는 함수는 정지 */
export function startHoldWorker(): () => void {
  const cfg = getConfig().aa.hold;
  if (cfg.mode === 'off') return () => undefined;
  let running = false;
  const timer = setInterval(() => {
    if (running) return;
    running = true;
    metrics
      .track('economy_hold', runHoldCheck)
      .catch((err: unknown) => logger.error({ err }, 'hold worker failed'))
      .finally(() => {
        running = false;
      });
  }, cfg.checkSeconds * 1000);
  timer.unref();
  return () => clearInterval(timer);
}

/** economy-hold-sweep: 최근 7일 활동 캐릭터 전체를 평가하고 활성 정지의 전파를 다시 확인한다(더티 집합이 재시작으로 사라져도 따라잡는다) */
export async function holdSweepJob(ctx: JobCtx): Promise<JobResult> {
  if (getConfig().aa.hold.mode === 'off') return { rows: 0, detail: { skipped: 'off' } };
  const now = getNow();
  const db = getPool();
  const r = await db.query<{ character_id: string }>('SELECT DISTINCT character_id FROM income_hourly WHERE updated_at > $1', [new Date(now.getTime() - 7 * 24 * HOUR_MS)]);
  let checked = 0;
  let violated = 0;
  for (const row of r.rows) {
    if (ctx.shouldStop()) break;
    try {
      checked++;
      if ((await checkCharacter(Number(row.character_id), now)).violated) violated++;
    } catch (err) {
      logger.error({ err }, 'hold sweep character failed');
    }
  }
  let linked = 0;
  const open = await db.query<{ id: string }>("SELECT id FROM economy_holds WHERE state = 'active' AND kind IN ('velocity', 'auction') AND created_at > $1", [new Date(now.getTime() - 7 * 24 * HOUR_MS)]);
  for (const o of open.rows) {
    if (ctx.shouldStop()) break;
    const hold = await repo.holdById(db, Number(o.id));
    if (hold) linked += await propagate(hold, now);
  }
  return { rows: checked, detail: { checked, violated, linked } };
}

/** income-reconcile: 최근 2일치 income_hourly를 원장과 대조하고 어긋나면 고친다 */
export async function incomeReconcileJob(): Promise<JobResult> {
  const r = await reconcileIncome({ hours: 48, fix: true });
  return { rows: r.fixed, detail: { checked: r.checked, mismatched: r.mismatched, fixed: r.fixed, samples: r.samples } };
}

/** presence-sweep: 신선도를 잃은 활성 프레즌스를 닫는다 */
export async function presenceSweepJob(): Promise<JobResult> {
  const n = await sweepPresence();
  return { rows: n, detail: { closed: n } };
}
