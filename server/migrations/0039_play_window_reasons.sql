-- 0039_play_window_reasons: 게임시간 선택제(청소년 보호 정책 제4조)와 제재 사유 코드 확장(2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0038.
--   accounts.play_window_start/end: 법정대리인 요청으로 운영자가 정한 이용 가능 시간대(KST 분 0~1439). 둘 다 NULL이면 제한 없음.
--   start > end 이면 자정을 넘는 구간(22:00~07:00). start = end 는 허용하지 않는다.
--   account_sanctions.reason_code: sexual, hate, impersonation, rmt, exploit, name 추가.

-- ============ UP ============
ALTER TABLE accounts ADD COLUMN IF NOT EXISTS play_window_start SMALLINT;
ALTER TABLE accounts ADD COLUMN IF NOT EXISTS play_window_end SMALLINT;
ALTER TABLE accounts ADD CONSTRAINT accounts_play_window_chk CHECK (
  (play_window_start IS NULL AND play_window_end IS NULL)
  OR (play_window_start BETWEEN 0 AND 1439 AND play_window_end BETWEEN 0 AND 1439 AND play_window_start <> play_window_end)
);
COMMENT ON COLUMN accounts.play_window_start IS '게임시간 선택제(0039): 이용 가능 시작, KST 자정부터의 분(0~1439). NULL이면 제한 없음';
COMMENT ON COLUMN accounts.play_window_end IS '게임시간 선택제(0039): 이용 가능 끝(이 분은 제외), KST 분. start > end 이면 자정을 넘는 구간';

ALTER TABLE account_sanctions DROP CONSTRAINT IF EXISTS account_sanctions_reason_code_check;
ALTER TABLE account_sanctions ADD CONSTRAINT account_sanctions_reason_code_check CHECK (reason_code IN (
  'abuse', 'spam', 'scam_ad', 'cheat', 'other', 'filter_strikes', 'repeat_spam',
  'sexual', 'hate', 'impersonation', 'rmt', 'exploit', 'name'));

-- ============ DOWN ============
ALTER TABLE account_sanctions DROP CONSTRAINT IF EXISTS account_sanctions_reason_code_check;
ALTER TABLE account_sanctions ADD CONSTRAINT account_sanctions_reason_code_check CHECK (reason_code IN ('abuse', 'spam', 'scam_ad', 'cheat', 'other', 'filter_strikes', 'repeat_spam'));
ALTER TABLE accounts DROP CONSTRAINT IF EXISTS accounts_play_window_chk;
ALTER TABLE accounts DROP COLUMN IF EXISTS play_window_end;
ALTER TABLE accounts DROP COLUMN IF EXISTS play_window_start;
