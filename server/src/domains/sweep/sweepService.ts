// 던전 소탕(S1~S3), 클리어권 구매(T1), 주간 활동 보상(T2). 설계: Docs/server/phase10_sweep_mail.md 4~5절.
// 재화(골드·경험치·아이템·클리어권)가 바뀌는 경로는 모두 runEconomy(캐릭터 행 잠금, request_log 멱등성) 안에서
// 계정 행을 잠근 뒤 바꾸고 원장을 남긴다. 금액·확률·보상량·횟수·시간은 요청에서 받지 않는다.
import { randomUUID } from 'node:crypto';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import type { DungeonDef } from '../../gamedata/economyData';
import { getGameData } from '../../gamedata/loader';
import type { SweepData } from '../../gamedata/sweepData';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { metrics } from '../../ops/metrics';
import { getRng } from '../../utils/rng';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { assertNoHold } from '../antiabuse/holds';
import * as holdsRepo from '../antiabuse/holdsRepository';
import { assertActionPresence } from '../antiabuse/killPresence';
import * as charRepo from '../characters/characterRepository';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { diffOf, isOpenToday, RANK_NAMES, type DiffNumbers } from '../dungeons/dungeonRules';
import type { EconCtx } from '../economy/economyContext';
import { AnomalyError, runEconomy, type EconResult, type StoredResult } from '../economy/economyService';
import * as repo from './sweepRepository';
import { rollSweepCard, sweepXp, ticketUnitPrice } from './sweepRules';
import * as wallet from './ticketWallet';
import type { Lot, TicketView } from './ticketWallet';
import * as weekly from './weeklyCounter';
import type { SweepBuyBody, SweepClaimBody, SweepRunBody } from './sweepValidation';

/** 기능 플래그가 꺼져 있으면 새 경로는 모두 503(데이터 파일이 없는 경우 포함) */
export function requireSweep(): SweepData {
  const sweep = getGameData().sweep;
  if (!getConfig().sweep.enabled || !sweep) throw new AppError(503, '아직 준비 중인 기능입니다.', 'FEATURE_DISABLED');
  return sweep;
}

export type BlockCode = 'CLOSED_TODAY' | 'NOT_CLEARED' | 'RANK_LOW' | 'LEVEL_TOO_LOW';

interface Eligibility {
  block: BlockCode | null;
  bestRank: number | null;
  needLevel: number;
}

/** 4.1의 5~7번(던전 조건). 레이드가 아닌 던전만 부른다 */
function eligibility(d: DungeonDef, difficulty: number, diff: DiffNumbers, level: number, best: Map<string, number>, sweep: SweepData, now: Date): Eligibility {
  const eco = getGameData().economy;
  const rank = best.get(`${d.id}:${difficulty}`) ?? null;
  const base = { bestRank: rank, needLevel: diff.recommendedLevel };
  if (!isOpenToday(eco, d, now)) return { block: 'CLOSED_TODAY', ...base };
  if (rank === null) return { block: 'NOT_CLEARED', ...base };
  if (rank > sweep.minRank) return { block: 'RANK_LOW', ...base };
  // 직접 입장의 파티 레벨 여유(partyMinLevelSlack)는 적용하지 않는다
  if (level < diff.recommendedLevel) return { block: 'LEVEL_TOO_LOW', ...base };
  return { block: null, ...base };
}

function weeklyActivityView(sweep: SweepData, progress: number, claimed: boolean) {
  const goal = sweep.weekly.directClears;
  return {
    goal,
    progress: Math.min(goal, progress),
    reward_tickets: sweep.weekly.rewardTickets,
    claimed,
    claimable: !claimed && progress >= goal,
  };
}

function shopView(sweep: SweepData, used: number, unitPrice: number) {
  const limit = sweep.shop.weeklyLimit;
  return { weekly_limit: limit, weekly_used: used, weekly_left: Math.max(0, limit - used), unit_price: unitPrice };
}

// ---------- S1 GET /characters/{uuid}/sweep ----------

export async function sweepStatus(accountId: number, characterUuid: string) {
  const sweep = requireSweep();
  const db = getPool();
  const c = await charRepo.findOwnedAlive(db, accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const eco = getGameData().economy;
  const now = getNow();
  const b = resetBoundaries(now);
  const weekStart = weekly.weekStartOf(now);
  const [used, best, lots, buyUsed, directClears, claimedRow, maxLevel, hold] = await Promise.all([
    dungeonRepo.countEntries(db, c.id, new Date(b.dailyStartAt)),
    repo.bestRanks(db, c.id),
    wallet.readLive(db, accountId, now),
    weekly.readUsed(db, accountId, weekStart, 'sweep_buy'),
    weekly.readUsed(db, accountId, weekStart, 'direct_clear'),
    weekly.readUsed(db, accountId, weekStart, 'activity_claim'),
    repo.accountMaxLevel(db, accountId),
    holdsRepo.findBlocking(db, accountId, c.id),
  ]);
  const limit = eco.dungeons.dailyEntries;
  return {
    server_time: now.toISOString(),
    reset: { daily_start_at: b.dailyStartAt, next_daily_at: b.nextDailyAt, weekly_start_at: b.weeklyStartAt, next_weekly_at: b.nextWeeklyAt },
    tickets: wallet.viewOf(lots),
    entries: { limit, used, left: Math.max(0, limit - used) },
    shop: shopView(sweep, buyUsed, ticketUnitPrice(sweep, maxLevel)),
    weekly_activity: weeklyActivityView(sweep, directClears, claimedRow > 0),
    hold: hold !== null,
    dungeons: [...eco.dungeons.byId.values()]
      .filter((d) => !d.isRaid)
      .map((d) => ({
        id: d.id,
        open_today: isOpenToday(eco, d, now),
        difficulties: eco.dungeons.difficulties.map((_x, i) => {
          const diff = diffOf(eco, d, i) as DiffNumbers;
          const e = eligibility(d, i, diff, c.level, best, sweep, now);
          return {
            difficulty: i,
            can_sweep: e.block === null,
            block: e.block,
            best_rank: e.bestRank,
            need_level: e.needLevel,
            xp: sweepXp(d, diff, i, sweep),
          };
        }),
      })),
  };
}

// ---------- S2, S3 소탕 ----------

const DENIED = (code: string, message: string, detail: Record<string, unknown>, extra?: Record<string, unknown>) =>
  new AnomalyError(422, message, code, { kind: 'sweep_denied', severity: 1, detail: { code, ...detail } }, extra);

export function runSweep(accountId: number, characterUuid: string, body: SweepRunBody, all: boolean): Promise<StoredResult> {
  requireSweep();
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: all ? 'POST /characters/:uuid/sweep/run-all' : 'POST /characters/:uuid/sweep/run',
    requestId,
    payload,
    handler: (ctx) => processSweep(ctx, body, all),
  });
}

async function processSweep(ctx: EconCtx, body: SweepRunBody, all: boolean): Promise<EconResult> {
  const sweep = requireSweep();
  const eco = getGameData().economy;
  const acct = ctx.char.accountId;
  // 4.1 검사 순서: 2 정지 -> 3 프레즌스 -> 4 진행 중인 판 -> 5 던전 -> 6 클리어 기록·등급 -> 7 레벨 -> 8 입장 -> 9 클리어권
  await assertNoHold(ctx.client, acct, ctx.char.id);
  await assertActionPresence(ctx, 'sweep', null);

  const playing = await dungeonRepo.findPlayingRun(ctx.client, ctx.char.id);
  if (playing && ctx.now.getTime() - playing.started_at.getTime() <= getConfig().policy.runStaleSeconds * 1000) {
    throw new AppError(409, '진행 중인 던전이 있습니다.', 'RUN_ACTIVE', { run_id: playing.uuid });
  }
  if (await dungeonRepo.inPartyRun(ctx.client, ctx.char.id)) throw new AppError(409, '파티 판에 참여 중입니다.', 'IN_PARTY_RUN');

  const d = eco.dungeons.byId.get(body.dungeon_id);
  if (!d) throw new AppError(422, '알 수 없는 던전입니다.', 'DUNGEON_UNKNOWN');
  if (d.isRaid) throw DENIED('SWEEP_NOT_ALLOWED', '소탕할 수 없는 던전입니다.', { dungeon_id: d.id });
  const diff = diffOf(eco, d, body.difficulty);
  if (!diff) throw new AppError(422, '알 수 없는 난이도입니다.', 'DUNGEON_UNKNOWN');
  const best = await repo.bestRanks(ctx.client, ctx.char.id);
  const e = eligibility(d, body.difficulty, diff, ctx.level, best, sweep, ctx.now);
  switch (e.block) {
    case 'CLOSED_TODAY':
      throw new AppError(422, '오늘은 열리지 않는 던전입니다.', 'DUNGEON_CLOSED_TODAY');
    case 'NOT_CLEARED':
      throw DENIED('SWEEP_NOT_CLEARED', '이 난이도를 직접 클리어한 기록이 없습니다.', { dungeon_id: d.id, difficulty: body.difficulty });
    case 'RANK_LOW':
      throw DENIED('SWEEP_RANK_LOW', `${RANK_NAMES[sweep.minRank]}등급 이상으로 클리어한 기록이 필요합니다.`, { dungeon_id: d.id, difficulty: body.difficulty, best_rank: e.bestRank }, {
        need_rank: RANK_NAMES[sweep.minRank],
        best_rank: RANK_NAMES[e.bestRank as number] ?? null,
      });
    case 'LEVEL_TOO_LOW':
      throw DENIED('LEVEL_TOO_LOW', '레벨이 부족합니다.', { dungeon_id: d.id, difficulty: body.difficulty, need: e.needLevel, have: ctx.level }, { need: e.needLevel, have: ctx.level });
    default:
      break;
  }

  const resetDay = new Date(resetBoundaries(ctx.now).dailyStartAt);
  const limit = eco.dungeons.dailyEntries;
  const used = await dungeonRepo.countEntries(ctx.client, ctx.char.id, resetDay);
  const entriesLeft = limit - used;
  if (entriesLeft <= 0) throw new AppError(422, '오늘 입장 횟수를 모두 사용했습니다.', 'NO_ENTRIES_LEFT');

  // 락 순서 ②: 캐릭터 행(runEconomy) 다음에 계정 행, 그다음 로트 행
  await wallet.lockAccount(ctx.client, acct);
  const lots = await wallet.lockLive(ctx.client, acct, ctx.now);
  const ticketTotal = lots.reduce((a, l) => a + l.remaining, 0);
  if (ticketTotal <= 0) throw new AppError(422, '클리어권이 없습니다.', 'NO_TICKET');

  const n = all ? Math.min(entriesLeft, ticketTotal) : 1;
  const sweeps: Record<string, unknown>[] = [];
  let totalXp = 0;
  const actor = { accountId: acct, characterId: ctx.char.id, requestId: ctx.requestId, now: ctx.now };
  for (let i = 0; i < n; i++) {
    const id = randomUUID();
    const lot = nextLot(lots);
    if (!lot || !(await wallet.consumeOne(ctx.client, actor, lot, id))) throw new AppError(422, '클리어권이 없습니다.', 'NO_TICKET');
    const xp = sweepXp(d, diff, body.difficulty, sweep);
    const g = await ctx.grantXp(xp, 'dungeon_sweep', id);
    const card = rollSweepCard(eco, d, diff, ctx.char.class, getRng(), sweep);
    if (card.item_key === 'gold') await ctx.changeGold(card.count, 'dungeon_card', id);
    else await ctx.addItem('bag', card.item_key, card.count, 'dungeon_card', id);
    await repo.insertSweep(ctx.client, {
      uuid: id,
      characterId: ctx.char.id,
      dungeonId: d.id,
      difficulty: body.difficulty,
      resetDay,
      lotId: lot.id,
      xpGranted: g.granted,
      card,
      requestId: ctx.requestId,
    });
    totalXp += g.granted;
    sweeps.push({ id, dungeon_id: d.id, difficulty: body.difficulty, xp: g.granted, leveled_up: g.leveledUp, card, ticket: lot.kind });
  }
  const data: Record<string, unknown> = {
    sweeps,
    summary: { count: n, total_xp: totalXp },
    entries: { limit, used: used + n, left: entriesLeft - n },
    tickets: wallet.viewOf(lots),
    delta: ctx.delta(),
  };
  if (all) data.limited_by = entriesLeft < ticketTotal ? 'entries' : ticketTotal < entriesLeft ? 'tickets' : null;
  return { status: 201, data };
}

/** 만료가 가까운 이벤트 로트부터, 일반 로트는 마지막(lockLive의 정렬 그대로) */
const nextLot = (lots: Lot[]): Lot | undefined => lots.find((l) => l.remaining > 0);

// ---------- T1 클리어권 구매 ----------

export function buyTickets(accountId: number, characterUuid: string, body: SweepBuyBody): Promise<StoredResult> {
  requireSweep();
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/sweep/tickets/buy',
    requestId,
    payload,
    handler: async (ctx): Promise<EconResult> => {
      const sweep = requireSweep();
      const acct = ctx.char.accountId;
      await assertNoHold(ctx.client, acct, ctx.char.id);
      await wallet.lockAccount(ctx.client, acct);
      const weekStart = weekly.weekStartOf(ctx.now);
      const limit = sweep.shop.weeklyLimit;
      const usedBefore = await weekly.readUsed(ctx.client, acct, weekStart, 'sweep_buy');
      if (usedBefore + body.count > limit) {
        metrics.sweepBuyLimitHits++;
        throw new AppError(422, '이번 주 구매 한도를 넘었습니다.', 'WEEKLY_LIMIT', { limit, used: usedBefore });
      }
      // 가격은 서버가 계산한다: 기본가 x (장비 단계(계정 최고 레벨, 삭제한 캐릭터 포함) + 1)
      const unit = ticketUnitPrice(sweep, await repo.accountMaxLevel(ctx.client, acct));
      const total = unit * body.count;
      if (ctx.gold < total) throw new AppError(422, '골드가 모자랍니다.', 'NOT_ENOUGH_GOLD', { need: total, have: ctx.gold });
      await ctx.changeGold(-total, 'sweep_ticket_buy', 'sweep_ticket');
      await wallet.addNormal(ctx.client, { accountId: acct, characterId: ctx.char.id, requestId: ctx.requestId, now: ctx.now }, body.count, 'shop_buy', ctx.requestId);
      if (!(await weekly.addBuy(ctx.client, acct, weekStart, body.count, limit))) {
        throw new AppError(422, '이번 주 구매 한도를 넘었습니다.', 'WEEKLY_LIMIT', { limit, used: usedBefore });
      }
      const lots = await wallet.readLive(ctx.client, acct, ctx.now);
      return {
        status: 200,
        data: {
          count: body.count,
          unit_price: unit,
          total,
          tickets: wallet.viewOf(lots),
          shop: shopView(sweep, usedBefore + body.count, unit),
          delta: ctx.delta(),
        },
      };
    },
  });
}

// ---------- T2 주간 활동 보상 ----------

export function claimWeekly(accountId: number, characterUuid: string, body: SweepClaimBody): Promise<StoredResult> {
  requireSweep();
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/sweep/weekly/claim',
    requestId: body.request_id,
    payload: {},
    handler: async (ctx): Promise<EconResult> => {
      const sweep = requireSweep();
      const acct = ctx.char.accountId;
      await assertNoHold(ctx.client, acct, ctx.char.id);
      const acctUuid = await wallet.lockAccount(ctx.client, acct);
      const weekStart = weekly.weekStartOf(ctx.now);
      const progress = await weekly.readUsed(ctx.client, acct, weekStart, 'direct_clear');
      const goal = sweep.weekly.directClears;
      if (progress < goal) throw new AppError(422, '이번 주 활동 목표를 채우지 못했습니다.', 'WEEKLY_NOT_READY', { goal, progress });
      if (!(await weekly.markClaimed(ctx.client, acct, weekStart))) {
        throw new AppError(409, '이번 주 보상을 이미 받았습니다.', 'WEEKLY_ALREADY_CLAIMED');
      }
      const n = sweep.weekly.rewardTickets;
      await wallet.addNormal(
        ctx.client,
        { accountId: acct, characterId: ctx.char.id, requestId: ctx.requestId, now: ctx.now },
        n,
        'weekly_activity',
        `weekly:${acctUuid}:${weekStart.toISOString()}`,
      );
      const lots = await wallet.readLive(ctx.client, acct, ctx.now);
      return {
        status: 200,
        data: { claimed: { tickets: n }, tickets: wallet.viewOf(lots) as TicketView, weekly_activity: weeklyActivityView(sweep, progress, true) },
      };
    },
  });
}
