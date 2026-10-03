// 개발용 embedded PostgreSQL을 띄우고 dotrpg DB를 만든다. Ctrl+C로 종료.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startEmbedded, ensureDatabase, resolveDataDir } from './embeddedPg.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = Number(process.env.DEV_PG_PORT ?? 5433);

const pg = await startEmbedded({ databaseDir: resolveDataDir(root, 'pgdata'), port });
await ensureDatabase(pg, 'dotrpg');
console.log(`PostgreSQL ready: postgres://postgres:postgres@localhost:${port}/dotrpg`);

const stop = async () => {
  await pg.stop();
  process.exit(0);
};
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
setInterval(() => {}, 1 << 30);
