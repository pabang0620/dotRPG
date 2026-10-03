import fs from 'node:fs';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { createAdminApp } from '../src/admin/adminApp';
import { createOwner } from '../src/admin/bootstrap';
import { runCli } from '../src/admin/cli/cli';
import { parseMinutes } from '../src/admin/cli/cliCommands';
import { render } from '../src/admin/cli/cliFormat';
import { base32Decode } from '../src/admin/common/totp';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { setMaintenance } from '../src/ops/maintenanceState';
import { buildApp, resetDb, shutdown } from './helpers';
import { codeFor } from './opsHelpers';

buildApp();
let server: http.Server;
let baseUrl: string;
let dir: string;

beforeAll(async () => {
  server = http.createServer(createAdminApp());
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});
beforeEach(async () => {
  await resetDb();
  getRateLimitStore().clear();
  setMaintenance(null);
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cli-'));
});
afterEach(() => fs.rmSync(dir, { recursive: true, force: true }));
afterAll(async () => {
  await new Promise((r) => server.close(r));
  await shutdown();
});

function makeIo(answers: (string | (() => string))[] = []) {
  const out: string[] = [];
  const err: string[] = [];
  const asked: string[] = [];
  return {
    out,
    err,
    asked,
    io: {
      out: (l: string): void => void out.push(l),
      err: (l: string): void => void err.push(l),
      prompt: async (q: string) => {
        asked.push(q);
        const a = answers.shift();
        return typeof a === 'function' ? a() : (a ?? '');
      },
      fetch,
      baseUrl,
      sessionFile: path.join(dir, 'session'),
    },
  };
}

describe('dotrpg-admin CLI', () => {
  it('첫 로그인 설정(비밀번호 변경, TOTP 등록) -> TOTP 로그인 -> 명령 실행 -> 로그아웃. 세션 파일은 0600', async () => {
    const owner = await createOwner('cli-owner');
    const newPw = 'a-long-new-password-77';

    // 1) 임시 비밀번호로 로그인하면 setup 세션
    let t = makeIo(['cli-owner', owner.tempPassword]);
    expect(await runCli(['login'], t.io)).toBe(0);
    expect(t.out.join('\n')).toContain('첫 로그인');
    expect((fs.statSync(t.io.sessionFile).mode & 0o777).toString(8)).toBe('600');
    // 비밀번호는 프롬프트로만 받는다(인자에 없다)
    expect(t.asked).toEqual(['아이디: ', '비밀번호: ']);

    // setup 상태에서 일반 명령은 막힌다
    t = makeIo();
    expect(await runCli(['admins', 'list'], t.io)).toBe(1);
    expect(t.err.join('\n')).toContain('SETUP_REQUIRED');

    // 2) 비밀번호 변경
    t = makeIo([owner.tempPassword, newPw, newPw]);
    expect(await runCli(['passwd'], t.io)).toBe(0);
    // 3) TOTP 등록: 출력된 비밀키로 코드를 만들어 입력한다
    let secret = '';
    t = makeIo([() => codeFor(base32Decode(secret))]);
    const originalOut = t.io.out;
    t.io.out = (l: string) => {
      const m = /^비밀키: (\S+)/.exec(l);
      if (m) secret = m[1] as string;
      originalOut(l);
    };
    expect(await runCli(['totp', 'enroll'], t.io)).toBe(0);
    expect(t.out.join('\n')).toContain('전체 권한 세션');

    // 4) 이제 일반 명령이 된다(--json은 data만 출력)
    t = makeIo();
    expect(await runCli(['whoami', '--json'], t.io)).toBe(0);
    expect(JSON.parse(t.out[0] as string)).toMatchObject({ login_id: 'cli-owner', role: 'owner', scope: 'full' });
    t = makeIo();
    expect(await runCli(['admins', 'list'], t.io)).toBe(0);
    expect(t.out.join('\n')).toContain('cli-owner');

    // 5) 로그아웃 후에는 로그인이 필요하다. 다시 로그인하면 TOTP 코드를 묻는다(다음 구간 코드)
    t = makeIo();
    expect(await runCli(['logout'], t.io)).toBe(0);
    expect(fs.existsSync(t.io.sessionFile)).toBe(false);
    t = makeIo();
    expect(await runCli(['whoami'], t.io)).toBe(1);
    expect(t.err.join('\n')).toContain('로그인이 필요합니다');
    t = makeIo(['cli-owner', newPw, () => codeFor(base32Decode(secret), 1)]);
    expect(await runCli(['login'], t.io)).toBe(0);
    expect(t.asked).toEqual(['아이디: ', '비밀번호: ', '인증 코드(6자리): ']);
    expect(t.out).toContain('로그인했습니다.');
  });

  it('점검 예약·취소와 서버 상태 명령, 오류 출력(코드와 필드), 알 수 없는 명령', async () => {
    const owner = await createOwner('cli-owner2');
    // 설정을 DB로 바로 끝낸다(첫 로그인 흐름은 위 테스트가 다룬다)
    const { sessionFor } = await import('./opsHelpers');
    const { getPool } = await import('../src/db/pool');
    const id = Number((await getPool().query('SELECT id FROM admin_users WHERE login_id = $1', ['cli-owner2'])).rows[0].id);
    const token = await sessionFor(id);
    void owner;
    const io0 = makeIo();
    fs.writeFileSync(io0.io.sessionFile, JSON.stringify({ token, expires_at: new Date(Date.now() + 3_600_000).toISOString(), url: baseUrl }));

    let t = makeIo();
    expect(await runCli(['maint', 'schedule', '--in', '30m', '--duration', '20m', '--notice', '업데이트', '--json'], t.io)).toBe(0);
    const win = JSON.parse(t.out[0] as string).window as { id: string; state: string };
    expect(win.state).toBe('scheduled');
    t = makeIo();
    expect(await runCli(['maint', 'status'], t.io)).toBe(0);
    expect(t.out.join('\n')).toContain('scheduled');
    t = makeIo();
    expect(await runCli(['maint', 'schedule', '--in', '5m', '--duration', '20m'], t.io)).toBe(1);
    expect(t.err.join('\n')).toContain('MAINTENANCE_EXISTS');
    t = makeIo();
    expect(await runCli(['maint', 'cancel', win.id], t.io)).toBe(0);

    t = makeIo();
    expect(await runCli(['ops', 'status', '--json'], t.io)).toBe(0);
    expect(JSON.parse(t.out[0] as string)).toMatchObject({ maintenance: { phase: 'none' }, shutting_down: false });
    t = makeIo();
    expect(await runCli(['ops', 'run', 'stale-runs'], t.io)).toBe(0);
    t = makeIo();
    expect(await runCli(['grant', 'add', 'not-a-uuid', '--code', 'event', '--gold', '10', '--memo', 'x'], t.io)).toBe(1);
    expect(t.err.join('\n')).toContain('VALIDATION');
    expect(t.err.join('\n')).toContain('character_id');
    t = makeIo();
    expect(await runCli(['nonsense', 'command'], t.io)).toBe(2);
    t = makeIo();
    expect(await runCli(['sanction', 'add'], t.io)).toBe(1); // uuid 없음
    expect(t.err.join('\n')).toContain('uuid');
    t = makeIo();
    expect(await runCli(['--bogus'], t.io)).toBe(2);
    t = makeIo();
    expect(await runCli([], t.io)).toBe(2);
  });

  it('시간 문자열과 표 출력 도우미', () => {
    expect(parseMinutes('30m')).toBe(30);
    expect(parseMinutes('2h')).toBe(120);
    expect(parseMinutes('1d')).toBe(1440);
    expect(() => parseMinutes('10')).toThrow(/형식/);
    expect(render({ rows: [{ a: 1, b: 'x' }, { a: 22, b: null }], n: 3 })).toEqual(['rows:', '  a   b', '  --  -', '  1   x', '  22  -', 'n: 3']);
  });
});
