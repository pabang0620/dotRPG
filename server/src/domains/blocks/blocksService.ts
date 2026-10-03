// 차단(B1~B3): 계정 단위. 차단하면 친구 관계·요청·파티 초대가 함께 닫힌다.
import { getConfig } from '../../config/env';
import { getPool, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { findCharacterByUuid } from '../chat/chatRepository';
import { getNotifier } from '../chat/realtimeNotifier';
import { lockAccounts } from '../social/socialTx';
import * as repo from './blocksRepository';

export async function listBlocks(accountId: number) {
  const rows = await repo.listLive(getPool(), accountId);
  return { blocks: rows.map((b) => ({ id: b.uuid, name: b.blocked_name, blocked_at: b.created_at.toISOString() })) };
}

export async function putBlock(accountId: number, characterUuid: string): Promise<{ status: number; data: unknown }> {
  const cfg = getConfig().social;
  const target = await findCharacterByUuid(getPool(), characterUuid);
  if (!target) throw new AppError(404, '모험가를 찾을 수 없습니다.', 'PLAYER_NOT_FOUND');
  if (target.account_id === accountId) throw new AppError(422, '자기 자신에게는 할 수 없습니다.', 'CANNOT_TARGET_SELF');
  const closedInvites: { uuid: string; inviter: number; invitee: number; silent: boolean }[] = [];
  let endedFriendship = false;
  const out = await withTransaction(async (client) => {
    await lockAccounts(client, [accountId, target.account_id]);
    const existing = await repo.findLive(client, accountId, target.account_id);
    if (existing) return { status: 200, row: existing };
    if ((await repo.countLive(client, accountId)) >= cfg.blockMax) {
      throw new AppError(422, '차단 목록이 가득 찼습니다.', 'BLOCK_LIST_FULL');
    }
    const row = await repo.insertBlock(client, accountId, target.account_id, target.id, target.name);
    endedFriendship = await repo.closeFriendships(client, accountId, target.account_id);
    closedInvites.push(...(await repo.cancelInvitesBetween(client, accountId, target.account_id)));
    return { status: 201, row };
  });
  if (out.status === 201) {
    getNotifier().blockChanged(accountId, target.account_id, true);
    for (const i of closedInvites) {
      if (!i.silent) getNotifier().partyInviteClosed(i.invitee, i.uuid, 'cancelled');
      getNotifier().partyInviteClosed(i.inviter, i.uuid, 'cancelled');
    }
    if (endedFriendship) {
      getNotifier().friendsChanged(accountId, 'removed');
      getNotifier().friendsChanged(target.account_id, 'removed');
    }
  }
  const b = out.row;
  return { status: out.status, data: { block: { id: b.uuid, name: b.blocked_name, blocked_at: b.created_at.toISOString() } } };
}

export async function deleteBlock(accountId: number, uuid: string) {
  const row = await repo.findByUuid(getPool(), uuid);
  if (!row || row.blocker_account_id !== accountId) throw new AppError(404, '차단을 찾을 수 없습니다.', 'BLOCK_NOT_FOUND');
  if (await repo.release(getPool(), row.id)) getNotifier().blockChanged(accountId, row.blocked_account_id, false);
  return { unblocked: true };
}
