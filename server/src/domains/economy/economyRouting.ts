import type { Request } from 'express';
import { getAccount } from '../../middleware/authMiddleware';

/** 캐릭터별 속도 제한 키. 남의 uuid로 남의 한도를 소진시키지 못하게 계정 id를 함께 쓴다 */
export const charKey = (req: Request, locals: Record<string, unknown>): string =>
  `${getAccount(locals).id}:${String(req.params.uuid)}`;
