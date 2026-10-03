import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

export const enterBody = z.strictObject({
  request_id: requestId,
  dungeon_id: z.string().min(1).max(40).regex(/^[a-z][a-z0-9_]*$/),
  difficulty: z.number().int().min(0).max(3),
});

/** 사실만 보낸다. 랭크·경험치·카드는 서버가 정한다 */
export const resultBody = z.strictObject({
  request_id: requestId,
  outcome: z.enum(['cleared', 'failed']),
  stats: z.strictObject({
    elapsed_ms: z.number().int().min(0).max(86_400_000),
    hits_taken: z.number().int().min(0).max(999),
    max_combo: z.number().int().min(0).max(9999),
    revives_used: z.number().int().min(0).max(9),
  }),
});

export const pickBody = z.strictObject({ request_id: requestId, index: z.number().int().min(0).max(3) });
export const runParams = z.object({ uuid: z.uuid(), run_id: z.uuid() });

export type EnterBody = z.infer<typeof enterBody>;
export type ResultBody = z.infer<typeof resultBody>;
export type PickBody = z.infer<typeof pickBody>;
export type RunParams = z.infer<typeof runParams>;
