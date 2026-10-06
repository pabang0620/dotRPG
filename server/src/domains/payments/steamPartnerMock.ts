// 프로세스 안 가짜 Steam(PAYMENTS_STEAM_MODE=mock, 운영 기동 거부). 자동 테스트는 이 어댑터로만 결제를 돌린다(실제 Steam 호출 금지).
// 이용자가 Steam 오버레이에서 하는 일(승인·거절)과 Steam 쪽 사건(환불·차지백)은 approve/deny/setStatus로 흉내 내고,
// 장애·위조 시나리오(응답 유실, 금액·통화·상품·수량·appid·steamid 불일치, 키 거절, 5xx)는 필드로 주입한다.
// 상태 문자열은 설계 7.4/9.2가 가정한 이름이다. TODO [확인] 샌드박스 실측 뒤 실제 철자·형태에 맞춘다(체크리스트 13번).
import {
  PartnerCallError,
  type FinalizeResult,
  type InitRequest,
  type InitResult,
  type QueryResult,
  type ReportEntry,
  type SteamPartner,
  type UserInfo,
} from './steamPartner';

export interface MockOrder {
  orderId: string;
  transId: string;
  steamId: string;
  appId: number;
  itemId: number;
  quantity: number;
  amountMinor: number;
  currency: string;
  status: string;
}

export type MockInitMode = 'ok' | 'reject' | 'timeout_after_create' | 'timeout_before_create';
export type MockFinalizeMode = 'ok' | 'reject' | 'timeout_after_finalize' | 'timeout_before_finalize';

/** QueryTxn 응답을 일부러 틀리게 만든다(위조·불일치 시나리오) */
export interface MockTamper {
  amountMinor?: number;
  currency?: string;
  itemId?: number;
  quantity?: number;
  appId?: number;
  steamId?: string;
  extraItem?: boolean;
  country?: string | null;
}

export class MockSteamPartner implements SteamPartner {
  orders = new Map<string, MockOrder>();
  calls = { getUserInfo: 0, initTxn: 0, queryTxn: 0, finalizeTxn: 0, getReport: 0 };
  user: UserInfo = { country: 'KR', currency: 'KRW' };
  userBySteamId = new Map<string, UserInfo>();
  /** 설정하면 모든 호출이 이 종류의 오류로 실패한다 */
  failAll: 'unavailable' | 'rejected' | null = null;
  initMode: MockInitMode = 'ok';
  finalizeMode: MockFinalizeMode = 'ok';
  tamper: MockTamper = {};
  /** getReport가 돌려줄 주문 외에 더 끼울 항목(우리 기록에 없는 주문 등) */
  extraReport: ReportEntry[] = [];
  /** 지연(ms): 동시성 시험에서 호출이 겹치게 한다 */
  finalizeDelayMs = 0;
  private seq = 5_000_000;

  reset(): void {
    this.orders.clear();
    this.calls = { getUserInfo: 0, initTxn: 0, queryTxn: 0, finalizeTxn: 0, getReport: 0 };
    this.user = { country: 'KR', currency: 'KRW' };
    this.userBySteamId.clear();
    this.failAll = null;
    this.initMode = 'ok';
    this.finalizeMode = 'ok';
    this.tamper = {};
    this.extraReport = [];
    this.finalizeDelayMs = 0;
  }

  private guard(): void {
    if (this.failAll === 'rejected') throw new PartnerCallError('rejected', 401);
    if (this.failAll === 'unavailable') throw new PartnerCallError('unavailable', 503);
  }

  // ---- 이용자·Steam 쪽 사건 ----
  approve(orderId: string): void {
    const o = this.must(orderId);
    if (o.status === 'Init') o.status = 'Approved';
  }
  deny(orderId: string): void {
    this.must(orderId).status = 'Failed';
  }
  setStatus(orderId: string, status: string): void {
    this.must(orderId).status = status;
  }
  private must(orderId: string): MockOrder {
    const o = this.orders.get(orderId);
    if (!o) throw new Error(`mock order ${orderId} not found`);
    return o;
  }
  /** 가장 최근 주문(시험 편의) */
  last(): MockOrder {
    const all = [...this.orders.values()];
    const o = all[all.length - 1];
    if (!o) throw new Error('mock has no orders');
    return o;
  }

  // ---- SteamPartner ----
  async getUserInfo(steamId: string): Promise<UserInfo> {
    this.calls.getUserInfo++;
    this.guard();
    return this.userBySteamId.get(steamId) ?? this.user;
  }

  async initTxn(r: InitRequest): Promise<InitResult> {
    this.calls.initTxn++;
    this.guard();
    if (this.initMode === 'timeout_before_create') throw new PartnerCallError('unavailable');
    if (this.initMode === 'reject') return { ok: false, errorCode: '9' };
    const o: MockOrder = {
      orderId: r.orderId,
      transId: String(this.seq++),
      steamId: r.steamId,
      appId: r.appId,
      itemId: r.itemId,
      quantity: 1,
      amountMinor: r.amountMinor,
      currency: r.currency,
      status: 'Init',
    };
    this.orders.set(r.orderId, o);
    if (this.initMode === 'timeout_after_create') throw new PartnerCallError('unavailable');
    return { ok: true, transId: o.transId };
  }

  async queryTxn(orderId: string): Promise<QueryResult> {
    this.calls.queryTxn++;
    this.guard();
    const o = this.orders.get(orderId);
    if (!o) return { found: false };
    const t = this.tamper;
    const items = [{ itemId: t.itemId ?? o.itemId, quantity: t.quantity ?? o.quantity, amountMinor: t.amountMinor ?? o.amountMinor }];
    if (t.extraItem) items.push({ itemId: o.itemId, quantity: 1, amountMinor: o.amountMinor });
    return {
      found: true,
      txn: {
        orderId: o.orderId,
        transId: o.transId,
        steamId: t.steamId ?? o.steamId,
        appId: t.appId ?? o.appId,
        status: o.status,
        country: t.country === undefined ? this.userBySteamId.get(o.steamId)?.country ?? this.user.country : t.country,
        currency: t.currency ?? o.currency,
        items,
      },
    };
  }

  async finalizeTxn(orderId: string): Promise<FinalizeResult> {
    this.calls.finalizeTxn++;
    this.guard();
    if (this.finalizeDelayMs > 0) await new Promise((res) => setTimeout(res, this.finalizeDelayMs));
    if (this.finalizeMode === 'timeout_before_finalize') throw new PartnerCallError('unavailable');
    if (this.finalizeMode === 'reject') return { ok: false, errorCode: '10' };
    const o = this.orders.get(orderId);
    if (!o || o.status !== 'Approved') return { ok: false, errorCode: 'not_approved' };
    o.status = 'Succeeded';
    if (this.finalizeMode === 'timeout_after_finalize') throw new PartnerCallError('unavailable');
    return { ok: true };
  }

  async getReport(_from: Date, _to: Date): Promise<ReportEntry[]> {
    this.calls.getReport++;
    this.guard();
    const mine = [...this.orders.values()].map((o) => ({ orderId: o.orderId, steamId: o.steamId, status: o.status, amountMinor: o.amountMinor, currency: o.currency }));
    return [...mine, ...this.extraReport];
  }
}
