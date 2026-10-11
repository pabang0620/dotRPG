// 스킬 스타일 3 해금(Docs/PLAN_SKILL_STYLES.md 4절): 별조각 한 번 구매(계정당 1회). 성장 패스 구매와 같은 흐름.
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { assertNoHold } from '../antiabuse/holds';
import { assertSpendAllowed } from '../starshop/starSpend';
import * as wallets from '../starshop/starWallet';
import * as tickets from '../sweep/ticketWallet';
import * as repo from './skillStyleRepository';
import type { StyleBuyBody } from './skillStyleValidation';

/** 스타일 3 가격(별조각). 바꿀 곳은 여기 한 곳 */
export const SKILL_STYLE3_PRICE = 3000;

export interface StyleView {
  style3_owned: boolean;
  price: number;
}

export interface StyleBuyResult {
  style3_owned: true;
  price: number;
  balance: number;
}

export async function view(accountId: number): Promise<StyleView> {
  return { style3_owned: await repo.style3Owned(getPool(), accountId), price: SKILL_STYLE3_PRICE };
}

async function replayBuy(accountId: number, requestId: string): Promise<StyleBuyResult> {
  const db = getPool();
  const balance = (await repo.ledgerBalanceAfter(db, accountId, requestId)) ?? (await wallets.readWallet(db, accountId)).balance;
  return { style3_owned: true, price: SKILL_STYLE3_PRICE, balance };
}

/** POST /skill-styles/buy: 계정 행 잠금 -> 같은 요청 재전송 -> 이미 보유(409) -> 별조각 차감(무료분 먼저) -> 해금 기록 */
export async function buy(accountId: number, body: StyleBuyBody): Promise<StyleBuyResult> {
  const price = SKILL_STYLE3_PRICE;
  try {
    return await withTransaction(async (db) => {
      await tickets.lockAccount(db, accountId);
      if (await repo.unlockByRequest(db, accountId, body.request_id)) return replayBuy(accountId, body.request_id);
      if (await repo.style3Owned(db, accountId)) throw new AppError(409, '이미 스킬 스타일 3을 가지고 있습니다.', 'ALREADY_OWNED');
      await assertNoHold(db, accountId, await repo.topCharacterId(db, accountId));
      const wallet = await wallets.lockWallet(db, accountId);
      wallets.assertNoStarDebt(wallet);
      if (wallet.balance < price) throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      await assertSpendAllowed(db, accountId, price, getNow());
      const paid = await wallets.debit(db, { accountId, reason: 'skill_style_buy', price, ref: 'skill_style_3', requestId: body.request_id });
      await repo.insertUnlock(db, { accountId, stars: price, requestId: body.request_id });
      return { style3_owned: true as const, price, balance: paid.balance };
    });
  } catch (err) {
    if (isUniqueViolation(err)) {
      if (await repo.unlockByRequest(getPool(), accountId, body.request_id)) return replayBuy(accountId, body.request_id);
      throw new AppError(409, '이미 스킬 스타일 3을 가지고 있습니다.', 'ALREADY_OWNED');
    }
    throw err;
  }
}
