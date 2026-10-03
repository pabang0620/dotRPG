import { z } from 'zod';

export const characterParams = z.object({ character_id: z.uuid() });
export const idParams = z.object({ id: z.uuid() });
export type CharacterParams = z.infer<typeof characterParams>;
export type IdParams = z.infer<typeof idParams>;
