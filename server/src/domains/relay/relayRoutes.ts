import { Router } from 'express';
import type { AppConfig } from '../../config/env';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './relayController';
import { fallbackBody, roomParams, ticketBody, transportBody } from './relayValidation';

export function createRelayRouter(): Router {
  const r = Router();
  const per = (name: string, windowMs: number, pick: (c: AppConfig) => number) =>
    rateLimit({ name: `relay-${name}`, limit: pick, windowMs, key: charKey });
  const base = '/characters/:uuid/rooms/:kind/:id';

  // 속도 제한: 캐릭터당 10초에 3회, 분당 10회
  r.post(
    `${base}/relay-ticket`,
    per('ticket-10s', 10_000, (c) => c.rate.p8.relayTicket10s),
    per('ticket-1m', MINUTE, (c) => c.rate.p8.relayTicketMin),
    validate({ params: roomParams, body: ticketBody }),
    controller.ticket,
  );
  // 전송 전환(T2): 10초에 1회. 클라이언트 형태(/transport)와 설계 문서 형태(/transport/fallback) 둘 다 받는다
  r.post(`${base}/transport`, per('switch', 10_000, (c) => c.rate.p8.transportSwitch10s), validate({ params: roomParams, body: transportBody }), controller.transport);
  r.post(`${base}/transport/fallback`, per('switch-fb', 10_000, (c) => c.rate.p8.transportSwitch10s), validate({ params: roomParams, body: fallbackBody }), controller.fallback);
  return r;
}
