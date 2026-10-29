// 별의 결정 -> 별조각 바꾸기(사용자 확정 2026-10-29). 결정을 내고 무료 별조각을 받는다(유료분이 되지 않는다).
// 락 순서: 캐릭터(runEconomy) -> 별조각 지갑 -> 결정 지갑(cashValueService와 같다). 원장: crystal_ledger convert_stars +
// star_ledger crystal_convert(같은 request_id). 바꾸는 양·횟수 제한은 두지 않는다(결제 제한 금지 원칙).
//
// 비율: 결정 CRYSTALS_PER_STAR(2)개 = 별조각 1개. 교환소 품목 값(결정 1 = 별조각 2 가치)보다 4배 낮아 교환소가 주 용도로 남는다.
// 되돌이 계산(별조각 1개를 써서 돌아오는 최대 몫, 모든 결과가 여분이라 전부 분해한다고 가정, test/crystalConvert.test.ts 가 검사):
//   스킨 뽑기 11회 묶음(1,000): 분해 별조각 약 385 + 결정 약 77(컬렉션 +20% 92) -> 92 / 2 = 46 => 약 43%
//   오라 뽑기 11회 묶음: 분해 별조각 약 345 + 결정 약 75(+20% 89) -> 45 => 약 39%
//   장비 뽑기 11회 묶음: 별조각 0 + 결정 약 90(전부 +20 강화해 2배면 179, 천장 유니크 몫 포함 약 190) -> 95 => 약 10%
//   합성은 같은 값의 여분을 더 적은 값으로 바꾸므로(예: 에픽 약 13.5개 -> 유니크 1개) 몫을 늘리지 않는다.
//   => 최악 약 43%로 100%보다 한참 낮다. 되돌려 다시 뽑아도 1 / (1 - 0.43) = 약 1.76배 뽑기가 한계(무한 루프 없음).
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as crystals from './crystalWallet';
import * as wallets from './starWallet';

/** 별조각 1개에 드는 별의 결정 */
export const CRYSTALS_PER_STAR = 2;

/** 결정 amount개로 받는 별조각과 실제로 쓰는 결정(남는 나머지 결정은 지갑에 그대로 둔다) */
export function convertPlan(amount: number): { stars: number; crystals: number } {
  const stars = Math.floor(Math.max(0, amount) / CRYSTALS_PER_STAR);
  return { stars, crystals: stars * CRYSTALS_PER_STAR };
}

export interface CrystalConvertBody {
  request_id: string;
  crystals: number;
}

/** POST /characters/:uuid/starshop/crystal-convert: 결정 crystals개(비율의 배수로 내림)를 무료 별조각으로 */
export function convertCrystals(accountId: number, characterUuid: string, body: CrystalConvertBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/crystal-convert',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const plan = convertPlan(body.crystals);
      if (plan.stars <= 0) {
        throw new AppError(422, `별의 결정 ${CRYSTALS_PER_STAR}개부터 바꿀 수 있습니다.`, 'CONVERT_TOO_SMALL', { per_star: CRYSTALS_PER_STAR });
      }
      // 경제 정지 중에는 별조각이 늘어나는 바꾸기도 막는다(분해와 같다)
      await assertNoHold(db, ctx.char.accountId, ctx.char.id);
      await wallets.lockWallet(db, accountId);
      // debitCrystals 가 결정 지갑을 잠그고 부채·잔액을 확인한다(모자라면 422 NOT_ENOUGH_CRYSTALS, 부채면 403 CRYSTAL_DEBT)
      const cw = await crystals.debitCrystals(db, { accountId, price: plan.crystals, ref: 'convert', requestId, reason: 'convert_stars' });
      const sw = await wallets.creditFree(db, { accountId, reason: 'crystal_convert', amount: plan.stars, ref: 'crystal_convert', requestId });
      return {
        status: 200,
        data: { crystals: plan.crystals, stars: plan.stars, per_star: CRYSTALS_PER_STAR, crystal_balance: cw.balance, balance: sw.balance, settled: sw.settled },
      };
    },
  });
}
