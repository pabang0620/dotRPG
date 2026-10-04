import request from 'supertest';
import { loadConfig } from '../src/config/env';
import { resetSteamStats, steamSelfCheck, steamStats, webApiVerifier } from '../src/domains/auth/steamProvider';
import { evaluate, type RuleInput } from '../src/ops/alertRules';
import { collectSnapshot } from '../src/ops/snapshot';
import { buildApp, resetDb, shutdown, ver } from './helpers';
import { sleep } from './wsHelpers';

afterAll(shutdown);
beforeAll(resetDb);

const STEAM = { STEAM_AUTH_MODE: 'web_api', STEAM_APP_ID: '480', STEAM_WEB_API_KEY: 'test-key', DEPLOY_STAGE: 'test' };
const okBody = { response: { params: { result: 'OK', steamid: '76561198000000001', ownersteamid: '76561198000000001', publisherbanned: false } } };
const json = (status: number, body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } }));

describe('Steam Web API 강화(S1~S7)', () => {
  let spy: jest.SpyInstance;
  beforeEach(() => {
    buildApp(STEAM);
    resetSteamStats();
    spy = jest.spyOn(globalThis, 'fetch');
  });
  afterEach(() => spy.mockRestore());

  it('정상: 티켓을 검증하고(키는 로그가 아닌 호출에만), STEAM_WEB_API_BASE 를 따른다', async () => {
    buildApp({ ...STEAM, STEAM_WEB_API_BASE: 'https://partner.steam-api.com' });
    spy.mockImplementation(() => json(200, okBody));
    const id = await webApiVerifier.verify('ab12');
    expect(id).toMatchObject({ steamId: '76561198000000001', publisherBanned: false });
    const url = new URL(String(spy.mock.calls[0]?.[0]));
    expect(url.host).toBe('partner.steam-api.com');
    expect(url.pathname).toBe('/ISteamUserAuth/AuthenticateUserTicket/v1/');
    expect(url.searchParams.get('appid')).toBe('480');
    expect(url.searchParams.get('identity')).toBe('dotrpg-server');
    expect(steamStats()).toMatchObject({ ok: 1, fail: 0, misconfigured_recent: false });
  });

  it('입력 오류: 무효 티켓(응답 error 또는 result != OK)은 401 STEAM_TICKET_INVALID, 재시도하지 않는다', async () => {
    spy.mockImplementation(() => json(200, { response: { error: { errorcode: 3, errordesc: 'Invalid ticket' } } }));
    await expect(webApiVerifier.verify('zz')).rejects.toMatchObject({ status: 401, code: 'STEAM_TICKET_INVALID' });
    spy.mockImplementation(() => json(200, { response: { params: { result: 'Fail' } } }));
    await expect(webApiVerifier.verify('zz')).rejects.toMatchObject({ status: 401, code: 'STEAM_TICKET_INVALID' });
    expect(spy).toHaveBeenCalledTimes(2);
    expect(steamStats().invalid).toBe(2);
  });

  it('키·앱 ID 거절(400/401/403)은 우리 설정 오류: 401이 아니라 503 STEAM_UNAVAILABLE + 긴급 알림 플래그, 재시도 없음', async () => {
    spy.mockImplementation(() => json(403, { error: 'Forbidden' }));
    await expect(webApiVerifier.verify('zz')).rejects.toMatchObject({ status: 503, code: 'STEAM_UNAVAILABLE' });
    expect(spy).toHaveBeenCalledTimes(1);
    expect(steamStats().misconfigured_recent).toBe(true);
    const snap = await collectSnapshot();
    expect(snap.steam?.misconfigured_recent).toBe(true);
    const alerts = evaluate({ snap, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 });
    expect(alerts.find((a) => a.key === 'steam_auth_misconfigured')).toMatchObject({ level: 'critical' });
  });

  it('네트워크 오류·5xx는 1회 재시도하고, 그래도 안 되면 503', async () => {
    spy.mockImplementationOnce(() => json(500, {})).mockImplementationOnce(() => json(200, okBody));
    expect(await webApiVerifier.verify('ab')).toMatchObject({ steamId: '76561198000000001' });
    expect(spy).toHaveBeenCalledTimes(2);
    spy.mockReset();
    spy.mockImplementation(() => Promise.reject(new Error('ECONNRESET')));
    await expect(webApiVerifier.verify('ab')).rejects.toMatchObject({ status: 503, code: 'STEAM_UNAVAILABLE' });
    expect(spy).toHaveBeenCalledTimes(2);
  });

  it('회로 차단: 연속 5회 실패하면 30초(시험에서는 1초) 동안 Steam 호출 없이 503 + Retry-After 를 즉시 돌려준다', async () => {
    buildApp({ ...STEAM, STEAM_WEB_API_RETRIES: '0', STEAM_BREAKER_OPEN_SECONDS: '1' });
    spy.mockImplementation(() => Promise.reject(new Error('down')));
    for (let i = 0; i < 5; i++) await expect(webApiVerifier.verify('ab')).rejects.toMatchObject({ status: 503 });
    expect(spy).toHaveBeenCalledTimes(5);
    expect(steamStats().breaker_open).toBe(true);
    await expect(webApiVerifier.verify('ab')).rejects.toMatchObject({ status: 503, extra: { retry_after_sec: expect.any(Number) } });
    expect(spy).toHaveBeenCalledTimes(5);
    await sleep(1100);
    spy.mockImplementation(() => json(200, okBody));
    expect(await webApiVerifier.verify('ab')).toBeTruthy();
    expect(steamStats().breaker_open).toBe(false);
  });

  it('기동 자가 점검: 무효 티켓 응답이면 정상(invalid), 403이면 rejected 를 돌려준다', async () => {
    spy.mockImplementation(() => json(200, { response: { error: { errorcode: 3 } } }));
    expect(await steamSelfCheck()).toBe('invalid');
    spy.mockImplementation(() => json(403, {}));
    expect(await steamSelfCheck()).toBe('rejected');
  });
});

describe('/meta 와 /health/ready', () => {
  it('meta: relay.enabled, steam.identity/app_id, combat_transport_order 를 공개한다', async () => {
    const app = buildApp({ ...STEAM, COMBAT_TRANSPORT_ORDER: 'relay,steam', STEAM_IDENTITY: 'my-identity' });
    const res = await request(app).get('/meta');
    expect(res.body.data).toMatchObject({ relay: { enabled: true }, steam: { identity: 'my-identity', app_id: 480 }, combat_transport_order: 'relay,steam' });
    const ready = await request(app).get('/health/ready').set(ver());
    expect(ready.status).toBe(200);
  });
});

describe('설정 검증(DEPLOY_STAGE, 중계 가드 G2/G8/G9/G10)', () => {
  const JWT = 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw';
  const SECRET = 'Qm8vN2xK5pL7wR3tY6uZ9aB4cD1eF0gHiJkLmNoPq';
  const base = { DATABASE_URL: 'x', MIN_CLIENT_VERSION: '0.1.0', JWT_SECRET: JWT };
  const prod = {
    ...base,
    NODE_ENV: 'production',
    STEAM_AUTH_MODE: 'web_api',
    STEAM_APP_ID: '2800000',
    STEAM_WEB_API_KEY: 'k',
    TRUST_PROXY: '1',
    ADMIN_SECRET_KEY: Buffer.alloc(32, 9).toString('base64'),
    RELAY_TICKET_SECRET: SECRET,
    RELAY_PUBLIC_URL: 'wss://game.example.org/relay',
  };

  it('G5: 480은 DEPLOY_STAGE=live(기본)에서 거부하고 test 에서는 허용한다. test 에서는 시험 구성(개발 로그인)도 허용', () => {
    expect(() => loadConfig({ ...prod, STEAM_APP_ID: '480' })).toThrow(/480/);
    expect(() => loadConfig({ ...prod, STEAM_APP_ID: '480', DEPLOY_STAGE: 'live' })).toThrow(/480/);
    const t = loadConfig({ ...prod, STEAM_APP_ID: '480', DEPLOY_STAGE: 'test' });
    expect(t).toMatchObject({ deployStage: 'test' });
    expect(t.steam.appId).toBe(480);
    expect(() => loadConfig({ ...prod, AUTH_DEV_ENABLED: 'true' })).toThrow(/ALLOW_DEV_AUTH_IN_PRODUCTION/);
    expect(loadConfig({ ...prod, AUTH_DEV_ENABLED: 'true', DEPLOY_STAGE: 'test', STEAM_AUTH_MODE: 'off' }).authDevEnabled).toBe(true);
  });

  it('결정 2: 전송 우선순위 기본값은 출시(live) relay,steam / 시험(test) steam,relay, 명시하면 그대로, 옛 PARTY_TRANSPORT 는 파생', () => {
    expect(loadConfig(prod).transport.order).toEqual(['relay', 'steam']);
    expect(loadConfig({ ...prod, DEPLOY_STAGE: 'test' }).transport.order).toEqual(['steam', 'relay']);
    expect(loadConfig({ ...prod, COMBAT_TRANSPORT_ORDER: 'steam' }).transport.order).toEqual(['steam']);
    expect(loadConfig({ ...base, PARTY_TRANSPORT: 'steam' }).transport.order).toEqual(['steam', 'relay']);
    expect(loadConfig({ ...base, PARTY_TRANSPORT: 'dev' }).transport.order).toEqual(['relay', 'dev']);
    expect(loadConfig({ ...base, RELAY_ENABLED: 'false' }).transport.order).toEqual(['relay', 'steam'].filter((t) => t !== 'relay'));
  });

  it('G2: 운영에서 COMBAT_TRANSPORT_ORDER 에 dev 가 있거나 알 수 없는 전송이면 거부', () => {
    expect(() => loadConfig({ ...prod, COMBAT_TRANSPORT_ORDER: 'relay,dev' })).toThrow(/dev/);
    expect(() => loadConfig({ ...prod, COMBAT_TRANSPORT_ORDER: 'relay,udp' })).toThrow(/udp/);
    expect(loadConfig({ ...base, COMBAT_TRANSPORT_ORDER: 'relay,dev' }).transport.order).toContain('dev');
  });

  it('G8: 운영에서 RELAY_TICKET_SECRET 필수·32자 이상·JWT_SECRET 과 다름·자리표시 거부, wss 공개 주소 필수', () => {
    const { RELAY_TICKET_SECRET: _s, ...noSecret } = prod;
    expect(() => loadConfig(noSecret)).toThrow(/RELAY_TICKET_SECRET/);
    expect(() => loadConfig({ ...prod, RELAY_TICKET_SECRET: 'short' })).toThrow(/32자/);
    expect(() => loadConfig({ ...prod, RELAY_TICKET_SECRET: JWT })).toThrow(/달라야/);
    expect(() => loadConfig({ ...prod, RELAY_TICKET_SECRET: 'changeme-changeme-changeme-changeme-0000' })).toThrow(/자리표시/);
    const { RELAY_PUBLIC_URL: _u, ...noUrl } = prod;
    expect(() => loadConfig(noUrl)).toThrow(/RELAY_PUBLIC_URL/);
    expect(() => loadConfig({ ...prod, RELAY_PUBLIC_URL: 'ws://x/relay' })).toThrow(/wss/);
    expect(loadConfig(prod).relay).toMatchObject({ enabled: true, publicUrl: 'wss://game.example.org/relay' });
    // 개발에서는 JWT 에서 파생한 값으로 뜬다(JWT_SECRET 과 다르다)
    const dev = loadConfig(base);
    expect(dev.relay.ticketSecret).not.toBe(JWT);
    expect(dev.relay.ticketSecret.length).toBeGreaterThanOrEqual(32);
    expect(loadConfig({ ...prod, RELAY_ENABLED: 'false', RELAY_TICKET_SECRET: '' }).relay.enabled).toBe(false);
  });

  it('한도 값은 양수여야 한다(0 이하 거부), 기본값은 설계 11절과 같다', () => {
    expect(() => loadConfig({ ...base, RELAY_FRAME_MAX_BYTES: '0' })).toThrow(/RELAY_FRAME_MAX_BYTES/);
    expect(() => loadConfig({ ...base, RELAY_MAX_CONNECTIONS: '-1' })).toThrow(/RELAY_MAX_CONNECTIONS/);
    const c = loadConfig(base);
    expect(c.relay).toMatchObject({ ticketTtlSeconds: 60, maxConnections: 400, maxRooms: 120, frameMaxBytes: 8192, packetMaxBytes: 6144, flushMs: 20, hostGraceMs: 3000, strikesPerMin: 20, roomEgressBps: 262144 });
    expect(c.field).toMatchObject({ electionWindowSeconds: 5, staleSeconds: 300, uncreditedMax: 24, creditSurplus: 8, carrySlack: 5, carryStep: 0.12, carryMin: 0.2 });
    expect(c.steam).toMatchObject({ webApiBase: 'https://api.steampowered.com', retries: 1, breakerFailures: 5, breakerOpenSeconds: 30 });
  });
});

describe('중계 알림 규칙(4.12)', () => {
  it('용량 80%, 느린 소비자, 플러시 지연, RTT, 남용, steam->relay 전환, 중계 정지를 경고·긴급으로 올린다', async () => {
    buildApp({});
    const snap = await collectSnapshot();
    const relay = { ...(snap.relay as NonNullable<typeof snap.relay>) };
    const input = (over: Partial<NonNullable<typeof snap.relay>>): RuleInput => ({ snap: { ...snap, relay: { ...relay, ...over } }, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 });
    const keys = (over: Partial<NonNullable<typeof snap.relay>>) => evaluate(input(over)).map((a) => a.key);
    expect(keys({})).not.toContain('relay_capacity');
    expect(keys({ conns: 330, max_conns: 400 })).toContain('relay_capacity');
    expect(keys({ close_codes_1m: { '4008': 11 } })).toContain('relay_slow_consumer');
    expect(keys({ close_codes_1m: { '4013': 4, '1000': 6 } })).toContain('relay_peer_timeout');
    expect(keys({ flush_lag_p99_ms: 61 })).toContain('relay_flush_lag');
    expect(keys({ egress_24h_gb: 9, egress_budget_gb: 10 })).toContain('relay_egress');
    expect(keys({ rtt_p95_ms: 300, rtt_high_for_s: 700 })).toContain('relay_rtt');
    expect(keys({ abuse_10m: 5 })).toContain('relay_abuse');
    expect(keys({ steam_to_relay_10m: 5 })).toContain('transport_fallback');
    const crit = evaluate(input({ unavailable_for_s: 301 })).find((a) => a.key === 'relay_unavailable');
    expect(crit).toMatchObject({ level: 'critical' });
  });
});
