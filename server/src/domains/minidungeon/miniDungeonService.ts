import { randomUUID } from 'node:crypto';
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { withTransaction } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { roundHalfEven } from '../../utils/itemKey';
import { assertKillPresence } from '../antiabuse/killPresence';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { activeSessionIdOf } from '../fieldsessions/fieldRepository';
import { attackCap, effectiveHp, powerAllows } from '../kills/killRules';
import { getQueueStore } from '../match/queueStore';
import { findPartyIdOf } from '../party/partyRepository';
import * as tradeRepo from '../trade/tradeRepository';
import * as repo from './miniDungeonRepository';
import { DAILY_LIMIT_MS, KILL_INTERVAL_MS, MINI_HP_MUL, MINI_TIERS, killCredit, miniMapId, tierOf, timeCharge, xpPerKill } from './miniDungeonRules';
import type { EnterBody, KillBody, LeaveBody, RequestBody } from './miniDungeonValidation';

const MONSTER_ID = 'skel_warrior';
const notFound = () => new AppError(404, '미니던전 기록을 찾을 수 없습니다.', 'MINI_RUN_NOT_FOUND');

/** Always under the character lock. Settle old-day runs against their original budget. */
async function settle(client: PoolClient, run: repo.MiniRun, now: Date): Promise<void> {
  if (run.state !== 'playing') return;
  const charge = timeCharge(run.charged_until, run.ends_at, now);
  await repo.charge(client, run, charge.ms, charge.until);
  if (charge.expired) await repo.close(client, run, 'expired', run.ends_at);
}

const runView = (run: repo.MiniRun) => ({
  id: run.uuid, tier: run.tier, map_id: miniMapId(run.tier), monster_id: MONSTER_ID,
  entry_level: run.entry_level, monster_level: run.entry_level, xp_per_kill: run.xp_per_kill,
  started_at: run.started_at.toISOString(), ends_at: run.ends_at.toISOString(),
  kills: run.kills, xp_granted: run.xp_granted,
  state: run.state, ended_at: run.ended_at?.toISOString() ?? null,
});

async function snapshot(ctx: EconCtx, endedRun?: repo.MiniRun) {
  const reset = resetBoundaries(ctx.now);
  const run = await repo.active(ctx.client, ctx.char.id);
  if (run) await settle(ctx.client, run, ctx.now);
  const used = await repo.day(ctx.client, ctx.char.id, new Date(reset.dailyStartAt));
  const ended = endedRun && endedRun.state !== 'playing' ? endedRun : run && run.state !== 'playing' ? run : null;
  return {
    daily_limit_seconds: DAILY_LIMIT_MS / 1000,
    used_seconds: used / 1000,
    remaining_seconds: Math.max(0, DAILY_LIMIT_MS - used) / 1000,
    server_time: ctx.now.toISOString(),
    reset: { daily_start_at: reset.dailyStartAt, next_daily_at: reset.nextDailyAt },
    tiers: MINI_TIERS.map((tier) => ({ tier, min_level: tier, max_level: tier + 9, map_id: miniMapId(tier), monster_id: MONSTER_ID })),
    active_run: run?.state === 'playing' ? runView(run) : null,
    completed_run: ended ? runView(ended) : null,
    delta: ctx.delta(),
  };
}

/** Status settles elapsed time without a client duration or an economic reward. */
export function status(accountId: number, uuid: string) {
  return withTransaction(async (client) => {
    const c = await econRepo.lockCharacter(client, accountId, uuid);
    if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
    return snapshot(new EconCtx(client, c, randomUUID(), getNow()));
  });
}

async function assertSolo(ctx: EconCtx): Promise<void> {
  await tradeRepo.expireDue(ctx.client, [ctx.char.id], ctx.now);
  if (await tradeRepo.openOfCharacter(ctx.client, ctx.char.id)) {
    throw new AppError(409, '교환을 마치거나 취소한 뒤 입장해 주세요.', 'TRADE_ALREADY_OPEN');
  }
  if (await findPartyIdOf(ctx.client, ctx.char.id) !== null || getQueueStore().get(ctx.char.id)) {
    throw new AppError(409, '미니던전은 혼자 입장합니다. 파티와 매칭을 종료해 주세요.', 'MINI_SOLO_ONLY');
  }
  if (await dungeonRepo.findPlayingRun(ctx.client, ctx.char.id) || await dungeonRepo.inPartyRun(ctx.client, ctx.char.id)) {
    throw new AppError(409, '진행 중인 던전을 먼저 종료해 주세요.', 'RUN_ACTIVE');
  }
  if (await activeSessionIdOf(ctx.client, ctx.char.id) !== null) {
    throw new AppError(409, '온라인 사냥을 종료한 뒤 입장해 주세요.', 'FIELD_SESSION_ACTIVE');
  }
}

export function enter(accountId: number, uuid: string, body: EnterBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'POST /characters/:uuid/mini-dungeon/enter', requestId: body.request_id,
    payload: { tier: body.tier }, handler: async (ctx) => {
      const tier = tierOf(ctx.level, getGameData().economy.progression.maxLevel);
      if (tier === null) throw new AppError(422, '미니던전은 10~39레벨 캐릭터가 이용할 수 있습니다.', 'MINI_LEVEL_LOCKED');
      if (body.tier !== tier) throw new AppError(422, '현재 레벨에 맞는 구간을 선택해 주세요.', 'MINI_TIER_MISMATCH', { tier });
      await assertSolo(ctx);
      await snapshot(ctx);
      // Re-entering closes the previous timer first; it never resets or refunds its elapsed time.
      const old = await repo.active(ctx.client, ctx.char.id);
      if (old) await repo.close(ctx.client, old, 'left', ctx.now);
      const reset = resetBoundaries(ctx.now);
      const resetDay = new Date(reset.dailyStartAt);
      const used = await repo.day(ctx.client, ctx.char.id, resetDay);
      const left = DAILY_LIMIT_MS - used;
      if (left <= 0) throw new AppError(422, '오늘의 미니던전 30분을 모두 이용했습니다.', 'MINI_TIME_EXHAUSTED');
      await repo.insert(ctx.client, { characterId: ctx.char.id, resetDay, tier, entryLevel: ctx.level,
        xp: xpPerKill(getGameData().economy.progression.xpToNext, ctx.level), now: ctx.now,
        endsAt: new Date(Math.min(ctx.now.getTime() + left, Date.parse(reset.nextDailyAt))) });
      return { status: 201, data: await snapshot(ctx) };
    } });
}

async function ownedRun(ctx: EconCtx, runUuid: string): Promise<repo.MiniRun> {
  const run = await repo.byUuid(ctx.client, ctx.char.id, runUuid);
  if (!run) throw notFound();
  await settle(ctx.client, run, ctx.now);
  return run;
}

export function pulse(accountId: number, uuid: string, runUuid: string, body: RequestBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'POST /characters/:uuid/mini-dungeon/runs/:run_id/pulse',
    requestId: body.request_id, payload: { run_id: runUuid }, handler: async (ctx) => {
      const run = await ownedRun(ctx, runUuid);
      return { status: 200, data: await snapshot(ctx, run) };
    } });
}

export function kill(accountId: number, uuid: string, runUuid: string, body: KillBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'POST /characters/:uuid/mini-dungeon/runs/:run_id/kills',
    requestId: body.request_id, payload: { run_id: runUuid, monster_ref: body.monster_ref }, handler: async (ctx) => {
      const run = await ownedRun(ctx, runUuid);
      if (run.state !== 'playing') return { status: 200, data: { ...await snapshot(ctx, run), accepted: false, granted_xp: 0, reason: 'MINI_RUN_ENDED' } };
      if (ctx.level >= getGameData().economy.progression.maxLevel) {
        await repo.close(ctx.client, run, 'level_cap', ctx.now);
        return { status: 200, data: { ...await snapshot(ctx, run), accepted: false, granted_xp: 0, reason: 'MINI_LEVEL_CAP' } };
      }
      await assertSolo(ctx);
      await assertKillPresence(ctx, { mapId: miniMapId(run.tier), dungeon: false, isFieldBoss: false, monsterId: MONSTER_ID });
      if (await repo.hasKill(ctx.client, run.id, body.monster_ref)) throw new AppError(409, '이미 보고한 몬스터입니다.', 'KILL_DUPLICATE');
      const credits = killCredit(run.kill_credits_ms, run.credits_at, ctx.now);
      if (credits < KILL_INTERVAL_MS) throw new AppError(429, '처치 보고가 너무 빠릅니다.', 'MINI_KILL_RATE_LIMITED', { retry_after_sec: Math.ceil((KILL_INTERVAL_MS - credits) / 1000) });
      const eco = getGameData().economy;
      const pol = getConfig().policy;
      const def = eco.monsters.get(MONSTER_ID);
      if (!def) throw new Error(`Mini dungeon monster ${MONSTER_ID} is missing`);
      const recent = await repo.killsSince(ctx.client, run.id, new Date(ctx.now.getTime() - 10_000));
      const hp = Math.max(20, roundHalfEven(effectiveHp(eco, def, run.entry_level, MINI_HP_MUL)));
      const cap = attackCap(eco, pol, ctx.level, await econRepo.listWornKeys(ctx.client, ctx.char.id));
      if (!powerAllows(eco, pol, cap, 10, Array(recent + 1).fill(hp) as number[])) {
        throw new AppError(429, '화력에 비해 처치 보고가 너무 빠릅니다.', 'MINI_KILL_RATE_LIMITED', { retry_after_sec: 2 });
      }
      // Only count XP actually usable before the level cap, matching the local mini result counter.
      // General combat/quest grantXp semantics remain unchanged.
      const p = eco.progression;
      const xpToCap = p.xpToNext.slice(ctx.level - 1, p.maxLevel - 1).reduce((sum, xp) => sum + xp, 0) - ctx.xp;
      const reward = await ctx.grantXp(Math.min(run.xp_per_kill, Math.max(0, xpToCap)), 'mini_dungeon', `${run.uuid}:${body.monster_ref}`);
      await repo.recordKill(ctx.client, run, body.monster_ref, ctx.requestId, reward.granted, credits - KILL_INTERVAL_MS, ctx.now);
      if (ctx.level >= eco.progression.maxLevel) await repo.close(ctx.client, run, 'level_cap', ctx.now);
      // No normal kill_log, kill_stats, drops, items, gold, quest progress or dungeon clear record.
      return { status: 200, data: { ...await snapshot(ctx, run), accepted: true, granted_xp: reward.granted, leveled_up: reward.leveledUp } };
    } });
}

export function leave(accountId: number, uuid: string, runUuid: string, body: LeaveBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'POST /characters/:uuid/mini-dungeon/runs/:run_id/leave',
    requestId: body.request_id, payload: { run_id: runUuid, reason: body.reason }, handler: async (ctx) => {
      const run = await ownedRun(ctx, runUuid);
      if (run.state === 'playing') await repo.close(ctx.client, run, body.reason, ctx.now);
      return { status: 200, data: await snapshot(ctx, run) };
    } });
}
