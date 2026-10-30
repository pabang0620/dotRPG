-- 0062_revive_log_item_key(2026-10-30): 부활 코인 아이템 사용 줄(context = item)은 아이템 사용 request_id를 그대로 쓴다.
-- /revive 와 같은 request_id를 쓰면 UNIQUE(character_id, request_id)에 걸려 500이 났다. 멱등 키를 (character_id, request_id, item 여부)로 나눈다.
-- 새 테이블·열 없음. 선행: 0024, 0056. 대상: PostgreSQL 14 이상.
-- ============ UP ============
ALTER TABLE revive_log DROP CONSTRAINT IF EXISTS revive_log_character_id_request_id_key;
CREATE UNIQUE INDEX revive_log_request_uq ON revive_log (character_id, request_id, ((context = 'item')));
-- ============ DOWN ============
-- DROP INDEX IF EXISTS revive_log_request_uq;
-- ALTER TABLE revive_log ADD CONSTRAINT revive_log_character_id_request_id_key UNIQUE (character_id, request_id); (같은 request_id의 item 줄이 있으면 먼저 정리)
