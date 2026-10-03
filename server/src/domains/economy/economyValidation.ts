import { z } from 'zod';
import { ITEM_KEY_RE } from '../../utils/itemKey';

export const requestId = z.uuid();
/** 아이템 키(기본 id 또는 id+강화단계). 'gold'는 아이템이 아니다 */
export const itemKeySchema = z
  .string()
  .max(48)
  .regex(ITEM_KEY_RE, '아이템 키 형식이 올바르지 않습니다')
  .refine((k) => k !== 'gold', '골드는 아이템이 아닙니다');
/** 기본 아이템 id(강화 단계 없음) */
export const itemIdSchema = z.string().min(1).max(40).regex(/^[a-z][a-z0-9_]*$/);
export const charParams = z.object({ uuid: z.uuid() });
