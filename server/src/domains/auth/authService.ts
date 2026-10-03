import { createHash, randomBytes, randomUUID } from 'node:crypto';
import argon2 from 'argon2';
import { getConfig } from '../../config/env';
import { isUniqueViolation, withTransaction } from '../../db/pool';
import { ACCESS_EXPIRES_IN_SEC, signAccessToken } from '../../middleware/authMiddleware';
import {
  HOUR,
  MINUTE,
  consumeOrThrow,
  getRateLimitStore,
} from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import * as repo from './authRepository';

const REFRESH_DAYS = 14;
const ARGON_OPTS = { type: argon2.argon2id } as const;

let dummyHash: string | null = null;
async function getDummyHash(): Promise<string> {
  if (!dummyHash) dummyHash = await argon2.hash(randomBytes(16).toString('hex'), ARGON_OPTS);
  return dummyHash;
}

const sha256 = (s: string): string => createHash('sha256').update(s).digest('hex');
const newRefreshToken = (): string => randomBytes(32).toString('base64url');

export interface TokenPair {
  access_token: string;
  access_expires_in: number;
  refresh_token: string;
  refresh_expires_at: string;
}

async function issueTokens(
  client: Parameters<typeof repo.insertRefreshToken>[0],
  accountId: number,
  accountUuid: string,
  familyId: string,
): Promise<TokenPair> {
  const refresh = newRefreshToken();
  const expiresAt = new Date(Date.now() + REFRESH_DAYS * 24 * 60 * 60 * 1000);
  await repo.insertRefreshToken(client, accountId, familyId, sha256(refresh), expiresAt);
  return {
    access_token: signAccessToken(accountUuid),
    access_expires_in: ACCESS_EXPIRES_IN_SEC,
    refresh_token: refresh,
    refresh_expires_at: expiresAt.toISOString(),
  };
}

export async function register(loginId: string, password: string, ip: string) {
  consumeOrThrow(`register:${ip}`, getConfig().rate.registerIp, HOUR);
  const secretHash = await argon2.hash(password, ARGON_OPTS);
  try {
    return await withTransaction(async (client) => {
      const account = await repo.insertAccount(client);
      await repo.insertDevIdentity(client, account.id, loginId, secretHash);
      const tokens = await issueTokens(client, account.id, account.uuid, randomUUID());
      return {
        account: { id: account.uuid, created_at: account.created_at.toISOString() },
        ...tokens,
      };
    });
  } catch (err) {
    if (isUniqueViolation(err)) {
      throw new AppError(409, '이미 사용 중인 아이디입니다.', 'LOGIN_ID_TAKEN');
    }
    throw err;
  }
}

export async function login(loginId: string, password: string, ip: string) {
  const cfg = getConfig();
  consumeOrThrow(`login-ip:${ip}`, cfg.rate.loginIp, MINUTE);

  const failKey = `login-fail:${loginId}`;
  const failWindow = 15 * MINUTE;
  const store = getRateLimitStore();
  const { count, retryAfterSec } = store.count(failKey, failWindow);
  if (count >= cfg.rate.loginFail) {
    throw new AppError(429, '요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.', 'RATE_LIMITED', {
      retry_after_sec: retryAfterSec,
    });
  }

  const identity = await repo.findDevIdentity(loginId);
  // 아이디가 없어도 더미 해시와 비교해 응답 시간을 맞춘다
  const ok = await argon2.verify(identity ? identity.secret_hash : await getDummyHash(), password);
  if (!identity || !ok || identity.deleted_at) {
    store.add(failKey, failWindow);
    throw new AppError(401, '아이디 또는 비밀번호가 올바르지 않습니다.', 'INVALID_CREDENTIALS');
  }
  if (identity.banned_until && identity.banned_until.getTime() > Date.now()) {
    throw new AppError(403, '정지된 계정입니다.', 'ACCOUNT_BANNED', {
      banned_until: identity.banned_until.toISOString(),
    });
  }
  store.reset(failKey);

  return withTransaction(async (client) => {
    const lastLogin = await repo.touchLastLogin(client, identity.id);
    const tokens = await issueTokens(client, identity.id, identity.uuid, randomUUID());
    return {
      account: {
        id: identity.uuid,
        created_at: identity.created_at.toISOString(),
        last_login_at: lastLogin.toISOString(),
      },
      ...tokens,
    };
  });
}

type RefreshOutcome =
  | { kind: 'ok'; tokens: TokenPair }
  | { kind: 'invalid' }
  | { kind: 'reused' }
  | { kind: 'expired' }
  | { kind: 'banned'; until: Date };

export async function refresh(refreshToken: string): Promise<TokenPair> {
  const hash = sha256(refreshToken);
  const accountId = await repo.findRefreshAccountId(hash);
  if (accountId !== null) {
    consumeOrThrow(`refresh:${accountId}`, getConfig().rate.refreshAccount, MINUTE);
  }

  // 재사용 감지 시 폐기를 커밋해야 하므로 던지지 않고 결과를 돌려준 뒤 밖에서 던진다
  const outcome = await withTransaction<RefreshOutcome>(async (client) => {
    const row = await repo.findRefreshForUpdate(client, hash);
    if (!row) return { kind: 'invalid' };
    if (row.revoked_at || row.used_at) {
      await repo.revokeFamily(client, row.family_id);
      logger.warn({ accountId: row.account_id, familyId: row.family_id }, 'refresh token reuse detected');
      return { kind: 'reused' };
    }
    if (row.expires_at.getTime() <= Date.now()) return { kind: 'expired' };

    const account = await repo.findAccountById(client, row.account_id);
    if (!account || account.deleted_at) return { kind: 'invalid' };
    if (account.banned_until && account.banned_until.getTime() > Date.now()) {
      return { kind: 'banned', until: account.banned_until };
    }
    await repo.markRefreshUsed(client, row.id);
    return { kind: 'ok', tokens: await issueTokens(client, account.id, account.uuid, row.family_id) };
  });

  switch (outcome.kind) {
    case 'ok':
      return outcome.tokens;
    case 'reused':
      throw new AppError(401, '이미 사용된 토큰입니다. 다시 로그인해 주세요.', 'REFRESH_REUSED');
    case 'expired':
      throw new AppError(401, '로그인이 만료되었습니다. 다시 로그인해 주세요.', 'REFRESH_EXPIRED');
    case 'banned':
      throw new AppError(403, '정지된 계정입니다.', 'ACCOUNT_BANNED', {
        banned_until: outcome.until.toISOString(),
      });
    default:
      throw new AppError(401, '토큰이 올바르지 않습니다.', 'REFRESH_INVALID');
  }
}

export async function logout(refreshToken: string): Promise<{ logged_out: true }> {
  const hash = sha256(refreshToken);
  await withTransaction(async (client) => {
    const family = await repo.findFamilyByHash(client, hash);
    if (family) await repo.revokeFamily(client, family);
  });
  return { logged_out: true };
}

export const CHARACTER_LIMIT = 4;

export async function getMe(accountId: number) {
  const me = await repo.findMe(accountId);
  if (!me) throw new AppError(401, '토큰이 올바르지 않습니다.', 'TOKEN_INVALID');
  return {
    account: { id: me.uuid, created_at: me.created_at.toISOString() },
    provider: me.provider,
    login_id: me.subject,
    character_count: me.character_count,
    character_limit: CHARACTER_LIMIT,
  };
}

