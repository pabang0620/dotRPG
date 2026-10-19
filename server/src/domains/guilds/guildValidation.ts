import { z } from 'zod';
import { charParams, requestId } from '../economy/economyValidation';
import { GUILD_NAME_MAX, GUILD_NAME_MIN, GUILD_NAME_RE, GUILD_NOTICE_MAX } from './guildRules';

export const guildName = z
  .string()
  .transform((s) => s.normalize('NFC'))
  .refine((s) => [...s].length >= GUILD_NAME_MIN && [...s].length <= GUILD_NAME_MAX, `길드 이름은 ${GUILD_NAME_MIN}~${GUILD_NAME_MAX}자여야 합니다`)
  .refine((s) => GUILD_NAME_RE.test(s), '길드 이름은 한글 완성형, 영문, 숫자만 쓸 수 있습니다');
const notice = z.string().transform((s) => s.normalize('NFC').trim()).refine((s) => [...s].length <= GUILD_NOTICE_MAX, `공지는 ${GUILD_NOTICE_MAX}자까지입니다`);
const joinMode = z.enum(['open', 'approval']);

export const createBody = z.strictObject({ request_id: requestId, name: guildName, notice: notice.optional(), join_mode: joinMode });
export const applyBody = z.strictObject({ request_id: requestId, guild_id: z.uuid() });
export const requestBody = z.strictObject({ request_id: requestId });
export const patchBody = z.strictObject({ request_id: requestId, notice: notice.optional(), join_mode: joinMode.optional() });
export const applicationParams = charParams.extend({ id: z.uuid(), action: z.enum(['accept', 'reject']) });
export const memberParams = charParams.extend({ characterId: z.uuid(), action: z.enum(['kick', 'promote', 'demote', 'transfer']) });
export const leaveParams = charParams.extend({ action: z.enum(['leave', 'disband']) });
export const searchQuery = z.object({
  query: z.string().max(GUILD_NAME_MAX).optional().default(''),
  page: z.coerce.number().int().min(1).max(1000).optional().default(1),
});

export type CreateBody = z.infer<typeof createBody>;
export type ApplyBody = z.infer<typeof applyBody>;
export type RequestBody = z.infer<typeof requestBody>;
export type PatchBody = z.infer<typeof patchBody>;
export type SearchQuery = z.infer<typeof searchQuery>;
