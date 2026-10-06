import { z } from 'zod';
import { charParams } from '../economy/economyValidation';

export { charParams };

/** 받지 않는 값: 시각, 활동 시간, 좌표, 기기 정보(기기는 로그인 세션에서 온다). request_id도 받지 않는다(마지막 값이 이기는 상태 신호) */
export const presenceBody = z.strictObject({
  map_id: z.string().min(1).max(64),
  auto_play: z.boolean(),
  input_recent: z.boolean(),
});
export const leaveBody = z.strictObject({});

export type PresenceBody = z.infer<typeof presenceBody>;
