// 시험 도구(Docs/PLAN_TEST_TOOLS_2026-10-29.md): 시험 서버(DEPLOY_STAGE=test)에서 개발 로그인 계정만 쓴다.
// 메인 퀘스트 밀기: 다음 메인 퀘스트의 조건(레벨·처치·던전·레이드·납품 재료)을 scripts/test-checkpoint.mjs 방식으로 채우고
// 실제 청구 경로(processClaim)로 보상을 받는다. 경험치 밀기: 다음 레벨(또는 상한)까지 필요한 경험치를 test_boost로 넣는다.
import { getConfig } from '../../config/env';
import { chapter3Open, isChapter3, levelCap } from '../../gamedata/chapterGate';
import type { EconomyData, QuestRule } from '../../gamedata/economyData';
import { getGameData } from '../../gamedata/loader';
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getGrant } from '../characters/careerGrantRepository';
import { clearCounts } from '../dungeons/dungeonRepository';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { cumulative, processClaim } from '../quests/questService';
import * as questRepo from '../quests/questRepository';
import * as repo from './testToolsRepository';
import type { LevelBody, MainQuestBody } from './testToolsValidation';

/** 원장 ref(경험치·아이템 test_boost) */
const REF = 'test-tools';
const CAREER_FLAG = 'career_promoted';
const QUEST_COMPLETED = 4;
/** 챕터 끝까지 한 번에 완료할 최대 개수(메인 퀘스트는 60개 남짓) */
const MAX_QUESTS = 120;

export const testToolsEnabled = (): boolean => getConfig().deployStage === 'test';

/** 운영(live)에서는 라우트를 등록하지 않는다. 직접 불려도 거절한다 */
function assertEnabled(): void {
  if (!testToolsEnabled()) throw new AppError(403, '시험 서버에서만 쓸 수 있습니다.', 'TEST_TOOLS_DISABLED');
}

async function assertDevAccount(accountId: number): Promise<void> {
  if (!(await repo.isDevAccount(getPool(), accountId))) {
    throw new AppError(403, '개발 로그인 계정만 쓸 수 있습니다.', 'TEST_TOOLS_DEV_ONLY');
  }
}

/** level 레벨 xp 경험치에서 target 레벨 0경험치까지 필요한 양(scripts/test-level.mjs와 같은 계산) */
export function xpToReach(xpToNext: number[], level: number, xp: number, target: number): number {
  if (target <= level) return 0;
  let delta = (xpToNext[level - 1] as number) - xp;
  for (let l = level + 1; l < target; l++) delta += xpToNext[l - 1] as number;
  return Math.max(0, delta);
}

async function raiseLevel(ctx: EconCtx, target: number): Promise<void> {
  const p = getGameData().economy.progression;
  const amount = xpToReach(p.xpToNext, ctx.level, ctx.xp, Math.min(target, levelCap(p.maxLevel)));
  if (amount > 0) await ctx.grantXp(amount, 'test_boost', REF);
}

// ---------------- 경험치 밀기 ----------------

export function pushLevel(accountId: number, characterUuid: string, body: LevelBody): Promise<StoredResult> {
  assertEnabled();
  return assertDevAccount(accountId).then(() =>
    runEconomy({
      accountId,
      characterUuid,
      endpoint: 'POST /characters/:uuid/test-tools/level',
      requestId: body.request_id,
      payload: { mode: body.mode },
      handler: async (ctx) => {
        const cap = levelCap(getGameData().economy.progression.maxLevel);
        if (ctx.level >= cap) throw new AppError(409, '이미 최고 레벨입니다.', 'TEST_LEVEL_MAX', { level: ctx.level, cap });
        const before = ctx.level;
        await raiseLevel(ctx, body.mode === 'one' ? ctx.level + 1 : cap);
        return { status: 200, data: { level_before: before, level: ctx.level, cap, delta: ctx.delta() } };
      },
    }),
  );
}

// ---------------- 메인 퀘스트 밀기 ----------------

const stepCount = (rule: QuestRule): number => Math.max(1, Number((rule as { stepCount?: number }).stepCount ?? 1));

/** 열린 챕터의 메인 퀘스트 중 아직 청구하지 않았고 선행을 모두 청구한 첫 퀘스트(데이터 순서) */
function nextMainQuest(eco: EconomyData, claimed: Set<string>): QuestRule | null {
  for (const q of eco.quests.values()) {
    if (q.kind !== 'main' || claimed.has(q.id)) continue;
    if (isChapter3('quest', q.id) && !chapter3Open()) continue;
    if (q.requires.every((r) => claimed.has(r))) return q;
  }
  return null;
}

/** 서버 청구 검사가 보는 조건을 채운다: 레벨, 처치 누적, 던전·레이드 클리어, 납품 재료, 진행 플래그 */
async function fillRequirements(ctx: EconCtx, eco: EconomyData, claimed: Set<string>, rule: QuestRule, flags: Set<string>): Promise<void> {
  const obj = rule.objectives;
  await raiseLevel(ctx, Math.max(rule.minLevel, obj.levelNeed));

  for (const [monster, need] of cumulative(eco, claimed, rule, (q) => Object.entries(q.objectives.killNeeds))) {
    await repo.raiseKills(ctx.client, ctx.char.id, monster, need);
  }
  const past = new Date(ctx.now.getTime() - 3 * 86_400_000);
  const dungeonNeeds = cumulative(eco, claimed, rule, (q) => q.objectives.dungeonNeeds.map((d) => [d.target, d.count]));
  if (dungeonNeeds.size > 0 || obj.raidNeeds.length > 0) {
    const clears = await clearCounts(ctx.client, ctx.char.id);
    for (const [target, need] of dungeonNeeds) {
      const have = target === '*' ? clears.total : (clears.byDungeon.get(target) ?? 0);
      if (need > have) await repo.insertClears(ctx.client, ctx.char.id, target === '*' ? 'gold_vein' : target, need - have, past);
    }
    for (const r of obj.raidNeeds) {
      const have = clears.byDungeon.get(r.target) ?? 0;
      if (r.count > have) await repo.insertClears(ctx.client, ctx.char.id, r.target, r.count - have, past);
    }
  }
  for (const c of obj.consumes) {
    const have = await ctx.stackCount('bag', c.itemKey);
    if (have < c.count) await ctx.addItem('bag', c.itemKey, c.count - have, 'test_boost', REF);
  }
  // 선행 플래그는 청구 검사가 character_state.story_flags에서 읽는다
  const lack = rule.requiresFlags.filter((f) => !flags.has(f));
  if (lack.length > 0) {
    for (const f of lack) flags.add(f);
    const st = await repo.questState(ctx.client, ctx.char.id);
    await repo.saveQuestState(ctx.client, ctx.char.id, st.quests, [...flags]);
  }
}

async function pushQuests(ctx: EconCtx, mode: MainQuestBody['mode']) {
  const eco = getGameData().economy;
  const state = await repo.questState(ctx.client, ctx.char.id);
  const flags = new Set(state.storyFlags);
  const completed: string[] = [];
  let stopped: { code: string; quest_id: string } | null = null;
  const limit = mode === 'one' ? 1 : MAX_QUESTS;

  while (completed.length < limit) {
    const claimed = await questRepo.allClaimed(ctx.client, ctx.char.id);
    const rule = nextMainQuest(eco, claimed);
    if (!rule) break;
    // 전직은 플레이어가 고르는 것이라 대신하지 않는다
    if (rule.objectives.flagNeeds.includes(CAREER_FLAG) && !(await getGrant(ctx.client, ctx.char.id))) {
      stopped = { code: 'TEST_NEEDS_CAREER', quest_id: rule.id };
      break;
    }
    // 레벨 상한(챕터 3 잠금이면 40) 위의 퀘스트는 밀 수 없다
    if (Math.max(rule.minLevel, rule.objectives.levelNeed) > levelCap(eco.progression.maxLevel)) {
      stopped = { code: 'TEST_LEVEL_CAP', quest_id: rule.id };
      break;
    }
    await fillRequirements(ctx, eco, claimed, rule, flags);
    await processClaim(ctx, rule.id);
    completed.push(rule.id);
    // 진행 플래그(workshop_built 등)도 남긴다. 전직 여부는 character_career가 기록하므로 넣지 않는다
    for (const f of [...rule.reward.setFlags, ...rule.objectives.flagNeeds]) if (f !== CAREER_FLAG) flags.add(f);
  }

  if (completed.length === 0) {
    if (stopped?.code === 'TEST_NEEDS_CAREER') {
      throw new AppError(422, '전직을 먼저 마쳐야 다음 메인 퀘스트를 진행할 수 있습니다.', 'TEST_NEEDS_CAREER', stopped);
    }
    throw new AppError(409, '진행할 수 있는 메인 퀘스트가 없습니다.', 'TEST_NO_MAIN_QUEST', stopped ?? undefined);
  }

  // 퀘스트 상태(완료, 마지막 단계)와 이야기 플래그를 서버 상태에도 남긴다(클라이언트도 응답으로 같은 값을 적용한다)
  const done = new Set(completed);
  const quests = state.quests.filter((q) => !done.has(q.id));
  const rows = completed.map((id) => ({ id, step: stepCount(eco.quests.get(id) as QuestRule), counts: [], status: QUEST_COMPLETED }));
  await repo.saveQuestState(ctx.client, ctx.char.id, [...quests, ...rows], [...flags]);

  return {
    status: 200,
    data: {
      completed,
      stopped,
      quests: rows,
      story_flags: [...flags],
      bonus_max_health: await questRepo.sumBonusMaxHealth(ctx.client, ctx.char.id),
      delta: ctx.delta(),
    },
  };
}

export function pushMainQuest(accountId: number, characterUuid: string, body: MainQuestBody): Promise<StoredResult> {
  assertEnabled();
  return assertDevAccount(accountId).then(() =>
    runEconomy({
      accountId,
      characterUuid,
      endpoint: 'POST /characters/:uuid/test-tools/main-quest',
      requestId: body.request_id,
      payload: { mode: body.mode },
      handler: (ctx) => pushQuests(ctx, body.mode),
    }),
  );
}
