// 접속 한 건의 서버 쪽 상태(계정 하나에 연결 하나, hello에서 캐릭터 하나를 고른다)
import type { WebSocket } from 'ws';
import { metrics } from '../../ops/metrics';
import { CLOSE, type Frame } from './wsProtocol';

const MAX_BUFFERED_BYTES = 1024 * 1024;

export class ChatSession {
  ready = false;
  closed = false;
  mapId: string | null = null;
  /** hello.caps(8단계). steam_p2p는 그 계정에 Steam 연결이 있을 때만 true로 인정한다 */
  caps = { steamP2p: false };
  shard = 1;
  blocks = new Set<number>();
  /** 제재로 인한 채팅 금지 끝 시각(ms). 0이면 없음 */
  sanctionMuteUntil = 0;
  sanctionMuteSource: 'sanction' | null = null;
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
