// 솔로 요일 던전: 입장(일일 횟수), 목록, 결과 검증, 카드 선택. 레이드와 파티는 4단계.
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import * as charRepo from '../characters/characterRepository';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as dungeonRepo from './dungeonRepository';
import { isOpenToday } from './dungeonRules';
import { settleResult } from './dungeonResult';
import type { EnterBody, PickBody, ResultBody } from './dungeonValidation';

const ONE_PLAYING = 'dungeon_runs_one_playing';

// ---------- GET /dungeons ----------

export async function listDungeons(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const eco = getGameData().economy;
  const now = getNow();
  const b = resetBoundaries(now);
  const [used, active, clears] = await Promise.all([
    dungeonRepo.countEntries(getPool(), c.id, new Date(b.dailyStartAt)),
    dungeonRepo.findPlayingRun(getPool(), c.id),
    dungeonRepo.clearSummary(getPool(), c.id),
  ]);
  const limit = eco.dungeons.dailyEntries;
  const best = new Map(clears.map((r) => [`${r.dungeon_id}:${r.difficulty}`, r.best_rank] as const));
  return {
    reset: { daily_start_at: b.dailyStartAt, next_daily_at: b.nextDailyAt },
    entries: { limit, used, left: Math.max(0, limit - used) },
    active_run: active
      ? {
          id: active.uuid,
          dungeon_id: active.dungeon_id,
          difficulty: active.difficulty,
          started_at: active.started_at.toISOString(),
        }
      : null,
    dungeons: [...eco.dungeons.byId.values()]
      .filter((d) => !d.isRaid)
      .map((d) => ({
        id: d.id,
        open_today: isOpenToday(eco, d, now),
        difficulties: eco.dungeons.difficulties.map((_x, i) => ({
          difficulty: i,
          unlocked: i === 0 || best.has(`${d.id}:${i - 1}`),
          cleared: best.has(`${d.id}:${i}`),
          best_rank: best.get(`${d.id}:${i}`) ?? null,
        })),
      })),
  };
}

// ---------- POST /dungeon-runs ----------

export function enter(accountId: number, characterUuid: string, body: EnterBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/dungeon-runs',
    requestId,
    payload,
    handler: (ctx) => processEnter(ctx, body),
  });
}

async function processEnter(ctx: EconCtx, body: EnterBody) {
  const eco = getGameData().economy;
  const stale = getConfig().policy.runStaleSeconds;
  const d = eco.dungeons.byId.get(body.dungeon_id);
  if (!d) throw new AppError(422, '알 수 없는 던전입니다.', 'DUNGEON_UNKNOWN');
  if (d.isRaid) throw new AppError(422, '레이드는 아직 열리지 않았습니다.', 'RAID_NOT_AVAILABLE');
  if (!isOpenToday(eco, d, ctx.now)) throw new AppError(422, '오늘은 열리지 않는 던전입니다.', 'DUNGEON_CLOSED_TODAY');

  if (body.difficulty > 0) {
    const clears = await dungeonRepo.clearSummary(ctx.client, ctx.char.id);
    if (!clears.some((r) => r.dungeon_id === d.id && r.difficulty === body.difficulty - 1)) {
      throw new AppError(422, '아직 입장할 수 없는 난이도입니다.', 'DIFFICULTY_LOCKED');
    }
  }

  const resetDay = new Date(resetBoundaries(ctx.now).dailyStartAt);
  const used = await dungeonRepo.countEntries(ctx.client, ctx.char.id, resetDay);
  const limit = eco.dungeons.dailyEntries;
  if (used >= limit) throw new AppError(422, '오늘 입장 횟수를 모두 사용했습니다.', 'NO_ENTRIES_LEFT');

  // 진행 중인 판: 오래 방치된 것만 닫는다(입장 횟수는 이미 썼다)
  const playing = await dungeonRepo.findPlayingRun(ctx.client, ctx.char.id);
  if (playing) {
    if (ctx.now.getTime() - playing.started_at.getTime() <= stale * 1000) {
      throw new AppError(409, '진행 중인 던전이 있습니다.', 'RUN_ACTIVE', { run_id: playing.uuid });
    }
    await dungeonRepo.abandonRun(ctx.client, playing.id, ctx.now);
  }

  let run;
  try {
    run = await dungeonRepo.insertRun(ctx.client, ctx.char.id, d.id, body.difficulty, resetDay, ctx.now);
  } catch (err) {
    if (isUniqueViolation(err, ONE_PLAYING)) throw new AppError(409, '진행 중인 던전이 있습니다.', 'RUN_ACTIVE');
    throw err;
  }
  return {
    status: 201,
    data: {
      run: {
        id: run.uuid,
        dungeon_id: run.dungeon_id,
        difficulty: run.difficulty,
        party_size: run.party_size,
        started_at: run.started_at.toISOString(),
      },
      entries_left: Math.max(0, limit - used - 1),
    },
  };
}

// ---------- 결과 ----------

export function reportResult(
  accountId: number,
  characterUuid: string,
  runUuid: string,
  body: ResultBody,
): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/dungeon-runs/:run_id/result',
    requestId,
    payload: { run_id: runUuid, ...payload },
    handler: (ctx) => settleResult(ctx, runUuid, body),
  });
}

// ---------- 카드 선택 ----------

export function pickCard(
  accountId: number,
  characterUuid: string,
  runUuid: string,
  body: PickBody,
): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/dungeon-runs/:run_id/cards/pick',
    requestId: body.request_id,
    payload: { run_id: runUuid, index: body.index },
    handler: async (ctx) => {
      const run = await dungeonRepo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
      if (!run) throw new AppError(404, '던전 기록을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
      if (run.state !== 'cleared' || !run.cards) throw new AppError(422, '고를 카드가 없습니다.', 'NO_CARDS');
      if (run.card_picked !== null) throw new AppError(409, '이미 카드를 골랐습니다.', 'CARD_ALREADY_PICKED');
      const ttl = getConfig().policy.dungeonCardTtlHours * 3_600_000;
      if (ctx.now.getTime() > (run.ended_at as Date).getTime() + ttl) {
        throw new AppError(410, '카드를 고를 수 있는 시간이 지났습니다.', 'CARDS_EXPIRED');
      }
      const card = run.cards[body.index];
      if (!card) throw new AppError(422, '고를 카드가 없습니다.', 'NO_CARDS');

      if (card.item_key === 'gold') await ctx.changeGold(card.count, 'dungeon_card', run.uuid);
      else await ctx.addItem('bag', card.item_key, card.count, 'dungeon_card', run.uuid);
      await dungeonRepo.setCardPicked(ctx.client, run.id, body.index, ctx.now);
      // 나머지 카드는 동료 AI가 뒤집는 연출용 공개일 뿐 지급되지 않는다
      return { status: 200, data: { card, cards: run.cards, delta: ctx.delta() } };
    },
  });
}
