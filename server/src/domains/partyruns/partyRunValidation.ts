import { z } from 'zod';
import { requestId } from '../economy/economyValidation';
import { charParams } from '../economy/economyValidation';

export const runParams = z.object({ uuid: z.uuid(), id: z.uuid() });
export { charParams };

export const requestOnlyBody = z.strictObject({ request_id: requestId });
export const startBody = z.strictObject({ request_id: requestId, ai_count: z.number().int().min(0).max(3) });
export const joinBody = z.strictObject({
  request_id: requestId,
  entry_token: z.string().length(22).regex(/^[A-Za-z0-9_-]+$/).optional(),
  host_steam_id: z.string().regex(/^\d{17}$/).optional(),
});
export const heartbeatBody = z.strictObject({ seen_epoch: z.number().int().min(1) });
export const claimBody = z.strictObject({ request_id: requestId, observed_epoch: z.number().int().min(1) });

/** 방장의 관찰: 경험치·골드·드롭·랭크·카드·확률은 받지 않는다(strict) */
export const hostReportBody = z.strictObject({
  request_id: requestId,
  host_epoch: z.number().int().min(1),
  outcome: z.enum(['cleared', 'failed']),
  elapsed_ms: z.number().int().min(0).max(3_600_000),
  rooms: z
    .array(
      z.strictObject({
        room_index: z.number().int().min(0).max(9),
        kills: z.array(z.strictObject({ monster_id: z.string().min(1).max(40), count: z.number().int().min(0).max(99) })).max(20),
      }),
    )
    .max(10),
  members: z
    .array(
      z.strictObject({
        character_id: z.uuid(),
        hits_taken: z.number().int().min(0).max(999),
        max_combo: z.number().int().min(0).max(9999),
        revives_used: z.number().int().min(0).max(9),
        damage_dealt: z.number().int().min(0).max(2_147_483_647),
        /** 9단계: 호스트가 관찰한 이 멤버의 적중 횟수(없어도 동작: 지분만으로 판정) */
        hits_landed: z.number().int().min(0).max(100_000).optional(),
        /** 9단계: 호스트가 멤버 카드(레벨·장비·직업)와 서버 뷰의 불일치를 봤다. 기록만 하고 판 결과에 쓰지 않는다 */
        card_mismatch: z.boolean().optional(),
      }),
    )
    .min(1)
    .max(4)
    .refine((ms) => new Set(ms.map((m) => m.character_id)).size === ms.length, '멤버가 중복되었습니다'),
  ai: z.array(z.strictObject({ slot: z.number().int().min(0).max(3), damage_dealt: z.number().int().min(0).max(2_147_483_647) })).max(3),
});

export type StartBody = z.infer<typeof startBody>;
export type JoinBody = z.infer<typeof joinBody>;
export type RequestOnlyBody = z.infer<typeof requestOnlyBody>;
export type HeartbeatBody = z.infer<typeof heartbeatBody>;
export type ClaimBody = z.infer<typeof claimBody>;
export type HostReportBody = z.infer<typeof hostReportBody>;
export type RunParams = z.infer<typeof runParams>;
