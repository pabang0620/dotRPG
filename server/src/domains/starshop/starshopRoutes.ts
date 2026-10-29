import { Router } from 'express';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { getAccount } from '../../middleware/authMiddleware';
import { charParams } from '../economy/economyValidation';
import * as controller from './starshopController';
import { crystalConvertBody } from './starshopValidation';
import { claimBody, codexBody, collectionBody, dismantleBody, exchangeBody, exchangeBuyBody, gearCeilingBody, gearDismantleBody, pullBody, starItemBuyBody, synthBody } from './starshopValidation';

/** 캐시샵(별조각): 지갑·확률표 조회, 뽑기, 확정 교환, 합성·분해·컬렉션. 인증·버전 검사는 /characters 공통 미들웨어가 한다 */
export function createStarshopRouter(): Router {
  const r = Router();
  const perChar = rateLimit({ name: 'starshop', limit: () => 3, windowMs: 1000, key: charKey });
  // 같은 계정의 캐릭터를 바꿔 가며 보내도 계정 전체로 초당 4회까지(지갑은 계정 하나다)
  const perAccount = rateLimit({ name: 'starshop_account', limit: () => 4, windowMs: 1000, key: (_req, locals) => String(getAccount(locals).id) });
  const limit = [perChar, perAccount];
  // 11단계 11.3: 소비 경로(뽑기·교환)는 계정당 분당 상한을 더한다(매크로 대량 소비 방지)
  const spendPerMin = rateLimit({ name: 'starshop_spend', limit: (c) => c.pay.rate.spendPerMin, windowMs: MINUTE, key: (_req, locals) => String(getAccount(locals).id) });
  r.get('/characters/:uuid/starshop', limit, validate({ params: charParams }), controller.summary);
  r.post('/characters/:uuid/starshop/pull', limit, spendPerMin, validate({ params: charParams, body: pullBody }), controller.pull);
  r.post('/characters/:uuid/starshop/claim', limit, validate({ params: charParams, body: claimBody }), controller.claim);
  r.post('/characters/:uuid/starshop/gear-ceiling', limit, validate({ params: charParams, body: gearCeilingBody }), controller.gearCeiling);
  r.post('/characters/:uuid/starshop/exchange', limit, spendPerMin, validate({ params: charParams, body: exchangeBody }), controller.exchange);
  r.post('/characters/:uuid/starshop/synth', limit, validate({ params: charParams, body: synthBody }), controller.synth);
  r.post('/characters/:uuid/starshop/dismantle', limit, validate({ params: charParams, body: dismantleBody }), controller.dismantle);
  r.post('/characters/:uuid/starshop/collection', limit, validate({ params: charParams, body: collectionBody }), controller.collection);
  // 0044 별빛 교환소(별의 결정), 뽑기 장비 분해, 장비 도감
  r.get('/characters/:uuid/starshop/exchange-shop', limit, validate({ params: charParams }), controller.exchangeShop);
  r.post('/characters/:uuid/starshop/exchange-shop/buy', limit, validate({ params: charParams, body: exchangeBuyBody }), controller.exchangeBuy);
  r.post('/characters/:uuid/starshop/gear-dismantle', limit, validate({ params: charParams, body: gearDismantleBody }), controller.gearDismantle);
  r.post('/characters/:uuid/starshop/codex', limit, validate({ params: charParams, body: codexBody }), controller.codex);
  // 0056 별조각 상품(부활 코인 등): 소비 경로라 분당 상한을 함께 건다
  r.post('/characters/:uuid/starshop/item-buy', limit, spendPerMin, validate({ params: charParams, body: starItemBuyBody }), controller.itemBuy);
  // 별의 결정 -> 무료 별조각(바꾸는 양·횟수 제한 없음, 분당 소비 상한도 걸지 않는다)
  r.post('/characters/:uuid/starshop/crystal-convert', limit, validate({ params: charParams, body: crystalConvertBody }), controller.crystalConvert);
  return r;
}
