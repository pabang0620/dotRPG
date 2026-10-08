// 장비 승급(POST /characters/{uuid}/promote): 정상, 재료 부족, 재전송, 동시 요청, 입력 오류, 남의 캐릭터
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { auth, buildApp, resetDb, shutdown, ver } from './helpers';
import {
  countOf,
  expectLedgerConsistent,
  goldOf,
  newHero,
  post,
  seedGold,
  seedItem,
  seedWorn,
  wornOf,
  type Hero,
} from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

// enhance.json promote: eq_sword_1_c -> _u (핵 1, 골드 2000), eq_sword_1_u -> _r (핵 2, 골드 5000)
const promote = (h: Hero, body: Record<string, unknown>, rid?: string) => post(app, h, '/promote', body, rid);
const bag = (key: string) => ({ target: { bag_key: key } });

async function hero(gold = 10_000, cores = 5): Promise<Hero> {
  const h = await newHero(app);
  // 새 캐릭터는 골드를 가지고 시작한다: 목표 잔액에 맞춘다
  await seedGold(h, gold - (await goldOf(h)));
  if (cores > 0) await seedItem(h, 'mat_core', cores);
  return h;
}

describe('POST /characters/:id/promote', () => {
  it('정상: 가방 장비가 다음 등급으로 바뀌고 강화 수치는 유지, 핵과 골드가 빠지고 원장이 맞는다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_1_c+3', 1);
    const res = await promote(h, bag('eq_sword_1_c+3'));
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ old_key: 'eq_sword_1_c+3', new_key: 'eq_sword_1_u+3', cost: { cores: 1, gold: 2000 } });
    expect(await countOf(h, 'eq_sword_1_c+3')).toBe(0);
    expect(await countOf(h, 'eq_sword_1_u+3')).toBe(1);
    expect(await countOf(h, 'mat_core')).toBe(4);
    expect(await goldOf(h)).toBe(8000);
    const g = await getPool().query("SELECT delta FROM gold_ledger WHERE character_id = $1 AND reason = 'promote_cost'", [h.dbId]);
    expect(g.rows.map((r) => Number(r.delta))).toEqual([-2000]);
    await expectLedgerConsistent(h);
  });

  it('정상: 착용 중인 장비도 같은 칸에서 바뀐다', async () => {
    const h = await hero();
    await seedWorn(h, 0, 'eq_sword_1_u+1');
    const res = await promote(h, { target: { worn_slot: 0 } });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ old_key: 'eq_sword_1_u+1', new_key: 'eq_sword_1_r+1', cost: { cores: 2, gold: 5000 } });
    expect(await wornOf(h, 0)).toBe('eq_sword_1_r+1');
    expect(await countOf(h, 'mat_core')).toBe(3);
    await expectLedgerConsistent(h);
  });

  it('재료 부족: 핵이 모자라면 422 PROMOTE_NOT_ENOUGH, 아무것도 변하지 않는다', async () => {
    const h = await hero(10_000, 1);
    await seedItem(h, 'eq_sword_1_u', 1); // 핵 2개 필요
    const res = await promote(h, bag('eq_sword_1_u'));
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('PROMOTE_NOT_ENOUGH');
    expect(res.body.errors.need).toEqual({ cores: 2, gold: 5000 });
    expect(res.body.errors.have).toEqual({ cores: 1, gold: 10_000 });
    expect(await countOf(h, 'eq_sword_1_u')).toBe(1);
    expect(await countOf(h, 'mat_core')).toBe(1);
    expect(await goldOf(h)).toBe(10_000);
  });

  it('골드 부족: 422 PROMOTE_NOT_ENOUGH', async () => {
    const h = await hero(1999, 5);
    await seedItem(h, 'eq_sword_1_c', 1);
    const res = await promote(h, bag('eq_sword_1_c'));
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('PROMOTE_NOT_ENOUGH');
    expect(await goldOf(h)).toBe(1999);
    expect(await countOf(h, 'eq_sword_1_c')).toBe(1);
  });

  it('승급 대상이 아닌 장비·없는 장비·빈 칸은 422 PROMOTE_INVALID_TARGET', async () => {
    const h = await hero();
    await seedItem(h, 'mat_bone', 3);
    expect((await promote(h, bag('mat_bone'))).body.errors.code).toBe('PROMOTE_INVALID_TARGET'); // 표에 없음
    expect((await promote(h, bag('eq_sword_1_c'))).body.errors.code).toBe('PROMOTE_INVALID_TARGET'); // 가방에 없음
    expect((await promote(h, { target: { worn_slot: 3 } })).body.errors.code).toBe('PROMOTE_INVALID_TARGET'); // 빈 칸
    expect(await countOf(h, 'mat_core')).toBe(5);
  });

  it('같은 request_id를 두 번 보내면 한 번만 처리하고 같은 응답을 돌려준다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_1_c', 1);
    const rid = randomUUID();
    const a = await promote(h, bag('eq_sword_1_c'), rid);
    const b = await promote(h, bag('eq_sword_1_c'), rid);
    expect(a.status).toBe(200);
    expect(b.body).toEqual(a.body);
    expect(await countOf(h, 'eq_sword_1_u')).toBe(1);
    expect(await countOf(h, 'mat_core')).toBe(4);
    expect(await goldOf(h)).toBe(8000);
    await expectLedgerConsistent(h);
  });

  it('같은 request_id에 다른 내용을 보내면 거절된다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_1_c', 1);
    await seedItem(h, 'eq_sword_1_u', 1);
    const rid = randomUUID();
    expect((await promote(h, bag('eq_sword_1_c'), rid)).status).toBe(200);
    const other = await promote(h, bag('eq_sword_1_u'), rid);
    expect(other.status).toBeGreaterThanOrEqual(400);
    expect(await countOf(h, 'eq_sword_1_u')).toBe(2); // 원래 1 + 승급 결과 1, 다른 내용은 처리되지 않음
  });

  it('동시 요청: 같은 장비 한 개를 동시에 승급해도 한 번만 처리된다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_1_c', 1);
    const [a, b] = await Promise.all([promote(h, bag('eq_sword_1_c')), promote(h, bag('eq_sword_1_c'))]);
    expect([a.status, b.status].sort()).toEqual([200, 422]);
    expect(await countOf(h, 'eq_sword_1_u')).toBe(1);
    expect(await countOf(h, 'mat_core')).toBe(4);
    expect(await goldOf(h)).toBe(8000);
    await expectLedgerConsistent(h);
  });

  it('동시 요청: 같은 request_id 두 개는 한 번만 처리된다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_1_c', 2);
    const rid = randomUUID();
    const [a, b] = await Promise.all([promote(h, bag('eq_sword_1_c'), rid), promote(h, bag('eq_sword_1_c'), rid)]);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(await countOf(h, 'eq_sword_1_u')).toBe(1);
    expect(await countOf(h, 'eq_sword_1_c')).toBe(1);
    expect(await goldOf(h)).toBe(8000);
    await expectLedgerConsistent(h);
  });

  it('입력 오류: request_id 없음, 결과 키·비용을 보냄, target 둘 다/없음, 범위 밖 슬롯은 400', async () => {
    const h = await hero();
    const url = `/characters/${h.id}/promote`;
    const send = (body: Record<string, unknown>) => request(app).post(url).set(auth(h.s)).send(body);
    const rid = randomUUID();
    expect((await send({ target: { bag_key: 'eq_sword_1_c' } })).status).toBe(400);
    expect((await send({ request_id: rid, target: { bag_key: 'eq_sword_1_c' }, to: 'eq_sword_1_l' })).status).toBe(400);
    expect((await send({ request_id: rid, target: { bag_key: 'eq_sword_1_c' }, cost: 0 })).status).toBe(400);
    expect((await send({ request_id: rid, target: { bag_key: 'eq_sword_1_c', worn_slot: 0 } })).status).toBe(400);
    expect((await send({ request_id: rid })).status).toBe(400);
    expect((await send({ request_id: rid, target: { worn_slot: 6 } })).status).toBe(400);
    expect((await send({ request_id: 'nope', target: { worn_slot: 0 } })).status).toBe(400);
    expect(await goldOf(h)).toBe(10_000);
  });

  it('남의 캐릭터 uuid로는 404, 내 재료는 그대로', async () => {
    const owner = await hero();
    await seedItem(owner, 'eq_sword_1_c', 1);
    const thief = await newHero(app);
    const res = await request(app)
      .post(`/characters/${owner.id}/promote`)
      .set(auth(thief.s))
      .send({ request_id: randomUUID(), target: { bag_key: 'eq_sword_1_c' } });
    expect(res.status).toBe(404);
    expect(await countOf(owner, 'eq_sword_1_c')).toBe(1);
    expect(await countOf(owner, 'mat_core')).toBe(5);
  });

  it('인증 없이는 401', async () => {
    const h = await newHero(app);
    const res = await request(app).post(`/characters/${h.id}/promote`).set(ver()).send({});
    expect(res.status).toBe(401);
  });
});
