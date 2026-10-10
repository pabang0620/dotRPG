// 성장 패스(Docs/PLAN_CASH_BOX_PASS.md 5절): 별조각 9,000 한 번 구매(계정당 1회) + 레벨 단계별 보상 1회 수령.
// 구매는 계정 단위(계정 행 잠금), 수령은 캐릭터 경로(runEconomy)라 아이템이 그 캐릭터 가방으로 간다.
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { getGrowthPass } from '../../gamedata/levelRewards';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { assertNoHold } from '../antiabuse/holds';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { grantItems } from '../sealedbox/sealedBoxGrant';
import { assertSpendAllowed } from '../starshop/starSpend';
import * as wallets from '../starshop/starWallet';
import * as tickets from '../sweep/ticketWallet';
import * as repo from './levelRewardsRepository';
import { view } from './levelRewardsService';
import type { PassBuyBody, PassClaimBody } from './levelRewardsValidation';

export interface PassBuyResult {
  pass_owned: true;
  pass_price: number;
  balance: number;
}

async function replayBuy(accountId: number, requestId: string): Promise<PassBuyResult> {
  const db = getPool();
  const balance = (await repo.passLedgerBalanceAfter(db, accountId, requestId)) ?? (await wallets.readWallet(db, accountId)).balance;
  return { pass_owned: true, pass_price: getGrowthPass().price, balance };
}

/** POST /level-rewards/pass/buy: 계정 행 잠금 -> 이미 보유(409) -> 별조각 차감(무료분 먼저, 유료 FIFO) -> 보유 기록 */
export async function buy(accountId: number, body: PassBuyBody): Promise<PassBuyResult> {
  const price = getGrowthPass().price;
  try {
    return await withTransaction(async (db) => {
      await tickets.lockAccount(db, accountId);
      // 같은 request_id: 그때 결과를 그대로(잠금 뒤에 봐서 동시 재전송도 여기로 온다)
      if (await repo.passByRequest(db, accountId, body.request_id)) return replayBuy(accountId, body.request_id);
      if (await repo.passOwned(db, accountId)) throw new AppError(409, '이미 성장 패스를 가지고 있습니다.', 'ALREADY_OWNED');
      const top = await repo.topCharacter(db, accountId);
      await assertNoHold(db, accountId, top?.id ?? 0);
      const wallet = await wallets.lockWallet(db, accountId);
      wallets.assertNoStarDebt(wallet);
      if (wallet.balance < price) throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      await assertSpendAllowed(db, accountId, price, getNow());
      const paid = await wallets.debit(db, { accountId, reason: 'pass_buy', price, ref: 'growth_pass', requestId: body.request_id });
      await repo.insertPass(db, { accountId, stars: price, requestId: body.request_id });
      return { pass_owned: true as const, pass_price: price, balance: paid.balance };
    });
  } catch (err) {
    // 락 순서 밖에서 UNIQUE가 터진 경우(방어): 같은 요청이면 같은 결과, 아니면 이미 보유
    if (isUniqueViolation(err)) {
      if (await repo.passByRequest(getPool(), accountId, body.request_id)) return replayBuy(accountId, body.request_id);
      throw new AppError(409, '이미 성장 패스를 가지고 있습니다.', 'ALREADY_OWNED');
    }
    throw err;
  }
}

/** POST /characters/:uuid/level-rewards/pass/claim: 단계 보상을 이 캐릭터 가방에 지급(계정당 단계별 1회, 지난 단계도 소급 수령) */
export function claim(accountId: number, characterUuid: string, body: PassClaimBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/level-rewards/pass/claim',
    requestId,
    payload,
    handler: async (ctx) => {
      const tier = getGrowthPass().tiers.find((t) => t.level === body.level);
      if (!tier) throw new AppError(422, '존재하지 않는 패스 보상 단계입니다.', 'UNKNOWN_TIER', { level: body.level });
      await assertNoHold(ctx.client, accountId, ctx.char.id);
      // 락 순서: 캐릭터(runEconomy) -> 계정. 같은 계정의 다른 캐릭터 요청도 여기서 줄을 선다
      await tickets.lockAccount(ctx.client, accountId);
      if (!(await repo.passOwned(ctx.client, accountId))) throw new AppError(409, '성장 패스가 필요합니다.', 'PASS_REQUIRED');
      if (ctx.char.level < tier.level) throw new AppError(409, '현재 캐릭터가 아직 이 레벨에 도달하지 않았습니다.', 'LEVEL_NOT_REACHED', { level: tier.level });
      if ((await repo.claimedPassLevels(ctx.client, accountId)).includes(tier.level)) throw new AppError(409, '이미 받은 보상입니다.', 'ALREADY_CLAIMED', { level: tier.level });
      await grantItems(ctx, tier.rewards, 'pass_reward', `pass:${tier.level}`);
      await repo.insertPassClaim(ctx.client, { accountId, level: tier.level, requestId, characterId: ctx.char.id });
      const v = await view(ctx.client, accountId, characterUuid);
      return { status: 200, data: { character_id: v.character_id, character_level: v.character_level, level: tier.level, rewards: tier.rewards, pass_tiers: v.pass_tiers, delta: ctx.delta() } };
    },
  });
}
