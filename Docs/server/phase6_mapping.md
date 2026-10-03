# 서버 6단계 게임 값 대응표 (경매장, 우편, 시세)

게임 쪽 정의(C# 필드·메서드, 목업 동작) <-> 서버 판정 <-> 테이블·컬럼 <-> API. API는 [phase6_api.md](phase6_api.md), 스키마는 `server/migrations/0007_auction.sql`(`server/schema.sql`에 합침). 앞 단계 대응표: [phase3_mapping.md](phase3_mapping.md), [phase4_mapping.md](phase4_mapping.md).
값(수치·목록)은 여기 복사하지 않고 `server/data/*.json` 이름과 C# 출처로만 참조한다. "예"로 든 값은 근거 설명용이다. 클라이언트 코드(`Assets/Scripts/Runtime`)는 이 문서가 읽은 2026-10-04 기준이다.

## 1. C# 규칙 <-> 서버 판정

출처는 `Net/AuctionService.cs`의 `AuctionRules`와 `MockAuctionService`, `UI/AuctionScreen.cs`. 서버 판정은 모두 서버가 다시 계산한다(클라이언트 계산은 미리보기).

| C# (출처) | 오프라인 목업의 현재 동작 | 서버 판정 | 데이터 / 테이블 |
|---|---|---|---|
| `AuctionRules.Durations` {12,24,48} | `Register`가 목록 밖이면 거절 | `BAD_DURATION`. `ends_at = 서버 지금 + hours` | `auction.json.durations`, `auction_listings.duration_hours`, `ends_at` |
| `FeePctGear`=5, `FeePctStack`=3, `FeePct(key)` (`EquipmentDatabase.IsEquipment`) | 장비 5, 그 밖 3 | 등록 때 종류별 수수료율을 `fee_pct`로 고정. 체결 때 그 값으로 계산 | `auction.json.feePct`, `items.json.kind`, `auction_listings.fee_pct`, `auction_trades.fee_pct` |
| `Fee(key, price)` = `Ceiling(price x pct / 100.0)` | 판매 시 대금에서 뺌(`DevSellMine`) | **정수** `(price x pct + 99) / 100`. DB CHECK가 같은 식을 강제 | `auction_trades.fee`, `auction_sinks('fee')` |
| `Payout(key, price)` = price - fee | 대금 + 보증금을 우편 | `seller_payout = price - fee + deposit`, 판매자 우편 `sold` | `auction_trades.seller_payout`, `mails.gold` |
| `DepositPct`=0.01f, `DepositMin`=10, `DepositMax`=10000, `Deposit(buyout)` = `Min(Max, Max(Min, (long)Math.Round(buyout x 0.01f)))` | 등록 때 가방 골드에서 차감, 취소·만료 시 미반환 | **정수** `clamp(floor((buyout x depositBps + 5000) / 10000), min, max)`(반올림은 half-up). C#의 float·은행가 반올림과는 `buyout x 1%`가 정확히 `.5`일 때만 다르다(예: 1,050G: C# 10 / 서버 11). C#도 같은 정수식으로 맞춘다(6절) | `auction.json.depositBps/depositMin/depositMax`, `auction_listings.deposit`, `gold_ledger('auction_deposit')` |
| `MaxListings`=20 | 목록 수가 20이면 거절 | `LISTING_LIMIT`. **캐릭터** 단위(캐릭터 행 잠금으로 정확히 센다) | `auction.json.maxListings`, `auction_listings_seller` |
| `MinBidStep`=1.05f, `MinBid(l)` = `!hasBid ? startBid : Max(Ceiling(currentBid x 1.05f), currentBid + 1)` | 입찰이 없으면 시작가 | **정수** `ceil(current x minBidStepBps / 10000)`와 `current + 1` 중 큰 값, **즉시 구매가를 넘으면 즉시 구매가로 자른다**(C#은 자르지 않아 최소 입찰가가 즉시 구매가 이상이 될 수 있었다). 응답의 `min_bid`가 정본이고 클라이언트는 이 값을 쓴다 | `auction.json.minBidStepBps`, 응답 `min_bid` |
| `PriceFloor`=0.2f, `PriceCeil`=5f (7일 평균 대비) | `BasePrice(key) x count`(목업 가짜 시세) 기준으로 거절 | `reference()`(api 6.1): 체결 **중앙값** 기준 하한 `priceFloorBps`, 상한 `priceCeilBps`. 기록이 없으면 상점 기준가의 1배~100배. **입찰 시작가에도 하한을 적용** | `auction.json.priceFloorBps/priceCeilBps`, `auction_trades`, `shop.json` |
| `BindOf(key)`: 골드·보호권 = Character, 시작 장비 = Character, 장비 `dropWeight>0` = Tradable, 그 밖의 장비 = Account, 재료·물약 = Tradable | 귀속이면 등록 거절, 가방 목록에서 선택 비활성 | **행의 `bind`가 정본**. 아이템 종류 하한(`items.json.bind`)에 획득 경로 규칙(api 5.1)을 덮는다. 등록은 가방의 `bind='none'` 행만. `BindOf`는 오프라인 전용으로 남는다(결정 대기 1) | `items.json.bind`, `character_items.bind`, `mails.bind` |
| 판매된 장비는 구매자 계정 귀속(PLAN §6) | **미구현**(목업은 구매하면 그냥 우편 + 가방) | 경매 구매 **장비**는 우편의 `bind='account'`, 수령하면 계정 귀속 행. 재료·소모품은 그대로 | `mails.bind`, `character_items.bind` |
| `CategoryOf(key)`: 장비 Weapon -> Weapon, Ring·Necklace -> Accessory, 그 밖의 장비 -> Armor; `EquipmentDatabase.GetMaterial != null` -> Material; 나머지 -> Consumable | 필터·분류 표시 | 서버가 `items.json.kind`와 `shop.json.equipment[].category`로 정한다(`wood`·`stone`·`carrot`·물약은 consumable) | `auction_listings.category` |
| `TimeBand(hours)`: <1, <6, <12, <24, 그 밖 | 남은 시간 구간 문구 | 서버가 구간 정수(1/6/12/24/48)만 내려준다. 정확한 시각은 내 등록에만 | `auction.json.timeBands`, 응답 `hours_left_band` |
| `BasePrice(key)` (목업 가짜 시세: `200 + Score x 3 + rarity x 600`, 강화 보정) | 목업 가격·한도 기준 | **쓰지 않는다**(오프라인 목업 전용). 서버 시세는 체결 기록과 상점 기준가 | - |
| 마감 연장: `Bid`가 `hoursLeft < 5/60`이면 `+5/60` | 횟수 제한 없음 | 마감 5분 이내 입찰이면 `ends_at += 5분`, **최대 6회**(PLAN §3) | `auction.json.extend*`, `auction_listings.extend_count` |
| 마감 정산(`DevSellMine`만 있음, 실제 마감 처리 없음) | 개발용 수동 판매 | 서버 틱 + 지연 정산(api 10절) | `auction_listings.status` |
| 우편 `Mail(text, key, count, gold)`, 보관 30일(화면 문구) | 수령 안 하면 영구 보관 | `kind` + 첨부 + 만료 시각. 30일 지나면 폐기(api 9.2) | `auction.json.mailDays`, `mails` |
| 입찰 시 골드 예치(`bag.Remove(gold, amount)`) | **직전 입찰자에게 돌려주지 않음**, `myBid`가 영구 true | 직전 최고 입찰자에게 `outbid` 우편으로 예치금 반환 | `auction_bids`, `mails(outbid)` |
| 즉시 구매(`Buyout`): `Authority.Current.ApplyGold("auction_buy", ...)`로 골드 차감, 우편에 아이템 | 판매자는 NPC(정산 없음) | 구매자 골드 차감 + 판매자 우편 대금 + 구매자 우편 아이템 | `gold_ledger('auction_buyout')`, `mails`, `auction_trades` |
| 같은 계정 다른 캐릭터 금지(PLAN §3) | **미구현**(`mine`만 본다) | 계정 단위로 금지(`OWN_LISTING`), 검색에서 제외, DB CHECK | `auction_listings.seller_account_id` |

## 2. 목업이 서버와 다르게 동작하는 곳 (서버 쪽이 정답)

| 목업 동작 | 서버 |
|---|---|
| `Search`가 전체 목록을 한 번에 돌려주고 클라이언트가 9건씩 자른다 | 서버 페이지네이션(`page`, `limit`). 클라이언트는 `limit=9`(UI 행 수)로 요청 |
| 검색에서 내 캐릭터 매물만 제외 | 내 **계정** 매물 제외 |
| 희귀도 필터가 한 등급 | 서버는 콤마 목록도 받는다(UI는 한 등급이면 그대로) |
| 초성 검색 없음(`IndexOf` 부분 일치) | 서버가 초성 검색(자음만 입력 시) |
| `Search(q)`에 직업 필터·개당 가격 정렬 없음 | 서버가 `class=mine`, `unit_price_asc`를 지원(UI는 나중에) |
| 입찰 금액을 클라이언트가 최소 입찰가로 계산해 보냄 | 서버가 락 후 최소 입찰가를 다시 계산. 부족하면 `409 BID_TOO_LOW`(`errors.min_bid`) |
| 취소·구매 결과 메시지를 클라이언트가 만든다 | 서버가 한국어 `message`와 `code`를 준다(4절) |
| 우편 `text`를 서비스가 만든다 | 서버는 `kind` + 필드만 준다. 문구는 클라이언트(`AuctionRules.MailText`) |

## 3. 추가로 내보낼 데이터 (Unity 쪽 작업, `Assets/Scripts/Editor/GameDataExport.cs`)

새 파일은 최상위에 `"schema": 1`을 둔다. 서버는 시작 시 zod로 검증하고 실패하면 기동하지 않는다. 클라이언트와 서버가 같은 값을 쓰도록 **원본은 C# `AuctionRules`** 이고, 아직 코드에 없는 값은 상수로 먼저 추가한다.

### 3.1 `auction.json` (신규)
| 키 | C# 출처 | 설명 |
|---|---|---|
| `durations` | `AuctionRules.Durations` | 기간(시간) 목록 |
| `feePct`: `{ equipment, stack }` | `FeePctGear`, `FeePctStack` | 정수 % |
| `depositBps`, `depositMin`, `depositMax` | `DepositPct`(0.01f -> 100 bps), `DepositMin`, `DepositMax` | float 값은 basis point 정수로 내보낸다 |
| `maxListings` | `MaxListings` | 캐릭터당 동시 등록 |
| `minBidStepBps` | `MinBidStep`(1.05f -> 10500) | 최소 입찰 증가율 |
| `priceFloorBps`, `priceCeilBps` | `PriceFloor`(0.2f -> 2000), `PriceCeil`(5f -> 50000) | 시세 대비 가격 한도 |
| `maxStack` | **코드에 없음**(PLAN §2의 "재료 999". `SubmitRegister`는 가방 수량 전부를 보낸다) -> `AuctionRules.MaxStack` 상수 추가 | 묶음 최대 |
| `extendWindowMinutes`, `extendMinutes`, `extendMax` | **코드에 없음**(`Bid`에 `5.0 / 60.0`으로 박혀 있다) -> 상수 추가 | 마감 연장: 5분 이내 입찰 시 5분, 최대 6회 |
| `mailDays` | **코드에 없음**(화면 문구 "보관 30일") -> 상수 추가 | 우편 보관 일수 |
| `timeBands` | `TimeBand`의 경계 | 남은 시간 구간 상한(시간) 목록 |

### 3.2 `items.json` 확장
| 필드 | 출처 | 설명 |
|---|---|---|
| `items[].name` | `DungeonDatabase.ItemName(key)` | 이름 검색·초성 검색용. 강화 단계는 키에서 서버가 붙인다 |
| `items[].bind`의 정의 변경 | 지금은 `AuctionRules.BindOf`(GameDataExport.cs 83행)를 그대로 쓴다 | 결정 1의 (a)이면 **하한만** 내보낸다: 시작 장비·보호권·(결정 4의 (a)면 레이드 열쇠) = `character`, 그 밖 = `none`. 장비의 "상점·퀘스트 장비 = account"는 내보내지 않는다(획득 경로가 정한다) |
| (정리) `shop.json equipment[].bind` | `AuctionRules.BindOf(e.id).ToString()`(108행), 값이 `"Tradable"` 같은 열거형 이름 | 서버는 `items.json`만 쓴다. 두 파일의 표기가 다르므로 `shop.json`의 `bind`는 지운다 |

## 4. 클라이언트 값 <-> API <-> 컬럼

### 4.1 `AuctionListing` (`Net/AuctionService.cs`)
| C# 필드 | API 응답 필드(`ListingView`) | 컬럼 | 비고 |
|---|---|---|---|
| `id` (string) | `id` (uuid) | `auction_listings.uuid` | |
| `itemKey` | `item_key` | `item_key` | |
| `count` | `count` | `count` | |
| `seller` | `seller_name` | `characters.name`(조인) | |
| `buyout` | `buyout` | `buyout_price` | |
| `startBid` (0 = 입찰 불가) | `start_bid` (null = 입찰 불가) | `start_bid` | `Biddable => startBid > 0`는 null -> 0으로 받으면 그대로 동작 |
| `currentBid`, `hasBid` | `current_bid`(null이면 `hasBid=false`) | `current_bid` | |
| `deposit` | `deposit`(내 등록만) | `deposit` | |
| `hoursLeft` (double) | `hours_left_band` (int) | 계산(`ends_at`) | `AuctionRules.TimeBand`를 정수 구간 입력으로 바꾼다 |
| `order` | (없음) | - | 서버가 정렬한 순서 그대로 쓴다 |
| `mine` | 검색에는 항상 false, `/auction/mine`의 `listings`는 true | 판매자 캐릭터 = 나 | |
| `myBid` | `my_bid` | `current_bidder_character_id` = 나 | 입찰이 밀리면 false가 된다(목업은 영구 true) |
| `category` | `category`(문자열) | `category` | 문자열 -> `AuctionCategory` 매핑 |
| `rarity` | `rarity`(0..5, null) | `rarity` | `ItemRarity` 순서(Common..Legendary) |
| `enhance` | `enhance` | `enhance` | |
| (신규) | `min_bid`, `bid_count`, `unit_price`, `will_bind`, `ends_at`(내 등록), `extend_count`(내 등록) | `current_bid` 계산, `bid_count`, `extend_count` | `AuctionRules.MinBid(l)`을 `l.minBid`로 대체 |

### 4.2 `AuctionQuery` -> `GET /auction/search`
| C# | 쿼리 | 비고 |
|---|---|---|
| `text` | `q` | 20자 |
| `category` | `category` | `all/weapon/armor/accessory/material/consumable` |
| `rarity` (-1 = 전체) | `rarity` | -1이면 보내지 않음 |
| `enhMin`, `enhMax` | `enh_min`, `enh_max` | |
| `priceMin`, `priceMax`(`long.MaxValue`) | `price_min`, `price_max` | `long.MaxValue`이면 보내지 않음(서버 상한은 `AUCTION_MAX_PRICE`) |
| `sort`: `PriceAsc/PriceDesc/TimeLeft/EnhanceDesc/Newest` | `price_asc/price_desc/time_left/enhance_desc/newest` | `unit_price_asc`는 서버만 지원 |
| (없음) | `class`, `page`, `limit` | `limit`은 UI 행 수 9 |

### 4.3 `AuctionMail`
| C# 필드 | API(`mails[]`) | 컬럼 |
|---|---|---|
| `id` | `id` | `mails.uuid` |
| `text` | (없음. `kind`와 필드로 클라이언트가 조립) | `kind`, `ref_item_key`, `ref_count`, `gold` |
| `itemKey`, `count` | `item.item_key`, `item.count`(null 가능) | `item_key`, `count` |
| `gold` | `gold` | `gold` |
| `claimed` | (목록에는 미수령만 온다) | `claimed_at` |
| (신규) | `kind`, `item.bind`, `expires_at`, `days_left` | `kind`, `bind`, `expires_at` |

### 4.4 `AuctionPrice`
| C# | API(`GET /auction/prices/{item_key}?count=`) | 비고 |
|---|---|---|
| `avg7d`, `min`, `max` (개당) | `avg7d`, `min`, `max` (`count`개 **총액**) | 클라이언트는 곱하지 않는다. 지금 `RowAction`·`RefreshRegister`가 `price.avg7d * count`를 곱하는 곳은 그대로 `price.avg7d`로 바꾼다 |
| `volume` | `volume` | 개수 합 |
| (신규) | `limits.min`, `limits.max`, `source`, `unit_avg`, `daily[]` | 등록 창이 한도와 시작 가격 제안에 사용 |

### 4.5 `AuctionResult`
`ok`/`message`는 그대로 쓴다. 서버 응답의 `success`/`message`/`code`가 채운다(`code`는 5절 표에서 UI 분기에 쓴다). 구매·입찰·수령의 `data.delta`(골드·재고)는 `ApiClient`가 `GameSession`의 골드·`Inventory`에 반영한다(3단계 규칙, `Authority.ApplyGold`는 쓰지 않는다).

## 5. 목업 메시지 <-> 서버 코드

| 목업 메시지(`AuctionResult.Fail`) | 서버 `code` (상태) |
|---|---|
| "이미 팔렸거나 기간이 끝난 물건입니다." | `LISTING_NOT_FOUND`(404) / `LISTING_NOT_ACTIVE`(409) / `LISTING_ENDED`(409) |
| "내가 등록한 물건은 살 수 없습니다." / "내 물건에는 입찰할 수 없습니다." | `OWN_LISTING`(403). 같은 계정 다른 캐릭터 포함 |
| "골드가 부족합니다." / "보증금 N G가 부족합니다." | `NOT_ENOUGH_GOLD`(422), 보증금이면 `errors.for="deposit"` |
| "즉시 구매만 가능한 물건입니다." | `NOT_BIDDABLE`(422) |
| "최소 입찰가는 N G입니다." | `BID_TOO_LOW`(409), `errors.min_bid` |
| "귀속 아이템은 등록할 수 없습니다." | `ITEM_BOUND`(422) / `NOT_TRADABLE`(422) |
| "가방에 아이템이 부족합니다." | `NOT_ENOUGH_ITEMS`(422) |
| "기간은 12/24/48시간입니다." | `BAD_DURATION`(422) |
| "동시 등록은 20건까지입니다." | `LISTING_LIMIT`(409) |
| "가격은 a~b G 사이여야 합니다." | `PRICE_OUT_OF_RANGE`(422), `errors.min`, `errors.max` |
| "입찰 시작가는 즉시 구매가보다 낮아야 합니다." | `START_BID_INVALID`(422) |
| "입찰자가 있어 취소할 수 없습니다." / "취소할 수 없습니다." | `HAS_BIDS`(409) / `LISTING_NOT_FOUND`(404) |
| "받을 우편이 없습니다." | `MAIL_NOT_FOUND`(404) / `MAIL_ALREADY_CLAIMED`(409) / `MAIL_EXPIRED`(410) |
| (목업에 없음) | `PAIR_LIMIT`, `LISTING_GATE`(신규 계정·레벨), `ALREADY_TOP_BIDDER`, `GOLD_CAP_EXCEEDED`, `BAD_COUNT` |

## 6. 클라이언트 변경 지점 (file:function)

Unity 쪽 작업이다. 클라이언트 쪽에서 가장 큰 일은 **동기 `IAuctionService`를 요청 기반(비동기)으로 바꾸는 것**이다(서버 호출은 응답을 기다린다).

| 파일 | 함수·타입 | 변경 |
|---|---|---|
| `Net/AuctionService.cs` | `IAuctionService` | 모든 메서드를 콜백(또는 `Task`) 시그니처로: 예 `void Search(AuctionQuery q, Action<AuctionPage> done)`, `void Buyout(string id, Action<AuctionResult> done)`. `AuctionPage { listings, total, page }`. `UnclaimedMail`은 캐시 속성(요약 폴링이 갱신) 유지. `Changed` 이벤트 유지 |
| 같은 파일 | `MockAuctionService` | 오프라인 목업은 남긴다. 새 시그니처에 맞추고 즉시 콜백을 호출. (선택) 서버 규칙과 맞추기: `Bid`가 직전 입찰자에게 우편 반환, 마감 연장 횟수 6회 |
| 같은 파일 | `AuctionListing` | `hoursLeft`(double) -> `hoursLeftBand`(int), `minBid`, `bidCount`, `willBind`, `endsAt`(내 등록) 추가. `startBid` null 처리 |
| 같은 파일 | `AuctionMail` | `kind`, `refItemKey`, `refCount`, `bind`, `expiresAt`, `daysLeft` 추가. `text`는 `AuctionRules.MailText(kind, ...)`로 조립 |
| 같은 파일 | `AuctionPrice`, `AuctionQuery` | 4.4, 4.2의 필드 추가(`limits`, `source`, `daily`, `classMine`, `page`, `limit`) |
| 같은 파일 | `AuctionRules.Deposit`, `Fee`, `MinBid` | float·`Math.Round` -> 정수식(1절 표): `Deposit` = `(buyout * DepositBps + 5000) / 10000`, `Fee` = `(price * pct + 99) / 100`, `MinBid`는 온라인에서 쓰지 않음(`l.minBid`) |
| 같은 파일 | `AuctionRules` 상수 | `DepositPct`(float)·`MinBidStep`·`PriceFloor`·`PriceCeil`을 bps 정수로. `MaxStack`, `ExtendWindowMinutes`, `ExtendMinutes`, `ExtendMax`, `MailDays` 추가. `GameDataExport`가 이 상수를 `auction.json`으로 내보낸다 |
| 같은 파일 | `AuctionRules.BindOf` | 오프라인 전용으로 둔다. 온라인 귀속은 서버가 준 `bind`(결정 1). 장비 분기(`dropWeight > 0` -> Tradable, 그 밖 -> Account)는 결정 1의 (a)이면 삭제하고 하한(시작 장비·보호권·열쇠)만 남긴다 |
| 같은 파일 | `AuctionRules.TimeBand(double)` | 정수 구간 입력(`int band`)을 받는 오버로드 추가 |
| `Net/`(신규) | `ServerAuctionService : IAuctionService` | `ApiClient`로 api 2절의 12개를 호출. 사용자 행동 1회마다 `request_id`를 만들고 타임아웃 재시도에는 같은 id를 쓴다. 에러 `code`를 `AuctionResult`로 변환. 우편 요약 폴링(창 열림 15초 / 닫힘 60초). 검색·내 등록·우편 결과 캐시 |
| `Net/PartyFinderService.cs` | `OnlineServices.Auction` getter(199행) | 온라인 모드 세션이면 `ServerAuctionService`, 오프라인이면 `MockAuctionService`를 만든다(지금은 항상 목업) |
| `UI/AuctionScreen.cs` | `Refresh()` | 서비스를 동기로 읽지 않는다. 탭·페이지·필터가 바뀔 때만 요청하고(`Reload`), 응답이 오면 캐시로 그린다. 로딩 표시, 응답 전 중복 클릭 방지. 지금 `Refresh()`가 매 클릭마다 `Service.Search(query).ToList()`를 부르는 구조를 바꾼다 |
| 같은 파일 | `Refresh()`의 검색 표 | `PageSize`(9)를 `limit=9`로 보내고 서버 `meta.total`로 페이지 수를 계산(지금은 전체 목록 길이) |
| 같은 파일 | `PreviewBanner()` 호출(`Create`) | 온라인이면 숨긴다(`PartyFinderScreen`처럼 `banner.SetActive(!Service.IsOnline)`, `OnlineWindows.cs:225`) |
| 같은 파일 | `RowAction()` | 구매 확인 창의 `price.avg7d * l.count`를 `price.avg7d`(총액)로. 입찰은 `AuctionRules.MinBid(l)` 대신 `l.minBid`. `BID_TOO_LOW`를 받으면 `errors.min_bid`로 확인 창을 다시 띄운다 |
| 같은 파일 | `SubmitRegister()` | `count`를 `min(가방 수량, sellable.max_count)`로(지금은 가방 수량 전부, 최대 묶음 999 초과 가능). 서버 에러 메시지·`PRICE_OUT_OF_RANGE`의 `errors.min/max` 표시 |
| 같은 파일 | `TradableBag()`, `RefreshRegister()` | 가방 아이템 목록을 `GET /auction/sellable`로(귀속 줄, `listable`, `reason`, `limits`, `gate`). `AuctionRules.BindOf(key)` 호출을 서버 `bind`로 대체. 등록 자격 미달(`gate.can_list=false`)이면 등록 탭에 사유 표시. 가격 입력 한도 안내 |
| 같은 파일 | 우편 탭 행(`Refresh()`의 `tab == Tab.Mail`) | `m.text` 대신 `kind`로 문구 조립, 남은 보관 일수(`daysLeft`) 표시. 머리글의 고정 문구 "보관 30일"은 `MailDays` 상수로 |
| 같은 파일 | `claimAllBtn` 핸들러 | `ClaimAll`이 한 번에 최대 50통이므로 `remaining > 0`이면 이어서 호출 |
| 같은 파일 | `Dev*` 훅들 | 콜백 시그니처에 맞춰 수정(`DevBuyRow`, `DevSubmitRegister` 등) |
| `UI/SideMenuView.cs` | 42행 경매장 메뉴 항목 | 미수령 우편 수(`UnclaimedMail`)가 있으면 아이콘에 빨간 점(PLAN §9. 지금은 경매장 창의 우편함 탭에만 숫자가 있다) |
| `UI/EquipmentScreen.Tooltip.cs` | 80~81행 `AuctionRules.BindOf(s.itemId)` | 온라인에서는 캐릭터 상세의 `items[].bind`(행의 귀속)로 라벨을 만든다. 같은 키의 귀속이 다른 재고가 둘일 때는 더 강한 쪽을 보여 준다 |
| `Editor/GameDataExport.cs` | 83행, 108행 | 3.2절: `items.json` 귀속을 하한만 내보내고 `shop.json`의 `bind` 제거. `auction.json`(3.1)과 `items[].name` 추가. 데이터 버전 해시에 새 파일 포함 |
| `Core/DevCapture.Online.cs` | `OnlineAuction()`(322~384행) | 목업의 동기 반환값(`svc.Search(...)`, `Buyout(...)`)을 쓰는 검증이 새 시그니처에 맞게 바뀐다 |
| `Net/Authority.cs` | `IAuthority.ApplyGold`(28행) | 경매의 `"auction_buy"` 호출은 목업 전용이다. 온라인 골드는 응답 `delta.gold`로만 바뀐다(3단계 mapping의 `ApplyGold` 줄이 이 단계를 가리킨다) |
| 타이틀(2단계 클라이언트) | 캐릭터 삭제 확인 | `409 CHARACTER_HAS_AUCTION`의 `message` 표시 |

골드는 클라이언트 `Inventory`가 `int`라 서버가 `AUCTION_MAX_PRICE`(1,000,000,000)와 `GOLD_CLIENT_MAX`로 상한을 건다(api 3절). 수령으로 상한을 넘는 우편은 `GOLD_CAP_EXCEEDED`로 남는다.

## 7. PLAN_AUCTION과 달라진 점, 확정한 해석

PLAN_AUCTION v1.0과 다르거나, 모호해서 이 설계가 정한 것이다. PLAN 본문은 고치지 않았다.

| 항목 | PLAN_AUCTION | 이 설계 | 이유 |
|---|---|---|---|
| 경로·멱등성 | `/auction/...`, `/mail`, `Idempotency-Key` 헤더 | `/characters/{uuid}/auction/...`, `/characters/{uuid}/mail...`, 본문 `request_id` | 3·4단계와 같은 규칙(캐릭터 행동 + 데이터 버전 검사 + `request_log`) |
| 아이템 보관 | `character_items.location='auction'`(등록), `mails.item_id` | 등록·우편 행이 직접 보관, `character_items`에 `auction`/`mail` 행 없음 | 보관자 하나, 소유 이전·복제 경로 제거. 원장 `location`은 그대로 쓴다 |
| 거래 후 계정 귀속 | 장비 거래 1회 후 구매자 `AccountBound` | 같다. 단 재고 키에 `bind`를 넣어 구현(13절 영향). **재료·소모품은 귀속 없음** | `item_key`만으로는 귀속이 다른 같은 키를 구별할 수 없다 |
| 귀속 기준 | 획득 경로(드롭 = 거래, 퀘스트 = 캐릭터, 상점 = 계정) | 같다(결정 대기 1). C# `BindOf`는 종류 기준이라 달랐다 | PLAN이 확정 규칙 |
| 시세 집계 | 하루 한 번 06:00 확정, 오늘은 근사 | 체결 트랜잭션에서 누적(upsert). `day_start`는 06:00 KST 경계 | 정확하고 별도 작업이 없다 |
| 가격 한도 기준 | 최근 7일 **평균**의 20~500%, 기록 없으면 상점 판매가 1배~"기본가 100배" | **중앙값**(쌍 중복 제한, 5건·3구매자 이상), 기록 없으면 상점 기준가의 1~100배. "기본가"는 상점 구매가·판매가 중 큰 값으로 해석 | 조작에 강하게. 응답의 `avg7d`는 표시용 평균으로 따로 준다 |
| 수수료·보증금 계산 | 5% / 3%, 1%(최소 10, 최대 10,000) | 같다. 정수 올림(수수료), half-up(보증금), basis point | C#의 float 반올림 차이 제거 |
| 입찰 시작가 | 입찰 시작가 < 즉시 구매가 | + 시작가도 가격 한도의 하한 이상 | 낙찰가가 한도 밑으로 내려가 한도가 무의미해지는 것을 막는다 |
| 최소 입찰가 | max(시작가, 현재가 x 1.05, 현재가 + 1) | + 즉시 구매가를 넘지 않게 자른다. 입찰 금액이 즉시 구매가 이상이면 즉시 구매 | 최소 입찰가가 즉시 구매가 이상이 되는 모순 제거 |
| 검색 | 이름·초성·분류·등급·강화·가격·직업, 정렬 6종, 20건 | 같다. 내 **계정** 매물은 결과에서 제외 | 살 수 없는 물건을 숨긴다 |
| 신규 계정 등록 제한 | "레벨 10 미만·7일 미만이면 등록 불가" | 둘 중 하나라도 미달이면 불가(OR), 환경변수. 개발 환경은 0 허용 | 문장이 모호해 더 엄격한 해석, 시험 편의는 설정으로 |
| 우편 기한 | 보관 30일 | 30일 지나면 첨부 아이템·골드 폐기(소각 기록). 목록에 남은 일수 표시 | PLAN은 폐기를 말하지 않음. 폐기하지 않으면 우편이 영구히 쌓인다 |
| 가방 가득 참 | 모두 받기가 들어가는 만큼만 | 현재 가방에 칸 제한이 없어 해당 없음. 제한이 생기면 `bag_full` 응답을 더한다 | 가방 용량 규칙이 코드에 없다 |
| 취소 | `DELETE`, 입찰 없을 때, 보증금 미반환(목업 문구) | 같다. 이미 취소한 것을 또 취소하면 성공(멱등) | |
| 악용 기록 | `gold_logs`, `item_logs`, `auction_trades` | 기존 원장 + `auction_trades` + `auction_sinks`(소각) + `auction_flags`(의심) | `anomaly_log` CHECK가 5단계 `0006`과 겹치지 않게 전용 표 |
| 추가 API | (없음) | `GET /auction/sellable`(귀속·한도·자격), `GET /mail/summary`(폴링), 시세 응답의 총액·한도 | 클라이언트가 귀속·한도 규칙을 따로 구현하지 않게, 5단계 없이도 알림이 동작하게 |
| 알림 | WebSocket `auction.sold`, `auction.outbid`, `mail.arrived` | `NotificationPublisher` 인터페이스(기본 no-op). 5단계가 구현. 폴링이 기본 | 6단계는 WebSocket 없이 완결 |
| 차단과 경매 | (언급 없음) | 5단계의 차단은 경매에 적용하지 않는다(익명 시장, 판매자 이름은 표시) | 경매는 사람과 사람의 직접 거래가 아니다 |

## 8. 다른 단계와의 접점

| 단계 | 접점 |
|---|---|
| 2단계 | `DELETE /characters/{uuid}`가 진행 중 등록·최고 입찰·미수령 우편이 있으면 `409 CHARACTER_HAS_AUCTION` |
| 3단계 | `character_items_stack_uq`에 `bind` 추가, `upsertStack`/`stackCount`/`decStack`, 귀속 소모 순서, 획득 경로별 귀속, 강화·창고·장착의 귀속 이어받기, `delta.stacks[].bind`(api 13절). `Authority.ApplyGold`의 6단계 줄은 이 단계로 닫힌다 |
| 4단계 | 락 순서(요청자 캐릭터 -> listing)는 4단계의 "여러 캐릭터는 id 오름차순" 규칙과 충돌하지 않는다(경매는 요청당 캐릭터 하나만 잠근다). 레이드 열쇠(`key_seal`)는 결정 4 |
| 5단계 | `NotificationPublisher`(api 12절)를 WebSocket이 구현. `0006`과 `0007`은 서로 의존하지 않는다. `gold_ledger`/`item_ledger` reason CHECK는 이 단계만 바꾼다고 가정 |
| 7단계 | 관리자 도구가 `auction_flags`, `auction_sinks`, 보존식(api 9.3)을 읽는다. `system` 우편으로 운영 지급·CS 보상. 보관 기간 정리 작업 정식화 |
