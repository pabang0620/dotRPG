# 회원 탈퇴 설계 (IMPROVEMENT_PLAN S5, 마이그레이션 0027)

기준: [PRELAUNCH_REVIEW.md](../PRELAUNCH_REVIEW.md) L4·L5(탈퇴 API가 없고 재화 원장이 영구 보관), [IMPROVEMENT_PLAN.md](../IMPROVEMENT_PLAN.md) S5(이 문서)·S7(구현). 앞 단계: [phase1_2_api.md](phase1_2_api.md)(응답 형식, 인증, 멱등성), [phase7_ops.md](phase7_ops.md)(관리자 API·CLI, JobRunner, 보관 일정 6.3), [phase9_anti_abuse.md](phase9_anti_abuse.md)(세션 id, 경제 정지), [phase11_payments.md](phase11_payments.md)(주문·원장·환불). 마이그레이션: `server/migrations/0027_withdrawal.sql`(이 문서 3절이 초안, 정본은 구현 때 만든다), 스키마: `server/schema.sql`(구현 때 맨 아래에 합친다).

이 문서는 설계다. 서버 코드는 dotrpg-backend-coder(S7)가 이 문서대로 만들고, Unity 클라이언트(탈퇴 화면, 오류 코드 처리)는 메인이 따로 한다(15.0절이 경계). 게임 값은 없으므로 `server/data/*.json` 참조는 없다.

> **법률 자문 전 기본값이다.** 보관 기간(5년, 90일, 180일), 유예 30일, 재가입 규칙, 제재 정보 보관은 모두 사용자가 준 기본값이거나 이 문서가 제안한 값이고, 법무 확인이 끝나기 전까지 환경변수(13절)로 바꿀 수 있게 만든다. 법령 조문은 문서에 단정해서 적지 않고, 확인할 항목은 15.2절에 모았다. 개인정보처리방침(L4)의 수집·보관 표는 2절과 10절이 근거 초안이다.

## 0. 핵심 설계 (먼저 읽기)

1. **탈퇴는 두 단계다: 요청(즉시 차단) -> 유예 30일 -> 익명화(되돌릴 수 없음).** 요청 때 로그인을 막고 세션을 끊고 남에게 보이는 이름을 지운다. 유예 안에는 같은 Steam 계정으로 철회할 수 있다. 유예가 끝나면 작업이 개인 식별 정보를 지우고 계정을 "껍데기"로 남긴다.
2. **기존 장치를 재사용한다.** `accounts.deleted_at`은 이미 인증 미들웨어(`authMiddleware.ts` `verifyAccessToken`이 매 요청 DB로 확인), 개발용 로그인, refresh, 캠페인 배달(`campaignDelivery.ts`)이 "삭제됨"으로 취급한다. 그래서 **탈퇴 요청 시각을 `deleted_at`에 넣는 것만으로 즉시 차단이 이미 동작한다**(새 차단 코드 없음). 철회하면 `deleted_at`을 NULL로 되돌린다. 실제로 지워졌는지는 새 열 `accounts.anonymized_at`이 구분한다.
3. **재화 원장과 결제 기록은 지우지 않고 `account_id`만 남긴다(5년).** 골드·아이템·경험치·별조각 원장, 경매·우편·주문 기록은 그대로 두고, 사람을 가리키는 값(Steam ID 연결, 닉네임, IP·기기, 채팅)만 지운다. 그래서 야간 정합성 점검(`integrity.ts` I1~I3, I6)의 보존식이 탈퇴 뒤에도 그대로 성립한다(잔액·재고를 건드리지 않는다).
4. **게임 진행 상태(`character_items`, `character_state` 등)도 5년까지 그대로 둔다.** 지우면 원장 보존식(I2)이 깨지고 숨은 FK를 건드릴 위험이 있다. 이 데이터는 Steam ID·닉네임이 사라진 뒤에는 사람과 이어지지 않는다(예외: 결제 이력이 있는 계정은 `star_orders.steam_id`가 5년 남는다, 15.2절 L3).
5. **제재·경제 정지·열린 신고·결제 미결이 있는 계정은 익명화를 미룬다.** 탈퇴로 조사를 피하지 못하게 한다. 미루는 상한은 180일이고(`WITHDRAW_DEFER_MAX_DAYS`), 그 뒤에도 식별 정보를 지우되 **Steam ID의 키 해시만 `withdrawn_identities`에 남겨** 같은 Steam ID의 재가입이 제재를 이어받게 한다(9절).
6. **요청은 "무엇을 하겠다"뿐이다.** 서버가 받는 것은 `request_id`, 확인 문구, 확인 체크, 재인증 자격(Steam 티켓 또는 개발용 비밀번호)이다. 유예 기간, 보관 기간, 손실 목록은 모두 서버가 계산한다.
7. **날짜 경계 함수는 쓰지 않는다.** 이 기능은 06:00 일일·목요일 주간 초기화와 무관하다. 유예(30일)와 보관(5년)은 서버 시계 기준 경과 시간(`interval`)으로 세고, 날짜 표시는 응답의 UTC ISO 시각을 클라이언트가 KST로 바꿔 보여 준다. KST 날짜를 새로 계산하는 코드를 만들지 않는다.

### 0.1 PLAN_SERVER와 앞 단계 문서에서 달라진 점

| 항목 | 이전 | 이 설계 | 이유 |
|---|---|---|---|
| 원장 보관 | 7단계 결정 "원장 영구 보관" | **활동 중인 계정은 그대로 영구**, 탈퇴한 계정은 5년 뒤 파기(8절) | L5. 전자상거래 기록 5년과 맞춘다 |
| `accounts.deleted_at` 의미 | "소프트 삭제" (쓰는 코드 없음) | "탈퇴 요청 시각. 철회하면 NULL" + `anonymized_at` 추가 | 즉시 차단을 기존 검사로 얻는다 |
| 캐릭터 삭제 | 진행 중 경매·미수령 우편이 있으면 거절(`CHARACTER_HAS_AUCTION`) | **변경 없음.** 탈퇴는 거절하지 않고 손실을 경고한다(4절 D4) | 탈퇴를 가입보다 어렵게 만들지 않는다(15.2절 L8) |
| 7단계 6.3 "지우지 않는다" 표 | 원장·결제·경매·우편 전부 영구 | 탈퇴 계정 한정으로 5년 뒤 파기 가능(`withdrawal-destroy`, 기본 실삭제 꺼짐) | 위와 같다 |
| `ledger_block_mutation()` | 모든 DELETE 거절 | 전용 DB 역할 `dotrpg_purge`의 DELETE만 허용 목록 표에 한해 통과 | 5년 파기를 가능하게 하되 앱 계정으로는 못 지운다(3.4) |
| PLAN_SERVER §9 단계 | 탈퇴 없음 | IMPROVEMENT_PLAN S5로 추가. 문서 번호 12는 파일명 지정이며 PLAN §9의 12단계(부활 코인)와 무관하다 | 사용자 지시 |

### 0.2 이 문서가 미루는 것

- 5년 파기의 **실삭제 실행**: 첫 대상이 2031년 이후에야 생긴다. S7은 대상 계산과 dry-run(건수 기록)까지 만들고, 삭제 SQL은 `WITHDRAW_DESTROY_ENABLED=true`일 때만 돈다(8절). 켜기 전에 법무 확인(L2)과 백업 보관 기간 대조가 필요하다.
- 백업 안의 개인정보: 오프사이트 백업(7단계 7절)에는 익명화 전 데이터가 남는다. 백업 보관 기간을 정해 처리방침에 적는 것은 운영 항목이다(15.2절 L7).
- 패밀리 공유(`steam_owner_id`) 대리인 키: tombstone은 `steam_id`의 해시만 쓴다(9.3).
- 만 14세 미만 가입자 처리(L10): 연령 정보를 수집하지 않으므로 다루지 않는다.
- Unity 클라이언트 구현과 탈퇴 안내 문구.

## 1. 상태 모델

```
(활동) --W2 요청--> requested --W3 철회(유예 안)--> cancelled -> (활동)
                       |                                            (새 요청 가능, 30일 3회 한도)
                       +--due_at 경과 + 보류 사유 없음--> completed(익명화됨)
                                                            |
                                                            +--retain_until 경과--> 파기(8절)
```

| 상태 | `accounts` | `account_withdrawals.state` | 로그인 | 철회 |
|---|---|---|---|---|
| 활동 | `deleted_at` NULL | (행 없음 또는 `cancelled`) | 가능 | - |
| 유예 | `deleted_at` = 요청 시각 | `requested` | `403 ACCOUNT_WITHDRAWAL_PENDING` | 가능(`now < due_at`, `cancel_allowed`) |
| 익명화됨 | `deleted_at` 유지, `anonymized_at` 채움 | `completed` | 신원 행이 없어 로그인 자체가 불가(Steam은 새 계정으로 가입) | 불가 |
| 파기됨 | 행 삭제(참조가 남은 껍데기는 유지) | 행 삭제 + `account_destruction_log` 한 줄 | - | - |

- 한 계정의 `requested`는 하나만(부분 유일 인덱스), `completed`도 하나만.
- 보류(제재 등) 중이어도 `state='requested'`로 남고 `defer_reasons`만 채운다. 철회는 `due_at` 전까지만 가능하고, `due_at`이 지났으면(작업이 보류 중이거나 지연 중이어도) 철회는 `410 WITHDRAWAL_DUE`다. 판단이 시각 하나로 결정되게 하려는 것이다.

## 2. 수집 항목과 보관 기간 (기본값)

사용자가 준 목록(L4)에 이 설계가 정한 처리를 붙인 표다. 개인정보처리방침 초안의 근거다.

| 수집 항목 | 어디에 | 정상 보관 | 탈퇴 시 |
|---|---|---|---|
| 계정 식별(내부 uuid, 생성 시각) | `accounts` | 계정이 있는 동안 | 껍데기로 5년(PII 아님) |
| Steam ID | `auth_identities.subject`, `login_events.steam_id`, `star_orders.steam_id` | 계정이 있는 동안 / 90일 / 5년 | `auth_identities`·`login_events`는 익명화 때 삭제. **`star_orders.steam_id`는 결제 기록으로 5년** |
| 개발용 로그인 ID·비밀번호 해시 | `auth_identities` | 개발 전용 | 익명화 때 삭제 |
| 접속 IP·기기 | `login_events`(90일), `account_ips`·`account_devices`(마지막 관측 후 180일), `online_sessions`, `star_orders.ip`·`device_hash`(180일) | 괄호 기간 | 익명화 때 앞의 네 표 삭제. 주문 표는 기존 180일 일정(분쟁 대비, 15.2절 L2) |
| 닉네임(캐릭터 이름) | `characters.name` 및 스냅샷(`chat_messages`, `blocks`, `reports`, `report_lines`) | 캐릭터가 있는 동안 | 요청 때 자리표시로 교체, 스냅샷은 10절 |
| 채팅(귓속말 포함) | `chat_messages` | 7일 | 익명화 때 남은 행 삭제(대부분 이미 7일이 지나 없음) |
| 로그인 기록 | `login_events` | 90일 | 익명화 때 삭제 |
| 기기·IP 집계 | `account_devices`, `account_ips` | 마지막 관측 후 180일 | 익명화 때 삭제 |
| 신고 증거 | `report_lines`(수정 불가 트리거) | 신고 종결 후 180일 | **변경 없음**(종결 후 180일 기존 정리에 맡긴다). 열린 신고가 있으면 익명화를 미룬다 |
| 재화·거래 원장 | `gold_ledger`, `item_ledger`, `xp_ledger`, `star_ledger` 등 | 영구 | 계정 id만 남기고 탈퇴 후 5년(`retain_until`) |
| 결제 기록 | `star_orders`, `star_order_events`, `star_paid_lots`, `star_spend_allocs` | 영구 | 5년(`retain_until`) |
| 제재 기록 | `account_sanctions`, `reports`, `economy_holds` | 영구 | 보관(조사 근거). 파기는 5년 일정에 포함 |

`retain_until`(익명화 시점에 서버가 계산): `GREATEST( 결제 기록 마지막 시각 + WITHDRAW_RETAIN_PAID_YEARS, 게임 내 원장 마지막 시각 + WITHDRAW_RETAIN_LEDGER_YEARS, anonymized_at + 최소 1일 )`. 두 값의 기본은 모두 5년이다(사용자 기본값). 게임 내 골드·아이템 원장까지 5년인 근거가 약할 수 있어 두 값을 따로 둔다(15.2절 L1).

## 3. 테이블 변경 (마이그레이션 0027 초안)

### 3.1 변경 요약

| 대상 | 변경 | 이유 |
|---|---|---|
| `accounts` | `anonymized_at` 열, CHECK, `deleted_at` 설명 갱신 | 익명화 완료 구분 |
| `account_withdrawals` (신규) | 탈퇴 요청의 상태 기계·멱등·보류 사유·보관 기한 | 1절 |
| `withdrawn_identities` (신규) | 제재·결제 차단을 재가입으로 피하지 못하게 하는 Steam ID 키 해시 | 9절 |
| `account_destruction_log` (신규) | 파기 관리대장(추가만, PII 없음) | 8절 |
| `refresh_tokens.revoke_reason` | `'withdrawal'` 값 추가 | 탈퇴 폐기 사유 |
| `online_sessions.end_reason` | `'withdrawal'` 값 추가 | 접속 종료 사유 |
| `ledger_block_mutation()` | `dotrpg_purge` 역할의 DELETE만 허용 표에 한해 통과 | 3.4, 8절 |

원장 사유(`gold_ledger.reason` 등)는 **추가하지 않는다**. 탈퇴가 잔액·재고를 움직이지 않기 때문이다(0절 3·4). 재화 변경이 없으므로 "원장 없는 잔액 변경" 규칙에 걸리지 않는다.

### 3.2 SQL 초안

```sql
-- 0027_withdrawal: 회원 탈퇴 (Docs/server/phase12_withdrawal.md)
-- 대상: PostgreSQL 14 이상. 선행: 0026_sealed_box_pass.
--   1. accounts.anonymized_at
--   2. account_withdrawals / withdrawn_identities / account_destruction_log
--   3. refresh_tokens.revoke_reason, online_sessions.end_reason 값 추가
--   4. ledger_block_mutation(): 전용 역할 dotrpg_purge 의 DELETE만 허용 표에 한해 통과

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
       'admin_account_notes']) THEN
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
```

구현 때 확인할 것: 위 CHECK 이름은 PostgreSQL 자동 이름(`<표>_<열>_check`) 가정이다(0026도 같은 방식). 허용 표 배열은 `SELECT tgrelid::regclass FROM pg_trigger WHERE tgfoid = 'ledger_block_mutation'::regproc`로 실제 목록과 대조해 정하고, **계정 한 곳에 속한 표만** 넣는다(공유 기록의 처리는 8.2).

### 3.3 인덱스의 이유 (조회 패턴)

| 인덱스 | 패턴 | 이유 |
|---|---|---|
| `account_withdrawals_one_open` / `_one_done` (유일) | 계정의 현재 요청 조회(W1, 로그인 거절 응답, W3) + 동시 요청 방어 | 계정당 열린 요청 1개를 DB가 보장. 로그인 경로의 `WHERE account_id=$1 AND state='requested'`가 이 인덱스를 쓴다 |
| `account_withdrawals_due` | 익명화 작업: `state='requested' AND NOT manual_hold AND due_at <= now() ORDER BY due_at LIMIT n` | 열린 요청은 소수라 부분 인덱스가 아주 작다 |
| `account_withdrawals_retain` | 파기 작업: `state='completed' AND retain_until <= now()` | 완료 건만 대상 |
| `account_withdrawals_account_time` | 최근 30일 요청 수 한도, 계정 상세의 이력 | `(account_id, requested_at DESC)`로 범위 카운트 |
| `withdrawn_identities.identity_hash` (유일) | 재가입 시 `WHERE identity_hash=$1 AND released_at IS NULL AND expires_at > now()` | 로그인 경로의 단건 조회. 유일 제약이 인덱스를 겸한다 |
| `withdrawn_identities_expires` | 만료 정리 | `purge-daily`의 범위 삭제 |

### 3.4 `dotrpg_purge` 역할 (운영 절차, 마이그레이션 밖)

마이그레이션은 역할 이름을 문자열로 비교할 뿐 역할을 만들지 않는다(생성은 슈퍼유저 작업). 운영에서 `CREATE ROLE dotrpg_purge LOGIN` 후 5년 파기 대상 표에 대한 `SELECT, DELETE` 권한만 부여하고, 접속 문자열은 `PURGE_DATABASE_URL`(비밀값)로 둔다. 앱의 기본 풀은 이 역할로 붙지 않으므로 SQL 주입이 있어도 원장을 지울 수 없다. `WITHDRAW_DESTROY_ENABLED=false`(기본)면 이 연결을 열지 않는다.

## 4. 공개 API (3개) + 기존 변경

공통: 응답은 `{ success, message, data, meta? }` / 실패 `{ success: false, message, errors? }`. 요청은 모두 zod `.strict()`. 클라이언트 버전 검사(`versionCheck({ data: false })`)를 거친다(`/auth/*` 규칙과 같다. 데이터 버전은 검사하지 않는다). 기능 플래그 `WITHDRAW_ENABLED=false`면 W1~W3와 관리자 WD3는 `503 FEATURE_DISABLED`. 멱등성은 `request_id` + `UNIQUE(account_id, request_id)`.

| # | 메서드 | 경로 | 인증 | 하는 일 |
|---|---|---|---|---|
| W1 | GET | `/me/withdrawal` | 액세스 토큰 | 탈퇴 전 확인: 확인 문구, 유예 일수, 손실 요약, 막는 사유 |
| W2 | POST | `/me/withdrawal` | 액세스 토큰 + 재인증 | 탈퇴 요청(즉시 차단, 유예 시작) |
| W3 | POST | `/auth/withdrawal/cancel` | 재인증(토큰 없음) | 유예 안 철회 |

경로 이유: W1·W2는 기존 `GET /me` 옆(`authRoutes.ts` 규칙, 본인 계정 단위), W3는 로그인이 막힌 상태에서 부르므로 `/auth/*`(토큰 없음). 구현은 새 도메인 `server/src/domains/withdrawal/`(routes, controller, service, repository, validation)에 두고 `routes/index.ts`에 등록한다.

### 4.1 W1 `GET /me/withdrawal`

- 인증: 액세스 토큰(`requireAuth`). 읽기 전용, 행을 잠그지 않는다.
- 응답 `200` `data`:

```json
{
  "enabled": true,
  "confirm_phrase": "탈퇴합니다",
  "grace_days": 30,
  "due_at_if_now": "2026-11-07T03:00:00.000Z",
  "reauth": "steam",
  "can_request": true,
  "blockers": [],
  "losses": {
    "characters": [ { "id": "<uuid>", "name": "검사하나", "class": "warrior", "level": 31 } ],
    "gold_total": 1250000,
    "paid_stars": 1200,
    "free_stars": 300,
    "star_debt": 0,
    "unclaimed_mails": 3,
    "active_listings": 2,
    "top_bids": 1
  },
  "notices": [ "paid_stars_forfeited", "refund_follows_steam_policy", "auction_and_mail_lost_after_grace", "friends_party_not_restored" ]
}
```

- `blockers`: 요청을 막는 사유 목록. 지금은 `'payment_open'`(열린 별조각 결제 주문이 있다)뿐이다. 있으면 `can_request=false`.
- `reauth`: `'steam'`(이 계정에 Steam 신원이 있음) 또는 `'dev'`. 클라이언트가 어떤 자격을 모을지 정한다.
- `notices`: 클라이언트가 문구를 조립하는 코드. 서버는 문장을 만들지 않는다. `paid_stars_forfeited`는 `paid_stars>0`일 때만, `refund_follows_steam_policy`는 항상(유료 잔액 환불은 Steam 정책을 따른다는 안내, 15.2절 L5).
- 에러: `401`, `403 ACCOUNT_BANNED`(정지 계정은 이 API를 못 쓴다. 운영자 대행 WD3로 처리, 9.2), `429`, `503 FEATURE_DISABLED`.
- 속도 제한: 계정당 분당 10회(`RATE_WITHDRAW_INFO_PER_MIN`). 멱등성: 읽기.
- 노출 금지: 제재·열린 신고·경제 정지 상태(`defer_reasons`)는 플레이어에게 알리지 않는다(조사 사실 노출 방지). 처리방침의 일반 문구("법령·조사로 삭제가 지연될 수 있다")로 대신한다.

### 4.2 W2 `POST /me/withdrawal`

- 요청:

```
z.strictObject({
  request_id: z.uuid(),
  confirm: z.string().min(1).max(40),        // 서버 상수 확인 문구와 NFC 정규화·양끝 공백 제거 후 일치해야 한다
  ack_progress_loss: z.literal(true),         // 캐릭터·재화·경매·우편이 사라짐을 확인
  ack_paid_loss: z.boolean().optional(),      // paid_stars > 0 이면 true 필수
  reauth: z.discriminatedUnion('provider', [
    z.strictObject({ provider: z.literal('steam'), ticket: <steamBody.ticket 와 같은 규칙> }),
    z.strictObject({ provider: z.literal('dev'), password: z.string().min(8).max(64) }),
  ]),
})
```

받지 않는 값: 유예 일수, 손실 수량, 기한, 사유 텍스트. `confirm`은 사용자가 입력한 확인 문구이지 금액·확률이 아니다.

- 처리(순서가 중요하다):
  1. 플래그·검증·속도 제한(DB 접근 전).
  2. **재인증**(트랜잭션 밖): `steam`이면 `verifyTicket`으로 얻은 SteamID가 이 계정의 `auth_identities(provider='steam').subject`와 같아야 한다. `dev`면 argon2 검증(실패 횟수는 로그인 실패 카운터와 같은 저장소를 쓴다). 실패는 `403 REAUTH_FAILED`. `WITHDRAW_REAUTH_REQUIRED=false`면 이 단계를 건너뛴다(개발 전용, 운영에서 기동 거부).
  3. 5절의 트랜잭션 한 개로 5.1~5.4를 수행한다.
  4. 커밋 뒤 접속 끊기와 대기열 정리(5.5).
- 응답 `201` `data`: `{ withdrawal: { id: "<uuid>", requested_at, due_at, cancel_allowed: true }, logged_out: true }`. 클라이언트는 토큰·로컬 캐시를 버리고 안내 화면으로 간다.
- 에러:

| 코드 | 상태 | 뜻 |
|---|---|---|
| `VALIDATION` | 400 | 형식 오류 |
| `TOKEN_*` | 401 | 기존 인증 오류 |
| `ACCOUNT_BANNED` | 403 | 정지 계정(기존). 9.2 |
| `REAUTH_FAILED` | 403 | 재인증 실패 |
| `CONFIRM_MISMATCH` | 422 | 확인 문구 불일치 |
| `ACK_REQUIRED` | 409 | `errors:{ need: ['paid_loss'] }` 유료 별조각 잔액이 있는데 확인이 없음. W1을 다시 읽고 확인하게 한다 |
| `WITHDRAW_BLOCKED` | 409 | `errors:{ blockers: ['payment_open'] }` 열린 결제 주문. 대사가 끝난 뒤 다시 |
| `WITHDRAW_LIMIT` | 429 | 최근 30일 요청 `WITHDRAW_REQUEST_MAX_PER_30D`(3)회 초과(요청-철회 반복으로 파티·친구를 흔드는 것 방지) |
| `IDEMPOTENCY_MISMATCH` | 422 | 같은 `request_id`에 다른 본문 |
| `RATE_LIMITED` | 429 | 속도 제한 |
| `FEATURE_DISABLED` | 503 | 플래그 꺼짐 |

- 멱등성: `request_id`. 같은 id 재전송은 첫 응답(201)을 그대로 돌려준다. **단 요청이 성공한 뒤에는 토큰이 무효(401)가 되므로**, 응답을 못 받은 클라이언트의 재전송은 보통 `401 TOKEN_INVALID`를 받는다. 클라이언트는 이때 로그인을 시도해 `403 ACCOUNT_WITHDRAWAL_PENDING`이 오면 "요청이 처리됨"으로 본다(클라이언트 계약, 15.0절).
- 속도 제한: 계정당 시간당 3회(`RATE_WITHDRAW_PER_HOUR`), IP당 시간당 10회.

### 4.3 W3 `POST /auth/withdrawal/cancel`

- 요청:

```
z.strictObject({
  request_id: z.uuid(),
  reauth: z.discriminatedUnion('provider', [
    z.strictObject({ provider: z.literal('steam'), ticket: <steamBody.ticket 규칙> }),
    z.strictObject({ provider: z.literal('dev'), login_id: <loginId 규칙>, password: <password 규칙> }),
  ]),
  device: deviceBody.optional(),
})
```

- 처리: 자격으로 계정을 찾는다(Steam 티켓의 SteamID -> `auth_identities`, 또는 개발용 아이디·비밀번호). 신원 행이 없거나(이미 익명화됨) 열린 요청이 없으면 `404 NO_PENDING_WITHDRAWAL`(계정이 있는지 알려 주지 않는 같은 응답). 찾았으면 한 트랜잭션: 캐릭터 행 잠금(id 오름차순) -> 계정 행 잠금 -> 요청 행 잠금 -> 상태가 `requested`이고 `now < due_at`이고 `cancel_allowed`인지 확인 -> `accounts.deleted_at = NULL`, 요청 `cancelled`(`cancelled_via='self'`, `cancel_request_id`), 이름 복원(6.2) -> 커밋.
- 응답 `200` `data`: `{ cancelled: true, renamed_characters: 0 }`. **토큰은 주지 않는다.** 클라이언트는 새 Steam 티켓으로 일반 로그인(`/auth/steam`)을 이어서 부른다(티켓은 1회용이라 철회에 쓴 것을 로그인에 재사용하지 않는다).
- 에러: `400`, `401 STEAM_TICKET_INVALID`(기존 코드), `403 REAUTH_FAILED`, `403 CANCEL_NOT_ALLOWED`(운영자 강제 처리), `404 NO_PENDING_WITHDRAWAL`, `410 WITHDRAWAL_DUE`(기한 경과, 익명화 대기 중), `422 IDEMPOTENCY_MISMATCH`, `429`.
- 멱등성: `request_id`는 `cancel_request_id`로 비교한다. 이미 `cancelled`이고 같은 id면 첫 응답을 돌려준다(그 사이 재로그인했어도 같은 200).
- 속도 제한: IP당 시간당 10회, 계정당 시간당 5회(`RATE_WITHDRAW_CANCEL_*`). 개발용 비밀번호 실패는 로그인 실패 카운터(`login-fail:<id>`)를 공유한다.
- 소유자 확인(IDOR 없음): 본문에 계정 id를 받지 않는다. 자격이 곧 소유 증명이다.

### 4.4 기존 API 변경

| # | 대상 | 변경 |
|---|---|---|
| E1 | `POST /auth/steam` (`authService.steamLogin`) | 신원이 있고 `deleted_at`이 있으며 열린 요청이 있으면 `401 STEAM_TICKET_INVALID` 대신 `403 ACCOUNT_WITHDRAWAL_PENDING` `errors:{ due_at, cancel_allowed }`. 신원이 없으면(익명화됨 또는 처음) 새 계정을 만들되 9.3의 이월 검사를 먼저 한다 |
| E2 | `POST /auth/dev/login` (`authService.login`) | 비밀번호가 맞은 **뒤에만** `deleted_at` 계정에 `403 ACCOUNT_WITHDRAWAL_PENDING`을 준다(틀리면 기존 `INVALID_CREDENTIALS`, 계정 존재를 알리지 않는다) |
| E3 | `POST /auth/refresh` | 변경 없음(토큰을 모두 폐기했으므로 `REFRESH_INVALID`/`REUSED` 경로) |
| E4 | `GET /me` | `data.withdrawal: null`을 더한다(활동 계정은 항상 null이라 사실상 안내용. 유예 중에는 토큰이 없어 호출 불가) |
| E5 | `POST /auth/steam/link` | 변경 없음. 단 유예 계정은 토큰이 없어 못 부른다 |
| E6 | `GET /admin/accounts/{uuid}` (PL2) | `data.withdrawal`(상태, `due_at`, `defer_reasons`, 이력 건수)을 더한다 |
| E7 | WebSocket `/ws`, `/relay` | 새 close 코드 `4012 WITHDRAWN`(`wsProtocol.ts` `CLOSE`) |

## 5. 탈퇴 요청의 정리 규칙 (W2 / WD3 트랜잭션)

### 5.1 락 순서

기존 규칙(① 캐릭터 행 id 오름차순 -> ② `accounts` 행)에 맞춘다: **살아 있는 모든 캐릭터 행 `FOR UPDATE`(id 오름차순) -> `accounts` 행 `FOR UPDATE` -> `account_withdrawals` 행 -> 그 밖(`party_members`, `friendships` 등)**. 경제 요청이 캐릭터 행을 먼저 잠그므로 진행 중인 상점·강화·수령 요청과 직렬화된다(탈퇴 직전 요청이 끝난 뒤에 탈퇴가 반영된다).

### 5.2 검사

1. 이미 열린 요청이 있으면 멱등 재전송(같은 `request_id`)이면 첫 응답, 아니면 `409 WITHDRAWAL_ALREADY_REQUESTED`(토큰 검증과 트랜잭션 사이의 경쟁에서만 도달).
2. 최근 30일 요청 수 < `WITHDRAW_REQUEST_MAX_PER_30D` (`account_withdrawals_account_time`).
3. **막는 사유**: 열린 결제 주문(`star_orders.state IN ('pending_init','created','authorized','finalized')`, 부분 유일 인덱스 `star_orders_one_open`)이 있으면 `WITHDRAW_BLOCKED`. 결제는 돈이 오가는 중이라 대사가 끝나야 한다(수 분).
4. 유료 별조각(`star_wallets.paid_balance > 0`)이 있는데 `ack_paid_loss`가 아니면 `ACK_REQUIRED`.

### 5.3 쓰기 (같은 트랜잭션)

| 영역 | 처리 | 철회 시 복구 |
|---|---|---|
| 로그인 차단 | `accounts.deleted_at = now()`, `active_family_id/install_id/device_hash/session_at = NULL` | `deleted_at` NULL로 복구 |
| 액세스 토큰 | 따로 없음: `verifyAccessToken`이 매 요청 `deleted_at`을 확인해 즉시 `401 TOKEN_INVALID` | - |
| 갱신 토큰 | 이 계정의 **모든 family**를 폐기(`UPDATE refresh_tokens SET revoked_at=now(), revoke_reason='withdrawal' WHERE account_id=$1 AND revoked_at IS NULL`) | 재로그인 |
| 접속 | `online_sessions` 종료(`end_reason='withdrawal'`) | 재접속 |
| 파티 | 속한 파티를 기존 이탈 규칙으로 나간다(방장이면 가장 오래된 멤버가 이어받고, 혼자면 해산). `party_applications` 대기 건 `cancelled`. 진행 중 `party_run_members`는 기존 이탈(`left`) 규칙 | 복구 안 됨 |
| 초대 | `party_invites`의 보낸 것·받은 것 중 `pending` -> `cancelled`(`silent=true`) | 복구 안 됨 |
| 필드 세션 | 멤버를 `left`(`left_reason='left'`) | 복구 안 됨 |
| 솔로 던전 판 | `playing` 판은 기존 방치 규칙과 같은 상태(`abandoned`)로 닫는다. 보상 없음 | 복구 안 됨 |
| 친구 | `accepted` -> `removed`, `pending` -> `cancelled`, `ended_by_account_id` = 이 계정, `silent=true` | 복구 안 됨(친구는 다시 맺는다) |
| 차단(내가 건 것) | 유지 | 그대로 |
| 캐릭터 이름 | 살아 있는 각 캐릭터를 자리표시 `'탈퇴' || 캐릭터 uuid 앞 6자리(hex)`(8자, 이름 규칙 `^[가-힣A-Za-z0-9]{2,8}$` 만족)로 바꾸고 원래 이름을 `saved_character_names`에 보관. 유일 인덱스 충돌(23505)이면 uuid의 다른 6자리로 재시도 | 이름 복원(6.2) |
| 경매 | **건드리지 않는다.** 진행 중 등록·입찰은 기존 마감 정산 틱이 그대로 끝내고 결과(대금·낙찰품·반환)는 우편으로 쌓인다 | 철회하면 수령 가능 |
| 우편 | **건드리지 않는다.** 미수령 우편은 30일 기한 폐기(기존 `expireMailById`, 로그인 불필요). 새 캠페인 우편은 배달되지 않는다(`campaignDelivery.ts`가 `deleted_at IS NULL` 확인) | 기한 안이면 수령 가능 |
| 재화 | 골드·아이템·별조각·클리어권 **변경 없음** | 그대로 |
| 제재·경제 정지·신고 | 유지 | 그대로 |
| 멱등·증거 | `account_withdrawals` INSERT(`loss_snapshot`, `ack_paid_loss`, `saved_character_names`), `request_log` INSERT | - |

### 5.4 경매·우편·유료 별조각 결정 근거 (D4)

- 경매 등록품·입찰금을 **자동 취소하지 않는 이유**: 입찰이 걸린 등록은 취소가 금지이고, 상대 입찰자의 예치금이 걸려 있다. 정산 틱이 정상 종료하면 상대는 아무 영향이 없고 판매자 몫만 우편에 쌓인다. 새 취소 로직을 만들지 않는다.
- **막지 않는 이유**: 캐릭터 삭제처럼 "경매가 끝날 때까지 탈퇴 불가"로 하면 탈퇴를 가입보다 어렵게 만드는 셈이다(15.2절 L8). 대신 W1이 건수를 보여 주고 `ack_progress_loss`로 확인받는다. 유예 30일 안에 우편 기한이 대부분 끝나므로 "철회하지 않으면 못 받는다"가 사실상 규칙이다.
- 유료 별조각: 요청 시점에 소멸시키지 않는다(철회 가능). 잔액은 그대로 두고 로그인이 불가능해 쓰이지 않는다. **환불·차지백 회수는 계속 정상 동작한다**(주문·로트·부채 모두 `account_id`로 이어져 있고 대사·감시 작업은 `deleted_at`을 보지 않는다). 환불은 Steam 정책을 따른다는 안내(`notices`)와 `ack_paid_loss` 확인이 법적 손실 고지의 전부이며, 소멸 vs 환불 의무는 법무 항목이다(L5).

### 5.5 커밋 뒤

- `pg_notify('dotrpg_session', '<accountId>:withdrawn')`을 트랜잭션 안에서 보낸다(`beginSession`과 같은 방식, 커밋 때 전달). `sessionListener.handleSessionNotice`가 family 값이 `'withdrawn'`이면 `/ws`를 `CLOSE.WITHDRAWN`(4012, 재연결 안 함)으로 닫고 `relayHub().kickAccount`를 부른다. 기존 `REPLACED` 경로와 구분한다.
- 매칭 대기열(`domains/match/queueStore.ts`, 메모리)에서 제거: `afterCommit` 훅. 대기열 항목이 남아도 매칭 시 계정 상태 검사로 걸러지지만 즉시 제거한다.
- `metrics`: 탈퇴 요청 수(`withdrawal_requested_total`).

## 6. 유예·철회

### 6.1 결정안: 철회를 허용한다 (D2)

- **유예 30일(`WITHDRAW_GRACE_DAYS`) 안에는 같은 Steam 계정(또는 개발용 계정)으로 철회할 수 있다.** 이유: 실수·충동 탈퇴 복구, 계정 탈취로 인한 탈퇴 방어(재인증이 있으므로 탈취자도 Steam 자격이 필요하지만 대비책), 유료 별조각이 있는 사용자 보호.
- 철회는 로그인과 별개의 W3로 한다. 로그인 시도(E1·E2)는 `403 ACCOUNT_WITHDRAWAL_PENDING`과 `due_at`을 줘서 클라이언트가 "탈퇴 철회" 화면으로 안내한다. 로그인 자체가 철회를 겸하지 않는다(실수로 켠 게임이 철회가 되는 것을 막는다).
- 철회 한도: 요청은 최근 30일 3회까지(`WITHDRAW_REQUEST_MAX_PER_30D`).
- 운영자 강제 처리 건(`cancel_allowed=false`, owner만 지정)은 본인 철회 불가.

### 6.2 철회가 되돌리는 것과 되돌리지 않는 것

되돌린다: 로그인 가능, 캐릭터 이름, 재화·우편·경매 결과(기한 안), 차단 목록, 제재 상태(그대로 유지).
되돌리지 않는다: 친구 관계, 파티·초대·대기열, 진행 중이던 던전 판, 세션(재로그인 필요). 클라이언트 안내 문구에 포함한다(`notices: friends_party_not_restored`).

이름 복원: `saved_character_names`의 각 이름을 되돌린다. 유예 중에 같은 이름을 다른 사람이 새 캐릭터로 만들었으면(자리표시로 바뀌며 이름이 풀리므로 가능) 원래 이름 앞 6자 + 2자리 숫자로 복원하고 `renamed_characters`를 올려 응답한다. 이름 변경 기능이 게임에 없으므로 이 경우는 운영자가 CLI로 수동 정리한다(15.1절 D5의 대안: 이름을 계속 예약하려면 `characters` 유일 인덱스를 건드려야 해 권장하지 않음).

## 7. 익명화 작업 `withdrawal-anonymize`

- 등록: `ops/jobs/index.ts` `registerJob({ name: 'withdrawal-anonymize', schedule: { kind: 'every', minutes: 10 }, run: withdrawalAnonymizeJob })`. `MANUAL_JOBS`에도 추가(OP3 수동 실행). 파일 `server/src/ops/jobs/withdrawalJobs.ts`(로직은 `domains/withdrawal/anonymizeService.ts`).
- JobRunner 규칙(7단계 6.1)을 따른다: advisory lock으로 한 곳에서만, `job_runs` 기록, 종료 신호는 계정 사이에서 확인.
- 한 실행에 최대 `WITHDRAW_JOB_BATCH`(20)개 계정, **계정마다 자기 트랜잭션**. 정리 대상이 계정의 행이라 크기가 작다(계정당 최대 수천 행).

### 7.1 대상 선택

```sql
SELECT id, account_id FROM account_withdrawals
 WHERE state = 'requested' AND NOT manual_hold AND due_at <= now()
 ORDER BY due_at
 LIMIT $batch FOR UPDATE SKIP LOCKED;   -- account_withdrawals_due
```

### 7.2 계정별 순서 (한 트랜잭션, 모든 단계는 멱등)

1. 캐릭터 행 잠금(id 오름차순, 삭제된 것 포함) -> `accounts` 행 -> 요청 행. 상태가 아직 `requested`이고 `deleted_at`이 있는지 재확인(그 사이 철회됐으면 건너뜀).
2. **보류 판정**(`defer_reasons` 계산, `defer_checked_at` 갱신):

| 사유 | 조건 |
|---|---|
| `sanction` | `account_sanctions`에 `revoked_at IS NULL`이고 `(ends_at IS NULL OR ends_at > now())`인 `ban` 또는 `chat_mute` (`warning`은 제외) |
| `economy_hold` | `economy_holds.state IN ('active','clawed_back')` |
| `open_report` | `reports.target_account_id = 이 계정`이고 `state IN ('open','reviewing')` |
| `payment_open` | 열린 `star_orders`, 또는 `payment_flags.state='open' AND severity >= 2` |

   사유가 하나라도 있고 `now() < due_at + WITHDRAW_DEFER_MAX_DAYS`(180일)이면 `defer_reasons`만 갱신하고 커밋(종료). 상한을 넘으면 보류를 풀고 계속 진행하되 이월 표시(9.3)를 반드시 만든다. 보류 목록과 남은 일수는 WD1에서 운영자가 본다(조사가 끝났으면 `sanction`·`hold` 정리가 보류도 푼다).
3. **이월 표시 판단**: 활성 정지, 결제 정지(`payment_profiles.status='blocked'`) 또는 `star_wallets.debt > 0`(부채는 환불 악용 신호라 `carry_payment_block='chargeback'`), 활성 경제 정지, 보류 상한 도달 중 하나라도 해당하고 이 계정에 Steam 신원이 있으면 `withdrawn_identities`를 upsert(해시 계산 9.3).
4. **지우기·익명화**(10절 표의 "익명화 때" 열을 그대로 실행): `auth_identities` 삭제, 남은 `refresh_tokens` 삭제, `login_events`·`account_devices`·`account_ips`·`online_sessions`·`play_time_hourly`·`income_hourly`·`request_log` 삭제, `anomaly_log`의 `ip_cluster`·`device_limit` 행 삭제, `chat_messages` 삭제(보낸 것·받은 것), 내가 건 `blocks` 삭제, 나를 차단한 `blocks.blocked_name` 자리표시, `reports.target_name` 자리표시, `party_run_members`의 `device_hash/steam_key/install_id` NULL, 열린 `friendships`·`party_invites` 잔여 정리, `mail_campaign_deliveries` 등은 유지.
5. `characters`: 모든 캐릭터 `deleted_at = now()`(이미 삭제된 것은 유지), 이름이 자리표시가 아니면 자리표시로 교체(운영자 대행·마이그레이션 전 요청 방어).
6. `accounts`: `anonymized_at = now()`, `last_login_at/prev_login_at/last_character_id = NULL`, `active_*` NULL.
7. `retain_until` 계산(2절 식), 요청 행을 `completed`로 바꾸고 `saved_character_names = NULL`.
8. `job_runs.detail`에 표별 삭제·갱신 행 수를 합산한다(계정 uuid는 넣지 않고 합계만. 단건 감사는 요청 행 자체가 근거).

실패 처리: 계정 하나의 오류는 그 트랜잭션만 롤백하고 다음 계정으로 간다. 연속 3회 실패한 요청은 `job_runs.detail.stuck`에 올려 경보(7단계 8.3의 작업 실패 알림 규칙). 데이터는 롤백되므로 "반쯤 익명화된" 계정이 생기지 않는다.

### 7.3 `withdrawal-anonymize`가 건드리지 않는 것

원장 6종(`gold_ledger`, `item_ledger`, `xp_ledger`, `star_ledger`, `sweep_ticket_ledger`, `enhance_log`), 지갑·재고·결제·경매·우편·던전 기록, `report_lines`(수정 불가), 관리자 표 전부. 10절 표가 정본이다.

### 7.4 기존 정리 작업 변경

| 작업 | 변경 |
|---|---|
| `purge-daily` (`ops/jobs/purge.ts`) | 단계 추가: ① `withdrawn_identities`의 `expires_at < now()` 삭제, ② `star_orders` 정리 문장에 `device_hash = NULL`을 `ip`와 같은 `PAY_IP_RETENTION_DAYS` 일정으로 포함 |
| `integrity-nightly` (`ops/jobs/integrity.ts`) | 점검 I7 추가(11.3) |
| 기존 `chat_messages`·`login_events` 등 | 변경 없음(정상 일정이 계속 돈다) |

## 8. 5년 파기 `withdrawal-destroy`

### 8.1 규칙

- 대상: `account_withdrawals.state='completed' AND retain_until <= now()`. 일정: 매일 KST 04:55(`payment-report` 04:50 뒤). 일정 계산은 JobRunner의 `daily_kst`가 한다(날짜 함수를 새로 만들지 않는다).
- **기본은 dry-run**: `WITHDRAW_DESTROY_ENABLED=false`면 대상 계정 수와 표별 파기 예정 행 수만 `job_runs.detail`에 기록하고 아무것도 지우지 않는다. 켜려면 `PURGE_DATABASE_URL`(역할 `dotrpg_purge`, 3.4)이 필요하다. 이 방식이면 5년 동안 코드가 한 번도 실삭제 경로를 실행하지 않는 상태로 출시할 수 있고, 켜기 전에 시험 데이터로 검증한다.
- 계정마다 자기 트랜잭션. 끝나면 `account_destruction_log` 한 줄(표별 행 수)과 `account_withdrawals` 행 삭제.

### 8.2 공유 기록의 규칙

경매 등록·체결·입찰, 우편, 친구, 신고처럼 **두 계정이 함께 가진 기록**은 한쪽이 파기 대상이어도 상대가 활동 중이면 지우지 않는다. 처리:

1. **계정 한 곳에만 속한 표**(자기 캐릭터의 원장, 지갑, 재고, 결제 6종, 소탕, 진행 상태, 업적 등)는 `retain_until`이 지나면 계정 단위로 파기한다. 자식 -> 부모 순서(원장 -> 재고 -> 캐릭터).
2. **공유 표**(`auction_*`, `mails`의 경매 건, `reports`, `friendships`)는 행의 모든 당사자 계정이 `completed`이고 각자 `retain_until`이 지났을 때만 파기한다. 하나라도 활동 중이면 남긴다(기존 "지우지 않는다" 정책이 그대로 적용된다).
3. `accounts`/`characters` 껍데기는 어떤 행도 참조하지 않을 때만 삭제한다(`DELETE` 시 FK 위반 `23503`이면 껍데기를 남기고 `account_destruction_log.tables`에 `shell_kept: true`). 껍데기에는 개인 정보가 없다(uuid, 생성 시각, 직업·레벨).
4. 파기 대상 표 목록과 순서는 **정책 레지스트리** `domains/withdrawal/withdrawalPolicy.ts`(10절 표와 1:1)가 정본이고, 미분류 표가 있으면 테스트가 실패한다(12절 T-W30).

## 9. 재가입과 제재 중 탈퇴

### 9.1 같은 Steam ID 재가입 규칙

| 시점 | 동작 |
|---|---|
| 유예 중 | 새 계정을 만들지 못한다. 로그인은 `403 ACCOUNT_WITHDRAWAL_PENDING`(철회하거나 기다리거나) |
| 익명화 직후 | **새 계정으로 가입할 수 있다.** `auth_identities`의 `UNIQUE(provider, subject)`가 풀려 `steamLogin`이 평소처럼 계정을 만든다. 옛 계정의 캐릭터·재화·우편·별조각은 복구되지 않는다 |
| 재가입 쿨다운 | 기본 0일(`WITHDRAW_REJOIN_COOLDOWN_DAYS`). 요청부터 익명화까지 이미 30일이라 별도 대기를 두지 않는다. 신규 계정에 이미 걸려 있는 제한(신규 결제 한도 `tier=new`, 신규 경매 자격)이 일회성 이득을 막는다 |
| 이월 사유가 있을 때 | 9.3 |

### 9.2 제재 중 계정의 탈퇴 (제재 회피 방지)

| 상태 | 처리 |
|---|---|
| 활성 정지(`ban`) | 액세스 토큰이 `403 ACCOUNT_BANNED`라 **본인이 W2를 부를 수 없다.** 정보주체 요청은 지원 채널로 받아 운영자가 WD3로 대행한다(`source='admin'`). 대행도 보류 판정을 받는다 |
| 활성 채팅 금지, 열린 신고의 대상, 활성 경제 정지, 결제 미결 | 요청은 받는다(탈퇴할 권리를 막지 않는다). **익명화만 미룬다**(7.2의 2번). 로그인은 요청 즉시 막힌다 |
| 보류 상한(180일) 도달 | 보류를 풀고 익명화한다. 대신 Steam ID 해시로 제재를 이월한다(9.3) |
| 제재 기록(`account_sanctions`), 신고(`reports`), 경제 정지, 이상 기록(심각도 2 이상) | 익명화 후에도 보관한다(조사·분쟁 근거). `account_id`만 남고 신원은 없다 |

### 9.3 이월 표시 `withdrawn_identities`

- 키: `identity_hash = HMAC-SHA256(WITHDRAW_ID_HMAC_KEY, steam_id 문자열)` hex. **단순 SHA-256은 쓰지 않는다**(17자리 숫자 공간이라 사전 대입으로 되돌려진다). 키는 환경변수 비밀값이며 없으면 `WITHDRAW_ENABLED=true`로 서버가 뜨지 않는다. 키를 바꾸면 기존 표시가 무효가 되므로 로테이션은 새 키로 재계산이 아니라 병행(`identity_hash`를 두 키로 조회)해야 한다(운영 항목, 15.2절 L6).
- 만드는 경우: 활성 정지, 결제 정지·부채, 활성 경제 정지, 보류 상한 도달. 만들 필요가 없으면 **아무것도 남기지 않는다**(데이터 최소화).
- 보관: `expires_at` = 이월 사유의 끝(정지 종료 등) 이되 `created_at + WITHDRAW_TOMBSTONE_MAX_DAYS`(1095일) 이하. 영구 정지는 상한으로 끊긴다(15.2절 L6).
- 재가입 검사(`steamLogin` 계정 생성 직전, 같은 트랜잭션): `identity_hash`로 조회(`released_at IS NULL AND expires_at > now()`).
  - `carry_ban_until > now()`: 계정을 만들지 않고 `403 ACCOUNT_BANNED`(`banned_until`).
  - 그 밖의 이월: 계정을 만들고 `payment_profiles(status='blocked', block_reason=carry_payment_block, blocked_by='system')` 및/또는 `economy_holds(kind='manual', state='active', evidence={ source:'withdrawn_identity' })`를 건다. 해제는 운영자.
- 개발용 계정(`provider='dev'`)과 `steam_owner_id`(패밀리 공유 소유자)는 이월하지 않는다(개발용은 시험 계정, 소유자 키는 이 단계 범위 밖).

## 10. 테이블별 처리 표

범례: **T0** 요청 시점(5.3), **T1** 익명화 시점(7.2), **T2** 이후 보관과 파기. "삭제"는 DELETE, "익명화"는 UPDATE로 값 교체, "보관"은 행 유지. "기존 일정"은 이미 있는 정리 작업이 그대로 지운다.

이 표는 `server/schema.sql`과 마이그레이션 0010~0019·0024~0026의 표 기준이다. 정본은 구현의 `withdrawalPolicy.ts`이며 12절 T-W30이 표와 DB를 대조한다.

### 10.1 계정·인증·접속

| 표 | 식별 정보 | T0 | T1 | T2 |
|---|---|---|---|---|
| `accounts` | uuid, 로그인 시각 | `deleted_at`, `active_*` NULL | `anonymized_at`, `last/prev_login_at`·`last_character_id` NULL | 껍데기 보관, 5년 뒤 참조가 없으면 파기 |
| `auth_identities` | Steam ID, 개발용 ID, 비밀번호 해시, `steam_owner_id` | 유지(철회용) | **삭제** (필요 시 9.3 해시) | - |
| `refresh_tokens` | `device_hash`, `install_id` | 전부 폐기 | **삭제** | - |
| `login_events` | IP, 기기, Steam ID | - | **삭제** | 정상은 90일 |
| `account_devices`, `account_ips` | 기기, IP | - | **삭제** | 정상은 180일 |
| `online_sessions` | IP, 기기 | 종료 | **삭제** | 정상은 끝난 뒤 7일 |
| `play_time_hourly`, `income_hourly` | 캐릭터 행동 집계 | - | **삭제** | 정상은 35일 |
| `request_log` | 응답 JSON | - | **삭제** | 정상은 7일 |
| `anomaly_log` | `detail`에 IP·기기가 들어갈 수 있다 | - | `kind IN ('ip_cluster','device_limit')` **삭제**, 나머지 **보관** | 기존 일정(심각도 1: 30일, 2 이상: 180일) |
| `kill_log`, `drops` | - | - | 변경 없음 | 기존 일정(7일, 이미 지남) |
| `account_withdrawals` | - | INSERT | `completed`, 이름 보관값 NULL | `retain_until` 뒤 삭제(8절) |
| `withdrawn_identities` | Steam ID 키 해시 | - | 조건부 INSERT | `expires_at`(최대 3년) 뒤 삭제 |
| `account_destruction_log` | uuid | - | - | 영구(PII 없음) |

### 10.2 캐릭터·진행·재화 (보관)

| 표 | T0 | T1 | T2 |
|---|---|---|---|
| `characters` | `name` 자리표시 | `deleted_at`(전부), 이름 확인 | 보관(직업·레벨·골드 합), 5년 파기 |
| `character_state`, `character_node_state`, `character_chests`, `site_deliveries`, `quest_claims`, `character_enhance_pity`, `character_achievements`, `character_career`, `character_career_trials`, `kill_stats`, `raid_claims`, `dungeon_runs`, `dungeon_sweeps`, `revive_log` | - | 보관 | 5년 파기 (비식별 진행 상태. 지울지는 D10) |
| `character_items` | - | 보관 | 5년 파기 (원장 보존식 I2) |
| `gold_ledger`, `item_ledger`, `xp_ledger`, `enhance_log` | - | 보관 (추가 전용) | 5년 파기 (`dotrpg_purge`만) |
| `sweep_ticket_lots`, `sweep_ticket_ledger`, `account_week_counters` | - | 보관 | 5년 파기. 주간 카운터는 기존 60일 일정 |
| `account_level_rewards`, `account_growth_pass`, `account_pass_claims`, `account_sealed_state`, `sealed_pulls`, `account_cosmetics`, `account_collections`, `gacha_pulls`, `star_synth_log` | - | 보관 (확률·구매 이력은 분쟁 근거) | 5년 파기 |

### 10.3 결제 (보관)

| 표 | 식별 정보 | T0 | T1 | T2 |
|---|---|---|---|---|
| `star_wallets`, `star_ledger`, `star_paid_lots`, `star_spend_allocs` | `account_id`만 | 열린 주문이 있으면 요청 거절 | 보관(환불·차지백 회수 정상 동작) | 5년 파기 |
| `star_orders` | `steam_id`, `ip`, `device_hash` | - | **`steam_id`는 결제 기록으로 보관**, `ip`·`device_hash`는 기존 180일 일정 | 5년 파기 |
| `star_order_events`, `star_admin_grants`, `payment_profiles`, `payment_flags` | `account_id`만 | - | 보관 | 5년 파기 |
| `star_rates_snapshots` | 계정 무관 | - | - | 영구 |

### 10.4 소셜·신고·제재

| 표 | 식별 정보 | T0 | T1 | T2 |
|---|---|---|---|---|
| `chat_messages` | 닉네임, 본문 | - | **삭제**(보낸 것·받은 것) | 정상은 7일 |
| `friendships` | `account_id`만 | 모두 `removed`/`cancelled` | 보관 | 기존 일정(끝난 뒤 90일) |
| `blocks` | `blocked_name` | 유지 | 내가 건 행 **삭제**, 나를 차단한 행 `blocked_name` **익명화** | 해제 행은 기존 일정(90일) |
| `party_invites` | - | 대기 건 `cancelled` | 보관 | 기존 일정(7일) |
| `parties`, `party_members`, `party_applications`, `party_runs`, `party_run_host_reports` | - | 이탈·취소 | 보관 | 나간 행은 기존 일정(30일), 나머지 5년 파기 |
| `party_run_members` | `device_hash`, `steam_key`, `install_id` | 이탈 | 세 열 **NULL**(익명화) | 5년 파기 |
| `field_sessions`, `field_session_members`, `relay_room_stats` | - | 이탈 | 보관 | 기존 일정 |
| `reports` | `target_name` | - | 대상이 이 계정인 행 `target_name` **익명화**. 열린 신고가 있으면 T1 자체를 보류 | 보관, 5년 파기(상대 계정 규칙 8.2) |
| `report_lines` | 닉네임, 채팅 본문 | - | **변경 없음**(UPDATE 금지 트리거) | 신고 종결 후 180일(기존 일정, 탈퇴가 앞당기지 않는다) |
| `account_sanctions`, `economy_holds` | 메모 | - | 보관 | 5년 파기 (조사 근거) |
| `admin_account_notes`, `held_run_reviews` | 운영 메모 | - | 보관. 메모에 Steam ID·실명을 쓰지 않는 규칙을 운영 문서에 명시 | 5년 파기(`admin_account_notes`만 허용 표) |
| `admin_audit_log` | `target_uuid`(계정 uuid)만 | `account.withdraw` 계열 기록 | 보관 | 영구(PII 없음, 감사 근거) |

### 10.5 경매·우편 (보관)

| 표 | T0 | T1 | T2 |
|---|---|---|---|
| `auction_listings`, `auction_bids` | 건드리지 않음(정상 마감) | 보관 | 8.2의 공유 기록 규칙 |
| `auction_trades`, `auction_sinks`, `auction_flags`, `auction_trade_flags`, `auction_price_daily` | - | 보관 | 8.2 (시세 표는 계정 무관, 영구) |
| `mails`, `mail_attachments`, `mail_campaign_deliveries` | 건드리지 않음 | 보관(미수령은 기한 폐기) | 8.2 |

## 11. 기존 코드에 닿는 지점

### 11.1 서버 (경로는 `server/` 기준)

| 파일 | 변경 |
|---|---|
| `src/middleware/authMiddleware.ts` | **변경 없음.** `row.deleted_at`이면 `401 TOKEN_INVALID`가 이미 있다. 회귀 테스트만 추가(T-W5) |
| `src/domains/auth/authService.ts` | `login`: 비밀번호 확인 뒤 `deleted_at`이면 `ACCOUNT_WITHDRAWAL_PENDING`. `steamLogin`: 같은 분기 + 계정 생성 직전 이월 검사(9.3). `getMe`에 `withdrawal: null` |
| `src/domains/auth/authRepository.ts` | `findAccountBySteam`는 `deleted_at`을 이미 돌려준다. 열린 요청 조회, 모든 family 폐기(`revokeAllForAccount`, `RevokeReason`에 `'withdrawal'`) 추가 |
| `src/domains/auth/authRoutes.ts` | W3를 `/auth/*` 규칙(`clientOnly`, 토큰 없음)으로 등록하거나 새 도메인 라우터에서 등록 |
| `src/domains/withdrawal/**` (신규) | `withdrawalRoutes/Controller/Service/Repository/Validation`, `anonymizeService.ts`, `withdrawalPolicy.ts`(레지스트리) |
| `src/routes/index.ts` | `createWithdrawalRouter()` 등록(`createAuthRouter()` 다음, 계정 단위라 `/characters` 앞) |
| `src/domains/antiabuse/sessionService.ts` | `SESSION_CHANNEL` 알림 형식에 `withdrawn` 값 허용(상수 `WITHDRAWN_FAMILY = 'withdrawn'`) |
| `src/domains/antiabuse/sessionListener.ts` | `handleSessionNotice`: family가 `withdrawn`이면 `CLOSE.WITHDRAWN`으로 닫고 중계 연결도 끊는다 |
| `src/domains/chat/wsProtocol.ts` | `CLOSE.WITHDRAWN: 4012` |
| `src/domains/match/queueStore.ts` | 계정 제거 함수 호출(`afterCommit`) |
| `src/domains/party/*`, `partyinvites/*Repository.ts`, `fieldsessions/*`, `friends/friendsRepository.ts`, `social/socialTx.ts` | 탈퇴 정리에서 기존 이탈·취소 함수를 재사용(새 규칙을 만들지 않는다). 이 함수들이 트랜잭션 클라이언트를 받는지 확인하고 아니면 받도록 한다 |
| `src/domains/characters/characterRepository.ts` | 이름 교체·복원 쿼리. `characterService.deleteCharacter`는 변경 없음 |
| `src/domains/mail/campaignDelivery.ts` | 변경 없음(`a.deleted_at IS NULL` 확인이 이미 있다) |
| `src/admin/economyholds/economyHoldsRepository.ts` (67행) | `accounts WHERE uuid=$1 AND deleted_at IS NULL`을 `AND anonymized_at IS NULL`로 바꾼다. **그대로면 유예 중인 계정에 운영자가 경제 정지를 걸 수 없다** |
| `src/admin/sanctions/*`, `src/admin/players/playersRepository.ts` | 유예 계정에도 제재·조회가 되는지 확인(현재 `deleted_at` 필터가 없어 동작해야 한다). `playersService`의 `deleted` 플래그는 유지하고 `withdrawal` 요약을 더한다 |
| `src/admin/withdrawals/**` (신규) | WD1~WD8 (`Validation/Repository/Service/Controller/Routes`), `adminServer.ts`에 마운트, `admin/cli/*`에 명령 |
| `src/ops/jobs/withdrawalJobs.ts` (신규), `ops/jobs/index.ts` | `withdrawal-anonymize`(10분), `withdrawal-destroy`(KST 04:55) 등록, `MANUAL_JOBS`에 두 이름 추가 |
| `src/ops/jobs/purge.ts` | 7.4 |
| `src/ops/jobs/integrity.ts` | I7 (11.3) |
| `src/config/env.ts` | 13절 변수, 운영에서 `WITHDRAW_ENABLED=true`이고 `WITHDRAW_ID_HMAC_KEY`가 없으면 기동 거부, `WITHDRAW_REAUTH_REQUIRED=false`는 운영 기동 거부 |
| `src/domains/payments/paymentsService.ts` | 변경 없음. 대사·감시·환불 회수가 `deleted_at`을 보지 않는지 테스트로 고정(T-W16) |

### 11.2 스키마·문서

- `server/migrations/0027_withdrawal.sql` 신규, `server/schema.sql` 맨 아래에 합치고 머리말 "기능별 테이블 사용처" 표에 아래 행을 더한다. 머리말의 "지우지 않는다" 절에 "탈퇴 계정은 5년 뒤 파기(8절)"를 한 줄 더한다.

| 기능 | 읽기 | 쓰기 |
|---|---|---|
| 탈퇴 확인 W1 | accounts, characters(골드 합), star_wallets, mails, auction_listings, auction_bids, star_orders | - |
| 탈퇴 요청 W2 | characters(행 잠금), accounts(행 잠금), account_withdrawals, auth_identities, star_wallets, star_orders, party_members, party_applications, party_invites, party_run_members, field_session_members, friendships, dungeon_runs | accounts(deleted_at, active_*), account_withdrawals, characters.name, refresh_tokens(revoked), online_sessions(ended), party_members·party_applications·party_invites·party_run_members·field_session_members, friendships, dungeon_runs(abandoned), request_log |
| 탈퇴 철회 W3 | auth_identities, accounts(행 잠금), characters(행 잠금), account_withdrawals(행 잠금) | accounts.deleted_at, account_withdrawals, characters.name |
| 로그인 거절·재가입 검사 (E1, E2) | accounts, auth_identities, account_withdrawals, withdrawn_identities | accounts, auth_identities, payment_profiles, economy_holds(이월 시) |
| 익명화 작업 | account_withdrawals, accounts, characters, account_sanctions, economy_holds, reports, star_orders, payment_flags, payment_profiles, star_wallets, 10절 표의 삭제·익명화 대상 | 10절 T1 열, account_withdrawals, withdrawn_identities, job_runs |
| 5년 파기 작업 | account_withdrawals, 10절 보관 표(dry-run은 건수만) | (실삭제) 10절 보관 표, account_destruction_log, account_withdrawals, job_runs |
| 관리자 WD1~WD8 | account_withdrawals, accounts, withdrawn_identities, admin_users | account_withdrawals, withdrawn_identities, admin_audit_log |

### 11.3 정합성 점검 I7 (`integrity-nightly`, 읽기 전용)

익명화 누락과 되돌림을 야간에 잡는다. 건수 > 0이면 기존 규칙대로 긴급 알림.

| # | 점검 | 불일치의 뜻 |
|---|---|---|
| I7a | `accounts.anonymized_at IS NOT NULL`인데 `auth_identities`·`refresh_tokens`·`login_events`·`account_devices`·`account_ips`·`online_sessions` 행이 남아 있다 | 익명화 누락 |
| I7b | 위 계정에 `characters.deleted_at IS NULL`이거나 이름이 자리표시가 아닌 캐릭터가 있다 | 이름 노출 |
| I7c | `account_withdrawals.state='requested'`인데 `accounts.deleted_at IS NULL`(또는 반대) | 상태 어긋남 |
| I7d | `state='requested'`이고 `due_at`이 `WITHDRAW_DEFER_MAX_DAYS`를 넘게 지났다 | 작업 정지 |

기존 I1~I3(골드·아이템 보존식)은 탈퇴가 잔액·재고를 건드리지 않으므로 수정이 필요 없다. 이를 T-W27이 고정한다.

## 12. 테스트 목록

`server/test/withdrawal.test.ts`(요청·철회·로그인), `withdrawalAnonymize.test.ts`(작업), `withdrawalAdmin.test.ts`(관리자), 기존 `auth.test.ts`·`steam.test.ts` 확장. 가짜 Steam(`mock:` 티켓)을 쓴다.

**요청(W2)**
- T-W1 재인증 없음/틀림 -> `403 REAUTH_FAILED`, 상태 변화 없음. 다른 계정의 Steam 티켓으로는 통과하지 않는다.
- T-W2 확인 문구 불일치(`CONFIRM_MISMATCH`), 공백·유니코드 정규화(NFC) 차이는 통과.
- T-W3 유료 별조각이 있는데 `ack_paid_loss` 없음 -> `ACK_REQUIRED`. 무료 잔액만이면 불필요.
- T-W4 열린 결제 주문 -> `WITHDRAW_BLOCKED`, 주문 종료 뒤 통과.
- T-W5 요청 직후 같은 액세스 토큰으로 `GET /me`, 경제 요청이 모두 `401`. WebSocket이 `4012`로 닫힌다. 중계 연결도 끊긴다.
- T-W6 모든 refresh 토큰 family가 `revoke_reason='withdrawal'`로 폐기, `/auth/refresh`가 거절.
- T-W7 파티 방장 탈퇴 시 인계, 신청·초대·필드 세션·대기열 정리. 솔로 던전 `playing` 판이 `abandoned`.
- T-W8 친구가 `removed`, 다른 사람의 친구 목록·파티 목록·경매 판매자 표시에 원래 이름이 나오지 않는다(자리표시).
- T-W9 멱등: 같은 `request_id` 재전송은 첫 응답, 다른 본문은 `IDEMPOTENCY_MISMATCH`, 요청 뒤 재전송은 `401`(문서화된 동작).
- T-W10 최근 30일 4번째 요청 -> `WITHDRAW_LIMIT`.
- T-W11 동시성: 같은 계정에 W2 두 개를 동시에 보내면 `account_withdrawals` 1행. 경제 요청(상점 구매)과 W2를 동시에 보내도 원장·잔액이 일관된다(락 순서).
- T-W12 `GET /me/withdrawal`의 손실 요약이 실제 잔액·건수와 같고 `defer_reasons`를 노출하지 않는다.

**로그인·철회(E1, E2, W3)**
- T-W13 유예 중 Steam 로그인 -> `403 ACCOUNT_WITHDRAWAL_PENDING`(`due_at`, `cancel_allowed`), 새 계정이 생기지 않는다. 개발용 로그인은 비밀번호가 틀리면 `INVALID_CREDENTIALS`(존재 비노출).
- T-W14 W3 정상: `deleted_at` NULL, 이름 복원, 재로그인 성공, 철회로 복구 안 되는 것(친구)은 그대로 비어 있다.
- T-W15 W3 거절: `due_at` 경과(`WITHDRAWAL_DUE`), `cancel_allowed=false`(`CANCEL_NOT_ALLOWED`), 열린 요청 없음/신원 없음(`NO_PENDING_WITHDRAWAL`, 두 경우 응답 동일), 남의 자격으로는 남의 요청을 철회할 수 없다.
- T-W16 유예 중에도 결제 대사·환불 회수·경매 정산 틱·우편 기한 폐기가 계속 동작하고(계정이 `deleted_at`이어도), 새 캠페인 우편은 배달되지 않는다.
- T-W17 철회 시 이름이 이미 다른 사람에게 넘어갔으면 접미사로 복원하고 `renamed_characters`가 오른다.

**익명화 작업**
- T-W18 `due_at` 전에는 건드리지 않고, 후에는 한 번에 처리한다. 두 번 실행해도 결과가 같다(멱등). 중간 오류는 전체 롤백.
- T-W19 익명화 뒤 10절 T1 열대로 `auth_identities`·접속 기록·채팅이 비어 있고, 나를 차단한 행의 이름과 `reports.target_name`이 익명화된다.
- T-W20 **PII 잔존 전수 점검**: 익명화한 계정의 Steam ID 문자열·닉네임·IP로 모든 텍스트·INET 열을 검색해, 허용 위치(`star_orders.steam_id`, `report_lines`)에만 남는다.
- T-W21 원장·지갑·재고가 익명화 전후 동일(행 수, 합계, 해시). 야간 정합성 점검 I1~I3·I6이 익명화한 계정 포함 전체에 통과.
- T-W22 보류: 활성 정지/채팅 금지/경제 정지/열린 신고/미결 결제 각각 `defer_reasons`에 올라가고 익명화가 미뤄진다. 사유가 풀리면 다음 실행에서 처리. 경고(`warning`)만 있으면 보류하지 않는다.
- T-W23 보류 상한(180일) 도달 시 익명화하되 `withdrawn_identities`가 생긴다.
- T-W24 운영자 보류(`manual_hold`)는 작업이 건너뛴다.

**재가입·이월**
- T-W25 익명화 뒤 같은 Steam ID로 로그인하면 완전히 새 계정(옛 캐릭터·재화 없음). 유예 중에는 새 계정이 안 생긴다.
- T-W26 `carry_ban_until`이 미래면 가입 거절(`ACCOUNT_BANNED`), 과거면 통과. 결제 정지 이월은 `payment_profiles(blocked)`, 경제 정지 이월은 수동 정지가 걸린다. `identity_hash`가 원문 Steam ID와 다르고 키가 달라지면 일치하지 않는다. 만료·해제된 표시는 무시.

**원장·파기**
- T-W27 탈퇴한 계정이 있어도 `integrity-nightly`가 불일치 0.
- T-W28 앱 계정으로 `DELETE FROM gold_ledger`는 여전히 거절, `UPDATE`·`TRUNCATE`는 어떤 역할도 거절. `dotrpg_purge`(시험에서 `SET SESSION AUTHORIZATION`)의 DELETE는 허용 표에서만 통과하고 그 밖의 표에서는 거절.
- T-W29 `withdrawal-destroy` dry-run: `WITHDRAW_DESTROY_ENABLED=false`면 아무 행도 삭제하지 않고 건수만 기록. 켜면 `retain_until`이 지난 계정만, 공유 기록은 상대가 활동 중이면 남기고, 껍데기 삭제가 FK로 막히면 유지하며, `account_destruction_log`가 한 줄 생긴다(시험 DB에서 `retain_until`을 과거로 조작).
- T-W30 **정책 레지스트리 대조**: `information_schema`에서 `account_id`·`character_id`(또는 `*_account_id`)를 가진 모든 표가 `withdrawalPolicy.ts`에 분류돼 있다. 새 표를 만들고 분류하지 않으면 실패한다.

**관리자·CLI**
- T-W31 WD3 대행 요청이 `source='admin'`이고 정지 계정에도 동작, 감사 행이 같은 트랜잭션. 권한 부족(viewer/operator의 owner 전용 호출)은 `403 FORBIDDEN_ROLE`. 같은 `request_id` 멱등.
- T-W32 WD4 취소, WD5 즉시 익명화(보류 무시는 `override_deferral` + 메모 필수), WD6 보류 설정·해제, WD7 Steam ID 조회가 해시를 계산해 찾고 원문을 응답·로그·감사 `params`에 남기지 않는다, WD8 해제.
- T-W33 `economyHoldsRepository`가 유예 계정에 정지를 걸 수 있고, 익명화된 계정에는 거절.

**설정**
- T-W34 운영 설정에서 `WITHDRAW_ID_HMAC_KEY` 없음, `WITHDRAW_REAUTH_REQUIRED=false`, `WITHDRAW_DESTROY_ENABLED=true`인데 `PURGE_DATABASE_URL` 없음 -> 기동 거부.
- T-W35 `WITHDRAW_ENABLED=false`면 W1~W3, WD3이 `503 FEATURE_DISABLED`이고 이미 진행 중인 요청의 익명화 작업은 계속 돈다.

## 13. 환경변수

| 이름 | 기본 | 뜻 |
|---|---|---|
| `WITHDRAW_ENABLED` | `true` | 기능 스위치(꺼도 진행 중인 요청의 작업은 계속) |
| `WITHDRAW_GRACE_DAYS` | `30` | 유예 일수(D1) |
| `WITHDRAW_CONFIRM_PHRASE` | `탈퇴합니다` | 확인 문구. W1이 내려준다 |
| `WITHDRAW_REAUTH_REQUIRED` | `true` | 재인증 필수(D3). 운영에서 false면 기동 거부 |
| `WITHDRAW_REQUEST_MAX_PER_30D` | `3` | 30일 요청 한도 |
| `WITHDRAW_DEFER_MAX_DAYS` | `180` | 보류 상한(`due_at` 기준) |
| `WITHDRAW_JOB_BATCH` | `20` | 익명화 작업 한 번에 계정 수 |
| `WITHDRAW_ID_HMAC_KEY` | 없음(비밀값) | 9.3 해시 키. 운영에서 필수, 32바이트 이상 |
| `WITHDRAW_TOMBSTONE_MAX_DAYS` | `1095` | 이월 표시 최대 보관(D8, 법무) |
| `WITHDRAW_REJOIN_COOLDOWN_DAYS` | `0` | 익명화 뒤 재가입 대기(D7) |
| `WITHDRAW_RETAIN_PAID_YEARS` | `5` | 결제 기록 보관 연수(법무) |
| `WITHDRAW_RETAIN_LEDGER_YEARS` | `5` | 게임 내 원장 보관 연수(법무) |
| `WITHDRAW_DESTROY_ENABLED` | `false` | 5년 실삭제 스위치 |
| `PURGE_DATABASE_URL` | 없음(비밀값) | `dotrpg_purge` 역할 접속 문자열. 파기 켤 때만 필요 |
| `RATE_WITHDRAW_INFO_PER_MIN` / `RATE_WITHDRAW_PER_HOUR` / `RATE_WITHDRAW_CANCEL_*` | `10` / `3` / 계정 5, IP 10 (시간당) | 속도 제한 |

## 14. 관리자 도구 (CLI, 강제 처리)

7단계 5절 규칙(별도 listener, 관리자 세션 + TOTP, 모든 변경 `request_id`, 감사 `admin_audit_log` 같은 트랜잭션, 응답 `{ success, message, data }`)을 그대로 쓴다. 외부 id는 uuid만. 요청은 zod `.strict()`.

| # | 메서드 | 경로 | 역할 | 하는 일 |
|---|---|---|---|---|
| WD1 | GET | `/admin/withdrawals` | viewer | 목록: `state?`, `deferred?`(보류만), `due_before?`, 커서 페이지(`next_cursor`는 마지막 항목의 요청 uuid, 내부 id 비노출). 항목: 요청 uuid, 계정 uuid, `source`, `requested_at`, `due_at`, `defer_reasons`, `manual_hold`, 남은 일수 |
| WD2 | GET | `/admin/accounts/{uuid}/withdrawal` | viewer | 계정의 탈퇴 이력과 현재 상태, 보류 사유별 근거 건수(제재·신고·정지·주문). 감사 `withdrawal.view` |
| WD3 | POST | `/admin/accounts/{uuid}/withdrawal` | operator | 대행 요청 `{ request_id, note: string(1..200), cancel_allowed?: boolean }`. `cancel_allowed:false`는 owner만(`403 FORBIDDEN_ROLE`). 재인증·확인 문구 없음(운영자 책임, 메모 필수). 정지 계정 가능. 5.2~5.5와 같은 처리 |
| WD4 | POST | `/admin/withdrawals/{uuid}/cancel` | operator | 유예 중 운영자 취소 `{ request_id, note }`. 기한 경과 후에는 `409 WITHDRAWAL_DUE` |
| WD5 | POST | `/admin/withdrawals/{uuid}/anonymize-now` | owner | 유예를 건너뛰고 즉시 익명화 `{ request_id, note, override_deferral?: boolean }`. 보류 사유가 있는데 override가 아니면 `409 DEFERRED`(`errors.defer_reasons`). 7.2를 요청 안에서 실행하고 감사 행이 같은 트랜잭션 |
| WD6 | POST | `/admin/withdrawals/{uuid}/hold` | operator | 운영자 보류 켜기·끄기 `{ request_id, on: boolean, note }` (`manual` 사유) |
| WD7 | GET | `/admin/tombstones` | viewer | `?steam=<steam_id64>`로 조회(서버가 HMAC 계산, **입력 원문을 감사 `params`·로그에 남기지 않는다**, 해시 앞 8자만 표시). 이월 내용·만료·해제 여부. 응답 `tombstone.id`는 uuid(내부 숫자 id 비노출). 감사 `tombstone.view` |
| WD8 | POST | `/admin/tombstones/{uuid}/release` | owner | 이월 해제 `{ request_id, note }` (`released_at/by`). 감사 |

- 에러: 공통(`400`, `401`, `403 FORBIDDEN_ROLE`, `404 NOT_FOUND`, `422 IDEMPOTENCY_MISMATCH`, `429`) + `409 WITHDRAWAL_ALREADY_REQUESTED`, `409 WITHDRAWAL_DUE`, `409 DEFERRED`, `409 WITHDRAW_BLOCKED`(열린 결제), `404 ACCOUNT_NOT_FOUND`, `409 ALREADY_ANONYMIZED`.
- 속도 제한: 7단계 5.6 값(조회 120/분, 변경 30/분). WD5는 관리자당 분당 5회.
- 감사 `params` 허용 목록: `note_len`, `cancel_allowed`, `override_deferral`, `on`. 메모 원문은 `admin_audit_log.params`에 넣지 않는다.
- 탈퇴 대행·즉시 익명화에서 `idempotency`는 `(admin_id, request_id)` 성공 행이 맡는다(7단계 5.6).

CLI 명령(7단계 5.9 표에 추가):

| 명령 | 엔드포인트 |
|---|---|
| `withdraw list [--state requested\|completed\|cancelled] [--deferred]`, `withdraw show <account-uuid>` | WD1, WD2 |
| `withdraw start <account-uuid> --note ... [--no-cancel]`, `withdraw cancel <withdrawal-uuid> --note ...` | WD3, WD4 |
| `withdraw anonymize-now <withdrawal-uuid> --note ... [--override-deferral]`, `withdraw hold <withdrawal-uuid> --on\|--off --note ...` | WD5, WD6 |
| `tombstone find --steam <id>`, `tombstone release <uuid> --note ...` | WD7, WD8 |
| `ops run withdrawal-anonymize`, `ops run withdrawal-destroy` | OP3(허용 목록에 추가) |

`account find <q>`(PL1)의 `steam:` 검색은 익명화 뒤에는 결과가 없다(`auth_identities`가 없으므로). 구매 이력이 있는 사용자는 결제 관리의 Steam ID 조회(`star_orders_steam_id`)로 찾고, 신원 이월은 `tombstone find`로 본다.

## 15. 클라이언트 계약과 결정 대기

### 15.0 클라이언트 계약 (Unity, 메인이 구현)

1. 설정 화면 "회원 탈퇴" -> `GET /me/withdrawal`로 손실 요약·확인 문구·`blockers`를 읽어 안내 화면을 만든다. 문구는 `notices` 코드로 조립(환불은 Steam 정책을 따른다는 문구 포함).
2. 확인 문구 입력 + 체크 2개(`ack_progress_loss`, 유료 잔액이 있으면 `ack_paid_loss`) + 새 Steam 티켓으로 `POST /me/withdrawal`(새 `request_id`).
3. `201`이면 토큰·로컬 캐시 삭제, "탈퇴 요청 완료, 유예 N일, 철회하려면 같은 Steam 계정으로 로그인" 안내 후 타이틀로.
4. 로그인에서 `403 ACCOUNT_WITHDRAWAL_PENDING`을 받으면 `due_at`을 보여 주고 "탈퇴 철회" 화면 -> 새 티켓으로 `POST /auth/withdrawal/cancel`(`200`이면 새 티켓으로 바로 일반 로그인).
5. W2 응답이 유실된 경우(타임아웃) 같은 `request_id`로 재전송하되 `401`이 오면 로그인을 시도해 `ACCOUNT_WITHDRAWAL_PENDING`이면 성공으로 처리.
6. 새 WebSocket close 코드 `4012`는 재연결하지 않고 위 안내를 띄운다.

### 15.1 사용자가 정할 것 (선택지와 추천)

| # | 질문 | 선택지 | 추천 |
|---|---|---|---|
| D1 | 유예 기간 | 7일 / 14일 / **30일** | 30일(사용자 기본값). 철회 여지와 개인정보 보유 기간의 균형 |
| D2 | 유예 중 철회 | **허용(재인증, 30일 3회)** / 불가 | 허용. 별도 W3로 하고 로그인이 철회를 겸하지 않는다 |
| D3 | 탈퇴 요청 재인증 | **필수(Steam 티켓/비밀번호)** / 토큰만 | 필수. 15분 토큰 탈취만으로 계정이 유예에 들어가는 것을 막는다. 클라이언트가 새 티켓을 받는 비용만 든다 |
| D4 | 진행 중 경매·미수령 우편 | **경고 후 진행(자연 종결, 철회 안 하면 기한 폐기)** / 해결될 때까지 탈퇴 거절(캐릭터 삭제와 같은 방식) | 경고 후 진행. 탈퇴를 막지 않고 새 취소 로직이 필요 없다 |
| D5 | 유예 중 닉네임 처리 | **요청 때 자리표시로 교체(철회 때 복원)** / 화면마다 필터 | 교체. 누락 시 노출되는 곳이 없다(fail-safe). 단점은 철회 때 이름 충돌 가능(접미사 복원) |
| D6 | 제재·조사 중인 계정의 탈퇴 | **요청은 받고 익명화만 보류(상한 180일) + 이월 표시** / 요청 자체를 거절 | 요청 수락 + 보류. 거절은 정보주체 권리 문제가 있다 |
| D7 | 같은 Steam ID 재가입 대기 | **0일** / 30일 | 0일(이미 30일 유예 지남). 신규 계정 제한이 일회성 이득을 막는다 |
| D8 | 이월 표시 최대 보관 | 1년 / **3년** / 영구 | 3년(영구 정지 포함 상한). 영구 보관은 개인정보 최소화에 불리 |
| D9 | 게임 내 원장 보관 | **5년(결제와 동일)** / 결제 5년 + 게임 원장 1~3년 | 사용자 기본값 5년. 법무가 게임 내 원장 근거를 약하다고 하면 두 번째 |
| D10 | 비식별 진행 상태(`character_state` 등) | **5년까지 보관** / 익명화 때 삭제 | 보관. 삭제는 원장 정합성·숨은 FK 위험이 있고 개인과 이어지지 않는다 |
| D11 | 유료 별조각 잔액 | **그대로 둠(탈퇴 시 사용 불가, 환불은 Steam 정책 안내)** / 탈퇴 시 소멸 기록 | 그대로 둠. 환불·차지백 회수가 정상 동작. 소멸 고지는 법무(L5) |

### 15.2 법무가 확인할 것 (법률 자문 전 기본값 근거)

| # | 확인 사항 | 현재 기본값 |
|---|---|---|
| L1 | 5년 보관의 범위: 결제·청약철회 기록(전자상거래 기록)은 5년이 맞는지, **게임 내 골드·아이템·경험치 원장**까지 5년 보관할 근거가 있는지(없으면 조사·부정 이용 방지의 정당한 이익으로 몇 년까지 가능한지) | 둘 다 5년 |
| L2 | 접속 로그: 로그인 기록 90일, IP·기기 집계 180일, 결제 주문 IP·기기 180일의 근거와 기간. 탈퇴 시 앞당겨 삭제하는 것이 맞는지(현재는 익명화 때 삭제, 결제 주문은 기존 일정) | 90/180/180일 |
| L3 | **분리 보관**: 다른 법령에 따라 보관하는 정보(`star_orders.steam_id` 등)를 다른 개인정보와 "분리하여" 보관해야 하는지. 지금은 같은 DB의 다른 표이고 익명화한 계정 껍데기와 `account_id`로 이어진다. 별도 스키마·역할로 접근을 제한하는 수준이 필요한지 | 같은 DB, 같은 표 |
| L4 | 파기 방법·기록 의무(파기 관리대장), 백업 안의 개인정보 보관 기간(오프사이트 백업은 익명화 전 데이터를 담는다) | `account_destruction_log`, 백업 보관 기간은 미정 |
| L5 | 미사용 **유료 별조각 잔액**: 탈퇴 시 소멸 고지로 충분한지, 환불 의무(콘텐츠 이용 약관, 청약철회, Steam 정책)와의 관계 | 소멸 확인을 받고 Steam 환불 정책을 안내 |
| L6 | 제재 이월 표시(해시 보관): 정당한 이익으로 몇 년까지 가능한지(영구 정지 포함), 해시가 개인정보에 해당하는지, 키 로테이션 | 최대 3년, HMAC 해시 |
| L7 | 개인정보처리방침 문구: 수집 항목 표(2절), 보관 기간, 탈퇴 절차(유예 30일 철회 가능), 보류 사유의 일반 문구, 국외 이전(Steam, 서버 위치), 만 14세 미만 처리 | 2절·10절이 초안 |
| L8 | 탈퇴가 가입보다 어렵지 않게: 재인증과 확인 문구, 유예 철회 방식, 진행 중 경매·우편이 있어도 탈퇴를 허용하는 방식이 적절한지 | 재인증 + 확인 문구, 경고 후 진행 |
| L9 | 유예 30일 동안의 처리 정지: 로그인을 막고 이름을 가리면 처리 정지로 충분한지, 정보주체가 **즉시 파기**를 요구하면 WD5(즉시 익명화)로 대응하는 절차가 맞는지 | WD5(owner) |
| L10 | 신고 증거(`report_lines`)에 탈퇴 사용자의 닉네임·채팅이 종결 후 180일 남는 것, 열린 신고가 있는 사용자의 익명화 보류 | 기존 일정 유지, 보류 |

### 15.3 구현 순서 (S7, dotrpg-backend-coder)

1. 마이그레이션 0027 + `schema.sql` 합본 + 머리말 표.
2. 레지스트리 `withdrawalPolicy.ts` + T-W30(미분류 표 검출)을 먼저 만든다.
3. W1·W2·W3 + E1·E2 + 세션 알림 `4012` + 5.3 정리.
4. 익명화 작업 + I7 + `purge.ts` 변경.
5. 관리자 WD1~WD8 + CLI.
6. 파기 작업 dry-run + 트리거 함수 변경 시험(T-W28, T-W29).

끝났다는 기준: 시나리오 "Steam 계정 가입 -> 캐릭터·재화·경매·우편 보유 -> 탈퇴 요청 -> 즉시 모든 요청 401·WebSocket 종료 -> 유예 안 철회 시 이름·재화 그대로 복구 -> 다시 탈퇴 -> 30일 뒤 익명화 -> 개인 식별 값이 허용 위치 외에 없음 -> 야간 정합성 점검 불일치 0 -> 같은 Steam ID로 새 계정 가입"이 시험으로 통과하고, 제재 중 계정은 보류·이월이 동작한다.

## 16. 구현 메모 (S7, 설계와 달라진 점)

- 허용 표(3.2의 `ledger_block_mutation`): 설계의 13개에 계정·캐릭터 한 곳에 속한 `dungeon_sweeps`, `mail_attachments`, `mail_campaign_deliveries`, `admin_grants`, `party_run_host_reports`를 더했다(파기 순서의 자식 표라서). `held_run_reviews`와 감사 로그는 넣지 않는다. 일치 여부는 T-W30이 `pg_trigger`와 대조한다.
- 파기(8절): 껍데기를 남기는 계정은 `account_withdrawals` 행도 함께 남겨 다음 실행에서 남은 공유 기록을 다시 시도한다(설계는 행 삭제). 관리대장은 지운 행이 있을 때만 한 줄 쓰므로 한 계정에 `shell_kept: true` 줄과 마지막 `shell_kept: false` 줄이 둘 다 있을 수 있다. 외래 키(23503)로 막힌 표는 SAVEPOINT로 되돌리고 남긴다.
- 정책 레지스트리의 SQL은 `$1/$2` 대신 `:acc/:chr` 자리표시를 쓰고 `bindIds()`가 바꾼다(문장마다 쓰는 배열이 달라서).
- 환경변수: 설계 13절에 IP당 W2 속도 제한 이름이 없어 `RATE_WITHDRAW_IP_PER_HOUR`로 정했다. `WITHDRAW_REJOIN_COOLDOWN_DAYS`는 익명화 때 `withdrawn_identities.carry_ban_until`을 그 일수만큼 거는 방식으로 구현했다. 개발·시험에서 `WITHDRAW_ID_HMAC_KEY`가 없으면 JWT_SECRET에서 파생한 키를 쓴다(운영은 필수).
- W2의 멱등 재전송은 1회용 Steam 티켓을 쓰는 재인증보다 먼저 확인한다(같은 `request_id`면 새 티켓 없이 첫 응답).
- 관리자 감사: 대상 종류(`target_type`)는 늘리지 않았다. WD4~WD6의 성공 행은 계정 uuid를 대상으로, 실패 행은 경로의 탈퇴 uuid를 대상으로 남는다. 운영 메모는 계정 메모(`admin_account_notes`)에 `[탈퇴 ...]` 접두로 남기고 감사 `params`에는 길이만 넣는다.
- 기존 시험 변경: 운영 설정을 검증하는 시험의 `prod` 값에 `WITHDRAW_ID_HMAC_KEY`를 더했다(운영 필수 비밀이 새로 생긴 의미 변경).
