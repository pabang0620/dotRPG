// 로그인 경로(E1, E2)가 쓰는 탈퇴 도우미. authService 가 가볍게 가져다 쓰도록 의존을 적게 둔다.
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import * as repo from './withdrawalRepository';

/** 유예 중 로그인: 새 계정을 만들거나 일반 로그인을 시키지 않고 철회 화면으로 안내한다 */
export async function pendingWithdrawalError(accountId: number): Promise<AppError | null> {
  const w = await repo.openOf(getPool(), accountId);
  if (!w) return null;
  return new AppError(403, '탈퇴 요청 중인 계정입니다.', 'ACCOUNT_WITHDRAWAL_PENDING', {
    due_at: w.due_at.toISOString(),
    cancel_allowed: w.cancel_allowed,
  });
}
