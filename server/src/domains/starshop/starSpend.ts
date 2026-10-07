// 일일 별조각 소비 상한(Docs/server/phase11_payments.md 11.3). 일일 경계는 resetBoundaries 하나만 쓴다.
// 탈취한 토큰·악성 코드가 지갑을 하루에 비우지 못하게 한다. 지갑 행을 잠근 상태에서 읽으므로 경쟁이 없다.
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { spentSince } from './starWallet';

export interface SpendCapView {
  /** 0 = 상한 꺼짐 */
  limit: number;
  used: number;
  left: number | null;
  resets_at: string;
}

export async function spendCapView(db: Queryable, accountId: number, now: Date): Promise<SpendCapView> {
  const limit = getConfig().pay.spendDailyCap;
  const b = resetBoundaries(now);
  const used = await spentSince(db, accountId, new Date(b.dailyStartAt));
  return { limit, used, left: limit > 0 ? Math.max(0, limit - used) : null, resets_at: b.nextDailyAt };
}

/** 오늘 소비 + 이번 소비가 상한을 넘으면 422 STAR_SPEND_CAP */
export async function assertSpendAllowed(db: Queryable, accountId: number, price: number, now: Date): Promise<void> {
  const limit = getConfig().pay.spendDailyCap;
  if (limit <= 0) return;
  const b = resetBoundaries(now);
  const used = await spentSince(db, accountId, new Date(b.dailyStartAt));
  if (used + price > limit) {
    throw new AppError(422, '오늘 별조각 사용 한도를 넘었습니다.', 'STAR_SPEND_CAP', { limit, used, resets_at: b.nextDailyAt });
  }
}
