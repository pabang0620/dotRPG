import { getConfig } from '../../config/env';
import { holdSweepJob, incomeReconcileJob, presenceSweepJob } from '../../domains/antiabuse/holdSweep';
import { registerJob } from '../jobRunner';
import { integrityJob } from './integrity';
import { maintenanceCloseJob } from './maintenanceClose';
import { playWindowSweepJob } from './playWindowSweep';
import { ratesPublicationJob } from './ratesPublication';
import { purgeDaily, purgeHourly } from './purge';
import { staleRunsJob } from './staleRuns';
import { paymentReconcileJob, paymentReportJob, paymentWatchJob, starGrantExpireJob } from './paymentJobs';
import { withdrawalAnonymizeJob, withdrawalDestroyJob } from './withdrawalJobs';
import { campaignRevokeJob, campaignSweepJob, sweepTicketExpireJob } from './sweepJobs';
import { settleDueSeasons, unequipExpiredRankTitles } from '../../domains/bossrush/bossRushSettle';

/** 관리자 OP3가 수동 실행할 수 있는 작업(허용 목록) */
export const MANUAL_JOBS = ['purge-hourly', 'purge-daily', 'stale-runs', 'integrity-nightly', 'presence-sweep', 'economy-hold-sweep', 'income-reconcile', 'sweep-ticket-expire', 'campaign-sweep', 'campaign-revoke', 'payment-reconcile', 'payment-watch', 'payment-report', 'star-grant-expire', 'withdrawal-anonymize', 'withdrawal-destroy', 'play-window-sweep', 'rates-publication', 'boss-settle', 'rank-title-expire'] as const;

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
  // 11단계: 결제 대사(60초), 환불·차지백 감시(5분), Steam 리포트 교차 점검(매일 KST 04:50), 운영 지급 대기 만료(1시간). 플래그와 무관하게 돈다
  registerJob({ name: 'payment-reconcile', schedule: { kind: 'every', minutes: 1 }, run: paymentReconcileJob });
  registerJob({ name: 'payment-watch', schedule: { kind: 'every', minutes: 5 }, run: paymentWatchJob });
  registerJob({ name: 'payment-report', schedule: { kind: 'daily_kst', hour: 4, minute: 50 }, run: paymentReportJob });
  registerJob({ name: 'star-grant-expire', schedule: { kind: 'every', minutes: 60 }, run: starGrantExpireJob });
  // 회원 탈퇴: 익명화(10분), 5년 파기(매일 KST 04:55, payment-report 04:50 뒤. 기본 dry-run)
  registerJob({ name: 'withdrawal-anonymize', schedule: { kind: 'every', minutes: 10 }, run: withdrawalAnonymizeJob });
  registerJob({ name: 'withdrawal-destroy', schedule: { kind: 'daily_kst', hour: 4, minute: 55 }, run: withdrawalDestroyJob });
  // 청소년 보호 정책 제4조: 게임시간 선택제 구간을 넘긴 접속 중 계정 끊기(60초)
  registerJob({ name: 'play-window-sweep', schedule: { kind: 'every', minutes: 1 }, run: playWindowSweepJob });
  // 이용약관 제14조의2 3항: 공개 중인 확률표 지문 기록(1시간)
  registerJob({ name: 'rates-publication', schedule: { kind: 'every', minutes: 60 }, run: ratesPublicationJob });
  registerJob({ name: 'maintenance-close', schedule: { kind: 'every', minutes: 1 }, run: maintenanceCloseJob });
  // 월드보스·길드보스 주간 정산: 목요일 06:00 KST에 끝난 시즌을 정산한다. 10분마다 "끝났는데 정산 안 한 시즌"을 찾으므로
  // 경계 직후 서버가 꺼져 있었어도 다시 켜지면 정산된다(시즌 행 잠금 + settled_at으로 한 번만)
  registerJob({ name: 'boss-settle', schedule: { kind: 'every', minutes: 10 }, run: async () => {
    const r = await settleDueSeasons();
    return { rows: r.people, detail: r };
  } });
  // 랭킹 칭호 만료(week_end + 7일 + 1시간)는 정산 사이에 오므로, 매일 KST 06:05에 장착한 만료 칭호를 뗀다
  registerJob({ name: 'rank-title-expire', schedule: { kind: 'daily_kst', hour: 6, minute: 5 }, run: async () => { const n = await unequipExpiredRankTitles(); return { rows: n, detail: { unequipped: n } }; } });
}
