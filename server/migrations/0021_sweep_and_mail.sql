-- 0021_sweep_and_mail: 던전 클리어권(소탕)과 운영 우편 캠페인 (Docs/server/phase10_sweep_mail.md)
-- 대상: PostgreSQL 14 이상. 선행: 0020_anti_abuse.
--
-- 이 마이그레이션이 하는 일
--   1. 클리어권 지갑(sweep_ticket_lots)과 추가 전용 원장(sweep_ticket_ledger).
--   2. 소탕 기록(dungeon_sweeps). dungeon_runs와 섞지 않는다(해금·업적이 새지 않게).
--   3. 계정 주간 카운터(account_week_counters): 클리어권 주 구매 수, 주간 직접 클리어 수, 주간 보상 수령.
--   4. 운영 우편 캠페인(mail_campaigns, mail_campaign_attachments, mail_campaign_deliveries).
--   5. 우편 확장(mails 제목·본문·캠페인·첨부 수, mail_attachments). 기존 우편 열은 그대로.
--   6. 원장 사유, 이상 기록 종류, 관리자 감사 대상 확장. 계정 최고 레벨 조회용 인덱스.

-- ============ UP ============

-- ---------- 1. 클리어권 지갑 ----------

CREATE TABLE sweep_ticket_lots (
  id         BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  kind       TEXT NOT NULL CHECK (kind IN ('normal', 'event')),
  granted    INT NOT NULL CHECK (granted > 0),
  remaining  INT NOT NULL CHECK (remaining >= 0),
  expires_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT sweep_lots_expiry_chk    CHECK ((kind = 'event') = (expires_at IS NOT NULL)),
  CONSTRAINT sweep_lots_remaining_chk CHECK (remaining <= granted)
);
COMMENT ON TABLE  sweep_ticket_lots IS '던전 클리어권 지갑(계정 소유). normal은 계정당 1행(누적), event는 지급(우편 수령)마다 1행. 가방·창고·경매에 들어가지 않는다. 외부에 id를 노출하지 않는다(잔량과 만료 시각만 응답에 나간다)';
COMMENT ON COLUMN sweep_ticket_lots.granted    IS 'normal은 누적 지급 수, event는 그 로트의 지급 수. remaining <= granted';
COMMENT ON COLUMN sweep_ticket_lots.remaining  IS '남은 장수. 소모는 UPDATE ... WHERE remaining >= 1로만. 변경은 sweep_ticket_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN sweep_ticket_lots.expires_at IS 'event만 값이 있다(수령 시각 + 서버 데이터 eventTicketDays일). normal은 NULL(기한 없음). 지난 로트는 조회·소모에서 제외하고 작업이 remaining을 0으로 만든다';
-- 계정당 일반 로트는 하나 (추가는 ON CONFLICT DO UPDATE)
CREATE UNIQUE INDEX sweep_lots_normal_uq ON sweep_ticket_lots (account_id) WHERE kind = 'normal';
-- 소탕 때 쓸 로트 조회: 계정의 남은 로트를 만료 가까운 순(일반은 NULLS LAST)으로
CREATE INDEX sweep_lots_live ON sweep_ticket_lots (account_id, expires_at) WHERE remaining > 0;
-- 만료 작업: 기한이 지났고 남은 event 로트
CREATE INDEX sweep_lots_expiring ON sweep_ticket_lots (expires_at) WHERE kind = 'event' AND remaining > 0;

CREATE TABLE sweep_ticket_ledger (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  lot_id        BIGINT NOT NULL REFERENCES sweep_ticket_lots(id),
  character_id  BIGINT REFERENCES characters(id),
  delta         INT NOT NULL CHECK (delta <> 0),
  balance_after INT NOT NULL CHECK (balance_after >= 0),
  reason        TEXT NOT NULL CHECK (reason IN ('shop_buy', 'weekly_activity', 'campaign_claim', 'sweep_use', 'expire')),
  ref           TEXT,
  request_id    UUID,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT sweep_ledger_sign_chk CHECK ((reason IN ('sweep_use', 'expire')) = (delta < 0))
);
COMMENT ON TABLE  sweep_ticket_ledger IS '클리어권 변동 원장(추가만, 트리거가 UPDATE/DELETE 차단). 로트별 SUM(delta) = sweep_ticket_lots.remaining(만료 작업 지연 구간 제외)';
COMMENT ON COLUMN sweep_ticket_ledger.character_id IS '요청한 캐릭터. 만료 작업은 NULL';
COMMENT ON COLUMN sweep_ticket_ledger.balance_after IS '변동 직후 그 계정의 쓸 수 있는 클리어권 합계';
COMMENT ON COLUMN sweep_ticket_ledger.ref IS 'shop_buy: 요청 request_id / weekly_activity: weekly:{계정 uuid}:{주 시작 ISO} / campaign_claim: 우편 uuid / sweep_use: 소탕 uuid / expire: 로트 id';
-- 계정별 이력 조회, 한 요청이 만든 행 찾기
CREATE INDEX sweep_ledger_account ON sweep_ticket_ledger (account_id, id);
CREATE INDEX sweep_ledger_request ON sweep_ticket_ledger (request_id) WHERE request_id IS NOT NULL;
-- 같은 주 활동 보상·같은 우편의 이중 지급을 DB가 막는다
CREATE UNIQUE INDEX sweep_ledger_grant_uq ON sweep_ticket_ledger (reason, ref) WHERE reason IN ('weekly_activity', 'campaign_claim');
CREATE TRIGGER sweep_ticket_ledger_append_only BEFORE UPDATE OR DELETE ON sweep_ticket_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER sweep_ticket_ledger_no_truncate BEFORE TRUNCATE ON sweep_ticket_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 2. 소탕 기록 ----------

CREATE TABLE dungeon_sweeps (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id   TEXT NOT NULL,
  difficulty   SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  reset_day    TIMESTAMPTZ NOT NULL,
  lot_id       BIGINT NOT NULL REFERENCES sweep_ticket_lots(id),
  xp_granted   INT NOT NULL CHECK (xp_granted >= 0),
  card         JSONB NOT NULL CHECK (jsonb_typeof(card) = 'object'),
  request_id   UUID NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  dungeon_sweeps IS '던전 소탕 한 번(추가만). dungeon_runs와 섞지 않는다: 난이도 해금·업적·최고 랭크는 이 표를 읽지 않는다. 하루 입장 횟수와 퀘스트 클리어 횟수만 이 표를 더해 읽는다';
COMMENT ON COLUMN dungeon_sweeps.reset_day IS '소탕 시각이 속한 일일 초기화 구간의 시작(resetBoundaries().dailyStartAt). 오늘 입장 수 집계 키';
COMMENT ON COLUMN dungeon_sweeps.lot_id    IS '쓴 클리어권 로트(이벤트인지 일반인지)';
COMMENT ON COLUMN dungeon_sweeps.xp_granted IS '실제로 들어간 경험치(만렙이면 0)';
COMMENT ON COLUMN dungeon_sweeps.card      IS '지급한 카드 {item_key, count}';
COMMENT ON COLUMN dungeon_sweeps.request_id IS '요청의 request_id. 모두 소탕 한 번이 만든 행들이 같은 값';
-- 오늘 입장 횟수(countEntries), 소탕은 캐릭터 단위
CREATE INDEX dungeon_sweeps_char_day ON dungeon_sweeps (character_id, reset_day);
-- 퀘스트 "던전 클리어 N회"의 던전별 합계
CREATE INDEX dungeon_sweeps_char_dungeon ON dungeon_sweeps (character_id, dungeon_id);
CREATE TRIGGER dungeon_sweeps_append_only BEFORE UPDATE OR DELETE ON dungeon_sweeps
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER dungeon_sweeps_no_truncate BEFORE TRUNCATE ON dungeon_sweeps
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 3. 계정 주간 카운터 ----------

CREATE TABLE account_week_counters (
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  week_start TIMESTAMPTZ NOT NULL,
  kind       TEXT NOT NULL CHECK (kind IN ('sweep_buy', 'direct_clear', 'activity_claim')),
  used       INT NOT NULL DEFAULT 0 CHECK (used >= 0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, week_start, kind),
  CONSTRAINT week_counters_claim_chk CHECK (kind <> 'activity_claim' OR used = 1)
);
COMMENT ON TABLE  account_week_counters IS '계정 주간 카운터. week_start는 resetBoundaries().weeklyStartAt(목요일 06:00 KST). 주가 바뀌면 새 행이 생겨 초기화 작업이 없다';
COMMENT ON COLUMN account_week_counters.kind IS 'sweep_buy 이번 주 클리어권 구매 장수 / direct_clear 이번 주 보상이 잠기지 않은 요일 던전 직접 클리어 수(계정 합산) / activity_claim 주간 보상 수령(행이 있으면 받음, used=1)';
-- 지난 주 행 정리(purge)
CREATE INDEX account_week_counters_week ON account_week_counters (week_start);

-- ---------- 4. 운영 우편 캠페인 ----------

CREATE TABLE mail_campaigns (
  id               BIGSERIAL PRIMARY KEY,
  uuid             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  title            TEXT NOT NULL CHECK (char_length(title) BETWEEN 1 AND 40),
  body             TEXT NOT NULL DEFAULT '' CHECK (char_length(body) <= 1000),
  category         TEXT NOT NULL CHECK (category IN ('maintenance', 'apology', 'event', 'attendance', 'other')),
  delivery_unit    TEXT NOT NULL DEFAULT 'account' CHECK (delivery_unit IN ('account', 'character')),
  target           JSONB NOT NULL CHECK (jsonb_typeof(target) = 'object'),
  mail_days        SMALLINT NOT NULL CHECK (mail_days BETWEEN 1 AND 30),
  starts_at        TIMESTAMPTZ NOT NULL,
  ends_at          TIMESTAMPTZ NOT NULL,
  cap_count        INT NOT NULL CHECK (cap_count > 0),
  issued_count     INT NOT NULL DEFAULT 0 CHECK (issued_count >= 0),
  status           TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'active', 'ended', 'cancelled')),
  memo             TEXT NOT NULL CHECK (char_length(memo) BETWEEN 1 AND 200),
  created_by       BIGINT NOT NULL REFERENCES admin_users(id),
  approved_by      BIGINT REFERENCES admin_users(id),
  approved_at      TIMESTAMPTZ,
  ended_at         TIMESTAMPTZ,
  cancelled_by     BIGINT REFERENCES admin_users(id),
  cancelled_at     TIMESTAMPTZ,
  cancel_reason    TEXT CHECK (char_length(cancel_reason) <= 200),
  revoke_requested BOOLEAN NOT NULL DEFAULT false,
  revoke_done_at   TIMESTAMPTZ,
  revoked_count    INT NOT NULL DEFAULT 0 CHECK (revoked_count >= 0),
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT mail_campaigns_window_chk     CHECK (ends_at > starts_at),
  CONSTRAINT mail_campaigns_cap_chk        CHECK (issued_count <= cap_count),
  CONSTRAINT mail_campaigns_two_person_chk CHECK (approved_by IS NULL OR approved_by <> created_by),
  CONSTRAINT mail_campaigns_state_chk CHECK (
       (status = 'pending'            AND approved_by IS NULL AND cancelled_at IS NULL)
    OR (status IN ('active', 'ended') AND approved_by IS NOT NULL AND approved_at IS NOT NULL AND cancelled_at IS NULL)
    OR (status = 'cancelled'          AND cancelled_at IS NOT NULL))
);
COMMENT ON TABLE  mail_campaigns IS '운영 우편 캠페인(공지 우편 한 종류). 대상이 접속할 때 우편을 만든다(전체 발송 때 한 번에 넣지 않는다). 내용 열은 만든 뒤 못 바꾸고(트리거) 삭제하지 않는다(상태로 닫는다)';
COMMENT ON COLUMN mail_campaigns.category      IS '점검 보상 maintenance / 사과 보상 apology / 이벤트 event / 출석 attendance / 기타 other. 우편의 system_code로 그대로 간다';
COMMENT ON COLUMN mail_campaigns.delivery_unit IS 'account 계정당 1통(받을 캐릭터는 그 계정에서 처음 접속·폴링한 캐릭터) / character 캐릭터당 1통(드물게)';
COMMENT ON COLUMN mail_campaigns.target        IS '{"all":true} 또는 조건 조합(min_account_level, max_account_level, classes, account_created_from/to, last_login_before, account_ids). 서버가 만들 때 검증한다';
COMMENT ON COLUMN mail_campaigns.mail_days     IS '우편 수령 기한(배달 시각부터 일수, 1~30)';
COMMENT ON COLUMN mail_campaigns.starts_at     IS '배달 기간. 이 구간 밖에서는 우편을 만들지 않는다';
COMMENT ON COLUMN mail_campaigns.cap_count     IS '총 지급 통수 상한(필수). 배달의 조건부 UPDATE가 강제한다';
COMMENT ON COLUMN mail_campaigns.issued_count  IS '지금까지 만든 우편 수. 배달 트랜잭션의 마지막 문장에서만 +1';
COMMENT ON COLUMN mail_campaigns.created_by    IS '작성 관리자. 승인자와 달라야 한다(2인 확인)';
COMMENT ON COLUMN mail_campaigns.cancelled_by  IS 'NULL이면 시스템 취소(승인 전 기간 만료 등)';
COMMENT ON COLUMN mail_campaigns.revoke_requested IS '취소 때 미수령 회수를 요청했는가. 작업 campaign_revoke가 처리하고 revoke_done_at을 기록한다';
-- 캐시 갱신·상태 작업: 진행 중·대기 중 캠페인만
CREATE INDEX mail_campaigns_open ON mail_campaigns (status, ends_at) WHERE status IN ('pending', 'active');
-- 목록(최신순 커서)
CREATE INDEX mail_campaigns_created ON mail_campaigns (created_at DESC);
-- 회수 작업 대상
CREATE INDEX mail_campaigns_revoke ON mail_campaigns (id) WHERE revoke_requested AND revoke_done_at IS NULL;

CREATE FUNCTION mail_campaigns_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'mail_campaigns is never deleted (close it by status)';
  END IF;
  IF NEW.title IS DISTINCT FROM OLD.title OR NEW.body IS DISTINCT FROM OLD.body
     OR NEW.category IS DISTINCT FROM OLD.category OR NEW.delivery_unit IS DISTINCT FROM OLD.delivery_unit
     OR NEW.target IS DISTINCT FROM OLD.target OR NEW.mail_days IS DISTINCT FROM OLD.mail_days
     OR NEW.starts_at IS DISTINCT FROM OLD.starts_at OR NEW.ends_at IS DISTINCT FROM OLD.ends_at
     OR NEW.cap_count IS DISTINCT FROM OLD.cap_count OR NEW.created_by IS DISTINCT FROM OLD.created_by
     OR NEW.memo IS DISTINCT FROM OLD.memo OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
    RAISE EXCEPTION 'mail_campaigns content is immutable after creation';
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER mail_campaigns_guard_upd BEFORE UPDATE ON mail_campaigns
  FOR EACH ROW EXECUTE FUNCTION mail_campaigns_guard();
CREATE TRIGGER mail_campaigns_guard_del BEFORE DELETE ON mail_campaigns
  FOR EACH ROW EXECUTE FUNCTION mail_campaigns_guard();

CREATE TABLE mail_campaign_attachments (
  id          BIGSERIAL PRIMARY KEY,
  campaign_id BIGINT NOT NULL REFERENCES mail_campaigns(id),
  slot        SMALLINT NOT NULL CHECK (slot BETWEEN 1 AND 5),
  kind        TEXT NOT NULL CHECK (kind IN ('gold', 'item', 'sweep_ticket')),
  item_key    TEXT,
  amount      BIGINT NOT NULL CHECK (amount > 0),
  bind        TEXT CHECK (bind IN ('none', 'account', 'character')),
  UNIQUE (campaign_id, slot),
  CONSTRAINT mail_campaign_att_shape_chk CHECK (
       (kind = 'gold'         AND item_key IS NULL     AND bind IS NULL)
    OR (kind = 'item'         AND item_key IS NOT NULL AND item_key <> 'gold' AND bind IS NOT NULL AND amount <= 2147483647)
    OR (kind = 'sweep_ticket' AND item_key IS NOT NULL AND bind IS NULL AND amount <= 1000))
);
COMMENT ON TABLE  mail_campaign_attachments IS '캠페인 첨부(최대 5, 추가만). 배달 때 mail_attachments로 복사된다';
COMMENT ON COLUMN mail_campaign_attachments.amount   IS 'gold는 골드, item·sweep_ticket은 개수';
COMMENT ON COLUMN mail_campaign_attachments.item_key IS 'sweep_ticket은 표시용 이벤트 클리어권 키(서버 데이터 sweep.json이 정한다). 값은 서버가 검증한다';
-- 골드·클리어권 첨부는 캠페인당 하나
CREATE UNIQUE INDEX mail_campaign_att_gold_uq   ON mail_campaign_attachments (campaign_id) WHERE kind = 'gold';
CREATE UNIQUE INDEX mail_campaign_att_ticket_uq ON mail_campaign_attachments (campaign_id) WHERE kind = 'sweep_ticket';
CREATE TRIGGER mail_campaign_attachments_append_only BEFORE UPDATE OR DELETE ON mail_campaign_attachments
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 5. 우편 확장 (기존 열은 그대로) ----------

ALTER TABLE mails
  ADD COLUMN title       TEXT CHECK (char_length(title) BETWEEN 1 AND 40),
  ADD COLUMN body        TEXT CHECK (char_length(body) <= 1000),
  ADD COLUMN campaign_id BIGINT REFERENCES mail_campaigns(id),
  ADD COLUMN attach_n    SMALLINT NOT NULL DEFAULT 0 CHECK (attach_n BETWEEN 0 AND 5);
COMMENT ON COLUMN mails.title       IS '제목. 옛 우편은 NULL(클라이언트가 종류·system_code로 문구를 조립). kind=system일 때만';
COMMENT ON COLUMN mails.body        IS '본문(줄바꿈 허용). 옛 우편은 NULL';
COMMENT ON COLUMN mails.campaign_id IS '캠페인이 배달한 우편이면 그 캠페인';
COMMENT ON COLUMN mails.attach_n    IS 'mail_attachments의 첨부 수. 0이 아니면 첨부는 그 표에 있고 이 행의 item_key는 NULL, gold는 0이다';

-- 첨부가 표에 있는 우편은 내용 검사를 통과시키고(content), 옛 열과 섞이지 않게 한다(mode)
ALTER TABLE mails DROP CONSTRAINT mails_content_chk;
ALTER TABLE mails ADD CONSTRAINT mails_content_chk CHECK (item_key IS NOT NULL OR gold > 0 OR attach_n > 0);
ALTER TABLE mails ADD CONSTRAINT mails_attach_mode_chk CHECK (attach_n = 0 OR (item_key IS NULL AND gold = 0));
ALTER TABLE mails ADD CONSTRAINT mails_title_chk CHECK ((title IS NULL AND body IS NULL) OR (kind = 'system' AND title IS NOT NULL));
ALTER TABLE mails ADD CONSTRAINT mails_campaign_chk CHECK (campaign_id IS NULL OR (kind = 'system' AND attach_n > 0));
ALTER TABLE mails ADD CONSTRAINT mails_attach_kind_chk CHECK (attach_n = 0 OR kind = 'system');

-- 캠페인 분류가 우편의 system_code (기존 4개 유지)
ALTER TABLE mails DROP CONSTRAINT mails_system_code_check;
ALTER TABLE mails ADD CONSTRAINT mails_system_code_check
  CHECK (system_code IN ('compensation', 'event', 'refund', 'notice', 'maintenance', 'apology', 'attendance', 'other'));
COMMENT ON COLUMN mails.system_code IS 'kind=system일 때만 값. compensation 보상 / event 이벤트 / refund 환불 / notice 안내 / maintenance 점검 보상 / apology 사과 보상 / attendance 출석 / other 기타. 캠페인 우편은 category가 그대로 온다';

-- 캠페인당 한 캐릭터에 한 통(배달 표와 이중 방어)
CREATE UNIQUE INDEX mails_campaign_once ON mails (campaign_id, character_id) WHERE campaign_id IS NOT NULL;
-- 취소 회수 작업: 캠페인의 미수령 우편을 id 순으로 배치 처리, 현황 집계
CREATE INDEX mails_campaign_open ON mails (campaign_id, id) WHERE campaign_id IS NOT NULL AND claimed_at IS NULL AND expired_at IS NULL;

CREATE TABLE mail_attachments (
  id       BIGSERIAL PRIMARY KEY,
  mail_id  BIGINT NOT NULL REFERENCES mails(id),
  slot     SMALLINT NOT NULL CHECK (slot BETWEEN 1 AND 5),
  kind     TEXT NOT NULL CHECK (kind IN ('gold', 'item', 'sweep_ticket')),
  item_key TEXT,
  amount   BIGINT NOT NULL CHECK (amount > 0),
  bind     TEXT CHECK (bind IN ('none', 'account', 'character')),
  UNIQUE (mail_id, slot),
  CONSTRAINT mail_att_shape_chk CHECK (
       (kind = 'gold'         AND item_key IS NULL     AND bind IS NULL)
    OR (kind = 'item'         AND item_key IS NOT NULL AND item_key <> 'gold' AND bind IS NOT NULL AND amount <= 2147483647)
    OR (kind = 'sweep_ticket' AND item_key IS NOT NULL AND bind IS NULL AND amount <= 1000))
);
COMMENT ON TABLE  mail_attachments IS '여러 첨부가 있는 우편의 첨부(추가만). 수령 여부는 mails.claimed_at이 정한다(부분 수령 없음). 첨부 아이템은 우편 위치(mail)의 item_ledger +n, 수령하면 mail -n / bag +n';
CREATE TRIGGER mail_attachments_append_only BEFORE UPDATE OR DELETE ON mail_attachments
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
-- 한 페이지 우편의 첨부를 mail_id IN (...)으로 읽는 것은 UNIQUE (mail_id, slot)이 맡는다

CREATE TABLE mail_campaign_deliveries (
  id           BIGSERIAL PRIMARY KEY,
  campaign_id  BIGINT NOT NULL REFERENCES mail_campaigns(id),
  delivery_key BIGINT NOT NULL,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  mail_id      BIGINT NOT NULL UNIQUE REFERENCES mails(id),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (campaign_id, delivery_key)
);
COMMENT ON TABLE  mail_campaign_deliveries IS '캠페인 배달 기록(추가만): 누구에게 어느 캐릭터로 언제. UNIQUE(campaign_id, delivery_key)가 계정당(또는 캐릭터당) 1통을 DB에서 강제한다';
COMMENT ON COLUMN mail_campaign_deliveries.delivery_key IS 'delivery_unit=account이면 계정 id, character이면 캐릭터 id';
-- 캠페인별 배달 목록(관리자 MC6, id 커서)
CREATE INDEX mail_campaign_deliveries_campaign ON mail_campaign_deliveries (campaign_id, id);
-- 한 계정이 받은 캠페인(계정 상세)
CREATE INDEX mail_campaign_deliveries_account ON mail_campaign_deliveries (account_id, created_at DESC);
CREATE TRIGGER mail_campaign_deliveries_append_only BEFORE UPDATE OR DELETE ON mail_campaign_deliveries
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 6. 보조 인덱스, 원장 사유, 이상 기록, 감사 대상 ----------

-- 클리어권 가격 계산: 계정의 최고 레벨(삭제한 캐릭터 포함). 기존 characters_account_alive는 살아 있는 캐릭터만 덮는다
CREATE INDEX characters_account_level ON characters (account_id, level DESC);

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy'));

ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check
  CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost', 'dungeon_sweep'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상 / dungeon_clear 던전 클리어 / test_boost 시험 서버 레벨 조정 / dungeon_sweep 던전 소탕(ref = 소탕 uuid)';

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution',
                  'sweep_denied'));
COMMENT ON COLUMN anomaly_log.kind IS '0020의 값 + sweep_denied 소탕 화면 조건을 우회한 요청(미클리어, 등급 미달, 레벨 부족, 소탕 불가 던전)';

ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold', 'campaign'));

-- ============ DOWN ============
-- 개발 DB 전용. 새 표·열을 지우고 CHECK를 0020 상태로 되돌린다(원장 행 삭제는 추가 전용 트리거를 잠시 끈다).
-- 캠페인 우편을 이미 수령해 올라간 골드·경험치 잔액은 되돌리지 않는다(개발 DB 정리용).

ALTER TABLE admin_audit_log DISABLE TRIGGER admin_audit_log_append_only;
DELETE FROM admin_audit_log WHERE target_type = 'campaign';
ALTER TABLE admin_audit_log ENABLE TRIGGER admin_audit_log_append_only;
ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold'));

DELETE FROM anomaly_log WHERE kind = 'sweep_denied';
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution'));
COMMENT ON COLUMN anomaly_log.kind IS '0020의 값';

ALTER TABLE xp_ledger DISABLE TRIGGER xp_ledger_append_only;
DELETE FROM xp_ledger WHERE reason = 'dungeon_sweep';
ALTER TABLE xp_ledger ENABLE TRIGGER xp_ledger_append_only;
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상 / dungeon_clear 던전 클리어 / test_boost 시험 서버 레벨 조정(scripts/test-level.mjs)';

ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason = 'sweep_ticket_buy';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback'));

DROP INDEX characters_account_level;

-- 캠페인 우편과 그 아이템 원장을 먼저 지운다(우편 -> 첨부·배달 -> 캠페인 순)
DROP TABLE mail_campaign_deliveries;
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE ref IN (SELECT uuid::text FROM mails WHERE campaign_id IS NOT NULL);
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
DROP TABLE mail_attachments;
DELETE FROM mails WHERE campaign_id IS NOT NULL;

DROP INDEX mails_campaign_open;
DROP INDEX mails_campaign_once;
ALTER TABLE mails DROP CONSTRAINT mails_system_code_check;
ALTER TABLE mails ADD CONSTRAINT mails_system_code_check CHECK (system_code IN ('compensation', 'event', 'refund', 'notice'));
COMMENT ON COLUMN mails.system_code IS 'kind=system일 때만 값이 있다. compensation 보상 / event 이벤트 / refund 환불 / notice 안내. 문구는 클라이언트가 이 코드로 조립한다(서버는 문장을 만들지 않는다)';
ALTER TABLE mails DROP CONSTRAINT mails_attach_kind_chk;
ALTER TABLE mails DROP CONSTRAINT mails_campaign_chk;
ALTER TABLE mails DROP CONSTRAINT mails_title_chk;
ALTER TABLE mails DROP CONSTRAINT mails_attach_mode_chk;
ALTER TABLE mails DROP CONSTRAINT mails_content_chk;
ALTER TABLE mails ADD CONSTRAINT mails_content_chk CHECK (item_key IS NOT NULL OR gold > 0);
ALTER TABLE mails DROP COLUMN attach_n, DROP COLUMN campaign_id, DROP COLUMN body, DROP COLUMN title;

DROP TABLE mail_campaign_attachments;
DROP TABLE mail_campaigns;
DROP FUNCTION mail_campaigns_guard();

DROP TABLE account_week_counters;

ALTER TABLE dungeon_sweeps DISABLE TRIGGER dungeon_sweeps_append_only;
DROP TABLE dungeon_sweeps;

ALTER TABLE sweep_ticket_ledger DISABLE TRIGGER sweep_ticket_ledger_append_only;
DROP TABLE sweep_ticket_ledger;
DROP TABLE sweep_ticket_lots;
