// 9단계 10절: 캐릭터 이름 예약어·금칙어
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { forbiddenReason, skeletonOf } from '../src/domains/antiabuse/reservedNames';
import { resetAntiAbuseDataCache } from '../src/gamedata/antiAbuseData';
import { buildApp, createChar, DATA_DIR, registerAccount, resetDb, shutdown } from './helpers';

const app = buildApp();
beforeEach(async () => {
  await resetDb();
  resetAntiAbuseDataCache();
  buildApp();
});
afterAll(async () => {
  resetAntiAbuseDataCache();
  await shutdown();
});

describe('판정(순수)', () => {
  it('예약어: 접기·숫자 제거·토큰 시작/끝·부분 문자열', () => {
    for (const name of ['운영자', '운영자1', 'GM', 'gm01', 'Gm철수', 'admin', 'Adm1n', '4dmin', '공지', '시스템2', '개발자님', 'AdminTest', 'admin7']) {
      expect([name, forbiddenReason(name)]).toEqual([name, 'RESERVED']);
    }
    for (const name of ['Pigmy', '길동', 'Hero77', '용사하나']) expect([name, forbiddenReason(name)]).toEqual([name, null]);
    expect(skeletonOf('4dm1n')).toBe('admin');
    expect(skeletonOf('aaaa')).toBe('aa');
  });

  it('욕설 금칙어: 원형, 숫자로 숨긴 변형', () => {
    expect(forbiddenReason('시발')).toBe('BANNED');
    expect(forbiddenReason('씨1발')).toBe('BANNED');
    expect(forbiddenReason('병신아')).toBe('BANNED');
  });
});

describe('POST /characters', () => {
  it('운영 예약어는 422 NAME_FORBIDDEN(reason RESERVED), 욕설은 reason BANNED(어느 단어인지는 알리지 않는다), 통과 이름은 201', async () => {
    const s = await registerAccount(app);
    const res = await createChar(app, s, '운영자');
    expect(res.status).toBe(422);
    expect(res.body.errors).toEqual({ code: 'NAME_FORBIDDEN', reason: 'RESERVED' });
    expect(JSON.stringify(res.body)).not.toContain('운영자1');
    const bad = await createChar(app, s, '시발');
    expect(bad.body.errors).toEqual({ code: 'NAME_FORBIDDEN', reason: 'BANNED' });
    expect((await createChar(app, s, '개발자님')).body.errors.reason).toBe('RESERVED');
    expect((await createChar(app, s, 'Pigmy')).status).toBe(201);
    expect((await createChar(app, s, '길동')).status).toBe(201);
  });

  it('문자 규칙이 먼저: 공백이 낀 "시 스 템"은 400 VALIDATION, 길이 규칙도 먼저', async () => {
    const s = await registerAccount(app);
    expect((await createChar(app, s, '시 스 템')).status).toBe(400);
    expect((await createChar(app, s, '가')).status).toBe(400);
  });

  it('allow 목록의 이름은 통과하고, 파일이 없으면 빈 목록으로 동작하며, NAME_RESERVED_MODE=off는 검사하지 않는다', async () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-'));
    for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
    const raw = JSON.parse(fs.readFileSync(path.join(dir, 'reserved_names.json'), 'utf8')) as Record<string, unknown>;
    raw.allow = ['Gm철수'];
    fs.writeFileSync(path.join(dir, 'reserved_names.json'), JSON.stringify(raw));
    const allowApp = buildApp({ GAME_DATA_DIR: dir });
    const s = await registerAccount(allowApp);
    expect((await createChar(allowApp, s, 'Gm철수')).status).toBe(201);
    expect((await createChar(allowApp, s, 'Gm영희')).status).toBe(422);
    // 파일이 없다
    fs.rmSync(path.join(dir, 'reserved_names.json'));
    resetAntiAbuseDataCache();
    expect((await createChar(allowApp, s, '운영자')).status).toBe(201);
    const off = buildApp({ NAME_RESERVED_MODE: 'off' });
    const s2 = await registerAccount(off);
    expect((await createChar(off, s2, '공지')).status).toBe(201);
    buildApp();
  });
});
