// 13단계 레이드 보상 통합 시험(8~18): 확정 골드, 카드 4장 모두 받기, 자동 지급 틱, 파티 정산, 보류 해제, 옛 판 호환, 속도 감시
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { checkCharacter } from '../src/domains/antiabuse/holds';
import { runCardAutoPickTick } from '../src/domains/dungeons/dungeonService';
import { raidPeriod } from '../src/domains/dungeons/entryRules';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, fakeRng, goldOf, seedClaims, seedLevel } from './economyHelpers';
import { clearAll, formParty, get, honestHostReport, newHero, post, startAndBegin, stats, type Hero } from './partyHelpers';
import { adminApp, adminPost, makeAdmin, rid, type TestAdmin } from './opsHelpers';

const app = buildApp();
const admin = adminApp();
const WED = '2026-10-07T03:00:00Z'; // 수요일 12:00 KST: 해골왕 열림
let fixed = new Date(WED);
const at = (iso: string) => {
  fixed = new Date(iso);
};
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};
const KING = 'raid_skeleton_king';
const king = () => getGameData().economy.dungeons.byId.get(KING) as { raidReward?: { goldMin: number; goldMax: number }; raidTier: string };

beforeEach(async () => {
  await resetDb();
  at(WED);
  setClockOverride(() => fixed);
  // int가 항상 min이라 골드는 goldMin, 카드는 표의 첫 줄(장비: 부위 0, 에픽)이 된다
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const raidHero = async (level = 20): Promise<Hero> => {
  const h = await newHero(app);
  await seedLevel(h, level);
  await seedClaims(h, ['c1_fortress']);
  return h;
};
const result = (h: Hero, runUuid: string, body: Record<string, unknown>, requestId?: string) => post(app, h, `/dungeon-runs/${runUuid}/result`, body, requestId);

/** 레이드 파티 판을 만들어 모두 클리어하고 방장 보고까지 보낸다 */
async function raidParty(heroes: Hero[]) {
  const [host, ...members] = heroes as [Hero, ...Hero[]];
  await formParty(app, host, members, { dungeon_id: KING, difficulty: 0 });
  const { runId, runs } = await startAndBegin(app, host, members);
  const t0 = fixed.getTime();
  for (const h of heroes) {
    at(new Date(t0 + 1).toISOString());
    await clearAll(app, h, runs.get(h.id) as string, advance, KING, 20);
  }
  at(new Date(t0 + 300_000).toISOString());
  const rep = await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(runs.get(host.id) as string, heroes, { elapsed_ms: 300_000 }, KING));
  if (rep.status !== 200) throw new Error(`host-report ${rep.status} ${JSON.stringify(rep.body)}`);
  const body = { outcome: 'cleared', stats: stats({ elapsed_ms: 300_000, max_combo: 300 }) };
  return { runs, body };
}

/** 이미 이번 기간의 보상을 받은 것으로 만든다(입장 때 ALREADY_CLAIMED로 잠긴다) */
async function preClaim(h: Hero): Promise<void> {
  const run = await getPool().query<{ id: string }>(
    `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, ended_at)
     VALUES ($1, $2, 0, 'abandoned', now(), now()) RETURNING id`,
    [h.dbId, KING],
  );
  const p = raidPeriod(king() as never, fixed);
  await getPool().query(
    'INSERT INTO raid_claims (character_id, dungeon_id, period_kind, period_start, dungeon_run_id) VALUES ($1, $2, $3, $4, $5)',
    [h.dbId, KING, p.kind, p.start, (run.rows[0] as { id: string }).id],
  );
}

const CARDS = [
  { item_key: 'mat_raid_king', count: 4 },
  { item_key: 'mat_ore', count: 24 },
  { item_key: 'mat_raid_king', count: 2 },
  { item_key: 'ticket_protect', count: 1 },
];

/** 클리어가 끝난 레이드 판을 직접 만든다(카드 4장 고정). mode/taken/endedAt로 상태를 고른다 */
async function clearedRun(
  h: Hero,
  o: { mode?: 'pick_one' | 'take_all'; taken?: number; endedAgo?: string; cards?: unknown[]; state?: string } = {},
): Promise<string> {
  const taken = o.taken ?? 0;
  const r = await getPool().query<{ uuid: string }>(
    `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, state, reset_day, started_at, ended_at, rank, cards,
                               cards_mode, cards_taken, cards_taken_at, humans)
     VALUES ($1, $2, 0, 2, $3, date_trunc('day', now()), now() - interval '1 day', now() - $4::interval, 3, $5::jsonb,
             $6, $7::smallint, CASE WHEN $7::smallint > 0 THEN now() ELSE NULL END, 2)
     RETURNING uuid`,
    [h.dbId, KING, o.state ?? 'cleared', o.endedAgo ?? '1 minute', JSON.stringify(o.cards ?? CARDS), o.mode ?? 'take_all', taken],
  );
  return (r.rows[0] as { uuid: string }).uuid;
}
const pick = (h: Hero, run: string, index: number, requestId?: string) => post(app, h, `/dungeon-runs/${run}/cards/pick`, { index }, requestId);
const cardLedger = async (h: Hero) =>
  (await getPool().query<{ ref: string; item_key: string; delta: number }>("SELECT ref, item_key, delta FROM item_ledger WHERE character_id = $1 AND reason = 'dungeon_card' ORDER BY id", [h.dbId])).rows;
const runRow = async (uuid: string) =>
  (await getPool().query<{ cards_mode: string; cards_taken: number; raid_gold: number; state: string }>('SELECT cards_mode, cards_taken, raid_gold, state FROM dungeon_runs WHERE uuid = $1', [uuid])).rows[0] as {
    cards_mode: string;
    cards_taken: number;
    raid_gold: number;
    state: string;
  };

describe('8, 9, 15. 파티 정산: 확정 골드, 카드, 잠금', () => {
  it('두 사람이 각자 골드와 카드 4장을 받고, 이미 받은 사람(ALREADY_CLAIMED)은 골드도 카드도 없다. 재호출은 gold_gain/card_mode를 돌려준다', async () => {
    const host = await raidHero();
    const member = await raidHero();
    const claimed = await raidHero();
    await preClaim(claimed);
    const { runs, body } = await raidParty([host, member, claimed]);
    const goldBefore = await goldOf(member);
    expect((await result(host, runs.get(host.id) as string, body)).body.data.result).toBe('pending');
    const rm = await result(member, runs.get(member.id) as string, body);
    const lo = (king().raidReward as { goldMin: number }).goldMin;
    const hi = (king().raidReward as { goldMax: number }).goldMax;
    expect(rm.body.data.card_count).toBe(4);
    expect(rm.body.data.raid).toMatchObject({ reward_locked: false, card_mode: 'take_all' });
    expect(rm.body.data.raid.gold_gain).toBeGreaterThanOrEqual(lo);
    expect(rm.body.data.raid.gold_gain).toBeLessThanOrEqual(hi);
    expect(rm.body.data.cards).toBeUndefined(); // 카드 내용은 뒤집기 전에 주지 않는다
    expect(JSON.stringify(rm.body.data)).not.toContain('eq_sword');
    const gain = rm.body.data.raid.gold_gain as number;
    expect((await goldOf(member)) - goldBefore).toBe(gain);

    // 원장 한 행, 판 행과 같은 값, take_all
    const led = await getPool().query("SELECT delta, ref FROM gold_ledger WHERE character_id = $1 AND reason = 'raid_gold'", [member.dbId]);
    expect(led.rows).toEqual([{ delta: String(gain), ref: runs.get(member.id) }]);
    expect(await runRow(runs.get(member.id) as string)).toMatchObject({ cards_mode: 'take_all', cards_taken: 0, raid_gold: gain, state: 'cleared' });

    // 방장도 정산하면 같은 규칙. 이미 받은 사람은 골드와 카드가 없다
    const rh = await post(app, host, `/dungeon-runs/${runs.get(host.id)}/settle`, {});
    expect(rh.body.data.raid.gold_gain).toBe(gain);
    const rc = await result(claimed, runs.get(claimed.id) as string, body);
    expect(rc.body.data).toMatchObject({ granted_xp: 0, card_count: 0 });
    expect(rc.body.data.raid).toEqual({ reward_locked: true, lock_reason: 'ALREADY_CLAIMED' });
    expect((await getPool().query("SELECT 1 FROM gold_ledger WHERE character_id = $1 AND reason = 'raid_gold'", [claimed.dbId])).rowCount).toBe(0);
    expect(await runRow(runs.get(claimed.id) as string)).toMatchObject({ raid_gold: 0, cards_mode: 'pick_one' });

    // 이미 정산된 판에 다시 settle: 저장된 gold_gain, card_mode
    const again = await post(app, member, `/dungeon-runs/${runs.get(member.id)}/settle`, {});
    expect(again.body.data.raid).toMatchObject({ reward_locked: false, gold_gain: gain, card_mode: 'take_all' });
    expect(again.body.data.card_count).toBe(4);
    const lockedAgain = await post(app, claimed, `/dungeon-runs/${runs.get(claimed.id)}/settle`, {});
    expect(lockedAgain.body.data.raid).toEqual({ reward_locked: true, lock_reason: 'ALREADY_CLAIMED' });

    // 조회 응답
    const run = await get(app, member, `/dungeon-runs/${runs.get(member.id)}`);
    expect(run.body.data.run).toMatchObject({ card_mode: 'take_all', raid_gold: gain, taken: [], card_count: 4 });
    // 접속 때 목록: 4장 모두 대기
    const list = await get(app, member, '/dungeons');
    expect(list.body.data.unpicked_runs).toEqual([expect.objectContaining({ run_id: runs.get(member.id), card_mode: 'take_all', remaining: 4 })]);

    // 뒤집기까지 이어서: fakeRng라 카드 4장이 모두 같은 장비다
    const flip = await pick(member, runs.get(member.id) as string, 3);
    expect(flip.status).toBe(200);
    expect(flip.body.data.remaining).toBe(3);
    await expectLedgerConsistent(member);
    await expectLedgerConsistent(claimed);
  });
});

describe('10~13. 카드 뒤집기', () => {
  it('한 장 뒤집기: 원장 한 행, 비트, 응답 배열. 같은 request_id 재전송은 같은 본문, 새 request_id의 같은 번호는 409', async () => {
    const h = await raidHero();
    const run = await clearedRun(h);
    const r = randomUUID();
    const a = await pick(h, run, 2, r);
    expect(a.status).toBe(200);
    expect(a.body.data).toMatchObject({ index: 2, card: { item_key: 'mat_raid_king', count: 2 }, taken: [2], remaining: 3 });
    expect(a.body.data.cards).toEqual([null, null, { item_key: 'mat_raid_king', count: 2 }, null]);
    expect(await runRow(run)).toMatchObject({ cards_taken: 4 });
    expect(await cardLedger(h)).toEqual([{ ref: `${run}:2`, item_key: 'mat_raid_king', delta: 2 }]);
    expect(await countOf(h, 'mat_raid_king')).toBe(2);

    const replay = await pick(h, run, 2, r);
    expect(replay.status).toBe(200);
    expect(replay.body).toEqual(a.body);
    expect((await cardLedger(h)).length).toBe(1);
    expect(await countOf(h, 'mat_raid_king')).toBe(2);

    const dup = await pick(h, run, 2);
    expect(dup.status).toBe(409);
    expect(dup.body.errors).toMatchObject({ code: 'CARD_ALREADY_FLIPPED', index: 2, card: { item_key: 'mat_raid_king', count: 2 }, taken: [2] });
    expect((await cardLedger(h)).length).toBe(1);
  });

  it('4장을 섞인 순서(3, 0, 2, 1)로 모두 받으면 cards_taken=15, 원장 4행, 가방 합계가 맞고 대기 목록에서 빠진다', async () => {
    const h = await raidHero();
    const run = await clearedRun(h);
    for (const i of [3, 0, 2, 1]) expect((await pick(h, run, i)).status).toBe(200);
    expect(await runRow(run)).toMatchObject({ cards_taken: 15 });
    expect((await cardLedger(h)).length).toBe(4);
    expect(await countOf(h, 'mat_raid_king')).toBe(6);
    expect(await countOf(h, 'mat_ore')).toBe(24);
    expect(await countOf(h, 'ticket_protect')).toBe(1);
    expect((await get(app, h, '/dungeons')).body.data.unpicked_runs).toEqual([]);
    expect((await get(app, h, `/dungeon-runs/${run}`)).body.data.run.taken).toHaveLength(4);
    await expectLedgerConsistent(h);
  });

  it('동시성: 같은 카드를 다른 request_id로 동시에 보내면 200 하나와 409 하나, 원장 한 행', async () => {
    const h = await raidHero();
    const run = await clearedRun(h);
    const rs = await Promise.all([pick(h, run, 1), pick(h, run, 1)]);
    expect(rs.map((x) => x.status).sort()).toEqual([200, 409]);
    expect((await cardLedger(h)).length).toBe(1);
    expect(await countOf(h, 'mat_ore')).toBe(24);
  });

  it('오류: index=4는 400, 다른 캐릭터의 판은 404, 클리어 아닌 판은 422 NO_CARDS, 유효 기간 지난 판은 410', async () => {
    const h = await raidHero();
    const other = await raidHero();
    const run = await clearedRun(h);
    expect((await pick(h, run, 4)).status).toBe(400);
    expect((await post(app, h, `/dungeon-runs/${run}/cards/pick`, { index: 0, extra: 1 })).status).toBe(400);
    expect((await pick(other, run, 0)).status).toBe(404);
    const notCleared = await clearedRun(h, { state: 'abandoned' });
    const nc = await pick(h, notCleared, 0);
    expect(nc.status).toBe(422);
    expect(nc.body.errors.code).toBe('NO_CARDS');
    const old = await clearedRun(h, { endedAgo: '30 days' });
    const exp = await pick(h, old, 0);
    expect(exp.status).toBe(410);
    expect(exp.body.errors.code).toBe('CARDS_EXPIRED');
    expect((await cardLedger(h)).length).toBe(0);
  });
});

describe('14. 자동 지급 틱', () => {
  it('아무것도 안 받은 판은 4장 모두, 1번만 받은 판은 나머지 3장만, 두 번 돌려도 추가 지급이 없고, 이후 뒤집기는 409', async () => {
    setClockOverride(() => new Date()); // 판 시각을 DB의 now()로 넣으므로 시계도 실제 시각으로 맞춘다
    const a = await raidHero();
    const b = await raidHero();
    const fresh = await raidHero();
    const runA = await clearedRun(a, { endedAgo: '1 hour' });
    const runB = await clearedRun(b, { endedAgo: '1 hour' });
    const runFresh = await clearedRun(fresh, { endedAgo: '1 minute' });
    expect((await pick(b, runB, 1)).status).toBe(200);

    await runCardAutoPickTick();
    expect((await cardLedger(a)).map((x) => x.ref)).toEqual([0, 1, 2, 3].map((i) => `${runA}:${i}`));
    expect((await cardLedger(b)).map((x) => x.ref)).toEqual([1, 0, 2, 3].map((i) => `${runB}:${i}`)); // 1번은 두 번 안 준다
    expect(await runRow(runA)).toMatchObject({ cards_taken: 15 });
    expect(await runRow(runB)).toMatchObject({ cards_taken: 15 });
    expect(await runRow(runFresh)).toMatchObject({ cards_taken: 0 }); // 10분이 안 지난 판은 그대로

    await runCardAutoPickTick();
    expect((await cardLedger(a)).length).toBe(4);
    expect((await cardLedger(b)).length).toBe(4);
    expect(await countOf(a, 'mat_raid_king')).toBe(6);

    const late = await pick(a, runA, 0);
    expect(late.status).toBe(409);
    expect(late.body.errors).toMatchObject({ code: 'CARD_ALREADY_FLIPPED', card: { item_key: 'mat_raid_king', count: 4 } });
    await expectLedgerConsistent(a);
    await expectLedgerConsistent(b);
  });
});

describe('16. 보류 해제', () => {
  it('해제된 레이드 판은 골드가 나가고 unpicked_runs에 take_all, remaining=4로 보인다', async () => {
    const operator: TestAdmin = await makeAdmin('operator');
    const h = await raidHero();
    const held = await getPool().query<{ uuid: string }>(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, state, reset_day, started_at, ended_at, room_index, room_kills, stats,
                                 hold_reason, humans, ai_count, counts_entry)
       VALUES ($1, $2, 0, 2, 'held', date_trunc('day', now()), now() - interval '10 minutes', now() - interval '1 minute', 2,
               '{}'::jsonb, '{"elapsed_ms": 300000, "hits_taken": 1, "max_combo": 100, "revives_used": 0}'::jsonb, 'POWER', 2, 0, false)
       RETURNING uuid`,
      [h.dbId, KING],
    );
    const run = (held.rows[0] as { uuid: string }).uuid;
    const rel = await adminPost(admin, operator, `/admin/held-runs/${run}/release`, { note: '확인 후 해제' }, rid());
    expect(rel.status).toBe(200);
    expect(rel.body.data).toMatchObject({ result: 'cleared', card_count: 4 });
    const row = await runRow(run);
    expect(row).toMatchObject({ cards_mode: 'take_all', cards_taken: 0, state: 'cleared' });
    expect(row.raid_gold).toBeGreaterThanOrEqual((king().raidReward as { goldMin: number }).goldMin);
    expect(await goldOf(h)).toBeGreaterThanOrEqual(row.raid_gold);
    const list = await get(app, h, '/dungeons');
    expect(list.body.data.unpicked_runs).toEqual([expect.objectContaining({ run_id: run, card_mode: 'take_all', remaining: 4 })]);
  });
});

describe('17. 마이그레이션 호환', () => {
  it('0028 이전에 클리어된 레이드 판(pick_one)은 예전처럼 한 장 고르기이고 두 번째는 409 CARD_ALREADY_PICKED', async () => {
    const h = await raidHero();
    const cards = [{ item_key: 'gold', count: 1000 }, ...CARDS.slice(0, 3)];
    const run = await clearedRun(h, { mode: 'pick_one', cards });
    const a = await pick(h, run, 0);
    expect(a.status).toBe(200);
    expect(a.body.data.card).toEqual({ item_key: 'gold', count: 1000 });
    expect(a.body.data.cards).toHaveLength(4);
    expect(await goldOf(h)).toBeGreaterThanOrEqual(1000);
    const b = await pick(h, run, 1);
    expect(b.status).toBe(409);
    expect(b.body.errors.code).toBe('CARD_ALREADY_PICKED');
    expect(await runRow(run)).toMatchObject({ cards_mode: 'pick_one', cards_taken: 0 });
    await runCardAutoPickTick(); // 이미 고른 판은 틱이 건드리지 않는다
    expect((await cardLedger(h)).length).toBe(0);
  });
});

describe('18. 속도 감시', () => {
  it('해골왕 하루 1회 클리어는 감시 임계를 넘지 않고, raid_gold는 gold_acq 버킷에 잡힌다', async () => {
    const host = await raidHero();
    const member = await raidHero();
    const { runs, body } = await raidParty([host, member]);
    await result(host, runs.get(host.id) as string, body);
    const rm = await result(member, runs.get(member.id) as string, body);
    const gain = rm.body.data.raid.gold_gain as number;
    const sum = async (col: string) =>
      Number((await getPool().query<{ n: string }>(`SELECT coalesce(sum(${col}), 0) AS n FROM income_hourly WHERE character_id = $1`, [member.dbId])).rows[0]?.n);
    expect(await sum('gold_acq')).toBeGreaterThanOrEqual(gain);
    expect((await checkCharacter(member.dbId, fixed)).violated).toBe(false);
    const holds = await getPool().query('SELECT 1 FROM economy_holds WHERE character_id = $1', [member.dbId]);
    expect(holds.rowCount).toBe(0);
  });
});
