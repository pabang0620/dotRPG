// 외형 여분 활용: 합성(같은 등급 4개 -> 한 등급 위), 분해(여분 -> 별조각), 컬렉션 등록(세트 완성 -> 능력치).
// 모두 계정 지갑 행을 잠근 뒤 처리한다(같은 계정의 동시 요청은 줄을 선다). 규칙과 수치는 starshopDefs.
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { COLLECTION_BY_ID, DISMANTLE_STARS, STAR_COSMETICS, STAR_COSMETIC_BY_ID, SYNTH, SYNTH_COUNT, type Rarity, type SynthFrom } from './starshopDefs';
import * as repo from './starshopRepository';
import { poolOf } from './starshopService';
import type { CollectionBody, DismantleBody, SynthBody } from './starshopValidation';

/** 합성 결과로 나올 수 있는 외형: 희귀·에픽은 오라(에픽은 내 직업 에픽 스킨 포함), 유니크는 내 직업 유니크 스킨 */
function synthPool(to: Rarity, cls: string) {
  if (to === 'unique') return poolOf('skin', 'unique', cls);
  if (to === 'epic') return [...poolOf('aura', 'epic', cls), ...poolOf('skin', 'epic', cls)];
  return poolOf('aura', to, cls);
}

/** 그 등급의 여분(재료로 쓸 수 있는 것): 외형 id별 개수 */
function spareOf(copies: Map<string, number>, rarity: Rarity): Map<string, number> {
  const out = new Map<string, number>();
  for (const c of STAR_COSMETICS) {
    const n = copies.get(c.id) ?? 0;
    if (c.rarity === rarity && n > 0) out.set(c.id, n);
  }
  return out;
}

/**
 * 합성 times회. 한 번에 같은 등급 여분 4개를 넣는다(여분이 많은 외형부터). 성공하면 한 등급 위 외형 하나(아직 없는 것
 * 우선, 다 가졌으면 그 외형의 여분), 실패하면 넣은 것 중 하나를 여분으로 돌려준다. 연속 실패가 천장에 닿으면 다음은 성공
 */
export function synth(accountId: number, characterUuid: string, body: SynthBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/synth',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const from: SynthFrom = body.rarity;
      const rule = SYNTH[from];
      const tier = rule.to as 'rare' | 'epic' | 'unique';
      const wallet = await repo.lockWallet(db, accountId);
      const owned = await repo.ownedOf(db, accountId);
      const copies = await repo.copiesOf(db, accountId);
      const spare = spareOf(copies, from);
      const have = [...spare.values()].reduce((a, n) => a + n, 0);
      if (have < SYNTH_COUNT) {
        throw new AppError(422, '합성할 여분이 모자랍니다.', 'SYNTH_NOT_ENOUGH', { need: SYNTH_COUNT, have });
      }
      const pool = synthPool(rule.to, ctx.char.class);
      if (pool.length === 0) throw new AppError(422, '합성으로 얻을 수 있는 외형이 없습니다.', 'SYNTH_EMPTY');
      let fails = wallet.synthFail[tier];
      const logs: repo.SynthLogRow[] = [];
      for (let i = 0; i < body.times; i++) {
        const total = [...spare.values()].reduce((a, n) => a + n, 0);
        if (total < SYNTH_COUNT) break;
        // 재료 4개: 여분이 가장 많은 외형부터 하나씩
        const inputs: string[] = [];
        for (let k = 0; k < SYNTH_COUNT; k++) {
          const [id] = [...spare.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))[0]!;
          inputs.push(id);
          const left = (spare.get(id) ?? 0) - 1;
          if (left > 0) spare.set(id, left);
          else spare.delete(id);
        }
        for (const id of new Set(inputs)) {
          const n = inputs.filter((x) => x === id).length;
          if (!(await repo.takeCopies(db, accountId, id, n))) throw new AppError(409, '여분이 바뀌었습니다. 다시 시도해 주세요.', 'SYNTH_CHANGED');
        }
        const failsBefore = fails;
        const byPity = rule.pity > 0 && fails >= rule.pity;
        const success = byPity || getRng().int(0, 1000) < rule.permille;
        let result: string;
        if (success) {
          const fresh = pool.filter((c) => !owned.has(c.id));
          const pick = (fresh.length > 0 ? fresh : pool)[getRng().int(0, fresh.length > 0 ? fresh.length : pool.length)]!;
          result = pick.id;
          if (owned.has(pick.id)) await repo.addCopies(db, accountId, pick.id, 1);
          else {
            owned.add(pick.id);
            await repo.addCosmetic(db, accountId, pick.id, 'synth');
          }
          fails = 0;
        } else {
          result = inputs[getRng().int(0, inputs.length)]!;
          await repo.addCopies(db, accountId, result, 1);
          spare.set(result, (spare.get(result) ?? 0) + 1);
          fails += 1;
        }
        logs.push({ seq: i, fromRarity: from, inputs, success, byPity, resultItem: result, failsBefore, failsAfter: fails });
      }
      await repo.setSynthFail(db, accountId, tier, fails);
      await repo.insertSynthLog(db, accountId, requestId, logs);
      return {
        status: 200,
        data: {
          rarity: from,
          results: logs.map((l) => ({ success: l.success, by_pity: l.byPity, item_id: l.resultItem, rarity: l.success ? rule.to : from, inputs: l.inputs })),
          fails,
          pity: rule.pity,
        },
      };
    },
  });
}

/** 여분 분해: 고른 외형의 여분 count개를 별조각으로(등급별 값). 착용 권한(첫 1개)은 건드리지 않는다 */
export function dismantle(accountId: number, characterUuid: string, body: DismantleBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/dismantle',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const def = STAR_COSMETIC_BY_ID.get(body.item_id);
      if (!def) throw new AppError(404, '분해할 수 없는 외형입니다.', 'COSMETIC_UNKNOWN');
      await repo.lockWallet(db, accountId);
      if (!(await repo.takeCopies(db, accountId, def.id, body.count))) {
        throw new AppError(422, '분해할 여분이 모자랍니다.', 'DISMANTLE_NOT_ENOUGH');
      }
      const stars = DISMANTLE_STARS[def.rarity] * body.count;
      const balance = await repo.changeBalance(db, accountId, stars, 'dismantle', def.id, requestId);
      return { status: 200, data: { item_id: def.id, count: body.count, stars, balance } };
    },
  });
}

/** 컬렉션 등록: 세트의 외형을 모두 가졌을 때(최종 세트는 앞 세트를 모두 등록했을 때). 외형은 소모하지 않는다 */
export function registerCollection(accountId: number, characterUuid: string, body: CollectionBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/collection',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const def = COLLECTION_BY_ID.get(body.set_id);
      if (!def) throw new AppError(404, '알 수 없는 컬렉션입니다.', 'COLLECTION_UNKNOWN');
      await repo.lockWallet(db, accountId);
      const owned = await repo.ownedOf(db, accountId);
      const done = await repo.collectionsOf(db, accountId);
      if (done.has(def.id)) throw new AppError(409, '이미 등록한 컬렉션입니다.', 'COLLECTION_DONE');
      const missing = def.members.filter((m) => !owned.has(m));
      const missingSets = (def.requires ?? []).filter((s) => !done.has(s));
      if (missing.length > 0 || missingSets.length > 0) {
        throw new AppError(422, '아직 모으지 못한 외형이 있습니다.', 'COLLECTION_INCOMPLETE', { missing, missing_sets: missingSets });
      }
      await repo.addCollection(db, accountId, def.id);
      return { status: 200, data: { set_id: def.id, attack: def.attack, health: def.health } };
    },
  });
}
