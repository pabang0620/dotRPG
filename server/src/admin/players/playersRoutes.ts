import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './playersController';
import { devCreateBody, findQuery, ledgerQuery, noteBody, requestOnlyBody, uuidParams } from './playersValidation';

export function createPlayersRouter(): Router {
  const r = Router();
  r.get('/admin/accounts', audit({ action: 'account.find', targetType: 'account' }), requireAdmin('viewer'), adminRate('read'), validate({ query: findQuery }), c.find);
  // 개발용 계정 발급은 :uuid 경로보다 앞에 둔다
  r.post('/admin/accounts/dev', audit({ action: 'account.dev_create', targetType: 'account' }), requireAdmin('owner'), adminRate('mutate'), validate({ body: devCreateBody }), c.devCreate);
  r.get('/admin/accounts/:uuid', audit({ action: 'account.view', targetType: 'account' }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.account);
  r.get('/admin/characters/:uuid', audit({ action: 'character.view', targetType: 'character' }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.character);
  r.get('/admin/characters/:uuid/ledger', audit({ action: 'ledger.view', targetType: 'character' }), requireAdmin('viewer'), adminRate('ledger'), validate({ params: uuidParams, query: ledgerQuery }), c.ledger);
  r.post('/admin/accounts/:uuid/notes', audit({ action: 'account.note', targetType: 'account' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: noteBody }), c.note);
  r.post('/admin/accounts/:uuid/kick', audit({ action: 'account.kick', targetType: 'account' }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: requestOnlyBody }), c.kick);
  r.post('/admin/accounts/:uuid/dev-password', audit({ action: 'account.dev_reset', targetType: 'account' }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: requestOnlyBody }), c.devReset);
  return r;
}
