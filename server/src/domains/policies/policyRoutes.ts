import { Router } from 'express';
import { requireAuth } from '../../middleware/authMiddleware';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './policyController';
import { consentBody, pageParams } from './policyValidation';

const clientOnly = versionCheck({ data: false });

/** 정책 문서(공개, 브라우저로도 연다)와 계정의 동의 기록 */
export function createPolicyRouter(): Router {
  const r = Router();
  r.get('/policies', controller.index);
  r.get('/policies/:lang/:doc', validate({ params: pageParams }), controller.page);
  r.get('/me/consents', clientOnly, requireAuth, controller.status);
  r.post('/me/consents', clientOnly, requireAuth, validate({ body: consentBody }), controller.agree);
  return r;
}
