import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './mailCampaignsController';
import { approveBody, cancelBody, createBody, deliveriesQuery, listQuery, uuidParams } from './mailCampaignsValidation';

export function createMailCampaignsRouter(): Router {
  const r = Router();
  const T = 'campaign' as const;
  r.post('/admin/mail-campaigns', audit({ action: 'campaign.create', targetType: T }), requireAdmin('operator'), adminRate('mutate'), validate({ body: createBody }), c.create);
  r.get('/admin/mail-campaigns', audit({ action: 'campaign.list', targetType: T }), requireAdmin('viewer'), adminRate('read'), validate({ query: listQuery }), c.list);
  r.get('/admin/mail-campaigns/:uuid', audit({ action: 'campaign.view', targetType: T }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams }), c.show);
  r.post('/admin/mail-campaigns/:uuid/approve', audit({ action: 'campaign.approve', targetType: T }), requireAdmin('owner'), adminRate('mutate'), validate({ params: uuidParams, body: approveBody }), c.approve);
  // 대기 중 취소는 작성자도 가능해 route는 operator 이상, 진행·종료 취소의 owner 검사는 service가 한다
  r.post('/admin/mail-campaigns/:uuid/cancel', audit({ action: 'campaign.cancel', targetType: T }), requireAdmin('operator'), adminRate('mutate'), validate({ params: uuidParams, body: cancelBody }), c.cancel);
  r.get('/admin/mail-campaigns/:uuid/deliveries', audit({ action: 'campaign.deliveries', targetType: T }), requireAdmin('viewer'), adminRate('read'), validate({ params: uuidParams, query: deliveriesQuery }), c.deliveries);
  return r;
}
