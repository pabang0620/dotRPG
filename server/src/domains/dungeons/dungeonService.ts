// 솔로(AI 동반) 요일 던전·레이드: 입장, 목록, 결과 검증, 카드 선택. 파티 판은 partyruns 도메인이 만든다.
import { closeMembershipOf } from '../fieldsessions/fieldCore';
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { getRng } from '../../utils/rng';
import { randomUUID } from 'node:crypto';
import { resetBoundaries } from '../../utils/resetBoundaries';
import * as charRepo from '../characters/characterRepository';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as dungeonRepo from './dungeonRepository';
import { isOpenToday } from './dungeonRules';
import { checkEntry, lockAtEntry } from './entryRules';
import { AnomalyError } from '../economy/economyService';
import { attackCap } from '../kills/killRules';
import * as econRepo from '../economy/economyRepository';
import { settleResult } from './dungeonResult';
import { reportPartyResult } from '../partyruns/partySettle';
import type { EnterBody, PickBody, ResultBody } from './dungeonValidation';

const ONE_PLAYING = 'dungeon_runs_one_playing';

// ---------- GET /dungeons ----------

export async function listDungeons(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const eco = getGameData().economy;
  const now = getNow();
  const b = resetBoundaries(now);
  const cardSince = new Date(now.getTime() - getConfig().policy.dungeonCardTtlHours * 3_600_000);
  const [used, active, clears, unpicked] = await Promise.all([
    dungeonRepo.countEntries(getPool(), c.id, new Date(b.dailyStartAt)),
    dungeonRepo.findPlayingRun(getPool(), c.id),
    dungeonRepo.clearSummary(getPool(), c.id),
    dungeonRepo.unpickedRuns(getPool(), c.id, cardSince),
  ]);
  const limit = eco.dungeons.dailyEntries;
  const best = new Map(clears.map((r) => [`${r.dungeon_id}:${r.difficulty}`, r.best_rank] as const));
  return {
    reset: { daily_start_at: b.dailyStartAt, next_daily_at: b.nextDailyAt },
    entries: { limit, used, left: Math.max(0, limit - used) },
    // 접속 때 카드 창을 띄울 판(보류가 해제돼 나중에 확정된 판도 여기서 알 수 있다: phase7_ops.md 5.8 HR3)
    unpicked_runs: unpicked.map((u) => ({ run_id: u.uuid, ended_at: u.ended_at.toISOString() })),
    active_run: active
      ? {
          id: active.uuid,
          party_run_id: active.party_run_id === null ? null : await dungeonRepo.partyRunUuid(getPool(), active.party_run_id),
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
  const pol = getConfig().policy;
  const stale = pol.runStaleSeconds;
  if (await dungeonRepo.inPartyRun(ctx.client, ctx.char.id)) {
    throw new AppError(409, '파티 판에 참여 중입니다.', 'IN_PARTY_RUN');
  }
  let info;
  try {
    info = await checkEntry(ctx.client, ctx.char, body.dungeon_id, body.difficulty, ctx.now);
  } catch (err) {
    // 클라이언트 UI가 막은 레이드 조건을 우회해 들어오려 한 흔적
    if (err instanceof AppError && err.code === 'RAID_LOCKED') {
      throw new AnomalyError(err.status, err.message, err.code, {
        kind: 'raid_enter',
        severity: 2,
        detail: { dungeon_id: body.dungeon_id, code: err.code },
      }, err.extra);
    }
    throw err;
  }
  const d = info.dungeon;
  const maxAi = Math.min(eco.dungeons.mercenary.maxCompanions, d.maxParty - 1);
  if (body.ai_count > maxAi) throw new AppError(422, '동행 AI 수가 너무 많습니다.', 'PARTY_TOO_BIG');

  // 진행 중인 판: 오래 방치된 것만 닫는다(입장 횟수는 이미 썼다)
  const playing = await dungeonRepo.findPlayingRun(ctx.client, ctx.char.id);
  if (playing) {
    if (ctx.now.getTime() - playing.started_at.getTime() <= stale * 1000) {
      throw new AppError(409, '진행 중인 던전이 있습니다.', 'RUN_ACTIVE', { run_id: playing.uuid });
    }
    await dungeonRepo.abandonRun(ctx.client, playing.id, ctx.now);
  }

  // 8단계: 던전 출발은 내 필드 세션을 닫는다(D1)
  await closeMembershipOf(ctx.client, ctx.char.id, 'dungeon_start', ctx.now);
  const lock = await lockAtEntry(ctx.client, ctx.char.id, d, 1, ctx.now);
  // AI가 없으면 3단계 방식(본인 화력만), 있으면 본인 상한 x (1 + AI 수 x 용병 딜 배율)
  let powerCap: number | null = null;
  if (body.ai_count > 0) {
    const cap = attackCap(eco, pol, ctx.char.level, await econRepo.listWornKeys(ctx.client, ctx.char.id));
    powerCap = cap * (1 + body.ai_count * eco.dungeons.mercenary.damageScale);
  }
  let run;
  try {
    run = await dungeonRepo.insertRun(ctx.client, {
      characterId: ctx.char.id,
      dungeonId: d.id,
      difficulty: body.difficulty,
      resetDay: info.resetDay,
      startedAt: ctx.now,
      humans: 1,
      aiCount: body.ai_count,
      countsEntry: info.countsEntry,
      rewardLocked: lock.locked,
      lockReason: lock.reason,
      powerCap,
    });
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
        humans: run.humans,
        ai_count: run.ai_count,
        reward_locked: run.reward_locked,
        lock_reason: run.lock_reason,
        started_at: run.started_at.toISOString(),
      },
      entries_left: Math.max(0, info.limit - info.used - (info.countsEntry ? 1 : 0)),
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
    handler: async (ctx) => {
      // 파티 판은 접수 후 대조(D2), 솔로(AI 동반, 레이드)는 즉시 판정
      const run = await dungeonRepo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
      if (run && run.state === 'playing' && run.party_run_id !== null) return reportPartyResult(ctx, run, body);
      return settleResult(ctx, runUuid, body);
    },
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
      const card = await grantCard(ctx, run, body.index);
      // 나머지 카드는 동료 AI가 뒤집는 연출용 공개일 뿐 지급되지 않는다
      return { status: 200, data: { card, cards: run.cards, delta: ctx.delta() } };
    },
  });
}

async function grantCard(ctx: EconCtx, run: dungeonRepo.RunRow, index: number) {
  const card = run.cards?.[index];
  if (!card) throw new AppError(422, '고를 카드가 없습니다.', 'NO_CARDS');
  if (card.item_key === 'gold') await ctx.changeGold(card.count, 'dungeon_card', run.uuid);
  else await ctx.addItem('bag', card.item_key, card.count, 'dungeon_card', run.uuid);
  await dungeonRepo.setCardPicked(ctx.client, run.id, index, ctx.now);
  return card;
}

// ---------- 서버 틱: 고르지 않은 카드 ----------

/**
 * 클리어 뒤 DUNGEON_CARD_AUTO_PICK_MINUTES가 지나도록 카드를 고르지 않은 판(결과 화면 전에 끊김, 클리어 뒤 이탈해
 * 서버가 대신 정산한 판)은 서버가 한 장을 골라 가방에 넣는다. 네 장은 뒤집기 전 모두 같은 가치라 무작위로 고른다.
 */
export async function runCardAutoPickTick(): Promise<void> {
  const cutoff = new Date(getNow().getTime() - getConfig().policy.dungeonCardAutoPickMinutes * 60_000);
  const r = await getPool().query<{ uuid: string; account_id: string; char_uuid: string }>(
    `SELECT d.uuid, c.account_id, c.uuid AS char_uuid
       FROM dungeon_runs d JOIN characters c ON c.id = d.character_id
      WHERE d.state = 'cleared' AND d.cards IS NOT NULL AND d.card_picked IS NULL AND d.ended_at < $1
      LIMIT 50`,
    [cutoff],
  );
  for (const row of r.rows) {
    await runEconomy({
      accountId: Number(row.account_id),
      characterUuid: row.char_uuid,
      endpoint: 'TICK card auto pick',
      requestId: randomUUID(),
      payload: { run_id: row.uuid },
      handler: async (ctx) => {
        const run = await dungeonRepo.findRunByUuid(ctx.client, ctx.char.id, row.uuid);
        if (!run || run.state !== 'cleared' || !run.cards || run.cards.length === 0 || run.card_picked !== null) {
          return { status: 200, data: { result: 'none' } };
        }
        const card = await grantCard(ctx, run, getRng().int(0, run.cards.length));
        return { status: 200, data: { card } };
      },
    });
  }
}

// ---------- D4 GET /dungeon-runs/{run_id} ----------

export async function getRun(accountId: number, characterUuid: string, runUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const run = await dungeonRepo.findRunByUuid(getPool(), c.id, runUuid);
  if (!run) throw new AppError(404, '던전 기록을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
  return {
    run: {
      id: run.uuid,
      dungeon_id: run.dungeon_id,
      difficulty: run.difficulty,
      state: run.state,
      party_run_id: run.party_run_id === null ? null : await dungeonRepo.partyRunUuid(getPool(), run.party_run_id),
      humans: run.humans,
      ai_count: run.ai_count,
      party_size: run.party_size,
      started_at: run.started_at.toISOString(),
      ended_at: run.ended_at ? run.ended_at.toISOString() : null,
      reward_locked: run.reward_locked,
      lock_reason: run.lock_reason,
      ...(run.state === 'cleared'
        ? {
            rank: run.rank,
            score: run.score,
            granted_xp: run.xp_granted ?? 0,
            card_count: run.cards ? run.cards.length : 0,
            card_picked: run.card_picked,
          }
        : {}),
    },
  };
}
