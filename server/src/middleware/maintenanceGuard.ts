// 점검 단계별 요청 차단(phase7_ops.md 4.2). 버전 검사(426)보다 앞에 둔다: 점검 안내가 업데이트 안내보다 먼저다.
import type { RequestHandler } from 'express';
import { AppError } from '../utils/AppError';
import { getNow } from '../utils/clock';
import { formatKstHm } from '../utils/resetBoundaries';
import { maintPhase, maintView, getMaintenance } from '../ops/maintenanceState';

/** 점검 중에도 허용하는 경로(헬스, 메타, 로그아웃) */
const EXEMPT = /^\/(health(\/live|\/ready)?|meta)$/;
const LOGOUT = '/auth/logout';
/** 새 로그인 경로 */
const LOGIN_PATHS = new Set(['/auth/dev/login', '/auth/dev/register', '/auth/steam']);
/** "새 판" 경로 한 곳 정의: 던전 입장, 파티 출발, 자동 매칭 대기·AI 채우기(POST만) */
const NEW_RUN = /^\/characters\/[^/]+\/(dungeon-runs|party\/start|match\/queue|match\/fill-ai)$/;

export const maintenanceGuard: RequestHandler = (req, _res, next) => {
  const phase = maintPhase();
  if (phase === 'none' || phase === 'scheduled') return next();
  if (EXEMPT.test(req.path) || req.path === LOGOUT) return next();
  const isPost = req.method === 'POST';
  if (phase === 'pre_block') {
    const blocked = isPost && (LOGIN_PATHS.has(req.path) || NEW_RUN.test(req.path));
    if (!blocked) return next();
  }
  // pre_block에서 /auth/refresh(접속 중인 사람의 토큰 갱신)는 위 blocked 판정에 걸리지 않아 지나간다. active에서는 모두 막힌다
  const w = getMaintenance();
  const view = maintView();
  if (!w || !view) return next();
  const now = getNow();
  const retry = Math.max(1, Math.min(300, Math.ceil((w.endsAt.getTime() - now.getTime()) / 1000)));
  const pending = phase === 'pre_block';
  const message = pending
    ? `곧 서버 점검이 시작됩니다. ${formatKstHm(w.startsAt)}(KST) 점검 전까지 새 던전 입장과 새 로그인이 제한됩니다.`
    : `서버 점검 중입니다. ${formatKstHm(w.endsAt)}(KST)에 종료될 예정입니다.`;
  next(
    new AppError(503, message, pending ? 'MAINTENANCE_PENDING' : 'MAINTENANCE', {
      starts_at: view.starts_at,
      ends_at: view.ends_at,
      notice: view.notice,
      retry_after_sec: retry,
    }),
  );
};
