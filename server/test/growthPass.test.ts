// 14단계: 성장 패스(구매·수령, GET /level-rewards 확장)
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import * as pass from '../src/domains/levelrewards/growthPassService';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { auth, createChar, randomName, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, post, seedLevel, type Hero } from './economyHelpers';
import { expectWalletConsistent, ledgerCount, payApp, purchase, resetPay, walletState } from './payHelpers';
import { newPlayer, type Player } from './sealedBoxHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterAll(shutdown);

const buy = (pl: Player, rid: string = randomUUID()) => request(app).post('/level-rewards/pass/buy').set(auth({ access: pl.p.access } as never)).send({ request_id: rid });
const claim = (pl: Player, level: number, rid?: string, hero: Hero = pl.hero) => post(app, hero, '/level-rewards/pass/claim', { level }, rid);
const list = (pl: Player) => request(app).get('/level-rewards').set(auth({ access: pl.p.access } as never));

describe('성장 패스 구매', () => {
  it('9000 차감(무료분 먼저, 유료는 오래된 로트부터), 보유 표시, 기존 필드 유지', async () => {
    const pl = await newPlayer(app, 8500);
    await purchase(app, pl.p, 'stars_1000');
    const before = await walletState(pl.p.accountId);
    expect(before).toMatchObject({ balance: 9500, paid: 1000 });
    const r = await buy(pl);
    expect(r.status).toBe(200);
    expect(r.body.data).toEqual({ pass_owned: true, pass_price: 9000, balance: 500 });
    const l = await getPool().query("SELECT delta, paid_delta, ref FROM star_ledger WHERE reason = 'pass_buy' AND account_id = $1", [pl.p.accountId]);
    expect(l.rows).toEqual([{ delta: '-9000', paid_delta: '-500', ref: 'growth_pass' }]);
    expect(await walletState(pl.p.accountId)).toMatchObject({ balance: 500, paid: 500, lots: 500 });
    await expectWalletConsistent(pl.p.accountId);
    const g = await list(pl);
    expect(g.body.data.pass_owned).toBe(true);
    expect(g.body.data.pass_price).toBe(9000);
    expect(g.body.data.pass_tiers).toHaveLength(8);
    expect(g.body.data.tiers).toHaveLength(5);
    expect(g.body.data.max_level).toBe(1);
    expect(g.body.data.pass_tiers[1]).toEqual({
      level: 10,
      rewards: [{ item_key: 'ticket_enh10', count: 1 }, { item_key: 'box_sealed', count: 5 }],
      claimable: false,
      claimed: false,
    });
  });

  it('두 번째 구매는 409 ALREADY_OWNED, 같은 request_id 재전송은 같은 결과(차감 1번)', async () => {
    const pl = await newPlayer(app, 20000);
    const rid = randomUUID();
    const a = await buy(pl, rid);
    const b = await buy(pl, rid);
    expect(b.status).toBe(200);
    expect(b.body.data).toEqual(a.body.data);
    const c = await buy(pl);
    expect(c.status).toBe(409);
    expect(c.body.errors.code).toBe('ALREADY_OWNED');
    expect(await ledgerCount(pl.p.accountId, 'pass_buy')).toBe(1);
    expect((await walletState(pl.p.accountId)).balance).toBe(11000);
  });

  it('별조각 부족은 NOT_ENOUGH_STARS, 입력 오류(가격 필드)는 422', async () => {
    const pl = await newPlayer(app, 8999);
    const r = await buy(pl);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('NOT_ENOUGH_STARS');
    expect((await list(pl)).body.data.pass_owned).toBe(false);
    const bad = await request(app).post('/level-rewards/pass/buy').set(auth({ access: pl.p.access } as never)).send({ request_id: randomUUID(), price: 1 });
    expect(bad.status).toBe(400);
    expect((await walletState(pl.p.accountId)).balance).toBe(8999);
  });

  it('동시 구매: 서로 다른 request_id 2개 -> 1개만 성공, 한 번만 차감', async () => {
    const pl = await newPlayer(app, 30000);
    const rs = await Promise.allSettled([pass.buy(pl.p.accountId, { request_id: randomUUID() }), pass.buy(pl.p.accountId, { request_id: randomUUID() })]);
    expect(rs.filter((r) => r.status === 'fulfilled')).toHaveLength(1);
    expect((rs.find((r) => r.status === 'rejected') as PromiseRejectedResult).reason).toMatchObject({ code: 'ALREADY_OWNED' });
    expect((await walletState(pl.p.accountId)).balance).toBe(21000);
    expect(await ledgerCount(pl.p.accountId, 'pass_buy')).toBe(1);
    await expectWalletConsistent(pl.p.accountId);
  });
});

describe('성장 패스 수령', () => {
  it('패스가 없으면 409 PASS_REQUIRED', async () => {
    const pl = await newPlayer(app);
    await seedLevel(pl.hero, 5);
    const r = await claim(pl, 5);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('PASS_REQUIRED');
  });

  it('레벨 미달 409 LEVEL_NOT_REACHED, 없는 단계 422 UNKNOWN_TIER', async () => {
    const pl = await newPlayer(app, 9000);
    await buy(pl);
    await seedLevel(pl.hero, 9);
    const r = await claim(pl, 10);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('LEVEL_NOT_REACHED');
    expect((await claim(pl, 7)).body.errors.code).toBe('UNKNOWN_TIER');
    expect(await countOf(pl.hero, 'ticket_enh10')).toBe(0);
  });

  it('Lv10 수령: 강화권 1장 + 봉인된 상자 5개가 이 캐릭터 가방으로, 표에 claimed', async () => {
    const pl = await newPlayer(app, 9000);
    await buy(pl);
    await seedLevel(pl.hero, 10);
    expect((await list(pl)).body.data.pass_tiers.filter((t: { claimable: boolean }) => t.claimable).map((t: { level: number }) => t.level)).toEqual([5, 10]);
    const r = await claim(pl, 10);
    expect(r.status).toBe(200);
    expect(r.body.data.level).toBe(10);
    expect(r.body.data.rewards).toEqual([{ item_key: 'ticket_enh10', count: 1 }, { item_key: 'box_sealed', count: 5 }]);
    expect(await countOf(pl.hero, 'ticket_enh10')).toBe(1);
    expect(await countOf(pl.hero, 'box_sealed')).toBe(5);
    expect(r.body.data.pass_tiers.find((t: { level: number }) => t.level === 10)).toMatchObject({ claimable: false, claimed: true });
    await expectLedgerConsistent(pl.hero);
  });

  it('소급 수령: Lv40이 된 뒤 패스를 사도 지난 단계를 모두 받을 수 있다', async () => {
    const pl = await newPlayer(app, 9000);
    await seedLevel(pl.hero, 40);
    await buy(pl);
    for (const level of [5, 10, 15, 20, 25, 30, 35, 40]) expect((await claim(pl, level)).status).toBe(200);
    expect(await countOf(pl.hero, 'box_sealed')).toBe(5 + 5 + 10 + 10 + 20);
    expect(await countOf(pl.hero, 'ticket_protect')).toBe(10);
    expect(await countOf(pl.hero, 'ticket_enh10')).toBe(3);
    expect(await countOf(pl.hero, 'ticket_enh12')).toBe(1);
    expect(await countOf(pl.hero, 'box_enh12_50')).toBe(1);
    expect((await list(pl)).body.data.pass_tiers.every((t: { claimed: boolean }) => t.claimed)).toBe(true);
  });

  it('계정당 단계별 1회: 같은 단계 재요청 409 ALREADY_CLAIMED, 다른 캐릭터로도 못 받는다, 같은 request_id는 같은 결과', async () => {
    const pl = await newPlayer(app, 9000);
    await buy(pl);
    await seedLevel(pl.hero, 5);
    const rid = randomUUID();
    const a = await claim(pl, 5, rid);
    const b = await claim(pl, 5, rid);
    expect(b.body.data).toEqual(a.body.data);
    expect(await countOf(pl.hero, 'box_sealed')).toBe(5);
    const again = await claim(pl, 5);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('ALREADY_CLAIMED');
    // 같은 계정의 두 번째 캐릭터
    const res = await createChar(app, { access: pl.p.access } as never, randomName());
    const id = res.body.data.character.id as string;
    const row = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
    const second: Hero = { s: { access: pl.p.access } as never, id, dbId: Number(row.rows[0]!.id), cls: 'warrior' };
    const other = await claim(pl, 5, undefined, second);
    expect(other.body.errors.code).toBe('ALREADY_CLAIMED');
    expect(await countOf(second, 'box_sealed')).toBe(0);
  });

  it('동시 수령: 두 캐릭터가 같은 단계를 동시에 -> 한 번만 지급', async () => {
    const pl = await newPlayer(app, 9000);
    await buy(pl);
    await seedLevel(pl.hero, 5);
    const res = await createChar(app, { access: pl.p.access } as never, randomName());
    const id2 = res.body.data.character.id as string;
    const rs = await Promise.allSettled([
      pass.claim(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), level: 5 }),
      pass.claim(pl.p.accountId, id2, { request_id: randomUUID(), level: 5 }),
    ]);
    expect(rs.filter((r) => r.status === 'fulfilled')).toHaveLength(1);
    expect((rs.find((r) => r.status === 'rejected') as PromiseRejectedResult).reason).toMatchObject({ code: 'ALREADY_CLAIMED' });
    const n = await getPool().query<{ n: string }>('SELECT count(*) AS n FROM account_pass_claims WHERE account_id = $1', [pl.p.accountId]);
    expect(n.rows[0]!.n).toBe('1');
  });
});
