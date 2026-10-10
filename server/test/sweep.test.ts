// 10단계 14절 B: 던전 소탕(S1~S3). 클리어권 소모, 자격, 멱등성, 동시성, 부정 방지 연결, 해금·업적 불인정
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { getIncomeCaps } from '../src/gamedata/antiAbuseData';
import { getGameData } from '../src/gamedata/loader';
import { XP_REASONS } from '../src/domains/antiabuse/incomeMeter';
import { diffOf, type DiffNumbers } from '../src/domains/dungeons/dungeonRules';
import { sweepXp } from '../src/domains/sweep/sweepRules';
import { clearCounts, clearSummary } from '../src/domains/dungeons/dungeonRepository';
import { setMaintenance } from '../src/ops/maintenanceState';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { anomalyKinds, expectLedgerConsistent, fakeRng, get, newHero, post, seedClaims, seedLevel, type Hero } from './economyHelpers';
import { secondChar } from './auctionHelpers';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { formParty } from './partyHelpers';
import { MONDAY, accountIdOf, expectTicketLedgerConsistent, seedClear, seedTickets, sweepAll, sweepRows, sweepRun, sweepStatus, ticketTotal } from './sweepHelpers';
import request from 'supertest';

let app: Express;
let fixed = new Date(MONDAY);
const ON = { SWEEP_ENABLED: 'true' };

beforeEach(async () => {
  await resetDb();
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
  setMaintenance(null);
  app = buildApp(ON);
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  setMaintenance(null);
  await shutdown();
});

/** 소탕이 가능한 영웅: 레벨 10, 황금 광맥 일반 S등급 직접 클리어 기록, 클리어권 n장 */
async function prep(tickets = 3, level = 10): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, level);
  await seedClear(h, 'gold_vein', 0, 2);
  if (tickets > 0) await seedTickets(h, { normal: tickets });
  return h;
}

/** 오늘(월요일 06:00 KST 시작 구간)의 직접 입장 n회를 심는다 */
async function seedEntries(h: Hero, n: number): Promise<void> {
  for (let i = 0; i < n; i++) {
    await getPool().query(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, ended_at, counts_entry)
       VALUES ($1, 'gold_vein', 0, 'failed', '2026-10-04T21:00:00Z', now(), true)`,
      [h.dbId],
    );
  }
}

const count = async (sql: string, params: unknown[] = []): Promise<number> =>
  Number(((await getPool().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);

describe('플래그', () => {
  it('SWEEP_ENABLED가 꺼져 있으면(기본) 새 경로 전부 503 FEATURE_DISABLED이고 아무것도 바뀌지 않는다', async () => {
    const off = buildApp();
    const h = await prep(2);
    for (const r of [
      get(off, h, '/sweep'),
      post(off, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 0 }),
      post(off, h, '/sweep/run-all', { dungeon_id: 'gold_vein', difficulty: 0 }),
      post(off, h, '/sweep/tickets/buy', { count: 1 }),
      post(off, h, '/sweep/weekly/claim', {}),
    ]) {
      const res = await r;
      expect(res.status).toBe(503);
      expect(res.body.errors.code).toBe('FEATURE_DISABLED');
    }
    expect(await sweepRows(h)).toHaveLength(0);
    expect(await ticketTotal(h, fixed)).toBe(2);
  });
});

describe('B1 정상 소탕 S2', () => {
  it('201: 소탕 행, 클리어권 -1, 경험치·카드 원장, 입장 +1', async () => {
    const h = await prep(3);
    const res = await sweepRun(app, h);
    expect(res.status).toBe(201);
    const s = res.body.data.sweeps[0];
    expect(s).toMatchObject({ dungeon_id: 'gold_vein', difficulty: 0, xp: 1102, ticket: 'normal', card: { item_key: 'gold', count: 300 } });
    expect(res.body.data.summary).toEqual({ count: 1, total_xp: 1102 });
    expect(res.body.data.entries).toEqual({ limit: 3, used: 1, left: 2 });
    expect(res.body.data.tickets).toEqual({ total: 2, normal: 2, event: [] });
    expect(res.body.data.delta.gold).toBe(400); // 시작 골드 100 + 카드 300

    const rows = await sweepRows(h);
    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ dungeon_id: 'gold_vein', xp_granted: 1102 });
    const use = await getPool().query("SELECT delta, ref, balance_after FROM sweep_ticket_ledger WHERE reason = 'sweep_use'");
    expect(use.rows).toEqual([{ delta: -1, ref: s.id, balance_after: 2 }]);
    expect(await count("SELECT count(*) AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_sweep' AND ref = $2", [h.dbId, s.id])).toBe(1);
    expect(await count("SELECT count(*) AS n FROM gold_ledger WHERE character_id = $1 AND reason = 'dungeon_card' AND delta = 300", [h.dbId])).toBe(1);
    // 소탕은 dungeon_runs를 만들지 않는다
    expect(await count("SELECT count(*) AS n FROM dungeon_runs WHERE character_id = $1", [h.dbId])).toBe(1);
    await expectLedgerConsistent(h);
    await expectTicketLedgerConsistent(h);
  });

  it('입력 오류: 경험치·횟수를 보내거나 난이도가 범위 밖이면 400(받지 않는 값)', async () => {
    const h = await prep();
    expect((await post(app, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 0, xp: 99999 })).status).toBe(400);
    expect((await post(app, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 0, count: 5 })).status).toBe(400);
    expect((await post(app, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 4 })).status).toBe(400);
    expect((await post(app, h, '/sweep/run', { difficulty: 0 })).status).toBe(400);
    expect((await post(app, h, '/sweep/run-all', { dungeon_id: 'gold_vein', difficulty: 0, tickets: 9 })).status).toBe(400);
    expect(await sweepRows(h)).toHaveLength(0);
  });

  it('S1 현황: 던전별 가능 여부, 예상 경험치, 클리어권·입장·구매 한도', async () => {
    const h = await prep(2);
    await seedTickets(h, { events: [{ n: 1, expiresAt: new Date(fixed.getTime() + 5 * 86_400_000) }] });
    const res = await sweepStatus(app, h);
    expect(res.status).toBe(200);
    const d = res.body.data;
    expect(d.tickets.total).toBe(3);
    expect(d.tickets.normal).toBe(2);
    expect(d.tickets.event).toEqual([{ count: 1, expires_at: new Date(fixed.getTime() + 5 * 86_400_000).toISOString() }]);
    expect(d.entries).toEqual({ limit: 3, used: 0, left: 3 });
    expect(d.shop).toMatchObject({ weekly_limit: 7, weekly_used: 0, weekly_left: 7, unit_price: 2000 }); // 레벨 10 -> 단계 1
    expect(d.weekly_activity).toMatchObject({ goal: 10, progress: 0, claimable: false, claimed: false });
    expect(d.hold).toBe(false);
    const gold = d.dungeons.find((x: { id: string }) => x.id === 'gold_vein');
    expect(gold.open_today).toBe(true);
    expect(gold.difficulties[0]).toEqual({ difficulty: 0, can_sweep: true, block: null, best_rank: 2, need_level: 5, xp: 1102 });
    expect(gold.difficulties[1]).toMatchObject({ can_sweep: false, block: 'NOT_CLEARED' });
    expect(d.dungeons.find((x: { id: string }) => x.id === 'smelter').difficulties[0].block).toBe('CLOSED_TODAY');
    expect(d.dungeons.some((x: { id: string }) => x.id.startsWith('raid_'))).toBe(false);
  });
});

describe('B2 모두 소탕 S3', () => {
  it('n = min(남은 입장, 클리어권): 클리어권 5장 입장 2회 남음이면 2회(limited_by entries)', async () => {
    const h = await prep(5);
    await seedEntries(h, 1);
    const res = await sweepAll(app, h);
    expect(res.status).toBe(201);
    expect(res.body.data.sweeps).toHaveLength(2);
    expect(res.body.data.summary).toEqual({ count: 2, total_xp: 2204 });
    expect(res.body.data.limited_by).toBe('entries');
    expect(res.body.data.entries).toEqual({ limit: 3, used: 3, left: 0 });
    expect(res.body.data.tickets.total).toBe(3);
    expect(await sweepRows(h)).toHaveLength(2);
    await expectLedgerConsistent(h);
    await expectTicketLedgerConsistent(h);
  });

  it('클리어권 1장 입장 3회면 1회와 limited_by tickets, 클리어권 0장이면 NO_TICKET', async () => {
    const h = await prep(1);
    const res = await sweepAll(app, h);
    expect(res.status).toBe(201);
    expect(res.body.data.sweeps).toHaveLength(1);
    expect(res.body.data.limited_by).toBe('tickets');
    const none = await sweepAll(app, h);
    expect(none.status).toBe(422);
    expect(none.body.errors.code).toBe('NO_TICKET');
  });

  it('전부 아니면 없음: 두 번째 소탕 중 오류가 나면 첫 번째도 남지 않는다', async () => {
    const h = await prep(3);
    let calls = 0;
    setRng({
      unit: () => 1,
      int: (min) => {
        calls++;
        if (calls >= 3) throw new Error('inject');
        return min;
      },
    });
    const res = await sweepAll(app, h);
    expect(res.status).toBe(500);
    setRng(fakeRng({ unit: 1 }));
    expect(await sweepRows(h)).toHaveLength(0);
    expect(await ticketTotal(h, fixed)).toBe(3);
    expect(await count("SELECT count(*) AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_sweep'", [h.dbId])).toBe(0);
    expect(await count("SELECT count(*) AS n FROM sweep_ticket_ledger WHERE reason = 'sweep_use'")).toBe(0);
    expect(await count("SELECT count(*) AS n FROM request_log WHERE endpoint LIKE '%sweep%'")).toBe(0);
  });
});

describe('B3 멱등성', () => {
  it('같은 request_id 재전송은 같은 응답이고 원장은 늘지 않는다, 다른 본문은 IDEMPOTENCY_MISMATCH', async () => {
    const h = await prep(3);
    const rid = randomUUID();
    const a = await sweepRun(app, h, 'gold_vein', 0, rid);
    const b = await sweepRun(app, h, 'gold_vein', 0, rid);
    expect(a.status).toBe(201);
    expect(b.status).toBe(201);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await sweepRows(h)).toHaveLength(1);
    expect(await ticketTotal(h, fixed)).toBe(2);
    expect(await count("SELECT count(*) AS n FROM sweep_ticket_ledger WHERE reason = 'sweep_use'")).toBe(1);
    const other = await sweepRun(app, h, 'gold_vein', 1, rid);
    expect(other.status).toBe(422);
    expect(other.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });
});

describe('B4 동시성', () => {
  it('같은 계정 두 캐릭터가 마지막 클리어권 1장을 동시에 쓰면 하나만 성공한다', async () => {
    const a = await prep(1);
    const b = await secondChar(app, a);
    await seedLevel(b, 10);
    await seedClear(b, 'gold_vein', 0, 2);
    const [r1, r2] = await Promise.all([sweepRun(app, a), sweepRun(app, b)]);
    expect([r1.status, r2.status].sort()).toEqual([201, 422]);
    const lose = r1.status === 422 ? r1 : r2;
    expect(lose.body.errors.code).toBe('NO_TICKET');
    expect(await ticketTotal(a, fixed)).toBe(0);
    await expectTicketLedgerConsistent(a);
  });

  it('입장 1회가 남은 같은 캐릭터가 소탕 두 개를 동시에 보내면 하나만 성공한다', async () => {
    const h = await prep(5);
    await seedEntries(h, 2);
    const [r1, r2] = await Promise.all([sweepRun(app, h), sweepRun(app, h)]);
    expect([r1.status, r2.status].sort()).toEqual([201, 422]);
    const lose = r1.status === 422 ? r1 : r2;
    expect(lose.body.errors.code).toBe('NO_ENTRIES_LEFT');
    expect(await sweepRows(h)).toHaveLength(1);
  });
});

describe('B5 소모 순서와 만료', () => {
  it('만료가 가까운 이벤트권 -> 먼 이벤트권 -> 일반권 순으로 쓴다', async () => {
    const h = await prep(0);
    const day = 86_400_000;
    await seedTickets(h, { normal: 1, events: [{ n: 1, expiresAt: new Date(fixed.getTime() + 10 * day) }, { n: 1, expiresAt: new Date(fixed.getTime() + 3 * day) }] });
    const res = await sweepAll(app, h);
    expect(res.body.data.sweeps.map((s: { ticket: string }) => s.ticket)).toEqual(['event', 'event', 'normal']);
    const used = await getPool().query(
      "SELECT l.kind, l.expires_at FROM sweep_ticket_ledger e JOIN sweep_ticket_lots l ON l.id = e.lot_id WHERE e.reason = 'sweep_use' ORDER BY e.id",
    );
    expect((used.rows[0] as { expires_at: Date }).expires_at.getTime()).toBe(fixed.getTime() + 3 * day);
    expect((used.rows[1] as { expires_at: Date }).expires_at.getTime()).toBe(fixed.getTime() + 10 * day);
    expect((used.rows[2] as { kind: string }).kind).toBe('normal');
    await expectTicketLedgerConsistent(h);
  });

  it('기한이 지난 로트는 만료 작업 전이라도 쓸 수 없다', async () => {
    const h = await prep(0);
    await seedTickets(h, { events: [{ n: 5, expiresAt: new Date(fixed.getTime() - 1000) }] });
    const res = await sweepRun(app, h);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('NO_TICKET');
    expect((await sweepStatus(app, h)).body.data.tickets).toEqual({ total: 0, normal: 0, event: [] });
  });
});

describe('B6~B7 자격', () => {
  it('미클리어, 등급 미달(C), 등급 B 통과, 레벨 부족(권장-1, 직접 입장은 여유로 되는 상황), 레이드, 닫힌 요일', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    await seedTickets(h, { normal: 10 });
    const no = await sweepRun(app, h);
    expect(no.body.errors.code).toBe('SWEEP_NOT_CLEARED');
    await seedClear(h, 'gold_vein', 0, 5); // C
    const low = await sweepRun(app, h);
    expect(low.status).toBe(422);
    expect(low.body.errors).toMatchObject({ code: 'SWEEP_RANK_LOW', need_rank: 'B', best_rank: 'C' });
    await seedClear(h, 'gold_vein', 0, 4); // B
    expect((await sweepRun(app, h)).status).toBe(201);

    // 권장 레벨 5보다 1 낮은 캐릭터: 직접 입장은 PARTY_MIN_LEVEL_SLACK 여유로 통과하지만 소탕은 거절
    const g = await newHero(app);
    await seedLevel(g, 4);
    await seedClear(g, 'gold_vein', 0, 2);
    await seedTickets(g, { normal: 1 });
    const lv = await sweepRun(app, g);
    expect(lv.status).toBe(422);
    expect(lv.body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: 5, have: 4 });
    expect((await post(app, g, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 })).status).toBe(201);

    const raid = await sweepRun(app, h, 'raid_skeleton_king', 0);
    expect(raid.body.errors.code).toBe('SWEEP_NOT_ALLOWED');
    const closed = await sweepRun(app, h, 'smelter', 0);
    expect(closed.body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    expect((await sweepRun(app, h, 'nope', 0)).body.errors.code).toBe('DUNGEON_UNKNOWN');

    // 화면이 막은 조건을 우회한 요청(미클리어, 등급, 레벨, 레이드)은 sweep_denied로 남는다
    const kinds = await anomalyKinds(h);
    expect(kinds.filter((k) => k === 'sweep_denied')).toHaveLength(3); // 미클리어, 등급 미달, 레이드(g의 레벨 부족은 g에)
    expect((await anomalyKinds(g)).filter((k) => k === 'sweep_denied')).toHaveLength(1);
  });

  it('B7 보상이 잠긴 직접 클리어(LOW_CONTRIBUTION)만 있으면 소탕할 수 없다', async () => {
    const h = await newHero(app);
    await seedLevel(h, 10);
    await seedTickets(h, { normal: 2 });
    await seedClear(h, 'gold_vein', 0, 1, true);
    const res = await sweepRun(app, h);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('SWEEP_NOT_CLEARED');
    expect((await sweepStatus(app, h)).body.data.dungeons[0].difficulties[0].block).toBe('NOT_CLEARED');
  });

  it('난이도마다 따로: 일반 클리어로 모험 난이도는 소탕할 수 없다', async () => {
    const h = await prep(2, 15);
    expect((await sweepRun(app, h, 'gold_vein', 1)).body.errors.code).toBe('SWEEP_NOT_CLEARED');
  });
});

describe('B8 소탕은 해금·최고 랭크·업적·주간 활동에 반영되지 않는다', () => {
  it('목록의 unlocked/cleared/best_rank가 그대로, dungeon_runs·주간 카운터 없음', async () => {
    const h = await prep(3, 15);
    const before = (await get(app, h, '/dungeons')).body.data.dungeons;
    await sweepRun(app, h);
    await sweepRun(app, h);
    const after = (await get(app, h, '/dungeons')).body.data;
    expect(after.dungeons).toEqual(before);
    expect(after.entries.used).toBe(2); // 입장 횟수는 공유한다
    const gold = after.dungeons.find((x: { id: string }) => x.id === 'gold_vein');
    expect(gold.difficulties[1].unlocked).toBe(true); // 시드한 직접 클리어(일반)로 이미 열려 있다
    expect(gold.difficulties[2].unlocked).toBe(false); // 소탕이 모험을 클리어한 것으로 치지 않는다
    expect(await count("SELECT count(*) AS n FROM dungeon_runs WHERE character_id = $1 AND state = 'cleared'", [h.dbId])).toBe(1);
    expect(await count('SELECT count(*) AS n FROM account_week_counters')).toBe(0);
  });
});

const MAIN_TO_STRONGER = [
  'c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise', 'c1_rebuild', 'c1_trail',
  'c1s_patrol', 'c1s_ruins', 'c1s_firstdungeon', 'c1s_depths', 'c1_crossing', 'c2s_trader', 'c2s_trader_guard',
  'career_path', 'c2s_trader_ore', 'c1_quarry', 'c1_scout',
];

describe('B9 퀘스트 "던전 클리어 N회"', () => {
  it('직접 클리어 기록 없이 소탕 1회만으로 c1_stronger(던전 클리어 1회)를 청구할 수 있다', async () => {
    const h = await newHero(app);
    // 던전 목표는 받은 퀘스트끼리 누적되므로 1-10 요일 던전(1회)은 빼고 c1_stronger 몫 1회만 보게 한다
    await seedClaims(h, MAIN_TO_STRONGER.filter((q) => q !== 'c1s_firstdungeon'));
    await seedLevel(h, 20);
    await seedTickets(h, { normal: 1 });
    const claim = () => post(app, h, '/quests/c1_stronger/claim', {}, randomUUID());
    expect((await claim()).body.errors?.code).toBe('QUEST_NOT_DONE');
    await getPool().query(
      `INSERT INTO dungeon_sweeps (character_id, dungeon_id, difficulty, reset_day, lot_id, xp_granted, card, request_id)
       SELECT $1, 'gold_vein', 0, now(), l.id, 0, '{"item_key":"gold","count":1}'::jsonb, gen_random_uuid() FROM sweep_ticket_lots l LIMIT 1`,
      [h.dbId],
    );
    expect((await claim()).status).toBe(200);
  });

  it('clearCounts는 직접 클리어에 소탕을 더하고(total, 던전별), clearSummary(해금)는 소탕을 읽지 않는다', async () => {
    const h = await prep(3);
    expect((await sweepRun(app, h)).status).toBe(201);
    expect((await sweepRun(app, h)).status).toBe(201);
    const c = await clearCounts(getPool(), h.dbId);
    expect(c.total).toBe(3); // 직접 1(시드) + 소탕 2
    expect(c.byDungeon.get('gold_vein')).toBe(3);
    expect(await clearSummary(getPool(), h.dbId)).toEqual([{ dungeon_id: 'gold_vein', difficulty: 0, best_rank: 2 }]);
  });
});

describe('B10 입장 횟수 공유', () => {
  it('직접 2회 + 소탕 1회 후 직접 입장은 NO_ENTRIES_LEFT, 06:00 경계 뒤에는 다시 3회', async () => {
    const h = await prep(5);
    await seedEntries(h, 2);
    expect((await sweepRun(app, h)).status).toBe(201);
    const enter = await post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 });
    expect(enter.body.errors.code).toBe('NO_ENTRIES_LEFT');
    expect((await sweepRun(app, h)).body.errors.code).toBe('NO_ENTRIES_LEFT');
    // 다음 날 06:00 KST 직후
    fixed = new Date('2026-10-05T21:00:01Z');
    const next = await sweepStatus(app, h);
    expect(next.body.data.entries).toEqual({ limit: 3, used: 0, left: 3 });
  });

  it('소탕만 3회 하면 직접 입장도 거절', async () => {
    const h = await prep(5);
    for (let i = 0; i < 3; i++) expect((await sweepRun(app, h)).status).toBe(201);
    expect((await post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 })).body.errors.code).toBe('NO_ENTRIES_LEFT');
  });
});

describe('B11 진행 중인 판', () => {
  it('진행 중인 판이 있으면 RUN_ACTIVE', async () => {
    const h = await prep(3);
    expect((await post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 })).status).toBe(201);
    const res = await sweepRun(app, h);
    expect(res.status).toBe(409);
    expect(res.body.errors.code).toBe('RUN_ACTIVE');
  });

  it('파티 판에 참여 중(아직 입장 전)이면 IN_PARTY_RUN', async () => {
    const member = await prep(3);
    const host = await newHero(app);
    await formParty(app, host, [member]);
    expect((await post(app, host, '/party/start', { ai_count: 0 })).status).toBe(201);
    const res = await sweepRun(app, member);
    expect(res.status).toBe(409);
    expect(res.body.errors.code).toBe('IN_PARTY_RUN');
    expect(await sweepRows(member)).toHaveLength(0);
  });
});

describe('B12 부정 방지: 경제 정지, 프레즌스', () => {
  it('경제 정지 중에는 403 ECONOMY_HOLD이고 어떤 행도 생기지 않는다', async () => {
    const h = await prep(3);
    await getPool().query(
      `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence)
       VALUES ($1, $2, 'manual', 'active', '24h', now() - interval '1 day', now() + interval '1 day', '{"metric":"xp","value":1,"cap":1}'::jsonb)`,
      [await accountIdOf(h), h.dbId],
    );
    const res = await sweepRun(app, h);
    expect(res.status).toBe(403);
    expect(res.body.errors.code).toBe('ECONOMY_HOLD');
    expect(await sweepRows(h)).toHaveLength(0);
    expect(await ticketTotal(h, fixed)).toBe(3);
    expect((await sweepStatus(app, h)).body.data.hold).toBe(true);
  });

  it('PRESENCE_KILL_MODE=enforce에서 프레즌스가 없으면 409 PRESENCE_REQUIRED, 신호를 보낸 뒤 같은 request_id로 성공한다', async () => {
    const enf = buildApp({ ...ON, PRESENCE_KILL_MODE: 'enforce' });
    const h = await prep(3);
    const rid = randomUUID();
    const no = await post(enf, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 0 }, rid);
    expect(no.status).toBe(409);
    expect(no.body.errors.code).toBe('PRESENCE_REQUIRED');
    expect((await request(enf).post(`/characters/${h.id}/presence`).set(auth(h.s)).send({ map_id: 'village', auto_play: false, input_recent: true })).status).toBe(200);
    const ok = await post(enf, h, '/sweep/run', { dungeon_id: 'gold_vein', difficulty: 0 }, rid);
    expect(ok.status).toBe(201);
  });

  it('log 모드(기본)는 프레즌스가 없어도 통과한다', async () => {
    const h = await prep(1);
    expect((await sweepRun(app, h)).status).toBe(201);
  });
});

describe('B13 만렙', () => {
  it('경험치 0, 카드는 지급, xp_ledger 행 없음', async () => {
    const h = await prep(1, getGameData().economy.progression.maxLevel);
    const res = await sweepRun(app, h);
    expect(res.status).toBe(201);
    expect(res.body.data.sweeps[0].xp).toBe(0);
    expect(res.body.data.sweeps[0].card.count).toBe(300);
    expect(await count("SELECT count(*) AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_sweep'", [h.dbId])).toBe(0);
  });
});

describe('B14 소득 정합', () => {
  it('XP_REASONS에 dungeon_sweep이 있고 income_hourly.xp에 쌓인다', async () => {
    expect(XP_REASONS.has('dungeon_sweep')).toBe(true);
    const h = await prep(2);
    await sweepRun(app, h);
    const r = await getPool().query<{ xp: string }>('SELECT sum(xp) AS xp FROM income_hourly WHERE character_id = $1', [h.dbId]);
    expect(Number((r.rows[0] as { xp: string }).xp)).toBe(1102);
  });

  it('하루 3회 소탕 경험치가 모든 던전·난이도에서 income_caps의 일일 던전 덩어리 안이다', () => {
    const caps = getIncomeCaps();
    expect(caps).not.toBeNull();
    const eco = getGameData().economy;
    const sweep = getGameData().sweep as NonNullable<ReturnType<typeof getGameData>['sweep']>;
    for (const d of eco.dungeons.byId.values()) {
      if (d.isRaid) continue;
      eco.dungeons.difficulties.forEach((_x, i) => {
        const diff = diffOf(eco, d, i) as DiffNumbers;
        const band = (caps?.bands ?? []).find((b) => b.minLevel <= diff.recommendedLevel && diff.recommendedLevel <= b.maxLevel);
        expect(band).toBeDefined();
        expect(3 * sweepXp(d, diff, i, sweep)).toBeLessThanOrEqual(band?.perDay.dungeonXp ?? 0);
      });
    }
  });
});

describe('B15 점검', () => {
  it('점검 직전(새 판 차단) 구간에는 소탕도 막힌다', async () => {
    const h = await prep(2);
    setMaintenance({ uuid: randomUUID(), notice: '점검', blockLoginAt: new Date(fixed.getTime() - 60_000), startsAt: new Date(fixed.getTime() + 30 * 60_000), endsAt: new Date(fixed.getTime() + 60 * 60_000) });
    for (const p of ['/sweep/run', '/sweep/run-all']) {
      const res = await post(app, h, p, { dungeon_id: 'gold_vein', difficulty: 0 });
      expect(res.status).toBe(503);
      expect(res.body.errors.code).toBe('MAINTENANCE_PENDING');
    }
    // 현황 조회는 열려 있다
    expect((await sweepStatus(app, h)).status).toBe(200);
    expect(await sweepRows(h)).toHaveLength(0);
  });
});

describe('보호', () => {
  it('클리어권 키는 가방에 넣을 수 없다(E7)', async () => {
    const { EconCtx } = await import('../src/domains/economy/economyContext');
    const sweep = getGameData().sweep as NonNullable<ReturnType<typeof getGameData>['sweep']>;
    const ctx = Object.create(EconCtx.prototype) as InstanceType<typeof EconCtx>;
    await expect(ctx.addItem('bag', sweep.ticketItem, 1, 'drop_claim', 'x')).rejects.toThrow(/클리어권/);
    await expect(ctx.addItem('bag', sweep.eventTicketItem, 1, 'drop_claim', 'x')).rejects.toThrow(/클리어권/);
  });

  it('남의 캐릭터 uuid는 404', async () => {
    const a = await prep(1);
    const b = await newHero(app);
    const res = await request(app).post(`/characters/${a.id}/sweep/run`).set(auth(b.s)).send({ request_id: randomUUID(), dungeon_id: 'gold_vein', difficulty: 0 });
    expect(res.status).toBe(404);
  });
});
