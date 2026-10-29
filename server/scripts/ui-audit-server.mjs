// UI 검수 전용: 환경 파일을 읽지 않고 새 임시 DB와 루프백 서버만 만든다.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { startEmbedded, ensureDatabase } from './embeddedPg.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.resolve(process.argv[2] ?? path.join(root, '..', 'Temp', 'ui-online-qa', randomUUID()));
if (fs.existsSync(output)) throw new Error('새 출력 폴더를 지정해야 합니다. 기존 폴더는 사용하지 않습니다.');
fs.mkdirSync(output, { recursive: true });
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'dotrpg-uiqa-'));
const stopFile = path.join(output, 'STOP');
const dbName = `uiqa_${randomUUID().replaceAll('-', '')}`;
const clientVersion = fs.readFileSync(path.join(root, '..', 'ProjectSettings', 'ProjectSettings.asset'), 'utf8').match(/^\s*bundleVersion:\s*(\S+)/m)?.[1];
if (!clientVersion) throw new Error('Unity 클라이언트 버전을 읽을 수 없습니다.');
const httpPort = Number(process.argv[3] ?? 0);
if (!Number.isInteger(httpPort) || httpPort < 0 || httpPort > 65535) throw new Error('QA HTTP 포트가 잘못됐습니다.');
const port = await new Promise((resolve, reject) => {
  const s = net.createServer(); s.on('error', reject); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => resolve(p)); });
});
let pg, child, timer, stopping = false;
async function stop(code = 0) {
  if (stopping) return; stopping = true;
  if (timer) clearInterval(timer);
  fs.writeFileSync(stopFile, 'stop');
  if (child && child.exitCode === null) {
    await Promise.race([new Promise(r => child.once('exit', r)), new Promise(r => setTimeout(r, 12000))]);
    if (child.exitCode === null) child.kill();
  }
  try { if (pg) await pg.stop(); }
  finally {
    // 이번 프로세스가 만든 임시 폴더만 지운다.
    if (path.dirname(temp) === path.resolve(os.tmpdir()) && path.basename(temp).startsWith('dotrpg-uiqa-')) fs.rmSync(temp, { recursive: true, force: true });
    const credential = path.join(output, 'qa-credentials.json');
    if (fs.existsSync(credential)) fs.rmSync(credential);
    fs.writeFileSync(path.join(output, 'stopped.json'), JSON.stringify({ stopped: true, code }));
    process.exitCode = code;
  }
}
try {
  pg = await startEmbedded({ databaseDir: temp, port });
  await ensureDatabase(pg, dbName);
  // 호출자 DB/계정 환경값은 전달하지 않는다.
  const env = {};
  for (const key of ['PATH', 'Path', 'SystemRoot', 'WINDIR', 'TEMP', 'TMP', 'HOME', 'USERPROFILE']) if (process.env[key]) env[key] = process.env[key];
  Object.assign(env, { TEST_DATABASE_URL: `postgres://postgres:postgres@127.0.0.1:${port}/${dbName}`, UI_QA_OUTPUT: output,
    NODE_ENV: 'test', DEPLOY_STAGE: 'test', AUTH_DEV_ENABLED: 'true', AUTH_DEV_REGISTER_ENABLED: 'true', CHAPTER3_OPEN: 'true',
    GAME_DATA_DIR: path.join(root, 'data'), UI_QA_CLIENT_VERSION: clientVersion, UI_QA_HTTP_PORT: String(httpPort) });
  const log = fs.openSync(path.join(output, 'server.log'), 'a');
  child = spawn(process.execPath, ['--import', 'tsx', 'scripts/ui-audit-worker.ts'], { cwd: root, env, stdio: ['ignore', log, log], windowsHide: true });
  fs.closeSync(log);
  child.once('error', async () => { await stop(1); });
  child.once('exit', async code => { await stop(code ?? 1); });
  timer = setInterval(() => { if (fs.existsSync(stopFile)) { clearInterval(timer); void stop(); } }, 500);
  process.once('SIGINT', () => { clearInterval(timer); void stop(); });
  process.once('SIGTERM', () => { clearInterval(timer); void stop(); });
  console.log(`QA 출력: ${output}\nready.json 생성 후 시작. STOP 파일로 종료.`);
} catch (error) { console.error(error.message); await stop(1); }
