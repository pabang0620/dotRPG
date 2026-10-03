import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './reportsController';
import { listQuery, resolveBody, takeBody, uuidParams } from './reportsValidation';

export function createReportsAdminRouter(): Router {
  const r = Router();
  r.get('/admin/reports', audit({ action: 'report.list', targetType: 'report' }), requireAdmin('viewer'), adminRate('read'), validate({ query: listQuery }), c.list);
  r.get('/admin/reports/:uuid', audit({ action: 'report.view', targetType: 'report' }), requireAdmin('operator'), adminRate('read'), validate({ params: uuidParams }), c.detail);
  r.post('/admin/reports/:uuid/take', audit({ action: 'report.take', targetType: 'report' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: takeBody }), c.take);
  r.post('/admin/reports/:uuid/resolve', audit({ action: 'report.resolve', targetType: 'report' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: resolveBody }), c.resolve);
  return r;
}
