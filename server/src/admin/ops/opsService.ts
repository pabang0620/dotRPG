// OP1~OP3: 서버 상태 스냅샷, 작업 목록과 정합성 결과, 작업 수동 실행(허용 목록만)
import { getPool } from '../../db/pool';
import { consumeOrThrow, MINUTE } from '../../middleware/rateLimiter';
import { isJobRunning, listJobs, runJob } from '../../ops/jobRunner';
import { MANUAL_JOBS } from '../../ops/jobs';
import { collectSnapshot } from '../../ops/snapshot';
import { AppError } from '../../utils/AppError';
import { runAdminActionDetached, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './opsRepository';

/** OP1 */
export const status = () => collectSnapshot();

/** OP2 */
export async function jobs() {
  const db = getPool();
  const [runs, ok, integrity] = await Promise.all([repo.recentRuns(db, 3), repo.lastOk(db), repo.lastIntegrity(db)]);
  return {
    jobs: listJobs().map((d) => ({
      name: d.name,
      schedule: d.schedule,
      running: isJobRunning(d.name),
      last_ok_at: ok.get(d.name)?.toISOString() ?? null,
      recent: runs
        .filter((r) => r.job === d.name)
        .map((r) => ({
          status: r.status,
          started_by: r.started_by,
          started_at: r.started_at.toISOString(),
          finished_at: r.finished_at ? r.finished_at.toISOString() : null,
          rows_affected: r.rows_affected,
          error: r.error,
        })),
    })),
    integrity: integrity ? { at: integrity.started_at.toISOString(), result: integrity.detail } : null,
  };
}

/** OP3: 허용 목록의 작업만. 이미 실행 중이면 409 JOB_RUNNING. 작업당 1분에 1회 */
export function runJobManually(admin: AdminCtx, ip: string, name: string, requestId: string, full: boolean): Promise<ActionResult> {
  return runAdminActionDetached({
    admin,
    ip,
    action: 'job.run',
    targetType: 'job',
    requestId,
    params: { name, ...(full ? { full } : {}) },
    work: async () => {
      if (!(MANUAL_JOBS as readonly string[]).includes(name)) throw new AppError(404, '실행할 수 없는 작업입니다.', 'JOB_NOT_FOUND');
      if (isJobRunning(name)) throw new AppError(409, '이미 실행 중인 작업입니다.', 'JOB_RUNNING');
      consumeOrThrow(`admin-job:${name}`, 1, MINUTE);
      const r = await runJob(name, 'manual', admin.id, { full });
      if (r.status === 'skipped') throw new AppError(409, '이미 실행 중인 작업입니다.', 'JOB_RUNNING');
      return { status: 200, data: { job: name, status: r.status, rows_affected: r.rows, detail: r.detail, ...(r.error ? { error: r.error } : {}) } };
    },
  });
}
