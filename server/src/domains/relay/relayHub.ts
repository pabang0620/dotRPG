// 서비스(파티 판, 필드 세션)가 중계 서버(메모리)에게 알리는 통로. 중계가 붙어 있지 않으면(테스트, 비활성) 아무 일도 하지 않는다.
// 방 상태의 진실은 DB다: 서비스는 "바뀌었다"만 알리고 중계가 DB를 다시 읽어 맞춘다(resync).
import type { RoomKind } from './relayTicket';

export interface HostPresence {
  /** 호스트 좌석 연결이 있는가 */
  connected: boolean;
  /** 호스트 연결이 없어진 지 얼마나 됐는가(ms, 연결 중이면 0) */
  absentMs: number;
}

export interface RelayHub {
  /** 메모리에 이 종류의 방이 하나라도 있는가(없으면 호출 쪽이 조회를 건너뛴다) */
  hasRooms(kind: RoomKind): boolean;
  /** 방 상태를 DB에서 다시 읽어 맞춘다(커밋 뒤에 부른다) */
  resync(kind: RoomKind, uuid: string): void;
  /** 방이 메모리에 없으면 null */
  hostPresence(kind: RoomKind, uuid: string): HostPresence | null;
  /** 이 계정의 모든 중계 연결을 끊는다(정지) */
  kickAccount(accountId: number, code: number, reason: string): number;
  /** 현재 수용 상태 */
  admission(): { ok: boolean; reason?: 'connections' | 'rooms' | 'disabled' };
  /** 지금 방·연결 수 */
  counts(): { rooms: number; conns: number };
  /** 모든 중계 연결에 bye를 보내고 끊는다(점검 시작). 끊은 수를 돌려준다 */
  closeAll(code: number, reason: string, reconnect: boolean, retryAfterMs: number): number;
}

const noop: RelayHub = {
  hasRooms: () => false,
  resync: () => undefined,
  hostPresence: () => null,
  kickAccount: () => 0,
  admission: () => ({ ok: true }),
  closeAll: () => 0,
  counts: () => ({ rooms: 0, conns: 0 }),
};

let current: RelayHub = noop;
export const relayHub = (): RelayHub => current;
export const setRelayHub = (h: RelayHub | null): void => {
  current = h ?? noop;
};

/** unused: 중계를 쓰지 않는 구성(테스트) / pending: 붙이는 중 / attached: 연결됨 / closing: 종료 중 */
let state: 'unused' | 'pending' | 'attached' | 'closing' = 'unused';
export const setRelayState = (v: typeof state): void => {
  state = v;
};
/** GET /health/ready 의 relay 항목: 리스너가 등록됐고 종료 중이 아니다 */
export const isRelayReady = (): boolean => state === 'unused' || state === 'attached';
