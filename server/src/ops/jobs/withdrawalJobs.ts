// 회원 탈퇴 작업(설계 7, 8절). 로직은 domains/withdrawal 에 있고 여기는 JobRunner 에 붙이는 얇은 어댑터다.
import { runAnonymizeBatch } from '../../domains/withdrawal/anonymizeService';
import { runDestroy } from '../../domains/withdrawal/destroyService';
import type { JobCtx, JobResult } from '../jobRunner';

/** 10분마다. 기한이 지난 탈퇴 요청을 익명화한다(기능 스위치가 꺼져도 진행 중인 요청은 계속 처리한다) */
export async function withdrawalAnonymizeJob(ctx: JobCtx): Promise<JobResult> {
  const r = await runAnonymizeBatch(() => ctx.shouldStop());
  return { rows: r.completed, detail: { ...r } };
}

/** 매일 KST 04:55. 기본은 dry-run(건수만 기록). WITHDRAW_DESTROY_ENABLED=true 일 때만 실삭제 */
export async function withdrawalDestroyJob(ctx: JobCtx): Promise<JobResult> {
  const r = await runDestroy(() => ctx.shouldStop());
  return { rows: r.destroyed, detail: { ...r } };
}
