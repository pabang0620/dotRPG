// 부활 코인(Docs/server/phase12_revive_coins.md). 일일 지급은 조회·사용 시점에 계산한다(lazy).
// 캐릭터 행을 FOR UPDATE로 잠근 한 트랜잭션 안에서 지급 반영, 소모, revive_log 기록을 한다.
import { withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { assertNoHold } from '../antiabuse/holds';
import * as repo from './reviveRepository';
import type { ReviveBody } from './reviveValidation';

export const REVIVE_MAX = 5;
export const REVIVE_FREE_UNTIL_LEVEL = 10;
const DAY_MS = 24 * 60 * 60 * 1000;
const KST_OFFSET_MS = 9 * 60 * 60 * 1000;

/** 게임 일자 'YYYY-MM-DD': 직전 06:00 KST의 KST 날짜(던전 입장 횟수와 같은 resetBoundaries 기준) */
export function gameDay(now: Date): { day: string; nextGrantAt: string } {
  const b = resetBoundaries(now);
  return { day: new Date(Date.parse(b.dailyStartAt) + KST_OFFSET_MS).toISOString().slice(0, 10), nextGrantAt: b.nextDailyAt };
}

/** 일일 지급 반영: 마지막 반영 이후 지난 게임 일수만큼 더하고 상한 5. day가 NULL이면 오늘부터 시작 */
export function applyGrant(coins: number, lastDay: string | null, today: string): number {
  if (!lastDay) return coins;
  const days = Math.floor((Date.parse(today) - Date.parse(lastDay)) / DAY_MS);
  return days > 0 ? Math.min(REVIVE_MAX, coins + days) : coins;
}

export interface ReviveStatus {
  coins: number;
  max: number;
  free: boolean;
  free_until_level: number;
  next_grant_at: string;
}

export interface ReviveResult {
  free: boolean;
  coins: number;
  max: number;
  next_grant_at: string;
}

export async function reviveStatus(accountId: number, uuid: string): Promise<ReviveStatus> {
  return withTransaction(async (db) => {
    const c = await repo.lockOwned(db, accountId, uuid);
    if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
    const { day, nextGrantAt } = gameDay(getNow());
    const coins = applyGrant(c.revive_coins, c.revive_coin_day, day);
    if (coins !== c.revive_coins || c.revive_coin_day !== day) await repo.setCoins(db, c.id, coins, day);
    return { coins, max: REVIVE_MAX, free: c.level <= REVIVE_FREE_UNTIL_LEVEL, free_until_level: REVIVE_FREE_UNTIL_LEVEL, next_grant_at: nextGrantAt };
  });
}

export async function revive(accountId: number, uuid: string, body: ReviveBody): Promise<ReviveResult> {
  return withTransaction(async (db) => {
    const c = await repo.lockOwned(db, accountId, uuid);
    if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
    // 1. 멱등: 같은 request_id가 있으면 그때 결과를 그대로 돌려준다(행 잠금 뒤라 동시 요청도 여기서 걸린다)
    const prev = await repo.findLog(db, c.id, body.request_id);
    if (prev) {
      return { free: prev.free, coins: prev.coins_after, max: REVIVE_MAX, next_grant_at: gameDay(prev.created_at).nextGrantAt };
    }
    await assertNoHold(db, accountId, c.id);
    // 2. 일일 지급 반영
    const now = getNow();
    const { day, nextGrantAt } = gameDay(now);
    let coins = applyGrant(c.revive_coins, c.revive_coin_day, day);
    // 3~4. 무료 구간 또는 코인 1개 소모
    const free = c.level <= REVIVE_FREE_UNTIL_LEVEL;
    if (!free) {
      if (coins <= 0) {
        // 지급 반영은 남겨 둔다(롤백하면 같은 날 다시 계산할 뿐이라 결과는 같다)
        throw new AppError(409, '부활 코인이 없습니다.', 'NO_REVIVE_COIN', { next_grant_at: nextGrantAt });
      }
      coins -= 1;
    }
    if (coins !== c.revive_coins || c.revive_coin_day !== day) await repo.setCoins(db, c.id, coins, day);
    // 5. 기록
    await repo.insertLog(db, { characterId: c.id, requestId: body.request_id, context: body.context, free, coinsAfter: coins, level: c.level, mapId: body.map_id ?? null, at: now });
    return { free, coins, max: REVIVE_MAX, next_grant_at: nextGrantAt };
  });
}
