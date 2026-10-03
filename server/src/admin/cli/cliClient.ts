// CLI의 HTTP 클라이언트와 세션 파일. 비밀번호·TOTP는 프롬프트로만 받고 인자·환경변수·셸 이력에 남기지 않는다.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';

export interface CliIo {
  out: (line: string) => void;
  err: (line: string) => void;
  /** secret이면 입력을 화면에 보이지 않게 한다 */
  prompt: (question: string, secret: boolean) => Promise<string>;
  fetch: typeof fetch;
  baseUrl: string;
  sessionFile: string;
}

export interface Session {
  token: string;
  expires_at: string;
  url: string;
}

export const defaultSessionFile = (): string => path.join(os.homedir(), '.dotrpg-admin', 'session');

export function readSession(file: string): Session | null {
  try {
    const s = JSON.parse(fs.readFileSync(file, 'utf8')) as Session;
    if (typeof s.token !== 'string') return null;
    if (Date.parse(s.expires_at) <= Date.now()) return null;
    return s;
  } catch {
    return null;
  }
}

export function writeSession(file: string, s: Session): void {
  fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
  fs.writeFileSync(file, JSON.stringify(s), { mode: 0o600 });
  fs.chmodSync(file, 0o600);
}

export function clearSession(file: string): void {
  fs.rmSync(file, { force: true });
}

export interface ApiResult {
  status: number;
  ok: boolean;
  body: { success: boolean; message?: string; data?: unknown; meta?: Record<string, unknown>; errors?: Record<string, unknown> };
}

export async function call(io: CliIo, method: 'GET' | 'POST', route: string, opts: { body?: unknown; query?: Record<string, string | number | undefined>; token?: string | null } = {}): Promise<ApiResult> {
  const url = new URL(route, io.baseUrl);
  for (const [k, v] of Object.entries(opts.query ?? {})) if (v !== undefined) url.searchParams.set(k, String(v));
  const headers: Record<string, string> = {};
  if (opts.body !== undefined) headers['content-type'] = 'application/json';
  if (opts.token) headers.authorization = `Bearer ${opts.token}`;
  const res = await io.fetch(url, { method, headers, ...(opts.body !== undefined ? { body: JSON.stringify(opts.body) } : {}) });
  const body = (await res.json().catch(() => ({ success: false, message: '응답을 읽을 수 없습니다.' }))) as ApiResult['body'];
  return { status: res.status, ok: res.ok, body };
}

/** 터미널 프롬프트. secret이면 입력 문자를 가린다 */
export function ttyPrompt(question: string, secret: boolean): Promise<string> {
  return new Promise((resolve) => {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: true });
    if (secret) {
      const anyRl = rl as unknown as { _writeToOutput: (s: string) => void };
      anyRl._writeToOutput = (s: string): void => {
        if (s.includes(question)) process.stdout.write(s);
      };
    }
    rl.question(question, (answer) => {
      rl.close();
      if (secret) process.stdout.write('\n');
      resolve(answer);
    });
  });
}
