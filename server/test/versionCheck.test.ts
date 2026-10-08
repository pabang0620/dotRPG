// versionCheck 미들웨어: 클라이언트 버전 -> 데이터 버전 순서, 426은 여기서만, 점검(503)이 항상 먼저
import request from 'supertest';
import { setMaintenance } from '../src/ops/maintenanceState';
import { setClockOverride } from '../src/utils/clock';
import { CLIENT_VERSION, DATA_VERSION, buildApp, resetDb, shutdown } from './helpers';
import { newHero } from './economyHelpers';
import { auth } from './helpers';

const app = buildApp(); // MIN_CLIENT_VERSION = 0.2.0 (setupEnv)
const MIN = 60_000;
const T0 = new Date('2026-10-05T03:00:00Z').getTime();

beforeAll(resetDb);
afterEach(() => {
  setMaintenance(null);
  setClockOverride(null);
});
afterAll(shutdown);

const BAD_DATA = '0123456789abcdef' === DATA_VERSION ? 'fedcba9876543210' : '0123456789abcdef';
type H = Record<string, string>;
/** 인증 없이 보내서 versionCheck가 통과하면 requireAuth의 401이 나오게 한다 */
const chars = (h: H) => request(app).get('/characters').set(h);
const friends = (h: H) => request(app).get('/friends').set(h);
const ok: H = { 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': DATA_VERSION };

describe('X-Client-Version', () => {
  it('통과하면 다음 미들웨어(인증)로 넘어간다: 401', async () => {
    expect((await chars(ok)).status).toBe(401);
  });

  it('없으면 400 CLIENT_VERSION_MISSING', async () => {
    const r = await chars({ 'X-Data-Version': DATA_VERSION });
    expect(r.status).toBe(400);
    expect(r.body.errors.code).toBe('CLIENT_VERSION_MISSING');
  });

  it.each(['abc', '1.2', '1.2.3.4', 'v1.2.3', '1.2.x', ' ', '1.2.3-beta'])('형식이 틀리면(%s) 400', async (v) => {
    const r = await chars({ ...ok, 'X-Client-Version': v });
    expect(r.status).toBe(400);
    expect(r.body.errors.code).toBe('CLIENT_VERSION_MISSING');
  });

  it('최소 버전 미만이면 426 CLIENT_OUTDATED와 두 버전을 알려준다', async () => {
    const r = await chars({ ...ok, 'X-Client-Version': '0.1.9' });
    expect(r.status).toBe(426);
    expect(r.body.success).toBe(false);
    expect(r.body.errors).toMatchObject({ code: 'CLIENT_OUTDATED', client_version: '0.1.9', min_client_version: '0.2.0' });
  });

  it('최소 버전과 같거나 높으면 통과(0.2.0, 0.10.0, 1.0.0), 숫자 비교라 0.10.0 > 0.2.0', async () => {
    for (const v of ['0.2.0', '0.10.0', '1.0.0']) expect((await chars({ ...ok, 'X-Client-Version': v })).status).toBe(401);
  });

  it('MIN_CLIENT_VERSION 설정을 따른다', async () => {
    const strict = buildApp({ MIN_CLIENT_VERSION: '1.5.0' });
    const r = await request(strict).get('/friends').set({ 'X-Client-Version': '1.4.9' });
    expect(r.status).toBe(426);
    expect(r.body.errors.min_client_version).toBe('1.5.0');
    expect((await request(strict).get('/friends').set({ 'X-Client-Version': '1.5.0' })).status).toBe(401);
    buildApp();
  });

  it('클라이언트 버전이 낮으면 데이터 버전이 틀려도 CLIENT_OUTDATED가 먼저다', async () => {
    const r = await chars({ 'X-Client-Version': '0.1.0', 'X-Data-Version': BAD_DATA });
    expect(r.status).toBe(426);
    expect(r.body.errors.code).toBe('CLIENT_OUTDATED');
  });
});

describe('X-Data-Version (/characters 계열: data=true)', () => {
  it('없으면 400 DATA_VERSION_MISSING', async () => {
    const r = await chars({ 'X-Client-Version': CLIENT_VERSION });
    expect(r.status).toBe(400);
    expect(r.body.errors.code).toBe('DATA_VERSION_MISSING');
  });

  it.each(['abc', '0123456789abcde', '0123456789abcdef0', '0123456789ABCDEF', 'ghijklmnopqrstuv'])('형식이 틀리면(%s) 400', async (v) => {
    const r = await chars({ 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': v });
    expect(r.status).toBe(400);
    expect(r.body.errors.code).toBe('DATA_VERSION_MISSING');
  });

  it('형식은 맞지만 서버와 다르면 426 DATA_OUTDATED와 두 값을 알려준다', async () => {
    const r = await chars({ 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': BAD_DATA });
    expect(r.status).toBe(426);
    expect(r.body.errors).toMatchObject({ code: 'DATA_OUTDATED', client_data_version: BAD_DATA, data_version: DATA_VERSION });
  });

  it('로그인한 상태에서도 같다: 데이터 버전이 다르면 인증보다 먼저 426', async () => {
    const h = await newHero(app);
    const r = await request(app).get('/characters').set({ ...auth(h.s), 'X-Data-Version': BAD_DATA });
    expect(r.status).toBe(426);
    expect((await request(app).get('/characters').set(auth(h.s))).status).toBe(200);
  });
});

describe('계정 단위 경로(data=false)는 데이터 버전을 보지 않는다', () => {
  it('/friends: X-Data-Version 없음·틀린 형식·다른 값이어도 통과(401), 클라이언트 버전은 검사', async () => {
    expect((await friends({ 'X-Client-Version': CLIENT_VERSION })).status).toBe(401);
    expect((await friends({ 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': 'garbage' })).status).toBe(401);
    expect((await friends({ 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': BAD_DATA })).status).toBe(401);
    expect((await friends({})).status).toBe(400);
    const old = await friends({ 'X-Client-Version': '0.0.1' });
    expect(old.status).toBe(426);
    expect(old.body.errors.code).toBe('CLIENT_OUTDATED');
  });

  it.each(['/blocks', '/level-rewards', '/reports', '/payments'])('%s 도 데이터 버전 없이 통과', async (path) => {
    const r = await request(app).get(path).set({ 'X-Client-Version': CLIENT_VERSION });
    expect(r.status).not.toBe(400);
    expect(r.status).not.toBe(426);
  });
});

describe('점검 모드와의 순서(maintenanceGuard가 먼저)', () => {
  const activeWindow = () => {
    setClockOverride(() => new Date(T0));
    setMaintenance({
      uuid: '11111111-1111-1111-1111-111111111111',
      notice: '',
      blockLoginAt: new Date(T0 - 20 * MIN),
      startsAt: new Date(T0 - 10 * MIN),
      endsAt: new Date(T0 + 10 * MIN),
    });
  };

  it('점검 중이면 오래된 클라이언트·데이터 버전·헤더 없음 모두 503 MAINTENANCE', async () => {
    activeWindow();
    for (const h of [{}, { 'X-Client-Version': '0.0.1' }, { 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': BAD_DATA }, ok]) {
      const r = await chars(h);
      expect(r.status).toBe(503);
      expect(r.body.errors.code).toBe('MAINTENANCE');
    }
    expect((await friends({ 'X-Client-Version': '0.0.1' })).status).toBe(503);
  });

  it('점검이 끝나면(ends_at 이후) 다시 426이 나온다', async () => {
    activeWindow();
    setClockOverride(() => new Date(T0 + 11 * MIN));
    const r = await chars({ ...ok, 'X-Client-Version': '0.0.1' });
    expect(r.status).toBe(426);
  });

  it('점검 예고(scheduled)는 아무것도 막지 않아 버전 검사가 그대로 동작한다', async () => {
    setClockOverride(() => new Date(T0));
    setMaintenance({
      uuid: '11111111-1111-1111-1111-111111111111',
      notice: '',
      blockLoginAt: new Date(T0 + 20 * MIN),
      startsAt: new Date(T0 + 30 * MIN),
      endsAt: new Date(T0 + 50 * MIN),
    });
    expect((await chars({ ...ok, 'X-Client-Version': '0.0.1' })).status).toBe(426);
  });

  it('/health는 버전 헤더 없이 점검 중에도 응답한다', async () => {
    activeWindow();
    const r = await request(app).get('/health');
    expect(r.status).not.toBe(503);
    expect(r.status).not.toBe(400);
  });
});
