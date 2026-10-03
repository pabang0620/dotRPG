import { z } from 'zod';
import { adminLoginId } from '../auth/adminAuthValidation';

const requestId = z.uuid();
const role = z.enum(['viewer', 'operator', 'owner']);

export const createAdminBody = z.strictObject({
  request_id: requestId,
  login_id: adminLoginId,
  display_name: z.string().min(1).max(30),
  role,
});

export const adminActionBody = z.strictObject({
  request_id: requestId,
  action: z.enum(['set_role', 'disable', 'enable', 'reset_2fa', 'unlock', 'reset_password']),
  role: role.optional(),
});

export const uuidParams = z.object({ uuid: z.uuid() });

export const auditQuery = z.object({
  admin: z.uuid().optional(),
  target: z.uuid().optional(),
  action: z.string().regex(/^[a-z_.]{1,60}$/).optional(),
  result: z.enum(['ok', 'denied', 'invalid', 'error']).optional(),
  since: z.iso.datetime().optional(),
  until: z.iso.datetime().optional(),
  before: z.string().regex(/^\d{1,18}$/).optional(),
  limit: z.coerce.number().int().min(1).max(200).default(50),
});

export type CreateAdminBody = z.infer<typeof createAdminBody>;
export type AdminActionBody = z.infer<typeof adminActionBody>;
export type AuditQuery = z.infer<typeof auditQuery>;
