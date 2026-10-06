// 3단계 경제 요청의 공통 틀: 캐릭터 행 잠금 -> 멱등성 재생 -> 처리 -> 응답 저장(한 트랜잭션).
// 이상 기록은 4xx 롤백과 무관하게 남기려고 트랜잭션 밖(풀)에서 따로 쓴다.
import {
  REQUEST_LOG_UNIQUE,
  findRequest,
  hashRequest,
  saveRequest,
} from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction, type Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { flush as flushIncome } from '../antiabuse/incomeMeter';
import { EconCtx } from './economyContext';
import * as repo from './economyRepository';
import type { AnomalyKind } from './economyRepository';

export interface ApiBody {
  success: true;
  message: string;
  data: unknown;
}
export interface StoredResult {
  status: number;
  body: ApiBody;
  replay: boolean;
}
export interface EconResult {
  status: number;
  message?: string;
  data: unknown;
}

const NOT_FOUND = () => new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');

/** 거절하면서 이상 기록을 남겨야 할 때 던진다. runEconomy가 롤백 뒤에 기록한다. */
export class AnomalyError extends AppError {
  constructor(
    status: number,
    message: string,
    code: string,
    readonly anomaly: { kind: AnomalyKind; severity: 1 | 2 | 3; detail: Record<string, unknown> },
    extra?: Record<string, unknown>,
  ) {
    super(status, message, code, extra);
    this.name = 'AnomalyError';
  }
}

export async function recordAnomaly(
  accountId: number,
  characterId: number | null,
  a: AnomalyError['anomaly'],
  db: Queryable = getPool(),
): Promise<void> {
  try {
    await repo.insertAnomaly(db, accountId, characterId, a.kind, a.severity, a.detail);
  } catch (err) {
    // 기록 실패가 원래 에러 응답을 가리지 않는다
    logger.error({ err }, 'anomaly_log insert failed');
  }
}

function replayOf(
  stored: NonNullable<Awaited<ReturnType<typeof findRequest>>>,
  endpoint: string,
  hash: string,
): StoredResult {
  if (stored.requestHash !== hash || stored.endpoint !== endpoint) {
    throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
  }
  return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
}

export interface RunOptions {
  accountId: number;
  characterUuid: string;
  /** 예: "POST /characters/:uuid/kills" (경로 패턴) */
  endpoint: string;
  requestId: string;
  /** request_id를 뺀 정규화 본문과 경로 인자(퀘스트 id 등). 해시에 캐릭터 uuid가 함께 들어간다 */
  payload: unknown;
  handler: (ctx: EconCtx) => Promise<EconResult>;
}

export async function runEconomy(o: RunOptions): Promise<StoredResult> {
  const hash = hashRequest({ endpoint: o.endpoint, character: o.characterUuid, payload: o.payload });
  let characterId: number | null = null;
  try {
    return await withTransaction(async (client) => {
      const c = await repo.lockCharacter(client, o.accountId, o.characterUuid);
      if (!c) throw NOT_FOUND();
      characterId = c.id;
      const stored = await findRequest(client, o.accountId, o.requestId);
      if (stored) return replayOf(stored, o.endpoint, hash);

      const ctx = new EconCtx(client, c, o.requestId, getNow());
      const r = await o.handler(ctx);
      // 9단계: 원장에서 파생한 시간별 집계를 같은 트랜잭션에서 올린다(경제 속도 감시)
      await flushIncome(client, c.id, ctx.level, ctx.now, ctx.income);
      const body: ApiBody = { success: true, message: r.message ?? '', data: r.data };
      await saveRequest(client, o.accountId, o.requestId, o.endpoint, hash, r.status, body);
      return { status: r.status, body, replay: false };
    });
  } catch (err) {
    if (err instanceof AnomalyError) await recordAnomaly(o.accountId, characterId, err.anomaly);
    // 락을 우회한 UNIQUE 충돌(이론상 드묾): 먼저 끝난 요청의 응답을 돌려준다
    if (isUniqueViolation(err, REQUEST_LOG_UNIQUE)) {
      const stored = await findRequest(getPool(), o.accountId, o.requestId);
      if (stored) return replayOf(stored, o.endpoint, hash);
    }
    throw err;
  }
}
