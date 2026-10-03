import { Pool, type PoolClient, type QueryResult, type QueryResultRow } from 'pg';
import { getConfig } from '../config/env';
import { logger } from '../utils/logger';

let pool: Pool | null = null;

export function getPool(): Pool {
  if (!pool) {
    pool = new Pool({ connectionString: getConfig().databaseUrl, max: 10 });
  }
  return pool;
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
