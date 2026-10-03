// npm run dev: embedded PostgreSQL(개발용 데이터 폴더) + 마이그레이션 + 서버(tsx watch)
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ensureDatabase, resolveDataDir, startEmbedded } from './embeddedPg.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const envFile = path.join(root, '.env');
if (!fs.existsSync(envFile)) {
  console.error('.env 가 없습니다. .env.example 을 복사해 JWT_SECRET 을 채워 주세요.');
  process.exit(1);
}
process.loadEnvFile(envFile);
if (!process.env.DATABASE_URL) {
  console.error('.env 에 DATABASE_URL 이 없습니다.');
  process.exit(1);
}

const port = Number(new URL(process.env.DATABASE_URL).port || 5433);
const pg = await startEmbedded({ databaseDir: resolveDataDir(root, 'pgdata'), port });
await ensureDatabase(pg, new URL(process.env.DATABASE_URL).pathname.slice(1));

const mig = spawnSync(process.execPath, [path.join(root, 'node_modules/tsx/dist/cli.mjs'), 'src/db/migrate.ts', 'up'], {
  cwd: root,
  stdio: 'inherit',
  env: process.env,
});
if (mig.status !== 0) {
  await pg.stop();
  process.exit(1);
}

const server = spawn(process.execPath, [path.join(root, 'node_modules/tsx/dist/cli.mjs'), 'watch', 'src/server.ts'], {
  cwd: root,
  stdio: 'inherit',
  env: process.env,
});

let stopping = false;
const stop = async () => {
  if (stopping) return;
  stopping = true;
  server.kill('SIGTERM');
  await pg.stop();
  process.exit(0);
};
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
server.on('exit', stop);
