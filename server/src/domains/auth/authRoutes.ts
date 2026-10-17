import { Router } from 'express';
import { getConfig } from '../../config/env';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { ipKey, MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './authController';
import { credentialsBody, oauthPollBody, oauthProviderParams, oauthStartBody, refreshBody, steamBody } from './authValidation';

const clientOnly = versionCheck({ data: false });

/** /auth/* 와 /me. 개발용 로그인은 꺼진 환경에서 라우트 자체를 등록하지 않는다(404). */
export function createAuthRouter(): Router {
  const r = Router();
  const cfg = getConfig();
  // 가입은 로그인과 별도 스위치다(G4): 운영 기본 꺼짐, 개발용 계정은 관리자 도구(PL7)가 발급한다
  if (cfg.authDevRegisterEnabled) {
    r.post('/auth/dev/register', clientOnly, validate({ body: credentialsBody }), controller.register);
  }
  if (cfg.authDevEnabled) {
    r.post('/auth/dev/login', clientOnly, validate({ body: credentialsBody }), controller.login);
  }
  // Steam 인증: off에서는 라우트를 등록하지 않는다(404). 데이터 버전은 검사하지 않는다(/auth/* 규칙)
  if (getConfig().steam.mode !== 'off') {
    r.post(
      '/auth/steam',
      clientOnly,
      rateLimit({ name: 'steam-ip', limit: (c) => c.rate.steamIp, windowMs: MINUTE, key: ipKey }),
      validate({ body: steamBody }),
      controller.steamLogin,
    );
    r.post(
      '/auth/steam/link',
      clientOnly,
      requireAuth,
      rateLimit({ name: 'steam-link', limit: (c) => c.rate.steamLink, windowMs: MINUTE, key: (_req, l) => String(getAccount(l).id) }),
      validate({ body: steamBody }),
      controller.steamLink,
    );
  }
  // 카카오·Google(0045): 꺼진 공급자는 providers 에 false, start 는 404. 콜백은 브라우저가 오므로 버전 검사를 하지 않는다
  r.get('/auth/oauth/providers', clientOnly, controller.oauthProviders);
  r.post(
    '/auth/oauth/start',
    clientOnly,
    rateLimit({ name: 'oauth-start-ip', limit: (c) => c.rate.oauthStartIp, windowMs: MINUTE, key: ipKey }),
    (req, res, next) => ((req.body as { mode?: unknown } | undefined)?.mode === 'link' ? requireAuth(req, res, next) : next()),
    validate({ body: oauthStartBody }),
    controller.oauthStart,
  );
  r.get('/auth/oauth/callback/:provider', validate({ params: oauthProviderParams }), controller.oauthCallback);
  r.post(
    '/auth/oauth/poll',
    clientOnly,
    rateLimit({ name: 'oauth-poll-ip', limit: (c) => c.rate.oauthPollIp, windowMs: MINUTE, key: ipKey }),
    validate({ body: oauthPollBody }),
    controller.oauthPoll,
  );
  r.post('/auth/refresh', clientOnly, validate({ body: refreshBody }), controller.refresh);
  // 로그아웃은 버전과 무관하게 항상 가능해야 한다
  r.post('/auth/logout', validate({ body: refreshBody }), controller.logout);
  r.get('/me', clientOnly, requireAuth, controller.me);
  return r;
}
