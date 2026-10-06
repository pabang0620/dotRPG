import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { ipKey, MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './paymentsController';
import { createOrderBody, emptyBody, listQuery, orderParams } from './paymentsValidation';

const acctKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

/**
 * 플레이어 결제 API B1~B6(Docs/server/phase11_payments.md 13.1). 계정 단위(캐릭터·데이터 버전과 무관): 클라이언트 버전만 검사한다.
 * 지급 API는 없다: 별조각은 서버가 Steam에 직접 확인한 결과로만 들어온다.
 */
export function createPaymentsRouter(): Router {
  const r = Router();
  r.use('/payments', versionCheck({ data: false }), requireAuth);
  const read = rateLimit({ name: 'pay-read', limit: (c) => c.pay.rate.readPerMin, windowMs: MINUTE, key: acctKey });
  const order = rateLimit({ name: 'pay-order', limit: (c) => c.pay.rate.orderPerMin, windowMs: MINUTE, key: acctKey });
  const orderIp = rateLimit({ name: 'pay-order-ip', limit: (c) => c.pay.rate.orderIpPerMin, windowMs: MINUTE, key: ipKey });
  const sync = rateLimit({ name: 'pay-sync', limit: (c) => c.pay.rate.syncPerMin, windowMs: MINUTE, key: acctKey });
  r.get('/payments/products', read, controller.products);
  r.post('/payments/orders', order, orderIp, validate({ body: createOrderBody }), controller.createOrder);
  r.get('/payments/orders', read, validate({ query: listQuery }), controller.listOrders);
  r.post('/payments/reconcile', sync, validate({ body: emptyBody }), controller.reconcile);
  r.get('/payments/orders/:uuid', read, validate({ params: orderParams }), controller.getOrder);
  r.post('/payments/orders/:uuid/sync', sync, validate({ params: orderParams, body: emptyBody }), controller.sync);
  return r;
}
