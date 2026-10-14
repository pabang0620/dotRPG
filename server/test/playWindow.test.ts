// 0039: 게임시간 선택제, 정지 거절의 reason_code, 제재 사유 코드 확장, 동의 강제의 상태 저장 예외
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { clearConsentCache } from '../src/domains/policies/consentGate';
import * as realtime from '../src/domains/chat/realtimeNotifier';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { playWindowSweepJob } from '../src/ops/jobs/playWindowSweep';
import { setClockOverride } from '../src/utils/clock';
import { auth, buildApp, createChar, emptyState, randomName, registerAccount, resetDb, shutdown, ver } from './helpers';
import { adminApp, adminPost, auditRows, makeAdmin, type TestAdmin } from './opsHelpers';

const pub = buildApp();
const app = adminApp();
let operator: TestAdmin;
let viewer: TestAdmin;

/** KST 시:분에 해당하는 UTC 시각 */
const kst = (h: number, m = 0): Date => new Date(Date.UTC(2026, 9, 14, h - 9, m));

beforeEach(async () => {
  await resetDb();
  getRateLimitStore().clear();
  clearConsentCache();
  [operator, viewer] = [await makeAdmin('operator'), await makeAdmin('viewer')];
});
afterEach(() => {
  setClockOverride(null);
  jest.restoreAllMocks();
});
afterAll(async () => {
  await shutdown();
});

const setWindow = (uuid: string, start: string | null, end: string | null, requestId?: string) =>
  adminPost(app, operator, `/admin/accounts/${uuid}/play-window`, { start, end, note: '법정대리인 요청' }, requestId);
/** 시계는 로그인·갱신 요청에만 고정한다(관리자 세션 만료도 같은 시계라 관리자 호출에는 쓰지 않는다) */
let clockAt: Date | null = null;
const clock = (d: Date | null): void => {
  clockAt = d;
};
const login = async (loginId: string, password: string) => {
  setClockOverride(clockAt ? () => clockAt as Date : null);
  try {
    return await request(pub).post('/auth/dev/login').set(ver()).send({ login_id: loginId, password });
  } finally {
    setClockOverride(null);
  }
};
const row = async (uuid: string) =>
  (await getPool().query('SELECT play_window_start AS s, play_window_end AS e FROM accounts WHERE uuid = $1', [uuid])).rows[0];

describe('[0039] 게임시간 선택제 관리자 API', () => {
  it('operator가 설정·해제하고 감사 로그가 남는다. viewer는 403', async () => {
    const s = await registerAccount(pub);
    const path = `/admin/accounts/${s.accountId}/play-window`;
    expect((await adminPost(app, viewer, path, { start: '22:00', end: '07:00', note: 'x' })).status).toBe(403);
    const r = await setWindow(s.accountId, '22:00', '07:00');
    expect(r.status).toBe(200);
    expect(r.body.data.play_window).toEqual({ start: '22:00', end: '07:00' });
    expect(await row(s.accountId)).toEqual({ s: 1320, e: 420 });
    expect((await auditRows("action = 'account.play_window' AND result = 'ok'")).length).toBe(1);
    const off = await setWindow(s.accountId, null, null);
    expect(off.body.data.play_window).toBeNull();
    expect(await row(s.accountId)).toEqual({ s: null, e: null });
    expect((await setWindow(randomUUID(), '22:00', '07:00')).status).toBe(404);
  });

  it('입력 오류: 한쪽만 null, 같은 시각, 형식 오류는 400', async () => {
    const s = await registerAccount(pub);
    for (const [a, b] of [['22:00', null], [null, '07:00'], ['10:00', '10:00'], ['24:00', '07:00'], ['9:00', '10:00']] as const) {
      expect((await setWindow(s.accountId, a, b)).status).toBe(400);
    }
    expect(await row(s.accountId)).toEqual({ s: null, e: null });
  });

  it('같은 request_id 재전송은 처음 응답을 돌려준다', async () => {
    const s = await registerAccount(pub);
    const rid = randomUUID();
    const a = await setWindow(s.accountId, '08:00', '20:00', rid);
    const b = await setWindow(s.accountId, '08:00', '20:00', rid);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body.data).toEqual(a.body.data);
  });

  it('동시 두 요청: 둘 다 끝나고 마지막 값이 하나로 남는다', async () => {
    const s = await registerAccount(pub);
    const [a, b] = await Promise.all([setWindow(s.accountId, '08:00', '12:00'), setWindow(s.accountId, '13:00', '18:00')]);
    expect([a.status, b.status]).toEqual([200, 200]);
    const r = await row(s.accountId);
    expect([{ s: 480, e: 720 }, { s: 780, e: 1080 }]).toContainEqual(r);
  });
});

describe('[0039] 로그인·토큰 갱신 차단', () => {
  it('구간 밖이면 403 PLAY_WINDOW_CLOSED(opens_at), 안이면 통과, 경계는 시작 포함 끝 제외', async () => {
    const s = await registerAccount(pub);
    await setWindow(s.accountId, '22:00', '07:00');
    clock(kst(12));
    const closed = await login(s.loginId, s.password);
    expect(closed.status).toBe(403);
    expect(closed.body.errors).toMatchObject({ code: 'PLAY_WINDOW_CLOSED', opens_at: '22:00' });
    setClockOverride(() => kst(12));
    const ref = await request(pub).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    setClockOverride(null);
    expect(ref.status).toBe(403);
    expect(ref.body.errors.code).toBe('PLAY_WINDOW_CLOSED');
    clock(kst(7, 0));
    expect((await login(s.loginId, s.password)).status).toBe(403);
    clock(kst(6, 59));
    expect((await login(s.loginId, s.password)).status).toBe(200);
    clock(kst(22, 0));
    expect((await login(s.loginId, s.password)).status).toBe(200);
    clock(kst(23, 30));
    const ok = await login(s.loginId, s.password);
    expect(ok.status).toBe(200);
    // 같은 날 구간(09:00~18:00)과 해제
    await setWindow(s.accountId, '09:00', '18:00');
    clock(kst(19));
    expect((await login(s.loginId, s.password)).status).toBe(403);
    await setWindow(s.accountId, null, null);
    expect((await login(s.loginId, s.password)).status).toBe(200);
  });

  it('설정 직후 구간 밖이면 끊고, 구간 안이면 끊지 않는다. 매분 작업도 구간 밖 접속자만 끊는다', async () => {
    const s = await registerAccount(pub);
    const id = Number((await getPool().query('SELECT id FROM accounts WHERE uuid = $1', [s.accountId])).rows[0].id);
    const kick = jest.spyOn(realtime, 'kickAccount').mockReturnValue(true);
    jest.spyOn(realtime.registry, 'all').mockReturnValue([{ accountId: id }] as never);
    jest.spyOn(realtime.registry, 'ofAccount').mockReturnValue({ accountId: id } as never);
    // 지금 KST 분을 기준으로 안(+-3시간)과 밖(+3~+4시간)의 구간을 만든다
    const hm = (m: number) => `${String(Math.floor(((m % 1440) + 1440) % 1440 / 60)).padStart(2, '0')}:${String(((m % 60) + 60) % 60).padStart(2, '0')}`;
    const now = Math.floor(((Date.now() + 9 * 3_600_000) % 86_400_000) / 60_000);
    await setWindow(s.accountId, hm(now - 180), hm(now + 180));
    await new Promise((r) => setTimeout(r, 50));
    expect(kick).not.toHaveBeenCalled();
    expect((await playWindowSweepJob()).rows).toBe(0);
    await setWindow(s.accountId, hm(now + 180), hm(now + 240));
    await new Promise((r) => setTimeout(r, 50));
    expect(kick).toHaveBeenCalledWith(id, 4011, 'KICKED');
    kick.mockClear();
    expect((await playWindowSweepJob()).rows).toBe(1);
    expect(kick).toHaveBeenCalledWith(id, 4011, 'PLAY_WINDOW_CLOSED');
  });
});

describe('[0039] 정지 거절 사유와 사유 코드 확장', () => {
  it('새 사유 코드로 정지하면 로그인 거절 details에 reason_code가 실린다', async () => {
    const s = await registerAccount(pub);
    const r = await adminPost(app, operator, `/admin/accounts/${s.accountId}/sanctions`, { kind: 'ban', reason_code: 'rmt', duration: '1d', note: '현금 거래' });
    expect(r.status).toBe(201);
    const res = await login(s.loginId, s.password);
    expect(res.status).toBe(403);
    expect(res.body.errors).toMatchObject({ code: 'ACCOUNT_BANNED', reason_code: 'rmt' });
    const ref = await request(pub).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(ref.body.errors).toMatchObject({ code: 'ACCOUNT_BANNED', reason_code: 'rmt' });
  });

  it.each(['sexual', 'hate', 'impersonation', 'rmt', 'exploit', 'name'])('사유 코드 %s는 제재에 쓸 수 있다', async (code) => {
    const s = await registerAccount(pub);
    const r = await adminPost(app, operator, `/admin/accounts/${s.accountId}/sanctions`, { kind: 'warning', reason_code: code, note: '테스트' });
    expect(r.status).toBe(201);
    expect(r.body.data.sanction.reason_code).toBe(code);
    expect((await adminPost(app, operator, `/admin/accounts/${s.accountId}/sanctions`, { kind: 'warning', reason_code: 'nope', note: 'x' })).status).toBe(400);
  });
});

describe('[0039] 동의 강제와 상태 저장', () => {
  it('약관 미동의여도 상태 저장(PUT)은 통과하고, 다른 게임 API는 428', async () => {
    const s = await registerAccount(pub);
    const c = await createChar(pub, s, randomName());
    const id = c.body.data.character.id as string;
    const enforced = buildApp({ POLICY_CONSENT_ENFORCE: 'true' });
    const put = await request(enforced).put(`/characters/${id}/state`).set(auth(s)).send(emptyState(0));
    expect(put.status).toBe(200);
    expect((await request(enforced).get(`/characters/${id}`).set(auth(s))).status).toBe(428);
    expect((await request(enforced).get('/characters').set(auth(s))).status).toBe(428);
    expect((await request(enforced).put(`/characters/${id}/rename`).set(auth(s)).send({})).status).not.toBe(200);
  });
});
