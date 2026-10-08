// 클라이언트 재시도용: 경매 등록 취소(DELETE)와 파티 fill-ai는 같은 request_id 재전송에 저장된 첫 결과를 돌려준다
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { getQueueStore } from '../src/domains/match/queueStore';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { cancelReq, listIron, mk, resetClock, secondChar } from './auctionHelpers';
import { fakeRng } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { newHero, post } from './partyHelpers';

const app = buildApp();
beforeEach(async () => {
  resetClock();
  await resetDb();
  getQueueStore().clear();
  setRng(fakeRng({ unit: 1 }));
});
const MONDAY = '2026-10-05T03:00:00Z'; // 요일 던전 gold_vein이 열려 있다
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

describe('경매 등록 취소 DELETE 재전송', () => {
  it('같은 request_id 재전송은 첫 결과(already=false, 같은 우편)를 그대로 돌려주고, request_id 없는 재호출은 예전처럼 already=true', async () => {
    const seller = await mk(app, 100_000);
    const id = await listIron(app, seller);
    const r = randomUUID();
    const first = await cancelReq(app, seller, id, r);
    expect(first.status).toBe(200);
    expect(first.body.data.already).toBe(false);
    const again = await cancelReq(app, seller, id, r);
    expect(again.status).toBe(200);
    expect(again.body.data).toEqual(first.body.data);
    expect(first.body.data.mail_id).toBeTruthy();
    expect((await getPool().query('SELECT 1 FROM mails')).rowCount).toBe(1);
    // 새 request_id나 없는 재호출은 상태 기반 멱등
    expect((await cancelReq(app, seller, id, randomUUID())).body.data.already).toBe(true);
    expect((await cancelReq(app, seller, id)).body.data.already).toBe(true);
    // 같은 request_id로 다른 등록을 취소하려 하면 거절
    const id2 = await listIron(app, seller, { key: 'eq_sword_10_c', buyout: 100 });
    expect((await cancelReq(app, seller, id2, r)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    // 남의 등록은 request_id가 있어도 404
    const other = await secondChar(app, await mk(app, 1000));
    expect((await cancelReq(app, other, id2, randomUUID())).status).toBe(404);
  });
});

describe('파티 fill-ai 재전송', () => {
  it('같은 request_id 재전송은 저장된 같은 결과(같은 파티, 201)를 돌려주고 실패 응답은 저장하지 않는다', async () => {
    setClockOverride(() => new Date(MONDAY));
    const a = await newHero(app);
    const none = await newHero(app);
    const fail = await post(app, none, '/match/fill-ai', {}, randomUUID());
    expect(fail.body.errors.code).toBe('NOT_QUEUED');
    const rid = randomUUID();
    expect((await post(app, a, '/match/queue', { dungeon_id: 'gold_vein', difficulty: 0 })).status).toBe(200);
    const f1 = await post(app, a, '/match/fill-ai', {}, rid);
    expect(f1.status).toBe(201);
    const f2 = await post(app, a, '/match/fill-ai', {}, rid);
    expect(f2.status).toBe(201);
    expect(f2.body.data).toEqual(f1.body.data);
    const stored = await getPool().query("SELECT count(*) AS n FROM request_log WHERE request_id = $1", [rid]);
    expect(stored.rows[0]).toEqual({ n: '1' });
  });
});
