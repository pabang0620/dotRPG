// SA1·SA2: 제재 생성·해제. 자동 제재와 같은 insertSanction(+ banned_until 갱신) 경로를 쓴다(D3).
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { insertSanction, PERMANENT_BAN_AT, recomputeBannedUntil } from '../../domains/chat/sanctionService';
import { AppError } from '../../utils/AppError';
import { runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './sanctionsRepository';
import type { CreateSanctionBody, RevokeBody, SanctionInput } from './sanctionsValidation';

const DURATION_MS: Record<string, number> = { '1h': 3_600_000, '1d': 86_400_000, '7d': 7 * 86_400_000, '30d': 30 * 86_400_000 };
const DAY = 86_400_000;

/** 기간 프리셋을 검사하고 종료 시각을 정한다(임의 시각은 받지 않는다) */
function endsAtOf(admin: AdminCtx, s: SanctionInput, now: Date): Date | null {
  if (s.kind === 'warning') {
    if (s.duration !== undefined) throw new AppError(422, '경고에는 기간이 없습니다.', 'BAD_DURATION');
    return null;
  }
  if (s.duration === undefined) throw new AppError(422, '기간이 필요합니다.', 'BAD_DURATION');
  if (s.duration === 'permanent') {
    if (s.kind === 'chat_mute') throw new AppError(422, '채팅 금지는 영구로 할 수 없습니다.', 'BAD_DURATION');
    if (admin.role !== 'owner') throw new AppError(403, '영구 정지는 owner만 할 수 있습니다.', 'FORBIDDEN_ROLE');
    return PERMANENT_BAN_AT;
  }
  const ms = DURATION_MS[s.duration] as number;
  if (s.kind === 'ban' && admin.role !== 'owner' && ms > getConfig().admin.operatorBanMaxDays * DAY) {
    throw new AppError(403, `operator의 정지는 최대 ${getConfig().admin.operatorBanMaxDays}일입니다.`, 'FORBIDDEN_ROLE');
  }
  return new Date(now.getTime() + ms);
}

export interface AppliedSanction {
  sanction: { id: string; kind: string; reason_code: string; ends_at: string | null };
  banned_until: string | null;
}

/** 한 트랜잭션 안에서 제재를 만든다(계정 행 FOR UPDATE로 정지 변경을 직렬화). 신고 처리(RP4)도 이 함수를 쓴다 */
export async function applySanction(client: PoolClient, admin: AdminCtx, accountUuid: string, s: SanctionInput): Promise<AppliedSanction> {
  const acct = await repo.lockAccount(client, accountUuid);
  if (!acct) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
  const now = new Date();
  const endsAt = endsAtOf(admin, s, now);
  let reportId: number | null = null;
  if (s.report_id) {
    const rep = await repo.lockReport(client, s.report_id);
    if (!rep) throw new AppError(404, '신고를 찾을 수 없습니다.', 'REPORT_NOT_FOUND');
    if (rep.state !== 'open' && rep.state !== 'reviewing') throw new AppError(409, '이미 처리된 신고입니다.', 'REPORT_CLOSED');
    if (rep.target_account_id !== acct.id) throw new AppError(422, '신고 대상 계정과 다릅니다.', 'REPORT_TARGET_MISMATCH');
    reportId = rep.id;
  }
  if (s.kind !== 'warning' && endsAt && (await repo.hasLongerActive(client, acct.id, s.kind, endsAt))) {
    throw new AppError(409, '같은 종류의 제재가 더 긴 기간으로 이미 있습니다. 늘리려면 해제 후 다시 만드세요.', 'SANCTION_EXISTS');
  }
  const created = await insertSanction(client, {
    accountId: acct.id,
    kind: s.kind,
    source: 'admin',
    reasonCode: s.reason_code,
    reportId,
    endsAt,
    createdBy: admin.loginId,
    note: s.note,
  });
  if (reportId !== null) await repo.closeReport(client, reportId, 'actioned', admin.loginId, s.note);
  const banned = s.kind === 'ban' ? await recomputeBannedUntil(client, acct.id) : null;
  return {
    sanction: { id: created.uuid, kind: s.kind, reason_code: s.reason_code, ends_at: endsAt ? endsAt.toISOString() : null },
    banned_until: banned ? banned.toISOString() : null,
  };
}

/** SA1 */
export function createSanction(admin: AdminCtx, ip: string, accountUuid: string, body: CreateSanctionBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'sanction.create',
    targetType: 'account',
    targetUuid: accountUuid,
    requestId: body.request_id,
    params: { kind: body.kind, reason_code: body.reason_code, ...(body.duration ? { duration: body.duration } : {}), ...(body.report_id ? { report_id: body.report_id } : {}), note_length: body.note.length },
    handler: async (client) => ({ status: 201, data: await applySanction(client, admin, accountUuid, body) }),
  });
}

/** SA2: 해제. 정지면 banned_until을 남은 활성 정지 중 가장 늦은 종료로 다시 계산한다(없으면 NULL) */
export function revokeSanction(admin: AdminCtx, ip: string, sanctionUuid: string, body: RevokeBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'sanction.revoke',
    targetType: 'sanction',
    targetUuid: sanctionUuid,
    requestId: body.request_id,
    params: { note_length: body.note.length },
    handler: async (client) => {
      const s0 = await repo.lockSanction(client, sanctionUuid);
      if (!s0) throw new AppError(404, '제재를 찾을 수 없습니다.', 'SANCTION_NOT_FOUND');
      const acct = await client.query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1 FOR UPDATE', [s0.account_id]);
      const s = await repo.lockSanction(client, sanctionUuid);
      if (!s || s.revoked_at) throw new AppError(409, '이미 해제된 제재입니다.', 'SANCTION_REVOKED');
      await repo.revoke(client, s.id, admin.loginId, body.note);
      const banned = s.kind === 'ban' ? await recomputeBannedUntil(client, s.account_id) : null;
      return {
        status: 200,
        data: { sanction: { id: sanctionUuid, revoked: true }, banned_until: banned ? banned.toISOString() : null, account: acct.rows[0]?.uuid },
      };
    },
  });
}
