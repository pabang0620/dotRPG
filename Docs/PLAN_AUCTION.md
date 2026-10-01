# 경매장 · 우편함 · 거래 규칙 기획서 (v1.0)

> 상태: **v1.0 확정.** 던전앤파이터 경매장을 기준으로 한다. 서버는 기획만 하고, 클라이언트는 `IAuctionService` 뒤의 `MockAuctionService`(오프라인 목업)로 UI를 먼저 만든다.
> 관련 문서: [PLAN_ONLINE.md](PLAN_ONLINE.md) §5 (서버 구조·DB·API 공통 규칙)

## 1. 목표
- 던전에서 얻은 장비·재료를 다른 사람과 사고팔아 파밍의 가치를 만든다.
- 골드 가치가 무너지지 않도록 **수수료(골드 소각)**, **가격 제한**, **서버 판정**으로 경제를 지킨다.
- 오프라인 캐릭터와 섞지 않는다(PLAN_ONLINE O1): 경매장은 온라인 캐릭터 전용.

## 2. 등록 (판매)
| 항목 | 규칙 |
|---|---|
| 판매 방식 | **즉시 구매가**(필수) + **입찰 시작가**(선택). 입찰 시작가 < 즉시 구매가 |
| 기간 | 12시간 / 24시간 / 48시간 |
| 보증금 | 즉시 구매가의 1%(최소 10G, 최대 10,000G). 팔리면 돌려주고, 만료·취소되면 **돌려주지 않는다**(허위 등록 억제) |
| 수수료 | 팔릴 때 판매 금액에서 뺀다. 일반 **5%**, 재료·소모품 **3%**. 수수료는 소각(골드 회수) |
| 동시 등록 | 캐릭터당 20건 |
| 묶음 | 재료·소모품은 1~최대 묶음(재료 999)까지 한 건으로. 장비는 1개씩 |
| 등록 취소 | 입찰자가 없을 때만. 아이템은 우편함으로 |
| 미리보기 | 등록 창에 "보증금 / 예상 수수료 / 예상 수령액"을 실시간 표시 |

## 3. 구매 · 입찰
- **즉시 구매**: 확인 창("○○를 12,000G에 구매합니다") → 서버가 잔액·상태 확인 → 골드 차감 → 아이템은 **우편함**으로(가방이 꽉 차도 잃지 않게). 판매자에게는 대금 − 수수료 + 보증금이 우편으로.
- **입찰**: 최소 입찰가 = max(시작가, 현재가 × 1.05, 현재가 + 1G). 입찰 시 골드를 **예치**(가방에서 빠짐). 더 높은 입찰이 오면 이전 입찰자에게 예치금을 우편으로 돌려준다. 입찰가가 즉시 구매가 이상이면 즉시 구매로 처리.
- **마감 연장**: 마감 5분 안에 입찰이 오면 마감을 5분 늘린다(최대 6회, 저격 방지).
- **자기 물건**은 사거나 입찰할 수 없다. 같은 계정의 다른 캐릭터도 금지.

## 4. 검색 · 필터 · 정렬
- 검색어: 아이템 이름 부분 일치(초성 검색 지원: "ㅊㅅㄱ" → 철 소드…는 서버에서 처리).
- 필터: **분류**(전체 / 무기 / 방어구 / 장신구 / 강화 재료 / 소모품), **등급**(일반·고급·레어·유니크·에픽 다중 선택), **강화 범위**(+0 ~ +20 최소·최대), **가격 범위**, **착용 직업**(내 직업만 보기 체크).
- 정렬: 가격 낮은 순(기본) / 높은 순 / 개당 가격 / 남은 시간 짧은 순 / 강화 높은 순 / 최근 등록 순.
- 결과는 페이지당 20건, 행: 아이콘 · 이름(등급 색) · `+강화` · 등급 · 즉시 구매가 · 현재 입찰가 · 판매자 · 남은 시간(`12시간 미만`처럼 구간 표시, 정확한 초는 숨김: 저격 완화).
- 검색 요청은 계정당 초당 2회.

## 5. 시세
- 같은 **아이템 키**(`기본id+강화`)마다 최근 거래 기록을 보여 준다: 최근 7일 평균가, 최저·최고, 거래량, 일별 그래프(최근 14일).
- 구매 확인 창에 "최근 평균가 대비 +35%" 처럼 비교를 띄운다(평균가의 2배 이상이면 빨간 경고).
- 시세 집계는 `auction_price_daily`에 하루 한 번(06:00) 확정, 오늘 값은 실시간 근사.

## 6. 거래 가능 규칙 (귀속)
| 플래그 | 뜻 | 표시 |
|---|---|---|
| `Tradable` | 경매장 등록 가능 | 툴팁 "거래 가능" (초록) |
| `AccountBound` | 같은 계정 캐릭터끼리 창고로만 이동 | "계정 귀속" (하늘색) |
| `CharacterBound` | 이 캐릭터만 사용 | "캐릭터 귀속" (주황) |

| 아이템 | 기본 플래그 | 이유 |
|---|---|---|
| 몬스터 드롭 장비 | Tradable | 파밍 결과를 거래하는 경매장의 중심 |
| 던전 카드 보상 장비 | Tradable | 위와 같음 |
| 퀘스트 보상(루비 반지 등) | CharacterBound | 진행 보상은 사고팔면 안 됨 |
| 상점 구매 장비 | AccountBound | 상점 → 경매 차익 거래 방지 |
| 강화 재료 | Tradable | 재료 시장이 강화 수요를 받친다 |
| **장비 보호권** | **CharacterBound** | 강화 실패 위험을 돈으로 사는 통로를 막는다(던파 보호권과 같음) |
| 회복 물약 | Tradable(3% 수수료) | 소모품 |

**강화된 장비는 거래 가능 (결정)**: 강화 수치를 그대로 유지한 채 등록할 수 있다.
- 이유 ① 던파와 같다(고강 장비가 경매장 최상위 상품). ② 강화에 쓴 골드·재료가 다시 시장으로 돌아와 "강화 장인 → 구매자" 분업이 생긴다. ③ 막으면 +10 이상 장비가 계정에 묶여 경제 순환이 멈춘다.
- 대신 **거래 횟수 1회**: 경매장에서 한 번 팔린 장비는 구매자 쪽에서 `AccountBound`가 된다(되팔기·작업장 세탁 방지). 강화를 다시 해도 귀속은 유지.
- 강화 실패로 파괴되거나 `+0`으로 초기화되면 귀속 상태는 그대로.
- 착용 중이거나 잠금(자물쇠)된 아이템은 등록 창 목록에 나오지 않는다.

## 7. 서버
### 7.1 처리 원칙
- 모든 등록·구매·입찰·정산은 **경매 서비스 한 곳의 DB 트랜잭션**(행 잠금 `SELECT … FOR UPDATE`). 클라이언트 값은 믿지 않고 서버가 아이템 소유·귀속·잔액·가격 범위를 다시 확인한다.
- 등록 시 아이템은 `character_items.location = 'auction'`으로 옮겨 가방에서 빠진다(복사 방지). 판매·만료 시 우편으로만 이동.
- 마감 처리기는 1분마다 마감된 등록을 정산(입찰 있음 → 낙찰, 없음 → 만료 우편).
- 같은 요청이 두 번 와도 한 번만 처리(`Idempotency-Key` 헤더).

### 7.2 테이블 (PLAN_ONLINE §5.2와 같은 규칙: uuid, bigint 금액, 로그는 추가만)
| 테이블 | 컬럼 |
|---|---|
| auction_listings | id, seller_character_id, seller_account_id, item_id(→character_items), item_key, count, category, rarity, enhance, buyout_price, start_bid, current_bid, current_bidder_id, deposit, fee_pct, created_at, ends_at, extend_count, status(active/sold/expired/cancelled), version |
| auction_bids | id, listing_id, bidder_character_id, amount, created_at, refunded_mail_id |
| auction_trades | id, listing_id, item_key, count, price, fee, buyer_character_id, seller_character_id, kind(buyout/bid), traded_at |
| auction_price_daily | item_key, day, avg_price, min_price, max_price, volume |
| mails | id, character_id, kind(sold/expired/outbid/bought/cancelled/system), item_id, item_key, count, gold, listing_id, created_at, expires_at(30일), claimed_at |
- 인덱스: `auction_listings(status, category, rarity, enhance, buyout_price)`, `(item_key, status)`, `(seller_character_id, status)`, `(ends_at) WHERE status='active'`.

### 7.3 API
- `GET /auction/search?q=&category=&rarity=&enhMin=&enhMax=&priceMin=&priceMax=&class=&sort=&page=` → {items[], total}
- `GET /auction/price/{itemKey}` → {avg7d, min, max, volume, daily[]}
- `POST /auction/listings` {itemId, count, buyout, startBid?, hours} → {listingId, deposit, endsAt}
- `DELETE /auction/listings/{id}` → 취소(입찰 없을 때)
- `POST /auction/listings/{id}/buyout` → {mailId}
- `POST /auction/listings/{id}/bids` {amount} → {currentBid, endsAt}
- `GET /auction/mine` → 내 등록·내 입찰
- `GET /mail` · `POST /mail/{id}/claim` · `POST /mail/claim-all`
- WebSocket: `auction.sold`, `auction.outbid`, `mail.arrived`

## 8. 부정 거래 방지
| 위험 | 대책 |
|---|---|
| 골드 세탁(헐값·폭리 거래로 골드 이동) | 아이템 키별 **가격 하한·상한**: 최근 7일 평균의 20%~500%(거래 기록이 없으면 상점 판매가의 1배~기본가 100배). 범위를 벗어나면 등록 거부 |
| 작업장 되팔기 | 장비 거래 1회 후 계정 귀속, 수수료 소각 |
| 도배 등록·검색 폭주 | 등록 분당 10건, 검색 초당 2회, 동시 등록 20건 |
| 복사 버그 | 아이템을 `auction` 위치로 옮기는 트랜잭션, 우편 수령도 트랜잭션, Idempotency-Key |
| 저격 | 남은 시간 구간 표시, 마감 연장 5분 |
| 클라이언트 조작 | 가격·수수료·귀속은 서버가 다시 계산. 클라이언트 표시는 미리보기일 뿐 |
| 신규·도용 계정 | 계정 생성 후 레벨 10 미만·7일 미만이면 등록 불가(구매는 가능) |
- 모든 거래는 `auction_trades` + `gold_logs` + `item_logs`에 남아 CS·제재 근거가 된다.

## 9. 우편함
- 탭: 전체 / 판매 대금 / 아이템. 행: 종류 아이콘, 내용("철 소드 +7 판매 대금 11,400G"), 받은 날짜, 남은 보관 기간(30일), [받기].
- [모두 받기]: 가방 칸이 모자라면 들어가는 만큼만 받고 "가방이 가득 찼습니다".
- 새 우편이 오면 사이드 메뉴 경매장 아이콘에 빨간 점.

## 10. 클라이언트 UI (오프라인 미리보기)
- 사이드 메뉴 **"경매장"** → `AuctionScreen` (탭: 검색 / 내 등록 / 등록하기 / 우편함). 화면 위에 "오프라인 미리보기 (서버 연결 전)".
- 검색 탭: 검색창 + 분류·등급·강화 범위·정렬 버튼, 결과 목록(아이콘·이름 등급색·+강화·가격·판매자·남은 시간), 행마다 [즉시 구매] [입찰] → `UIRoot.Confirm(..., overlay:true)`.
- 등록 탭: 가방의 거래 가능 아이템 목록 → 선택 → 가격(±버튼)·기간(12/24/48) → 보증금·수수료·예상 수령액 미리보기 → [등록].
- 목업 `MockAuctionService`는 메모리 안에서 동작하며, 구매하면 실제 오프라인 가방의 골드를 빼고 아이템을 우편함에 넣는다(받기 → 가방). 서버 연결 시 같은 인터페이스의 `ServerAuctionService`로 바꾼다.
