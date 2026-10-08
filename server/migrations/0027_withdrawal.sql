-- 0027_withdrawal: 회원 탈퇴 (Docs/server/phase12_withdrawal.md)
-- 대상: PostgreSQL 14 이상. 선행: 0026_sealed_box_pass.
--   1. accounts.anonymized_at
--   2. account_withdrawals / withdrawn_identities / account_destruction_log
--   3. refresh_tokens.revoke_reason, online_sessions.end_reason 값 추가
--   4. ledger_block_mutation(): 전용 역할 dotrpg_purge 의 DELETE만 허용 표에 한해 통과
--      (허용 표 = 설계 3.2의 13개 + 계정·캐릭터 한 곳에 속한 dungeon_sweeps, mail_attachments, mail_campaign_deliveries, admin_grants, party_run_host_reports)

-- ============ UP ============
ALTER TABLE accounts ADD COLUMN anonymized_at TIMESTAMPTZ;
ALTER TABLE accounts ADD CONSTRAINT accounts_anonymized_chk
  CHECK (anonymized_at IS NULL OR deleted_at IS NOT NULL);
COMMENT ON COLUMN accounts.deleted_at IS '탈퇴 요청 시각. NULL이 아니면 로그인 불가(철회하면 NULL로 돌아간다). 유예 안에는 account_withdrawals(state=requested)가 짝이다';
COMMENT ON COLUMN accounts.anonymized_at IS '익명화 완료 시각(되돌릴 수 없다). Steam ID 연결·IP·기기·채팅이 지워진 뒤에만 채운다';

CREATE TABLE account_withdrawals (
  id                    BIGSERIAL PRIMARY KEY,
  uuid                  UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id            BIGINT NOT NULL REFERENCES accounts(id),
  state                 TEXT NOT NULL DEFAULT 'requested' CHECK (state IN ('requested', 'cancelled', 'completed')),
  source                TEXT NOT NULL CHECK (source IN ('self', 'admin')),
  requested_by_admin_id BIGINT REFERENCES admin_users(id),
  request_id            UUID NOT NULL,
  requested_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  due_at                TIMESTAMPTZ NOT NULL,
  cancel_allowed        BOOLEAN NOT NULL DEFAULT true,
  ack_paid_loss         BOOLEAN NOT NULL DEFAULT false,
  loss_snapshot         JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(loss_snapshot) = 'object'),
  saved_character_names JSONB CHECK (saved_character_names IS NULL OR jsonb_typeof(saved_character_names) = 'object'),
  defer_reasons         TEXT[] NOT NULL DEFAULT '{}',
  defer_checked_at      TIMESTAMPTZ,
  manual_hold           BOOLEAN NOT NULL DEFAULT false,
  manual_hold_note      TEXT CHECK (char_length(manual_hold_note) <= 200),
  cancelled_at          TIMESTAMPTZ,
  cancelled_via         TEXT CHECK (cancelled_via IN ('self', 'admin')),
  cancel_request_id     UUID,
  anonymized_at         TIMESTAMPTZ,
  retain_until          TIMESTAMPTZ,
  UNIQUE (account_id, request_id),
  CONSTRAINT account_withdrawals_due_chk       CHECK (due_at > requested_at),
  CONSTRAINT account_withdrawals_source_chk    CHECK ((source = 'admin') = (requested_by_admin_id IS NOT NULL)),
  CONSTRAINT account_withdrawals_cancel_chk    CHECK ((state = 'cancelled') = (cancelled_at IS NOT NULL AND cancelled_via IS NOT NULL)),
  CONSTRAINT account_withdrawals_complete_chk  CHECK ((state = 'completed') = (anonymized_at IS NOT NULL AND retain_until IS NOT NULL)),
  CONSTRAINT account_withdrawals_names_chk     CHECK (state = 'requested' OR saved_character_names IS NULL),
  CONSTRAINT account_withdrawals_defer_chk     CHECK (defer_reasons <@ ARRAY['sanction', 'economy_hold', 'open_report', 'payment_open', 'manual']::TEXT[])
);
COMMENT ON TABLE  account_withdrawals IS '회원 탈퇴 요청(상태 기계). 요청 때 한 줄을 만들고 철회하면 cancelled, 익명화하면 completed. 같은 계정이 다시 요청하면 새 줄. 5년 파기 때 함께 삭제한다';
COMMENT ON COLUMN account_withdrawals.source IS 'self 본인 요청 / admin 운영자 대행(정보주체 요청을 지원 채널로 받은 경우, 제재 중인 계정 포함)';
COMMENT ON COLUMN account_withdrawals.request_id IS 'W2/WD3의 멱등 키. (account_id, request_id) 유일';
COMMENT ON COLUMN account_withdrawals.due_at IS '익명화 가능 시각 = requested_at + WITHDRAW_GRACE_DAYS. 이 시각 전에만 본인이 철회할 수 있다';
COMMENT ON COLUMN account_withdrawals.cancel_allowed IS 'false면 본인 철회 불가(운영자 강제 처리, owner만 지정). 운영자는 WD4로 취소할 수 있다';
COMMENT ON COLUMN account_withdrawals.ack_paid_loss IS '요청 때 유료 별조각 소멸 안내를 확인했는가(유료 잔액이 있으면 true여야 요청이 통과한다)';
COMMENT ON COLUMN account_withdrawals.loss_snapshot IS '요청 시점 손실 요약(건수·수량만): paid_stars, free_stars, debt, gold_total, characters, unclaimed_mails, active_listings, top_bids. 동의 확인의 증거';
COMMENT ON COLUMN account_withdrawals.saved_character_names IS '{캐릭터 uuid: 원래 이름}. 철회 때 복원하고, 익명화·철회 뒤에는 NULL로 지운다(CHECK)';
COMMENT ON COLUMN account_withdrawals.defer_reasons IS '익명화를 미루는 사유(작업이 매번 다시 계산): sanction 활성 정지·채팅 금지 / economy_hold 활성 경제 정지 / open_report 열린 신고의 대상 / payment_open 열린 결제 주문·심각 플래그 / manual 운영자 보류';
COMMENT ON COLUMN account_withdrawals.retain_until IS '익명화 때 계산하는 기록 보관 기한(2절). 이 시각 뒤 withdrawal-destroy 대상';
-- 한 계정의 열린 요청은 하나, 완료도 하나(동시 요청의 마지막 안전장치)
CREATE UNIQUE INDEX account_withdrawals_one_open ON account_withdrawals (account_id) WHERE state = 'requested';
CREATE UNIQUE INDEX account_withdrawals_one_done ON account_withdrawals (account_id) WHERE state = 'completed';
-- 익명화 작업: "기한이 지났고 운영자 보류가 아닌 요청"을 기한순으로 읽는다
CREATE INDEX account_withdrawals_due ON account_withdrawals (due_at) WHERE state = 'requested' AND NOT manual_hold;
-- 파기 작업: 보관 기한이 지난 완료 건
CREATE INDEX account_withdrawals_retain ON account_withdrawals (retain_until) WHERE state = 'completed';
-- 요청 한도(최근 30일 N회) 계산과 계정 상세
CREATE INDEX account_withdrawals_account_time ON account_withdrawals (account_id, requested_at DESC);

CREATE TABLE withdrawn_identities (
  id                   BIGSERIAL PRIMARY KEY,
  uuid                 UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  identity_hash        TEXT NOT NULL UNIQUE CHECK (identity_hash ~ '^[0-9a-f]{64}$'),
  source_account_id    BIGINT NOT NULL REFERENCES accounts(id),
  carry_ban_until      TIMESTAMPTZ,
  carry_payment_block  TEXT CHECK (carry_payment_block IN ('chargeback', 'refund_abuse', 'linked_chargeback', 'fraud_suspect', 'manual')),
  carry_econ_hold      BOOLEAN NOT NULL DEFAULT false,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at           TIMESTAMPTZ NOT NULL,
  released_at          TIMESTAMPTZ,
  released_by          TEXT CHECK (char_length(released_by) <= 40),
  CONSTRAINT withdrawn_identities_reason_chk CHECK (carry_ban_until IS NOT NULL OR carry_payment_block IS NOT NULL OR carry_econ_hold),
  CONSTRAINT withdrawn_identities_release_chk CHECK ((released_at IS NULL) = (released_by IS NULL))
);
COMMENT ON TABLE  withdrawn_identities IS '탈퇴한 Steam 계정의 제재·결제 차단 이월 표시. identity_hash = HMAC-SHA256(키 WITHDRAW_ID_HMAC_KEY, steam_id)라 원문 Steam ID는 어디에도 없고 키 없이 되돌릴 수 없다. 이월할 사유가 있을 때만 만든다. expires_at 뒤 정리 작업이 지운다';
COMMENT ON COLUMN withdrawn_identities.uuid IS '관리자 API 외부 식별자. 내부 id는 밖으로 내보내지 않는다';
COMMENT ON COLUMN withdrawn_identities.carry_ban_until IS '활성 정지의 종료 시각(영구 정지는 9999-12-31). 재가입 시 이 시각 전이면 계정을 만들지 않고 ACCOUNT_BANNED';
COMMENT ON COLUMN withdrawn_identities.carry_payment_block IS '결제 정지 사유 또는 별조각 부채가 남은 경우의 chargeback. 재가입 계정에 payment_profiles(blocked)를 만든다';
COMMENT ON COLUMN withdrawn_identities.carry_econ_hold IS '활성 경제 정지가 있었는가. 재가입 계정에 수동 경제 정지를 건다';
COMMENT ON COLUMN withdrawn_identities.expires_at IS 'max(이월 사유의 끝, 만들 때) 이되 만든 시각 + WITHDRAW_TOMBSTONE_MAX_DAYS를 넘지 않는다';
-- 만료 정리 (expires_at 범위 스캔, 행 수가 작아도 의도를 드러낸다)
CREATE INDEX withdrawn_identities_expires ON withdrawn_identities (expires_at);

CREATE TABLE account_destruction_log (
  id            BIGSERIAL PRIMARY KEY,
  account_uuid  UUID NOT NULL,
  requested_at  TIMESTAMPTZ NOT NULL,
  anonymized_at TIMESTAMPTZ NOT NULL,
  destroyed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  mode          TEXT NOT NULL CHECK (mode IN ('scheduled', 'admin')),
  tables        JSONB NOT NULL CHECK (jsonb_typeof(tables) = 'object')
);
COMMENT ON TABLE  account_destruction_log IS '파기 관리대장(추가만). 누가 언제 어떤 표에서 몇 줄을 지웠는지. 계정 uuid 외 식별 정보 없음. FK를 두지 않는다(계정 행이 지워져도 남아야 한다)';
CREATE TRIGGER account_destruction_log_append_only BEFORE UPDATE OR DELETE ON account_destruction_log
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER account_destruction_log_no_truncate BEFORE TRUNCATE ON account_destruction_log
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 사유 값 추가
ALTER TABLE refresh_tokens DROP CONSTRAINT refresh_tokens_revoke_reason_check;
ALTER TABLE refresh_tokens ADD CONSTRAINT refresh_tokens_revoke_reason_check
  CHECK (revoke_reason IN ('logout', 'reuse', 'replaced', 'device_mismatch', 'admin', 'legacy', 'withdrawal'));
ALTER TABLE online_sessions DROP CONSTRAINT online_sessions_end_reason_check;
ALTER TABLE online_sessions ADD CONSTRAINT online_sessions_end_reason_check
  CHECK (end_reason IN ('leave', 'replaced', 'timeout', 'banned', 'withdrawal'));

-- 5년 파기 통로: 전용 역할(session_user = dotrpg_purge)의 DELETE만 허용 표에 한해 통과. UPDATE, TRUNCATE, 앱 계정의 DELETE는 그대로 거절
CREATE OR REPLACE FUNCTION ledger_block_mutation() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' AND session_user = 'dotrpg_purge' AND TG_TABLE_NAME = ANY (ARRAY[
       'gold_ledger', 'item_ledger', 'xp_ledger', 'enhance_log', 'star_ledger', 'star_order_events', 'star_spend_allocs',
       'sweep_ticket_ledger', 'auction_trades', 'auction_sinks', 'auction_flags', 'auction_trade_flags',
       'admin_account_notes', 'dungeon_sweeps', 'mail_attachments', 'mail_campaign_deliveries', 'admin_grants', 'party_run_host_reports']) THEN
    RETURN OLD;
  END IF;
  RAISE EXCEPTION '% is append-only (% blocked)', TG_TABLE_NAME, TG_OP;
END;
$$ LANGUAGE plpgsql;

-- ============ DOWN ============
-- 개발 DB 전용: 새 값을 쓰는 행이 있으면 옛 값으로 바꾼 뒤 제약을 되돌린다.
CREATE OR REPLACE FUNCTION ledger_block_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION '% is append-only (% blocked)', TG_TABLE_NAME, TG_OP;
END;
$$ LANGUAGE plpgsql;
UPDATE online_sessions SET end_reason = 'leave' WHERE end_reason = 'withdrawal';
ALTER TABLE online_sessions DROP CONSTRAINT IF EXISTS online_sessions_end_reason_check;
ALTER TABLE online_sessions ADD CONSTRAINT online_sessions_end_reason_check
  CHECK (end_reason IN ('leave', 'replaced', 'timeout', 'banned'));
UPDATE refresh_tokens SET revoke_reason = 'admin' WHERE revoke_reason = 'withdrawal';
ALTER TABLE refresh_tokens DROP CONSTRAINT IF EXISTS refresh_tokens_revoke_reason_check;
ALTER TABLE refresh_tokens ADD CONSTRAINT refresh_tokens_revoke_reason_check
  CHECK (revoke_reason IN ('logout', 'reuse', 'replaced', 'device_mismatch', 'admin', 'legacy'));
DROP TABLE IF EXISTS account_destruction_log;
DROP TABLE IF EXISTS withdrawn_identities;
DROP TABLE IF EXISTS account_withdrawals;
ALTER TABLE accounts DROP CONSTRAINT IF EXISTS accounts_anonymized_chk;
ALTER TABLE accounts DROP COLUMN IF EXISTS anonymized_at;
