// 입장 토큰: base64url(HMAC-SHA256(run_key, "<판 uuid>.<캐릭터 uuid>.<자리>")[0..16]) 22자. 서버는 저장하지 않고 필요할 때 계산한다.
import { createHmac, timingSafeEqual } from 'node:crypto';

export function entryToken(runKey: Buffer, runUuid: string, characterUuid: string, slot: number): string {
  return createHmac('sha256', runKey).update(`${runUuid}.${characterUuid}.${slot}`).digest().subarray(0, 16).toString('base64url');
}

export function tokenMatches(expected: string, given: string): boolean {
  const a = Buffer.from(expected);
  const b = Buffer.from(given);
  return a.length === b.length && timingSafeEqual(a, b);
}
