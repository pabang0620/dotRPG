import { getGameData } from '../../gamedata/loader';
import type { EconomyData, QuestRule } from '../../gamedata/economyData';
import { isUniqueViolation } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { clearCounts } from '../dungeons/dungeonRepository';
import type { EconCtx } from '../economy/economyContext';
import { AnomalyError, runEconomy, type StoredResult } from '../economy/economyService';
import { getDeliveries } from '../gathering/gatheringRepository';
import * as repo from './questRepository';
import type { ClaimBody } from './questValidation';

const QUEST_PK = 'quest_claims_pkey';

export function claimQuest(
  accountId: number,
  characterUuid: string,
  questId: string,
  body: ClaimBody,
): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/quests/:quest_id/claim',
    requestId: body.request_id,
    payload: { quest_id: questId },
    handler: (ctx) => processClaim(ctx, questId),
  });
}

const denied = (code: string, message: string, questId: string, extra: Record<string, unknown>, status = 422) =>
  new AnomalyError(status, message, code, { kind: 'quest_denied', severity: 1, detail: { quest_id: questId, code, ...extra } }, extra);

/**
 * 목표 수치: 이 퀘스트가 요구하는 목표(몬스터·던전)마다, 이미 청구한 퀘스트들과 이 퀘스트의 같은 목표 합.
 * (C#은 퀘스트마다 0부터 세므로 같거나 약한 검사)
 */
function cumulative(
  eco: EconomyData,
  claimed: Set<string>,
  rule: QuestRule,
  pick: (q: QuestRule) => [string, number][],
): Map<string, number> {
  const own = new Set(pick(rule).map(([k]) => k));
  const out = new Map<string, number>();
  const add = (q: QuestRule) => {
    for (const [k, n] of pick(q)) if (own.has(k)) out.set(k, (out.get(k) ?? 0) + n);
  };
  for (const id of claimed) {
    const q = eco.quests.get(id);
    if (q) add(q);
  }
  add(rule);
  return out;
}

async function siteComplete(ctx: EconCtx, eco: EconomyData, siteId: string): Promise<boolean> {
  const site = eco.config.deliverySites[siteId];
  if (!site) return false;
  const done = await getDeliveries(ctx.client, ctx.char.id, siteId);
  return site.items.every((i) => (done.get(i.itemKey) ?? 0) >= i.required);
}

async function processClaim(ctx: EconCtx, questId: string) {
  const eco = getGameData().economy;
  const rule = eco.quests.get(questId);
  if (!rule) throw new AppError(422, '알 수 없는 퀘스트입니다.', 'QUEST_UNKNOWN');

  const claimed = await repo.allClaimed(ctx.client, ctx.char.id);
  if (claimed.has(questId)) throw new AppError(409, '이미 보상을 받은 퀘스트입니다.', 'QUEST_ALREADY_CLAIMED');

  const missing = rule.requires.filter((r) => !claimed.has(r));
  if (missing.length > 0) throw denied('QUEST_PREREQ', '선행 퀘스트를 먼저 완료해야 합니다.', questId, { missing });
  if (rule.minLevel > ctx.level) {
    throw denied('QUEST_LEVEL', '레벨이 모자랍니다.', questId, { need: rule.minLevel, have: ctx.level });
  }
  if (rule.requiresFlags.length > 0) {
    const flags = await repo.storyFlags(ctx.client, ctx.char.id);
    const lack = rule.requiresFlags.filter((f) => !flags.has(f));
    if (lack.length > 0) throw denied('QUEST_FLAGS', '필요한 진행 조건이 없습니다.', questId, { missing: lack });
  }

  const obj = rule.objectives;
  // 레이드 목표는 4단계 전까지 거절한다
  if (obj.raidNeeds.length > 0) {
    throw new AppError(422, '아직 보상을 받을 수 없는 퀘스트입니다.', 'QUEST_OBJECTIVE_UNSUPPORTED', {
      types: ['raid'],
    });
  }
  const notDone = (objective: Record<string, unknown>) =>
    denied('QUEST_NOT_DONE', '아직 완료 조건을 채우지 못했습니다.', questId, { objective });

  // 서버가 검증할 수 있는 목표: level, kill, collect+consume, 공방 완공 플래그, dungeon
  if (obj.levelNeed > ctx.level) throw notDone({ type: 'level', need: obj.levelNeed, have: ctx.level });

  const killNeeds = cumulative(eco, claimed, rule, (q) => Object.entries(q.objectives.killNeeds));
  if (killNeeds.size > 0) {
    const have = await repo.killTotals(ctx.client, ctx.char.id);
    for (const [m, need] of killNeeds) {
      if ((have.get(m) ?? 0) < need) throw notDone({ type: 'kill', monster_id: m, need, have: have.get(m) ?? 0 });
    }
  }

  for (const c of obj.consumes) {
    const have = await ctx.stackCount('bag', c.itemKey);
    if (have < c.count) throw notDone({ type: 'collect', item_key: c.itemKey, need: c.count, have });
  }

  for (const f of obj.flagNeeds) {
    // "<납품처>_built" 플래그는 납품 기록(site_deliveries)으로 직접 확인한다. 그 밖의 플래그는 검증할 수 없다
    const site = f.endsWith('_built') ? f.slice(0, -'_built'.length) : null;
    if (site && eco.config.deliverySites[site] && !(await siteComplete(ctx, eco, site))) {
      throw notDone({ type: 'flag', flag: f });
    }
  }

  const dungeonNeeds = cumulative(eco, claimed, rule, (q) => q.objectives.dungeonNeeds.map((d) => [d.target, d.count]));
  if (dungeonNeeds.size > 0) {
    const clears = await clearCounts(ctx.client, ctx.char.id);
    for (const [target, need] of dungeonNeeds) {
      const have = target === '*' ? clears.total : (clears.byDungeon.get(target) ?? 0);
      if (have < need) throw notDone({ type: 'dungeon', target, need, have });
    }
  }
  // talk, cutscene, interact, reach 등은 서버가 확인할 방법이 없다(수용된 위험, api 5절)

  // ---- 지급 ----
  const consumed: { item_key: string; count: number }[] = [];
  for (const c of obj.consumes) {
    await ctx.removeItem('bag', c.itemKey, c.count, 'quest_consume', questId);
    consumed.push({ item_key: c.itemKey, count: c.count });
  }
  const r = rule.reward;
  const { leveledUp } = await ctx.grantXp(r.xp, 'quest_reward', questId);
  if (r.gold > 0) await ctx.changeGold(r.gold, 'quest_reward', questId);
  const items = r.items.map((i) => ({ item_key: i.id, count: i.count }));
  for (const i of items) await ctx.addItem('bag', i.item_key, i.count, 'quest_reward', questId);

  const reward = { xp: r.xp, gold: r.gold, items, max_health: r.maxHealth, set_flags: r.setFlags };
  try {
    await repo.insertClaim(ctx.client, ctx.char.id, questId, { ...reward, consumed }, ctx.requestId, ctx.now);
  } catch (err) {
    if (isUniqueViolation(err, QUEST_PK)) throw new AppError(409, '이미 보상을 받은 퀘스트입니다.', 'QUEST_ALREADY_CLAIMED');
    throw err;
  }

  return {
    status: 200,
    data: {
      quest_id: questId,
      reward,
      consumed,
      bonus_max_health: await repo.sumBonusMaxHealth(ctx.client, ctx.char.id),
      leveled_up: leveledUp,
      delta: ctx.delta(),
    },
  };
}
