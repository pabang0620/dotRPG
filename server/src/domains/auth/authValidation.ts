import { z } from 'zod';

// 대문자가 오면 소문자로 바꿔 받아준다
const loginId = z
  .string()
  .transform((s) => s.toLowerCase())
  .pipe(z.string().regex(/^[a-z0-9_]{4,20}$/, '아이디는 영문 소문자·숫자·_ 4~20자입니다'));

const password = z.string().min(8, '비밀번호는 8~64자입니다').max(64, '비밀번호는 8~64자입니다');

export const credentialsBody = z.strictObject({ login_id: loginId, password });
export const refreshBody = z.strictObject({ refresh_token: z.string().min(1).max(256) });

export type CredentialsBody = z.infer<typeof credentialsBody>;
export type RefreshBody = z.infer<typeof refreshBody>;
