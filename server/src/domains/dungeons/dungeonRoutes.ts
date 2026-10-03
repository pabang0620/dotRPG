import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './dungeonController';
import { enterBody, pickBody, resultBody, runParams } from './dungeonValidation';

export function createDungeonRouter(): Router {
  const r = Router();
  // 경로마다 따로 센다(목록 조회 직후의 입장, 결과 직후의 카드 선택이 서로 막히지 않게)
  const limit = (name: string) =>
    rateLimit({ name: `dungeon-${name}`, limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey });
  r.get('/characters/:uuid/dungeons', limit('list'), validate({ params: charParams }), controller.list);
  r.post('/characters/:uuid/dungeon-runs', limit('enter'), validate({ params: charParams, body: enterBody }), controller.enter);
  r.post(
    '/characters/:uuid/dungeon-runs/:run_id/result',
    limit('result'),
    validate({ params: runParams, body: resultBody }),
    controller.result,
  );
  r.post(
    '/characters/:uuid/dungeon-runs/:run_id/cards/pick',
    limit('pick'),
    validate({ params: runParams, body: pickBody }),
    controller.pick,
  );
  return r;
}
