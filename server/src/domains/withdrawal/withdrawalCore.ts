// 탈퇴 요청(W2 / 관리자 WD3)과 철회(W3 / WD4)의 트랜잭션 본체. 락 순서: 캐릭터(id 오름차순) -> 계정 -> 요청 행.
// 골드·아이템·별조각은 건드리지 않는다(원장 변화 없음). 호출 쪽이 withTransaction 안에서 부른다.
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { findRequest, saveRequest } from '../../db/idempotency';
import { afterCommit } from '../../db/pool';
import { metrics } from '../../ops/metrics';
import { AppError } from '../../utils/AppError';
import { revokeAllForAccount } from '../auth/authRepository';
import { WITHDRAWN_FAMILY } from '../antiabuse/sessionService';
import type { ApiBody, StoredResult } from '../economy/economyService';
import { SESSION_CHANNEL } from '../antiabuse/sessionService';
import { closeAccountActivity, renameToPlaceholders, restoreNames } from './withdrawalCleanup';
import * as repo from './withdrawalRepository';

export const WITHDRAW_ENDPOINT = 'POST /me/withdrawal';
const DAY_MS = 86_400_000;

export const withdrawalView = (w: repo.WithdrawalRow) => ({
  id: w.uuid,
  requested_at: w.requested_at.toISOString(),
  due_at: w.due_at.toISOString(),
  cancel_allowed: w.cancel_allowed,
});

const bodyOf = (w: repo.WithdrawalRow): ApiBody => ({ success: true, message: '', data: { withdrawal: withdrawalView(w), logged_out: true } });

export interface StartOpts {
  accountId: number;
  requestId: string;
  source: 'self' | 'admin';
  adminId: number | null;
  now: Date;
  /** 본인 요청: 유료 별조각 확인 필수, 30일 한도 적용 */
  self?: { ackPaidLoss: boolean };
  cancelAllowed: boolean;
  /** 본인 요청의 멱등(request_log). 관리자 대행은 관리자 감사 로그가 맡는다 */
  idem?: { endpoint: string; hash: string };
}

/** 탈퇴 요청. 같은 request_id 재전송이면 첫 응답을 돌려준다(replay=true) */
export async function startWithdrawal(client: PoolClient, o: StartOpts): Promise<StoredResult & { row: repo.WithdrawalRow }> {
  const cfg = getConfig().withdraw;
  // 1. 락 순서
  const chars = await repo.lockAliveCharacters(client, o.accountId);
  const acc = await repo.lockAccount(client, o.accountId);
  if (!acc) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
  if (acc.anonymized_at) throw new AppError(409, '이미 익명화된 계정입니다.', 'ALREADY_ANONYMIZED');

  // 2. 멱등: 같은 request_id 는 첫 응답. 본문이 다르면 422
  if (o.idem) {
    const stored = await findRequest(client, o.accountId, o.requestId);
    if (stored) {
      if (stored.requestHash !== o.idem.hash || stored.endpoint !== o.idem.endpoint) {
        throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
      }
      const prev = await repo.byRequestId(client, o.accountId, o.requestId);
      return { status: stored.statusCode, body: stored.response as ApiBody, replay: true, row: prev as repo.WithdrawalRow };
    }
  }
  const same = await repo.byRequestId(client, o.accountId, o.requestId);
  if (same) return { status: 201, body: bodyOf(same), replay: true, row: same };

  // 3. 검사
  const open = await repo.lockOpen(client, o.accountId);
  if (open || acc.deleted_at) throw new AppError(409, '이미 탈퇴가 요청되었습니다.', 'WITHDRAWAL_ALREADY_REQUESTED');
  if (o.self) {
    const since = new Date(o.now.getTime() - 30 * DAY_MS);
    if ((await repo.countRequestedSince(client, o.accountId, since)) >= cfg.requestMaxPer30d) {
      throw new AppError(429, '최근 30일 안에 탈퇴 요청이 너무 많습니다.', 'WITHDRAW_LIMIT');
    }
  }
  if (await repo.hasOpenOrder(client, o.accountId)) {
    throw new AppError(409, '진행 중인 결제가 있어 지금은 탈퇴할 수 없습니다.', 'WITHDRAW_BLOCKED', { blockers: ['payment_open'] });
  }
  const loss = await repo.lossSummary(client, o.accountId);
  if (o.self && loss.paid_stars > 0 && !o.self.ackPaidLoss) {
    throw new AppError(409, '유료 별조각 소멸 안내를 확인해야 합니다.', 'ACK_REQUIRED', { need: ['paid_loss'] });
  }

  // 4. 쓰기
  const savedNames = await renameToPlaceholders(client, chars);
  await repo.blockLogin(client, o.accountId, o.now);
  await revokeAllForAccount(client, o.accountId, 'withdrawal');
  await repo.endSessions(client, o.accountId);
  await closeAccountActivity(client, o.accountId, chars.map((c) => c.id), o.now);
  const row = await repo.insert(client, {
    accountId: o.accountId,
    source: o.source,
    adminId: o.adminId,
    requestId: o.requestId,
    requestedAt: o.now,
    dueAt: new Date(o.now.getTime() + cfg.graceDays * DAY_MS),
    cancelAllowed: o.cancelAllowed,
    ackPaidLoss: o.self?.ackPaidLoss ?? false,
    lossSnapshot: { ...loss, characters: loss.characters.length },
    savedNames,
  });
  const body = bodyOf(row);
  if (o.idem) await saveRequest(client, o.accountId, o.requestId, o.idem.endpoint, o.idem.hash, 201, body);
  // 접속 끊기(/ws 4012, 중계 연결): 커밋 때 전달된다
  await client.query('SELECT pg_notify($1, $2)', [SESSION_CHANNEL, `${o.accountId}:${WITHDRAWN_FAMILY}`]);
  afterCommit(client, () => {
    metrics.withdrawalRequested++;
  });
  return { status: 201, body, replay: false, row };
}

export interface CancelOpts {
  accountId: number;
  via: 'self' | 'admin';
  cancelRequestId: string;
  now: Date;
}

export interface CancelResult {
  cancelled: true;
  renamed_characters: number;
  replay: boolean;
}

/** 철회. 본인(self)은 cancel_allowed 와 기한(410)을, 운영자(admin)는 기한(409)만 본다 */
export async function cancelWithdrawal(client: PoolClient, o: CancelOpts): Promise<CancelResult> {
  const chars = await repo.lockAliveCharacters(client, o.accountId);
  const acc = await repo.lockAccount(client, o.accountId);
  if (!acc) throw new AppError(404, '열린 탈퇴 요청이 없습니다.', 'NO_PENDING_WITHDRAWAL');
  const row = await repo.lockOpen(client, o.accountId);
  if (!row) {
    // 이미 이 request_id 로 철회됐으면 같은 응답(그 사이 재로그인했어도 200)
    const done = await repo.byCancelRequestId(client, o.accountId, o.cancelRequestId);
    if (done && done.state === 'cancelled') return { cancelled: true, renamed_characters: 0, replay: true };
    if (await repo.byRequestId(client, o.accountId, o.cancelRequestId)) {
      throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
    }
    throw new AppError(404, '열린 탈퇴 요청이 없습니다.', 'NO_PENDING_WITHDRAWAL');
  }
  if (row.request_id === o.cancelRequestId) throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
  if (o.via === 'self' && !row.cancel_allowed) throw new AppError(403, '운영자 처리 건이라 직접 철회할 수 없습니다.', 'CANCEL_NOT_ALLOWED');
  if (o.now.getTime() >= row.due_at.getTime()) {
    throw new AppError(o.via === 'self' ? 410 : 409, '철회 가능 기한이 지났습니다.', 'WITHDRAWAL_DUE');
  }
  const renamed = await restoreNames(client, chars, row.saved_character_names);
  await repo.restoreLogin(client, o.accountId);
  await repo.markCancelled(client, row.id, o.via, o.cancelRequestId, o.now);
  return { cancelled: true, renamed_characters: renamed, replay: false };
}
