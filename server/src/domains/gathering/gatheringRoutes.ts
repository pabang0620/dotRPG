import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './gatheringController';
import { chestBody, deliveryBody, gatherBody, nodesParams } from './gatheringValidation';

export function createGatheringRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/gathers',
    rateLimit({ name: 'gather', limit: (c) => c.rate.gatherPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: gatherBody }),
    controller.gather,
  );
  r.get(
    '/characters/:uuid/maps/:map_id/nodes',
    rateLimit({ name: 'nodes', limit: (c) => c.rate.nodesPerSec, windowMs: 1000, key: charKey }),
    validate({ params: nodesParams }),
    controller.nodes,
  );
  r.post(
    '/characters/:uuid/chests/open',
    rateLimit({ name: 'chest', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: chestBody }),
    controller.openChest,
  );
  r.post(
    '/characters/:uuid/deliveries',
    rateLimit({ name: 'delivery', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: deliveryBody }),
    controller.deliver,
  );
  return r;
}
