import { Pool, type PoolClient, type QueryResult, type QueryResultRow } from 'pg';
import { getConfig } from '../config/env';

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

/** 한 트랜잭션. 콜백이 던지면 롤백하고 그대로 다시 던진다. */
export async function withTransaction<T>(fn: (client: PoolClient) => Promise<T>): Promise<T> {
  const client = await getPool().connect();
  try {
    await client.query('BEGIN');
    const result = await fn(client);
    await client.query('COMMIT');
    return result;
  } catch (err) {
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
