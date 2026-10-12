import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 상자 id: "맵id:x:y" */
const chestId = z.string().min(3).max(64).regex(/^[A-Za-z0-9_]+:\d+:\d+$/, '상자 id 형식이 올바르지 않습니다');

export const chestBody = z.strictObject({ request_id: requestId, chest_id: chestId });

export type ChestBody = z.infer<typeof chestBody>;
