// dotrpg-admin: 관리자 API의 CLI(phase7_ops.md 5.1). node:util parseArgs만 쓴다(새 의존성 없음).
//   docker compose exec api node dist/admin/cli/cli.js <명령> [--json]
import { parseArgs } from 'node:util';
import { call, clearSession, defaultSessionFile, readSession, ttyPrompt, writeSession, type ApiResult, type CliIo } from './cliClient';
import { COMMANDS, newRequestId, type CmdCtx } from './cliCommands';
import { render } from './cliFormat';

const OPTIONS = {
  json: { type: 'boolean' },
  help: { type: 'boolean' },
  name: { type: 'string' },
  role: { type: 'string' },
  limit: { type: 'string' },
  before: { type: 'string' },
  since: { type: 'string' },
  admin: { type: 'string' },
  target: { type: 'string' },
  action: { type: 'string' },
  kind: { type: 'string' },
  reason: { type: 'string' },
  for: { type: 'string' },
  report: { type: 'string' },
  note: { type: 'string' },
  state: { type: 'string' },
  account: { type: 'string' },
  character: { type: 'string' },
  request: { type: 'string' },
  'min-severity': { type: 'string' },
  day: { type: 'string' },
  code: { type: 'string' },
  gold: { type: 'string' },
  item: { type: 'string' },
  count: { type: 'string' },
  memo: { type: 'string' },
  in: { type: 'string' },
  at: { type: 'string' },
  duration: { type: 'string' },
  notice: { type: 'string' },
  by: { type: 'string' },
  dismiss: { type: 'boolean' },
  'no-merge': { type: 'boolean' },
  full: { type: 'boolean' },
  // 10단계 운영 우편 캠페인
  file: { type: 'string' },
  status: { type: 'string' },
  revoke: { type: 'boolean' },
  // 9단계 재화 이상 정지
  q: { type: 'string' },
  cursor: { type: 'string' },
  linked: { type: 'boolean' },
  'no-gold': { type: 'boolean' },
  'no-items': { type: 'boolean' },
  'no-mail': { type: 'boolean' },
} as const;

const usage = (): string[] => [
  '사용법: dotrpg-admin <명령> [인자] [--json]',
  '  login | logout | passwd | totp enroll',
  ...COMMANDS.map((c) => `  ${c.usage}`),
];

function show(io: CliIo, json: boolean, r: ApiResult): void {
  if (json) io.out(JSON.stringify(r.body.data ?? r.body));
  else {
    if (r.body.message) io.out(r.body.message);
    for (const l of render(r.body.data)) io.out(l);
    if (r.body.meta?.next_before) io.out(`다음 페이지: --before ${String(r.body.meta.next_before)}`);
  }
}

function fail(io: CliIo, r: ApiResult): number {
  const code = (r.body.errors as { code?: string } | undefined)?.code;
  io.err(`오류 ${r.status}${code ? ` ${code}` : ''}: ${r.body.message ?? ''}`);
  const fields = (r.body.errors as { fields?: { path: string; message: string }[] } | undefined)?.fields;
  for (const f of fields ?? []) io.err(`  ${f.path}: ${f.message}`);
  return 1;
}

/** 로그인: 아이디·비밀번호(·TOTP)를 프롬프트로 받는다. setup 세션이면 비밀번호 변경과 TOTP 등록을 이어서 안내한다 */
async function login(io: CliIo): Promise<number> {
  const loginId = (await io.prompt('아이디: ', false)).trim();
  const password = await io.prompt('비밀번호: ', true);
  let r = await call(io, 'POST', '/admin/auth/login', { body: { login_id: loginId, password } });
  if (r.status === 401 && (r.body.errors as { code?: string } | undefined)?.code === 'TOTP_REQUIRED') {
    const totp = (await io.prompt('인증 코드(6자리): ', true)).trim();
    r = await call(io, 'POST', '/admin/auth/login', { body: { login_id: loginId, password, totp } });
  }
  if (!r.ok) return fail(io, r);
  const d = r.body.data as { token: string; expires_at: string; scope: string; must_change_password: boolean };
  writeSession(io.sessionFile, { token: d.token, expires_at: d.expires_at, url: io.baseUrl });
  io.out(d.scope === 'full' ? '로그인했습니다.' : '첫 로그인입니다. dotrpg-admin passwd 와 dotrpg-admin totp enroll 을 먼저 끝내 주세요.');
  return 0;
}

async function setupCall(io: CliIo, route: string, body: Record<string, unknown>, token: string): Promise<ApiResult> {
  return call(io, 'POST', route, { body, token });
}

function saveUpgrade(io: CliIo, r: ApiResult): void {
  const s = (r.body.data as { session?: { token: string; expires_at: string } | null } | undefined)?.session;
  if (s) {
    writeSession(io.sessionFile, { token: s.token, expires_at: s.expires_at, url: io.baseUrl });
    io.out('설정이 끝나 전체 권한 세션으로 바뀌었습니다.');
  }
}

export async function runCli(argv: string[], io: CliIo): Promise<number> {
  let parsed;
  try {
    parsed = parseArgs({ args: argv, options: OPTIONS, allowPositionals: true });
  } catch (err) {
    io.err(err instanceof Error ? err.message : String(err));
    return 2;
  }
  const { values, positionals } = parsed;
  const json = values.json === true;
  const [head, second] = positionals;
  if (!head || values.help) {
    for (const l of usage()) io.out(l);
    return head ? 0 : 2;
  }
  try {
    if (head === 'login') return await login(io);
    const session = readSession(io.sessionFile);
    if (head === 'logout') {
      if (session) await call(io, 'POST', '/admin/auth/logout', { body: {}, token: session.token });
      clearSession(io.sessionFile);
      io.out('로그아웃했습니다.');
      return 0;
    }
    if (!session) {
      io.err('로그인이 필요합니다: dotrpg-admin login');
      return 1;
    }
    if (head === 'passwd') {
      const current = await io.prompt('현재 비밀번호: ', true);
      const next = await io.prompt('새 비밀번호(12자 이상): ', true);
      const again = await io.prompt('새 비밀번호 확인: ', true);
      if (next !== again) {
        io.err('새 비밀번호가 서로 다릅니다.');
        return 1;
      }
      const r = await setupCall(io, '/admin/auth/password', { current_password: current, new_password: next }, session.token);
      if (!r.ok) return fail(io, r);
      saveUpgrade(io, r);
      io.out('비밀번호를 바꿨습니다.');
      return 0;
    }
    if (head === 'totp' && second === 'enroll') {
      const s = await setupCall(io, '/admin/auth/totp/start', {}, session.token);
      if (!s.ok) return fail(io, s);
      const d = s.body.data as { secret: string; otpauth_uri: string };
      io.out('인증 앱에 아래 비밀키(또는 URI)를 등록한 뒤 6자리 코드를 입력하세요.');
      io.out(`비밀키: ${d.secret}`);
      io.out(`URI: ${d.otpauth_uri}`);
      const code = (await io.prompt('인증 코드: ', true)).trim();
      const c = await setupCall(io, '/admin/auth/totp/confirm', { code }, session.token);
      if (!c.ok) return fail(io, c);
      saveUpgrade(io, c);
      io.out('2단계 인증을 등록했습니다.');
      return 0;
    }
    const cmd = COMMANDS.find((c) => c.path.every((p, i) => positionals[i] === p) && positionals.length >= c.path.length);
    if (!cmd) {
      io.err(`알 수 없는 명령: ${positionals.join(' ')}`);
      for (const l of usage()) io.err(l);
      return 2;
    }
    let last: ApiResult | null = null;
    const ctx: CmdCtx = {
      args: positionals.slice(cmd.path.length),
      flags: values as Record<string, string | boolean | undefined>,
      prompt: io.prompt,
      get: async (route, query) => {
        last = await call(io, 'GET', route, { ...(query ? { query } : {}), token: session.token });
        if (!last.ok) throw last;
        return last.body.data;
      },
      post: async (route, body = {}) => {
        last = await call(io, 'POST', route, { body: { request_id: newRequestId(), ...body }, token: session.token });
        if (!last.ok) throw last;
        return last.body.data;
      },
    };
    try {
      await cmd.run(ctx);
    } catch (e) {
      if (e && typeof e === 'object' && 'body' in e && 'status' in e) return fail(io, e as ApiResult);
      throw e;
    }
    if (last) show(io, json, last);
    return 0;
  } catch (err) {
    io.err(err instanceof Error ? err.message : String(err));
    return 1;
  }
}

if (require.main === module) {
  const baseUrl = process.env.ADMIN_URL ?? `http://127.0.0.1:${process.env.ADMIN_PORT ?? '3001'}`;
  const io: CliIo = {
    out: (l) => console.log(l),
    err: (l) => console.error(l),
    prompt: ttyPrompt,
    fetch,
    baseUrl,
    sessionFile: defaultSessionFile(),
  };
  runCli(process.argv.slice(2), io).then((code) => process.exit(code));
}
