import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

const dungeonId = z.string().min(1).max(40).regex(/^[a-z][a-z0-9_]*$/);

export const createPartyBody = z.strictObject({
  request_id: requestId,
  dungeon_id: dungeonId,
  difficulty: z.number().int().min(0).max(3),
  max_members: z.number().int().min(2).max(4),
  min_power: z.number().int().min(0).max(99999),
  message: z.string().max(30).optional(),
  listed: z.boolean(),
});
export const patchPartyBody = z
  .strictObject({
    listed: z.boolean().optional(),
    message: z.string().max(30).optional(),
    min_power: z.number().int().min(0).max(99999).optional(),
    max_members: z.number().int().min(2).max(4).optional(),
    /** 이미 만든 파티의 목적 던전·난이도 변경(함께 보낸다) */
    dungeon_id: dungeonId.optional(),
    difficulty: z.number().int().min(0).max(3).optional(),
  })
  .refine((b) => Object.keys(b).length > 0, '하나 이상 필요합니다')
  .refine((b) => (b.dungeon_id === undefined) === (b.difficulty === undefined), 'dungeon_id와 difficulty는 함께 보냅니다');
export const listQuery = z.object({
  dungeon_id: dungeonId.optional(),
  difficulty: z.coerce.number().int().min(0).max(3).optional(),
  page: z.coerce.number().int().min(1).default(1),
  limit: z.coerce.number().int().min(1).max(50).default(20),
});
export const pollQuery = z.object({ after_version: z.coerce.number().int().min(0).optional() });
export const requestOnlyBody = z.strictObject({ request_id: requestId });
export const respondBody = z.strictObject({ request_id: requestId, accept: z.boolean() });
export const readyBody = z.strictObject({ ready: z.boolean() });
export const targetBody = z.strictObject({ request_id: requestId, target: z.uuid() });
export const charParams = z.object({ uuid: z.uuid() });
export const partyParams = z.object({ uuid: z.uuid(), party_id: z.uuid() });
export const applicationParams = z.object({ uuid: z.uuid(), application_id: z.uuid() });

export type CreatePartyBody = z.infer<typeof createPartyBody>;
export type PatchPartyBody = z.infer<typeof patchPartyBody>;
export type ListQuery = z.infer<typeof listQuery>;
export type PollQuery = z.infer<typeof pollQuery>;
export type RequestOnlyBody = z.infer<typeof requestOnlyBody>;
export type RespondBody = z.infer<typeof respondBody>;
export type ReadyBody = z.infer<typeof readyBody>;
export type TargetBody = z.infer<typeof targetBody>;
export type PartyParams = z.infer<typeof partyParams>;
export type ApplicationParams = z.infer<typeof applicationParams>;
