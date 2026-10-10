import { Pool, type PoolClient, type QueryResult, type QueryResultRow } from 'pg';
import { getConfig } from '../config/env';
import { logger } from '../utils/logger';

let pool: Pool | null = null;

export function getPool(): Pool {
  if (!pool) {
    const cfg = getConfig();
    // 앱 풀에만 건다(마이그레이션·pg_dump가 끊기지 않게 역할 전체에는 걸지 않는다)
    pool = new Pool({
      connectionString: cfg.databaseUrl,
      max: cfg.dbPoolMax,
      statement_timeout: cfg.dbStatementTimeoutMs,
      idle_in_transaction_session_timeout: 30_000,
    });
    // 쉬는 연결이 끊기면(풀러 재시작·네트워크) pg가 'error'를 낸다. 리스너가 없으면 프로세스가 죽는다: 기록만 하고 풀이 새 연결을 만든다
    pool.on('error', (err) => logger.error({ err }, 'db.pool idle client error'));
    pool.on('connect', (client) => watchSlowQueries(client, cfg.load.slowQueryMs));
  }
  return pool;
}

/** 15단계 G4: 연결마다 query를 감싸 기준(SLOW_QUERY_MS)을 넘긴 쿼리만 SQL 앞 80자와 함께 남긴다(값은 남기지 않는다) */
function watchSlowQueries(client: PoolClient, slowMs: number): void {
  const original = client.query.bind(client) as (...args: unknown[]) => unknown;
  (client as unknown as { query: (...args: unknown[]) => unknown }).query = (...args: unknown[]) => {
    const started = Date.now();
    const done = (): void => {
      const ms = Date.now() - started;
      if (ms < slowMs) return;
      const first = args[0];
      const text = typeof first === 'string' ? first : ((first as { text?: string } | null)?.text ?? '');
      logger.warn({ ms, sql: text.replace(/\s+/g, ' ').trim().slice(0, 80) }, 'db.query.slow');
    };
    // pool.query는 콜백 형태로 부른다: 콜백을 감싼다
    const last = args[args.length - 1];
    if (typeof last === 'function') {
      args[args.length - 1] = (...cbArgs: unknown[]) => {
        done();
        return (last as (...a: unknown[]) => unknown)(...cbArgs);
      };
      return original(...args);
    }
    const result = original(...args);
    if (result && typeof (result as Promise<unknown>).then === 'function') (result as Promise<unknown>).then(done, done);
    return result;
  };
}

/** 풀 상태(관리자 ops status, 감시자) */
export function poolStats(): { total: number; idle: number; waiting: number; max: number } {
  return pool
    ? { total: pool.totalCount, idle: pool.idleCount, waiting: pool.waitingCount, max: getConfig().dbPoolMax }
    : { total: 0, idle: 0, waiting: 0, max: getConfig().dbPoolMax };
}

export async function closePool(): Promise<void> {
  if (pool) {
    const p = pool;
    pool = null;
    await p.end();
  }
}

export type Queryable = Pick<PoolClient, 'query'>;

export async function query<R extends QueryResultRow = QueryResultRow>(
  text: string,
  params: unknown[] = [],
): Promise<QueryResult<R>> {
  return getPool().query<R>(text, params);
}

const commitHooks = new WeakMap<PoolClient, Map<string, () => void | Promise<void>>>();
let hookSeq = 0;

/**
 * 이 트랜잭션이 커밋된 뒤에 실행한다(롤백이면 버린다). 알림처럼 커밋 전 데이터를 보면 안 되는 일에 쓴다.
 * key가 같은 훅은 한 번만 실행한다(같은 트랜잭션에서 같은 알림이 여러 번 등록되는 것을 합친다).
 */
export function afterCommit(client: PoolClient, fn: () => void | Promise<void>, key?: string): void {
  const map = commitHooks.get(client) ?? new Map<string, () => void | Promise<void>>();
  map.set(key ?? `#${hookSeq++}`, fn);
  commitHooks.set(client, map);
}

/** 한 트랜잭션. 콜백이 던지면 롤백하고 그대로 다시 던진다. */
export async function withTransaction<T>(fn: (client: PoolClient) => Promise<T>): Promise<T> {
  const client = await getPool().connect();
  try {
    await client.query('BEGIN');
    const result = await fn(client);
    await client.query('COMMIT');
    const hooks = commitHooks.get(client);
    commitHooks.delete(client);
    if (hooks) {
      for (const h of hooks.values()) {
        try {
          await h();
        } catch (err) {
          logger.error({ err }, 'afterCommit hook failed');
        }
      }
    }
    return result;
  } catch (err) {
    commitHooks.delete(client);
    try {
      await client.query('ROLLBACK');
    } catch {
      // 롤백 실패는 원래 에러를 가리지 않는다
    }
    throw err;
  } finally {
    client.release();
  }
}

export interface PgErrorLike {
  code?: string;
  constraint?: string;
}

export function isUniqueViolation(err: unknown, constraint?: string): boolean {
  const e = err as PgErrorLike | null;
  if (!e || e.code !== '23505') return false;
  return constraint === undefined || e.constraint === constraint;
}
