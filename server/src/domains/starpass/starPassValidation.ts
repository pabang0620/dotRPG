import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 지급량은 받지 않는다(strict) */
export const dailyBody = z.strictObject({ request_id: requestId });
export type DailyBody = z.infer<typeof dailyBody>;
