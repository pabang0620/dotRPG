// 파티 판의 시작 단계: 출발(R1), 조회(R2), 입장 확인(R3), 판 시작(R4), 입장 마감 지연 처리.
import { randomBytes } from 'node:crypto';
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { checkEntry, lockAtEntry } from '../dungeons/entryRules';
import * as econRepo from '../economy/economyRepository';
import type { StoredResult } from '../economy/economyService';
import { attackCap } from '../kills/killRules';
import * as partyRepo from '../party/partyRepository';
import { lockPartyAndRun, runParty, withPartyLocks, type PartyCtx, type PartyRunRow } from '../party/partyTx';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { closeMembershipOf } from '../fieldsessions/fieldCore';
import { pickTransport } from '../transport/pickTransport';
import { humanGroups } from '../antiabuse/contribution';
import { snapshotRunHumans } from '../antiabuse/humans';
import * as repo from './partyRunRepository';
import { buildRunView } from './runView';
import { entryToken, tokenMatches } from './runTokens';
import type { JoinBody, RequestOnlyBody, StartBody } from './partyRunValidation';

const notFound = () => new AppError(404, '판을 찾을 수 없습니다.', 'RUN_NOT_FOUND');

// ---------- R1 POST /party/start ----------

export function startRun(accountId: number, characterUuid: string, body: StartBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party/start',
    requestId,
    payload,
    lock: { kind: 'party' },
    handler: (ctx) => processStart(ctx, body),
  });
}

async function processStart(ctx: PartyCtx, body: StartBody) {
  const cfg = getConfig();
  const eco = getGameData().economy;
  const partyId = await partyRepo.findPartyIdOf(ctx.client, ctx.char.id);
  if (partyId === null) throw new AppError(404, '속한 파티가 없습니다.', 'NOT_IN_PARTY');
  const party = await partyRepo.lockParty(ctx.client, partyId);
  if (!party || party.state === 'closed') throw new AppError(404, '속한 파티가 없습니다.', 'NOT_IN_PARTY');
  if (party.leader_character_id !== ctx.char.id) throw new AppError(403, '방장만 할 수 있습니다.', 'NOT_LEADER');
  if (party.state !== 'forming') throw new AppError(409, '이미 출발했습니다.', 'PARTY_BUSY');

  const all = await partyRepo.activeMembers(ctx.client, partyId);
  const dungeon = eco.dungeons.byId.get(party.dungeon_id);
  if (!dungeon) throw new AppError(422, '알 수 없는 던전입니다.', 'DUNGEON_UNKNOWN');
  // 파티는 하나다: 준비 확인 없이 방장이 출발하면 접속한 파티원은 자동으로 들어오고(입장 마감까지 안 오면 AI가 그 자리),
  // 들어갈 수 없는 파티원(오늘 횟수 소진 등)은 이번 판만 빠진다. 방장 자신이 못 들어가면 출발하지 않는다.
  for (const m of all) {
    if (await dungeonRepo.inPartyRun(ctx.client, m.character_id)) {
      throw new AppError(409, '이미 판에 참여 중인 멤버가 있습니다.', 'IN_PARTY_RUN');
    }
  }
  // 멤버별 자격 재판정(생성·신청 시점의 통과는 자리 예약이 아니다)
  const sittingOut: { character_id: string; name: string; code: string }[] = [];
  const members: typeof all = [];
  for (const m of all) {
    const c = ctx.chars.get(m.character_id);
    if (!c) throw new AppError(409, '파티 구성이 바뀌었습니다.', 'PARTY_CHANGED');
    try {
      await checkEntry(ctx.client, c, party.dungeon_id, party.difficulty, ctx.now);
      members.push(m);
    } catch (err) {
      if (!(err instanceof AppError)) throw err;
      if (m.character_id === party.leader_character_id) throw err; // 방장이 못 들어가면 그 이유 그대로
      sittingOut.push({ character_id: m.character_uuid, name: m.name, code: err.code ?? 'ERROR' });
    }
  }
  const h = members.length;
  if (h + body.ai_count > dungeon.maxParty || h + body.ai_count > 4) {
    throw new AppError(422, '파티 인원이 너무 많습니다.', 'PARTY_TOO_BIG');
  }
  // 8단계: 전송은 서버가 정한다(자격이 되는 후보가 없으면 422 TRANSPORT_UNAVAILABLE). Steam 없는 멤버도 중계로 들어온다
  const picked = await pickTransport(ctx.client, members.map((m) => m.account_id));
  // 출발하는 멤버 전원의 필드 세션을 닫는다(출발이 막히지 않는다, 같은 트랜잭션)
  for (const m of members) await closeMembershipOf(ctx.client, m.character_id, 'dungeon_start', ctx.now);

  const runKey = randomBytes(32);
  try {
    const run = await repo.insertRun(ctx.client, {
      partyId,
      dungeonId: party.dungeon_id,
      difficulty: party.difficulty,
      hostId: ctx.char.id,
      humans: h,
      aiCount: body.ai_count,
      runKey,
      deadline: new Date(ctx.now.getTime() + cfg.policy.partyGatherSeconds * 1000),
      now: ctx.now,
      transport: picked.transport,
      transportOrder: picked.order,
    });
    // 자리: 방장 0, 나머지는 파티 입장 순
    const ordered = [...members].sort((a, b) => {
      if (a.character_id === ctx.char.id) return -1;
      if (b.character_id === ctx.char.id) return 1;
      return a.joined_at.getTime() - b.joined_at.getTime() || a.member_id - b.member_id;
    });
    for (let i = 0; i < ordered.length; i++) {
      const m = ordered[i] as (typeof ordered)[number];
      await repo.insertRunMember(ctx.client, run.id, m.character_id, m.account_id, i, m.character_id === ctx.char.id ? 'joined' : 'invited', ctx.now);
    }
    await repo.setPartyState(ctx.client, partyId, 'starting');
    const view = await buildRunView(ctx.client, run, ctx.char.id);
    return { status: 201, data: { run: view, host_key: runKey.toString('base64url'), sitting_out: sittingOut } };
  } catch (err) {
    if (isUniqueViolation(err)) throw new AppError(409, '이미 판에 참여 중입니다.', 'IN_PARTY_RUN');
    throw err;
  }
}

// ---------- begin(자동·수동 공통) ----------

/** 판을 시작한다. 멤버 전원이 잠겨 있어야 한다. 시작하면 'begun', 아무도 못 들어가면 취소하고 'cancelled' */
async function beginRun(ctx: PartyCtx, run: PartyRunRow): Promise<'begun' | 'cancelled'> {
  const eco = getGameData().economy;
  const pol = getConfig().policy;
  const dungeon = eco.dungeons.byId.get(run.dungeon_id);
  if (!dungeon) throw new Error(`던전 데이터가 없습니다: ${run.dungeon_id}`);
  const members = await repo.runMembers(ctx.client, run.id);

  // 1. 들어오지 않은 사람은 불참(입장 횟수를 쓰지 않는다)
  for (const m of members.filter((x) => x.state === 'invited')) {
    await repo.setMemberState(ctx.client, run.id, m.character_id, 'no_show', ctx.now);
  }
  // 2. 남은 멤버 자격 재확인
  const standing: repo.RunMemberRow[] = [];
  for (const m of members.filter((x) => x.state === 'joined')) {
    const c = ctx.chars.get(m.character_id);
    let ok = !!c;
    if (c) {
      try {
        await checkEntry(ctx.client, c, run.dungeon_id, run.difficulty, ctx.now);
        const playing = await dungeonRepo.findPlayingRun(ctx.client, c.id);
        if (playing) {
          if (ctx.now.getTime() - playing.started_at.getTime() <= pol.runStaleSeconds * 1000) ok = false;
          else await dungeonRepo.abandonRun(ctx.client, playing.id, ctx.now);
        }
      } catch (err) {
        if (!(err instanceof AppError)) throw err;
        ok = false;
      }
    }
    if (ok) standing.push(m);
    else await repo.setMemberState(ctx.client, run.id, m.character_id, 'dropped', ctx.now);
  }
  const hostStands = standing.some((m) => m.character_id === run.host_character_id);
  if (standing.length === 0 || !hostStands) {
    await repo.cancelRun(ctx.client, run.id, standing.length === 0 ? 'nobody_joined' : 'ineligible', ctx.now);
    await repo.setPartyState(ctx.client, run.party_id, 'forming');
    return 'cancelled';
  }

  // 9단계 5.1: 판 시작 때 각 멤버 세션의 기기·Steam 소유자를 스냅샷으로 남기고, 같은 기기·같은 Steam 소유자는 한 사람으로 센다.
  // 파티 크기(AI 채우기·몬스터 체력 배율)는 실제 머릿수 그대로이고, 보상 최소 인원(TOO_FEW_HUMANS) 판정에만 "사람 수"를 쓴다
  const humanKeys = await snapshotRunHumans(ctx.client, run.id, standing.map((m) => ({ characterId: m.character_id, accountId: m.account_id })));
  const humanCount = humanGroups(standing.map((m) => humanKeys.get(m.character_id) ?? { deviceHash: null, steamKey: null, installId: null })).length;

  // 3. 불참한 자리는 AI가 채운다(합 4 이하)
  const humans = standing.length;
  const aiCount = Math.max(0, Math.min(eco.dungeons.mercenary.maxCompanions, 4 - humans, run.ai_count + (run.humans - humans)));
  // 5. 파티 화력 상한: 사람 멤버 attackCap 합 + AI 수 x 용병 딜 배율 x 가장 센 사람의 attackCap
  const caps: number[] = [];
  for (const m of standing) {
    const c = ctx.chars.get(m.character_id) as econRepo.LockedChar;
    caps.push(attackCap(eco, pol, c.level, await econRepo.listWornKeys(ctx.client, c.id)));
  }
  const powerCap = caps.reduce((a, b) => a + b, 0) + aiCount * eco.dungeons.mercenary.damageScale * Math.max(...caps);

  // 4. 멤버마다 dungeon_runs(전원 같은 started_at). 요일 던전은 입장 횟수를 쓴다
  const resetDay = new Date(resetBoundaries(ctx.now).dailyStartAt);
  for (const m of standing) {
    const lock = await lockAtEntry(ctx.client, m.character_id, dungeon, humanCount, ctx.now);
    const dr = await dungeonRepo.insertRun(ctx.client, {
      characterId: m.character_id,
      dungeonId: run.dungeon_id,
      difficulty: run.difficulty,
      resetDay,
      startedAt: ctx.now,
      humans,
      aiCount,
      countsEntry: !dungeon.isRaid,
      rewardLocked: lock.locked,
      lockReason: lock.reason,
      powerCap,
      partyRunId: run.id,
      slot: m.slot,
    });
    await repo.setMemberState(ctx.client, run.id, m.character_id, 'playing', ctx.now, { dungeonRunId: dr.id });
  }
  // 6. 판과 파티 상태
  await repo.updateRunBegin(ctx.client, run.id, humans, aiCount, powerCap, ctx.now);
  await repo.setPartyState(ctx.client, run.party_id, 'in_run');
  return 'begun';
}

/** 입장 마감이 지난 모으는 중 판: 들어온 사람으로 시작하거나 취소한다 */
async function resolveDeadline(ctx: PartyCtx, run: PartyRunRow): Promise<PartyRunRow> {
  if (run.state === 'gathering' && ctx.now.getTime() >= run.gather_deadline_at.getTime()) {
    await beginRun(ctx, run);
    return (await repo.getRunById(ctx.client, run.id)) as PartyRunRow;
  }
  return run;
}

// ---------- R2 GET /party-runs/{id} ----------

export async function getRun(accountId: number, characterUuid: string, runUuid: string) {
  const db = getPool();
  const me = await db.query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL', [
    characterUuid,
    accountId,
  ]);
  if (!me.rows[0]) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const meId = Number(me.rows[0].id);
  let run = await repo.getRunByUuid(db, runUuid);
  if (!run) throw notFound();
  const members = await repo.runMembers(db, run.id);
  if (!members.some((m) => m.character_id === meId)) throw notFound();
  if (await ensureGatherResolved(accountId, characterUuid, run)) {
    run = (await repo.getRunByUuid(db, runUuid)) as PartyRunRow;
  }
  const view = await buildRunView(db, run, meId);
  // 서버 주도 인계·앱 재시작 뒤 복구에 필요하다: 호스트 본인에게만(이미 R1·R6 응답으로 같은 사람에게 주던 값)
  const hostKey = run.host_character_id === meId && run.run_key && ['gathering', 'playing'].includes(run.state) ? run.run_key.toString('base64url') : null;
  return { run: view, host_key: hostKey };
}

/** 입장 마감이 지난 모으는 중 판이면 지연 처리(시작 또는 취소)하고 true */
export async function ensureGatherResolved(accountId: number, characterUuid: string, run: PartyRunRow): Promise<boolean> {
  if (run.state !== 'gathering' || getNow().getTime() < run.gather_deadline_at.getTime()) return false;
  await withPartyLocks(accountId, characterUuid, { kind: 'run', runUuid: run.uuid }, async (ctx) => {
    const locked = await lockPartyAndRun(ctx.client, run.uuid);
    if (locked) await resolveDeadline({ ...ctx, requestId: '' }, locked.run);
  });
  return true;
}

/** 입장을 확인한다. 마지막 사람이 들어오면 같은 트랜잭션에서 시작한다. 시작했으면 true */
async function markJoined(ctx: PartyCtx, run: PartyRunRow, members: repo.RunMemberRow[]): Promise<boolean> {
  await repo.setMemberState(ctx.client, run.id, ctx.char.id, 'joined', ctx.now);
  const stillInvited = members.some((m) => m.character_id !== ctx.char.id && m.state === 'invited');
  return !stillInvited && (await beginRun(ctx, run)) === 'begun';
}

/**
 * 중계 hello 성공이 입장 확인이다(R3 자동 입장, 멱등). 모으는 중이면 입장 확인(마지막이면 시작),
 * 끊겨 있던 멤버가 60초 안에 돌아오면 playing으로 되돌린다.
 */
export async function relayJoin(accountId: number, characterUuid: string, runUuid: string): Promise<void> {
  const pol = getConfig().policy;
  await withPartyLocks(accountId, characterUuid, { kind: 'run', runUuid }, async (base) => {
    const ctx = { ...base, requestId: '' };
    const locked = await lockPartyAndRun(ctx.client, runUuid);
    if (!locked) return;
    const run = await resolveDeadline(ctx, locked.run);
    const members = await repo.runMembers(ctx.client, run.id);
    const me = members.find((m) => m.character_id === ctx.char.id);
    if (!me) return;
    if (run.state === 'gathering' && me.state === 'invited' && run.run_key) {
      await markJoined(ctx, run, members);
    } else if (run.state === 'playing' && me.state === 'disconnected') {
      const since = me.disconnected_at ? ctx.now.getTime() - me.disconnected_at.getTime() : 0;
      if (since <= pol.partyRejoinSeconds * 1000) await repo.setMemberState(ctx.client, run.id, me.character_id, 'playing', ctx.now);
    } else if (me.state === 'joined' || me.state === 'playing') {
      await repo.touchMember(ctx.client, run.id, me.character_id, ctx.now);
    }
  });
}

// ---------- R3 POST /party-runs/{id}/join ----------

export function joinRun(accountId: number, characterUuid: string, runUuid: string, body: JoinBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party-runs/:id/join',
    requestId,
    payload: { run_id: runUuid, ...payload },
    lock: { kind: 'run', runUuid },
    handler: async (ctx) => {
      const locked = await lockPartyAndRun(ctx.client, runUuid);
      if (!locked) throw notFound();
      let run = locked.run;
      const members = await repo.runMembers(ctx.client, run.id);
      const me = members.find((m) => m.character_id === ctx.char.id);
      if (!me) throw notFound();
      if (['joined', 'playing', 'disconnected'].includes(me.state)) {
        // 이미 들어와 있으면 성공으로 본다
        return { status: 200, data: { state: me.state, begun: run.state === 'playing', run: await buildRunView(ctx.client, run, ctx.char.id) } };
      }
      run = await resolveDeadline(ctx, run);
      if (run.state !== 'gathering' || me.state !== 'invited' || !run.run_key) {
        throw new AppError(409, '입장을 받는 중이 아닙니다.', 'RUN_NOT_GATHERING');
      }
      // 중계 전송은 서버가 연결을 인증하므로 입장 토큰이 필요 없다. 주어졌으면 검사한다(Steam·dev는 필수)
      if (run.transport !== 'relay' && body.entry_token === undefined) {
        throw new AppError(400, 'entry_token 이 필요합니다.', 'VALIDATION', { fields: [{ path: 'entry_token', message: '필수 값입니다' }] });
      }
      if (body.entry_token !== undefined && !tokenMatches(entryToken(run.run_key, run.uuid, me.character_uuid, me.slot), body.entry_token)) {
        throw new AppError(403, '입장 토큰이 올바르지 않습니다.', 'ENTRY_TOKEN_INVALID');
      }
      if (run.transport === 'steam') {
        const host = members.find((m) => m.character_id === run.host_character_id);
        const steam = host ? (await repo.steamIdsOf(ctx.client, [host.account_id])).get(host.account_id) : undefined;
        if (!body.host_steam_id) throw new AppError(400, 'host_steam_id 가 필요합니다.', 'VALIDATION');
        if (!steam || steam !== body.host_steam_id) throw new AppError(422, '방장 Steam ID가 다릅니다.', 'HOST_MISMATCH');
      }
      const begun = await markJoined(ctx, run, members);
      const fresh = (await repo.getRunById(ctx.client, run.id)) as PartyRunRow;
      return { status: 200, data: { state: begun ? 'playing' : 'joined', begun, run: await buildRunView(ctx.client, fresh, ctx.char.id) } };
    },
  });
}

// ---------- R4 POST /party-runs/{id}/begin ----------

export function beginRunByHost(accountId: number, characterUuid: string, runUuid: string, body: RequestOnlyBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party-runs/:id/begin',
    requestId: body.request_id,
    payload: { run_id: runUuid },
    lock: { kind: 'run', runUuid },
    handler: async (ctx) => {
      const locked = await lockPartyAndRun(ctx.client, runUuid);
      if (!locked) throw notFound();
      const run = locked.run;
      if (run.host_character_id !== ctx.char.id) throw new AppError(403, '방장만 할 수 있습니다.', 'NOT_HOST');
      if (run.state !== 'gathering') throw new AppError(409, '입장을 받는 중이 아닙니다.', 'RUN_NOT_GATHERING');
      await beginRun(ctx, run);
      const fresh = (await repo.getRunById(ctx.client, run.id)) as PartyRunRow;
      return { status: 201, data: { run: await buildRunView(ctx.client, fresh, ctx.char.id) } };
    },
  });
}
