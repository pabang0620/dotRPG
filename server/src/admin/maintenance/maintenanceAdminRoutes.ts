import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './maintenanceAdminController';
import { broadcastBody, cancelBody, emptyQuery, extendBody, scheduleBody, uuidParams } from './maintenanceValidation';

export function createMaintenanceAdminRouter(): Router {
  const r = Router();
  const m = { targetType: 'maintenance' as const };
  // drain은 :uuid 경로가 아니다(GET /admin/maintenance/drain)
  r.get('/admin/maintenance/drain', audit({ action: 'maintenance.drain', ...m }), requireAdmin('viewer'), adminRate('read'), validate({ query: emptyQuery }), c.drain);
  r.get('/admin/maintenance', audit({ action: 'maintenance.status', ...m }), requireAdmin('viewer'), adminRate('read'), validate({ query: emptyQuery }), c.status);
  r.post('/admin/maintenance', audit({ action: 'maintenance.schedule', ...m }), requireAdmin('operator'), adminRate('mutate'), validate({ body: scheduleBody }), c.schedule);
  r.post('/admin/maintenance/:uuid/cancel', audit({ action: 'maintenance.cancel', ...m }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: cancelBody }), c.cancel);
  r.post('/admin/maintenance/:uuid/extend', audit({ action: 'maintenance.extend', ...m }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: extendBody }), c.extend);
  r.post('/admin/maintenance/:uuid/end', audit({ action: 'maintenance.end', ...m }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: cancelBody }), c.end);
  r.post('/admin/broadcast', audit({ action: 'server.broadcast', targetType: 'server' }), requireAdmin('operator'), adminRate('mutate'), validate({ body: broadcastBody }), c.broadcast);
  return r;
}
