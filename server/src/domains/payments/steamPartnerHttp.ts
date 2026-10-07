// 실제 Steam 결제 API 호출(PAYMENTS_STEAM_MODE=sandbox|live). 이 파일은 테스트에서 실행하지 않는다(실제 Steam 호출 금지).
// 키는 POST 본문(form)에 싣고, GET이 키를 쿼리로 실어야 하는 호출도 URL을 어떤 로그·오류 메시지에도 넣지 않는다(7.6).
//
// TODO [확인] 아래 경로·버전·파라미터명·응답 필드는 Steamworks Microtransactions 문서(7.8)와 샌드박스 실측으로 확정한다.
// 현재 값은 설계 시점의 기억에 따른 추정이다: 호스트(STEAM_PARTNER_API_BASE), 샌드박스 인터페이스 이름(ISteamMicroTxnSandbox),
// InitTxn v3(orderid, steamid, appid, itemcount, language, currency, usersession=client, itemid[0], qty[0], amount[0], description[0]),
// QueryTxn v3(orderid), FinalizeTxn v2(orderid), GetUserInfo v2(steamid, ipaddress), GetReport v5(type, time, maxresults),
// amount 단위(통화 최소 단위인지, 세금 포함인지), status 문자열 철자, 항목 필드(itemid, qty, amount).
import { getConfig } from '../../config/env';
import {
  PartnerCallError,
  registerPartnerFactories,
  type FinalizeResult,
  type InitRequest,
  type InitResult,
  type QueryResult,
  type ReportEntry,
  type SteamPartner,
  type UserInfo,
} from './steamPartner';
import { MockSteamPartner } from './steamPartnerMock';

const TIMEOUT_MS = 8000;

interface Envelope {
  response?: { result?: string; error?: { errorcode?: number; errordesc?: string }; params?: Record<string, unknown> };
}

class HttpSteamPartner implements SteamPartner {
  private iface(): string {
    return getConfig().pay.mode === 'sandbox' ? 'ISteamMicroTxnSandbox' : 'ISteamMicroTxn';
  }

  /** 응답 본문과 URL은 이 함수 밖으로 나가지 않는다. 오류는 PartnerCallError(키 없음)로만 던진다 */
  private async request(method: 'GET' | 'POST', name: string, version: string, params: Record<string, string>): Promise<Envelope> {
    const cfg = getConfig().pay;
    const form = new URLSearchParams({ key: cfg.publisherKey ?? '', ...params });
    const url = new URL(`/${this.iface()}/${name}/${version}/`, cfg.partnerApiBase);
    let res: Response;
    try {
      if (method === 'GET') {
        url.search = form.toString();
        res = await fetch(url, { method: 'GET', signal: AbortSignal.timeout(TIMEOUT_MS) });
      } else {
        res = await fetch(url, {
          method: 'POST',
          headers: { 'content-type': 'application/x-www-form-urlencoded' },
          body: form,
          signal: AbortSignal.timeout(TIMEOUT_MS),
        });
      }
    } catch {
      throw new PartnerCallError('unavailable');
    }
    if (res.status === 401 || res.status === 403) throw new PartnerCallError('rejected', res.status);
    if (res.status >= 500) throw new PartnerCallError('unavailable', res.status);
    try {
      return (await res.json()) as Envelope;
    } catch {
      throw new PartnerCallError('unavailable', res.status);
    }
  }

  async getUserInfo(steamId: string): Promise<UserInfo> {
    const e = await this.request('GET', 'GetUserInfo', 'v2', { steamid: steamId, appid: String(getConfig().steam.appId ?? '') });
    const p = e.response?.params;
    const country = typeof p?.country === 'string' ? p.country.toUpperCase() : '';
    const currency = typeof p?.currency === 'string' ? p.currency.toUpperCase() : '';
    if (e.response?.result !== 'OK' || !/^[A-Z]{2}$/.test(country) || !/^[A-Z]{3}$/.test(currency)) throw new PartnerCallError('unavailable');
    return { country, currency };
  }

  async initTxn(r: InitRequest): Promise<InitResult> {
    const e = await this.request('POST', 'InitTxn', 'v3', {
      orderid: r.orderId,
      steamid: r.steamId,
      appid: String(r.appId),
      itemcount: '1',
      language: r.language,
      currency: r.currency,
      usersession: 'client',
      'itemid[0]': String(r.itemId),
      'qty[0]': '1',
      'amount[0]': String(r.amountMinor),
      'description[0]': r.itemName,
    });
    if (e.response?.result === 'OK') {
      const t = e.response.params?.transid;
      return { ok: true, transId: t === undefined || t === null ? null : String(t) };
    }
    return { ok: false, errorCode: String(e.response?.error?.errorcode ?? 'unknown') };
  }

  async queryTxn(orderId: string): Promise<QueryResult> {
    const e = await this.request('GET', 'QueryTxn', 'v3', { appid: String(getConfig().steam.appId ?? ''), orderid: orderId });
    if (e.response?.result !== 'OK') {
      // 주문이 없다는 응답과 일시 오류를 구분하는 방법은 [확인]. 구분이 안 되면 모름(unavailable)으로 다룬다
      if (e.response?.error?.errorcode === 8) return { found: false };
      throw new PartnerCallError('unavailable');
    }
    const p = e.response.params ?? {};
    const items = Array.isArray(p.items) ? (p.items as Record<string, unknown>[]) : [];
    return {
      found: true,
      txn: {
        orderId: String(p.orderid ?? ''),
        transId: p.transid === undefined ? null : String(p.transid),
        steamId: String(p.steamid ?? ''),
        appId: Number(p.appid ?? getConfig().steam.appId ?? 0),
        status: String(p.status ?? '').slice(0, 40),
        country: typeof p.country === 'string' ? p.country.toUpperCase() : null,
        currency: String(p.currency ?? '').toUpperCase(),
        items: items.map((i) => ({ itemId: Number(i.itemid), quantity: Number(i.qty), amountMinor: Number(i.amount) })),
      },
    };
  }

  async finalizeTxn(orderId: string): Promise<FinalizeResult> {
    const e = await this.request('POST', 'FinalizeTxn', 'v2', { orderid: orderId, appid: String(getConfig().steam.appId ?? '') });
    return e.response?.result === 'OK' ? { ok: true } : { ok: false, errorCode: String(e.response?.error?.errorcode ?? 'unknown') };
  }

  async getReport(from: Date, _to: Date): Promise<ReportEntry[]> {
    const e = await this.request('GET', 'GetReport', 'v5', {
      appid: String(getConfig().steam.appId ?? ''),
      type: 'STANDARD',
      time: from.toISOString(),
      maxresults: '1000',
    });
    if (e.response?.result !== 'OK') throw new PartnerCallError('unavailable');
    const orders = Array.isArray(e.response.params?.orders) ? (e.response.params?.orders as Record<string, unknown>[]) : [];
    return orders.map((o) => ({
      orderId: String(o.orderid ?? ''),
      steamId: String(o.steamid ?? ''),
      status: String(o.status ?? '').slice(0, 40),
      amountMinor: Number(o.amount ?? 0),
      currency: String(o.currency ?? '').toUpperCase(),
    }));
  }
}

registerPartnerFactories({ http: () => new HttpSteamPartner(), mock: () => getMockPartner() });

let mockSingleton: MockSteamPartner | null = null;
/** mode=mock(개발·시험)이 쓰는 프로세스 안 가짜 Steam */
export function getMockPartner(): MockSteamPartner {
  mockSingleton ??= new MockSteamPartner();
  return mockSingleton;
}
