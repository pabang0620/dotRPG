// 교환 요청의 공통 틀: 두 캐릭터 행을 id 순서로 잠금 -> 멱등성 재생 -> 처리 -> 응답 저장(한 트랜잭션).
// runEconomy와 달리 처음부터 상대 캐릭터까지 id 순서로 잠가 서로 반대 방향 요청이 교착하지 않는다.
import { REQUEST_LOG_UNIQUE, findRequest, hashRequest, saveRequest } from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import type { ApiBody, EconResult, StoredResult } from '../economy/economyService';
import { lockCharacters, type LockedChar } from '../economy/economyRepository';
import * as repo from './tradeRepository';
import type { TradeState } from './tradeRepository';
import { randomUUID } from 'node:crypto';
import type { PoolClient } from 'pg';

export interface TradeCtx {
  client: PoolClient;
  me: LockedChar;
  /** 잠근 캐릭터 전부(나 포함) */
  chars: Map<number, LockedChar>;
  requestId: string;
  now: Date;
}

/** 거절하면서 거래를 닫아야 할 때 던진다. 러너가 닫는 문장만 커밋하고 에러를 다시 던진다(호출 쪽은 던지기 전에 아무것도 쓰지 않는다) */
export class TradeAbort extends AppError {
  constructor(
    status: number,
    message: string,
    code: string,
    readonly close: { tradeId: number; state: TradeState; reason: string },
    extra?: Record<string, unknown>,
  ) {
    super(status, message, code, extra);
    this.name = 'TradeAbort';
  }
}

export interface TradeRun {
  accountId: number;
  characterUuid: string;
  endpoint: string;
  /** 없으면 멱등 기록 없이 처리한다(상태로 멱등한 요청) */
  requestId?: string;
  payload: unknown;
  /** 잠글 캐릭터 id(내 것 포함). 서비스가 미리 읽어 넘긴다 */
  lockIds: number[];
  handler: (ctx: TradeCtx) => Promise<EconResult>;
}

const replayOf = (stored: NonNullable<Awaited<ReturnType<typeof findRequest>>>, endpoint: string, hash: string): StoredResult => {
  if (stored.requestHash !== hash || stored.endpoint !== endpoint) {
    throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
  }
  return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
};

export async function runTrade(o: TradeRun): Promise<StoredResult> {
  const hash = hashRequest({ endpoint: o.endpoint, character: o.characterUuid, payload: o.payload });
  const box: { aborted: TradeAbort | null } = { aborted: null };
  try {
    const out = await withTransaction(async (client): Promise<StoredResult | null> => {
      const locked = await lockCharacters(client, [...new Set(o.lockIds)]);
      const me = locked.find((c) => c.uuid === o.characterUuid && c.accountId === o.accountId);
      if (!me) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
      if (locked.length !== new Set(o.lockIds).size) throw new AppError(404, '거래 상대를 찾을 수 없습니다.', 'PLAYER_NOT_FOUND');
      if (o.requestId) {
        const stored = await findRequest(client, o.accountId, o.requestId);
        if (stored) return replayOf(stored, o.endpoint, hash);
      }
      const ctx: TradeCtx = { client, me, chars: new Map(locked.map((c) => [c.id, c])), requestId: o.requestId ?? randomUUID(), now: getNow() };
      let r: EconResult;
      try {
        r = await o.handler(ctx);
      } catch (err) {
        if (!(err instanceof TradeAbort)) throw err;
        await repo.closeTrade(client, err.close.tradeId, err.close.state, err.close.reason, ctx.now);
        box.aborted = err;
        return null;
      }
      const body: ApiBody = { success: true, message: r.message ?? '', data: r.data };
      if (o.requestId) await saveRequest(client, o.accountId, o.requestId, o.endpoint, hash, r.status, body);
      return { status: r.status, body, replay: false };
    });
    if (box.aborted) throw box.aborted;
    return out as StoredResult;
  } catch (err) {
    if (o.requestId && isUniqueViolation(err, REQUEST_LOG_UNIQUE)) {
      const stored = await findRequest(getPool(), o.accountId, o.requestId);
      if (stored) return replayOf(stored, o.endpoint, hash);
    }
    throw err;
  }
}
