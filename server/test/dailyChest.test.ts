// 일일·주간 완료 상자(Docs/PLAN_DAILY_REWARDS_2026-10-24.md): 칸을 다 받아야 열림, 한 번만·재생, 7일 연속 단계와 금 상자,
// 하루 유예(어제 상자를 오늘 열기), 끊기면 1단계, 보호권 조각 자동 합치기, 만렙 추가분, 주간 상자 스킨 게이지(계정당 주 1회),
// 강화 재료 상자 열기(구간·수량·기록), 확률 공개 문서.
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { getDailyQuests } from '../src/gamedata/dailyQuests';
import { levelCap } from '../src/gamedata/chapterGate';
import { buildRatesMarkdown } from '../src/domains/policies/ratesDisclosure';
import { dailyRatesContent } from '../src/domains/dailies/dailyChest';
import { gaugeCap } from '../src/domains/starshop/starshopDefs';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, createChar, DATA_DIR, randomName, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, get, newHero, post, seedClaims, seedItem, seedLevel, type Hero } from './economyHelpers';

// 2026-10-05 월요일 12:00 KST = 게임 날짜 2026-10-05, 주 시작 2026-10-01(목)
const T0 = Date.parse('2026-10-05T03:00:00Z');
const DAY = 24 * 60 * 60 * 1000;
let clock = T0;
let app: Express;

/** 정수 난수: 앞에서부터 꺼내고, 다 쓰면 최솟값 */
function queueRng(values: number[]): void {
  const q = [...values];
  setRng({ int: (min) => (q.length > 0 ? (q.shift() as number) : min), unit: () => 1 });
}

beforeEach(async () => {
  clock = T0;
  setClockOverride(() => new Date(clock));
  setRng({ int: (min) => min, unit: () => 1 }); // 보너스 굴림은 늘 첫 줄(강화석 x10)
  await resetDb();
  app = buildApp();
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const dayOf = (offset: number) => new Date(Date.parse('2026-10-05T00:00:00Z') + offset * DAY).toISOString().slice(0, 10);

async function unlockedHero(level: number): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, level);
  await seedClaims(h, ['career_path']);
  return h;
}

/** 그 기간 날짜의 칸을 청구 완료로 채운다(진행 집계는 dailies.test.ts가 본다) */
async function seedClaimed(h: Hero, period: 'day' | 'week', day: string, slots: number): Promise<void> {
  for (let s = 0; s < slots; s++) {
    await getPool().query(
      `INSERT INTO daily_quests (character_id, period, game_day, slot, template_id, accepted_at, claimed_at, reward, request_id)
       VALUES ($1, $2, $3::date, $4, 't', now(), now(), '{"xp":0,"gold":0}'::jsonb, $5)`,
      [h.dbId, period, day, s, randomUUID()],
    );
  }
}

const open = (h: Hero, day: string, period: 'day' | 'week' = 'day', rid?: string) => post(app, h, '/dailies/chest', { day, period }, rid);
const view = async (h: Hero) => (await get(app, h, '/dailies')).body.data;
const chestRow = async (h: Hero, day: string) =>
  (await getPool().query('SELECT streak, step FROM daily_chests WHERE character_id = $1 AND game_day = $2::date', [h.dbId, day])).rows[0];

describe('일일 완료 상자', () => {
  it('칸을 다 받기 전에는 잠김, 다 받으면 1단계 나무 상자: 고정 내용 + 보너스 1회, 재생과 두 번째 열기', async () => {
    const h = await unlockedHero(20);
    await seedClaimed(h, 'day', dayOf(0), 2);
    const v0 = await view(h);
    expect(v0.chests.day.at(-1)).toMatchObject({ day: dayOf(0), state: 'locked', done: 2, need: 3, streak: 1, step: 1, grade: 'wood', bonus_rolls: 1, max_level: false });
    const locked = await open(h, dayOf(0));
    expect(locked.status).toBe(422);
    expect(locked.body.errors.code).toBe('DAILY_CHEST_LOCKED');

    await getPool().query(
      `INSERT INTO daily_quests (character_id, period, game_day, slot, template_id, claimed_at, reward) VALUES ($1, 'day', $2::date, 2, 't', now(), '{}'::jsonb)`,
      [h.dbId, dayOf(0)],
    );
    const rid = randomUUID();
    const r = await open(h, dayOf(0), 'day', rid);
    expect(r.status).toBe(200);
    expect(r.body.data.opened).toMatchObject({ streak: 1, step: 1, grade: 'wood', skin_gauge: 0 });
    expect(await countOf(h, 'mat_ore')).toBe(8 + 10); // 고정 8 + 보너스(강화석 x10)
    expect(await countOf(h, 'frag_protect')).toBe(1);
    expect(await countOf(h, 'box_mat')).toBe(1);
    expect(await countOf(h, 'key_seal')).toBe(0);
    expect(r.body.data.chests.day.at(-1)).toMatchObject({ state: 'claimed' });
    expect(r.body.data.chests.streak).toEqual({ current: 1, cycle: 7, grace: false });

    const again = await open(h, dayOf(0), 'day', rid);
    expect(again.status).toBe(200);
    expect(again.body.data.opened).toEqual(r.body.data.opened);
    const twice = await open(h, dayOf(0));
    expect(twice.status).toBe(409);
    expect(twice.body.errors.code).toBe('DAILY_CHEST_CLAIMED');
    expect(await countOf(h, 'mat_ore')).toBe(18);

    const log = await getPool().query("SELECT source, row_id, rate FROM sealed_pulls WHERE character_id = $1", [h.dbId]);
    expect(log.rows).toEqual([{ source: 'daily_chest', row_id: 'ore_x10', rate: 60 }]);
    await expectLedgerConsistent(h);
  });

  it('7일 연속이면 7일째 금 상자(장비 보호권 확정), 조각 10개는 자동으로 보호권 1장', async () => {
    const h = await unlockedHero(20);
    for (let d = 0; d < 7; d++) {
      clock = T0 + d * DAY;
      await seedClaimed(h, 'day', dayOf(d), 3);
      const r = await open(h, dayOf(d));
      expect(r.status).toBe(200);
      expect(r.body.data.opened.streak).toBe(d + 1);
      expect(r.body.data.opened.step).toBe(d + 1);
    }
    const last = await chestRow(h, dayOf(6));
    expect(last).toEqual({ streak: 7, step: 7 });
    // 조각: 1+1+1+1+2+2+2 = 10 -> 보호권 1장으로 합쳐지고 금 상자 확정 1장
    expect(await countOf(h, 'frag_protect')).toBe(0);
    expect(await countOf(h, 'ticket_protect')).toBe(2);
    expect(await countOf(h, 'box_mat')).toBe(6 + 2);
    // 8일째는 다시 1단계(연속 8일)
    clock = T0 + 7 * DAY;
    await seedClaimed(h, 'day', dayOf(7), 3);
    const r8 = await open(h, dayOf(7));
    expect(r8.body.data.opened).toMatchObject({ streak: 8, step: 1, grade: 'wood' });
    await expectLedgerConsistent(h);
  });

  it('하루를 통째로 놓치면 1단계부터, 어제 의뢰를 오늘 끝내 어제 상자를 열면 이어진다(오늘 상자를 먼저 열어도)', async () => {
    const h = await unlockedHero(20);
    await seedClaimed(h, 'day', dayOf(0), 3);
    await open(h, dayOf(0));
    // 다음 날(1일)은 손대지 않고 2일에: 1일 상자는 유예로 아직 열 수 있다
    clock = T0 + 2 * DAY;
    const v = await view(h);
    expect(v.chests.streak).toEqual({ current: 1, cycle: 7, grace: true });
    expect(v.chests.day.map((c: { day: string }) => c.day)).toEqual([dayOf(1), dayOf(2)]);
    // 오늘(2일) 상자를 먼저 연다: 어제가 비어 있어 1단계
    await seedClaimed(h, 'day', dayOf(2), 3);
    expect((await open(h, dayOf(2))).body.data.opened.streak).toBe(1);
    // 어제(1일) 칸을 마저 끝내고 연다: 0일에 이어 2, 오늘 기록은 3으로 이어 붙는다
    await seedClaimed(h, 'day', dayOf(1), 3);
    const late = await open(h, dayOf(1));
    expect(late.body.data.opened).toMatchObject({ streak: 2, step: 2 });
    expect(await chestRow(h, dayOf(2))).toEqual({ streak: 3, step: 1 });
    expect(late.body.data.chests.streak).toEqual({ current: 3, cycle: 7, grace: false });
    // 5일: 3·4일을 통째로 놓쳐 끊겼고, 이틀 지난 3일 상자는 열 수 없다
    clock = T0 + 5 * DAY;
    expect((await view(h)).chests.streak.current).toBe(0);
    await seedClaimed(h, 'day', dayOf(3), 3);
    const expired = await open(h, dayOf(3));
    expect(expired.status).toBe(422);
    expect(expired.body.errors.code).toBe('DAILY_EXPIRED');
    await seedClaimed(h, 'day', dayOf(5), 3);
    expect((await open(h, dayOf(5))).body.data.opened.streak).toBe(1);
  });

  it('만렙이면 레이드 열쇠 조각(금 상자는 고대의 핵까지)을 더 준다', async () => {
    const cap = levelCap(getGameData().economy.progression.maxLevel);
    const h = await unlockedHero(cap);
    await seedClaimed(h, 'day', dayOf(0), 3);
    const v = await view(h);
    expect(v.chests.day.at(-1)).toMatchObject({ max_level: true });
    expect(v.chests.day.at(-1).rewards).toEqual(expect.arrayContaining([{ item_key: 'key_seal', count: 10 }]));
    await open(h, dayOf(0));
    expect(await countOf(h, 'key_seal')).toBe(10);
    await expectLedgerConsistent(h);
  });
});

describe('주간 완료 상자', () => {
  it('같은 계정의 두 캐릭터가 동시에 열어도 주간 게이지는 한 번만 지급된다', async () => {
    const h = await unlockedHero(30);
    const created = await createChar(app, h.s, randomName(), 'mage');
    const id = created.body.data.character.id;
    const alt: Hero = { s: h.s, id, dbId: Number((await getPool().query('SELECT id FROM characters WHERE uuid = $1', [id])).rows[0].id), cls: 'mage' };
    await seedLevel(alt, 30);
    await seedClaims(alt, ['career_path']);
    const week = '2026-10-01';
    for (const hero of [h, alt]) await seedClaimed(hero, 'week', week, 3);
    const replies = await Promise.all([h, alt].map((hero) => open(hero, week, 'week')));
    expect(replies.map((r) => r.status)).toEqual([200, 200]);
    expect(replies.map((r) => r.body.data.opened.skin_gauge).sort()).toEqual([0, 1]);
    expect((await getPool().query('SELECT skin_pity FROM star_wallets WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)', [h.dbId])).rows[0].skin_pity).toBe(1);
  });

  it('스킨 게이지가 상한을 넘어도 주간 상자는 열리고 누적 게이지는 보존된다', async () => {
    const h = await unlockedHero(30);
    const week = '2026-10-01';
    const before = gaugeCap('skin') + 11;
    await getPool().query('INSERT INTO star_wallets (account_id, skin_pity) SELECT account_id, $2 FROM characters WHERE id = $1', [h.dbId, before]);
    await seedClaimed(h, 'week', week, 3);
    const rid = randomUUID();
    const r = await open(h, week, 'week', rid);
    expect(r.status).toBe(200);
    expect(r.body.data.opened.skin_gauge).toBe(0);
    expect(await countOf(h, 'mat_core')).toBe(1);
    expect((await getPool().query('SELECT skin_pity FROM star_wallets WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)', [h.dbId])).rows[0].skin_pity).toBe(before);
    const replay = await open(h, week, 'week', rid);
    expect(replay.status).toBe(200);
    expect(await countOf(h, 'mat_core')).toBe(1);
  });

  it('주간 칸 3개를 다 받으면 주간 상자: 고대의 핵·스킨 게이지 +1(같은 계정 다른 캐릭터는 그 주 게이지 없음)', async () => {
    const h = await unlockedHero(30);
    const week = '2026-10-01';
    await seedClaimed(h, 'week', week, 3);
    const r = await open(h, week, 'week');
    expect(r.status).toBe(200);
    expect(r.body.data.opened).toMatchObject({ period: 'week', streak: 0, step: 0, skin_gauge: 1 });
    expect(await countOf(h, 'mat_core')).toBe(1);
    expect(await countOf(h, 'frag_protect')).toBe(3);
    const pity = async () => (await getPool().query('SELECT skin_pity FROM star_wallets WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)', [h.dbId])).rows[0]?.skin_pity;
    expect(await pity()).toBe(1);

    const res = await createChar(app, h.s, randomName(), 'mage');
    const alt: Hero = { s: h.s, id: res.body.data.character.id, dbId: Number((await getPool().query('SELECT id FROM characters WHERE uuid = $1', [res.body.data.character.id])).rows[0].id), cls: 'mage' };
    await seedLevel(alt, 30);
    await seedClaims(alt, ['career_path']);
    await seedClaimed(alt, 'week', week, 3);
    const r2 = await open(alt, week, 'week');
    expect(r2.status).toBe(200);
    expect(r2.body.data.opened.skin_gauge).toBe(0);
    expect(await countOf(alt, 'mat_core')).toBe(1);
    expect(await pity()).toBe(1);
    await expectLedgerConsistent(h);
    await expectLedgerConsistent(alt);
  });
});

describe('강화 재료 상자', () => {
  it('마지막 상자를 동시에 열어도 한 번만 지급하며 성공 요청 재전송은 보상을 늘리지 않는다', async () => {
    const h = await unlockedHero(20);
    await seedItem(h, 'box_mat', 1, 'bag', 'character');
    const ids = [randomUUID(), randomUUID()];
    const replies = await Promise.all(ids.map((id) => post(app, h, '/items/open', { item_key: 'box_mat' }, id)));
    expect(replies.map((r) => r.status).sort()).toEqual([200, 422]);
    expect(await countOf(h, 'box_mat')).toBe(0);
    expect(await countOf(h, 'mat_essence')).toBe(1);
    const successId = ids[replies.findIndex((r) => r.status === 200)];
    expect((await post(app, h, '/items/open', { item_key: 'box_mat' }, successId)).status).toBe(200);
    expect(await countOf(h, 'mat_essence')).toBe(1);
    expect((await getPool().query("SELECT count(*) AS n FROM sealed_pulls WHERE character_id = $1 AND source = 'mat_box'", [h.dbId])).rows[0].n).toBe('1');
  });

  it('구간을 고르고 구간 안에서 수량: 50~99 구간에서 77개, 기록은 구간 확률', async () => {
    const h = await unlockedHero(20);
    await seedItem(h, 'box_mat', 2, 'bag', 'character');
    // 가중치 누적 550,800,920,975,997,1000 -> 980 은 50~99 구간. 수량 굴림은 77
    queueRng([980, 77]);
    const r = await post(app, h, '/items/open', { item_key: 'box_mat' });
    expect(r.status).toBe(200);
    expect(r.body.data.results).toEqual([expect.objectContaining({ item_key: 'mat_essence', count: 77, tier: 'rare' })]);
    expect(await countOf(h, 'box_mat')).toBe(1);
    expect(await countOf(h, 'mat_essence')).toBe(77);
    const log = await getPool().query("SELECT source, row_id, count, rate FROM sealed_pulls WHERE character_id = $1", [h.dbId]);
    expect(log.rows).toEqual([{ source: 'mat_box', row_id: 'mat_essence_50_99', count: 77, rate: 2.2 }]);
    // 사용(items/use)으로는 소모되지 않는다
    const use = await post(app, h, '/items/use', { item_id: 'box_mat' });
    expect(use.body.errors.code).toBe('USE_OPEN_INSTEAD');
    expect(await countOf(h, 'box_mat')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('확률 공개 문서와 스냅샷에 완료 상자·재료 상자 표가 있다', () => {
    const md = buildRatesMarkdown(DATA_DIR, 'ko', new Date(0));
    expect(md).toContain('## 일일·주간 완료 상자 (확률표 버전 dailychest-1, 유료 재화 아님)');
    expect(md).toContain('| 장비 보호권 x1 | 1.000% |');
    expect(md).toContain('| 100 | 0.300% | 0.300% |');
    expect(md).toContain('| 1~5 | 55.000% | 11.000% |');
    const snap = dailyRatesContent();
    expect(snap.version).toBe(getDailyQuests().chests.ratesVersion);
    const en = buildRatesMarkdown(DATA_DIR, 'en', new Date(0));
    expect(en).toContain('## Daily and Weekly Completion Chests');
  });
});
