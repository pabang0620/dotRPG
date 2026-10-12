// 장비 뽑기 천장 교환(0033, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A).
// 게이지가 GEAR_CEILING.max 이상이면 이용자가 뽑기(무기·방어구·장신구)와 열린 레벨 단계를 골라 그 표의 유니크 장비
// 하나를 받는다(그 단계에 유니크가 없으면 가장 높은 등급). 게이지는 max만큼 줄고 넘친 만큼은 남는다.
import { tierOfLevel, TIER_LEVELS } from '../../utils/gearTier';
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import * as ceilings from '../ceilings/ceilingRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { GEAR_CEILING } from './starshopDefs';
import * as wallets from './starWallet';
import { gearCeilingView, gearTable } from './starshopService';
import type { GearCeilingBody } from './starshopValidation';

/** 그 뽑기·단계에서 천장으로 고를 수 있는 장비(유니크, 없으면 가장 높은 등급) */
export function gearCeilingChoices(banner: GearCeilingBody['banner'], cls: string, tier: number): string[] {
  const table = gearTable(banner, cls, tier);
  const row = table.find((t) => t.rarity === GEAR_CEILING.rarity) ?? table[0];
  return row ? row.items.map((i) => i.id) : [];
}

/** POST /characters/:uuid/starshop/gear-ceiling */
export function claimGearCeiling(accountId: number, characterUuid: string, body: GearCeilingBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/gear-ceiling',
    requestId,
    payload,
    handler: async (ctx) => {
      await assertNoHold(ctx.client, accountId, ctx.char.id);
      const myTier = tierOfLevel(ctx.char.level);
      const tier = body.tier ?? myTier;
      if (tier > myTier) {
        throw new AppError(422, '아직 열리지 않은 레벨 단계입니다.', 'GACHA_TIER_LOCKED', { need: TIER_LEVELS[tier], have: ctx.char.level });
      }
      if (!gearCeilingChoices(body.banner, ctx.char.class, tier).includes(body.item_id)) {
        throw new AppError(422, '고를 수 없는 항목입니다.', 'CLAIM_NOT_ALLOWED');
      }
      // 환불로 별조각 빚이 남은 계정은 뽑기와 같이 교환도 막는다. 락 순서: 지갑 -> 천장 행.
      wallets.assertNoStarDebt(await wallets.lockWallet(ctx.client, accountId));
      const state = await ceilings.lockCeilings(ctx.client, accountId);
      const before = state.gear;
      if (before < GEAR_CEILING.max) {
        throw new AppError(422, '천장 게이지가 아직 다 차지 않았습니다.', 'GAUGE_NOT_FULL', { need: GEAR_CEILING.max, have: before });
      }
      const after = before - GEAR_CEILING.max;
      await ceilings.setGauge(ctx.client, accountId, 'gear', after);
      // 원장: item_ledger reason gacha(뽑기와 같은 귀속), ref ceiling_<뽑기>
      await ctx.addItem('bag', body.item_id, 1, 'gacha', `ceiling_${body.banner}`);
      await ceilings.insertClaim(ctx.client, {
        accountId, characterId: ctx.char.id, requestId, kind: 'gear', itemKey: body.item_id, count: 1, before, after,
      });
      return { status: 200, data: { item_id: body.item_id, banner: body.banner, tier, gear_ceiling: gearCeilingView(after), delta: ctx.delta() } };
    },
  });
}
