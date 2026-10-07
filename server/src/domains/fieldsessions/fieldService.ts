// 필드 파티 세션(F1~F7): 같은 파티가 같은 필드 맵에서 한 PC(호스트)의 몬스터를 함께 사냥한다. 이 도메인은 골드·아이템·경험치를 바꾸지 않는다
// (보상은 3단계 /kills 가 멤버마다 따로 판정한다). 락 순서: 캐릭터 -> parties -> party_runs -> field_sessions.
import { randomBytes } from 'node:crypto';
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as charRepo from '../characters/characterRepository';
import { inPartyRun } from '../dungeons/dungeonRepository';
import * as econRepo from '../economy/economyRepository';
import { recordAnomaly, type StoredResult } from '../economy/economyService';
import { attackCap } from '../kills/killRules';
import * as partyRepo from '../party/partyRepository';
import { runParty, withPartyLocks, type PartyCtx } from '../party/partyTx';
import { touchMap } from '../antiabuse/presenceService';
import { recordCardMismatch } from '../antiabuse/cardMismatch';
import { relayHub } from '../relay/relayHub';
import { pickTransport } from '../transport/pickTransport';
import { transportView } from '../partyruns/runView';
import { candidatesOf, closeMember, freeSeat, handoff, sweepMembers } from './fieldCore';
import { notifyFieldChanged } from './fieldNotify';
import * as repo from './fieldRepository';
import { electHost, electionWinner } from './hostElection';
import { buildFieldView, hostKeyOf } from './fieldView';
import { gearHashes } from './gearHash';
import type { ClaimBody, EnterBody, GetQuery, HeartbeatBody, ObserveBody, RequestOnlyBody } from './fieldValidation';

const notFound = () => new AppError(404, '필드 세션을 찾을 수 없습니다.', 'FIELD_SESSION_NOT_FOUND');
type BaseCtx = Omit<PartyCtx, 'requestId'>;

/** 지금 맵에서 세션을 만들 수 있는가(maps.json: instanced=false, fieldSpawns 있음) */
export function isSharedMap(mapId: string): boolean {
  const data = getGameData();
  const map = data.maps.get(mapId);
  const extra = data.economy.mapExtra.get(mapId);
  return !!map && !map.instanced && !!extra && extra.sharedField && extra.fieldSpawns.length > 0;
}

async function capOf(client: PoolClient, c: { id: number; level: number }): Promise<number> {
  const eco = getGameData().economy;
  return attackCap(eco, getConfig().policy, c.level, await econRepo.listWornKeys(client, c.id));
}

async function myCharacterId(accountId: number, characterUuid: string): Promise<number> {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c.id;
}

const hostBrief = async (client: PoolClient, session: repo.SessionRow) => {
  const members = await repo.activeMembers(client, session.id);
  const h = members.find((m) => m.character_id === session.host_character_id);
  if (!h) return null;
  const steam = await repo.steamIdsOf(client, [h.account_id]);
  return { character_id: h.character_uuid, seat: h.seat, steam_id: steam.get(h.account_id) ?? null, epoch: session.host_epoch };
};

// ---------- F1 POST /field-sessions/enter ----------

export function enterField(accountId: number, characterUuid: string, body: EnterBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/field-sessions/enter',
    requestId: body.request_id,
    payload: { map_id: body.map_id },
    handler: (ctx) => processEnter(ctx, body.map_id),
  }).catch((err: unknown) => {
    // 같은 파티·맵의 동시 첫 입장이 유일 인덱스에 걸렸다(파티 잠금 아래에서는 드물다): 한 번 다시 해서 합류시킨다
    if (isUniqueViolation(err, 'field_sessions_one_active') || isUniqueViolation(err, 'field_session_members_seat_uq')) {
      return runParty({
        accountId,
        characterUuid,
        endpoint: 'POST /characters/:uuid/field-sessions/enter',
        requestId: body.request_id,
        payload: { map_id: body.map_id },
        handler: (ctx) => processEnter(ctx, body.map_id),
      });
    }
    throw err;
  });
}

async function processEnter(ctx: PartyCtx, mapId: string) {
  const { client, char: me, now } = ctx;
  const none = (reason: string) => ({ status: 200, data: { session: null, host_key: null, reason } });
  const partyId = await partyRepo.findPartyIdOf(client, me.id);
  if (partyId === null) return none('NO_PARTY');
  const party = await partyRepo.lockParty(client, partyId);
  if (!party || party.state === 'closed') return none('NO_PARTY');
  const humans = await partyRepo.activeMembers(client, partyId);
  if (humans.length < 2) return none('ALONE');
  if (!isSharedMap(mapId)) throw new AppError(422, '함께 사냥할 수 없는 맵입니다.', 'MAP_NOT_SHARED');
  if (await inPartyRun(client, me.id)) throw new AppError(409, '파티 판에 참여 중입니다.', 'IN_PARTY_RUN');

  // 내 다른 세션과 이 맵의 세션을 id 오름차순으로 한 번에 잠근다(교착 방지)
  const mySid = await repo.activeSessionIdOf(client, me.id);
  const sameMap = await client.query<{ id: string }>("SELECT id FROM field_sessions WHERE party_id = $1 AND map_id = $2 AND state = 'active'", [partyId, mapId]);
  const targetId = sameMap.rows[0] ? Number(sameMap.rows[0].id) : null;
  const locked = await repo.lockMany(client, [...new Set([mySid, targetId].filter((x): x is number => x !== null))]);
  const old = locked.find((s) => s.id === mySid && s.state === 'active');
  let target = locked.find((s) => s.id === targetId && s.state === 'active') ?? null;
  if (old && old.id !== target?.id) await closeMember(client, old, me.id, old.map_id === mapId ? 'replaced' : 'map_move', now);

  const cap = await capOf(client, me);
  let created = false;
  if (!target) {
    const picked = await pickTransport(client, humans.map((m) => m.account_id));
    target = await repo.insertSession(client, { partyId, mapId, hostId: me.id, key: randomBytes(32), transport: picked.transport, order: picked.order, now });
    await repo.insertMember(client, target.id, me.id, me.accountId, 0, cap, now, await repo.carriedDebt(client, partyId, mapId, me.id, now, getConfig().field.debtCarryMinutes));
    created = true;
  } else {
    const members = await repo.activeMembers(client, target.id);
    const row = await repo.memberRow(client, target.id, me.id);
    if (row && row.state !== 'left') {
      await repo.touchMember(client, target.id, me.id, now, cap);
      if (row.state === 'disconnected') await repo.setMemberState(client, target.id, me.id, 'joined', now);
    } else {
      const seat = freeSeat(members);
      if (seat === null) throw new AppError(409, '세션이 가득 찼습니다.', 'FIELD_FULL');
      if (row) await repo.reviveMember(client, target.id, me.id, seat, cap, now);
      else await repo.insertMember(client, target.id, me.id, me.accountId, seat, cap, now, await repo.carriedDebt(client, partyId, mapId, me.id, now, getConfig().field.debtCarryMinutes));
    }
    // 호스트 정하기: 호스트가 없으면 연결 확인된 사람, 시작 선출 창 안이면 더 우선인 멤버(방장)에게 넘긴다. 그 밖에는 바꾸지 않는다(고정 호스트)
    const all = await repo.activeMembers(client, target.id);
    const inWindow = now.getTime() < target.created_at.getTime() + getConfig().field.electionWindowSeconds * 1000;
    if (target.host_character_id === null) {
      await handoff(client, target, all, now, { freshSelf: me.id });
    } else if (inWindow) {
      const cands = await candidatesOf(client, target, all, now, me.id);
      const winner = electionWinner(cands, target.host_character_id);
      if (winner !== null) await repo.setHost(client, target.id, winner, now);
    }
    await repo.bump(client, target.id, now);
  }
  // 9단계 E4: 서버가 맵을 확정하는 순간 프레즌스 맵도 맞춘다(처치 보고의 맵 확인이 세션 맵과 어긋나지 않게)
  await touchMap(client, me.id, mapId, now);
  notifyFieldChanged(client, target.id);
  const fresh = (await repo.getById(client, target.id)) as repo.SessionRow;
  return {
    status: created ? 201 : 200,
    data: { session: await buildFieldView(client, fresh, me.id, now), host_key: hostKeyOf(fresh, me.id) },
  };
}

// ---------- F2 GET /field-sessions/{id} ----------

export async function getSession(accountId: number, characterUuid: string, sessionUuid: string, q: GetQuery) {
  const meId = await myCharacterId(accountId, characterUuid);
  const db = getPool();
  const session = await repo.getByUuid(db, sessionUuid);
  const row = session ? await repo.memberRow(db, session.id, meId) : null;
  if (!session || !row || row.state === 'left') throw notFound();
  if (q.after_version !== undefined && q.after_version === session.version) return { changed: false };
  return { changed: true, session: await buildFieldView(db, session, meId, getNow()) };
}

// ---------- F7 GET /field-session ----------

export async function mySession(accountId: number, characterUuid: string) {
  const meId = await myCharacterId(accountId, characterUuid);
  const db = getPool();
  const sid = await repo.activeSessionIdOf(db, meId);
  const session = sid === null ? null : await repo.getById(db, sid);
  if (!session) return { session: null, host_key: null };
  return { session: await buildFieldView(db, session, meId, getNow()), host_key: hostKeyOf(session, meId) };
}

// ---------- F3 POST /field-sessions/{id}/heartbeat ----------

export function heartbeat(accountId: number, characterUuid: string, sessionUuid: string, body: HeartbeatBody) {
  const pol = getConfig().policy;
  return withPartyLocks(accountId, characterUuid, { kind: 'self' }, async (ctx) => {
    const { client, char: me, now } = ctx;
    let session = await repo.lockByUuid(client, sessionUuid);
    const row = session ? await repo.memberRow(client, session.id, me.id) : null;
    if (!session || !row || row.state === 'left') throw notFound();
    let changed = false;
    if (session.state === 'active') {
      if (row.state === 'disconnected' && row.disconnected_at && now.getTime() - row.disconnected_at.getTime() > pol.partyRejoinSeconds * 1000) {
        await closeMember(client, session, me.id, 'rejoin_timeout', now);
        session = (await repo.getById(client, session.id)) as repo.SessionRow;
        return heartbeatBody(client, session, me.id, body, now, 'left');
      }
      const next = body.synced ? 'playing' : row.state === 'disconnected' ? 'joined' : row.state;
      await repo.touchMember(client, session.id, me.id, now, await capOf(client, me));
      if (next !== row.state) {
        await repo.setMemberState(client, session.id, me.id, next, now);
        changed = true;
      }
      const sw = await sweepMembers(client, session, now, me.id);
      changed = changed || sw.changed;
      if (changed) {
        await repo.bump(client, session.id, now);
        notifyFieldChanged(client, session.id);
      } else await repo.touchSession(client, session.id, now);
      session = (await repo.getById(client, session.id)) as repo.SessionRow;
    }
    return heartbeatBody(client, session, me.id, body, now);
  });
}

async function heartbeatBody(client: PoolClient, session: repo.SessionRow, meId: number, body: HeartbeatBody, now: Date, forceState?: string) {
  const members = await repo.activeMembers(client, session.id);
  const mine = members.find((m) => m.character_id === meId);
  const hashes = await gearHashes(client, members.map((m) => m.character_id));
  return {
    state: session.state,
    host: await hostBrief(client, session),
    host_changed: body.seen_epoch !== session.host_epoch,
    transport: transportView(session),
    me: { state: forceState ?? mine?.state ?? 'left' },
    members: members.map((m) => ({ character_id: m.character_uuid, seat: m.seat, state: m.state, level: m.level, gear_hash: hashes.get(m.character_id) ?? '' })),
    server_time: now.toISOString(),
  };
}

// ---------- F4 POST /field-sessions/{id}/leave ----------

export function leaveField(accountId: number, characterUuid: string, sessionUuid: string, body: RequestOnlyBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/field-sessions/:id/leave',
    requestId: body.request_id,
    payload: { session_id: sessionUuid },
    handler: async (ctx) => {
      const { client, char: me, now } = ctx;
      const session = await repo.lockByUuid(client, sessionUuid);
      const row = session ? await repo.memberRow(client, session.id, me.id) : null;
      if (!session || !row) throw notFound();
      // 이미 떠났으면 성공(멱등)
      if (row.state !== 'left' && session.state === 'active') await closeMember(client, session, me.id, 'left', now);
      const fresh = (await repo.getById(client, session.id)) as repo.SessionRow;
      return { status: 200, data: { left: true, host: fresh.state === 'active' ? await hostBrief(client, fresh) : null } };
    },
  });
}

// ---------- F5 POST /field-sessions/{id}/host/claim ----------

export function claimHost(accountId: number, characterUuid: string, sessionUuid: string, body: ClaimBody): Promise<StoredResult> {
  const cfg = getConfig();
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/field-sessions/:id/host/claim',
    requestId: body.request_id,
    payload: { session_id: sessionUuid, observed_epoch: body.observed_epoch },
    handler: async (ctx) => {
      const { client, char: me, now } = ctx;
      const session = await repo.lockByUuid(client, sessionUuid);
      const row = session ? await repo.memberRow(client, session.id, me.id) : null;
      if (!session || !row || row.state === 'left') throw notFound();
      if (session.state !== 'active') throw new AppError(409, '끝난 세션입니다.', 'FIELD_SESSION_ENDED');
      if (body.observed_epoch !== session.host_epoch) {
        throw new AppError(409, '이미 다른 호스트가 이어받았습니다.', 'HOST_CHANGED', { host: await hostBrief(client, session) });
      }
      const members = await repo.activeMembers(client, session.id);
      const host = members.find((m) => m.character_id === session.host_character_id);
      const staleHb = !host || host.state === 'disconnected' || !host.last_seen_at || now.getTime() - host.last_seen_at.getTime() > cfg.policy.hostStaleSeconds * 1000;
      // 중계 방: 호스트 연결이 있으면 하트비트가 늦어도 살아 있고, 연결이 grace 이상 없으면 하트비트가 신선해도 죽은 것이다
      const presence = session.transport === 'relay' ? relayHub().hostPresence('field', session.uuid) : null;
      const dead = session.host_character_id === null || !host || (presence ? !presence.connected && presence.absentMs >= cfg.relay.hostGraceMs : staleHb);
      if (!dead || session.host_character_id === me.id) throw new AppError(409, '호스트가 아직 살아 있습니다.', 'HOST_ALIVE');
      const cands = await candidatesOf(client, session, members, now, me.id);
      const next = electHost(cands, session.host_character_id);
      if (next !== me.id) {
        const seat = members.find((m) => m.character_id === next)?.seat ?? null;
        throw new AppError(409, '다음 호스트가 아닙니다.', 'NOT_NEXT_HOST', { next_seat: seat });
      }
      await repo.touchMember(client, session.id, me.id, now);
      await repo.setHost(client, session.id, me.id, now);
      await touchMap(client, me.id, session.map_id, now);
      if (host && host.state !== 'disconnected') await repo.setMemberState(client, session.id, host.character_id, 'disconnected', now);
      await repo.bump(client, session.id, now);
      notifyFieldChanged(client, session.id);
      const fresh = (await repo.getById(client, session.id)) as repo.SessionRow;
      const all = await repo.activeMembers(client, session.id);
      return {
        status: 200,
        data: {
          host: await hostBrief(client, fresh),
          host_key: hostKeyOf(fresh, me.id),
          members: all.map((m) => ({ character_id: m.character_uuid, seat: m.seat, state: m.state })),
        },
      };
    },
  });
}

// ---------- F6 POST /field-sessions/{id}/observe ----------

/** 좌석 한 명이 window 동안 기여할 수 있는 처치 수의 상한(3단계 리스폰 공급 상한과 같은 근거) */
export function observeCap(mapId: string, windowMs: number): number {
  const data = getGameData();
  const extra = data.economy.mapExtra.get(mapId);
  const margin = getConfig().policy.killSupplyMargin;
  let total = 0;
  for (const s of extra?.fieldSpawns ?? []) {
    const respawn = s.respawnSeconds ?? data.economy.monsters.get(s.monsterId)?.respawnSeconds ?? 25;
    total += Math.ceil(s.points * margin) * (windowMs / 1000 / respawn + 1);
  }
  return Math.ceil(total);
}

export function observe(accountId: number, characterUuid: string, sessionUuid: string, body: ObserveBody): Promise<StoredResult> {
  const cfg = getConfig().field;
  const { request_id: requestId, ...payload } = body;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/field-sessions/:id/observe',
    requestId,
    payload: { session_id: sessionUuid, ...payload },
    handler: async (ctx) => {
      const { client, char: me, now } = ctx;
      const session = await repo.lockByUuid(client, sessionUuid);
      const row = session ? await repo.memberRow(client, session.id, me.id) : null;
      if (!session || !row || row.state === 'left') throw notFound();
      if (session.state !== 'active') throw new AppError(409, '끝난 세션입니다.', 'FIELD_SESSION_ENDED');
      if (session.host_character_id !== me.id) throw new AppError(403, '호스트만 할 수 있습니다.', 'NOT_HOST');
      if (body.host_epoch !== session.host_epoch) throw new AppError(409, '호스트 세대가 바뀌었습니다.', 'HOST_EPOCH_STALE');
      const members = await repo.activeMembers(client, session.id);
      // 창 길이는 서버가 정한다: 요청의 window_ms 와 마지막 관찰(없으면 세션 생성) 이후 실제 경과 중 작은 값
      const sinceMs = now.getTime() - (session.last_observe_at ?? session.created_at).getTime();
      const windowMs = Math.max(0, Math.min(body.window_ms, sinceMs));
      const max = observeCap(session.map_id, windowMs);
      for (const c of body.credits) {
        const m = members.find((x) => x.seat === c.seat);
        if (!m) continue;
        // 9단계 9.3: 호스트가 멤버 카드 불일치를 봤다. 활성 멤버(호스트 본인 제외)만 기록하고 세션당 한 번만 남긴다
        if (c.card_mismatch === true && m.character_id !== me.id) {
          await recordCardMismatch(client, { accountId: m.account_id, characterId: m.character_id, characterUuid: m.character_uuid }, { kind: 'session_id', id: session.uuid });
        }
        if (c.kills > max) {
          await recordAnomaly(accountId, me.id, { kind: 'field_host', severity: 2, detail: { session_id: session.uuid, seat: c.seat, kills: c.kills, max, window_ms: body.window_ms, window_used_ms: windowMs } }, client);
          continue;
        }
        await repo.addCredit(client, session.id, m.character_id, c.kills, cfg.creditSurplus);
      }
      await repo.setObserved(client, session.id, now);
      return { status: 200, data: { accepted: true } };
    },
  });
}
