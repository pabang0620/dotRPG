-- 0042_dungeon_entry_resets: 요일 던전 초기화권(2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0041.
--   요일 던전 초기화권(ticket_dungeon_reset)을 쓰면 그 캐릭터의 오늘 요일 던전 입장 횟수가 0으로 돌아간다.
--   쓴 시각을 여기 남기고, 입장 횟수는 max(오늘 06:00, 마지막 초기화 시각) 뒤에 시작한 판·소탕만 센다.
--   아이템 소모는 같은 트랜잭션의 item_ledger(use_item)에 남는다. 일반 던전(always)과 레이드는 원래 횟수가 없다.

-- ============ UP ============
CREATE TABLE dungeon_entry_resets (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  reset_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE dungeon_entry_resets IS '요일 던전 초기화권 사용 기록(0042). 입장 횟수는 마지막 초기화 뒤부터 센다';
CREATE INDEX dungeon_entry_resets_char_idx ON dungeon_entry_resets (character_id, reset_at DESC);

-- ============ DOWN ============
DROP TABLE IF EXISTS dungeon_entry_resets;
