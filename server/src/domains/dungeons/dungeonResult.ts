// 던전 결과 검증(9.5): 구조·시간·화력·값 범위가 어긋나면 held로 닫고 보상을 주지 않는다.
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { effectiveHp, attackCap, powerAllows } from '../kills/killRules';
import { killsOfRun } from '../kills/killRepository';
import * as repo from './dungeonRepository';
import { clearXp, monsterTotal, RANK_NAMES, rankOf, rollCards, roomKillCount, roomTotal, scoreRun } from './dungeonRules';
import type { ResultBody } from './dungeonValidation';

/** 허용 오차: 입장 후 서버 경과 시간 + 5초 */
const ELAPSED_SLACK_SECONDS = 5;
/** 최소 클리어 시간 = 이 비율 x 실측 최솟값(PLAN_SERVER §5의 60%) */
const MIN_CLEAR_RATIO = 0.6;
/** minClearSeconds가 0(실측 전)이면 이 비율 x referenceSeconds로 대신한다 */
const FALLBACK_REFERENCE_RATIO = 0.5;

export async function settleResult(ctx: EconCtx, runUuid: string, body: ResultBody) {
  const eco = getGameData().economy;
  const pol = getConfig().policy;
  const run = await repo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
  if (!run || run.state !== 'playing') throw new AppError(409, '진행 중인 던전이 아닙니다.', 'RUN_NOT_PLAYING');
  const dungeon = eco.dungeons.byId.get(run.dungeon_id);
  const diff = eco.dungeons.difficulties[run.difficulty];
  if (!dungeon || !diff) throw new Error(`던전 데이터가 없습니다: ${run.dungeon_id}`);
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

  // ---- 검증: 구조, 시간, 화력, 값 범위 ----
  const serverElapsed = (ctx.now.getTime() - run.started_at.getTime()) / 1000;
  const claimed = stats.elapsed_ms / 1000;
  const minMeasured = eco.dungeons.minClearSeconds[run.difficulty] ?? 0;
  const minClear =
    minMeasured > 0
      ? MIN_CLEAR_RATIO * minMeasured
      : FALLBACK_REFERENCE_RATIO * (dungeon.referenceSeconds[run.difficulty] as number);

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
  if (claimed < minClear) reasons.push('TOO_FAST');

  const kills = await killsOfRun(ctx.client, run.id);
  const hpMul = diff.hpMul * (eco.dungeons.partyHpScale[run.party_size - 1] ?? 1);
  const hpList = kills.map((k) => {
    const def = eco.monsters.get(k.monster_id);
    return def ? effectiveHp(eco, def, k.monster_level, hpMul) : 0;
  });
  const cap = attackCap(eco, pol, ctx.level, await econRepo.listWornKeys(ctx.client, ctx.char.id));
  if (!powerAllows(eco, pol, cap, serverElapsed + ELAPSED_SLACK_SECONDS, hpList)) reasons.push('POWER');

  if (stats.revives_used > diff.revives) reasons.push('REVIVES_OVER');
  if (stats.max_combo > claimed / eco.player.attackCooldown) reasons.push('COMBO_OVER');

  if (reasons.length > 0) {
    // 보류: 보상을 주지 않고 닫는다. 사유는 클라이언트에 알리지 않는다(관리자 도구 7단계가 검토한다)
    await repo.closeRun(ctx.client, run.id, {
      state: 'held',
      endedAt: ctx.now,
      stats: { ...stats, server_elapsed_s: serverElapsed },
      rank: null,
      score: null,
      xpGranted: null,
      holdReason: reasons.join(','),
      cards: null,
    });
    await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'dungeon_result', 3, {
      run_id: runUuid,
      reasons,
    });
    return { status: 200, data: { result: 'held' } };
  }

  // ---- 통과: 랭크 점수는 서버가 계산한다(처치 수와 방 마릿수는 서버 기록) ----
  const acceptedKills = Object.values(run.room_kills).reduce((a, b) => a + b, 0);
  // 시간 점수는 서버 시계 기준: 클라이언트 경과 시간이 서버 경과(로딩·연출 여유를 뺀 값)보다 짧으면 서버 값을 쓴다
  const scoredSeconds = Math.max(claimed, serverElapsed - getConfig().policy.dungeonTimeOverheadSeconds);
  const score = scoreRun(
    eco,
    scoredSeconds,
    dungeon.referenceSeconds[run.difficulty] as number,
    stats.hits_taken,
    acceptedKills,
    monsterTotal(dungeon),
    stats.max_combo,
    stats.revives_used,
  );
  const rank = rankOf(eco, score.total);
  const xp = clearXp(eco, dungeon, diff, rank);
  const { granted, leveledUp } = await ctx.grantXp(xp, 'dungeon_clear', run.uuid);
  // 선택 전에는 내용을 클라이언트에 주지 않는다
  const cards = rollCards(eco, dungeon, run.difficulty, ctx.char.class, getRng());
  await repo.closeRun(ctx.client, run.id, {
    state: 'cleared',
    endedAt: ctx.now,
    stats,
    rank,
    score: { ...score },
    xpGranted: xp,
    holdReason: null,
    cards,
  });
  return {
    status: 200,
    data: {
      result: 'cleared',
      rank: RANK_NAMES[rank] ?? 'F',
      score,
      granted_xp: granted,
      leveled_up: leveledUp,
      card_count: cards.length,
      delta: ctx.delta(),
    },
  };
}
