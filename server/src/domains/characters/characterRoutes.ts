import { Router } from 'express';
import { requireAuth, getAccount } from '../../middleware/authMiddleware';
import { rateLimit, MINUTE } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './characterController';
import { characterParams, createCharacterBody, stateBody } from './characterValidation';

export function createCharacterRouter(): Router {
  const r = Router();
  // 순서: 버전(426) -> 인증 -> 속도 제한 -> 입력 검증 -> 핸들러
  r.use('/characters', versionCheck({ data: true }), requireAuth);

  r.get('/characters', controller.list);
  r.post(
    '/characters',
    rateLimit({
      name: 'char-create',
      limit: (c) => c.rate.charCreate,
      windowMs: MINUTE,
      key: (_req, locals) => String(getAccount(locals).id),
    }),
    validate({ body: createCharacterBody }),
    controller.create,
  );
  r.get('/characters/:uuid', validate({ params: characterParams }), controller.get);
  r.delete('/characters/:uuid', validate({ params: characterParams }), controller.remove);
  r.put(
    '/characters/:uuid/state',
    rateLimit({
      name: 'state-save',
      limit: (c) => c.rate.stateSave,
      windowMs: 1000,
      key: (req) => String(req.params.uuid),
    }),
    validate({ params: characterParams, body: stateBody }),
    controller.saveState,
  );
  return r;
}
