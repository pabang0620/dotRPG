import type { Response } from 'express';
import { replayResponse, successResponse } from '../../utils/response';
import type { ActionResult } from './audit';

/** 변경 작업의 응답. 같은 request_id의 재전송이면 저장된 응답을 Idempotent-Replay: true로 돌려준다 */
export const sendAction = (res: Response, r: ActionResult): Response => {
  if (r.replay) return replayResponse(res, r.body.status, { success: true, message: r.body.message, data: r.body.data });
  return successResponse(res, r.body.data, r.body.message, r.body.status);
};
