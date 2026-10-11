import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 가격은 받지 않는다(strict) */
export const styleBuyBody = z.strictObject({ request_id: requestId });
export type StyleBuyBody = z.infer<typeof styleBuyBody>;
