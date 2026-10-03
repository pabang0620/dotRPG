import { z } from 'zod';
import { REPORT_REASON_CODES } from '../../gamedata/chatData';
import { requestId } from '../economy/economyValidation';

export const reportBody = z.strictObject({
  request_id: requestId,
  character_id: z.uuid(),
  target: z.uuid(),
  reason: z.enum(REPORT_REASON_CODES),
});
export type ReportBody = z.infer<typeof reportBody>;
