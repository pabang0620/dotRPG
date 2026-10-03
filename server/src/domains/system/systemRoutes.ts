import { Router } from 'express';
import * as controller from './systemController';

// 버전 검사 없음: 오래된 클라이언트도 /meta로 업데이트 필요를 알 수 있어야 한다
export function createSystemRouter(): Router {
  const r = Router();
  r.get('/health', controller.health);
  r.get('/meta', controller.meta);
  return r;
}
