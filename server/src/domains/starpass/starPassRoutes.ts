import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './starPassController';
import { dailyBody } from './starPassValidation';

/** 별빛 정기권: 상태는 계정 단위, 일일 지급은 캐릭터 경로(상자 우편이 그 캐릭터에게 간다) */
export function createStarPassRouter(): Router {
  const r = Router();
  const key = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  r.get('/star-pass', versionCheck({ data: false }), requireAuth, rateLimit({ name: 'star-pass-read', limit: () => 30, windowMs: MINUTE, key }), controller.status);
  r.post(
    '/characters/:uuid/star-pass/daily',
    rateLimit({ name: 'star-pass-daily', limit: () => 10, windowMs: MINUTE, key: charKey }),
    validate({ params: charParams, body: dailyBody }),
    controller.daily,
  );
  return r;
}
