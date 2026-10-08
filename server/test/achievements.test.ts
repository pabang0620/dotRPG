// 업적(GET /characters/{uuid}/achievements, POST /characters/{uuid}/title)
// 이 도메인에는 보상 수령이 없다(달성하면 칭호만 달 수 있다): 보상 중복 수령 케이스는 해당 없음.
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { ACHIEVEMENTS } from '../src/domains/achievements/achievementDefs';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { auth, buildApp, resetDb, shutdown, ver } from './helpers';
import { newHero, seedLevel, type Hero } from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
beforeEach(() => getRateLimitStore().clear());
afterAll(shutdown);

const list = (h: Hero, id: string = h.id) => request(app).get(`/characters/${id}/achievements`).set(auth(h.s));
const equip = (h: Hero, body: unknown, id: string = h.id) => request(app).post(`/characters/${id}/title`).set(auth(h.s)).send(body as object);
const doneRows = async (h: Hero) =>
  Number((await getPool().query<{ n: string }>('SELECT count(*) AS n FROM character_achievements WHERE character_id = $1', [h.dbId])).rows[0]!.n);
const titleOf = async (h: Hero) =>
  (await getPool().query<{ t: string | null }>('SELECT title_achievement AS t FROM characters WHERE id = $1', [h.dbId])).rows[0]!.t;

describe('GET /characters/:id/achievements', () => {
  it('새 캐릭터: 전부 목록에 있고 달성 없음, 진행도는 목표 이하', async () => {
    const h = await newHero(app);
    const r = await list(h);
    expect(r.status).toBe(200);
    expect(r.body.success).toBe(true);
    expect(r.body.data.title).toBeNull();
    expect(r.body.data.newly).toEqual([]);
    expect(r.body.data.achievements).toHaveLength(ACHIEVEMENTS.length);
    for (const a of r.body.data.achievements) {
      expect(a.progress).toBeLessThanOrEqual(a.goal);
      expect(a.achieved_at).toBeNull();
    }
    expect(await doneRows(h)).toBe(0);
  });

  it('조건을 채우면 newly에 한 번만 나오고 저장된다(두 번째 호출은 newly 비어 있고 시각 유지)', async () => {
    const h = await newHero(app);
    await seedLevel(h, 20);
    const a = await list(h);
    expect(a.body.data.newly.sort()).toEqual(['level_10', 'level_20']);
    const row = a.body.data.achievements.find((x: { id: string }) => x.id === 'level_20');
    expect(row.achieved_at).not.toBeNull();
    expect(row.progress).toBe(20);
    expect(await doneRows(h)).toBe(2);
    const b = await list(h);
    expect(b.body.data.newly).toEqual([]);
    expect(b.body.data.achievements.find((x: { id: string }) => x.id === 'level_20').achieved_at).toBe(row.achieved_at);
  });

  it('동시 조회: 같은 업적이 두 번 저장되지 않는다', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    const [a, b] = await Promise.all([list(h), list(h)]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect(await doneRows(h)).toBe(1);
  });

  it('입력 오류: uuid 형식이 아니면 400', async () => {
    const h = await newHero(app);
    expect((await list(h, 'not-a-uuid')).status).toBe(400);
  });

  it('IDOR: 남의 캐릭터 uuid는 404이고 아무것도 저장되지 않는다', async () => {
    const owner = await newHero(app);
    await seedLevel(owner, 10);
    const other = await newHero(app);
    const r = await list(other, owner.id);
    expect(r.status).toBe(404);
    expect(r.body.errors.code).toBe('CHARACTER_NOT_FOUND');
    expect(await doneRows(owner)).toBe(0);
  });

  it('없는 캐릭터 uuid는 404', async () => {
    const h = await newHero(app);
    expect((await list(h, randomUUID())).status).toBe(404);
  });

  it('인증 없음 401, X-Client-Version 없음 400, X-Data-Version 없음 400(/characters 공통 검사가 먼저 걸린다)', async () => {
    const h = await newHero(app);
    expect((await request(app).get(`/characters/${h.id}/achievements`).set(ver())).status).toBe(401);
    expect((await request(app).get(`/characters/${h.id}/achievements`).set({ Authorization: `Bearer ${h.s.access}` })).status).toBe(400);
    const noData = await request(app)
      .get(`/characters/${h.id}/achievements`)
      .set({ 'X-Client-Version': '0.2.0', Authorization: `Bearer ${h.s.access}` });
    expect(noData.status).toBe(400);
    expect(noData.body.errors.code).toBe('DATA_VERSION_MISSING');
  });

  // 메모: 라우트의 versionCheck({ data: false })는 routes/index.ts에서 createCharacterRouter(/characters 전체 data:true)가
  // 먼저 지나가므로 실제로는 효과가 없다(동작은 일관되어 문제는 아니고 죽은 옵션).
  it.todo('버그 의심(경미): achievementRoutes의 versionCheck({ data: false })는 characterRoutes의 data:true 때문에 적용되지 않는다');

  it('속도 제한: 계정당 1초에 4회를 넘기면 429', async () => {
    const h = await newHero(app);
    const rs = await Promise.all(Array.from({ length: 8 }, () => list(h)));
    expect(rs.filter((r) => r.status === 429).length).toBeGreaterThan(0);
    expect(rs.some((r) => r.status === 200)).toBe(true);
  });
});

describe('POST /characters/:id/title', () => {
  it('달성한 업적은 장착되고 목록·DB에 반영, null로 해제', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    await list(h);
    const on = await equip(h, { achievement_id: 'level_10' });
    expect(on.status).toBe(200);
    expect(on.body.data.title).toEqual({ id: 'level_10', name: '첫걸음' });
    expect(await titleOf(h)).toBe('level_10');
    expect((await list(h)).body.data.title).toEqual({ id: 'level_10', name: '첫걸음' });
    const off = await equip(h, { achievement_id: null });
    expect(off.status).toBe(200);
    expect(off.body.data.title).toBeNull();
    expect(await titleOf(h)).toBeNull();
  });

  it('달성하지 않은 업적은 409 ACHIEVEMENT_NOT_DONE, 알 수 없는 id는 404 ACHIEVEMENT_UNKNOWN', async () => {
    const h = await newHero(app);
    const a = await equip(h, { achievement_id: 'level_40' });
    expect(a.status).toBe(409);
    expect(a.body.errors.code).toBe('ACHIEVEMENT_NOT_DONE');
    const b = await equip(h, { achievement_id: 'no_such_thing' });
    expect(b.status).toBe(404);
    expect(b.body.errors.code).toBe('ACHIEVEMENT_UNKNOWN');
    expect(await titleOf(h)).toBeNull();
  });

  it('조회 전에는 달성 기록이 없어 장착이 거절된다(판정은 조회 때 저장)', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    expect((await equip(h, { achievement_id: 'level_10' })).status).toBe(409);
  });

  it('남이 달성한 업적을 내 캐릭터에 달 수 없다', async () => {
    const a = await newHero(app);
    await seedLevel(a, 10);
    await list(a);
    const b = await newHero(app);
    expect((await equip(b, { achievement_id: 'level_10' })).status).toBe(409);
  });

  it('입력 오류: 필드 없음, 숫자, 빈 문자열, 긴 문자열, 추가 필드는 400', async () => {
    const h = await newHero(app);
    for (const body of [{}, { achievement_id: 5 }, { achievement_id: '' }, { achievement_id: 'x'.repeat(41) }, { achievement_id: null, extra: 1 }]) {
      getRateLimitStore().clear(); // 업적 경로는 1초 4회 제한이 있다
      expect((await equip(h, body)).status).toBe(400);
    }
    getRateLimitStore().clear();
    expect((await equip(h, { achievement_id: null }, 'bad')).status).toBe(400);
  });

  it('IDOR: 남의 캐릭터 uuid에 칭호를 달 수 없다(404), 남의 칭호는 그대로', async () => {
    const owner = await newHero(app);
    await seedLevel(owner, 10);
    await list(owner);
    await equip(owner, { achievement_id: 'level_10' });
    const other = await newHero(app);
    const r = await equip(other, { achievement_id: null }, owner.id);
    expect(r.status).toBe(404);
    expect(await titleOf(owner)).toBe('level_10');
  });

  it('동시 요청: 장착과 해제를 동시에 보내도 둘 중 하나의 상태로 끝난다', async () => {
    const h = await newHero(app);
    await seedLevel(h, 20);
    await list(h);
    const [a, b] = await Promise.all([equip(h, { achievement_id: 'level_10' }), equip(h, { achievement_id: 'level_20' })]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect(['level_10', 'level_20']).toContain(await titleOf(h));
  });

  it('인증 없음 401', async () => {
    const h = await newHero(app);
    expect((await request(app).post(`/characters/${h.id}/title`).set(ver()).send({ achievement_id: null })).status).toBe(401);
  });
});
