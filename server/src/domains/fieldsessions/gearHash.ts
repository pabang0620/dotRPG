// 착용 장비 지문(phase8_api.md 6.9): 장비 키를 정렬해 이은 문자열의 SHA-256 앞 16자. 호스트가 멤버 카드를 믿어도 되는지 대조하는 서버 기준값.
import { createHash } from 'node:crypto';
import type { Queryable } from '../../db/pool';
import { wornKeysOf } from './fieldRepository';

export const gearHash = (keys: string[]): string => createHash('sha256').update([...keys].sort().join(',')).digest('hex').slice(0, 16);

export async function gearHashes(db: Queryable, characterIds: number[]): Promise<Map<number, string>> {
  const worn = await wornKeysOf(db, characterIds);
  return new Map(characterIds.map((id) => [id, gearHash(worn.get(id) ?? [])] as const));
}
