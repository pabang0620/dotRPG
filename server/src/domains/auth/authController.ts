import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import type { CredentialsBody, OauthPollBody, OauthStartBody, RefreshBody, SteamBody } from './authValidation';
import * as oauth from './oauthService';
import { enabledProviders } from './oauthProvider';
import * as service from './authService';
import type { AccessMeta } from './authService';

/** 접속 신호(기기·클라이언트 버전). IP는 service가 req.ip로 따로 받는다 */
function meta(req: Request, device: AccessMeta['device']): AccessMeta {
  return { device, clientVersion: req.header('x-client-version') };
}

export async function register(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CredentialsBody>(res);
    const data = await service.register(body.login_id, body.password, req.ip ?? 'unknown', meta(req, body.device));
    successResponse(res, data, '', 201);
  } catch (err) {
    next(err);
  }
}

export async function login(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CredentialsBody>(res);
    successResponse(res, await service.login(body.login_id, body.password, req.ip ?? 'unknown', meta(req, body.device)));
  } catch (err) {
    next(err);
  }
}

export async function refresh(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<RefreshBody>(res);
    successResponse(res, await service.refresh(body.refresh_token, meta(req, body.device), req.ip ?? ''));
  } catch (err) {
    next(err);
  }
}

export async function logout(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<RefreshBody>(res);
    successResponse(res, await service.logout(body.refresh_token));
  } catch (err) {
    next(err);
  }
}

export async function me(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.getMe(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function steamLogin(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<SteamBody>(res);
    const data = await service.steamLogin(body.ticket, meta(req, body.device), req.ip ?? '');
    successResponse(res, data, '', data.created ? 201 : 200);
  } catch (err) {
    next(err);
  }
}

export async function steamLink(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<SteamBody>(res);
    successResponse(res, await service.steamLink(getAccount(res.locals).id, body.ticket));
  } catch (err) {
    next(err);
  }
}

// ---------- 카카오·Google (0045) ----------

export function oauthProviders(_req: Request, res: Response): void {
  successResponse(res, enabledProviders());
}

export async function oauthStart(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<OauthStartBody>(res);
    const accountId = body.mode === 'link' ? getAccount(res.locals).id : null;
    successResponse(res, await oauth.start(body.provider, body.mode, accountId));
  } catch (err) {
    next(err);
  }
}

const esc = (s: string) => s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
function resultPage(ok: boolean): string {
  const title = ok ? '로그인 완료' : '로그인 실패';
  const body = ok ? '게임으로 돌아가세요. 이 창은 닫아도 됩니다.' : '게임으로 돌아가 다시 시도해 주세요.';
  const en = ok ? 'Signed in. Return to the game; you can close this window.' : 'Sign-in failed. Return to the game and try again.';
  return `<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>dotRPG</title>
<style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#14121c;color:#f2ead8;font-family:system-ui,sans-serif;text-align:center}
h1{font-size:22px;margin:0 0 10px}p{margin:4px 0;color:#c9bfa8}</style></head>
<body><main><h1>${esc(title)}</h1><p>${esc(body)}</p><p>${esc(en)}</p></main></body></html>`;
}

/** 공급자가 브라우저를 돌려보내는 곳. 결과는 세션에만 적고 화면에는 토큰·번호를 싣지 않는다 */
export async function oauthCallback(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { provider: 'kakao' | 'google' }>(res);
    const str = (v: unknown) => (typeof v === 'string' && v.length <= 2048 ? v : undefined);
    const r = await oauth.callback(params.provider, { code: str(req.query.code), state: str(req.query.state), error: str(req.query.error) });
    res.status(r.ok ? 200 : 400).set('Cache-Control', 'no-store').set('Referrer-Policy', 'no-referrer').type('html').send(resultPage(r.ok));
  } catch (err) {
    next(err);
  }
}

export async function oauthPoll(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<OauthPollBody>(res);
    successResponse(res, await oauth.poll(body.state, body.poll_secret, meta(req, body.device), req.ip ?? ''));
  } catch (err) {
    next(err);
  }
}
