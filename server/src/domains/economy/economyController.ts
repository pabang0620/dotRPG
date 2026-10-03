import type { Response } from 'express';
import { replayResponse, successResponse } from '../../utils/response';
import type { StoredResult } from './economyService';

/** 멱등성 재전송이면 처음 응답을 그대로, 아니면 새 응답을 보낸다 */
export function sendStored(res: Response, r: StoredResult): void {
  if (r.replay) replayResponse(res, r.status, r.body);
  else successResponse(res, r.body.data, r.body.message, r.status);
}
