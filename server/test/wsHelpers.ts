import { randomUUID } from 'node:crypto';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import type { Express } from 'express';
import WebSocket from 'ws';
import { attachRealtime, type RealtimeHandle } from '../src/domains/chat/wsServer';
import { attachRelay } from '../src/domains/relay/relayServer';
import type { Frame } from '../src/domains/chat/wsProtocol';
import { CLIENT_VERSION } from './helpers';
import type { Hero } from './economyHelpers';

export interface TestServer {
  port: number;
  stop(): Promise<void>;
}

/** 실제 포트에 HTTP + WebSocket 서버를 띄운다 */
export async function startServer(app: Express): Promise<TestServer> {
  const server = http.createServer(app);
  const handle: RealtimeHandle = await attachRealtime(server);
  const relay = await attachRelay(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as AddressInfo).port;
  return {
    port,
    async stop() {
      await relay.close();
      await handle.close();
      await new Promise<void>((resolve) => {
        server.close(() => resolve());
        server.closeAllConnections();
      });
    },
  };
}

export const sleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));

export async function until(fn: () => boolean | Promise<boolean>, ms = 3000): Promise<void> {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    if (await fn()) return;
    await sleep(20);
  }
  throw new Error('until: 시간 초과');
}

export class WsClient {
  frames: Frame[] = [];
  closed: { code: number } | null = null;
  private used = new Set<number>();

  private constructor(readonly ws: WebSocket) {
    ws.on('message', (d) => this.frames.push(JSON.parse(d.toString()) as Frame));
    ws.on('close', (code) => {
      this.closed = { code };
    });
    ws.on('error', () => undefined);
  }

  static open(port: number, path = '/ws'): Promise<WsClient> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`ws://127.0.0.1:${port}${path}`);
      const c = new WsClient(ws);
      ws.once('open', () => resolve(c));
      ws.once('error', reject);
    });
  }

  send(frame: Record<string, unknown>): void {
    this.ws.send(JSON.stringify(frame));
  }

  /** 아직 소비하지 않은 프레임 중 조건에 맞는 첫 프레임을 기다린다(소비한다) */
  async waitFor(pred: (f: Frame) => boolean, ms = 3000): Promise<Frame> {
    const end = Date.now() + ms;
    for (;;) {
      for (let i = 0; i < this.frames.length; i++) {
        const f = this.frames[i] as Frame;
        if (!this.used.has(i) && pred(f)) {
          this.used.add(i);
          return f;
        }
      }
      if (Date.now() > end) throw new Error(`waitFor 시간 초과. 안 쓴 프레임: ${JSON.stringify(this.frames.filter((_, i) => !this.used.has(i)))}`);
      await sleep(10);
    }
  }
  waitT(t: string, ms?: number): Promise<Frame> {
    return this.waitFor((f) => f.t === t, ms);
  }

  /** 일정 시간 안에 조건에 맞는 프레임이 오지 않았음을 확인 */
  async expectNone(pred: (f: Frame) => boolean, ms = 300): Promise<void> {
    await sleep(ms);
    const hit = this.frames.find((f, i) => !this.used.has(i) && pred(f));
    if (hit) throw new Error(`받지 말아야 할 프레임: ${JSON.stringify(hit)}`);
  }

  async waitClose(ms = 3000): Promise<number> {
    await until(() => this.closed !== null, ms);
    return (this.closed as { code: number }).code;
  }

  close(): void {
    if (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING) this.ws.close();
  }
}

export interface HelloOpts {
  since?: number | null;
  token?: string;
  version?: string;
  characterId?: string;
}

export function helloFrame(h: Hero, o: HelloOpts = {}): Record<string, unknown> {
  return {
    t: 'hello',
    v: 1,
    token: o.token ?? h.s.access,
    client_version: o.version ?? CLIENT_VERSION,
    character_id: o.characterId ?? h.id,
    since: o.since ?? null,
  };
}

/** 연결하고 hello를 보낸 뒤 ready와 backlog까지 받는다 */
export async function connect(port: number, h: Hero, o: HelloOpts = {}): Promise<{ c: WsClient; ready: Frame; backlog: Frame }> {
  const c = await WsClient.open(port);
  c.send(helloFrame(h, o));
  const ready = await c.waitT('ready');
  const backlog = await c.waitT('backlog');
  return { c, ready, backlog };
}

export const chat = (channel: string, text: string, extra: Record<string, unknown> = {}): Record<string, unknown> => ({
  t: 'chat.send',
  cid: randomUUID(),
  channel,
  text,
  ...extra,
});
