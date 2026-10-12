import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { assertActionPresence } from '../antiabuse/killPresence';
import { AnomalyError, runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './chestRepository';
import type { ChestBody } from './chestValidation';

export function openChest(accountId: number, characterUuid: string, body: ChestBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/chests/open',
    requestId,
    payload,
    handler: async (ctx) => {
      const eco = getGameData().economy;
      const known = [...eco.mapExtra.values()].some((m) => m.chests.has(body.chest_id));
      if (!known) {
        throw new AnomalyError(422, '알 수 없는 상자입니다.', 'CHEST_UNKNOWN', {
          kind: 'chest_unknown',
          severity: 2,
          detail: { chest_id: body.chest_id },
        });
      }
      const chestMap = [...eco.mapExtra].find(([, m]) => m.chests.has(body.chest_id))?.[0] ?? null;
      await assertActionPresence(ctx, 'chest', chestMap);
      // PK(character_id, chest_id)가 "캐릭터당 한 번"을 DB에서 보장한다
      if (!(await repo.insertChest(ctx.client, ctx.char.id, body.chest_id, ctx.now))) {
        throw new AppError(409, '이미 연 상자입니다.', 'CHEST_ALREADY_OPENED');
      }
      const reward = eco.config.chestReward;
      await ctx.addItem('bag', reward.itemKey, reward.count, 'chest', body.chest_id);
      return {
        status: 200,
        data: {
          chest_id: body.chest_id,
          granted: { item_key: reward.itemKey, count: reward.count },
          delta: ctx.delta(),
        },
      };
    },
  });
}
