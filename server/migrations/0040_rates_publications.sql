-- 0040_rates_publications: 확률표 공개 기록(2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0039.
--   이용약관 제14조의2 3항: 각 시각에 적용된 확률표(버전과 내용의 지문)를 결제 기록과 같은 기간 보관한다.
--   서버가 기동할 때와 1시간마다, 지금 공개 중인 확률 문서(한국어판, 생성 시각 줄 제외)의 sha256 지문을 upsert 한다.
--   같은 지문이면 last_seen_at 만 갱신한다. 계정과 무관한 표라 탈퇴 파기·purge 대상이 아니다.

-- ============ UP ============
CREATE TABLE rates_publications (
  id            BIGSERIAL PRIMARY KEY,
  fingerprint   TEXT NOT NULL UNIQUE CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
  versions      JSONB NOT NULL CHECK (jsonb_typeof(versions) = 'object'),
  content       TEXT NOT NULL,
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  rates_publications IS '확률표 공개 기록(0040). 지문(fingerprint)별로 처음·마지막으로 공개 중이던 시각을 남긴다. 지우지 않는다';
COMMENT ON COLUMN rates_publications.fingerprint IS '공개 문서(생성 시각 줄 제외) 내용의 sha256 hex';
COMMENT ON COLUMN rates_publications.versions IS '각 표의 버전: cosmetic(외형·장비 뽑기 RATES_VERSION), sealed(봉인된 상자), raid(레이드 상점)';
COMMENT ON COLUMN rates_publications.content IS '공개 Markdown 한국어판 전문(생성 시각 줄 제외)';

-- ============ DOWN ============
DROP TABLE IF EXISTS rates_publications;
