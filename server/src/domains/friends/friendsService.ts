// 친구(F1~F5): 계정 단위, 요청 -> 수락. 접속 상태·위치는 맺어진 친구에게만 보인다.
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { findCharacterByUuid } from '../chat/chatRepository';
import { friendLinks, presenceEntries } from '../chat/presenceService';
import { getNotifier } from '../chat/realtimeNotifier';
import type { StoredResult } from '../economy/economyService';
import { runSocial } from '../social/socialTx';
import * as repo from './friendsRepository';
import type { RespondBody, SendRequestBody } from './friendsValidation';

const PLAYER_NOT_FOUND = () => new AppError(404, '모험가를 찾을 수 없습니다.', 'PLAYER_NOT_FOUND');
const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);

// ---------- F1 ----------

export async function listFriends(accountId: number) {
  const cfg = getConfig().social;
  const db = getPool();
  await repo.expireOld(db, accountId, cfg.friendRequestDays);
  const entries = await presenceEntries(await friendLinks(accountId));
  entries.sort((a, b) => Number(b.online) - Number(a.online) || a.character.name.localeCompare(b.character.name, 'ko'));
  const [incoming, outgoing] = await Promise.all([repo.pendingViews(db, accountId, 'in'), repo.pendingViews(db, accountId, 'out')]);
  const person = (v: repo.RequestView) => ({ id: v.char_uuid, name: v.name, class: v.class, level: v.level });
  return {
    friends: entries.map((e) => ({ id: e.id, character: e.character, online: e.online, where: e.where, since: iso(e.since) })),
    incoming: incoming.map((v) => ({
      id: v.uuid,
      from: person(v),
      created_at: v.created_at.toISOString(),
      expires_at: new Date(v.created_at.getTime() + cfg.friendRequestDays * 86_400_000).toISOString(),
    })),
    outgoing: outgoing.map((v) => ({ id: v.uuid, to: person(v), created_at: v.created_at.toISOString() })),
  };
}

async function friendView(friendship: repo.FriendshipRow, meAccount: number) {
  const other = friendship.requester_account_id === meAccount ? friendship.target_account_id : friendship.requester_account_id;
  const [e] = await presenceEntries([{ friendshipUuid: friendship.uuid, otherAccountId: other, since: friendship.responded_at }]);
  if (!e) throw PLAYER_NOT_FOUND();
  return { id: e.id, character: e.character, online: e.online, where: e.where, since: iso(e.since) };
}

// ---------- F2 ----------

export async function sendRequest(accountId: number, body: SendRequestBody): Promise<StoredResult> {
  const cfg = getConfig().social;
  const db = getPool();
  const mine = await repo.ownedAliveCharacter(db, accountId, body.character_id);
  if (!mine) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const target = await findCharacterByUuid(db, body.target);
  if (!target || target.deleted) throw PLAYER_NOT_FOUND();
  if (target.account_id === accountId) throw new AppError(422, '자기 자신에게는 할 수 없습니다.', 'CANNOT_TARGET_SELF');
  const other = target.account_id;
  let notify = null as 'request' | 'accepted' | null;

  const result = await runSocial({
    accountId,
    alsoLock: [other],
    endpoint: 'POST /friends/requests',
    requestId: body.request_id,
    payload: { character_id: body.character_id, target: body.target },
    handler: async (client) => {
      await repo.expireOld(client, accountId, cfg.friendRequestDays);
      if (await repo.blockedEitherWay(client, accountId, other)) {
        throw new AppError(409, '차단한 사람입니다. 먼저 차단을 해제하세요.', 'YOU_BLOCKED_TARGET');
      }
      const live = await repo.findLive(client, accountId, other);
      if (live?.state === 'accepted') throw new AppError(409, '이미 친구입니다.', 'ALREADY_FRIENDS');
      if ((await repo.countFriends(client, accountId)) >= cfg.friendMax) {
        throw new AppError(422, '친구가 가득 찼습니다.', 'FRIEND_LIST_FULL');
      }
      if (!live && (await repo.countOutgoing(client, accountId)) >= cfg.friendPendingOutMax) {
        throw new AppError(429, '보낸 요청이 너무 많습니다.', 'TOO_MANY_PENDING');
      }
      const out = await resolvePending(client, live, accountId, other, mine.id, target.id);
      if (out.kind === 'existing') {
        return { status: 200, data: { status: 'pending', request: await requestView(client, out.row.uuid, accountId) } };
      }
      if (out.kind === 'accepted') {
        notify = 'accepted';
        return { status: 200, data: { status: 'accepted', friend: await friendView(out.row, accountId) } };
      }
      // 조용한 무시로 만든 행도 응답 모양은 같다(내 보낸 목록에만 보이고 상대에게는 알림이 가지 않는다)
      if (!out.row.silent) notify = 'request';
      return { status: 201, data: { status: 'pending', request: await requestView(client, out.row.uuid, accountId) } };
    },
  });
  if (!result.replay && notify) getNotifier().friendsChanged(other, notify);
  return result;
}

type Pending =
  | { kind: 'existing'; row: repo.FriendshipRow }
  | { kind: 'accepted'; row: repo.FriendshipRow }
  | { kind: 'created'; row: repo.FriendshipRow };

async function acceptChecked(client: PoolClient, row: repo.FriendshipRow, a: number, b: number): Promise<repo.FriendshipRow> {
  const cfg = getConfig().social;
  if ((await repo.countFriends(client, a)) >= cfg.friendMax || (await repo.countFriends(client, b)) >= cfg.friendMax) {
    throw new AppError(422, '친구가 가득 찼습니다.', 'FRIEND_LIST_FULL');
  }
  const accepted = await repo.accept(client, row.id);
  if (!accepted) throw new AppError(409, '이미 처리된 요청입니다.', 'REQUEST_NOT_PENDING');
  return accepted;
}

/** 이미 있는 대기 요청 처리(같은 방향이면 그대로, 반대 방향이면 상호 수락), 없으면 쿨다운·조용한 무시 검사 후 만든다 */
async function resolvePending(
  client: PoolClient,
  live: repo.FriendshipRow | null,
  me: number,
  other: number,
  myChar: number,
  targetChar: number,
): Promise<Pending> {
  const cfg = getConfig().social;
  if (live && live.state === 'pending') {
    if (live.requester_account_id === me) return { kind: 'existing', row: live };
    return { kind: 'accepted', row: await acceptChecked(client, live, me, other) };
  }
  const wait = await repo.rerequestWaitSec(client, me, other, cfg.friendRerequestHours);
  if (wait > 0) throw new AppError(429, '잠시 뒤에 다시 요청해 주세요.', 'REREQUEST_COOLDOWN', { retry_after_sec: wait });
  // 조용한 무시: 상대가 나를 차단했거나 상대의 받은 요청·친구가 가득 찼다(상태가 드러나지 않게 정상 응답)
  const silent =
    (await repo.blockedEitherWay(client, other, me)) ||
    (await repo.countIncoming(client, other)) >= cfg.friendPendingInMax ||
    (await repo.countFriends(client, other)) >= cfg.friendMax;
  await client.query('SAVEPOINT friend_request');
  try {
    const row = await repo.insertRequest(client, me, other, myChar, targetChar, silent);
    await client.query('RELEASE SAVEPOINT friend_request');
    return { kind: 'created', row };
  } catch (err) {
    if (!isUniqueViolation(err, 'friendships_pair_live')) throw err;
    await client.query('ROLLBACK TO SAVEPOINT friend_request');
    // 동시에 반대 방향 요청이 먼저 들어갔다: 다시 읽어 같은 규칙으로
    const again = await repo.findLive(client, me, other);
    if (!again) throw err;
    if (again.state === 'accepted') throw new AppError(409, '이미 친구입니다.', 'ALREADY_FRIENDS');
    if (again.requester_account_id === me) return { kind: 'existing', row: again };
    return { kind: 'accepted', row: await acceptChecked(client, again, me, other) };
  }
}

async function requestView(client: PoolClient, uuid: string, accountId: number) {
  const views = await repo.pendingViews(client, accountId, 'out');
  const v = views.find((x) => x.uuid === uuid);
  if (!v) throw PLAYER_NOT_FOUND();
  return { id: v.uuid, to: { id: v.char_uuid, name: v.name, class: v.class, level: v.level }, created_at: v.created_at.toISOString() };
}

// ---------- F3 ----------

export async function respond(accountId: number, requestUuid: string, body: RespondBody): Promise<StoredResult> {
  const cfg = getConfig().social;
  const found = await repo.findByUuid(getPool(), requestUuid);
  if (!found || found.silent || found.target_account_id !== accountId) throw new AppError(404, '요청을 찾을 수 없습니다.', 'REQUEST_NOT_FOUND');
  const other = found.requester_account_id;
  let notified = false;
  const result = await runSocial({
    accountId,
    alsoLock: [other],
    endpoint: 'POST /friends/requests/:id/respond',
    requestId: body.request_id,
    payload: { id: requestUuid, accept: body.accept },
    handler: async (client) => {
      const row = await repo.findByUuid(client, requestUuid);
      if (!row || row.silent || row.target_account_id !== accountId) throw new AppError(404, '요청을 찾을 수 없습니다.', 'REQUEST_NOT_FOUND');
      if (row.state !== 'pending') throw new AppError(409, '이미 처리된 요청입니다.', 'REQUEST_NOT_PENDING');
      if (row.created_at.getTime() + cfg.friendRequestDays * 86_400_000 <= Date.now()) {
        await repo.cancel(getPool(), row.id, accountId);
        throw new AppError(410, '요청이 만료되었습니다.', 'REQUEST_EXPIRED');
      }
      if (!body.accept) {
        if (!(await repo.decline(client, row.id, accountId))) throw new AppError(409, '이미 처리된 요청입니다.', 'REQUEST_NOT_PENDING');
        return { status: 200, data: { declined: true } };
      }
      const accepted = await acceptChecked(client, row, accountId, other);
      notified = true;
      return { status: 200, data: { friend: await friendView(accepted, accountId) } };
    },
  });
  if (!result.replay && notified) getNotifier().friendsChanged(other, 'accepted');
  return result;
}

// ---------- F4, F5 ----------

export async function cancelRequest(accountId: number, uuid: string) {
  const row = await repo.findByUuid(getPool(), uuid);
  if (!row || row.requester_account_id !== accountId) throw new AppError(404, '요청을 찾을 수 없습니다.', 'REQUEST_NOT_FOUND');
  if (row.state === 'pending') {
    await repo.cancel(getPool(), row.id, accountId);
    return { cancelled: true };
  }
  // 이미 취소된 것을 또 취소해도 성공. 수락·거절로 끝난 요청은 취소할 수 없다
  const again = await repo.findByUuid(getPool(), uuid);
  if (again?.state === 'cancelled') return { cancelled: true };
  throw new AppError(404, '요청을 찾을 수 없습니다.', 'REQUEST_NOT_FOUND');
}

export async function removeFriend(accountId: number, uuid: string) {
  const row = await repo.findByUuid(getPool(), uuid);
  if (!row || (row.requester_account_id !== accountId && row.target_account_id !== accountId) || row.state === 'pending') {
    throw new AppError(404, '친구를 찾을 수 없습니다.', 'FRIEND_NOT_FOUND');
  }
  if (row.state === 'accepted' && (await repo.remove(getPool(), row.id, accountId))) {
    const other = row.requester_account_id === accountId ? row.target_account_id : row.requester_account_id;
    getNotifier().friendsChanged(other, 'removed');
    return { removed: true };
  }
  if (row.state === 'removed') return { removed: true };
  throw new AppError(404, '친구를 찾을 수 없습니다.', 'FRIEND_NOT_FOUND');
}
