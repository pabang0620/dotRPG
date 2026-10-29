// 던전 결과 검증(9.5)과 클리어 확정. 구조·시간·화력·값 범위가 어긋나면 held로 닫고 보상을 주지 않는다.
// 4단계: 검증(validateClear)과 확정(finalizeCleared)을 나눠 솔로(AI 동반, 레이드)와 파티 정산(partyruns)이 같이 쓴다.
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import { applyUniqueBonus } from './uniquePity';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { effectiveHp, attackCap, powerAllows } from '../kills/killRules';
import { humanGroups, underlevelFactor } from '../antiabuse/contribution';
import { killsOfRun } from '../kills/killRepository';
import * as repo from './dungeonRepository';
import { partyHpScaleOf } from './partyScale';
import {
  clearXp,
  diffOf,
  monsterTotal,
  RAID_HEROIC,
  RANK_NAMES,
  rankOf,
  rollCards,
  rollClearRewards,
  rollLordBonus,
  rollRaidCards,
  rollRaidGold,
  roomKillCount,
  roomTotal,
  scoreRun,
  type DiffNumbers,
} from './dungeonRules';
import { raidClaimed, raidPeriod } from './entryRules';
import { bumpDirectClear } from '../sweep/weeklyCounter';
import { countDungeonRevives } from '../revive/reviveRepository';
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
  // 요일던전은 난이도가 올라도 몬스터가 단단해질 뿐이라, 더 쉬운 난이도의 실측 최단 기록이 안전한 하한이다
  // (실측이 없는 난이도에 기준 시간 절반을 쓰면 고레벨이 정상적으로 빨리 깬 판이 보류된다)
  const easier = eco.dungeons.minClearSeconds.slice(0, run.difficulty + 1).filter((v) => v > 0);
  const measured = dungeon.isRaid
    ? (eco.dungeons.raidMinClearSeconds[dungeon.id] ?? 0)
    : (easier.length > 0 ? Math.max(...easier) : 0);
  return measured > 0
    ? MIN_CLEAR_RATIO * measured
    : FALLBACK_REFERENCE_RATIO * (dungeon.referenceSeconds[run.difficulty] ?? dungeon.referenceSeconds[0] ?? 0);
}

/** 화력 상한: 파티·AI 동반 판은 판 스냅샷 x 여유, 그 밖은 본인 상한(3단계) */
export async function runAttackCap(ctx: EconCtx, run: repo.RunRow): Promise<number> {
  const eco = getGameData().economy;
  const pol = getConfig().policy;
  if (run.power_cap !== null) return run.power_cap * pol.partyPowerSlack;
  // 스냅샷이 없는 판(옛 기록 등)도 AI 동료가 있으면 그 몫을 더한다(입장 때 스냅샷과 같은 식)
  const own = attackCap(eco, pol, ctx.level, await econRepo.listWornKeys(ctx.client, ctx.char.id));
  return run.ai_count > 0 ? own * (1 + run.ai_count * eco.dungeons.mercenary.damageScale) : own;
}

export interface ClearCheck {
  /** 보상을 줄 수 없는 사유(구조 위반, 파티 화력으로 불가능한 속도). 비면 정상 정산 */
  reasons: string[];
  /** 2026-10-14: 기록만 남기는 사유(빠른 클리어, 시간·콤보·부활 값 이상). 정산은 그대로 한다 */
  soft: string[];
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

  // 2026-10-14: 파티 판은 파티 전원의 처치 기록을 합쳐 본다(한 사람의 PC가 처치 보고를 놓쳐도 클리어로 인정)
  let roomIndex = run.room_index;
  let roomKills = run.room_kills;
  if (run.party_run_id !== null) {
    roomKills = { ...run.room_kills };
    for (const other of await repo.runsOfPartyRun(ctx.client, run.party_run_id)) {
      roomIndex = Math.max(roomIndex, other.room_index);
      for (const [k, v] of Object.entries(other.room_kills)) roomKills[k] = Math.max(roomKills[k] ?? 0, v);
    }
  }
  const bossRoom = dungeon.rooms[dungeon.bossRoom];
  if (roomIndex !== dungeon.bossRoom || !bossRoom) reasons.push('BOSS_ROOM_NOT_REACHED');
  else {
    for (const g of bossRoom.groups) {
      if (g.isBoss && (roomKills[`${dungeon.bossRoom}:${g.monsterId}`] ?? 0) < g.count) reasons.push('BOSS_NOT_KILLED');
    }
  }
  for (let i = 0; i < dungeon.bossRoom; i++) {
    if (roomKillCount(roomKills, i) < roomTotal(dungeon, i) * pol.dungeonRoomClearRatio) {
      reasons.push('ROOMS_NOT_CLEARED');
      break;
    }
  }
  // 2026-10-14 사용자 결정: 빠른 클리어(쩔 포함)는 운영자 확인 대기에 넣지 않는다. 판정은 아래 화력 검사 하나로 한다
  const soft: string[] = [];
  // 시험 서버 전용(TEST_OPEN_ALL_DAYS=1, DEPLOY_STAGE=test): 빨리 깬 판도 보상을 준다
  const relaxed = process.env.DEPLOY_STAGE === 'test' && process.env.TEST_OPEN_ALL_DAYS === '1';
  if (claimed > serverElapsed + ELAPSED_SLACK_SECONDS) soft.push('TIME_OVER_SERVER');
  if (!relaxed && claimed < minClearSeconds(run)) soft.push('TOO_FAST');

  const kills = await killsOfRun(ctx.client, run.id);
  const hpMul = diff.hpMul * partyHpScaleOf(eco, run.humans, run.ai_count, dungeon.isRaid);
  const hpList = kills.map((k) => {
    const def = eco.monsters.get(k.monster_id);
    return def ? effectiveHp(eco, def, k.monster_level, hpMul) : 0;
  });
  // 파티 전원의 화력 상한(판 스냅샷: 사람 상한 합 + AI 몫)으로 이 시간 안에 몬스터 체력 합을 깎을 수 있는지 본다.
  // 여유 IMPLAUSIBLE_TOLERANCE배를 줘서 고레벨 쩔은 통과하고 명백히 불가능한 속도만 걸린다
  if (!relaxed && !powerAllows(eco, pol, (await runAttackCap(ctx, run)) * IMPLAUSIBLE_TOLERANCE, serverElapsed + ELAPSED_SLACK_SECONDS, hpList)) {
    reasons.push('POWER');
  }
  if (!relaxed && stats.revives_used > diff.revives) soft.push('REVIVES_OVER');
  if (!relaxed && stats.max_combo > claimed / eco.player.attackCooldown) soft.push('COMBO_OVER');
  return { reasons, soft, serverElapsed, claimed };
}

/** 필요한 화력이 파티 상한의 이 배수를 넘어야 "불가능"으로 본다 */
export const IMPLAUSIBLE_TOLERANCE = 1.5;

/**
 * 2026-10-14: 불가능한 클리어는 보류(운영자 확인 대기) 대신 보상 없이 바로 닫는다. 이상 기록만 남기고 관리자 대기열에는 넣지 않는다.
 * 상태는 failed, 사유는 hold_reason 칸에 남긴다(held가 아니라서 관리자 보류 목록에 뜨지 않는다)
 */
export async function rejectClear(
  ctx: EconCtx,
  run: repo.RunRow,
  stats: Record<string, unknown>,
  reasons: string[],
  at: Date,
  anomalyKind: 'dungeon_result' | 'party_result' = 'dungeon_result',
): Promise<{ status: number; data: Record<string, unknown> }> {
  await repo.closeRun(ctx.client, run.id, {
    state: 'failed',
    endedAt: at,
    stats,
    rank: null,
    score: null,
    xpGranted: null,
    holdReason: reasons.join(','),
    cards: null,
  });
  await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, anomalyKind, 3, { run_id: run.uuid, reasons, rejected: true });
  return { status: 200, data: { result: 'rejected', reasons } };
}

/** 기록만 남기는 사유가 있으면 이상 기록 한 줄(정산은 계속한다) */
export async function logSoftReasons(ctx: EconCtx, run: repo.RunRow, soft: string[], anomalyKind: 'dungeon_result' | 'party_result' = 'dungeon_result'): Promise<void> {
  if (soft.length === 0) return;
  await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, anomalyKind, 1, { run_id: run.uuid, reasons: soft, settled: true });
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
 * 레이드 보상 최소 인원 재확인(RAID_REWARD_MIN_HUMANS, 기본 1이라 혼자도 통과): 아직 진행 중이거나 클리어를 보고(정산 대기)·확정한 사람만 센다.
 * 실패·보류·이탈로 끝난 사람은 세지 않는다(친구가 바로 실패해 대조를 피하는 것을 막는다).
 * 9단계: 사람 수는 같은 기기·같은 Steam 소유자를 한 사람으로 센 연결 요소 수이고, metBy가 주어지면(CONTRIBUTION_MODE=enforce)
 * 기여 충족 멤버가 한 명 이상 있는 요소만 센다(서 있기만 하는 부계정은 "다른 사람"이 아니다).
 */
async function humansStanding(ctx: EconCtx, run: repo.RunRow): Promise<number> {
  if (run.party_run_id === null) return run.humans;
  // 2026-10-14: 판에 함께 들어가 실패·포기하지 않은 사람을 센다. 보고·정산 순서나 보류(held) 여부와 무관하다
  // (먼저 정산하거나 보류에서 풀린 사람이 혼자로 세지던 문제). 바로 실패해 빠진 사람은 여전히 세지 않는다
  const r = await ctx.client.query<{ character_id: string; device_hash: string | null; steam_key: string | null; install_id: string | null }>(
    `SELECT m.character_id, m.device_hash, m.steam_key, m.install_id FROM party_run_members m
       JOIN dungeon_runs d ON d.id = m.dungeon_run_id
      WHERE m.party_run_id = $1 AND m.state NOT IN ('invited', 'left', 'no_show', 'dropped')
        AND d.state NOT IN ('failed', 'abandoned')`,
    [run.party_run_id],
  );
  const members = r.rows.map((x) => ({ characterId: Number(x.character_id), deviceHash: x.device_hash, steamKey: x.steam_key, installId: x.install_id }));
  return humanGroups(members).length;
}

/** 9단계: 파티 정산이 호스트 관찰로 계산한 이 멤버의 기여(5.3). metByCharacter는 같은 판 모든 멤버의 충족 여부(사람 수 판정용) */
export interface ClearContribution {
  share: number;
  hits: number | null;
  source: 'host' | 'none';
  met: boolean;
  metByCharacter: Map<number, boolean> | null;
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
  /** 9단계: 파티 판이면 호스트 관찰 기반 기여(없으면 판단 불가) */
  contribution?: ClearContribution | null;
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
  const aa = getConfig().aa.contribution;
  let locked = run.reward_locked;
  let reason = run.lock_reason;
  const contrib = aa.mode !== 'off' ? (inp.contribution ?? null) : null;
  // 입장 때 ALREADY_CLAIMED였어도 클리어 시각이 새 기간(리셋 경계를 넘김)이면 그 기간의 청구가 없을 때만 다시 판정한다.
  // 두 번 받는 길은 아래 raid_claims INSERT(PK, ON CONFLICT)가 막는다
  if (dungeon.isRaid && locked && reason === 'ALREADY_CLAIMED' && !(await raidClaimed(ctx.client, ctx.char.id, dungeon, inp.at, run.difficulty))) {
    locked = false;
    reason = null;
  }
  // 판정 순서: ALREADY_CLAIMED(입장 때 미리 잠금) -> LOW_CONTRIBUTION -> TOO_FEW_HUMANS -> KEYS_MISSING -> 청구 INSERT.
  // 잠그면 기간당 1회 청구(raid_claims)가 소진되지 않는다
  // 2026-10-14: 기여 부족은 기록만 한다(힐러·탱커가 딜이 적어도 보상을 받는다)
  if (contrib && !contrib.met && run.party_run_id !== null && reason !== 'ALREADY_CLAIMED') {
    await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'contribution', 1, { run_id: run.uuid, why: 'low_contribution_log', share: contrib.share, hits: contrib.hits });
  }
  let keyGain = 0;
  let keyCost = 0;
  let coreGain = 0;
  if (dungeon.isRaid && !locked) {
    if ((await humansStanding(ctx, run)) < pol.raidRewardMinHumans) {
      locked = true;
      reason = 'TOO_FEW_HUMANS';
    } else if (dungeon.raidTier === 'Final' && dungeon.keyCost > 0 && (await ctx.stackCount('bag', eco.dungeons.keyItem)) < dungeon.keyCost) {
      locked = true;
      reason = 'KEYS_MISSING';
    } else {
      const period = raidPeriod(dungeon, inp.at, run.difficulty);
      const ins = await ctx.client.query(
        `INSERT INTO raid_claims (character_id, dungeon_id, difficulty, period_kind, period_start, dungeon_run_id, claimed_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7) ON CONFLICT DO NOTHING`,
        [ctx.char.id, dungeon.id, run.difficulty, period.kind, period.start, run.id, ctx.now],
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
  let raidGold = 0;
  let lordBonus: { item_key: string; count: number } | null = null;
  if (!locked) {
    let xp = clearXp(eco, dungeon, diff, rank);
    // 9단계 A4: 권장 레벨보다 DUNGEON_UNDERLEVEL_GAP 이상 낮은 멤버는 클리어 경험치를 깎는다(카드·열쇠는 줄이지 않는다)
    const under = underlevelFactor(diff.recommendedLevel - ctx.level, aa);
    if (under < 1) xp = Math.max(1, Math.round(xp * under));
    ({ granted, leveledUp } = await ctx.grantXp(xp, 'dungeon_clear', run.uuid));
    // 선택 전에는 내용을 클라이언트에 주지 않는다
    if (dungeon.isRaid) {
      // 13단계: 난수 순서는 확정 골드 1회 -> 카드 4장(장비 카드는 부위 -> 등급). 골드는 카드를 안 뒤집어도 이 트랜잭션에서 들어간다
      // 영웅 레이드는 확정 골드에 raidHeroic.rewardMul을 곱한다(DungeonDatabase.RaidRewardScale과 같은 식)
      const goldMul = run.difficulty === RAID_HEROIC ? (eco.dungeons.raidHeroic?.rewardMul ?? 1) : 1;
      const rolled = Math.round(rollRaidGold(dungeon, getRng()) * goldMul);
      const before = ctx.gold;
      if (rolled > 0) await ctx.changeGold(rolled, 'raid_gold', run.uuid);
      raidGold = ctx.gold - before; // 골드 상한에 걸리면 실제로 들어간 만큼만
      cards = rollRaidCards(eco, dungeon, ctx.char.class, getRng());
    } else {
      // 2026-10-14: 확정 재료 + 확정 골드 + 운 카드를 모두 받는다(take_all). sure가 없는 옛 데이터만 4장 중 1장
      cards = rollClearRewards(eco, dungeon, diff, ctx.char.class, getRng()) ?? rollCards(eco, dungeon, diff, ctx.char.class, getRng());
      // 요일 던전 유니크 보너스(판당 22%, 7판 연속 실패 뒤 보장, 없는 부위 우선). 적용 대상이 아니면 그대로 돌려준다
      cards = (await applyUniqueBonus(ctx.client, ctx.char.id, eco, dungeon, diff, ctx.char.class, cards, getRng())).cards;
      // 챕터 3 군주: 카드와 별개로 침식 결정·잿빛 인장을 바로 가방에(카드 4장 틀은 그대로)
      lordBonus = rollLordBonus(dungeon, run.difficulty, getRng());
      if (lordBonus) await ctx.addItem('bag', lordBonus.item_key, lordBonus.count, 'dungeon_card', run.uuid);
    }
    if (dungeon.isRaid) {
      if (dungeon.raidTier === 'Final' && dungeon.keyCost > 0) {
        keyCost = dungeon.keyCost;
        const spent = await ctx.removeItem('bag', eco.dungeons.keyItem, keyCost, 'raid_key_cost', run.uuid);
        // 앞의 stackCount 확인 덕에 오지 않는 경로. 열쇠 없이 보상이 나가지 않도록 던져 판 전체를 롤백한다
        if (spent === null) throw new AppError(409, '레이드 열쇠가 부족합니다.', 'RAID_KEY_MISSING');
      } else if (dungeon.keyMax > 0) {
        keyGain = getRng().int(dungeon.keyMin, dungeon.keyMax + 1);
        if (keyGain > 0) await ctx.addItem('bag', eco.dungeons.keyItem, keyGain, 'raid_key', run.uuid);
      }
      // 승급 재료(고대의 핵): 레이드 보상으로만 나온다. 난이도가 높을수록 많다
      const promote = eco.enhance.promote;
      if (promote) {
        const table = dungeon.raidTier === 'Final' ? promote.raidCoreFinal : promote.raidCoreMid;
        coreGain = table[run.difficulty] ?? 0;
        if (coreGain > 0) await ctx.addItem('bag', promote.coreItem, coreGain, 'raid_core', run.uuid);
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
    ...(dungeon.isRaid && cards ? { cardsMode: 'take_all' as const, raidGold } : {}),
    ...(!dungeon.isRaid && cards && dungeon.sure ? { cardsMode: 'take_all' as const } : {}),
    contribution: contrib ? { share: contrib.share, hits: contrib.hits, source: contrib.source, met: contrib.met } : null,
  });
  // 10단계 E3: 보상이 잠기지 않은 요일 던전 직접 클리어는 계정 주간 활동에 +1(그 요청의 마지막 쓰기)
  if (!dungeon.isRaid && !dungeon.always && !locked) await bumpDirectClear(ctx.client, ctx.char.accountId, inp.at);
  const data: Record<string, unknown> = {
    result: 'cleared',
    rank: RANK_NAMES[rank] ?? 'F',
    score,
    granted_xp: granted,
    leveled_up: leveledUp,
    card_count: cards ? cards.length : 0,
    card_mode: cards && (dungeon.isRaid || dungeon.sure) ? 'take_all' : 'pick_one',
    ...(lordBonus ? { lord_bonus: lordBonus } : {}),
    delta: ctx.delta(),
  };
  // 9단계: 파티 던전(레이드 아님)의 기여 잠금은 data.reward_locked / reward_lock_reason 으로 알린다
  if (!dungeon.isRaid && locked) {
    data.reward_locked = true;
    data.reward_lock_reason = reason;
  }
  if (dungeon.isRaid) {
    data.raid = {
      reward_locked: locked,
      ...(reason ? { lock_reason: reason } : {}),
      ...(keyGain > 0 ? { key_gain: keyGain } : {}),
      ...(keyCost > 0 ? { key_cost: keyCost } : {}),
      ...(coreGain > 0 ? { core_gain: coreGain } : {}),
      ...(!locked ? { gold_gain: raidGold, card_mode: 'take_all' } : {}),
    };
  }
  return { status: 200, data };
}

/** 솔로(AI 동반, 레이드) 결과. 파티 판은 partyruns의 접수·정산이 처리한다 */
export async function settleResult(ctx: EconCtx, runUuid: string, body: ResultBody) {
  const run = await repo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
  if (!run || run.state !== 'playing') throw new AppError(409, '진행 중인 던전이 아닙니다.', 'RUN_NOT_PLAYING');
  // 부활 횟수는 서버 기록(revive_log)보다 적게 보고할 수 없다: 줄여 보고해 부활 감점·상한을 피하는 것을 막는다
  const revives = Math.max(body.stats.revives_used, await countDungeonRevives(ctx.client, ctx.char.id, run.started_at));
  const stats = { ...body.stats, revives_used: revives };

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
  if (check.reasons.length > 0) return rejectClear(ctx, run, { ...stats, server_elapsed_s: check.serverElapsed }, check.reasons, ctx.now);
  await logSoftReasons(ctx, run, check.soft);
  // 값 이상은 정산 범위로 자른다(부활은 난이도 한도, 콤보는 시간당 최대)
  const { eco: eco2, diff: diff2 } = runContext(run);
  stats.revives_used = Math.min(stats.revives_used, diff2.revives);
  stats.max_combo = Math.min(stats.max_combo, Math.floor(check.claimed / eco2.player.attackCooldown));
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
