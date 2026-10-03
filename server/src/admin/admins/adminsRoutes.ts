import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './adminsController';
import { adminActionBody, auditQuery, createAdminBody, uuidParams } from './adminsValidation';

export function createAdminsRouter(): Router {
  const r = Router();
  r.get('/admin/admins', audit({ action: 'admin.list', targetType: 'admin' }), requireAdmin('owner'), adminRate('read'), c.list);
  r.post('/admin/admins', audit({ action: 'admin.create', targetType: 'admin' }), requireAdmin('owner'), adminRate('mutate'), validate({ body: createAdminBody }), c.create);
  r.post('/admin/admins/:uuid/actions', audit({ action: 'admin.action', targetType: 'admin' }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: adminActionBody }), c.action);
  r.get('/admin/audit', audit({ action: 'audit.query', targetType: 'admin' }), requireAdmin('owner'), adminRate('read'), validate({ query: auditQuery }), c.audit);
  return r;
}
