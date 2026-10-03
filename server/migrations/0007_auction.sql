-- 0007_auction: 6단계(경매장 + 우편 + 시세) 테이블
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0005와 같다. 선행: 0005_party. (0006은 5단계 채팅·친구가 쓴다)
-- 설계: Docs/server/phase6_api.md, Docs/server/phase6_mapping.md   (초안: 구현 전에 사용자 결정 대기 항목을 확인한다)
--
-- 이 마이그레이션이 하는 일
--   1. 소지품 재고 키에 귀속(bind)을 넣는다: 같은 item_key라도 귀속이 다르면 다른 행(경매 구매 장비는 계정 귀속, 같은 키의 거래 가능 재고와 섞이면 안 된다).
--   2. 경매 등록(auction_listings), 입찰(auction_bids), 우편(mails), 체결(auction_trades), 일별 시세(auction_price_daily).
--   3. 골드 소각 기록(auction_sinks), 경매 악용 기록(auction_flags).
--   4. gold_ledger / item_ledger reason 확장, 우편·경매 원장의 "한 번만" 유니크 인덱스.
--
-- 만들지 않는 것
--   - character_items.location 'mail' / 'auction' 행. 보관 중인 아이템은 mails / auction_listings 행이 직접 들고 있다(보관자 하나, 소유 이전 없음).
--     원장의 location 'mail' / 'auction'은 그대로 쓰고, 이 두 위치의 정합 기준은 "원장 합 = 보관 행(mails.count 미수령분 / auction_listings.count 진행분)"이다.
--   - anomaly_log kind 확장. 5단계(0006)가 같은 CHECK를 건드릴 수 있어 충돌을 피하려고 경매는 auction_flags를 따로 쓴다.
--   - 플레이어끼리 보내는 우편 표. 우편은 서버만 만든다(캐릭터 간 골드·아이템 송금 우회로를 만들지 않는다).
--
-- 락 순서(경매): ① 요청자 캐릭터 행 하나(3단계 규칙) ② auction_listings 한 행. 판매자·직전 최고 입찰자 캐릭터 행은 잠그지 않는다(우편 INSERT만 한다).
--   마감 정산 틱은 auction_listings 한 행만 잠근다(캐릭터 행 없음). 그래서 한 요청이 두 listing을 잠그는 일이 없다.
-- 0006과의 관계: 0006이 gold_ledger / item_ledger의 reason CHECK를 바꾸지 않는다고 가정한다. 바꾸면 이 파일의 두 CHECK 목록을 합쳐 다시 쓴다.

-- ============ UP ============

-- ---------- 1. 소지품 재고 키에 귀속 추가 ----------
-- 코드 영향(3단계 구현): economyRepository.upsertStack의 ON CONFLICT 대상, stackCount/decStack(귀속 합산·강한 귀속부터 소모). Docs/server/phase6_api.md 13절.

DROP INDEX character_items_stack_uq;
CREATE UNIQUE INDEX character_items_stack_uq ON character_items (character_id, location, item_key, bind)
  WHERE location IN ('bag', 'storage');
COMMENT ON TABLE character_items IS '캐릭터 소지품. 변경은 item_ledger와 같은 트랜잭션에서만. bag·storage는 (키, 귀속)별 한 행(수량 합), worn은 슬롯별 한 행(count=1). 수량이 0이 되면 행을 지운다. 우편·경매 보관분은 이 표가 아니라 mails / auction_listings가 들고 있다';
COMMENT ON COLUMN character_items.bind IS 'none 거래 가능 / account 계정 귀속 / character 캐릭터 귀속. 획득 경로가 정한다(경매 구매 장비는 account). 강화·장착·창고 이동은 귀속을 그대로 가져간다';

-- ---------- 2. 원장 reason 확장 ----------

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

-- 같은 경매·우편 사건의 원장은 한 번만 (코드 버그가 있어도 DB가 이중 기록을 거절한다)
CREATE UNIQUE INDEX gold_ledger_auction_uq ON gold_ledger (reason, ref)
  WHERE reason IN ('auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim');
CREATE UNIQUE INDEX item_ledger_auction_uq ON item_ledger (reason, ref, location)
  WHERE reason IN ('auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire');

-- ---------- 3. 경매 등록 ----------

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
-- 마감 정산 틱(ends_at <= now)과 "남은 시간 짧은 순" 정렬
CREATE INDEX auction_listings_due ON auction_listings (ends_at) WHERE status = 'active';
-- 분류만 고르고 가격 순으로 훑는 기본 검색(정렬 기본값 가격 낮은 순)
CREATE INDEX auction_listings_browse ON auction_listings (category, buyout_price, id) WHERE status = 'active';
-- 이름 검색(item_base 목록) + 강화 범위 + 가격 순. 같은 아이템의 매물 비교가 가장 흔한 조회다
CREATE INDEX auction_listings_item ON auction_listings (item_base, enhance, buyout_price) WHERE status = 'active';
-- 내 등록(내 등록 탭, 동시 등록 수 상한 검사, 마감 즉시 정산 확인)
CREATE INDEX auction_listings_seller ON auction_listings (seller_character_id) WHERE status = 'active';
-- 내 입찰(내 등록 탭의 입찰 목록, 최고 입찰자 정산 확인)
CREATE INDEX auction_listings_bidder ON auction_listings (current_bidder_character_id) WHERE status = 'active' AND current_bidder_character_id IS NOT NULL;

-- ---------- 4. 입찰 ----------

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
-- 등록마다 최고 입찰은 하나
CREATE UNIQUE INDEX auction_bids_one_top ON auction_bids (listing_id) WHERE state = 'top';
-- 등록의 입찰 내역(CS), 한 캐릭터의 입찰 이력
CREATE INDEX auction_bids_listing ON auction_bids (listing_id, id);
CREATE INDEX auction_bids_bidder ON auction_bids (bidder_character_id, id);

-- ---------- 5. 우편 ----------

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
-- 받을 우편 목록·개수·요약(캐릭터별, 최신순). 수령·폐기된 우편은 인덱스에서 빠진다
CREATE INDEX mails_open ON mails (character_id, created_at DESC) WHERE claimed_at IS NULL AND expired_at IS NULL;
-- 기한 폐기 청소(틱)
CREATE INDEX mails_expiring ON mails (expires_at) WHERE claimed_at IS NULL AND expired_at IS NULL;
-- 정산이 두 번 돌아도 같은 사건의 우편은 한 통(코드 버그가 있어도 DB가 거절). outbid는 입찰마다 한 통
CREATE UNIQUE INDEX mails_once_per_event ON mails (listing_id, character_id, kind)
  WHERE listing_id IS NOT NULL AND kind <> 'outbid';
CREATE UNIQUE INDEX mails_outbid_per_bid ON mails (bid_id) WHERE kind = 'outbid';

-- ---------- 6. 체결 기록, 일별 시세 ----------

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
-- 아이템 키별 최근 7일 중앙값·최저·최고(가격 한도 계산, 시세 응답). 키 하나의 최근 체결만 읽는다
CREATE INDEX auction_trades_item_time ON auction_trades (item_key, traded_at DESC);
-- 같은 판매자-구매자 계정 쌍의 오늘 체결 수·금액(쌍 한도), 중앙값의 쌍 중복 제한
CREATE INDEX auction_trades_pair ON auction_trades (seller_account_id, buyer_account_id, traded_at);
-- 보관 기간 정리
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
-- 시세 응답: PK 접두 (item_key, 최근 14일)로 충분하다(별도 인덱스 없음)

-- ---------- 7. 골드 소각 기록, 악용 기록 ----------

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
-- 일일·종류별 소각량 집계
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

-- ============ DOWN ============
-- 개발 DB 전용. 경매 원장 행을 지우므로 잔액과 원장이 어긋난다. 운영에서는 실행하지 않는다.
-- 귀속이 다른 같은 키 재고(account 귀속 장비)가 있으면 첫 인덱스 복원이 실패한다: 먼저 합쳐야 한다(아래 UPDATE).

DROP TABLE IF EXISTS auction_flags;
DROP TABLE IF EXISTS auction_sinks;
DROP TABLE IF EXISTS auction_price_daily;
DROP TABLE IF EXISTS auction_trades;
DROP TABLE IF EXISTS mails;
DROP TABLE IF EXISTS auction_bids;
DROP TABLE IF EXISTS auction_listings;

DROP INDEX IF EXISTS item_ledger_auction_uq;
DROP INDEX IF EXISTS gold_ledger_auction_uq;

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason IN ('auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost'));

ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason IN ('auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim');
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card'));

-- 귀속만 다른 같은 키 행을 하나로 합친 뒤(남는 행의 귀속은 가장 강한 쪽) 원래 인덱스로 되돌린다
WITH merged AS (
  SELECT character_id, location, item_key, SUM(count)::int AS total,
         MIN(id) AS keep_id,
         (ARRAY_AGG(bind ORDER BY CASE bind WHEN 'character' THEN 0 WHEN 'account' THEN 1 ELSE 2 END))[1] AS strongest
    FROM character_items
   WHERE location IN ('bag', 'storage')
   GROUP BY character_id, location, item_key
  HAVING COUNT(*) > 1
)
UPDATE character_items ci SET count = m.total, bind = m.strongest
  FROM merged m WHERE ci.id = m.keep_id;
DELETE FROM character_items ci
 USING (SELECT character_id, location, item_key, MIN(id) AS keep_id
          FROM character_items WHERE location IN ('bag', 'storage')
         GROUP BY character_id, location, item_key HAVING COUNT(*) > 1) d
 WHERE ci.character_id = d.character_id AND ci.location = d.location AND ci.item_key = d.item_key AND ci.id <> d.keep_id;
DROP INDEX IF EXISTS character_items_stack_uq;
CREATE UNIQUE INDEX character_items_stack_uq ON character_items (character_id, location, item_key)
  WHERE location IN ('bag', 'storage');
COMMENT ON COLUMN character_items.bind IS NULL;
