import { pickGroups } from '../src/domains/match/matchRules';
import type { QueueTicket } from '../src/domains/match/queueStore';
import { consistent, mergeStats, reconcile, type ReportFacts, type Tolerance } from '../src/domains/partyruns/reconcile';
import { loadConfig } from '../src/config/env';

const tol: Tolerance = { elapsedMs: 5000, killRatio: 0.2, roomSizes: [5, 5] };
const facts = (over: Partial<ReportFacts> = {}): ReportFacts => ({
  outcome: 'cleared',
  elapsedMs: 80_000,
  rooms: new Map([[0, 5], [1, 5]]),
  ...over,
});
const base = { pastDeadline: false, tol, hostReport: null as ReportFacts | null };

describe('결과 대조 순수 함수', () => {
  it('일치: 결과가 같고 시간·방별 처치 차가 허용 안', () => {
    expect(consistent(facts(), facts({ elapsedMs: 84_000 }), tol)).toBe(true);
    expect(consistent(facts(), facts({ elapsedMs: 90_000 }), tol)).toBe(false);
    expect(consistent(facts(), facts({ outcome: 'failed' }), tol)).toBe(false);
    expect(consistent(facts(), facts({ rooms: new Map([[0, 4], [1, 5]]) }), tol)).toBe(true); // ceil(0.2*5)=1
    expect(consistent(facts(), facts({ rooms: new Map([[0, 3], [1, 5]]) }), tol)).toBe(false);
  });

  it('증인이 없으면 솔로 수준으로 정산한다', () => {
    expect(reconcile({ ...base, iAmHost: false, mine: facts(), witnesses: [] })).toEqual({ kind: 'settle' });
  });

  it('멤버: 방장 보고와 일치하면 정산, 어긋나고 뒷받침이 없으면 held, 다른 멤버가 맞으면 정산(방장 이상치)', () => {
    const host = facts();
    const bad = facts({ elapsedMs: 50_000 });
    const w = [{ isHost: true, report: host }];
    expect(reconcile({ ...base, iAmHost: false, mine: facts(), hostReport: host, witnesses: w })).toEqual({ kind: 'settle' });
    expect(reconcile({ ...base, iAmHost: false, mine: bad, hostReport: host, witnesses: w })).toEqual({ kind: 'held', reason: 'MISMATCH' });
    const w2 = [...w, { isHost: false, report: bad }];
    expect(reconcile({ ...base, iAmHost: false, mine: bad, hostReport: host, witnesses: w2 })).toEqual({ kind: 'settle', hostOutlier: true });
    // 다른 멤버가 아직 보고 전이면 마감까지 기다린다
    const w3 = [...w, { isHost: false, report: null }];
    expect(reconcile({ ...base, iAmHost: false, mine: bad, hostReport: host, witnesses: w3 })).toEqual({ kind: 'pending' });
    expect(reconcile({ ...base, pastDeadline: true, iAmHost: false, mine: bad, hostReport: host, witnesses: w3 })).toEqual({ kind: 'held', reason: 'MISMATCH' });
  });

  it('방장 보고가 없으면 마감 전 pending, 마감 후 뒷받침이 없으면 NO_HOST_REPORT', () => {
    const w = [{ isHost: true, report: null }, { isHost: false, report: facts() }];
    expect(reconcile({ ...base, iAmHost: false, mine: facts(), witnesses: w })).toEqual({ kind: 'pending' });
    expect(reconcile({ ...base, pastDeadline: true, iAmHost: false, mine: facts(), witnesses: w })).toEqual({ kind: 'settle' });
    const only = [{ isHost: true, report: null }];
    expect(reconcile({ ...base, pastDeadline: true, iAmHost: false, mine: facts(), witnesses: only })).toEqual({ kind: 'held', reason: 'NO_HOST_REPORT' });
  });

  it('방장: 증인 보고가 맞으면 정산, 없으면 마감까지 pending 후 NO_WITNESS, 어긋나면 held', () => {
    const w = [{ isHost: false, report: null }];
    expect(reconcile({ ...base, iAmHost: true, mine: facts(), witnesses: w })).toEqual({ kind: 'pending' });
    expect(reconcile({ ...base, pastDeadline: true, iAmHost: true, mine: facts(), witnesses: w })).toEqual({ kind: 'held', reason: 'NO_WITNESS' });
    expect(reconcile({ ...base, iAmHost: true, mine: facts(), witnesses: [{ isHost: false, report: facts() }] })).toEqual({ kind: 'settle' });
    expect(reconcile({ ...base, iAmHost: true, mine: facts(), witnesses: [{ isHost: false, report: facts({ elapsedMs: 1000 }) }] })).toEqual({ kind: 'held', reason: 'MISMATCH' });
  });

  it('정산 값 합성: 피격·부활은 큰 값, 콤보는 작은 값, 방장 보고가 없으면 내 값', () => {
    const mine = { hits: 2, combo: 30, revives: 0 };
    expect(mergeStats({ mine, host: { hits: 5, combo: 25, revives: 1 } })).toMatchObject({ hits: 5, combo: 25, revives: 1, outlier: false });
    expect(mergeStats({ mine, host: { hits: 1, combo: 40, revives: 0 } })).toMatchObject({ hits: 2, combo: 30, revives: 0, outlier: false });
    expect(mergeStats({ mine, host: null })).toEqual({ ...mine, outlier: false });
    // 허용 범위(피격 10, 콤보 10, 부활 1)를 넘게 어긋난 항목은 멤버 자기 값을 쓰고 outlier
    expect(mergeStats({ mine, host: { hits: 999, combo: 0, revives: 9 } })).toEqual({ hits: 2, combo: 30, revives: 0, outlier: true });
  });
});

describe('매칭 규칙 순수 함수', () => {
  const t = (id: number, power: number, queuedSec: number, force = false): QueueTicket => ({
    characterId: id, accountId: id, dungeonId: 'd', difficulty: 0, level: 1, power,
    queuedAt: new Date(queuedSec * 1000), lastPollAt: new Date(0), forceDepart: force,
  });

  it('4명이 모이면 즉시, 아니면 시드가 60초에 닿았을 때, 전투력 +-30% 밖은 제외', () => {
    const four = [t(1, 1000, 0), t(2, 1100, 1), t(3, 900, 2), t(4, 1200, 3)];
    expect(pickGroups(four, 5_000, 0.3, 60_000)).toHaveLength(1);
    const two = [t(1, 1000, 0), t(2, 1100, 1)];
    expect(pickGroups(two, 30_000, 0.3, 60_000)).toHaveLength(0);
    expect(pickGroups(two, 60_000, 0.3, 60_000)[0]).toHaveLength(2);
    const far = [t(1, 1000, 0), t(2, 2000, 1)];
    expect(pickGroups(far, 61_000, 0.3, 60_000).map((g) => g.length)).toEqual([1, 1]);
    expect(pickGroups([t(1, 1000, 0, true)], 1000, 0.3, 60_000)).toHaveLength(1);
  });
});

describe('4단계 환경변수 검증', () => {
  const base = { DATABASE_URL: 'x', JWT_SECRET: 'a'.repeat(32), MIN_CLIENT_VERSION: '0.1.0' };
  it('web_api 에는 앱 ID와 키가 필요하고, 운영에서 mock+dev 조합은 거부한다', () => {
    expect(() => loadConfig({ ...base, STEAM_AUTH_MODE: 'web_api' })).toThrow(/STEAM_WEB_API_KEY/);
    expect(loadConfig({ ...base, STEAM_AUTH_MODE: 'web_api', STEAM_APP_ID: '480', STEAM_WEB_API_KEY: 'k' }).steam.appId).toBe(480);
    expect(() => loadConfig({ ...base, NODE_ENV: 'production', STEAM_AUTH_MODE: 'mock', PARTY_TRANSPORT: 'dev' })).toThrow(/mock/);
    // 7단계 가드(G5, G6): 운영에서 480(Valve 시험용 앱)은 거부하고, 유효한 운영 값이면 통과한다
    const prod = {
      ...base,
      JWT_SECRET: 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw',
      NODE_ENV: 'production',
      STEAM_AUTH_MODE: 'web_api',
      STEAM_WEB_API_KEY: 'k',
      PARTY_TRANSPORT: 'steam',
      TRUST_PROXY: '1',
      ADMIN_SECRET_KEY: Buffer.alloc(32, 9).toString('base64'),
    };
    expect(() => loadConfig({ ...prod, STEAM_APP_ID: '480' })).toThrow(/480/);
    expect(loadConfig({ ...prod, STEAM_APP_ID: '2800000' }).partyTransport).toBe('steam');
    const c = loadConfig(base);
    expect(c.policy).toMatchObject({ raidRewardMinHumans: 2, raidPracticePaysKills: false, partyMinLevelSlack: 5 });
  });
});
