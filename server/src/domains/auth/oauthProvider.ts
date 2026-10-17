// 카카오·Google 로그인(0045, Docs/server/OAUTH_LOGIN.md): 인가 주소 만들기와 인가 코드 -> 공급자 사용자 번호 교환.
// 네트워크 호출은 바꿔 끼울 수 있는 fetch 하나로만 한다(시험은 setOauthFetch 로 가짜를 넣는다). 토큰·코드 원문은 로그에 남기지 않는다.
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';

export type OauthProvider = 'kakao' | 'google';
export const OAUTH_PROVIDERS: readonly OauthProvider[] = ['kakao', 'google'];

type FetchFn = (url: string, init?: RequestInit) => Promise<Response>;
let fetchImpl: FetchFn = (url, init) => fetch(url, init);

/** 시험 전용: null 이면 실제 fetch 로 되돌린다 */
export function setOauthFetch(f: FetchFn | null): void {
  fetchImpl = f ?? ((url, init) => fetch(url, init));
}

function creds(p: OauthProvider): { clientId: string; clientSecret: string; redirectUri: string } {
  const o = getConfig().oauth;
  const c = o[p];
  if (!c || !o.publicBase) throw new AppError(404, '지원하지 않는 로그인 방식입니다.', 'OAUTH_PROVIDER_DISABLED');
  return { ...c, redirectUri: `${o.publicBase}/auth/oauth/callback/${p}` };
}

export function enabledProviders(): Record<OauthProvider, boolean> {
  const o = getConfig().oauth;
  return { kakao: !!(o.publicBase && o.kakao), google: !!(o.publicBase && o.google) };
}

/** 브라우저로 여는 공급자 로그인 주소. 카카오는 회원번호만 쓰므로 추가 동의 항목을 요청하지 않는다 */
export function authorizeUrl(p: OauthProvider, state: string): string {
  const c = creds(p);
  if (p === 'kakao') {
    const q = new URLSearchParams({ client_id: c.clientId, redirect_uri: c.redirectUri, response_type: 'code', state });
    return `https://kauth.kakao.com/oauth/authorize?${q}`;
  }
  const q = new URLSearchParams({ client_id: c.clientId, redirect_uri: c.redirectUri, response_type: 'code', scope: 'openid', state, prompt: 'select_account' });
  return `https://accounts.google.com/o/oauth2/v2/auth?${q}`;
}

const fail = (why: string): never => {
  throw new AppError(401, '로그인 확인에 실패했습니다.', 'OAUTH_EXCHANGE_FAILED', { why });
};

async function json(res: Response): Promise<Record<string, unknown>> {
  try {
    return (await res.json()) as Record<string, unknown>;
  } catch {
    return {};
  }
}

const timeout = () => AbortSignal.timeout(8000);

/** 인가 코드를 공급자의 사용자 번호(subject)로 바꾼다. 실패하면 OAUTH_EXCHANGE_FAILED */
export async function exchangeCode(p: OauthProvider, code: string): Promise<string> {
  const c = creds(p);
  const form = new URLSearchParams({ grant_type: 'authorization_code', client_id: c.clientId, client_secret: c.clientSecret, redirect_uri: c.redirectUri, code });
  const tokenUrl = p === 'kakao' ? 'https://kauth.kakao.com/oauth/token' : 'https://oauth2.googleapis.com/token';
  let tok: Record<string, unknown>;
  try {
    const res = await fetchImpl(tokenUrl, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded;charset=utf-8' }, body: form.toString(), signal: timeout() });
    tok = await json(res);
    if (!res.ok) {
      logger.warn({ provider: p, status: res.status, error: tok.error }, 'oauth token exchange rejected');
      return fail('token');
    }
  } catch (err) {
    if (err instanceof AppError) throw err;
    logger.warn({ provider: p, err: (err as Error).message }, 'oauth token exchange error');
    return fail('network');
  }

  if (p === 'kakao') {
    const access = typeof tok.access_token === 'string' ? tok.access_token : fail('token');
    try {
      const res = await fetchImpl('https://kapi.kakao.com/v2/user/me', { headers: { Authorization: `Bearer ${access}` }, signal: timeout() });
      const me = await json(res);
      const id = me.id;
      if (!res.ok || (typeof id !== 'number' && typeof id !== 'string') || !/^\d{1,20}$/.test(String(id))) return fail('profile');
      return String(id);
    } catch (err) {
      if (err instanceof AppError) throw err;
      return fail('network');
    }
  }

  // Google: id_token 을 tokeninfo 로 확인한다(발급 대상이 우리 클라이언트인지, 발급자, 만료)
  const idToken = typeof tok.id_token === 'string' ? tok.id_token : fail('token');
  try {
    const res = await fetchImpl(`https://oauth2.googleapis.com/tokeninfo?id_token=${encodeURIComponent(idToken)}`, { signal: timeout() });
    const info = await json(res);
    const iss = String(info.iss ?? '');
    const exp = Number(info.exp ?? 0);
    if (!res.ok || info.aud !== c.clientId || (iss !== 'accounts.google.com' && iss !== 'https://accounts.google.com') || exp * 1000 < Date.now()) return fail('id_token');
    const sub = String(info.sub ?? '');
    if (!/^\d{1,40}$/.test(sub)) return fail('id_token');
    return sub;
  } catch (err) {
    if (err instanceof AppError) throw err;
    return fail('network');
  }
}
