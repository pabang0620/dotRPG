// npm test: embedded PostgreSQL을 임시 폴더에 띄우고 jest를 돌린 뒤 정리한다.
// 이미 PG를 가지고 있으면 TEST_PG_ADMIN_URL(postgres 슈퍼유저, 아무 DB)을 주면 그걸 쓴다.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { startEmbedded } from './embeddedPg.mjs';

function freePort() {
  return new Promise((resolve, reject) => {
    const srv = net.createServer();
    srv.listen(0, () => {
      const { port } = srv.address();
      srv.close(() => resolve(port));
    });
    srv.on('error', reject);
  });
}

let pg = null;
let dir = null;
const env = { ...process.env };
if (!env.TEST_PG_ADMIN_URL) {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dotrpg-testpg-'));
  const port = await freePort();
  pg = await startEmbedded({ databaseDir: dir, port });
  env.TEST_PG_ADMIN_URL = `postgres://postgres:postgres@localhost:${port}/postgres`;
}

const jestBin = path.resolve('node_modules/jest/bin/jest.js');
const child = spawn(process.execPath, [jestBin, '--runInBand', ...process.argv.slice(2)], {
  stdio: 'inherit',
  env,
});
const code = await new Promise((resolve) => child.on('exit', (c) => resolve(c ?? 1)));

if (pg) await pg.stop();
if (dir) fs.rmSync(dir, { recursive: true, force: true });
process.exit(code);
