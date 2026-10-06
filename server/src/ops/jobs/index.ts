import { getConfig } from '../../config/env';
import { holdSweepJob, incomeReconcileJob, presenceSweepJob } from '../../domains/antiabuse/holdSweep';
import { registerJob } from '../jobRunner';
import { integrityJob } from './integrity';
import { maintenanceCloseJob } from './maintenanceClose';
import { purgeDaily, purgeHourly } from './purge';
import { staleRunsJob } from './staleRuns';
import { campaignRevokeJob, campaignSweepJob, sweepTicketExpireJob } from './sweepJobs';

/** 관리자 OP3가 수동 실행할 수 있는 작업(허용 목록) */
export const MANUAL_JOBS = ['purge-hourly', 'purge-daily', 'stale-runs', 'integrity-nightly', 'presence-sweep', 'economy-hold-sweep', 'income-reconcile', 'sweep-ticket-expire', 'campaign-sweep', 'campaign-revoke'] as const;

let done = false;

/** 작업 등록(여러 번 불러도 한 번만). 스케줄은 6.2: 시간당 작업은 매시 :17, 새벽 배치는 KST 04:10~04:40 */
export function registerAllJobs(): void {
  if (done) return;
  done = true;
  registerJob({ name: 'purge-hourly', schedule: { kind: 'hourly', minute: 17 }, run: purgeHourly });
  registerJob({ name: 'purge-daily', schedule: { kind: 'daily_kst', hour: 4, minute: 10 }, run: purgeDaily });
  registerJob({ name: 'stale-runs', schedule: { kind: 'every', minutes: 10 }, run: staleRunsJob });
  registerJob({ name: 'integrity-nightly', schedule: { kind: 'daily_kst', hour: 4, minute: 30 }, run: integrityJob });
  // 9단계: 프레즌스 정리(60초), 경제 정지 전체 훑기(기본 10분), 원장 대조(매일 KST 04:20)
  registerJob({ name: 'presence-sweep', schedule: { kind: 'every', minutes: 1 }, run: presenceSweepJob });
  registerJob({ name: 'economy-hold-sweep', schedule: { kind: 'every', minutes: getConfig().aa.hold.sweepMinutes }, run: holdSweepJob });
  registerJob({ name: 'income-reconcile', schedule: { kind: 'daily_kst', hour: 4, minute: 20 }, run: incomeReconcileJob });
  // 10단계: 클리어권 만료(10분), 캠페인 상태 정리(60초), 미수령 우편 회수(30초)
  registerJob({ name: 'sweep-ticket-expire', schedule: { kind: 'every', minutes: 10 }, run: sweepTicketExpireJob });
  registerJob({ name: 'campaign-sweep', schedule: { kind: 'every', minutes: 1 }, run: campaignSweepJob });
  registerJob({ name: 'campaign-revoke', schedule: { kind: 'every', minutes: 0.5 }, run: campaignRevokeJob });
  registerJob({ name: 'maintenance-close', schedule: { kind: 'every', minutes: 1 }, run: maintenanceCloseJob });
}
