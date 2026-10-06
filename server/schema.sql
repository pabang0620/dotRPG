-- dotRPG 서버 전체 스키마 (1~8단계 기준. 6단계 0007, 7단계 0008, 8단계 0009는 초안). 정본은 migrations/*.sql 이고, 이 파일은 읽기용으로 UP 본문을 모아 둔 것이다.
-- 마이그레이션을 추가하면 이 파일도 같은 커밋에서 맞춘다. 구성: 0001_init(1~2단계) + 0002_economy(3단계) + 0003_dungeon_solo(3단계-b, 선택)
-- + 0004_gather_rate(이상 기록 kind, 위 anomaly_log 정의에 이미 포함) + 0005_party(4단계) + 0006_chat_social(5단계, 이 파일 아래쪽) + 0007_auction(6단계, 0006 구역 바로 앞)
-- + 0008_ops(7단계, 이 파일 아래쪽) + 0009_relay_field(8단계) + 0020_anti_abuse(9단계, 이 파일 아래쪽. 0010~0019는 이 파일에 합치지 않았다)
-- + 0021_sweep_and_mail(10단계, 이 파일 맨 아래).
-- (0006의 accounts.last_character_id는 아래 accounts 정의에 합치지 않고 0006 구역의 ALTER로 둔다: 정본 순서를 그대로 보이기 위해서다.)
--
-- 컨벤션
--   - 내부 키 id BIGSERIAL(API 비노출), 외부 노출은 uuid UUID UNIQUE DEFAULT gen_random_uuid()
--     (1:1 상태 표와 "캐릭터당 한 번" 같은 복합 키 표는 자연 키를 PK로 쓴다: character_state, quest_claims 등)
--   - 열거 값은 TEXT + CHECK, 시각은 TIMESTAMPTZ(now()), 삭제는 deleted_at(소프트), 의미는 COMMENT ON
--   - 재화(골드·아이템·경험치)가 움직이는 쪽: 잔액 BIGINT CHECK(>=0) + 추가만 하는 원장 + request_log의 UNIQUE(account_id, request_id)
--     아이템 재고는 (character_id, location, item_key)별로 "원장 delta 합 = character_items.count"가 성립한다
--   - 같은 캐릭터를 바꾸는 요청은 characters 행을 먼저 FOR UPDATE로 잠근 뒤 자식 표를 만진다(락 순서 하나)
--   - 클라이언트가 보내는 값은 행동·대상 id·request_id뿐. 금액·확률·보상량·시간은 받지 않는다
--   - 게임 데이터 값(드롭률·가격 등)은 SQL에 복사하지 않고 server/data/*.json 이름으로만 참조한다
--   - 시각 경계(06:00 일일, 목요일 06:00 주간)는 서버 KST 함수 하나로만 계산한다 (Docs/server/phase1_2_api.md 0.5)
--
-- 기능별 테이블 사용처
--   | 기능                         | 읽기                          | 쓰기                                                        |
--   |------------------------------|-------------------------------|-------------------------------------------------------------|
--   | /health, /meta               | (SELECT 1)                    | -                                                           |
--   | dev register                 | auth_identities               | accounts, auth_identities, refresh_tokens                   |
--   | dev login / refresh / logout | accounts, auth_identities, refresh_tokens | refresh_tokens, accounts.last_login_at          |
--   | GET /me                      | accounts, auth_identities, characters |  -                                                  |
--   | 캐릭터 생성                  | accounts(행 잠금), characters, request_log | characters, character_state, character_items, gold_ledger, item_ledger, request_log |
--   | 캐릭터 목록·상세             | characters, character_state, character_items, quest_claims, character_enhance_pity, character_chests, site_deliveries | - |
--   | 캐릭터 삭제                  | characters                    | characters.deleted_at                                       |
--   | 상태 저장 PUT                | characters(level, class)      | character_state                                             |
--   | 처치 보고                    | characters(행 잠금), kill_log(속도 창), kill_stats, character_items(착용 무기 화력 상한) | kill_log, kill_stats, drops, xp_ledger, characters(level, xp), anomaly_log(거절), request_log |
--   | 드롭 줍기                    | characters(행 잠금), drops    | drops.claimed_at, characters.gold, character_items, gold_ledger, item_ledger, request_log, anomaly_log(남의 id) |
--   | 채집·노드 쿨다운 조회        | characters(행 잠금), character_node_state | character_node_state, character_items, item_ledger, request_log, anomaly_log |
--   | 상자 열기                    | characters(행 잠금), character_chests | character_chests, character_items, item_ledger, request_log, anomaly_log |
--   | 공사장 납품                  | characters(행 잠금), quest_claims, site_deliveries, character_items | site_deliveries, character_items, item_ledger, request_log |
--   | 퀘스트 보상 청구             | characters(행 잠금), quest_claims, kill_stats, character_state(story_flags), site_deliveries, dungeon_runs(0003), character_items | quest_claims, characters(level, xp, gold), character_items, gold_ledger, item_ledger, xp_ledger, request_log, anomaly_log |
--   | 상점 구매·판매               | characters(행 잠금), character_items | characters.gold, character_items, gold_ledger, item_ledger, request_log |
--   | 강화                         | characters(행 잠금), character_items, character_enhance_pity | characters.gold, character_items, character_enhance_pity, enhance_log, gold_ledger, item_ledger, request_log |
--   | 아이템 사용(물약·주문서·당근)| characters(행 잠금), character_items | character_items, item_ledger, request_log                 |
--   | 창고 이동                    | characters(행 잠금), character_items | character_items, item_ledger, request_log                |
--   | 장착·해제                    | characters(class, 행 잠금), character_items | character_items, item_ledger, request_log          |
--   | 솔로 던전(0003)              | characters(행 잠금), dungeon_runs, kill_log | dungeon_runs, kill_log, drops, xp_ledger, characters, character_items, gold_ledger, item_ledger, request_log, anomaly_log |
--   | Steam 로그인·연동 (4단계)    | auth_identities, accounts     | accounts, auth_identities, refresh_tokens (Steam 티켓 재사용 방지는 메모리) |
--   | 모집 게시판 목록 (4단계)     | parties, party_members, characters, character_items(전투력 계산) | -                      |
--   | 파티 만들기·신청·수락·준비·강퇴·방장 위임·나가기 (4단계) | characters(행 잠금), parties, party_members, party_applications, character_items | parties, party_members, party_applications, request_log |
--   | 자동 매칭 (4단계)            | characters, character_items, parties(만들어질 때) | parties, party_members, request_log (대기열은 메모리, 표 없음) |
--   | 파티 던전 출발·입장·시작 (4단계) | characters(여러 명 id 순 잠금), parties, party_members, party_runs, party_run_members, dungeon_runs | party_runs, party_run_members, dungeon_runs, parties.state, request_log, anomaly_log |
--   | 하트비트·호스트 인계 (4단계) | party_runs, party_run_members | party_run_members.last_seen_at/state, party_runs.host_*, dungeon_runs(이탈 시 abandoned), request_log |
--   | 파티 던전 처치 보고 (4단계)  | characters(행 잠금), dungeon_runs, party_runs(power_cap), kill_log | 3단계 처치 보고와 같다 (+ dungeon_runs.room_kills, 레이드 연습판은 지급 건너뜀) |
--   | 방장 보고 (4단계)            | party_runs, party_run_members | party_run_host_reports, party_runs.first_report_at, request_log, anomaly_log |
--   | 멤버 결과 보고·정산 (4단계)  | characters(행 잠금), dungeon_runs(같은 판 모든 행), party_run_host_reports, party_runs, raid_claims | dungeon_runs, characters(level, xp), xp_ledger, item_ledger(raid_key*), raid_claims, party_run_members, request_log, anomaly_log |
--   | 레이드 상태 조회 (4단계)     | characters, raid_claims, quest_claims, character_state, character_items | -                                                            |
--   | 중계 티켓 발급·방 입장 (8단계) | characters, party_run_members 또는 field_session_members, party_runs·field_sessions(전송·호스트) | - (티켓 단일 사용·연결 상태는 메모리, 표 없음)                 |
--   | 중계 연결 이벤트 (8단계)     | party_run_members, field_session_members, parties(방장) | party_run_members.state/last_seen_at, field_session_members.state, party_runs·field_sessions(host_*, version), relay_room_stats(방 종료 때 한 행) |
--   | 전송 전환 (8단계)            | party_runs 또는 field_sessions(행 잠금), party_run_members·field_session_members | party_runs.transport*, field_sessions.transport*, request_log |
--   | 필드 세션 입장·합류 (8단계)  | characters(행 잠금), parties, party_members, field_sessions(행 잠금), field_session_members, character_items(화력 상한) | field_sessions, field_session_members, request_log |
--   | 필드 세션 하트비트·떠나기·호스트 인계 (8단계) | field_sessions(행 잠금), field_session_members, parties(방장), party_members(가입 순서), character_items(화력 상한·장비 지문) | field_sessions.host_*/version/last_active_at, field_session_members, request_log |
--   | 필드 세션 호스트 관찰 (8단계) | field_sessions, field_session_members | field_session_members.kills_credited, field_sessions.last_observe_at, request_log, anomaly_log |
--   | 필드 세션 처치 보고 (8단계)  | 처치 보고와 같다 + field_sessions, field_session_members(멤버십·화력 상한 합·기여 부채) | 처치 보고와 같다 + kill_log.field_session_id/monster_ref/xp_factor, field_session_members.kills_accepted |
--   | 방치 필드 세션 정리 (8단계)  | field_sessions, field_session_members | field_sessions(ended), field_session_members(left), job_runs |
--   | WebSocket 접속·채팅 전송 (5단계) | accounts, characters, party_members(파티 채널 수신자), blocks(접속 때 세션 메모리로), account_sanctions(채팅 금지) | chat_messages, accounts.last_character_id, account_sanctions(자동 제재) |
--   | 놓친 메시지 따라잡기 (5단계) | chat_messages, party_members(가입 시각), blocks(세션 메모리)                | -                                                           |
--   | 친구 목록·요청·수락·삭제 (5단계) | friendships, characters, accounts(last_character_id), blocks               | friendships, request_log                                    |
--   | 차단·해제 (5단계)            | blocks, characters            | blocks, friendships(상태 removed), party_invites(cancelled), request_log |
--   | 신고 (5단계)                 | characters, chat_messages, party_members, friendships, reports(한도·중복) | reports, report_lines, account_sanctions(자동 신고 제재를 켠 경우), request_log |
--   | 파티 초대 (5단계)            | characters, parties, party_members, party_invites, blocks, character_items(전투력) | party_invites, party_members(수락 시), parties.version, request_log |
--   | 제재 처리 (5단계 SQL, 7단계 관리자 도구) | reports, report_lines, account_sanctions | reports.state, account_sanctions, accounts.banned_until(ban) |
--   | 경매 검색·시세 조회·내 등록 (6단계) | characters(이름·직업), auction_listings, auction_bids, auction_trades, auction_price_daily, character_items(내 가방 거래 가능분) | -  |
--   | 경매 등록 (6단계)            | characters(행 잠금, level), accounts(created_at), character_items(bag), auction_listings(내 등록 수), auction_trades(시세 중앙값) | auction_listings, character_items(bag 감소), characters.gold(보증금), item_ledger(auction_list), gold_ledger(auction_deposit), request_log, auction_flags |
--   | 경매 즉시 구매·입찰 (6단계)  | characters(행 잠금), auction_listings(행 잠금), auction_trades(쌍 한도) | characters.gold, auction_listings, auction_bids, mails(구매자·판매자·직전 입찰자), auction_trades, auction_price_daily, auction_sinks, gold_ledger, item_ledger, request_log, auction_flags |
--   | 경매 취소 (6단계)            | characters(행 잠금), auction_listings(행 잠금) | auction_listings, mails, auction_sinks(보증금), item_ledger |
--   | 경매 마감 정산 틱·우편 기한 폐기 (6단계) | auction_listings(행 잠금, 캐릭터 행 없음), mails | auction_listings, auction_bids, mails, auction_trades, auction_price_daily, auction_sinks, item_ledger (gold_ledger는 없다) |
--   | 우편 조회·요약 (6단계)       | mails, auction_listings(요청자 소유분 지연 정산) | (지연 정산 시 위 정산 틱과 같다) |
--   | 우편 수령 (6단계)            | characters(행 잠금), mails(행 잠금) | mails.claimed_at, characters.gold, character_items(bag), gold_ledger(mail_claim), item_ledger(mail_claim), request_log |
--   | 캐릭터 삭제 (2단계 변경, 6단계) | auction_listings, mails (미수령·진행 중이면 삭제 거절) | -                                  |
--   | 관리자 로그인·세션 (7단계)   | admin_users, admin_sessions   | admin_users(failed_count, totp_last_step, last_login_at), admin_sessions, admin_audit_log |
--   | 관리자 계정 관리·감사 조회 (7단계) | admin_users, admin_audit_log | admin_users, admin_sessions(폐기), admin_audit_log                                   |
--   | 계정·캐릭터 조회, 원장 조회, 메모·검토 확인 (7단계) | accounts, auth_identities, characters, character_items, gold_ledger, item_ledger, xp_ledger, account_sanctions, reports, anomaly_log, auction_flags, auction_trades, dungeon_runs, admin_account_notes | admin_account_notes, admin_audit_log(조회 기록) |
--   | 제재 생성·해제 (7단계)       | accounts(행 잠금), account_sanctions, reports | account_sanctions, accounts.banned_until(ban), reports(연결 신고), admin_audit_log (pg_notify는 5단계 트리거) |
--   | 신고 처리 (7단계)            | reports, report_lines, account_sanctions | reports.state, account_sanctions, admin_audit_log |
--   | 보류 던전 검토·해제 (7단계)  | characters(행 잠금), dungeon_runs, party_run_host_reports, kill_log, anomaly_log, raid_claims | dungeon_runs(cleared로), characters(level, xp), xp_ledger, item_ledger(raid_key*), raid_claims, held_run_reviews, admin_audit_log |
--   | 이상 기록·감시 목록 조회 (7단계) | anomaly_log, auction_flags, dungeon_runs, admin_account_notes | -                                                        |
--   | 운영 지급 (7단계)            | characters(행 잠금), admin_grants | mails(kind=system), admin_grants, item_ledger(admin_grant), admin_audit_log (골드는 수령 때 mail_claim) |
--   | 점검 창 예약·취소·연장·종료, 공지 방송 (7단계) | maintenance_windows | maintenance_windows, admin_audit_log (chat.sys 푸시는 메모리) |
--   | 점검 중 로그인·새 판 차단 (7단계) | maintenance_windows(서버가 주기적으로 읽어 메모리에 둔다) | -                                  |
--   | 정리·점검 작업 (7단계)       | job_runs, 각 정리 대상 표, 원장(정합성 점검) | job_runs, 각 정리 대상 표(삭제), dungeon_runs·parties(방치 정리) |
--   | 로그인·가입·Steam 로그인 (9단계) | accounts(행 잠금), auth_identities, account_devices | accounts(active_*), refresh_tokens(가족 폐기·device), login_events, account_devices, account_ips, auth_identities.steam_owner_id |
--   | refresh (9단계)              | refresh_tokens, accounts      | refresh_tokens, login_events(기기·IP가 바뀐 때만)           |
--   | 프레즌스 (9단계)             | characters, accounts(active_*), online_sessions(같은 기기 수) | online_sessions, play_time_hourly, login_events(kind=enter), anomaly_log(ip_cluster, device_limit) |
--   | 처치 보고 맵·체류 검사 (9단계) | online_sessions             | anomaly_log(kill_presence)                                  |
--   | 파티 판 시작·정산 (9단계)    | party_run_members(device_hash, steam_key), party_run_host_reports, auth_identities | party_run_members(스냅샷), dungeon_runs(contribution, lock_reason), anomaly_log(contribution, member_card) |
--   | 경매 구매·입찰·체결 (9단계)  | account_devices, account_ips, auth_identities, auction_trades | auction_trade_flags, income_hourly(auction_*)        |
--   | 소탕 현황 S1 (10단계)        | characters, accounts, dungeon_runs(클리어 기록), dungeon_sweeps(오늘 횟수), sweep_ticket_lots, account_week_counters, economy_holds | - |
--   | 소탕 S2, S3 (10단계)         | characters(행 잠금), accounts(행 잠금), dungeon_runs, dungeon_sweeps, sweep_ticket_lots(행 잠금), economy_holds, online_sessions | dungeon_sweeps, sweep_ticket_lots, sweep_ticket_ledger(sweep_use), characters(level, xp, gold), xp_ledger(dungeon_sweep), gold_ledger·item_ledger(dungeon_card), character_items, income_hourly, anomaly_log(sweep_denied), request_log |
--   | 클리어권 구매 T1 (10단계)    | characters(행 잠금, 골드), accounts(행 잠금), characters(계정 최고 레벨), account_week_counters, economy_holds | characters.gold, gold_ledger(sweep_ticket_buy), sweep_ticket_lots, sweep_ticket_ledger(shop_buy), account_week_counters(sweep_buy), request_log |
--   | 주간 활동 수령 T2 (10단계)   | characters(행 잠금), accounts(행 잠금), account_week_counters, economy_holds | account_week_counters(activity_claim), sweep_ticket_lots, sweep_ticket_ledger(weekly_activity), request_log |
--   | 요일 던전 직접 클리어 (10단계 변경) | (기존) | account_week_counters(direct_clear) |
--   | 우편 조회·요약 (10단계 변경) | mails, mail_attachments, mail_campaigns(캠페인 캐시) | (배달 시 아래 행) |
--   | 우편 수령·모두 받기 (10단계 변경) | characters(행 잠금), accounts(행 잠금), mails(행 잠금), mail_attachments, sweep_ticket_lots, economy_holds | mails.claimed_at, characters.gold, gold_ledger(mail_claim), character_items, item_ledger(mail_claim), sweep_ticket_lots, sweep_ticket_ledger(campaign_claim), request_log |
--   | 캠페인 배달 (10단계)         | mail_campaigns(캐시), mail_campaign_deliveries, accounts, characters, mail_campaign_attachments | mails, mail_attachments, item_ledger(admin_grant, 위치 mail), mail_campaign_deliveries, mail_campaigns.issued_count |
--   | 캠페인 관리자 MC1~MC6 (10단계) | mail_campaigns, mail_campaign_attachments, mail_campaign_deliveries, mails, accounts, admin_users | mail_campaigns, mail_campaign_attachments, admin_audit_log |
--   | 클리어권 만료·캠페인 정리 작업 (10단계) | sweep_ticket_lots, mail_campaigns, mails | sweep_ticket_lots(remaining 0), sweep_ticket_ledger(expire), mail_campaigns(status), mails.expires_at(회수), job_runs |
--   | 전직·각성 (9단계)            | characters, character_career, character_career_trials, online_sessions, character_state(career) | character_career, character_career_trials, character_state.career, request_log |
--   | 경제 속도 감시 (9단계)       | income_hourly, play_time_hourly, characters(level), economy_holds, account_devices | economy_holds, anomaly_log                    |
--   | 경제 정지 대상 7개 경로 (9단계) | economy_holds              | -                                                           |
--   | 관리자 회수 (9단계)          | characters(행 잠금), economy_holds, income_hourly, gold_ledger, item_ledger, mails | gold_ledger(admin_clawback), item_ledger(admin_clawback), characters.gold, character_items, mails.expires_at, economy_holds, admin_audit_log |
--
--   보관·정리(7단계에서 정식화, 상세 Docs/server/phase7_ops.md 6절)
--     지운다: request_log 7일, kill_log 7일(drops는 ON DELETE CASCADE + 1일), anomaly_log 심각도 1은 30일·2 이상은 180일, chat_messages 7일(CHAT_RETENTION_DAYS),
--             party_invites 7일, friendships 끝난 행 90일, blocks 해제 행 90일, report_lines 신고가 닫힌 뒤 180일(REPORT_RETENTION_DAYS), party_applications 끝난 행 30일,
--             party_members 나간 행 30일, 종료된 field_sessions·field_session_members 30일, relay_room_stats 90일(8단계), refresh_tokens 만료·폐기 30일 뒤, 수령된 일반 우편(kind<>system) 180일, admin_sessions 만료 30일 뒤, job_runs 90일.
--     지우지 않는다(추가 전용 트리거 또는 FK 연결 때문): gold_ledger, item_ledger, xp_ledger, enhance_log, auction_trades, auction_sinks, auction_flags, auction_price_daily,
--             auction_listings, auction_bids, 폐기된·system 우편, dungeon_runs, party_runs 계열, parties, reports, account_sanctions, 관리자 표 전부.
--     (6단계 문서의 "auction_trades 180일, 종료 listing 180일, auction_flags 90일"은 7단계에서 "지우지 않는다"로 바뀌었다.)
--

-- 추가만 하는 원장의 수정·삭제를 DB가 막는다 (사후 추적의 근거)
CREATE FUNCTION ledger_block_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION '% is append-only (% blocked)', TG_TABLE_NAME, TG_OP;
END;
$$ LANGUAGE plpgsql;

-- ---------- 1단계: 계정·인증 ----------

CREATE TABLE accounts (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_login_at TIMESTAMPTZ,
  banned_until  TIMESTAMPTZ,
  deleted_at    TIMESTAMPTZ
);
COMMENT ON TABLE  accounts IS '게임 계정. 로그인 수단은 auth_identities에 따로 둔다';
COMMENT ON COLUMN accounts.id IS '내부 키. API에 노출하지 않는다';
COMMENT ON COLUMN accounts.uuid IS '외부 노출용 id. JWT sub, 응답의 account id';
COMMENT ON COLUMN accounts.last_login_at IS '마지막 로그인 성공 시각 (refresh는 갱신하지 않는다)';
COMMENT ON COLUMN accounts.banned_until IS '이 시각 전까지 로그인·refresh 거부. NULL = 정지 아님';
COMMENT ON COLUMN accounts.deleted_at IS '소프트 삭제. NULL이 아니면 로그인 불가';

CREATE TABLE auth_identities (
  id          BIGSERIAL PRIMARY KEY,
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  provider    TEXT NOT NULL CHECK (provider IN ('dev', 'steam')),
  subject     TEXT NOT NULL,
  secret_hash TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (provider, subject),
  CONSTRAINT auth_identities_dev_secret CHECK (provider <> 'dev' OR secret_hash IS NOT NULL),
  CONSTRAINT auth_identities_dev_lower CHECK (provider <> 'dev' OR subject = lower(subject))
);
COMMENT ON TABLE  auth_identities IS '로그인 수단. 지금은 dev(아이디·비밀번호), 나중에 steam';
COMMENT ON COLUMN auth_identities.subject IS 'dev: 소문자 아이디, steam: steam_id 문자열';
COMMENT ON COLUMN auth_identities.secret_hash IS 'dev 비밀번호 해시(argon2id 문자열). steam은 NULL';
CREATE INDEX auth_identities_account_idx ON auth_identities (account_id);

CREATE TABLE refresh_tokens (
  id         BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  family_id  UUID NOT NULL,
  token_hash TEXT NOT NULL UNIQUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at TIMESTAMPTZ NOT NULL,
  used_at    TIMESTAMPTZ,
  revoked_at TIMESTAMPTZ
);
COMMENT ON TABLE  refresh_tokens IS '교체형 갱신 토큰. 원문은 저장하지 않고 해시만 둔다';
COMMENT ON COLUMN refresh_tokens.family_id IS '로그인 1회에서 이어진 토큰 묶음. 재사용 감지 시 묶음 전체를 폐기';
COMMENT ON COLUMN refresh_tokens.token_hash IS 'SHA-256(hex) of 토큰 원문(32바이트 난수)';
COMMENT ON COLUMN refresh_tokens.used_at IS '교체되어 쓰인 시각. 이미 쓴 토큰이 또 오면 탈취로 본다';
COMMENT ON COLUMN refresh_tokens.revoked_at IS '로그아웃 또는 재사용 감지로 폐기된 시각';
CREATE INDEX refresh_tokens_family_idx  ON refresh_tokens (family_id);
CREATE INDEX refresh_tokens_expires_idx ON refresh_tokens (expires_at);

-- ---------- 2단계: 캐릭터 ----------

CREATE TABLE characters (
  id         BIGSERIAL PRIMARY KEY,
  uuid       UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  name       TEXT NOT NULL CHECK (name ~ '^[가-힣A-Za-z0-9]{2,8}$'),
  class      TEXT NOT NULL CHECK (class IN ('warrior', 'mage')),
  level      INT NOT NULL DEFAULT 1 CHECK (level >= 1),
  xp         INT NOT NULL DEFAULT 0 CHECK (xp >= 0),
  gold       BIGINT NOT NULL DEFAULT 0 CHECK (gold >= 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  deleted_at TIMESTAMPTZ
);
COMMENT ON TABLE  characters IS '온라인 캐릭터. level·xp·gold는 서버 판정(3단계)만 바꾼다';
COMMENT ON COLUMN characters.name IS '2~8자, 한글 완성형·영문·숫자. 서버가 NFC 정규화 후 저장';
COMMENT ON COLUMN characters.class IS 'CharacterClass: warrior | mage';
COMMENT ON COLUMN characters.level IS '2단계에서는 항상 1. 클라이언트 요청으로 바뀌지 않는다';
COMMENT ON COLUMN characters.xp IS '현재 레벨 내 경험치. 클라이언트 요청으로 바뀌지 않는다';
COMMENT ON COLUMN characters.gold IS '잔액. 변경은 gold_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN characters.deleted_at IS '소프트 삭제. 삭제하면 이름과 계정 슬롯이 풀린다';
-- 살아 있는 캐릭터끼리만 이름 중복 불가 (대소문자 무시). 생성 시 23505를 NAME_TAKEN으로 바꾼다
CREATE UNIQUE INDEX characters_name_alive ON characters (lower(name)) WHERE deleted_at IS NULL;
-- 목록 조회와 계정당 최대 4개 검사
CREATE INDEX characters_account_alive ON characters (account_id) WHERE deleted_at IS NULL;

CREATE TABLE character_state (
  character_id BIGINT PRIMARY KEY REFERENCES characters(id),
  map_id       TEXT NOT NULL,
  pos_x        REAL,
  pos_y        REAL,
  facing       SMALLINT NOT NULL DEFAULT 0 CHECK (facing >= 0),
  quests       JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(quests) = 'array'),
  story_flags  TEXT[] NOT NULL DEFAULT '{}',
  tracked_quest TEXT NOT NULL DEFAULT '',
  passives     TEXT[] NOT NULL DEFAULT '{}',
  skill_gems   JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(skill_gems) = 'array'),
  version      INT NOT NULL DEFAULT 0 CHECK (version >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT character_state_pos_pair CHECK ((pos_x IS NULL) = (pos_y IS NULL))
);
COMMENT ON TABLE  character_state IS '재화가 아닌 상태. 클라이언트가 PUT으로 통째로 저장하고 서버는 검증만 한다';
COMMENT ON COLUMN character_state.map_id IS 'maps.json의 instanced=false 맵 id';
COMMENT ON COLUMN character_state.pos_x IS '월드 좌표. NULL(pos_y도 NULL) = 맵 시작 지점';
COMMENT ON COLUMN character_state.facing IS 'Facing enum 값. 상한은 enums.json facingCount';
COMMENT ON COLUMN character_state.quests IS '[{id,status,step,counts[]}] QuestSave 배열';
COMMENT ON COLUMN character_state.story_flags IS 'QuestJournal.Flags';
COMMENT ON COLUMN character_state.tracked_quest IS '추적 중인 퀘스트 id. 없으면 빈 문자열';
COMMENT ON COLUMN character_state.passives IS '찍은 패시브 노드 id (시작 노드 S 제외). 개수 <= 레벨-1';
COMMENT ON COLUMN character_state.skill_gems IS '[{slot,supports:[id|null,id|null]}] 보조 젬 장착';
COMMENT ON COLUMN character_state.version IS '낙관적 잠금. 저장 성공마다 +1, 생성 직후 0. 재화 변경은 올리지 않는다';

CREATE TABLE character_items (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  item_key     TEXT NOT NULL,
  count        INT NOT NULL CHECK (count > 0),
  location     TEXT NOT NULL CHECK (location IN ('bag', 'storage', 'worn', 'mail', 'auction')),
  slot         INT CHECK (slot >= 0),
  bind         TEXT NOT NULL DEFAULT 'none' CHECK (bind IN ('none', 'account', 'character')),
  version      INT NOT NULL DEFAULT 0,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  character_items IS '캐릭터 소지품. 2단계는 시작 지급품(bag, worn)만 만든다. 변경은 item_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN character_items.item_key IS '게임 데이터 아이템 키. 장비는 기본 id 또는 "id+강화단계"';
COMMENT ON COLUMN character_items.location IS 'bag 가방 / storage 창고 / worn 착용 / mail 우편 / auction 경매 보관';
COMMENT ON COLUMN character_items.slot IS 'worn은 EquipSlot 번호. bag·storage 쌓이는 물건은 NULL';
COMMENT ON COLUMN character_items.version IS '3단계 이후 낙관적 잠금용';
-- 한 캐릭터의 같은 위치·슬롯에 두 아이템이 겹치지 않게 (착용 슬롯 등)
CREATE UNIQUE INDEX character_items_slot_uq ON character_items (character_id, location, slot) WHERE slot IS NOT NULL;
-- 캐릭터 상세에서 가방·착용 목록을 한 번에 읽는다
CREATE INDEX character_items_char_loc ON character_items (character_id, location);

-- ---------- 멱등성·원장 ----------

CREATE TABLE request_log (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  request_id   UUID NOT NULL,
  endpoint     TEXT NOT NULL,
  request_hash TEXT NOT NULL,
  status_code  SMALLINT NOT NULL,
  response     JSONB NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id)
);
COMMENT ON TABLE  request_log IS '멱등성: 같은 계정의 같은 request_id는 처음 응답을 그대로 돌려준다';
COMMENT ON COLUMN request_log.endpoint IS '"POST /characters" 같은 메서드+경로 패턴';
COMMENT ON COLUMN request_log.request_hash IS '요청 본문 정규화 해시. 같은 id에 다른 본문이 오면 IDEMPOTENCY_MISMATCH';
COMMENT ON COLUMN request_log.response IS '처음 응답의 body 전체';
-- 보관 기간이 지난 행을 지우는 정리 작업용
CREATE INDEX request_log_created_idx ON request_log (created_at);

CREATE TABLE gold_ledger (
  id            BIGSERIAL PRIMARY KEY,
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  delta         BIGINT NOT NULL CHECK (delta <> 0),
  balance_after BIGINT NOT NULL CHECK (balance_after >= 0),
  reason        TEXT NOT NULL CHECK (reason IN ('starter')),
  ref           TEXT,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  gold_ledger IS '골드 변동 원장. 추가만 한다(트리거가 UPDATE/DELETE 차단)';
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. 3단계가 kill, quest, shop_buy 등을 마이그레이션으로 추가한다';
COMMENT ON COLUMN gold_ledger.ref IS '사유별 참조(캐릭터 uuid, 퀘스트 id 등)';
CREATE INDEX gold_ledger_char_idx ON gold_ledger (character_id, id);
-- 시작 지급은 캐릭터당 한 번만 (DB 수준 이중 지급 방지)
CREATE UNIQUE INDEX gold_ledger_starter_uq ON gold_ledger (character_id) WHERE reason = 'starter';
CREATE TRIGGER gold_ledger_append_only BEFORE UPDATE OR DELETE ON gold_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER gold_ledger_no_truncate BEFORE TRUNCATE ON gold_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

CREATE TABLE item_ledger (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  item_key     TEXT NOT NULL,
  delta        INT NOT NULL CHECK (delta <> 0),
  reason       TEXT NOT NULL CHECK (reason IN ('starter')),
  ref          TEXT,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  item_ledger IS '아이템 수량 변동 원장. 추가만 한다(트리거가 UPDATE/DELETE 차단)';
COMMENT ON COLUMN item_ledger.delta IS '양수 지급, 음수 소모';
COMMENT ON COLUMN item_ledger.reason IS '변동 사유. 3단계가 마이그레이션으로 추가한다';
CREATE INDEX item_ledger_char_idx ON item_ledger (character_id, id);
CREATE UNIQUE INDEX item_ledger_starter_uq ON item_ledger (character_id, item_key) WHERE reason = 'starter';
CREATE TRIGGER item_ledger_append_only BEFORE UPDATE OR DELETE ON item_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER item_ledger_no_truncate BEFORE TRUNCATE ON item_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- =====================================================================================
-- 0002_economy (3단계 경제 판정). 아래는 migrations/0002_economy.sql 의 UP 본문이다.
-- =====================================================================================

-- ---------- 3단계: 기존 원장 확장 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost'));
ALTER TABLE gold_ledger ADD COLUMN request_id UUID;
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. drop_claim 줍기 / quest_reward 퀘스트 보상 / shop_buy·shop_sell 상점 / enhance_cost 강화 비용. 던전 카드(dungeon_card)는 0003이 추가';
COMMENT ON COLUMN gold_ledger.ref IS 'drop_claim: 드롭 uuid / quest_reward: 퀘스트 id / shop_*: 아이템 id / enhance_cost: enhance_log uuid';
COMMENT ON COLUMN gold_ledger.request_id IS '이 변동을 만든 요청의 request_id. 한 요청이 만든 모든 원장 행을 찾는다. starter 행은 NULL 가능';
CREATE INDEX gold_ledger_request_idx ON gold_ledger (request_id) WHERE request_id IS NOT NULL;
CREATE UNIQUE INDEX gold_ledger_drop_uq ON gold_ledger (ref) WHERE reason = 'drop_claim';

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item'));
ALTER TABLE item_ledger ADD COLUMN location TEXT NOT NULL DEFAULT 'bag'
  CHECK (location IN ('bag', 'storage', 'worn', 'mail', 'auction'));
ALTER TABLE item_ledger ADD COLUMN balance_after INT;
ALTER TABLE item_ledger ADD COLUMN request_id UUID;

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
UPDATE item_ledger l SET location = 'worn'
 WHERE l.reason = 'starter'
   AND EXISTS (SELECT 1 FROM character_items ci
                WHERE ci.character_id = l.character_id AND ci.item_key = l.item_key AND ci.location = 'worn');
UPDATE item_ledger SET balance_after = delta WHERE reason = 'starter' AND balance_after IS NULL;
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;

ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_balance_chk
  CHECK (balance_after >= 0 AND (reason = 'starter' OR balance_after IS NOT NULL));
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_no_gold CHECK (item_key <> 'gold');
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). enhance_result는 강화 결과로 키가 바뀌는 쌍(-1 옛 키, +1 새 키) / equip·unequip·storage_move는 위치 이동 쌍 / use_item은 물약·주문서·당근 소모. 던전 카드는 0003이 추가';
COMMENT ON COLUMN item_ledger.ref IS 'drop_claim: 드롭 uuid / gather: 노드 id / chest: 상자 id / quest_*: 퀘스트 id / delivery: 납품처 id / shop_*: 아이템 id / enhance_*: enhance_log uuid / use_item: 아이템 id';
COMMENT ON COLUMN item_ledger.location IS '이 변동이 일어난 위치. 위치 이동은 (출발 -n, 도착 +n) 두 행. 합계는 (character_id, location, item_key)별로 character_items.count와 같다';
COMMENT ON COLUMN item_ledger.balance_after IS '변동 직후 그 위치의 수량(0 가능). starter 행만 NULL 허용';
COMMENT ON COLUMN item_ledger.request_id IS '이 변동을 만든 요청의 request_id. starter 행은 NULL 가능';
CREATE INDEX item_ledger_request_idx ON item_ledger (request_id) WHERE request_id IS NOT NULL;
CREATE UNIQUE INDEX item_ledger_drop_uq ON item_ledger (ref) WHERE reason = 'drop_claim';

-- ---------- 3단계: 소지품 재고 규칙 ----------

CREATE UNIQUE INDEX character_items_stack_uq ON character_items (character_id, location, item_key)
  WHERE location IN ('bag', 'storage');
ALTER TABLE character_items ADD CONSTRAINT character_items_no_gold CHECK (item_key <> 'gold');
COMMENT ON TABLE  character_items IS '캐릭터 소지품. 변경은 item_ledger와 같은 트랜잭션에서만. bag·storage는 키별 한 행(수량 합), worn은 슬롯별 한 행(count=1). 수량이 0이 되면 행을 지운다';
COMMENT ON COLUMN character_items.item_key IS '게임 데이터 아이템 키. 장비는 기본 id 또는 "id+강화단계". 골드는 아이템이 아니라 characters.gold';
COMMENT ON COLUMN character_items.version IS '행이 바뀔 때마다 +1(진단용). 동시성은 characters 행 잠금이 맡는다';

-- ---------- 3단계: 경험치 원장 ----------

CREATE TABLE xp_ledger (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  delta        INT NOT NULL CHECK (delta > 0),
  level_after  INT NOT NULL CHECK (level_after >= 1),
  xp_after     INT NOT NULL CHECK (xp_after >= 0),
  reason       TEXT NOT NULL CHECK (reason IN ('kill', 'quest_reward')),
  ref          TEXT,
  request_id   UUID,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  xp_ledger IS '경험치 지급 원장. 추가만 한다. characters.level/xp는 이 원장과 같은 트랜잭션에서만 바뀐다. 만렙에서 버려지는 경험치는 기록하지 않는다';
COMMENT ON COLUMN xp_ledger.delta IS '요청이 준 경험치(몬스터 레벨 보정 후). 레벨업으로 xp가 줄어도 양수';
COMMENT ON COLUMN xp_ledger.level_after IS '지급 직후 레벨. 레벨 변화 이력 조회용';
COMMENT ON COLUMN xp_ledger.xp_after IS '지급 직후 현재 레벨 내 경험치';
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상. 던전 클리어(dungeon_clear)는 0003이 추가';
COMMENT ON COLUMN xp_ledger.ref IS 'kill: 몬스터 id / quest_reward: 퀘스트 id';
CREATE INDEX xp_ledger_char_idx ON xp_ledger (character_id, id);
CREATE TRIGGER xp_ledger_append_only BEFORE UPDATE OR DELETE ON xp_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER xp_ledger_no_truncate BEFORE TRUNCATE ON xp_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 3단계: 처치 보고 ----------

CREATE TABLE kill_log (
  id            BIGSERIAL PRIMARY KEY,
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  map_id        TEXT NOT NULL,
  monster_id    TEXT NOT NULL,
  monster_level SMALLINT NOT NULL CHECK (monster_level >= 1),
  context       TEXT NOT NULL CHECK (context IN ('field', 'scripted', 'dungeon')),
  hits          SMALLINT NOT NULL DEFAULT 0 CHECK (hits >= 0),
  xp_granted    INT NOT NULL CHECK (xp_granted >= 0),
  request_id    UUID NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  kill_log IS '서버가 받아들인 처치 보고(그럴듯함 검사를 통과한 것만). 속도 창 계산과 퀘스트 처치 검증, 사후 추적에 쓴다. 7일 보관 후 정리';
COMMENT ON COLUMN kill_log.map_id IS '보고된 맵 id. 던전 처치는 방 맵 id';
COMMENT ON COLUMN kill_log.monster_level IS '서버가 계산한 몬스터 레벨(필드 1, 던전 1+난이도 몬스터 레벨+그룹 오프셋). 요청으로 받지 않는다';
COMMENT ON COLUMN kill_log.context IS 'field 필드 스포너 / scripted 연출 스폰(마을 해골 습격) / dungeon 던전 방(0003의 run_id와 함께)';
COMMENT ON COLUMN kill_log.hits IS '황금 해골류가 타격마다 흘린 골드 횟수(서버가 상한으로 자른 값). 그 밖의 몬스터는 0';
COMMENT ON COLUMN kill_log.xp_granted IS '이 처치로 지급한 경험치(레벨 보정 후)';
CREATE INDEX kill_log_char_time ON kill_log (character_id, created_at);
CREATE INDEX kill_log_created_idx ON kill_log (created_at);

CREATE TABLE kill_stats (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  monster_id   TEXT NOT NULL,
  kills        BIGINT NOT NULL DEFAULT 0 CHECK (kills >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, monster_id)
);
COMMENT ON TABLE  kill_stats IS '캐릭터·몬스터별 누적 처치 수. 퀘스트 처치 목표 검증(서버가 받아들인 처치만 센다)과 통계. kill_log 정리와 무관하게 평생 누적';
COMMENT ON COLUMN kill_stats.kills IS '받아들인 처치 누적. 처치를 받아들일 때 같은 트랜잭션에서 +1';

-- ---------- 3단계: 드롭 ----------

CREATE TABLE drops (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  kill_id      BIGINT NOT NULL REFERENCES kill_log(id) ON DELETE CASCADE,
  item_key     TEXT NOT NULL,
  count        INT NOT NULL CHECK (count > 0),
  expires_at   TIMESTAMPTZ NOT NULL,
  claimed_at   TIMESTAMPTZ,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  drops IS '서버가 굴린 드롭. 줍기는 uuid로만, 한 번만. 한 행 = 바닥의 줍기 물체 하나(골드 더미, 재료 1개, 장비 1개)';
COMMENT ON COLUMN drops.uuid IS '외부 노출 id(응답의 drop id). 줍기 요청이 이 값으로만 온다';
COMMENT ON COLUMN drops.item_key IS '아이템 키. 골드 더미는 "gold"(이 테이블에서만 허용)';
COMMENT ON COLUMN drops.count IS '골드면 금액, 그 밖에는 개수';
COMMENT ON COLUMN drops.expires_at IS '이 시각이 지나면 줍기 불가(미수령은 그냥 사라진다)';
COMMENT ON COLUMN drops.claimed_at IS '줍기 완료 시각. NULL = 미수령. UPDATE ... WHERE claimed_at IS NULL AND expires_at > now() 한 문장으로 한 번만 성립';
CREATE INDEX drops_char_open ON drops (character_id) WHERE claimed_at IS NULL;
CREATE INDEX drops_expires_idx ON drops (expires_at);

-- ---------- 3단계: 채집 노드, 상자, 납품 ----------

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

CREATE TABLE character_chests (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  chest_id     TEXT NOT NULL,
  opened_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, chest_id)
);
COMMENT ON TABLE  character_chests IS '연 상자(SaveData.openedChests). PK가 "캐릭터당 한 번"을 DB 수준에서 보장한다';
COMMENT ON COLUMN character_chests.chest_id IS '"맵id:x:y" (WorldBuilder의 TreasureChest id와 같은 규칙)';

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

-- ---------- 3단계: 퀘스트 청구 ----------

CREATE TABLE quest_claims (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  quest_id     TEXT NOT NULL,
  claimed_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  reward       JSONB NOT NULL CHECK (jsonb_typeof(reward) = 'object'),
  request_id   UUID,
  PRIMARY KEY (character_id, quest_id)
);
COMMENT ON TABLE  quest_claims IS '퀘스트 보상 청구 기록. PK가 "퀘스트당 한 번"을 DB 수준에서 보장한다. character_state.quests(클라이언트 저장 상태)와 무관한 정본';
COMMENT ON COLUMN quest_claims.reward IS '지급 당시 보상 스냅샷 {xp, gold, items:[{item_key,count}], max_health, set_flags:[], consumed:[{item_key,count}]}. 최대 체력 보너스 합계는 이 값에서 계산(max_health)';
COMMENT ON COLUMN quest_claims.request_id IS '청구 요청의 request_id (원장 행과 연결)';

-- ---------- 3단계: 강화 ----------

CREATE TABLE character_enhance_pity (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  item_key     TEXT NOT NULL,
  pity         SMALLINT NOT NULL CHECK (pity BETWEEN 0 AND 100),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, item_key)
);
COMMENT ON TABLE  character_enhance_pity IS '강화 천장(보정 %p). 무기 +10/+11 시도에서 실패마다 +1, 성공하면 그 키의 행을 지운다. 키(예: eq_sword_iron+10) 단위라 같은 키의 모든 복사본이 공유한다(C# Equipment.pity와 같다)';
COMMENT ON COLUMN character_enhance_pity.item_key IS '시도하는 쪽 키("id+레벨"). 시도 레벨 키로만 쌓인다';
COMMENT ON COLUMN character_enhance_pity.pity IS '보정 %p. 상한 100(서버 데이터 enhance.json maxPity)';

CREATE TABLE enhance_log (
  id              BIGSERIAL PRIMARY KEY,
  uuid            UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  request_id      UUID NOT NULL,
  from_key        TEXT NOT NULL,
  to_key          TEXT,
  target_location TEXT NOT NULL CHECK (target_location IN ('bag', 'worn')),
  target_slot     SMALLINT CHECK (target_slot >= 0),
  outcome         TEXT NOT NULL CHECK (outcome IN ('success', 'keep', 'drop3', 'destroyed', 'protected')),
  roll            SMALLINT NOT NULL CHECK (roll BETWEEN 0 AND 99),
  success_percent SMALLINT NOT NULL CHECK (success_percent BETWEEN 0 AND 100),
  pity_before     SMALLINT NOT NULL CHECK (pity_before BETWEEN 0 AND 100),
  pity_after      SMALLINT NOT NULL CHECK (pity_after BETWEEN 0 AND 100),
  gold            INT NOT NULL CHECK (gold >= 0),
  bone            INT NOT NULL CHECK (bone >= 0),
  ore             INT NOT NULL CHECK (ore >= 0),
  essence         INT NOT NULL CHECK (essence >= 0),
  ticket_used     BOOLEAN NOT NULL DEFAULT false,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT enhance_log_slot_chk CHECK ((target_location = 'worn') = (target_slot IS NOT NULL))
);
COMMENT ON TABLE  enhance_log IS '강화 시도 기록(추가만). 굴린 값·확률·비용을 남겨 분쟁과 확률 통계를 검증한다. 원장 행은 이 행의 uuid를 ref로 가리킨다';
COMMENT ON COLUMN enhance_log.from_key IS '시도 전 키';
COMMENT ON COLUMN enhance_log.to_key IS '시도 후 키. 파괴되면 NULL. protected(보호권)는 +0 기본 id';
COMMENT ON COLUMN enhance_log.outcome IS 'success 성공 / keep 실패·유지 / drop3 실패·3단계 하락 / destroyed 파괴 / protected 파괴를 보호권이 막아 +0으로 초기화';
COMMENT ON COLUMN enhance_log.roll IS '서버 RNG 0..99. roll < success_percent 이면 성공';
COMMENT ON COLUMN enhance_log.success_percent IS '표 확률 + 천장 보정(상한 100)';
COMMENT ON COLUMN enhance_log.pity_before IS '시도 직전 천장 값(천장 없는 레벨은 0)';
COMMENT ON COLUMN enhance_log.pity_after IS '시도 직후 천장 값';
COMMENT ON COLUMN enhance_log.gold IS '낸 골드(재료는 bone/ore/essence). 보호권 소모는 ticket_used';
CREATE INDEX enhance_log_char_idx ON enhance_log (character_id, id);
CREATE TRIGGER enhance_log_append_only BEFORE UPDATE OR DELETE ON enhance_log
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER enhance_log_no_truncate BEFORE TRUNCATE ON enhance_log
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 3단계: 이상 기록 ----------

CREATE TABLE anomaly_log (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT REFERENCES characters(id),
  kind         TEXT NOT NULL CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                                             'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown')),
  severity     SMALLINT NOT NULL DEFAULT 1 CHECK (severity BETWEEN 1 AND 3),
  detail       JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  anomaly_log IS '그럴듯하지 않아 지급을 막은 요청의 기록(PLAN_SERVER §5 "지급 보류 + 기록"). 지급을 미루지 않고 거절하며 기록만 남긴다. 제재는 7단계 관리자 도구가 이 표를 본다';
COMMENT ON COLUMN anomaly_log.kind IS 'kill_target 맵·몬스터 불일치 / kill_rate 1초 창 초과 / kill_supply 리스폰 공급 초과 / kill_power 화력 상한 초과 / gather_node 없는 노드 / gather_early 재생 전 채집 / drop_foreign 남의 드롭 id / quest_denied 근거 없는 퀘스트 청구 / chest_unknown 없는 상자';
COMMENT ON COLUMN anomaly_log.severity IS '1 참고(경계 오차 가능) / 2 의심 / 3 불가능한 값. 반복 횟수 기준 차단은 service 설정';
COMMENT ON COLUMN anomaly_log.detail IS '요청 값과 판정 근거(창 안 수, 한도, 맵·몬스터 등). 원본 요청 본문 전체는 넣지 않는다';
CREATE INDEX anomaly_log_char_time ON anomaly_log (character_id, created_at) WHERE character_id IS NOT NULL;
CREATE INDEX anomaly_log_created_idx ON anomaly_log (created_at);

-- =====================================================================================
-- 0003_dungeon_solo (3단계-b, 선택: 솔로 요일 던전). 아래는 migrations/0003_dungeon_solo.sql 의 UP 본문이다.
-- 채택하지 않으면 이 구역을 지운다(설계 문서 Docs/server/phase3_api.md 9절).
-- =====================================================================================

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
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result'));

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
CREATE UNIQUE INDEX dungeon_runs_one_playing ON dungeon_runs (character_id) WHERE state = 'playing';
CREATE INDEX dungeon_runs_char_day ON dungeon_runs (character_id, reset_day);
CREATE INDEX dungeon_runs_clears ON dungeon_runs (character_id, dungeon_id, difficulty) WHERE state = 'cleared';

ALTER TABLE kill_log ADD COLUMN run_id BIGINT REFERENCES dungeon_runs(id);
ALTER TABLE kill_log ADD COLUMN room_index SMALLINT CHECK (room_index >= 0);
ALTER TABLE kill_log ADD CONSTRAINT kill_log_run_chk CHECK ((run_id IS NULL) = (room_index IS NULL) AND ((context = 'dungeon') = (run_id IS NOT NULL)));
COMMENT ON COLUMN kill_log.run_id IS '던전 처치일 때 dungeon_runs.id. 필드·연출 처치는 NULL';
COMMENT ON COLUMN kill_log.room_index IS '던전 방 번호(run_id와 함께)';
CREATE INDEX kill_log_run_idx ON kill_log (run_id) WHERE run_id IS NOT NULL;

-- =====================================================================================
-- 0005_party (4단계 파티 협동). 아래는 migrations/0005_party.sql 의 UP 본문이다. 설계: Docs/server/phase4_api.md
-- 락 순서(4단계 추가): ① 필요한 캐릭터를 id 오름차순으로 한 번에 FOR UPDATE ② parties ③ party_runs.
-- 매칭 대기열은 표가 없다(서버 메모리, QueueStore 인터페이스 뒤).
-- =====================================================================================

CREATE UNIQUE INDEX auth_identities_account_provider_uq ON auth_identities (account_id, provider);

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter'));

CREATE TABLE parties (
  id                  BIGSERIAL PRIMARY KEY,
  uuid                UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  leader_character_id BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id          TEXT NOT NULL,
  difficulty          SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  max_members         SMALLINT NOT NULL CHECK (max_members BETWEEN 2 AND 4),
  min_power           INT NOT NULL DEFAULT 0 CHECK (min_power >= 0),
  message             TEXT NOT NULL DEFAULT '' CHECK (char_length(message) <= 30),
  source              TEXT NOT NULL CHECK (source IN ('board', 'match')),
  listed              BOOLEAN NOT NULL,
  listed_until        TIMESTAMPTZ,
  state               TEXT NOT NULL DEFAULT 'forming' CHECK (state IN ('forming', 'starting', 'in_run', 'closed')),
  version             INT NOT NULL DEFAULT 0 CHECK (version >= 0),
  start_by            TIMESTAMPTZ,
  last_active_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  closed_at           TIMESTAMPTZ,
  close_reason        TEXT CHECK (close_reason IN ('disbanded', 'idle_timeout', 'start_timeout')),
  CONSTRAINT parties_listed_chk CHECK (NOT listed OR listed_until IS NOT NULL),
  CONSTRAINT parties_closed_chk CHECK ((state = 'closed') = (closed_at IS NOT NULL) AND (state = 'closed') = (close_reason IS NOT NULL))
);
COMMENT ON TABLE  parties IS '파티 로비. 모집 글(listed)과 자동 매칭 결과(source=match)를 같은 표로 둔다. 판이 끝나도 파티는 남아 다시 도전할 수 있다';
COMMENT ON COLUMN parties.uuid IS '외부 노출 id(모집 글 id, 참가 신청 경로). 내부 키 id는 API에 나가지 않는다';
COMMENT ON COLUMN parties.leader_character_id IS '방장(로비 방장 = 판의 첫 호스트). 활성 멤버여야 한다. 방장이 나가면 가장 오래된 멤버로 서버가 즉시 넘긴다';
COMMENT ON COLUMN parties.dungeon_id IS 'dungeons.json 던전 id. 요일 던전과 레이드 모두 가능';
COMMENT ON COLUMN parties.difficulty IS 'DungeonDifficulty 0~3. 레이드는 0';
COMMENT ON COLUMN parties.max_members IS '사람 정원 2~4. 빈자리 AI는 정원에 세지 않고 출발할 때 정한다';
COMMENT ON COLUMN parties.min_power IS '참가 최소 전투력. 서버가 계산한 power_estimate와 비교하고 방장 자신의 값을 넘을 수 없다';
COMMENT ON COLUMN parties.message IS '모집 메시지 30자 이하. 금칙어는 서버가 최종 판정한다';
COMMENT ON COLUMN parties.source IS 'board 방장이 만든 모집 글 / match 자동 매칭이 만든 파티(멤버 준비 완료 상태로 시작)';
COMMENT ON COLUMN parties.listed IS '모집 게시판에 보이는가. false면 신청을 받지 않는다(비공개 파티는 5단계 초대용)';
COMMENT ON COLUMN parties.listed_until IS '게시 만료(만든 뒤 10분). 만료돼도 파티는 남고 목록에서만 빠진다';
COMMENT ON COLUMN parties.state IS 'forming 모집·로비 / starting 판 모으는 중(party_runs.gathering) / in_run 판 진행 중 / closed 해산';
COMMENT ON COLUMN parties.version IS '변경될 때마다 +1. 클라이언트 폴링이 after_version으로 변화를 확인한다';
COMMENT ON COLUMN parties.start_by IS '자동 매칭 파티의 출발 기한(만든 뒤 60초). 넘기면 해산한다. board 파티는 NULL';
COMMENT ON COLUMN parties.last_active_at IS '마지막 활동 시각. 30분 지나면 지연 정리가 해산한다';
CREATE INDEX parties_board ON parties (dungeon_id, difficulty, created_at DESC) WHERE listed AND state = 'forming';
CREATE INDEX parties_leader ON parties (leader_character_id) WHERE state <> 'closed';
CREATE INDEX parties_idle ON parties (last_active_at) WHERE state <> 'closed';

CREATE TABLE party_members (
  id           BIGSERIAL PRIMARY KEY,
  party_id     BIGINT NOT NULL REFERENCES parties(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  ready        BOOLEAN NOT NULL DEFAULT false,
  joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  left_at      TIMESTAMPTZ,
  left_reason  TEXT CHECK (left_reason IN ('left', 'kicked', 'disbanded', 'idle_timeout', 'start_timeout', 'matched_other')),
  CONSTRAINT party_members_left_chk CHECK ((left_at IS NULL) = (left_reason IS NULL))
);
COMMENT ON TABLE  party_members IS '파티 멤버(한 번의 소속 = 한 행). 나가면 left_at을 채우고 행은 남긴다(알림·분쟁 확인용)';
COMMENT ON COLUMN party_members.ready IS '준비 완료. 방장은 항상 준비로 본다. 구성이 바뀌면(입장·방장 설정 변경) 멤버의 준비는 해제된다';
COMMENT ON COLUMN party_members.joined_at IS '입장 시각. 방장이 나갈 때 가장 오래된 멤버가 다음 방장';
COMMENT ON COLUMN party_members.left_reason IS 'left 스스로 / kicked 강퇴 / disbanded 파티 해산 / idle_timeout·start_timeout 지연 정리 / matched_other 다른 매칭 파티로 이동';
CREATE UNIQUE INDEX party_members_one_active ON party_members (character_id) WHERE left_at IS NULL;
CREATE INDEX party_members_party ON party_members (party_id) WHERE left_at IS NULL;
CREATE INDEX party_members_notice ON party_members (character_id, left_at DESC) WHERE left_at IS NOT NULL;

CREATE TABLE party_applications (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id       BIGINT NOT NULL REFERENCES parties(id),
  character_id   BIGINT NOT NULL REFERENCES characters(id),
  account_id     BIGINT NOT NULL REFERENCES accounts(id),
  state          TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'accepted', 'rejected', 'expired', 'cancelled')),
  power_estimate INT NOT NULL CHECK (power_estimate >= 0),
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at     TIMESTAMPTZ NOT NULL,
  responded_at   TIMESTAMPTZ
);
COMMENT ON TABLE  party_applications IS '참가 신청. 방장이 수락·거절하고, 30초 무응답이면 만료(지연 처리)';
COMMENT ON COLUMN party_applications.power_estimate IS '신청 시점에 서버가 계산한 전투력(방장 화면 표시용 스냅샷). 클라이언트 값이 아니다';
COMMENT ON COLUMN party_applications.expires_at IS '신청 시각 + 30초';
CREATE UNIQUE INDEX party_applications_pending_uq ON party_applications (party_id, character_id) WHERE state = 'pending';
CREATE INDEX party_applications_party ON party_applications (party_id) WHERE state = 'pending';
CREATE INDEX party_applications_char ON party_applications (character_id, created_at DESC);

CREATE TABLE party_runs (
  id                 BIGSERIAL PRIMARY KEY,
  uuid               UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id           BIGINT NOT NULL REFERENCES parties(id),
  dungeon_id         TEXT NOT NULL,
  difficulty         SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  state              TEXT NOT NULL DEFAULT 'gathering' CHECK (state IN ('gathering', 'playing', 'ended', 'cancelled')),
  host_character_id  BIGINT NOT NULL REFERENCES characters(id),
  host_epoch         INT NOT NULL DEFAULT 1 CHECK (host_epoch >= 1),
  humans             SMALLINT NOT NULL CHECK (humans BETWEEN 1 AND 4),
  ai_count           SMALLINT NOT NULL DEFAULT 0 CHECK (ai_count BETWEEN 0 AND 3),
  run_key            BYTEA CHECK (octet_length(run_key) = 32),
  power_cap          NUMERIC(14, 3) CHECK (power_cap > 0),
  gather_deadline_at TIMESTAMPTZ NOT NULL,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
  begun_at           TIMESTAMPTZ,
  first_report_at    TIMESTAMPTZ,
  ended_at           TIMESTAMPTZ,
  cancel_reason      TEXT CHECK (cancel_reason IN ('timeout', 'host_cancel', 'nobody_joined', 'ineligible')),
  CONSTRAINT party_runs_begun_chk  CHECK ((state IN ('playing', 'ended')) = (begun_at IS NOT NULL)),
  CONSTRAINT party_runs_end_chk    CHECK ((state IN ('ended', 'cancelled')) = (ended_at IS NOT NULL)),
  CONSTRAINT party_runs_cancel_chk CHECK ((state = 'cancelled') = (cancel_reason IS NOT NULL)),
  CONSTRAINT party_runs_size_chk   CHECK (humans + ai_count <= 4)
);
COMMENT ON TABLE  party_runs IS '같은 던전을 함께 도는 한 판(PLAN_ONLINE의 run_instances). 멤버별 보상 행은 dungeon_runs, 이 표는 방장·인원·시작과 호스트 세대만 가진다';
COMMENT ON COLUMN party_runs.uuid IS '외부 노출 id. 입장 토큰의 기준, 호스트 보고·하트비트 경로';
COMMENT ON COLUMN party_runs.host_character_id IS '지금 몬스터를 계산하는 방장 PC의 캐릭터. 호스트 인계로 바뀐다';
COMMENT ON COLUMN party_runs.host_epoch IS '호스트 세대. 인계마다 +1. 옛 세대의 보고는 거절한다(스플릿 브레인 방지)';
COMMENT ON COLUMN party_runs.humans IS 'gathering 동안은 출발 때 사람 수, begin 때 실제로 들어온 사람 수로 고정';
COMMENT ON COLUMN party_runs.ai_count IS '빈자리 AI 수. begin에서 고정(방장이 정한 수 + 불참 인원, 합이 4 이하). 던전 안에서 바꾸지 않는다';
COMMENT ON COLUMN party_runs.run_key IS '이 판의 입장 토큰 서명 키(난수 32바이트). 방장 PC만 받는다(출발 응답·호스트 인계 응답). 판이 끝나면 NULL';
COMMENT ON COLUMN party_runs.power_cap IS 'begin 때 계산한 파티 화력 상한(사람 멤버 attackCap 합 + AI 몫). 처치 보고의 화력 검사가 쓴다';
COMMENT ON COLUMN party_runs.gather_deadline_at IS '입장 마감(출발 + 90초). 넘으면 들어온 사람으로 시작하거나 취소';
COMMENT ON COLUMN party_runs.begun_at IS '판 시작 시각. 모든 멤버 dungeon_runs.started_at이 같은 값';
COMMENT ON COLUMN party_runs.first_report_at IS '첫 결과 보고 시각. 결과 대조 대기 시간의 기준';
CREATE UNIQUE INDEX party_runs_one_active ON party_runs (party_id) WHERE state IN ('gathering', 'playing');
CREATE INDEX party_runs_active_idx ON party_runs (created_at) WHERE state IN ('gathering', 'playing');

ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_state_check;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_state_check
  CHECK (state IN ('playing', 'reported', 'cleared', 'failed', 'abandoned', 'held'));
ALTER TABLE dungeon_runs ADD COLUMN party_run_id BIGINT REFERENCES party_runs(id);
ALTER TABLE dungeon_runs ADD COLUMN slot SMALLINT CHECK (slot BETWEEN 0 AND 3);
ALTER TABLE dungeon_runs ADD COLUMN humans SMALLINT NOT NULL DEFAULT 1 CHECK (humans BETWEEN 1 AND 4);
ALTER TABLE dungeon_runs ADD COLUMN ai_count SMALLINT NOT NULL DEFAULT 0 CHECK (ai_count BETWEEN 0 AND 3);
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_size_chk CHECK (party_size = humans + ai_count);
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_party_chk CHECK ((party_run_id IS NULL) = (slot IS NULL));
ALTER TABLE dungeon_runs ADD COLUMN counts_entry BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE dungeon_runs ADD COLUMN reward_locked BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE dungeon_runs ADD COLUMN lock_reason TEXT CHECK (lock_reason IN ('ALREADY_CLAIMED', 'TOO_FEW_HUMANS', 'KEYS_MISSING'));
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_lock_chk CHECK (reward_locked = (lock_reason IS NOT NULL));
ALTER TABLE dungeon_runs ADD COLUMN power_cap NUMERIC(14, 3) CHECK (power_cap > 0);
ALTER TABLE dungeon_runs ADD COLUMN reported_outcome TEXT CHECK (reported_outcome IN ('cleared', 'failed'));
ALTER TABLE dungeon_runs ADD COLUMN reported_at TIMESTAMPTZ;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_reported_chk
  CHECK (state <> 'reported' OR (reported_outcome IS NOT NULL AND reported_at IS NOT NULL));
-- 연습 입장(reward_locked)의 클리어는 카드가 없다: 0003의 cleared 제약을 "잠기지 않았으면 카드 필수"로 완화
ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_cleared_chk;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_cleared_chk
  CHECK (state <> 'cleared' OR (rank IS NOT NULL AND ended_at IS NOT NULL AND (reward_locked OR cards IS NOT NULL)));
COMMENT ON COLUMN dungeon_runs.state IS 'playing 진행 / reported 결과 보고함(파티 판: 다른 사람의 보고와 대조 대기) / cleared 클리어(카드 있음) / failed 실패 보고 / abandoned 방치·이탈 / held 불가능해 보여 보상 보류';
COMMENT ON COLUMN dungeon_runs.party_run_id IS '파티 판이면 party_runs.id, 솔로(AI 동반 포함)는 NULL';
COMMENT ON COLUMN dungeon_runs.slot IS '파티 판에서의 자리 0~3(사람은 앞 번호, AI는 뒤). 솔로는 NULL';
COMMENT ON COLUMN dungeon_runs.humans IS '이 판의 사람 수. 파티 크기 = humans + ai_count';
COMMENT ON COLUMN dungeon_runs.ai_count IS '빈자리 AI 수. 몬스터 HP 배율(partyHpScale)과 화력 상한이 이 값을 쓴다';
COMMENT ON COLUMN dungeon_runs.counts_entry IS '일일 입장 횟수에 세는가. 레이드는 false(레이드는 횟수 제한 대신 일일·주간 보상 제한)';
COMMENT ON COLUMN dungeon_runs.reward_locked IS '연습 입장. 클리어 경험치·카드·열쇠·보상 청구가 없다(레이드 보상을 이미 받은 기간, 사람 수 부족, 열쇠 부족)';
COMMENT ON COLUMN dungeon_runs.lock_reason IS 'ALREADY_CLAIMED 이번 기간 보상 수령 / TOO_FEW_HUMANS 보상 최소 인원 미달 / KEYS_MISSING 정산 때 봉인 열쇠 부족';
COMMENT ON COLUMN dungeon_runs.power_cap IS '이 판의 화력 상한 스냅샷(솔로+AI: 본인 attackCap x (1 + AI 몫), 파티: party_runs.power_cap과 같은 값). NULL이면 3단계 방식(본인만)';
COMMENT ON COLUMN dungeon_runs.reported_outcome IS '멤버가 보고한 결과(cleared | failed). 대조 전에는 확정이 아니다';
COMMENT ON COLUMN dungeon_runs.reported_at IS '멤버가 결과를 보고한 시각';
DROP INDEX dungeon_runs_char_day;
CREATE INDEX dungeon_runs_char_day ON dungeon_runs (character_id, reset_day) WHERE counts_entry;
CREATE INDEX dungeon_runs_party_idx ON dungeon_runs (party_run_id) WHERE party_run_id IS NOT NULL;

CREATE TABLE party_run_members (
  party_run_id    BIGINT NOT NULL REFERENCES party_runs(id),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  account_id      BIGINT NOT NULL REFERENCES accounts(id),
  slot            SMALLINT NOT NULL CHECK (slot BETWEEN 0 AND 3),
  state           TEXT NOT NULL DEFAULT 'invited'
                  CHECK (state IN ('invited', 'joined', 'playing', 'disconnected', 'done', 'left', 'no_show', 'dropped')),
  left_reason     TEXT CHECK (left_reason IN ('left', 'rejoin_timeout')),
  dungeon_run_id  BIGINT REFERENCES dungeon_runs(id),
  joined_at       TIMESTAMPTZ,
  last_seen_at    TIMESTAMPTZ,
  disconnected_at TIMESTAMPTZ,
  finished_at     TIMESTAMPTZ,
  PRIMARY KEY (party_run_id, character_id),
  UNIQUE (party_run_id, slot),
  CONSTRAINT party_run_members_left_chk CHECK ((state = 'left') = (left_reason IS NOT NULL))
);
COMMENT ON TABLE  party_run_members IS '판의 멤버(사람만). 입장 토큰 발급·입장 확인·하트비트·호스트 인계 자격의 근거';
COMMENT ON COLUMN party_run_members.slot IS '자리. 방장이 0, 나머지는 파티 입장 순. 호스트 인계는 살아 있는 가장 작은 자리가 이어받는다';
COMMENT ON COLUMN party_run_members.state IS 'invited 출발 알림 받음 / joined 방장에게 연결됨(입장 확인) / playing 판 진행 / disconnected 하트비트 끊김(AI가 대신 싸움, 60초 안에 돌아오면 playing) / done 결과를 보고했거나 판이 끝남 / left 이탈(결과 보상 없음) / no_show 입장 마감까지 안 들어옴 / dropped begin 때 자격 미달';
COMMENT ON COLUMN party_run_members.left_reason IS 'left 스스로 이탈 / rejoin_timeout 끊긴 뒤 60초 안에 못 돌아옴. 판 안에서 강퇴하는 API는 없다(방장이 P2P 연결을 끊어도 서버 기록은 그대로이고, 내보내진 사람은 이탈 API를 부른다)';
COMMENT ON COLUMN party_run_members.dungeon_run_id IS 'begin이 만든 이 멤버의 dungeon_runs 행';
COMMENT ON COLUMN party_run_members.last_seen_at IS '마지막 하트비트(서버 시계). 호스트 생존 판단과 연결 끊김 판단의 근거';
CREATE UNIQUE INDEX party_run_members_one_active ON party_run_members (character_id)
  WHERE state IN ('invited', 'joined', 'playing', 'disconnected');

CREATE TABLE party_run_host_reports (
  id                BIGSERIAL PRIMARY KEY,
  party_run_id      BIGINT NOT NULL REFERENCES party_runs(id),
  host_epoch        INT NOT NULL CHECK (host_epoch >= 1),
  host_character_id BIGINT NOT NULL REFERENCES characters(id),
  request_id        UUID NOT NULL,
  outcome           TEXT NOT NULL CHECK (outcome IN ('cleared', 'failed')),
  elapsed_ms        INT NOT NULL CHECK (elapsed_ms >= 0),
  rooms             JSONB NOT NULL CHECK (jsonb_typeof(rooms) = 'array'),
  members           JSONB NOT NULL CHECK (jsonb_typeof(members) = 'array'),
  ai                JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(ai) = 'array'),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (party_run_id, host_epoch)
);
COMMENT ON TABLE  party_run_host_reports IS '방장이 보낸 판 전체 관찰(결과 대조의 기준). 추가만 한다(트리거가 UPDATE/DELETE 차단). 호스트 세대마다 한 번';
COMMENT ON COLUMN party_run_host_reports.rooms IS '[{room_index, kills:[{monster_id,count}]}] 방별 처치 수(방장이 본 값)';
COMMENT ON COLUMN party_run_host_reports.members IS '[{character_id(uuid), hits_taken, max_combo, revives_used, damage_dealt}] 사람 멤버별 관찰. 지급 값이 아니라 대조·화력 검사용 사실';
COMMENT ON COLUMN party_run_host_reports.ai IS '[{slot, damage_dealt}] AI별 딜. 화력 검사에만 쓴다';
CREATE TRIGGER party_run_host_reports_append_only BEFORE UPDATE OR DELETE ON party_run_host_reports
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER party_run_host_reports_no_truncate BEFORE TRUNCATE ON party_run_host_reports
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

CREATE TABLE raid_claims (
  character_id   BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id     TEXT NOT NULL,
  period_kind    TEXT NOT NULL CHECK (period_kind IN ('daily', 'weekly')),
  period_start   TIMESTAMPTZ NOT NULL,
  dungeon_run_id BIGINT NOT NULL REFERENCES dungeon_runs(id),
  claimed_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, dungeon_id, period_start)
);
COMMENT ON TABLE  raid_claims IS '레이드 보상 수령 기록. PK가 "기간당 한 번"을 DB 수준에서 보장한다. 중간 레이드는 일일(개방 요일마다), 최종 레이드는 주간(목요일 06:00 KST)';
COMMENT ON COLUMN raid_claims.period_kind IS 'daily: raidTier Mid / weekly: raidTier Final (dungeons.json)';
COMMENT ON COLUMN raid_claims.period_start IS 'resetBoundaries의 dailyStartAt 또는 weeklyStartAt(서버 KST 함수 하나)';
COMMENT ON COLUMN raid_claims.dungeon_run_id IS '보상을 받은 판(dungeon_runs). 이번 주 n/3 표시는 같은 주간 구간의 행 수';

-- =====================================================================================
-- 0007_auction (6단계 경매장·우편·시세). 아래는 migrations/0007_auction.sql 의 UP 본문이다.
-- 파일 안에서는 0006_chat_social 구역(맨 아래)보다 앞에 있다(두 단계를 동시에 설계해서 들어온 순서). 적용 순서는 0006 -> 0007이고 서로 의존하지 않는다.
-- 설계 문서 Docs/server/phase6_api.md. 락 순서: 요청자 캐릭터 행 -> auction_listings 한 행(정산 틱은 listing 한 행만).
-- 보관 중인 아이템은 character_items가 아니라 mails / auction_listings 행이 들고 있다(원장 location mail / auction).
-- =====================================================================================

-- ---------- 6단계: 소지품 재고 키에 귀속 추가 ----------

DROP INDEX character_items_stack_uq;
CREATE UNIQUE INDEX character_items_stack_uq ON character_items (character_id, location, item_key, bind)
  WHERE location IN ('bag', 'storage');
COMMENT ON TABLE character_items IS '캐릭터 소지품. 변경은 item_ledger와 같은 트랜잭션에서만. bag·storage는 (키, 귀속)별 한 행(수량 합), worn은 슬롯별 한 행(count=1). 수량이 0이 되면 행을 지운다. 우편·경매 보관분은 이 표가 아니라 mails / auction_listings가 들고 있다';
COMMENT ON COLUMN character_items.bind IS 'none 거래 가능 / account 계정 귀속 / character 캐릭터 귀속. 획득 경로가 정한다(경매 구매 장비는 account). 강화·장착·창고 이동은 귀속을 그대로 가져간다';

-- ---------- 6단계: 원장 reason 확장 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim'));
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. 경매: auction_deposit 등록 보증금(-) / auction_bid 입찰 예치(-) / auction_buyout 즉시 구매 대금(-) / mail_claim 우편 수령(+). 경매 판매 대금·수수료는 이 원장에 없다(대금은 우편 gold로 보관되다가 수령할 때 mail_claim)';
COMMENT ON COLUMN gold_ledger.ref IS 'drop_claim: 드롭 uuid / quest_reward: 퀘스트 id / shop_*: 아이템 id / enhance_cost: enhance_log uuid / auction_deposit·auction_buyout: listing uuid / auction_bid: bid uuid / mail_claim: mail uuid';

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire'));
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). 경매: auction_list 등록(가방 -n, auction +n) / auction_return 취소·만료(auction -n, 판매자 mail +n) / auction_sold 판매 체결(판매자 auction -n) / auction_buy 구매(구매자 mail +n) / mail_claim 우편 수령(mail -n, bag +n) / mail_expire 30일 경과 폐기(mail -n)';
COMMENT ON COLUMN item_ledger.ref IS 'drop_claim: 드롭 uuid / gather: 노드 id / chest: 상자 id / quest_*: 퀘스트 id / delivery: 납품처 id / shop_*: 아이템 id / enhance_*: enhance_log uuid / use_item: 아이템 id / auction_*: listing uuid / mail_claim·mail_expire: mail uuid';
COMMENT ON COLUMN item_ledger.balance_after IS '변동 직후 그 위치의 수량(0 가능). location mail / auction은 그 보관 행(우편 한 통, 등록 한 건) 하나의 수량. starter 행만 NULL 허용';

CREATE UNIQUE INDEX gold_ledger_auction_uq ON gold_ledger (reason, ref)
  WHERE reason IN ('auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim');
CREATE UNIQUE INDEX item_ledger_auction_uq ON item_ledger (reason, ref, location)
  WHERE reason IN ('auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire');

-- ---------- 6단계: 경매 등록 ----------

CREATE TABLE auction_listings (
  id                          BIGSERIAL PRIMARY KEY,
  uuid                        UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  seller_character_id         BIGINT NOT NULL REFERENCES characters(id),
  seller_account_id           BIGINT NOT NULL REFERENCES accounts(id),
  item_key                    TEXT NOT NULL CHECK (item_key ~ '^[a-z][a-z0-9_]{0,39}(\+[1-9][0-9]?)?$' AND item_key <> 'gold'),
  item_base                   TEXT NOT NULL,
  enhance                     SMALLINT NOT NULL DEFAULT 0 CHECK (enhance >= 0),
  count                       INT NOT NULL CHECK (count BETWEEN 1 AND 9999),
  category                    TEXT NOT NULL CHECK (category IN ('weapon', 'armor', 'accessory', 'material', 'consumable')),
  rarity                      SMALLINT CHECK (rarity BETWEEN 0 AND 5),
  class_only                  TEXT CHECK (class_only IN ('warrior', 'mage')),
  buyout_price                BIGINT NOT NULL CHECK (buyout_price > 0),
  start_bid                   BIGINT CHECK (start_bid > 0),
  current_bid                 BIGINT CHECK (current_bid > 0),
  current_bidder_character_id BIGINT REFERENCES characters(id),
  current_bidder_account_id   BIGINT REFERENCES accounts(id),
  bid_count                   INT NOT NULL DEFAULT 0 CHECK (bid_count >= 0),
  deposit                     BIGINT NOT NULL CHECK (deposit >= 0),
  fee_pct                     SMALLINT NOT NULL CHECK (fee_pct BETWEEN 0 AND 50),
  duration_hours              SMALLINT NOT NULL CHECK (duration_hours > 0),
  status                      TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'sold', 'expired', 'cancelled')),
  sold_kind                   TEXT CHECK (sold_kind IN ('buyout', 'bid')),
  created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
  ends_at                     TIMESTAMPTZ NOT NULL,
  extend_count                SMALLINT NOT NULL DEFAULT 0 CHECK (extend_count >= 0),
  closed_at                   TIMESTAMPTZ,
  version                     INT NOT NULL DEFAULT 0,
  CONSTRAINT auction_listings_bid_pair   CHECK ((current_bid IS NULL) = (current_bidder_character_id IS NULL)
                                                AND (current_bid IS NULL) = (current_bidder_account_id IS NULL)),
  CONSTRAINT auction_listings_bid_range  CHECK (start_bid IS NULL OR start_bid < buyout_price),
  CONSTRAINT auction_listings_bid_valid  CHECK (current_bid IS NULL OR (start_bid IS NOT NULL AND current_bid >= start_bid AND current_bid < buyout_price)),
  CONSTRAINT auction_listings_not_self   CHECK (current_bidder_account_id IS NULL OR current_bidder_account_id <> seller_account_id),
  CONSTRAINT auction_listings_closed_chk CHECK ((status = 'active') = (closed_at IS NULL)),
  CONSTRAINT auction_listings_sold_chk   CHECK ((status = 'sold') = (sold_kind IS NOT NULL)),
  CONSTRAINT auction_listings_time_chk   CHECK (ends_at > created_at)
);
COMMENT ON TABLE  auction_listings IS '경매 등록 한 건. 등록한 아이템은 이 행이 직접 들고 있다(character_items에 없다). status가 active인 동안 count개가 경매 보관(원장 location auction)이다';
COMMENT ON COLUMN auction_listings.uuid IS '외부 노출 id(응답의 listing id). 원장 ref';
COMMENT ON COLUMN auction_listings.seller_account_id IS '판매자 계정. 같은 계정의 다른 캐릭터가 사거나 입찰하는 것을 막는 기준(캐릭터 삭제·이전과 무관하게 등록 때 고정)';
COMMENT ON COLUMN auction_listings.item_key IS '"기본id+강화" 키. 거래 가능(bind none) 재고에서만 등록된다';
COMMENT ON COLUMN auction_listings.item_base IS '기본 id(강화 제외). 이름 검색이 이름 -> item_base 목록으로 바뀐 뒤 이 열로 거른다';
COMMENT ON COLUMN auction_listings.enhance IS 'item_key의 강화 단계(검색 필터용 복사). 비장비는 0';
COMMENT ON COLUMN auction_listings.category IS 'weapon / armor / accessory / material / consumable. items.json·shop.json에서 서버가 정한다(요청으로 받지 않는다)';
COMMENT ON COLUMN auction_listings.rarity IS '장비 등급 0..5(shop.json equipment rarity 순서: Common, Uncommon, Rare, Epic, Unique, Legendary). 비장비는 NULL';
COMMENT ON COLUMN auction_listings.class_only IS '착용 직업 제한(shop.json equipment classOnly). 제한 없음·비장비는 NULL';
COMMENT ON COLUMN auction_listings.buyout_price IS '즉시 구매가(묶음 전체 금액). 등록 후 바꾸지 않는다(수정 API 없음)';
COMMENT ON COLUMN auction_listings.start_bid IS '입찰 시작가. NULL이면 즉시 구매만 가능';
COMMENT ON COLUMN auction_listings.current_bid IS '현재 최고 입찰가(예치된 금액). 즉시 구매로 팔리면 NULL로 비운다(직전 입찰은 auction_bids에 남는다)';
COMMENT ON COLUMN auction_listings.bid_count IS '이 등록에 들어온 입찰 수(누적)';
COMMENT ON COLUMN auction_listings.deposit IS '등록 때 낸 보증금(즉시 구매가 기준 서버 계산). 팔리면 판매자 우편에 돌아가고 만료·취소면 소각';
COMMENT ON COLUMN auction_listings.fee_pct IS '등록 당시 수수료율 스냅샷(장비/재료·소모품, auction.json). 체결 때 이 값으로 계산한다';
COMMENT ON COLUMN auction_listings.status IS 'active 진행 / sold 팔림 / expired 입찰 없이 마감 / cancelled 판매자 취소';
COMMENT ON COLUMN auction_listings.sold_kind IS 'buyout 즉시 구매 / bid 낙찰. sold일 때만';
COMMENT ON COLUMN auction_listings.ends_at IS '마감 시각(서버 시계). 저격 방지 연장으로 늘 수 있다. 검색 결과에는 구간으로만 보인다';
COMMENT ON COLUMN auction_listings.extend_count IS '마감 연장 횟수(서버 데이터 상한까지)';
COMMENT ON COLUMN auction_listings.version IS '변경마다 +1(진단용). 동시성은 행 잠금이 맡는다';
CREATE INDEX auction_listings_due ON auction_listings (ends_at) WHERE status = 'active';
CREATE INDEX auction_listings_browse ON auction_listings (category, buyout_price, id) WHERE status = 'active';
CREATE INDEX auction_listings_item ON auction_listings (item_base, enhance, buyout_price) WHERE status = 'active';
CREATE INDEX auction_listings_seller ON auction_listings (seller_character_id) WHERE status = 'active';
CREATE INDEX auction_listings_bidder ON auction_listings (current_bidder_character_id) WHERE status = 'active' AND current_bidder_character_id IS NOT NULL;

-- ---------- 6단계: 입찰 ----------

CREATE TABLE auction_bids (
  id                   BIGSERIAL PRIMARY KEY,
  uuid                 UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  listing_id           BIGINT NOT NULL REFERENCES auction_listings(id),
  bidder_character_id  BIGINT NOT NULL REFERENCES characters(id),
  bidder_account_id    BIGINT NOT NULL REFERENCES accounts(id),
  amount               BIGINT NOT NULL CHECK (amount > 0),
  request_id           UUID NOT NULL,
  state                TEXT NOT NULL DEFAULT 'top' CHECK (state IN ('top', 'outbid', 'won', 'lost_to_buyout')),
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  closed_at            TIMESTAMPTZ,
  UNIQUE (listing_id, amount),
  CONSTRAINT auction_bids_closed_chk CHECK ((state = 'top') = (closed_at IS NULL))
);
COMMENT ON TABLE  auction_bids IS '입찰 기록. 입찰한 금액은 입찰 때 캐릭터 골드에서 빠져 예치되고(gold_ledger auction_bid) 이 행과 auction_listings.current_bid가 들고 있다. 상태만 바뀐다';
COMMENT ON COLUMN auction_bids.amount IS '예치 금액. 같은 등록에서 같은 금액의 입찰은 두 번 있을 수 없다(최소 입찰가가 매번 오른다. 동시성 버그의 마지막 안전장치)';
COMMENT ON COLUMN auction_bids.state IS 'top 현재 최고 / outbid 더 높은 입찰에 밀림(예치금은 우편으로 반환) / won 낙찰 / lost_to_buyout 즉시 구매에 밀림(예치금은 우편으로 반환)';
COMMENT ON COLUMN auction_bids.request_id IS '입찰 요청의 request_id(원장 연결)';
CREATE UNIQUE INDEX auction_bids_one_top ON auction_bids (listing_id) WHERE state = 'top';
CREATE INDEX auction_bids_listing ON auction_bids (listing_id, id);
CREATE INDEX auction_bids_bidder ON auction_bids (bidder_character_id, id);

-- ---------- 6단계: 우편 ----------

CREATE TABLE mails (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  kind          TEXT NOT NULL CHECK (kind IN ('sold', 'expired', 'outbid', 'bought', 'cancelled', 'system')),
  listing_id    BIGINT REFERENCES auction_listings(id),
  bid_id        BIGINT REFERENCES auction_bids(id),
  ref_item_key  TEXT,
  ref_count     INT CHECK (ref_count > 0),
  item_key      TEXT CHECK (item_key <> 'gold'),
  count         INT CHECK (count > 0),
  bind          TEXT CHECK (bind IN ('none', 'account', 'character')),
  gold          BIGINT NOT NULL DEFAULT 0 CHECK (gold >= 0),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at    TIMESTAMPTZ NOT NULL,
  claimed_at    TIMESTAMPTZ,
  expired_at    TIMESTAMPTZ,
  CONSTRAINT mails_attach_chk  CHECK ((item_key IS NULL) = (count IS NULL) AND (item_key IS NULL) = (bind IS NULL)),
  CONSTRAINT mails_content_chk CHECK (item_key IS NOT NULL OR gold > 0),
  CONSTRAINT mails_state_chk   CHECK (claimed_at IS NULL OR expired_at IS NULL),
  CONSTRAINT mails_time_chk    CHECK (expires_at > created_at)
);
COMMENT ON TABLE  mails IS '우편 한 통. 서버만 만든다(플레이어 간 우편 없음). 첨부 아이템과 골드를 이 행이 직접 들고 있다(수령 전까지 원장 location mail). 30일 안에 받지 않으면 폐기';
COMMENT ON COLUMN mails.character_id IS '받는 캐릭터. 이 캐릭터만 수령할 수 있다';
COMMENT ON COLUMN mails.kind IS 'sold 판매 대금(골드) / expired 만료 반환(아이템) / outbid 입찰 반환(골드) / bought 구매 아이템 / cancelled 등록 취소 반환(아이템) / system 운영 지급(7단계 도구)';
COMMENT ON COLUMN mails.bid_id IS 'outbid 우편이 돌려주는 입찰';
COMMENT ON COLUMN mails.ref_item_key IS '우편이 설명하는 경매 아이템(목록 문구용). 첨부와 같을 수도, 판매 대금처럼 첨부가 없을 수도 있다';
COMMENT ON COLUMN mails.item_key IS '첨부 아이템 키. 없으면 NULL';
COMMENT ON COLUMN mails.bind IS '수령해서 가방에 들어갈 때의 귀속. 장비 구매는 account, 그 밖은 등록 때의 귀속(none)';
COMMENT ON COLUMN mails.gold IS '첨부 골드. 판매 대금(대금 - 수수료 + 보증금) 또는 입찰 반환';
COMMENT ON COLUMN mails.expires_at IS '보관 기한(생성 + 서버 데이터 mailDays)';
COMMENT ON COLUMN mails.claimed_at IS '수령 시각. NULL이고 expired_at도 NULL이면 받을 수 있다. UPDATE ... WHERE claimed_at IS NULL AND expired_at IS NULL로 한 번만';
COMMENT ON COLUMN mails.expired_at IS '기한이 지나 폐기된 시각(첨부 아이템은 mail_expire 원장, 골드는 auction_sinks mail_expire)';
CREATE INDEX mails_open ON mails (character_id, created_at DESC) WHERE claimed_at IS NULL AND expired_at IS NULL;
CREATE INDEX mails_expiring ON mails (expires_at) WHERE claimed_at IS NULL AND expired_at IS NULL;
CREATE UNIQUE INDEX mails_once_per_event ON mails (listing_id, character_id, kind)
  WHERE listing_id IS NOT NULL AND kind <> 'outbid';
CREATE UNIQUE INDEX mails_outbid_per_bid ON mails (bid_id) WHERE kind = 'outbid';

-- ---------- 6단계: 체결 기록, 일별 시세 ----------

CREATE TABLE auction_trades (
  id                  BIGSERIAL PRIMARY KEY,
  uuid                UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  listing_id          BIGINT NOT NULL UNIQUE REFERENCES auction_listings(id),
  item_key            TEXT NOT NULL,
  item_base           TEXT NOT NULL,
  count               INT NOT NULL CHECK (count > 0),
  price               BIGINT NOT NULL CHECK (price > 0),
  fee_pct             SMALLINT NOT NULL CHECK (fee_pct BETWEEN 0 AND 50),
  fee                 BIGINT NOT NULL CHECK (fee >= 0),
  deposit_returned    BIGINT NOT NULL CHECK (deposit_returned >= 0),
  seller_payout       BIGINT NOT NULL CHECK (seller_payout >= 0),
  unit_price          NUMERIC(16, 4) GENERATED ALWAYS AS (price::numeric / count) STORED,
  kind                TEXT NOT NULL CHECK (kind IN ('buyout', 'bid')),
  buyer_character_id  BIGINT NOT NULL REFERENCES characters(id),
  buyer_account_id    BIGINT NOT NULL REFERENCES accounts(id),
  seller_character_id BIGINT NOT NULL REFERENCES characters(id),
  seller_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  traded_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT auction_trades_fee_chk     CHECK (fee = (price * fee_pct + 99) / 100),
  CONSTRAINT auction_trades_payout_chk  CHECK (seller_payout = price - fee + deposit_returned),
  CONSTRAINT auction_trades_not_self    CHECK (buyer_account_id <> seller_account_id)
);
COMMENT ON TABLE  auction_trades IS '체결 기록(추가만, 트리거가 UPDATE/DELETE 차단). 시세 중앙값, 쌍 한도, 사후 추적(CS·제재)의 근거. 등록 한 건에 한 행';
COMMENT ON COLUMN auction_trades.price IS '구매자가 낸 금액(즉시 구매가 또는 낙찰가). 묶음 전체';
COMMENT ON COLUMN auction_trades.fee IS '소각된 수수료 = ceil(price x fee_pct / 100). CHECK가 정수 계산을 강제한다';
COMMENT ON COLUMN auction_trades.deposit_returned IS '판매자 우편에 돌아간 보증금(= 등록의 deposit)';
COMMENT ON COLUMN auction_trades.seller_payout IS '판매자 우편의 골드 = price - fee + deposit_returned';
COMMENT ON COLUMN auction_trades.unit_price IS '개당 가격(시세 비교용, 묶음 크기와 무관). 생성 열';
COMMENT ON COLUMN auction_trades.buyer_account_id IS 'CHECK로 판매자 계정과 같을 수 없다(같은 계정 캐릭터 간 이전은 DB가 거절한다)';
CREATE TRIGGER auction_trades_append_only BEFORE UPDATE OR DELETE ON auction_trades
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER auction_trades_no_truncate BEFORE TRUNCATE ON auction_trades
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();
CREATE INDEX auction_trades_item_time ON auction_trades (item_key, traded_at DESC);
CREATE INDEX auction_trades_pair ON auction_trades (seller_account_id, buyer_account_id, traded_at);
CREATE INDEX auction_trades_time ON auction_trades (traded_at);

CREATE TABLE auction_price_daily (
  item_key       TEXT NOT NULL,
  day_start      TIMESTAMPTZ NOT NULL,
  trade_count    INT NOT NULL CHECK (trade_count > 0),
  volume         INT NOT NULL CHECK (volume > 0),
  sum_price      BIGINT NOT NULL CHECK (sum_price > 0),
  min_unit_price NUMERIC(16, 4) NOT NULL,
  max_unit_price NUMERIC(16, 4) NOT NULL,
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (item_key, day_start),
  CONSTRAINT auction_price_daily_minmax CHECK (min_unit_price <= max_unit_price)
);
COMMENT ON TABLE  auction_price_daily IS '아이템 키별 일별 시세(게임 일 06:00 KST 구간). 체결 트랜잭션이 같은 문장으로 누적(upsert)해서 오늘 값도 정확하다. 평균 = sum_price / volume';
COMMENT ON COLUMN auction_price_daily.day_start IS 'resetBoundaries(체결 시각).dailyStartAt (서버 KST 함수 하나)';
COMMENT ON COLUMN auction_price_daily.volume IS '거래된 개수 합(묶음은 count만큼)';
COMMENT ON COLUMN auction_price_daily.sum_price IS '체결 금액 합. 평균 단가 = sum_price / volume';

-- ---------- 6단계: 골드 소각 기록, 악용 기록 ----------

CREATE TABLE auction_sinks (
  id           BIGSERIAL PRIMARY KEY,
  kind         TEXT NOT NULL CHECK (kind IN ('fee', 'deposit_forfeit', 'mail_expire')),
  amount       BIGINT NOT NULL CHECK (amount > 0),
  listing_id   BIGINT REFERENCES auction_listings(id),
  mail_id      BIGINT REFERENCES mails(id),
  character_id BIGINT REFERENCES characters(id),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  auction_sinks IS '경매가 소각한 골드의 기록(추가만). 어느 캐릭터 잔액에도 없고 어느 우편에도 없는 골드. 일일 소각량 집계와 골드 보존식의 우변';
COMMENT ON COLUMN auction_sinks.kind IS 'fee 체결 수수료 / deposit_forfeit 만료·취소로 돌려주지 않은 보증금 / mail_expire 기한이 지나 폐기된 우편의 골드';
COMMENT ON COLUMN auction_sinks.character_id IS '골드를 잃은 캐릭터(fee: 판매자, deposit_forfeit: 판매자, mail_expire: 받을 사람)';
CREATE TRIGGER auction_sinks_append_only BEFORE UPDATE OR DELETE ON auction_sinks
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER auction_sinks_no_truncate BEFORE TRUNCATE ON auction_sinks
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();
CREATE INDEX auction_sinks_time ON auction_sinks (created_at, kind);

CREATE TABLE auction_flags (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT REFERENCES characters(id),
  kind         TEXT NOT NULL CHECK (kind IN ('self_account', 'price_band', 'pair_limit', 'foreign_id', 'gate')),
  severity     SMALLINT NOT NULL DEFAULT 1 CHECK (severity BETWEEN 1 AND 3),
  detail       JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  auction_flags IS '경매 악용 의심 기록(anomaly_log와 같은 역할, 경매 전용). 요청이 거절돼 롤백돼도 남도록 별도 트랜잭션으로 쓴다. 제재는 7단계 관리자 도구가 이 표를 본다';
COMMENT ON COLUMN auction_flags.kind IS 'self_account 같은 계정 매물 구매·입찰 시도 / price_band 가격 한도 밖 등록 시도 / pair_limit 같은 계정 쌍의 하루 체결 한도 초과 / foreign_id 남의 우편·등록 id / gate 신규 계정 등록 시도';
COMMENT ON COLUMN auction_flags.severity IS '1 참고(실수 가능) / 2 의심 / 3 불가능한 값. self_account는 UI가 막아 주므로 정직한 클라이언트는 만들지 못한다(2)';
COMMENT ON COLUMN auction_flags.detail IS '판정 근거(listing uuid, 가격, 한도, 쌍 합계 등). 요청 본문 전체는 넣지 않는다';
CREATE INDEX auction_flags_account_time ON auction_flags (account_id, created_at);
CREATE INDEX auction_flags_time ON auction_flags (created_at);
CREATE TRIGGER auction_flags_append_only BEFORE UPDATE OR DELETE ON auction_flags
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER auction_flags_no_truncate BEFORE TRUNCATE ON auction_flags
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- =====================================================================================
-- 0006_chat_social (5단계 채팅·친구). 아래는 migrations/0006_chat_social.sql 의 UP 본문이다. 설계: Docs/server/phase5_api.md
-- 재화가 움직이지 않는 단계라 원장·잔액 CHECK는 없다. 멱등성: REST는 request_log, 채팅 전송은 chat_messages UNIQUE(sender_account_id, client_msg_id).
-- =====================================================================================

ALTER TABLE accounts ADD COLUMN last_character_id BIGINT REFERENCES characters(id);
COMMENT ON COLUMN accounts.last_character_id IS '마지막으로 WebSocket에 접속한 캐릭터. 친구 목록의 이름·직업·레벨 표시용(친구는 계정 단위라 접속 중이 아니면 이 캐릭터를 보여 준다). 삭제된 캐릭터면 서버가 가장 오래된 살아 있는 캐릭터로 대신한다';

CREATE TABLE chat_messages (
  id                     BIGSERIAL PRIMARY KEY,
  channel                TEXT NOT NULL CHECK (channel IN ('general', 'party', 'whisper')),
  shard                  INT CHECK (shard >= 1),
  party_id               BIGINT REFERENCES parties(id),
  sender_account_id      BIGINT NOT NULL REFERENCES accounts(id),
  sender_character_id    BIGINT NOT NULL REFERENCES characters(id),
  sender_name            TEXT NOT NULL,
  recipient_account_id   BIGINT REFERENCES accounts(id),
  recipient_character_id BIGINT REFERENCES characters(id),
  recipient_name         TEXT,
  text                   TEXT NOT NULL CHECK (char_length(text) BETWEEN 1 AND 200),
  filtered               BOOLEAN NOT NULL DEFAULT false,
  client_msg_id          UUID NOT NULL,
  created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT chat_messages_shape_chk CHECK (
    (channel = 'general' AND shard IS NOT NULL AND party_id IS NULL
       AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
    OR (channel = 'party' AND party_id IS NOT NULL AND shard IS NULL
       AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
    OR (channel = 'whisper' AND shard IS NULL AND party_id IS NULL
       AND recipient_account_id IS NOT NULL AND recipient_character_id IS NOT NULL AND recipient_name IS NOT NULL)
  ),
  CONSTRAINT chat_messages_client_uq UNIQUE (sender_account_id, client_msg_id)
);
COMMENT ON TABLE  chat_messages IS '채팅 기록. 서버가 저장한 뒤에 전달한다(저장 성공 = 전달 대상). id 순서가 곧 전달 순서이고, 프로세스 안 단일 쓰기 큐가 id 순서와 커밋 순서를 같게 만든다. 시스템 채널은 저장하지 않는다. 차단당한 귓속말은 저장하지 않는다';
COMMENT ON COLUMN chat_messages.id IS '순번(seq). 클라이언트에는 커서로만 나가고, 이 값으로 메시지를 조회하는 API는 없다(IDOR 대상 아님). 프로젝트 규칙 "내부 id 비노출"의 의도된 예외이며 uuid 열은 두지 않는다';
COMMENT ON COLUMN chat_messages.shard IS '일반 채널 방 번호(1부터). 시작은 1개뿐이고 정원(CHAT_SHARD_SIZE)을 넘으면 늘어난다. 파티·귓속말은 NULL';
COMMENT ON COLUMN chat_messages.party_id IS '파티 채널: 보낼 때의 파티. 놓친 메시지 따라잡기는 "내 현재 파티 + 내 가입 시각 이후"만 준다';
COMMENT ON COLUMN chat_messages.sender_name IS '보낸 캐릭터 이름 스냅샷(캐릭터가 지워져도 기록·신고에서 읽힌다)';
COMMENT ON COLUMN chat_messages.text IS '금칙어를 가린 뒤의 최종 문장(전달된 그대로). 원문은 저장하지 않는다. 실제 길이 한도는 server/data/chat.json의 maxLength(C# ChatRules.MaxLength)이고 DB는 이상값만 막는다';
COMMENT ON COLUMN chat_messages.filtered IS '금칙어를 가린 적이 있는가(자동 제재의 반복 횟수 근거)';
COMMENT ON COLUMN chat_messages.client_msg_id IS '클라이언트가 보낸 메시지 id(uuid). 같은 id 재전송은 새 행을 만들지 않고 처음 결과(ack)를 돌려준다. 채팅의 request_id';
-- 일반 채널 따라잡기: WHERE channel='general' AND shard=$1 AND id > $since ORDER BY id (최근 N건만)
CREATE INDEX chat_messages_general ON chat_messages (shard, id) WHERE channel = 'general';
-- 파티 채널 따라잡기: WHERE channel='party' AND party_id=$1 AND id > $since AND created_at >= 내 가입 시각
CREATE INDEX chat_messages_party ON chat_messages (party_id, id) WHERE channel = 'party';
-- 귓속말 따라잡기와 신고 증거(두 사람 사이의 양방향은 받는 사람 기준 조회 2번으로 모은다)
CREATE INDEX chat_messages_whisper ON chat_messages (recipient_account_id, id) WHERE channel = 'whisper';
-- 신고 증거: "신고 대상이 최근에 한 말" 조회
CREATE INDEX chat_messages_sender ON chat_messages (sender_account_id, id DESC);
-- 보관 기간 정리(created_at < 기준)
CREATE INDEX chat_messages_created ON chat_messages (created_at);

CREATE TABLE friendships (
  id                     BIGSERIAL PRIMARY KEY,
  uuid                   UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  requester_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  target_account_id      BIGINT NOT NULL REFERENCES accounts(id),
  requester_character_id BIGINT NOT NULL REFERENCES characters(id),
  target_character_id    BIGINT NOT NULL REFERENCES characters(id),
  state                  TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'accepted', 'declined', 'cancelled', 'removed')),
  created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
  responded_at           TIMESTAMPTZ,
  ended_at               TIMESTAMPTZ,
  ended_by_account_id    BIGINT REFERENCES accounts(id),
  silent                 BOOLEAN NOT NULL DEFAULT false,
  CONSTRAINT friendships_self_chk  CHECK (requester_account_id <> target_account_id),
  CONSTRAINT friendships_ended_chk CHECK ((state IN ('pending', 'accepted')) = (ended_at IS NULL)),
  CONSTRAINT friendships_responded_chk CHECK (state = 'pending' OR state = 'cancelled' OR responded_at IS NOT NULL)
);
COMMENT ON TABLE  friendships IS '친구 요청과 관계(계정 단위, 상호 수락). 한 쌍(A,B)은 방향과 무관하게 pending 또는 accepted 행이 최대 하나. 끝난 행(declined, cancelled, removed)은 남겨 재요청 쿨다운·괴롭힘 확인에 쓴다';
COMMENT ON COLUMN friendships.requester_character_id IS '요청을 보낸 캐릭터(받는 사람 화면의 "누가 보냈나" 이름)';
COMMENT ON COLUMN friendships.target_character_id IS '요청자가 지목한 캐릭터. 친구 관계 자체는 계정 단위이고, 목록에는 accounts.last_character_id가 나온다';
COMMENT ON COLUMN friendships.state IS 'pending 요청 중 / accepted 친구 / declined 받은 쪽이 거절 / cancelled 보낸 쪽이 취소 / removed 친구였다가 삭제(차단 포함)';
COMMENT ON COLUMN friendships.ended_by_account_id IS '끝낸 쪽(삭제·거절·취소·차단). 차단으로 끝난 행은 ended_by = 차단한 계정';
-- 같은 두 계정 사이의 살아 있는 행은 하나(A->B 요청과 B->A 요청이 동시에 와도 하나만 남는다)
CREATE UNIQUE INDEX friendships_pair_live ON friendships
  (LEAST(requester_account_id, target_account_id), GREATEST(requester_account_id, target_account_id))
  WHERE state IN ('pending', 'accepted');
-- 내 친구·보낸 요청(요청자 쪽)과 받은 요청·친구(대상 쪽): 두 인덱스를 UNION으로 읽는다
CREATE INDEX friendships_requester ON friendships (requester_account_id) WHERE state IN ('pending', 'accepted');
CREATE INDEX friendships_target    ON friendships (target_account_id)    WHERE state IN ('pending', 'accepted');
-- 거절·취소 뒤 재요청 쿨다운(FRIEND_REREQUEST_HOURS)
CREATE INDEX friendships_pair_hist ON friendships (requester_account_id, target_account_id, created_at DESC);
-- 보관 기간 정리
CREATE INDEX friendships_ended ON friendships (ended_at) WHERE ended_at IS NOT NULL;

CREATE TABLE blocks (
  id                   BIGSERIAL PRIMARY KEY,
  uuid                 UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  blocker_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  blocked_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  blocked_character_id BIGINT NOT NULL REFERENCES characters(id),
  blocked_name         TEXT NOT NULL,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  deleted_at           TIMESTAMPTZ,
  CONSTRAINT blocks_self_chk CHECK (blocker_account_id <> blocked_account_id)
);
COMMENT ON TABLE  blocks IS '차단(계정 단위: 상대가 다른 캐릭터로 접속해도 막힌다). 차단하면 상대의 일반·파티·귓속말·친구 요청·파티 초대가 나에게 오지 않는다. 해제는 deleted_at(소프트 삭제)';
COMMENT ON COLUMN blocks.blocked_character_id IS '차단 당시 지목한 캐릭터';
COMMENT ON COLUMN blocks.blocked_name IS '차단 목록에 보일 이름 스냅샷(캐릭터가 지워져도 목록이 읽힌다)';
-- 내 차단 목록 로드(접속 때 세션 메모리로), 차단 여부 확인(친구 요청·초대·귓속말), 중복 차단 방지
CREATE UNIQUE INDEX blocks_pair_live ON blocks (blocker_account_id, blocked_account_id) WHERE deleted_at IS NULL;

CREATE TABLE reports (
  id                    BIGSERIAL PRIMARY KEY,
  uuid                  UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  reporter_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  reporter_character_id BIGINT NOT NULL REFERENCES characters(id),
  target_account_id     BIGINT NOT NULL REFERENCES accounts(id),
  target_character_id   BIGINT NOT NULL REFERENCES characters(id),
  target_name           TEXT NOT NULL,
  reason                TEXT NOT NULL CHECK (reason IN ('abuse', 'spam', 'scam_ad', 'cheat', 'other')),
  state                 TEXT NOT NULL DEFAULT 'open' CHECK (state IN ('open', 'reviewing', 'actioned', 'dismissed')),
  line_count            SMALLINT NOT NULL DEFAULT 0 CHECK (line_count >= 0),
  created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
  handled_at            TIMESTAMPTZ,
  handled_by            TEXT,
  note                  TEXT CHECK (char_length(note) <= 500),
  CONSTRAINT reports_self_chk CHECK (reporter_account_id <> target_account_id),
  CONSTRAINT reports_handled_chk CHECK ((state IN ('actioned', 'dismissed')) = (handled_at IS NOT NULL))
);
COMMENT ON TABLE  reports IS '신고 접수. 증거는 클라이언트가 보낸 값이 아니라 서버가 접수 순간 chat_messages에서 찍어 report_lines에 복사한다';
COMMENT ON COLUMN reports.reason IS 'abuse 욕설·비하 / spam 도배 / scam_ad 광고·사기 / cheat 불법 프로그램 의심 / other 기타 (C# ChatRules.ReportReasons 5개와 1:1)';
COMMENT ON COLUMN reports.state IS 'open 접수 / reviewing 운영자 검토 중 / actioned 제재함(account_sanctions.report_id로 연결) / dismissed 위반 없음·중복';
COMMENT ON COLUMN reports.line_count IS '붙은 증거 줄 수(report_lines 행 수)';
COMMENT ON COLUMN reports.handled_by IS '처리한 운영자 표시(7단계 관리자 도구 전에는 SQL을 실행한 사람 이름)';
-- 같은 사람이 같은 대상을 같은 사유로 처리 전에 다시 신고하는 것을 막는다(두 번째는 기존 신고를 돌려준다)
CREATE UNIQUE INDEX reports_open_uq ON reports (reporter_account_id, target_account_id, reason) WHERE state IN ('open', 'reviewing');
-- 운영자 대기열(오래된 것부터)
CREATE INDEX reports_queue ON reports (created_at) WHERE state IN ('open', 'reviewing');
-- 한 사람에 대한 신고 모음(서로 다른 신고자 수 세기, 운영자 조회)
CREATE INDEX reports_target ON reports (target_account_id, created_at DESC);
-- 신고자별 시간당·일일 한도 검사
CREATE INDEX reports_reporter ON reports (reporter_account_id, created_at DESC);

CREATE TABLE report_lines (
  id                  BIGSERIAL PRIMARY KEY,
  report_id           BIGINT NOT NULL REFERENCES reports(id),
  seq                 BIGINT NOT NULL,
  channel             TEXT NOT NULL CHECK (channel IN ('general', 'party', 'whisper')),
  sender_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  sender_character_id BIGINT NOT NULL REFERENCES characters(id),
  sender_name         TEXT NOT NULL,
  recipient_name      TEXT,
  text                TEXT NOT NULL,
  sent_at             TIMESTAMPTZ NOT NULL,
  is_target           BOOLEAN NOT NULL,
  UNIQUE (report_id, seq)
);
COMMENT ON TABLE  report_lines IS '신고 증거 스냅샷(접수 때 chat_messages에서 복사). 원본이 보관 기간으로 지워져도 남는다. 수정 불가(UPDATE 트리거). 정리는 신고가 닫힌 뒤 REPORT_RETENTION_DAYS';
COMMENT ON COLUMN report_lines.seq IS '원본 chat_messages.id(원본은 지워졌을 수 있어 FK를 두지 않는다). UNIQUE(report_id, seq)가 증거 줄의 순서와 중복 방지를 맡는다';
COMMENT ON COLUMN report_lines.is_target IS '신고 대상이 한 말인가(운영자 화면 강조용)';
CREATE TRIGGER report_lines_no_update BEFORE UPDATE ON report_lines
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

CREATE TABLE account_sanctions (
  id          BIGSERIAL PRIMARY KEY,
  uuid        UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  kind        TEXT NOT NULL CHECK (kind IN ('warning', 'chat_mute', 'ban')),
  source      TEXT NOT NULL CHECK (source IN ('auto_filter', 'auto_spam', 'auto_report', 'admin')),
  reason_code TEXT NOT NULL CHECK (reason_code IN ('abuse', 'spam', 'scam_ad', 'cheat', 'other', 'filter_strikes', 'repeat_spam')),
  report_id   BIGINT REFERENCES reports(id),
  starts_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  ends_at     TIMESTAMPTZ,
  created_by  TEXT NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  notified_at TIMESTAMPTZ,
  revoked_at  TIMESTAMPTZ,
  revoked_by  TEXT,
  note        TEXT CHECK (char_length(note) <= 500),
  CONSTRAINT account_sanctions_mute_chk   CHECK (kind <> 'chat_mute' OR ends_at IS NOT NULL),
  CONSTRAINT account_sanctions_warn_chk   CHECK (kind <> 'warning' OR ends_at IS NULL),
  CONSTRAINT account_sanctions_range_chk  CHECK (ends_at IS NULL OR ends_at > starts_at),
  CONSTRAINT account_sanctions_revoke_chk CHECK ((revoked_at IS NULL) = (revoked_by IS NULL))
);
COMMENT ON TABLE  account_sanctions IS '제재 기록. 자동(system)과 운영자가 같은 표를 쓴다. chat_mute는 이 표만 본다. ban은 같은 트랜잭션에서 accounts.banned_until도 갱신하고(정지 판정은 기존 인증 미들웨어가 한다), 풀 때도 같이 되돌린다. 기록은 지우지 않는다';
COMMENT ON COLUMN account_sanctions.kind IS 'warning 경고(접속 때 한 번 알림) / chat_mute 채팅 금지(ends_at까지) / ban 이용 정지(ends_at NULL = 무기한)';
COMMENT ON COLUMN account_sanctions.source IS 'auto_filter 금칙어 반복 / auto_spam 도배 반복 / auto_report 서로 다른 신고자 다수(기본 꺼짐) / admin 운영자';
COMMENT ON COLUMN account_sanctions.created_by IS '자동이면 system, 운영자면 표시 이름';
COMMENT ON COLUMN account_sanctions.notified_at IS '플레이어에게 알린 시각. NULL이면 다음 WebSocket 접속(또는 즉시 푸시)에서 알리고 채운다';
-- 지금 채팅 금지인가: WHERE account_id=$1 AND kind='chat_mute' AND revoked_at IS NULL AND ends_at > now()
CREATE INDEX account_sanctions_active ON account_sanctions (account_id, kind, ends_at) WHERE revoked_at IS NULL;
-- 접속 때 아직 알리지 않은 제재 찾기
CREATE INDEX account_sanctions_unnotified ON account_sanctions (account_id) WHERE notified_at IS NULL AND revoked_at IS NULL;
-- 자동 제재 단계 올리기(최근 24시간 자동 금지 횟수), 운영자 이력 조회
CREATE INDEX account_sanctions_history ON account_sanctions (account_id, created_at DESC);
-- 제재가 생기거나 바뀌면(자동이든 운영자 SQL이든) 서버 프로세스가 LISTEN으로 즉시 알아 접속 중인 소켓의 금지 상태를 갱신하고 ban이면 끊는다.
-- 7단계 관리자 도구 전에도 운영자가 SQL만 실행하면 반영된다. 서버가 여러 대가 되어도 같은 방식이 그대로 동작한다.
CREATE FUNCTION notify_account_sanction() RETURNS trigger AS $$
BEGIN
  PERFORM pg_notify('dotrpg_sanction', NEW.account_id::text);
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER account_sanctions_notify AFTER INSERT OR UPDATE ON account_sanctions
  FOR EACH ROW EXECUTE FUNCTION notify_account_sanction();

CREATE TABLE party_invites (
  id                   BIGSERIAL PRIMARY KEY,
  uuid                 UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id             BIGINT NOT NULL REFERENCES parties(id),
  inviter_character_id BIGINT NOT NULL REFERENCES characters(id),
  invitee_character_id BIGINT NOT NULL REFERENCES characters(id),
  invitee_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  state                TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'accepted', 'declined', 'expired', 'cancelled')),
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at           TIMESTAMPTZ NOT NULL,
  responded_at         TIMESTAMPTZ,
  silent               BOOLEAN NOT NULL DEFAULT false,
  CONSTRAINT party_invites_responded_chk CHECK ((state IN ('accepted', 'declined')) = (responded_at IS NOT NULL) OR state IN ('expired', 'cancelled'))
);
COMMENT ON TABLE  party_invites IS '파티 초대(만든 뒤 PARTY_INVITE_SECONDS 동안 유효, 지연 만료). 받는 사람은 WebSocket으로 즉시 받고, 접속 중이 아니면 못 보낸다. 수락은 4단계 신청 수락과 같은 검사(자격·전투력·정원)를 지난다';
COMMENT ON COLUMN party_invites.invitee_account_id IS '받는 사람의 계정(차단 확인, 푸시 대상)';
-- 같은 파티가 같은 사람에게 동시에 둘 이상 보내지 못한다
CREATE UNIQUE INDEX party_invites_pending_uq ON party_invites (party_id, invitee_character_id) WHERE state = 'pending';
-- 받는 사람이 접속할 때 대기 중 초대 조회, 수락·거절 경로
CREATE INDEX party_invites_invitee ON party_invites (invitee_character_id, created_at DESC) WHERE state = 'pending';
-- 파티가 가진 대기 초대 수 제한(정원 초과 방지)과 보관 기간 정리
CREATE INDEX party_invites_party ON party_invites (party_id) WHERE state = 'pending';
CREATE INDEX party_invites_created ON party_invites (created_at);

-- =====================================================================================
-- 0008_ops (7단계 운영). 아래는 migrations/0008_ops.sql 의 UP 본문이다(초안). 설계: Docs/server/phase7_ops.md
-- 관리자 계정은 게임 계정(accounts)과 분리. 운영 지급은 system 우편으로만(캐릭터 잔액을 직접 고치는 경로 없음).
-- =====================================================================================

-- ---------- 7단계: 관리자 계정·세션 ----------

CREATE TABLE admin_users (
  id                  BIGSERIAL PRIMARY KEY,
  uuid                UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  login_id            TEXT NOT NULL UNIQUE,
  display_name        TEXT NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 30),
  role                TEXT NOT NULL CHECK (role IN ('viewer', 'operator', 'owner')),
  password_hash       TEXT NOT NULL,
  password_changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  must_change_password BOOLEAN NOT NULL DEFAULT true,
  totp_secret_enc     BYTEA,
  totp_confirmed_at   TIMESTAMPTZ,
  totp_last_step      BIGINT,
  failed_count        SMALLINT NOT NULL DEFAULT 0 CHECK (failed_count >= 0),
  locked_until        TIMESTAMPTZ,
  last_login_at       TIMESTAMPTZ,
  created_by          BIGINT REFERENCES admin_users(id),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  disabled_at         TIMESTAMPTZ,
  CONSTRAINT admin_users_login_chk CHECK (login_id ~ '^[a-z0-9_.-]{3,32}$'),
  CONSTRAINT admin_users_totp_chk  CHECK (totp_confirmed_at IS NULL OR totp_secret_enc IS NOT NULL)
);
COMMENT ON TABLE  admin_users IS '관리자 계정. 게임 계정(accounts)과 분리되어 서로의 토큰·비밀번호가 통하지 않는다. 행은 지우지 않고 disabled_at으로 비활성화한다(감사 로그가 참조)';
COMMENT ON COLUMN admin_users.uuid IS '외부 노출 id(감사 로그 조회, 관리자 목록)';
COMMENT ON COLUMN admin_users.login_id IS '소문자 아이디(3~32자 영문 소문자·숫자·_.-)';
COMMENT ON COLUMN admin_users.role IS 'viewer 조회만 / operator 신고·보류·제재·점검 처리 / owner 관리자 관리·운영 지급·영구 정지·작업 수동 실행';
COMMENT ON COLUMN admin_users.password_hash IS 'argon2id 문자열(게임 dev 로그인과 같은 라이브러리)';
COMMENT ON COLUMN admin_users.must_change_password IS 'true면 첫 로그인(임시 비밀번호)이므로 비밀번호 변경 전에는 setup 범위 세션만 받는다';
COMMENT ON COLUMN admin_users.totp_secret_enc IS 'TOTP 비밀키. ADMIN_SECRET_KEY(환경변수, 32바이트)로 AES-256-GCM 암호화한 값(nonce || 암호문 || 태그). 원문은 저장하지 않는다';
COMMENT ON COLUMN admin_users.totp_confirmed_at IS '첫 코드 확인으로 등록이 끝난 시각. NULL이면 2FA 미등록이라 setup 범위 세션만 받는다';
COMMENT ON COLUMN admin_users.totp_last_step IS '마지막으로 받아들인 TOTP 30초 구간 번호. 같은 코드 재사용(재전송)을 막는다';
COMMENT ON COLUMN admin_users.failed_count IS '연속 로그인 실패 횟수. 성공하면 0';
COMMENT ON COLUMN admin_users.locked_until IS '연속 실패로 잠긴 시각까지. 지나면 다시 시도할 수 있다';

CREATE TABLE admin_sessions (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  admin_id     BIGINT NOT NULL REFERENCES admin_users(id),
  token_hash   TEXT NOT NULL UNIQUE,
  scope        TEXT NOT NULL DEFAULT 'full' CHECK (scope IN ('full', 'setup')),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at   TIMESTAMPTZ NOT NULL,
  revoked_at   TIMESTAMPTZ,
  client_note  TEXT CHECK (char_length(client_note) <= 100),
  CONSTRAINT admin_sessions_range_chk CHECK (expires_at > created_at)
);
COMMENT ON TABLE  admin_sessions IS '관리자 세션. 불투명 토큰(32바이트 난수)의 SHA-256만 저장한다. 유휴 ADMIN_SESSION_IDLE_MINUTES, 절대 수명 ADMIN_SESSION_MAX_HOURS';
COMMENT ON COLUMN admin_sessions.scope IS 'full 전체 기능 / setup 비밀번호 변경·2FA 등록만 가능(첫 로그인)';
COMMENT ON COLUMN admin_sessions.client_note IS 'CLI 이름·버전 같은 표시용 문자열';
-- 매 요청 토큰 조회는 token_hash UNIQUE가 맡는다. 관리자별 살아 있는 세션(비활성화 때 일괄 폐기)
CREATE INDEX admin_sessions_admin ON admin_sessions (admin_id, created_at DESC) WHERE revoked_at IS NULL;
-- 만료 세션 정리 작업
CREATE INDEX admin_sessions_expires ON admin_sessions (expires_at);

-- ---------- 7단계: 감사 로그·메모·운영 지급·보류 검토 ----------

CREATE TABLE admin_audit_log (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  admin_id       BIGINT REFERENCES admin_users(id),
  login_id_tried TEXT CHECK (char_length(login_id_tried) <= 64),
  action         TEXT NOT NULL,
  target_type    TEXT CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server')),
  target_uuid    UUID,
  request_id     UUID,
  result         TEXT NOT NULL CHECK (result IN ('ok', 'denied', 'invalid', 'error')),
  error_code     TEXT,
  params         JSONB NOT NULL DEFAULT '{}'::jsonb,
  response       JSONB,
  ip             TEXT,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT admin_audit_action_chk CHECK (action ~ '^[a-z_]+(\.[a-z_]+)+$'),
  CONSTRAINT admin_audit_actor_chk  CHECK (admin_id IS NOT NULL OR login_id_tried IS NOT NULL OR action LIKE 'bootstrap.%')
);
COMMENT ON TABLE  admin_audit_log IS '관리자 행동 기록(추가만, 트리거가 UPDATE/DELETE 차단, 지우지 않는다). 변경 작업은 작업과 같은 트랜잭션에서 result=ok로 쓰고, 거절·실패·로그인 실패는 롤백되지 않도록 별도 트랜잭션으로 쓴다. 민감 조회(신고 증거·계정 상세)도 남긴다';
COMMENT ON COLUMN admin_audit_log.admin_id IS '행동한 관리자. 아이디를 모르는 로그인 실패와 bootstrap 명령은 NULL';
COMMENT ON COLUMN admin_audit_log.login_id_tried IS '로그인 실패 때 입력된 아이디(없는 아이디 공격 추적). 비밀번호·코드는 어디에도 저장하지 않는다';
COMMENT ON COLUMN admin_audit_log.action IS '점으로 구분한 소문자 이름. 예: auth.login, sanction.create, report.resolve, held_run.release, maintenance.schedule, grant.create, account.view, bootstrap.create_owner';
COMMENT ON COLUMN admin_audit_log.target_uuid IS '대상의 외부 id(계정·캐릭터·신고·던전 판·제재·점검 창·관리자). 내부 키는 넣지 않는다';
COMMENT ON COLUMN admin_audit_log.request_id IS '변경 요청의 request_id. (admin_id, request_id)가 ok 행에서 유일해 멱등성 기록을 겸한다';
COMMENT ON COLUMN admin_audit_log.params IS '행동의 허용 목록 필드만 정리한 요청 값(사유 코드, 기간 프리셋, 메모 길이 등). 비밀번호·코드·토큰·채팅 원문은 넣지 않는다';
COMMENT ON COLUMN admin_audit_log.response IS 'ok 변경 작업의 응답 data(멱등 재전송 때 그대로 돌려준다). 조회·실패 행은 NULL';
COMMENT ON COLUMN admin_audit_log.ip IS '접속 출처(관리자 listener는 loopback이라 보통 127.0.0.1. 프록시를 거치면 그 값)';
-- 같은 관리자의 같은 request_id는 성공 행이 하나(동시 재전송을 DB가 거절)
CREATE UNIQUE INDEX admin_audit_request_uq ON admin_audit_log (admin_id, request_id) WHERE request_id IS NOT NULL AND result = 'ok';
-- 최근 기록 보기(최신순), 한 관리자의 행동, 한 대상(계정·신고 등)을 누가 건드렸나
CREATE INDEX admin_audit_time   ON admin_audit_log (created_at DESC);
CREATE INDEX admin_audit_admin  ON admin_audit_log (admin_id, created_at DESC) WHERE admin_id IS NOT NULL;
CREATE INDEX admin_audit_target ON admin_audit_log (target_type, target_uuid, created_at DESC) WHERE target_uuid IS NOT NULL;
CREATE TRIGGER admin_audit_log_append_only BEFORE UPDATE OR DELETE ON admin_audit_log
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER admin_audit_log_no_truncate BEFORE TRUNCATE ON admin_audit_log
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

CREATE TABLE admin_account_notes (
  id         BIGSERIAL PRIMARY KEY,
  uuid       UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  admin_id   BIGINT NOT NULL REFERENCES admin_users(id),
  kind       TEXT NOT NULL DEFAULT 'note' CHECK (kind IN ('note', 'review_ack')),
  note       TEXT NOT NULL CHECK (char_length(note) BETWEEN 1 AND 500),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  admin_account_notes IS '계정에 대한 운영 메모(추가만). 수정이 필요하면 새 메모를 쓴다';
COMMENT ON COLUMN admin_account_notes.kind IS 'note 일반 메모 / review_ack "이상 기록을 검토했고 조치 불필요". 감시 목록은 계정의 마지막 review_ack 이후 기록만 점수에 넣는다';
-- 계정 상세의 최근 메모, 감시 목록의 마지막 review_ack 조회
CREATE INDEX admin_account_notes_account ON admin_account_notes (account_id, created_at DESC);
CREATE TRIGGER admin_account_notes_append_only BEFORE UPDATE OR DELETE ON admin_account_notes
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER admin_account_notes_no_truncate BEFORE TRUNCATE ON admin_account_notes
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 운영 지급: system 우편 한 통 + 이 기록 한 줄. 골드는 우편이 들고 있다가 수령할 때 gold_ledger(mail_claim)로 캐릭터에 들어간다
ALTER TABLE mails ADD COLUMN system_code TEXT CHECK (system_code IN ('compensation', 'event', 'refund', 'notice'));
ALTER TABLE mails ADD CONSTRAINT mails_system_chk CHECK ((kind = 'system') = (system_code IS NOT NULL));
COMMENT ON COLUMN mails.system_code IS 'kind=system일 때만 값이 있다. compensation 보상 / event 이벤트 / refund 환불 / notice 안내. 문구는 클라이언트가 이 코드로 조립한다(서버는 문장을 만들지 않는다)';

CREATE TABLE admin_grants (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  admin_id     BIGINT NOT NULL REFERENCES admin_users(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  mail_id      BIGINT NOT NULL UNIQUE REFERENCES mails(id),
  request_id   UUID NOT NULL,
  system_code  TEXT NOT NULL CHECK (system_code IN ('compensation', 'event', 'refund', 'notice')),
  gold         BIGINT NOT NULL DEFAULT 0 CHECK (gold >= 0),
  item_key     TEXT CHECK (item_key <> 'gold'),
  count        INT CHECK (count > 0),
  memo         TEXT NOT NULL CHECK (char_length(memo) BETWEEN 1 AND 200),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (admin_id, request_id),
  CONSTRAINT admin_grants_item_chk    CHECK ((item_key IS NULL) = (count IS NULL)),
  CONSTRAINT admin_grants_content_chk CHECK (gold > 0 OR item_key IS NOT NULL)
);
COMMENT ON TABLE  admin_grants IS '운영 지급 기록(추가만). 우편(mails.kind=system) 한 통과 1:1. 한도(1회·관리자별 일일)는 서버 환경변수 ADMIN_GRANT_*가 검사한다. 골드 보존식의 SUM(mails.gold WHERE kind=system)이 이 표와 같아야 한다';
COMMENT ON COLUMN admin_grants.memo IS '지급 사유(고객 문의 번호, 사고 설명 등). 필수';
-- 한 캐릭터가 받은 지급 이력(계정 상세), 관리자별 일일 지급 합계(한도 검사: admin_id + created_at 범위)
CREATE INDEX admin_grants_character ON admin_grants (character_id, created_at DESC);
CREATE INDEX admin_grants_admin_time ON admin_grants (admin_id, created_at);
CREATE TRIGGER admin_grants_append_only BEFORE UPDATE OR DELETE ON admin_grants
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER admin_grants_no_truncate BEFORE TRUNCATE ON admin_grants
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 운영 지급 아이템은 item_ledger에 mail 위치 +n으로 남는다(수령하면 mail_claim으로 mail -n, bag +n)
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant'));
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). 경매: auction_list 등록(가방 -n, auction +n) / auction_return 취소·만료(auction -n, 판매자 mail +n) / auction_sold 판매 체결(판매자 auction -n) / auction_buy 구매(구매자 mail +n) / mail_claim 우편 수령(mail -n, bag +n) / mail_expire 30일 경과 폐기(mail -n) / admin_grant 운영 지급(system 우편 첨부, mail +n)';
-- 같은 지급 우편의 원장은 한 번만
CREATE UNIQUE INDEX item_ledger_admin_grant_uq ON item_ledger (ref, location) WHERE reason = 'admin_grant';

CREATE TABLE held_run_reviews (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  dungeon_run_id BIGINT NOT NULL UNIQUE REFERENCES dungeon_runs(id),
  admin_id       BIGINT NOT NULL REFERENCES admin_users(id),
  decision       TEXT NOT NULL CHECK (decision IN ('released', 'rejected')),
  note           TEXT NOT NULL CHECK (char_length(note) BETWEEN 1 AND 500),
  prev_ended_at  TIMESTAMPTZ NOT NULL,
  request_id     UUID NOT NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (admin_id, request_id)
);
COMMENT ON TABLE  held_run_reviews IS '보류(held)된 던전 판의 검토 결과(추가만). 한 판에 한 번만 결정한다(UNIQUE). 검토 대기 목록 = state=held이고 이 표에 행이 없는 판. released는 판 상태를 cleared로 바꾸고(finalizeCleared) rejected는 held로 남긴다';
COMMENT ON COLUMN held_run_reviews.prev_ended_at IS '해제 전 dungeon_runs.ended_at(보류 시각). 해제하면 ended_at을 해제 시각으로 옮겨 카드 선택 시간(DUNGEON_CARD_TTL_HOURS)이 해제부터 다시 시작한다';
CREATE TRIGGER held_run_reviews_append_only BEFORE UPDATE OR DELETE ON held_run_reviews
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER held_run_reviews_no_truncate BEFORE TRUNCATE ON held_run_reviews
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();
-- 검토 대기 목록(오래된 순). 보류 판은 드물어 부분 인덱스가 아주 작다
CREATE INDEX dungeon_runs_held ON dungeon_runs (ended_at) WHERE state = 'held';

-- ---------- 7단계: 점검 창, 작업 실행 기록 ----------

CREATE TABLE maintenance_windows (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  state          TEXT NOT NULL DEFAULT 'scheduled' CHECK (state IN ('scheduled', 'cancelled', 'ended')),
  notice         TEXT NOT NULL DEFAULT '' CHECK (char_length(notice) <= 100),
  block_login_at TIMESTAMPTZ NOT NULL,
  starts_at      TIMESTAMPTZ NOT NULL,
  ends_at        TIMESTAMPTZ NOT NULL,
  created_by     BIGINT NOT NULL REFERENCES admin_users(id),
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  closed_at      TIMESTAMPTZ,
  closed_by      BIGINT REFERENCES admin_users(id),
  CONSTRAINT maintenance_range_chk  CHECK (block_login_at <= starts_at AND starts_at < ends_at),
  CONSTRAINT maintenance_closed_chk CHECK ((state = 'scheduled') = (closed_at IS NULL))
);
COMMENT ON TABLE  maintenance_windows IS '점검 창. 단계(공지 -> 새 로그인·새 판 차단 -> 점검 중)는 이 행과 서버 시각으로 계산한다(상태를 시각 경계마다 쓰지 않는다). 서버는 MAINT_POLL_SECONDS마다 읽어 메모리에 둔다';
COMMENT ON COLUMN maintenance_windows.state IS 'scheduled 예약됨(시각이 지나 점검 중이어도 종료 전까지 scheduled) / cancelled 시작 전 취소 / ended 종료(운영자가 끝냈거나 ends_at이 지나 작업이 닫음)';
COMMENT ON COLUMN maintenance_windows.notice IS '운영자가 덧붙이는 안내 문구(100자 이하). 서버가 공지 문장 틀에 끼워 chat.sys와 /meta로 내보낸다';
COMMENT ON COLUMN maintenance_windows.block_login_at IS '새 로그인·새 던전 판(입장·파티 출발·판 시작·매칭)을 막기 시작하는 시각 = starts_at - MAINT_PRE_BLOCK_MINUTES(진행 중인 판이 끝날 시간을 준다)';
COMMENT ON COLUMN maintenance_windows.starts_at IS '점검 시작: 접속 중인 모두에게 bye(MAINTENANCE)를 보내고 /health·/meta·/auth/logout 외 요청은 503 MAINTENANCE';
COMMENT ON COLUMN maintenance_windows.ends_at IS '예정 종료. 이 시각이 지나면 점검이 자동으로 풀린다(연장하려면 extend). 운영자가 더 일찍 end 할 수 있다';
COMMENT ON COLUMN maintenance_windows.closed_by IS '운영자가 닫았으면 그 관리자, ends_at 경과로 자동 종료면 NULL';
-- 열려 있는(scheduled) 점검 창은 한 번에 하나
CREATE UNIQUE INDEX maintenance_one_open ON maintenance_windows ((true)) WHERE state = 'scheduled';
CREATE INDEX maintenance_time ON maintenance_windows (starts_at DESC);

CREATE TABLE job_runs (
  id            BIGSERIAL PRIMARY KEY,
  job           TEXT NOT NULL CHECK (job ~ '^[a-z0-9-]+$'),
  started_by    TEXT NOT NULL DEFAULT 'schedule' CHECK (started_by IN ('schedule', 'manual', 'startup')),
  triggered_by  BIGINT REFERENCES admin_users(id),
  status        TEXT NOT NULL DEFAULT 'running' CHECK (status IN ('running', 'ok', 'failed')),
  started_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  finished_at   TIMESTAMPTZ,
  rows_affected INT NOT NULL DEFAULT 0 CHECK (rows_affected >= 0),
  detail        JSONB NOT NULL DEFAULT '{}'::jsonb,
  error         TEXT CHECK (char_length(error) <= 1000),
  CONSTRAINT job_runs_finished_chk CHECK ((status = 'running') = (finished_at IS NULL))
);
COMMENT ON TABLE  job_runs IS '정리·점검 작업(JobRunner)의 실행 기록. 작업 하나는 pg_try_advisory_lock으로 한 곳에서만 돌고, 못 잡으면 기록 없이 건너뛴다. "마지막 성공이 오래됐다" 알림과 정합성 점검 결과의 보관소. 서버가 죽어 running으로 남은 행은 다음 기동에서 failed(interrupted)로 닫는다. 90일 보관';
COMMENT ON COLUMN job_runs.job IS '작업 이름(소문자·숫자·하이픈). 예: purge-hourly, purge-daily, stale-runs, integrity-nightly, maintenance-close';
COMMENT ON COLUMN job_runs.rows_affected IS '지운·바꾼 행 수 합계';
COMMENT ON COLUMN job_runs.detail IS '표별 행 수, 배치 수, 걸린 시간(ms), 정합성 점검이면 항목별 불일치 건수와 표본(최대 20건)';
-- 작업별 마지막 실행·마지막 성공 조회, 보관 기간 정리
CREATE INDEX job_runs_job_time ON job_runs (job, started_at DESC);
CREATE INDEX job_runs_started ON job_runs (started_at);

-- ---------- 7단계: 정리·관리자 조회용 인덱스, 문서 보정 ----------

-- 원장 일일 집계·"오늘 변동이 있던 캐릭터" 찾기(정합성 점검): 시간순으로 쌓이는 추가 전용 표라 BRIN(수 KB)이면 충분하다
CREATE INDEX gold_ledger_created_brin ON gold_ledger USING brin (created_at);
CREATE INDEX item_ledger_created_brin ON item_ledger USING brin (created_at);
CREATE INDEX xp_ledger_created_brin   ON xp_ledger   USING brin (created_at);

-- kill_log 정리의 ON DELETE CASCADE가 처치 한 건마다 drops를 찾는다. 인덱스가 없으면 행마다 전체 스캔이다
CREATE INDEX drops_kill_idx ON drops (kill_id);
-- 우편 정리(수령된 행)와 소각 기록 FK 확인
CREATE INDEX mails_claimed_idx ON mails (claimed_at) WHERE claimed_at IS NOT NULL;
CREATE INDEX auction_sinks_mail_idx ON auction_sinks (mail_id) WHERE mail_id IS NOT NULL;

-- 계정별 이상 기록(계정 상세, 감시 목록), 심각도 2 이상 최신순(검토 대기)
CREATE INDEX anomaly_log_account_time ON anomaly_log (account_id, created_at DESC);
CREATE INDEX anomaly_log_severe ON anomaly_log (created_at DESC) WHERE severity >= 2;

-- 쓰기·삭제가 잦은 표: 기본값(20%)이면 정리 뒤 죽은 행이 오래 남는다
ALTER TABLE chat_messages SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE kill_log      SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE drops         SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE request_log   SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE anomaly_log   SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);

COMMENT ON COLUMN accounts.banned_until IS '이 시각 전까지 로그인·refresh 거부. NULL = 정지 아님. 영구 정지는 9999-12-31T00:00:00Z를 쓴다(PostgreSQL infinity는 node-pg가 Date가 아닌 값으로 돌려줘 인증 미들웨어가 깨진다)';

-- ===========================================================================
-- 8단계(0009_relay_field): 전투 중계 + 필드 파티 사냥. 정본은 migrations/0009_relay_field.sql 이고 아래는 UP 본문의 읽기용 사본이다.
-- 설계: Docs/server/phase8_api.md. 재화·원장 변경 없음(필드 세션 처치는 3단계 /kills 경로와 같다).
-- ===========================================================================

-- ---------- 8단계: 전투 전송 선택 (party_runs) ----------

ALTER TABLE party_runs ADD COLUMN transport TEXT NOT NULL DEFAULT 'relay' CHECK (transport IN ('relay', 'steam', 'dev'));
ALTER TABLE party_runs ADD COLUMN transport_epoch INT NOT NULL DEFAULT 1 CHECK (transport_epoch >= 1);
ALTER TABLE party_runs ADD COLUMN transport_switches SMALLINT NOT NULL DEFAULT 0 CHECK (transport_switches BETWEEN 0 AND 4);
ALTER TABLE party_runs ADD COLUMN transport_order TEXT[] NOT NULL DEFAULT ARRAY['relay']::TEXT[]
  CHECK (cardinality(transport_order) BETWEEN 1 AND 3 AND transport_order <@ ARRAY['relay', 'steam', 'dev']::TEXT[]);
COMMENT ON COLUMN party_runs.transport IS '지금 이 판의 전투 연결 방식. relay 우리 서버 중계(기본, 모바일 경로) / steam Steam P2P(SDR) / dev 같은 PC UDP(개발 빌드 전용). 판 단위로 하나이며 모든 멤버가 같은 방식을 쓴다';
COMMENT ON COLUMN party_runs.transport_epoch IS '전송 세대. 전환(fallback)마다 +1. 옛 세대를 보고 온 전환 요청은 거절한다(TRANSPORT_CHANGED)';
COMMENT ON COLUMN party_runs.transport_switches IS '이 판에서 전환한 횟수. 4를 넘기면 더 이상 전환하지 않는다(왕복 방지, TRANSPORT_EXHAUSTED)';
COMMENT ON COLUMN party_runs.transport_order IS '출발 때 고정한 우선순위(서버 설정 COMBAT_TRANSPORT_ORDER에서 멤버 자격으로 걸러낸 것). 전환은 이 순서의 다음 후보로만 한다. 설정이 바뀌어도 진행 중 판은 영향받지 않는다';

-- ---------- 8단계: 필드 파티 세션 ----------

CREATE TABLE field_sessions (
  id                 BIGSERIAL PRIMARY KEY,
  uuid               UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id           BIGINT NOT NULL REFERENCES parties(id),
  map_id             TEXT NOT NULL,
  state              TEXT NOT NULL DEFAULT 'active' CHECK (state IN ('active', 'ended')),
  host_character_id  BIGINT REFERENCES characters(id),
  host_epoch         INT NOT NULL DEFAULT 1 CHECK (host_epoch >= 1),
  host_since         TIMESTAMPTZ,
  session_key        BYTEA CHECK (octet_length(session_key) = 32),
  transport          TEXT NOT NULL DEFAULT 'relay' CHECK (transport IN ('relay', 'steam', 'dev')),
  transport_epoch    INT NOT NULL DEFAULT 1 CHECK (transport_epoch >= 1),
  transport_switches SMALLINT NOT NULL DEFAULT 0 CHECK (transport_switches BETWEEN 0 AND 4),
  transport_order    TEXT[] NOT NULL DEFAULT ARRAY['relay']::TEXT[]
                     CHECK (cardinality(transport_order) BETWEEN 1 AND 3 AND transport_order <@ ARRAY['relay', 'steam', 'dev']::TEXT[]),
  version            INT NOT NULL DEFAULT 0 CHECK (version >= 0),
  last_observe_at    TIMESTAMPTZ,
  last_active_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
  ended_at           TIMESTAMPTZ,
  end_reason         TEXT CHECK (end_reason IN ('empty', 'party_closed', 'stale')),
  CONSTRAINT field_sessions_end_chk CHECK ((state = 'ended') = (ended_at IS NOT NULL) AND (state = 'ended') = (end_reason IS NOT NULL)),
  CONSTRAINT field_sessions_key_chk CHECK ((state = 'active') = (session_key IS NOT NULL))
);
COMMENT ON TABLE  field_sessions IS '같은 파티가 같은 필드 맵에서 몬스터를 함께 사냥하는 한 덩어리(던파·메이플식 공유 필드). 파티와 맵 하나당 활성 세션 하나. 몬스터는 host_character_id의 PC가 계산하고 서버는 상태를 저장하지 않는다. 보상은 3단계 /kills로 멤버마다 따로 판정한다';
COMMENT ON COLUMN field_sessions.uuid IS '외부 노출 id(릴레이 방 id, 하트비트·떠나기·처치 보고의 session_id). 내부 키 id는 API에 나가지 않는다';
COMMENT ON COLUMN field_sessions.map_id IS 'maps.json 맵 id. fieldSpawns가 있고 instanced=false인 맵만 세션을 만들 수 있다';
COMMENT ON COLUMN field_sessions.host_character_id IS '지금 몬스터를 계산하는 PC의 캐릭터. NULL = 호스트 없음(후보가 없는 짧은 구간, 다음에 연결 확인된 멤버가 이어받는다). 선출 규칙: 파티 방장 우선, 없으면 파티 가입이 가장 빠른 멤버';
COMMENT ON COLUMN field_sessions.host_epoch IS '호스트 세대. 인계마다 +1(4단계 party_runs.host_epoch와 같은 뜻). 옛 세대의 observe는 거절한다';
COMMENT ON COLUMN field_sessions.host_since IS '현재 호스트가 된 시각. 세션 시작 직후 선출 창(FIELD_ELECTION_WINDOW_SECONDS) 판단에 쓴다';
COMMENT ON COLUMN field_sessions.session_key IS '이 세션의 입장 토큰 서명 키(난수 32바이트). 호스트 PC만 받는다. Steam P2P·dev 전송에서만 쓴다(중계는 서버가 좌석을 도장 찍으므로 토큰이 필요 없다). 세션이 끝나면 NULL';
COMMENT ON COLUMN field_sessions.transport IS 'party_runs.transport 와 같은 뜻';
COMMENT ON COLUMN field_sessions.version IS '멤버·호스트·전송이 바뀔 때마다 +1. GET 폴링의 after_version 기준';
COMMENT ON COLUMN field_sessions.last_observe_at IS '호스트의 마지막 관찰 보고(observe) 시각. 오래되면 처치 대조 게이트를 끄고 공급 상한만 쓴다(FIELD_OBSERVE_GRACE_SECONDS)';
COMMENT ON COLUMN field_sessions.last_active_at IS '마지막 활동(하트비트·입장·처치). 방치 세션 정리 기준';
COMMENT ON COLUMN field_sessions.end_reason IS 'empty 마지막 멤버가 떠남 / party_closed 파티 해산 / stale 방치 정리';
CREATE UNIQUE INDEX field_sessions_one_active ON field_sessions (party_id, map_id) WHERE state = 'active';
CREATE INDEX field_sessions_stale ON field_sessions (last_active_at) WHERE state = 'active';
CREATE INDEX field_sessions_host ON field_sessions (host_character_id) WHERE state = 'active';

CREATE TABLE field_session_members (
  session_id      BIGINT NOT NULL REFERENCES field_sessions(id),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  account_id      BIGINT NOT NULL REFERENCES accounts(id),
  seat            SMALLINT NOT NULL CHECK (seat BETWEEN 0 AND 3),
  state           TEXT NOT NULL DEFAULT 'joined' CHECK (state IN ('joined', 'playing', 'disconnected', 'left')),
  left_reason     TEXT CHECK (left_reason IN ('left', 'map_move', 'party_left', 'kicked', 'party_closed', 'rejoin_timeout', 'stale', 'dungeon_start', 'replaced')),
  joined_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at    TIMESTAMPTZ,
  disconnected_at TIMESTAMPTZ,
  left_at         TIMESTAMPTZ,
  attack_cap      NUMERIC(14, 3) NOT NULL DEFAULT 0 CHECK (attack_cap >= 0),
  kills_accepted  INT NOT NULL DEFAULT 0 CHECK (kills_accepted >= 0),
  kills_credited  INT NOT NULL DEFAULT 0 CHECK (kills_credited >= 0),
  PRIMARY KEY (session_id, character_id),
  CONSTRAINT field_session_members_left_chk CHECK ((state = 'left') = (left_reason IS NOT NULL AND left_at IS NOT NULL))
);
COMMENT ON TABLE  field_session_members IS '세션 멤버(사람만). 좌석, 연결 상태(하트비트), 화력 상한 스냅샷, 처치 대조 카운터. 같은 세션에 다시 들어오면(맵 왕복) 행을 되살린다(카운터 유지)';
COMMENT ON COLUMN field_session_members.seat IS '중계 방의 좌석이자 PartyNet 슬롯(0~3). 세션 안에서 안정적이다. 호스트 선출 순서와는 무관하다(선출은 파티 방장 우선, 다음 파티 가입 순)';
COMMENT ON COLUMN field_session_members.state IS 'joined 입장함(연결·환영 전) / playing 연결 확인(호스트 후보 자격) / disconnected 하트비트 끊김(60초 안에 복귀하면 playing) / left 떠남';
COMMENT ON COLUMN field_session_members.left_reason IS 'left 스스로 / map_move 다른 맵으로 이동 / party_left·kicked 파티 이탈·강퇴 / party_closed 해산 / rejoin_timeout 끊긴 뒤 60초 초과 / stale 방치 정리 / dungeon_start 던전 출발 / replaced 같은 캐릭터의 다른 세션 입장';
COMMENT ON COLUMN field_session_members.attack_cap IS '하트비트 때 서버가 다시 계산한 이 멤버의 attackCap(레벨·착용 장비). 세션 화력 상한 = 활성 멤버 합 x FIELD_POWER_SLACK. 클라이언트 값이 아니다';
COMMENT ON COLUMN field_session_members.kills_accepted IS '이 세션에서 받아들인 처치 수(멤버가 보고하고 서버가 인정한 것). kills_credited 와 비교하는 부채 계산용';
COMMENT ON COLUMN field_session_members.kills_credited IS '호스트가 observe 로 "이 멤버의 기여 처치"로 알린 수. 부채 = accepted - credited 가 FIELD_UNCREDITED_MAX 를 넘으면 새 처치를 받지 않는다(자리만 지키는 얹혀가기 방지)';
CREATE UNIQUE INDEX field_session_members_one_active ON field_session_members (character_id) WHERE state <> 'left';
CREATE UNIQUE INDEX field_session_members_seat_uq ON field_session_members (session_id, seat) WHERE state <> 'left';
CREATE INDEX field_session_members_session ON field_session_members (session_id) WHERE state <> 'left';

-- ---------- 8단계: kill_log 확장 ----------

ALTER TABLE kill_log ADD COLUMN field_session_id BIGINT REFERENCES field_sessions(id);
ALTER TABLE kill_log ADD COLUMN monster_ref BIGINT CHECK (monster_ref >= 0);
ALTER TABLE kill_log ADD COLUMN xp_factor NUMERIC(4, 3) CHECK (xp_factor > 0 AND xp_factor <= 1);
ALTER TABLE kill_log ADD CONSTRAINT kill_log_field_chk
  CHECK ((field_session_id IS NULL OR context = 'field') AND (monster_ref IS NULL OR field_session_id IS NOT NULL));
COMMENT ON COLUMN kill_log.field_session_id IS '공유 필드 세션에서의 처치(context=field)일 때 field_sessions.id. 솔로 필드·던전·연출은 NULL';
COMMENT ON COLUMN kill_log.monster_ref IS '호스트가 붙인 몬스터 식별(세대 x 2^20 + 몬스터 번호)을 멤버가 그대로 보고한 값. 같은 멤버가 같은 몬스터를 두 번 보고하는 사고를 막는 중복 키로만 쓴다. 지급 근거가 아니다';
COMMENT ON COLUMN kill_log.xp_factor IS '파티 세션의 레벨 격차 감쇠 배율(1 이하). 경험치와 드롭 확률에 곱했다. NULL = 감쇠 없음(솔로·던전). 사후 분석용이고 요청으로 받지 않는다';
CREATE UNIQUE INDEX kill_log_field_ref_uq ON kill_log (field_session_id, character_id, monster_ref) WHERE monster_ref IS NOT NULL;

-- ---------- 8단계: 이상 기록 kind 확장 ----------

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse'));

-- ---------- 8단계: 중계 방 사용 요약 ----------

CREATE TABLE relay_room_stats (
  id                 BIGSERIAL PRIMARY KEY,
  room_kind          TEXT NOT NULL CHECK (room_kind IN ('run', 'field')),
  room_ref           UUID NOT NULL,
  started_at         TIMESTAMPTZ NOT NULL,
  ended_at           TIMESTAMPTZ NOT NULL,
  peak_peers         SMALLINT NOT NULL CHECK (peak_peers BETWEEN 1 AND 4),
  bytes_in           BIGINT NOT NULL DEFAULT 0 CHECK (bytes_in >= 0),
  bytes_out          BIGINT NOT NULL DEFAULT 0 CHECK (bytes_out >= 0),
  frames_in          BIGINT NOT NULL DEFAULT 0 CHECK (frames_in >= 0),
  frames_out         BIGINT NOT NULL DEFAULT 0 CHECK (frames_out >= 0),
  dropped_unreliable BIGINT NOT NULL DEFAULT 0 CHECK (dropped_unreliable >= 0),
  reconnects         INT NOT NULL DEFAULT 0 CHECK (reconnects >= 0),
  rtt_p50_ms         INT CHECK (rtt_p50_ms >= 0),
  rtt_p95_ms         INT CHECK (rtt_p95_ms >= 0),
  close_codes        JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(close_codes) = 'object'),
  CONSTRAINT relay_room_stats_time_chk CHECK (ended_at >= started_at)
);
COMMENT ON TABLE  relay_room_stats IS '중계 방 한 번의 사용 요약(방이 비어 닫힐 때 한 행). 대역폭 비용 추정 검증, 남용 추적, 용량 계획용. 내용(payload)은 어디에도 저장하지 않는다. 서버가 재시작하면 같은 room_ref 로 행이 여러 개일 수 있다';
COMMENT ON COLUMN relay_room_stats.room_ref IS '판이면 party_runs.uuid, 필드면 field_sessions.uuid';
COMMENT ON COLUMN relay_room_stats.dropped_unreliable IS '느린 소비자 때문에 최신 값만 남기고 버린 비신뢰 프레임(스냅샷·멤버 상태) 수';
COMMENT ON COLUMN relay_room_stats.close_codes IS '종료 코드별 횟수 예: {"1000":2,"4008":1}. SLOW_CONSUMER·PEER_TIMEOUT 비율이 네트워크 품질 지표다';
CREATE INDEX relay_room_stats_ended ON relay_room_stats (ended_at);
CREATE INDEX relay_room_stats_room ON relay_room_stats (room_kind, room_ref);

-- ================= 9단계: 부정 행위 방지 (0020_anti_abuse, Docs/server/phase9_anti_abuse.md) =================

-- ---------- 1. 계정 세션, 로그인 기록 ----------

ALTER TABLE accounts
  ADD COLUMN active_family_id    UUID,
  ADD COLUMN active_install_id   UUID,
  ADD COLUMN active_device_hash  TEXT CHECK (active_device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN active_session_at   TIMESTAMPTZ;
COMMENT ON COLUMN accounts.active_family_id IS '현재 유효한 세션(리프레시 가족). 액세스 토큰 sid가 이것과 다르면 SESSION_REPLACED. 같은 계정 재로그인이 갱신';
COMMENT ON COLUMN accounts.active_device_hash IS '현재 세션의 기기(클라이언트 지문의 HMAC-SHA256 hex). 프레즌스의 기기 한도·레이드 사람 수 스냅샷의 원본. 기기를 모르면 NULL';

ALTER TABLE auth_identities
  ADD COLUMN steam_owner_id TEXT CHECK (steam_owner_id ~ '^[0-9]{17}$');
COMMENT ON COLUMN auth_identities.steam_owner_id IS 'Steam 패밀리 공유일 때 앱 소유자의 SteamID(AuthenticateUserTicket ownersteamid). 사람 키 = COALESCE(steam_owner_id, subject)';
CREATE INDEX auth_identities_steam_owner ON auth_identities (steam_owner_id) WHERE steam_owner_id IS NOT NULL;

ALTER TABLE refresh_tokens
  ADD COLUMN install_id    UUID,
  ADD COLUMN device_hash   TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN revoke_reason TEXT CHECK (revoke_reason IN ('logout', 'reuse', 'replaced', 'device_mismatch', 'admin', 'legacy')),
  ADD CONSTRAINT refresh_tokens_revoke_chk CHECK (revoke_reason IS NULL OR revoked_at IS NOT NULL);
UPDATE refresh_tokens SET revoke_reason = 'legacy' WHERE revoked_at IS NOT NULL;
COMMENT ON COLUMN refresh_tokens.device_hash IS '이 가족을 만든 로그인의 기기. 리프레시 기기 불일치 감지용';
COMMENT ON COLUMN refresh_tokens.revoke_reason IS 'replaced는 같은 계정 재로그인으로 정상 교체(REFRESH_REUSED 경보를 내지 않는다)';

CREATE TABLE login_events (
  id             BIGSERIAL PRIMARY KEY,
  account_id     BIGINT NOT NULL REFERENCES accounts(id),
  character_id   BIGINT REFERENCES characters(id),
  kind           TEXT NOT NULL CHECK (kind IN ('register', 'login', 'steam_login', 'refresh', 'enter')),
  install_id     UUID,
  device_hash    TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ip             INET,
  steam_id       TEXT CHECK (steam_id ~ '^[0-9]{17}$'),
  steam_owner_id TEXT CHECK (steam_owner_id ~ '^[0-9]{17}$'),
  client_version TEXT,
  flags          TEXT[] NOT NULL DEFAULT '{}',
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  login_events IS '로그인·세션 진입 기록(추가 위주, 90일 보관). 다계정 추적과 사후 조사의 원본. 외부 노출 id 없음(관리자 응답은 필드만)';
COMMENT ON COLUMN login_events.character_id IS 'kind=enter(프레즌스 첫 신호)일 때만';
COMMENT ON COLUMN login_events.flags IS 'device_missing / device_mismatch / replaced_other';
COMMENT ON COLUMN login_events.ip IS '개인정보 성격. 보관 기간 후 삭제';
CREATE INDEX login_events_account_time ON login_events (account_id, created_at DESC);
CREATE INDEX login_events_created ON login_events (created_at);
CREATE INDEX login_events_device ON login_events (device_hash, created_at DESC) WHERE device_hash IS NOT NULL;
CREATE INDEX login_events_ip ON login_events (ip, created_at DESC) WHERE ip IS NOT NULL;

CREATE TABLE account_devices (
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  device_hash   TEXT NOT NULL CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  seen_count    INT NOT NULL DEFAULT 1 CHECK (seen_count >= 1),
  PRIMARY KEY (account_id, device_hash)
);
COMMENT ON TABLE account_devices IS '계정이 쓴 기기 집계. "같은 기기를 쓴 계정" 조회(레이드·경매 플래그·정지 전파)의 원본. 마지막 관측 180일 뒤 삭제';
CREATE INDEX account_devices_device ON account_devices (device_hash, last_seen_at DESC);
CREATE INDEX account_devices_seen ON account_devices (last_seen_at);

CREATE TABLE account_ips (
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  ip            INET NOT NULL,
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  seen_count    INT NOT NULL DEFAULT 1 CHECK (seen_count >= 1),
  PRIMARY KEY (account_id, ip)
);
COMMENT ON TABLE account_ips IS '계정이 쓴 IP 집계. 거절 근거가 아니라 감시 점수와 경매 SAME_IP 플래그용';
CREATE INDEX account_ips_ip ON account_ips (ip, last_seen_at DESC);
CREATE INDEX account_ips_seen ON account_ips (last_seen_at);

-- ---------- 2. 프레즌스, 플레이 시간 ----------

CREATE TABLE online_sessions (
  account_id       BIGINT PRIMARY KEY REFERENCES accounts(id),
  character_id     BIGINT NOT NULL REFERENCES characters(id),
  family_id        UUID NOT NULL,
  install_id       UUID,
  device_hash      TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ip               INET,
  map_id           TEXT NOT NULL,
  prev_map_id      TEXT,
  map_since        TIMESTAMPTZ NOT NULL,
  map_changed_at   TIMESTAMPTZ NOT NULL,
  auto_play        BOOLEAN NOT NULL DEFAULT false,
  input_recent     BOOLEAN NOT NULL DEFAULT true,
  unattended_since TIMESTAMPTZ,
  started_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at     TIMESTAMPTZ NOT NULL,
  ended_at         TIMESTAMPTZ,
  end_reason       TEXT CHECK (end_reason IN ('leave', 'replaced', 'timeout', 'banned')),
  CONSTRAINT online_sessions_end_chk CHECK ((ended_at IS NULL) = (end_reason IS NULL))
);
COMMENT ON TABLE  online_sessions IS '계정당 한 행: 지금(또는 마지막) 온라인 캐릭터. 온라인 = ended_at IS NULL AND last_seen_at > now() - PRESENCE_ONLINE_SECONDS(90). 기기 동시 접속·처치 맵 확인·보스 체류의 근거';
COMMENT ON COLUMN online_sessions.map_since IS '현재 맵에 끊김 없이 있기 시작한 시각(맵 이동 또는 신호 공백 때 갱신). 필드 보스 체류';
COMMENT ON COLUMN online_sessions.prev_map_id IS '직전 맵. 맵 이동 직후 PRESENCE_MAP_GRACE_SECONDS 동안 옛 맵 처치 보고를 받아 준다';
CREATE INDEX online_sessions_device ON online_sessions (device_hash, last_seen_at) WHERE ended_at IS NULL AND device_hash IS NOT NULL;
CREATE INDEX online_sessions_ip ON online_sessions (ip, last_seen_at) WHERE ended_at IS NULL AND ip IS NOT NULL;
CREATE INDEX online_sessions_char ON online_sessions (character_id);

CREATE TABLE play_time_hourly (
  character_id       BIGINT NOT NULL REFERENCES characters(id),
  hour_start         TIMESTAMPTZ NOT NULL,
  active_seconds     SMALLINT NOT NULL DEFAULT 0 CHECK (active_seconds BETWEEN 0 AND 3600),
  auto_seconds       SMALLINT NOT NULL DEFAULT 0 CHECK (auto_seconds BETWEEN 0 AND 3600),
  unattended_seconds SMALLINT NOT NULL DEFAULT 0 CHECK (unattended_seconds BETWEEN 0 AND 3600),
  beats              SMALLINT NOT NULL DEFAULT 0 CHECK (beats >= 0),
  PRIMARY KEY (character_id, hour_start)
);
COMMENT ON TABLE  play_time_hourly IS '프레즌스가 쌓은 활동 시간(서버 시계, 신호 간격 60초 이하만). 경제 속도 정지의 분모. 35일 보관';
COMMENT ON COLUMN play_time_hourly.hour_start IS 'date_trunc(hour, 신호를 받은 시각) UTC';
CREATE INDEX play_time_hourly_hour ON play_time_hourly (hour_start);

-- ---------- 3. 경제 속도 감시 ----------

CREATE TABLE income_hourly (
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  hour_start    TIMESTAMPTZ NOT NULL,
  level_max     SMALLINT NOT NULL CHECK (level_max >= 1),
  xp            BIGINT NOT NULL DEFAULT 0 CHECK (xp >= 0),
  gold_acq      BIGINT NOT NULL DEFAULT 0 CHECK (gold_acq >= 0),
  item_value    BIGINT NOT NULL DEFAULT 0 CHECK (item_value >= 0),
  ore           INT NOT NULL DEFAULT 0 CHECK (ore >= 0),
  essence       INT NOT NULL DEFAULT 0 CHECK (essence >= 0),
  core          INT NOT NULL DEFAULT 0 CHECK (core >= 0),
  epic_plus     INT NOT NULL DEFAULT 0 CHECK (epic_plus >= 0),
  unique_plus   INT NOT NULL DEFAULT 0 CHECK (unique_plus >= 0),
  auction_in    BIGINT NOT NULL DEFAULT 0 CHECK (auction_in >= 0),
  auction_in_w  BIGINT NOT NULL DEFAULT 0 CHECK (auction_in_w >= 0),
  auction_out   BIGINT NOT NULL DEFAULT 0 CHECK (auction_out >= 0),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, hour_start)
);
COMMENT ON TABLE  income_hourly IS '원장에서 파생한 시간별 획득 집계(재화 정본은 원장). 경제 요청 트랜잭션이 같은 트랜잭션에서 증분하고 일 1회 원장과 대조·재계산한다. 35일 보관';
COMMENT ON COLUMN income_hourly.item_value IS '획득한 아이템을 상점 판매가로 환산한 값. 모아 두고 팔지 않아도 감시된다';
COMMENT ON COLUMN income_hourly.auction_in_w IS '경매 판매 수입 x 상대 위험 가중(플래그 최댓값/100). 다른 계정에서 들어온 순유입 감시';
CREATE INDEX income_hourly_updated ON income_hourly (updated_at);
CREATE INDEX income_hourly_hour ON income_hourly (hour_start);

CREATE TABLE economy_holds (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id     BIGINT NOT NULL REFERENCES accounts(id),
  character_id   BIGINT REFERENCES characters(id),
  scope_char     BIGINT GENERATED ALWAYS AS (COALESCE(character_id, 0)) STORED,
  kind           TEXT NOT NULL CHECK (kind IN ('velocity', 'auction', 'linked', 'manual')),
  state          TEXT NOT NULL CHECK (state IN ('shadow', 'active', 'released', 'clawed_back')),
  origin_hold_id BIGINT REFERENCES economy_holds(id),
  window_kind    TEXT CHECK (window_kind IN ('1h', '24h', '7d', 'auction_24h')),
  window_start   TIMESTAMPTZ,
  window_end     TIMESTAMPTZ,
  evidence       JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(evidence) = 'object'),
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  reviewed_by    TEXT,
  reviewed_at    TIMESTAMPTZ,
  released_at    TIMESTAMPTZ,
  note           TEXT CHECK (char_length(note) <= 500),
  clawback       JSONB CHECK (clawback IS NULL OR jsonb_typeof(clawback) = 'object'),
  CONSTRAINT economy_holds_linked_chk CHECK ((kind = 'linked') = (origin_hold_id IS NOT NULL)),
  CONSTRAINT economy_holds_review_chk CHECK (state IN ('shadow', 'active') OR reviewed_at IS NOT NULL)
);
COMMENT ON TABLE  economy_holds IS '경제 정지(검토 중). shadow = log_only 모드 기록(막지 않음), active = 막음. 자동 해제 없음. 기록은 지우지 않는다';
COMMENT ON COLUMN economy_holds.character_id IS 'NULL이면 계정 전체(경매 위반·연결 전파·수동 계정 정지)';
COMMENT ON COLUMN economy_holds.evidence IS '근거: { metric, value, cap, mult, active_seconds, band, windows:{...} }. 플레이어에게 보이지 않는다';
COMMENT ON COLUMN economy_holds.released_at IS '해제 시각. 이후 평가에서 이 시각 이전 버킷은 제외(운영자가 승인한 수입)';
-- 막는 상태는 active와 clawed_back(회수 뒤에도 운영자가 해제할 때까지 막는다). 같은 (계정, 범위)에 막는 정지는 하나
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char) WHERE state IN ('active', 'clawed_back');
CREATE INDEX economy_holds_account_active ON economy_holds (account_id) WHERE state IN ('active', 'clawed_back');
CREATE INDEX economy_holds_state_time ON economy_holds (state, created_at DESC);
CREATE INDEX economy_holds_shadow ON economy_holds (account_id, scope_char, created_at DESC) WHERE state = 'shadow';
CREATE INDEX economy_holds_origin ON economy_holds (origin_hold_id) WHERE origin_hold_id IS NOT NULL;

-- ---------- 4. 파티 사람 수, 기여 ----------

ALTER TABLE party_run_members
  ADD COLUMN device_hash TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN steam_key   TEXT CHECK (steam_key ~ '^[0-9]{17}$'),
  ADD COLUMN install_id  UUID;
COMMENT ON COLUMN party_run_members.install_id IS '판 시작 때 세션의 설치 id 스냅샷(device_hash를 생략·위조해도 같은 설치 = 한 사람)';
COMMENT ON COLUMN party_run_members.device_hash IS '판 시작 때 이 멤버 세션의 기기 스냅샷(같은 기기 = 한 사람)';
COMMENT ON COLUMN party_run_members.steam_key IS '판 시작 때 COALESCE(steam_owner_id, steam subject) 스냅샷(같은 Steam 소유자 = 한 사람)';

ALTER TABLE dungeon_runs ADD COLUMN contribution JSONB CHECK (contribution IS NULL OR jsonb_typeof(contribution) = 'object');
COMMENT ON COLUMN dungeon_runs.contribution IS '정산 때의 기여 판정 { share, hits, source: host|none, met }. 관리자 검토용';

ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_lock_reason_check;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_lock_reason_check
  CHECK (lock_reason IN ('ALREADY_CLAIMED', 'TOO_FEW_HUMANS', 'KEYS_MISSING', 'LOW_CONTRIBUTION'));
COMMENT ON COLUMN dungeon_runs.lock_reason IS 'ALREADY_CLAIMED 이번 기간 보상 수령 / TOO_FEW_HUMANS 보상 최소 인원(사람 수, 같은 기기·Steam은 1명) 미달 / KEYS_MISSING 열쇠 부족 / LOW_CONTRIBUTION 피해 지분·적중 수 기여 부족';

-- ---------- 5. 전직·각성 서버 기록 ----------

CREATE TABLE character_career (
  character_id     BIGINT PRIMARY KEY REFERENCES characters(id),
  career           SMALLINT NOT NULL CHECK (career BETWEEN 1 AND 4),
  stage            SMALLINT NOT NULL DEFAULT 0 CHECK (stage BETWEEN 0 AND 5),
  promoted_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  stage_changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  source           TEXT NOT NULL CHECK (source IN ('promote', 'legacy_backfill', 'admin'))
);
COMMENT ON TABLE  character_career IS '전직·각성의 서버 진실. 행 없음 = 미전직. PUT state는 이 값과 일치하는 career·questStage·awakened만 받는다';
COMMENT ON COLUMN character_career.stage IS 'Progression.AwakeningStage와 같다: 0~1 대화, 2 시련 대기, 3~4 대화, 5 각성 완료';
COMMENT ON COLUMN character_career.source IS 'legacy_backfill = 이 마이그레이션 전에 클라이언트 값으로 저장된 상태를 이전(감사용)';

CREATE TABLE character_career_trials (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  career       SMALLINT NOT NULL CHECK (career BETWEEN 1 AND 4),
  started_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  ended_at     TIMESTAMPTZ,
  outcome      TEXT CHECK (outcome IN ('success', 'fail', 'expired')),
  CONSTRAINT career_trials_end_chk CHECK ((ended_at IS NULL) = (outcome IS NULL))
);
COMMENT ON TABLE character_career_trials IS '각성 시련 시도(시작과 보고). 성공 보고는 최소 시간·마을 체류 검증을 통과해야 stage 3으로 넘어간다';
CREATE UNIQUE INDEX career_trials_one_open ON character_career_trials (character_id) WHERE ended_at IS NULL;
CREATE INDEX career_trials_char ON character_career_trials (character_id, started_at DESC);

INSERT INTO character_career (character_id, career, stage, promoted_at, stage_changed_at, source)
SELECT cs.character_id,
       (cs.career->>'career')::int,
       LEAST(5, GREATEST(0, COALESCE((cs.career->>'questStage')::int, 0))),
       now(), now(), 'legacy_backfill'
  FROM character_state cs
 WHERE cs.career IS NOT NULL AND jsonb_typeof(cs.career) = 'object'
   AND COALESCE((cs.career->>'career')::int, 0) BETWEEN 1 AND 4;

-- ---------- 6. 경매 의심 거래 ----------

CREATE TABLE auction_trade_flags (
  id         BIGSERIAL PRIMARY KEY,
  trade_id   BIGINT NOT NULL REFERENCES auction_trades(id),
  flag       TEXT NOT NULL CHECK (flag IN ('CEILING_PRICE', 'NEW_BUYER', 'SAME_DEVICE', 'SAME_STEAM', 'SAME_IP', 'PAIR_REPEAT')),
  weight_pct SMALLINT NOT NULL CHECK (weight_pct BETWEEN 100 AND 1000),
  detail     JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (trade_id, flag)
);
COMMENT ON TABLE auction_trade_flags IS '성공한 체결의 의심 표시(추가만). 거절하지 않고 기록해 관리자 목록·경매 위반 가중·정지 전파의 근거가 된다';
CREATE INDEX auction_trade_flags_time ON auction_trade_flags (created_at DESC);
CREATE INDEX auction_trade_flags_flag ON auction_trade_flags (flag, created_at DESC);
CREATE TRIGGER auction_trade_flags_append_only BEFORE UPDATE OR DELETE ON auction_trade_flags
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER auction_trade_flags_no_truncate BEFORE TRUNCATE ON auction_trade_flags
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 7. 원장 사유, 이상 기록 종류, 관리자 감사 대상 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback'));

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution'));
COMMENT ON COLUMN anomaly_log.kind IS '기존 값 + kill_presence 처치 보고의 맵·보스 체류 불일치 / device_limit 기기 동시 접속 초과 / ip_cluster 같은 IP 다수 동시 접속(감시 점수) / member_card 호스트가 보고한 멤버 카드 불일치 / career_state 전직·각성 비정상 시도 / contribution 기여 판단 불가·분쟁';

ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold'));

-- ============================================================
-- 10단계: 던전 클리어권(소탕)과 운영 우편 캠페인 (migrations/0021_sweep_and_mail.sql UP 본문)
-- ============================================================

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
