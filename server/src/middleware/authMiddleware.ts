import type { RequestHandler } from 'express';
import jwt from 'jsonwebtoken';
import { getConfig } from '../config/env';
import { query } from '../db/pool';
import { AppError } from '../utils/AppError';

export interface AuthAccount {
  id: number;
  uuid: string;
}

interface AccountRow {
  id: string;
  uuid: string;
  banned_until: Date | null;
  deleted_at: Date | null;
  active_family_id: string | null;
}

/** familyId는 9단계의 세션 id(sid 클레임). 같은 계정이 다른 곳에서 로그인하면 옛 토큰이 즉시 무효가 된다 */
export function signAccessToken(accountUuid: string, familyId?: string): string {
  return jwt.sign(familyId ? { sid: familyId } : {}, getConfig().jwtSecret, {
    algorithm: 'HS256',
    subject: accountUuid,
    expiresIn: ACCESS_EXPIRES_IN_SEC,
  });
}

export const ACCESS_EXPIRES_IN_SEC = 900;

export interface VerifiedToken {
  account: AuthAccount;
  /** 토큰 만료 시각(초 단위 epoch). WebSocket이 갱신 안내에 쓴다 */
  expiresAtSec: number;
  /** 토큰의 세션 id(sid). 배포 전에 발급된 토큰은 null */
  familyId: string | null;
}

/** 토큰 서명·만료를 검사하고 계정 정지·삭제를 DB로 확인한다. REST(requireAuth)와 WebSocket(hello, auth)이 같이 쓴다. */
export async function verifyAccessToken(token: string): Promise<VerifiedToken> {
  let sub: string;
  let exp: number;
  let sid: string | null = null;
  try {
    const payload = jwt.verify(token, getConfig().jwtSecret, { algorithms: ['HS256'] });
    if (typeof payload === 'string' || typeof payload.sub !== 'string' || typeof payload.exp !== 'number') {
      throw new AppError(401, '토큰이 올바르지 않습니다.', 'TOKEN_INVALID');
    }
    sub = payload.sub;
    exp = payload.exp;
    sid = typeof payload.sid === 'string' ? payload.sid : null;
  } catch (err) {
    if (err instanceof AppError) throw err;
    if (err instanceof jwt.TokenExpiredError) {
      throw new AppError(401, '토큰이 만료되었습니다.', 'TOKEN_EXPIRED');
    }
    throw new AppError(401, '토큰이 올바르지 않습니다.', 'TOKEN_INVALID');
  }

  if (!/^[0-9a-f-]{36}$/i.test(sub)) {
    throw new AppError(401, '토큰이 올바르지 않습니다.', 'TOKEN_INVALID');
  }
  const r = await query<AccountRow>(
    'SELECT id, uuid, banned_until, deleted_at, active_family_id FROM accounts WHERE uuid = $1',
    [sub],
  );
  const row = r.rows[0];
  if (!row || row.deleted_at) {
    throw new AppError(401, '토큰이 올바르지 않습니다.', 'TOKEN_INVALID');
  }
  if (row.banned_until && row.banned_until.getTime() > Date.now()) {
    throw new AppError(403, '정지된 계정입니다.', 'ACCOUNT_BANNED', {
      banned_until: row.banned_until.toISOString(),
    });
  }
  // 9단계 E2: 세션 id 대조(이미 읽은 행의 값이라 추가 쿼리 없음). sid 없는 옛 토큰은 active_family_id 가 아직 비어 있을 때만 통과한다
  if (getConfig().aa.sessionSingle && sid !== row.active_family_id) {
    throw new AppError(401, '다른 곳에서 로그인하여 이 접속이 종료되었습니다.', 'SESSION_REPLACED');
  }
  return { account: { id: Number(row.id), uuid: row.uuid }, expiresAtSec: exp, familyId: sid };
}

/** 설계 0.2: 토큰 검증 후 매 요청 계정 정지·삭제를 DB로 확인한다. */
export const requireAuth: RequestHandler = async (req, res, next) => {
  try {
    const header = req.header('authorization');
    const m = header ? /^Bearer\s+(\S+)$/i.exec(header) : null;
    if (!m || !m[1]) throw new AppError(401, '로그인이 필요합니다.', 'TOKEN_MISSING');
    const { account } = await verifyAccessToken(m[1]);
    res.locals.account = account;
    next();
  } catch (err) {
    next(err);
  }
};

export function getAccount(locals: Record<string, unknown>): AuthAccount {
  return locals.account as AuthAccount;
}
