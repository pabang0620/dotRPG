import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 받지 않는 값: 경험치, 골드, 드롭, 몬스터 레벨, 시각 (strict라 보내면 400 VALIDATION) */
export const killBody = z
  .strictObject({
    request_id: requestId,
    map_id: z.string().min(1).max(32),
    monster_id: z.string().min(1).max(40),
    hits: z.number().int().min(0).max(60).optional(),
    run_id: z.uuid().optional(),
    room_index: z.number().int().min(0).max(9).optional(),
    /** 8단계: 필드 파티 세션에서의 처치. monster_ref 는 호스트가 붙인 몬스터 식별(세대 x 2^20 + 번호) */
    session_id: z.uuid().optional(),
    monster_ref: z.number().int().min(0).max(Number.MAX_SAFE_INTEGER).optional(),
  })
  .refine((b) => (b.run_id === undefined) === (b.room_index === undefined), {
    message: 'run_id와 room_index는 함께 보내야 합니다',
    path: ['room_index'],
  })
  .refine((b) => b.session_id === undefined || b.run_id === undefined, {
    message: 'session_id와 run_id는 함께 보낼 수 없습니다',
    path: ['session_id'],
  })
  .refine((b) => b.monster_ref === undefined || b.session_id !== undefined, {
    message: 'monster_ref는 session_id와 함께 보내야 합니다',
    path: ['monster_ref'],
  })
  .refine((b) => b.session_id === undefined || b.monster_ref !== undefined, {
    message: 'session_id가 있으면 monster_ref가 필요합니다',
    path: ['monster_ref'],
  });

export type KillBody = z.infer<typeof killBody>;
