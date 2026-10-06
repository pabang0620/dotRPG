// 11단계 결제: 라우트 목록 스냅샷(지급 API 없음), 실제 Steam 어댑터의 오류 처리와 키 비노출(G1). fetch 는 가짜로 바꾼다(실제 Steam 호출 금지)
import { createPaymentsRouter } from '../src/domains/payments/paymentsRoutes';
import { LOG_REDACT, logger } from '../src/utils/logger';
import { getSteamPartner, PartnerCallError, payPartnerStats, resetPayPartnerStats, setSteamPartner } from '../src/domains/payments/steamPartner';
import { buildApp, resetDb, shutdown } from './helpers';
import { PAY_ENV, resetPay } from './payHelpers';

afterAll(async () => {
  jest.restoreAllMocks();
  resetPay();
  buildApp();
  await shutdown();
});
beforeAll(resetDb);
beforeEach(() => jest.restoreAllMocks());

const KEY = 'PUBLISHER-KEY-SECRET-0123456789';

describe('A1. 플레이어 결제 라우트 목록 스냅샷', () => {
  it('B1~B6 여섯 개뿐이고 지급·확정·영수증 같은 경로는 없다', () => {
    const r = createPaymentsRouter() as unknown as { stack: { route?: { path: string; methods: Record<string, boolean> } }[] };
    const routes = r.stack.filter((l) => l.route).map((l) => `${Object.keys((l.route as { methods: Record<string, boolean> }).methods)[0]?.toUpperCase()} ${(l.route as { path: string }).path}`).sort();
    expect(routes).toEqual(
      [
        'GET /payments/products',
        'POST /payments/orders',
        'GET /payments/orders',
        'POST /payments/reconcile',
        'GET /payments/orders/:uuid',
        'POST /payments/orders/:uuid/sync',
      ].sort(),
    );
  });
});

describe('G1. 실제 Steam 어댑터(HttpSteamPartner): 키는 POST 본문, 오류·로그에 키와 URL 이 없다', () => {
  const sandbox = (): void => {
    resetPay();
    buildApp({
      ...PAY_ENV,
      PAYMENTS_STEAM_MODE: 'sandbox',
      STEAM_PUBLISHER_API_KEY: KEY,
      STEAM_AUTH_MODE: 'web_api',
      STEAM_APP_ID: '480',
      STEAM_WEB_API_KEY: 'auth-key-auth-key',
      STAR_RATES_ACK_REQUIRED: 'true',
    });
    setSteamPartner(null);
    resetPayPartnerStats();
  };

  it('InitTxn 은 키를 본문(form)에 싣고 URL 에는 쿼리가 없다, 응답을 못 받으면 재시도하지 않는다(fetch 1회)', async () => {
    sandbox();
    const fetchMock = jest.spyOn(globalThis, 'fetch').mockRejectedValue(new Error(`connect failed https://partner/?key=${KEY}`));
    const warn = jest.spyOn(logger, 'warn');
    const error = jest.spyOn(logger, 'error');
    const p = getSteamPartner();
    await expect(p.initTxn({ orderId: '1', steamId: '76561198000000001', appId: 480, itemId: 1001, itemName: 'x', amountMinor: 1100, currency: 'KRW', language: 'ko' })).rejects.toBeInstanceOf(PartnerCallError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [URL, RequestInit];
    expect(String(url)).not.toContain(KEY);
    expect(String(url)).not.toContain('?');
    expect(String(init.body)).toContain('key=');
    const logged = JSON.stringify([warn.mock.calls, error.mock.calls]);
    expect(logged).not.toContain(KEY);
    try {
      await p.initTxn({ orderId: '1', steamId: '76561198000000001', appId: 480, itemId: 1001, itemName: 'x', amountMinor: 1100, currency: 'KRW', language: 'ko' });
    } catch (e) {
      expect((e as Error).message).not.toContain(KEY);
      expect((e as Error).message).not.toMatch(/https?:/);
    }
  });

  it('GET(QueryTxn)은 2회까지 재시도하고, 401 은 재시도 없이 키 거절로 기록한다(키 없는 오류)', async () => {
    sandbox();
    const fetchMock = jest.spyOn(globalThis, 'fetch').mockRejectedValue(new Error(`boom ${KEY}`));
    const p = getSteamPartner();
    await expect(p.queryTxn('1')).rejects.toMatchObject({ kind: 'unavailable' });
    expect(fetchMock).toHaveBeenCalledTimes(3);
    sandbox();
    jest.restoreAllMocks();
    const f2 = jest.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status: 401 }));
    const err = await getSteamPartner().queryTxn('1').catch((e: unknown) => e as PartnerCallError);
    expect(err).toMatchObject({ kind: 'rejected', httpStatus: 401 });
    expect(f2).toHaveBeenCalledTimes(1);
    expect(payPartnerStats().key_rejected_recent).toBe(true);
    expect((err as Error).message).not.toContain(KEY);
  });

  it('LOG_REDACT 에 퍼블리셔 키·견적 비밀 이름이 있다', () => {
    for (const k of ['STEAM_PUBLISHER_API_KEY', 'PAYMENT_QUOTE_SECRET', '*.key', '*.steam_key']) expect(LOG_REDACT).toContain(k);
  });
});
