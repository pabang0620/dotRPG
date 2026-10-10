// 접속 한 건의 서버 쪽 상태(계정 하나에 연결 하나, hello에서 캐릭터 하나를 고른다)
import type { WebSocket } from 'ws';
import { metrics } from '../../ops/metrics';
import { CLOSE, type Frame } from './wsProtocol';

export interface TownLook {
  map: string;
  x: number;
  y: number;
  f: number;
  m: boolean;
  cls: number;
  career: number;
  skin: string;
  weapon: string;
  level: number;
}

export interface ServerProfile {
  cls: number;
  level: number;
  career: number;
  weapon: string;
  /** 이 계정이 소유한 외형 id(없으면 빈 집합) */
  ownedSkins: Set<string>;
}

const MAX_BUFFERED_BYTES = 1024 * 1024;

export class ChatSession {
  ready = false;
  closed = false;
  mapId: string | null = null;
  /** 마을에 서 있을 때의 모습·위치(townPresence). 마을 밖이면 null */
  town: TownLook | null = null;
  townAt = 0;
  /** 15단계 L6: 마지막 presence.set 맵 변경, 마지막 마을 진입 시각(서로 따로 센다: 포털 이동은 둘이 연달아 온다) */
  mapChangeAt = 0;
  townEnterAt = 0;
  mapPushPending = false;
  /** hello.caps(8단계). steam_p2p는 그 계정에 Steam 연결이 있을 때만 true로 인정한다 */
  caps = { steamP2p: false };
  shard = 1;
  blocks = new Set<number>();
  /** 제재로 인한 채팅 금지 끝 시각(ms). 0이면 없음 */
  sanctionMuteUntil = 0;
  sanctionMuteSource: 'sanction' | null = null;
  /** 9단계: 액세스 토큰의 세션 id(sid). 다른 곳에서 로그인하면 이 값과 다른 통지가 와서 끊긴다 */
  familyId: string | null = null;
  /** 9단계: 마을 표시에 쓰는 서버 값(townPresence). 클라이언트가 보낸 값은 무시한다 */
  profile: ServerProfile | null = null;
  profileAt = 0;
  tokenWarned = false;
  lastFrameAt = Date.now();
  lastRevalidateAt = Date.now();
  /** 접속 시각: 신고 증거의 일반 채팅은 이 시각 이후 것만 "내가 본 줄"이다 */
  readonly connectedAt = new Date();
  private buffer: Frame[] = [];

  constructor(
    readonly ws: WebSocket,
    readonly accountId: number,
    readonly accountUuid: string,
    readonly characterId: number,
    readonly characterUuid: string,
    readonly characterName: string,
    public tokenExpSec: number,
    private readonly queueMax: number,
  ) {}

  /** 준비 전(backlog 구성 중)에는 쌓아 두고, 준비 뒤에는 바로 보낸다 */
  send(frame: Frame): void {
    if (this.closed) return;
    if (!this.ready) {
      if (this.buffer.length >= this.queueMax) {
        this.close(CLOSE.SLOW_CONSUMER, 'SLOW_CONSUMER', true);
        return;
      }
      this.buffer.push(frame);
      return;
    }
    this.sendNow(frame);
  }

  sendNow(frame: Frame): void {
    if (this.closed || this.ws.readyState !== this.ws.OPEN) return;
    if (this.ws.bufferedAmount > MAX_BUFFERED_BYTES) {
      this.close(CLOSE.SLOW_CONSUMER, 'SLOW_CONSUMER', true);
      return;
    }
    this.ws.send(JSON.stringify(frame));
  }

  /** backlog까지 보낸 뒤 쌓인 프레임을 내보낸다. chat.msg는 cursor 이하(이미 backlog에 있다)를 버린다 */
  flush(cursor: number): void {
    this.ready = true;
    const pending = this.buffer;
    this.buffer = [];
    for (const f of pending) {
      if (f.t === 'chat.msg' && typeof f.seq === 'number' && f.seq <= cursor) continue;
      this.sendNow(f);
    }
  }

  /** 끊기 직전에 bye를 보내고 닫는다 */
  close(code: number, reason: string, reconnect: boolean, retryAfterMs?: number): void {
    if (this.closed) return;
    this.closed = true;
    metrics.recordWsClose(reason);
    try {
      if (this.ws.readyState === this.ws.OPEN) {
        const bye: Frame = { t: 'bye', code, reason, reconnect };
        if (retryAfterMs !== undefined) bye.retry_after_ms = retryAfterMs;
        this.ws.send(JSON.stringify(bye));
        this.ws.close(code, reason.slice(0, 100));
      }
    } catch {
      this.ws.terminate();
    }
  }
}
