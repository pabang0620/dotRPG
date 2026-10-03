import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './adminAuthController';
import { emptyBody, loginBody, passwordBody, totpConfirmBody } from './adminAuthValidation';

export function createAdminAuthRouter(): Router {
  const r = Router();
  r.post('/admin/auth/login', audit({ action: 'auth.login', targetType: 'admin' }), validate({ body: loginBody }), c.login);
  r.post('/admin/auth/logout', audit({ action: 'auth.logout', targetType: 'admin' }), requireAdmin('viewer', { allowSetup: true }), validate({ body: emptyBody }), c.logout);
  r.get('/admin/me', audit({ action: 'auth.me', targetType: 'admin' }), requireAdmin('viewer', { allowSetup: true }), adminRate('read'), c.me);
  r.post('/admin/auth/password', audit({ action: 'auth.password', targetType: 'admin' }), requireAdmin('viewer', { allowSetup: true }), adminRate('mutate'), validate({ body: passwordBody }), c.password);
  r.post('/admin/auth/totp/start', audit({ action: 'auth.totp_start', targetType: 'admin' }), requireAdmin('viewer', { allowSetup: true }), adminRate('mutate'), validate({ body: emptyBody }), c.totpStart);
  r.post('/admin/auth/totp/confirm', audit({ action: 'auth.totp_confirm', targetType: 'admin' }), requireAdmin('viewer', { allowSetup: true }), adminRate('mutate'), validate({ body: totpConfirmBody }), c.totpConfirm);
  return r;
}
