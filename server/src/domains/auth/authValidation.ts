import { z } from 'zod';

// 대문자가 오면 소문자로 바꿔 받아준다
const loginId = z
  .string()
  .transform((s) => s.toLowerCase())
  .pipe(z.string().regex(/^[a-z0-9_]{4,20}$/, '아이디는 영문 소문자·숫자·_ 4~20자입니다'));

const password = z.string().min(8, '비밀번호는 8~64자입니다').max(64, '비밀번호는 8~64자입니다');

/**
 * 9단계 E1: 기기 신호. install_id는 설치마다 한 번 만드는 UUID, device_hash는 SHA-256 hex(소문자).
 * SystemInfo.deviceUniqueIdentifier가 "n/a"(미지원)이면 device_hash를 생략하고 install_id만 보낸다.
 */
export const deviceBody = z.strictObject({
  install_id: z.uuid(),
  device_hash: z.string().regex(/^[0-9a-f]{64}$/).optional(),
});

export const credentialsBody = z.strictObject({ login_id: loginId, password, device: deviceBody.optional() });
export const refreshBody = z.strictObject({ refresh_token: z.string().min(1).max(256), device: deviceBody.optional() });

export type CredentialsBody = z.infer<typeof credentialsBody>;
export type RefreshBody = z.infer<typeof refreshBody>;

/** 클라이언트가 SteamID를 같이 보내도 받지 않는다(서버가 티켓에서 얻는다). mock 모드 티켓은 mock:<steam_id64>:<nonce> */
export const steamBody = z.strictObject({
  ticket: z
    .string()
    .min(16)
    .max(2048)
    .regex(/^(?:[0-9a-fA-F]+|mock:\d{17}:[A-Za-z0-9]{8,32})$/),
  device: deviceBody.optional(),
});
export type SteamBody = z.infer<typeof steamBody>;
