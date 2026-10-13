-- 0038_force_rename: 운영정책의 "이름 변경 요청" 제재(2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0037.
--   운영자가 부적절한 이름을 임시 이름("이름변경" + 숫자 4자리)으로 바꾸고 rename_required를 켠다.
--   플레이어는 POST /characters/:uuid/rename 으로 새 이름을 한 번 고르고, 그러면 표시가 꺼진다.

-- ============ UP ============
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rename_required BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rename_note TEXT CHECK (rename_note IS NULL OR char_length(rename_note) <= 200);
COMMENT ON COLUMN characters.rename_required IS '운영자 이름 변경 요청(0038). true면 플레이어가 새 이름을 골라야 한다';
COMMENT ON COLUMN characters.rename_note IS '이름 변경 요청 사유(플레이어에게 보인다, 200자 이하)';

-- ============ DOWN ============
ALTER TABLE characters DROP COLUMN IF EXISTS rename_note;
ALTER TABLE characters DROP COLUMN IF EXISTS rename_required;
