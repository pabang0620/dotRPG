import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './sealedBoxController';
import { openBody, pullBody } from './sealedBoxValidation';

/**
 * 봉인된 상자(14단계). GET /starshop/sealed 는 계정 단위(클라이언트 버전만 검사),
 * 뽑기·열기는 /characters/:uuid 아래라 인증·버전은 /characters 공통 미들웨어가 한다
 */
export function createSealedBoxRouter(): Router {
  const r = Router();
  const acct = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  r.get('/starshop/sealed', versionCheck({ data: false }), requireAuth, rateLimit({ name: 'sealed-read', limit: () => 30, windowMs: MINUTE, key: acct }), controller.summary);
  const perChar = rateLimit({ name: 'sealed', limit: () => 3, windowMs: 1000, key: charKey });
  const perAccount = rateLimit({ name: 'sealed_account', limit: () => 4, windowMs: 1000, key: acct });
  // 별조각 소비 경로: 계정당 분당 상한(뽑기·교환과 같다)
  const spendPerMin = rateLimit({ name: 'sealed_spend', limit: (c) => c.pay.rate.spendPerMin, windowMs: MINUTE, key: acct });
  r.post('/characters/:uuid/starshop/sealed/pull', perChar, perAccount, spendPerMin, validate({ params: charParams, body: pullBody }), controller.pull);
  r.post('/characters/:uuid/items/open', perChar, perAccount, validate({ params: charParams, body: openBody }), controller.open);
  return r;
}
