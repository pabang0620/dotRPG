import { createHash, randomBytes, randomUUID } from 'node:crypto';
import argon2 from 'argon2';
import { getConfig } from '../../config/env';
import { isUniqueViolation, withTransaction } from '../../db/pool';
import { ACCESS_EXPIRES_IN_SEC, signAccessToken } from '../../middleware/authMiddleware';
import { validationError } from '../../middleware/validationMiddleware';
import { accessSignal, recordLogin, recordRefreshIfChanged, type AccessSignal, type DeviceInput } from '../antiabuse/deviceRecords';
import { beginSession } from '../antiabuse/sessionService';
import {
  HOUR,
  MINUTE,
  consumeOrThrow,
  getRateLimitStore,
} from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { metrics } from '../../ops/metrics';
import { logger } from '../../utils/logger';
import * as repo from './authRepository';
import { verifyTicket } from './steamProvider';
import { reconcileOpenOrderQuietly } from '../payments/paymentsService';
import { applyCarry, checkRejoin } from '../withdrawal/withdrawalIdentity';
import { pendingWithdrawalError } from '../withdrawal/withdrawalLogin';

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

/** 9단계: DEVICE_INFO_REQUIRED=true 인데 device 가 없으면 400 VALIDATION */
function requireDevice(device: DeviceInput | undefined): void {
  if (getConfig().aa.deviceInfoRequired && !device) {
    throw validationError([{ path: 'device', message: '필수 값입니다' }]);
  }
}

async function issueTokens(
  client: Parameters<typeof repo.insertRefreshToken>[0],
  accountId: number,
  accountUuid: string,
  familyId: string,
  bind: { installId: string | null; deviceHash: string | null },
): Promise<TokenPair> {
  const refresh = newRefreshToken();
  const expiresAt = new Date(Date.now() + REFRESH_DAYS * 24 * 60 * 60 * 1000);
  await repo.insertRefreshToken(client, accountId, familyId, sha256(refresh), expiresAt, bind.installId, bind.deviceHash);
  return {
    access_token: signAccessToken(accountUuid, familyId),
    access_expires_in: ACCESS_EXPIRES_IN_SEC,
    refresh_token: refresh,
    refresh_expires_at: expiresAt.toISOString(),
  };
}

export interface AccessMeta {
  device?: DeviceInput | undefined;
  clientVersion?: string | undefined;
}

/** 로그인 성공 직후 같은 트랜잭션: 세션 교체, 토큰 발급, 로그인 기록 */
async function openSession(
  client: Parameters<typeof repo.insertRefreshToken>[0] & Parameters<typeof beginSession>[0],
  acc: { id: number; uuid: string },
  kind: 'register' | 'login' | 'steam_login',
  sig: AccessSignal,
  extra: { steamId?: string; steamOwnerId?: string | null } = {},
) {
  const familyId = randomUUID();
  const session = await beginSession(client, acc.id, familyId, sig);
  const tokens = await issueTokens(client, acc.id, acc.uuid, familyId, sig);
  await recordLogin(client, acc.id, kind, sig, { steamId: extra.steamId ?? null, steamOwnerId: extra.steamOwnerId ?? null, flags: session.replacedOther ? ['replaced_other'] : [] });
  return { tokens, session: { replaced_other: session.replacedOther } };
}

export async function register(loginId: string, password: string, ip: string, meta: AccessMeta = {}) {
  consumeOrThrow(`register:${ip}`, getConfig().rate.registerIp, HOUR);
  requireDevice(meta.device);
  const sig = accessSignal(meta.device, ip, meta.clientVersion);
  const secretHash = await argon2.hash(password, ARGON_OPTS);
  try {
    return await withTransaction(async (client) => {
      const account = await repo.insertAccount(client);
      await repo.insertDevIdentity(client, account.id, loginId, secretHash);
      const { tokens, session } = await openSession(client, account, 'register', sig);
      return {
        account: { id: account.uuid, created_at: account.created_at.toISOString() },
        ...tokens,
        session,
      };
    });
  } catch (err) {
    if (isUniqueViolation(err)) {
      throw new AppError(409, '이미 사용 중인 아이디입니다.', 'LOGIN_ID_TAKEN');
    }
    throw err;
  }
}

export async function login(loginId: string, password: string, ip: string, meta: AccessMeta = {}) {
  const cfg = getConfig();
  consumeOrThrow(`login-ip:${ip}`, cfg.rate.loginIp, MINUTE);
  requireDevice(meta.device);
  const sig = accessSignal(meta.device, ip, meta.clientVersion);

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
  if (!identity || !ok) {
    store.add(failKey, failWindow);
    metrics.recordLoginFailure();
    throw new AppError(401, '아이디 또는 비밀번호가 올바르지 않습니다.', 'INVALID_CREDENTIALS');
  }
  if (identity.deleted_at) {
    // 비밀번호가 맞은 뒤에만 탈퇴 유예를 알린다(틀리면 위의 INVALID_CREDENTIALS: 계정 존재를 알리지 않는다)
    const pending = await pendingWithdrawalError(identity.id);
    if (pending) throw pending;
    store.add(failKey, failWindow);
    metrics.recordLoginFailure();
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
    const { tokens, session } = await openSession(client, identity, 'login', sig);
    return {
      account: {
        id: identity.uuid,
        created_at: identity.created_at.toISOString(),
        last_login_at: lastLogin.toISOString(),
      },
      ...tokens,
      session,
    };
  });
}

type RefreshOutcome =
  | { kind: 'ok'; tokens: TokenPair }
  | { kind: 'invalid' }
  | { kind: 'reused' }
  | { kind: 'replaced' }
  | { kind: 'expired' }
  | { kind: 'banned'; until: Date };

export async function refresh(refreshToken: string, meta: AccessMeta = {}, ip = ''): Promise<TokenPair> {
  requireDevice(meta.device);
  const sig = accessSignal(meta.device, ip, meta.clientVersion);
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
      // 9단계: 같은 계정의 다른 곳 로그인으로 정상 교체된 가족은 도난 의심(REFRESH_REUSED)이 아니다
      if (row.revoked_at && row.revoke_reason === 'replaced') return { kind: 'replaced' };
      await repo.revokeFamily(client, row.family_id, 'reuse');
      logger.warn({ accountId: row.account_id, familyId: row.family_id }, 'refresh token reuse detected');
      return { kind: 'reused' };
    }
    if (row.expires_at.getTime() <= Date.now()) return { kind: 'expired' };

    const account = await repo.findAccountById(client, row.account_id);
    if (!account || account.deleted_at) return { kind: 'invalid' };
    if (account.banned_until && account.banned_until.getTime() > Date.now()) {
      return { kind: 'banned', until: account.banned_until };
    }
    // 리프레시 기기 바인딩(3.3): 가족을 만든 로그인의 기기와 다르면 log 는 기록만, enforce 는 도용 의심으로 가족 폐기
    const flags: string[] = [];
    const bindMode = getConfig().aa.refreshDeviceBind;
    if (bindMode !== 'off' && sig.deviceHash && row.device_hash && sig.deviceHash !== row.device_hash) {
      if (bindMode === 'enforce') {
        await repo.revokeFamily(client, row.family_id, 'device_mismatch');
        logger.warn({ accountId: account.id, familyId: row.family_id }, 'refresh device mismatch');
        return { kind: 'invalid' };
      }
      flags.push('device_mismatch');
    }
    await repo.markRefreshUsed(client, row.id);
    await repo.adoptFamilyIfNone(client, account.id, row.family_id);
    // 회전은 가족을 만든 로그인의 기기 바인딩을 그대로 이어 간다
    const tokens = await issueTokens(client, account.id, account.uuid, row.family_id, { installId: sig.installId, deviceHash: row.device_hash ?? sig.deviceHash });
    await recordRefreshIfChanged(client, account.id, sig, flags);
    return { kind: 'ok', tokens };
  });

  switch (outcome.kind) {
    case 'ok':
      return outcome.tokens;
    case 'reused':
      throw new AppError(401, '이미 사용된 토큰입니다. 다시 로그인해 주세요.', 'REFRESH_REUSED');
    case 'replaced':
      throw new AppError(401, '다른 곳에서 로그인하여 이 접속이 종료되었습니다.', 'SESSION_REPLACED');
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
    if (family) await repo.revokeFamily(client, family, 'logout');
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
    steam_linked: await repo.hasSteam(accountId),
    // 탈퇴 유예 중에는 토큰이 없어 이 API를 부를 수 없다: 활동 계정은 항상 null(안내용 자리)
    withdrawal: null,
  };
}


// ---------- Steam (4단계) ----------

const STEAM_UNIQUE = 'auth_identities_provider_subject_key';
const ACCOUNT_PROVIDER_UNIQUE = 'auth_identities_account_provider_uq';

/** A1: Steam 티켓으로 로그인(없으면 계정 생성). 클라이언트가 SteamID를 보내도 받지 않는다 */
export async function steamLogin(ticket: string, meta: AccessMeta = {}, ip = '') {
  requireDevice(meta.device);
  const sig = accessSignal(meta.device, ip, meta.clientVersion);
  const id = await verifyTicket(ticket);
  // 패밀리 공유면 앱 소유자의 SteamID를 사람 키로 기록한다(같은 소유자의 여러 계정을 한 사람으로 센다)
  const ownerId = id.ownerSteamId && id.ownerSteamId !== id.steamId ? id.ownerSteamId : null;
  let created = false;
  let account = await repo.findAccountBySteam(id.steamId);
  if (!account) {
    try {
      account = await withTransaction(async (client) => {
        // 탈퇴 이월 표시(9.3): 정지가 남았으면 계정을 만들지 않고 403, 결제·경제 이월은 만든 계정에 건다
        const carry = await checkRejoin(client, id.steamId);
        const a = await repo.insertAccount(client);
        await repo.insertSteamIdentity(client, a.id, id.steamId, ownerId);
        if (carry) await applyCarry(client, a.id, carry);
        return a;
      });
      created = true;
    } catch (err) {
      if (!isUniqueViolation(err, STEAM_UNIQUE)) throw err;
      account = await repo.findAccountBySteam(id.steamId);
    }
  }
  if (account?.deleted_at) {
    // 탈퇴 유예 중: 새 계정을 만들지 않고 철회 안내(E1). 열린 요청이 없으면 기존처럼 무효 처리
    const pending = await pendingWithdrawalError(account.id);
    if (pending) throw pending;
  }
  if (!account || account.deleted_at) throw new AppError(401, 'Steam 티켓이 올바르지 않습니다.', 'STEAM_TICKET_INVALID');
  if (account.banned_until && account.banned_until.getTime() > Date.now()) {
    throw new AppError(403, '정지된 계정입니다.', 'ACCOUNT_BANNED', { banned_until: account.banned_until.toISOString() });
  }
  const acc = account;
  const result = await withTransaction(async (client) => {
    const lastLogin = await repo.touchLastLogin(client, acc.id);
    await repo.setSteamOwner(client, acc.id, ownerId);
    const { tokens, session } = await openSession(client, acc, 'steam_login', sig, { steamId: id.steamId, steamOwnerId: ownerId });
    return {
      account: { id: acc.uuid, created_at: acc.created_at.toISOString(), last_login_at: lastLogin.toISOString() },
      ...tokens,
      session,
      created,
    };
  });
  // 11단계 7.4: 로그인 직후 그 계정의 열린 결제 주문 1개를 즉시 대사한다(응답을 기다리지 않는다)
  if (!created) reconcileOpenOrderQuietly(acc.id);
  return result;
}

/** A2: 기존 계정에 Steam 연결 */
export async function steamLink(accountId: number, ticket: string) {
  const id = await verifyTicket(ticket);
  try {
    const ownerId = id.ownerSteamId && id.ownerSteamId !== id.steamId ? id.ownerSteamId : null;
    await withTransaction((client) => repo.insertSteamIdentity(client, accountId, id.steamId, ownerId));
  } catch (err) {
    if (isUniqueViolation(err, STEAM_UNIQUE)) throw new AppError(409, '이미 다른 계정에 연결된 Steam 계정입니다.', 'STEAM_ALREADY_LINKED');
    if (isUniqueViolation(err, ACCOUNT_PROVIDER_UNIQUE)) throw new AppError(409, '이미 Steam이 연결된 계정입니다.', 'ACCOUNT_ALREADY_LINKED');
    throw err;
  }
  return { linked: true };
}
