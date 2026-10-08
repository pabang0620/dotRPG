import { z } from 'zod';
import { deviceBody, steamBody } from '../auth/authValidation';

// 대문자가 오면 소문자로 바꿔 받아준다(authValidation 의 loginId 와 같은 규칙)
const loginId = z
  .string()
  .transform((s) => s.toLowerCase())
  .pipe(z.string().regex(/^[a-z0-9_]{4,20}$/, '아이디는 영문 소문자·숫자·_ 4~20자입니다'));
const password = z.string().min(8, '비밀번호는 8~64자입니다').max(64, '비밀번호는 8~64자입니다');
const ticket = steamBody.shape.ticket;

/** W2 재인증: 유예 일수·손실 수량·기한·사유 텍스트는 받지 않는다(strict) */
const reauthSelf = z.discriminatedUnion('provider', [
  z.strictObject({ provider: z.literal('steam'), ticket }),
  z.strictObject({ provider: z.literal('dev'), password }),
]);

export const withdrawBody = z.strictObject({
  request_id: z.uuid(),
  confirm: z.string().min(1).max(40),
  ack_progress_loss: z.literal(true),
  ack_paid_loss: z.boolean().optional(),
  /** WITHDRAW_REAUTH_REQUIRED=false(개발 전용)에서만 생략할 수 있다 */
  reauth: reauthSelf.optional(),
});

export const cancelBody = z.strictObject({
  request_id: z.uuid(),
  reauth: z.discriminatedUnion('provider', [
    z.strictObject({ provider: z.literal('steam'), ticket }),
    z.strictObject({ provider: z.literal('dev'), login_id: loginId, password }),
  ]),
  device: deviceBody.optional(),
});

export type WithdrawBody = z.infer<typeof withdrawBody>;
export type CancelBody = z.infer<typeof cancelBody>;
