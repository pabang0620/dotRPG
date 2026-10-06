import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './paymentsAdminController';
import { blockBody, flagListQuery, grantActionBody, grantCreateBody, grantListQuery, orderListQuery, recheckBody, resolveBody, revokeBody, unblockBody, uuidParams } from './paymentsAdminValidation';

/** PA1~PA14. 응답 형식·인증(TOTP 세션)·감사·속도 제한은 다른 관리자 라우터와 같다 */
export function createPaymentsAdminRouter(): Router {
  const r = Router();
  const O = 'payment_order' as const;
  const A = 'payment_account' as const;
  const G = 'star_grant' as const;
  const F = 'payment_flag' as const;
  r.get('/admin/payments/orders', audit({ action: 'payment.order.list', targetType: O }), requireAdmin('viewer'), adminRate('read'), validate({ query: orderListQuery }), c.listOrders);
  r.get('/admin/payments/orders/:uuid', audit({ action: 'payment.order.view', targetType: O }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.orderDetail);
  r.post('/admin/payments/orders/:uuid/recheck', audit({ action: 'payment.order.recheck', targetType: O }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: recheckBody }), c.recheck);
  r.post('/admin/payments/orders/:uuid/revoke-outcomes', audit({ action: 'payment.revoke_outcomes', targetType: O }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: revokeBody }), c.revokeOutcomes);
  r.get('/admin/payments/accounts/:uuid', audit({ action: 'payment.account.view', targetType: A }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.accountPayments);
  r.post('/admin/payments/accounts/:uuid/block', audit({ action: 'payment.account.block', targetType: A }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: blockBody }), c.block);
  r.post('/admin/payments/accounts/:uuid/unblock', audit({ action: 'payment.account.unblock', targetType: A }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: unblockBody }), c.unblock);
  r.get('/admin/payments/flags', audit({ action: 'payment.flag.list', targetType: F }), requireAdmin('viewer'), adminRate('read'), validate({ query: flagListQuery }), c.listFlags);
  r.post('/admin/payments/flags/:uuid/resolve', audit({ action: 'payment.flag.resolve', targetType: F }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: resolveBody }), c.resolveFlag);
  r.post('/admin/payments/star-grants', audit({ action: 'payment.grant.create', targetType: G }), requireAdmin('operator'), adminRate('mutate'), validate({ body: grantCreateBody }), c.createGrant);
  r.get('/admin/payments/star-grants', audit({ action: 'payment.grant.list', targetType: G }), requireAdmin('viewer'), adminRate('read'), validate({ query: grantListQuery }), c.listGrants);
  r.post('/admin/payments/star-grants/:uuid/approve', audit({ action: 'payment.grant.approve', targetType: G }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: grantActionBody }), c.approveGrant);
  // 작성자(operator 이상) 또는 owner: 작성자 검사는 service가 한다
  r.post('/admin/payments/star-grants/:uuid/cancel', audit({ action: 'payment.grant.cancel', targetType: G }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: grantActionBody }), c.cancelGrant);
  r.get('/admin/payments/reconcile', audit({ action: 'payment.reconcile.view' }), requireAdmin('viewer'), adminRate('read'), c.reconcile);
  return r;
}
