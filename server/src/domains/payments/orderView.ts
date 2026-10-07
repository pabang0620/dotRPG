// 플레이어에게 보이는 주문 모양(13.1 OrderView). 내부 fail_reason·정지 사유·Steam 원문은 절대 싣지 않는다(위협 22): fail_code 4종만.
import type { OrderRow } from './paymentsRepository';

export type FailCode = 'DECLINED' | 'EXPIRED' | 'NOT_COMPLETED' | 'UNDER_REVIEW';

export interface OrderView {
  id: string;
  state: OrderRow['state'];
  product_id: string;
  stars: number;
  currency: string;
  display_price: string;
  steam_order_id: string;
  created_at: string;
  expires_at: string;
  granted_at: string | null;
  final: boolean;
  /** true = Steam이 청구함, false = 확실히 청구 없음, null = 아직 모르거나 확인 중 */
  charged: boolean | null;
  fail_code: FailCode | null;
}

const ZERO_DECIMAL = new Set(['KRW', 'JPY', 'VND', 'CLP', 'ISK', 'HUF']);

/** 통화 최소 단위 정수를 표시 문자열로. TODO [확인] Steam amount 의 단위(7.8-2)가 확정되면 통화별 자릿수를 맞춘다 */
export function formatPrice(amountMinor: number, currency: string): string {
  const exp = ZERO_DECIMAL.has(currency) ? 0 : 2;
  const v = amountMinor / 10 ** exp;
  return `${v.toLocaleString('en-US', { minimumFractionDigits: exp, maximumFractionDigits: exp })} ${currency}`;
}

const FINAL_STATES: readonly OrderRow['state'][] = ['granted', 'failed', 'expired', 'refunded', 'chargeback'];

export function failCodeOf(o: OrderRow): FailCode | null {
  if (o.state === 'expired') return 'EXPIRED';
  if (o.state !== 'failed') return null;
  // 청구되었을 수 있는데 검증이 어긋난 주문(needs_review)은 확인 중으로 보인다
  if (o.needs_review || o.fail_reason === 'mismatch') return 'UNDER_REVIEW';
  if (o.fail_reason === 'user_denied') return 'DECLINED';
  return 'NOT_COMPLETED';
}

export function chargedOf(o: OrderRow): boolean | null {
  if (o.state === 'finalized' || o.state === 'granted' || o.state === 'refunded' || o.state === 'chargeback') return true;
  if ((o.state === 'failed' || o.state === 'expired') && !o.needs_review) return false;
  return null;
}

export function orderView(o: OrderRow): OrderView {
  return {
    id: o.uuid,
    state: o.state,
    product_id: o.product_id,
    stars: o.stars,
    currency: o.currency,
    display_price: formatPrice(o.amount_minor, o.currency),
    steam_order_id: o.steam_order_id,
    created_at: o.created_at.toISOString(),
    expires_at: o.expires_at.toISOString(),
    granted_at: o.granted_at ? o.granted_at.toISOString() : null,
    final: FINAL_STATES.includes(o.state),
    charged: chargedOf(o),
    fail_code: failCodeOf(o),
  };
}
