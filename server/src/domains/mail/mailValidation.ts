import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

export const claimBody = z.strictObject({ request_id: requestId });
export const claimAllBody = z.strictObject({ request_id: requestId });
export const mailParams = z.object({ uuid: z.uuid(), mail_id: z.uuid() });

export const listQuery = z.strictObject({
  tab: z.enum(['all', 'gold', 'item']).default('all'),
  page: z.coerce.number().int().min(1).default(1),
  limit: z.coerce.number().int().min(1).max(50).default(20),
});
export const summaryQuery = z.strictObject({ since: z.iso.datetime({ offset: true }).optional() });

export type ClaimBody = z.infer<typeof claimBody>;
export type ClaimAllBody = z.infer<typeof claimAllBody>;
export type MailParams = z.infer<typeof mailParams>;
export type ListQuery = z.infer<typeof listQuery>;
export type SummaryQuery = z.infer<typeof summaryQuery>;
