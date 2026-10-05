import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './starshopController';
import { claimBody, collectionBody, dismantleBody, exchangeBody, pullBody, synthBody } from './starshopValidation';

/** 캐시샵(별조각): 지갑·확률표 조회, 뽑기, 확정 교환, 합성·분해·컬렉션. 인증·버전 검사는 /characters 공통 미들웨어가 한다 */
export function createStarshopRouter(): Router {
  const r = Router();
  const limit = rateLimit({ name: 'starshop', limit: () => 3, windowMs: 1000, key: charKey });
  r.get('/characters/:uuid/starshop', limit, validate({ params: charParams }), controller.summary);
  r.post('/characters/:uuid/starshop/pull', limit, validate({ params: charParams, body: pullBody }), controller.pull);
  r.post('/characters/:uuid/starshop/claim', limit, validate({ params: charParams, body: claimBody }), controller.claim);
  r.post('/characters/:uuid/starshop/exchange', limit, validate({ params: charParams, body: exchangeBody }), controller.exchange);
  r.post('/characters/:uuid/starshop/synth', limit, validate({ params: charParams, body: synthBody }), controller.synth);
  r.post('/characters/:uuid/starshop/dismantle', limit, validate({ params: charParams, body: dismantleBody }), controller.dismantle);
  r.post('/characters/:uuid/starshop/collection', limit, validate({ params: charParams, body: collectionBody }), controller.collection);
  return r;
}
