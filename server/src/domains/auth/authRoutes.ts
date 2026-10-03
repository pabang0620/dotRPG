import { Router } from 'express';
import { getConfig } from '../../config/env';
import { requireAuth } from '../../middleware/authMiddleware';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './authController';
import { credentialsBody, refreshBody } from './authValidation';

const clientOnly = versionCheck({ data: false });

/** /auth/* 와 /me. 개발용 로그인은 꺼진 환경에서 라우트 자체를 등록하지 않는다(404). */
export function createAuthRouter(): Router {
  const r = Router();
  if (getConfig().authDevEnabled) {
    r.post('/auth/dev/register', clientOnly, validate({ body: credentialsBody }), controller.register);
    r.post('/auth/dev/login', clientOnly, validate({ body: credentialsBody }), controller.login);
  }
  r.post('/auth/refresh', clientOnly, validate({ body: refreshBody }), controller.refresh);
  // 로그아웃은 버전과 무관하게 항상 가능해야 한다
  r.post('/auth/logout', validate({ body: refreshBody }), controller.logout);
  r.get('/me', clientOnly, requireAuth, controller.me);
  return r;
}
