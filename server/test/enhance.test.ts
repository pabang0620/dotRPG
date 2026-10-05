import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import {
  countOf,
  expectLedgerConsistent,
  fakeRng,
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
afterAll(async () => {
  setRng(null);
  await shutdown();
});
afterEach(() => setRng(null));

/** 주사위를 고정한다(rng.int(0, 100)의 결과) */
const roll = (n: number) => setRng(fakeRng({ int: () => n }));

async function rich(cls: 'warrior' | 'mage' = 'warrior'): Promise<Hero> {
  const h = await newHero(app, cls);
  await seedGold(h, 100_000);
  await seedItem(h, 'mat_bone', 500);
  await seedItem(h, 'mat_ore', 500);
  await seedItem(h, 'mat_essence', 500);
  return h;
}

const bag = (key: string) => ({ target: { bag_key: key } });
const enhance = (h: Hero, body: Record<string, unknown>, rid?: string) => post(app, h, '/enhance', body, rid);

async function pityOf(h: Hero, key: string): Promise<number | null> {
  const r = await getPool().query('SELECT pity FROM character_enhance_pity WHERE character_id = $1 AND item_key = $2', [
    h.dbId,
    key,
  ]);
  return r.rows[0]?.pity ?? null;
}

describe('POST /characters/:id/enhance', () => {
  it('성공: 키가 +1, 비용은 표대로(10레벨 고급검 +4: 골드 60, 뼈 5, 강화석 1), 로그와 원장', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+4', 1);
    roll(0);
    const res = await enhance(h, bag('eq_sword_10_u+4'));
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({
      outcome: 'success',
      old_key: 'eq_sword_10_u+4',
      new_key: 'eq_sword_10_u+5',
      roll: 0,
      success_percent: 80,
      cost: { gold: 60, bone: 5, ore: 1, essence: 0, ticket_used: false },
    });
    expect(await countOf(h, 'eq_sword_10_u+4')).toBe(0);
    expect(await countOf(h, 'eq_sword_10_u+5')).toBe(1);
    expect(await goldOf(h)).toBe(100_100 - 60);
    expect(await countOf(h, 'mat_bone')).toBe(495);
    expect(await countOf(h, 'mat_ore')).toBe(499);
    const log = await getPool().query('SELECT outcome, roll, success_percent, gold FROM enhance_log WHERE character_id = $1', [h.dbId]);
    expect(log.rows).toEqual([{ outcome: 'success', roll: 0, success_percent: 80, gold: 60 }]);
    await expectLedgerConsistent(h);
  });

  it('성공 판정 경계: roll이 확률 미만일 때만 성공(80%에서 79 성공, 80 실패)', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+4', 2);
    roll(79);
    expect((await enhance(h, bag('eq_sword_10_u+4'))).body.data.outcome).toBe('success');
    roll(80);
    const fail = await enhance(h, bag('eq_sword_10_u+4'));
    expect(fail.body.data.outcome).toBe('keep');
    expect(fail.body.data.new_key).toBe('eq_sword_10_u+4');
    await expectLedgerConsistent(h);
  });

  it('실패(Keep): 레벨 유지, 비용은 낸다. 천장 없는 레벨은 천장이 쌓이지 않는다', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+5', 1);
    roll(99);
    const res = await enhance(h, bag('eq_sword_10_u+5'));
    expect(res.body.data).toMatchObject({ outcome: 'keep', new_key: 'eq_sword_10_u+5', pity: 0 });
    expect(await goldOf(h)).toBe(100_100 - 66);
    expect(await pityOf(h, 'eq_sword_10_u+5')).toBeNull();
  });

  it('무기 +10 실패(Drop3): 레벨 -3, 천장 +1 -> 다음 시도 확률 +1%p, 성공하면 천장 삭제', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+10', 2);
    roll(99);
    const fail = await enhance(h, bag('eq_sword_10_u+10'));
    expect(fail.body.data).toMatchObject({ outcome: 'drop3', new_key: 'eq_sword_10_u+7', pity: 1, success_percent: 25 });
    expect(await countOf(h, 'eq_sword_10_u+10')).toBe(1);
    expect(await countOf(h, 'eq_sword_10_u+7')).toBe(1);
    expect(await pityOf(h, 'eq_sword_10_u+10')).toBe(1);
    // 천장 +1이라 26%: roll 25 가 성공한다
    roll(25);
    const ok = await enhance(h, bag('eq_sword_10_u+10'));
    expect(ok.body.data).toMatchObject({ outcome: 'success', new_key: 'eq_sword_10_u+11', success_percent: 26 });
    expect(await pityOf(h, 'eq_sword_10_u+10')).toBeNull();
    await expectLedgerConsistent(h);
  });

  it('천장 100%p: 표 확률에 더해 상한 100이면 어떤 roll이든 성공', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+11', 1);
    await getPool().query("INSERT INTO character_enhance_pity (character_id, item_key, pity) VALUES ($1, 'eq_sword_10_u+11', 90)", [h.dbId]);
    roll(99);
    const res = await enhance(h, bag('eq_sword_10_u+11'));
    expect(res.body.data).toMatchObject({ outcome: 'success', success_percent: 100, new_key: 'eq_sword_10_u+12' });
  });

  it('파괴: 보호권이 없으면 장비가 사라진다', async () => {
    const h = await rich();
    await seedItem(h, 'eq_plate_1_u+10', 1);
    roll(99);
    const res = await enhance(h, bag('eq_plate_1_u+10'));
    expect(res.body.data).toMatchObject({ outcome: 'destroyed', new_key: null });
    expect(await countOf(h, 'eq_plate_1_u+10')).toBe(0);
    expect(await countOf(h, 'eq_plate_1_u')).toBe(0);
    await expectLedgerConsistent(h);
  });

  it('파괴 + 보호권: 보호권 1개를 쓰고 +0 기본 id로 초기화', async () => {
    const h = await rich();
    await seedItem(h, 'eq_plate_1_u+10', 1);
    await seedItem(h, 'ticket_protect', 2);
    roll(99);
    const res = await enhance(h, bag('eq_plate_1_u+10'));
    expect(res.body.data).toMatchObject({ outcome: 'protected', new_key: 'eq_plate_1_u', cost: { ticket_used: true } });
    expect(await countOf(h, 'ticket_protect')).toBe(1);
    expect(await countOf(h, 'eq_plate_1_u')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('시작 장비는 강화할 수 있고 보호권을 쓰지 않는다. 전사는 파괴되면 무기 슬롯이 빈다(C# FixSlots)', async () => {
    const h = await rich('warrior');
    await seedWorn(h, 0, 'eq_sword_wood+12');
    await seedItem(h, 'ticket_protect', 1);
    roll(99);
    const res = await enhance(h, { target: { worn_slot: 0 } });
    expect(res.body.data).toMatchObject({ outcome: 'destroyed', cost: { ticket_used: false } });
    expect(await wornOf(h, 0)).toBeNull();
    expect(await countOf(h, 'ticket_protect')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('마법사는 착용 무기가 파괴되면 시작 무기를 다시 받는다', async () => {
    const h = await rich('mage');
    await seedWorn(h, 0, 'eq_staff_oak+12');
    roll(99);
    const res = await enhance(h, { target: { worn_slot: 0 } });
    expect(res.body.data.outcome).toBe('destroyed');
    expect(await wornOf(h, 0)).toBe('eq_staff_oak');
    expect(res.body.data.delta.worn).toEqual([{ slot: 0, item_key: 'eq_staff_oak' }]);
    await expectLedgerConsistent(h);
  });

  it('착용 슬롯 성공: 슬롯의 키가 바뀐다', async () => {
    const h = await rich();
    await seedWorn(h, 0, 'eq_sword_wood'); // +0은 100%
    roll(50);
    const res = await enhance(h, { target: { worn_slot: 0 } });
    expect(res.body.data).toMatchObject({ outcome: 'success', new_key: 'eq_sword_wood+1' });
    expect(await wornOf(h, 0)).toBe('eq_sword_wood+1');
    expect(res.body.data.delta.worn).toEqual([{ slot: 0, item_key: 'eq_sword_wood+1' }]);
    await expectLedgerConsistent(h);
  });

  it('입력 오류: 확률·비용·주사위를 보내거나 대상이 둘/없음이면 400', async () => {
    const h = await rich();
    expect((await enhance(h, { ...bag('eq_sword_10_u'), chance: 100 })).status).toBe(400);
    expect((await enhance(h, { ...bag('eq_sword_10_u'), roll: 0 })).status).toBe(400);
    expect((await enhance(h, { target: { worn_slot: 0, bag_key: 'eq_sword_10_u' } })).status).toBe(400);
    expect((await enhance(h, { target: {} })).status).toBe(400);
    expect((await enhance(h, {})).status).toBe(400);
    expect((await enhance(h, { target: { worn_slot: 6 } })).status).toBe(400);
    expect((await enhance(h, { target: { bag_key: 'Bad Key' } })).status).toBe(400);
  });

  it('잘못된 대상과 최대 강화', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+20', 1);
    const max = await enhance(h, bag('eq_sword_10_u+20'));
    expect(max.status).toBe(422);
    expect(max.body.errors.code).toBe('ENHANCE_MAX_LEVEL');
    expect((await enhance(h, bag('eq_sword_10_u+3'))).body.errors.code).toBe('ENHANCE_INVALID_TARGET'); // 가방에 없음
    expect((await enhance(h, { target: { worn_slot: 3 } })).body.errors.code).toBe('ENHANCE_INVALID_TARGET'); // 빈 슬롯
    expect((await enhance(h, bag('potion_hp'))).body.errors.code).toBe('ENHANCE_INVALID_TARGET'); // 장비 아님
    expect(await goldOf(h)).toBe(100_100);
  });

  it('골드·재료 부족: 422 ENHANCE_NOT_ENOUGH(need/have)이고 아무것도 바뀌지 않는다', async () => {
    const h = await newHero(app); // 골드 100, 재료 없음
    await seedItem(h, 'eq_sword_10_u+4', 1);
    const res = await enhance(h, bag('eq_sword_10_u+4'));
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({
      code: 'ENHANCE_NOT_ENOUGH',
      need: { gold: 60, bone: 5, ore: 1, essence: 0 },
      have: { gold: 100, bone: 0, ore: 0, essence: 0 },
    });
    expect(await goldOf(h)).toBe(100);
    expect(await countOf(h, 'eq_sword_10_u+4')).toBe(1);
    // 창고의 재료는 쓰지 않는다
    await seedItem(h, 'mat_bone', 10, 'storage');
    await seedItem(h, 'mat_ore', 10, 'storage');
    expect((await enhance(h, bag('eq_sword_10_u+4'))).status).toBe(422);
  });

  it('재전송: 같은 request_id는 다시 굴리지 않는다', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+4', 1);
    const rid = randomUUID();
    roll(0);
    const a = await enhance(h, bag('eq_sword_10_u+4'), rid);
    roll(99); // 다시 굴렸다면 결과가 달라진다
    const b = await enhance(h, bag('eq_sword_10_u+4'), rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    const n = await getPool().query('SELECT count(*)::int AS n FROM enhance_log WHERE character_id = $1', [h.dbId]);
    expect(n.rows[0].n).toBe(1);
    expect(await goldOf(h)).toBe(100_100 - 60);
  });

  it('동시 요청: 같은 장비 한 개를 동시에 강화해도 한 번만 처리된다', async () => {
    const h = await rich();
    await seedItem(h, 'eq_sword_10_u+4', 1);
    roll(0);
    const [a, b] = await Promise.all([enhance(h, bag('eq_sword_10_u+4')), enhance(h, bag('eq_sword_10_u+4'))]);
    expect([a.status, b.status].sort()).toEqual([200, 422]);
    expect(await countOf(h, 'eq_sword_10_u+5')).toBe(1);
    expect(await goldOf(h)).toBe(100_100 - 60);
    await expectLedgerConsistent(h);
  });
});
