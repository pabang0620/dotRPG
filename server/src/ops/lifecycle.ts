// 프로세스 수명 상태: 종료 중 플래그, 진행 중 요청 수, 데이터 로드 여부(헬스 체크와 정상 종료가 쓴다)
import { randomUUID } from 'node:crypto';
import type { RequestHandler } from 'express';
import { AppError } from '../utils/AppError';
import { metrics, routeKey } from './metrics';

let shuttingDown = false;
let inFlight = 0;
/** unused: WebSocket을 쓰지 않는 구성(테스트) / pending: 붙이는 중 / attached: 연결됨 */
let wsState: 'unused' | 'pending' | 'attached' = 'unused';

export const isShuttingDown = (): boolean => shuttingDown;
export const beginShutdown = (): void => {
  shuttingDown = true;
};
/** 테스트 전용: 종료 상태를 되돌린다 */
export const resetLifecycle = (): void => {
  shuttingDown = false;
  inFlight = 0;
};
export const inFlightCount = (): number => inFlight;
export const setWsState = (v: 'unused' | 'pending' | 'attached'): void => {
  wsState = v;
};
export const isWsReady = (): boolean => wsState !== 'pending';

const ALWAYS_OK = /^\/(health(\/live|\/ready)?|meta)$/;

/** req_id(X-Request-Id 또는 생성), 진행 중 요청 카운터, 요청 지표. 종료 중이면 새 요청을 503 SHUTTING_DOWN으로 거절한다 */
export const lifecycleGuard: RequestHandler = (req, res, next) => {
  const incoming = req.header('x-request-id');
  const reqId = incoming && /^[A-Za-z0-9_.-]{1,64}$/.test(incoming) ? incoming : randomUUID();
  res.locals.reqId = reqId;
  res.setHeader('X-Request-Id', reqId);
  if (shuttingDown && !ALWAYS_OK.test(req.path)) {
    next(new AppError(503, '서버를 종료하는 중입니다. 잠시 후 다시 시도해 주세요.', 'SHUTTING_DOWN', { retry_after_sec: 5 }));
    return;
  }
  inFlight++;
  const start = Date.now();
  let done = false;
  const finish = (): void => {
    if (done) return;
    done = true;
    inFlight--;
    metrics.recordRequest(res.statusCode, Date.now() - start, res.locals.errCode as string | undefined, routeKey(req.method, req.path));
  };
  res.on('finish', finish);
  res.on('close', finish);
  next();
};

/** 진행 중인 요청이 0이 되거나 제한 시간이 지날 때까지 기다린다 */
export async function waitForInFlight(maxMs: number): Promise<boolean> {
  const end = Date.now() + maxMs;
  while (inFlight > 0 && Date.now() < end) await new Promise((r) => setTimeout(r, 50));
  return inFlight === 0;
}
