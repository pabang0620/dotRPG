// 길드 만들기·가입·직위·탈퇴·해체. 쓰기는 runEconomy(행동하는 캐릭터 잠금 + request_id 멱등) 안에서 길드 행을 잠근다.
import type { PoolClient } from 'pg';
import { getPool, isUniqueViolation, type Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { maskBannedWords, nameHasBannedWord } from '../../utils/bannedWords';
import * as charRepo from '../characters/characterRepository';
import { registry } from '../chat/realtimeNotifier';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './guildRepository';
import {
  GUILD_COOLDOWN_MS, GUILD_CREATE_GOLD, GUILD_CREATE_LEVEL, GUILD_MAX_MEMBERS, GUILD_MAX_OFFICERS,
  GUILD_MAX_PENDING_APPLICATIONS, GUILD_PAGE_SIZE, canKick, canManage, type GuildRole,
} from './guildRules';
import type { ApplyBody, CreateBody, PatchBody, RequestBody, SearchQuery } from './guildValidation';

const forbidden = () => new AppError(403, '권한이 없습니다.', 'GUILD_FORBIDDEN');
const notInGuild = () => new AppError(409, '길드에 속해 있지 않습니다.', 'NOT_IN_GUILD');
const iso = (d: Date | null) => (d ? d.toISOString() : null);

/** 길드 상태 화면. 직위가 있는 사람만 신청 목록을 본다 */
async function view(db: Queryable, characterId: number, now: Date) {
  const m = await repo.membershipOf(db, characterId);
  const cooldown = await repo.cooldownUntil(db, characterId, now);
  const mine = await repo.myApplications(db, characterId);
  const base = { cooldown_until: iso(cooldown), my_applications: mine.map((a) => ({ guild_id: a.guild_uuid, guild_name: a.guild_name, applied_at: a.created_at.toISOString() })) };
  if (!m) return { guild: null, ...base };
  const g = await repo.byId(db, m.guild_id);
  if (!g || g.disbanded_at) return { guild: null, ...base };
  const members = await repo.members(db, g.id);
  const apps = canManage(m.role) ? await repo.applicationsOf(db, g.id) : [];
  return {
    guild: {
      id: g.uuid, name: g.name, notice: g.notice, join_mode: g.join_mode,
      member_count: g.member_count, max_members: GUILD_MAX_MEMBERS, my_role: m.role,
      honor_color: g.honor_until && g.honor_until > now ? g.honor_color : null,
      members: members.map((x) => ({
        character_id: x.uuid, name: x.name, level: x.level, career: x.career, role: x.role,
        // 접속 중이면 지금, 아니면 계정의 마지막 로그인
        last_seen_at: registry.ofCharacter(x.character_id) ? now.toISOString() : iso(x.last_login_at),
        joined_at: x.joined_at.toISOString(),
      })),
      applications: apps.map((a) => ({ id: a.uuid, character_id: a.character_uuid, name: a.name, level: a.level, career: a.career, applied_at: a.created_at.toISOString() })),
    },
    ...base,
  };
}

export async function status(accountId: number, uuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, uuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return view(getPool(), c.id, getNow());
}

export async function search(q: SearchQuery) {
  const query = q.query.normalize('NFC').trim();
  const r = await repo.search(getPool(), query, q.page, GUILD_PAGE_SIZE);
  return {
    guilds: r.rows.map((g) => ({ id: g.uuid, name: g.name, notice: g.notice, join_mode: g.join_mode, member_count: g.member_count, max_members: GUILD_MAX_MEMBERS, leader_name: g.leader_name })),
    page: q.page,
    total: r.total,
  };
}

async function assertFree(ctx: EconCtx): Promise<void> {
  if (await repo.membershipOf(ctx.client, ctx.char.id)) throw new AppError(409, '이미 길드에 속해 있습니다.', 'ALREADY_IN_GUILD');
  const until = await repo.cooldownUntil(ctx.client, ctx.char.id, ctx.now);
  if (until) throw new AppError(409, '길드를 떠난 뒤 24시간 동안은 다른 길드에 들어갈 수 없습니다.', 'GUILD_COOLDOWN', { until: until.toISOString() });
}

function cleanNotice(text: string | undefined): string | undefined {
  return text === undefined ? undefined : maskBannedWords(text).text;
}

/** 내 길드와 직위(잠금). 없으면 NOT_IN_GUILD */
async function myGuild(ctx: EconCtx): Promise<{ g: repo.GuildRow; role: GuildRole }> {
  const m = await repo.membershipOf(ctx.client, ctx.char.id);
  if (!m) throw notInGuild();
  const g = await repo.lockAlive(ctx.client, m.guild_id);
  if (!g) throw notInGuild();
  // 길드 잠금 뒤 다시 읽는다(그사이 추방·위임이 끝났을 수 있다)
  const again = await repo.membershipOf(ctx.client, ctx.char.id);
  if (!again || again.guild_id !== g.id) throw notInGuild();
  return { g, role: again.role };
}

const done = async (ctx: EconCtx, status = 200) => ({ status, data: { ...await view(ctx.client, ctx.char.id, ctx.now), delta: ctx.delta() } });

export function create(accountId: number, uuid: string, body: CreateBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'POST /characters/:uuid/guild', requestId: body.request_id,
    payload: { name: body.name, notice: body.notice ?? '', join_mode: body.join_mode }, handler: async (ctx) => {
      if (ctx.level < GUILD_CREATE_LEVEL) throw new AppError(422, `길드는 ${GUILD_CREATE_LEVEL}레벨부터 만들 수 있습니다.`, 'LEVEL_TOO_LOW', { need: GUILD_CREATE_LEVEL });
      await assertFree(ctx);
      if (nameHasBannedWord(body.name)) throw new AppError(422, '사용할 수 없는 이름입니다.', 'GUILD_NAME_BLOCKED');
      if (ctx.gold < GUILD_CREATE_GOLD) throw new AppError(422, '골드가 모자랍니다.', 'GOLD_SHORT', { need: GUILD_CREATE_GOLD, have: ctx.gold });
      let g: repo.GuildRow;
      try {
        // 이름 중복은 저장점으로 되돌려 다른 처리를 남기지 않는다
        await ctx.client.query('SAVEPOINT guild_name');
        g = await repo.insertGuild(ctx.client, { name: body.name, notice: cleanNotice(body.notice) ?? '', joinMode: body.join_mode, leaderId: ctx.char.id, now: ctx.now });
        await ctx.client.query('RELEASE SAVEPOINT guild_name');
      } catch (err) {
        if (isUniqueViolation(err, 'guilds_name_alive')) throw new AppError(409, '이미 있는 길드 이름입니다.', 'GUILD_NAME_TAKEN');
        throw err;
      }
      await repo.insertMember(ctx.client, g.id, ctx.char.id, 'leader', ctx.now);
      await repo.deleteApplicationsOf(ctx.client, ctx.char.id);
      await ctx.changeGold(-GUILD_CREATE_GOLD, 'guild_create', g.uuid);
      return done(ctx, 201);
    } });
}

/** 들어가기: 길드 행을 잠근 상태에서 인원 확인 -> 멤버 추가 -> 내 다른 신청 정리 */
async function join(ctx: EconCtx, g: repo.GuildRow, characterId: number): Promise<void> {
  if (g.member_count >= GUILD_MAX_MEMBERS) throw new AppError(409, '길드 인원이 가득 찼습니다.', 'GUILD_FULL');
  await repo.insertMember(ctx.client, g.id, characterId, 'member', ctx.now);
  await repo.addCount(ctx.client, g.id, 1);
  await repo.deleteApplicationsOf(ctx.client, characterId);
}

export function apply(accountId: number, uuid: string, body: ApplyBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'POST /characters/:uuid/guild/apply', requestId: body.request_id,
    payload: { guild_id: body.guild_id }, handler: async (ctx) => {
      await assertFree(ctx);
      const g = await repo.lockAliveByUuid(ctx.client, body.guild_id);
      if (!g) throw new AppError(404, '길드를 찾을 수 없습니다.', 'GUILD_NOT_FOUND');
      if (g.join_mode === 'open') {
        await join(ctx, g, ctx.char.id);
        return { status: 200, data: { joined: true, ...await view(ctx.client, ctx.char.id, ctx.now) } };
      }
      if (g.member_count >= GUILD_MAX_MEMBERS) throw new AppError(409, '길드 인원이 가득 찼습니다.', 'GUILD_FULL');
      const pending = await repo.myApplications(ctx.client, ctx.char.id);
      if (!pending.some((a) => a.guild_uuid === g.uuid) && pending.length >= GUILD_MAX_PENDING_APPLICATIONS) {
        throw new AppError(409, `가입 신청은 동시에 ${GUILD_MAX_PENDING_APPLICATIONS}곳까지 할 수 있습니다.`, 'GUILD_APPLY_LIMIT');
      }
      await repo.insertApplication(ctx.client, g.id, ctx.char.id, ctx.now);
      return { status: 200, data: { joined: false, ...await view(ctx.client, ctx.char.id, ctx.now) } };
    } });
}

export function decide(accountId: number, uuid: string, appUuid: string, action: 'accept' | 'reject', body: RequestBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: `POST /characters/:uuid/guild/applications/:id/${action}`, requestId: body.request_id,
    payload: { id: appUuid }, handler: async (ctx) => {
      const { g, role } = await myGuild(ctx);
      if (!canManage(role)) throw forbidden();
      const a = await repo.lockApplication(ctx.client, g.id, appUuid);
      if (!a) throw new AppError(404, '가입 신청을 찾을 수 없습니다.', 'GUILD_APPLICATION_NOT_FOUND');
      if (action === 'reject') {
        await repo.deleteApplication(ctx.client, a.id);
        return done(ctx);
      }
      // 신청자 행도 잠근다(캐릭터 잠금 순서: 행동하는 사람 -> 길드 -> 신청자. 신청자는 길드 행을 잡지 않은 채 자기 행만 잠그므로 교착 없음)
      const target = await ctx.client.query('SELECT 1 FROM characters WHERE id = $1 AND deleted_at IS NULL FOR UPDATE', [a.character_id]);
      if (target.rows.length === 0) {
        await repo.deleteApplication(ctx.client, a.id);
        throw new AppError(404, '신청한 캐릭터가 없습니다.', 'CHARACTER_NOT_FOUND');
      }
      if (await repo.membershipOf(ctx.client, a.character_id)) {
        await repo.deleteApplication(ctx.client, a.id);
        throw new AppError(409, '이미 다른 길드에 들어갔습니다.', 'ALREADY_IN_GUILD');
      }
      if (await repo.cooldownUntil(ctx.client, a.character_id, ctx.now)) throw new AppError(409, '신청한 사람이 아직 가입 대기 시간입니다.', 'GUILD_COOLDOWN');
      await join(ctx, g, a.character_id);
      return done(ctx);
    } });
}

/** 떠나기(탈퇴)와 해체. 길드장은 혼자 남았을 때만 해체로 떠난다 */
export function leave(accountId: number, uuid: string, action: 'leave' | 'disband', body: RequestBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: `POST /characters/:uuid/guild/${action}`, requestId: body.request_id,
    payload: {}, handler: async (ctx) => {
      const { g, role } = await myGuild(ctx);
      const cooldown = new Date(ctx.now.getTime() + GUILD_COOLDOWN_MS);
      if (action === 'disband') {
        if (role !== 'leader') throw forbidden();
        if (g.member_count > 1) throw new AppError(409, '길드원이 모두 나간 뒤에 해체할 수 있습니다.', 'GUILD_NOT_EMPTY');
        await repo.deleteMember(ctx.client, ctx.char.id);
        await repo.disband(ctx.client, g.id, ctx.now);
      } else {
        if (role === 'leader') throw new AppError(409, '길드장은 위임하거나 혼자 남았을 때 해체해야 떠날 수 있습니다.', 'GUILD_LEADER_MUST_TRANSFER');
        await repo.deleteMember(ctx.client, ctx.char.id);
        await repo.addCount(ctx.client, g.id, -1);
      }
      await repo.setCooldown(ctx.client, ctx.char.id, cooldown);
      return done(ctx);
    } });
}

export function memberAction(accountId: number, uuid: string, targetUuid: string, action: 'kick' | 'promote' | 'demote' | 'transfer', body: RequestBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: `POST /characters/:uuid/guild/members/:characterId/${action}`, requestId: body.request_id,
    payload: { target: targetUuid }, handler: async (ctx) => {
      const { g, role } = await myGuild(ctx);
      const t = await repo.memberByUuid(ctx.client, g.id, targetUuid);
      if (!t) throw new AppError(404, '길드원을 찾을 수 없습니다.', 'GUILD_MEMBER_NOT_FOUND');
      if (t.id === ctx.char.id) throw new AppError(409, '자기 자신에게는 할 수 없습니다.', 'GUILD_SELF');
      if (action === 'kick') {
        if (!canKick(role, t.role)) throw forbidden();
        await repo.deleteMember(ctx.client, t.id);
        await repo.addCount(ctx.client, g.id, -1);
        await repo.setCooldown(ctx.client, t.id, new Date(ctx.now.getTime() + GUILD_COOLDOWN_MS));
        return done(ctx);
      }
      if (role !== 'leader') throw forbidden();
      if (action === 'promote') {
        if (t.role !== 'member') throw new AppError(409, '길드원만 부길드장으로 올릴 수 있습니다.', 'GUILD_ROLE_INVALID');
        if (await repo.officerCount(ctx.client, g.id) >= GUILD_MAX_OFFICERS) throw new AppError(409, `부길드장은 ${GUILD_MAX_OFFICERS}명까지입니다.`, 'GUILD_OFFICER_LIMIT');
        await repo.setRole(ctx.client, t.id, 'officer');
      } else if (action === 'demote') {
        if (t.role !== 'officer') throw new AppError(409, '부길드장만 내릴 수 있습니다.', 'GUILD_ROLE_INVALID');
        await repo.setRole(ctx.client, t.id, 'member');
      } else {
        // 위임: 받는 사람이 길드장, 나는 자리가 있으면 부길드장, 없으면 길드원
        const officers = await repo.officerCount(ctx.client, g.id) - (t.role === 'officer' ? 1 : 0);
        await repo.setRole(ctx.client, ctx.char.id, officers < GUILD_MAX_OFFICERS ? 'officer' : 'member');
        await repo.setRole(ctx.client, t.id, 'leader');
        await repo.setLeader(ctx.client, g.id, t.id);
      }
      return done(ctx);
    } });
}

export function patch(accountId: number, uuid: string, body: PatchBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: 'PATCH /characters/:uuid/guild', requestId: body.request_id,
    payload: { notice: body.notice ?? null, join_mode: body.join_mode ?? null }, handler: async (ctx) => {
      const { g, role } = await myGuild(ctx);
      if (!canManage(role)) throw forbidden();
      await repo.updateSettings(ctx.client, g.id, cleanNotice(body.notice), body.join_mode);
      return done(ctx);
    } });
}

/**
 * 캐릭터가 사라질 때(삭제·회원 탈퇴 익명화) 길드에서 빼낸다. 호출 쪽 트랜잭션 안에서, 캐릭터 행을 잠근 뒤 부른다.
 * - 'refuse_leader'(캐릭터 삭제): 길드장이면 거부한다(위임이나 해체를 먼저 하게).
 * - 'auto'(회원 탈퇴): 길드장이면 가장 오래된 부길드장, 없으면 가장 오래된 길드원에게 넘기고, 혼자면 해체한다.
 * 어느 쪽이든 멤버 행·대기 신청을 지우고 member_count를 맞춘다.
 */
export async function detachCharacter(client: PoolClient, characterId: number, now: Date, mode: 'refuse_leader' | 'auto'): Promise<void> {
  await repo.deleteApplicationsOf(client, characterId);
  const m = await repo.membershipOf(client, characterId);
  if (!m) return;
  const g = await repo.lockAlive(client, m.guild_id);
  if (!g) { await repo.deleteMember(client, characterId); return; }
  if (m.role === 'leader') {
    if (mode === 'refuse_leader') throw new AppError(409, '길드장을 위임하거나 길드를 해체한 뒤 삭제할 수 있어요.', 'GUILD_LEADER_MUST_TRANSFER');
    const next = await repo.successorOf(client, g.id, characterId);
    await repo.deleteMember(client, characterId);
    if (!next) { await repo.disband(client, g.id, now); return; }
    await repo.setRole(client, next, 'leader');
    await repo.setLeader(client, g.id, next);
  } else {
    await repo.deleteMember(client, characterId);
  }
  await repo.addCount(client, g.id, -1);
}
