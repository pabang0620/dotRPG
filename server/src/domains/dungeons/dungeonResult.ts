// 던전 결과 검증(9.5)과 클리어 확정. 구조·시간·화력·값 범위가 어긋나면 held로 닫고 보상을 주지 않는다.
// 4단계: 검증(validateClear)과 확정(finalizeCleared)을 나눠 솔로(AI 동반, 레이드)와 파티 정산(partyruns)이 같이 쓴다.
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { effectiveHp, attackCap, powerAllows } from '../kills/killRules';
import { killsOfRun } from '../kills/killRepository';
import * as repo from './dungeonRepository';
import {
  clearXp,
  diffOf,
  monsterTotal,
  RANK_NAMES,
  rankOf,
  rollCards,
  roomKillCount,
  roomTotal,
  scoreRun,
  type DiffNumbers,
} from './dungeonRules';
import { raidPeriod } from './entryRules';
import type { ResultBody } from './dungeonValidation';

/** 허용 오차: 입장 후 서버 경과 시간 + 5초 */
export const ELAPSED_SLACK_SECONDS = 5;
/** 최소 클리어 시간 = 이 비율 x 실측 최솟값(PLAN_SERVER §5의 60%) */
const MIN_CLEAR_RATIO = 0.6;
/** 실측 전(0)이면 이 비율 x referenceSeconds로 대신한다 */
const FALLBACK_REFERENCE_RATIO = 0.5;

export type RunStats = ResultBody['stats'];

/** 이 판의 던전·난이도 수치. 데이터가 없으면 서버 데이터 오류라 던진다 */
export function runContext(run: repo.RunRow) {
  const eco = getGameData().economy;
  const dungeon = eco.dungeons.byId.get(run.dungeon_id);
  const diff = dungeon ? diffOf(eco, dungeon, run.difficulty) : null;
  if (!dungeon || !diff) throw new Error(`던전 데이터가 없습니다: ${run.dungeon_id}`);
  return { eco, dungeon, diff };
}

/** 던전의 최소 클리어 시간(초) */
export function minClearSeconds(run: repo.RunRow): number {
  const { eco, dungeon } = runContext(run);
  const measured = dungeon.isRaid
    ? (eco.dungeons.raidMinClearSeconds[dungeon.id] ?? 0)
    : (eco.dungeons.minClearSeconds[run.difficulty] ?? 0);
  return measured > 0
    ? MIN_CLEAR_RATIO * measured
    : FALLBACK_REFERENCE_RATIO * (dungeon.referenceSeconds[run.difficulty] ?? dungeon.referenceSeconds[0] ?? 0);
}

/** 화력 상한: 파티·AI 동반 판은 판 스냅샷 x 여유, 그 밖은 본인 상한(3단계) */
export async function runAttackCap(ctx: EconCtx, run: repo.RunRow): Promise<number> {
  const eco = getGameData().economy;
  const pol = getConfig().policy;
  if (run.power_cap !== null) return run.power_cap * pol.partyPowerSlack;
  return attackCap(eco, pol, ctx.level, await econRepo.listWornKeys(ctx.client, ctx.char.id));
}

export interface ClearCheck {
  reasons: string[];
  /** 입장부터 보고까지 서버 경과(초) */
  serverElapsed: number;
  claimed: number;
}

/** 클리어 보고의 자체 검증(구조, 시간, 화력, 값 범위). `at`은 보고 시각 */
export async function validateClear(ctx: EconCtx, run: repo.RunRow, stats: RunStats, at: Date): Promise<ClearCheck> {
  const { eco, dungeon, diff } = runContext(run);
  const pol = getConfig().policy;
  const serverElapsed = (at.getTime() - run.started_at.getTime()) / 1000;
  const claimed = stats.elapsed_ms / 1000;
  const reasons: string[] = [];

  const bossRoom = dungeon.rooms[dungeon.bossRoom];
  if (run.room_index !== dungeon.bossRoom || !bossRoom) reasons.push('BOSS_ROOM_NOT_REACHED');
  else {
    for (const g of bossRoom.groups) {
      if (g.isBoss && (run.room_kills[`${dungeon.bossRoom}:${g.monsterId}`] ?? 0) < g.count) reasons.push('BOSS_NOT_KILLED');
    }
  }
  for (let i = 0; i < dungeon.bossRoom; i++) {
    if (roomKillCount(run.room_kills, i) < roomTotal(dungeon, i) * pol.dungeonRoomClearRatio) {
      reasons.push('ROOMS_NOT_CLEARED');
      break;
    }
  }
  if (claimed > serverElapsed + ELAPSED_SLACK_SECONDS) reasons.push('TIME_OVER_SERVER');
  if (claimed < minClearSeconds(run)) reasons.push('TOO_FAST');

  const kills = await killsOfRun(ctx.client, run.id);
  const hpMul = diff.hpMul * (eco.dungeons.partyHpScale[run.party_size - 1] ?? 1);
  const hpList = kills.map((k) => {
    const def = eco.monsters.get(k.monster_id);
    return def ? effectiveHp(eco, def, k.monster_level, hpMul) : 0;
  });
  if (!powerAllows(eco, pol, await runAttackCap(ctx, run), serverElapsed + ELAPSED_SLACK_SECONDS, hpList)) {
    reasons.push('POWER');
  }
  if (stats.revives_used > diff.revives) reasons.push('REVIVES_OVER');
  if (stats.max_combo > claimed / eco.player.attackCooldown) reasons.push('COMBO_OVER');
  return { reasons, serverElapsed, claimed };
}

/** 보류: 보상을 주지 않고 닫는다. 사유는 클라이언트에 알리지 않는다(관리자 도구 7단계가 검토한다) */
export async function holdRun(
  ctx: EconCtx,
  run: repo.RunRow,
  stats: Record<string, unknown>,
  reasons: string[],
  at: Date,
  anomalyKind: 'dungeon_result' | 'party_result' = 'dungeon_result',
): Promise<void> {
  await repo.closeRun(ctx.client, run.id, {
    state: 'held',
    endedAt: at,
    stats,
    rank: null,
    score: null,
    xpGranted: null,
    holdReason: reasons.join(','),
    cards: null,
  });
  await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, anomalyKind, anomalyKind === 'party_result' ? 2 : 3, {
    run_id: run.uuid,
    reasons,
  });
}

/**
 * 레이드 보상 최소 인원 재확인: 아직 진행 중이거나 클리어를 보고(정산 대기)·확정한 사람만 센다.
 * 실패·보류·이탈로 끝난 사람은 세지 않는다(친구가 바로 실패해 대조를 피하는 것을 막는다).
 */
async function humansStanding(ctx: EconCtx, run: repo.RunRow): Promise<number> {
  if (run.party_run_id === null) return run.humans;
  const r = await ctx.client.query<{ n: string }>(
    `SELECT count(*) AS n FROM dungeon_runs
      WHERE party_run_id = $1 AND state IN ('playing', 'reported', 'cleared')`,
    [run.party_run_id],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export interface ClearInput {
  hitsTaken: number;
  maxCombo: number;
  revivesUsed: number;
  /** 시간 점수에 쓸 초(주장과 서버 경과 중 서버 기준 보정 뒤 값) */
  scoredSeconds: number;
  /** 기록할 stats(주장) */
  stats: Record<string, unknown>;
  at: Date;
}

/** 검증을 통과한 클리어를 확정한다: 랭크, (레이드 청구), 경험치, 카드, 레이드 열쇠. 한 트랜잭션 안에서만 부른다 */
export async function finalizeCleared(ctx: EconCtx, run: repo.RunRow, inp: ClearInput) {
  const { eco, dungeon, diff } = runContext(run);
  const pol = getConfig().policy;
  const acceptedKills = Object.values(run.room_kills).reduce((a, b) => a + b, 0);
  const score = scoreRun(
    eco,
    inp.scoredSeconds,
    dungeon.referenceSeconds[run.difficulty] as number,
    inp.hitsTaken,
    acceptedKills,
    monsterTotal(dungeon),
    inp.maxCombo,
    inp.revivesUsed,
  );
  const rank = rankOf(eco, score.total);

  // ---- 레이드 보상 잠금(10.3, 10.4): 기간당 한 번은 raid_claims의 PK가 막는다. 경험치·카드보다 먼저 한다 ----
  let locked = run.reward_locked;
  let reason = run.lock_reason;
  let keyGain = 0;
  let keyCost = 0;
  if (dungeon.isRaid && !locked) {
    if ((await humansStanding(ctx, run)) < pol.raidRewardMinHumans) {
      locked = true;
      reason = 'TOO_FEW_HUMANS';
    } else if (dungeon.raidTier === 'Final' && dungeon.keyCost > 0 && (await ctx.stackCount('bag', eco.dungeons.keyItem)) < dungeon.keyCost) {
      locked = true;
      reason = 'KEYS_MISSING';
    } else {
      const period = raidPeriod(dungeon, inp.at);
      const ins = await ctx.client.query(
        `INSERT INTO raid_claims (character_id, dungeon_id, period_kind, period_start, dungeon_run_id, claimed_at)
         VALUES ($1, $2, $3, $4, $5, $6) ON CONFLICT DO NOTHING`,
        [ctx.char.id, dungeon.id, period.kind, period.start, run.id, ctx.now],
      );
      if ((ins.rowCount ?? 0) === 0) {
        locked = true;
        reason = 'ALREADY_CLAIMED';
      }
    }
  }

  let granted = 0;
  let leveledUp = false;
  let cards: { item_key: string; count: number }[] | null = null;
  if (!locked) {
    const xp = clearXp(eco, dungeon, diff, rank);
    ({ granted, leveledUp } = await ctx.grantXp(xp, 'dungeon_clear', run.uuid));
    // 선택 전에는 내용을 클라이언트에 주지 않는다
    cards = rollCards(eco, dungeon, diff, ctx.char.class, getRng());
    if (dungeon.isRaid) {
      if (dungeon.raidTier === 'Final' && dungeon.keyCost > 0) {
        keyCost = dungeon.keyCost;
        await ctx.removeItem('bag', eco.dungeons.keyItem, keyCost, 'raid_key_cost', run.uuid);
      } else if (dungeon.keyMax > 0) {
        keyGain = getRng().int(dungeon.keyMin, dungeon.keyMax + 1);
        if (keyGain > 0) await ctx.addItem('bag', eco.dungeons.keyItem, keyGain, 'raid_key', run.uuid);
      }
    }
  }
  await repo.closeRun(ctx.client, run.id, {
    state: 'cleared',
    endedAt: inp.at,
    stats: inp.stats,
    rank,
    score: { ...score },
    xpGranted: locked ? 0 : granted,
    holdReason: null,
    cards,
    rewardLocked: locked,
    lockReason: reason,
  });
  const data: Record<string, unknown> = {
    result: 'cleared',
    rank: RANK_NAMES[rank] ?? 'F',
    score,
    granted_xp: granted,
    leveled_up: leveledUp,
    card_count: cards ? cards.length : 0,
    delta: ctx.delta(),
  };
  if (dungeon.isRaid) {
    data.raid = {
      reward_locked: locked,
      ...(reason ? { lock_reason: reason } : {}),
      ...(keyGain > 0 ? { key_gain: keyGain } : {}),
      ...(keyCost > 0 ? { key_cost: keyCost } : {}),
    };
  }
  return { status: 200, data };
}

/** 솔로(AI 동반, 레이드) 결과. 파티 판은 partyruns의 접수·정산이 처리한다 */
export async function settleResult(ctx: EconCtx, runUuid: string, body: ResultBody) {
  const run = await repo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
  if (!run || run.state !== 'playing') throw new AppError(409, '진행 중인 던전이 아닙니다.', 'RUN_NOT_PLAYING');
  const stats = body.stats;

  if (body.outcome === 'failed') {
    // 이미 받은 처치 경험치·드롭은 유지된다. 보상 없음
    await repo.closeRun(ctx.client, run.id, {
      state: 'failed',
      endedAt: ctx.now,
      stats,
      rank: null,
      score: null,
      xpGranted: null,
      holdReason: null,
      cards: null,
    });
    return { status: 200, data: { result: 'failed' } };
  }

  const check = await validateClear(ctx, run, stats, ctx.now);
  if (check.reasons.length > 0) {
    await holdRun(ctx, run, { ...stats, server_elapsed_s: check.serverElapsed }, check.reasons, ctx.now);
    return { status: 200, data: { result: 'held' } };
  }
  // 시간 점수는 서버 시계 기준: 클라이언트 경과 시간이 서버 경과(로딩·연출 여유를 뺀 값)보다 짧으면 서버 값을 쓴다
  const scoredSeconds = Math.max(check.claimed, check.serverElapsed - getConfig().policy.dungeonTimeOverheadSeconds);
  return finalizeCleared(ctx, run, {
    hitsTaken: stats.hits_taken,
    maxCombo: stats.max_combo,
    revivesUsed: stats.revives_used,
    scoredSeconds,
    stats,
    at: ctx.now,
  });
}

export type { DiffNumbers };
