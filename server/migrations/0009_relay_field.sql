-- 0009_relay_field: 8단계(전투 중계 + 필드 파티 사냥) 테이블
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0008과 같다. 선행: 0005_party, 0008_ops.   (초안: 구현 전에 Docs/server/phase8_api.md 16절 결정 대기를 확인한다)
-- 설계: Docs/server/phase8_api.md, Docs/server/phase8_mapping.md
--
-- 이 마이그레이션이 하는 일
--   1. party_runs: 전투 전송 선택 기록(transport, 세대, 우선순위, 전환 횟수). 중계(relay)가 기본이고 Steam P2P는 대안이다.
--   2. 필드 파티 세션: field_sessions(같은 파티가 같은 필드 맵에서 함께 사냥하는 한 덩어리), field_session_members(멤버별 자리·연결 상태·처치 대조 카운터).
--   3. kill_log 확장: 필드 세션 처치 표시(field_session_id), 몬스터 중복 보고 방지(monster_ref), 레벨 격차 감쇠 기록(xp_factor).
--   4. anomaly_log kind 확장(field_uncredited, field_host, relay_abuse).
--   5. relay_room_stats: 중계 방 한 번의 사용 요약(대역폭·종료 사유). 운영 비용·남용 추적용이고 재화와 무관하다.
--
-- 만들지 않는 것
--   - 중계 연결 상태 표. 어느 좌석이 지금 중계에 붙어 있는지는 서버 메모리(RelayRegistry)가 가진다. 재시작하면 클라이언트가
--     재접속(티켓 재발급)하면서 방이 DB의 판·세션 멤버십에서 다시 만들어진다.
--   - 필드 채집 노드·상자·드롭의 공유 표. 모두 캐릭터별 그대로다(character_node_state, character_chests, drops).
--   - 재화·아이템이 움직이는 새 경로. 필드 세션 처치는 3단계의 /kills 한 경로를 그대로 쓰고 원장도 같다(xp_ledger 'kill', item_ledger/gold_ledger 'drop_claim').
--
-- 락 순서(4단계 규칙에 한 줄 추가): ① 캐릭터 행(id 오름차순) ② parties ③ party_runs ④ field_sessions. field_session_members 는 세션 행 잠금 아래에서만 바꾼다.
--   필드 요청(enter, leave, heartbeat, claim, observe)은 자기 캐릭터 하나만 잠그므로 교착이 없다.

-- ============ UP ============

-- ---------- 1. 전투 전송 선택 (party_runs) ----------

ALTER TABLE party_runs ADD COLUMN transport TEXT NOT NULL DEFAULT 'relay' CHECK (transport IN ('relay', 'steam', 'dev'));
ALTER TABLE party_runs ADD COLUMN transport_epoch INT NOT NULL DEFAULT 1 CHECK (transport_epoch >= 1);
ALTER TABLE party_runs ADD COLUMN transport_switches SMALLINT NOT NULL DEFAULT 0 CHECK (transport_switches BETWEEN 0 AND 4);
ALTER TABLE party_runs ADD COLUMN transport_order TEXT[] NOT NULL DEFAULT ARRAY['relay']::TEXT[]
  CHECK (cardinality(transport_order) BETWEEN 1 AND 3 AND transport_order <@ ARRAY['relay', 'steam', 'dev']::TEXT[]);
COMMENT ON COLUMN party_runs.transport IS '지금 이 판의 전투 연결 방식. relay 우리 서버 중계(기본, 모바일 경로) / steam Steam P2P(SDR) / dev 같은 PC UDP(개발 빌드 전용). 판 단위로 하나이며 모든 멤버가 같은 방식을 쓴다';
COMMENT ON COLUMN party_runs.transport_epoch IS '전송 세대. 전환(fallback)마다 +1. 옛 세대를 보고 온 전환 요청은 거절한다(TRANSPORT_CHANGED)';
COMMENT ON COLUMN party_runs.transport_switches IS '이 판에서 전환한 횟수. 4를 넘기면 더 이상 전환하지 않는다(왕복 방지, TRANSPORT_EXHAUSTED)';
COMMENT ON COLUMN party_runs.transport_order IS '출발 때 고정한 우선순위(서버 설정 COMBAT_TRANSPORT_ORDER에서 멤버 자격으로 걸러낸 것). 전환은 이 순서의 다음 후보로만 한다. 설정이 바뀌어도 진행 중 판은 영향받지 않는다';

-- ---------- 2. 필드 파티 세션 ----------

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
-- 파티와 맵 하나당 활성 세션 하나(enter가 조회·생성하는 유일성). 같은 파티가 다른 맵에 동시에 세션을 가질 수 있다
CREATE UNIQUE INDEX field_sessions_one_active ON field_sessions (party_id, map_id) WHERE state = 'active';
-- 방치 세션 정리(stale-runs 작업)
CREATE INDEX field_sessions_stale ON field_sessions (last_active_at) WHERE state = 'active';
-- 호스트 캐릭터로 찾기(호스트 권한 검사, 캐릭터 삭제·정지 때 정리)
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
-- 한 캐릭터는 동시에 한 필드 세션에만 있다(내 세션 조회, 처치 보고의 멤버십 확인, 맵 이동 시 이전 세션 정리)
CREATE UNIQUE INDEX field_session_members_one_active ON field_session_members (character_id) WHERE state <> 'left';
-- 세션 안 좌석은 활성 멤버끼리 겹치지 않는다(릴레이 from 도장의 근거)
CREATE UNIQUE INDEX field_session_members_seat_uq ON field_session_members (session_id, seat) WHERE state <> 'left';
-- 세션 멤버 목록(호스트 선출, 하트비트 응답, 화력 상한 합계)
CREATE INDEX field_session_members_session ON field_session_members (session_id) WHERE state <> 'left';

-- ---------- 3. kill_log 확장 ----------

ALTER TABLE kill_log ADD COLUMN field_session_id BIGINT REFERENCES field_sessions(id);
ALTER TABLE kill_log ADD COLUMN monster_ref BIGINT CHECK (monster_ref >= 0);
ALTER TABLE kill_log ADD COLUMN xp_factor NUMERIC(4, 3) CHECK (xp_factor > 0 AND xp_factor <= 1);
ALTER TABLE kill_log ADD CONSTRAINT kill_log_field_chk
  CHECK ((field_session_id IS NULL OR context = 'field') AND (monster_ref IS NULL OR field_session_id IS NOT NULL));
COMMENT ON COLUMN kill_log.field_session_id IS '공유 필드 세션에서의 처치(context=field)일 때 field_sessions.id. 솔로 필드·던전·연출은 NULL';
COMMENT ON COLUMN kill_log.monster_ref IS '호스트가 붙인 몬스터 식별(세대 x 2^20 + 몬스터 번호)을 멤버가 그대로 보고한 값. 같은 멤버가 같은 몬스터를 두 번 보고하는 사고를 막는 중복 키로만 쓴다. 지급 근거가 아니다';
COMMENT ON COLUMN kill_log.xp_factor IS '파티 세션의 레벨 격차 감쇠 배율(1 이하). 경험치와 드롭 확률에 곱했다. NULL = 감쇠 없음(솔로·던전). 사후 분석용이고 요청으로 받지 않는다';
-- 같은 세션에서 같은 멤버의 같은 몬스터 이중 보고 차단. kill_log 7일 보관과 같은 수명이라 인덱스가 커지지 않는다
CREATE UNIQUE INDEX kill_log_field_ref_uq ON kill_log (field_session_id, character_id, monster_ref) WHERE monster_ref IS NOT NULL;

-- ---------- 4. 이상 기록 kind 확장 ----------

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse'));

-- ---------- 5. 중계 방 사용 요약 ----------

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
-- 일자별 합계(비용 점검), 특정 방 추적
CREATE INDEX relay_room_stats_ended ON relay_room_stats (ended_at);
CREATE INDEX relay_room_stats_room ON relay_room_stats (room_kind, room_ref);

-- ============ DOWN ============
-- 개발 DB 전용. kill_log 행의 필드 세션 표시가 사라지고(보상 자체는 원장에 그대로 남는다), 중계 요약이 지워진다. 운영에서는 실행하지 않는다.

DROP TABLE IF EXISTS relay_room_stats;

DELETE FROM anomaly_log WHERE kind IN ('field_uncredited', 'field_host', 'relay_abuse');
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter'));

DROP INDEX IF EXISTS kill_log_field_ref_uq;
ALTER TABLE kill_log DROP CONSTRAINT IF EXISTS kill_log_field_chk;
ALTER TABLE kill_log DROP COLUMN IF EXISTS xp_factor;
ALTER TABLE kill_log DROP COLUMN IF EXISTS monster_ref;
ALTER TABLE kill_log DROP COLUMN IF EXISTS field_session_id;

DROP TABLE IF EXISTS field_session_members;
DROP TABLE IF EXISTS field_sessions;

ALTER TABLE party_runs DROP COLUMN IF EXISTS transport_order;
ALTER TABLE party_runs DROP COLUMN IF EXISTS transport_switches;
ALTER TABLE party_runs DROP COLUMN IF EXISTS transport_epoch;
ALTER TABLE party_runs DROP COLUMN IF EXISTS transport;
