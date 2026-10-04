// T1: 중계 방 입장 티켓 발급(phase8_api.md 4.3). 액세스 토큰은 "이 계정이다"만 말하므로 방·좌석·전송 세대에 묶인 짧은 단일 사용 티켓을 따로 준다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import * as charRepo from '../characters/characterRepository';
import { relayHub } from './relayHub';
import { relayMetrics } from './relayMetrics';
import { snapshotOf } from './relayRepository';
import { signTicket, type RoomKind } from './relayTicket';

export async function issueTicket(accountId: number, accountUuid: string, characterUuid: string, kind: RoomKind, roomUuid: string) {
  const cfg = getConfig();
  const db = getPool();
  const c = await charRepo.findOwnedAlive(db, accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const snap = await snapshotOf(db, kind, roomUuid);
  const me = snap?.members.find((m) => m.characterId === c.id);
  // 없는 방과 남의 방을 구분하지 않는다
  if (!snap || !me) throw new AppError(404, '방을 찾을 수 없습니다.', 'ROOM_NOT_FOUND');
  if (snap.closed) {
    relayMetrics.ticketReject('room_closed');
    throw new AppError(409, '끝난 방입니다.', 'ROOM_CLOSED');
  }
  if (!me.active) throw new AppError(404, '방을 찾을 수 없습니다.', 'ROOM_NOT_FOUND');
  if (snap.transport !== 'relay') {
    throw new AppError(409, '이 방은 중계를 쓰지 않습니다.', 'TRANSPORT_NOT_RELAY', { current: { transport: snap.transport, epoch: snap.transportEpoch } });
  }
  const adm = relayHub().admission();
  if (!cfg.relay.enabled || !adm.ok) {
    relayMetrics.unavailable(true);
    throw new AppError(503, '중계 서버가 혼잡합니다. 잠시 후 다시 시도해 주세요.', 'RELAY_UNAVAILABLE', { retry_after_sec: 5 });
  }
  relayMetrics.unavailable(false);
  const ticket = signTicket({ accountUuid, characterUuid, kind, roomUuid, seat: me.seat, epoch: snap.transportEpoch });
  return {
    relay_url: cfg.relay.publicUrl,
    ticket,
    expires_in: cfg.relay.ticketTtlSeconds,
    room: {
      kind,
      id: roomUuid,
      transport: { current: 'relay', epoch: snap.transportEpoch },
      seat: me.seat,
      host_seat: snap.hostSeat,
      host_epoch: snap.hostEpoch,
      members: snap.members.filter((m) => m.active).map((m) => ({ seat: m.seat, character_id: m.characterUuid, state: m.state })),
    },
  };
}
