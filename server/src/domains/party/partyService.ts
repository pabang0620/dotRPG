// 파티 모집 게시판·로비(P1~P11). 이 도메인은 골드·아이템·경험치를 바꾸지 않는다(그건 3단계 경제와 정산).
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { containsBannedWord } from '../../utils/bannedWords';
import { powerOf } from '../characters/powerEstimate';
import * as charRepo from '../characters/characterRepository';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { checkEntry } from '../dungeons/entryRules';
import type { StoredResult } from '../economy/economyService';
import { getQueueStore } from '../match/queueStore';
import * as runRepo from '../partyruns/partyRunRepository';
import { ensureGatherResolved } from '../partyruns/partyRunService';
import { notifyCharacters, notifyPartyChanged } from '../chat/partyNotify';
import * as inviteRepo from '../partyinvites/partyInvitesRepository';
import { inviteBody } from '../partyinvites/partyInvitesView';
import * as repo from './partyRepository';
import { buildPartyView } from './partyView';
import { runParty, withPartyLocks, type PartyCtx } from './partyTx';
import type {
  ApplicationParams, CreatePartyBody, ListQuery, PatchPartyBody, PollQuery, ReadyBody, RequestOnlyBody, RespondBody, TargetBody,
} from './partyValidation';

const NOT_IN_PARTY = () => new AppError(404, '속한 파티가 없습니다.', 'NOT_IN_PARTY');
const NOT_LEADER = () => new AppError(403, '방장만 할 수 있습니다.', 'NOT_LEADER');
const BUSY = () => new AppError(409, '지금은 바꿀 수 없습니다.', 'PARTY_BUSY');

type BaseCtx = Omit<PartyCtx, 'requestId'>;

export async function ensureFree(ctx: BaseCtx): Promise<void> {
  const partyId = await repo.findPartyIdOf(ctx.client, ctx.char.id);
  if (partyId !== null) {
    const p = await repo.getParty(ctx.client, partyId);
    throw new AppError(409, '이미 파티에 있습니다.', 'ALREADY_IN_PARTY', { party_id: p?.uuid });
  }
  if (getQueueStore().get(ctx.char.id)) throw new AppError(409, '매칭 대기 중입니다.', 'IN_QUEUE');
  if (await dungeonRepo.inPartyRun(ctx.client, ctx.char.id)) throw new AppError(409, '파티 판에 참여 중입니다.', 'IN_PARTY_RUN');
}

/** 내 파티를 잠가 읽는다(캐릭터 잠금 뒤 parties 잠금) */
async function myLockedParty(ctx: BaseCtx): Promise<repo.PartyRow> {
  const partyId = await repo.findPartyIdOf(ctx.client, ctx.char.id);
  if (partyId === null) throw NOT_IN_PARTY();
  const party = await repo.lockParty(ctx.client, partyId);
  if (!party || party.state === 'closed') throw NOT_IN_PARTY();
  return party;
}

const done = async (ctx: BaseCtx, party: repo.PartyRow | null, status = 200) => ({
  status,
  data: { party: party ? await buildPartyView(ctx.client, (await repo.getParty(ctx.client, party.id)) as repo.PartyRow, ctx.char.id, ctx.now) : null },
});

// ---------- P1 GET /parties ----------

export async function listParties(accountId: number, characterUuid: string, q: ListQuery) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const rows = await repo.listBoard(getPool(), c.id, getNow(), q.dungeon_id, q.difficulty);
  const posts = rows.map((r) => ({
    id: r.uuid,
    dungeon_id: r.dungeon_id,
    difficulty: r.difficulty,
    members: r.members,
    max_members: r.max_members,
    min_power: r.min_power,
    message: r.message,
    leader: { name: r.leader_name, class: r.leader_class, level: r.leader_level },
    mine: r.leader_character_id === c.id,
    applied: r.applied,
    full: r.members >= r.max_members,
    listed_until: r.listed_until.toISOString(),
    created_at: r.created_at.getTime(),
  }));
  // 내 글, 빈자리 있는 글 먼저, 최소 전투력 오름차순, 같으면 최신순(C# MockPartyFinderService.List와 같다)
  posts.sort((a, b) => Number(b.mine) - Number(a.mine) || Number(a.full) - Number(b.full) || a.min_power - b.min_power || b.created_at - a.created_at);
  const start = (q.page - 1) * q.limit;
  return {
    data: { posts: posts.slice(start, start + q.limit).map(({ created_at: _c, ...p }) => p) },
    meta: { total: posts.length, page: q.page, limit: q.limit },
  };
}

// ---------- P2 POST /parties ----------

export function createParty(accountId: number, characterUuid: string, body: CreatePartyBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/parties',
    requestId,
    payload,
    handler: async (ctx) => {
      await ensureFree(ctx);
      const info = await checkEntry(ctx.client, ctx.char, body.dungeon_id, body.difficulty, ctx.now);
      if (body.max_members > info.dungeon.maxParty) throw new AppError(422, '파티 인원이 너무 많습니다.', 'PARTY_TOO_BIG');
      const power = await powerOf(ctx.client, ctx.char.id, ctx.char.class, ctx.char.level);
      if (body.min_power > power) throw new AppError(422, '최소 전투력이 내 전투력보다 높습니다.', 'MIN_POWER_TOO_HIGH', { max: power });
      const message = body.message ?? '';
      if (message && containsBannedWord(message)) throw new AppError(422, '사용할 수 없는 말이 들어 있습니다.', 'MESSAGE_BLOCKED');
      const pol = getConfig().policy;
      const party = await repo.insertParty(ctx.client, {
        leaderId: ctx.char.id,
        dungeonId: body.dungeon_id,
        difficulty: body.difficulty,
        maxMembers: body.max_members,
        minPower: body.min_power,
        message,
        source: 'board',
        listed: body.listed,
        listedUntil: body.listed ? new Date(ctx.now.getTime() + pol.partyListingMinutes * 60_000) : null,
        startBy: null,
        now: ctx.now,
      });
      await repo.insertMember(ctx.client, party.id, ctx.char.id, ctx.char.accountId, true, ctx.now);
      return done(ctx, party, 201);
    },
  });
}

// ---------- P3 GET /party (폴링) ----------

export async function getMyParty(accountId: number, characterUuid: string, q: PollQuery) {
  const pol = getConfig().policy;
  const me = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!me) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  // 입장 마감이 지난 판은 먼저 지연 처리한다(자동 시작 또는 취소)
  const pid = await repo.findPartyIdOf(getPool(), me.id);
  if (pid !== null) {
    const run = await runRepo.activeRunOfParty(getPool(), pid);
    if (run) await ensureGatherResolved(accountId, characterUuid, run);
  }
  const store = getQueueStore();
  const ticket = store.get(me.id);
  const now = getNow();
  if (ticket) ticket.lastPollAt = now;

  const out = await withPartyLocks(accountId, characterUuid, { kind: 'self' }, async (ctx) => {
    let partyId = await repo.findPartyIdOf(ctx.client, ctx.char.id);
    // 지연 만료: 내 신청과 내 파티의 대기 신청
    await repo.expireApplications(ctx.client, ctx.char.id, partyId, ctx.now);
    await inviteRepo.expireDue(ctx.client, ctx.now, { inviteeCharacterId: ctx.char.id });
    let party: repo.PartyRow | null = null;
    if (partyId !== null) {
      party = await repo.lockParty(ctx.client, partyId);
      const why = await repo.idleOrTimedOut(ctx.client, partyId, ctx.now, pol.partyIdleMinutes);
      if (party && why) {
        await repo.closeParty(ctx.client, partyId, why, ctx.now);
        party = null;
        partyId = null;
      }
    }
    if (party && q.after_version !== undefined && q.after_version === party.version && !ticket) return { changed: false as const };
    const humans = ticket ? store.listByKey(ticket.dungeonId, ticket.difficulty).length : 0;
    const notice = await repo.recentNotice(ctx.client, ctx.char.id, new Date(ctx.now.getTime() - 120_000));
    const apps = await repo.recentApplicationsOf(ctx.client, ctx.char.id);
    // 5단계: WebSocket이 끊겨 있는 동안에도 폴링으로 초대를 받는다(안전망)
    const invites = (await inviteRepo.incomingFor(ctx.client, ctx.char.id, ctx.now)).map(inviteBody);
    return {
      changed: true as const,
      invites_incoming: invites,
      party: party && party.state !== 'closed' ? await buildPartyView(ctx.client, party, ctx.char.id, ctx.now) : null,
      queue: ticket
        ? {
            dungeon_id: ticket.dungeonId,
            difficulty: ticket.difficulty,
            queued_at: ticket.queuedAt.toISOString(),
            depart_at: new Date(ticket.queuedAt.getTime() + pol.matchQueueSeconds * 1000).toISOString(),
            humans_waiting: humans,
            fill_ai_available: humans < 4,
          }
        : null,
      applications_mine: apps.map((a) => ({ id: a.uuid, party_id: a.party_uuid, state: a.state, expires_at: a.expires_at.toISOString() })),
      notice: notice
        ? {
            code: notice.left_reason === 'kicked' ? 'KICKED' : notice.left_reason === 'start_timeout' ? 'START_TIMEOUT' : 'PARTY_CLOSED',
            at: notice.left_at.toISOString(),
          }
        : null,
    };
  });
  return out;
}

// ---------- P4 PATCH /party ----------

export function patchParty(accountId: number, characterUuid: string, body: PatchPartyBody) {
  const pol = getConfig().policy;
  return withPartyLocks(accountId, characterUuid, { kind: 'self' }, async (ctx) => {
    const party = await myLockedParty(ctx);
    if (party.leader_character_id !== ctx.char.id) throw NOT_LEADER();
    if (party.state !== 'forming') throw BUSY();
    const members = await repo.activeMembers(ctx.client, party.id);
    if (body.max_members !== undefined && body.max_members < members.length) {
      throw new AppError(422, '현재 인원보다 작게 줄일 수 없습니다.', 'MAX_MEMBERS_TOO_LOW');
    }
    if (body.min_power !== undefined) {
      const power = await powerOf(ctx.client, ctx.char.id, ctx.char.class, ctx.char.level);
      if (body.min_power > power) throw new AppError(422, '최소 전투력이 내 전투력보다 높습니다.', 'MIN_POWER_TOO_HIGH', { max: power });
    }
    if (body.message && containsBannedWord(body.message)) throw new AppError(422, '사용할 수 없는 말이 들어 있습니다.', 'MESSAGE_BLOCKED');
    await repo.patchParty(ctx.client, party.id, {
      listed: body.listed,
      listedUntil: body.listed === true ? new Date(ctx.now.getTime() + pol.partyListingMinutes * 60_000) : undefined,
      message: body.message,
      minPower: body.min_power,
      maxMembers: body.max_members,
    });
    if (body.min_power !== undefined || body.max_members !== undefined) await repo.resetReady(ctx.client, party.id, party.leader_character_id);
    await repo.bump(ctx.client, party.id, ctx.now);
    return (await done(ctx, party)).data;
  });
}

// ---------- P5 POST /parties/{party_id}/apply ----------

export function applyToParty(accountId: number, characterUuid: string, partyUuid: string, body: RequestOnlyBody): Promise<StoredResult> {
  const pol = getConfig().policy;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/parties/:party_id/apply',
    requestId: body.request_id,
    payload: { party_id: partyUuid },
    handler: async (ctx) => {
      const party = await repo.getPartyByUuid(ctx.client, partyUuid);
      if (!party || !party.listed || party.state !== 'forming' || !party.listed_until || party.listed_until <= ctx.now) {
        throw new AppError(404, '모집이 끝난 글입니다.', 'PARTY_NOT_FOUND');
      }
      if (party.leader_character_id === ctx.char.id) throw new AppError(422, '내 모집 글에는 신청할 수 없습니다.', 'OWN_PARTY');
      await ensureFree(ctx);
      const members = await repo.activeMembers(ctx.client, party.id);
      if (members.length >= party.max_members) throw new AppError(409, '정원이 찼습니다.', 'PARTY_FULL');
      await checkEntry(ctx.client, ctx.char, party.dungeon_id, party.difficulty, ctx.now);
      const power = await powerOf(ctx.client, ctx.char.id, ctx.char.class, ctx.char.level);
      if (power < party.min_power) throw new AppError(422, '전투력이 부족합니다.', 'POWER_TOO_LOW', { need: party.min_power, have: power });
      await repo.expireApplications(ctx.client, ctx.char.id, null, ctx.now);
      const existing = await repo.pendingApplication(ctx.client, party.id, ctx.char.id);
      if (existing) return appResult(existing);
      if ((await repo.countPending(ctx.client, ctx.char.id, ctx.now)) >= 3) {
        throw new AppError(429, '동시에 신청할 수 있는 수를 넘었습니다.', 'TOO_MANY_APPLICATIONS');
      }
      const app = await repo.insertApplication(
        ctx.client, party.id, ctx.char.id, ctx.char.accountId, power, ctx.now, new Date(ctx.now.getTime() + pol.partyApplySeconds * 1000),
      );
      notifyPartyChanged(ctx.client, party.id, ctx.now);
      return appResult(app, 201);
    },
  });
}

const appResult = (a: repo.ApplicationRow, status = 200) => ({
  status,
  data: { application: { id: a.uuid, party_id: a.party_uuid, state: a.state, expires_at: a.expires_at.toISOString() } },
});

// ---------- P6 DELETE /party/applications/{application_id} ----------

export function cancelApplication(accountId: number, characterUuid: string, p: ApplicationParams) {
  return withPartyLocks(accountId, characterUuid, { kind: 'self' }, async (ctx) => {
    const a = await repo.findApplication(ctx.client, p.application_id);
    if (!a || a.character_id !== ctx.char.id) throw new AppError(404, '신청을 찾을 수 없습니다.', 'APPLICATION_NOT_FOUND');
    if (a.state === 'cancelled') return { cancelled: true };
    if (a.state !== 'pending') throw new AppError(404, '신청을 찾을 수 없습니다.', 'APPLICATION_NOT_FOUND');
    await repo.setApplicationState(ctx.client, a.id, 'cancelled', ctx.now);
    return { cancelled: true };
  });
}

// ---------- P7 POST /party/applications/{application_id}/respond ----------

export async function respondToApplication(
  accountId: number,
  characterUuid: string,
  p: ApplicationParams,
  body: RespondBody,
): Promise<StoredResult> {
  let unavailable: number | null = null;
  try {
    return await runParty({
      accountId,
      characterUuid,
      endpoint: 'POST /characters/:uuid/party/applications/:application_id/respond',
      requestId: body.request_id,
      payload: { application_id: p.application_id, accept: body.accept },
      handler: async (ctx) => {
        const party = await myLockedParty(ctx);
        if (party.leader_character_id !== ctx.char.id) throw NOT_LEADER();
        const a = await repo.findApplication(ctx.client, p.application_id);
        if (!a || a.party_id !== party.id) throw new AppError(404, '신청을 찾을 수 없습니다.', 'APPLICATION_NOT_FOUND');
        if (a.state !== 'pending') throw new AppError(409, '이미 처리된 신청입니다.', 'APPLICATION_NOT_PENDING');
        if (a.expires_at <= ctx.now) throw new AppError(410, '신청이 만료되었습니다.', 'APPLICATION_EXPIRED');
        if (!body.accept) {
          await repo.setApplicationState(ctx.client, a.id, 'rejected', ctx.now);
          notifyCharacters(ctx.client, [a.character_id]);
          return done(ctx, party);
        }
        if (party.state !== 'forming') throw BUSY();
        const members = await repo.activeMembers(ctx.client, party.id);
        if (members.length >= party.max_members) throw new AppError(409, '정원이 찼습니다.', 'PARTY_FULL');
        const fail = (): never => {
          unavailable = a.id;
          throw new AppError(409, '신청자가 다른 파티에 들어갔습니다.', 'APPLICANT_UNAVAILABLE');
        };
        if (getQueueStore().get(a.character_id)) fail();
        if ((await repo.findPartyIdOf(ctx.client, a.character_id)) !== null) fail();
        await ctx.client.query('SAVEPOINT join_member');
        try {
          await repo.insertMember(ctx.client, party.id, a.character_id, a.account_id, false, ctx.now);
        } catch (err) {
          if (!isUniqueViolation(err, 'party_members_one_active')) throw err;
          await ctx.client.query('ROLLBACK TO SAVEPOINT join_member');
          fail();
        }
        await repo.setApplicationState(ctx.client, a.id, 'accepted', ctx.now);
        await repo.cancelOtherApplications(ctx.client, a.character_id, a.id, ctx.now);
        await repo.resetReady(ctx.client, party.id, party.leader_character_id);
        await repo.bump(ctx.client, party.id, ctx.now);
        return done(ctx, party);
      },
    });
  } catch (err) {
    // 신청자가 갈 수 없게 되었다: 신청은 거절로 닫는다(롤백과 무관하게 남긴다)
    if (unavailable !== null) await repo.setApplicationState(getPool(), unavailable, 'rejected', getNow());
    throw err;
  }
}

// ---------- P8 ready, P9 leave, P10 kick, P11 leader ----------

export function setReady(accountId: number, characterUuid: string, body: ReadyBody) {
  return withPartyLocks(accountId, characterUuid, { kind: 'self' }, async (ctx) => {
    const party = await myLockedParty(ctx);
    if (party.leader_character_id === ctx.char.id) throw new AppError(422, '방장은 항상 준비 상태입니다.', 'LEADER_ALWAYS_READY');
    if (party.state !== 'forming') throw BUSY();
    await repo.setReady(ctx.client, party.id, ctx.char.id, body.ready);
    await repo.bump(ctx.client, party.id, ctx.now);
    return (await done(ctx, party)).data;
  });
}

export function leaveParty(accountId: number, characterUuid: string, body: RequestOnlyBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party/leave',
    requestId: body.request_id,
    payload: {},
    handler: async (ctx) => {
      const party = await myLockedParty(ctx);
      if (await dungeonRepo.inPartyRun(ctx.client, ctx.char.id)) throw new AppError(409, '파티 판에 참여 중입니다.', 'IN_PARTY_RUN');
      const members = await repo.activeMembers(ctx.client, party.id);
      const rest = members.filter((m) => m.character_id !== ctx.char.id);
      if (rest.length === 0) {
        // 혼자 나가며 해산하는 사람에게는 해산 알림을 주지 않는다
        await repo.leaveMember(ctx.client, party.id, ctx.char.id, 'left', ctx.now);
        await repo.closeParty(ctx.client, party.id, 'disbanded', ctx.now);
        return { status: 200, data: { party: null } };
      }
      await repo.leaveMember(ctx.client, party.id, ctx.char.id, 'left', ctx.now);
      const leaderId = party.leader_character_id === ctx.char.id ? (rest[0] as (typeof rest)[number]).character_id : party.leader_character_id;
      if (leaderId !== party.leader_character_id) await repo.setLeader(ctx.client, party.id, leaderId);
      await repo.resetReady(ctx.client, party.id, leaderId);
      await repo.bump(ctx.client, party.id, ctx.now);
      return { status: 200, data: { party: null } };
    },
  });
}

async function memberByUuid(client: PoolClient, partyId: number, uuid: string) {
  const m = (await repo.activeMembers(client, partyId)).find((x) => x.character_uuid === uuid);
  if (!m) throw new AppError(404, '파티원을 찾을 수 없습니다.', 'MEMBER_NOT_FOUND');
  return m;
}

export function kickMember(accountId: number, characterUuid: string, body: TargetBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party/kick',
    requestId: body.request_id,
    payload: { target: body.target },
    handler: async (ctx) => {
      const party = await myLockedParty(ctx);
      if (party.leader_character_id !== ctx.char.id) throw NOT_LEADER();
      if (party.state !== 'forming') throw BUSY();
      if (body.target === ctx.char.uuid) throw new AppError(422, '자기 자신은 강퇴할 수 없습니다.', 'CANNOT_KICK_SELF');
      const m = await memberByUuid(ctx.client, party.id, body.target);
      await repo.leaveMember(ctx.client, party.id, m.character_id, 'kicked', ctx.now);
      await repo.resetReady(ctx.client, party.id, party.leader_character_id);
      await repo.bump(ctx.client, party.id, ctx.now);
      return done(ctx, party);
    },
  });
}

export function passLeader(accountId: number, characterUuid: string, body: TargetBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party/leader',
    requestId: body.request_id,
    payload: { target: body.target },
    handler: async (ctx) => {
      const party = await myLockedParty(ctx);
      if (party.leader_character_id !== ctx.char.id) throw NOT_LEADER();
      if (party.state !== 'forming') throw BUSY();
      const m = await memberByUuid(ctx.client, party.id, body.target);
      await repo.setLeader(ctx.client, party.id, m.character_id);
      await repo.resetReady(ctx.client, party.id, m.character_id);
      await repo.bump(ctx.client, party.id, ctx.now);
      return done(ctx, party);
    },
  });
}
