-- 0003_dungeon_solo (선택): 3단계에 솔로 요일 던전을 넣는 경우의 테이블
-- 대상: PostgreSQL 14 이상. 선행: 0002_economy. 설계: Docs/server/phase3_api.md 9절 (제안 - 사용자 확정 전)
-- 채택하지 않으면 이 파일과 phase3_api.md 9절, schema.sql의 "3단계-b" 구역을 지운다. 0002는 이 파일에 의존하지 않는다.
--
-- 한 행 = 한 캐릭터의 한 번 도전. 4단계 파티 던전은 같은 표에 방 단위 식별자(party_session_id)를 더하고,
-- 멤버마다 자기 행을 가진다(보상은 멤버별이다).

-- ============ UP ============

-- ---------- 1. 원장·이상 기록 사유 확장 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card'));

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card'));

ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result'));

-- ---------- 2. 던전 도전 ----------

CREATE TABLE dungeon_runs (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id    TEXT NOT NULL,
  difficulty    SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  party_size    SMALLINT NOT NULL DEFAULT 1 CHECK (party_size BETWEEN 1 AND 4),
  state         TEXT NOT NULL DEFAULT 'playing'
                CHECK (state IN ('playing', 'cleared', 'failed', 'abandoned', 'held')),
  reset_day     TIMESTAMPTZ NOT NULL,
  started_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  ended_at      TIMESTAMPTZ,
  room_index    SMALLINT NOT NULL DEFAULT 0 CHECK (room_index >= 0),
  room_kills    JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(room_kills) = 'object'),
  stats         JSONB,
  rank          SMALLINT CHECK (rank BETWEEN 0 AND 8),
  score         JSONB,
  xp_granted    INT CHECK (xp_granted >= 0),
  hold_reason   TEXT,
  cards         JSONB CHECK (cards IS NULL OR jsonb_typeof(cards) = 'array'),
  card_picked   SMALLINT CHECK (card_picked >= 0),
  card_picked_at TIMESTAMPTZ,
  CONSTRAINT dungeon_runs_cleared_chk CHECK (state <> 'cleared' OR (rank IS NOT NULL AND cards IS NOT NULL AND ended_at IS NOT NULL)),
  CONSTRAINT dungeon_runs_ended_chk CHECK ((state = 'playing') = (ended_at IS NULL)),
  CONSTRAINT dungeon_runs_card_chk CHECK ((card_picked IS NULL) = (card_picked_at IS NULL))
);
COMMENT ON TABLE  dungeon_runs IS '솔로 요일 던전 도전 기록. 입장 횟수(일일)·클리어·최고 랭크·카드가 모두 이 표에서 나온다(별도 집계 표 없음)';
COMMENT ON COLUMN dungeon_runs.uuid IS '외부 노출 id(run id). 처치 보고·결과·카드 선택 요청이 이 값으로 온다';
COMMENT ON COLUMN dungeon_runs.dungeon_id IS 'dungeons.json의 요일 던전 id(레이드는 4단계 이후)';
COMMENT ON COLUMN dungeon_runs.difficulty IS 'DungeonDifficulty 0 일반 1 모험 2 왕 3 영웅';
COMMENT ON COLUMN dungeon_runs.party_size IS '3단계는 항상 1(AI 용병 없음). 4단계에서 파티 인원';
COMMENT ON COLUMN dungeon_runs.state IS 'playing 진행 / cleared 클리어(카드 있음) / failed 실패 보고 / abandoned 방치 만료 / held 결과가 불가능해 보상 보류(관리자 검토)';
COMMENT ON COLUMN dungeon_runs.reset_day IS '입장 시각이 속한 일일 초기화 구간의 시작(서버 KST 06:00 함수를 UTC로 바꾼 값). 오늘 입장 수 집계 키';
COMMENT ON COLUMN dungeon_runs.room_index IS '서버가 인정한 현재 방. 처치 보고가 다음 방 몬스터를 가리키면 앞 방이 충분히 정리됐을 때만 올라간다';
COMMENT ON COLUMN dungeon_runs.room_kills IS '{"방번호:몬스터id": 받아들인 처치 수}. 방 구성(그룹 count)을 넘는 보고를 막고 클리어 검증에 쓴다';
COMMENT ON COLUMN dungeon_runs.stats IS '클라이언트가 보고한 사실 {elapsed_ms, hits_taken, max_combo, revives_used} 와 서버가 자른 값. 서버가 검증하지 못하는 항목은 랭크 점수에만 쓰인다';
COMMENT ON COLUMN dungeon_runs.rank IS '서버가 계산한 랭크 0 SSS ~ 8 F';
COMMENT ON COLUMN dungeon_runs.score IS '점수 구성 {time, hits, kills, combo, revive_penalty, total}';
COMMENT ON COLUMN dungeon_runs.xp_granted IS '클리어 경험치(난이도·랭크 보정 후). 처치별 경험치는 xp_ledger(kill)';
COMMENT ON COLUMN dungeon_runs.hold_reason IS 'held일 때 사유 코드(예: TOO_FAST, ROOMS_NOT_CLEARED). 클라이언트에는 알리지 않는다';
COMMENT ON COLUMN dungeon_runs.cards IS '서버가 굴린 카드 4장 [{item_key,count}]. 선택 전에는 클라이언트에 내용을 주지 않는다';
COMMENT ON COLUMN dungeon_runs.card_picked IS '고른 카드 번호(0부터). NULL = 아직 안 골랐다';
-- 한 캐릭터는 동시에 한 판만 진행(동시 입장 요청이 둘 다 성공하는 것을 DB가 막는다)
CREATE UNIQUE INDEX dungeon_runs_one_playing ON dungeon_runs (character_id) WHERE state = 'playing';
-- 오늘 입장 수 집계: WHERE character_id = $1 AND reset_day = $2 (모든 상태 포함, 입장하면 횟수를 쓴다)
CREATE INDEX dungeon_runs_char_day ON dungeon_runs (character_id, reset_day);
-- 난이도 잠금 해제, 최고 랭크, 퀘스트 "요일 던전 클리어 n회" 검증: 클리어 행만 읽는다
CREATE INDEX dungeon_runs_clears ON dungeon_runs (character_id, dungeon_id, difficulty) WHERE state = 'cleared';

-- ---------- 3. 처치 기록에 던전 연결 ----------

ALTER TABLE kill_log ADD COLUMN run_id BIGINT REFERENCES dungeon_runs(id);
ALTER TABLE kill_log ADD COLUMN room_index SMALLINT CHECK (room_index >= 0);
ALTER TABLE kill_log ADD CONSTRAINT kill_log_run_chk CHECK ((run_id IS NULL) = (room_index IS NULL) AND ((context = 'dungeon') = (run_id IS NOT NULL)));
COMMENT ON COLUMN kill_log.run_id IS '던전 처치일 때 dungeon_runs.id. 필드·연출 처치는 NULL';
COMMENT ON COLUMN kill_log.room_index IS '던전 방 번호(run_id와 함께)';
-- 한 판의 누적 처치·화력 예산 계산
CREATE INDEX kill_log_run_idx ON kill_log (run_id) WHERE run_id IS NOT NULL;

-- ============ DOWN ============
-- 개발 DB 전용. 던전 사유의 원장 행을 지우므로 잔액과 원장이 어긋난다. 운영에서는 실행하지 않는다.

DROP INDEX IF EXISTS kill_log_run_idx;
ALTER TABLE kill_log DROP CONSTRAINT IF EXISTS kill_log_run_chk;
ALTER TABLE kill_log DROP COLUMN IF EXISTS room_index;
ALTER TABLE kill_log DROP COLUMN IF EXISTS run_id;
DELETE FROM kill_log WHERE context = 'dungeon';

DROP TABLE IF EXISTS dungeon_runs;

DELETE FROM anomaly_log WHERE kind IN ('dungeon_enter', 'dungeon_result');
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'drop_foreign', 'quest_denied', 'chest_unknown'));

ALTER TABLE xp_ledger DISABLE TRIGGER xp_ledger_append_only;
DELETE FROM xp_ledger WHERE reason = 'dungeon_clear';
ALTER TABLE xp_ledger ENABLE TRIGGER xp_ledger_append_only;
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward'));

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason = 'dungeon_card';
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item'));

ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason = 'dungeon_card';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost'));
