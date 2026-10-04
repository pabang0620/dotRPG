// 필드 세션 방의 중계 훅(phase8_api.md 4.8, 6.3).
import { withTransaction } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { withPartyLocks } from '../party/partyTx';
import { candidatesOf, handoff } from './fieldCore';
import { notifyFieldChanged } from './fieldNotify';
import * as repo from './fieldRepository';
import { electHost } from './hostElection';

export interface FieldHookMember {
  accountId: number;
  characterId: number;
  characterUuid: string;
  seat: number;
}

/** 연결 확인: disconnected 에서 60초 안에 돌아오면 playing, 호스트가 없으면 이 멤버(가장 우선인 연결자)가 호스트 */
export async function fieldPeerConnected(sessionUuid: string, m: FieldHookMember): Promise<void> {
  await withPartyLocks(m.accountId, m.characterUuid, { kind: 'self' }, async (ctx) => {
    const session = await repo.lockByUuid(ctx.client, sessionUuid);
    if (!session || session.state !== 'active') return;
    const row = await repo.memberRow(ctx.client, session.id, m.characterId);
    if (!row || row.state === 'left') return;
    let changed = false;
    if (row.state === 'disconnected') {
      await repo.setMemberState(ctx.client, session.id, m.characterId, 'playing', ctx.now);
      changed = true;
    } else await repo.touchMember(ctx.client, session.id, m.characterId, ctx.now);
    if (session.host_character_id === null) {
      const members = await repo.activeMembers(ctx.client, session.id);
      const next = await handoff(ctx.client, session, members, ctx.now, { freshSelf: m.characterId });
      changed = changed || next !== null;
    }
    if (changed) {
      await repo.bump(ctx.client, session.id, ctx.now);
      notifyFieldChanged(ctx.client, session.id);
    }
  });
}

/** 연결이 끊겼다: playing·joined 멤버는 바로 disconnected */
export async function fieldPeerLost(sessionUuid: string, m: FieldHookMember): Promise<void> {
  await withPartyLocks(m.accountId, m.characterUuid, { kind: 'self' }, async (ctx) => {
    const session = await repo.lockByUuid(ctx.client, sessionUuid);
    if (!session || session.state !== 'active') return;
    const row = await repo.memberRow(ctx.client, session.id, m.characterId);
    if (row && (row.state === 'playing' || row.state === 'joined')) {
      await repo.setMemberState(ctx.client, session.id, m.characterId, 'disconnected', ctx.now);
      await repo.bump(ctx.client, session.id, ctx.now);
      notifyFieldChanged(ctx.client, session.id);
    }
  });
}

/** 호스트 연결이 grace 이상 없다: 서버가 직접 인계한다(후보가 없으면 호스트 없음 NULL) */
export async function fieldHostLost(
  sessionUuid: string,
  lostSeat: number,
  observedEpoch: number,
  connectedSeats: number[],
): Promise<{ hostSeat: number | null; hostEpoch: number } | null> {
  return withTransaction(async (client) => {
    const session = await repo.lockByUuid(client, sessionUuid);
    if (!session || session.state !== 'active') return null;
    const members = await repo.activeMembers(client, session.id);
    const host = members.find((m) => m.character_id === session.host_character_id);
    if (session.host_epoch !== observedEpoch || !host || host.seat !== lostSeat) {
      return { hostSeat: host ? host.seat : null, hostEpoch: session.host_epoch };
    }
    const now = getNow();
    const cands = (await candidatesOf(client, session, members, now)).map((c) => ({ ...c, fresh: connectedSeats.includes(c.seat) }));
    const next = electHost(cands, host.character_id);
    await repo.setHost(client, session.id, next, now);
    if (host.state !== 'disconnected') await repo.setMemberState(client, session.id, host.character_id, 'disconnected', now);
    await repo.bump(client, session.id, now);
    notifyFieldChanged(client, session.id);
    const nextSeat = members.find((m) => m.character_id === next)?.seat ?? null;
    return { hostSeat: nextSeat, hostEpoch: session.host_epoch + 1 };
  });
}
