// 파티 초대(I1~I3): 방장이 접속 중인 사람을 파티로 부른다. 수락은 4단계 신청 수락과 같은 검사를 지난다.
import { getConfig } from '../../config/env';
import { afterCommit, getPool, isUniqueViolation } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { getGameData } from '../../gamedata/loader';
import { findAliveByName, findCharacterByUuid } from '../chat/chatRepository';
import { getNotifier, registry } from '../chat/realtimeNotifier';
import * as charRepo from '../characters/characterRepository';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { checkEntry } from '../dungeons/entryRules';
import { powerOf } from '../characters/powerEstimate';
import type { StoredResult } from '../economy/economyService';
import { getQueueStore } from '../match/queueStore';
import { ensureFree } from '../party/partyService';
import * as partyRepo from '../party/partyRepository';
import { buildPartyView } from '../party/partyView';
import { runParty } from '../party/partyTx';
import * as repo from './partyInvitesRepository';
import { inviteBody } from './partyInvitesView';
import type { InviteBodyIn, InviteParams, RespondBody } from './partyInvitesValidation';

const NOT_FOUND = () => new AppError(404, '초대를 찾을 수 없습니다.', 'INVITE_NOT_FOUND');
const isBlocked = async (blocker: number, blocked: number): Promise<boolean> =>
  (await getPool().query('SELECT 1 FROM blocks WHERE blocker_account_id = $1 AND blocked_account_id = $2 AND deleted_at IS NULL', [blocker, blocked])).rows.length > 0;

// ---------- I1 ----------

/** 초대로 시작하는 파티: 오늘 방장이 들어갈 수 있는 첫 던전(일반 난이도)을 목적지로, 게시판에는 올리지 않는다 */
async function createPrivateParty(ctx: Parameters<Parameters<typeof runParty>[0]['handler']>[0]): Promise<partyRepo.PartyRow> {
  await ensureFree(ctx);
  const dungeons = [...getGameData().economy.dungeons.byId.values()].filter((d) => !d.isRaid);
  let target = dungeons[0];
  for (const d of dungeons) {
    try {
      await checkEntry(ctx.client, ctx.char, d.id, 0, ctx.now);
      target = d;
      break;
    } catch (err) {
      if (!(err instanceof AppError)) throw err;
    }
  }
  if (!target) throw new AppError(500, '던전 데이터가 없습니다.', 'NO_DUNGEON');
  const created = await partyRepo.insertParty(ctx.client, {
    leaderId: ctx.char.id,
    dungeonId: target.id,
    difficulty: 0,
    maxMembers: Math.min(4, target.maxParty),
    minPower: 0,
    message: '',
    source: 'board',
    listed: false,
    listedUntil: null,
    startBy: null,
    now: ctx.now,
  });
  await partyRepo.insertMember(ctx.client, created.id, ctx.char.id, ctx.char.accountId, true, ctx.now);
  return (await partyRepo.lockParty(ctx.client, created.id)) as partyRepo.PartyRow;
}

export function sendInvite(accountId: number, characterUuid: string, body: InviteBodyIn): Promise<StoredResult> {
  const cfg = getConfig().social;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party/invites',
    requestId: body.request_id,
    payload: { target: body.target ?? null, target_name: body.target_name ?? null },
    handler: async (ctx) => {
      const client = ctx.client;
      const partyId = await partyRepo.findPartyIdOf(client, ctx.char.id);
      let party = partyId === null ? null : await partyRepo.lockParty(client, partyId);
      // 파티가 없으면 초대하면서 비공개 파티(모집 글 없음)를 만든다. 목적 던전은 방장이 파티 창에서 바꾼다
      if (!party || party.state === 'closed') party = await createPrivateParty(ctx);
      if (party.leader_character_id !== ctx.char.id) throw new AppError(403, '방장만 할 수 있습니다.', 'NOT_LEADER');
      if (party.state !== 'forming') throw new AppError(409, '지금은 바꿀 수 없습니다.', 'PARTY_BUSY');

      const target = body.target !== undefined ? await findCharacterByUuid(client, body.target) : await findAliveByName(body.target_name as string);
      if (!target || target.deleted) throw new AppError(404, '모험가를 찾을 수 없습니다.', 'PLAYER_NOT_FOUND');
      if (target.id === ctx.char.id || target.account_id === accountId) throw new AppError(422, '자기 자신에게는 할 수 없습니다.', 'CANNOT_TARGET_SELF');

      await repo.expireDue(client, ctx.now, { partyId: party.id });
      const existing = await repo.pendingFor(client, party.id, target.id, ctx.now);
      if (existing) return { status: 200, data: { invite: { id: existing.uuid, state: 'pending', expires_at: existing.expires_at.toISOString() } } };

      if (await isBlocked(accountId, target.account_id)) throw new AppError(409, '차단한 사람입니다. 먼저 차단을 해제하세요.', 'YOU_BLOCKED_TARGET');
      const members = await partyRepo.activeMembers(client, party.id);
      if (members.length + (await repo.countPendingOfParty(client, party.id, ctx.now)) >= party.max_members) {
        throw new AppError(409, '정원이 찼습니다.', 'PARTY_FULL');
      }
      // 상대가 나를 차단했으면 접속·상태를 확인하기 전에 조용한 행으로 처리한다(응답이 정상 초대와 구별되지 않게)
      const silent = await isBlocked(target.account_id, accountId);
      if (!silent) {
        if (!registry.ofCharacter(target.id)) throw new AppError(422, '접속 중이 아닌 모험가입니다.', 'TARGET_OFFLINE');
        if (
          (await partyRepo.findPartyIdOf(client, target.id)) !== null ||
          getQueueStore().get(target.id) ||
          (await dungeonRepo.inPartyRun(client, target.id))
        ) {
          throw new AppError(409, '다른 파티나 대기열에 있는 모험가입니다.', 'TARGET_BUSY');
        }
      }
      const minuteAgo = new Date(ctx.now.getTime() - 60_000);
      const targetAgo = new Date(ctx.now.getTime() - cfg.invitePerTargetSeconds * 1000);
      if (
        (await repo.countRecentBy(client, ctx.char.id, minuteAgo)) >= cfg.invitePerMin ||
        (await repo.countRecentBy(client, ctx.char.id, targetAgo, target.id)) > 0
      ) {
        throw new AppError(429, '초대는 잠시 후에 다시 보낼 수 있습니다.', 'INVITE_COOLDOWN');
      }
      const expiresAt = new Date(ctx.now.getTime() + cfg.partyInviteSeconds * 1000);
      let invite: repo.InviteRow;
      await client.query('SAVEPOINT invite_insert');
      try {
        invite = await repo.insertInvite(client, party.id, ctx.char.id, target.id, target.account_id, ctx.now, expiresAt, silent);
      } catch (err) {
        if (!isUniqueViolation(err, 'party_invites_pending_uq')) throw err;
        await client.query('ROLLBACK TO SAVEPOINT invite_insert');
        const dup = await repo.pendingFor(client, party.id, target.id, ctx.now);
        if (!dup) throw err;
        return { status: 200, data: { invite: { id: dup.uuid, state: 'pending', expires_at: dup.expires_at.toISOString() } } };
      }
      if (silent) return { status: 201, data: { invite: { id: invite.uuid, state: 'pending', expires_at: expiresAt.toISOString() } } };
      afterCommit(client, async () => {
        const view = (await repo.incomingFor(getPool(), target.id, ctx.now)).find((v) => v.uuid === invite.uuid);
        if (view) getNotifier().partyInvite(target.id, { t: 'party.invite', ...inviteBody(view) });
      });
      return { status: 201, data: { invite: { id: invite.uuid, state: 'pending', expires_at: expiresAt.toISOString() } } };
    },
  });
}

// ---------- I2 ----------

export function respondInvite(accountId: number, characterUuid: string, p: InviteParams, body: RespondBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party/invites/:id/respond',
    requestId: body.request_id,
    payload: { id: p.id, accept: body.accept },
    handler: async (ctx) => {
      const client = ctx.client;
      const inv = await repo.findByUuid(client, p.id);
      if (!inv || inv.silent || inv.invitee_character_id !== ctx.char.id) throw NOT_FOUND();
      if (inv.state !== 'pending') throw new AppError(409, '이미 처리된 초대입니다.', 'INVITE_NOT_PENDING');
      if (inv.expires_at <= ctx.now) {
        // 롤백과 무관하게 만료로 남긴다
        await repo.setState(getPool(), inv.id, 'expired', ctx.now);
        getNotifier().partyInviteClosed(inv.inviter_character_id, inv.uuid, 'expired');
        throw new AppError(410, '초대가 만료되었습니다.', 'INVITE_EXPIRED');
      }
      const closed = (state: 'accepted' | 'declined'): void =>
        afterCommit(client, () => getNotifier().partyInviteClosed(inv.inviter_character_id, inv.uuid, state));
      if (!body.accept) {
        await repo.setState(client, inv.id, 'declined', ctx.now);
        closed('declined');
        return { status: 200, data: { declined: true } };
      }
      // 4단계 신청 수락과 같은 순서: 내 캐릭터 잠금 -> parties 잠금 -> 자격·전투력·정원
      const party = await partyRepo.lockParty(client, inv.party_id);
      if (!party || party.state !== 'forming') throw new AppError(404, '모집이 끝난 파티입니다.', 'PARTY_NOT_FOUND');
      await ensureFree(ctx);
      const members = await partyRepo.activeMembers(client, party.id);
      if (members.length >= party.max_members) throw new AppError(409, '정원이 찼습니다.', 'PARTY_FULL');
      // 던전 입장 자격(오늘 횟수, 난이도 해금)은 보지 않는다: 초대 파티는 필드 사냥만 같이 할 수도 있고, 출발(start) 때 다시 본다
      const power = await powerOf(client, ctx.char.id, ctx.char.class, ctx.char.level);
      if (power < party.min_power) throw new AppError(422, '전투력이 부족합니다.', 'POWER_TOO_LOW', { need: party.min_power, have: power });
      await client.query('SAVEPOINT invite_join');
      try {
        await partyRepo.insertMember(client, party.id, ctx.char.id, ctx.char.accountId, false, ctx.now);
      } catch (err) {
        if (!isUniqueViolation(err, 'party_members_one_active')) throw err;
        await client.query('ROLLBACK TO SAVEPOINT invite_join');
        throw new AppError(409, '이미 파티에 있습니다.', 'ALREADY_IN_PARTY');
      }
      await repo.setState(client, inv.id, 'accepted', ctx.now);
      await partyRepo.cancelOtherApplications(client, ctx.char.id, 0, ctx.now);
      for (const o of await repo.cancelOthersOf(client, ctx.char.id, inv.id)) {
        afterCommit(client, () => getNotifier().partyInviteClosed(o.inviter, o.uuid, 'cancelled'));
      }
      await partyRepo.resetReady(client, party.id, party.leader_character_id);
      await partyRepo.bump(client, party.id, ctx.now);
      closed('accepted');
      const fresh = (await partyRepo.getParty(client, party.id)) as partyRepo.PartyRow;
      return { status: 200, data: { party: await buildPartyView(client, fresh, ctx.char.id, ctx.now) } };
    },
  });
}

// ---------- I3 ----------

export async function cancelInvite(accountId: number, characterUuid: string, p: InviteParams) {
  const me = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!me) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const inv = await repo.findByUuid(getPool(), p.id);
  if (!inv || inv.inviter_character_id !== me.id) throw NOT_FOUND();
  if (inv.state === 'pending' && (await repo.setState(getPool(), inv.id, 'cancelled', getNow()))) {
    if (!inv.silent) getNotifier().partyInviteClosed(inv.invitee_character_id, inv.uuid, 'cancelled');
  }
  return { cancelled: true };
}

/** hello 직후와 GET /party가 쓰는 받은 초대 목록 */
export async function incomingInvites(characterId: number, now: Date) {
  return (await repo.incomingFor(getPool(), characterId, now)).map(inviteBody);
}
