// 서명 견적(Docs/server/phase11_payments.md 5.2). 상태 없는 토큰이다: 계정·상품·통화·금액·별조각·상품표 버전·만료를 묶는다.
// 견적은 비밀이 아니라 "화면에 보여 준 것과 지금 서버가 계산한 것이 같은가"를 확인하는 무결성 참조다.
// 서명이 맞아도 서버는 토큰 안의 가격을 쓰지 않고 상품표에서 다시 읽는다.
import { createHmac, timingSafeEqual } from 'node:crypto';
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';

export interface QuotePayload {
  /** 계정 uuid */
  a: string;
  /** 상품 id */
  p: string;
  /** 통화 */
  c: string;
  /** 금액(통화 최소 단위) */
  m: number;
  /** 별조각 수 */
  s: number;
  /** 상품표 버전 */
  v: string;
  /** 만료 epoch초 */
  e: number;
}

const b64 = (b: Buffer | string): string => Buffer.from(b).toString('base64url');

function secret(): string {
  const s = getConfig().pay.quoteSecret;
  if (!s) throw new AppError(503, '결제를 사용할 수 없습니다.', 'FEATURE_DISABLED');
  return s;
}

const mac = (body: string): Buffer => createHmac('sha256', secret()).update(`v1.${body}`).digest();

export function signQuote(p: QuotePayload): string {
  const body = b64(JSON.stringify(p));
  return `v1.${body}.${b64(mac(body))}`;
}

/** 서명이 맞으면 내용을, 아니면 null(형식 오류 포함). 상수 시간 비교 */
export function readQuote(token: string): QuotePayload | null {
  const parts = token.split('.');
  if (parts.length !== 3 || parts[0] !== 'v1') return null;
  const [, body, sig] = parts as [string, string, string];
  const want = mac(body);
  let got: Buffer;
  try {
    got = Buffer.from(sig, 'base64url');
  } catch {
    return null;
  }
  if (got.length !== want.length || !timingSafeEqual(got, want)) return null;
  try {
    const p = JSON.parse(Buffer.from(body, 'base64url').toString('utf8')) as Partial<QuotePayload>;
    if (
      typeof p.a !== 'string' || typeof p.p !== 'string' || typeof p.c !== 'string' || typeof p.v !== 'string' ||
      typeof p.m !== 'number' || typeof p.s !== 'number' || typeof p.e !== 'number'
    ) return null;
    return p as QuotePayload;
  } catch {
    return null;
  }
}
