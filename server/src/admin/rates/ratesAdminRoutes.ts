import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './ratesAdminController';
import { idParams, listQuery } from './ratesAdminValidation';

/** 확률표 공개 기록(이용약관 제14조의2 3항). 읽기 전용 */
export function createRatesAdminRouter(): Router {
  const r = Router();
  r.get('/admin/rates/publications', audit({ action: 'rates.publications' }), requireAdmin('viewer'), adminRate('read'), validate({ query: listQuery }), c.list);
  r.get('/admin/rates/publications/:id', audit({ action: 'rates.publication' }), requireAdmin('viewer'), adminRate('read'), validate({ params: idParams }), c.show);
  return r;
}
