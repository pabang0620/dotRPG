import { z } from 'zod';

/** 15단계 G8: 클라이언트 예외 보고. 길이를 넘는 값은 거절한다(클라이언트가 잘라서 보낸다) */
export const clientErrorBody = z.strictObject({
  version: z.string().min(1).max(32),
  platform: z.string().min(1).max(32),
  message: z.string().min(1).max(500),
  stack: z.string().max(2000).default(''),
  scene: z.string().max(64).optional(),
  at: z.iso.datetime({ offset: true }).optional(),
});
export type ClientErrorBody = z.infer<typeof clientErrorBody>;

/** 본문 상한(바이트). express.json 전체 한도(64KB)보다 좁게 이 경로만 본다 */
export const CLIENT_ERROR_MAX_BYTES = 4096;
