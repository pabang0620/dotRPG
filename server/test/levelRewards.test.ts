// 13단계: 레벨 달성 보상(GET /level-rewards, POST /level-rewards/claim)
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

const list = (h: Hero) => request(app).get('/level-rewards').set(auth(h.s));
const claim = (h: Hero, level: number, requestId: string = randomUUID()) =>
  request(app).post('/level-rewards/claim').set(auth(h.s)).send({ request_id: requestId, level });
const wallet = async (h: Hero) =>
  (await getPool().query<{ balance: string; paid_balance: string }>('SELECT balance, paid_balance FROM star_wallets WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)', [h.dbId])).rows[0];
const ledgerRows = async (h: Hero) =>
  Number((await getPool().query<{ n: string }>("SELECT count(*) AS n FROM star_ledger WHERE reason = 'level_reward' AND account_id = (SELECT account_id FROM characters WHERE id = $1)", [h.dbId])).rows[0]!.n);

describe('레벨 달성 보상', () => {
  it('단계 목록: 최고 레벨과 수령 가능 여부', async () => {
    const h = await newHero(app);
    await seedLevel(h, 15);
    const r = await list(h);
    expect(r.status).toBe(200);
    expect(r.body.data.max_level).toBe(15);
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

  it('같은 계정의 두 번째 캐릭터로는 다시 받을 수 없다(계정 최고 레벨 기준)', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    expect((await claim(h, 10)).status).toBe(200);
    const c2 = await createChar(app, h.s, randomName(), 'mage');
    expect(c2.status).toBe(201);
    const r = await claim(h, 10);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('ALREADY_CLAIMED');
    // 새 캐릭터(Lv1)가 있어도 최고 레벨은 유지된다
    expect((await list(h)).body.data.max_level).toBe(10);
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
    const bad = await request(app).post('/level-rewards/claim').set(auth(h.s)).send({ request_id: 'x', level: 10, stars: 99999 });
    expect(bad.status).toBe(400); // 공용 validate 미들웨어는 입력 형식 오류를 400으로 돌려준다
    expect(await ledgerRows(h)).toBe(0);
  });
});
