// 레벨 달성 보상. 현재 캐릭터 -> 계정 순으로 잠근 한 트랜잭션에서 검사 -> 무료 별조각 지급 -> 계정당 1회 기록.
import { getPool, isUniqueViolation, withTransaction, type Queryable } from '../../db/pool';
import { getGrowthPass, getLevelRewards } from '../../gamedata/levelRewards';
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import { findOwnedAlive } from '../characters/characterRepository';
import { lockCharacter } from '../economy/economyRepository';
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
  character_id: string;
  character_level: number;
  /** 호환 필드: 계정 최고 레벨이 아니라 선택한 캐릭터의 레벨. */
  max_level: number;
  tiers: TierView[];
  pass_owned: boolean;
  pass_price: number;
  pass_tiers: PassTierView[];
}

/** 패스 보상 줄: 패스를 가졌고 현재 캐릭터가 단계에 닿았고 계정에서 아직 안 받았으면 claimable */
export async function passTiers(db: Queryable, accountId: number, characterLevel: number, owned: boolean): Promise<PassTierView[]> {
  const claimed = new Set(await repo.claimedPassLevels(db, accountId));
  return getGrowthPass().tiers.map((t) => ({ level: t.level, rewards: t.rewards, claimed: claimed.has(t.level), claimable: owned && !claimed.has(t.level) && characterLevel >= t.level }));
}

export async function view(db: Queryable, accountId: number, characterUuid: string): Promise<RewardsView> {
  const character = await findOwnedAlive(db, accountId, characterUuid);
  if (!character) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const characterLevel = character.level;
  const claimed = new Set(await repo.claimedLevels(db, accountId));
  const owned = await repo.passOwned(db, accountId);
  return {
    character_id: character.uuid,
    character_level: characterLevel,
    max_level: characterLevel,
    tiers: getLevelRewards().map((t) => ({ level: t.level, stars: t.stars, claimed: claimed.has(t.level), claimable: !claimed.has(t.level) && characterLevel >= t.level })),
    pass_owned: owned,
    pass_price: getGrowthPass().price,
    pass_tiers: await passTiers(db, accountId, characterLevel, owned),
  };
}

export async function list(accountId: number, characterUuid: string): Promise<RewardsView> {
  return view(getPool(), accountId, characterUuid);
}

export interface ClaimResult {
  character_id: string;
  character_level: number;
  level: number;
  stars: number;
  balance: number;
  tiers: TierView[];
}

async function replay(db: Queryable, accountId: number, characterUuid: string, prior: { level: number; stars: number }): Promise<ClaimResult> {
  const balance = (await repo.ledgerBalanceAfter(db, accountId, prior.level)) ?? (await wallets.readWallet(db, accountId)).balance;
  const v = await view(db, accountId, characterUuid);
  return { character_id: v.character_id, character_level: v.character_level, level: prior.level, stars: prior.stars, balance, tiers: v.tiers };
}

function assertSameRequest(prior: { level: number; characterId: number | null }, characterId: number, level: number): void {
  if (prior.level !== level || prior.characterId !== characterId) throw new AppError(409, '같은 요청 번호가 다른 캐릭터나 단계에 이미 쓰였습니다.', 'REQUEST_ID_REUSED', { level: prior.level });
}

export async function claim(accountId: number, characterUuid: string, body: ClaimBody): Promise<ClaimResult> {
  const tier = getLevelRewards().find((t) => t.level === body.level);
  try {
    return await withTransaction(async (db) => {
      const character = await lockCharacter(db, accountId, characterUuid);
      if (!character) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
      await repo.lockAccount(db, accountId);
      // 1. 같은 request_id: 그때 결과를 그대로(잠금 뒤에 봐서 동시 재전송도 여기로 온다)
      const prior = await repo.byRequest(db, accountId, body.request_id);
      if (prior) {
        assertSameRequest(prior, character.id, body.level);
        return replay(db, accountId, characterUuid, prior);
      }
      // 2. 단계 표에 없는 level
      if (!tier) throw new AppError(422, '존재하지 않는 보상 단계입니다.', 'UNKNOWN_TIER', { level: body.level });
      // 3. 현재 캐릭터의 서버 레벨(같은 계정의 다른 캐릭터 레벨은 수령 조건에 쓰지 않는다)
      if (character.level < tier.level) throw new AppError(409, '현재 캐릭터가 아직 이 레벨에 도달하지 않았습니다.', 'LEVEL_NOT_REACHED', { level: tier.level });
      // 4. 이미 받은 단계
      if ((await repo.claimedLevels(db, accountId)).includes(tier.level)) throw new AppError(409, '이미 받은 보상입니다.', 'ALREADY_CLAIMED', { level: tier.level });
      await assertNoHold(db, accountId, character.id);
      // 5. 무료 별조각 지급(원장 level_reward) + 기록
      const r = await wallets.creditFree(db, { accountId, reason: 'level_reward', amount: tier.stars, ref: `level:${tier.level}`, requestId: body.request_id });
      await repo.insertClaim(db, { accountId, level: tier.level, stars: tier.stars, requestId: body.request_id, characterId: character.id });
      return { character_id: character.uuid, character_level: character.level, level: tier.level, stars: tier.stars, balance: r.balance, tiers: (await view(db, accountId, characterUuid)).tiers };
    });
  } catch (err) {
    // 락 순서 밖에서 UNIQUE가 터진 경우(방어): 같은 결과 반환
    if (isUniqueViolation(err)) {
      const db = getPool();
      const prior = await repo.byRequest(db, accountId, body.request_id);
      if (prior) {
        const character = await findOwnedAlive(db, accountId, characterUuid);
        if (!character) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
        assertSameRequest(prior, character.id, body.level);
        return replay(db, accountId, characterUuid, prior);
      }
      throw new AppError(409, '이미 받은 보상입니다.', 'ALREADY_CLAIMED', { level: body.level });
    }
    throw err;
  }
}
