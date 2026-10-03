import { registerJob } from '../jobRunner';
import { integrityJob } from './integrity';
import { maintenanceCloseJob } from './maintenanceClose';
import { purgeDaily, purgeHourly } from './purge';
import { staleRunsJob } from './staleRuns';

/** 관리자 OP3가 수동 실행할 수 있는 작업(허용 목록) */
export const MANUAL_JOBS = ['purge-hourly', 'purge-daily', 'stale-runs', 'integrity-nightly'] as const;

let done = false;

/** 작업 등록(여러 번 불러도 한 번만). 스케줄은 6.2: 시간당 작업은 매시 :17, 새벽 배치는 KST 04:10~04:40 */
export function registerAllJobs(): void {
  if (done) return;
  done = true;
  registerJob({ name: 'purge-hourly', schedule: { kind: 'hourly', minute: 17 }, run: purgeHourly });
  registerJob({ name: 'purge-daily', schedule: { kind: 'daily_kst', hour: 4, minute: 10 }, run: purgeDaily });
  registerJob({ name: 'stale-runs', schedule: { kind: 'every', minutes: 10 }, run: staleRunsJob });
  registerJob({ name: 'integrity-nightly', schedule: { kind: 'daily_kst', hour: 4, minute: 30 }, run: integrityJob });
  registerJob({ name: 'maintenance-close', schedule: { kind: 'every', minutes: 1 }, run: maintenanceCloseJob });
}
