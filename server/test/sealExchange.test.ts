// 챕터 3 아카브 교환(POST /characters/{uuid}/shop/exchange): 침식 결정 5 + 20,000 -> 잿빛 인장 1, 계정당 주 2회, 잠금
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, goldOf, newHero, post, seedGold, seedItem, type Hero } from './economyHelpers';
import { setClockOverride } from '../src/utils/clock';
import type { Express } from 'express';

let app: Express;
beforeEach(async () => {
  await resetDb();
  setClockOverride(() => new Date('2026-10-23T03:00:00Z'));
  app = buildApp({ CHAPTER3_OPEN: 'true' });
});
afterAll(async () => {
  setClockOverride(null);
  buildApp();
  await shutdown();
});

const exchange = (h: Hero, rid?: string) => post(app, h, '/shop/exchange', { exchange_id: 'akab_seal' }, rid);

async function hero(gold = 100_000, shards = 30): Promise<Hero> {
  const h = await newHero(app);
  await seedGold(h, gold - (await goldOf(h)));
  if (shards > 0) await seedItem(h, 'mat_raid_xibak', shards);
  return h;
}

describe('POST /characters/:id/shop/exchange', () => {
  it('정상: 결정 5와 20,000골드가 빠지고 인장 1이 들어온다', async () => {
    const h = await hero();
    const res = await exchange(h);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ exchange_id: 'akab_seal', used_this_week: 1, weekly_limit: 2 });
    expect(await countOf(h, 'mat_raid_xibak')).toBe(25);
    expect(await countOf(h, 'mat_raid_kamatz')).toBe(1);
    expect(await goldOf(h)).toBe(80_000);
    await expectLedgerConsistent(h);
  });

  it('계정당 주 2회: 같은 계정의 세 번째는 422 EXCHANGE_WEEKLY_LIMIT, 아무것도 변하지 않는다', async () => {
    const h = await hero();
    expect((await exchange(h)).status).toBe(200);
    expect((await exchange(h)).status).toBe(200);
    const third = await exchange(h);
    expect(third.status).toBe(422);
    expect(third.body.errors.code).toBe('EXCHANGE_WEEKLY_LIMIT');
    expect(await countOf(h, 'mat_raid_kamatz')).toBe(2);
    expect(await goldOf(h)).toBe(60_000);
  });

  it('다음 주(목요일 06:00 KST 뒤)에는 다시 2회', async () => {
    const h = await hero();
    await exchange(h);
    await exchange(h);
    setClockOverride(() => new Date('2026-10-29T22:00:00Z')); // 다음 주 금요일
    expect((await exchange(h)).status).toBe(200);
  });

  it('재료 부족: 422 EXCHANGE_NOT_ENOUGH, 횟수도 쓰지 않는다', async () => {
    const h = await hero(100_000, 4);
    const res = await exchange(h);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('EXCHANGE_NOT_ENOUGH');
    await seedItem(h, 'mat_raid_xibak', 10);
    expect((await exchange(h)).status).toBe(200);
    expect((await exchange(h)).status).toBe(200);
  });

  it('3장이 닫혀 있으면 422 CHAPTER_LOCKED', async () => {
    app = buildApp();
    const h = await hero();
    const res = await exchange(h);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('CHAPTER_LOCKED');
  });
});
