// embedded-postgres 시작 도우미 (개발용 .pgdata, 테스트용 임시 폴더 공용)
import { existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import EmbeddedPostgres from 'embedded-postgres';

export async function startEmbedded({ databaseDir, port, persistent = true }) {
  const pg = new EmbeddedPostgres({
    databaseDir,
    user: 'postgres',
    password: 'postgres',
    port,
    persistent,
    onLog: () => {},
    onError: (e) => console.error('[pg]', String(e).trim()),
  });
  if (!existsSync(`${databaseDir}/PG_VERSION`)) {
    await pg.initialise();
  }
  await pg.start();
  return pg;
}

export async function ensureDatabase(pg, name) {
  const client = pg.getPgClient();
  await client.connect();
  try {
    const r = await client.query('SELECT 1 FROM pg_database WHERE datname = $1', [name]);
    if (r.rowCount === 0) {
      await client.query(`CREATE DATABASE "${name.replace(/"/g, '')}"`);
    }
  } finally {
    await client.end();
  }
}

// PostgreSQL은 데이터 폴더 권한 0700이 필요하다. WSL의 /mnt/c(NTFS)에서는 chmod가 안 먹으므로
// 그 경우에만 리눅스 파일시스템(~/.cache)으로 옮긴다. DEV_PGDATA로 직접 지정할 수도 있다.
export function resolveDataDir(serverRoot, name) {
  if (process.env.DEV_PGDATA && name === 'pgdata') return process.env.DEV_PGDATA;
  const local = path.join(serverRoot, name === 'pgdata' ? '.pgdata' : `.pgdata-${name}`);
  if (!path.resolve(local).startsWith('/mnt/')) return local;
  return path.join(os.homedir(), '.cache', 'dotrpg-server', name);
}
