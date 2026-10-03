import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './sanctionsController';
import { createSanctionBody, revokeBody, uuidParams } from './sanctionsValidation';

export function createSanctionsRouter(): Router {
  const r = Router();
  r.post('/admin/accounts/:uuid/sanctions', audit({ action: 'sanction.create', targetType: 'account' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: createSanctionBody }), c.create);
  r.post('/admin/sanctions/:uuid/revoke', audit({ action: 'sanction.revoke', targetType: 'sanction' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: revokeBody }), c.revoke);
  return r;
}
