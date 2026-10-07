// 레벨 달성 보상(Docs/server/phase13_level_rewards.md). 계정 행을 잠근 한 트랜잭션에서 검사 -> 무료 별조각 지급 -> 기록.
import { getPool, isUniqueViolation, withTransaction, type Queryable } from '../../db/pool';
import { getGrowthPass, getLevelRewards } from '../../gamedata/levelRewards';
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import * as wallets from '../starshop/starWallet';
import * as repo from './levelRewardsRepository';
import type { ClaimBody } from './levelRewardsValidation';

export interface TierView {
  level: number;
  stars: number;
  claimable: boolean;
  claimed: boolean;
}
export interface PassTierView {
  level: number;
  rewards: { item_key: string; count: number }[];
  claimable: boolean;
  claimed: boolean;
}
export interface RewardsView {
  max_level: number;
  tiers: TierView[];
  pass_owned: boolean;
  pass_price: number;
  pass_tiers: PassTierView[];
}

/** 패스 보상 줄: 패스를 가졌고 계정 최고 레벨이 단계에 닿았고 아직 안 받았으면 claimable */
export async function passTiers(db: Queryable, accountId: number, maxLevel: number, owned: boolean): Promise<PassTierView[]> {
  const claimed = new Set(await repo.claimedPassLevels(db, accountId));
  return getGrowthPass().tiers.map((t) => ({ level: t.level, rewards: t.rewards, claimed: claimed.has(t.level), claimable: owned && !claimed.has(t.level) && maxLevel >= t.level }));
}

export async function view(db: Queryable, accountId: number): Promise<RewardsView> {
  const top = await repo.topCharacter(db, accountId);
  const maxLevel = top?.level ?? 0;
  const claimed = new Set(await repo.claimedLevels(db, accountId));
  const owned = await repo.passOwned(db, accountId);
  return {
    max_level: maxLevel,
    tiers: getLevelRewards().map((t) => ({ level: t.level, stars: t.stars, claimed: claimed.has(t.level), claimable: !claimed.has(t.level) && maxLevel >= t.level })),
    pass_owned: owned,
    pass_price: getGrowthPass().price,
    pass_tiers: await passTiers(db, accountId, maxLevel, owned),
  };
}

export async function list(accountId: number): Promise<RewardsView> {
  return view(getPool(), accountId);
}

export interface ClaimResult {
  level: number;
  stars: number;
  balance: number;
  tiers: TierView[];
}

async function replay(db: Queryable, accountId: number, prior: { level: number; stars: number }): Promise<ClaimResult> {
  const balance = (await repo.ledgerBalanceAfter(db, accountId, prior.level)) ?? (await wallets.readWallet(db, accountId)).balance;
  return { level: prior.level, stars: prior.stars, balance, tiers: (await view(db, accountId)).tiers };
}

export async function claim(accountId: number, body: ClaimBody): Promise<ClaimResult> {
  const tier = getLevelRewards().find((t) => t.level === body.level);
  try {
    return await withTransaction(async (db) => {
      await repo.lockAccount(db, accountId);
      // 1. 같은 request_id: 그때 결과를 그대로(잠금 뒤에 봐서 동시 재전송도 여기로 온다)
      const prior = await repo.byRequest(db, accountId, body.request_id);
      if (prior && prior.level !== body.level) throw new AppError(409, '같은 요청 번호가 다른 단계에 이미 쓰였습니다.', 'REQUEST_ID_REUSED', { level: prior.level });
      if (prior) return replay(db, accountId, prior);
      // 2. 단계 표에 없는 level
      if (!tier) throw new AppError(422, '존재하지 않는 보상 단계입니다.', 'UNKNOWN_TIER', { level: body.level });
      // 3. 계정 최고 레벨
      const top = await repo.topCharacter(db, accountId);
      if (!top || top.level < tier.level) throw new AppError(409, '아직 이 레벨에 도달하지 않았습니다.', 'LEVEL_NOT_REACHED', { level: tier.level });
      // 4. 이미 받은 단계
      if ((await repo.claimedLevels(db, accountId)).includes(tier.level)) throw new AppError(409, '이미 받은 보상입니다.', 'ALREADY_CLAIMED', { level: tier.level });
      await assertNoHold(db, accountId, top.id);
      // 5. 무료 별조각 지급(원장 level_reward) + 기록
      const r = await wallets.creditFree(db, { accountId, reason: 'level_reward', amount: tier.stars, ref: `level:${tier.level}`, requestId: body.request_id });
      await repo.insertClaim(db, { accountId, level: tier.level, stars: tier.stars, requestId: body.request_id, characterId: top.id });
      return { level: tier.level, stars: tier.stars, balance: r.balance, tiers: (await view(db, accountId)).tiers };
    });
  } catch (err) {
    // 락 순서 밖에서 UNIQUE가 터진 경우(방어): 같은 결과 반환
    if (isUniqueViolation(err)) {
      const db = getPool();
      const prior = await repo.byRequest(db, accountId, body.request_id);
      if (prior) return replay(db, accountId, prior);
      throw new AppError(409, '이미 받은 보상입니다.', 'ALREADY_CLAIMED', { level: body.level });
    }
    throw err;
  }
}
