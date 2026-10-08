// 탈퇴 설정 검증과 기능 스위치 (Docs/server/phase12_withdrawal.md 12절 T-W34, T-W35)
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { loadConfig } from '../src/config/env';
import { withdrawalAnonymizeJob } from '../src/ops/jobs/withdrawalJobs';
import { resetDb, shutdown, ver } from './helpers';
import { adminApp, adminPost, makeAdmin } from './opsHelpers';
import { payApp, resetPay } from './payHelpers';
import {
  cancelBody, devUser, jobCtx, makeDue, requestWithdrawal, steamUser, withdrawalInfo, withdrawalRows,
} from './withdrawalHelpers';

beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(shutdown);

describe('설정 검증 (T-W34)', () => {
  const JWT = 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw';
  const SECRET = 'Qm8vN2xK5pL7wR3tY6uZ9aB4cD1eF0gHiJkLmNoPq';
  const KEY = 'Wd7hK2mQ9xL4vR8tY1uZ5aB3cD6eF0gHnJkLoNpRsTw';
  const base = { DATABASE_URL: 'x', MIN_CLIENT_VERSION: '0.1.0', JWT_SECRET: JWT };
  const prod = {
    ...base,
    NODE_ENV: 'production',
    STEAM_AUTH_MODE: 'web_api',
    STEAM_APP_ID: '2800000',
    DEPLOY_STAGE: 'live',
    DEVICE_HASH_PEPPER: 'Pe9rL4vK7mQ2xW5tY8uZ3aB6cD1eF0gHnJkLoNpRs',
    STEAM_WEB_API_KEY: 'k',
    TRUST_PROXY: '1',
    ADMIN_SECRET_KEY: Buffer.alloc(32, 9).toString('base64'),
    RELAY_TICKET_SECRET: SECRET,
    RELAY_PUBLIC_URL: 'wss://game.example.org/relay',
    WITHDRAW_ID_HMAC_KEY: KEY,
  };

  it('T-W34 운영 설정: HMAC 키 없음, REAUTH 끔, 파기를 켰는데 PURGE_DATABASE_URL 없음은 기동을 거부한다', () => {
    expect(loadConfig(prod).withdraw).toMatchObject({ enabled: true, graceDays: 30, reauthRequired: true, destroyEnabled: false, purgeDatabaseUrl: null });
    const { WITHDRAW_ID_HMAC_KEY: _drop, ...noKey } = prod;
    void _drop;
    expect(() => loadConfig(noKey)).toThrow(/WITHDRAW_ID_HMAC_KEY/);
    // 기능을 끄면 키가 없어도 뜬다(진행 중인 요청의 작업은 키 없이 파생 키로 계속 돈다)
    expect(loadConfig({ ...noKey, WITHDRAW_ENABLED: 'false' }).withdraw.enabled).toBe(false);
    expect(() => loadConfig({ ...prod, WITHDRAW_ID_HMAC_KEY: 'short' })).toThrow(/32자/);
    expect(() => loadConfig({ ...prod, WITHDRAW_ID_HMAC_KEY: JWT })).toThrow(/JWT_SECRET/);
    expect(() => loadConfig({ ...prod, WITHDRAW_REAUTH_REQUIRED: 'false' })).toThrow(/WITHDRAW_REAUTH_REQUIRED/);
    expect(() => loadConfig({ ...prod, WITHDRAW_DESTROY_ENABLED: 'true' })).toThrow(/PURGE_DATABASE_URL/);
    const ok = loadConfig({ ...prod, WITHDRAW_DESTROY_ENABLED: 'true', PURGE_DATABASE_URL: 'postgres://dotrpg_purge:pw@db:5432/dotrpg' });
    expect(ok.withdraw).toMatchObject({ destroyEnabled: true, purgeDatabaseUrl: 'postgres://dotrpg_purge:pw@db:5432/dotrpg' });
    // 개발·시험은 REAUTH 를 끌 수 있고 키가 없으면 파생 키를 쓴다. 값은 환경변수로 바꾼다(코드에 숫자를 두지 않는다)
    const dev = loadConfig({ ...base, WITHDRAW_REAUTH_REQUIRED: 'false', WITHDRAW_GRACE_DAYS: '14', WITHDRAW_DEFER_MAX_DAYS: '90', WITHDRAW_RETAIN_PAID_YEARS: '3' });
    expect(dev.withdraw).toMatchObject({ reauthRequired: false, graceDays: 14, deferMaxDays: 90, retainPaidYears: 3, retainLedgerYears: 5, tombstoneMaxDays: 1095, requestMaxPer30d: 3, jobBatch: 20 });
    expect(dev.withdraw.idHmacKey.length).toBeGreaterThan(0);
    expect(() => loadConfig({ ...base, WITHDRAW_GRACE_DAYS: '0' })).toThrow();
    expect(() => loadConfig({ ...base, WITHDRAW_ENABLED: 'yes' })).toThrow();
  });

  it('유예 일수 등 설정값이 실제 기한 계산에 반영된다(WITHDRAW_GRACE_DAYS)', async () => {
    const app = payApp({ WITHDRAW_GRACE_DAYS: '7' });
    const u = await devUser(app);
    const info = await withdrawalInfo(app, u);
    expect(info.body.data.grace_days).toBe(7);
    const res = await requestWithdrawal(app, u);
    expect(res.status).toBe(201);
    const due = new Date(res.body.data.withdrawal.due_at).getTime();
    const at = new Date(res.body.data.withdrawal.requested_at).getTime();
    expect(Math.round((due - at) / 86_400_000)).toBe(7);
    // 확인 문구도 설정값이다
    const app2 = payApp({ WITHDRAW_CONFIRM_PHRASE: '계정을 삭제합니다' });
    const v = await devUser(app2);
    expect((await requestWithdrawal(app2, v)).status).toBe(422);
    expect((await requestWithdrawal(app2, v, { confirm: '계정을 삭제합니다' })).status).toBe(201);
    payApp();
  });
});

describe('기능 스위치 (T-W35)', () => {
  it('T-W35 WITHDRAW_ENABLED=false 면 W1~W3 와 WD3 가 503 FEATURE_DISABLED 이고, 이미 진행 중인 요청의 익명화 작업은 계속 돈다', async () => {
    let app = payApp();
    const pending = await steamUser(app);
    expect((await requestWithdrawal(app, pending)).status).toBe(201);
    const live = await devUser(app);
    await makeDue(pending.accountId);

    app = payApp({ WITHDRAW_ENABLED: 'false' });
    const info = await withdrawalInfo(app, live);
    expect(info.status).toBe(503);
    expect(info.body.errors.code).toBe('FEATURE_DISABLED');
    const req = await requestWithdrawal(app, live);
    expect(req.status).toBe(503);
    expect(req.body.errors.code).toBe('FEATURE_DISABLED');
    const cancel = await request(app).post('/auth/withdrawal/cancel').set(ver()).send(cancelBody(pending));
    expect(cancel.status).toBe(503);
    expect(cancel.body.errors.code).toBe('FEATURE_DISABLED');
    const adm = adminApp();
    const operator = await makeAdmin('operator');
    const wd3 = await adminPost(adm, operator, `/admin/accounts/${live.accountUuid}/withdrawal`, { note: '꺼진 상태' });
    expect(wd3.status).toBe(503);
    expect(wd3.body.errors.code).toBe('FEATURE_DISABLED');
    expect((await getPool().query('SELECT deleted_at FROM accounts WHERE id = $1', [live.accountId])).rows[0]).toEqual({ deleted_at: null });
    expect(await withdrawalRows(live.accountId)).toHaveLength(0);
    // 로그인 거절(E1)과 이월 검사는 스위치와 무관하게 동작한다
    expect((await request(app).post('/auth/steam').set(ver()).send({ ticket: `mock:${pending.steamId}:abcdefgh12345678` })).status).toBe(403);
    // 진행 중인 요청은 익명화 작업이 계속 처리한다
    const r = await withdrawalAnonymizeJob(jobCtx);
    expect((r.detail as { completed: number }).completed).toBeGreaterThanOrEqual(1);
    expect((await withdrawalRows(pending.accountId))[0]).toMatchObject({ state: 'completed' });
    payApp();
  });
});
