-- 0030_client_errors: 클라이언트(PC·폰) 예외 보고 (Docs/server/phase15_load_and_logging.md G8)
-- 대상: PostgreSQL 14 이상. 선행: 0029_daily_quests.
--   1. client_errors: 계정별 예외 보고. POST /client-errors 가 넣고, 정리 잡(purge-hourly)이 CLIENT_ERROR_RETENTION_DAYS(14일) 뒤 지운다

-- ============ UP ============
CREATE TABLE client_errors (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  version      TEXT NOT NULL,
  platform     TEXT NOT NULL,
  message      TEXT NOT NULL,
  stack        TEXT NOT NULL DEFAULT '',
  scene        TEXT,
  client_at    TIMESTAMPTZ,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  client_errors IS '클라이언트 예외 보고(진단용). 계정당 분당 RATE_CLIENT_ERROR_ACCOUNT_MAX건, 본문 4KB 이하. 보관 CLIENT_ERROR_RETENTION_DAYS(14일)';
COMMENT ON COLUMN client_errors.version IS '클라이언트 앱 버전(Application.version)';
COMMENT ON COLUMN client_errors.platform IS '플랫폼(WindowsPlayer, Android 등)';
COMMENT ON COLUMN client_errors.message IS '예외 메시지 앞 500자';
COMMENT ON COLUMN client_errors.stack IS '스택 앞 2000자';
COMMENT ON COLUMN client_errors.scene IS '보고 당시 맵·화면(있으면)';
COMMENT ON COLUMN client_errors.client_at IS '클라이언트가 적은 발생 시각(참고용, 신뢰하지 않는다)';
CREATE INDEX client_errors_created_idx ON client_errors (created_at);
CREATE INDEX client_errors_account_idx ON client_errors (account_id, created_at);

-- ============ DOWN ============
DROP TABLE IF EXISTS client_errors;
