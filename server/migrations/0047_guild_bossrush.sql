-- 길드, 월드보스·길드보스 주간 도전, 랭킹 칭호(Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md).
-- 보스 전투는 각자 자기 방(레이드와 같은 방식)에서 3분 동안 하고, 서버는 끝 보고의 피해를 상한으로 잘라 모은다.
-- ============ UP ============
CREATE TABLE guilds (
  id                  BIGSERIAL PRIMARY KEY,
  uuid                UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  name                TEXT NOT NULL CHECK (char_length(name) BETWEEN 2 AND 12),
  notice              TEXT NOT NULL DEFAULT '' CHECK (char_length(notice) <= 60),
  join_mode           TEXT NOT NULL CHECK (join_mode IN ('open', 'approval')),
  leader_character_id BIGINT NOT NULL REFERENCES characters(id),
  member_count        INT NOT NULL DEFAULT 1 CHECK (member_count BETWEEN 0 AND 30),
  honor_color         TEXT CHECK (honor_color IN ('gold', 'silver')),
  honor_until         TIMESTAMPTZ,
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  disbanded_at        TIMESTAMPTZ,
  CHECK ((honor_color IS NULL) = (honor_until IS NULL))
);
CREATE UNIQUE INDEX guilds_name_alive ON guilds (lower(name)) WHERE disbanded_at IS NULL;
COMMENT ON TABLE guilds IS '길드. 해체하면 disbanded_at만 남기고 이름이 풀린다';
COMMENT ON COLUMN guilds.member_count IS '살아 있는 멤버 수(guild_members와 같은 트랜잭션에서 맞춘다). 상한 30';
COMMENT ON COLUMN guilds.honor_color IS '길드보스 주간 랭킹 꾸미기: 1위 gold, 2~10위 silver. honor_until까지';

CREATE TABLE guild_members (
  character_id BIGINT PRIMARY KEY REFERENCES characters(id),
  guild_id     BIGINT NOT NULL REFERENCES guilds(id),
  role         TEXT NOT NULL CHECK (role IN ('leader', 'officer', 'member')),
  joined_at    TIMESTAMPTZ NOT NULL
);
CREATE INDEX guild_members_guild ON guild_members (guild_id);
CREATE UNIQUE INDEX guild_members_one_leader ON guild_members (guild_id) WHERE role = 'leader';
COMMENT ON TABLE guild_members IS '캐릭터당 길드 하나(PK). 탈퇴·추방·해체 때 행을 지운다';

CREATE TABLE guild_applications (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  guild_id     BIGINT NOT NULL REFERENCES guilds(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  created_at   TIMESTAMPTZ NOT NULL,
  UNIQUE (guild_id, character_id)
);
CREATE INDEX guild_applications_char ON guild_applications (character_id);
COMMENT ON TABLE guild_applications IS '승인 대기 신청. 한 사람이 동시에 3곳까지. 가입·거절·길드 해체 때 지운다';

CREATE TABLE guild_cooldowns (
  character_id BIGINT PRIMARY KEY REFERENCES characters(id),
  until        TIMESTAMPTZ NOT NULL
);
COMMENT ON TABLE guild_cooldowns IS '탈퇴·추방 뒤 24시간 동안 다른 길드에 들어갈 수 없다(길드보스 기록 옮겨 다니기 방지)';

CREATE TABLE boss_seasons (
  id          BIGSERIAL PRIMARY KEY,
  uuid        UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  kind        TEXT NOT NULL CHECK (kind IN ('world', 'guild')),
  week_start  TIMESTAMPTZ NOT NULL,
  week_end    TIMESTAMPTZ NOT NULL,
  boss_id     TEXT NOT NULL,
  max_hp      BIGINT NOT NULL CHECK (max_hp > 0),
  hp_left     BIGINT NOT NULL CHECK (hp_left >= 0),
  defeated_at TIMESTAMPTZ,
  settled_at  TIMESTAMPTZ,
  UNIQUE (kind, week_start),
  CHECK (week_end > week_start)
);
COMMENT ON TABLE boss_seasons IS '주(목요일 06:00 KST 경계)마다 종류별 한 행. world는 서버 전체 공유 체력, guild는 1단계 체력 기준값(길드별 진행은 guild_boss_progress)';
COMMENT ON COLUMN boss_seasons.settled_at IS '주간 정산(우편·칭호)을 마친 시각. 행 잠금 + 이 값으로 한 번만 정산한다';

CREATE TABLE guild_boss_progress (
  season_id        BIGINT NOT NULL REFERENCES boss_seasons(id),
  guild_id         BIGINT NOT NULL REFERENCES guilds(id),
  stage            INT NOT NULL DEFAULT 1 CHECK (stage BETWEEN 1 AND 10),
  stage_hp         BIGINT NOT NULL CHECK (stage_hp > 0),
  hp_left          BIGINT NOT NULL CHECK (hp_left >= 0),
  total_damage     BIGINT NOT NULL DEFAULT 0 CHECK (total_damage >= 0),
  stage_reached_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (season_id, guild_id)
);
COMMENT ON TABLE guild_boss_progress IS '길드별 이번 주 길드보스. 단계 체력을 다 깎으면 다음 단계(체력 x1.5). 10단계가 끝';

CREATE TABLE boss_attempts (
  id              BIGSERIAL PRIMARY KEY,
  uuid            UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  season_id       BIGINT NOT NULL REFERENCES boss_seasons(id),
  kind            TEXT NOT NULL CHECK (kind IN ('world', 'guild')),
  guild_id        BIGINT REFERENCES guilds(id),
  reset_day       TIMESTAMPTZ NOT NULL,
  party_size      SMALLINT NOT NULL DEFAULT 1 CHECK (party_size BETWEEN 1 AND 4),
  state           TEXT NOT NULL DEFAULT 'open' CHECK (state IN ('open', 'finished', 'expired')),
  started_at      TIMESTAMPTZ NOT NULL,
  ends_at         TIMESTAMPTZ NOT NULL,
  finished_at     TIMESTAMPTZ,
  damage_reported BIGINT NOT NULL DEFAULT 0 CHECK (damage_reported >= 0),
  damage_accepted BIGINT NOT NULL DEFAULT 0 CHECK (damage_accepted >= 0),
  capped          BOOLEAN NOT NULL DEFAULT false,
  CHECK ((kind = 'guild') = (guild_id IS NOT NULL)),
  CHECK (damage_accepted <= damage_reported),
  CHECK ((state = 'open') = (finished_at IS NULL))
);
CREATE UNIQUE INDEX boss_attempts_one_open ON boss_attempts (character_id, kind) WHERE state = 'open';
CREATE INDEX boss_attempts_day ON boss_attempts (character_id, kind, reset_day);
CREATE INDEX boss_attempts_season ON boss_attempts (season_id, character_id);
COMMENT ON TABLE boss_attempts IS '보스 도전 한 번(시작 때 횟수 차감). 끝 보고는 한 번만 받고, 피해는 화력 상한으로 잘라 damage_accepted에 둔다';

CREATE TABLE rank_titles (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  season_id    BIGINT NOT NULL REFERENCES boss_seasons(id),
  title_id     TEXT NOT NULL CHECK (title_id IN ('rank_world_1', 'rank_world_10', 'rank_world_100', 'rank_guild_1')),
  name_color   TEXT CHECK (name_color IN ('gold', 'orange')),
  expires_at   TIMESTAMPTZ NOT NULL,
  UNIQUE (character_id, season_id, title_id)
);
CREATE INDEX rank_titles_char ON rank_titles (character_id, expires_at);
COMMENT ON TABLE rank_titles IS '주간 랭킹 칭호. 업적 칭호처럼 장착하고(characters.title_achievement에 title_id), 다음 정산까지 유효하다';

ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in', 'guild_create'));

ALTER TABLE mails DROP CONSTRAINT mails_system_code_check;
ALTER TABLE mails ADD CONSTRAINT mails_system_code_check
  CHECK (system_code IN ('compensation', 'event', 'refund', 'notice', 'maintenance', 'apology', 'attendance', 'other', 'world_boss', 'guild_boss'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution',
                  'sweep_denied', 'boss_damage'));

-- 길드 채팅: 보낼 때의 길드(guild_id). 놓친 메시지 따라잡기는 하지 않는다(접속 중인 길드원에게만 전달)
ALTER TABLE chat_messages ADD COLUMN guild_id BIGINT REFERENCES guilds(id);
ALTER TABLE chat_messages DROP CONSTRAINT chat_messages_channel_check;
ALTER TABLE chat_messages ADD CONSTRAINT chat_messages_channel_check CHECK (channel IN ('general', 'party', 'whisper', 'guild'));
ALTER TABLE chat_messages DROP CONSTRAINT chat_messages_shape_chk;
ALTER TABLE chat_messages ADD CONSTRAINT chat_messages_shape_chk CHECK (
  (channel = 'general' AND shard IS NOT NULL AND party_id IS NULL AND guild_id IS NULL
     AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
  OR (channel = 'party' AND party_id IS NOT NULL AND shard IS NULL AND guild_id IS NULL
     AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
  OR (channel = 'whisper' AND shard IS NULL AND party_id IS NULL AND guild_id IS NULL
     AND recipient_account_id IS NOT NULL AND recipient_character_id IS NOT NULL AND recipient_name IS NOT NULL)
  OR (channel = 'guild' AND guild_id IS NOT NULL AND shard IS NULL AND party_id IS NULL
     AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
);
ALTER TABLE report_lines DROP CONSTRAINT report_lines_channel_check;
ALTER TABLE report_lines ADD CONSTRAINT report_lines_channel_check CHECK (channel IN ('general', 'party', 'whisper', 'guild'));
-- ============ DOWN ============
-- 길드 채팅·보스 우편·길드 생성 원장이 생긴 뒤에는 되돌리지 않는다(원장은 추가만).
ALTER TABLE report_lines DROP CONSTRAINT report_lines_channel_check;
ALTER TABLE report_lines ADD CONSTRAINT report_lines_channel_check CHECK (channel IN ('general', 'party', 'whisper'));
ALTER TABLE chat_messages DROP CONSTRAINT chat_messages_shape_chk;
ALTER TABLE chat_messages ADD CONSTRAINT chat_messages_shape_chk CHECK (
  (channel = 'general' AND shard IS NOT NULL AND party_id IS NULL
     AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
  OR (channel = 'party' AND party_id IS NOT NULL AND shard IS NULL
     AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
  OR (channel = 'whisper' AND shard IS NULL AND party_id IS NULL
     AND recipient_account_id IS NOT NULL AND recipient_character_id IS NOT NULL AND recipient_name IS NOT NULL)
);
ALTER TABLE chat_messages DROP CONSTRAINT chat_messages_channel_check;
ALTER TABLE chat_messages ADD CONSTRAINT chat_messages_channel_check CHECK (channel IN ('general', 'party', 'whisper'));
ALTER TABLE chat_messages DROP COLUMN guild_id;
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution',
                  'sweep_denied'));
ALTER TABLE mails DROP CONSTRAINT mails_system_code_check;
ALTER TABLE mails ADD CONSTRAINT mails_system_code_check
  CHECK (system_code IN ('compensation', 'event', 'refund', 'notice', 'maintenance', 'apology', 'attendance', 'other'));
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in'));
DROP TABLE rank_titles;
DROP TABLE boss_attempts;
DROP TABLE guild_boss_progress;
DROP TABLE boss_seasons;
DROP TABLE guild_cooldowns;
DROP TABLE guild_applications;
DROP TABLE guild_members;
DROP TABLE guilds;
