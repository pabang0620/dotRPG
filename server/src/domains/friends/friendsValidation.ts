import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

export const sendRequestBody = z.strictObject({ request_id: requestId, character_id: z.uuid(), target: z.uuid() });
export const respondBody = z.strictObject({ request_id: requestId, accept: z.boolean() });
export const idParams = z.object({ id: z.uuid() });

export type SendRequestBody = z.infer<typeof sendRequestBody>;
export type RespondBody = z.infer<typeof respondBody>;
export type IdParams = z.infer<typeof idParams>;
