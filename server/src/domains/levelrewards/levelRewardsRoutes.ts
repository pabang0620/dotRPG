import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import { AppError } from '../../utils/AppError';
import * as controller from './levelRewardsController';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import { claimBody, passBuyBody, passClaimBody } from './levelRewardsValidation';

/** 수령 자격은 현재 캐릭터 레벨, 지급 횟수와 패스 구매는 계정 단위. */
export function createLevelRewardsRouter(): Router {
  const r = Router();
  r.use('/level-rewards', versionCheck({ data: false }), requireAuth);
  const key = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  const read = rateLimit({ name: 'level-rewards-read', limit: () => 30, windowMs: MINUTE, key });
  const write = rateLimit({ name: 'level-rewards-claim', limit: () => 10, windowMs: MINUTE, key });
  // 구 클라이언트의 계정 최고 레벨 경로는 지급하지 않는다. 캐릭터를 명시해야 한다.
  const characterRequired = () => { throw new AppError(400, '보상을 조회하거나 받을 캐릭터를 선택해 주세요.', 'CHARACTER_REQUIRED'); };
  r.get('/level-rewards', read, characterRequired);
  r.post('/level-rewards/claim', write, characterRequired);
  r.get('/characters/:uuid/level-rewards', read, validate({ params: charParams }), controller.list);
  r.post('/characters/:uuid/level-rewards/claim', write, validate({ params: charParams, body: claimBody }), controller.claim);
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
