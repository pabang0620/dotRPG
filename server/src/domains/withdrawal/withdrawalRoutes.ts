import { Router } from 'express';
import { requireAuth } from '../../middleware/authMiddleware';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './withdrawalController';
import { cancelBody, withdrawBody } from './withdrawalValidation';

const clientOnly = versionCheck({ data: false });

/** W1~W3. 속도 제한과 기능 스위치(503 FEATURE_DISABLED)는 서비스가 처리한다 */
export function createWithdrawalRouter(): Router {
  const r = Router();
  r.get('/me/withdrawal', clientOnly, requireAuth, controller.info);
  r.post('/me/withdrawal', clientOnly, requireAuth, validate({ body: withdrawBody }), controller.request);
  // 로그인이 막힌 상태에서 부르므로 토큰이 없다(/auth/* 규칙). 자격이 곧 소유 증명이다
  r.post('/auth/withdrawal/cancel', clientOnly, validate({ body: cancelBody }), controller.cancel);
  return r;
}
