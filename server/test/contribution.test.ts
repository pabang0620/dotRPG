// 9단계 5절: 한 사람으로 세기(humanGroups), 기여 판정(레이드·파티 던전), 언더레벨 경험치 감쇠
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { contributionMet, contributionsOf, humanGroups, isDisputed, underlevelFactor } from '../src/domains/antiabuse/contribution';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { anomalyKinds, fakeRng, seedClaims, seedLevel, type Hero } from './economyHelpers';
import { auth, buildApp, createChar, randomLoginId, randomName, resetDb, shutdown, ver, type Session } from './helpers';
import { clearAll, formParty, get, honestHostReport, post, startAndBegin, stats } from './partyHelpers';

let app: Express = buildApp();
const MONDAY = '2026-10-05T03:00:00Z';
const WED = '2026-10-07T03:00:00Z';
let fixed = new Date(MONDAY);
const at = (ms: number) => {
  fixed = new Date(ms);
};
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeEach(async () => {
  await resetDb();
  app = buildApp();
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const dev = (hash: string | null) => (hash ? { install_id: randomUUID(), device_hash: hash } : undefined);
const hashOf = (): string => createHash('sha256').update(randomBytes(8)).digest('hex');

/** 기기 해시(같은 값이면 같은 기기)를 가진 계정의 영웅 */
async function heroOn(a: Express, deviceHash: string | null, level = 1, claims: string[] = []): Promise<Hero> {
  const loginId = randomLoginId();
  const reg = await request(a).post('/auth/dev/register').set(ver()).send({ login_id: loginId, password: 'password-1234', ...(deviceHash ? { device: dev(deviceHash) } : {}) });
  const s: Session = { loginId, password: 'password-1234', accountId: reg.body.data.account.id, access: reg.body.data.access_token, refresh: reg.body.data.refresh_token };
  const made = await createChar(a, s, randomName());
  const id = made.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  const h: Hero = { s, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' };
  if (level > 1) await seedLevel(h, level);
  if (claims.length) await seedClaims(h, claims);
  return h;
}

const result = (h: Hero, runUuid: string, body: Record<string, unknown>) => post(app, h, `/dungeon-runs/${runUuid}/result`, body);
const settle = (h: Hero, runUuid: string) => post(app, h, `/dungeon-runs/${runUuid}/settle`, {});
const runRow = async (uuid: string): Promise<{ reward_locked: boolean; lock_reason: string | null; contribution: Record<string, unknown> | null; xp_granted: number | null; state: string; hold_reason: string | null }> =>
  (await getPool().query('SELECT reward_locked, lock_reason, contribution, xp_granted, state, hold_reason FROM dungeon_runs WHERE uuid = $1', [uuid])).rows[0];
const claims = async (h: Hero): Promise<number> => Number(((await getPool().query('SELECT count(*) AS n FROM raid_claims WHERE character_id = $1', [h.dbId])).rows[0] as { n: string }).n);

describe('순수 함수', () => {
  it('humanGroups: 같은 기기, 같은 Steam 소유자, 체인으로 이어진 사람은 한 사람. NULL 기기끼리는 같은 사람이 아니다', () => {
    const k = (d: string | null, s: string | null = null) => ({ deviceHash: d, steamKey: s });
    expect(humanGroups([k('a'), k('a')])).toHaveLength(1);
    expect(humanGroups([k(null, '1'), k(null, '1')])).toHaveLength(1);
    expect(humanGroups([k('a'), k('b')])).toHaveLength(2);
    expect(humanGroups([k(null), k(null)])).toHaveLength(2);
    // 체인: A-B는 기기, B-C는 Steam
    expect(humanGroups([k('a', null), k('a', '9'), k('c', '9')])).toHaveLength(1);
    expect(humanGroups([k('a', '1'), k('b', '2'), k('b', null)]).map((g) => g.length).sort()).toEqual([1, 2]);
  });

  it('contributionMet: 지분 4.9%+적중 39는 잠금, 5.0%는 통과, 지분 1%+적중 40(치유형)은 통과, 적중 수가 없으면 지분만', () => {
    expect(contributionMet(0.049, 39, 0.05, 40)).toBe(false);
    expect(contributionMet(0.05, 39, 0.05, 40)).toBe(true);
    expect(contributionMet(0.01, 40, 0.05, 40)).toBe(true);
    expect(contributionMet(0.01, null, 0.05, 40)).toBe(false);
    const m = contributionsOf([{ id: 1, damage: 95, hits: null }, { id: 2, damage: 5, hits: null }], 0.05, 40);
    expect(m.get(2)).toMatchObject({ share: 0.05, met: true });
    expect(contributionsOf([{ id: 1, damage: 0, hits: null }], 0.05, 40).get(1)?.met).toBe(false);
  });

  it('언더레벨: 권장 20, 멤버 Lv10(gap 10) -> 0.25배, Lv6(gap 14) -> 0.05배, Lv11(gap 9) -> 1배', () => {
    const p = getConfig().aa.contribution;
    expect(underlevelFactor(20 - 10, p)).toBeCloseTo(0.25, 10);
    expect(underlevelFactor(20 - 6, p)).toBeCloseTo(0.05, 10);
    expect(underlevelFactor(20 - 11, p)).toBe(1);
    expect(underlevelFactor(40, p)).toBeCloseTo(0.05, 10);
  });

  it('isDisputed: 주장이 호스트 관찰의 2배 이상이고 주장 기준으로는 충족하지만 호스트 기준으로는 못 할 때만', () => {
    const p = { ratio: 2, minShare: 0.05, minHits: 40 };
    const host = { damage: 10, hits: null, totalDamage: 1000 };
    expect(isDisputed(host, { damage: 500 }, p)).toBe(true);
    expect(isDisputed(host, { damage: 15 }, p)).toBe(false);
    expect(isDisputed({ ...host, damage: 100 }, { damage: 500 }, p)).toBe(false);
    expect(isDisputed(host, {}, p)).toBe(false);
    expect(isDisputed({ damage: 10, hits: 1, totalDamage: 1000 }, { hits: 60 }, p)).toBe(true);
  });
});

/** 레이드 파티 판: 방장 보고의 피해를 가중치(비율)로 나눈다. 정산(result)은 호출 쪽 몫 */
async function raidRun(heroes: Hero[], weights: number[], hits: (number | undefined)[] = [], reportOver: Record<string, unknown> = {}) {
  at(new Date(WED).getTime());
  const [host, ...members] = heroes as [Hero, ...Hero[]];
  await formParty(app, host, members, { dungeon_id: 'raid_skeleton_king', difficulty: 0 });
  const { runId, runs } = await startAndBegin(app, host, members);
  const t0 = fixed.getTime();
  for (const h of heroes) {
    at(t0 + 1);
    await clearAll(app, h, runs.get(h.id) as string, advance, 'raid_skeleton_king', 20);
  }
  at(t0 + 300_000);
  const body = await honestHostReport(runs.get(host.id) as string, heroes, { elapsed_ms: 300_000, ...reportOver }, 'raid_skeleton_king');
  const each = (body.members as { damage_dealt: number }[])[0]?.damage_dealt as number;
  const w = weights.reduce((a, b) => a + b, 0);
  const unit = Math.ceil((each * heroes.length) / w);
  body.members = (body.members as Record<string, unknown>[]).map((m, i) => ({ ...m, damage_dealt: unit * (weights[i] as number), ...(hits[i] !== undefined ? { hits_landed: hits[i] } : {}) }));
  const rep = await post(app, host, `/party-runs/${runId}/host-report`, body);
  if (rep.status !== 200) throw new Error(`host-report ${rep.status} ${JSON.stringify(rep.body)}`);
  return { runId, runs };
}
const resultBody = (extra: Record<string, unknown> = {}) => ({ outcome: 'cleared', stats: stats({ elapsed_ms: 300_000, max_combo: 300, ...extra }) });

/** 모두 결과를 보고하고(방장 먼저) 방장이 정산한다. 각자의 응답을 돌려준다 */
async function finishRaid(heroes: Hero[], runs: Map<string, string>) {
  const [host, ...members] = heroes as [Hero, ...Hero[]];
  const out = new Map<string, Record<string, unknown>>();
  expect((await result(host, runs.get(host.id) as string, resultBody())).body.data.result).toBe('pending');
  for (const m of members) out.set(m.id, (await result(m, runs.get(m.id) as string, resultBody())).body.data);
  out.set(host.id, (await settle(host, runs.get(host.id) as string)).body.data);
  return out;
}

describe('레이드: 같은 기기 부계정과 기여', () => {
  it('본캐 + 친구 + 같은 기기의 서 있는 부계정: 본캐·친구는 보상, 부계정만 LOW_CONTRIBUTION', async () => {
    const d = hashOf();
    const host = await heroOn(app, d, 20, ['c1_fortress']);
    const alt = await heroOn(app, d, 20, ['c1_fortress']);
    const friend = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const { runs } = await raidRun([host, alt, friend], [10, 0, 10]);
    const out = await finishRaid([host, alt, friend], runs);
    expect((out.get(alt.id)?.raid as Record<string, unknown>)).toEqual({ reward_locked: true, lock_reason: 'LOW_CONTRIBUTION' });
    expect(out.get(host.id)?.granted_xp).toBeGreaterThan(0);
    expect(out.get(friend.id)?.granted_xp).toBeGreaterThan(0);
    expect(await claims(host)).toBe(1);
    expect(await claims(friend)).toBe(1);
    expect(await claims(alt)).toBe(0);
    expect(await runRow(runs.get(alt.id) as string)).toMatchObject({ reward_locked: true, lock_reason: 'LOW_CONTRIBUTION', contribution: { source: 'host', met: false, share: 0 } });
  });

  it('본캐 + 같은 기기 부계정(서 있음): 둘 다 보상 없음(부계정 LOW_CONTRIBUTION, 본캐 TOO_FEW_HUMANS), 청구 없음', async () => {
    const d = hashOf();
    const host = await heroOn(app, d, 20, ['c1_fortress']);
    const alt = await heroOn(app, d, 20, ['c1_fortress']);
    const { runs } = await raidRun([host, alt], [1, 0]);
    const out = await finishRaid([host, alt], runs);
    expect(out.get(alt.id)?.raid).toEqual({ reward_locked: true, lock_reason: 'LOW_CONTRIBUTION' });
    expect(out.get(host.id)?.raid).toEqual({ reward_locked: true, lock_reason: 'TOO_FEW_HUMANS' });
    expect(await claims(host)).toBe(0);
    expect(await claims(alt)).toBe(0);
  });

  it('지분 5.0%는 통과, 지분 1%+적중 39회는 잠금(청구 미소진), 적중 40회(치유형)는 통과', async () => {
    const a = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const b = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const ok = await raidRun([a, b], [19, 1]);
    const outOk = await finishRaid([a, b], ok.runs);
    expect((outOk.get(b.id)?.raid as Record<string, unknown>).reward_locked).toBe(false);
    expect(await claims(b)).toBe(1);

    await resetDb();
    const c = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const d = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const low = await raidRun([c, d], [99, 1], [undefined, 39]);
    const outLow = await finishRaid([c, d], low.runs);
    expect(outLow.get(d.id)?.raid).toEqual({ reward_locked: true, lock_reason: 'LOW_CONTRIBUTION' });
    // 잠긴 멤버의 주간·일일 청구는 소진되지 않는다(다음 기회에 청구할 수 있다)
    expect(await claims(d)).toBe(0);
    // 남은 한 사람만 보상을 받는 판은 사람 수 미달이다
    expect(outLow.get(c.id)?.raid).toEqual({ reward_locked: true, lock_reason: 'TOO_FEW_HUMANS' });

    await resetDb();
    const e = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const f = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const heal = await raidRun([e, f], [99, 1], [undefined, 40]);
    const outHeal = await finishRaid([e, f], heal.runs);
    expect((outHeal.get(f.id)?.raid as Record<string, unknown>).reward_locked).toBe(false);
    expect(await claims(f)).toBe(1);
  });

  it('호스트 보고가 무효인 레이드(enforce): 기여를 알 수 없어 그 멤버 정산이 held(CONTRIBUTION_UNKNOWN)', async () => {
    const [a, b, c] = [await heroOn(app, hashOf(), 20, ['c1_fortress']), await heroOn(app, hashOf(), 20, ['c1_fortress']), await heroOn(app, hashOf(), 20, ['c1_fortress'])];
    // ai_count 0인데 AI 항목이 있으면 방장 보고가 무효
    const { runs } = await raidRun([a, b, c], [1, 1, 1], [], { ai: [{ slot: 3, damage_dealt: 1 }] });
    await result(b, runs.get(b.id) as string, resultBody());
    await result(c, runs.get(c.id) as string, resultBody());
    advance(95);
    const s1 = await settle(b, runs.get(b.id) as string);
    expect(s1.body.data).toEqual({ result: 'held' });
    expect((await runRow(runs.get(b.id) as string)).hold_reason).toBe('CONTRIBUTION_UNKNOWN');
    expect(await claims(b)).toBe(0);
  });

  it('호스트 보고에 적중 수 물리 상한을 넘는 값이 있으면 보고가 무효(HITS_OVER_CAP)', async () => {
    const a = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const b = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const c = await heroOn(app, hashOf(), 20, ['c1_fortress']);
    const { runs } = await raidRun([a, b, c], [1, 1, 1], [100_000, undefined, undefined]);
    await result(a, runs.get(a.id) as string, resultBody());
    await result(b, runs.get(b.id) as string, resultBody());
    await result(c, runs.get(c.id) as string, resultBody());
    // 방장의 정산 때 무효 사유가 한 번 기록된다
    await settle(a, runs.get(a.id) as string);
    const r = await getPool().query("SELECT detail->'reasons' AS reasons FROM anomaly_log WHERE kind = 'party_host' AND character_id = $1", [a.dbId]);
    expect(r.rows.flatMap((x) => x.reasons)).toContain('HITS_OVER_CAP');
  });
});

/** 요일 던전(gold_vein) 2인 파티: 모든 방을 처치하고 방장 보고를 가중치로 보낸다 */
async function duoDungeon(app2: Express, weights: number[], hits: (number | undefined)[] = []) {
  const host = await heroOn(app2, hashOf());
  const member = await heroOn(app2, hashOf());
  await formParty(app2, host, [member]);
  const { runId, runs } = await startAndBegin(app2, host, [member]);
  const hostRun = runs.get(host.id) as string;
  const memberRun = runs.get(member.id) as string;
  await clearAll(app2, host, hostRun, advance);
  at(new Date(MONDAY).getTime() + 1000);
  await clearAll(app2, member, memberRun, advance);
  advance(40);
  const body = await honestHostReport(hostRun, [host, member]);
  const each = (body.members as { damage_dealt: number }[])[0]?.damage_dealt as number;
  const w = weights.reduce((a, b) => a + b, 0);
  const unit = Math.ceil((each * 2) / w);
  body.members = (body.members as Record<string, unknown>[]).map((m, i) => ({ ...m, damage_dealt: unit * (weights[i] as number), ...(hits[i] !== undefined ? { hits_landed: hits[i] } : {}) }));
  const rep = await post(app2, host, `/party-runs/${runId}/host-report`, body);
  if (rep.status !== 200) throw new Error(`host-report ${rep.status} ${JSON.stringify(rep.body)}`);
  return { host, member, hostRun, memberRun };
}

describe('파티 요일 던전의 기여', () => {
  it('기여 없는 멤버: 클리어 경험치·카드 없음(LOW_CONTRIBUTION), 입장 횟수는 소모된 채, 방장은 정상 보상', async () => {
    const { host, member, hostRun, memberRun } = await duoDungeon(app, [99, 1], [undefined, 39]);
    expect((await result(host, hostRun, { outcome: 'cleared', stats: stats() })).body.data.result).toBe('pending');
    const m = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(m.body.data).toMatchObject({ result: 'cleared', granted_xp: 0, card_count: 0, reward_locked: true, reward_lock_reason: 'LOW_CONTRIBUTION' });
    expect(m.body.data.raid).toBeUndefined();
    const h = await settle(host, hostRun);
    expect(h.body.data.granted_xp).toBeGreaterThan(0);
    expect(h.body.data.reward_locked).toBeUndefined();
    // 10단계 E3: 보상이 잠긴 판은 계정 주간 활동(direct_clear)에 세지 않고, 정상 보상을 받은 방장의 클리어는 센다
    const weekly = async (x: Hero): Promise<number> =>
      Number(((await getPool().query("SELECT coalesce(sum(used), 0) AS n FROM account_week_counters WHERE kind = 'direct_clear' AND account_id = (SELECT account_id FROM characters WHERE id = $1)", [x.dbId])).rows[0] as { n: string }).n);
    expect(await weekly(member)).toBe(0);
    expect(await weekly(host)).toBe(1);
    expect(await runRow(memberRun)).toMatchObject({ reward_locked: true, lock_reason: 'LOW_CONTRIBUTION', contribution: { source: 'host', met: false, hits: 39 } });
    expect((await get(app, member, '/dungeons')).body.data.entries.used).toBe(1);
    const xp = await getPool().query("SELECT count(*) AS n FROM xp_ledger WHERE reason = 'dungeon_clear' AND character_id = $1", [member.dbId]);
    expect(Number((xp.rows[0] as { n: string }).n)).toBe(0);
    // 같은 응답을 다시 정산해도 잠금 정보가 그대로다(재생)
    expect((await settle(member, memberRun)).body.data).toMatchObject({ reward_locked: true, reward_lock_reason: 'LOW_CONTRIBUTION' });
  });

  it('지분 5%는 통과하고, 적중 40회는 지분이 낮아도 통과한다', async () => {
    const a = await duoDungeon(app, [19, 1]);
    expect((await result(a.host, a.hostRun, { outcome: 'cleared', stats: stats() })).body.data.result).toBe('pending');
    expect((await result(a.member, a.memberRun, { outcome: 'cleared', stats: stats() })).body.data.granted_xp).toBeGreaterThan(0);
    await resetDb();
    fixed = new Date(MONDAY);
    const b = await duoDungeon(app, [99, 1], [undefined, 40]);
    await result(b.host, b.hostRun, { outcome: 'cleared', stats: stats() });
    expect((await result(b.member, b.memberRun, { outcome: 'cleared', stats: stats() })).body.data.granted_xp).toBeGreaterThan(0);
  });

  it('CONTRIBUTION_MODE=log: 잠그지 않고 dungeon_runs.contribution과 이상 기록만 남긴다. off는 아무것도 하지 않는다', async () => {
    const logApp = buildApp({ CONTRIBUTION_MODE: 'log' });
    const a = await duoDungeon(logApp, [99, 1]);
    await result(a.host, a.hostRun, { outcome: 'cleared', stats: stats() });
    const m = await post(logApp, a.member, `/dungeon-runs/${a.memberRun}/result`, { outcome: 'cleared', stats: stats() });
    expect(m.body.data.granted_xp).toBeGreaterThan(0);
    expect(await runRow(a.memberRun)).toMatchObject({ reward_locked: false, contribution: { met: false } });
  });

  it('멤버 주장 damage_dealt가 호스트 관찰의 2배 이상이고 주장 기준으로 충족하면 보류(CONTRIBUTION_DISPUTE)', async () => {
    const { host, member, hostRun, memberRun } = await duoDungeon(app, [99, 1]);
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    const total = Number(((await getPool().query("SELECT members FROM party_run_host_reports ORDER BY id DESC LIMIT 1")).rows[0] as { members: { damage_dealt: number }[] }).members.reduce((a: number, m) => a + m.damage_dealt, 0));
    const m = await result(member, memberRun, { outcome: 'cleared', stats: stats({ damage_dealt: total }) });
    expect(m.body.data).toEqual({ result: 'held' });
    expect((await runRow(memberRun)).hold_reason).toBe('CONTRIBUTION_DISPUTE');
  });

  it('호스트 보고가 무효인 파티 던전(레이드 아님)은 잠그지 않고 contribution 이상 기록만 남긴다', async () => {
    const host = await heroOn(app, hashOf());
    const members = [await heroOn(app, hashOf()), await heroOn(app, hashOf())];
    await formParty(app, host, members);
    const { runId, runs } = await startAndBegin(app, host, members);
    for (const h of [host, ...members]) {
      at(new Date(MONDAY).getTime() + 1000);
      await clearAll(app, h, runs.get(h.id) as string, advance);
    }
    advance(40);
    const body = await honestHostReport(runs.get(host.id) as string, [host, ...members], { ai: [{ slot: 3, damage_dealt: 1 }] });
    await post(app, host, `/party-runs/${runId}/host-report`, body);
    await result(host, runs.get(host.id) as string, { outcome: 'cleared', stats: stats() });
    const m1 = members[0] as Hero;
    await result(m1, runs.get(m1.id) as string, { outcome: 'cleared', stats: stats() });
    const m2 = members[1] as Hero;
    await result(m2, runs.get(m2.id) as string, { outcome: 'cleared', stats: stats() });
    advance(95);
    const s = await settle(m1, runs.get(m1.id) as string);
    expect(s.body.data.result).toBe('cleared');
    expect(s.body.data.granted_xp).toBeGreaterThan(0);
    expect(await anomalyKinds(m1)).toContain('contribution');
  });
});

describe('언더레벨 경험치 감쇠 (5.4)', () => {
  it('권장 레벨보다 10 이상 낮은 솔로 멤버의 클리어 경험치는 깎인다(gap 11 -> 0.2배, 최소 1). 감쇠를 끄면 원래 값', async () => {
    const run = async (a: Express): Promise<number> => {
      const h = await heroOn(a, null);
      await getPool().query(
        `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, started_at, ended_at, rank, cards, reward_locked)
         VALUES ($1, 'gold_vein', 0, 'cleared', now() - interval '3 days', now(), now(), 3, '[]'::jsonb, false)`,
        [h.dbId],
      );
      const enter = await post(a, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 1 });
      expect(enter.status).toBe(201);
      const id = enter.body.data.run.id as string;
      await clearAll(a, h, id, advance);
      advance(40);
      // 처치 경험치로 오른 레벨을 되돌린다(감쇠는 정산 시점의 서버 레벨로 정한다)
      await getPool().query('UPDATE characters SET level = 1, xp = 0 WHERE id = $1', [h.dbId]);
      const res = await post(a, h, `/dungeon-runs/${id}/result`, { outcome: 'cleared', stats: stats() });
      expect(res.body.data.result).toBe('cleared');
      return res.body.data.granted_xp as number;
    };
    // PARTY_MIN_LEVEL_SLACK=20: 레벨 1이 권장 12인 난이도(모험)에 입장할 수 있다
    const decayed = await run(buildApp({ PARTY_MIN_LEVEL_SLACK: '20' }));
    fixed = new Date(MONDAY);
    await resetDb();
    const plain = await run(buildApp({ PARTY_MIN_LEVEL_SLACK: '20', DUNGEON_UNDERLEVEL_GAP: '100' }));
    expect(plain).toBeGreaterThan(20);
    expect(decayed).toBe(Math.max(1, Math.round(plain * 0.2)));
    app = buildApp();
  });
});
