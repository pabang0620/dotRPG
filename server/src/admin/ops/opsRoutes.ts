import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './opsController';
import { emptyQuery, jobParams, runJobBody } from './opsValidation';

export function createOpsRouter(): Router {
  const r = Router();
  r.get('/admin/ops/status', audit({ action: 'ops.status', targetType: 'server' }), requireAdmin('viewer'), adminRate('read'), validate({ query: emptyQuery }), c.status);
  r.get('/admin/ops/jobs', audit({ action: 'ops.jobs', targetType: 'job' }), requireAdmin('viewer'), adminRate('read'), validate({ query: emptyQuery }), c.jobs);
  r.post('/admin/ops/jobs/:name/run', audit({ action: 'job.run', targetType: 'job' }), requireAdmin('owner'), adminRate('mutate'), validate({ params: jobParams, body: runJobBody }), c.run);
  return r;
}
