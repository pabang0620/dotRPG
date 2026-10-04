import { z } from 'zod';

export const charParams = z.object({ uuid: z.uuid() });
export const titleBody = z.strictObject({ achievement_id: z.string().min(1).max(40).nullable() });

export type CharParams = z.infer<typeof charParams>;
export type TitleBody = z.infer<typeof titleBody>;
