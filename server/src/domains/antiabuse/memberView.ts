// 멤버 카드 대조용 서버 값(9.1): 착용 장비 원본(slot, item_key)과 서버가 기록한 전직. 호스트 PC가 멤버 카드와 대조해 불일치하면 서버 값으로 덮어쓴다.
import type { Queryable } from '../../db/pool';
import { gearHash } from '../fieldsessions/gearHash';

export interface MemberLook {
  worn: { slot: number; item_key: string }[];
  career: number;
  gear_hash: string;
}

/** 캐릭터별 착용 장비(최대 8개)·전직·장비 지문을 쿼리 2번으로 읽는다(멤버 4명이면 약 1KB) */
export async function memberLooks(db: Queryable, characterIds: number[]): Promise<Map<number, MemberLook>> {
  const out = new Map<number, MemberLook>();
  if (characterIds.length === 0) return out;
  const worn = await db.query<{ character_id: string; slot: number; item_key: string }>(
    "SELECT character_id, slot, item_key FROM character_items WHERE location = 'worn' AND character_id = ANY($1::bigint[]) ORDER BY character_id, slot",
    [characterIds],
  );
  const career = await db.query<{ character_id: string; career: number }>('SELECT character_id, career FROM character_career WHERE character_id = ANY($1::bigint[])', [characterIds]);
  const careerOf = new Map(career.rows.map((r) => [Number(r.character_id), r.career] as const));
  for (const id of characterIds) out.set(id, { worn: [], career: careerOf.get(id) ?? 0, gear_hash: '' });
  for (const r of worn.rows) (out.get(Number(r.character_id)) as MemberLook).worn.push({ slot: r.slot, item_key: r.item_key });
  for (const look of out.values()) look.gear_hash = gearHash(look.worn.map((w) => w.item_key));
  return out;
}
