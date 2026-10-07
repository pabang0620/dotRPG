// 9단계 11.2: 마을 표시를 서버 값으로(클라이언트가 보낸 레벨·직업·전직·외형·무기는 무시)
import { getPool } from '../src/db/pool';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { newHero, seedLevel, seedWorn, type Hero } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { connect, startServer, until, type TestServer, type WsClient } from './wsHelpers';

let srv: TestServer;
const open: WsClient[] = [];
beforeAll(async () => {
  srv = await startServer(buildApp());
});
beforeEach(async () => {
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

const pos = (map: string, over: Record<string, unknown> = {}) => ({
  t: 'town.pos', map_id: map, x: 1, y: 1, f: 0, m: false, cls: 1, career: 4, skin: 'skin_unowned', weapon: 'eq_staff_99', level: 999, ...over,
});

async function pairInTown(): Promise<{ a: Hero; b: Hero; ca: WsClient; cb: WsClient }> {
  const app = buildApp();
  const a = await newHero(app);
  const b = await newHero(app);
  const ca = (await connect(srv.port, a)).c;
  const cb = (await connect(srv.port, b)).c;
  open.push(ca, cb);
  cb.send(pos('village', { cls: 0, career: 0, skin: '', weapon: '', level: 1 }));
  await until(() => registry.ofCharacter(b.dbId)?.town !== null);
  return { a, b, ca, cb };
}
const seenFrom = async (c: WsClient, id: string, map = 'village') => c.waitFor((f) => f.t === 'town.pos' && f.id === id && f.map_id === map);

describe('town.pos 서버 값', () => {
  it('클라이언트가 level 999, career 4, cls 1, 미소유 skin을 보내도 이웃이 받는 값은 서버 값', async () => {
    const { a, ca, cb } = await pairInTown();
    ca.send(pos('village'));
    const f = await seenFrom(cb, a.id);
    const weapon = (await getPool().query("SELECT item_key FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = 0", [a.dbId])).rows[0]?.item_key as string;
    expect(f).toMatchObject({ name: expect.any(String), map_id: 'village', cls: 0, career: 0, skin: '', level: 1, weapon });
  });

  it('소유한 외형은 통과, 레벨업·전직은 다음 마을 진입 때 반영, 무기 교체(+강화)는 기본 id로', async () => {
    const { a, ca, cb } = await pairInTown();
    await getPool().query("INSERT INTO account_cosmetics (account_id, item_id, source) VALUES ((SELECT account_id FROM characters WHERE id = $1), 'skin_owned', 'gacha')", [a.dbId]);
    await getPool().query("INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, 1, 0, 'promote')", [a.dbId]);
    await seedLevel(a, 7);
    await seedWorn(a, 0, 'eq_sword_1_c+3');
    // 다른 마을을 거쳐 다시 진입한다
    ca.send(pos('canyon', { skin: 'skin_owned' }));
    ca.send(pos('village', { skin: 'skin_owned' }));
    const f = await seenFrom(cb, a.id);
    expect(f).toMatchObject({ map_id: 'village', skin: 'skin_owned', level: 7, career: 1, cls: 0, weapon: 'eq_sword_1_c' });
  });
});
