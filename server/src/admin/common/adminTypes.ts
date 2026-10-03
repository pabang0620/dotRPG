export type AdminRole = 'viewer' | 'operator' | 'owner';
export type AdminScope = 'full' | 'setup';

export const ROLE_RANK: Record<AdminRole, number> = { viewer: 1, operator: 2, owner: 3 };

/** 인증된 관리자(요청 하나 동안 res.locals.admin) */
export interface AdminCtx {
  id: number;
  uuid: string;
  loginId: string;
  displayName: string;
  role: AdminRole;
  scope: AdminScope;
  sessionId: number;
  sessionExpiresAt: Date;
  mustChangePassword: boolean;
  totpConfirmed: boolean;
}

export type AuditTarget =
  | 'account'
  | 'character'
  | 'report'
  | 'dungeon_run'
  | 'sanction'
  | 'maintenance'
  | 'job'
  | 'admin'
  | 'mail'
  | 'server';

/** 라우트가 res.locals.audit에 싣는 감사 정보(실패 때 오류 처리기가 한 줄을 남긴다) */
export interface AuditMeta {
  action: string;
  targetType?: AuditTarget;
}
