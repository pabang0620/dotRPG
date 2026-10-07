-- 0022_prev_login: 직전 로그인 시각 (Docs/server/phase10_sweep_mail.md 16절, 캠페인 조건 last_login_before)
-- 대상: PostgreSQL 14 이상. 선행: 0021_sweep_and_mail.
--
-- 로그인은 last_login_at을 지금으로 덮어쓰므로, "오래 쉬다 돌아온 계정"을 고르려면 그 직전 값이 필요하다.
-- 로그인 때 prev_login_at = (바뀌기 전) last_login_at 으로 함께 갱신한다. 기존 계정은 NULL에서 시작한다.

-- ============ UP ============
ALTER TABLE accounts ADD COLUMN prev_login_at TIMESTAMPTZ;
COMMENT ON COLUMN accounts.prev_login_at IS '바로 앞 로그인 성공 시각. 휴면 복귀 판정(캠페인 last_login_before)에 쓴다. NULL = 첫 로그인 또는 0022 이전';

-- ============ DOWN ============
-- ALTER TABLE accounts DROP COLUMN prev_login_at;
