import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './partyController';
import {
  applicationParams, charParams, createPartyBody, listQuery, partyParams, patchPartyBody, pollQuery, readyBody, requestOnlyBody, respondBody,
  targetBody,
} from './partyValidation';

export function createPartyRouter(): Router {
  const r = Router();
  const per = (name: string, windowMs: number, pick: (c: import('../../config/env').AppConfig) => number) =>
    rateLimit({ name: `party-${name}`, limit: pick, windowMs, key: charKey });
  const action = per('action', 1000, (c) => c.rate.partyActionPerSec);
  const base = '/characters/:uuid';

  r.get(`${base}/parties`, per('list', 1000, (c) => c.rate.partyListPerSec), validate({ params: charParams, query: listQuery }), controller.list);
  r.post(`${base}/parties`, per('create', 3000, (c) => c.rate.partyCreatePer3Sec), validate({ params: charParams, body: createPartyBody }), controller.create);
  r.get(`${base}/party`, per('poll', 1000, (c) => c.rate.partyPollPerSec), validate({ params: charParams, query: pollQuery }), controller.mine);
  r.patch(`${base}/party`, action, validate({ params: charParams, body: patchPartyBody }), controller.patch);
  r.post(`${base}/parties/:party_id/apply`, action, validate({ params: partyParams, body: requestOnlyBody }), controller.apply);
  r.delete(`${base}/party/applications/:application_id`, action, validate({ params: applicationParams }), controller.cancelApplication);
  r.post(`${base}/party/applications/:application_id/respond`, action, validate({ params: applicationParams, body: respondBody }), controller.respond);
  r.post(`${base}/party/ready`, action, validate({ params: charParams, body: readyBody }), controller.ready);
  r.post(`${base}/party/leave`, action, validate({ params: charParams, body: requestOnlyBody }), controller.leave);
  r.post(`${base}/party/kick`, action, validate({ params: charParams, body: targetBody }), controller.kick);
  r.post(`${base}/party/leader`, action, validate({ params: charParams, body: targetBody }), controller.leader);
  return r;
}
