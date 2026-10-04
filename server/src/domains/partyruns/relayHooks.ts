// 파티 판 방의 중계 훅(phase8_api.md 4.8): 연결 끊김은 즉시 disconnected, 호스트 연결이 사라지면 grace 뒤 서버가 직접 인계한다.
import { withTransaction } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { lockPartyAndRun, withPartyLocks } from '../party/partyTx';
import { notifyRunChanged } from '../chat/partyNotify';
import * as repo from './partyRunRepository';

export interface HookMember {
  accountId: number;
  characterId: number;
  characterUuid: string;
  seat: number;
}

/** 중계 연결이 끊겼다: playing 멤버는 12초를 기다리지 않고 바로 disconnected (60초 안에 돌아오면 playing) */
export async function runPeerLost(runUuid: string, m: HookMember): Promise<void> {
  await withPartyLocks(m.accountId, m.characterUuid, { kind: 'self' }, async (ctx) => {
    const locked = await lockPartyAndRun(ctx.client, runUuid);
    if (!locked || locked.run.state !== 'playing') return;
    const members = await repo.runMembers(ctx.client, locked.run.id);
    const me = members.find((x) => x.character_id === m.characterId);
    if (me && me.state === 'playing') await repo.setMemberState(ctx.client, locked.run.id, m.characterId, 'disconnected', ctx.now);
  });
}

/**
 * 호스트 연결이 grace 이상 없다: 연결이 있는 활성 멤버 중 가장 작은 좌석이 이어받는다(R6 승인 조건 4번과 같은 규칙).
 * 이미 세대가 바뀌었으면 현재 값을 돌려준다. 후보가 없으면 null(4단계 규칙: 멤버가 끊김 시간 초과로 정리된다)
 */
export async function runHostLost(
  runUuid: string,
  lostSeat: number,
  observedEpoch: number,
  connectedSeats: number[],
): Promise<{ hostSeat: number | null; hostEpoch: number } | null> {
  return withTransaction(async (client) => {
    const locked = await lockPartyAndRun(client, runUuid);
    if (!locked || locked.run.state !== 'playing') return null;
    const run = locked.run;
    const members = await repo.runMembers(client, run.id);
    const host = members.find((m) => m.character_id === run.host_character_id);
    if (run.host_epoch !== observedEpoch || !host || host.slot !== lostSeat) {
      return { hostSeat: host ? host.slot : null, hostEpoch: run.host_epoch };
    }
    const next = members.filter((m) => m.state === 'playing' && m.character_id !== host.character_id && connectedSeats.includes(m.slot)).sort((a, b) => a.slot - b.slot)[0];
    if (!next) return null;
    const now = getNow();
    await repo.touchMember(client, run.id, next.character_id, now);
    await repo.setHost(client, run.id, next.character_id);
    if (host.state !== 'left') await repo.setMemberState(client, run.id, host.character_id, 'disconnected', now);
    await client.query('UPDATE parties SET version = version + 1 WHERE id = $1', [run.party_id]);
    notifyRunChanged(client, run.id);
    return { hostSeat: next.slot, hostEpoch: run.host_epoch + 1 };
  });
}
