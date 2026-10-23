// 2026-10-23 새 직업 도적(rogue): Docs/PLAN_CLASS_ROGUE.md 5.1절(서버)·4.2~4.3절(장비)·6.1절(스킨 33종)
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getConfig } from '../src/config/env';
import { getPool, withTransaction } from '../src/db/pool';
import { targetSchema } from '../src/admin/mailcampaigns/mailCampaignsValidation';
import { careerIds, MAIN_HUNTERS, validateCareer, type CareerState } from '../src/domains/characters/careerRules';
import { computePower } from '../src/domains/characters/powerEstimate';
import { rollEquipmentDrop } from '../src/domains/kills/killRules';
import { buildRatesMarkdown } from '../src/domains/policies/ratesDisclosure';
import { creditCrystals } from '../src/domains/starshop/crystalWallet';
import { buildRatesContent } from '../src/domains/starshop/ratesSnapshot';
import { poolOf } from '../src/domains/starshop/starshopService';
import { EXCHANGE_GOOD_BY_ID, GACHA_COSMETICS, SKIN_GAUGE_MAX, STAR_COSMETICS } from '../src/domains/starshop/starshopDefs';
import { getGameData } from '../src/gamedata/loader';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { baseClassOfCareer, usableBy } from '../src/utils/characterClass';
import { setRng } from '../src/utils/rng';
import { fakeRng, get, newHero, post, seedItem, seedLevel, wornOf, type Hero } from './economyHelpers';
import { auth, createChar, randomName, resetDb, shutdown } from './helpers';
import { newPayer, payApp, resetPay, seedStars } from './payHelpers';

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

const eco = () => getGameData().economy;
const blank = (career = 0): CareerState => ({ schema: 1, career, nodes: [], training: [], refunded: 0, questStage: 0, awakened: false });
const check = (s: CareerState, level = 30, cls = 'rogue') => validateCareer(s, null, { level, class: cls }, [], () => false);
const promote = (h: Hero, career: number, rid: string = randomUUID()) => post(app, h, '/career/promote', { career }, rid);

/** Steam 계정의 도적 캐릭터(별조각·결정은 계정 지갑) */
async function rogueOfPayer(): Promise<{ hero: Hero; accountId: number }> {
  const p = await newPayer(app);
  const res = await createChar(app, { access: p.access } as never, randomName(), 'rogue');
  if (res.status !== 201) throw new Error(`createChar failed ${res.status}`);
  const id = res.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { hero: { s: { access: p.access } as never, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'rogue' }, accountId: p.accountId };
}

describe('도적 만들기', () => {
  it('정상: class rogue로 만들고 시작 무기 나무 단검을 낀다', async () => {
    const h = await newHero(app, 'rogue');
    expect((await getPool().query('SELECT class FROM characters WHERE id = $1', [h.dbId])).rows[0]).toEqual({ class: 'rogue' });
    expect(await wornOf(h, 0)).toBe('eq_dagger_wood');
    expect(getGameData().starter.gear.rogue).toMatchObject({ itemKey: 'eq_dagger_wood', slot: 0 });
  });

  it('입력 오류: 모르는 직업은 400', async () => {
    const p = await newPayer(app);
    const res = await request(app).post('/characters').set(auth({ access: p.access } as never)).send({ request_id: randomUUID(), name: randomName(), class: 'thief' });
    expect(res.status).toBe(400);
  });

  it('마이그레이션 0050 CHECK: rogue·전직 6은 들어가고, 모르는 직업·전직 7은 거절', async () => {
    const h = await newHero(app, 'rogue');
    await expect(getPool().query("UPDATE characters SET class = 'thief' WHERE id = $1", [h.dbId])).rejects.toThrow(/characters_class_check/);
    await expect(getPool().query("INSERT INTO character_career (character_id, career, source) VALUES ($1, 7, 'admin')", [h.dbId])).rejects.toThrow(/career_check/);
    await getPool().query("INSERT INTO character_career (character_id, career, source) VALUES ($1, 6, 'admin')", [h.dbId]);
    await getPool().query('INSERT INTO character_career_trials (character_id, career) VALUES ($1, 6)', [h.dbId]);
    const r = await getPool().query("SELECT pg_get_constraintdef(oid) AS d FROM pg_constraint WHERE conname = 'auction_listings_class_only_check'");
    expect((r.rows[0] as { d: string }).d).toContain('rogue');
  });

  it('직업 번호 매핑은 한 함수: 1,2 전사 / 3,4 마법사 / 5,6 도적', () => {
    expect([1, 2, 3, 4, 5, 6, 0, 7].map(baseClassOfCareer)).toEqual(['warrior', 'warrior', 'mage', 'mage', 'rogue', 'rogue', null, null]);
  });
});

describe('어쌔신·닌자 전직', () => {
  it('정상: 도적 Lv15가 5 어쌔신으로 전직, 같은 request_id 재전송은 같은 응답', async () => {
    const h = await newHero(app, 'rogue');
    await seedLevel(h, 15);
    const rid = randomUUID();
    const a = await promote(h, 5, rid);
    expect(a.status).toBe(200);
    expect(a.body.data).toMatchObject({ career: 5, stage: 0 });
    const b = await promote(h, 5, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect((await getPool().query('SELECT career FROM character_career WHERE character_id = $1', [h.dbId])).rows[0]).toEqual({ career: 5 });
  });

  it('다른 기본 직업 거부: 도적은 1~4로, 전사·마법사는 5·6으로 전직할 수 없다', async () => {
    const r = await newHero(app, 'rogue');
    await seedLevel(r, 15);
    for (const c of [1, 2, 3, 4]) expect((await promote(r, c)).body.errors.code).toBe('CAREER_BASE_MISMATCH');
    const w = await newHero(app, 'warrior');
    await seedLevel(w, 15);
    expect((await promote(w, 6)).body.errors.code).toBe('CAREER_BASE_MISMATCH');
    const m = await newHero(app, 'mage');
    await seedLevel(m, 15);
    expect((await promote(m, 5)).body.errors.code).toBe('CAREER_BASE_MISMATCH');
  });

  it('동시 전직 두 개(5와 6): 하나만 성공한다', async () => {
    const h = await newHero(app, 'rogue');
    await seedLevel(h, 15);
    const res = await Promise.all([promote(h, 5), promote(h, 6)]);
    expect(res.map((x) => x.status).sort()).toEqual([200, 409]);
    const n = await getPool().query('SELECT count(*)::int AS n FROM character_career WHERE character_id = $1', [h.dbId]);
    expect(n.rows[0]).toEqual({ n: 1 });
  });

  it('노드 목록: 어쌔신 10개(9번 은신), 닌자 9개, 주력 연사기 a_rend·n_volley', () => {
    expect(careerIds[5]).toHaveLength(10);
    expect(careerIds[6]).toHaveLength(9);
    expect(careerIds[5]?.[9]).toBe('a_stealth');
    expect(careerIds[5]?.[8]).toBe('a_awake');
    expect(careerIds[6]?.[8]).toBe('n_awake');
    expect(MAIN_HUNTERS).toEqual(expect.arrayContaining(['a_rend', 'n_volley']));
  });

  it('노드 레벨·선행: 연환 베기 Lv18, 쿠나이 순간이동 Lv18, 맹독 도포 Lv22, 각성은 노드로 못 찍는다', () => {
    const s = blank(5);
    s.nodes = [{ id: 'a_ambush', rank: 1 }, { id: 'a_rend', rank: 1 }];
    expect(() => check(s, 17)).toThrow();
    expect(() => check(s, 18)).not.toThrow();
    s.nodes = [{ id: 'a_rend', rank: 1 }];
    expect(() => check(s, 30)).toThrow(); // 선행 a_ambush 없음
    s.nodes = [{ id: 'a_vital', rank: 1 }, { id: 'a_phantom', rank: 1 }, { id: 'a_kunai', rank: 1 }, { id: 'a_venom', rank: 1 }];
    expect(() => check(s, 21)).toThrow();
    expect(() => check(s, 22)).not.toThrow();
    s.nodes = [{ id: 'a_awake', rank: 1 }];
    expect(() => check(s, 40)).toThrow();
    const n = blank(6);
    n.nodes = [{ id: 'n_far', rank: 1 }, { id: 'n_volley', rank: 1 }];
    expect(() => check(n, 17)).toThrow();
    expect(() => check(n, 18)).not.toThrow();
    n.nodes = [{ id: 'a_phantom', rank: 1 }];
    expect(() => check(n, 40)).toThrow(); // 다른 전직 노드
  });

  it('2026-10-23: 쿠나이 순간이동은 어쌔신 2번(Lv18), 연막 도약은 닌자 1번(Lv15)', () => {
    expect(careerIds[5]?.[2]).toBe('a_kunai');
    expect(careerIds[6]?.[1]).toBe('n_veil');
    const s = blank(5);
    s.nodes = [{ id: 'a_vital', rank: 1 }, { id: 'a_phantom', rank: 1 }, { id: 'a_kunai', rank: 1 }];
    expect(() => check(s, 17)).toThrow();
    expect(() => check(s, 18)).not.toThrow();
    const n = blank(6);
    n.nodes = [{ id: 'n_throw', rank: 1 }, { id: 'n_veil', rank: 1 }];
    expect(() => check(n, 15)).not.toThrow();
    n.nodes = [{ id: 'n_throw', rank: 1 }, { id: 'n_kunai', rank: 1 }];
    expect(() => check(n, 30)).toThrow(); // 옛 닌자 노드 id
  });

  it('추가 노드 은신: 파이터와 같음(선행 쿠나이 순간이동, Lv18)', () => {
    const s = blank(5);
    s.nodes = [{ id: 'a_stealth', rank: 1 }];
    expect(() => check(s, 30)).toThrow();
    s.nodes = [{ id: 'a_vital', rank: 1 }, { id: 'a_phantom', rank: 1 }, { id: 'a_kunai', rank: 1 }, { id: 'a_stealth', rank: 1 }];
    expect(() => check(s, 18)).not.toThrow();
    expect(() => check(s, 17)).toThrow();
  });
});

describe('장비: 단검·판금·각반은 도적, 로브는 거부', () => {
  it('usableBy: 판금은 전사·도적, 단검은 도적만, 로브는 마법사만', () => {
    const eq = (id: string) => eco().shop.equipment.get(id)!;
    expect(['warrior', 'mage', 'rogue'].map((c) => usableBy(eq('eq_plate_10_u'), c))).toEqual([true, false, true]);
    expect(['warrior', 'mage', 'rogue'].map((c) => usableBy(eq('eq_dagger_10_u'), c))).toEqual([false, false, true]);
    expect(['warrior', 'mage', 'rogue'].map((c) => usableBy(eq('eq_robe_10_u'), c))).toEqual([false, true, false]);
    expect(usableBy(eq('eq_ring_10_u'), 'rogue')).toBe(true);
  });

  it('장착: 단검·판금은 되고 로브·검은 CLASS_MISMATCH', async () => {
    const h = await newHero(app, 'rogue');
    await seedLevel(h, 10);
    for (const k of ['eq_dagger_10_u', 'eq_plate_10_u', 'eq_greaves_10_u', 'eq_robe_10_u', 'eq_sword_10_u']) await seedItem(h, k, 1);
    expect((await post(app, h, '/equipment/equip', { item_key: 'eq_dagger_10_u' })).status).toBe(200);
    expect(await wornOf(h, 0)).toBe('eq_dagger_10_u');
    expect((await post(app, h, '/equipment/equip', { item_key: 'eq_plate_10_u' })).status).toBe(200);
    expect((await post(app, h, '/equipment/equip', { item_key: 'eq_greaves_10_u' })).status).toBe(200);
    const robe = await post(app, h, '/equipment/equip', { item_key: 'eq_robe_10_u' });
    expect(robe.status).toBe(422);
    expect(robe.body.errors.code).toBe('CLASS_MISMATCH');
    expect((await post(app, h, '/equipment/equip', { item_key: 'eq_sword_10_u' })).body.errors.code).toBe('CLASS_MISMATCH');
  });

  it('무기 해제는 거부(WEAPON_REQUIRED), 문구는 직업 공통', async () => {
    const h = await newHero(app, 'rogue');
    const r = await post(app, h, '/equipment/unequip', { slot: 0 });
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('WEAPON_REQUIRED');
    expect(r.body.message).toContain('이 직업은');
  });

  it('드롭 후보: 도적에게 단검·판금이 나오고, 못 쓰는 종류는 무게 x0.75', () => {
    const tier = 1;
    const pool = eco().shop.equipmentList.filter((e) => e.dropWeight > 0 && !e.bossOnly && e.levelTier === tier);
    const seen = new Set<string>();
    // 정수 난수 0..total-1 을 모두 굴려 후보 전체를 본다(쓰는 장비 x4, 못 쓰는 장비 x3)
    const total = pool.reduce((a, e) => a + (usableBy(e, 'rogue') ? 4 : 3) * e.dropWeight, 0);
    for (let v = 0; v < total; v += 1) {
      const id = rollEquipmentDrop(eco(), 'rogue', 1, fakeRng({ unit: 0, int: () => v }), 10);
      if (id) seen.add(id.replace(/^(eq_[a-z]+)_.*$/, '$1'));
    }
    expect(seen).toEqual(new Set(['eq_dagger', 'eq_plate', 'eq_greaves', 'eq_neck', 'eq_ring', 'eq_sword', 'eq_staff', 'eq_robe', 'eq_skirt']));
  });

  it('레이드 상점·장비 뽑기 표: 도적 후보는 단검·판금·각반·장신구', () => {
    const rs = getGameData().raidShop;
    if (rs) {
      for (const p of rs.products.values()) {
        const ids = Object.values(p.results.rogue ?? {}).flat();
        expect(ids.length).toBeGreaterThan(0);
        for (const id of ids) expect(usableBy(eco().shop.equipment.get(id)!, 'rogue')).toBe(true);
      }
    }
    const content = buildRatesContent() as { gear: Record<string, { items: string[] }[]> };
    const weapon = content.gear['weapon.rogue.2']!.flatMap((t) => t.items);
    expect(weapon.length).toBeGreaterThan(0);
    expect(weapon.every((id) => id.startsWith('eq_dagger_'))).toBe(true);
  });

  it('전투력: 도적 기본 공격력은 전사와 같다(10)', () => {
    expect(computePower('rogue', 20, [], 0)).toBe(computePower('warrior', 20, [], 0));
  });

  it('메일 캠페인 대상 직업은 셋까지(rogue 허용)', () => {
    expect(targetSchema.safeParse({ classes: ['warrior', 'mage', 'rogue'] }).success).toBe(true);
    expect(targetSchema.safeParse({ classes: ['rogue', 'rogue', 'rogue', 'rogue'] }).success).toBe(false);
    expect(targetSchema.safeParse({ classes: ['thief'] }).success).toBe(false);
  });
});

describe('도적 스킨 33종(6.1절)', () => {
  const rogueSkins = STAR_COSMETICS.filter((c) => c.skin === 'rogue');

  it('구성: 일반 10, 희귀 10, 에픽 7 + 교환 전용 1, 유니크 5, id는 skin_r_ 접두', () => {
    expect(rogueSkins).toHaveLength(33);
    const count = (r: string, ex = false) => rogueSkins.filter((c) => c.rarity === r && !!c.exchangeOnly === ex).length;
    expect([count('common'), count('rare'), count('epic'), count('epic', true), count('unique')]).toEqual([10, 10, 7, 1, 5]);
    expect(rogueSkins.every((c) => c.id.startsWith('skin_r_'))).toBe(true);
    expect(rogueSkins.find((c) => c.id === 'skin_r_navy')?.name).toBe('남색 밀정복');
    expect(rogueSkins.find((c) => c.id === 'skin_r_scorpion')).toMatchObject({ name: '황금 전갈', rarity: 'unique' });
    expect(new Set(STAR_COSMETICS.map((c) => c.id)).size).toBe(STAR_COSMETICS.length);
    expect(GACHA_COSMETICS.some((c) => c.id === 'skin_r_starshade')).toBe(false);
    for (const r of ['common', 'rare', 'epic', 'unique'] as const) expect(poolOf('skin', r, 'rogue').every((c) => c.skin === 'rogue')).toBe(true);
  });

  it('뽑기: 도적 스킨 뽑기 10+1은 모두 도적 스킨이고 별조각이 줄어든다', async () => {
    const { hero, accountId } = await rogueOfPayer();
    await seedStars(accountId, 1000);
    const r = await post(app, hero, '/starshop/pull', { count: 10, banner: 'skin' });
    expect(r.status).toBe(200);
    expect(r.body.data.results).toHaveLength(11);
    for (const x of r.body.data.results as { item_id: string }[]) expect(x.item_id).toMatch(/^skin_r_/);
    expect(r.body.data.balance).toBe(0);
    const poor = await post(app, hero, '/starshop/pull', { count: 1, banner: 'skin' });
    expect(poor.status).toBe(422);
    expect(poor.body.errors.code).toBe('NOT_ENOUGH_STARS');
  });

  it('선택 게이지: 도적은 도적 유니크 스킨만 고른다', async () => {
    const { hero, accountId } = await rogueOfPayer();
    await getPool().query('INSERT INTO star_wallets (account_id, skin_pity) VALUES ($1, $2) ON CONFLICT (account_id) DO UPDATE SET skin_pity = $2', [accountId, SKIN_GAUGE_MAX]);
    const wrong = await post(app, hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_w_phoenix' });
    expect(wrong.body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    const ok = await post(app, hero, '/starshop/claim', { banner: 'skin', item_id: 'skin_r_nightqueen' });
    expect(ok.status).toBe(200);
    expect(ok.body.data.item_id).toBe('skin_r_nightqueen');
  });

  it('교환: 별빛 교환소 전용 에픽 별빛 그림자(결정 900)', async () => {
    expect(EXCHANGE_GOOD_BY_ID.get('ex_skin_starshade')).toMatchObject({ itemId: 'skin_r_starshade', price: 900 });
    const { hero, accountId } = await rogueOfPayer();
    const poor = await post(app, hero, '/starshop/exchange-shop/buy', { good_id: 'ex_skin_starshade' });
    expect(poor.body.errors.code).toBe('NOT_ENOUGH_CRYSTALS');
    await withTransaction((c) => creditCrystals(c, { accountId, amount: 900, reason: 'test_grant', ref: 'test', requestId: null }));
    const rid = randomUUID();
    const ok = await post(app, hero, '/starshop/exchange-shop/buy', { good_id: 'ex_skin_starshade' }, rid);
    expect(ok.status).toBe(200);
    const again = await post(app, hero, '/starshop/exchange-shop/buy', { good_id: 'ex_skin_starshade' }, rid);
    expect(again.body).toEqual(ok.body);
    const own = await getPool().query("SELECT count(*)::int AS n FROM account_cosmetics WHERE account_id = $1 AND item_id = 'skin_r_starshade'", [accountId]);
    expect(own.rows[0]).toEqual({ n: 1 });
  });

  it('요약 화면의 스킨 확률표와 확률 공시에 도적 줄', async () => {
    const { hero } = await rogueOfPayer();
    const s = await get(app, hero, '/starshop');
    expect(s.status).toBe(200);
    const items = (s.body.data.skin_rates as { items: { id: string }[] }[]).flatMap((t) => t.items.map((i) => i.id));
    expect(items.length).toBe(32);
    expect(items.every((id) => id.startsWith('skin_r_'))).toBe(true);
    const ko = buildRatesMarkdown(getConfig().gameDataDir, 'ko');
    expect(ko).toContain('스킨 뽑기 (도적)');
    expect(ko).toContain('칠흑의 그림자 여왕');
    expect(buildRatesMarkdown(getConfig().gameDataDir, 'en')).toContain('Skin Draw (Rogue)');
  });
});
