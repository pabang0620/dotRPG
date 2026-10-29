import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** one: 한 칸, chapter: 열린 챕터 끝까지 */
export const mainQuestBody = z.strictObject({ request_id: requestId, mode: z.enum(['one', 'chapter']).default('one') });
/** one: 한 레벨, max: 레벨 상한까지 */
export const levelBody = z.strictObject({ request_id: requestId, mode: z.enum(['one', 'max']).default('one') });

export type MainQuestBody = z.infer<typeof mainQuestBody>;
export type LevelBody = z.infer<typeof levelBody>;
