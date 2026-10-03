// RP1~RP4: 신고 대기열, 증거 보기, 검토 시작, 처리(기각 또는 제재와 함께)
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import { closeReport } from '../sanctions/sanctionsRepository';
import { applySanction, type AppliedSanction } from '../sanctions/sanctionsService';
import * as repo from './reportsRepository';
import type { ListQuery, ResolveBody } from './reportsValidation';

const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);

/** RP1: 증거 줄은 싣지 않는다(viewer도 볼 수 있다) */
export async function list(q: ListQuery) {
  const rows = await repo.queue(getPool(), q.state.split(','), q.reason ?? null);
  return {
    reports: rows.map((r) => ({
      id: r.uuid,
      reason: r.reason,
      target_name: r.target_name,
      line_count: r.line_count,
      created_at: r.created_at.toISOString(),
      state: r.state,
      open_for_same_target: Number(r.same_target_open),
    })),
  };
}

/** RP2: 증거 줄 포함(operator 이상). report.view로 남긴다 */
export async function detail(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const r = await repo.findByUuid(db, uuid);
  if (!r) throw new AppError(404, '신고를 찾을 수 없습니다.', 'REPORT_NOT_FOUND');
  const [lines, sanctions, others, ratio] = await Promise.all([
    repo.lines(db, r.id),
    repo.targetSanctions(db, r.target_account_id),
    repo.otherReports(db, r.target_account_id, r.id),
    repo.reporterRatio(db, r.reporter_account_id),
  ]);
  await auditView(admin, ip, 'report.view', 'report', uuid);
  const total = Number(ratio.total);
  return {
    report: {
      id: r.uuid,
      state: r.state,
      reason: r.reason,
      target: { account_id: r.target_account_uuid, character_id: r.target_character_uuid, name: r.target_name },
      created_at: r.created_at.toISOString(),
      handled_at: iso(r.handled_at),
      handled_by: r.handled_by,
      note: r.note,
    },
    lines: lines.map((l) => ({
      seq: Number(l.seq),
      channel: l.channel,
      sender_name: l.sender_name,
      recipient_name: l.recipient_name,
      text: l.text,
      sent_at: l.sent_at.toISOString(),
      is_target: l.is_target,
    })),
    target_sanctions: sanctions.map((s) => ({
      id: s.uuid, kind: s.kind, reason_code: s.reason_code, starts_at: s.starts_at.toISOString(), ends_at: iso(s.ends_at), revoked: s.revoked_at !== null, by: s.created_by,
    })),
    other_reports_30d: others.map((o) => ({ id: o.uuid, reason: o.reason, state: o.state, at: o.created_at.toISOString() })),
    reporter: { total_reports: total, dismissed_ratio: total === 0 ? null : Number(ratio.dismissed) / total },
  };
}

/** RP3 */
export function take(admin: AdminCtx, ip: string, uuid: string, requestId: string): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'report.take',
    targetType: 'report',
    targetUuid: uuid,
    requestId,
    params: {},
    handler: async (client) => {
      const r = await repo.findByUuid(client, uuid, true);
      if (!r) throw new AppError(404, '신고를 찾을 수 없습니다.', 'REPORT_NOT_FOUND');
      if (r.state !== 'open') throw new AppError(409, '검토를 시작할 수 없는 상태입니다.', 'REPORT_CLOSED');
      await repo.markReviewing(client, r.id, admin.loginId);
      return { status: 200, data: { report: { id: uuid, state: 'reviewing' } } };
    },
  });
}

/** RP4: 기각 또는 제재와 함께 처리. 제재는 SA1과 같은 내부 함수로 만든다 */
export function resolve(admin: AdminCtx, ip: string, uuid: string, body: ResolveBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'report.resolve',
    targetType: 'report',
    targetUuid: uuid,
    requestId: body.request_id,
    params: {
      decision: body.decision,
      note_length: body.note.length,
      close_similar: body.close_similar,
      ...(body.sanction ? { sanction: { kind: body.sanction.kind, reason_code: body.sanction.reason_code, ...(body.sanction.duration ? { duration: body.sanction.duration } : {}) } } : {}),
    },
    handler: async (client) => {
      const r = await repo.findByUuid(client, uuid, true);
      if (!r) throw new AppError(404, '신고를 찾을 수 없습니다.', 'REPORT_NOT_FOUND');
      if (r.state !== 'open' && r.state !== 'reviewing') throw new AppError(409, '이미 처리된 신고입니다.', 'REPORT_CLOSED');
      let applied: AppliedSanction | null = null;
      if (body.decision === 'sanction' && body.sanction) {
        applied = await applySanction(client, admin, r.target_account_uuid, { ...body.sanction, report_id: uuid, note: body.note });
      } else {
        await closeReport(client, r.id, 'dismissed', admin.loginId, body.note);
      }
      const merged = body.close_similar ? await repo.closeSimilar(client, r, applied ? 'actioned' : 'dismissed', admin.loginId) : 0;
      return {
        status: 200,
        data: { report: { id: uuid, state: applied ? 'actioned' : 'dismissed' }, ...(applied ? { sanction: applied.sanction, banned_until: applied.banned_until } : {}), merged },
      };
    },
  });
}
