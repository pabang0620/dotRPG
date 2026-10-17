-- 0045_oauth_login: 카카오·Google 로그인(2026-10-18, Docs/server/OAUTH_LOGIN.md)
-- 대상: PostgreSQL 14 이상. 선행: 0044.
--   1. auth_identities.provider 에 kakao, google 추가(subject = 공급자의 사용자 번호. kakao: 회원번호, google: sub)
--   2. login_events.kind 에 oauth_login 추가
--   3. oauth_sessions: 브라우저 로그인 한 번(시작 -> 콜백 -> 게임의 폴링). state 는 주소에 실리므로 원문, 폴링 비밀은 해시만 둔다

-- ============ UP ============
ALTER TABLE auth_identities DROP CONSTRAINT auth_identities_provider_check;
ALTER TABLE auth_identities ADD CONSTRAINT auth_identities_provider_check CHECK (provider IN ('dev', 'steam', 'kakao', 'google'));
COMMENT ON COLUMN auth_identities.subject IS 'dev: 소문자 아이디, steam: steam_id 문자열, kakao: 회원번호, google: sub';

ALTER TABLE login_events DROP CONSTRAINT login_events_kind_check;
ALTER TABLE login_events ADD CONSTRAINT login_events_kind_check CHECK (kind IN ('register', 'login', 'steam_login', 'oauth_login', 'refresh', 'enter'));

CREATE TABLE oauth_sessions (
  id               BIGSERIAL PRIMARY KEY,
  state            TEXT NOT NULL UNIQUE,
  poll_secret_hash TEXT NOT NULL,
  provider         TEXT NOT NULL CHECK (provider IN ('kakao', 'google')),
  mode             TEXT NOT NULL CHECK (mode IN ('login', 'link')),
  account_id       BIGINT REFERENCES accounts(id),
  status           TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'ready', 'error', 'done')),
  subject          TEXT,
  error_code       TEXT,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at       TIMESTAMPTZ NOT NULL,
  completed_at     TIMESTAMPTZ,
  CONSTRAINT oauth_sessions_link_account CHECK (mode <> 'link' OR account_id IS NOT NULL),
  CONSTRAINT oauth_sessions_ready_subject CHECK (status NOT IN ('ready', 'done') OR subject IS NOT NULL)
);
CREATE INDEX oauth_sessions_expires_idx ON oauth_sessions (expires_at);
COMMENT ON TABLE oauth_sessions IS '브라우저 로그인 한 번(0045). pending -> 콜백 성공 ready / 실패 error -> 게임이 폴링으로 가져가면 done. 10분 뒤 만료';
COMMENT ON COLUMN oauth_sessions.poll_secret_hash IS '게임만 아는 폴링 비밀의 SHA-256. state 만 아는 사람(주소를 본 사람)은 토큰을 가져갈 수 없다';

-- ============ DOWN ============
DROP TABLE IF EXISTS oauth_sessions;
DELETE FROM login_events WHERE kind = 'oauth_login';
ALTER TABLE login_events DROP CONSTRAINT login_events_kind_check;
ALTER TABLE login_events ADD CONSTRAINT login_events_kind_check CHECK (kind IN ('register', 'login', 'steam_login', 'refresh', 'enter'));
DELETE FROM auth_identities WHERE provider IN ('kakao', 'google');
ALTER TABLE auth_identities DROP CONSTRAINT auth_identities_provider_check;
ALTER TABLE auth_identities ADD CONSTRAINT auth_identities_provider_check CHECK (provider IN ('dev', 'steam'));
