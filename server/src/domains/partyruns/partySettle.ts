// 파티 판의 결과 접수(D2)와 대조·정산(D3). 보상 지급은 멤버마다 자기 요청에서만 한다(다른 멤버의 행은 건드리지 않는다).
import { randomUUID } from 'node:crypto';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { finalizeCleared, holdRun, minClearSeconds, runContext, ELAPSED_SLACK_SECONDS, validateClear, type ClearContribution } from '../dungeons/dungeonResult';
import { contributionsOf, hitsCap, isDisputed, type MemberDamage } from '../antiabuse/contribution';
import { RANK_NAMES } from '../dungeons/dungeonRules';
import type { ResultBody, SettleBody } from '../dungeons/dungeonValidation';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { runEconomy, type EconResult, type StoredResult } from '../economy/economyService';
import { attackCap, effectiveHp } from '../kills/killRules';
import { countDungeonRevives } from '../revive/reviveRepository';
import { killsOfRun } from '../kills/killRepository';
import { lockPartyAndRun } from '../party/partyTx';
import * as repo from './partyRunRepository';
import { mergeStats, reconcile, type ReportFacts, type Tolerance, type Witness } from './reconcile';

const SETTLE_AFTER_MS = 2000;
const notPlaying = () => new AppError(409, '진행 중인 던전이 아닙니다.', 'RUN_NOT_PLAYING');

function roomsOfKills(roomKills: Record<string, number>): Map<number, number> {
  const m = new Map<number, number>();
  for (const [k, v] of Object.entries(roomKills)) {
    const room = Number(k.split(':')[0]);
    m.set(room, (m.get(room) ?? 0) + v);
  }
  return m;
}

/** 멤버의 보고(주장) + 서버가 받은 처치 기록. 보고하지 않았으면 null */
function factsOfRun(r: dungeonRepo.RunRow): ReportFacts | null {
  if (!r.reported_outcome || !r.stats || typeof r.stats.elapsed_ms !== 'number') return null;
  return { outcome: r.reported_outcome, elapsedMs: r.stats.elapsed_ms, rooms: roomsOfKills(r.room_kills) };
}

function factsOfHost(h: repo.HostReportRow): ReportFacts {
  const rooms = new Map<number, number>();
  for (const r of h.rooms) rooms.set(r.room_index, (rooms.get(r.room_index) ?? 0) + r.kills.reduce((a, k) => a + k.count, 0));
  return { outcome: h.outcome, elapsedMs: h.elapsed_ms, rooms };
}

// ---------- D2: 파티 판 결과 접수 ----------

export async function reportPartyResult(ctx: EconCtx, run: dungeonRepo.RunRow, body: ResultBody): Promise<EconResult> {
  const partyRunUuid = await dungeonRepo.partyRunUuid(ctx.client, run.party_run_id as number);
  const locked = partyRunUuid ? await lockPartyAndRun(ctx.client, partyRunUuid) : null;
  if (!locked) throw notPlaying();
  const pr = locked.run;
  const member = (await repo.runMembers(ctx.client, pr.id)).find((m) => m.character_id === ctx.char.id);
  if (pr.state !== 'playing' || !member || !['playing', 'disconnected'].includes(member.state)) throw notPlaying();
  const stats = body.stats;

  let result: EconResult;
  if (body.outcome === 'failed') {
    // 실패는 즉시 닫는다(지급이 없어 대조가 필요 없다)
    await dungeonRepo.closeRun(ctx.client, run.id, {
      state: 'failed', endedAt: ctx.now, stats, rank: null, score: null, xpGranted: null, holdReason: null, cards: null,
    });
    await ctx.client.query("UPDATE dungeon_runs SET reported_outcome = 'failed', reported_at = $2 WHERE id = $1", [run.id, ctx.now]);
    await repo.setMemberState(ctx.client, pr.id, ctx.char.id, 'done', ctx.now);
    await repo.setFirstReport(ctx.client, pr.id, ctx.now);
    result = { status: 200, data: { result: 'failed' } };
  } else {
    // 먼저 자기 검증(구조, 시간, 화력, 값 범위)
    const check = await validateClear(ctx, run, stats, ctx.now);
    if (check.reasons.length > 0) {
      await holdRun(ctx, run, { ...stats, server_elapsed_s: check.serverElapsed }, check.reasons, ctx.now);
      await repo.setMemberState(ctx.client, pr.id, ctx.char.id, 'done', ctx.now);
      result = { status: 200, data: { result: 'held' } };
    } else {
      await dungeonRepo.markReported(ctx.client, run.id, 'cleared', stats, ctx.now);
      await repo.setMemberState(ctx.client, pr.id, ctx.char.id, 'done', ctx.now);
      await repo.setFirstReport(ctx.client, pr.id, ctx.now);
      const fresh = (await dungeonRepo.findRunById(ctx.client, run.id)) as dungeonRepo.RunRow;
      result = await settleOne(ctx, fresh);
    }
  }
  await repo.endRunIfDone(ctx.client, pr, ctx.now);
  return result;
}

// ---------- D3: 대조·정산 ----------

export function settleRun(accountId: number, characterUuid: string, runUuid: string, body: SettleBody): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/dungeon-runs/:run_id/settle',
    requestId: body.request_id,
    payload: { run_id: runUuid },
    handler: async (ctx) => {
      const run = await dungeonRepo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
      if (!run) throw new AppError(404, '던전 기록을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
      if (run.state === 'playing') throw new AppError(409, '아직 결과를 보고하지 않았습니다.', 'RUN_NOT_REPORTED');
      if (run.state === 'abandoned') throw notPlaying();
      return settleOne(ctx, run);
    },
  });
}

function storedResult(run: dungeonRepo.RunRow): EconResult {
  if (run.state === 'held') return { status: 200, data: { result: 'held' } };
  if (run.state === 'failed') return { status: 200, data: { result: 'failed' } };
  const dungeon = getGameData().economy.dungeons.byId.get(run.dungeon_id);
  const data: Record<string, unknown> = {
    result: 'cleared',
    rank: RANK_NAMES[run.rank ?? 8] ?? 'F',
    score: run.score,
    granted_xp: run.xp_granted ?? 0,
    card_count: run.cards ? run.cards.length : 0,
  };
  if (dungeon?.isRaid) {
    data.raid = {
      reward_locked: run.reward_locked,
      ...(run.lock_reason ? { lock_reason: run.lock_reason } : {}),
      ...(!run.reward_locked && run.cards_mode === 'take_all' ? { gold_gain: run.raid_gold, card_mode: run.cards_mode } : {}),
    };
  }
  else if (run.reward_locked) {
    data.reward_locked = true;
    data.reward_lock_reason = run.lock_reason;
  }
  return { status: 200, data };
}

/** 내 행 하나만 정산한다 */
async function settleOne(ctx: EconCtx, run: dungeonRepo.RunRow): Promise<EconResult> {
  if (run.state !== 'reported') return storedResult(run);
  const pol = getConfig().policy;
  const { eco, dungeon } = runContext(run);
  const pr = (await repo.getRunById(ctx.client, run.party_run_id as number)) as NonNullable<Awaited<ReturnType<typeof repo.getRunById>>>;
  const members = await repo.runMembers(ctx.client, pr.id);
  const all = await dungeonRepo.runsOfPartyRun(ctx.client, pr.id);
  const reports = await repo.hostReports(ctx.client, pr.id);
  const h = reports[0] ?? null; // 가장 최근 세대의 방장 보고
  const mine = factsOfRun(run) as ReportFacts;
  const iAmHost = pr.host_character_id === ctx.char.id;

  const hostEval = h ? await validateHostReport(ctx, run, pr, members, h) : null;
  const deadline = pr.first_report_at ? pr.first_report_at.getTime() + pol.partyResultWaitSeconds * 1000 : Number.POSITIVE_INFINITY;
  const pastDeadline = ctx.now.getTime() >= deadline;
  // 방장 보고(H)는 방장 자신의 결과 보고와 같아야 한다. 방장이 아직 보고 전이면 마감까지 기다린다
  if (h && hostEval) {
    const hostRun = all.find((r) => r.character_id === h.host_character_id);
    if (hostRun && hostRun.id !== run.id && hostRun.state === 'playing' && !pastDeadline) {
      return { status: 200, data: { result: 'pending', settle_after_ms: SETTLE_AFTER_MS } };
    }
    const own = hostRun ? (hostRun.reported_outcome ?? (hostRun.state === 'failed' ? 'failed' : hostRun.state === 'cleared' ? 'cleared' : null)) : null;
    if (!hostRun || hostRun.state === 'abandoned' || (own !== null && own !== h.outcome) || (hostRun.state === 'playing' && hostRun.id !== run.id)) {
      hostEval.push('HOST_SELF_MISMATCH');
      if (!iAmHost) {
        const host = members.find((m) => m.character_id === h.host_character_id);
        if (host) {
          await econRepo.insertAnomaly(ctx.client, host.account_id, host.character_id, 'party_host', 3, {
            run_id: run.uuid,
            reasons: ['HOST_SELF_MISMATCH'],
            reported: own,
            host_report: h.outcome,
          });
        }
      }
    }
  }
  const hostValid = hostEval !== null && hostEval.length === 0;

  const witnesses: Witness[] = members
    .filter((m) => m.character_id !== ctx.char.id && !['left', 'no_show', 'dropped'].includes(m.state))
    .map((m) => {
      const r = all.find((x) => x.id === m.dungeon_run_id);
      return { isHost: m.character_id === pr.host_character_id, report: r ? factsOfRun(r) : null };
    });
  const tol: Tolerance = {
    elapsedMs: pol.partyElapsedToleranceMs,
    killRatio: pol.partyKillSlackRatio,
    roomSizes: dungeon.rooms.map((_r, i) => dungeon.rooms[i]?.groups.reduce((a, g) => a + g.count, 0) ?? 0),
  };
  const decision = reconcile({
    iAmHost,
    mine,
    hostReport: hostValid && h ? factsOfHost(h) : null,
    witnesses,
    pastDeadline,
    tol,
  });

  if (decision.kind === 'pending') return { status: 200, data: { result: 'pending', settle_after_ms: SETTLE_AFTER_MS } };

  // 방장 보고가 무효였다는 사실은 방장의 정산 때 한 번 남긴다
  if (iAmHost && hostEval && hostEval.length > 0) {
    await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'party_host', 3, { run_id: run.uuid, reasons: hostEval });
  }
  if (decision.kind === 'held') {
    await holdRun(ctx, run, (run.stats ?? {}) as Record<string, unknown>, [decision.reason], ctx.now, 'party_result');
    return { status: 200, data: { result: 'held' } };
  }
  if (decision.hostOutlier) {
    // 다른 멤버의 보고가 내 보고와 맞고 방장 보고만 어긋났다: 방장이 이상치
    const host = members.find((m) => m.character_id === pr.host_character_id);
    if (host) {
      await econRepo.insertAnomaly(ctx.client, host.account_id, host.character_id, 'party_host', 3, {
        run_id: run.uuid,
        reasons: ['HOST_OUTLIER'],
      });
    }
  }

  // 정산 값 합성: 피격·부활은 큰 값, 콤보는 작은 값. H가 무효면 내 값을 쓴다
  const st = run.stats as { elapsed_ms: number; hits_taken: number; max_combo: number; revives_used: number; damage_dealt?: number; hits_landed?: number };
  const hm = hostValid && h ? h.members.find((m) => m.character_id === ctx.char.uuid) : undefined;
  const merged = mergeStats({
    // 내 부활 횟수는 서버 기록(revive_log)보다 적을 수 없다
    mine: { hits: st.hits_taken, combo: st.max_combo, revives: Math.max(st.revives_used, await countDungeonRevives(ctx.client, ctx.char.id, run.started_at)) },
    host: hm ? { hits: hm.hits_taken, combo: hm.max_combo, revives: hm.revives_used } : null,
  });
  if (merged.outlier) {
    // 방장이 멤버의 피격·콤보·부활을 허용 범위 밖의 값으로 보고했다: 멤버 자기 값을 쓰고 방장을 기록한다
    const host = members.find((m) => m.character_id === pr.host_character_id);
    if (host && host.character_id !== ctx.char.id) {
      await econRepo.insertAnomaly(ctx.client, host.account_id, host.character_id, 'party_host', 2, {
        run_id: run.uuid,
        reasons: ['HOST_STATS_OUTLIER'],
        target: ctx.char.uuid,
      });
    }
  }
  const claimed = st.elapsed_ms / 1000;
  const serverElapsed = ((run.reported_at as Date).getTime() - run.started_at.getTime()) / 1000;

  // ---- 9단계 5.2~5.3: 호스트 관찰로 기여를 판정한다 ----
  const aa = getConfig().aa.contribution;
  let contribution: ClearContribution | null = null;
  if (aa.mode !== 'off') {
    if (hostValid && h) {
      const minShare = dungeon.isRaid ? aa.raidMinShare : aa.dungeonMinShare;
      const minHits = dungeon.isRaid ? aa.raidMinHits : aa.dungeonMinHits;
      const byUuid = new Map(members.map((m) => [m.character_uuid, m] as const));
      const entries = new Map<number, MemberDamage>();
      for (const m of members) if (!['no_show', 'dropped'].includes(m.state)) entries.set(m.character_id, { id: m.character_id, damage: 0, hits: null });
      for (const hm of h.members) {
        const m = byUuid.get(hm.character_id);
        if (m) entries.set(m.character_id, { id: m.character_id, damage: hm.damage_dealt, hits: hm.hits_landed ?? null });
      }
      const list = [...entries.values()];
      const all = contributionsOf(list, minShare, minHits);
      const metBy = new Map([...all].map(([id, c]) => [id, c.met] as const));
      const mine2 = all.get(ctx.char.id) ?? { share: 0, hits: null, met: false };
      const hostMine = entries.get(ctx.char.id);
      const disputed = isDisputed(
        { damage: hostMine?.damage ?? 0, hits: hostMine?.hits ?? null, totalDamage: list.reduce((a, e) => a + e.damage, 0) },
        { damage: st.damage_dealt, hits: st.hits_landed },
        { ratio: aa.disputeRatio, minShare, minHits },
      );
      if (disputed) {
        if (aa.mode === 'enforce') {
          await holdRun(ctx, run, (run.stats ?? {}) as Record<string, unknown>, ['CONTRIBUTION_DISPUTE'], ctx.now, 'party_result');
          return { status: 200, data: { result: 'held' } };
        }
        await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'contribution', 2, { run_id: run.uuid, why: 'dispute_log', claimed_damage: st.damage_dealt ?? null, claimed_hits: st.hits_landed ?? null });
      }
      contribution = { share: mine2.share, hits: mine2.hits, source: 'host', met: mine2.met, metByCharacter: metBy };
    } else if (dungeon.isRaid && aa.mode === 'enforce') {
      // 호스트 보고를 무효로 해 기여 판정을 피하는 짝을 막는다: 그 멤버 정산을 보류하고 관리자가 검토한다
      await holdRun(ctx, run, (run.stats ?? {}) as Record<string, unknown>, ['CONTRIBUTION_UNKNOWN'], ctx.now, 'party_result');
      return { status: 200, data: { result: 'held' } };
    } else {
      // 파티 던전(레이드 아님)은 잠그지 않는다: 판단 불가만 기록
      await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'contribution', 1, { run_id: run.uuid, why: 'host_report_unusable', raid: dungeon.isRaid });
    }
  }
  return finalizeCleared(ctx, run, {
    contribution,
    hitsTaken: merged.hits,
    maxCombo: merged.combo,
    revivesUsed: merged.revives,
    scoredSeconds: Math.max(claimed, serverElapsed - pol.dungeonTimeOverheadSeconds),
    stats: { ...st, hits_taken: merged.hits, max_combo: merged.combo, revives_used: merged.revives },
    at: ctx.now,
  });
}

// ---------- 8.2 방장 보고의 자체 검증 ----------

/** 무효 사유 목록(비어 있으면 유효). DB는 읽기만 한다 */
async function validateHostReport(
  ctx: EconCtx,
  mine: dungeonRepo.RunRow,
  pr: NonNullable<Awaited<ReturnType<typeof repo.getRunById>>>,
  members: repo.RunMemberRow[],
  h: repo.HostReportRow,
): Promise<string[]> {
  const pol = getConfig().policy;
  const { eco, dungeon, diff } = runContext(mine);
  const reasons: string[] = [];

  // 1. 방 구성을 넘는 처치 불가
  for (const room of h.rooms) {
    const def = dungeon.rooms[room.room_index];
    if (!def) {
      reasons.push('ROOM_UNKNOWN');
      continue;
    }
    for (const k of room.kills) {
      const cap = def.groups.filter((g) => g.monsterId === k.monster_id).reduce((a, g) => a + g.count, 0);
      if (k.count > cap) reasons.push('KILLS_OVER_ROOM');
    }
  }
  // 2. 시간: 보고 시각 기준 서버 경과 + 5초 이하, 최소 클리어 시간 이상
  const serverElapsed = ((h.created_at.getTime() - (pr.begun_at as Date).getTime()) / 1000);
  if (h.elapsed_ms / 1000 > serverElapsed + ELAPSED_SLACK_SECONDS) reasons.push('TIME_OVER_SERVER');
  if (h.outcome === 'cleared' && h.elapsed_ms / 1000 < minClearSeconds(mine)) reasons.push('TOO_FAST');
  // 3. 화력 일관성
  const kills = await killsOfRun(ctx.client, mine.id);
  const hpMul = diff.hpMul * (eco.dungeons.partyHpScale[mine.party_size - 1] ?? 1);
  const hpSum = kills.reduce((a, k) => {
    const def = eco.monsters.get(k.monster_id);
    return a + (def ? effectiveHp(eco, def, k.monster_level, hpMul) : 0);
  }, 0);
  if (h.ai.length > mine.ai_count) reasons.push('AI_OVER_COUNT');
  const dmgSum = h.members.reduce((a, m) => a + m.damage_dealt, 0) + h.ai.reduce((a, m) => a + m.damage_dealt, 0);
  if (dmgSum < pol.partyDamageMinRatio * hpSum) reasons.push('DAMAGE_TOO_LOW');
  const byUuid = new Map(members.map((m) => [m.character_uuid, m] as const));
  // AI 한 명의 딜 상한: 가장 센 사람의 상한 x 용병 딜 배율 (사람 한 명 몫과 같은 식)
  const humanCaps: number[] = [];
  for (const hm of h.members) {
    const m = byUuid.get(hm.character_id);
    if (m) humanCaps.push(attackCap(eco, pol, m.level, await econRepo.listWornKeys(ctx.client, m.character_id)));
  }
  const aiCap = Math.max(0, ...humanCaps) * eco.dungeons.mercenary.damageScale;
  const aiDps = (aiCap / eco.player.attackCooldown) * pol.powerSkillFactor;
  for (const a of h.ai) {
    if (a.damage_dealt > aiDps * (serverElapsed + ELAPSED_SLACK_SECONDS) * pol.powerAoeCap) reasons.push('AI_DAMAGE_OVER_CAP');
  }
  for (const hm of h.members) {
    const m = byUuid.get(hm.character_id);
    if (!m) {
      reasons.push('MEMBER_UNKNOWN');
      continue;
    }
    const cap = attackCap(eco, pol, m.level, await econRepo.listWornKeys(ctx.client, m.character_id));
    const dpsCap = (cap / eco.player.attackCooldown) * pol.powerSkillFactor;
    if (hm.damage_dealt > dpsCap * (serverElapsed + ELAPSED_SLACK_SECONDS) * pol.powerAoeCap) reasons.push('DAMAGE_OVER_CAP');
    // 9단계: 적중 수의 물리 상한(콤보 검사와 같은 식)
    if (hm.hits_landed !== undefined && hm.hits_landed > hitsCap(serverElapsed, ELAPSED_SLACK_SECONDS, eco.player.attackCooldown, pol.powerAoeCap)) reasons.push('HITS_OVER_CAP');
    // 4. 부활
    if (hm.revives_used > diff.revives) reasons.push('REVIVES_OVER');
  }
  return [...new Set(reasons)];
}

// ---------- 서버 틱: 클라이언트가 정산을 부르지 않아 멈춘 reported 행 ----------

/** 대기 마감(first_report_at + 대기 시간)이 지난 reported 행을 정산하거나 보류한다. 서버가 1초마다 부른다 */
export async function runSettleTick(): Promise<void> {
  const pol = getConfig().policy;
  const now = getNow();
  const r = await getPool().query<{ uuid: string; account_id: string; char_uuid: string }>(
    `SELECT d.uuid, c.account_id, c.uuid AS char_uuid
       FROM dungeon_runs d
       JOIN party_runs p ON p.id = d.party_run_id
       JOIN characters c ON c.id = d.character_id
      WHERE d.state = 'reported' AND p.first_report_at IS NOT NULL
        AND p.first_report_at + ($1::int * interval '1 second') <= $2
      LIMIT 50`,
    [pol.partyResultWaitSeconds, now],
  );
  for (const row of r.rows) {
    try {
      await runEconomy({
        accountId: Number(row.account_id),
        characterUuid: row.char_uuid,
        endpoint: 'TICK settle',
        requestId: randomUUID(),
        payload: { run_id: row.uuid },
        handler: async (ctx) => {
          const run = await dungeonRepo.findRunByUuid(ctx.client, ctx.char.id, row.uuid);
          return run ? settleOne(ctx, run) : { status: 200, data: { result: 'none' } };
        },
      });
    } catch (err) {
      logger.error({ err }, 'settle tick failed');
    }
  }
}
