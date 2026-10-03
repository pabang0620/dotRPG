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
  })
  .refine((b) => (b.run_id === undefined) === (b.room_index === undefined), {
    message: 'run_id와 room_index는 함께 보내야 합니다',
    path: ['room_index'],
  });

export type KillBody = z.infer<typeof killBody>;
