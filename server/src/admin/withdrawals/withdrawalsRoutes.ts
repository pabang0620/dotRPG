import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './withdrawalsController';
import { anonymizeNowBody, cancelBody, holdBody, listQuery, releaseBody, startBody, tombstoneQuery, uuidParams } from './withdrawalsValidation';

/** WD1~WD8. 응답 형식·인증(TOTP 세션)·감사·속도 제한은 다른 관리자 라우터와 같다 */
export function createWithdrawalsAdminRouter(): Router {
  const r = Router();
  r.get('/admin/withdrawals', audit({ action: 'withdrawal.list' }), requireAdmin('viewer'), adminRate('read'), validate({ query: listQuery }), c.list);
  r.get('/admin/accounts/:uuid/withdrawal', audit({ action: 'withdrawal.view', targetType: 'account' }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.accountDetail);
  r.post('/admin/accounts/:uuid/withdrawal', audit({ action: 'withdrawal.start', targetType: 'account' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: startBody }), c.start);
  // 아래 셋의 :uuid 는 탈퇴 요청의 uuid 다(계정 uuid 가 아니므로 실패 감사에는 대상 종류를 붙이지 않는다)
  r.post('/admin/withdrawals/:uuid/cancel', audit({ action: 'withdrawal.cancel' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: cancelBody }), c.cancel);
  r.post('/admin/withdrawals/:uuid/anonymize-now', audit({ action: 'withdrawal.anonymize_now' }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: anonymizeNowBody }), c.anonymizeNow);
  r.post('/admin/withdrawals/:uuid/hold', audit({ action: 'withdrawal.hold' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: holdBody }), c.hold);
  r.get('/admin/tombstones', audit({ action: 'tombstone.view' }), requireAdmin('viewer'), adminRate('read'), validate({ query: tombstoneQuery }), c.findTombstone);
  r.post('/admin/tombstones/:uuid/release', audit({ action: 'tombstone.release' }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: releaseBody }), c.releaseTombstone);
  return r;
}
