// 2026-10-24 스킨 등급 개편(Docs/PLAN_SKIN_TIERS_2026-10-23.md): 전설·히든 칸 확률, 빈 칸 몫 이전, 두 게이지, 합성·분해·교환, 컬렉션
import { getConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { buildRatesMarkdown } from '../src/domains/policies/ratesDisclosure';
import { slotTable } from '../src/domains/starshop/starshopService';
import {
  BANNER_ROWS,
  BANNER_WEIGHT,
  CEILING,
  COLLECTION_BY_ID,
  RATE_SCALE,
  SKIN_POOL_SHAPE,
  STAR_COSMETIC_BY_ID,
  damageOf,
  exchangePriceOf,
  skinsOf,
  type Rarity,
} from '../src/domains/starshop/starshopDefs';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { setRng } from '../src/utils/rng';
import { fakeRng, get, post } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { heroOf, newPayer, payApp, resetPay, seedStars } from './payHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterAll(async () => {
  setRng(null);
  await shutdown();
});

const go = (h: Parameters<typeof post>[1], path: string, body: Record<string, unknown>) => {
  getRateLimitStore().clear();
  return post(app, h, path, body);
};
const give = (accountId: number, itemId: string, copies = 0) =>
  getPool().query(
    `INSERT INTO account_cosmetics (account_id, item_id, source, copies) VALUES ($1, $2, 'grant', $3)
     ON CONFLICT (account_id, item_id) DO UPDATE SET copies = account_cosmetics.copies + EXCLUDED.copies`,
    [accountId, itemId, copies],
  );
const setGauges = (accountId: number, skin: number, hidden: number) =>
  getPool().query(
    `INSERT INTO star_wallets (account_id, skin_pity, hidden_pity) VALUES ($1, $2, $3)
     ON CONFLICT (account_id) DO UPDATE SET skin_pity = $2, hidden_pity = $3`,
    [accountId, skin, hidden],
  );
const walletOf = async (accountId: number) =>
  (await getPool().query('SELECT skin_pity, hidden_pity, synth_fail_legend FROM star_wallets WHERE account_id = $1', [accountId])).rows[0];

/** 직업 풀 구성이 SKIN_POOL_SHAPE와 같다고 확정된 직업(2026-10-24 세 직업 모두) */
const SHAPE_CONFIRMED: string[] = ['warrior', 'mage', 'rogue'];

describe('스킨 칸 확률표', () => {
  it('합 100,000, 등급 합: 일반 60%, 에픽 5%, 유니크 0.5%, 전설 0.026%(히든 0.006%), 희귀 나머지', () => {
    expect(BANNER_ROWS.skin.reduce((a, r) => a + r.weight, 0)).toBe(RATE_SCALE);
    expect(BANNER_WEIGHT.skin).toEqual({ common: 60_000, rare: 34_474, epic: 5_000, unique: 500, legend: 26 });
    expect(BANNER_ROWS.skin.find((r) => r.rarity === 'legend' && r.hidden)?.weight).toBe(6);
  });

  it('히든 1종 몫 = 같은 등급 보통 1종 몫의 1/5(풀 구성 기준, 반올림 1칸 안, 전설 제외)', () => {
    for (const r of ['unique', 'epic', 'rare', 'common'] as Rarity[]) {
      const { normal, hidden } = SKIN_POOL_SHAPE[r];
      const hw = BANNER_ROWS.skin.find((x) => x.rarity === r && x.hidden)!.weight;
      const nw = BANNER_ROWS.skin.find((x) => x.rarity === r && !x.hidden)!.weight;
      expect(Math.abs(hw / hidden - nw / normal / 5) * hidden).toBeLessThanOrEqual(1);
    }
  });

  (SHAPE_CONFIRMED.length > 0 ? it : it.skip)('확정된 직업 풀의 종류 수가 SKIN_POOL_SHAPE와 같다', () => {
    for (const cls of SHAPE_CONFIRMED) {
      for (const r of Object.keys(SKIN_POOL_SHAPE) as Rarity[]) {
        expect({ cls, r, normal: skinsOf(cls, r, false).length, hidden: skinsOf(cls, r, true).length }).toEqual({ cls, r, ...SKIN_POOL_SHAPE[r] });
      }
    }
  });

  it('빈 칸의 몫: 풀에 없는 칸은 같은 등급 보통(히든) 또는 한 등급 아래 보통으로 옮기고 합은 그대로(가상 직업)', () => {
    const rows = slotTable('skin', 'nobody');
    expect(rows).toEqual([]);
    const aura = slotTable('aura', 'warrior');
    expect(aura.map((r) => [r.rarity, r.weight])).toEqual([['epic', 1_000], ['rare', 18_000], ['common', 81_000]]);
  });

  it('구성이 확정된 직업(세 직업)은 칸을 옮기지 않는다: 실제 표 = BANNER_ROWS', () => {
    for (const cls of SHAPE_CONFIRMED) {
      const rows = slotTable('skin', cls).map((r) => [r.rarity, r.hidden, r.weight]);
      expect(rows).toEqual(BANNER_ROWS.skin.map((r) => [r.rarity, r.hidden, r.weight]));
    }
  });

  it('능력치·확정 교환: 히든은 한 단계 위, 유니크·전설·히든·retired는 교환 불가, 희귀 스킨 600', () => {
    expect(damageOf(STAR_COSMETIC_BY_ID.get('skin_w_skyqueen')!)).toBe(7);
    expect(damageOf(STAR_COSMETIC_BY_ID.get('skin_w_solar')!)).toBe(6);
    expect(damageOf(STAR_COSMETIC_BY_ID.get('skin_w_skyblue')!)).toBe(1);
    for (const id of ['skin_w_solar', 'skin_w_skyqueen', 'skin_w_moonlit', 'skin_w_skyblue', 'skin_w_valkyrie', 'skin_lion']) expect(exchangePriceOf(STAR_COSMETIC_BY_ID.get(id)!)).toBe(0);
    expect(exchangePriceOf(STAR_COSMETIC_BY_ID.get('skin_w_silverbob')!)).toBe(600);
    expect(exchangePriceOf(STAR_COSMETIC_BY_ID.get('skin_obsidian')!)).toBe(4000);
  });

  it('공시: 히든 줄과 1종당 확률, 게이지 목표가 문서에 있다', () => {
    const ko = buildRatesMarkdown(getConfig().gameDataDir, 'ko');
    expect(ko).toContain('| 전설 히든 | 0.006% |');
    expect(ko).toContain('일반 보통 칸만 제외하며 일반 히든은 나올 수 있습니다');
    expect(ko).toContain('같은 등급·히든 여부의 칸 안에서는');
    expect(ko).toContain('천공의 발키리 여왕');
    expect(ko).toContain(`전설 ${CEILING.skinLegend}칸`);
    expect(ko).toContain(`전설 히든 ${CEILING.hiddenLegend}칸`);
    expect(buildRatesMarkdown(getConfig().gameDataDir, 'en')).toContain('Legend Hidden');
    expect(buildRatesMarkdown(getConfig().gameDataDir, 'en')).toContain('excludes only the non-Hidden Common slot; Common Hidden can still appear');
  });
});

describe('게이지·합성·분해·컬렉션(전사)', () => {
  it('스킨 뽑기 1회에 스킨 게이지와 히든 게이지가 1칸씩, 10+1은 11칸씩', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 1100);
    expect((await go(hero, '/starshop/pull', { count: 1, banner: 'skin' })).status).toBe(200);
    const ten = await go(hero, '/starshop/pull', { count: 10, banner: 'skin' });
    expect(ten.body.data).toMatchObject({ pity: 12, hidden_pity: 12 });
    expect(await walletOf(p.accountId)).toMatchObject({ skin_pity: 12, hidden_pity: 12 });
    const s = await get(app, hero, '/starshop');
    expect(s.body.data.gauges.skin.targets.map((t: { cost: number }) => t.cost)).toEqual([CEILING.skinUnique, CEILING.skinLegend]);
    expect(s.body.data.gauges.hidden.targets.map((t: { cost: number }) => t.cost)).toEqual([CEILING.hiddenEpic, CEILING.hiddenUnique, CEILING.hiddenLegend]);
  });

  it('스킨 게이지: 유니크는 100칸, 전설은 1,000칸. 남은 칸은 이월, 히든은 스킨 게이지로 고를 수 없다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await setGauges(p.accountId, CEILING.skinLegend - 1, 0);
    const early = await go(hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_w_solar' });
    expect(early.body.errors.code).toBe('GAUGE_NOT_FULL');
    const hiddenBySkin = await go(hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_w_moonprincess' });
    expect(hiddenBySkin.body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    const retired = await go(hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_w_aurora' });
    expect(retired.body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    const uni = await go(hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_lion' });
    expect(uni.status).toBe(200);
    expect(uni.body.data.pity).toBe(CEILING.skinLegend - 1 - CEILING.skinUnique);
    await setGauges(p.accountId, CEILING.skinLegend + 5, 0);
    const leg = await go(hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_w_solar' });
    expect(leg.status).toBe(200);
    expect(leg.body.data.pity).toBe(5);
    const row = await getPool().query("SELECT rarity, by_pity FROM gacha_pulls WHERE account_id = $1 AND item_id = 'skin_w_solar'", [p.accountId]);
    expect(row.rows[0]).toEqual({ rarity: 'legend', by_pity: true });
  });

  it('히든 게이지: 에픽 히든 350칸, 유니크 히든 2,000칸, 전설 히든 3,000칸', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await setGauges(p.accountId, 0, CEILING.hiddenEpic);
    expect((await go(hero, '/starshop/claim', { banner: 'hidden', item_id: 'skin_w_skyqueen' })).body.errors.code).toBe('GAUGE_NOT_FULL');
    expect((await go(hero, '/starshop/claim', { banner: 'hidden', item_id: 'skin_w_solar' })).body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    const ep = await go(hero, '/starshop/claim', { banner: 'hidden', item_id: 'skin_w_moonlit' });
    expect(ep.status).toBe(200);
    expect(ep.body.data.pity).toBe(0);
    await setGauges(p.accountId, 0, CEILING.hiddenLegend);
    expect((await go(hero, '/starshop/claim', { banner: 'hidden', item_id: 'skin_w_skyqueen' })).status).toBe(200);
    expect(await walletOf(p.accountId)).toMatchObject({ hidden_pity: 0 });
  });

  it('교환소 "히든 게이지 +10칸"(결정 300, 주 1회)은 히든 게이지만 채운다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const { creditCrystals } = await import('../src/domains/starshop/crystalWallet');
    const { withTransaction } = await import('../src/db/pool');
    await withTransaction((c) => creditCrystals(c, { accountId: p.accountId, amount: 600, reason: 'test_grant', ref: 'test', requestId: null }));
    expect((await go(hero, '/starshop/exchange-shop/buy', { good_id: 'hidden_gauge10' })).status).toBe(200);
    expect(await walletOf(p.accountId)).toMatchObject({ skin_pity: 0, hidden_pity: 10 });
    expect((await go(hero, '/starshop/exchange-shop/buy', { good_id: 'hidden_gauge10' })).body.errors.code).toBe('GOOD_SOLD_OUT');
  });

  it('합성 유니크 -> 전설: 5번 실패 뒤 확정, 히든 여분은 재료가 아니다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await give(p.accountId, 'skin_w_moonprincess', 8); // 유니크 히든 여분
    const none = await go(hero, '/starshop/synth', { rarity: 'unique', times: 1 });
    expect(none.body.errors.code).toBe('SYNTH_NOT_ENOUGH');
    await give(p.accountId, 'skin_lion', 30);
    setRng(fakeRng({ int: (min, max) => Math.max(min, max - 1) }));
    const r = await go(hero, '/starshop/synth', { rarity: 'unique', times: 6 });
    expect(r.status).toBe(200);
    const res = r.body.data.results as { success: boolean; by_pity: boolean; item_id: string; rarity: string }[];
    expect(res.slice(0, 5).every((x) => !x.success)).toBe(true);
    expect(res[5]).toMatchObject({ success: true, by_pity: true, item_id: 'skin_w_solar', rarity: 'legend' });
    setRng(null);
    expect(await walletOf(p.accountId)).toMatchObject({ synth_fail_legend: 0 });
    const left = await getPool().query("SELECT copies FROM account_cosmetics WHERE account_id = $1 AND item_id = 'skin_w_moonprincess'", [p.accountId]);
    expect(left.rows[0]).toEqual({ copies: 8 });
  });

  it('분해: 스킨 표(전설 4,500·900), 히든은 3배', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await give(p.accountId, 'skin_w_solar', 1);
    await give(p.accountId, 'skin_w_skyqueen', 1);
    await give(p.accountId, 'skin_w_silverbob', 1);
    expect((await go(hero, '/starshop/dismantle', { item_id: 'skin_w_solar', count: 1 })).body.data).toMatchObject({ stars: 4500, crystals: 900 });
    expect((await go(hero, '/starshop/dismantle', { item_id: 'skin_w_skyqueen', count: 1 })).body.data).toMatchObject({ stars: 13500, crystals: 2700 });
    expect((await go(hero, '/starshop/dismantle', { item_id: 'skin_w_silverbob', count: 1 })).body.data).toMatchObject({ stars: 25, crystals: 5 });
  });

  it('컬렉션: 전사 히든 컬렉션은 전사 히든 12종, 예전 세트 id는 없다', async () => {
    const set = COLLECTION_BY_ID.get('col_w_hidden')!;
    expect(set.members.length).toBe(12);
    expect(set.members.every((m) => STAR_COSMETIC_BY_ID.get(m)?.hidden)).toBe(true);
    for (const old of ['col_sword', 'col_valor', 'col_knight', 'col_shade']) expect(COLLECTION_BY_ID.has(old)).toBe(false);
    expect(COLLECTION_BY_ID.get('col_hidden_all')!.requires).toEqual(['col_w_hidden', 'col_m_hidden', 'col_r_hidden']);
    expect(COLLECTION_BY_ID.get('col_master')!.requires).toEqual(['col_dawn', 'col_starlight', 'col_legend', 'col_w_epic', 'col_m_epic']);
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    for (const m of set.members) await give(p.accountId, m);
    const r = await go(hero, '/starshop/collection', { set_id: 'col_w_hidden' });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ attack: 1, health: 1 });
  });
});
