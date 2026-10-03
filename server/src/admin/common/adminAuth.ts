// 관리자 세션 토큰 검증, 역할 검사, 속도 제한, 감사 정보 지정 미들웨어
import { createHash } from 'node:crypto';
import type { RequestHandler } from 'express';
import { getConfig } from '../../config/env';
import { consumeOrThrow, MINUTE } from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as repo from '../auth/adminAuthRepository';
import { getPool } from '../../db/pool';
import { ROLE_RANK, type AdminCtx, type AdminRole, type AuditMeta } from './adminTypes';
import { ipAllowed } from './cidr';

export const sha256Hex = (s: string): string => createHash('sha256').update(s).digest('hex');

/** 이 요청의 감사 이름을 정한다. 실패하면 오류 처리기가 이 이름으로 한 줄을 남긴다 */
export const audit =
  (meta: AuditMeta): RequestHandler =>
  (_req, res, next) => {
    res.locals.audit = meta;
    next();
  };

/** 출처 CIDR 검사(ADMIN_ALLOWED_CIDRS). 포트를 실수로 열어도 막힌다 */
export const originGuard: RequestHandler = (req, _res, next) => {
  const raw = req.socket.remoteAddress;
  if (!ipAllowed(raw, getConfig().admin.allowedCidrs)) {
    next(new AppError(403, '허용되지 않은 접속 위치입니다.', 'ORIGIN_DENIED'));
    return;
  }
  next();
};

export const requireAdmin =
  (min: AdminRole, opts: { allowSetup?: boolean } = {}): RequestHandler =>
  async (req, res, next) => {
    try {
      const header = req.header('authorization');
      const m = header ? /^Bearer\s+(\S+)$/i.exec(header) : null;
      if (!m || !m[1]) throw new AppError(401, '관리자 로그인이 필요합니다.', 'ADMIN_TOKEN_MISSING');
      const s = await repo.findSession(getPool(), sha256Hex(m[1]));
      if (!s || s.revokedAt || s.admin.disabledAt) throw new AppError(401, '관리자 토큰이 올바르지 않습니다.', 'ADMIN_TOKEN_INVALID');
      const now = getNow();
      const cfg = getConfig().admin;
      if (s.expiresAt.getTime() <= now.getTime() || now.getTime() - s.lastSeenAt.getTime() > cfg.sessionIdleMinutes * 60_000) {
        throw new AppError(401, '관리자 세션이 만료되었습니다.', 'ADMIN_TOKEN_EXPIRED');
      }
      if (s.scope === 'setup' && !opts.allowSetup) {
        throw new AppError(403, '비밀번호 변경과 2단계 인증 등록을 먼저 끝내야 합니다.', 'SETUP_REQUIRED');
      }
      if (ROLE_RANK[s.admin.role] < ROLE_RANK[min]) throw new AppError(403, '권한이 없습니다.', 'FORBIDDEN_ROLE');
      await repo.touchSession(getPool(), s.sessionId, now);
      const ctx: AdminCtx = {
        id: s.admin.id,
        uuid: s.admin.uuid,
        loginId: s.admin.loginId,
        displayName: s.admin.displayName,
        role: s.admin.role,
        scope: s.scope,
        sessionId: s.sessionId,
        sessionExpiresAt: s.expiresAt,
        mustChangePassword: s.admin.mustChangePassword,
        totpConfirmed: s.admin.totpConfirmedAt !== null,
      };
      res.locals.admin = ctx;
      next();
    } catch (err) {
      next(err);
    }
  };

export type RateKind = 'read' | 'mutate' | 'ledger';

/** 관리자별 속도 제한: 조회 ADMIN_RATE_PER_MIN, 변경 30/분, 원장 조회 30/분 */
export const adminRate =
  (kind: RateKind): RequestHandler =>
  (_req, res, next) => {
    try {
      const a = res.locals.admin as AdminCtx;
      const limit = kind === 'read' ? getConfig().admin.ratePerMin : 30;
      consumeOrThrow(`admin-${kind}:${a.id}`, limit, MINUTE);
      next();
    } catch (err) {
      next(err);
    }
  };

export const getAdmin = (locals: Record<string, unknown>): AdminCtx => locals.admin as AdminCtx;
