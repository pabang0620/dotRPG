// 15단계(부하 방지·로그 보강) 환경변수. 값의 뜻은 Docs/server/phase15_load_and_logging.md.
import { z } from 'zod';

const posInt = (def: number) => z.coerce.number().int().positive().default(def);

export const loadShape = {
  /** 잦은 경로(처치·줍기·채집·상태 저장)의 request_log 보관 시간. 재전송 확인은 몇 분이면 충분하다 */
  REQUEST_LOG_HOT_RETENTION_HOURS: posInt(24),
  /** 경제 경로 캐릭터 단위 분당 한도(IP 한도와 별개) */
  RATE_ECONOMY_CHAR_MAX: posInt(300),
  /** 클라이언트 오류 보고: 계정 단위 분당 건수, 보관 일수 */
  RATE_CLIENT_ERROR_ACCOUNT_MAX: posInt(5),
  CLIENT_ERROR_RETENTION_DAYS: posInt(14),
  /** Node HTTP 서버 시간 제한 */
  HTTP_REQUEST_TIMEOUT_MS: posInt(30_000),
  HTTP_HEADERS_TIMEOUT_MS: posInt(15_000),
  HTTP_KEEPALIVE_TIMEOUT_MS: posInt(65_000),
  /** 느린 요청·쿼리 경고 기준 */
  SLOW_REQUEST_MS: posInt(1000),
  SLOW_QUERY_MS: posInt(500),
  /** WebSocket: 맵 변경(presence.set, 마을 진입)의 소켓당 최소 간격. 프레임 수 상한은 기존 WS_FRAMES_PER_SEC */
  WS_MAP_CHANGE_MIN_MS: posInt(1000),
};

export type LoadRaw = z.output<z.ZodObject<typeof loadShape>>;

export interface LoadConfig {
  requestLogHotHours: number;
  economyCharPerMin: number;
  clientErrorPerMin: number;
  clientErrorDays: number;
  http: { requestTimeoutMs: number; headersTimeoutMs: number; keepAliveTimeoutMs: number };
  slowRequestMs: number;
  slowQueryMs: number;
  ws: { mapChangeMinMs: number };
}

export function buildLoad(e: LoadRaw): LoadConfig {
  if (e.HTTP_HEADERS_TIMEOUT_MS > e.HTTP_REQUEST_TIMEOUT_MS) {
    throw new Error('환경변수 검증 실패: HTTP_HEADERS_TIMEOUT_MS 는 HTTP_REQUEST_TIMEOUT_MS 이하여야 합니다');
  }
  return {
    requestLogHotHours: e.REQUEST_LOG_HOT_RETENTION_HOURS,
    economyCharPerMin: e.RATE_ECONOMY_CHAR_MAX,
    clientErrorPerMin: e.RATE_CLIENT_ERROR_ACCOUNT_MAX,
    clientErrorDays: e.CLIENT_ERROR_RETENTION_DAYS,
    http: { requestTimeoutMs: e.HTTP_REQUEST_TIMEOUT_MS, headersTimeoutMs: e.HTTP_HEADERS_TIMEOUT_MS, keepAliveTimeoutMs: e.HTTP_KEEPALIVE_TIMEOUT_MS },
    slowRequestMs: e.SLOW_REQUEST_MS,
    slowQueryMs: e.SLOW_QUERY_MS,
    ws: { mapChangeMinMs: e.WS_MAP_CHANGE_MIN_MS },
  };
}
