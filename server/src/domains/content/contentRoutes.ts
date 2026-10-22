// 챕터 3 이야기 묶음(스포일러 보호). 로그인한 계정만, 데이터 버전이 같은 클라이언트만 받는다
import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './contentController';

const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

export function createContentRouter(): Router {
  const r = Router();
  const limit = rateLimit({ name: 'content', limit: () => 2, windowMs: 1000, key: accountKey });
  r.get('/content/chapter3', versionCheck({ data: true }), requireAuth, limit, controller.chapter3);
  return r;
}
