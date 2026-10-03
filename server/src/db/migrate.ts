// 얇은 마이그레이션 러너: migrations/*.sql 의 "-- ============ UP ============" 구간을 실행하고
// schema_migrations 테이블에 기록한다. down은 마지막 적용분의 DOWN 구간을 실행한다.
import fs from 'node:fs';
import path from 'node:path';
import { Client } from 'pg';

const UP_MARK = /^-- =+ UP =+\s*$/m;
const DOWN_MARK = /^-- =+ DOWN =+\s*$/m;

export const MIGRATIONS_DIR = path.resolve(__dirname, '..', '..', 'migrations');

export function splitMigration(sql: string): { up: string; down: string } {
  const up = UP_MARK.exec(sql);
  const down = DOWN_MARK.exec(sql);
  if (!up || !down || down.index < up.index) {
    throw new Error('마이그레이션에 UP/DOWN 마커가 없습니다');
  }
  return {
    up: sql.slice(up.index + up[0].length, down.index),
    down: sql.slice(down.index + down[0].length),
  };
}

// `-- no-transaction` 줄이 UP 구간에 있으면 문장마다 따로(트랜잭션 없이) 실행한다: CREATE INDEX CONCURRENTLY용(phase7_ops.md 3.5).
// 문장은 줄 끝의 `;` 로 나눈다(함수 본문처럼 `;`가 줄 끝에 나오는 구문은 이 모드에서 쓰지 않는다).
const NO_TX_MARK = /^-- no-transaction\s*$/m;

export function isNoTransaction(up: string): boolean {
  return NO_TX_MARK.test(up);
}

export function splitStatements(sql: string): string[] {
  return sql
    .split(/;[ \t]*\r?\n/)
    .map((x) => x.trim())
    .filter((x) => x.replace(/--.*$/gm, '').trim().length > 0);
}

function listFiles(dir: string): string[] {
  return fs
    .readdirSync(dir)
    .filter((f) => /^\d+_.+\.sql$/.test(f))
    .sort();
}

async function ensureTable(client: Client): Promise<void> {
  await client.query(
    `CREATE TABLE IF NOT EXISTS schema_migrations (
       name TEXT PRIMARY KEY,
       applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
     )`,
  );
}

export async function migrateUp(databaseUrl: string, dir = MIGRATIONS_DIR): Promise<string[]> {
  const client = new Client({ connectionString: databaseUrl });
  await client.connect();
  const applied: string[] = [];
  try {
    await ensureTable(client);
    // 동시에 두 프로세스가 돌려도 한 번만 적용되게 한다
    await client.query('SELECT pg_advisory_lock(7301)');
    const done = new Set(
      (await client.query<{ name: string }>('SELECT name FROM schema_migrations')).rows.map(
        (r) => r.name,
      ),
    );
    for (const file of listFiles(dir)) {
      if (done.has(file)) continue;
      const { up } = splitMigration(fs.readFileSync(path.join(dir, file), 'utf8'));
      if (isNoTransaction(up)) {
        try {
          for (const stmt of splitStatements(up)) await client.query(stmt);
          await client.query('INSERT INTO schema_migrations (name) VALUES ($1)', [file]);
        } catch (err) {
          throw new Error(`마이그레이션 ${file} 실패(트랜잭션 없는 파일이라 앞 문장은 적용됐을 수 있습니다): ${(err as Error).message}`);
        }
      } else {
        try {
          await client.query('BEGIN');
          await client.query(up);
          await client.query('INSERT INTO schema_migrations (name) VALUES ($1)', [file]);
          await client.query('COMMIT');
        } catch (err) {
          await client.query('ROLLBACK');
          throw new Error(`마이그레이션 ${file} 실패: ${(err as Error).message}`);
        }
      }
      applied.push(file);
    }
    await client.query('SELECT pg_advisory_unlock(7301)');
  } finally {
    await client.end();
  }
  return applied;
}

export async function migrateDown(databaseUrl: string, dir = MIGRATIONS_DIR): Promise<string | null> {
  const client = new Client({ connectionString: databaseUrl });
  await client.connect();
  try {
    await ensureTable(client);
    const last = (
      await client.query<{ name: string }>(
        'SELECT name FROM schema_migrations ORDER BY name DESC LIMIT 1',
      )
    ).rows[0];
    if (!last) return null;
    const { down } = splitMigration(fs.readFileSync(path.join(dir, last.name), 'utf8'));
    try {
      await client.query('BEGIN');
      await client.query(down);
      await client.query('DELETE FROM schema_migrations WHERE name = $1', [last.name]);
      await client.query('COMMIT');
    } catch (err) {
      await client.query('ROLLBACK');
      throw new Error(`되돌리기 ${last.name} 실패: ${(err as Error).message}`);
    }
    return last.name;
  } finally {
    await client.end();
  }
}

if (require.main === module) {
  const url = process.env.DATABASE_URL;
  if (!url) {
    console.error('DATABASE_URL 이 필요합니다');
    process.exit(1);
  }
  const cmd = process.argv[2] ?? 'up';
  const run = cmd === 'down' ? migrateDown(url).then((n) => (n ? [n] : [])) : migrateUp(url);
  run
    .then((names) => {
      console.log(`${cmd}: ${names.length ? names.join(', ') : '변경 없음'}`);
    })
    .catch((e: Error) => {
      console.error(e.message);
      process.exit(1);
    });
}
