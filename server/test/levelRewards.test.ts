// 레벨 달성 보상: 현재 캐릭터 레벨 조건 + 계정당 단계별 한 번 지급.
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { createChar, auth, randomName, buildApp, resetDb, shutdown } from './helpers';
import { newHero, seedLevel, type Hero } from './economyHelpers';

let app: Express;
// resetDb 가 이 표를 비우지 않으므로 직접 지운다(FK가 걸린 행이 계정 삭제보다 먼저 사라져야 한다)
const clean = () => getPool().query('DELETE FROM account_level_rewards');
beforeEach(async () => {
  await clean();
  await resetDb();
  app = buildApp();
});
afterAll(async () => {
  await clean();
  await shutdown();
});

const list = (h: Hero) => request(app).get(`/characters/${h.id}/level-rewards`).set(auth(h.s));
const claim = (h: Hero, level: number, requestId: string = randomUUID()) =>
  request(app).post(`/characters/${h.id}/level-rewards/claim`).set(auth(h.s)).send({ request_id: requestId, level });
const anotherHero = async (h: Hero): Promise<Hero> => {
  const r = await createChar(app, h.s, randomName(), 'mage');
  expect(r.status).toBe(201);
  const id = r.body.data.character.id as string;
  const row = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { s: h.s, id, dbId: Number(row.rows[0]!.id), cls: 'mage' };
};
const wallet = async (h: Hero) =>
  (await getPool().query<{ balance: string; paid_balance: string }>('SELECT balance, paid_balance FROM star_wallets WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)', [h.dbId])).rows[0];
const ledgerRows = async (h: Hero) =>
  Number((await getPool().query<{ n: string }>("SELECT count(*) AS n FROM star_ledger WHERE reason = 'level_reward' AND account_id = (SELECT account_id FROM characters WHERE id = $1)", [h.dbId])).rows[0]!.n);

describe('레벨 달성 보상', () => {
  it('단계 목록: 현재 캐릭터 식별자·레벨과 수령 가능 여부', async () => {
    const h = await newHero(app);
    await seedLevel(h, 15);
    const r = await list(h);
    expect(r.status).toBe(200);
    expect(r.body.data.max_level).toBe(15);
    expect(r.body.data).toMatchObject({ character_id: h.id, character_level: 15 });
    expect(r.body.data.tiers).toEqual([
      { level: 10, stars: 300, claimable: true, claimed: false },
      { level: 15, stars: 300, claimable: true, claimed: false },
      { level: 20, stars: 600, claimable: false, claimed: false },
      { level: 30, stars: 1000, claimable: false, claimed: false },
      { level: 40, stars: 3000, claimable: false, claimed: false },
    ]);
  });

  it('Lv10 수령: 무료 300 지급, 유료분 불변, 원장 level_reward', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    const r = await claim(h, 10);
    expect(r.status).toBe(200);
    expect(r.body.data.level).toBe(10);
    expect(r.body.data).toMatchObject({ character_id: h.id, character_level: 10 });
    expect(r.body.data.stars).toBe(300);
    expect(r.body.data.balance).toBe(300);
    expect(r.body.data.tiers[0]).toEqual({ level: 10, stars: 300, claimable: false, claimed: true });
    expect(await wallet(h)).toEqual({ balance: '300', paid_balance: '0' });
    const l = await getPool().query<{ ref: string; delta: string; paid_delta: string }>("SELECT ref, delta, paid_delta FROM star_ledger WHERE reason = 'level_reward'");
    expect(l.rows).toEqual([{ ref: 'level:10', delta: '300', paid_delta: '0' }]);
  });

  it('같은 단계 두 번째 요청은 409 ALREADY_CLAIMED', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    expect((await claim(h, 10)).status).toBe(200);
    const r = await claim(h, 10);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('ALREADY_CLAIMED');
    expect((await wallet(h))!.balance).toBe('300');
  });

  it('레벨 미달은 409 LEVEL_NOT_REACHED', async () => {
    const h = await newHero(app);
    await seedLevel(h, 9);
    const r = await claim(h, 10);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('LEVEL_NOT_REACHED');
    expect(await ledgerRows(h)).toBe(0);
  });

  it('같은 계정의 다른 적격 캐릭터로도 이미 받은 단계는 다시 받을 수 없다', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    expect((await claim(h, 10)).status).toBe(200);
    const second = await anotherHero(h);
    await seedLevel(second, 10);
    const r = await claim(second, 10);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('ALREADY_CLAIMED');
    await seedLevel(second, 1);
    const secondView = (await list(second)).body.data;
    expect(secondView.character_level).toBe(1);
    expect(secondView.tiers[0]).toEqual({ level: 10, stars: 300, claimable: false, claimed: true });
    expect(await ledgerRows(h)).toBe(1);
  });

  it('같은 request_id 두 번은 한 번만 지급하고 같은 응답', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    const id = randomUUID();
    const a = await claim(h, 10, id);
    const b = await claim(h, 10, id);
    expect(b.status).toBe(200);
    expect(b.body.data).toEqual(a.body.data);
    expect(await ledgerRows(h)).toBe(1);
    expect((await wallet(h))!.balance).toBe('300');
  });

  it('동시 요청 두 개(다른 request_id)도 한 번만 지급', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    const rs = await Promise.all([claim(h, 10), claim(h, 10)]);
    expect(rs.map((r) => r.status).sort()).toEqual([200, 409]);
    expect(await ledgerRows(h)).toBe(1);
    expect((await wallet(h))!.balance).toBe('300');
  });

  it('단계 표에 없는 level은 422 UNKNOWN_TIER, 잘못된 입력은 400', async () => {
    const h = await newHero(app);
    await seedLevel(h, 40);
    const r = await claim(h, 12);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('UNKNOWN_TIER');
    const bad = await request(app).post(`/characters/${h.id}/level-rewards/claim`).set(auth(h.s)).send({ request_id: 'x', level: 10, stars: 99999 });
    expect(bad.status).toBe(400); // 공용 validate 미들웨어는 입력 형식 오류를 400으로 돌려준다
    expect(await ledgerRows(h)).toBe(0);
  });

  it('같은 계정에 Lv40 캐릭터가 있어도 Lv1 캐릭터의 모든 무료 단계는 잠겨 있다', async () => {
    const high = await newHero(app);
    await seedLevel(high, 40);
    const low = await anotherHero(high);
    const before = await list(low);
    expect(before.body.data).toMatchObject({ character_id: low.id, character_level: 1, max_level: 1 });
    expect(before.body.data.tiers.every((t: { claimable: boolean; claimed: boolean }) => !t.claimable && !t.claimed)).toBe(true);
    for (const level of [10, 15, 20, 30, 40]) {
      const r = await claim(low, level);
      expect(r.status).toBe(409);
      expect(r.body.errors.code).toBe('LEVEL_NOT_REACHED');
    }
    expect(await ledgerRows(high)).toBe(0);
    expect((await list(high)).body.data.tiers.every((t: { claimable: boolean }) => t.claimable)).toBe(true);
  });

  it.each([10, 15, 20, 30, 40])('Lv%i 단계는 직전 레벨에서 거절하고 정확히 도달하면 지급한다', async (level) => {
    const h = await newHero(app);
    await seedLevel(h, level - 1);
    expect((await claim(h, level)).body.errors.code).toBe('LEVEL_NOT_REACHED');
    expect(await ledgerRows(h)).toBe(0);
    await seedLevel(h, level);
    expect((await claim(h, level)).status).toBe(200);
    expect(await ledgerRows(h)).toBe(1);
  });

  it('캐릭터 없는 구 경로는 최고 레벨이 충분해도 조회·지급하지 않는다', async () => {
    const h = await newHero(app);
    await seedLevel(h, 40);
    const rs = [
      await request(app).get('/level-rewards').set(auth(h.s)),
      await request(app).post('/level-rewards/claim').set(auth(h.s)).send({ request_id: randomUUID(), level: 40 }),
    ];
    for (const r of rs) {
      expect(r.status).toBe(400);
      expect(r.body.errors.code).toBe('CHARACTER_REQUIRED');
    }
    expect(await ledgerRows(h)).toBe(0);
  });

  it('남의 캐릭터·삭제된 캐릭터·없는 캐릭터는 조회와 수령이 모두 거절된다', async () => {
    const owner = await newHero(app);
    const other = await newHero(app);
    await seedLevel(owner, 40);
    const foreign = { ...owner, s: other.s };
    for (const h of [foreign, { ...other, id: randomUUID() }]) {
      expect((await list(h)).body.errors.code).toBe('CHARACTER_NOT_FOUND');
      expect((await claim(h, 10)).body.errors.code).toBe('CHARACTER_NOT_FOUND');
    }
    await getPool().query('UPDATE characters SET deleted_at = now() WHERE id = $1', [owner.dbId]);
    expect((await list(owner)).body.errors.code).toBe('CHARACTER_NOT_FOUND');
    expect((await claim(owner, 10)).body.errors.code).toBe('CHARACTER_NOT_FOUND');
    expect(await ledgerRows(owner)).toBe(0);
    expect(await ledgerRows(other)).toBe(0);
  });

  it('같은 request_id는 다른 단계나 다른 캐릭터의 지급에 재사용할 수 없다', async () => {
    const first = await newHero(app);
    const second = await anotherHero(first);
    await seedLevel(first, 15);
    await seedLevel(second, 15);
    const id = randomUUID();
    expect((await claim(first, 10, id)).status).toBe(200);
    expect((await claim(first, 15, id)).body.errors.code).toBe('REQUEST_ID_REUSED');
    expect((await claim(second, 10, id)).body.errors.code).toBe('REQUEST_ID_REUSED');
    expect(await ledgerRows(first)).toBe(1);
  });

  it('같은 request_id의 동시 재전송과 두 적격 캐릭터의 경쟁도 한 번만 지급한다', async () => {
    const first = await newHero(app);
    const second = await anotherHero(first);
    await seedLevel(first, 15);
    await seedLevel(second, 15);
    const id = randomUUID();
    const retries = await Promise.all([claim(first, 10, id), claim(first, 10, id)]);
    expect(retries.map((r) => r.status)).toEqual([200, 200]);
    expect(retries[0]!.body.data).toEqual(retries[1]!.body.data);
    const competing = await Promise.all([claim(first, 15), claim(second, 15)]);
    expect(competing.map((r) => r.status).sort()).toEqual([200, 409]);
    expect(await ledgerRows(first)).toBe(2);
    expect((await wallet(first))!.balance).toBe('600');
  });
});
