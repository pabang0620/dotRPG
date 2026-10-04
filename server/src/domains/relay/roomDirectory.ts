// 방 멤버십 질의·이벤트 인터페이스(phase8_api.md 3.1). 임베디드는 함수 호출(DB 구현)이고, 중계를 분리하면 PG LISTEN/NOTIFY로 같은 이벤트를 받는다.
import { getPool } from '../../db/pool';
import { fieldHostLost, fieldPeerConnected, fieldPeerLost } from '../fieldsessions/relayHooks';
import { relayJoin } from '../partyruns/partyRunService';
import { runHostLost, runPeerLost } from '../partyruns/relayHooks';
import { snapshotOf, type RoomSnapshot } from './relayRepository';
import type { RoomKind } from './relayTicket';

export interface DirMember {
  accountId: number;
  characterId: number;
  characterUuid: string;
  seat: number;
}

export interface RoomDirectory {
  snapshot(kind: RoomKind, uuid: string): Promise<RoomSnapshot | null>;
  peerConnected(kind: RoomKind, uuid: string, m: DirMember): Promise<void>;
  peerLost(kind: RoomKind, uuid: string, m: DirMember): Promise<void>;
  hostLost(kind: RoomKind, uuid: string, lostSeat: number, epoch: number, connectedSeats: number[]): Promise<{ hostSeat: number | null; hostEpoch: number } | null>;
}

export const dbDirectory: RoomDirectory = {
  snapshot: (kind, uuid) => snapshotOf(getPool(), kind, uuid),
  peerConnected: (kind, uuid, m) => (kind === 'run' ? relayJoin(m.accountId, m.characterUuid, uuid) : fieldPeerConnected(uuid, m)),
  peerLost: (kind, uuid, m) => (kind === 'run' ? runPeerLost(uuid, m) : fieldPeerLost(uuid, m)),
  hostLost: (kind, uuid, seat, epoch, connected) => (kind === 'run' ? runHostLost(uuid, seat, epoch, connected) : fieldHostLost(uuid, seat, epoch, connected)),
};

let current: RoomDirectory = dbDirectory;
export const getDirectory = (): RoomDirectory => current;
export const setDirectory = (d: RoomDirectory | null): void => {
  current = d ?? dbDirectory;
};
