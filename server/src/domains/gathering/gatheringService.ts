import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as charRepo from '../characters/characterRepository';
import type { EconCtx } from '../economy/economyContext';
import { AnomalyError, runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './gatheringRepository';
import type { ChestBody, DeliveryBody, GatherBody } from './gatheringValidation';

const NOT_FOUND = () => new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');

// ---------- 채집 ----------

export function gather(accountId: number, characterUuid: string, body: GatherBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/gathers',
    requestId,
    payload,
    handler: (ctx) => processGather(ctx, body),
  });
}

async function processGather(ctx: EconCtx, body: GatherBody) {
  const eco = getGameData().economy;
  const grace = getConfig().policy.gatherGraceSeconds;
  const kind = eco.mapExtra.get(body.map_id)?.nodes.get(body.node_id);
  if (!kind) {
    throw new AnomalyError(422, '알 수 없는 채집 노드입니다.', 'NODE_UNKNOWN', {
      kind: 'gather_node',
      severity: 2,
      detail: { map_id: body.map_id, node_id: body.node_id },
    });
  }
  const spec = eco.config.nodeKinds[kind];
  if (!spec) throw new Error(`노드 종류 ${kind} 설정이 없습니다`);

  const last = await repo.getLastGathered(ctx.client, ctx.char.id, body.node_id);
  if (last) {
    const readyAt = new Date(last.getTime() + spec.respawnSeconds * 1000);
    // 보고 지연 여유(grace): 서버는 보고를 받은 시점부터 재생을 센다
    if (ctx.now.getTime() < readyAt.getTime() - grace * 1000) {
      throw new AnomalyError(
        409,
        '아직 재생되지 않았습니다.',
        'NODE_NOT_READY',
        { kind: 'gather_early', severity: 1, detail: { node_id: body.node_id, ready_at: readyAt.toISOString() } },
        { ready_at: readyAt.toISOString() },
      );
    }
  }
  // 맵에 가지 않고 노드 id를 돌며 거두는 것을 막는 분당 상한
  const perMinute = getConfig().policy.gatherPerMinute;
  if ((await repo.countGatheredSince(ctx.client, ctx.char.id, new Date(ctx.now.getTime() - 60_000))) >= perMinute) {
    throw new AnomalyError(429, '채집이 너무 빠릅니다. 잠시 후 다시 시도하세요.', 'GATHER_RATE_LIMITED', {
      kind: 'gather_rate',
      severity: 2,
      detail: { node_id: body.node_id, limit: perMinute },
    });
  }
  await ctx.addItem('bag', spec.item, spec.amount, 'gather', body.node_id);
  await repo.upsertNodeState(ctx.client, ctx.char.id, body.node_id, body.map_id, ctx.now);
  return {
    status: 200,
    data: {
      granted: { item_key: spec.item, count: spec.amount },
      ready_at: new Date(ctx.now.getTime() + spec.respawnSeconds * 1000).toISOString(),
      delta: ctx.delta(),
    },
  };
}

/** 맵을 불러올 때 아직 재생 중인 노드만 */
export async function listCoolingNodes(accountId: number, characterUuid: string, mapId: string) {
  const eco = getGameData().economy;
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw NOT_FOUND();
  const map = getGameData().maps.get(mapId);
  const extra = eco.mapExtra.get(mapId);
  if (!map || map.instanced || !extra) throw new AppError(422, '노드를 조회할 수 없는 맵입니다.', 'MAP_INVALID');

  const now = getNow();
  const maxRespawn = Math.max(...Object.values(eco.config.nodeKinds).map((n) => n.respawnSeconds));
  const rows = await repo.listRecentNodes(getPool(), c.id, mapId, new Date(now.getTime() - maxRespawn * 1000));
  const cooling: { node_id: string; kind: string; ready_at: string }[] = [];
  for (const r of rows) {
    const kind = extra.nodes.get(r.node_id);
    const spec = kind ? eco.config.nodeKinds[kind] : undefined;
    if (!kind || !spec) continue;
    const readyAt = new Date(r.last_gathered_at.getTime() + spec.respawnSeconds * 1000);
    if (readyAt.getTime() > now.getTime()) cooling.push({ node_id: r.node_id, kind, ready_at: readyAt.toISOString() });
  }
  return { map_id: mapId, cooling };
}

// ---------- 상자 ----------

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

// ---------- 공사장 납품 ----------

export function deliver(accountId: number, characterUuid: string, body: DeliveryBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/deliveries',
    requestId,
    payload,
    handler: async (ctx) => {
      const site = getGameData().economy.config.deliverySites[body.site_id];
      if (!site) throw new AppError(422, '알 수 없는 납품처입니다.', 'SITE_UNKNOWN');
      const claimed = await repo.claimedAmong(ctx.client, ctx.char.id, site.requiresQuestClaimed);
      if (site.requiresQuestClaimed.some((q) => !claimed.has(q))) {
        throw new AppError(422, '아직 납품할 수 없습니다.', 'SITE_LOCKED');
      }
      const done = await repo.getDeliveries(ctx.client, ctx.char.id, body.site_id);
      if (site.items.every((i) => (done.get(i.itemKey) ?? 0) >= i.required)) {
        throw new AppError(409, '이미 완공되었습니다.', 'SITE_COMPLETE');
      }
      // C# DeliverMaterials: 재료마다 min(필요량 - 낸 양, 가방 수량)을 옮긴다
      let movedTotal = 0;
      for (const i of site.items) {
        const need = i.required - (done.get(i.itemKey) ?? 0);
        const move = Math.min(need, await ctx.stackCount('bag', i.itemKey));
        if (move <= 0) continue;
        await ctx.removeItem('bag', i.itemKey, move, 'delivery', body.site_id);
        await repo.addDelivery(ctx.client, ctx.char.id, body.site_id, i.itemKey, move, ctx.now);
        done.set(i.itemKey, (done.get(i.itemKey) ?? 0) + move);
        movedTotal += move;
      }
      if (movedTotal === 0) throw new AppError(422, '낼 재료가 없습니다.', 'NOTHING_TO_DELIVER');
      return {
        status: 200,
        data: {
          site_id: body.site_id,
          items: site.items.map((i) => ({
            item_key: i.itemKey,
            delivered: done.get(i.itemKey) ?? 0,
            required: i.required,
          })),
          complete: site.items.every((i) => (done.get(i.itemKey) ?? 0) >= i.required),
          delta: ctx.delta(),
        },
      };
    },
  });
}
