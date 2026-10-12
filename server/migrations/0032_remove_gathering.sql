-- 0032_remove_gathering: 나무·돌 채집과 당근 밭 폐지 (Docs/PLAN_REMOVE_GATHERING.md 2차 정리)
-- 대상: PostgreSQL 14 이상. 선행: 0031_skill_styles.
--   1. 목재(wood)·돌(stone)·당근(carrot) 보유분과 그 원장 행 삭제(시험 데이터. 원장 보존식 I2가 그대로 성립하도록 둘 다 지운다)
--   2. 이 아이템이 걸린 미수령 드롭, 입찰 없는 진행 중 경매 등록, 첨부가 이 아이템뿐인 미수령 우편 정리
--   3. 납품 기록(site_deliveries)과 채집 노드 상태(character_node_state) 테이블 삭제
-- 첨부 목록 우편(mail_attachments), 체결 기록(auction_trades) 같은 append-only 기록은 지우지 않는다.

-- ============ UP ============
DO $$
DECLARE
  n_bid_listings INT;
  n_list_mails INT;
BEGIN
  SELECT count(*) INTO n_bid_listings FROM auction_listings
   WHERE status = 'active' AND bid_count > 0 AND item_base IN ('wood', 'stone', 'carrot');
  SELECT count(DISTINCT mail_id) INTO n_list_mails FROM mail_attachments a JOIN mails m ON m.id = a.mail_id
   WHERE a.kind = 'item' AND a.item_key IN ('wood', 'stone', 'carrot') AND m.claimed_at IS NULL AND m.expired_at IS NULL;
  IF n_bid_listings > 0 OR n_list_mails > 0 THEN
    RAISE NOTICE '0032: 손대지 않은 행 - 입찰 중인 등록 %건, 목록 첨부 미수령 우편 %통', n_bid_listings, n_list_mails;
  END IF;
END $$;

DELETE FROM drops WHERE item_key IN ('wood', 'stone', 'carrot') AND claimed_at IS NULL;

UPDATE auction_listings SET status = 'cancelled', closed_at = now(), version = version + 1
 WHERE status = 'active' AND bid_count = 0 AND item_base IN ('wood', 'stone', 'carrot');

UPDATE mails SET expired_at = now()
 WHERE item_key IN ('wood', 'stone', 'carrot') AND gold = 0 AND claimed_at IS NULL AND expired_at IS NULL;

DELETE FROM character_items WHERE item_key IN ('wood', 'stone', 'carrot');

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE item_key IN ('wood', 'stone', 'carrot');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;

DELETE FROM auction_price_daily WHERE item_key IN ('wood', 'stone', 'carrot');

DROP TABLE site_deliveries;
DROP TABLE character_node_state;

-- ============ DOWN ============
-- 삭제한 보유분·원장 행은 되돌리지 않는다(시험 데이터). 테이블만 0002_economy와 같게 다시 만든다.
CREATE TABLE character_node_state (
  character_id     BIGINT NOT NULL REFERENCES characters(id),
  node_id          TEXT NOT NULL,
  map_id           TEXT NOT NULL,
  last_gathered_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (character_id, node_id)
);
COMMENT ON TABLE  character_node_state IS '캐릭터별 채집 노드(나무·바위·당근밭) 마지막 채집 시각. 재생 시간 판정의 정본. 한 번도 안 캔 노드는 행이 없다(= 언제든 가능)';
COMMENT ON COLUMN character_node_state.node_id IS '"맵id:x:y" (상자 id와 같은 규칙, 서버 데이터 maps.json의 nodes[].id)';
COMMENT ON COLUMN character_node_state.map_id IS 'node_id의 맵 부분(맵 입장 때 쿨다운 목록을 한 번에 읽기 위해 따로 둔다)';
COMMENT ON COLUMN character_node_state.last_gathered_at IS '마지막으로 받아들인 채집 시각(서버 시계). 재생 시간 이후에만 다시 채집 가능';
CREATE INDEX character_node_state_map ON character_node_state (character_id, map_id, last_gathered_at);

CREATE TABLE site_deliveries (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  site_id      TEXT NOT NULL,
  item_key     TEXT NOT NULL,
  delivered    INT NOT NULL DEFAULT 0 CHECK (delivered >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, site_id, item_key)
);
COMMENT ON TABLE  site_deliveries IS '납품처(공방 공사장)에 낸 재료 누적(QuestProgress.woodDelivered/stoneDelivered 대체). 필요량은 서버 데이터 deliverySites';
COMMENT ON COLUMN site_deliveries.site_id IS '납품처 id (예: workshop)';
COMMENT ON COLUMN site_deliveries.delivered IS '지금까지 낸 수량. 필요량을 넘지 않는다(서버가 min으로 자른다)';
