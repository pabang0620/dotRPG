// 봉인된 상자 천장 교환(0033, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A).
// 게이지가 every 이상이면 이용자가 눌러서 reward(+12 강화권) 하나와 바꾼다. 게이지는 every만큼 줄고 넘친 만큼은 남는다.
// 확률로 같은 보상이 나와도 게이지는 그대로다(교환해야만 줄어든다).
import { getSealedBox } from '../../gamedata/sealedBox';
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import * as ceilings from '../ceilings/ceilingRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as wallets from '../starshop/starWallet';
import * as tickets from '../sweep/ticketWallet';
import { grantItems } from './sealedBoxGrant';
import { ceilingView } from './sealedBoxService';
import type { CeilingBody } from './sealedBoxValidation';

/** POST /characters/:uuid/starshop/sealed/ceiling */
export function claimSealedCeiling(accountId: number, characterUuid: string, body: CeilingBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/sealed/ceiling',
    requestId,
    payload,
    handler: async (ctx) => {
      const d = getSealedBox();
      await assertNoHold(ctx.client, accountId, ctx.char.id);
      // 락 순서: 캐릭터(runEconomy) -> 계정(보상이 클리어권일 때) -> 별조각 지갑 -> 천장 행
      await tickets.lockAccount(ctx.client, accountId);
      // 환불로 별조각 빚이 남은 계정은 뽑기와 같이 교환도 막는다(환불한 결제로 채운 게이지).
      wallets.assertNoStarDebt(await wallets.lockWallet(ctx.client, accountId));
      const state = await ceilings.lockCeilings(ctx.client, accountId);
      const before = state.sealed;
      if (before < d.ceiling.every) {
        throw new AppError(422, '천장 게이지가 아직 다 차지 않았습니다.', 'GAUGE_NOT_FULL', { need: d.ceiling.every, have: before });
      }
      const after = before - d.ceiling.every;
      await ceilings.setGauge(ctx.client, accountId, 'sealed', after);
      const reward = d.ceiling.reward;
      // 원장: item_ledger reason sealed_box, ref ceiling (클리어권이면 계정 지갑)
      await grantItems(ctx, [reward], 'sealed_box', 'ceiling');
      await ceilings.insertClaim(ctx.client, {
        accountId, characterId: ctx.char.id, requestId, kind: 'sealed', itemKey: reward.item_key, count: reward.count, before, after,
      });
      return { status: 200, data: { reward, ceiling: ceilingView(after, d), delta: ctx.delta() } };
    },
  });
}
