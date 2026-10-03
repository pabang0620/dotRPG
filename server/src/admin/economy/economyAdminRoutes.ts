import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './economyAdminController';
import { dailyQuery, grantBody, grantListQuery } from './economyAdminValidation';

export function createEconomyAdminRouter(): Router {
  const r = Router();
  r.get('/admin/economy/daily', audit({ action: 'economy.daily' }), requireAdmin('viewer'), adminRate('read'), validate({ query: dailyQuery }), c.daily);
  r.post('/admin/grants', audit({ action: 'grant.create', targetType: 'character' }), requireAdmin('owner'), adminRate('mutate'), validate({ body: grantBody }), c.grant);
  r.get('/admin/grants', audit({ action: 'grant.list', targetType: 'character' }), requireAdmin('viewer'), adminRate('read'), validate({ query: grantListQuery }), c.grants);
  return r;
}
