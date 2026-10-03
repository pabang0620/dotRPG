import { z } from 'zod';

export const adminLoginId = z
  .string()
  .transform((s) => s.toLowerCase())
  .pipe(z.string().regex(/^[a-z0-9_.-]{3,32}$/, '아이디는 영문 소문자·숫자·_.- 3~32자입니다'));

export const loginBody = z.strictObject({
  login_id: adminLoginId,
  password: z.string().min(1).max(200),
  totp: z.string().regex(/^\d{6}$/).optional(),
});

export const passwordBody = z.strictObject({
  current_password: z.string().min(1).max(200),
  new_password: z.string().min(12, '비밀번호는 12자 이상입니다').max(200),
});

export const totpConfirmBody = z.strictObject({ code: z.string().regex(/^\d{6}$/) });
export const emptyBody = z.strictObject({});

export type LoginBody = z.infer<typeof loginBody>;
export type PasswordBody = z.infer<typeof passwordBody>;
export type TotpConfirmBody = z.infer<typeof totpConfirmBody>;
