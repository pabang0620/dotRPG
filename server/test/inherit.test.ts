// 챕터 3 장비 계승(POST /characters/{uuid}/promote/inherit): 등급별 비용, 강화·보호권 유지, 결과 키, 거부, 멱등, 잠금
import { getPool } from '../src/db/pool';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, goldOf, newHero, post, seedGold, seedItem, seedWorn, wornOf, type Hero } from './economyHelpers';
import type { Express } from 'express';

let app: Express = buildApp({ CHAPTER3_OPEN: 'true' });
beforeEach(async () => {
  await resetDb();
  app = buildApp({ CHAPTER3_OPEN: 'true' });
});
afterAll(async () => {
  buildApp();
  await shutdown();
});

// enhance.json promote.inherit: 유니크 핵 3·결정 5·인장 10·150,000 / 레전더리 핵 6·결정 10·인장 20·400,000
const inherit = (h: Hero, body: Record<string, unknown>, rid?: string) => post(app, h, '/promote/inherit', body, rid);
const bag = (key: string) => ({ target: { bag_key: key } });

async function hero(gold = 500_000, cores = 10, shards = 20, seals = 30): Promise<Hero> {
  const h = await newHero(app);
  await seedGold(h, gold - (await goldOf(h)));
  if (cores > 0) await seedItem(h, 'mat_core', cores);
  if (shards > 0) await seedItem(h, 'mat_raid_xibak', shards);
  if (seals > 0) await seedItem(h, 'mat_raid_kamatz', seals);
  return h;
}

async function pityOf(h: Hero, key: string): Promise<number> {
  const r = await getPool().query<{ pity: number }>('SELECT pity FROM character_enhance_pity WHERE character_id = $1 AND item_key = $2', [h.dbId, key]);
  return r.rows[0]?.pity ?? 0;
}

describe('POST /characters/:id/promote/inherit', () => {
  it('유니크: Lv.40 -> Lv.50, 강화 수치 유지, 핵·결정·인장·골드가 빠지고 원장이 맞는다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_40_un+12', 1);
    const res = await inherit(h, bag('eq_sword_40_un+12'));
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({
      old_key: 'eq_sword_40_un+12',
      new_key: 'eq_sword_50_un+12',
      cost: { cores: 3, shards: 5, seals: 10, gold: 150_000 },
    });
    expect(await countOf(h, 'eq_sword_40_un+12')).toBe(0);
    expect(await countOf(h, 'eq_sword_50_un+12')).toBe(1);
    expect(await countOf(h, 'mat_core')).toBe(7);
    expect(await countOf(h, 'mat_raid_xibak')).toBe(15);
    expect(await countOf(h, 'mat_raid_kamatz')).toBe(20);
    expect(await goldOf(h)).toBe(350_000);
    const g = await getPool().query("SELECT delta FROM gold_ledger WHERE character_id = $1 AND reason = 'inherit'", [h.dbId]);
    expect(g.rows.map((r) => Number(r.delta))).toEqual([-150_000]);
    await expectLedgerConsistent(h);
  });

  it('레전더리(착용 중): 같은 칸에서 바뀌고 비용은 레전더리 행', async () => {
    const h = await hero();
    await seedWorn(h, 0, 'eq_sword_40_l+15');
    const res = await inherit(h, { target: { worn_slot: 0 } });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ new_key: 'eq_sword_50_l+15', cost: { cores: 6, shards: 10, seals: 20, gold: 400_000 } });
    expect(await wornOf(h, 0)).toBe('eq_sword_50_l+15');
    expect(await goldOf(h)).toBe(100_000);
    await expectLedgerConsistent(h);
  });

  it('보호권 이력(강화 실패 누적)이 새 키로 옮겨지고 옛 키 이력은 지워진다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_40_un+14', 1);
    await getPool().query('INSERT INTO character_enhance_pity (character_id, item_key, pity, updated_at) VALUES ($1, $2, 7, now())', [h.dbId, 'eq_sword_40_un+14']);
    const res = await inherit(h, bag('eq_sword_40_un+14'));
    expect(res.status).toBe(200);
    expect(res.body.data.pity).toBe(7);
    expect(await pityOf(h, 'eq_sword_50_un+14')).toBe(7);
    expect(await pityOf(h, 'eq_sword_40_un+14')).toBe(0);
  });

  it('커먼~에픽과 다른 단계는 거절(422 INHERIT_INVALID_TARGET)', async () => {
    const h = await hero();
    for (const key of ['eq_sword_40_e+5', 'eq_sword_40_c', 'eq_sword_35_un+3']) {
      await seedItem(h, key, 1);
      const res = await inherit(h, bag(key));
      expect(res.status).toBe(422);
      expect(res.body.errors.code).toBe('INHERIT_INVALID_TARGET');
      expect(await countOf(h, key)).toBe(1);
    }
  });

  it('재료 부족: 422 INHERIT_NOT_ENOUGH, 아무것도 변하지 않는다', async () => {
    const h = await hero(500_000, 10, 20, 5); // 인장 10개 필요
    await seedItem(h, 'eq_sword_40_un', 1);
    const res = await inherit(h, bag('eq_sword_40_un'));
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('INHERIT_NOT_ENOUGH');
    expect(res.body.errors.need).toEqual({ cores: 3, shards: 5, seals: 10, gold: 150_000 });
    expect(await countOf(h, 'eq_sword_40_un')).toBe(1);
    expect(await goldOf(h)).toBe(500_000);
  });

  it('재전송(같은 request_id)은 한 번만 계승하고 같은 응답을 돌려준다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_40_un+3', 2);
    const rid = '0a1b2c3d-0000-4000-8000-00000000c3c3';
    const a = await inherit(h, bag('eq_sword_40_un+3'), rid);
    const b = await inherit(h, bag('eq_sword_40_un+3'), rid);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(b.body.data.new_key).toBe(a.body.data.new_key);
    expect(await countOf(h, 'eq_sword_40_un+3')).toBe(1);
    expect(await countOf(h, 'eq_sword_50_un+3')).toBe(1);
    expect(await goldOf(h)).toBe(350_000);
  });

  it('3장이 닫혀 있으면 422 CHAPTER_LOCKED', async () => {
    app = buildApp();
    const h = await hero();
    await seedItem(h, 'eq_sword_40_un', 1);
    const res = await inherit(h, bag('eq_sword_40_un'));
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('CHAPTER_LOCKED');
    expect(await countOf(h, 'eq_sword_40_un')).toBe(1);
  });
});
