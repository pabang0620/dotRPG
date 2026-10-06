// 10단계 14절 C: 클리어권 구매(T1)와 주간 활동 보상(T2)
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { expectLedgerConsistent, fakeRng, get, goldOf, newHero, post, seedGold, seedLevel, type Hero } from './economyHelpers';
import { mk, secondChar } from './auctionHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { MONDAY, accountIdOf, expectTicketLedgerConsistent, ticketTotal } from './sweepHelpers';

let app: Express;
let fixed = new Date(MONDAY);
// 이번 주(2026-10-01 목 06:00 KST ~ 10-08 목 06:00 KST)의 시작과 다음 주 시작
const WEEK_START = new Date('2026-09-30T21:00:00Z');
const NEXT_WEEK = new Date('2026-10-07T21:00:00Z');

beforeEach(async () => {
  await resetDb();
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
  app = buildApp({ SWEEP_ENABLED: 'true' });
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const buy = (h: Hero, n: number, rid?: string) => post(app, h, '/sweep/tickets/buy', { count: n }, rid);
const claimWeekly = (h: Hero, rid?: string) => post(app, h, '/sweep/weekly/claim', {}, rid);
const counter = async (h: Hero, kind: string, week = WEEK_START): Promise<number> => {
  const r = await getPool().query<{ used: number }>('SELECT used FROM account_week_counters WHERE account_id = $1 AND week_start = $2 AND kind = $3', [await accountIdOf(h), week, kind]);
  return r.rows[0]?.used ?? 0;
};
const setCounter = async (h: Hero, kind: string, used: number, week = WEEK_START): Promise<void> => {
  await getPool().query(
    `INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES ($1, $2, $3, $4)
     ON CONFLICT (account_id, week_start, kind) DO UPDATE SET used = $4`,
    [await accountIdOf(h), week, kind, used],
  );
};
const count = async (sql: string, params: unknown[] = []): Promise<number> => Number(((await getPool().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);

describe('C1 T1 구매: 가격과 골드', () => {
  it('가격 = 기본가 x (단계 + 1): 레벨 10이면 2,000, 원장 한 줄씩과 지갑 일반 로트', async () => {
    const h = await mk(app, 100_000);
    await seedLevel(h, 10);
    const rid = randomUUID();
    const res = await buy(h, 3, rid);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ count: 3, unit_price: 2000, total: 6000, tickets: { total: 3, normal: 3, event: [] } });
    expect(res.body.data.shop).toEqual({ weekly_limit: 7, weekly_used: 3, weekly_left: 4, unit_price: 2000 });
    expect(res.body.data.delta.gold).toBe(94_000);
    expect(await goldOf(h)).toBe(94_000);
    const g = await getPool().query("SELECT delta, balance_after, ref FROM gold_ledger WHERE character_id = $1 AND reason = 'sweep_ticket_buy'", [h.dbId]);
    expect(g.rows).toEqual([{ delta: '-6000', balance_after: '94000', ref: 'sweep_ticket' }]);
    const t = await getPool().query("SELECT delta, balance_after, ref, request_id FROM sweep_ticket_ledger WHERE reason = 'shop_buy'");
    expect(t.rows).toEqual([{ delta: 3, balance_after: 3, ref: rid, request_id: rid }]);
    expect(await counter(h, 'sweep_buy')).toBe(3);
    await expectLedgerConsistent(h);
    await expectTicketLedgerConsistent(h);
  });

  it('삭제한 높은 레벨 캐릭터가 있으면 그 단계로 가격을 계산한다', async () => {
    const h = await mk(app, 200_000);
    await seedLevel(h, 5);
    const high = await secondChar(app, h);
    await seedLevel(high, 40);
    expect((await buy(h, 1)).body.data.unit_price).toBe(8000);
    await getPool().query('UPDATE characters SET deleted_at = now() WHERE id = $1', [high.dbId]);
    expect((await buy(h, 1)).body.data.unit_price).toBe(8000); // 삭제해도 계정 최고 레벨로 센다
  });

  it('골드 부족이면 NOT_ENOUGH_GOLD이고 원장·지갑·카운터가 하나도 생기지 않는다', async () => {
    const h = await mk(app, 100);
    const res = await buy(h, 2);
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'NOT_ENOUGH_GOLD', need: 2000, have: 100 });
    expect(await count("SELECT count(*) AS n FROM gold_ledger WHERE reason = 'sweep_ticket_buy'")).toBe(0);
    expect(await count('SELECT count(*) AS n FROM sweep_ticket_lots')).toBe(0);
    expect(await count('SELECT count(*) AS n FROM account_week_counters')).toBe(0);
    expect(await goldOf(h)).toBe(100);
  });

  it('입력 오류: 금액을 보내거나 장수가 범위 밖이면 400, 같은 request_id 재전송은 같은 응답이고 이중 차감이 없다', async () => {
    const h = await mk(app, 100_000);
    expect((await post(app, h, '/sweep/tickets/buy', { count: 1, price: 1 })).status).toBe(400);
    expect((await buy(h, 0)).status).toBe(400);
    expect((await buy(h, 8)).status).toBe(400);
    const rid = randomUUID();
    const a = await buy(h, 2, rid);
    const b = await buy(h, 2, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await goldOf(h)).toBe(100_000 - 2000);
    expect(await ticketTotal(h, fixed)).toBe(2);
    expect((await buy(h, 3, rid)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });
});

describe('C2~C3 주 7장 한도', () => {
  it('3장 + 4장 성공, 8번째는 WEEKLY_LIMIT, 한 번에 남은 한도를 넘으면 전체 거절, 목요일 06:00 뒤 다시 7장', async () => {
    const h = await mk(app, 1_000_000);
    expect((await buy(h, 3)).status).toBe(200);
    const over = await buy(h, 5);
    expect(over.status).toBe(422);
    expect(over.body.errors).toMatchObject({ code: 'WEEKLY_LIMIT', limit: 7, used: 3 });
    expect(await ticketTotal(h, fixed)).toBe(3);
    expect((await buy(h, 4)).status).toBe(200);
    const eighth = await buy(h, 1);
    expect(eighth.body.errors).toMatchObject({ code: 'WEEKLY_LIMIT', limit: 7, used: 7 });
    expect(await ticketTotal(h, fixed)).toBe(7);
    fixed = new Date(NEXT_WEEK.getTime() + 1000);
    expect((await buy(h, 7)).status).toBe(200);
    expect(await counter(h, 'sweep_buy', NEXT_WEEK)).toBe(7);
    expect(await ticketTotal(h, fixed)).toBe(14);
    await expectTicketLedgerConsistent(h);
  });

  it('같은 계정 두 캐릭터가 합계 8장을 동시에 사려 하면 한 쪽만 성공한다', async () => {
    const a = await mk(app, 1_000_000);
    const b = await secondChar(app, a);
    await seedGold(b, 1_000_000);
    const [r1, r2] = await Promise.all([buy(a, 4), buy(b, 4)]);
    expect([r1.status, r2.status].sort()).toEqual([200, 422]);
    expect(await counter(a, 'sweep_buy')).toBe(4);
    expect(await ticketTotal(a, fixed)).toBe(4);
    // 남은 3장은 더 살 수 있고 4장은 못 산다
    expect((await buy(a, 4)).status).toBe(422);
    expect((await buy(a, 3)).status).toBe(200);
    await expectTicketLedgerConsistent(a);
  });
});

async function seedHold(h: Hero): Promise<void> {
  await getPool().query(
    `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence)
     VALUES ($1, $2, 'manual', 'active', '24h', now() - interval '1 day', now() + interval '1 day', '{"metric":"xp","value":1,"cap":1}'::jsonb)`,
    [await accountIdOf(h), h.dbId],
  );
}

describe('C4 경제 정지', () => {
  it('정지 중에는 구매·주간 수령이 403 ECONOMY_HOLD', async () => {
    const h = await mk(app, 100_000);
    await setCounter(h, 'direct_clear', 10);
    await seedHold(h);
    expect((await buy(h, 1)).body.errors.code).toBe('ECONOMY_HOLD');
    expect((await claimWeekly(h)).body.errors.code).toBe('ECONOMY_HOLD');
    expect(await goldOf(h)).toBe(100_000);
    expect(await count('SELECT count(*) AS n FROM sweep_ticket_lots')).toBe(0);
    expect(await counter(h, 'activity_claim')).toBe(0);
  });
});

describe('C5 T2 주간 활동', () => {
  it('9회 WEEKLY_NOT_READY, 10회 성공(3장, weekly_activity 원장), 재수령 WEEKLY_ALREADY_CLAIMED', async () => {
    const h = await newHero(app);
    await setCounter(h, 'direct_clear', 9);
    const no = await claimWeekly(h);
    expect(no.status).toBe(422);
    expect(no.body.errors).toMatchObject({ code: 'WEEKLY_NOT_READY', goal: 10, progress: 9 });
    expect((await get(app, h, '/sweep')).body.data.weekly_activity).toMatchObject({ progress: 9, claimable: false, claimed: false });
    await setCounter(h, 'direct_clear', 10);
    expect((await get(app, h, '/sweep')).body.data.weekly_activity).toMatchObject({ progress: 10, claimable: true });
    const rid = randomUUID();
    const ok = await claimWeekly(h, rid);
    expect(ok.status).toBe(200);
    expect(ok.body.data.claimed).toEqual({ tickets: 3 });
    expect(ok.body.data.tickets).toEqual({ total: 3, normal: 3, event: [] });
    expect(ok.body.data.weekly_activity).toMatchObject({ claimed: true, claimable: false });
    const accUuid = ((await getPool().query('SELECT uuid FROM accounts WHERE id = $1', [await accountIdOf(h)])).rows[0] as { uuid: string }).uuid;
    const l = await getPool().query("SELECT delta, ref FROM sweep_ticket_ledger WHERE reason = 'weekly_activity'");
    expect(l.rows).toEqual([{ delta: 3, ref: `weekly:${accUuid}:${WEEK_START.toISOString()}` }]);
    const again = await claimWeekly(h);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('WEEKLY_ALREADY_CLAIMED');
    // 같은 request_id 재전송은 같은 응답(이중 지급 없음)
    expect((await claimWeekly(h, rid)).body).toEqual(ok.body);
    expect(await ticketTotal(h, fixed)).toBe(3);
    // 다음 주에는 카운터가 새로 시작한다
    fixed = new Date(NEXT_WEEK.getTime() + 1000);
    expect((await claimWeekly(h)).body.errors.code).toBe('WEEKLY_NOT_READY');
  });

  it('동시 두 요청 중 하나만 성공한다', async () => {
    const h = await newHero(app);
    await setCounter(h, 'direct_clear', 10);
    const [a, b] = await Promise.all([claimWeekly(h), claimWeekly(h)]);
    expect([a.status, b.status].sort()).toEqual([200, 409]);
    expect(await ticketTotal(h, fixed)).toBe(3);
    await expectTicketLedgerConsistent(h);
  });

  it('두 캐릭터의 직접 클리어를 계정 합산으로 센다(한 캐릭터만 받을 수 있고 부캐로 곱해지지 않는다)', async () => {
    const a = await newHero(app);
    const b = await secondChar(app, a);
    await seedLevel(a, 10);
    await seedLevel(b, 10);
    const run = async (h: Hero) => {
      const r = await post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 });
      return r.body.data.run.id as string;
    };
    expect(await run(a)).toBeTruthy();
    expect((await get(app, b, '/sweep')).body.data.weekly_activity.progress).toBe(0); // 입장만으로는 세지 않는다
    await setCounter(a, 'direct_clear', 6);
    await setCounter(b, 'direct_clear', 10); // 같은 계정이라 같은 행(덮어씀)
    expect((await get(app, a, '/sweep')).body.data.weekly_activity.progress).toBe(10);
    expect((await claimWeekly(a)).status).toBe(200);
    expect((await claimWeekly(b)).body.errors.code).toBe('WEEKLY_ALREADY_CLAIMED');
  });
});

// ---------- 직접 클리어가 주간 카운터를 올리는지(E3): 실제 던전 한 판 ----------

const ROOMS: { map: string; kills: [string, number][] }[] = [
  { map: 'dgn_canyon_1', kills: [['skel_gold', 3], ['skel_warrior', 2]] },
  { map: 'dgn_canyon_2', kills: [['skel_gold', 3], ['skel_miner', 2]] },
  { map: 'dgn_canyon_3', kills: [['skel_gold', 4], ['skel_warrior', 2]] },
  { map: 'dgn_canyon_boss', kills: [['boss_gold_foreman', 1], ['skel_gold', 2]] },
];

describe('C6 직접 클리어 +1(E3), 소탕은 세지 않는다', () => {
  it('솔로 요일 던전 클리어가 계정 direct_clear를 1 올리고, 클리어 시각의 주에 쌓인다', async () => {
    const h = await newHero(app);
    const run = await post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 });
    const runId = run.body.data.run.id as string;
    for (let room = 0; room < ROOMS.length; room++) {
      for (const [monster, n] of (ROOMS[room] as (typeof ROOMS)[number]).kills) {
        for (let i = 0; i < n; i++) {
          fixed = new Date(fixed.getTime() + 2000);
          const k = await post(app, h, '/kills', { map_id: (ROOMS[room] as { map: string }).map, monster_id: monster, run_id: runId, room_index: room });
          expect(k.status).toBe(200);
        }
      }
    }
    fixed = new Date(fixed.getTime() + 40_000);
    const res = await post(app, h, `/dungeon-runs/${runId}/result`, { outcome: 'cleared', stats: { elapsed_ms: 80_000, hits_taken: 2, max_combo: 20, revives_used: 0 } });
    expect(res.body.data.result).toBe('cleared');
    expect(await counter(h, 'direct_clear')).toBe(1);
    // 소탕은 세지 않는다
    await seedLevel(h, 10);
    await getPool().query("INSERT INTO sweep_ticket_lots (account_id, kind, granted, remaining) VALUES ($1, 'normal', 2, 2)", [await accountIdOf(h)]);
    await getPool().query(
      `INSERT INTO sweep_ticket_ledger (account_id, lot_id, character_id, delta, balance_after, reason, ref) SELECT account_id, id, NULL, 2, 2, 'shop_buy', 'seed' FROM sweep_ticket_lots`,
    );
    expect((await post(app, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 0 })).status).toBe(201);
    expect(await counter(h, 'direct_clear')).toBe(1);
  });
});

describe('주간 카운터 DB 제약', () => {
  it('activity_claim은 used=1만, 같은 (계정, 주, 종류)는 한 행', async () => {
    const h = await newHero(app);
    await expect(setCounter(h, 'activity_claim', 2)).rejects.toThrow();
    await setCounter(h, 'activity_claim', 1);
    await expect(
      getPool().query("INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES ($1, $2, 'activity_claim', 1)", [await accountIdOf(h), WEEK_START]),
    ).rejects.toThrow();
  });
});
