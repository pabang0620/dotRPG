// 9단계 11.1: 시험 스크립트 가드(DEPLOY_STAGE=test 명시)와 운영 기동 가드
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { loadConfig } from '../src/config/env';

const ROOT = path.resolve(__dirname, '..');

/** 가드를 자식 프로세스에서 실행한다(process.exit 때문에). envFile은 server/.env 대신 읽을 파일 */
function runGuard(env: Record<string, string>, envFileText?: string): { code: number | null; out: string } {
  let file = '/nonexistent/.env';
  if (envFileText !== undefined) {
    file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'guard-')), '.env');
    fs.writeFileSync(file, envFileText);
  }
  const code = `import('./scripts/_stageGuard.mjs').then((m) => m.assertTestStage(process.env, new URL(${JSON.stringify(`file://${file}`)})))`;
  const r = spawnSync(process.execPath, ['-e', code], { cwd: ROOT, env: { PATH: process.env.PATH ?? '', ...env }, encoding: 'utf8' });
  return { code: r.status, out: `${r.stdout}${r.stderr}` };
}

describe('시험 스크립트 가드 (scripts/_stageGuard.mjs)', () => {
  it('DEPLOY_STAGE가 없음·live·dev·그 밖이면 종료 코드 1과 안내', () => {
    for (const stage of [undefined, 'live', 'dev', 'prod', 'TEST']) {
      const r = runGuard(stage === undefined ? {} : { DEPLOY_STAGE: stage });
      expect([stage, r.code]).toEqual([stage, 1]);
      expect(r.out).toContain('DEPLOY_STAGE=test 가 명시된 환경에서만 실행합니다');
      expect(r.out).toContain(`(현재: ${stage ?? '없음'})`);
    }
  });

  it('test면 통과하고 대상 DB를 한 줄 출력한다(비밀번호는 출력하지 않는다)', () => {
    const r = runGuard({ DEPLOY_STAGE: 'test', DATABASE_URL: 'postgres://postgres:s3cret-pw@db.example.org:5433/dotrpg_test' });
    expect(r.code).toBe(0);
    expect(r.out).toContain('[TEST SCRIPT] stage=test db=db.example.org:5433/dotrpg_test');
    expect(r.out).not.toContain('s3cret-pw');
  });

  it('환경변수가 없으면 server/.env 파일에서 읽는다', () => {
    expect(runGuard({}, 'DATABASE_URL=postgres://u:p@localhost:5433/dotrpg\nDEPLOY_STAGE=test\n').code).toBe(0);
    expect(runGuard({}, 'DEPLOY_STAGE=live\n').code).toBe(1);
    // 환경변수가 파일보다 우선한다
    expect(runGuard({ DEPLOY_STAGE: 'live' }, 'DEPLOY_STAGE=test\n').code).toBe(1);
  });

  it('5개 시험 스크립트가 모두 가드를 맨 앞에서 부른다', () => {
    for (const f of ['test-stars.ts', 'test-give.ts', 'test-release-held.ts', 'test-level.mjs', 'test-reset-entries.mjs']) {
      const text = fs.readFileSync(path.join(ROOT, 'scripts', f), 'utf8');
      expect([f, text.includes("from './_stageGuard.mjs'"), text.includes('assertTestStage();')]).toEqual([f, true, true]);
      expect(text).not.toMatch(/\?\? 'dev'/);
    }
  });
});

describe('운영 기동 가드: DEPLOY_STAGE 명시', () => {
  const base = { DATABASE_URL: 'x', MIN_CLIENT_VERSION: '0.1.0', JWT_SECRET: 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw' };
  const prod = {
    ...base,
    NODE_ENV: 'production',
    STEAM_AUTH_MODE: 'web_api',
    STEAM_APP_ID: '2800000',
    STEAM_WEB_API_KEY: 'k',
    TRUST_PROXY: '1',
    ADMIN_SECRET_KEY: Buffer.alloc(32, 9).toString('base64'),
    // 회원 탈퇴(0027): 운영은 이월 표시용 HMAC 키가 필요하다(WITHDRAW_ENABLED 기본 true)
    WITHDRAW_ID_HMAC_KEY: 'Wd7hK2mQ9xL4vR8tY1uZ5aB3cD6eF0gHnJkLoNpRsTw',
    RELAY_TICKET_SECRET: 'Qm8vN2xK5pL7wR3tY6uZ9aB4cD1eF0gHiJkLmNoPq',
    RELAY_PUBLIC_URL: 'wss://game.example.org/relay',
    DEVICE_HASH_PEPPER: 'Pe9rL4vK7mQ2xW5tY8uZ3aB6cD1eF0gHnJkLoNpRs',
  };

  it('production + 미지정이면 기동 실패, live/test면 통과, 개발 환경은 영향 없음', () => {
    expect(() => loadConfig(prod)).toThrow(/DEPLOY_STAGE/);
    expect(loadConfig({ ...prod, DEPLOY_STAGE: 'live' }).deployStage).toBe('live');
    expect(loadConfig({ ...prod, DEPLOY_STAGE: 'test' }).deployStage).toBe('test');
    expect(() => loadConfig({ ...prod, DEPLOY_STAGE: '' })).toThrow(/DEPLOY_STAGE/);
    expect(loadConfig({ ...base, NODE_ENV: 'development' }).deployStage).toBe('live');
  });

  it('운영에는 DEVICE_HASH_PEPPER(32자 이상, JWT와 다름)가 필요하고, 구매자 자격 0은 거부', () => {
    const { DEVICE_HASH_PEPPER: _drop, ...noPepper } = prod;
    expect(() => loadConfig({ ...noPepper, DEPLOY_STAGE: 'live' })).toThrow(/DEVICE_HASH_PEPPER/);
    expect(() => loadConfig({ ...prod, DEPLOY_STAGE: 'live', AUCTION_BUYER_MIN_LEVEL: '0' })).toThrow(/AUCTION_BUYER_MIN_LEVEL/);
    expect(() => loadConfig({ ...prod, DEPLOY_STAGE: 'live', AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS: '0' })).toThrow(/AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS/);
    // 개발에서는 비밀이 없어도 JWT 비밀에서 파생해 동작한다
    expect(loadConfig({ ...base, NODE_ENV: 'development' }).aa.devicePepper.length).toBeGreaterThanOrEqual(32);
  });
});
