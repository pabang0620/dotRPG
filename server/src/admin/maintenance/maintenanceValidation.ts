import { z } from 'zod';

// 운영자가 쓰는 문구는 제어 문자를 지운다(공지에 그대로 실리므로)
const clean = (max: number) =>
  z
    .string()
    .transform((s) => s.replace(/[\u0000-\u001f\u007f]/g, '').trim())
    .pipe(z.string().max(max));

export const uuidParams = z.object({ uuid: z.uuid() });
export const emptyQuery = z.object({});

export const scheduleBody = z
  .strictObject({
    request_id: z.uuid(),
    start_in_minutes: z.number().int().min(0).max(10080).optional(),
    starts_at: z.iso.datetime().optional(),
    duration_minutes: z.number().int().min(5).max(720),
    notice: clean(100).optional(),
  })
  .refine((b) => (b.start_in_minutes === undefined) !== (b.starts_at === undefined), {
    message: 'start_in_minutes 와 starts_at 중 하나만 보냅니다',
    path: ['start_in_minutes'],
  });

export const cancelBody = z.strictObject({ request_id: z.uuid() });
export const extendBody = z.strictObject({ request_id: z.uuid(), extend_minutes: z.number().int().min(5).max(240) });
export const broadcastBody = z.strictObject({
  request_id: z.uuid(),
  text: clean(100).pipe(z.string().min(1)),
});

export type ScheduleBody = z.infer<typeof scheduleBody>;
export type ExtendBody = z.infer<typeof extendBody>;
export type BroadcastBody = z.infer<typeof broadcastBody>;
