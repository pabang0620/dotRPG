// 거절하면서 경매 의심 기록(auction_flags)을 남겨야 할 때 쓰는 에러와 실행 틀.
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { runEconomy, type RunOptions, type StoredResult } from '../economy/economyService';
import * as repo from './auctionRepository';
import type { FlagKind } from './auctionRepository';

/** 거절하면서 의심 기록을 남겨야 할 때 던진다. 롤백 뒤에 풀에서 따로 쓴다(거절돼도 남는다) */
export class FlagError extends AppError {
  constructor(
    status: number,
    message: string,
    code: string,
    readonly flag: { accountId: number; characterId: number; kind: FlagKind; severity: 1 | 2 | 3; detail: Record<string, unknown> },
    extra?: Record<string, unknown>,
  ) {
    super(status, message, code, extra);
    this.name = 'FlagError';
  }
}

export async function recordFlag(f: FlagError['flag']): Promise<void> {
  try {
    await repo.insertFlag(getPool(), f.accountId, f.characterId, f.kind, f.severity, f.detail);
  } catch {
    // 기록 실패가 원래 에러 응답을 가리지 않는다
  }
}

/** PostgreSQL 교착 감지(40P01): 서로 반대 방향으로 거래하는 두 요청이 상대 캐릭터 행의 FK 잠금에서 맞물릴 수 있다 */
const isDeadlock = (err: unknown): boolean => (err as { code?: string } | null)?.code === '40P01';

export async function runAuction(o: RunOptions): Promise<StoredResult> {
  // 교착의 희생자는 롤백되어 아무것도 저장되지 않았으므로(request_log 포함) 같은 요청을 다시 처리해도 안전하다
  for (let attempt = 0; ; attempt++) {
    try {
      return await runEconomy(o);
    } catch (err) {
      if (isDeadlock(err) && attempt < 2) continue;
      if (err instanceof FlagError) await recordFlag(err.flag);
      throw err;
    }
  }
}

