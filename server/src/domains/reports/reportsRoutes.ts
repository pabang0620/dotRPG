import { Router } from 'express';
import { requireAuth } from '../../middleware/authMiddleware';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './reportsController';
import { reportBody } from './reportsValidation';

/** 계정별 시간당·일일 한도는 서비스가 reports 표로 센다(REPORT_LIMIT). IP 한도는 app.ts의 SOCIAL 버킷 */
export function createReportsRouter(): Router {
  const r = Router();
  r.use('/reports', versionCheck({ data: false }), requireAuth);
  r.post('/reports', validate({ body: reportBody }), controller.submit);
  return r;
}
