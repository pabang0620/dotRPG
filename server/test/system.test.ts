import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { loadConfig } from '../src/config/env';
import { loadGameData } from '../src/gamedata/loader';
import { resetBoundaries } from '../src/utils/resetBoundaries';
import { DATA_DIR, DATA_VERSION, buildApp, shutdown } from './helpers';

afterAll(shutdown);

describe('GET /health, /meta', () => {
  const app = buildApp();

  it('health: 정상(버전 헤더 없이)', async () => {
    const res = await request(app).get('/health');
    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ success: true, data: { status: 'ok', db: 'ok' } });
  });

  it('meta: 버전 헤더 없이도 응답하고 no-store', async () => {
    const res = await request(app).get('/meta');
    expect(res.status).toBe(200);
    expect(res.headers['cache-control']).toBe('no-store');
    expect(res.body.data).toMatchObject({
      min_client_version: '0.2.0',
      data_version: DATA_VERSION,
      auth: { dev_login_enabled: true },
      character: { max_per_account: 4, name_min: 2, name_max: 8 },
    });
    expect(Date.parse(res.body.data.server_time)).not.toBeNaN();
    expect(res.body.data.reset.next_daily_at).toMatch(/Z$/);
  });

  it('없는 경로는 404 NOT_FOUND', async () => {
    const res = await request(app).get('/nope');
    expect(res.status).toBe(404);
    expect(res.body.errors.code).toBe('NOT_FOUND');
  });
});

describe('resetBoundaries (KST 06:00, 목요일)', () => {
  it('일일 경계 직전은 같은 날 06:00 KST, 직후는 다음 날', () => {
    // 2026-10-05(월) 05:59:59 KST = 2026-10-04T20:59:59Z
    expect(resetBoundaries(new Date('2026-10-04T20:59:59Z')).nextDailyAt).toBe('2026-10-04T21:00:00.000Z');
    // 정확히 06:00:00 KST 는 이미 지난 경계로 본다
    expect(resetBoundaries(new Date('2026-10-04T21:00:00Z')).nextDailyAt).toBe('2026-10-05T21:00:00.000Z');
  });

  it('주간 경계는 목요일 06:00 KST', () => {
    // 2026-10-08(목) 05:59:59 KST 직전 -> 그날 06:00 KST
    expect(resetBoundaries(new Date('2026-10-07T20:59:59Z')).nextWeeklyAt).toBe('2026-10-07T21:00:00.000Z');
    // 목요일 06:00 정각 이후 -> 다음 주 목요일
    expect(resetBoundaries(new Date('2026-10-07T21:00:00Z')).nextWeeklyAt).toBe('2026-10-14T21:00:00.000Z');
    // 월요일에서는 같은 주 목요일
    expect(resetBoundaries(new Date('2026-10-05T03:00:00Z')).nextWeeklyAt).toBe('2026-10-07T21:00:00.000Z');
  });
});

describe('환경변수 검증', () => {
  it('필수 값이 없으면 던진다', () => {
    expect(() => loadConfig({ NODE_ENV: 'test' })).toThrow(/환경변수 검증 실패/);
    expect(() =>
      loadConfig({ DATABASE_URL: 'x', JWT_SECRET: 'short', MIN_CLIENT_VERSION: '0.1.0' }),
    ).toThrow(/JWT_SECRET/);
  });

  it('production 에서는 개발용 로그인 기본값이 꺼진다', () => {
    const base = { DATABASE_URL: 'x', JWT_SECRET: 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw', MIN_CLIENT_VERSION: '0.1.0' };
    // 운영 값(7단계 가드): Steam web_api, steam 전송, 프록시 1단, 관리자 비밀키
    const prod = {
      ...base,
      NODE_ENV: 'production',
      STEAM_AUTH_MODE: 'web_api',
      STEAM_APP_ID: '2800000',
      STEAM_WEB_API_KEY: 'k',
      PARTY_TRANSPORT: 'steam',
      TRUST_PROXY: '1',
      RELAY_TICKET_SECRET: 'Qm8vN2xK5pL7wR3tY6uZ9aB4cD1eF0gHiJkLmNoPq', RELAY_PUBLIC_URL: 'wss://game.example.org/relay',
      ADMIN_SECRET_KEY: Buffer.alloc(32, 9).toString('base64'),
    };
    expect(loadConfig(prod).authDevEnabled).toBe(false);
    expect(loadConfig(prod).authDevRegisterEnabled).toBe(false);
    expect(loadConfig({ ...base, NODE_ENV: 'development' }).authDevEnabled).toBe(true);
  });
});

describe('게임 데이터 로더', () => {
  it('실제 데이터를 읽는다', () => {
    const d = loadGameData(DATA_DIR);
    expect(d.dataVersion).toBe(DATA_VERSION);
    expect(d.maps.get('village')?.instanced).toBe(false);
  });

  function copyData(mutate: (dir: string) => void): string {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-'));
    for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
    mutate(dir);
    return dir;
  }

  it('알 수 없는 필드는 무시한다', () => {
    const dir = copyData((d) => {
      const p = path.join(d, 'enums.json');
      const j = JSON.parse(fs.readFileSync(p, 'utf8'));
      j.futureField = { a: 1 };
      fs.writeFileSync(p, JSON.stringify(j));
    });
    expect(() => loadGameData(dir)).not.toThrow();
  });

  it('시작 지급 아이템이 items.json에 없으면 실패한다', () => {
    const dir = copyData((d) => {
      const p = path.join(d, 'starter.json');
      const j = JSON.parse(fs.readFileSync(p, 'utf8'));
      j.items.push({ itemKey: 'no_such_item', count: 1 });
      fs.writeFileSync(p, JSON.stringify(j));
    });
    expect(() => loadGameData(dir)).toThrow(/no_such_item/);
  });

  it('schema 가 1이 아니면 실패한다', () => {
    const dir = copyData((d) => {
      const p = path.join(d, 'maps.json');
      const j = JSON.parse(fs.readFileSync(p, 'utf8'));
      j.schema = 2;
      fs.writeFileSync(p, JSON.stringify(j));
    });
    expect(() => loadGameData(dir)).toThrow(/maps\.json/);
  });
});
