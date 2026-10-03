// AM1~AM4: 관리자 계정 관리(owner 전용)와 감사 로그 조회
import { getPool } from '../../db/pool';
import { isUniqueViolation } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as authRepo from '../auth/adminAuthRepository';
import { hashPassword, newTempPassword } from '../auth/adminAuthService';
import { runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './adminsRepository';
import type { AdminActionBody, AuditQuery, CreateAdminBody } from './adminsValidation';

const view = (a: authRepo.AdminRow) => ({
  id: a.uuid,
  login_id: a.loginId,
  display_name: a.displayName,
  role: a.role,
  disabled: a.disabledAt !== null,
  totp_enrolled: a.totpConfirmedAt !== null,
  must_change_password: a.mustChangePassword,
  locked_until: a.lockedUntil && a.lockedUntil.getTime() > Date.now() ? a.lockedUntil.toISOString() : null,
  last_login_at: a.lastLoginAt ? a.lastLoginAt.toISOString() : null,
  created_at: a.createdAt.toISOString(),
});

export async function listAdmins() {
  return { admins: (await repo.listAdmins(getPool())).map(view) };
}

/** AM2: 임시 비밀번호는 응답에 한 번만 나가고 감사 로그(response)에는 남지 않는다 */
export function createAdmin(admin: AdminCtx, ip: string, body: CreateAdminBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'admin.create',
    targetType: 'admin',
    requestId: body.request_id,
    params: { login_id: body.login_id, role: body.role },
    handler: async (client) => {
      const temp = newTempPassword();
      try {
        const a = await repo.insertAdmin(client, {
          loginId: body.login_id,
          displayName: body.display_name,
          role: body.role,
          passwordHash: await hashPassword(temp),
          createdBy: admin.id,
        });
        return {
          status: 201,
          data: { admin: view(a), temp_password: temp, note: '임시 비밀번호는 지금 한 번만 표시됩니다.' },
          stored: { admin: view(a) },
          targetUuid: a.uuid,
        };
      } catch (err) {
        if (isUniqueViolation(err)) throw new AppError(409, '이미 사용 중인 아이디입니다.', 'LOGIN_ID_TAKEN');
        throw err;
      }
    },
  });
}

/** AM3 */
export function adminAction(admin: AdminCtx, ip: string, targetUuid: string, body: AdminActionBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    // 감사 action 이름은 소문자·밑줄만 허용된다(0008 admin_audit_action_chk): reset_2fa는 reset_totp로 기록한다
    action: `admin.${body.action === 'reset_2fa' ? 'reset_totp' : body.action}`,
    targetType: 'admin',
    targetUuid,
    requestId: body.request_id,
    params: { action: body.action, ...(body.role ? { role: body.role } : {}) },
    handler: async (client) => {
      const target = await repo.findByUuid(client, targetUuid, true);
      if (!target) throw new AppError(404, '관리자를 찾을 수 없습니다.', 'ADMIN_NOT_FOUND');
      const now = getNow();
      let temp: string | null = null;
      switch (body.action) {
        case 'set_role': {
          if (!body.role) throw new AppError(422, '역할이 필요합니다.', 'ROLE_REQUIRED');
          if (target.id === admin.id) throw new AppError(409, '자기 자신의 역할은 바꿀 수 없습니다.', 'SELF_ACTION');
          if (target.role === 'owner' && body.role !== 'owner' && !target.disabledAt && (await repo.activeOwnerCount(client)) <= 1) {
            throw new AppError(409, '마지막 owner의 역할은 바꿀 수 없습니다.', 'LAST_OWNER');
          }
          await repo.setRole(client, target.id, body.role);
          await authRepo.revokeAllSessions(client, target.id, now);
          break;
        }
        case 'disable': {
          if (target.id === admin.id) throw new AppError(409, '자기 자신은 비활성화할 수 없습니다.', 'SELF_ACTION');
          if (target.role === 'owner' && !target.disabledAt && (await repo.activeOwnerCount(client)) <= 1) {
            throw new AppError(409, '마지막 owner는 비활성화할 수 없습니다.', 'LAST_OWNER');
          }
          await repo.setDisabled(client, target.id, now);
          await authRepo.revokeAllSessions(client, target.id, now);
          break;
        }
        case 'enable':
          await repo.setDisabled(client, target.id, null);
          break;
        case 'reset_2fa':
          await repo.resetTotp(client, target.id);
          await authRepo.revokeAllSessions(client, target.id, now);
          break;
        case 'unlock':
          await repo.unlock(client, target.id);
          break;
        case 'reset_password':
          temp = newTempPassword();
          await authRepo.setPassword(client, target.id, await hashPassword(temp), true, now);
          await authRepo.revokeAllSessions(client, target.id, now);
          break;
      }
      const after = (await repo.findByUuid(client, targetUuid)) as authRepo.AdminRow;
      return {
        status: 200,
        data: { admin: view(after), ...(temp ? { temp_password: temp, note: '임시 비밀번호는 지금 한 번만 표시됩니다.' } : {}) },
        stored: { admin: view(after) },
      };
    },
  });
}

/** AM4 */
export async function queryAudit(q: AuditQuery) {
  let adminId: number | null = null;
  if (q.admin) {
    const a = await repo.findByUuid(getPool(), q.admin);
    if (!a) return { entries: [], next_before: null };
    adminId = a.id;
  }
  const rows = await repo.queryAudit(getPool(), {
    adminId,
    targetUuid: q.target ?? null,
    action: q.action ?? null,
    result: q.result ?? null,
    since: q.since ?? null,
    until: q.until ?? null,
    before: q.before ? Number(q.before) : null,
    limit: q.limit,
  });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return {
    entries: page.map((r) => ({
      id: r.uuid,
      at: r.created_at.toISOString(),
      admin: r.admin_uuid ? { id: r.admin_uuid, login_id: r.login_id } : null,
      login_id_tried: r.login_id_tried,
      action: r.action,
      target_type: r.target_type,
      target_id: r.target_uuid,
      result: r.result,
      error_code: r.error_code,
      params: r.params,
      ip: r.ip,
    })),
    next_before: rows.length > q.limit && last ? last.id : null,
  };
}
