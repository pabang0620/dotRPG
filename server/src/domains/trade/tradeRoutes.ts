import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as c from './tradeController';
import { confirmBody, createBody, lockBody, offerBody, plainBody, tradeParams } from './tradeValidation';

export function createTradeRouter(): Router {
  const r = Router();
  // 클라이언트가 1초마다 폴링하므로 조회는 넉넉하게(경매 가격 조회와 같은 한도)
  const pollLimit = rateLimit({ name: 'trade-current', limit: (x) => x.rate.auctionPricePerSec, windowMs: 1000, key: charKey });
  const actionLimit = rateLimit({ name: 'trade-action', limit: (x) => x.rate.auctionPricePerSec, windowMs: 1000, key: charKey });

  // 고정 경로(current)를 /:trade_id 보다 먼저 둔다
  r.get('/characters/:uuid/trades/current', pollLimit, validate({ params: charParams }), c.current);
  r.post('/characters/:uuid/trades', actionLimit, validate({ params: charParams, body: createBody }), c.create);
  r.post('/characters/:uuid/trades/:trade_id/accept', actionLimit, validate({ params: tradeParams, body: plainBody }), c.accept);
  r.post('/characters/:uuid/trades/:trade_id/decline', actionLimit, validate({ params: tradeParams, body: plainBody }), c.decline);
  r.post('/characters/:uuid/trades/:trade_id/cancel', actionLimit, validate({ params: tradeParams, body: plainBody }), c.cancel);
  r.put('/characters/:uuid/trades/:trade_id/offer', actionLimit, validate({ params: tradeParams, body: offerBody }), c.offer);
  r.post('/characters/:uuid/trades/:trade_id/lock', actionLimit, validate({ params: tradeParams, body: lockBody }), c.lock);
  r.post('/characters/:uuid/trades/:trade_id/confirm', actionLimit, validate({ params: tradeParams, body: confirmBody }), c.confirm);
  return r;
}
