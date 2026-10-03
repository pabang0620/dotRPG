import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './heldRunsController';
import { emptyQuery, reviewBody, uuidParams } from './heldRunsValidation';

export function createHeldRunsRouter(): Router {
  const r = Router();
  r.get('/admin/held-runs', audit({ action: 'held_run.list', targetType: 'dungeon_run' }), requireAdmin('viewer'), adminRate('read'), validate({ query: emptyQuery }), c.list);
  r.get('/admin/held-runs/:uuid', audit({ action: 'held_run.view', targetType: 'dungeon_run' }), requireAdmin('operator'), adminRate('read'), validate({ params: uuidParams }), c.detail);
  r.post('/admin/held-runs/:uuid/release', audit({ action: 'held_run.release', targetType: 'dungeon_run' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: reviewBody }), c.release);
  r.post('/admin/held-runs/:uuid/reject', audit({ action: 'held_run.reject', targetType: 'dungeon_run' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: reviewBody }), c.reject);
  return r;
}
