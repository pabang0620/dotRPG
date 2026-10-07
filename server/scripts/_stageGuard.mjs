// 시험 스크립트 공용 가드(phase9_anti_abuse.md 11.1): DEPLOY_STAGE 가 정확히 "test" 로 명시된 환경에서만 실행한다.
// 환경변수를 먼저 보고, 없으면 server/.env 파일에서 읽는다. 없음·live·dev·그 밖은 모두 거절한다(예전에는 없으면 dev 로 보고 실행했다).
import { existsSync, readFileSync } from 'node:fs';

const ENV_FILE = new URL('../.env', import.meta.url);

function readEnvFile(file) {
  try {
    if (!existsSync(file)) return {};
    return Object.fromEntries(
      readFileSync(file, 'utf8')
        .split(/\r?\n/)
        .filter((l) => /^[A-Z0-9_]+=/.test(l))
        .map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1).trim().replace(/^["']|["']$/g, '')]),
    );
  } catch {
    return {};
  }
}

/** 판정만 한다(테스트가 직접 부른다). { ok, stage, db } */
export function evaluateStage(env = process.env, envFile = ENV_FILE) {
  const file = readEnvFile(envFile);
  const stage = env.DEPLOY_STAGE || file.DEPLOY_STAGE || null;
  let db = '(알 수 없음)';
  const url = env.DATABASE_URL || file.DATABASE_URL;
  if (url) {
    try {
      const u = new URL(url);
      db = `${u.hostname}${u.port ? `:${u.port}` : ''}/${u.pathname.replace(/^\//, '')}`;
    } catch {
      db = '(해석 불가)';
    }
  }
  return { ok: stage === 'test', stage, db };
}

/** 스크립트 맨 앞에서 부른다. 통과하면 대상 DB를 한 줄 출력한다(비밀번호는 출력하지 않는다). 아니면 종료 코드 1 */
export function assertTestStage(env = process.env, envFile = ENV_FILE) {
  const r = evaluateStage(env, envFile);
  if (!r.ok) {
    console.error(`DEPLOY_STAGE=test 가 명시된 환경에서만 실행합니다 (현재: ${r.stage ?? '없음'})`);
    process.exit(1);
  }
  console.log(`[TEST SCRIPT] stage=test db=${r.db}`);
}
