// TOTP(RFC 6238): HMAC-SHA1, 30초, 6자리. node:crypto만 쓴다(의존성 없음).
import { createHmac, randomBytes } from 'node:crypto';

const STEP_SEC = 30;
const DIGITS = 6;
const B32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

export function base32Encode(buf: Buffer): string {
  let bits = 0;
  let value = 0;
  let out = '';
  for (const byte of buf) {
    value = (value << 8) | byte;
    bits += 8;
    while (bits >= 5) {
      out += B32[(value >>> (bits - 5)) & 31];
      bits -= 5;
    }
  }
  if (bits > 0) out += B32[(value << (5 - bits)) & 31];
  return out;
}

export function base32Decode(s: string): Buffer {
  let bits = 0;
  let value = 0;
  const out: number[] = [];
  for (const ch of s.replace(/=+$/, '').toUpperCase()) {
    const idx = B32.indexOf(ch);
    if (idx < 0) throw new Error('base32 문자가 아닙니다');
    value = (value << 5) | idx;
    bits += 5;
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 255);
      bits -= 8;
    }
  }
  return Buffer.from(out);
}

export const newTotpSecret = (): Buffer => randomBytes(20);

export const stepOf = (at: Date): number => Math.floor(at.getTime() / 1000 / STEP_SEC);

export function totpCode(secret: Buffer, step: number): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const h = createHmac('sha1', secret).update(counter).digest();
  const off = (h[h.length - 1] as number) & 0x0f;
  const bin =
    (((h[off] as number) & 0x7f) << 24) |
    (((h[off + 1] as number) & 0xff) << 16) |
    (((h[off + 2] as number) & 0xff) << 8) |
    ((h[off + 3] as number) & 0xff);
  return String(bin % 10 ** DIGITS).padStart(DIGITS, '0');
}

/** 앞뒤 1구간을 허용하고, 맞은 구간 번호를 돌려준다. lastStep 이하(이미 쓴 코드)는 거절한다 */
export function verifyTotp(secret: Buffer, code: string, now: Date, lastStep: number | null): number | null {
  const cur = stepOf(now);
  for (const step of [cur, cur - 1, cur + 1]) {
    if (lastStep !== null && step <= lastStep) continue;
    if (totpCode(secret, step) === code) return step;
  }
  return null;
}

export function otpauthUri(issuer: string, account: string, secret: Buffer): string {
  const label = encodeURIComponent(`${issuer}:${account}`);
  return `otpauth://totp/${label}?secret=${base32Encode(secret)}&issuer=${encodeURIComponent(issuer)}&algorithm=SHA1&digits=${DIGITS}&period=${STEP_SEC}`;
}
