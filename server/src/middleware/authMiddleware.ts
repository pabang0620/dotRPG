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
}

export function signAccessToken(accountUuid: string): string {
  return jwt.sign({}, getConfig().jwtSecret, {
    algorithm: 'HS256',
    subject: accountUuid,
    expiresIn: ACCESS_EXPIRES_IN_SEC,
  });
}

export const ACCESS_EXPIRES_IN_SEC = 900;

/** 설계 0.2: 토큰 검증 후 매 요청 계정 정지·삭제를 DB로 확인한다. */
export const requireAuth: RequestHandler = async (req, res, next) => {
  try {
    const header = req.header('authorization');
    const m = header ? /^Bearer\s+(\S+)$/i.exec(header) : null;
    if (!m || !m[1]) throw new AppError(401, '로그인이 필요합니다.', 'TOKEN_MISSING');

    let sub: string;
    try {
      const payload = jwt.verify(m[1], getConfig().jwtSecret, { algorithms: ['HS256'] });
      if (typeof payload === 'string' || typeof payload.sub !== 'string') {
        throw new AppError(401, '토큰이 올바르지 않습니다.', 'TOKEN_INVALID');
      }
      sub = payload.sub;
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
      'SELECT id, uuid, banned_until, deleted_at FROM accounts WHERE uuid = $1',
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
    const account: AuthAccount = { id: Number(row.id), uuid: row.uuid };
    res.locals.account = account;
    next();
  } catch (err) {
    next(err);
  }
};

export function getAccount(locals: Record<string, unknown>): AuthAccount {
  return locals.account as AuthAccount;
}
