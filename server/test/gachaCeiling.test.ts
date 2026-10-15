// 천장 게이지(0033, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A): 봉인된 상자 100회마다 +12 강화권, 장비 뽑기 300회마다 유니크 장비 선택
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { setRng } from '../src/utils/rng';
import { gearCeilingChoices } from '../src/domains/starshop/gearCeiling';
import { GEAR_CEILING } from '../src/domains/starshop/starshopDefs';
import { countOf, expectLedgerConsistent, get, post, seedItem } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { payApp, resetPay } from './payHelpers';
import { newPlayer, openApi, pullApi, rollOf, scriptRng, sealedInfo, type Player } from './sealedBoxHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterEach(() => setRng(null));
afterAll(async () => {
  setRng(null);
  await shutdown();
});

const setGauge = (pl: Player, col: 'sealed_gauge' | 'gear_gauge', v: number) =>
  getPool().query(`INSERT INTO account_ceilings (account_id, ${col}) VALUES ($1, $2) ON CONFLICT (account_id) DO UPDATE SET ${col} = $2`, [pl.p.accountId, v]);
const sealedCeiling = (pl: Player, requestId?: string) => post(app, pl.hero, '/starshop/sealed/ceiling', {}, requestId);
const gearCeiling = (pl: Player, body: Record<string, unknown>, requestId?: string) => post(app, pl.hero, '/starshop/gear-ceiling', body, requestId);

describe('봉인된 상자 천장', () => {
  it('11연은 게이지 +11, 요약에 천장 표시, 100 전에는 교환 거절', async () => {
    const pl = await newPlayer(app, 1000);
    scriptRng([rollOf('potion_hi_x5', false)]);
    const r = await pullApi(app, pl, 11);
    expect(r.status).toBe(200);
    expect(r.body.data.ceiling).toEqual({ gauge: 11, max: 100, ready: false, reward: { item_key: 'ticket_enh12', count: 1 } });
    const info = await sealedInfo(app, pl);
    expect(info.body.data.ceiling).toMatchObject({ gauge: 11, max: 100, ready: false });
    const c = await sealedCeiling(pl);
    expect(c.status).toBe(422);
    expect(c.body.errors.code).toBe('GAUGE_NOT_FULL');
  });

  it('가방의 봉인된 상자를 열어도 +1, 확률 상자는 세지 않는다', async () => {
    const pl = await newPlayer(app);
    await seedItem(pl.hero, 'box_sealed', 1);
    await seedItem(pl.hero, 'box_enh10_30', 1);
    scriptRng([rollOf('potion_hi_x5', false)]);
    const a = await openApi(app, pl, 'box_sealed');
    expect(a.status).toBe(200);
    expect(a.body.data.ceiling.gauge).toBe(1);
    getRateLimitStore().clear();
    const b = await openApi(app, pl, 'box_enh10_30');
    expect(b.status).toBe(200);
    const row = await getPool().query<{ sealed_gauge: number }>('SELECT sealed_gauge FROM account_ceilings WHERE account_id = $1', [pl.p.accountId]);
    expect(row.rows[0]?.sealed_gauge).toBe(1);
  });

  it('가득 차면 +12 강화권 1장, 넘친 만큼 남고, 같은 request_id 재전송은 한 번만 지급', async () => {
    const pl = await newPlayer(app);
    await setGauge(pl, 'sealed_gauge', 105);
    const id = randomUUID();
    const r = await sealedCeiling(pl, id);
    expect(r.status).toBe(200);
    expect(r.body.data.reward).toEqual({ item_key: 'ticket_enh12', count: 1 });
    expect(r.body.data.ceiling).toMatchObject({ gauge: 5, ready: false });
    getRateLimitStore().clear();
    const again = await sealedCeiling(pl, id);
    expect(again.status).toBe(200);
    expect(again.body.data.ceiling.gauge).toBe(5);
    expect(await countOf(pl.hero, 'ticket_enh12')).toBe(1);
    const claims = await getPool().query("SELECT kind, item_key, gauge_before, gauge_after FROM ceiling_claims WHERE account_id = $1", [pl.p.accountId]);
    expect(claims.rows).toEqual([{ kind: 'sealed', item_key: 'ticket_enh12', gauge_before: 105, gauge_after: 5 }]);
    const led = await getPool().query("SELECT reason, ref FROM item_ledger WHERE character_id = $1 AND item_key = 'ticket_enh12'", [pl.hero.dbId]);
    expect(led.rows).toEqual([{ reason: 'sealed_box', ref: 'ceiling' }]);
    await expectLedgerConsistent(pl.hero);
  });
});

describe('장비 뽑기 천장', () => {
  it('장비 뽑기마다 +1(외형 뽑기는 세지 않음), 요약에 표시', async () => {
    const pl = await newPlayer(app, 1200);
    const r = await post(app, pl.hero, '/starshop/pull', { count: 1, banner: 'weapon' });
    expect(r.status).toBe(200);
    expect(r.body.data.gear_ceiling).toEqual({ gauge: 1, max: GEAR_CEILING.max, ready: false, rarity: 'unique' });
    getRateLimitStore().clear();
    const aura = await post(app, pl.hero, '/starshop/pull', { count: 10 });
    expect(aura.status).toBe(200);
    expect(aura.body.data.gear_ceiling).toBeUndefined();
    getRateLimitStore().clear();
    const s = await get(app, pl.hero, '/starshop');
    expect(s.body.data.gear_ceiling).toMatchObject({ gauge: 1, max: 300 });
  });

  it('가득 차면 고른 유니크 장비 1개, 표에 없는 장비는 거절, 내 레벨 위 단계도 고를 수 있다', async () => {
    const pl = await newPlayer(app);
    const choices = gearCeilingChoices('weapon', 'warrior', 0);
    expect(choices.length).toBeGreaterThan(0);
    const early = await gearCeiling(pl, { banner: 'weapon', item_id: choices[0] });
    expect(early.body.errors.code).toBe('GAUGE_NOT_FULL');
    await setGauge(pl, 'gear_gauge', 300);
    getRateLimitStore().clear();
    const wrong = await gearCeiling(pl, { banner: 'weapon', item_id: 'potion_hp_hi' });
    expect(wrong.body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    getRateLimitStore().clear();
    // 2026-10-14: 단계 잠금이 없다. Lv1 캐릭터가 Lv40 단계를 골라도 잠김이 아니라 그 단계 표로 판정한다(다른 단계 장비라 거절)
    const otherTier = await gearCeiling(pl, { banner: 'weapon', item_id: choices[0], tier: 7 });
    expect(otherTier.body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    getRateLimitStore().clear();
    const ok = await gearCeiling(pl, { banner: 'weapon', item_id: choices[0] });
    expect(ok.status).toBe(200);
    expect(ok.body.data.gear_ceiling).toMatchObject({ gauge: 0, ready: false });
    expect(await countOf(pl.hero, choices[0]!)).toBe(1);
    const bind = await getPool().query<{ bind: string }>("SELECT bind FROM character_items WHERE character_id = $1 AND item_key = $2", [pl.hero.dbId, choices[0]]);
    expect(bind.rows.map((x) => x.bind)).toContain('account');
    await expectLedgerConsistent(pl.hero);
  });
});
