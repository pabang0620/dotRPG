import type { Express } from 'express';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { bidReq, cancelReq, listIron, mk, resetClock } from './auctionHelpers';
import { resetDb, shutdown } from './helpers';
import { buildApp } from './auctionHelpers';
import { connect, startServer, until, type TestServer, type WsClient } from './wsHelpers';
import type { Hero } from './economyHelpers';

let app: Express;
let srv: TestServer;
const open: WsClient[] = [];

beforeAll(async () => {
  app = buildApp();
  srv = await startServer(app);
});
beforeEach(async () => {
  resetClock();
  await resetDb();
});
afterEach(async () => {
  for (const c of open.splice(0)) c.close();
  await until(() => registry.size() === 0);
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

const join = async (h: Hero) => {
  const r = await connect(srv.port, h);
  open.push(r.c);
  return r.c;
};

describe('우편 도착 알림은 5단계 chat.sys 한 줄로 간다', () => {
  it('입찰이 밀린 접속자는 반환 안내, 판매자는 취소 우편 안내를 받고 접속하지 않은 사람은 영향이 없다', async () => {
    const seller = await mk(app);
    const b1 = await mk(app);
    const b2 = await mk(app);
    const c1 = await join(b1);
    const cs = await join(seller);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    await bidReq(app, b1, id, 100);
    expect((await bidReq(app, b2, id, 105)).status).toBe(200); // b2는 접속하지 않았다
    const f = await c1.waitT('chat.sys');
    expect(f.text).toBe('[경매] 뼈손잡이 장검 입찰이 밀려 100G가 우편으로 반환되었습니다.');
    expect(typeof f.at).toBe('string');
    expect(f.seq).toBeUndefined();
    await c1.expectNone((x) => x.t === 'chat.sys');

    const id2 = await listIron(app, seller, { buyout: 1000 });
    expect((await cancelReq(app, seller, id2)).status).toBe(200);
    const g = await cs.waitT('chat.sys');
    expect(g.text).toBe('[우편] 새 우편이 도착했습니다.');
  });
});
