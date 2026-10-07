import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './levelRewardsController';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import { claimBody, passBuyBody, passClaimBody } from './levelRewardsValidation';

/** 레벨 달성 보상(13단계). 계정 단위: 클라이언트 버전만 검사한다 */
export function createLevelRewardsRouter(): Router {
  const r = Router();
  r.use('/level-rewards', versionCheck({ data: false }), requireAuth);
  const key = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  const read = rateLimit({ name: 'level-rewards-read', limit: () => 30, windowMs: MINUTE, key });
  const write = rateLimit({ name: 'level-rewards-claim', limit: () => 10, windowMs: MINUTE, key });
  r.get('/level-rewards', read, controller.list);
  r.post('/level-rewards/claim', write, validate({ body: claimBody }), controller.claim);
  // 14단계: 성장 패스. 구매는 계정 단위, 수령은 캐릭터 경로(아이템이 그 캐릭터 가방으로 간다)
  r.post('/level-rewards/pass/buy', write, validate({ body: passBuyBody }), controller.passBuy);
  r.post(
    '/characters/:uuid/level-rewards/pass/claim',
    rateLimit({ name: 'pass-claim', limit: () => 10, windowMs: MINUTE, key: charKey }),
    validate({ params: charParams, body: passClaimBody }),
    controller.passClaim,
  );
  return r;
}
