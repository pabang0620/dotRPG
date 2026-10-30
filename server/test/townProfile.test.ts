// 9단계 11.2: 마을 표시를 서버 값으로(클라이언트가 보낸 레벨·직업·전직·외형·무기는 무시)
import { getPool } from '../src/db/pool';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { newHero, seedLevel, seedWorn, type Hero } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { connect, startServer, until, type TestServer, type WsClient } from './wsHelpers';

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

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

  it('오라는 소유한 것과 무료 오라만 중계하고 나머지는 빈 문자열', async () => {
    const { a, ca, cb } = await pairInTown();
    await getPool().query("INSERT INTO account_cosmetics (account_id, item_id, source) VALUES ((SELECT account_id FROM characters WHERE id = $1), 'aura_owned', 'gacha')", [a.dbId]);
    const seenAura = (aura: string) => cb.waitFor((f) => f.t === 'town.pos' && f.id === a.id && f.aura === aura);
    ca.send(pos('village', { aura: 'aura_unowned' }));
    expect(await seenAura('')).toBeTruthy();
    await sleep(200); // 같은 마을 위치 프레임 최소 간격(150ms)
    ca.send(pos('village', { aura: 'aura_owned', x: 2 }));
    expect(await seenAura('aura_owned')).toBeTruthy();
    await sleep(200);
    ca.send(pos('village', { aura: 'aura_sky', x: 3 }));
    expect(await seenAura('aura_sky')).toBeTruthy();
  });

  it('장착한 칭호 id와 그 랭킹 칭호의 이름 색을 싣고, 칭호가 없으면 둘 다 없다(2026-10-30)', async () => {
    const { a, b, ca, cb } = await pairInTown();
    const season = await getPool().query<{ id: string }>(
      "INSERT INTO boss_seasons (kind, week_start, week_end, boss_id, max_hp, hp_left) VALUES ('world', '2026-01-01T00:00:00Z', '2026-01-15T00:00:00Z', 'boss_abyss_titan', 1, 1) RETURNING id");
    await getPool().query("INSERT INTO rank_titles (character_id, season_id, title_id, name_color, expires_at) VALUES ($1, $2, 'rank_world_1', 'gold', NULL)", [a.dbId, season.rows[0]?.id]);
    await getPool().query("UPDATE characters SET title_achievement = 'rank_world_1' WHERE id = $1", [a.dbId]);
    ca.send(pos('village'));
    expect(await seenFrom(cb, a.id)).toMatchObject({ title_id: 'rank_world_1', title: '세계를 꺾은 자', name_color: 'gold' });
    cb.send(pos('canyon'));
    cb.send(pos('village', { x: 2 }));
    const fb = await seenFrom(ca, b.id);
    expect(fb.title_id).toBeUndefined();
    expect(fb.name_color).toBeUndefined();
  });
});
