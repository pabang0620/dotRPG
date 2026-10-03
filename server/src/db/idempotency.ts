import { createHash } from 'node:crypto';
import type { Queryable } from './pool';

export interface StoredRequest {
  requestHash: string;
  statusCode: number;
  response: unknown;
  endpoint: string;
}

export const REQUEST_LOG_UNIQUE = 'request_log_account_id_request_id_key';

/** 키를 정렬해 같은 내용이면 항상 같은 문자열이 되게 한 뒤 SHA-256 */
export function hashRequest(payload: unknown): string {
  return createHash('sha256').update(canonicalJson(payload)).digest('hex');
}

function canonicalJson(v: unknown): string {
  if (Array.isArray(v)) return `[${v.map(canonicalJson).join(',')}]`;
  if (v && typeof v === 'object') {
    const obj = v as Record<string, unknown>;
    return `{${Object.keys(obj)
      .sort()
      .map((k) => `${JSON.stringify(k)}:${canonicalJson(obj[k])}`)
      .join(',')}}`;
  }
  return JSON.stringify(v) ?? 'null';
}

export async function findRequest(
  db: Queryable,
  accountId: number,
  requestId: string,
): Promise<StoredRequest | null> {
  const r = await db.query<{
    request_hash: string;
    status_code: number;
    response: unknown;
    endpoint: string;
  }>(
    'SELECT request_hash, status_code, response, endpoint FROM request_log WHERE account_id = $1 AND request_id = $2',
    [accountId, requestId],
  );
  const row = r.rows[0];
  if (!row) return null;
  return {
    requestHash: row.request_hash,
    statusCode: row.status_code,
    response: row.response,
    endpoint: row.endpoint,
  };
}

export async function saveRequest(
  db: Queryable,
  accountId: number,
  requestId: string,
  endpoint: string,
  requestHash: string,
  statusCode: number,
  response: unknown,
): Promise<void> {
  await db.query(
    `INSERT INTO request_log (account_id, request_id, endpoint, request_hash, status_code, response)
     VALUES ($1, $2, $3, $4, $5, $6::jsonb)`,
    [accountId, requestId, endpoint, requestHash, statusCode, JSON.stringify(response)],
  );
}

export async function purgeExpiredRequestLogs(db: Queryable, ttlDays: number): Promise<number> {
  const r = await db.query(
    `DELETE FROM request_log WHERE created_at < now() - ($1::int * interval '1 day')`,
    [ttlDays],
  );
  return r.rowCount ?? 0;
}
