# 서버 6단계 API 명세 (경매장, 우편, 시세)

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §9 6단계(등록·입찰·즉시 구매·정산·우편·시세, 끝났다는 기준 = PLAN_AUCTION 시나리오 테스트 통과, 동시 입찰 포함), [PLAN_AUCTION.md](../PLAN_AUCTION.md) v1.0. 앞 단계: [phase3_api.md](phase3_api.md)(원장·멱등성·락·`delta`), [phase4_api.md](phase4_api.md)(락 순서, 지연 전이, 속도 제한). 스키마: `server/migrations/0007_auction.sql`(초안, 5단계는 `0006`), `server/schema.sql`. 게임 값·C# 대응: [phase6_mapping.md](phase6_mapping.md).
이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만든다. 게임 값(수수료율·보증금·기간·가격 한도 비율)은 문서에 복사하지 않고 `server/data/auction.json`(신규 내보내기, mapping 3절)의 키 이름으로만 참조한다. 본문의 수치는 서버 정책 상수(3절, 환경변수)이거나 계산 예시다.

## 0. 핵심 설계 (먼저 읽기)

1. **클라이언트가 보내는 값은 "무엇을 하겠다"뿐이다.** 등록 요청의 가격·입찰 요청의 금액은 플레이어의 호가(시장 가격)이지 보상량이 아니다. 서버는 이 값을 믿지 않고 범위(가격 한도·최소 입찰가), 잔액, 귀속, 소유를 다시 확인한다. 보증금·수수료·예치·정산액·마감 시각·귀속·분류·등급은 요청에 없고 서버가 정한다(요청 `.strict()`). 등록 가격을 바꾸는 API는 만들지 않는다(구매 확인 창의 가격이 확인 후 바뀌는 낚시를 원천 차단).
2. **보관자가 하나다.** 등록한 아이템은 `auction_listings` 행이, 우편 첨부는 `mails` 행이 직접 들고 있다. `character_items`에 `auction`/`mail` 행을 만들지 않는다(소유 이전·복제 경로 제거). 입찰 예치금은 `auction_listings.current_bid`가, 우편 골드는 `mails.gold`가 들고 있다. 어느 순간에도 골드·아이템은 정확히 한 곳에 있다(9.3의 보존식).
3. **정산은 락 한 개다.** 구매·입찰은 `요청자 캐릭터 행 -> auction_listings 한 행` 순서로 잠근다. 판매자와 직전 입찰자의 캐릭터 행은 잠그지 않는다(결과는 전부 우편 INSERT). 정산 틱은 listing 한 행만 잠근다. 두 요청이 서로 다른 순서로 두 행을 잠그는 일이 없어 교착이 없다(8절).
4. **결과는 우편으로만 전달된다.** 즉시 구매든 낙찰이든 만료든 구매자·판매자·밀려난 입찰자는 우편을 받고, 수령할 때 비로소 가방·골드에 들어온다. 가방이 가득 차도 잃지 않는다는 PLAN 목표와 같고, 서버 구현에서는 "정산 트랜잭션이 캐릭터 잔액을 건드리지 않는다"는 장점이 있다.
5. **서버 틱 + 요청 시 지연 정산.** 1분 틱이 마감된 등록을 정산하고, 내 등록·내 입찰·우편 조회 요청이 닿으면 내 것 중 마감된 건을 먼저 정산해 틱을 기다리지 않게 한다(phase 4의 지연 전이 방식).
6. **WebSocket 없이도 완결된다.** 우편함 빨간 점·판매 알림은 `GET /mail/summary` 폴링으로 되고, 5단계 WebSocket은 12절의 `NotificationPublisher` 인터페이스에 붙어 같은 사건을 푸시한다. 사건의 정본은 `mails` 표다.
7. **귀속은 재고의 키에 들어간다.** 같은 `item_key`라도 귀속이 다르면 다른 재고 행이다(`character_items_stack_uq`에 `bind` 추가). 경매로 산 장비는 계정 귀속 행이 되어 거래 가능 재고와 섞이지 않는다(5절). 3단계 구현에 닿는 변경이라 13절에 목록으로 둔다.

## 1. 공통 규칙

3단계 0.1~0.6(응답 형식 `{ success, message, data, meta? }`, 멱등성, 락, `delta`, 날짜 경계, RNG, 속도 제한)과 1~2단계 0.2~0.3(인증, 버전 헤더)을 그대로 쓴다. 경로는 3·4단계와 같이 `/characters/{uuid}/...` 아래다(PLAN_AUCTION §7.3의 `/auction/...`, `/mail`을 캐릭터 경로 아래로 옮긴 것이다. 어느 캐릭터가 행동하는지가 필요하고, 클라이언트·데이터 버전을 둘 다 검사하기 위해서다. 가격·수수료 데이터가 다르면 판정을 시작하지 않는다). 응답에는 uuid만 싣고 내부 bigint 키는 싣지 않는다.

- **멱등성**: 상태를 바꾸는 `POST`는 본문 `request_id`(uuid)를 받고 3단계 `request_log` 규칙을 쓴다. PLAN_AUCTION §7.1의 `Idempotency-Key` 헤더는 프로젝트 규칙(본문 `request_id`)으로 대체한다. `DELETE`(등록 취소)는 상태 기반 멱등이다(5.5절).
- **시각**: 서버 시계만 쓴다. 마감 비교는 **락을 잡은 뒤의 시각**(`clock_timestamp()` 또는 락 직후 읽은 서버 시각)으로 한다. 트랜잭션 시작 시각(`now()`)은 락 대기 중에 낡는다. 일일 경계(시세의 하루)는 `resetBoundaries().dailyStartAt`(phase 3 0.4) 하나만 쓴다.
- **락 이후 재조회**: `auction_listings`를 `FOR UPDATE`로 잠근 뒤 상태·가격·입찰자를 다시 읽고 그 값으로만 판정한다. 잠그기 전에 읽은 값(검색 결과 등)으로 판정하지 않는다.
- **공통 에러**: `400 VALIDATION`, `401 TOKEN_*`, `403 ACCOUNT_BANNED`, `404 CHARACTER_NOT_FOUND`, `422 IDEMPOTENCY_MISMATCH`, `426`, `429 RATE_LIMITED`. 규칙 위반은 `422`, 경쟁으로 상태가 바뀐 결과는 `409`.

### 1.1 경매 경로의 에러 코드

| code | 상태 | 뜻 |
|---|---|---|
| `LISTING_NOT_FOUND` | 404 | 없는 등록, 또는 남의 등록을 취소하려 함(구별하지 않는다) |
| `LISTING_NOT_ACTIVE` | 409 | 이미 팔렸거나 취소·만료됨 |
| `LISTING_ENDED` | 409 | 마감 시각이 지났다(정산 틱 전이라도 거절) |
| `OWN_LISTING` | 403 | 내 계정의 등록(같은 계정 다른 캐릭터 포함)은 사거나 입찰할 수 없다 |
| `PAIR_LIMIT` | 422 | 같은 판매자-구매자 계정 쌍의 오늘 체결 한도 초과(`errors.limit`) |
| `NOT_BIDDABLE` | 422 | 즉시 구매만 가능한 등록 |
| `ALREADY_TOP_BIDDER` | 409 | 내가 이미 최고 입찰자 |
| `BID_TOO_LOW` | 409 | 최소 입찰가 미달(`errors.min_bid`; 그 사이 다른 입찰이 들어온 경우 포함) |
| `NOT_ENOUGH_GOLD` | 422 | 골드 부족(`errors.need`, `errors.have`, 보증금이면 `errors.for="deposit"`) |
| `ITEM_UNKNOWN` | 404 | 모르는 아이템 키 |
| `NOT_TRADABLE` | 422 | 거래 불가 종류(골드, 시작 장비, 상점 기준가가 없는 아이템). `errors.reason` |
| `ITEM_BOUND` | 422 | 귀속된 재고뿐이다(캐릭터·계정 귀속) |
| `NOT_ENOUGH_ITEMS` | 422 | 가방에 거래 가능 재고가 모자라다(3단계와 같은 코드) |
| `BAD_COUNT` | 422 | 장비는 1개, 묶음은 최대 묶음 이하(`errors.max`) |
| `BAD_DURATION` | 422 | `auction.json.durations`에 없는 기간 |
| `LISTING_LIMIT` | 409 | 동시 등록 한도(`errors.limit`) |
| `LISTING_GATE` | 422 | 신규 계정·낮은 레벨의 등록 제한(`errors.need_level`, `errors.need_days`) |
| `PRICE_OUT_OF_RANGE` | 422 | 가격 한도 밖(`errors.min`, `errors.max`, `errors.source`) |
| `START_BID_INVALID` | 422 | 입찰 시작가가 즉시 구매가 이상이거나 한도 밖 |
| `HAS_BIDS` | 409 | 입찰자가 있어 취소할 수 없다 |
| `MAIL_NOT_FOUND` | 404 | 없거나 내 우편이 아님(구별하지 않는다) |
| `MAIL_ALREADY_CLAIMED` | 409 | 이미 받음 |
| `MAIL_EXPIRED` | 410 | 보관 기한이 지나 폐기됨 |
| `GOLD_CAP_EXCEEDED` | 422 | 수령하면 골드가 클라이언트 정수 상한을 넘는다(우편은 그대로 남는다) |
| `CHARACTER_HAS_AUCTION` | 409 | (2단계 `DELETE /characters/{uuid}`) 진행 중 등록·최고 입찰·미수령 우편이 있어 삭제 불가 |

## 2. 엔드포인트 요약 (신규 12개 + 확장 2개)

경로는 `/characters/{uuid}` 아래(표에서 생략), 인증은 액세스 토큰이다.

| # | 메서드 | 경로 | 하는 일 |
|---|---|---|---|
| A1 | GET | `/auction/search` | 검색(필터·정렬·페이지) |
| A2 | GET | `/auction/prices/{item_key}` | 시세·가격 한도 |
| A3 | GET | `/auction/sellable` | 내 가방의 등록 후보와 등록 자격 |
| A4 | POST | `/auction/listings` | 등록 |
| A5 | DELETE | `/auction/listings/{listing_id}` | 등록 취소(입찰 없을 때) |
| A6 | POST | `/auction/listings/{listing_id}/buyout` | 즉시 구매 |
| A7 | POST | `/auction/listings/{listing_id}/bids` | 입찰 |
| A8 | GET | `/auction/mine` | 내 등록, 내 최고 입찰 |
| M1 | GET | `/mail` | 받을 우편 목록 |
| M2 | POST | `/mail/{mail_id}/claim` | 우편 한 통 수령 |
| M3 | POST | `/mail/claim-all` | 모두 받기(최대 50통) |
| M4 | GET | `/mail/summary` | 빨간 점·새 우편 폴링 |

확장 2개: `DELETE /characters/{uuid}`(진행 중 경매·미수령 우편이 있으면 `409 CHARACTER_HAS_AUCTION`), 3단계 `delta.stacks[]`에 `bind` 필드 추가(13절).

## 3. 데이터 출처와 정책 상수

게임 값은 `server/data/auction.json`(Unity `AuctionRules`에서 내보냄)과 기존 파일에서 읽는다. 값을 이 문서에 복사하지 않는다.

| 필요한 값 | 출처 |
|---|---|
| 기간 목록, 수수료율(장비 / 비장비), 보증금 비율·최소·최대, 동시 등록 수, 묶음 최대, 최소 입찰 증가율, 가격 한도 비율(하한·상한), 마감 연장(구간·분·최대 횟수), 우편 보관 일수, 남은 시간 구간 | `auction.json`(신규, 정수 단위: 비율은 basis point) |
| 아이템 종류(equipment / material / consumable / world), 기본 귀속(하한) | `items.json` |
| 장비 분류·등급·직업 제한·판매가, 강화 판매 보너스 | `shop.json` `equipment[]`, `enhancedSellBonusPerLevel` |
| 재료·소모품 판매가·구매가 | `shop.json` `materials[]`, `sellPrices[]`, `stock[]` |
| 아이템 이름(검색) | `items.json`에 `name` 추가(mapping 3절) |
| 최대 강화 | `enhance.json` `maxEnhance` |

**정수 계산 원칙**: 서버는 금액·비율을 전부 정수(BigInt / SQL bigint)로 계산한다. C#의 float 연산(`0.01f`, `1.05f`)은 쓰지 않는다. 수수료 `ceil(가격 x 수수료율 / 100)` = `(가격 x 율 + 99) / 100`(정수 나눗셈), 보증금 `clamp(round_half_up(구매가 x bps / 10000), 최소, 최대)`, 최소 입찰 `ceil(현재가 x bps / 10000)`. C# 쪽도 같은 정수식으로 맞춘다(mapping 4절).

**서버 정책 상수** (환경변수, 코드에 흩어 두지 않는다. 방어·운영 값이고 시작값은 근거 예시다):

| 이름 | 시작값(예) | 의미 |
|---|---|---|
| `AUCTION_TICK_SECONDS` | 60 | 마감 정산 틱 주기 |
| `AUCTION_TICK_BATCH` | 100 | 틱 한 번에 정산하는 최대 건수 |
| `AUCTION_MIN_LEVEL` / `AUCTION_MIN_ACCOUNT_AGE_DAYS` | 10 / 7 | 등록 자격(PLAN §8. 둘 중 하나라도 미달이면 불가, 구매·입찰은 가능). 개발 환경에서는 0으로 내릴 수 있고 `NODE_ENV=production`에서 0이면 기동을 거부 |
| `AUCTION_MAX_PRICE` | 1,000,000,000 | 즉시 구매가·입찰가 상한(클라이언트 `Inventory` 수량이 `int`) |
| `GOLD_CLIENT_MAX` | 2,147,483,647 | 우편 수령 후 골드 상한(넘으면 `GOLD_CAP_EXCEEDED`) |
| `AUCTION_REF_WINDOW_DAYS` | 7 | 시세 기준 기간 |
| `AUCTION_REF_MIN_TRADES` / `AUCTION_REF_MIN_BUYERS` | 5 / 3 | 체결 기록 시세를 믿기 위한 최소 건수·서로 다른 구매 계정 수 |
| `AUCTION_REF_PAIR_MAX` | 2 | 시세에 반영하는 같은 계정 쌍의 최근 체결 수 |
| `AUCTION_COLD_FLOOR_MULT` / `AUCTION_COLD_CEIL_MULT` | 1 / 100 | 기록이 없을 때 가격 한도 = 상점 기준가 x 배율(PLAN §8 "1배 ~ 100배") |
| `AUCTION_PAIR_DAILY_TRADES` / `AUCTION_PAIR_DAILY_GOLD` | 3 / (시세를 본 뒤 정한다) | 같은 판매자-구매자 계정 쌍이 하루(게임 일)에 체결할 수 있는 건수·금액 합 |
| `MAIL_CLAIM_ALL_MAX` | 50 | 모두 받기 한 번의 최대 통수 |
| 보관 | trades·종료된 등록·입찰·수령/폐기 우편 180일, flags 90일 | 정리 스크립트(7단계에서 정식화) |

### 3.1 속도 제한 (메모리, `RateLimitStore` 인터페이스 뒤, 캐릭터당. 별도 표기만 계정당)

3·4단계처럼 IP 한도(600/분)를 상속한다.

| 엔드포인트 | 한도 | 근거 |
|---|---|---|
| A1 검색 | **계정당** 초당 2회 | PLAN §4 |
| A2 시세, A3 후보 | 초당 5회 / 초당 1회 | 확인 창·등록 창 열 때 1회 |
| A4 등록 | 분당 10건 + 초당 1회 | PLAN §8 도배 방지 |
| A5 취소, A6 구매, A7 입찰 | 초당 2회 | 사람이 누르는 속도, 입찰 연타 방지 |
| A8 내 등록, M1 우편 목록 | 초당 1회 | 탭 전환 |
| M2 수령, M3 모두 받기 | 초당 5회 / 초당 1회 | |
| M4 요약 | 5초에 1회 | 클라이언트는 창이 열려 있으면 15초, 닫혀 있으면 60초마다 |

## 4. 테이블 개요와 인덱스 이유 (SQL은 `0007_auction.sql`)

| 테이블 | 역할 | 비고 |
|---|---|---|
| `auction_listings` | 등록 한 건. 진행 중에는 아이템을 직접 보관(원장 location `auction`) | 상태 active / sold / expired / cancelled. 검색 열(`item_base`, `enhance`, `category`, `rarity`, `class_only`)은 등록 때 서버가 복사 |
| `auction_bids` | 입찰 기록. 예치 금액의 상태(top / outbid / won / lost_to_buyout) | `UNIQUE(listing_id, amount)`, `auction_bids_one_top` |
| `mails` | 우편. 첨부 아이템·골드를 직접 보관(원장 location `mail`) | 서버만 만든다. 30일 후 폐기 |
| `auction_trades` | 체결 기록(추가만) | `buyer_account_id <> seller_account_id` CHECK, 수수료·정산액 산식 CHECK |
| `auction_price_daily` | 키별 일별 시세(체결 트랜잭션에서 누적) | PK `(item_key, day_start)` |
| `auction_sinks` | 소각된 골드(수수료, 몰수 보증금, 폐기된 우편 골드). 추가만 | 9절 |
| `auction_flags` | 경매 악용 의심 기록. 롤백되지 않도록 별도 트랜잭션 | `anomaly_log` 대신 전용(5단계 `0006`과 CHECK 목록이 충돌하지 않게) |
| `gold_ledger`, `item_ledger` | reason 확장 + 경매·우편 "한 번만" 유니크 | `gold_ledger_auction_uq`, `item_ledger_auction_uq` |
| `character_items` | `character_items_stack_uq`가 `(character_id, location, item_key, bind)`로 바뀜 | 5절, 13절 |

**인덱스와 조회 패턴**

| 인덱스 | 조회 패턴 | 이유 |
|---|---|---|
| `auction_listings_due (ends_at) WHERE status='active'` | 정산 틱 `ends_at <= now`, 정렬 "남은 시간 짧은 순" | 부분 인덱스라 진행 중인 건만 담는다. 틱은 1분마다 맨 앞 몇 건만 읽는다 |
| `auction_listings_browse (category, buyout_price, id) WHERE active` | 분류만 고르고 기본 정렬(가격 낮은 순) | 가장 흔한 첫 화면 |
| `auction_listings_item (item_base, enhance, buyout_price) WHERE active` | 이름 검색(item_base 목록) + 강화 범위 + 가격 순 | 같은 아이템의 매물을 가격 순으로 비교하는 조회 |
| `auction_listings_seller (seller_character_id) WHERE active` | 내 등록 탭, 동시 등록 수(`COUNT`), 마감 지연 정산 | 캐릭터당 20건 이하라 작다 |
| `auction_listings_bidder (current_bidder_character_id) WHERE active AND NOT NULL` | 내 최고 입찰 목록, 낙찰 지연 정산 | |
| `auction_bids_listing (listing_id, id)`, `auction_bids_bidder` | 입찰 내역(CS), 한 캐릭터의 이력 | 자주 읽지 않아 최소로 둔다 |
| `mails_open (character_id, created_at DESC) WHERE 미수령` | 우편 목록·개수·요약 | 수령·폐기된 우편이 인덱스에서 빠져 항상 작다 |
| `mails_expiring (expires_at) WHERE 미수령` | 기한 폐기 청소 | 틱이 `expires_at <= now`를 읽는다 |
| `mails_once_per_event`, `mails_outbid_per_bid` | (조회 아님) 같은 사건의 우편은 한 통 | 정산이 두 번 돌아도 DB가 거절 |
| `auction_trades_item_time (item_key, traded_at DESC)` | 7일 중앙값·최저·최고(가격 한도, 시세) | 키 하나의 최근 체결만 읽는다 |
| `auction_trades_pair (seller_account_id, buyer_account_id, traded_at)` | 쌍 한도(오늘 건수·금액), 시세의 쌍 중복 제한 | |
| `auction_trades_time`, `auction_sinks_time`, `auction_flags_*` | 정리, 소각량 집계, 계정별 의심 이력 | |

100명 미만에서 등록은 수천 건 이하라 대부분의 검색은 어떤 인덱스든 빠르다. 위 인덱스는 "확장 1" 단계의 수만 건에서도 검색이 정렬·필터를 인덱스로 끝내도록 미리 둔 최소 집합이다. `meta.total`은 `COUNT(*)`로 구하고, 규모가 커지면 상한(예: 1,000)으로 자른다.

## 5. 귀속(bind)과 거래 가능 판정

### 5.1 규칙
`character_items.bind`는 행의 속성이고 **획득 경로가 정한다**(PLAN_AUCTION §6). 아이템 종류가 정하는 하한(`items.json`의 `bind`, 시작 장비·장비 보호권은 `character`)은 어떤 경로로도 풀리지 않고, 경로 규칙은 그 위에 더 강하게만 덮는다(강도 `none < account < character`).

| 획득 경로 | 장비의 귀속 | 비고 |
|---|---|---|
| 몬스터 드롭, 던전 카드, 채집, 상자 | `none`(거래 가능) | 파밍 결과가 경매의 중심 |
| 퀘스트 보상(`quest_reward`) | `character` | 진행 보상은 거래 불가(PLAN §6, 결정 대기 1) |
| 상점 구매(`shop_buy`) | `account` | 현재 상점은 장비를 팔지 않는다(`shop.json.stock`에 없다). 판매하게 되면 적용 |
| 경매 구매(우편 수령) | `account` | 거래 횟수 1회 규칙. **장비만**(재료·소모품은 그대로 `none`) |
| 강화 결과 | 소모한 행의 귀속을 그대로 | 강화해도 귀속 유지, 파괴·+0 초기화도 동일 |
| 장착·해제·창고 이동 | 행의 귀속을 그대로 | |

### 5.2 거래 가능 판정 (`listable`)
등록 대상은 **가방(`bag`)의 `bind='none'` 재고**뿐이다. 착용 중인 장비(`worn`), 창고(`storage`)는 등록 대상이 아니다(창고는 꺼낸 뒤 등록). 아래를 순서대로 검사하고 첫 실패에서 중단한다.
1. 키가 `items.json`에 있다(`ITEM_UNKNOWN`), 종류가 `currency`가 아니다.
2. 기본 귀속이 `none`이다(시작 장비·장비 보호권 제외, `NOT_TRADABLE`/`reason=BASE_BOUND`).
3. 가방에 그 키의 `bind='none'` 행이 있다. 귀속 행만 있으면 `ITEM_BOUND`.
4. 가격 기준이 있다(상점 기준가 또는 체결 기록, 6.1절). 없으면 `NOT_TRADABLE`/`reason=NO_PRICE`(예: 상점 판매가가 없는 레이드 봉인 열쇠. 결정 대기 4).

**재고 키가 `(키, 귀속)`이 되는 결과**: 같은 키가 `none`과 `account`로 동시에 있을 수 있다. 소모 대상을 키만으로 가리키는 3단계 API(판매·강화·장착·창고)는 **귀속이 강한 쪽부터 소모**한다(거래 가능 재고를 남기는 쪽이 플레이어에게 유리). `GET /characters/{uuid}`의 `items[]`는 이미 `bind`를 가진다.

## 6. 시세와 가격 한도

### 6.1 기준가 `reference(item_key, count)`
등록 가격 한도와 시세 응답이 같은 함수를 쓴다.
1. **체결 기록 기준** (조건: 최근 `AUCTION_REF_WINDOW_DAYS`일 체결 중 쌍 중복 제한 후 `AUCTION_REF_MIN_TRADES`건 이상, 서로 다른 구매 계정 `AUCTION_REF_MIN_BUYERS`명 이상): **개당 가격의 중앙값**(`percentile_cont(0.5)` over `auction_trades.unit_price`). 쌍 중복 제한 = 같은 `(seller_account_id, buyer_account_id)`의 최근 체결을 `AUCTION_REF_PAIR_MAX`건까지만 센다(`ROW_NUMBER() OVER (PARTITION BY 쌍 ORDER BY traded_at DESC)`). 평균이 아니라 중앙값을 쓰는 이유: 두 계정이 극단 가격으로 몇 번 거래해도 기준이 움직이지 않는다(PLAN §8의 "7일 평균"을 조작에 강한 값으로 대체. 응답의 `avg7d`는 표시용 평균을 따로 준다).
2. **콜드 스타트** (기록이 모자랄 때): 상점 기준가 `vendor_unit`. 장비는 `round_half_away(sellPrice x (1 + enhancedSellBonusPerLevel x 강화 단계))`(3단계 상점 판매가 공식과 같다), 재료·소모품은 `sellPrices[]`/`materials[]`의 판매가와 `stock[]`의 구매가 중 큰 값. 한도 = 하한 `vendor_unit x AUCTION_COLD_FLOOR_MULT`, 상한 `vendor_unit x AUCTION_COLD_CEIL_MULT`. `vendor_unit`이 없거나 0이면 기준 없음(`NO_PRICE`, 등록 불가).
3. **한도 계산**: 묶음 `count`개의 총 한도 = 기준 개당 가격 x `count`에 비율을 곱해 정수로 자른다. 하한은 올림, 상한은 내림. 체결 기준이면 하한 `priceFloorBps`, 상한 `priceCeilBps`(`auction.json`, PLAN의 20%~500%).

### 6.2 시세 집계
`auction_price_daily`는 **체결 트랜잭션이 같은 문장으로 누적(upsert)** 한다: `ON CONFLICT (item_key, day_start) DO UPDATE SET trade_count+1, volume+count, sum_price+price, min/max 갱신`. PLAN §5의 "하루 한 번 06:00 확정, 오늘은 근사"는 쓰지 않는다: 누적이 정확하고 별도 일일 작업이 필요 없다(`day_start`는 `resetBoundaries(체결 시각).dailyStartAt`이라 06:00 경계가 그대로 지켜진다). 같은 행에 체결이 몰려도 행 잠금은 그 트랜잭션 끝까지이고 한 트랜잭션은 그 행 하나만 더 만지므로 교착이 없다(8.1).

## 7. 엔드포인트 상세

경로 접두 `/characters/{uuid}`는 생략한다.

### 7.1 A1 GET /auction/search
- 쿼리(zod `.strict()`):
```
q?: string(1..20)                       // 이름 부분 일치. 한글 자음(ㄱ-ㅎ)만으로 이루어졌으면 초성 검색
category?: "all"|"weapon"|"armor"|"accessory"|"material"|"consumable"   = "all"
rarity?: "0,2,3"  (콤마 구분 0..5, 최대 6개)                               // 장비만 대상. 비장비는 결과에서 빠진다
enh_min?: int(0..), enh_max?: int(0..)  = 0 .. enhance.maxEnhance          // refine: min <= max
price_min?: int(0..), price_max?: int   // 즉시 구매가(묶음 전체) 범위
class?: "mine"|"any"                    = "any"                            // mine: 내 직업이 쓸 수 있는 장비(class_only가 NULL이거나 같음) + 비장비
sort?: "price_asc"|"price_desc"|"unit_price_asc"|"time_left"|"enhance_desc"|"newest"  = "price_asc"
page?: int(1..)=1, limit?: int(1..20)=20
```
- 대상: `status='active' AND ends_at > 지금 AND seller_account_id <> 내 계정`. **내 계정의 매물은 검색에서 빠진다**(같은 계정 다른 캐릭터 것 포함. 못 사는 물건이 목록에 있는 혼란을 없앤다. 내 캐릭터의 등록은 A8로 본다).
- 이름 검색: 서버가 메모리의 아이템 이름표(`items.json.name`)에서 `q`와 맞는 `item_base` 목록을 만들고 `item_base = ANY($)`로 거른다. 맞는 이름이 없으면 질의 없이 빈 결과.
- 정렬 동률은 `id`(최근 등록 순은 `id DESC`). `time_left`는 `ends_at` 오름차순(구간만 보이지만 정렬은 실제 값).
- 응답 `200` `data`: `{ "listings": [ListingView] }`, `meta`: `{ total, page, limit }`.
```
ListingView = {
  "id": uuid, "item_key": "eq_sword_iron+7", "count": 1, "seller_name": "달빛마녀",
  "category": "weapon", "rarity": 1 | null, "enhance": 7, "class_only": "warrior" | null,
  "buyout": 12000, "start_bid": 7200 | null,           // null = 즉시 구매만
  "current_bid": 7560 | null, "bid_count": 1,
  "min_bid": 7938 | null,                               // 다음 최소 입찰가(서버 계산, 즉시 구매가 이상이면 즉시 구매가로 자른다). null = 입찰 불가
  "unit_price": 12000.0,                                // buyout / count
  "hours_left_band": 12,                                // 1 | 6 | 12 | 24 | 48 (미만 구간). 정확한 시각은 주지 않는다
  "my_bid": false,                                      // 내 캐릭터가 최고 입찰자
  "will_bind": "account" | null                         // 사면 귀속되는가(장비)
}
```
- 에러: `400 VALIDATION`, `429`. 멱등성: 읽기라 해당 없음.

### 7.2 A2 GET /auction/prices/{item_key}
- 경로 `item_key`는 `ITEM_KEY_RE`(`itemKey.ts`)로 검증한다(`+`는 경로에서 리터럴이다). 쿼리 `count?: int(1..9999)=1`.
- 응답 `200` `data`:
```
{ "item_key": "eq_sword_iron+7", "count": 1,
  "source": "history" | "vendor" | "none",             // 한도 기준(6.1)
  "avg7d": 11800 | null, "min": 9000 | null, "max": 14000 | null,   // 최근 7 게임 일의 체결 합계 기준, count개 총액으로 환산(정수, 개당 평균에 count를 곱한 뒤 half-away 반올림)
  "unit_avg": 11800.0 | null, "volume": 14,                          // volume = 7일간 거래된 개수
  "limits": { "min": 2400, "max": 59000 } | null,                    // 등록 가능한 즉시 구매가(count개 총액). null = 등록 불가(NO_PRICE)
  "daily": [ { "day": ISO, "avg": 11700.0, "volume": 3 } ] }         // 최근 14 게임 일, 체결 없는 날은 생략, 오래된 순
```
- 총액으로 환산해 주는 이유: 싼 재료는 개당 평균을 정수로 줄이면 묶음 총액이 어긋난다(mapping의 `AuctionPrice`). 구매 확인 창의 "평균 대비 +35%"는 클라이언트가 `avg7d`와 `buyout`으로 계산한다.
- 에러: `404 ITEM_UNKNOWN`, `400`, `429`. 서버는 키별 응답을 60초 메모리 캐시할 수 있다(읽기 전용, 체결이 많지 않음).

### 7.3 A3 GET /auction/sellable
내 가방의 재고를 귀속별로 주고 등록 자격을 알린다. 클라이언트가 귀속·가격 규칙을 따로 구현하지 않게 한다.
- 응답 `200` `data`:
```
{ "gate": { "can_list": true, "need_level": 10, "need_days": 7, "active": 3, "max_listings": 20 },
  "items": [ { "item_key": "eq_sword_iron+7", "bind": "none", "count": 1,
               "listable": true, "reason": null | "BOUND" | "NOT_TRADABLE" | "NO_PRICE",
               "max_count": 1, "fee_pct": 5, "will_bind": "account" | null,
               "limits": { "min": 2400, "max": 59000 } | null } ] }
```
- 골드는 나오지 않는다. 한 키가 귀속별로 여러 줄일 수 있다. `max_count` = 장비 1, 그 밖은 `min(수량, 묶음 최대)`. `limits`는 `max_count`개 기준.

### 7.4 A4 POST /auction/listings
- 요청(`.strict()`):
```
{ request_id: uuid, item_key: string(ITEM_KEY_RE), count: int(1..9999),
  buyout: int(1..AUCTION_MAX_PRICE), start_bid?: int(1..AUCTION_MAX_PRICE), hours: int }
```
  **받지 않는 값: 보증금, 수수료, 마감 시각, 분류·등급, 귀속, 판매자.** `hours`는 zod가 아니라 서버가 `auction.json.durations`로 검사한다(`BAD_DURATION`).
- 처리(요청자 캐릭터 행 잠금 후 한 트랜잭션, 순서대로 첫 실패에서 중단):
  1. **자격**: `characters.level >= AUCTION_MIN_LEVEL` 그리고 `accounts.created_at <= now - AUCTION_MIN_ACCOUNT_AGE_DAYS`. 미달이면 `422 LISTING_GATE` + `auction_flags(gate, 1)`(별도 트랜잭션).
  2. **아이템**: 5.2절(`ITEM_UNKNOWN`, `NOT_TRADABLE`, `ITEM_BOUND`, `NOT_ENOUGH_ITEMS`). 장비는 `count=1`, 그 밖은 `count <= 묶음 최대`(`BAD_COUNT`).
  3. **기간**: `hours`가 `auction.json.durations`에 있다.
  4. **동시 등록**: 내 캐릭터의 `active` 등록 수 < `maxListings`(`409 LISTING_LIMIT`). 캐릭터 행이 잠겨 있어 같은 캐릭터의 동시 등록 요청은 직렬화되므로 정확히 센다.
  5. **가격**: 6.1절 한도 `[min, max]`에 `buyout`이 들어간다(`422 PRICE_OUT_OF_RANGE` + `auction_flags(price_band, 1)`). `start_bid`가 있으면 `min <= start_bid < buyout`(`START_BID_INVALID`). 시작가가 한도 아래면 낙찰가가 한도 아래로 내려가 한도의 의미가 없어지므로 시작가에도 하한을 적용한다.
  6. **보증금**: `clamp(round_half_up(buyout x depositBps / 10000), depositMin, depositMax)`. 골드가 모자라면 `422 NOT_ENOUGH_GOLD`(`errors.for="deposit"`).
  7. **효과**: `auction_listings` INSERT(`item_base`·`enhance`·`category`·`rarity`·`class_only`는 서버가 채움, `fee_pct`는 종류별 수수료율 스냅샷, `ends_at = 지금 + hours`, 입찰 열은 비움) → 가방 재고 `bind='none'` 행 -n(`item_ledger('auction_list')`, location `bag`, `ref`=listing uuid) 그리고 `auction` +n(`balance_after`는 등록 수량) → 골드 -보증금(`gold_ledger('auction_deposit')`, `ref`=listing uuid).
- 응답 `201` `data`: `{ "listing": MyListingView, "deposit": 120, "fee_pct": 5, "delta": {...} }`.
```
MyListingView = ListingView + { "deposit": 120, "fee_pct": 5, "ends_at": ISO, "extend_count": 0 }   // 내 등록에는 정확한 마감 시각을 준다
```
- 멱등성: `request_id`. 인덱스: `auction_listings_seller`(등록 수), `auction_trades_item_time`(시세).

### 7.5 A5 DELETE /auction/listings/{listing_id}
- 대상: 내 **캐릭터**가 등록한 `active` 등록만(`404 LISTING_NOT_FOUND`: 없음·남의 것은 구별하지 않고 남의 id가 오면 `auction_flags(foreign_id, 1)`).
- 처리(캐릭터 잠금 -> listing 잠금): 이미 `cancelled`이고 판매자가 내 캐릭터이면 `200`(`already: true`, 멱등: 응답을 못 받고 다시 보낸 경우). 그 밖에 `active`가 아니면 `409 LISTING_NOT_ACTIVE`. `ends_at`이 지났으면 `409 LISTING_ENDED`(마감 정산으로 같은 결과가 나온다). 입찰이 있으면 `409 HAS_BIDS`. 통과하면 `cancelled` → 판매자에게 우편 `cancelled`(아이템, 귀속 `none`) + 원장 `auction_return`(`auction -n`, 판매자 `mail +n`) → 보증금 소각(`auction_sinks('deposit_forfeit')`, 돌려주지 않는다. 허위·낚시 등록 억제).
- 응답 `200` `data`: `{ "mail_id": uuid, "forfeited_deposit": 120 }`. 입찰이 있으면 취소할 수 없다는 규칙이 입찰자의 예치금을 지킨다.
- 멱등성: 본문이 없어 `request_id` 대신 상태로 보장(두 번째 호출은 `already: true`).

### 7.6 A6 POST /auction/listings/{listing_id}/buyout
- 요청: `{ request_id: uuid }`. 금액은 받지 않는다(서버가 `buyout_price`를 낸다. 등록 가격은 바뀌지 않으므로 확인 창의 가격과 다를 수 없다).
- 처리(요청자 캐릭터 잠금 -> listing `FOR UPDATE` -> 재조회):
  1. 없음 `404 LISTING_NOT_FOUND`, `active`가 아님 `409 LISTING_NOT_ACTIVE`, 마감 지남(락 후 시각) `409 LISTING_ENDED`.
  2. **내 계정의 등록**이면 `403 OWN_LISTING` + `auction_flags(self_account, 2)`.
  3. **쌍 한도**(8.3): 오늘 같은 쌍의 체결 수 < `AUCTION_PAIR_DAILY_TRADES`, 금액 합 + 이번 가격 <= `AUCTION_PAIR_DAILY_GOLD`(`422 PAIR_LIMIT` + `auction_flags(pair_limit, 1)`).
  4. 골드 >= `buyout_price`(`422 NOT_ENOUGH_GOLD`).
  5. 효과(한 트랜잭션): 구매자 골드 -`buyout_price`(`gold_ledger('auction_buyout')`) → **직전 최고 입찰이 있으면** 그 입찰을 `lost_to_buyout`로 닫고 그 입찰자에게 우편 `outbid`(골드 = 입찰 예치금) → `closeAsSold`(9.1).
- 응답 `200` `data`: `{ "result": "bought", "price": 12000, "mail_id": uuid, "bind": "account" | "none", "delta": { "gold": 8000 } }`. 아이템은 우편으로 온다(`GET /mail`).
- 멱등성: `request_id`. 같은 등록에 새 `request_id`로 또 보내면 `409 LISTING_NOT_ACTIVE`.

### 7.7 A7 POST /auction/listings/{listing_id}/bids
- 요청: `{ request_id: uuid, amount: int(1..AUCTION_MAX_PRICE) }`.
- 처리(요청자 캐릭터 잠금 -> listing 잠금 -> 재조회):
  1. A6의 1~3과 같다(없음, 상태, 마감, `OWN_LISTING`, 쌍 한도는 `amount` 기준).
  2. `start_bid IS NULL`이면 `422 NOT_BIDDABLE`. 내가 이미 최고 입찰자(`current_bidder_character_id = 나`)이면 `409 ALREADY_TOP_BIDDER`(입찰 취소·증액 API는 없다).
  3. `amount >= buyout_price`이면 **즉시 구매로 처리**한다(A6 5와 같은 효과, `buyout_price`만 낸다). 응답 `result: "bought"`.
  4. **최소 입찰가**(락 후 현재가로 계산): 입찰이 없으면 `start_bid`, 있으면 `ceil(current_bid x minBidStepBps / 10000)`와 `current_bid + 1` 중 큰 값, 그리고 `buyout_price`를 넘지 않게 자른다. `amount < min_bid`이면 `409 BID_TOO_LOW`(`errors.min_bid`; 클라이언트는 이 값으로 다시 확인 창을 띄운다).
  5. 골드 >= `amount`(`422 NOT_ENOUGH_GOLD`).
  6. 효과 순서: 직전 최고 입찰이 있으면 `outbid`로 닫기(`closed_at`) → 새 `auction_bids` INSERT(`top`) → 골드 -`amount`(`gold_ledger('auction_bid')`, `ref`=bid uuid) → `auction_listings` UPDATE(`current_bid`, `current_bidder_*`, `bid_count+1`, **마감 연장**: `ends_at - 지금 <= 연장 구간(5분)`이고 `extend_count < 최대(6)`이면 `ends_at += 5분`, `extend_count+1`) → 직전 입찰자에게 우편 `outbid`(골드 = 직전 예치금, `bid_id`).
- 응답 `200` `data`: `{ "result": "bid", "current_bid": 7938, "min_bid": 8335 | null, "hours_left_band": 1, "extended": false, "delta": { "gold": ... } }`.
- 멱등성: `request_id`. 같은 금액을 다른 `request_id`로 또 보내면 이미 최고 입찰자라 `409 ALREADY_TOP_BIDDER`.

### 7.8 A8 GET /auction/mine
- 처리: 먼저 내 캐릭터가 판매자이거나 최고 입찰자인 마감 지난 등록을 지연 정산한다(10.2). 그다음 조회.
- 응답 `200` `data`: `{ "listings": [MyListingView], "bids": [MyBidView] }`. `listings`는 내 캐릭터의 `active` 등록(최근 등록 순, 상한 `maxListings`), `bids`는 내가 최고 입찰자인 `active` 등록: `ListingView + { "my_amount": 7938 }`(입찰자에게는 정확한 마감 시각을 주지 않고 `hours_left_band`만 준다). 밀려난 입찰은 우편(`outbid`)으로 알린다.
- 종료된 등록(팔림·만료·취소)은 이 응답에 없고 우편이 결과다(PLAN의 목업과 같다).

### 7.9 M1 GET /mail
- 쿼리: `tab?: "all"|"gold"|"item" = "all"`(gold: 골드 첨부, item: 아이템 첨부), `page?: int(1..)=1`, `limit?: int(1..50)=20`.
- 처리: 지연 정산(내 소유분) 후 `mails_open`으로 최신순 조회(받을 수 있는 우편만. 수령·폐기된 것은 목록에 없다).
- 응답 `200` `data`: `{ "mails": [ { "id": uuid, "kind": "sold", "ref_item_key": "eq_sword_iron+7", "ref_count": 1, "item": { "item_key", "count", "bind" } | null, "gold": 11400, "created_at": ISO, "expires_at": ISO, "days_left": 28 } ] }`, `meta`: `{ total, page, limit }`. 문구("철 소드 +7 판매 대금 11,400G")는 클라이언트가 `kind`와 필드로 조립한다(서버는 문장을 만들지 않는다).

### 7.10 M2 POST /mail/{mail_id}/claim
- 요청: `{ request_id: uuid }`.
- 처리(요청자 캐릭터 잠금 -> 우편 행 `FOR UPDATE`): 내 우편이 아니거나 없음 `404 MAIL_NOT_FOUND`(+ 남의 id면 `auction_flags(foreign_id, 1)`), 이미 수령 `409 MAIL_ALREADY_CLAIMED`, 폐기됨·기한 지남 `410 MAIL_EXPIRED`. 골드가 있으면 수령 후 합계가 `GOLD_CLIENT_MAX`를 넘는지 확인(`422 GOLD_CAP_EXCEEDED`, 우편은 남는다). 효과: `UPDATE mails SET claimed_at=now() WHERE id=$1 AND claimed_at IS NULL AND expired_at IS NULL` 한 문장(영향 행이 0이면 중단) → 골드 +`gold`(`gold_ledger('mail_claim')`, `ref`=mail uuid) → 첨부가 있으면 가방 재고 `+count`(우편의 `bind` 행, `item_ledger('mail_claim')` 두 줄: `mail -n`, `bag +n`).
- 응답 `200` `data`: `{ "claimed": { "item": {...} | null, "gold": 11400 }, "delta": {...} }`.
- 한 번만 보장: ① 조건부 UPDATE ② 캐릭터 행 잠금 ③ `gold_ledger_auction_uq`/`item_ledger_auction_uq`(같은 우편의 원장은 한 번). 멱등성: `request_id`.

### 7.11 M3 POST /mail/claim-all
- 요청: `{ request_id: uuid }`. 받을 수 있는 우편을 오래된 순으로 최대 `MAIL_CLAIM_ALL_MAX`통 수령한다(한 트랜잭션, 우편 행을 `id` 오름차순으로 잠근다).
- 응답 `200` `data`: `{ "claimed_count": 12, "remaining": 0, "skipped": 0, "delta": {...} }`. `remaining > 0`이면 클라이언트가 이어서 부른다. `skipped`는 `GOLD_CAP_EXCEEDED`로 남은 우편. 현재 가방에는 칸 제한이 없어(창고만 `storage_capacity`) PLAN의 "가방이 가득 찼습니다"는 해당이 없고 가방 칸 제한이 생기면 이 응답에 `bag_full`을 더한다.

### 7.12 M4 GET /mail/summary
- 쿼리: `since?: ISO`(클라이언트가 마지막으로 본 `latest_at`).
- 처리: 지연 정산(내 소유분) 후 가벼운 집계.
- 응답 `200` `data`: `{ "unclaimed": 3, "latest_at": ISO | null, "active_listings": 4, "my_top_bids": 1, "new": [ { "kind": "sold", "at": ISO, "ref_item_key": "...", "gold": 11400 } ], "server_time": ISO }`. `new`는 `since` 이후 도착한 우편(최대 20건, 토스트용). 정본은 `mails` 표라 서버가 푸시를 놓쳐도 이 응답이 같은 정보를 준다(12절).

## 8. 동시성 (동시 입찰 포함)

### 8.1 락 계층 (유일한 순서)
1. 요청자 **캐릭터 행 하나**(3단계 규칙). 판매자·직전 최고 입찰자·구매자 상대의 캐릭터 행은 잠그지 않는다.
2. `auction_listings` **한 행** `FOR UPDATE`. 한 트랜잭션이 두 listing을 잠그지 않는다.
3. 그 밖의 쓰기(`mails`·`auction_bids`·`auction_trades` INSERT, `auction_price_daily` upsert, `auction_sinks`)는 잠금 순서에 영향이 없다. `auction_price_daily`의 행 잠금은 마지막에 잡히고 그 행을 잡은 트랜잭션은 다른 것을 기다리지 않으므로 사이클이 없다.
- 정산 틱과 지연 정산은 캐릭터 행 없이 listing 한 행만 잠근다. 우편 수령(M2/M3)은 캐릭터 행 -> 자기 우편 행들이다. 어떤 경로도 "listing을 잡은 채 캐릭터 행을 기다리는" 일이 없다.
- **READ COMMITTED**에서 `FOR UPDATE`로 잠근 뒤 재조회한다. 실수로 잠그기 전 값을 쓰는 것을 막는 마지막 안전장치는 DB 제약이다(`auction_bids_one_top`, `UNIQUE(listing_id, amount)`, `auction_listings_closed_chk`, `mails_once_per_event`, 원장 유니크).

### 8.2 경쟁 시나리오와 결과

| 경쟁 | 결과 |
|---|---|
| 두 사람이 같은 등록에 동시 입찰(금액 다름) | listing 잠금으로 직렬화. 먼저 커밋한 입찰이 현재가가 되고, 나중 요청은 락 후 재조회에서 자기 금액이 새 최소 입찰가보다 낮으면 `409 BID_TOO_LOW`(`errors.min_bid`), 높으면 정상 입찰(앞 사람은 우편으로 반환) |
| 같은 금액 두 입찰 | 나중 요청은 최소 입찰가 미달로 `BID_TOO_LOW`. `UNIQUE(listing_id, amount)`가 마지막 안전장치 |
| 입찰 vs 즉시 구매 | 먼저 잠근 쪽이 이긴다. 구매가 먼저면 입찰은 `LISTING_NOT_ACTIVE`. 입찰이 먼저면 구매가 이어서 처리되고 그 입찰자는 우편으로 예치금을 돌려받는다 |
| 즉시 구매 두 건 동시 | 하나만 성공, 나머지 `LISTING_NOT_ACTIVE`. 둘째 구매자의 골드는 차감되지 않는다(잠금 후 재조회 단계에서 중단, 롤백) |
| 입찰 vs 등록 취소 | 입찰이 먼저면 취소는 `HAS_BIDS`. 취소가 먼저면 입찰은 `LISTING_NOT_ACTIVE` |
| 마감 직전 입찰 vs 정산 틱 | 틱은 listing을 `FOR UPDATE SKIP LOCKED`로만 잡고 사용 중이면 건너뛰어 다음 틱이 다시 본다. 입찰이 락을 잡았고 마감 5분 안이면 `ends_at`이 연장되어 틱이 락을 잡았을 때 `ends_at > 지금`이라 정산하지 않는다 |
| 마감 이후 도착한 입찰 | 락 후 시각 `>= ends_at`이면 `LISTING_ENDED`(틱이 아직 정산하지 않았어도). 클라이언트 시계·네트워크 지연은 판정에 쓰지 않는다 |
| 같은 요청 재전송(`request_id` 동일) | 처음 응답을 그대로 돌려줌(`Idempotent-Replay: true`) |
| 같은 사용자가 다른 `request_id`로 같은 입찰 연타 | 두 번째는 `ALREADY_TOP_BIDDER` |
| 한 사용자가 두 캐릭터로 동시 구매 | 서로 다른 캐릭터 락이라 병렬이지만 listing 락에서 직렬화. 같은 계정 쌍 한도는 3.1의 소프트 한도이므로 동시 요청이면 1건 초과할 수 있다(허용, 8.3) |
| 틱 두 개(서버 두 대 또는 이중 실행) | `SKIP LOCKED` + 정산 후 `status` 변경 + `mails_once_per_event`로 한 번만 처리. 리더 선출로 중복 스캔도 줄인다(10.1) |
| 정산 도중 서버 중단 | 정산은 listing 한 건당 트랜잭션 하나. 롤백되어 `active`로 남고 다음 틱(기동 직후 즉시 한 번)이 처음부터 다시 처리 |
| 우편 수령 두 요청 동시 | 우편 행 조건부 UPDATE가 한쪽만 성공, 나머지 `MAIL_ALREADY_CLAIMED`. 원장 유니크가 마지막 안전장치 |

### 8.3 쌍 한도는 소프트 한도
같은 계정 쌍의 오늘 체결 수·금액 합을 `auction_trades_pair`로 읽어 검사한다. 쌍을 직렬화하는 잠금을 따로 두지 않아 정확히 같은 순간의 여러 요청은 한도를 1건 넘을 수 있다. 목적이 조작·세탁의 규모 제한이라 허용한다(자문 잠금을 listing 락 뒤에 두면 정확해지지만 현재 규모에서 필요하지 않다. 필요해지면 `pg_advisory_xact_lock(쌍 해시)`를 listing 락 **뒤에** 잡는 것이 안전한 순서다).

## 9. 정산, 수수료, 골드 소각

### 9.1 정산 함수 `closeAsSold(tx, listing, price, buyer, kind)` (즉시 구매와 낙찰이 공유)
listing이 잠긴 트랜잭션 안에서:
1. `fee = (price x fee_pct + 99) / 100`(정수), `seller_payout = price - fee + deposit`.
2. listing `status='sold'`, `sold_kind`, `closed_at`. 낙찰이면 승자 입찰 `won`. 즉시 구매면 `current_bid` 열을 NULL로 비운다(직전 입찰은 `lost_to_buyout`로 `auction_bids`에 남는다).
3. 우편 두 통: 판매자 `sold`(골드 `seller_payout`, 첨부 없음, `ref_item_key`), 구매자 `bought`(아이템 첨부. 장비면 `bind='account'`, 그 밖은 등록 때의 `bind`).
4. 원장: 판매자 `item_ledger('auction_sold')`(`auction -n`), 구매자 `item_ledger('auction_buy')`(`mail +n`, `ref`=listing uuid).
5. `auction_trades` INSERT(수수료·정산액 CHECK가 산식을 강제, 같은 계정 CHECK가 마지막 방어).
6. `auction_price_daily` upsert(6.2).
7. `auction_sinks('fee', fee)`(`fee > 0`일 때).
8. 커밋 후 알림 이벤트(12절).

### 9.2 만료·취소·우편 폐기
| 사건 | 아이템 | 골드 | 소각 기록 |
|---|---|---|---|
| 마감, 입찰 없음 | 판매자 우편 `expired`(귀속 `none`), 원장 `auction_return` | 보증금은 돌려주지 않는다 | `deposit_forfeit` = `deposit` |
| 등록 취소(입찰 없음) | 판매자 우편 `cancelled`, 원장 `auction_return` | 보증금 몰수 | `deposit_forfeit` |
| 우편 보관 기한(30일) 경과 | 첨부는 폐기(`item_ledger('mail_expire')`, `mail -n`) | 첨부 골드는 폐기 | `mail_expire` = 우편 골드 |

### 9.3 골드 보존식 (감사용, 서버에 상시 실행하지 않고 테스트·운영 쿼리로 쓴다)
경매 이유의 골드 원장만으로 닫힌다.
```
R = SUM(-delta) FROM gold_ledger WHERE reason IN ('auction_deposit','auction_bid','auction_buyout')   -- 캐릭터에서 빠진 골드
C = SUM(delta)  FROM gold_ledger WHERE reason = 'mail_claim'                                          -- 캐릭터로 돌아온 골드
R - C + SUM(mails.gold WHERE kind='system') =                -- system 우편(7단계 운영 지급)은 캐릭터 밖에서 생긴 골드라 좌변에 더한다
     SUM(deposit WHERE listing.status='active')                -- 진행 중 등록의 보증금
   + SUM(current_bid WHERE listing.status='active')            -- 진행 중 입찰 예치금
   + SUM(mails.gold WHERE 미수령 AND 미폐기)                    -- 우편이 들고 있는 골드
   + SUM(auction_sinks.amount)                                 -- 소각 누계(fee + deposit_forfeit + mail_expire)
```
같은 식의 아이템 버전: `(character_id, location in (auction, mail), item_key)`별 원장 합 = 진행 중 `auction_listings.count` 합 / 미수령 `mails.count` 합. 14절 시나리오 테스트의 속성 테스트(임의 순서의 등록·입찰·구매·취소·정산·수령 뒤에 이 식이 성립)가 6단계의 핵심 안전망이다. 한 건의 체결에 대해서는 DB CHECK가 `price = fee + (seller_payout - deposit)`를 강제한다.

### 9.4 수수료·골드 소각 요약
- **수수료**: 판매 금액의 장비 5%, 재료·소모품 3%(`auction.json`), 올림(`ceil`), 판매자 수령액에서 뺀다. 예: 12,000G 장비 = 수수료 600G, 판매자 우편 = 11,400 + 보증금. 어느 캐릭터로도 가지 않고 `auction_sinks('fee')`에 기록된다.
- **보증금**: 즉시 구매가의 1%(최소·최대는 `auction.json`), 등록할 때 내고, 팔리면 판매자 우편에 합쳐 돌려주고 만료·취소면 소각.
- **소각 총량 감시**: `SELECT date_trunc('day', created_at AT TIME ZONE 'Asia/Seoul'), kind, SUM(amount) FROM auction_sinks GROUP BY 1, 2`(일일 소각량). 3단계의 골드 공급(드롭·퀘스트·카드)과 비교해 소각 비율을 보고 수수료율을 조정한다(수수료율은 `auction.json`이라 Unity 데이터 버전을 올려 배포).

## 10. 마감 정산·만료 처리 (서버 틱)

### 10.1 `AuctionTicker`
- 한 프로세스 안의 타이머(`AUCTION_TICK_SECONDS`, 기본 60초) + **기동 직후 즉시 1회**(서버가 꺼져 있던 동안 마감된 건 복구). 이전 틱이 끝나기 전에 다음 틱이 겹치지 않게 한다(실행 중 플래그).
- 서버를 여러 대로 늘릴 때(확장 1): 틱은 `pg_try_advisory_lock(경매 틱 키)`를 얻은 인스턴스만 돌린다. 못 얻으면 건너뛴다. 락을 놓치거나 인스턴스가 죽어도 다음 주기에 다른 인스턴스가 맡고, 어느 쪽이든 listing 단위 `SKIP LOCKED`와 DB 제약이 한 번만 처리를 보장한다.
- 순서: ① 마감된 등록 정산(아래) ② 기한 지난 우편 폐기 ③ (하루 1회, 06:00 직후) 보관 기간 지난 행 정리(3절 보관 표).
- 정산 대상 조회: `SELECT id FROM auction_listings WHERE status='active' AND ends_at <= clock_timestamp() ORDER BY ends_at LIMIT $AUCTION_TICK_BATCH`(`auction_listings_due`). 각 id를 **별도 트랜잭션**으로: `SELECT ... WHERE id=$1 AND status='active' AND ends_at <= clock_timestamp() FOR UPDATE SKIP LOCKED` → 행이 없으면 건너뜀 → 입찰 없음이면 만료, 있으면 9.1의 낙찰 정산. 한 건의 실패(예외)는 롤백하고 로그(pino `auction.settle_failed`)만 남기고 다음 건으로 간다. 그 건은 `active`로 남아 다음 틱에 재시도한다.
- 우편 폐기: `mails_expiring`으로 `expires_at <= now`인 미수령 우편을 `id`별 트랜잭션(`SKIP LOCKED`)으로 `expired_at=now()` + 첨부 폐기 원장(`mail_expire`) + 골드 소각 기록. 폐기된 우편은 수령할 수 없다(`410 MAIL_EXPIRED`).
- 관측: 정산마다 로그(`auction.settled`: listing uuid, 종류, 가격, 수수료). 틱 끝에 `lag_seconds`(가장 늦게 정산된 건의 `now - ends_at`)를 로그로 남기고 `3 x AUCTION_TICK_SECONDS`를 넘으면 경고. 정산이 늦어도 결과가 틀리지는 않는다(입찰·구매는 `ends_at`으로 직접 막히므로).

### 10.2 지연 정산
`GET /auction/mine`, `GET /mail`, `GET /mail/summary`는 응답 전에 "내 캐릭터가 판매자이거나 최고 입찰자인 마감 지난 등록"을 `auction_listings_seller`·`auction_listings_bidder`로 찾아 10.1의 listing 단위 정산을 호출한다(요청자 캐릭터 행은 **잠그지 않는다**: 8.1의 순서를 지키기 위해 정산은 listing 한 행만 잠근다). 이미 정산됐거나 다른 곳에서 처리 중이면 건너뛴다. 덕분에 판매자는 마감 후 틱을 기다리지 않고 판매 대금 우편을 본다.

## 11. 악용 방어

| 위협 | 방어 | 구현 위치 |
|---|---|---|
| **같은 계정의 캐릭터끼리 골드·아이템 이전**(캐릭터 A가 높은 가격으로 등록, 캐릭터 B가 구매) | ① 구매·입찰은 `seller_account_id`가 내 계정이면 `OWN_LISTING` ② 검색에서 내 계정 매물은 빠짐 ③ `auction_trades` CHECK가 `buyer_account_id <> seller_account_id`를 강제 ④ 입찰자 계정이 판매자 계정과 같을 수 없다는 `auction_listings_not_self` CHECK ⑤ **플레이어 간 우편·선물·송금 API는 만들지 않는다**(우편은 서버만 만든다) ⑥ 계정 귀속 아이템은 등록할 수 없다 ⑦ 시도는 `auction_flags(self_account)`로 남는다 | 서비스, DB |
| **다른 계정을 쓴 세탁**(두 계정이 협업, RMT·작업장) | ① 가격 한도(체결 기준 20~500%, 콜드 스타트는 상점가 1~100배) ② **같은 계정 쌍의 하루 체결 한도(건수·금액)** ③ 수수료 소각(순환마다 비용) ④ 등록 자격(레벨 10·계정 7일) ⑤ 경매로 산 장비는 계정 귀속(재판매 불가) ⑥ `auction_flags`·`auction_trades`로 사후 추적 | 서비스, 7단계 관리자 도구 |
| **시세 조작**(극단 가격 체결로 기준을 끌어올려 한도를 넓힘) | ① 기준은 평균이 아니라 **중앙값** ② 같은 쌍의 체결은 최근 2건까지만 반영 ③ 기록이 건수 5·구매 계정 3 미만이면 체결 기준을 쓰지 않고 상점 기준가 ④ 쌍 한도로 하루 영향 상한 | `reference()` |
| **자기 물건 구매·입찰로 가격 띄우기**(허위 입찰) | 같은 계정 입찰 불가. 입찰은 먼저 골드를 예치하고 철회 API가 없다(허위 입찰의 비용은 수수료 + 묶인 골드) | 서비스 |
| **복사 버그** | 보관자가 하나(등록·우편 행), 이동은 한 트랜잭션, 원장 유니크 인덱스, 우편 조건부 UPDATE | DB |
| **이중 정산·이중 수령** | listing 상태 전이, `auction_trades.listing_id UNIQUE`, `mails_once_per_event`, `mails_outbid_per_bid`, 원장 유니크 | DB |
| **클라이언트 값 조작** | 가격·수수료·귀속·분류는 서버가 계산, 등록 가격 수정 API 없음, 입찰 금액은 서버가 최소 입찰가·잔액을 재확인 | 서비스 |
| **저격** | 남은 시간은 구간만 노출, 마감 5분 안 입찰은 5분 연장(최대 6회). 마감 판정은 서버 시각 | 서비스 |
| **도배 등록·검색 폭주** | 등록 분당 10, 검색 계정당 초당 2, 동시 등록 20 | 속도 제한, 서비스 |
| **보증금 회피 낚시(등록-취소 반복)** | 취소하면 보증금 몰수 | 서비스 |
| **캐릭터 삭제로 자산 증발·우편 유실** | 진행 중 등록·최고 입찰·미수령 우편이 있으면 삭제 거절(`CHARACTER_HAS_AUCTION`) | 2단계 삭제 API |
| **남의 우편·등록 id 시도** | `404`로 구별하지 않고 `auction_flags(foreign_id)` | 서비스 |

## 12. 알림 이벤트 인터페이스 (5단계 WebSocket 연결 지점)

서버는 우편이 만들어진 **커밋 뒤에** `NotificationPublisher`를 부른다. 기본 구현은 아무것도 하지 않는다(`NoopPublisher`). 5단계의 WebSocket은 이 인터페이스를 구현해 접속 중인 캐릭터에게 푸시한다. 6단계는 이것 없이 `GET /mail/summary` 폴링만으로 동작해야 한다.
```ts
interface NotificationPublisher {
  publish(event: {
    type: 'mail.arrived' | 'auction.sold' | 'auction.outbid';   // PLAN_AUCTION §7.3의 WS 이벤트
    characterUuid: string;
    mailUuid: string;
    kind: 'sold' | 'expired' | 'outbid' | 'bought' | 'cancelled' | 'system';
    refItemKey: string | null;
    gold: number;
    at: string;                                                 // ISO
  }): void;                                                      // 동기, 실패해도 던지지 않는다(best-effort)
}
```
- 사건 -> 이벤트: 모든 우편은 `mail.arrived`, 우편 `sold`는 `auction.sold`, 우편 `outbid`는 `auction.outbid`도 함께(한 우편이 두 이벤트를 만들 수 있다).
- 푸시는 **유실될 수 있다**(서버 재시작, 접속 끊김). 정본은 `mails` 표이고 클라이언트는 접속 직후와 주기적으로 `GET /mail/summary?since=`로 따라잡는다. 이벤트에는 외부로 내부 키를 싣지 않고 uuid만 싣는다.
- 틱·지연 정산·요청 처리가 만든 우편 모두 같은 지점에서 발행한다(트랜잭션 안이 아니라 커밋 후 콜백에서).

## 13. 앞 단계에 닿는 변경 (구현 때 같이 고칠 곳)

6단계가 설계상 필요로 하는 변경이다. 3단계 이후 코드가 이미 있으므로 목록으로 둔다.

| 영역 | 변경 | 이유 |
|---|---|---|
| `character_items_stack_uq` | `(character_id, location, item_key, bind)`로 변경(0007) | 귀속이 다른 같은 키 재고를 분리 |
| `economyRepository.upsertStack` | `ON CONFLICT (character_id, location, item_key, bind) WHERE location IN ('bag','storage')` | 위 인덱스에 맞춤 |
| `stackCount` / `decStack` | 키의 모든 귀속 행을 합산, 소모는 강한 귀속부터(`character` -> `account` -> `none`), 소모한 귀속(들)을 돌려준다 | 키만으로 가리키는 3단계 API 호환 |
| `EconCtx.addItem` | 귀속을 받는다. `bindOf(key)`를 `bindFor(경로, key)`로(5.1절 경로 규칙, 하한은 `items.json`) | 획득 경로별 귀속 |
| 강화·창고 이동·장착·해제 서비스 | 소모한 행의 귀속을 결과 행에 이어 준다 | 귀속 유지 |
| 응답 `delta.stacks[]` | `{ item_key, location, bind, count }`로 `bind` 추가(기존 필드는 그대로, 한 키가 귀속별로 여러 줄) | 클라이언트가 합산해 쓴다 |
| `DELETE /characters/{uuid}` | 진행 중 등록·최고 입찰·미수령 우편(첨부·골드)이 있으면 `409 CHARACTER_HAS_AUCTION` | 자산 증발 방지 |
| `gold_ledger`/`item_ledger` reason CHECK | 0007이 확장(0006은 이 CHECK를 바꾸지 않는다고 가정. 바꾸면 목록을 합친다) | |
| Unity 내보내기 | `auction.json` 신규, `items.json`에 `name`, 귀속 하한 정의(mapping 3절) | |

## 14. 시나리오 테스트 (끝났다는 기준)

PLAN_SERVER §9 6단계의 "PLAN_AUCTION 시나리오 테스트 통과, 동시 입찰 포함"을 아래 목록으로 구체화한다. 시계는 주입 가능한 `Clock`으로 고정하고, 동시 시나리오는 실제 DB 연결 여러 개로 `Promise.all` 한다.

| # | 시나리오 | 확인할 것 |
|---|---|---|
| S1 | 등록 -> 즉시 구매 -> 구매자 우편 수령 | 판매자 우편 = 대금 - 수수료 + 보증금, 구매 장비 `bind=account`, 소각 기록, 보존식 |
| S2 | 등록 -> 입찰 -> 더 높은 입찰 -> 마감 정산 | 밀려난 입찰자 우편에 예치금, 낙찰자 우편에 아이템, 판매자 우편에 대금 |
| S3 | 입찰 없이 마감 | 판매자 우편에 아이템 반환, 보증금 소각 |
| S4 | 입찰 없을 때 취소 / 입찰 있을 때 취소 거절 | `cancelled` 우편, 보증금 몰수 / `HAS_BIDS` |
| S5 | 마감 5분 안 입찰 연장, 7번째 연장 거절 | `ends_at` 증가, `extend_count <= 6` |
| S6 | 즉시 구매가 이상의 입찰 | 즉시 구매로 처리, `buyout_price`만 차감 |
| S7 | 같은 캐릭터·같은 계정 다른 캐릭터의 구매·입찰 | `OWN_LISTING`, `auction_flags` |
| S8 | 가격 한도 밖 등록, 콜드 스타트 한도, 시작가 한도 | `PRICE_OUT_OF_RANGE`, `START_BID_INVALID` |
| S9 | 귀속: 시작 장비·보호권·퀘스트 보상 장비·경매 구매 장비 등록 시도 | `NOT_TRADABLE`/`ITEM_BOUND` |
| S10 | 신규 계정·낮은 레벨 등록, 동시 등록 21번째 | `LISTING_GATE`, `LISTING_LIMIT` |
| S11 | **동시** 즉시 구매 N건(서로 다른 구매자) | 정확히 1건 성공, 나머지 골드 차감 없음 |
| S12 | **동시** 입찰 N건(금액 다름·같음) | 최종 최고가 = 가장 높은 유효 입찰, 나머지는 `BID_TOO_LOW` 또는 우편으로 반환, `auction_bids_one_top` 위반 없음 |
| S13 | **동시** 입찰 vs 즉시 구매 / 입찰 vs 취소 / 입찰 vs 마감 정산 | 8.2 표대로, 골드·아이템 손실·복제 없음 |
| S14 | 같은 `request_id` 재전송 / 다른 본문 재사용 | 처음 응답 그대로 / `IDEMPOTENCY_MISMATCH` |
| S15 | 우편 이중 수령(동시, 순차), 모두 받기 50통 한도 | 한 번만 지급 |
| S16 | 틱 두 개 동시, 정산 중 예외 주입 | 한 번만 정산, 실패 건은 다음 틱에 재시도 |
| S17 | 우편 30일 경과 폐기 | `mail_expire` 원장, `auction_sinks`, `410 MAIL_EXPIRED` |
| S18 | 쌍 한도, 시세 중앙값(쌍 중복 제한), 기록 부족 시 콜드 스타트 | 한도·`source` |
| S19 | 속성 테스트: 임의 연산열 뒤 골드·아이템 보존식 | 9.3의 식 성립 |
| S20 | 캐릭터 삭제 거절, 클라이언트 값 조작(`.strict()`, 음수·큰 값·`AUCTION_MAX_PRICE`) | `CHARACTER_HAS_AUCTION`, `400`/`422` |

## 15. 결정 대기

실제로 사용자 결정이 필요한 것만 둔다(나머지는 위에서 추천값으로 정했다).

| # | 항목 | 선택지 | 추천 |
|---|---|---|---|
| 1 | **귀속을 어느 기준으로 정할지** (PLAN_AUCTION §6은 획득 경로 기준: 퀘스트 보상·상점 구매 장비는 거래 불가/계정 귀속. 지금 C# `AuctionRules.BindOf`와 내보낸 `items.json`·`shop.json`은 아이템 종류 기준이라 퀘스트 보상 루비 반지도 드롭 가능 장비라 거래 가능이다) | (a) PLAN대로 경로 기준: 서버가 획득할 때 귀속을 찍고 클라이언트는 서버가 준 귀속을 쓴다(퀘스트 보상 장비 `character`) (b) 지금처럼 종류 기준: 퀘스트 보상 장비도 거래 가능 | (a). PLAN v1.0이 확정한 규칙이다. 어차피 "경매 구매 후 계정 귀속"이 행 단위 귀속을 요구해 재고 키 변경(13절)은 (b)에도 필요하고, (a)의 추가 비용은 지급 경로 몇 곳에서 귀속을 넘기는 정도다 |
| 2 | **초반 시장이 비어 있을 때 운영 매물을 둘지** (100명 미만이면 등록이 몇 건뿐이라 시세가 안 생기고 콜드 스타트 한도가 오래 간다) | (a) 두지 않는다 (b) 운영 계정이 상점가 근처 매물을 올린다 | (a). 운영 매물은 골드·아이템 공급원이 되어 보존식과 소각 감시를 흐리고 시세를 인위로 만든다. 콜드 스타트 한도(상점가 1~100배)와 쌍 한도로 충분히 안전하다 |
| 3 | **계정 창고(같은 계정 캐릭터 간 이동)를 만들지** (PLAN_AUCTION §6 "계정 귀속은 같은 계정 캐릭터끼리 창고로만 이동"인데 PLAN_SERVER §9에 그 기능이 없다. 지금 창고는 캐릭터 소속이라 계정 귀속 아이템은 그 캐릭터가 쓰거나 상점에 파는 것밖에 못 한다) | (a) 지금은 만들지 않는다. 계정 귀속 = "경매 불가, 이 캐릭터 전용" (b) 7단계 전에 계정 창고 단계를 추가 | (a). 이 단계의 목표(경매)에는 필요 없고, 계정 창고는 캐릭터 간 이전 경로라 11절의 방어를 다시 검토해야 한다. 별도 기능으로 정하는 편이 안전하다 |
| 4 | **레이드 봉인 열쇠(`key_seal`)를 거래 가능으로 둘지** (`items.json`상 귀속 `none`이지만 상점 기준가가 없어 이 설계에서는 자동으로 등록 불가 `NO_PRICE`다) | (a) 거래 불가 유지(C# `BindOf`에서 `CharacterBound`로 명시해 의도를 코드에 남김) (b) 거래 가능(열쇠 기준 가격을 따로 정해 내보냄) | (a). 최종 레이드 입장권을 사고팔면 주간 보상 제한(3회)이 사실상 풀린다 |
