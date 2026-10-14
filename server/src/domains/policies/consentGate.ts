// 정책 동의 강제(POLICY_CONSENT_ENFORCE=true): 필요한 버전에 동의하지 않은 계정의 게임 API를 428 CONSENT_REQUIRED로 막는다.
// 토큰 검증은 뒤의 requireAuth가 한다. 여기서는 서명 확인 없이 sub(계정 uuid)만 읽어 동의 상태를 본다:
// 위조 토큰이면 requireAuth가 401로 거절하므로, 이 단계는 "동의 여부"만 판단하면 된다. 상태는 계정별로 60초 캐시한다.
import type { RequestHandler } from 'express';
import jwt from 'jsonwebtoken';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { agreedOfUuid } from './policyRepository';
import { statusFrom } from './policyService';

const TTL_MS = 60_000;
const cache = new Map<string, { needs: boolean; at: number }>();

/** 동의 직후(POST /me/consents) 바로 풀리도록 */
export function clearConsentCache(accountUuid?: string): void {
  if (accountUuid) cache.delete(accountUuid);
  else cache.clear();
}

const EXEMPT = /^\/(me\/consents|me\/withdrawal|auth\/|policies|health|system\/|meta)/;
// 약관 버전이 바뀐 뒤 거부해도 마지막 진행분 저장(PUT /characters/:uuid/state)은 되도록 둔다
const STATE_SAVE = /^\/characters\/[0-9a-f-]{36}\/state\/?$/i;

async function needsConsent(accountUuid: string): Promise<boolean> {
  const hit = cache.get(accountUuid);
  if (hit && Date.now() - hit.at < TTL_MS) return hit.needs;
  const needs = statusFrom(await agreedOfUuid(getPool(), accountUuid)).needs;
  if (cache.size > 50_000) cache.clear();
  cache.set(accountUuid, { needs, at: Date.now() });
  return needs;
}

export const consentGate: RequestHandler = async (req, _res, next) => {
  try {
    if (!getConfig().policyConsentEnforce || EXEMPT.test(req.path) || (req.method === 'PUT' && STATE_SAVE.test(req.path))) return next();
    const m = /^Bearer\s+(\S+)$/i.exec(req.header('authorization') ?? '');
    if (!m) return next();
    const decoded = jwt.decode(m[1] as string);
    const sub = decoded && typeof decoded === 'object' && typeof decoded.sub === 'string' ? decoded.sub : null;
    if (!sub || !/^[0-9a-f-]{36}$/i.test(sub)) return next();
    if (await needsConsent(sub)) throw new AppError(428, '약관 동의가 필요합니다.', 'CONSENT_REQUIRED');
    next();
  } catch (err) {
    next(err);
  }
};
