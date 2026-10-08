// 레이드 기간 경계: 입장은 리셋 전, 클리어는 리셋 후면 클리어 시점 기간으로 다시 판정한다(입장 때 ALREADY_CLAIMED여도)
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, fakeRng, seedClaims, seedLevel } from './economyHelpers';
import { clearAll, formParty, honestHostReport, newHero, post, startAndBegin, stats, type Hero } from './partyHelpers';

const app = buildApp();
const DUNGEON = 'raid_skeleton_king';
const WED_NOON = '2026-10-07T03:00:00Z'; // 수요일 12:00 KST
const THU_0559 = '2026-10-07T20:59:00Z'; // 목요일 05:59 KST(리셋 1분 전, 게임 날짜는 아직 수요일)
let fixed = new Date(WED_NOON);
const at = (iso: string) => {
  fixed = new Date(iso);
};
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeEach(async () => {
  formed.clear();
  await resetDb();
  at(WED_NOON);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const raidHero = async (): Promise<Hero> => {
  const h = await newHero(app);
  await seedLevel(h, 20);
  await seedClaims(h, ['c1_fortress']);
  return h;
};

/** 파티 레이드 한 판: 입장(현재 시각)부터 방장 보고, 방장·멤버 정산까지. 입장 판의 잠금 사유와 멤버 결과를 돌려준다 */
async function runRaid(host: Hero, member: Hero, totalSec: number, memberRid?: string) {
  // 파티는 판이 끝나도 유지되므로 처음에만 만들고, 이후는 같은 파티로 다시 출발한다
  if (!formed.has(host.id)) {
    await formParty(app, host, [member], { dungeon_id: DUNGEON, difficulty: 0 });
    formed.add(host.id);
  }
  const { runId, runs } = await startAndBegin(app, host, [member]);
  const entry = await getPool().query<{ lock_reason: string | null }>(
    'SELECT lock_reason FROM dungeon_runs WHERE uuid = $1',
    [runs.get(member.id)],
  );
  const t0 = fixed.getTime();
  for (const h of [host, member]) {
    at(new Date(t0 + 1).toISOString());
    await clearAll(app, h, runs.get(h.id) as string, advance, DUNGEON, 20);
  }
  at(new Date(t0 + totalSec * 1000).toISOString());
  const elapsed = totalSec * 1000;
  const rep = await post(
    app,
    host,
    `/party-runs/${runId}/host-report`,
    await honestHostReport(runs.get(host.id) as string, [host, member], { elapsed_ms: elapsed }, DUNGEON),
  );
  if (rep.status !== 200) throw new Error(`host-report ${rep.status} ${JSON.stringify(rep.body)}`);
  const body = { outcome: 'cleared', stats: stats({ elapsed_ms: elapsed, max_combo: 300 }) };
  await post(app, host, `/dungeon-runs/${runs.get(host.id)}/result`, body);
  const rm = await post(app, member, `/dungeon-runs/${runs.get(member.id)}/result`, body, memberRid);
  await post(app, host, `/dungeon-runs/${runs.get(host.id)}/settle`, {});
  return { entryLock: entry.rows[0]?.lock_reason ?? null, rm, memberRun: runs.get(member.id) as string };
}

const formed = new Set<string>();
const claimsOf = async (h: Hero): Promise<number> =>
  Number(((await getPool().query('SELECT count(*) AS n FROM raid_claims WHERE character_id = $1', [h.dbId])).rows[0] as { n: string }).n);

describe('레이드 기간 경계(리셋 06:00 KST)', () => {
  it('수요일에 받은 뒤 목요일 05:59 입장 / 06:05 클리어: 새 기간 보상을 받고, 같은 기간 두 번째는 잠기며, 재전송은 같은 응답이다', async () => {
    const host = await raidHero();
    const member = await raidHero();
    // 수요일 낮: 첫 수령
    const first = await runRaid(host, member, 300);
    expect(first.entryLock).toBeNull();
    expect(first.rm.body.data.raid).toMatchObject({ reward_locked: false });
    expect(await claimsOf(member)).toBe(1);
    const keysAfterFirst = await countOf(member, 'key_seal');

    // 목요일 05:59 입장(아직 수요일 기간이라 ALREADY_CLAIMED), 06:05에 클리어(새 기간)
    at(THU_0559);
    const rid = randomUUID();
    const second = await runRaid(host, member, 360, rid);
    expect(second.entryLock).toBe('ALREADY_CLAIMED');
    expect(second.rm.status).toBe(200);
    expect(second.rm.body.data.raid).toMatchObject({ reward_locked: false });
    expect(second.rm.body.data.granted_xp).toBeGreaterThan(0);
    expect(await claimsOf(member)).toBe(2);
    const keysAfterSecond = await countOf(member, 'key_seal');
    expect(keysAfterSecond).toBeGreaterThan(keysAfterFirst);

    // 재전송: 같은 request_id는 같은 응답, 청구·열쇠는 늘지 않는다
    const again = await post(
      app,
      member,
      `/dungeon-runs/${second.memberRun}/result`,
      { outcome: 'cleared', stats: stats({ elapsed_ms: 360_000, max_combo: 300 }) },
      rid,
    );
    expect(again.body).toEqual(second.rm.body);
    expect(await claimsOf(member)).toBe(2);
    expect(await countOf(member, 'key_seal')).toBe(keysAfterSecond);
    await expectLedgerConsistent(member);
  });

  it('같은 새 기간 안의 두 번째 클리어는 잠긴다(ALREADY_CLAIMED), 청구·열쇠는 늘지 않는다', async () => {
    const host = await raidHero();
    const member = await raidHero();
    // 토요일 낮 수령 -> 일요일 05:59 입장 / 06:05 클리어(새 기간 수령) -> 일요일 낮 한 번 더(주말은 요일 던전·레이드가 열린다)
    at('2026-10-10T03:00:00Z');
    await runRaid(host, member, 300);
    at('2026-10-10T20:59:00Z');
    const boundary = await runRaid(host, member, 360);
    expect(boundary.entryLock).toBe('ALREADY_CLAIMED');
    expect(boundary.rm.body.data.raid).toMatchObject({ reward_locked: false });
    expect(await claimsOf(member)).toBe(2);
    const keys = await countOf(member, 'key_seal');
    at('2026-10-11T03:00:00Z');
    const third = await runRaid(host, member, 300);
    expect(third.entryLock).toBe('ALREADY_CLAIMED');
    expect(third.rm.body.data.raid).toMatchObject({ reward_locked: true, lock_reason: 'ALREADY_CLAIMED' });
    expect(await claimsOf(member)).toBe(2);
    expect(await countOf(member, 'key_seal')).toBe(keys);
  });
});
