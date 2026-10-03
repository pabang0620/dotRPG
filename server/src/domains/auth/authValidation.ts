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

/** 클라이언트가 SteamID를 같이 보내도 받지 않는다(서버가 티켓에서 얻는다). mock 모드 티켓은 mock:<steam_id64>:<nonce> */
export const steamBody = z.strictObject({
  ticket: z
    .string()
    .min(16)
    .max(4096)
    .regex(/^(?:[0-9a-fA-F]+|mock:\d{17}:[A-Za-z0-9]{8,32})$/),
});
export type SteamBody = z.infer<typeof steamBody>;
