// 계정 단위 쓰기 요청(친구·신고)의 공통 틀: 관련 계정 행 잠금(id 오름차순) -> 멱등성 재생 -> 처리 -> 응답 저장.
// 재화가 움직이는 경로가 아니므로 원장은 없다. 같은 계정의 동시 요청은 계정 행 잠금으로 직렬화된다.
import type { PoolClient } from 'pg';
import { REQUEST_LOG_UNIQUE, findRequest, hashRequest, saveRequest } from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import type { ApiBody, EconResult, StoredResult } from '../economy/economyService';

export async function lockAccounts(client: PoolClient, ids: number[]): Promise<void> {
  const sorted = [...new Set(ids)].sort((a, b) => a - b);
  await client.query('SELECT id FROM accounts WHERE id = ANY($1::bigint[]) ORDER BY id FOR UPDATE', [sorted]);
}

export interface SocialRunOptions {
  accountId: number;
  /** 함께 잠글 다른 계정(상대) */
  alsoLock?: number[];
  endpoint: string;
  requestId: string;
  payload: unknown;
  handler: (client: PoolClient) => Promise<EconResult>;
}

export async function runSocial(o: SocialRunOptions): Promise<StoredResult> {
  const hash = hashRequest({ endpoint: o.endpoint, payload: o.payload });
  const replay = (stored: NonNullable<Awaited<ReturnType<typeof findRequest>>>): StoredResult => {
    if (stored.requestHash !== hash || stored.endpoint !== o.endpoint) {
      throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
    }
    return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
  };
  try {
    return await withTransaction(async (client) => {
      await lockAccounts(client, [o.accountId, ...(o.alsoLock ?? [])]);
      const stored = await findRequest(client, o.accountId, o.requestId);
      if (stored) return replay(stored);
      const r = await o.handler(client);
      const body: ApiBody = { success: true, message: r.message ?? '', data: r.data };
      await saveRequest(client, o.accountId, o.requestId, o.endpoint, hash, r.status, body);
      return { status: r.status, body, replay: false };
    });
  } catch (err) {
    if (isUniqueViolation(err, REQUEST_LOG_UNIQUE)) {
      const stored = await findRequest(getPool(), o.accountId, o.requestId);
      if (stored) return replay(stored);
    }
    throw err;
  }
}
