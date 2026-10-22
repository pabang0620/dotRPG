// 장비 계승(Docs/PLAN_GEAR_INHERIT_2026-10-22.md): Lv10 -> 20, 20 -> 30, 30 -> 40 장비로 갈아타며 강화 단계를 확률로 이어받는다.
// 표는 C# InheritRules에서 내보낸 enhance.json의 inheritTiers. 확률은 서버가 굴리고(crypto 난수), 클라이언트는 미리보기만 한다.
import { randomUUID } from 'node:crypto';
import { assertNoHold } from '../antiabuse/holds';
import { getGameData } from '../../gamedata/loader';
import { inheritResultLevel } from '../../gamedata/inheritData';
import { AppError } from '../../utils/AppError';
import { keyAt, parseItemKey } from '../../utils/itemKey';
import { getRng } from '../../utils/rng';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as pityRepo from '../enhance/enhanceRepository';
import { moveGachaCredit } from '../starshop/cashValueService';
import type { InheritBody } from './inheritValidation';

export function inherit(accountId: number, characterUuid: string, body: InheritBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/inherit',
    requestId,
    payload,
    handler: (ctx) => processInherit(ctx, body),
  });
}

const invalidTarget = () => new AppError(422, '계승할 수 없는 장비입니다.', 'INHERIT_INVALID_TARGET');

/** 유지 판정: [0, rollRange) 정수를 굴려 roll < 유지 확률이면 유지 */
export function rollKeep(rollRange: number, keepPercent: number): { roll: number; kept: boolean } {
  const roll = getRng().int(0, rollRange);
  return { roll, kept: roll < keepPercent };
}

async function processInherit(ctx: EconCtx, body: InheritBody) {
  // 경제 정지 중에는 계승할 수 없다
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
  const eco = getGameData().economy;
  const table = eco.enhance.inheritTiers;
  if (!table) throw new AppError(503, '계승을 준비 중입니다.', 'INHERIT_UNAVAILABLE');
  const wornSlot = 'worn_slot' in body.target ? body.target.worn_slot : null;

  // 1. 대상: 착용 슬롯의 키 또는 가방 스택 수량 >= 1. 표에 없는 장비(시작 장비, Lv1·15·25·35, 필드 보스 장신구 등)는 거절
  const key = wornSlot !== null ? await ctx.wornKey(wornSlot) : (body.target as { bag_key: string }).bag_key;
  if (!key) throw invalidTarget();
  if (wornSlot === null && (await ctx.stackCount('bag', key)) < 1) throw invalidTarget();
  const parsed = parseItemKey(key);
  const row = parsed ? table.rows.get(parsed.base) : undefined;
  const to = row ? eco.shop.equipment.get(row.to) : undefined;
  if (!parsed || !row || !to || parsed.level > eco.enhance.maxEnhance) throw invalidTarget();
  if (parsed.inherit >= table.maxCount) throw new AppError(422, '더 이상 계승할 수 없는 장비입니다.', 'INHERIT_MAX_COUNT');
  // 착용 중인 장비는 새 장비를 입을 수 있는 레벨이어야 한다(가방에서는 레벨과 상관없이 된다)
  if (wornSlot !== null && ctx.level < to.reqLevel) {
    throw new AppError(422, '착용 중인 장비는 새 장비의 레벨이 되어야 계승할 수 있습니다.', 'INHERIT_LEVEL', { need: to.reqLevel, have: ctx.level });
  }

  // 2. 비용
  const gold = row.gold[parsed.level] ?? 0;
  if (ctx.gold < gold) throw new AppError(422, '골드가 모자랍니다.', 'INHERIT_NOT_ENOUGH', { need: { gold }, have: { gold: ctx.gold } });

  // 3. 비용 지급 후 서버가 굴린다
  const logId = randomUUID();
  if (gold > 0) await ctx.changeGold(-gold, 'inherit', logId);
  const keepPercent = table.keepPercent[parsed.level] ?? 0;
  const { kept } = rollKeep(eco.enhance.rollRange, keepPercent);
  const newLevel = inheritResultLevel(table, parsed.level, parsed.inherit, kept);
  const newKey = keyAt(row.to, newLevel, parsed.inherit + 1);

  // 4. 장비 바꾸기(귀속은 소모한 행 그대로, 승급과 같다)
  let bind: string | null = null;
  if (wornSlot !== null) {
    const w = await ctx.client.query<{ bind: string }>(
      "SELECT bind FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2",
      [ctx.char.id, wornSlot],
    );
    bind = w.rows[0]?.bind ?? null;
    await ctx.wornReplace(wornSlot, key, newKey, 'inherit', logId);
  } else {
    const consumed = await ctx.removeItem('bag', key, 1, 'inherit', logId);
    if (consumed === null) throw invalidTarget();
    await ctx.addConsumed('bag', newKey, consumed, 'inherit', logId);
    bind = consumed[0]?.bind ?? null;
  }
  // 뽑기 사본(계정 귀속)이면 뽑기 몫도 새 기본 id로 옮긴다(상점 판매 금지·분해 허용이 따라간다)
  if (bind === 'account') await moveGachaCredit(ctx.client, ctx.char.id, parsed.base, row.to, 1);

  // 5. 보호권(강화 보정) 이력은 결과 장비로 옮긴다. 같은 키의 다른 사본이 남아 있으면 옛 키 쪽도 지우지 않는다
  let pity = await pityRepo.getPity(ctx.client, ctx.char.id, newKey);
  const oldPity = await pityRepo.getPity(ctx.client, ctx.char.id, key);
  if (oldPity > 0) {
    pity = Math.max(pity, oldPity);
    await pityRepo.setPity(ctx.client, ctx.char.id, newKey, pity, ctx.now);
    if (!(await stillHeld(ctx, key))) await pityRepo.clearPity(ctx.client, ctx.char.id, key);
  }

  return {
    status: 200,
    data: { old_key: key, new_key: newKey, kept, cost: { gold }, pity, delta: ctx.delta() },
  };
}

/** 이 캐릭터가 아직 그 키를 가지고 있는지(가방·창고·착용) */
async function stillHeld(ctx: EconCtx, key: string): Promise<boolean> {
  const r = await ctx.client.query<{ n: string }>(
    'SELECT COALESCE(SUM(count), 0) AS n FROM character_items WHERE character_id = $1 AND item_key = $2',
    [ctx.char.id, key],
  );
  return Number(r.rows[0]?.n ?? 0) > 0;
}
