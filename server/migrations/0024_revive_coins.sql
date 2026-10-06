-- 0024_revive_coins: 부활 코인(Docs/server/phase12_revive_coins.md 2절)
-- 대상: PostgreSQL 14 이상. 선행: 0023_payments.
--
-- Lv.11 이상의 "그 자리에서 부활"은 코인 1개를 쓴다. 지급(게임 하루 1개, 상한 5)은 조회·사용 시점에 계산한다(lazy).
-- 기존 캐릭터도 DEFAULT 1로 코인 1개에서 시작한다.

-- ============ UP ============
ALTER TABLE characters
  ADD COLUMN revive_coins SMALLINT NOT NULL DEFAULT 1 CHECK (revive_coins BETWEEN 0 AND 5),
  ADD COLUMN revive_coin_day DATE NULL;
COMMENT ON COLUMN characters.revive_coins IS '부활 코인 보유 수(0~5). 일일 지급은 조회·사용 시점에 반영한다';
COMMENT ON COLUMN characters.revive_coin_day IS '마지막으로 일일 지급을 반영한 게임 일자(06:00 KST 기준 날짜). NULL = 아직 없음(처음 조회한 날부터 시작)';

CREATE TABLE revive_log (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  request_id   UUID NOT NULL,
  context      TEXT NOT NULL CHECK (context IN ('field', 'dungeon')),
  free         BOOLEAN NOT NULL,
  coins_after  SMALLINT NOT NULL,
  level        INT NOT NULL,
  map_id       TEXT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (character_id, request_id)
);
COMMENT ON TABLE  revive_log IS '부활 기록(멱등 키와 남용 지표). 같은 (character_id, request_id)는 한 번만 처리한다';
COMMENT ON COLUMN revive_log.free IS 'true면 Lv.10 이하 무료 부활(코인 소모 없음)';
CREATE INDEX revive_log_character_idx ON revive_log (character_id, created_at DESC);

-- ============ DOWN ============
-- DROP TABLE IF EXISTS revive_log;
-- ALTER TABLE characters DROP COLUMN IF EXISTS revive_coin_day, DROP COLUMN IF EXISTS revive_coins;
