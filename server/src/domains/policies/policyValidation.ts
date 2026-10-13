import { z } from 'zod';

export const consentBody = z.strictObject({
  agree: z
    .array(z.strictObject({ id: z.string().min(1).max(20), version: z.string().min(1).max(40) }))
    .min(1)
    .max(10),
});
export type ConsentBody = z.infer<typeof consentBody>;

export const pageParams = z.object({ lang: z.string().min(1).max(10), doc: z.string().min(1).max(30) });
export type PageParams = z.infer<typeof pageParams>;
