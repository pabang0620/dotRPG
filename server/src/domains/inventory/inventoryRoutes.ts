import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './inventoryController';
import { equipBody, storageBody, unequipBody, useBody } from './inventoryValidation';

export function createInventoryRouter(): Router {
  const r = Router();
  const shopLimit = rateLimit({ name: 'inventory', limit: (c) => c.rate.shopPerSec, windowMs: 1000, key: charKey });
  const useLimit = rateLimit({ name: 'item-use', limit: (c) => c.rate.usePerSec, windowMs: 1000, key: charKey });
  r.post('/characters/:uuid/items/use', useLimit, validate({ params: charParams, body: useBody }), controller.use);
  r.post(
    '/characters/:uuid/storage/move',
    shopLimit,
    validate({ params: charParams, body: storageBody }),
    controller.moveStorage,
  );
  r.post(
    '/characters/:uuid/equipment/equip',
    shopLimit,
    validate({ params: charParams, body: equipBody }),
    controller.equip,
  );
  r.post(
    '/characters/:uuid/equipment/unequip',
    shopLimit,
    validate({ params: charParams, body: unequipBody }),
    controller.unequip,
  );
  return r;
}
