-- 0037_policy_consents: 이용약관·개인정보처리방침 동의와 만 14세 이상 확인 기록(2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0036.
--   필요한 버전은 코드(src/domains/policies/policyVersions.ts)에 있다. 문서가 바뀌면 버전을 올리고 다시 동의를 받는다.
--   동의 기록은 계정 껍데기와 함께 남는다(탈퇴 익명화 대상이 아니다: 언제 무엇에 동의했는지는 분쟁 대응 근거다).

-- ============ UP ============
CREATE TABLE IF NOT EXISTS account_consents (
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  doc_id     TEXT NOT NULL CHECK (doc_id IN ('terms', 'privacy', 'age14')),
  version    TEXT NOT NULL,
  agreed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, doc_id, version)
);
COMMENT ON TABLE  account_consents IS '정책 동의 기록(terms 이용약관, privacy 개인정보 수집·이용, age14 만 14세 이상 확인). 계정 껍데기와 함께 보관한다';
COMMENT ON COLUMN account_consents.version IS '동의한 문서 버전. 필요한 버전은 policyVersions.ts REQUIRED';

-- ============ DOWN ============
DROP TABLE IF EXISTS account_consents;
