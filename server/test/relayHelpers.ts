import type { Express } from 'express';
import request from 'supertest';
import WebSocket from 'ws';
import type { Frame } from '../src/domains/chat/wsProtocol';
import { decodeFrame, encodeBatch, encodePing, type Packet } from '../src/domains/relay/relayFrames';
import { auth, CLIENT_VERSION } from './helpers';
import type { Hero } from './partyHelpers';
import { sleep, until } from './wsHelpers';

export interface RxPacket {
  channel: number;
  from: number;
  payload: Buffer;
}

/** 중계 클라이언트(시험용): 제어는 JSON 텍스트, 데이터는 BATCH/PING/PONG 바이너리 */
export class RelayClient {
  texts: Frame[] = [];
  packets: RxPacket[] = [];
  pongs: number[] = [];
  closed: { code: number } | null = null;
  private usedT = new Set<number>();
  private usedP = new Set<number>();

  private constructor(readonly ws: WebSocket) {
    ws.on('message', (d: Buffer, isBinary: boolean) => {
      if (!isBinary) {
        this.texts.push(JSON.parse(d.toString()) as Frame);
        return;
      }
      const f = decodeFrame(d, 1 << 20);
      if (f.kind === 'batch') for (const p of f.packets) this.packets.push({ channel: p.channel, from: p.addr, payload: Buffer.from(p.payload) });
      else if (f.kind === 'pong') this.pongs.push(f.nonce);
    });
    ws.on('close', (code) => {
      this.closed = { code };
    });
    ws.on('error', () => undefined);
  }

  static open(port: number): Promise<RelayClient> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`ws://127.0.0.1:${port}/relay`);
      const c = new RelayClient(ws);
      ws.once('open', () => resolve(c));
      ws.once('error', reject);
    });
  }

  sendText(frame: Record<string, unknown>): void {
    this.ws.send(JSON.stringify(frame));
  }

  hello(ticket: string, over: Record<string, unknown> = {}): void {
    this.sendText({ t: 'hello', v: 1, ticket, client_version: CLIENT_VERSION, wire: 1, resume: false, ...over });
  }

  sendBatch(packets: Packet[]): void {
    this.ws.send(encodeBatch(packets), { binary: true });
  }

  send(channel: number, addr: number, payload: Buffer | string = 'x'): void {
    this.sendBatch([{ channel, addr, payload: Buffer.from(payload) }]);
  }

  ping(nonce: number): void {
    this.ws.send(encodePing(nonce), { binary: true });
  }

  async waitText(pred: (f: Frame) => boolean, ms = 3000): Promise<Frame> {
    const end = Date.now() + ms;
    for (;;) {
      for (let i = 0; i < this.texts.length; i++) {
        if (!this.usedT.has(i) && pred(this.texts[i] as Frame)) {
          this.usedT.add(i);
          return this.texts[i] as Frame;
        }
      }
      if (Date.now() > end) throw new Error(`waitText 시간 초과: ${JSON.stringify(this.texts.filter((_, i) => !this.usedT.has(i)))}`);
      await sleep(10);
    }
  }
  waitT(t: string, ms?: number): Promise<Frame> {
    return this.waitText((f) => f.t === t, ms);
  }

  async waitPacket(pred: (p: RxPacket) => boolean = () => true, ms = 3000): Promise<RxPacket> {
    const end = Date.now() + ms;
    for (;;) {
      for (let i = 0; i < this.packets.length; i++) {
        if (!this.usedP.has(i) && pred(this.packets[i] as RxPacket)) {
          this.usedP.add(i);
          return this.packets[i] as RxPacket;
        }
      }
      if (Date.now() > end) throw new Error(`waitPacket 시간 초과(받은 ${this.packets.length}개)`);
      await sleep(10);
    }
  }

  async expectNoPacket(pred: (p: RxPacket) => boolean = () => true, ms = 300): Promise<void> {
    await sleep(ms);
    const hit = this.packets.find((p, i) => !this.usedP.has(i) && pred(p));
    if (hit) throw new Error(`받지 말아야 할 패킷: ${JSON.stringify(hit)}`);
  }

  async expectNoText(pred: (f: Frame) => boolean, ms = 300): Promise<void> {
    await sleep(ms);
    const hit = this.texts.find((f, i) => !this.usedT.has(i) && pred(f));
    if (hit) throw new Error(`받지 말아야 할 프레임: ${JSON.stringify(hit)}`);
  }

  async waitClose(ms = 3000): Promise<number> {
    await until(() => this.closed !== null, ms);
    return (this.closed as { code: number }).code;
  }

  terminate(): void {
    this.ws.terminate();
  }

  close(): void {
    if (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING) this.ws.close();
  }
}

export interface TicketResponse {
  status: number;
  headers: Record<string, string>;
  body: { data: { ticket: string; relay_url: string; expires_in: number; room: { seat: number; host_seat: number | null; host_epoch: number; members: unknown[]; transport: { current: string; epoch: number } } }; errors?: { code: string; current?: { transport: string; epoch: number } } };
}

/** T1은 본문이 `{}`(.strict())라 request_id를 보내지 않는다 */
export const ticketFor = (app: Express, h: Hero, kind: 'run' | 'field', roomId: string): Promise<TicketResponse> =>
  request(app).post(`/characters/${h.id}/rooms/${kind}/${roomId}/relay-ticket`).set(auth(h.s)).send({}) as unknown as Promise<TicketResponse>;

/** 티켓을 받아 연결하고 ready까지 기다린다 */
export async function joinRoom(
  app: Express,
  port: number,
  h: Hero,
  kind: 'run' | 'field',
  roomId: string,
  over: Record<string, unknown> = {},
): Promise<{ c: RelayClient; ready: Frame; ticket: string }> {
  const t = await ticketFor(app, h, kind, roomId);
  if (t.status !== 200) throw new Error(`ticket ${t.status} ${JSON.stringify(t.body)}`);
  const c = await RelayClient.open(port);
  c.hello(t.body.data.ticket, over);
  const ready = await c.waitT('ready');
  return { c, ready, ticket: t.body.data.ticket };
}

