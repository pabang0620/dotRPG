import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

export const inviteBodyIn = z.strictObject({ request_id: requestId, target: z.uuid() });
export const respondBody = z.strictObject({ request_id: requestId, accept: z.boolean() });
export const charParams = z.object({ uuid: z.uuid() });
export const inviteParams = z.object({ uuid: z.uuid(), id: z.uuid() });

export type InviteBodyIn = z.infer<typeof inviteBodyIn>;
export type RespondBody = z.infer<typeof respondBody>;
export type InviteParams = z.infer<typeof inviteParams>;
