import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './economyHoldsController';
import { clawbackBody, holdListQuery, linksQuery, manualHoldBody, releaseBody, tradeFlagsQuery, uuidParams } from './economyHoldsValidation';

/** H1~H9. 응답 형식·인증(TOTP 세션)·감사·속도 제한은 다른 관리자 라우터와 같다 */
export function createEconomyHoldsRouter(): Router {
  const r = Router();
  r.get('/admin/economy/holds', audit({ action: 'economy.hold.list', targetType: 'hold' }), requireAdmin('viewer'), adminRate('read'), validate({ query: holdListQuery }), c.list);
  r.post('/admin/economy/holds', audit({ action: 'economy.hold.create', targetType: 'hold' }), requireAdmin('operator'), adminRate('mutate'), validate({ body: manualHoldBody }), c.create);
  r.get('/admin/economy/income-caps', audit({ action: 'economy.caps.view' }), requireAdmin('viewer'), adminRate('read'), c.incomeCaps);
  r.get('/admin/economy/holds/:uuid', audit({ action: 'economy.hold.view', targetType: 'hold' }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.detail);
  r.post('/admin/economy/holds/:uuid/release', audit({ action: 'economy.hold.release', targetType: 'hold' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: releaseBody }), c.release);
  r.post('/admin/economy/holds/:uuid/clawback', audit({ action: 'economy.hold.clawback', targetType: 'hold' }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: clawbackBody }), c.clawback);
  r.get('/admin/accounts/:uuid/links', audit({ action: 'account.links', targetType: 'account' }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams, query: linksQuery }), c.links);
  r.get('/admin/auction/trade-flags', audit({ action: 'auction.trade_flags' }), requireAdmin('viewer'), adminRate('read'), validate({ query: tradeFlagsQuery }), c.tradeFlags);
  r.get('/admin/characters/:uuid/velocity', audit({ action: 'character.velocity', targetType: 'character' }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.velocity);
  return r;
}
