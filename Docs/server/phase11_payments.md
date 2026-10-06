# 서버 11단계 설계: 별조각(유료 재화) Steam 결제와 결제 보호

기준: [PLAN_MONETIZATION.md](../PLAN_MONETIZATION.md)(별조각 캐시샵, "실제 결제는 Steam 연동 뒤 `star_ledger.reason = 'purchase'`"), [RESEARCH_MONETIZATION.md](../RESEARCH_MONETIZATION.md)(Steam 구현 가이드의 주문 생성 -> 이용자 승인 -> 확정 -> 지급, 환불 상태 추적), [PLAN_ANTI_ABUSE.md](../PLAN_ANTI_ABUSE.md) B10(결제·환불 미구현), 클라이언트 계약 `Assets/Scripts/Runtime/Commerce/ICommerceProvider.cs`. 앞 단계: [phase1_2_api.md](phase1_2_api.md)(응답 형식, 멱등성, Steam 로그인), [phase3_api.md](phase3_api.md)(`EconCtx`, 원장), [phase7_ops.md](phase7_ops.md)(관리자, 감사 로그, 작업, 경보), [phase9_anti_abuse.md](phase9_anti_abuse.md)(경제 정지 `economy_holds`, 기기·연결 계정, 계정 귀속), [phase10_sweep_mail.md](phase10_sweep_mail.md)(2인 승인 캠페인 패턴). 마이그레이션: `server/migrations/0023_payments.sql`(이 문서 14절이 초안, 정본은 구현 때 만든다), 스키마: `server/schema.sql`(구현 때 맨 아래에 합친다).

이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만들고, Unity 클라이언트는 메인이 따로 한다(15절이 그 경계). 게임 데이터(별조각 가격표, 확률표)는 문서에 복사하지 않고 `starshopDefs.ts`, `server/data/star_products.json` 이름으로만 참조한다. 8절과 10절의 한도 숫자는 운영 정책 상수(환경변수 시작값)이며 상품 가격표가 정해지면 조정한다.

표기: **[확인]** = Steam API 이름·필드·값에서 내가 확신하지 못하는 것. 구현 시 Steamworks 문서(Microtransactions 구현 가이드, Web API 레퍼런스)와 샌드박스 실측으로 확인한다. 7.8절에 모아 둔다. 확인 표시 없이 쓴 Steam 용어는 사용자가 지정한 이름, 곧 서버 쪽 `ISteamMicroTxn`의 InitTxn / QueryTxn / FinalizeTxn과 클라이언트 쪽 `MicroTxnAuthorizationResponse` 콜백의 이름뿐이다. 그 호출의 파라미터·버전·상태 값은 모두 [확인] 대상이다.

## 1. 위협 -> 대책 (먼저 읽기)

| # | 위협(공격 시나리오) | 대책 | 절 |
|---|---|---|---|
| 1 | 가짜 "결제 성공" 신호·영수증으로 별조각을 받는다 | **클라이언트가 호출할 지급 API가 없다.** Steam 콜백·오버레이 닫힘은 "서버가 Steam에 다시 물어봐 달라"는 힌트(B3)일 뿐이고, 서버가 Steam에 직접 조회(QueryTxn)한 결과로만 지급한다 | 7 |
| 2 | 금액·수량·통화·상품을 바꿔 싸게 산다 | 요청에는 상품 id와 서버가 서명한 견적 참조뿐. InitTxn의 금액·통화는 서버 상품표에서. Steam이 돌려준 steamid·appid·금액·통화·상품·수량을 주문 스냅샷과 전부 대조하고 하나라도 다르면 지급하지 않고 경보 | 5, 7.3 |
| 3 | 남의 주문을 조회·확정한다(IDOR), 남의 Steam ID로 주문한다 | 주문은 소유 계정만 조회(남의 것은 404). Steam ID는 로그인 계정의 `auth_identities`에서만 얻고 요청으로 받지 않는다. 확정 직전에 steamid·appid를 다시 대조 | 7.3 |
| 4 | 같은 주문으로 두 번 지급(재전송, 동시 확정, 작업과 클라이언트의 경쟁) | 주문당 1회를 DB가 강제: `star_ledger`의 `ref`(주문 uuid) 부분 유일 인덱스, `star_paid_lots` PK, 주문 행 `FOR UPDATE` + 임대(lease) + 상태 조건부 UPDATE | 8, 6.4 |
| 5 | FinalizeTxn 직후 서버가 죽거나 응답이 사라져 돈만 나가고 지급이 안 된다 | `finalized` 상태가 남고 대사 작업이 다시 지급한다. InitTxn 직후 죽어도 `pending_init`을 QueryTxn으로 대사 | 7.4 |
| 6 | 결제 중 클라이언트가 꺼지거나 응답을 못 받는다 | 서버 작업이 열린 주문을 주기적으로 조회. 상점을 열 때(B1)와 복원(B6)이 새 견적 발행 전에 열린 주문을 먼저 대사 | 7.4, 15 |
| 7 | Web API 키가 유출된다 | 환경변수만, 인증용 키와 분리한 퍼블리셔 키, 키가 든 URL·응답 원문을 로그·오류에 남기지 않음, 거절(401/403) 즉시 critical 경보, Steam 일일 리포트에 우리 기록에 없는 주문이 있으면 키 유출 의심 경보 | 7.6 |
| 8 | 사서 뽑고 환불한다 | 환불·차지백 감지 시 남은 유료분 회수 + 이미 소비한 분은 부채(소비 차단, 모든 입금이 먼저 상환) + 반복 패턴은 한도 강화·결제 정지 | 9 |
| 9 | 지불 거절(차지백) | 결제 정지 + 경제 정지(9단계 holds, `kind='payment'`) + 검토 큐 + critical 경보. 얻은 것의 회수는 owner 승인 도구로만 | 9 |
| 10 | 도용 카드로 대량 구매 | 계정별 일·월 한도(신규·제한 단계는 더 낮게), 시간당 건수·최소 간격, 열린 주문 1개, 실패 급증 쿨다운. **한도 초과는 주문 생성 단계에서 거절** | 10 |
| 11 | 여러 계정이 같은 기기·묶음으로 구매(차지백 농장, 세탁) | 같은 기기·연결 계정에 차지백 이력이 있거나 구매 계정이 여럿이면 제한(restricted) 단계 + 검토 큐. 결제 수단은 Steam만 알아서 서버는 모른다(한계) | 10.5 |
| 12 | Steam 지갑 국가·통화를 바꿔 싼 지역 가격으로 구매 | 매 주문에 Steam이 알려 준 국가·통화를 기록, 허용 통화는 상품표에 있는 것만, 90일 안에 국가가 바뀐 횟수가 기준을 넘으면 restricted | 10.4 |
| 13 | 서버 코드 버그·삽입된 악성 코드가 임의로 별조각을 만든다 | 지급·소비 경로는 `starWallet` 모듈 하나(구조 테스트가 다른 파일의 `INSERT INTO star_ledger`를 금지). DB 제약 트리거가 **근거 행이 없는 양수 원장**을 거절(결제 주문, 승인된 운영 지급, 시험 스위치). 원장은 추가 전용 | 4 |
| 14 | 관리자 계정 탈취·내부자의 임의 지급 | 별조각 운영 지급은 작성(operator) + 승인(작성자 외 owner) + 1건·관리자별 일일·전체 일일 상한 + 사유 필수 + 감사 + DB CHECK(작성자 != 승인자). 우편 캠페인은 별조각을 첨부할 수 없다 | 12.1 |
| 15 | 시험용 지급 스크립트가 운영에서 실행된다 | 스테이지 가드 + DB 세션 설정이 없으면 `test_grant` 원장 INSERT가 트리거에서 거절 | 4 |
| 16 | 동시 소비로 별조각 복사·음수 | 계정 지갑 행 잠금 -> 유료 로트 잠금 -> CHECK, 소비는 원장 1줄 + 배분 줄을 한 트랜잭션에 | 8, 11 |
| 17 | 뽑기 결과·확률 조작, 표시와 다른 확률 | 판정은 이미 서버. 확률표를 버전별 내용 해시로 스냅샷(`star_rates_snapshots`)하고 같은 버전에 다른 내용이면 기동 거부. 뽑기마다 버전을 기록하고 클라이언트가 본 버전과 다르면 거절 | 11.4 |
| 18 | 탈취한 토큰·악성 코드가 지갑을 비운다 | 일일 별조각 소비 상한, 계정당 소비 속도 제한, 단일 세션(9단계), 부채 중 소비 차단. 결제 자체는 이용자의 Steam 오버레이 승인이 있어야 성립한다 | 11 |
| 19 | 시험 앱(480) 주문이 운영에 반영된다 | 주문에 `app_id` 저장·대조, 운영에서 480 거부(기존 기동 검사), 모드 `mock`은 운영에서 기동 거부 | 16 |
| 20 | Steam 장애·지연으로 이중 청구나 지급 누락 | InitTxn·FinalizeTxn은 맹목적으로 재시도하지 않고 QueryTxn으로 상태를 판별. 결제 전용 회로 차단(인증과 분리). 주문 생성 실패는 청구 없음 | 7.7 |
| 21 | 날짜 경계 조작으로 한도 초기화 | 일일 경계는 `resetBoundaries()` 하나(서버 시각), 월 한도는 롤링 30일(새 경계 함수 없음) | 3 |
| 22 | 응답·로그로 부정 탐지 기준이 샌다 | 플레이어 응답에 Steam 원문·정지 사유·내부 점수 없음. 실패는 4개 코드(`DECLINED`, `EXPIRED`, `NOT_COMPLETED`, `UNDER_REVIEW`)만 | 13 |
| 23 | 환불된 별조각으로 얻은 외형을 합성·분해해 세탁한다 | 모든 입금(분해 포함)이 부채를 먼저 갚고, 부채 중 뽑기·교환·선택·합성 차단, 차지백은 경제 정지로 분해·경매·판매·강화까지 차단 | 9.4 |
| 24 | 한도 검사와 주문 생성 사이 경쟁(동시 주문으로 한도 우회) | `accounts` 행 잠금 + "계정당 열린 주문 1개" 부분 유일 인덱스 | 10.1 |

남는 위험(숨기지 않는다): 차지백·환불은 보통 며칠~몇 주 뒤에 도착하므로 그 사이 소비된 별조각의 **경제적 손실은 막을 수 없다**. 이 설계는 (a) 계정별 한도로 손실 상한을 두고(상한 = 기간 한도 x 별조각 단가), (b) 소비분을 부채로 남겨 재발을 막고, (c) 반복 패턴은 결제 정지로 끊고, (d) 회수 도구로 되돌릴 길을 연다.

## 2. 핵심 설계와 달라진 점

### 2.1 핵심 설계

1. **결제 지급의 단일 경로: "Steam이 확정한 주문".** 별조각이 생기는 길은 두 개뿐이다: Steam이 승인·확정했고 서버가 직접 확인한 결제 주문(`reason='purchase'`), 그리고 owner 2인 승인을 거친 운영 지급(`reason='admin_grant'`). 나머지 입금 사유(`dismantle`, 시험용 `test_grant`)는 기존 기능이며 4절에서 봉쇄 범위를 정한다. 클라이언트가 호출할 수 있는 "지급" API는 존재하지 않는다.
2. **클라이언트는 행동만 보낸다.** 요청에는 `request_id`, 상품 id, 서버 서명 견적 참조, 주문 uuid뿐이다. 금액·수량·통화·확률·보상량·시간은 어떤 요청에도 없다. 클라이언트가 Steam 오버레이에서 받는 콜백도 서버에는 "다시 확인해 줘"라는 신호로만 전달된다.
3. **Steam이 진실의 원천(결제), DB가 진실의 원천(별조각).** 결제 상태는 항상 서버가 Steam에 물어 확인하고, 별조각 수량은 원장이 정본이다. 둘이 어긋나면 지급하지 않고 사람이 본다(`needs_review`).
4. **주문은 상태 기계다.** `pending_init -> created -> authorized -> finalized -> granted`, 실패는 `failed`/`expired`, 사후는 `refunded`/`chargeback`. 모든 전이는 임대(lease)를 잡은 하나의 작업자만 하고, 모든 전이는 불변 이벤트 로그에 한 줄 남는다.
5. **유료분과 무료분을 나눠 회계한다.** 지갑 총액은 `balance`를 그대로 두고(클라이언트 호환) `paid_balance`와 `debt`를 더한다. 유료분은 주문 단위 로트(`star_paid_lots`)로 추적해 환불된 주문의 남은 분만 정확히 회수한다. 소비는 **무료분 먼저, 유료분은 오래된 로트부터**(8.3절).
6. **음수 잔액은 허용하지 않는다(CHECK `balance >= 0` 유지).** 이미 쓴 분은 `debt` 열에 부채로 기록하고, 부채가 있으면 별조각 소비를 막고, 이후 모든 입금이 부채를 먼저 갚는다.
7. **차지백 = 결제 정지 + 경제 정지 + 검토 큐 + 경보.** 경제 정지는 9단계 `economy_holds`에 `kind='payment'`로 건다(모드와 무관하게 `active`, 연결 계정으로 자동 전파하지 않음, 해제는 owner만).
8. **새 기능은 모두 기능 플래그가 있고 기본은 꺼짐.** `PAYMENTS_ENABLED=false`면 주문 생성은 `503 FEATURE_DISABLED`. 이미 생긴 주문의 대사·환불 감시 작업은 플래그와 무관하게 돈다(결제 중인 돈을 놓치지 않기 위해).

### 2.2 PLAN과 앞 단계에서 달라지거나 보태는 것

| 항목 | 기존 | 이 설계 | 이유 |
|---|---|---|---|
| `PLAN_MONETIZATION`의 외형 직접 결제(`aura_sunset` 등 상품 id, `OwnedProductIds`) | 외형 상품을 플랫폼 결제로 직접 판매하는 구조 | 현금 결제 대상은 **별조각 묶음만**. 외형은 별조각으로 교환(이미 구현) | 구현이 별조각 경유로 바뀌었고, 현금 경로를 하나로 줄이는 편이 지급 경로·환불 회수를 단순하게 한다 |
| `ICommerceProvider.FetchAsync`의 `OwnedProductIds` | 소유권 스냅샷 | 별조각 묶음은 소모품이라 항상 빈 배열, 잔액은 기존 캐시샵 요약으로 | 15절 |
| 9단계 `economy_holds.kind` | `velocity`, `auction`, `linked`, `manual` | `payment` 추가, 활성 정지 유일 인덱스를 `(계정, 범위, kind='payment' 여부)`로 | 속도 정지가 먼저 걸려 있어도 차지백 정지가 그 안에 흡수되어 같이 풀리는 일을 막는다 |
| 9단계 H3(정지 해제) | operator 가능 | `kind='payment'`는 owner만 | 결제 정지는 돈 문제라 더 높은 권한 |
| 9단계 12.7 전파 | 정지 생성 때 연결 계정으로 전파 | `kind='payment'`는 전파하지 않고 연결 계정은 결제 제한 단계 + 검토 큐만 | 같은 기기 오탐으로 무고한 계정의 경제 전체를 막지 않는다 |
| 9단계 12.5 정지 대상 경로 | 7개 | 주문 생성(B2)과 별조각 합성·선택 추가(이미 포함 확인) | 정지된 계정이 새 돈을 들이지 못하게 |
| `star_ledger.reason` | `purchase`, `test_grant`, `gacha`, `gacha_refund`, `exchange`, `refund_revoke`, `dismantle` | `chargeback_revoke`, `debt_settle`, `debt_forgive`, `admin_grant` 추가, `gacha_refund`는 새 입금을 거절 | 4절, 8절 |
| `star_ledger` | 변경·삭제 가능(트리거 없음) | 추가 전용 트리거 + 사유별 부호 CHECK + 근거 행 확인 트리거 | 재화 원장 원칙(PLAN_SERVER 5절, 원장은 추가만) |
| PLAN_SERVER 9절 단계 표 | 1~8단계 | 9, 10단계가 문서로 추가되어 있고 이 문서가 11단계 | 사용자 지시로 추가한 단계. PLAN_SERVER 진행 현황 표에 행을 더해야 한다(이 작업에서는 수정하지 않음) |

### 2.3 이 문서가 미루는 것

- 모바일 스토어(Apple/Google) 결제: PLAN_MONETIZATION 3절의 후속. 이 단계는 Steam만. 주문 표의 `platform` 열은 두지 않고 모바일은 별도 마이그레이션으로 확장한다(Steam 전용 열 `steam_*`가 많아 억지로 일반화하면 검증이 약해진다).
- 외형·구독·패스의 직접 현금 판매, 선물하기, 별조각 계정 간 이전: 하지 않는다(경로가 늘수록 방어면이 는다).
- 뽑기 장비의 NPC 판매 경로(별조각 -> 장비 -> 골드): 스택 단위 재고에 출처가 없어 서버만으로 구분할 수 없다. 결정 대기 D17, 측정 후 판단.
- 미성년자 결제 한도, 지역별 확률 공개 법령 적용 범위: Steam 지갑 정책과 법무 확인 사항이며 서버가 판정하지 않는다. 서버는 확률표를 버전별로 보존·내보내기만 한다(11.4).
- 자동 차지백 분쟁 대응 서류 제출: 운영 절차(Steamworks 파트너 사이트)다.

## 3. 공통 규칙

1~2단계 0.1~0.4(응답 형식 `{ success, message, data, meta? }` / 실패 `{ success: false, message, errors? }`, 인증, 버전 헤더, 멱등성), 3단계 0.2~0.3(락, `delta`), 9단계 1절(모드 환경변수, `ECONOMY_HOLD` 응답 본문), 10단계 1절(관리자 `runAdminAction`, 2인 확인)을 그대로 쓴다. 이 단계에서 달라지는 것만 적는다.

- **새 REST 경로**: 플레이어는 `/payments/...`(계정 단위, 인증 + 클라이언트 버전 검사, 캐릭터·데이터 버전과 무관), 관리자는 `/admin/payments/...`(7단계 관리자 listener). 기존 캐시샵은 `/characters/{uuid}/starshop/...` 그대로.
- **멱등성**:
  - B2(주문 생성)는 `star_orders (account_id, request_id)` 유일 제약과 `request_hash`로 한다(외부 호출이 끼어 `request_log` 한 트랜잭션 저장이 맞지 않는다). 같은 `request_id`에 같은 본문은 현재 주문 상태를 돌려주고, 다른 본문은 `422 IDEMPOTENCY_MISMATCH`.
  - B3(sync), B1, B4~B6은 "서버 상태 기계가 멱등이라 request_id를 받지 않는 상태 신호/조회"다(프레즌스와 같은 이유). 지급의 멱등성은 DB 제약이 보장한다(8.4).
  - 별조각 소비(뽑기 등)는 기존 `runEconomy`의 `request_log`.
  - 관리자 변경 요청은 `runAdminAction`의 `(admin_id, request_id)`.
- **락 순서**(9·10단계 위에 이어 붙인다): ① 캐릭터 행(소비 경로만) ② `accounts` 행(주문 생성, 운영 지급 승인) ③ `star_orders` 또는 `star_admin_grants` 행(한 요청에서 둘을 같이 잠그지 않는다) ④ `star_wallets` 행 ⑤ `star_paid_lots` 행(`created_at, order_id` 오름차순) ⑥ `economy_holds`, `payment_profiles`. 소비는 ① -> ④ -> ⑤, 지급·회수는 ③ -> ④ -> ⑤라 순환이 없다. 소비 경로는 주문 행을 잠그지 않는다.
- **외부 호출은 DB 트랜잭션 밖에서 한다.** Steam 호출(최대 8초)을 행 잠금을 쥔 채 기다리지 않는다. 대신 주문 행의 **임대**(`lease_until`, `lease_token`, 30초)로 한 작업자만 주문을 진행하게 하고, 이후 쓰기는 `WHERE lease_token = $token AND state = $기대상태`로 펜싱한다(6.4).
- **날짜 경계**: 일일(06:00 KST)은 `utils/resetBoundaries.ts`의 `resetBoundaries(now).dailyStartAt`/`nextDailyAt` 하나만 쓴다. 이 단계의 사용처: 일일 결제 한도, 일일 별조각 소비 상한. 월 한도·시간당 건수·환불 90일·국가 변경 90일은 **롤링 창**이라 새 경계 함수를 만들지 않는다(9단계 1절과 같은 방식). KST를 직접 계산하는 코드를 새로 쓰지 않는다.
- **새 에러 코드**(403 권한·정지 / 404 없음 / 409 상태 충돌 / 410 만료 / 422 규칙 위반 / 429 속도 / 503 꺼짐·Steam 불가):

| 코드 | 상태 | 어디서 | 뜻 |
|---|---|---|---|
| `FEATURE_DISABLED` | 503 | B1, B2 | `PAYMENTS_ENABLED=false`(기존 코드 재사용) |
| `STEAM_UNAVAILABLE` | 503 | B1, B2, B3 | Steam 결제 API에 닿지 못함, 회로 차단 중(기존 코드 재사용, `errors:{ retry_after_sec }`) |
| `NOT_STEAM_ACCOUNT` | 403 | B1, B2 | 로그인 계정에 Steam 신원이 없음(개발용 로그인) |
| `PAYMENT_BLOCKED` | 403 | B1, B2 | 결제 정지(문구는 "결제 이용이 제한되었습니다. 문의해 주세요", 사유 없음) |
| `ECONOMY_HOLD` | 403 | B2 | 계정에 활성 경제 정지가 있다(기존 코드) |
| `PRODUCT_NOT_FOUND` | 404 | B2 | 상품 id 없음 또는 판매 중 아님 |
| `ORDER_NOT_FOUND` | 404 | B3~B5 | 주문 없음 또는 내 것이 아님(구분하지 않는다) |
| `QUOTE_EXPIRED` | 410 | B2 | 견적 유효 시간 지남 |
| `QUOTE_CHANGED` | 409 | B2 | 견적 서명 불일치, 계정·상품·통화·가격·상품표 버전이 달라짐 |
| `CURRENCY_UNSUPPORTED` | 422 | B1, B2 | Steam 지갑 통화가 상품표에 없음 |
| `ORDER_IN_PROGRESS` | 409 | B2 | 이 계정에 열린 주문이 있다(`errors:{ order_id }`로 sync 안내) |
| `ORDER_TOO_FAST` | 429 | B2 | 최소 간격·시간당 건수·실패 쿨다운(`errors:{ retry_after_sec }`) |
| `PAY_LIMIT_EXCEEDED` | 422 | B2 | 일·월 한도 초과(`errors:{ scope:'daily'|'monthly', limit, used, resets_at }`, 본인 수치라 노출한다) |
| `STAR_DEBT` | 403 | 뽑기·교환·선택·합성 | 환불된 결제분이 있어 별조각 사용이 막혀 있다(`errors:{ debt }`) |
| `STAR_SPEND_CAP` | 422 | 뽑기·교환 | 일일 소비 상한 초과(`errors:{ limit, used, resets_at }`) |
| `RATES_CHANGED` | 409 | 뽑기 | 클라이언트가 본 확률표 버전과 서버 현재 버전이 다름(`errors:{ rates_version }`) |
| `GRANT_STATE` | 409 | PA10~PA12 | 이 상태에서는 할 수 없는 동작 |
| `GRANT_SELF_APPROVAL` | 403 | PA11 | 작성자는 승인할 수 없다 |
| `GRANT_LIMIT` | 422 | PA9, PA11 | 1건·관리자별 일일·전체 일일 상한 초과(`errors:{ field }`) |
| `GRANT_NEEDS_TWO_OWNERS` | 409 | PA9 | 활성 owner가 2명 미만 |
| `PAYMENT_STATE` | 409 | PA3, PA5, PA6, PA13 | 이 상태에서는 할 수 없는 관리자 동작 |

기존 코드 재사용: `IDEMPOTENCY_MISMATCH`(422), `ACCOUNT_BANNED`(403), `RATE_LIMITED`(429), `VALIDATION`(400), `426`(버전).

## 4. 현재 별조각 경로 전수와 지급 경로 봉쇄

2026-10-06 기준 `server/src`에서 `star_wallets`·`star_ledger`를 만지는 곳은 캐시샵 도메인(`domains/starshop/`)과 시험 스크립트 `scripts/test-stars.ts`뿐이고, 업적(`achievementRepository`)은 읽기만 한다. **관리자 경로에는 별조각 지급이 없다**(`server/src/admin/**` 확인, 운영 지급 `createGrant`는 골드·아이템 우편, 캠페인 첨부 종류는 `gold`/`item`/`sweep_ticket`). 모든 입금·출금 사유:

| 방향 | 사유(`star_ledger.reason`) | 지금 호출처 | 이 단계 이후 |
|---|---|---|---|
| 입금 | `purchase` | 없음(정의만) | **8.2절 지급 함수 하나**. DB 트리거: `granted` 이후 상태의 주문(같은 계정, `stars = delta`)이 없으면 거절 |
| 입금 | `admin_grant` | 없음 | **PA11 승인 트랜잭션 하나**. DB 트리거: `state='applied'`인 `star_admin_grants`(kind `grant`)가 없으면 거절 |
| 입금 | `dismantle` | `starshopSynth.dismantle` | 유지(무료 입금, 여분 외형을 소모하는 같은 트랜잭션). 부채 상환 규칙 적용 |
| 입금 | `test_grant` | `scripts/test-stars.ts`(`DEPLOY_STAGE=test` 가드) | 가드 유지 + DB 세션 설정 `dotrpg.allow_test_grant='on'`이 없으면 트리거가 거절. 스크립트는 `SET LOCAL`로 켠다 |
| 입금 | `gacha_refund` | `starshopService.pull`(환급이 0이라 실제로는 도달하지 않음) | 호출 제거. 트리거가 새 INSERT를 거절(과거 행은 보존) |
| 회수 | `refund_revoke`, `chargeback_revoke` | 없음(`refund_revoke`는 정의만) | **9.3절 회수 함수 하나**. DB 트리거: `refunded`/`chargeback` 주문이 없으면 거절 |
| 상환 | `debt_settle` | 없음 | 입금 함수가 같은 트랜잭션에서 자동 기록(8.5) |
| 탕감 | `debt_forgive` | 없음 | PA11 승인(kind `debt_forgive`)만 |
| 출금 | `gacha`, `exchange` | `starshopService.pull`, `exchange` | 같은 모듈의 소비 함수로 교체(8.3) |

**지급 경로의 단일화 규칙**

1. `domains/starshop/starWallet.ts`(신규)가 `INSERT INTO star_ledger`, `UPDATE star_wallets SET balance|paid_balance|debt`를 하는 **유일한 파일**이다. 공개 함수는 사유별로 나뉜다: `creditPaid`(주문 지급 전용), `creditFree`(`dismantle`/`admin_grant`/`test_grant`), `debit`(`gacha`/`exchange`), `reversePaid`, `forgiveDebt`. 사유 문자열을 받는 범용 `changeBalance`는 삭제한다(현재 `starshopRepository.changeBalance`가 임의 사유·임의 부호를 받는다).
2. **구조 테스트**(`test/starWalletOnly.test.ts`): `server/src`와 `server/scripts` 전체에서 `INSERT INTO star_ledger`와 `UPDATE star_wallets`를 포함한 파일이 `starWallet.ts` 하나뿐인지 검사한다. 새 파일이 지급 SQL을 쓰면 테스트가 깨진다.
3. DB가 마지막 방어선이다(14절): 부호 CHECK, 근거 행 트리거, 추가 전용 트리거. 앱 코드가 뚫려도 근거 없는 별조각은 커밋되지 않는다. 같은 권한으로 DB에 직접 붙은 공격자는 막지 못한다는 한계는 7단계 운영(DB 접근 통제, 백업)의 몫이다.

## 5. 상품표와 견적

### 5.1 상품표 `server/data/star_products.json`

서버가 읽는 사람 관리 데이터다. 값은 문서에 복사하지 않는다(가격은 Steamworks 가격 설정과 같아야 하므로 운영이 한 곳에서 관리). 형태만 정한다:

```
{
  "version": "<상품표 버전 문자열>",
  "products": [
    { "id": "<소문자 영숫자·밑줄 3~40자>", "steam_item_id": <양의 정수>, "name": "<표시 이름>",
      "stars": <양의 정수>, "enabled": <boolean>, "sort": <정수>,
      "prices": { "<ISO 4217 통화 코드>": <통화 최소 단위 정수>, ... } }
  ]
}
```

- 서버 기동 때 zod로 검증하고 하나라도 어긋나면 기동 실패(운영) 또는 경고(개발): `id`·`steam_item_id` 유일, `stars > 0`, 가격 > 0, 통화 코드 3글자 대문자, 판매 중인 상품마다 가격 1개 이상.
- **보너스 별조각(무료 덤)은 두지 않는다**(D11 권장): 한 주문이 유료 로트 하나로 정확히 대응해야 환불 회수가 단순하다. 보너스가 필요하면 별도 운영 지급으로 한다.
- 이 파일은 클라이언트에 내려가지 않는다. 상품·가격 표시는 B1 응답이 정본이고, `data_version.json` 해시에 포함하지 않는다(가격 변경이 클라이언트 데이터 업데이트를 요구하지 않게).
- 상품표 버전(`version`)은 주문 행에 `catalog_version`으로 남는다. 과거 주문의 가격·별조각은 주문 행 스냅샷이 정본이며 이 파일이 바뀌어도 과거 주문 해석은 변하지 않는다.

### 5.2 견적(quote)

`ICommerceProvider`의 `QuoteId`는 "계정·상품·가격·만료에 묶인 서버 견적, 변경·만료 시 결제를 거절"이다. 서버는 **상태 없는 서명 토큰**으로 구현한다.

```
quote_id = "v1." + base64url(JSON{ a: 계정 uuid, p: 상품 id, c: 통화, m: 금액(최소 단위), s: 별조각 수, v: 상품표 버전, e: 만료 epoch초 }) + "." + base64url(HMAC-SHA256(PAYMENT_QUOTE_SECRET, "v1." + payload))
```

- B1이 발급(유효 `PAYMENT_QUOTE_TTL_SECONDS`, 기본 600). B2가 검증: 상수 시간 비교(`timingSafeEqual`), 계정 일치, 만료(`QUOTE_EXPIRED`), **서버가 지금 상품표와 Steam 통화로 다시 계산한 값과 토큰 값이 같은지**(`QUOTE_CHANGED`).
- 견적은 비밀이 아니라 **무결성 참조**다. 클라이언트가 값을 바꿔도 서명이 깨지고, 서명이 맞아도 서버는 토큰 안의 가격을 쓰지 않고 상품표에서 다시 읽는다(토큰은 "화면에 보여 준 것과 지금 서버가 계산한 것이 같은가"를 확인하는 용도).
- 한 견적으로 여러 번 주문할 수 있다(일회용이 아님). 주문 수는 열린 주문 1개 규칙·한도·속도 제한이 막는다.

## 6. 주문 상태 기계

### 6.1 상태

```
(없음) -P2-> pending_init -InitTxn OK-> created -Steam Approved-> authorized -FinalizeTxn OK + Succeeded-> finalized -지급-> granted
                 |                          |                         |                                                    |
                 +-> failed                 +-> failed / expired      +-> failed(blocked) / expired                        +-> refunded / chargeback (사후)
finalized, authorized, created에서도 Steam이 환불·차지백이라 하면 refunded / chargeback (지급 전이면 회수 없음)
```

| 상태 | 뜻 | 열린 주문? | 종결? |
|---|---|---|---|
| `pending_init` | 서버가 주문 행을 만들었고 InitTxn 결과를 아직 모른다 | 예 | 아니오 |
| `created` | InitTxn 성공. 이용자의 Steam 승인을 기다림 | 예 | 아니오 |
| `authorized` | Steam이 "이용자가 승인했다"고 알려 줌(QueryTxn). 아직 확정 전(청구 전 [확인]) | 예 | 아니오 |
| `finalized` | FinalizeTxn 성공 + QueryTxn이 성공(`Succeeded` [확인]) 확인. 지급 대기 | 예 | 아니오 |
| `granted` | 별조각 지급 완료(원장·로트·지갑) | 아니오 | 사후 감시 계속 |
| `failed` | 청구 없이 끝남(`fail_reason`: `init_rejected`, `init_lost`, `user_denied`, `steam_failed`, `blocked`, `mismatch`) | 아니오 | 예 |
| `expired` | 만료까지 승인되지 않음(청구 없음) | 아니오 | 예 |
| `refunded` | Steam이 환불함(지급했다면 회수 완료) | 아니오 | 예 |
| `chargeback` | 지불 거절(또는 부정 의심 환불). 지급했다면 회수 완료 + 계정 조치 | 아니오 | 예 |

- `needs_review`는 상태가 아니라 **별도 불리언**(`needs_review`, `review_reason`)이다. Steam은 성공이라는데 우리가 검증에서 어긋난 경우(금액 불일치 등) 상태를 `failed(mismatch)`로 닫되 `needs_review=true`로 두고 대사 일정을 유지한다. 이 주문은 청구되었을 수 있어 클라이언트에는 `UNDER_REVIEW`(청구 여부 불명)로 보인다.
- **계정당 열린 주문은 최대 1개**(`pending_init`, `created`, `authorized`, `finalized`): 부분 유일 인덱스 `star_orders_one_open`. 새 견적 발행(B1)과 새 주문(B2) 전에 열린 주문을 먼저 대사한다(`ICommerceProvider` 계약).

### 6.2 주문 생성(B2) 처리 순서

처음 실패하는 하나만 거절한다(위쪽이 싸다).

| # | 검사 | 실패 |
|---|---|---|
| 0 | `(account_id, request_id)`로 기존 주문 조회: 있으면 `request_hash` 비교 후 **현재 상태를 돌려주고 끝**(크래시 뒤 재시도가 만료된 견적 때문에 실패하지 않게 가장 먼저) | `422 IDEMPOTENCY_MISMATCH` |
| 1 | `PAYMENTS_ENABLED`와 `PAYMENTS_STEAM_MODE != off` | `503 FEATURE_DISABLED` |
| 2 | 계정 정지(기존 인증 미들웨어) | `403 ACCOUNT_BANNED` |
| 3 | `auth_identities`에 `provider='steam'` 신원이 있다(Steam ID = `subject`) | `403 NOT_STEAM_ACCOUNT` |
| 4 | `payment_profiles.status != 'blocked'` | `403 PAYMENT_BLOCKED` |
| 5 | 계정에 활성 경제 정지가 하나라도 없다(`economy_holds_account_active`, 캐릭터·계정 범위 불문) | `403 ECONOMY_HOLD` |
| 6 | 상품 존재·`enabled` | `404 PRODUCT_NOT_FOUND` |
| 7 | Steam에 이용자 국가·통화 조회(GetUserInfo [확인]), 통화가 상품 `prices`에 있다 | `503 STEAM_UNAVAILABLE` / `422 CURRENCY_UNSUPPORTED` |
| 8 | 견적 서명·만료·내용(5.2) | `410 QUOTE_EXPIRED` / `409 QUOTE_CHANGED` |
| | **트랜잭션 1 시작, `accounts` 행 `FOR UPDATE`** | |
| 9 | 같은 `request_id` 재확인(동시 요청) | 0과 같음 |
| 10 | 열린 주문 없음 | `409 ORDER_IN_PROGRESS` |
| 11 | 속도: 마지막 주문 생성 후 `PAY_MIN_ORDER_GAP_SECONDS`, 최근 1시간 주문 수 `<= PAY_MAX_ORDERS_PER_HOUR`, 실패·만료 주문 1시간 `< PAY_FAIL_COOLDOWN_THRESHOLD` | `429 ORDER_TOO_FAST` |
| 12 | 단계(tier) 계산(10.2) + 일·월 한도(10.1) | `422 PAY_LIMIT_EXCEEDED` |
| 13 | 주문 행 INSERT(`pending_init`, 상품·가격·통화·별조각·Steam ID·appid 스냅샷, `steam_order_id = nextval('star_order_no_seq')`, 만료 = 지금 + `PAY_ORDER_EXPIRE_MINUTES`) + 이벤트 `created`. **커밋** | |
| 14 | (트랜잭션 밖) InitTxn 호출 | 결과별 6.3 |
| 15 | 트랜잭션 2: 결과 반영(`created` 또는 `failed`) + 이벤트 | |

13번을 먼저 커밋하는 이유: Steam에 주문을 보내기 **전에** 우리 쪽 의도를 남겨, InitTxn 응답을 놓치거나 서버가 죽어도 `pending_init` 행으로 Steam 쪽 주문을 찾아 정리할 수 있다. `steam_order_id`는 서버가 만든 순번이며 클라이언트가 정하지 않는다. 한 번 쓴 번호는 실패해도 재사용하지 않는다.

### 6.3 InitTxn 결과 처리

| 결과 | 처리 |
|---|---|
| Steam이 성공(`result=OK` [확인])을 돌려줌 | `created`, `init_at`, `steam_trans_id`(응답에 있으면 [확인]), 다음 대사 시각 = 지금 + 30초 |
| Steam이 명시적 거절(`result`가 OK 아님, 오류 코드 [확인]) | `failed(init_rejected)`. 청구 없음. 오류 코드는 이벤트에만 기록하고 플레이어에게는 `NOT_COMPLETED` |
| 시간 초과·5xx·연결 실패 | **재시도하지 않는다**(InitTxn은 비멱등, 같은 주문 번호를 다시 보내면 중복 오류가 날 수 있다). 상태는 `pending_init`으로 두고 응답은 `201`로 `state:'pending_init'`을 돌려준다. 클라이언트는 sync를 부르고, 서버는 즉시 QueryTxn(주문 번호)로 Steam에 그 주문이 있는지 판별한다: 있으면 `created`로 채택, 없다고 확실하면 `failed(init_lost)`, 모르겠으면 대사 작업이 계속 본다 |

### 6.4 진행 함수 `advanceOrder(orderId)`와 임대

B3(sync), 대사 작업, 관리자 재확인(PA3), B1·B6의 열린 주문 대사가 **모두 이 함수 하나**로 주문을 움직인다.

1. **임대 획득**(짧은 트랜잭션): `UPDATE star_orders SET lease_until = now() + 30s, lease_token = gen_random_uuid() WHERE id = $1 AND state IN (열린 상태 + needs_review) AND (lease_until IS NULL OR lease_until < now()) RETURNING ...`. 0행이면 다른 작업자가 진행 중이므로 현재 상태를 읽어 돌려주고 끝낸다(Steam을 부르지 않는다).
2. QueryTxn(트랜잭션 밖). 응답은 7.3 검증을 통과해야만 다음 단계로 간다. 결과의 화이트리스트 필드만 `star_order_events.detail`에 남긴다.
3. 상태 매핑(Steam status 문자열 [확인]):

| Steam 상태 | 현재 우리 상태 | 전이 |
|---|---|---|
| `Init` | `created` | 만료 시각이 지났으면 `expired`, 아니면 그대로(다음 대사 시각만 갱신) |
| `Approved` | `created` | `authorized` |
| `Approved` | `authorized` | 결제 정지·정지 계정이면 확정하지 않고 `failed(blocked)`(Steam 쪽은 확정되지 않아 청구되지 않는다 [확인]). 아니면 FinalizeTxn 호출(4번) |
| `Succeeded` | `created`/`authorized`/`finalized` | 7.3 검증 통과 시 `finalized`, 이어서 지급(8.2) |
| `Failed` | 열린 상태 | `failed(user_denied)` 또는 `failed(steam_failed)`(구분 코드 [확인]) |
| `Refunded`, `PartialRefund`, `Chargeback`, `RefundedSuspectedFraud`, `RefundedFriendlyFraud` | 열린 상태 | 지급 전이므로 회수 없이 `refunded`/`chargeback` |
| `Refunded` 등 | `granted` | 9절 회수 |
| 알 수 없는 문자열 | 모두 | 상태 변경 없음 + `needs_review` + 이벤트 + `unknown_steam_status` 경보 |

4. **확정**: FinalizeTxn(트랜잭션 밖, 재시도 없음). 성공이면 곧바로 QueryTxn으로 `Succeeded`를 확인하고 `finalized`로 전이한다. 실패·불명확하면 QueryTxn으로 판별(이미 확정되었는지 [확인])하고, 판별이 안 되면 `authorized`로 남겨 다음 대사 때 다시 한다(`attempts` 증가, `PAY_FINALIZE_MAX_ATTEMPTS` 초과 시 `needs_review`).
5. 이후 모든 쓰기는 `WHERE id = $1 AND lease_token = $2 AND state = $기대상태`(펜싱). 임대가 만료되어 다른 작업자가 가져갔다면 0행이라 아무것도 바꾸지 못한다.
6. 끝날 때 임대 해제.

## 7. Steam 연동

### 7.1 호출 목록 (서버가 Steam Web API 퍼블리셔 키로 호출)

| 호출 | 용도 | 언제 | 재시도 | 실패 시 |
|---|---|---|---|---|
| GetUserInfo [확인] | 이용자 Steam 지갑의 국가·통화 | B1, B2 | GET이라 2회 | `503 STEAM_UNAVAILABLE` |
| InitTxn | 주문 개시. 파라미터는 주문 번호(서버 생성), Steam ID(서버가 계정에서), appid(설정), 항목 1개(항목 id, 수량 1, 금액, 이름), 통화, 언어, `usersession=client` [확인] | B2 | **없음** | 6.3 |
| QueryTxn | 주문 상태·금액·통화·항목 조회(주문 번호로) | sync, 대사, 감시, 재확인 | 2회 | 상태 유지 후 다음 대사 |
| FinalizeTxn | 승인된 주문 확정(이 시점에 청구 [확인]) | 승인 확인 뒤 | **없음** | QueryTxn으로 판별 |
| GetReport [확인] | 기간별 주문 목록(일일 교차 점검) | `payment-report` 작업 | 2회 | 경보만, 상태 변경 없음 |
| RefundTxn | **사용하지 않는다.** 환불은 운영자가 Steamworks 파트너 사이트에서 하고, 서버는 결과를 QueryTxn으로 감지한다 | - | - | - |

호출 주소는 설정(`STEAM_PARTNER_API_BASE`, 기본값은 Steamworks 문서의 퍼블리셔 API 호스트 [확인])이다. 샌드박스 모드의 주소·방식도 [확인]. 요청 파라미터명과 버전 번호(v2/v3 등)는 구현 시 문서로 확인한다.

### 7.2 클라이언트 콜백의 위치

클라이언트에서 `MicroTxnAuthorizationResponse`(Steamworks 클라이언트 콜백, 앱 id·주문 번호·승인 여부 필드 [확인])가 오면 클라이언트는 **서버 주문 uuid로 B3(sync)를 호출**한다. 서버는 이 콜백의 내용(승인 여부 포함)을 믿지 않는다. 승인 여부를 클라이언트가 "false"로 보내도, "true"로 보내도 서버는 같은 일을 한다: Steam에 QueryTxn. 그래서 콜백을 위조·재전송·생략해도 결과가 달라지지 않고, 콜백 없이 앱이 꺼져도 대사 작업이 주문을 끝낸다. (Steam이 서버로 직접 알림을 보내는 웹훅은 없다고 가정하고 모든 상태를 우리가 Steam에 물어서 확인한다. 웹훅이 있어도 이 설계는 그대로 동작한다. [확인])

### 7.3 검증 체크리스트 (QueryTxn 응답 vs 주문 스냅샷, 하나라도 어긋나면 지급 안 함)

1. 응답의 주문 번호 = `steam_order_id`. (`steam_trans_id`가 이미 있으면 그것도 같아야 한다.)
2. 응답의 Steam ID = `star_orders.steam_id`, **그리고** 지금 `auth_identities`의 이 계정 Steam 신원 `subject`와 같다(주문 후 연결 변경 방지).
3. 응답의 appid = `STEAM_APP_ID`(= 주문 `app_id`). 운영(`DEPLOY_STAGE=live`)에서 480이면 기동 단계에서 이미 거부된다.
4. 항목이 정확히 1개이고 항목 id = `steam_item_id`, 수량 = 1.
5. 항목 금액 = `amount_minor`(통화 최소 단위, 세금 포함 여부는 샌드박스에서 InitTxn 금액과 QueryTxn 금액을 실측해 규칙 확정 [확인]).
6. 통화 = 주문 `currency`.
7. 상태 문자열이 알려진 값.
8. 국가를 돌려주면 `steam_country`에 기록, 이전 주문 국가와 다르면 10.4의 플래그.
9. 검증 실패 + 상태가 아직 `Succeeded`가 아님: 확정하지 않는다. `failed(mismatch)` + `needs_review` + 플래그(`amount_mismatch`, `steamid_mismatch`, `appid_mismatch`) + critical 경보. 검증 실패 + 이미 `Succeeded`(청구됨): 지급하지 않고 같은 처리 + `UNDER_REVIEW`. 운영자가 파트너 사이트에서 환불하면 감시가 `Refunded`를 확인한다.

### 7.4 대사(복원) 작업

| 작업 (`job_runs.job`) | 주기 | 대상 | 하는 일 |
|---|---|---|---|
| `payment-reconcile` | 60초 | 열린 주문 중 `next_check_at <= now`(`pending_init`는 생성 30초 뒤부터) | `advanceOrder`. 만료 시각을 넘긴 `created`는 `expired`. `pending_init`이 `PAY_INIT_LOST_MINUTES`(10) 넘도록 Steam에 없으면 `failed(init_lost)`. `finalized`는 지급 재시도 |
| `payment-watch` | 5분 | `granted` 주문 중 `next_check_at <= now` | QueryTxn으로 환불·차지백 감지(9.1). 점검 간격은 주문 나이로: 3일까지 15분, 30일까지 6시간, `PAY_WATCH_DAYS`(180)까지 24시간, 이후 점검 종료 |
| `payment-report` | 1일 | 최근 `PAY_REPORT_DAYS`(7)일 | GetReport [확인]로 Steam이 보는 우리 앱 주문 목록을 가져와 우리 `star_orders`와 대조: (a) Steam에는 있고 우리에는 없는 주문 = `unknown_steam_order`(critical, 키 유출 의심), (b) 상태가 다른 주문 = 재확인 대기열에 넣기, (c) 금액 합계 불일치 |
| `star-grant-expire` | 1시간 | 만료된 `pending` 운영 지급 | `expired`로 닫기 |

- 모든 Steam 호출은 작업 단위 속도 제한(`PAY_STEAM_MAX_CALLS_PER_MIN`, 기본 60)을 지킨다. 대사가 Steam을 때리지 않게 한다.
- 로그인(`/auth/steam`) 직후와 B1·B6 호출은 그 계정의 열린 주문 1개를 즉시 대사한다(전체 대기열을 기다리지 않는다).
- 작업이 놓친 주문이 없는지는 정합성 점검 I6이 본다(12.3).

### 7.5 환불·차지백 감지

9.1절. 요약: 1차는 `payment-watch`가 `granted` 주문을 나이별 간격으로 QueryTxn, 2차는 `payment-report`가 리포트로 교차 확인.

### 7.6 키 보관과 로그

- 키는 환경변수 `STEAM_PUBLISHER_API_KEY`(운영 필수, 기존 로그인용 `STEAM_WEB_API_KEY`와 **분리**: 노출 범위를 줄이고 한쪽이 거절되어도 다른 기능이 살아 있다. 같은 키만 쓸 수 있다면 [확인] 그 키를 쓰되 별도 이름의 설정으로 읽는다). 파일·DB·응답·감사 로그에 키를 두지 않는다.
- 호출 래퍼 `partnerCall(method, params)`: POST는 키를 **본문**(form)에 담는다. GET이 키를 쿼리로 실어야 한다면 URL을 어떤 로그·오류 메시지·예외 `message`에도 넣지 않는다. 래퍼가 오류를 잡아 `PartnerCallError(code, httpStatus)`처럼 키가 없는 오류로 다시 던진다. 로그는 호출 이름·주문 uuid·소요 시간·결과 분류만.
- `LOG_REDACT`에 `STEAM_PUBLISHER_API_KEY`, `PAYMENT_QUOTE_SECRET`, `*.key`, `*.steam_key`를 더한다(기존 목록에 `STEAM_WEB_API_KEY`가 이미 있다).
- 키 거절(401/403): critical 경보 `payment_key_rejected`(기존 `steam_auth_misconfigured`와 같은 방식), 주문 상태는 바꾸지 않는다. 키 교체는 새 값으로 배포(재시작).
- Steamworks에서 키 사용 IP를 제한할 수 있으면 서버 IP로 제한한다 [확인].
- 테스트: 오류를 주입한 호출의 로그 캡처에 키 문자열이 없다(18절 G).

### 7.7 장애와 회로 차단

- 결제 호출 전용 회로 차단: 연속 실패 `STEAM_BREAKER_FAILURES`(기존 값 재사용)번이면 `STEAM_BREAKER_OPEN_SECONDS`(기존) 동안 새 호출을 막는다. **인증용 회로와 카운터를 분리**한다(로그인 장애가 결제를 막거나 그 반대가 되지 않게).
- 열린 동안 B1·B2는 `503 STEAM_UNAVAILABLE`(`retry_after_sec`), 대사 작업은 호출을 건너뛰고 주문 상태를 유지한다(만료 시각이 지나도 Steam 확인 없이 `expired`로 닫지 않는다: 이미 승인되었을 수 있어서, 회로가 닫힌 뒤 확인하고 닫는다).
- 경보: `payment_steam_breaker`(warning), 열린 주문 정체 `payment_stuck`(critical, 12.4).

### 7.8 Steamworks 문서로 확인할 것 [확인] 목록

1. 퍼블리셔 API 호스트와 샌드박스 사용법(샌드박스 주문 흐름, 시험 앱).
2. InitTxn: 파라미터명·버전, `usersession` 값(`client`/`web`), `ipaddress` 필요 여부, 항목 배열 표기, `amount`의 단위(통화 최소 단위인지)와 세금 포함 여부, 통화·언어 코드 형식, 응답에 주문 번호·거래 번호가 오는지.
3. QueryTxn: 주문 번호/거래 번호 조회 지원, 응답 필드(Steam ID, appid, 국가, 통화, 시각, 항목 목록), **상태 문자열 전체**(내가 가정한 `Init`, `Approved`, `Succeeded`, `Failed`, `Refunded`, `PartialRefund`, `Chargeback`, `RefundedSuspectedFraud`, `RefundedFriendlyFraud`의 정확한 철자와 의미).
4. FinalizeTxn: 이미 확정된 주문에 다시 호출했을 때의 응답(멱등 여부), 승인 후 미확정 주문의 Steam 쪽 만료 규칙(청구되지 않는지, 몇 시간 뒤 무효인지).
5. GetUserInfo: 국가·통화·상태 반환, IP 인자 필요 여부, 이용자 거주 제한 상태.
6. GetReport: 형식, 반환 상태 값, 조회 기간·최대 건수, 환불·차지백 반영 지연.
7. 환불·차지백 정책: 환불 가능 조건(소비 여부 등), 차지백 통지 시점·지연, 부분 환불 발생 가능성.
8. `MicroTxnAuthorizationResponse` 콜백 필드명과 승인 거절 시 호출 여부, 오버레이가 닫힐 때 동작.
9. 퍼블리셔 키가 서버 IP 제한을 지원하는지, 키 교체 절차.
10. 주문 번호(uint64) 유일성 범위(앱별), 서버 재시작 후 번호 연속성 요구 여부.

## 8. 지급과 유료/무료 분리 회계

### 8.1 지갑 모델

| 열 | 뜻 | 불변식 |
|---|---|---|
| `star_wallets.balance`(기존) | 총 잔액(유료 + 무료) | `>= 0` |
| `star_wallets.paid_balance`(신규) | 그중 유료분 | `0 <= paid_balance <= balance`, `= SUM(star_paid_lots.remaining)` |
| `star_wallets.debt`(신규) | 환불된 결제분 중 이미 소비된 별조각 | `>= 0` |
| 무료분 | `balance - paid_balance` | 파생값 |

클라이언트가 읽는 `balance`는 그대로다. 요약(`GET starshop`)에 `paid_balance`, `free_balance`, `debt`, `spend_cap`을 **응답으로** 더한다(13.2).

### 8.2 지급 `grantOrder` (주문당 정확히 1회, 한 트랜잭션)

호출: `advanceOrder`가 `finalized`에 도달했을 때, 또는 대사 작업이 `finalized`로 남은 주문을 발견했을 때.

1. 주문 행 `FOR UPDATE`(락 ③). `state = 'finalized'`가 아니면 아무것도 하지 않는다(이미 `granted`면 멱등 성공).
2. 지급 조건 재확인: 마지막 QueryTxn 결과가 `Succeeded`이고 7.3 검증이 통과된 기록(이벤트)이 5분 안에 있다. 없으면 QueryTxn을 다시 한다(그 사이 환불되었을 수 있다). 환불 상태면 `refunded`로 닫고 지급하지 않는다.
3. 지갑 행을 만들고(없으면) 잠근다(락 ④).
4. `creditPaid`: 지갑 `balance += stars`, `paid_balance += stars`, 원장 `purchase`(`delta = +stars`, `paid_delta = +stars`, `ref = 주문 uuid`, `request_id = NULL`), 로트 `INSERT INTO star_paid_lots (order_id, account_id, granted = stars, remaining = stars)`.
5. 부채 상환(8.5): `debt > 0`이면 `applied = min(debt, stars)`만큼 `debt_settle` 원장 1줄(`delta = -applied`, `paid_delta = -applied`, `debt_delta = -applied`)과 배분 줄(로트 -applied)을 더 쓴다.
6. 주문 `state = 'granted'`, `granted_at = now()`, 이벤트 `granted`, 대사 일정 `next_check_at`을 감시 주기로.
7. 커밋. 응답·알림은 커밋 뒤.

**이중 지급을 막는 장치(겹겹)**

| 층 | 장치 |
|---|---|
| 상태 기계 | `state='finalized'`에서만 지급, 조건부 UPDATE, 임대 펜싱 |
| 행 잠금 | 주문 행 `FOR UPDATE`: 동시에 온 두 지급 호출 중 하나가 기다렸다가 `granted`를 보고 멱등 종료 |
| DB 유일 제약 | `star_ledger_purchase_uq`(`ref` 유일, `reason='purchase'`), `star_paid_lots` PK(`order_id`), `star_orders` `steam_order_id`·`steam_trans_id` 유일 |
| DB 트리거 | `purchase` 원장은 같은 계정의 `granted`/`refunded`/`chargeback`(`granted_at` 있음) 주문이 있고 `stars = delta`일 때만 커밋(지연 제약 트리거) |
| 정합성 점검 | 주문당 `purchase` 원장이 정확히 1줄(I6, 12.3) |

### 8.3 소비 `debit` (뽑기·교환)

`runEconomy`가 캐릭터 행을 잠근 뒤(락 ①) 기존처럼 `lockWallet`(락 ④)을 잡고 부른다.

1. 지갑 `balance >= price`(모자라면 기존 `NOT_ENOUGH_STARS`), `debt = 0`(아니면 `STAR_DEBT`, 11.2).
2. **무료분 먼저**: `fromFree = min(balance - paid_balance, price)`, `fromPaid = price - fromFree`.
3. `fromPaid > 0`이면 유료 로트를 `created_at, order_id` 오름차순으로 `FOR UPDATE`(락 ⑤) 읽으며 `remaining`을 차례로 깎는다. 읽은 로트 `remaining` 합이 `paid_balance`와 다르면 불변식 위반이므로 소비를 거절하고 `anomaly`와 critical 로그(`star_wallet_invariant`)를 남긴다.
4. 지갑 `balance -= price`, `paid_balance -= fromPaid`.
5. 원장 1줄(`gacha`/`exchange`, `delta = -price`, `paid_delta = -fromPaid`)과, `fromPaid > 0`이면 **배분 줄**(`star_spend_allocs`: 이 원장 줄이 어느 주문 로트에서 몇 개를 썼나)을 로트별로 쓴다.

**무료분 먼저를 권장하는 이유(D2)**: (a) 환불 대상이 되는 유료분이 오래 남아 환불·차지백 때 회수가 잘 되고 부채가 덜 생긴다. (b) 정직한 환불 요청자의 미사용 유료분이 보존된다. (c) 무료분(분해 입금 등)은 환불 대상이 아니라 먼저 써도 이용자에게 불리하지 않다. 유료분 안에서는 오래된 로트부터(FIFO)라 어느 주문의 별조각이 어디에 쓰였는지 배분 줄로 정확히 추적된다.

### 8.4 왜 로트와 배분이 필요한가

주문이 둘(A는 전부 소비, B는 전부 미사용)일 때 A만 환불되면 **B의 미사용분이 아니라 A의 소비분**이 부채가 되어야 한다. 합계 하나(`paid_balance`)만 있으면 B의 별조각이 A의 환불로 사라진다. 로트 단위로 추적해야 환불 회수가 정확하고, 배분 줄이 있어야 "환불된 주문의 별조각으로 얻은 것"(뽑기 결과, 교환 외형)을 원장에서 되짚는다(9.5).

### 8.5 부채 상환 규칙 (모든 입금)

`creditPaid`, `creditFree`(분해, 운영 지급, 시험 지급) 모두 입금 직후 같은 트랜잭션에서 `applied = min(debt, 입금액)`을 상환한다: `debt_settle` 원장 1줄(`delta = -applied`, `debt_delta = -applied`, 유료 입금이면 `paid_delta = -applied` + 로트 배분 줄, 무료 입금이면 `paid_delta = 0`). 입금 원장 줄의 `delta`는 입금액 전체로 남는다(감사에서 입금과 상환이 따로 보인다). 운영 지급(`admin_grant`)도 상환된다(보상을 주고 싶으면 먼저 `debt_forgive`, D4).

## 9. 환불·차지백 대응

### 9.1 감지

| 경로 | 방식 |
|---|---|
| `payment-watch`(1차) | `granted` 주문을 나이별 간격(7.4)으로 QueryTxn. 상태가 `Refunded`, `PartialRefund`, `Chargeback`, `RefundedSuspectedFraud`, `RefundedFriendlyFraud` [확인]로 바뀌면 `applyReversal` |
| `payment-report`(2차, 일 1회) | 리포트에서 우리가 `granted`로 알고 있는데 Steam이 환불·차지백이라는 주문을 재확인 대기열에 넣는다. 리포트에만 있는 주문은 `unknown_steam_order` |
| 열린 주문 대사 | 지급 전 주문이 환불되면 회수 없이 닫는다(6.4) |
| B3/B6 | 클라이언트가 부르면 그 주문을 즉시 재확인 |

환불·차지백 통지가 며칠 늦게 올 수 있으므로 감시 기간은 `PAY_WATCH_DAYS`(권장 180, D13).

### 9.2 정책표

| Steam 이벤트 [확인] | 주문 상태 | 유료분 회수 | 이미 쓴 분 | 계정 조치 | 큐·경보 |
|---|---|---|---|---|---|
| `Refunded` | `refunded` | 그 주문 로트의 **남은 분 전부** | **부채**(= 지급량 - 남은 분) | 기본은 없음. 소비율(부채/지급량) `>= PAY_REFUND_SPEND_RATIO`이면 `refund_after_spend` 플래그. 90일 안에 그런 환불이 `PAY_REFUND_RESTRICT_COUNT`(2)회면 restricted(30일), `PAY_REFUND_BLOCK_COUNT`(3)회면 결제 정지 | 소비율 기준 초과면 검토 큐. 24시간 환불 급증은 warning |
| `PartialRefund` | `refunded`(전량 취급) + `needs_review` | 같음 | 부채 | 큐 | 항상 큐(수량 1개 주문이라 정상 흐름에서는 나오지 않아야 한다) |
| `Chargeback`, `RefundedSuspectedFraud`, `RefundedFriendlyFraud` | `chargeback` | 같음 | 부채 | **결제 정지** + **경제 정지**(`economy_holds`, `kind='payment'`, `active`, 계정 범위) + 연결 계정은 restricted + 검토 | 항상 큐 + critical 경보 |
| 그 외 새로운 종결 상태 | 변경 없음 | 없음 | 없음 | 없음 | `needs_review` + 경보(7.8 확인 항목) |

`refunded`로 먼저 처리된 주문이 나중에 `Chargeback`이 되면 **회수는 이미 끝났으므로 다시 하지 않고**(회수 원장은 주문당 1줄, 유일 인덱스) 상태를 `chargeback`으로 올리고 차지백 조치만 추가한다. 반대(차지백 -> 환불)는 상태를 낮추지 않는다.

### 9.3 `applyReversal` (한 트랜잭션)

1. 주문 행 `FOR UPDATE`(락 ③). 이미 `refunded`/`chargeback`이면 위 승격 규칙만 적용하고 끝(멱등).
2. 지급 전 주문(`granted_at IS NULL`)이면 상태만 바꾸고 이벤트를 남긴다(회수 없음).
3. 지급된 주문: 지갑 잠금(락 ④), 로트 `FOR UPDATE`(락 ⑤). `taken = lot.remaining`, `owed = lot.granted - lot.remaining`.
4. 원장 `refund_revoke`(또는 `chargeback_revoke`) 1줄: `delta = -taken`, `paid_delta = -taken`, `debt_delta = +owed`, `ref = 주문 uuid`. 로트 `remaining = 0`, `revoked_at = now()`, 배분 줄(`taken`). 지갑 `balance -= taken`, `paid_balance -= taken`, `debt += owed`.
   - `taken <= lot.remaining <= paid_balance <= balance`가 불변식이라 **회수는 실패하지 않고 잔액이 음수가 되지 않는다.** 부족분은 `owed`(부채)로 남는다.
5. 주문 `state`, `reversed_at`, 이벤트(`refunded`/`chargeback`, `revoked_stars` + `taken`, `owed`).
6. 정책표의 계정 조치(9.2)를 같은 트랜잭션에서: 플래그, `payment_profiles` 갱신(결제 정지·`tier_floor`), 차지백이면 `economy_holds` INSERT(`kind='payment'`, `state='active'`, 모드와 무관. 같은 계정에 같은 종류의 활성 정지가 이미 있으면 건너뜀. **`holds.propagate`를 호출하지 않는다**).
7. 커밋 뒤 경보·로그(`payment.reversal`: 주문 uuid, 종류, `taken`, `owed`만).

### 9.4 부채의 효과와 해소

| 상태 | 별조각 소비(뽑기·교환·선택·합성) | 분해(입금) | 새 결제(B2) |
|---|---|---|---|
| `debt > 0` | 막힘(`STAR_DEBT`) | 허용, 입금이 부채를 먼저 상환 | 허용(차지백 정지가 아니면). 입금이 부채를 먼저 상환 |
| 결제 정지 | (부채와 무관) | | 막힘 |
| 경제 정지(`payment`) | 막힘 | **막힘**(분해로 별조각을 만들어 세탁하는 길 차단) | 막힘 |

해소: (1) 신규 입금이 자동 상환(8.5), (2) 운영 탕감 PA9(kind `debt_forgive`, 2인 승인, 전액만). 부채 열은 줄여서만 쓰고 늘리는 경로는 `applyReversal` 하나다.

### 9.5 "그 별조각으로 얻은 것"의 처리

배분 줄(`star_spend_allocs`) -> 소비 원장 줄(`gacha`/`exchange`) -> 결과: 뽑기는 `gacha_pulls`(`account_id`, `request_id`로 연결, 줄마다 `item_id`, `duplicate`, `kind`), 교환은 원장 `ref`(외형 id).

| 얻은 것 | 일반 환불 | 차지백 계열 |
|---|---|---|
| 외형(오라·스킨) | **유지**. 부채로 균형을 맞춘다 | 유지하되 **경제 정지로 합성·분해·선택·교환이 막혀** 더 늘지 않는다. 회수 여부는 운영 검토에서 결정(PA13, owner) |
| 외형 여분(`copies`) | 유지(분해는 부채 상환 입금이라 허용) | 분해 차단(경제 정지) |
| 장비(뽑기 장비, 계정 귀속) | 유지 | 경제 정지로 판매·경매·강화 차단. 회수 여부는 PA13 |
| 선택 게이지(`pity`, `skin_pity`) | 유지 | 선택(`claim`)이 막힘. 필요하면 PA13이 해당 뽑기 수만큼 게이지를 되돌린다 |
| 합성 결과 | 유지 | 합성 차단, 이미 한 합성은 PA13 대상 |

**자동 회수를 하지 않는 이유**: 정직한 환불 이용자(Steam이 승인한 환불)의 계정 물건을 서버가 자동으로 빼앗으면 오탐 피해가 크고 되돌리기 어렵다. 대신 (a) 부채가 재소비를 막고, (b) 반복하면 결제 정지, (c) 차지백은 경제 정지로 확산을 막고, (d) 확정적인 부정은 owner가 미리보기를 확인한 뒤 PA13으로 회수한다. 이 선택이 보수적이면서 안전한 이유와 남는 손실 상한은 1절 끝 문단과 D3.

**PA13 회수 도구(owner, 미리보기 후 적용)**: 선택한 주문의 배분 줄에서 소비 원장 줄을 찾고 그 결과를 계산한다. 외형 최초 획득(`duplicate=false`, 교환)은 `account_cosmetics` 행 삭제, 중복 획득은 `copies - 1`(0 미만이면 건너뜀), 장비는 가방·창고에 남아 있는 같은 키 수량만큼 `item_ledger(admin_clawback)`(9단계 H4와 같은 사유, 강화로 키가 바뀐 것은 추적하지 않고 `shortfall`로 보고), 게이지는 선택 항목. 적용은 미리보기 응답의 `preview_hash`를 함께 보내야 한다(본 것과 적용하는 것이 같음을 보장). 착용 중인 외형을 해제하는 방식(착용 상태가 서버에 어떻게 저장되는지)은 구현 시 확인한다. 부채 탕감은 별도 동작이다(PA9).

### 9.6 "사서 뽑고 환불" 패턴 탐지

| 신호 | 기준 | 조치 |
|---|---|---|
| 환불 시점 소비율 | `owed / granted >= PAY_REFUND_SPEND_RATIO`(0.5) | 플래그 `refund_after_spend`(severity 2) |
| 반복 | 90일 안에 위 환불이 2회 | restricted 30일 + 큐 |
| 반복 | 90일 안에 3회 | 결제 정지(사유 `refund_abuse`) + 큐 |
| 구매 직후 소진 후 환불 | 주문 지급 후 `PAY_BURN_MINUTES`(10) 안에 로트 90% 이상 소비되고 환불 | severity +1 |
| 연결 계정 | 같은 기기(공용 기기 제외)·같은 Steam 소유자의 다른 계정에 차지백/결제 정지가 있음 | restricted + 큐(`linked_chargeback`) |

## 10. 한도와 사기 결제 방지

### 10.1 일·월 한도 (주문 생성 단계에서 거절)

- 한도의 단위는 **별조각 수량**이다(통화가 여럿이라 금액으로 하면 환산이 필요하다). 상품 가격이 정해지면 별조각 단가로 금액 상한이 정해진다.
- 합산 대상: 이 계정의 주문 중 **`failed`·`expired`를 뺀 모든 주문**(진행 중·지급·환불·차지백 포함)의 `stars`. 환불된 주문도 센다("사서 환불하고 다시 사기" 반복이 한도를 우회하지 못하게).
- 창: 일일 = `resetBoundaries(now).dailyStartAt`부터 지금까지(06:00 KST 경계), 월 = 최근 30일 롤링. 검사 쿼리는 `star_orders_account_time (account_id, created_at DESC)`를 쓴다. 한도 + 지금 주문 별조각이 상한을 넘으면 `PAY_LIMIT_EXCEEDED`.
- 동시성: 주문 생성 트랜잭션이 `accounts` 행을 잠그고 합산하므로 같은 계정의 동시 주문은 직렬화된다. "열린 주문 1개" 부분 유일 인덱스가 이중으로 막는다.

### 10.2 단계(tier)와 시작값

주문마다 단계를 계산해 `star_orders.tier`에 기록한다. 제한이 겹치면 낮은 한도를 쓴다(restricted < new < standard).

| 단계 | 조건 | 일일(별조각) | 월(별조각) | 환경변수 |
|---|---|---|---|---|
| `restricted` | `payment_profiles.tier_floor='restricted'`이고 기간 안, 또는 90일 환불 소비 반복, 또는 국가 변경 초과, 또는 연결 계정 차지백, 또는 공용 아님 기기 공유 구매 계정 수 초과 | 1,000 | 3,000 | `PAY_LIMIT_RESTRICTED_DAILY_STARS`, `..._MONTHLY_STARS` |
| `new` | 계정 나이 < `PAY_NEW_ACCOUNT_DAYS`(14) | 3,000 | 10,000 | `PAY_LIMIT_NEW_DAILY_STARS`, `..._MONTHLY_STARS` |
| `standard` | 그 외 | 10,000 | 50,000 | `PAY_LIMIT_DAILY_STARS`, `..._MONTHLY_STARS` |

위 숫자는 **상품 가격표 확정 전 시작값(운영 전 확정)**이다. 기동 검사: 판매 중인 가장 큰 상품의 `stars`가 `new` 일일 한도보다 크면 경고(신규 계정이 그 상품을 살 수 없다). 소액 구매 위주로 상품을 구성하는 것을 권장한다(D11).

### 10.3 속도 제한

| 항목 | 기본 | 효과 |
|---|---|---|
| 열린 주문 | 계정당 1개 | `ORDER_IN_PROGRESS` |
| 최소 간격 | `PAY_MIN_ORDER_GAP_SECONDS`(60) | `ORDER_TOO_FAST` |
| 시간당 건수 | `PAY_MAX_ORDERS_PER_HOUR`(3) | `ORDER_TOO_FAST` |
| 실패 쿨다운 | 1시간에 실패·만료 `PAY_FAIL_COOLDOWN_THRESHOLD`(5)건 | 1시간 `ORDER_TOO_FAST` + 플래그 `fail_burst` |
| 요청 속도 | B2 계정당 분당 `RATE_PAY_ORDER_PER_MIN`(3) + IP당 `RATE_PAY_ORDER_IP_PER_MIN`(10) | `429 RATE_LIMITED` |

### 10.4 국가·통화 불일치 기록

- 매 주문에 `steam_country`(Steam이 알려 준 값 [확인])와 `currency`를 저장한다. `payment_profiles.last_country`, `last_currency`를 갱신한다.
- 허용 통화는 상품 `prices`의 키뿐(`CURRENCY_UNSUPPORTED`).
- 90일 안에 서로 다른 국가가 `PAY_COUNTRY_CHANGE_MAX`(1)번을 넘어 나타나면 플래그 `country_changed`(severity 1) + restricted 30일. 거절하지는 않는다(이사·여행의 정직한 사용자).
- 접속 IP의 국가와 비교하는 GeoIP는 서버에 데이터 원본이 없어 이 단계에 넣지 않는다. IP는 주문에 `ip`(INET)로 저장해 필요할 때 운영이 조회한다(보관: 일 1회 작업이 `PAY_IP_RETENTION_DAYS`(180) 지난 행의 `ip`를 NULL로).

### 10.5 다계정·공유 기기 신호

서버가 알 수 없는 것: 결제 수단(Steam이 가진다). 알 수 있는 것: 기기 해시, IP, Steam ID·소유자 ID, Steam이 알려 준 국가·통화.

- **같은 기기에서 구매한 계정 수**: 이 계정의 최근 14일 `account_devices` 기기마다, 같은 기기를 쓴 **다른 계정 중 최근 30일에 주문(실패·만료 제외)이 있는 계정 수**를 센다. 기기를 쓴 계정이 `HOLD_LINK_DEVICE_MAX_ACCOUNTS`(6)를 넘는 기기(공용 PC)는 건너뛴다. 수가 `PAY_SHARED_DEVICE_ACCOUNTS`(3) 이상이면 플래그 `shared_device`(severity 2) + restricted.
- **연결 계정 차지백**: 같은 비공용 기기, 같은 Steam 소유자 키(`steam_key`)로 묶인 계정에 `chargeback` 주문 또는 결제 정지가 있으면 `linked_chargeback` + restricted + 큐. **결제 주체는 `steam_id`(subject)**이고 패밀리 공유 소유자는 결제 주체가 아니므로 차지백 결제 정지를 소유자 기준으로 전파하지 않는다(대여받은 사람은 자기 지갑으로 결제한다).
- 한 Steam ID는 한 계정에만 연결된다(`auth_identities` 유일 제약)라 "같은 Steam 계정으로 여러 게임 계정이 결제"는 구조상 불가능하다.

## 11. 별조각 소비 경로 강화

### 11.1 현재 캐시샵 경로와 추가 검사

| 엔드포인트 | 기존 | 이 단계에서 추가 |
|---|---|---|
| `GET /characters/{uuid}/starshop` | 지갑·확률표·가격 | `paid_balance`, `free_balance`, `debt`, `spend_cap`, `payments.enabled` 응답 필드 |
| `POST .../pull` | 서버 RNG, 행 잠금, `assertNoHold` | `STAR_DEBT`, 일일 소비 상한, 확률표 버전 확인, 소비 함수 교체(8.3), 계정당 분당 소비 속도 제한 |
| `POST .../exchange` | 같음 | `STAR_DEBT`, 일일 소비 상한, 소비 함수 교체 |
| `POST .../claim` | 게이지(별조각 소비 없음) | `STAR_DEBT`(부채 중 선택 차단). 경제 정지는 기존 |
| `POST .../synth` | 여분 소비 | `STAR_DEBT` |
| `POST .../dismantle` | 여분 -> 별조각 | 입금 함수 교체(부채 상환). 경제 정지는 기존 |
| `POST .../collection` | 컬렉션 등록 | 변경 없음 |

### 11.2 부채 중 소비 차단

`assertNoStarDebt(client, accountId)`: 지갑 행을 이미 잠근 상태에서 `debt > 0`이면 `403 STAR_DEBT`. 호출 위치는 각 핸들러의 `assertNoHold` 바로 뒤.

### 11.3 일일 소비 상한과 속도 제한

- **일일 소비 상한**(`STAR_SPEND_DAILY_CAP`, 기본 30,000, 0이면 끔): 이 계정의 오늘(`dailyStartAt`부터) `gacha`·`exchange` 원장 `-delta` 합계 + 이번 소비액이 상한을 넘으면 `422 STAR_SPEND_CAP`(`errors:{ limit, used, resets_at }`). 지갑 행을 이미 잠가 경쟁이 없다. 인덱스: `star_ledger_spend_time (account_id, created_at) WHERE reason IN ('gacha','exchange')`.
- **목적**: 탈취한 토큰이나 악성 코드가 지갑을 하루에 전부 비우지 못하게 하고, 한 번에 큰 소비를 한 이용자의 실수·충동 피해도 줄인다. 상한이 정직한 큰 소비(게이지 채우기)를 막지 않도록 기본값은 일일 결제 한도보다 크게 잡았다(D6).
- **속도 제한**: 기존 캐릭터당 초당 3회에 더해 **계정당 분당** `RATE_STARSHOP_SPEND_PER_MIN`(30)을 소비 경로(pull, exchange)에 건다(매크로 대량 소비 방지).
- **하루 뽑기 횟수 상한은 두지 않는다**(D7 권장). 횟수는 소비 상한으로 충분히 묶이고, 횟수 상한은 정직한 이용자의 10+1 반복만 불편하게 한다.

### 11.4 확률표 버전 고정과 공개

- **스냅샷**: 서버가 기동할 때 현재 `RATES_VERSION`과 그 버전의 확률표 전체(오라·스킨·장비 뽑기, 레벨 단계별 등급·품목 확률, 천장·게이지·가격, 중복 규칙)를 JSON으로 만들고 SHA-256을 구해 `star_rates_snapshots (version, content, content_hash)`에 없으면 INSERT한다. **같은 `version`인데 해시가 다르면 기동 실패**(`RATES_VERSION_CONFLICT`: 확률을 바꾸고 버전을 올리지 않았다). 이 표는 추가 전용이다.
- **뽑기마다 버전 기록**: `gacha_pulls.rates_version`(기존)에 그 시점 버전이 이미 남는다. 분쟁 때 `rates_version`으로 스냅샷을 찾아 당시 확률을 증명한다.
- **표시한 버전 확인**: 뽑기 본문에 `rates_version`(클라이언트가 화면에 보여 준 확률표 버전)을 받는다. 서버 현재 버전과 다르면 `409 RATES_CHANGED`(`errors:{ rates_version }`). 이 값은 **지급 근거가 아니라 "이 표를 보고 사겠다"는 확인**이다. `STAR_RATES_ACK_REQUIRED=true`(결제를 켤 때 필수)면 없는 요청을 `400`으로 거절한다.
- **공개**: `GET starshop` 응답이 이미 확률표와 `rates_version`을 준다(화면 표시). 홈페이지 게시용 파일은 운영이 `scripts/export-rates.ts`(신규)로 스냅샷에서 내보낸다. **서버에 인증 없는 공개 엔드포인트는 만들지 않는다**(D15). 확률 공개 의무의 범위는 RESEARCH_MONETIZATION의 법령 항목을 따라 법무가 확인한다.

### 11.5 유료 별조각으로 얻은 것의 귀속·거래

| 결과물 | 거래 가능성 | 근거 |
|---|---|---|
| 외형(오라·스킨) | 불가(경매·우편 대상이 아님, 계정 소유 행) | `account_cosmetics`는 계정 단위이고 이전 경로가 없다 |
| 뽑기 장비 | 불가(계정 귀속, `ITEM_BOUND`) | 9단계 8.6(`bindFor`: `gacha` 장비는 `account`). 시험 데이터의 옛 스택은 소급 변경하지 않았으므로 **결제 개시 전에 옛 뽑기 장비 스택(귀속 없음)이 남아 있는지 점검하고 정리한다**(구현 체크리스트) |
| 별조각 자체 | 이전 불가 | 계정 간 전송 경로가 없다. 우편 첨부 종류에 없다 |
| 장비 NPC 판매 | 가능(미해결) | D17, 측정 후 판단 |

## 12. 감사와 운영

### 12.1 관리자 별조각 지급 (2인 승인, 상한, 감사)

기존에 관리자 별조각 경로가 없으므로 **새로 만들 때 가장 좁게** 만든다. 10단계 캠페인(MC1 작성 operator, MC4 승인 owner)과 같은 패턴이다.

| 장치 | 내용 |
|---|---|
| 작성·승인 분리 | PA9 작성(operator 이상), PA11 승인(**작성자 외 owner**). API가 `created_by <> 내 id`를 검사하고 DB CHECK(`created_by <> approved_by`)가 이중 장치 |
| 활성 owner 2명 | 작성 때 활성 owner가 2명 미만이면 `409 GRANT_NEEDS_TWO_OWNERS`(승인이 불가능한 지급을 만들지 않는다). 운영 전에 owner 계정 2개를 만든다 |
| 대상 | 계정 uuid 한 개. **대량 지급 없음, 우편 캠페인에 별조각 첨부 없음** |
| 상한 | 1건 `PAY_ADMIN_GRANT_MAX_STARS`(5,000), 작성 관리자별 일일 합계 `PAY_ADMIN_GRANT_DAILY_MAX_STARS_PER_ADMIN`(20,000), 전체 일일 합계 `PAY_ADMIN_GRANT_DAILY_MAX_STARS_TOTAL`(50,000). 일일 경계는 `resetBoundaries`. 작성 때와 승인 때 둘 다 검사 |
| 사유 | `memo`(1~200자) 필수, 선택적으로 근거 주문(`related_order_id`) |
| 내용 불변 | 만든 뒤 별조각 수·대상·사유·종류는 못 바꾼다(트리거). 삭제 불가 |
| 만료 | `PAY_ADMIN_GRANT_PENDING_HOURS`(24) 안에 승인되지 않으면 `expired` |
| 효과 | 승인 트랜잭션이 계정 행 잠금 -> 지갑 입금(`admin_grant`, **무료분**: 환불 대상이 아니다) -> `applied` 한 번. 원장 `ref`는 지급 uuid(유일 인덱스) |
| 부채 탕감 | 같은 표·같은 2인 승인(kind `debt_forgive`). 탕감 대상은 승인 시점의 **부채 전액**(금액을 입력하지 않는다) |
| 감사 | `admin_audit_log`(`target_type='star_grant'`), 지급 표, 원장. 같은 `request_id` 재전송은 같은 응답 |
| 평소 사용 | 이 경로는 비상용이다. 보상은 골드·아이템 우편 캠페인으로 하고 별조각 보상은 예외로만 한다 |

### 12.2 불변 결제 이벤트 원장

- `star_order_events`: 주문의 모든 전이·Steam 조회 결과·플래그·관리자 재확인을 한 줄씩. **추가 전용**(UPDATE/DELETE/TRUNCATE 트리거 차단). `detail`은 허용 목록 필드(Steam 상태 문자열, 주문·거래 번호, 통화, 금액, 국가, 시각, 사유 코드)만 담는다. 키·URL·원문 응답·IP는 넣지 않는다.
- `star_ledger`: 모든 별조각 증감(추가 전용 트리거 신규).
- `star_spend_allocs`: 유료 로트 배분(추가 전용).
- `admin_audit_log`: 관리자 행동(기존, 대상 종류 `payment_order`, `payment_account`, `star_grant`, `payment_flag` 추가).
- 보관: 위 표는 지우지 않는다(7단계 "지우지 않는다" 목록에 추가). 주문의 IP만 `PAY_IP_RETENTION_DAYS` 뒤 NULL. 법정 보관 기간은 법무 확인(D14).

### 12.3 정합성 점검 (`integrity-nightly`의 새 항목 I6, 불일치는 critical)

| 점검 | 식 |
|---|---|
| I6-1 지갑 | 계정별 `balance = SUM(star_ledger.delta) = 마지막 balance_after`, `paid_balance = SUM(paid_delta) = SUM(star_paid_lots.remaining)`, `debt = SUM(debt_delta)` |
| I6-2 주문당 1회 | `granted`·`refunded`/`chargeback`(지급됨) 주문마다 `purchase` 원장 정확히 1줄이고 `delta = stars`, 회수된 주문마다 회수 원장 정확히 1줄. 주문 없는 `purchase` 원장은 0줄 |
| I6-3 Steam 확정 금액 = 지급 별조각 x 단가 | 지급된 주문마다 `steam_amount_minor = amount_minor`이고 `(상품, 통화)`별로 `주문 수 x 상품표 가격 = SUM(steam_amount_minor)`, `주문 수 x 상품 별조각 = SUM(purchase 원장 delta)`(상품표 버전별로 계산, 과거 가격은 주문 스냅샷으로). 상품표가 바뀐 뒤 어긋나면 스냅샷 기준으로 본다 |
| I6-4 배분 | 원장 줄마다 `-paid_delta = SUM(star_spend_allocs.stars)`(`paid_delta < 0`인 줄) |
| I6-5 막힌 주문 | `pending_init` 10분 초과, `created`/`authorized` 만료 + 10분 초과, `finalized` 5분 초과가 0건 |
| I6-6 근거 없는 입금 | 트리거를 우회한 `purchase`·`admin_grant`·`test_grant` 입금 0건(트리거가 꺼진 DB 대비) |
| I6-7 Steam 교차 | 마지막 `payment-report` 결과의 `unknown_steam_order` 미해결 0건 |
| I6-8 부채 일관 | `debt > 0`인 계정마다 그 부채를 만든 회수 원장이 있다 |

### 12.4 경보 (`ops/alertRules.ts`의 `THRESHOLDS`에 상수로)

| 키 | 수준 | 조건 |
|---|---|---|
| `payment_chargeback` | critical | 최근 24시간 신규 `chargeback` >= 1 |
| `payment_unknown_order` | critical | `unknown_steam_order` 미해결 >= 1(키 유출 의심) |
| `payment_amount_mismatch` | critical | 최근 24시간 `amount_mismatch`/`steamid_mismatch`/`appid_mismatch` >= 1 |
| `payment_stuck` | critical | I6-5 정체 주문 >= 1 |
| `payment_key_rejected` | critical | 결제 호출이 401/403을 받음(최근 10분) |
| `payment_finalized_ungranted` | critical | `finalized`가 5분 넘게 `granted`가 되지 않음 |
| `payment_fail_rate` | warning | 최근 1시간 주문 >= 10건이고 실패·만료 비율 >= 30% |
| `payment_refund_spike` | warning | 최근 24시간 환불 >= 5건 또는 지급 대비 >= 20% |
| `payment_limit_hits` | warning | 최근 1시간 `PAY_LIMIT_EXCEEDED` 반복(한 계정 >= 3회, 또는 전체 >= 20회) |
| `payment_steam_breaker` | warning | 결제 회로 차단 중 |
| `payment_review_age` | warning | 열린 검토 큐 항목이 24시간 넘음 |
| `star_grant_pending_age` | warning | 승인 대기 운영 지급이 12시간 넘음 |
| 작업 정지 | warning | `payment-reconcile` 1시간, `payment-watch` 2시간, `payment-report` 30시간 동안 성공 없음(`jobStaleHours`에 추가) |

### 12.5 지표 (`ops/snapshot.ts`에 한 블록)

`payments: { enabled, orders_1h, granted_1h, failed_1h, expired_1h, open_orders, stuck_orders, refunds_24h, chargebacks_24h, review_open, holds_payment_active, debt_accounts, steam_breaker_open, key_rejected_recent }`.

### 12.6 운영 절차

- **환불 요청(고객 문의)**: 운영자가 Steamworks 파트너 사이트에서 환불 처리 -> 서버가 감시 주기에 감지 -> 자동 회수. 서버에 "환불하기" API는 없다(RefundTxn 미사용).
- **의심 주문**: PA7(큐)에서 확인 -> PA2(상세, 이벤트·배분·플래그) -> PA3(재확인) -> PA8(해결 기록).
- **결제 정지/해제**: PA5(operator 가능) / PA6(owner만, 차지백·환불 남용 사유는 해제 사유 필수).
- **회수**: PA13(owner, 미리보기 후 적용).
- **Steam 키 교체·유출 의심**: 새 키 배포, 기존 키 폐기(Steamworks), `payment-report` 수동 실행으로 미지 주문 확인.

## 13. API 명세

공통: 응답 `{ success, message, data, meta? }`, 실패 `{ success:false, message, errors? }`(`code`는 응답 본문 필드). 인증은 액세스 토큰(`Authorization: Bearer`), 헤더에 클라이언트 버전(426 규칙). 플레이어 `/payments/*`는 계정 단위라 캐릭터 경로가 아니다. 모든 시각은 ISO 8601 UTC.

### 13.1 플레이어 API 6개

| # | 메서드 | 경로 | 인증 | 하는 일 |
|---|---|---|---|---|
| B1 | GET | `/payments/products` | 액세스 | 판매 상품·견적·내 한도·열린 주문(열린 주문을 먼저 대사) |
| B2 | POST | `/payments/orders` | 액세스 | 주문 생성(InitTxn). Steam 오버레이가 이어서 뜬다 |
| B3 | POST | `/payments/orders/{uuid}/sync` | 액세스 | 이 주문을 Steam에 다시 확인하고 진행(콜백 직후, 대기 중 반복) |
| B4 | GET | `/payments/orders/{uuid}` | 액세스 | 내 주문 한 건 |
| B5 | GET | `/payments/orders` | 액세스 | 내 주문 이력 |
| B6 | POST | `/payments/reconcile` | 액세스 | 내 열린 주문을 모두 대사(`RestoreAsync`) |

**`OrderView`**(응답 공통 조각):

```json
{
  "id": "<uuid>",
  "state": "pending_init | created | authorized | finalized | granted | failed | expired | refunded | chargeback",
  "product_id": "...",
  "stars": 0,
  "currency": "KRW",
  "display_price": "...",
  "steam_order_id": "123456789",
  "created_at": "...", "expires_at": "...", "granted_at": null,
  "final": false,
  "charged": null,
  "fail_code": null
}
```

- `steam_order_id`는 uint64라 **문자열**로 준다(클라이언트가 콜백의 주문 번호와 맞출 때만 쓴다).
- `final`: 클라이언트가 더 기다리지 않아도 되는 상태(`granted`, `failed`, `expired`, `refunded`, `chargeback`).
- `charged`: `true` = Steam이 청구함(`finalized` 이후), `false` = 확실히 청구 없음(`failed`·`expired`, `needs_review` 아님), `null` = 아직 모르거나 확인 중.
- `fail_code`(플레이어용 4종만): `DECLINED`(승인하지 않음, 청구 없음), `EXPIRED`(시간 초과, 청구 없음), `NOT_COMPLETED`(완료되지 않음, 청구 없음), `UNDER_REVIEW`(확인 중, 청구되었을 수 있음 -> 문의 안내). 내부 `fail_reason`·정지 사유는 절대 싣지 않는다.

#### B1 `GET /payments/products`

- 처리: Steam 신원 확인 -> 열린 주문이 있으면 `advanceOrder` -> 국가·통화(GetUserInfo, 계정별 10분 메모리 캐시) -> 단계·한도 계산 -> 상품 목록.
- 응답 `200` `data`:

```json
{
  "server_time": "...",
  "tier": "new | standard | restricted",
  "country": "KR", "currency": "KRW",
  "open_order": null,
  "limits": {
    "daily":   { "limit": 0, "used": 0, "left": 0, "resets_at": "..." },
    "monthly": { "limit": 0, "used": 0, "left": 0 }
  },
  "offers": [
    { "product_id": "...", "name": "...", "stars": 0, "display_price": "...", "currency": "KRW",
      "quote_id": "v1....", "quote_expires_at": "...", "purchasable": true, "block": null }
  ]
}
```

- `block`: `null | 'OPEN_ORDER' | 'LIMIT_DAILY' | 'LIMIT_MONTHLY'`. 열린 주문이 있으면 `offers`는 비어 있고 `open_order`가 채워진다(`CosmeticStore`의 "이전 결제 확인 중" 흐름).
- 에러: `401`, `403 NOT_STEAM_ACCOUNT | PAYMENT_BLOCKED | ACCOUNT_BANNED`, `422 CURRENCY_UNSUPPORTED`, `429`, `503 FEATURE_DISABLED | STEAM_UNAVAILABLE`.
- 멱등성: 읽기(열린 주문 대사는 멱등). 속도 제한: 계정당 분당 `RATE_PAY_READ_PER_MIN`(30).

#### B2 `POST /payments/orders`

- 요청:

```
z.strictObject({
  request_id: z.uuid(),
  product_id: z.string().regex(/^[a-z0-9_]{3,40}$/),
  quote_id: z.string().min(20).max(512),   // B1이 준 서명 견적 참조
})
```

받지 않는 값: 금액, 수량, 통화, 별조각 수, Steam ID, 주문 번호, 시각.
- 처리: 6.2절.
- 응답 `201` `data`: `{ order: OrderView, next: 'wait_steam_authorization' | 'sync' }`. InitTxn이 성공하면 `order.state='created'`, `next='wait_steam_authorization'`(Steam 오버레이가 뜬다). 결과가 불명확하면 `state='pending_init'`, `next='sync'`. 같은 `request_id` 재전송은 같은 주문의 **현재** 상태로 `200`.
- 에러: `400`, `401`, `403 ACCOUNT_BANNED | NOT_STEAM_ACCOUNT | PAYMENT_BLOCKED | ECONOMY_HOLD`, `404 PRODUCT_NOT_FOUND`, `409 ORDER_IN_PROGRESS | QUOTE_CHANGED`, `410 QUOTE_EXPIRED`, `422 CURRENCY_UNSUPPORTED | PAY_LIMIT_EXCEEDED | IDEMPOTENCY_MISMATCH`, `429 RATE_LIMITED | ORDER_TOO_FAST`, `503 FEATURE_DISABLED | STEAM_UNAVAILABLE`.
- 멱등성: `request_id` + `UNIQUE (account_id, request_id)`.
- 속도 제한: 계정당 분당 `RATE_PAY_ORDER_PER_MIN`(3), IP당 분당 `RATE_PAY_ORDER_IP_PER_MIN`(10), 그리고 6.2의 11번.

#### B3 `POST /payments/orders/{uuid}/sync`

- 요청: 본문 `z.strictObject({})`(콜백의 승인 여부 등 어떤 값도 받지 않는다).
- 처리: `advanceOrder`(6.4). 같은 주문의 Steam 호출은 `PAY_SYNC_MIN_INTERVAL_SECONDS`(2) 안에는 반복하지 않고 현재 상태를 돌려준다(폭주 방지).
- 응답 `200` `data`: `{ order: OrderView, wallet: { balance, paid_balance, free_balance, debt } | null }`(`granted`일 때만 `wallet`).
- 에러: `401`, `404 ORDER_NOT_FOUND`(남의 주문 포함), `429`, `503 STEAM_UNAVAILABLE`(주문 상태는 바뀌지 않고, 클라이언트는 B4로 DB 상태를 읽을 수 있다).
- 멱등성: 상태 신호(서버 상태 기계가 멱등, 지급은 DB 제약). 속도 제한: 계정당 분당 `RATE_PAY_SYNC_PER_MIN`(30).

#### B4 `GET /payments/orders/{uuid}` / B5 `GET /payments/orders`

- B4 응답: `{ order: OrderView }`, DB 상태만(Steam 호출 없음).
- B5 쿼리 `limit`(1~50, 기본 20), `cursor`(불투명). 응답 `data: { items: OrderView[] }`, `meta: { next_cursor }`. 자기 주문만, `created_at DESC`.
- 속도 제한: B1과 같음.

#### B6 `POST /payments/reconcile`

- 요청: `{}`(strict). 처리: 내 열린 주문(최대 1개) 대사. 응답 `200` `data`: `{ resolved: OrderView[], open_order: OrderView | null }`. 에러·속도 제한은 B3과 같다.

### 13.2 기존 API 변경 (E1~E14)

| # | 대상 | 변경 | 절 |
|---|---|---|---|
| E1 | `GET /characters/{uuid}/starshop` | 응답에 `paid_balance`, `free_balance`, `debt`, `spend_cap: { limit, used, left, resets_at }`, `payments: { enabled }` | 8.1, 11.3 |
| E2 | `POST .../pull`, `.../exchange` | `STAR_DEBT`, `STAR_SPEND_CAP`, 계정당 소비 속도 제한, 소비 함수 `starWallet.debit`로 교체. 뽑기 본문에 `rates_version`(`STAR_RATES_ACK_REQUIRED` 때 필수), 불일치 `409 RATES_CHANGED` | 11 |
| E3 | `POST .../claim`, `.../synth` | `STAR_DEBT` | 11.2 |
| E4 | `POST .../dismantle` | 입금 함수 `starWallet.creditFree`로 교체(부채 상환) | 8.5 |
| E5 | `starshopRepository.changeBalance` | 삭제. 모든 지갑 변경은 `starWallet.ts`로 | 4 |
| E6 | `scripts/test-stars.ts` | `SET LOCAL dotrpg.allow_test_grant = 'on'`을 같은 트랜잭션에서, 스테이지 가드 유지 | 4 |
| E7 | `economy_holds`, H3 | `kind='payment'` 추가, 유일 인덱스 변경, H3은 payment 정지를 owner만 해제, `propagate` 호출 안 함 | 2.2, 9.3 |
| E8 | `admin_audit_log.target_type` | `payment_order`, `payment_account`, `star_grant`, `payment_flag` 추가 | 14 |
| E9 | `GET /meta` | `payments: { enabled }` 추가(클라이언트가 결제 UI 표시 여부 판단) | 15 |
| E10 | `utils/logger.ts` `LOG_REDACT` | 키·비밀 이름 추가 | 7.6 |
| E11 | `POST /auth/steam/link` | 열린 주문이 있거나 주문 기록이 있는 계정이 다른 Steam ID로 연결을 바꾸려 하면 거절(이미 Steam 신원이 있으면 거절하는지 구현 때 확인하고 같은 효과가 되게) | 7.3 |
| E12 | 서버 기동 | 확률표 스냅샷 확인(`RATES_VERSION_CONFLICT`), `star_products.json` 검증, 결제 환경 검사 | 11.4, 16 |
| E13 | `ops/jobs`, `ops/jobs/purge.ts`, `integrity.ts`, `alertRules.ts`, `snapshot.ts` | 작업 4개, 주문 IP 정리, I6, 경보, 지표 | 7.4, 12 |
| E14 | `economy/economyAdminService` 운영 지급 | 변경 없음. 별조각은 PA9~PA12 경로만(기존 우편 지급은 골드·아이템만 유지) | 12.1 |

### 13.3 관리자 API 14개 (7단계 관리자 listener, 응답 형식·TOTP 세션·감사는 기존)

| # | 메서드 | 경로 | 역할 | 하는 일 |
|---|---|---|---|---|
| PA1 | GET | `/admin/payments/orders` | viewer | 주문 목록(필터: 상태, 계정, 기간, 검토 필요) |
| PA2 | GET | `/admin/payments/orders/{uuid}` | viewer | 주문 상세(스냅샷, 이벤트, 배분, 플래그, 회수 결과) |
| PA3 | POST | `/admin/payments/orders/{uuid}/recheck` | operator | Steam에 다시 조회해 진행(`advanceOrder`) |
| PA4 | GET | `/admin/payments/accounts/{uuid}` | viewer | 계정의 결제 프로필(단계, 한도 사용량, 정지, 부채, 이력, 연결 계정) |
| PA5 | POST | `/admin/payments/accounts/{uuid}/block` | operator | 결제 정지(수동) |
| PA6 | POST | `/admin/payments/accounts/{uuid}/unblock` | owner | 결제 정지 해제 |
| PA7 | GET | `/admin/payments/flags` | viewer | 검토 큐(열린 플래그, 심각도순) |
| PA8 | POST | `/admin/payments/flags/{uuid}/resolve` | operator | 플래그 처리(`confirmed`/`dismissed` + 메모) |
| PA9 | POST | `/admin/payments/star-grants` | operator | 운영 지급·부채 탕감 작성(승인 대기) |
| PA10 | GET | `/admin/payments/star-grants` | viewer | 운영 지급 목록 |
| PA11 | POST | `/admin/payments/star-grants/{uuid}/approve` | owner(작성자 외) | 승인 = 실행 |
| PA12 | POST | `/admin/payments/star-grants/{uuid}/cancel` | 작성자 또는 owner | 취소 |
| PA13 | POST | `/admin/payments/orders/{uuid}/revoke-outcomes` | owner | 환불·차지백 주문의 결과물 회수(미리보기/적용) |
| PA14 | GET | `/admin/payments/reconcile` | viewer | 대사 현황(상태별 수, 정체 주문, Steam 대비 합계, 마지막 리포트 시각, 미지 주문) |

CLI(`cliCommands.ts`): `pay order list|show|recheck`, `pay account show|block|unblock`, `pay flag list|resolve`, `stars grant create|list|approve|cancel`, `pay revoke`, `pay report`를 PA1~PA14에 1:1로 둔다.

**PA3** `{ request_id }`. `needs_review` 주문과 `failed(mismatch)` 주문도 재확인 가능. 지급 가능한 상태가 되면 `advanceOrder`가 지급까지 한다(검증은 7.3 그대로, 운영자가 검증을 건너뛰게 하는 옵션은 없다). 에러 `404`, `409 PAYMENT_STATE`.

**PA5** `{ request_id, note: string(1..500), reason: 'manual' | 'fraud_suspect' }`. `payment_profiles.status='blocked'`. 이미 정지면 멱등. **PA6** `{ request_id, note: string(1..500) }`. 사유가 `chargeback`/`refund_abuse`/`linked_chargeback`이면 owner가 `note`에 근거를 적어야 하고 해제 후 14일 restricted(`tier_floor`)로 시작한다. 경제 정지(`payment`)는 H3에서 별도로 해제한다(결제 정지 해제가 경제 정지를 풀지 않는다).

**PA9**:

```
z.strictObject({
  request_id: z.uuid(),
  kind: z.enum(['grant', 'debt_forgive']),
  account_id: z.uuid(),
  stars: z.number().int().min(1).optional(),   // grant만 필수(서버가 상한 검사). debt_forgive에서는 거절(전액만)
  related_order_id: z.uuid().optional(),
  memo: z.string().trim().min(1).max(200),
})
```

응답 `201` `data: { grant: { id, kind, account_id, stars, state: 'pending', created_by, memo, expires_at } }`. 에러 `422 GRANT_LIMIT`, `409 GRANT_NEEDS_TWO_OWNERS`, `404`(계정 없음), `422 IDEMPOTENCY_MISMATCH`.

**PA11** `{ request_id }`. 처리: 지급 행 `FOR UPDATE` -> `pending`·미만료·`created_by <> 내 id` 확인 -> 상한 재검사 -> 계정 행·지갑 잠금 -> `creditFree(admin_grant)` 또는 `forgiveDebt` -> `applied`. 응답 `{ grant, wallet }`. 에러 `403 GRANT_SELF_APPROVAL`, `409 GRANT_STATE`, `422 GRANT_LIMIT`.

**PA13**:

```
z.strictObject({
  request_id: z.uuid(),
  mode: z.enum(['preview', 'apply']),
  include: z.strictObject({ cosmetics: z.boolean(), gear: z.boolean(), gauge: z.boolean() }),
  preview_hash: z.string().regex(/^[0-9a-f]{64}$/).optional(),   // apply에서 필수
  note: z.string().trim().min(1).max(500),
})
```

미리보기 응답: 회수 대상 목록과 `preview_hash`. 적용은 같은 `include`와 `preview_hash`일 때만. 주문 상태가 `refunded`/`chargeback`이 아니면 `409 PAYMENT_STATE`. 감사 `payment.revoke_outcomes`.

관리자 공통 규칙: 요청 본문 `.strict()`, 변경은 `runAdminAction`(멱등, 감사 행 같은 트랜잭션), 조회 중 민감한 것(PA2, PA4)은 조회 기록을 남긴다. 목록은 커서 기반 `meta:{ next_cursor }`. 관리자가 입력하는 금액은 PA9의 `stars`뿐이고 2인 승인과 상한 아래에 있다.

## 14. 마이그레이션 0023 초안

`server/migrations/0023_payments.sql`(정본은 구현 때). UP/DOWN 마커는 0001~0022와 같다. 선행: 0013, 0016, 0018(별조각), 0020(`economy_holds`, `ledger_block_mutation`), 0021.

### 14.1 SQL

```sql
-- 0023_payments: 별조각 Steam 결제(Docs/server/phase11_payments.md 14절)
-- 대상: PostgreSQL 14 이상. 선행: 0013_starshop, 0016, 0018, 0020_anti_abuse(economy_holds, ledger_block_mutation), 0021.

-- ============ UP ============

-- 1. 확률표 스냅샷(추가 전용): 같은 버전에 다른 내용이면 서버가 기동하지 않는다
CREATE TABLE star_rates_snapshots (
  version      TEXT PRIMARY KEY CHECK (char_length(version) BETWEEN 1 AND 40),
  content      JSONB NOT NULL CHECK (jsonb_typeof(content) = 'object'),
  content_hash TEXT NOT NULL CHECK (content_hash ~ '^[0-9a-f]{64}$'),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  star_rates_snapshots IS '뽑기 확률표 버전별 스냅샷(추가 전용). gacha_pulls.rates_version이 가리킨다. 확률 분쟁·공개 자료의 근거';
COMMENT ON COLUMN star_rates_snapshots.content IS '그 버전의 확률·가격·천장·게이지·중복 규칙 전체(서버가 starshopDefs에서 만든다)';
CREATE TRIGGER star_rates_snapshots_append_only BEFORE UPDATE OR DELETE ON star_rates_snapshots
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_rates_snapshots_no_truncate BEFORE TRUNCATE ON star_rates_snapshots
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 2. 결제 계정 상태
CREATE TABLE payment_profiles (
  account_id       BIGINT PRIMARY KEY REFERENCES accounts(id),
  status           TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'blocked')),
  block_reason     TEXT CHECK (block_reason IN ('chargeback', 'refund_abuse', 'linked_chargeback', 'fraud_suspect', 'manual')),
  blocked_at       TIMESTAMPTZ,
  blocked_by       TEXT CHECK (char_length(blocked_by) <= 40),
  tier_floor       TEXT NOT NULL DEFAULT 'none' CHECK (tier_floor IN ('none', 'restricted')),
  tier_floor_until TIMESTAMPTZ,
  last_country     TEXT CHECK (last_country ~ '^[A-Z]{2}$'),
  last_currency    TEXT CHECK (last_currency ~ '^[A-Z]{3}$'),
  note             TEXT CHECK (char_length(note) <= 500),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT payment_profiles_block_chk CHECK ((status = 'blocked') = (block_reason IS NOT NULL AND blocked_at IS NOT NULL)),
  CONSTRAINT payment_profiles_floor_chk CHECK (tier_floor = 'none' OR tier_floor_until IS NOT NULL)
);
COMMENT ON TABLE  payment_profiles IS '계정의 결제 상태. 행이 없으면 정상(active, 제한 없음). 결제 정지와 제한 단계 하한만 담고 한도 사용량은 star_orders에서 계산한다(파생 값을 따로 저장하지 않는다)';
COMMENT ON COLUMN payment_profiles.status IS 'blocked면 새 주문을 만들 수 없다(B1, B2가 PAYMENT_BLOCKED). 해제는 owner';
COMMENT ON COLUMN payment_profiles.blocked_by IS '"system" 또는 관리자 login_id(표시용). 정본 기록은 admin_audit_log';
COMMENT ON COLUMN payment_profiles.tier_floor IS 'restricted면 tier_floor_until 전까지 제한 단계(낮은 한도) 적용. 자동(환불 반복, 국가 변경 등)과 수동이 올린다';
COMMENT ON COLUMN payment_profiles.last_country IS '마지막 주문에서 Steam이 알려 준 국가. 국가 변경 감지용';

-- 3. 주문
CREATE SEQUENCE star_order_no_seq AS BIGINT START WITH 1000000 INCREMENT BY 1 NO CYCLE;
COMMENT ON SEQUENCE star_order_no_seq IS 'Steam 주문 번호(uint64 범위 안의 양수). 서버만 발급하고 한 번 쓴 번호는 재사용하지 않는다';

CREATE TABLE star_orders (
  id               BIGSERIAL PRIMARY KEY,
  uuid             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id       BIGINT NOT NULL REFERENCES accounts(id),
  request_id       UUID NOT NULL,
  request_hash     TEXT NOT NULL,
  steam_order_id   BIGINT NOT NULL UNIQUE DEFAULT nextval('star_order_no_seq'),
  steam_trans_id   BIGINT UNIQUE,
  steam_id         TEXT NOT NULL CHECK (steam_id ~ '^[0-9]{17}$'),
  app_id           BIGINT NOT NULL CHECK (app_id > 0),
  product_id       TEXT NOT NULL CHECK (product_id ~ '^[a-z0-9_]{3,40}$'),
  steam_item_id    INT NOT NULL CHECK (steam_item_id > 0),
  catalog_version  TEXT NOT NULL,
  stars            INT NOT NULL CHECK (stars > 0),
  currency         TEXT NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
  amount_minor     BIGINT NOT NULL CHECK (amount_minor > 0),
  steam_country    TEXT CHECK (steam_country ~ '^[A-Z]{2}$'),
  steam_status     TEXT CHECK (char_length(steam_status) <= 40),
  steam_amount_minor BIGINT,
  steam_currency   TEXT CHECK (steam_currency ~ '^[A-Z]{3}$'),
  tier             TEXT NOT NULL CHECK (tier IN ('new', 'standard', 'restricted')),
  state            TEXT NOT NULL CHECK (state IN ('pending_init', 'created', 'authorized', 'finalized', 'granted',
                                                  'failed', 'expired', 'refunded', 'chargeback')),
  fail_reason      TEXT CHECK (fail_reason IN ('init_rejected', 'init_lost', 'user_denied', 'steam_failed', 'blocked', 'mismatch')),
  needs_review     BOOLEAN NOT NULL DEFAULT false,
  review_reason    TEXT CHECK (char_length(review_reason) <= 100),
  ip               INET,
  device_hash      TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  lease_until      TIMESTAMPTZ,
  lease_token      UUID,
  attempts         INT NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  next_check_at    TIMESTAMPTZ,
  last_checked_at  TIMESTAMPTZ,
  expires_at       TIMESTAMPTZ NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  init_at          TIMESTAMPTZ,
  authorized_at    TIMESTAMPTZ,
  finalized_at     TIMESTAMPTZ,
  granted_at       TIMESTAMPTZ,
  reversed_at      TIMESTAMPTZ,
  closed_at        TIMESTAMPTZ,
  UNIQUE (account_id, request_id),
  CONSTRAINT star_orders_fail_chk      CHECK ((state = 'failed') = (fail_reason IS NOT NULL)),
  CONSTRAINT star_orders_granted_chk   CHECK (state <> 'granted' OR granted_at IS NOT NULL),
  CONSTRAINT star_orders_granted_at_chk CHECK (granted_at IS NULL OR state IN ('granted', 'refunded', 'chargeback')),
  CONSTRAINT star_orders_reversed_chk  CHECK ((state IN ('refunded', 'chargeback')) = (reversed_at IS NOT NULL)),
  CONSTRAINT star_orders_expiry_chk    CHECK (expires_at > created_at),
  CONSTRAINT star_orders_lease_chk     CHECK ((lease_until IS NULL) = (lease_token IS NULL)),
  CONSTRAINT star_orders_review_chk    CHECK (needs_review OR review_reason IS NULL)
);
COMMENT ON TABLE  star_orders IS '별조각 결제 주문(상태 기계, 정본). 가격·별조각·통화·Steam ID·appid는 주문 시점 스냅샷이며 상품표가 바뀌어도 변하지 않는다. 지우지 않는다';
COMMENT ON COLUMN star_orders.uuid IS '외부 노출 id(클라이언트·관리자). 내부 id와 steam_order_id는 직접 조회 키로 쓰지 않는다';
COMMENT ON COLUMN star_orders.request_id IS 'B2의 멱등 키. (account_id, request_id) 유일';
COMMENT ON COLUMN star_orders.request_hash IS '요청 본문 정규화 해시. 같은 request_id에 다른 본문이면 IDEMPOTENCY_MISMATCH';
COMMENT ON COLUMN star_orders.steam_order_id IS '서버가 발급해 InitTxn에 보낸 주문 번호. 클라이언트가 정하지 않는다';
COMMENT ON COLUMN star_orders.steam_trans_id IS 'Steam이 돌려준 거래 번호(응답에 있을 때). 유일';
COMMENT ON COLUMN star_orders.steam_id IS '결제 주체 Steam ID. 로그인 계정의 auth_identities에서만 얻는다(요청으로 받지 않는다)';
COMMENT ON COLUMN star_orders.catalog_version IS '주문 시점 star_products.json 버전';
COMMENT ON COLUMN star_orders.amount_minor IS '통화 최소 단위 정수. 서버 상품표 값';
COMMENT ON COLUMN star_orders.steam_amount_minor IS 'QueryTxn이 돌려준 항목 금액(검증 후 기록). amount_minor와 같아야 지급한다';
COMMENT ON COLUMN star_orders.steam_status IS '마지막 QueryTxn 상태 문자열(표시·감사용, 로직은 state)';
COMMENT ON COLUMN star_orders.tier IS '주문 시점 적용한 한도 단계';
COMMENT ON COLUMN star_orders.state IS 'pending_init InitTxn 결과 미확정 / created 승인 대기 / authorized Steam 승인됨(확정 전) / finalized 확정·성공 확인(지급 대기) / granted 지급 완료 / failed 청구 없이 종료 / expired 만료 / refunded 환불 / chargeback 지불 거절';
COMMENT ON COLUMN star_orders.needs_review IS '청구되었을 수 있는데 검증이 어긋난 주문 등 사람이 봐야 하는 주문. 상태와 별개로 대사 일정을 유지한다';
COMMENT ON COLUMN star_orders.ip IS '개인정보 성격. 보관 기간(PAY_IP_RETENTION_DAYS) 뒤 NULL로 지운다';
COMMENT ON COLUMN star_orders.lease_until IS '진행 임대 만료. 한 시점에 한 작업자만 주문을 진행한다';
COMMENT ON COLUMN star_orders.lease_token IS '임대 펜싱 토큰. 이후 쓰기는 이 값과 기대 상태를 WHERE에 건다';
COMMENT ON COLUMN star_orders.next_check_at IS '다음 Steam 확인 시각(열린 주문은 수십 초 간격, 지급된 주문은 나이별 감시 간격, 감시 종료 뒤 NULL)';
-- 계정당 열린 주문 1개(동시 주문·한도 우회 방지, 새 견적 전에 열린 주문을 먼저 대사하는 계약의 DB 보장)
CREATE UNIQUE INDEX star_orders_one_open ON star_orders (account_id)
  WHERE state IN ('pending_init', 'created', 'authorized', 'finalized');
-- 한도 합산(계정의 기간 내 주문), 내 주문 이력: account_id + 최신순
CREATE INDEX star_orders_account_time ON star_orders (account_id, created_at DESC);
-- 대사·감시 작업이 "확인할 시각이 된 주문"만 읽는다(열린 주문 + 지급된 주문)
CREATE INDEX star_orders_due ON star_orders (next_check_at)
  WHERE next_check_at IS NOT NULL AND state IN ('pending_init', 'created', 'authorized', 'finalized', 'granted', 'failed');
-- 검토 큐(needs_review) 조회
CREATE INDEX star_orders_review ON star_orders (created_at) WHERE needs_review;
-- 관리자 목록(상태별 최신순)
CREATE INDEX star_orders_state_time ON star_orders (state, created_at DESC);
-- Steam ID로 주문 찾기(관리자 조회, 리포트 교차 점검)
CREATE INDEX star_orders_steam_id ON star_orders (steam_id, created_at DESC);

-- 4. 주문 이벤트(추가 전용): 결제의 모든 전이와 Steam 조회 결과
CREATE TABLE star_order_events (
  id          BIGSERIAL PRIMARY KEY,
  order_id    BIGINT NOT NULL REFERENCES star_orders(id),
  kind        TEXT NOT NULL CHECK (kind IN ('created', 'init_ok', 'init_failed', 'init_unknown', 'status_seen', 'authorized',
                                            'finalize_ok', 'finalize_failed', 'finalized', 'granted', 'failed', 'expired',
                                            'refunded', 'chargeback', 'revoked_stars', 'mismatch', 'needs_review',
                                            'review_cleared', 'admin_recheck', 'outcomes_revoked')),
  from_state  TEXT,
  to_state    TEXT,
  steam_status TEXT CHECK (char_length(steam_status) <= 40),
  actor       TEXT NOT NULL CHECK (actor IN ('player', 'job', 'admin', 'system')),
  detail      JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(detail) = 'object'),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  star_order_events IS '주문 이벤트 원장(추가 전용). detail에는 허용 목록 필드(상태 문자열, 주문·거래 번호, 통화, 금액, 국가, 사유 코드)만 넣고 키·URL·원문 응답·IP는 넣지 않는다. 지우지 않는다';
-- 주문 상세에서 시간순 이벤트
CREATE INDEX star_order_events_order ON star_order_events (order_id, id);
CREATE TRIGGER star_order_events_append_only BEFORE UPDATE OR DELETE ON star_order_events
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_order_events_no_truncate BEFORE TRUNCATE ON star_order_events
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 5. 유료 로트(주문 1개 = 로트 1개) 와 소비 배분
CREATE TABLE star_paid_lots (
  order_id   BIGINT PRIMARY KEY REFERENCES star_orders(id),
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  granted    INT NOT NULL CHECK (granted > 0),
  remaining  INT NOT NULL CHECK (remaining >= 0 AND remaining <= granted),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  revoked_at TIMESTAMPTZ
);
COMMENT ON TABLE  star_paid_lots IS '유료 별조각 로트(지급된 주문마다 한 행, PK = order_id라 주문당 1회 지급을 한 번 더 막는다). 소비는 오래된 로트부터, 환불은 그 주문 로트만 회수한다. SUM(remaining) = star_wallets.paid_balance';
COMMENT ON COLUMN star_paid_lots.remaining IS '아직 쓰지 않은 유료 별조각. 소비·회수·부채 상환으로만 줄어든다';
-- 소비가 계정의 남은 로트를 오래된 순으로 읽는다(FOR UPDATE)
CREATE INDEX star_paid_lots_open ON star_paid_lots (account_id, created_at, order_id) WHERE remaining > 0;

CREATE TABLE star_spend_allocs (
  id        BIGSERIAL PRIMARY KEY,
  ledger_id BIGINT NOT NULL REFERENCES star_ledger(id),
  order_id  BIGINT NOT NULL REFERENCES star_orders(id),
  stars     INT NOT NULL CHECK (stars > 0),
  UNIQUE (ledger_id, order_id)
);
COMMENT ON TABLE  star_spend_allocs IS '유료 별조각 감소 원장 줄(소비·회수·상환)이 어느 주문 로트에서 몇 개를 가져갔나(추가 전용). 환불된 주문의 별조각으로 얻은 것을 되짚는 근거. 줄마다 SUM(stars) = -paid_delta';
-- "이 주문의 별조각이 어디에 쓰였나"(환불 분석, 관리자 상세)
CREATE INDEX star_spend_allocs_order ON star_spend_allocs (order_id);
CREATE TRIGGER star_spend_allocs_append_only BEFORE UPDATE OR DELETE ON star_spend_allocs
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_spend_allocs_no_truncate BEFORE TRUNCATE ON star_spend_allocs
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 6. 운영 지급·부채 탕감(2인 승인)
CREATE TABLE star_admin_grants (
  id               BIGSERIAL PRIMARY KEY,
  uuid             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  kind             TEXT NOT NULL CHECK (kind IN ('grant', 'debt_forgive')),
  account_id       BIGINT NOT NULL REFERENCES accounts(id),
  stars            BIGINT,
  related_order_id BIGINT REFERENCES star_orders(id),
  memo             TEXT NOT NULL CHECK (char_length(memo) BETWEEN 1 AND 200),
  state            TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'applied', 'cancelled', 'expired')),
  request_id       UUID NOT NULL,
  created_by       BIGINT NOT NULL REFERENCES admin_users(id),
  approved_by      BIGINT REFERENCES admin_users(id),
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  approved_at      TIMESTAMPTZ,
  expires_at       TIMESTAMPTZ NOT NULL,
  closed_at        TIMESTAMPTZ,
  applied_stars    BIGINT,
  UNIQUE (created_by, request_id),
  CONSTRAINT star_admin_grants_stars_chk   CHECK ((kind = 'grant') = (stars IS NOT NULL AND stars > 0)),
  CONSTRAINT star_admin_grants_two_person  CHECK (approved_by IS NULL OR approved_by <> created_by),
  CONSTRAINT star_admin_grants_applied_chk CHECK ((state = 'applied') = (approved_by IS NOT NULL AND approved_at IS NOT NULL)),
  CONSTRAINT star_admin_grants_expiry_chk  CHECK (expires_at > created_at)
);
COMMENT ON TABLE  star_admin_grants IS '관리자 별조각 지급·부채 탕감(2인 승인, 추가 전용에 가깝다: 내용은 불변, 상태만 pending에서 한 번 변한다). 지급 경로를 만드는 유일한 운영 경로. 승인 전에는 별조각이 움직이지 않는다';
COMMENT ON COLUMN star_admin_grants.kind IS 'grant 무료 별조각 지급 / debt_forgive 승인 시점 부채 전액 탕감';
COMMENT ON COLUMN star_admin_grants.stars IS 'grant의 지급량(상한은 서버 환경변수가 검사). debt_forgive는 NULL(전액만, 금액을 입력하지 않는다)';
COMMENT ON COLUMN star_admin_grants.applied_stars IS '실제 적용량(grant는 stars, debt_forgive는 승인 시점 부채)';
COMMENT ON COLUMN star_admin_grants.approved_by IS '작성자와 다른 owner. DB CHECK가 이중 장치';
-- 대상 계정의 지급 이력(계정 상세), 관리자별·전체 일일 합계(상한 검사), 승인 대기 목록
CREATE INDEX star_admin_grants_account ON star_admin_grants (account_id, created_at DESC);
CREATE INDEX star_admin_grants_creator_time ON star_admin_grants (created_by, created_at) WHERE kind = 'grant';
CREATE INDEX star_admin_grants_pending ON star_admin_grants (created_at) WHERE state = 'pending';
CREATE FUNCTION star_admin_grants_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'star_admin_grants rows are never deleted';
  END IF;
  IF NEW.kind <> OLD.kind OR NEW.account_id <> OLD.account_id OR NEW.stars IS DISTINCT FROM OLD.stars
     OR NEW.memo <> OLD.memo OR NEW.created_by <> OLD.created_by OR NEW.created_at <> OLD.created_at
     OR NEW.related_order_id IS DISTINCT FROM OLD.related_order_id THEN
    RAISE EXCEPTION 'star_admin_grants content is immutable';
  END IF;
  IF OLD.state <> 'pending' AND NEW.state <> OLD.state THEN
    RAISE EXCEPTION 'star_admin_grants state is final';
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER star_admin_grants_guard BEFORE UPDATE OR DELETE ON star_admin_grants
  FOR EACH ROW EXECUTE FUNCTION star_admin_grants_guard();

-- 7. 결제 플래그(검토 큐)
CREATE TABLE payment_flags (
  id          BIGSERIAL PRIMARY KEY,
  uuid        UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  order_id    BIGINT REFERENCES star_orders(id),
  kind        TEXT NOT NULL CHECK (kind IN ('rapid_orders', 'limit_exceeded', 'fail_burst', 'country_changed', 'shared_device',
                                            'linked_chargeback', 'refund_after_spend', 'chargeback', 'partial_refund',
                                            'amount_mismatch', 'steamid_mismatch', 'appid_mismatch', 'unknown_steam_order',
                                            'stuck_order', 'report_gap', 'unknown_steam_status')),
  severity    SMALLINT NOT NULL CHECK (severity BETWEEN 1 AND 3),
  detail      JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(detail) = 'object'),
  state       TEXT NOT NULL DEFAULT 'open' CHECK (state IN ('open', 'confirmed', 'dismissed')),
  reviewed_by TEXT CHECK (char_length(reviewed_by) <= 40),
  reviewed_at TIMESTAMPTZ,
  note        TEXT CHECK (char_length(note) <= 500),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT payment_flags_review_chk CHECK ((state = 'open') = (reviewed_at IS NULL))
);
COMMENT ON TABLE  payment_flags IS '결제 이상 신호와 검토 큐. 막지 않고 기록하는 신호(한도 반복, 국가 변경 등)와 반드시 사람이 보는 신호(차지백, 금액 불일치, 미지 주문)를 함께 둔다. 검토 필드만 바뀐다';
COMMENT ON COLUMN payment_flags.account_id IS '신호의 대상 계정. 미지 주문처럼 계정을 특정하지 못하는 신호는 이 표에 넣지 않고 job_runs.detail과 경보로만 남긴다(unknown_steam_order는 Steam ID로 우리 계정을 찾은 경우에만 이 표에 들어간다)';
-- 검토 큐(열린 것, 심각도 높은 순, 오래된 순)
CREATE INDEX payment_flags_open ON payment_flags (severity DESC, created_at) WHERE state = 'open';
-- 계정 상세에서 그 계정의 플래그, 같은 종류 중복 억제 조회
CREATE INDEX payment_flags_account ON payment_flags (account_id, kind, created_at DESC);

-- 8. 별조각 지갑·원장 확장
ALTER TABLE star_wallets
  ADD COLUMN paid_balance BIGINT NOT NULL DEFAULT 0 CHECK (paid_balance >= 0),
  ADD COLUMN debt         BIGINT NOT NULL DEFAULT 0 CHECK (debt >= 0),
  ADD CONSTRAINT star_wallets_paid_le_balance CHECK (paid_balance <= balance);
COMMENT ON COLUMN star_wallets.paid_balance IS '그중 유료분(결제로 산 별조각). = SUM(star_paid_lots.remaining). 무료분 = balance - paid_balance';
COMMENT ON COLUMN star_wallets.debt IS '환불·차지백된 주문 중 이미 소비된 별조각. >0이면 별조각 소비 차단, 모든 입금이 먼저 상환한다. 음수 잔액 대신 쓴다';

ALTER TABLE star_ledger
  ADD COLUMN paid_delta         BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN paid_balance_after BIGINT NOT NULL DEFAULT 0 CHECK (paid_balance_after >= 0),
  ADD COLUMN debt_delta         BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN debt_after         BIGINT NOT NULL DEFAULT 0 CHECK (debt_after >= 0),
  ADD CONSTRAINT star_ledger_paid_le_balance CHECK (paid_balance_after <= balance_after);
COMMENT ON COLUMN star_ledger.paid_delta IS '유료분 증감(유료 입금 +, 유료 소비·회수·상환 -). 무료 변동은 0';
COMMENT ON COLUMN star_ledger.debt_delta IS '부채 증감(회수 +, 상환·탕감 -)';

ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant'));
COMMENT ON COLUMN star_ledger.reason IS 'purchase 결제 지급(주문 필수) / admin_grant 승인된 운영 지급 / dismantle 여분 분해 / test_grant 시험 서버 전용 / gacha·exchange 소비 / refund_revoke·chargeback_revoke 환불·차지백 회수 / debt_settle 입금이 부채 상환 / debt_forgive 승인된 부채 탕감 / gacha_refund 새 입금 금지(과거 행만)';
COMMENT ON COLUMN star_ledger.ref IS 'purchase·refund_revoke·chargeback_revoke는 주문 uuid, admin_grant·debt_forgive는 star_admin_grants uuid';

-- 사유별 부호 규칙(앱 버그가 엉뚱한 부호로 원장을 쓰는 것을 DB가 막는다)
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange')                     AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);

-- 주문당 지급 1줄, 주문당 회수 1줄, 운영 지급당 1줄(이중 지급의 DB 보장)
CREATE UNIQUE INDEX star_ledger_purchase_uq ON star_ledger (ref) WHERE reason = 'purchase';
CREATE UNIQUE INDEX star_ledger_revoke_uq   ON star_ledger (ref) WHERE reason IN ('refund_revoke', 'chargeback_revoke');
CREATE UNIQUE INDEX star_ledger_admin_uq    ON star_ledger (ref) WHERE reason IN ('admin_grant', 'debt_forgive');
-- 일일 소비 상한: 계정의 오늘 gacha·exchange 합계(지갑 행을 잠근 상태에서 읽는다)
CREATE INDEX star_ledger_spend_time ON star_ledger (account_id, created_at) WHERE reason IN ('gacha', 'exchange');

-- 원장을 추가 전용으로(0013 이후 처음)
CREATE TRIGGER star_ledger_append_only BEFORE UPDATE OR DELETE ON star_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_ledger_no_truncate BEFORE TRUNCATE ON star_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 근거 없는 입금을 커밋 시점에 거절(지연 제약 트리거: 같은 트랜잭션에서 주문·지급 상태를 먼저 바꿔도 된다)
CREATE FUNCTION star_ledger_backing_check() RETURNS trigger AS $$
BEGIN
  IF NEW.reason = 'purchase' THEN
    IF NOT EXISTS (SELECT 1 FROM star_orders o
                    WHERE o.uuid::text = NEW.ref AND o.account_id = NEW.account_id AND o.stars = NEW.delta
                      AND o.granted_at IS NOT NULL AND o.state IN ('granted', 'refunded', 'chargeback')) THEN
      RAISE EXCEPTION 'star_ledger purchase % has no granted order', NEW.ref;
    END IF;
  ELSIF NEW.reason IN ('refund_revoke', 'chargeback_revoke') THEN
    IF NOT EXISTS (SELECT 1 FROM star_orders o
                    WHERE o.uuid::text = NEW.ref AND o.account_id = NEW.account_id
                      AND o.granted_at IS NOT NULL AND o.state IN ('refunded', 'chargeback')) THEN
      RAISE EXCEPTION 'star_ledger % % has no reversed order', NEW.reason, NEW.ref;
    END IF;
  ELSIF NEW.reason IN ('admin_grant', 'debt_forgive') THEN
    IF NOT EXISTS (SELECT 1 FROM star_admin_grants g
                    WHERE g.uuid::text = NEW.ref AND g.account_id = NEW.account_id AND g.state = 'applied'
                      AND g.kind = CASE WHEN NEW.reason = 'admin_grant' THEN 'grant' ELSE 'debt_forgive' END) THEN
      RAISE EXCEPTION 'star_ledger % % has no approved grant', NEW.reason, NEW.ref;
    END IF;
  ELSIF NEW.reason = 'test_grant' THEN
    IF coalesce(current_setting('dotrpg.allow_test_grant', true), '') <> 'on' THEN
      RAISE EXCEPTION 'star_ledger test_grant is allowed only on the test stage';
    END IF;
  ELSIF NEW.reason = 'gacha_refund' THEN
    RAISE EXCEPTION 'star_ledger gacha_refund is retired';
  END IF;
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;
CREATE CONSTRAINT TRIGGER star_ledger_backing AFTER INSERT ON star_ledger
  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION star_ledger_backing_check();

-- 9. 경제 정지: 결제 종류 추가, 활성 정지 유일 인덱스에 "결제 여부"를 넣어 속도 정지와 공존
ALTER TABLE economy_holds DROP CONSTRAINT economy_holds_kind_check;
ALTER TABLE economy_holds ADD CONSTRAINT economy_holds_kind_check
  CHECK (kind IN ('velocity', 'auction', 'linked', 'manual', 'payment'));
DROP INDEX economy_holds_one_active;
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char, ((kind = 'payment')))
  WHERE state IN ('active', 'clawed_back');

-- 10. 감사 로그 대상 종류
ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail',
                         'server', 'hold', 'campaign', 'payment_order', 'payment_account', 'star_grant', 'payment_flag'));

-- ============ DOWN ============
-- 개발 DB 전용(운영에서는 돈 기록이므로 되돌리지 않는다). 0018 DOWN이 star_ledger를 DELETE하므로 반드시 이 DOWN이 먼저 실행된다.

ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold', 'campaign'));

DELETE FROM economy_holds WHERE kind = 'payment';
DROP INDEX IF EXISTS economy_holds_one_active;
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char) WHERE state IN ('active', 'clawed_back');
ALTER TABLE economy_holds DROP CONSTRAINT economy_holds_kind_check;
ALTER TABLE economy_holds ADD CONSTRAINT economy_holds_kind_check CHECK (kind IN ('velocity', 'auction', 'linked', 'manual'));

DROP TRIGGER IF EXISTS star_ledger_backing ON star_ledger;
DROP FUNCTION IF EXISTS star_ledger_backing_check();
DROP TRIGGER IF EXISTS star_ledger_no_truncate ON star_ledger;
DROP TRIGGER IF EXISTS star_ledger_append_only ON star_ledger;
DROP TRIGGER IF EXISTS star_spend_allocs_no_truncate ON star_spend_allocs;
DROP TRIGGER IF EXISTS star_spend_allocs_append_only ON star_spend_allocs;
DROP TABLE IF EXISTS star_spend_allocs;
DROP INDEX IF EXISTS star_ledger_spend_time;
DROP INDEX IF EXISTS star_ledger_admin_uq;
DROP INDEX IF EXISTS star_ledger_revoke_uq;
DROP INDEX IF EXISTS star_ledger_purchase_uq;
DELETE FROM star_ledger WHERE reason IN ('purchase', 'refund_revoke', 'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant');
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle'));
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_paid_le_balance;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS debt_after;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS debt_delta;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS paid_balance_after;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS paid_delta;
ALTER TABLE star_wallets DROP CONSTRAINT IF EXISTS star_wallets_paid_le_balance;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS debt;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS paid_balance;

DROP TABLE IF EXISTS payment_flags;
DROP TRIGGER IF EXISTS star_admin_grants_guard ON star_admin_grants;
DROP TABLE IF EXISTS star_admin_grants;
DROP FUNCTION IF EXISTS star_admin_grants_guard();
DROP TABLE IF EXISTS star_paid_lots;
DROP TRIGGER IF EXISTS star_order_events_no_truncate ON star_order_events;
DROP TRIGGER IF EXISTS star_order_events_append_only ON star_order_events;
DROP TABLE IF EXISTS star_order_events;
DROP TABLE IF EXISTS star_orders;
DROP SEQUENCE IF EXISTS star_order_no_seq;
DROP TABLE IF EXISTS payment_profiles;
DROP TRIGGER IF EXISTS star_rates_snapshots_no_truncate ON star_rates_snapshots;
DROP TRIGGER IF EXISTS star_rates_snapshots_append_only ON star_rates_snapshots;
DROP TABLE IF EXISTS star_rates_snapshots;
```

### 14.2 인덱스와 제약의 이유 (조회 패턴)

| 인덱스·제약 | 조회 패턴과 이유 |
|---|---|
| `star_orders_one_open` (부분 유일) | B2 동시 요청 직렬화의 DB 보장, 열린 주문 1개 계약, 한도 우회 방지. 열린 주문 조회(B1, 로그인 직후)도 이 인덱스 |
| `star_orders_account_time` | 한도 합산(계정의 오늘·최근 30일), 시간당 주문 수, 마지막 주문 시각, 내 이력(B5), 90일 환불 수. 모두 `account_id` + 최신순 범위 |
| `star_orders_due` (부분) | 대사·감시 작업이 `next_check_at <= now`인 주문만 읽는다. 종결 주문과 감시 종료 주문은 인덱스에 없다 |
| `star_orders_review` (부분) | 검토 필요 주문 목록(PA1 필터), 경보 `payment_review_age` |
| `star_orders_state_time` | PA1 상태별 최신순 목록, 지표(최근 1시간 상태별 수) |
| `star_orders_steam_id` | 관리자가 Steam ID로 주문 찾기, 리포트 교차 점검 |
| `UNIQUE (account_id, request_id)` | B2 멱등성 |
| `steam_order_id`, `steam_trans_id` 유일 | 같은 Steam 주문이 두 행에 연결되지 않게(재전송·위조 방지) |
| `star_order_events_order` | 주문 상세의 시간순 이벤트 |
| `star_paid_lots_open` (부분) | 소비가 남은 로트를 오래된 순으로 잠그며 읽는다(`remaining > 0`만) |
| `star_spend_allocs_order` | 환불·차지백 분석(그 주문의 별조각이 어디에 쓰였나) |
| `star_ledger_purchase_uq` 등 부분 유일 | 주문당 지급 1줄, 주문당 회수 1줄, 운영 지급당 1줄 |
| `star_ledger_spend_time` (부분) | 일일 소비 상한: 계정의 오늘 `gacha`·`exchange` 합계 |
| `star_admin_grants_creator_time` (부분) | 관리자별 일일 지급 합계(상한 검사) |
| `star_admin_grants_pending` (부분) | 승인 대기 목록, 만료 작업, 경보 |
| `payment_flags_open` (부분) | 검토 큐(열린 것, 심각도·오래된 순) |
| `payment_flags_account` | 계정 상세의 플래그, 같은 종류 중복 억제(1시간 안 같은 종류는 새로 만들지 않음) |
| `economy_holds_one_active` (재정의) | 속도 정지와 결제 정지가 한 계정·범위에 공존, 같은 종류는 하나 |

### 14.3 기능별 테이블 사용처 (schema.sql 머리말 표에 더할 행)

| 기능 | 읽기 | 쓰기 |
|---|---|---|
| 상품 조회 B1 (11단계) | auth_identities, payment_profiles, star_orders(열린 주문·한도 합산), account_devices, economy_holds, accounts | (열린 주문 대사 시 아래 행) |
| 주문 생성 B2 (11단계) | accounts(행 잠금), auth_identities, payment_profiles, star_orders(한도·속도), account_devices, economy_holds | star_orders, star_order_events, payment_profiles(last_country), payment_flags |
| 주문 확인 B3·B6, 대사 작업 (11단계) | star_orders(행 잠금·임대), payment_profiles, auth_identities | star_orders, star_order_events, payment_flags |
| 지급 (11단계) | star_orders(행 잠금), star_wallets(행 잠금), star_paid_lots | star_wallets, star_ledger(purchase, debt_settle), star_paid_lots, star_spend_allocs, star_order_events, star_orders |
| 환불·차지백 회수 (11단계) | star_orders(행 잠금), star_wallets(행 잠금), star_paid_lots, star_spend_allocs, account_devices, economy_holds | star_wallets, star_ledger(refund_revoke, chargeback_revoke), star_paid_lots, star_spend_allocs, star_orders, star_order_events, payment_profiles, payment_flags, economy_holds(kind=payment) |
| 별조각 소비 (11단계 변경) | characters(행 잠금), star_wallets(행 잠금), star_paid_lots(행 잠금), star_ledger(오늘 소비 합), economy_holds, star_rates_snapshots(기동 때) | star_wallets, star_ledger(gacha, exchange), star_spend_allocs, gacha_pulls |
| 분해 (11단계 변경) | characters(행 잠금), account_cosmetics, star_wallets(행 잠금) | account_cosmetics.copies, star_wallets, star_ledger(dismantle, debt_settle) |
| 운영 지급·탕감 PA9~PA12 | admin_users, accounts, star_wallets, star_admin_grants | star_admin_grants, star_wallets, star_ledger(admin_grant, debt_forgive), admin_audit_log |
| 결제 관리 PA1~PA8, PA13, PA14 | star_orders, star_order_events, payment_profiles, payment_flags, star_spend_allocs, gacha_pulls, account_cosmetics, account_devices | payment_profiles, payment_flags(검토 필드), account_cosmetics·character_items·item_ledger(PA13 적용), star_order_events, admin_audit_log |
| 대사·감시·리포트 작업 (11단계) | star_orders, auth_identities | star_orders, star_order_events, payment_flags, job_runs |
| 확률표 스냅샷 (11단계) | star_rates_snapshots | star_rates_snapshots(기동 때 한 줄) |
| 정합성 점검 I6 | star_wallets, star_ledger, star_paid_lots, star_spend_allocs, star_orders | job_runs |

## 15. 클라이언트 계약 (메인이 직접 구현)

클라이언트는 아래만 안다. **금액·수량·통화·확률·보상량을 보내거나 계산하지 않는다.** 서버가 준 문자열(`display_price`, `stars`)은 표시용이다.

### 15.1 `ICommerceProvider` 구현이 호출할 경로

`ICommerceProvider`(`Assets/Scripts/Runtime/Commerce/ICommerceProvider.cs`)의 계약 문구와 서버 경로의 대응. 별조각 묶음 판매용 새 구현(예: `SteamStarPackProvider`)이 이것을 구현한다(기존 `StarShopProvider`는 별조각 -> 외형 교환 전용으로 그대로 둔다).

| `ICommerceProvider` 계약 | 서버 경로 | 서버가 보장하는 것 |
|---|---|---|
| `IsAvailable` | Steam 초기화 + 로그인이 Steam 계정 + `/meta`의 `payments.enabled` | 개발용 로그인은 `403 NOT_STEAM_ACCOUNT`, 꺼짐은 `503 FEATURE_DISABLED` -> `IsAvailable=false`(판매 버튼 비활성) |
| `FetchAsync()` | `GET /payments/products` | **새 견적 발행 전에 열린 주문을 먼저 대사**한다. 열린 주문이 남아 있으면 `offers`가 비고 `open_order`가 온다(클라이언트는 "이전 결제 확인 중", `CosmeticStore.NeedsPurchaseRecovery`와 같은 상태). `CommerceSnapshot.OwnedProductIds`는 소모품이라 빈 배열, `Offers`는 `offers[]`를 `CosmeticOffer(product_id, display_price, quote_id)`로 |
| `PurchaseAsync(offer)` | 1) `POST /payments/orders` 2) Steam 오버레이 승인 대기 3) `POST /payments/orders/{uuid}/sync` 반복 | 견적이 바뀌었거나 만료면 `QUOTE_CHANGED`/`QUOTE_EXPIRED`로 거절(`quote id binds product and price`). 지급은 서버가 Steam에 확인한 뒤에만 |
| `RestoreAsync()` | `POST /payments/reconcile` 후 `GET /payments/products` | 열린 주문을 Steam 기준으로 끝낸다 |
| "취소는 `OperationCanceledException`" | `fail_code`가 `DECLINED`/`EXPIRED`/`NOT_COMPLETED`이고 `charged:false`일 때만 던진다 | 청구가 불확실하면(`UNDER_REVIEW`, `charged:null`) 일반 오류로 던지고 열린 주문을 남긴다 |
| "UI 시간 초과는 플랫폼 청구를 취소하지 않는다" | 시간 초과 시 오류만 던지고 아무것도 취소하지 않는다. 다음 `FetchAsync`/`RestoreAsync`가 대사 | 서버 주문은 `PAY_ORDER_EXPIRE_MINUTES`까지 열려 있고 작업이 정리 |
| "로컬 설정·오버레이 닫힘 콜백으로 지급하지 않는다" | 지급 API가 없다 | 클라이언트 신호는 `sync`의 트리거로만 쓴다 |

### 15.2 구매 흐름 (클라이언트 시점)

1. 상점 화면 열기: `FetchAsync` -> `GET /payments/products`. `limits.daily.left`로 "오늘 구매 가능" 표시, `block`으로 버튼 상태.
2. 확인 창: 서버가 준 `display_price`와 `stars`(구매 총액·획득 수량)를 보여 주고 확인받는다(`Game.UI.Confirm`).
3. `POST /payments/orders { request_id, product_id, quote_id }` (`request_id`는 이 구매 시도마다 새 UUID, 재시도에는 같은 값).
4. 응답 `order.state='created'`면 Steam 오버레이가 자동으로 뜬다. `MicroTxnAuthorizationResponse` 콜백을 `order.steam_order_id`와 맞춰 기다린다(콜백의 승인 여부는 서버에 보내지 않는다).
5. 콜백이 오면 즉시 `POST /payments/orders/{id}/sync`. 응답이 `final:false`면 1초, 2초, 4초 간격으로 반복(최대 UI 대기 시간, `CosmeticStore`의 결제 대기 2분).
6. `state='granted'`: 응답의 `wallet`으로 별조각 표시를 갱신하고(`GET starshop`을 다시 읽어도 된다) 성공 처리.
7. `fail_code`: `DECLINED`("결제가 취소되었습니다"), `EXPIRED`("시간이 지나 취소되었습니다"), `NOT_COMPLETED`("결제가 완료되지 않았습니다. 청구되지 않았습니다"), `UNDER_REVIEW`("결제 확인이 지연되고 있습니다. 청구되었다면 운영팀이 확인합니다. 문의해 주세요").
8. 콜백이 오지 않거나 앱을 껐다 켠 경우: 다음 `FetchAsync`가 열린 주문을 대사하므로 별도 처리가 없다.

### 15.3 에러 문구와 상태

| 코드 | 클라이언트 처리 |
|---|---|
| `PAY_LIMIT_EXCEEDED` | "구매 한도를 넘었습니다"(`errors.scope`, `resets_at`로 시점 안내) |
| `ORDER_IN_PROGRESS` | 열린 주문을 `sync`하고 "이전 결제 확인 중" |
| `ORDER_TOO_FAST`, `429` | `retry_after_sec` 안내 |
| `PAYMENT_BLOCKED`, `ECONOMY_HOLD` | "결제 이용이 제한되었습니다. 문의해 주세요"(사유 표시 없음) |
| `NOT_STEAM_ACCOUNT` | 결제 UI 숨김 |
| `STAR_DEBT` | "환불된 결제분이 있어 별조각을 사용할 수 없습니다"(`errors.debt` 표시) |
| `STAR_SPEND_CAP` | "오늘 사용 한도를 넘었습니다"(`resets_at`) |
| `RATES_CHANGED` | 확률표를 다시 불러와 표시하고 다시 확인받는다 |
| `STEAM_UNAVAILABLE`, `FEATURE_DISABLED` | "지금은 결제할 수 없습니다" |

규칙: (1) 모든 호출은 Unity 주 스레드 문맥에서 완료 처리, (2) `request_id`는 구매 시도 단위로 생성해 재시도에 재사용, (3) 결제 중 이중 클릭은 UI에서 막되(`IsBusy`) 서버 멱등성의 대체가 아니다, (4) 뽑기 요청에 `rates_version`(화면에 표시 중인 버전)을 함께 보낸다, (5) 오프라인 모드에서는 결제 UI를 열지 않는다.

## 16. 환경변수, 기능 플래그, 켜는 순서

| 이름 | 기본 | 뜻 |
|---|---|---|
| `PAYMENTS_ENABLED` | `false` | **새 주문 허용(킬 스위치)**. 꺼지면 B1·B2가 `503 FEATURE_DISABLED`. B3~B6, 대사·감시 작업, 관리자 API는 계속 동작 |
| `PAYMENTS_STEAM_MODE` | `off` | `off|mock|sandbox|live`. `mock`은 프로세스 내부 가짜 Steam(시험 전용, 운영 기동 거부), `sandbox`는 Steam 샌드박스 [확인], `live`는 실제 |
| `STEAM_PUBLISHER_API_KEY` | (없음) | 결제 API 키(비밀). `sandbox|live`에서 필수. 로그·응답에 남기지 않는다 |
| `STEAM_PARTNER_API_BASE` | Steamworks 문서의 퍼블리셔 API 호스트 [확인] | |
| `PAYMENT_QUOTE_SECRET` | (없음) | 견적 서명 비밀. 32자 이상, `JWT_SECRET`과 달라야 함 |
| `PAYMENT_QUOTE_TTL_SECONDS` | `600` | 견적 유효 시간 |
| `PAY_ORDER_EXPIRE_MINUTES` | `15` | 승인되지 않은 주문 만료 |
| `PAY_LANGUAGE` | `ko` | InitTxn 언어 코드 [확인] |
| `PAY_INIT_LOST_MINUTES` | `10` | Steam에 없는 `pending_init`을 `failed(init_lost)`로 닫는 시간 |
| `PAY_FINALIZE_MAX_ATTEMPTS` | `5` | 확정 재시도 한도(초과 시 `needs_review`) |
| `PAY_SYNC_MIN_INTERVAL_SECONDS` | `2` | 같은 주문의 Steam 호출 최소 간격 |
| `PAY_STEAM_MAX_CALLS_PER_MIN` | `60` | 작업이 Steam을 부르는 속도 상한 |
| `PAY_WATCH_DAYS` | `180` | 환불·차지백 감시 기간 |
| `PAY_REPORT_DAYS` | `7` | 일일 리포트 교차 점검 범위 |
| `PAY_LIMIT_DAILY_STARS` / `PAY_LIMIT_MONTHLY_STARS` | `10000` / `50000` | standard 한도(별조각, 시작값) |
| `PAY_LIMIT_NEW_DAILY_STARS` / `PAY_LIMIT_NEW_MONTHLY_STARS` | `3000` / `10000` | new 한도 |
| `PAY_LIMIT_RESTRICTED_DAILY_STARS` / `PAY_LIMIT_RESTRICTED_MONTHLY_STARS` | `1000` / `3000` | restricted 한도 |
| `PAY_NEW_ACCOUNT_DAYS` | `14` | 신규 계정 기준 |
| `PAY_MAX_ORDERS_PER_HOUR` / `PAY_MIN_ORDER_GAP_SECONDS` / `PAY_FAIL_COOLDOWN_THRESHOLD` | `3` / `60` / `5` | 속도 제한 |
| `PAY_SHARED_DEVICE_ACCOUNTS` / `PAY_COUNTRY_CHANGE_MAX` | `3` / `1` | 다계정·국가 변경 기준 |
| `PAY_REFUND_SPEND_RATIO` / `PAY_REFUND_RESTRICT_COUNT` / `PAY_REFUND_BLOCK_COUNT` / `PAY_BURN_MINUTES` | `0.5` / `2` / `3` / `10` | 환불 패턴 |
| `PAY_IP_RETENTION_DAYS` | `180` | 주문 IP 보관 |
| `STAR_SPEND_DAILY_CAP` | `30000` | 일일 별조각 소비 상한(0 = 끔) |
| `STAR_RATES_ACK_REQUIRED` | `false` | 뽑기 본문의 `rates_version` 필수(결제를 켜면 `true` 필수) |
| `RATE_PAY_READ_PER_MIN` / `RATE_PAY_ORDER_PER_MIN` / `RATE_PAY_ORDER_IP_PER_MIN` / `RATE_PAY_SYNC_PER_MIN` | `30` / `3` / `10` / `30` | 속도 제한 |
| `RATE_STARSHOP_SPEND_PER_MIN` | `30` | 계정당 소비 속도 |
| `PAY_ADMIN_GRANT_MAX_STARS` / `PAY_ADMIN_GRANT_DAILY_MAX_STARS_PER_ADMIN` / `PAY_ADMIN_GRANT_DAILY_MAX_STARS_TOTAL` / `PAY_ADMIN_GRANT_PENDING_HOURS` | `5000` / `20000` / `50000` / `24` | 운영 지급 상한 |

**기동 검사**(`config/env.ts`, 어기면 기동 실패):

1. `PAYMENTS_ENABLED=true` -> `PAYMENTS_STEAM_MODE`는 `sandbox|live`, `STEAM_AUTH_MODE=web_api`, `STEAM_PUBLISHER_API_KEY`·`PAYMENT_QUOTE_SECRET`(32자 이상) 필수, `STAR_RATES_ACK_REQUIRED=true`.
2. `NODE_ENV=production`에서 `PAYMENTS_STEAM_MODE=mock` 금지. `DEPLOY_STAGE=live`이면 `PAYMENTS_STEAM_MODE=live`이고 `STEAM_APP_ID != 480`(기존 검사 재사용). `DEPLOY_STAGE=test`는 `sandbox`.
3. 한도 환경변수는 `restricted <= new <= standard`(일·월 각각)이고 일일 <= 월.
4. `star_products.json` 검증(5.1). 확률표 스냅샷 충돌(11.4).
5. 경고(기동은 진행): 판매 상품 중 `stars`가 `PAY_LIMIT_NEW_DAILY_STARS`보다 큰 것, 활성 owner 2명 미만.

**켜는 순서**

1. 서버 배포(플래그 꺼짐, 마이그레이션 0023, `star_products.json` 없이도 기동). 확률표 스냅샷이 첫 기록을 남긴다. 옛 뽑기 장비 귀속·옛 시험 별조각 점검(11.5, 4절).
2. `PAYMENTS_STEAM_MODE=sandbox`(시험 앱), `PAYMENTS_ENABLED=true`로 Steam 샌드박스 시나리오 전체(18절 H) 확인: 승인, 거절, 만료, 환불, 차지백, 금액·통화 불일치, 응답 유실, 키 거절.
3. 클라이언트 배포(`SteamStarPackProvider`, 확률표 버전 전송).
4. 운영 owner 2명 계정 확인, 경보 웹훅 확인.
5. 운영 `PAYMENTS_STEAM_MODE=live`, 실제 앱 ID, `STAR_RATES_ACK_REQUIRED=true`, 한도 확정 뒤 `PAYMENTS_ENABLED=true`. 처음에는 한도를 낮게 시작해 이상 신호를 보며 올린다.

## 17. 구현 체크리스트 (dotrpg-backend-coder가 이 순서대로)

1. **선행 확인**: 9·10단계(0020~0022, `assertNoHold`, `holds`, 관리자 `runAdminAction`, `account_devices`)가 서버에 들어가 있다. Steam 로그인이 `web_api`로 동작한다. 아니면 중단한다.
2. **데이터**: `server/data/star_products.json` 형태(5.1)와 `gamedata/starProducts.ts`(zod 로더), `data_version.json`에는 넣지 않음. 견적 서명·검증 모듈(`payments/quote.ts`).
3. **마이그레이션 0023**(14절) 작성, `schema.sql` 맨 아래에 UP 본문과 머리말 표 행(14.3) 추가. DB 제약 단위 테스트: 부호 CHECK, 부분 유일 인덱스, 추가 전용 트리거, 근거 행 지연 트리거(주문 없는 `purchase`, 승인 없는 `admin_grant`, 설정 없는 `test_grant`), 열린 주문 1개, 2인 CHECK, 지급 행 불변 트리거.
4. **지갑 모듈** `starshop/starWallet.ts`(8절): `lock`, `creditPaid`, `creditFree`, `debit`, `reversePaid`, `forgiveDebt`, 부채 상환, 배분 줄. 기존 `starshopRepository.changeBalance` 호출처를 모두 교체하고 삭제. 구조 테스트(`starWalletOnly`). `scripts/test-stars.ts`에 `SET LOCAL`. 옛 `gacha_refund` 호출 제거.
5. **Steam 클라이언트** `payments/steamPartner.ts`: 인터페이스(`getUserInfo`, `initTxn`, `queryTxn`, `finalizeTxn`, `getReport`), `mock`(시나리오 주입: 승인, 거절, 지연, 환불, 차지백, 금액·통화·steamid 불일치, 시간 초과, 5xx, 401), 실제 구현(키 마스킹 래퍼, 결제 전용 회로 차단, 재시도 규칙, 속도 제한). 7.8 확인 항목은 샌드박스에서 실측해 이 모듈 주석과 이 문서의 구현 기록에 반영.
6. **주문 상태 기계**: `orderService.ts`(`createOrder`), `advanceOrder`(임대·펜싱·검증·전이), `grantOrder`, `applyReversal`, 플래그 기록, 이벤트 기록.
7. **한도·단계**: `payTier.ts`(10절 순수 함수 + 쿼리), 계정 행 잠금 경로. 순수 함수 테스트(경계: 06:00 KST, 30일 롤링).
8. **플레이어 API** B1~B6 + 검증·라우트·속도 제한·플래그. `/payments` 라우터를 `routes/index.ts`에 등록(버전 검사, 데이터 버전은 검사하지 않음).
9. **소비 강화**(11절): `STAR_DEBT`, 일일 소비 상한, 계정당 소비 속도 제한, 확률표 스냅샷 기동 검사, 뽑기 `rates_version`, 요약 응답 필드(E1~E4). 경제 정지 `kind='payment'`(E7).
10. **작업·경보·지표**: `payment-reconcile`, `payment-watch`, `payment-report`, `star-grant-expire`, `purge` 한 줄(주문 IP), `integrity.ts` I6, `alertRules.ts`, `snapshot.ts`, `LOG_REDACT`.
11. **관리자 API** PA1~PA14 + CLI + 감사 + H3 규칙 변경(payment 정지는 owner). 운영 지급 2인 승인·상한.
12. **환경변수·기동 검사**(16절), `/meta`의 `payments.enabled`, `/auth/steam/link` 가드(E11).
13. **샌드박스 실측**: 7.8의 10개 항목을 하나씩 확인하고 7.3 검증 규칙(금액 세금 포함 여부 등)을 확정. 시험 주문이 `mock`과 같은 시나리오에서 같은 결과를 내는지 비교.
14. **정리 점검**: 옛 뽑기 장비 스택(귀속 없음) 정리 여부, 옛 시험 별조각(`test_grant`)을 무료분으로 인정, 시험 DB와 운영 DB 분리 확인.
15. **문서 갱신**: `schema.sql` 머리말 표와 본문, PLAN_SERVER 진행 현황 표에 9·10·11단계 행, PLAN_SERVER 5절 위협 표에 결제 행, 7단계 "지우지 않는다" 목록에 새 표, 이 문서의 구현 기록(설계와 달라진 점).
16. **켜는 순서**: 16절.

## 18. 테스트 시나리오 (공격 시나리오 위주)

`mock` Steam으로 자동 테스트, 동시성은 `Promise.all`. 지급 단언은 항상 "원장 줄 수, 지갑, 로트, 주문 상태가 모두 같다"를 함께 본다.

**A. 위조·재전송·경쟁**

1. 클라이언트가 임의 본문(금액, 별조각 수, 통화, Steam ID, 주문 번호)을 B2에 보낸다 -> `400`(strict). B3에 `{ authorized: true }`를 보낸다 -> `400`. 지급 API를 호출할 경로 자체가 라우트 표에 없다(라우트 목록 스냅샷 테스트).
2. Steam이 승인하지 않은 주문(`Init`)에 클라이언트가 sync를 100번 보내도 지급·상태 변화 없음.
3. 남의 주문 uuid로 B3·B4 -> `404 ORDER_NOT_FOUND`(존재 여부 구분 불가). 남의 Steam ID는 요청에 넣을 수 없다. 주문 후 계정의 Steam 신원이 바뀐 상태로 확정 -> 지급 안 함 + `steamid_mismatch` + `needs_review`.
4. mock이 QueryTxn에 다른 금액 / 다른 통화 / 다른 상품 / 수량 2 / 다른 appid / 다른 Steam ID를 돌려준다 -> 각각 지급 없음, `failed(mismatch)` 또는 `authorized` 유지(상태가 `Succeeded` 여부에 따라 7.3의 9번), 플래그, critical 경보, `star_ledger` 변화 없음.
5. 같은 주문에 대한 B3 동시 20개 + 대사 작업 동시 -> `FinalizeTxn` 호출 정확히 1회(mock 호출 카운트), `purchase` 원장 정확히 1줄, 로트 1행, 지갑 +stars 한 번.
6. 확정 직후 크래시(주입): `finalized`로 남고 대사 작업이 정확히 1회 지급한다. `InitTxn` 성공 직후 크래시: `pending_init`이 QueryTxn으로 `created`로 채택된다. InitTxn 응답 시간 초과: 재시도 호출 없음(mock 호출 카운트 1), sync가 판별.
7. 같은 `request_id`로 B2 재전송 -> 같은 주문의 현재 상태, 주문 행 1개. 같은 `request_id` + 다른 `product_id` -> `422 IDEMPOTENCY_MISMATCH`. 크래시 뒤 만료된 견적으로 재전송해도 기존 주문을 돌려준다(0번 검사).
8. 견적 위조: 서명 변조, 다른 계정의 견적, 만료된 견적, 상품표 버전·가격이 바뀐 뒤의 견적 -> `QUOTE_CHANGED`/`QUOTE_EXPIRED`.
9. 승인 전에 만료된 주문을 이용자가 나중에 Steam에서 승인(`Approved`) -> 서버는 확정하지 않는다(`expired` 유지), 별조각 없음, 청구 없음(mock에서 확정 호출 0회).
10. 결제 정지 계정의 열린 `authorized` 주문 -> 확정 호출 없이 `failed(blocked)`.
11. 임대 펜싱: 임대가 만료된 작업자가 늦게 쓰기를 시도 -> 0행, 새 작업자의 결과만 남는다.

**B. DB 방어선 (앱 코드를 우회한 INSERT/UPDATE)**

1. 주문 없이 `star_ledger(reason='purchase')` INSERT -> 커밋 시 거절. 주문은 있으나 `stars`와 `delta`가 다르면 거절. 다른 계정의 주문이면 거절.
2. 승인 없는 `admin_grant`·`debt_forgive` 거절. `test_grant`는 `dotrpg.allow_test_grant` 설정 없이 거절, 설정하면 통과. 새 `gacha_refund` 거절.
3. `star_ledger`·`star_order_events`·`star_spend_allocs`·`star_rates_snapshots`의 UPDATE/DELETE/TRUNCATE 거절.
4. 사유별 부호 위반(구매 음수, 소비 양수, 부채 상환에 `debt_delta` 양수) CHECK 거절. 같은 주문 uuid로 `purchase` 두 번 거절(유일 인덱스), 회수 두 번 거절.
5. `star_orders` 상태·시각 CHECK(지급 안 됐는데 `granted`, `granted_at` 없이 `granted`), 열린 주문 2개 거절.
6. `star_admin_grants` 작성자 = 승인자 거절(DB CHECK), 내용 UPDATE 거절, DELETE 거절, 최종 상태 되돌리기 거절.
7. 구조 테스트: `INSERT INTO star_ledger`/`UPDATE star_wallets`가 `starWallet.ts` 밖에 있으면 실패.

**C. 환불·차지백**

1. 지급 후 일부 소비(무료분 먼저 소비 확인)하고 `Refunded`: 로트 남은 분 전부 회수, `debt = granted - remaining`, 지갑 `balance >= 0`·`paid_balance` 일치, 회수 원장 1줄, 뽑기·교환 `STAR_DEBT`, 분해는 허용되며 입금이 부채를 먼저 갚는다(원장 `dismantle` + `debt_settle`).
2. 주문 A(전부 소비)와 B(전부 미사용): A만 환불 -> B 로트·`paid_balance` 변화 없음, A 소비분이 부채. B만 환불 -> B 로트 전부 회수, 부채 0.
3. 환불 이벤트 반복(작업이 두 번 감지, 동시 두 작업) -> 회수 1번(멱등).
4. `Refunded` 뒤 `Chargeback` -> 회수 재실행 없음, 상태 `chargeback`, 결제 정지 + `economy_holds(kind='payment', active)`, 연결 계정 restricted, 경보. `holds.propagate` 호출 안 함(연결 정지 행 없음).
5. 지급 전 주문(`authorized`/`finalized`)이 환불 -> 회수 없이 `refunded`, 원장 변화 없음.
6. `PartialRefund` -> `refunded`(전량) + `needs_review` + 큐. 알 수 없는 상태 문자열 -> 변경 없음 + `needs_review` + 경보.
7. 사서 뽑고 환불 반복: 소비율 50% 이상 환불 1회 = 플래그, 2회 = restricted, 3회 = 결제 정지.
8. 차지백 정지 계정이 속도 정지 `shadow`/`active`를 이미 가졌을 때 결제 정지가 공존한다. H3으로 payment 정지 해제는 operator `403`, owner 성공. 결제 정지 해제는 경제 정지를 풀지 않는다.
9. 환불 감시: `payment-watch`가 나이별 간격만 호출하는지(30일 지난 주문은 24시간 간격, 감시 기간 지난 주문은 호출 없음), Steam 호출 속도 상한을 지키는지.
10. PA13: 미리보기 해시와 다른 해시로 적용 -> 거절. 적용 시 외형·여분·장비 회수가 원장(`admin_clawback`)과 일치하고 `shortfall`을 보고한다. 일반 환불 주문에도 적용 가능하되 owner만. 자동 회수는 일어나지 않는다(환불만으로 외형이 사라지지 않는다).

**D. 한도·사기 결제**

1. 일일 한도 경계: 06:00 KST 직전 합산과 직후 초기화, 월 롤링 30일, 환불된 주문은 합산에 포함, `failed`·`expired`는 제외.
2. 한도 초과는 B2에서 `PAY_LIMIT_EXCEEDED`이고 **주문 행·Steam 호출이 생기지 않는다**(mock 호출 0회).
3. 동시 주문: 같은 계정이 서로 다른 `request_id`로 20개 동시 B2 -> 정확히 1개 성공, 나머지 `ORDER_IN_PROGRESS`(또는 속도 제한). 한도가 한 주문만 남은 상태에서 두 요청 -> 한 개만 통과.
4. 단계: 가입 14일 미만 `new`, 환불 반복·국가 변경·연결 계정 차지백·공유 기기 계정 3개 이상 -> `restricted`(가장 낮은 한도). 공용 기기(계정 7개 이상이 쓴 기기)는 연결 신호에서 제외.
5. 속도: 60초 안 재주문, 1시간 3건 초과, 1시간 실패·만료 5건 -> `ORDER_TOO_FAST`.
6. 통화: 상품표에 없는 통화 -> `CURRENCY_UNSUPPORTED`, 90일 안 국가 변경 2번 -> `country_changed` + restricted.
7. 개발용 로그인 계정 -> `NOT_STEAM_ACCOUNT`, 경제 정지 계정 -> `ECONOMY_HOLD`, 결제 정지 계정 -> `PAYMENT_BLOCKED`(응답에 사유 없음).

**E. 소비 강화**

1. 무료분 먼저 소비, 유료분 FIFO: 배분 줄 합 = `-paid_delta`, 로트 `remaining` 합 = `paid_balance`.
2. 동시 소비: 잔액 1,000으로 10+1 뽑기 20개 동시 -> 정확히 1개 성공, 잔액 음수 없음, 배분 줄 중복 없음.
3. 일일 소비 상한: 상한 직전까지 성공, 초과 `STAR_SPEND_CAP`(06:00 KST 경계), 상한 0 = 끔. 계정당 분당 소비 속도.
4. 확률표: 같은 `RATES_VERSION`에 다른 내용 -> 기동 실패. 클라이언트 버전 불일치 `RATES_CHANGED`. 뽑기 행에 버전 기록.
5. 부채 중 뽑기·교환·선택·합성 `STAR_DEBT`, 경제 정지 중 분해 거절.
6. 뽑기 장비 계정 귀속(`ITEM_BOUND`), 외형은 경매·우편으로 이전 불가.

**F. 운영 지급·감사**

1. 작성자 본인 승인 `403 GRANT_SELF_APPROVAL`(API)과 DB CHECK 직접 UPDATE 거절. operator가 승인 시도 403. owner 2명 미만이면 작성 `409 GRANT_NEEDS_TWO_OWNERS`.
2. 1건·관리자별 일일·전체 일일 상한 초과 `GRANT_LIMIT`(작성 때와 승인 때 둘 다). 승인 지급은 무료분(`paid_delta = 0`), 부채가 있으면 상환부터. 부채 탕감은 부채 전액, 금액 입력 시 `400`.
3. 같은 `request_id` 재전송 멱등. 만료된 지급 승인 거절. 우편 캠페인 첨부에 별조각 종류가 없다(기존 `.strict()` 스키마 테스트).
4. 모든 변경이 `admin_audit_log`에 있고 원장 `ref`로 지급 행을 찾을 수 있다.
5. `scripts/test-stars.ts`: `DEPLOY_STAGE`가 test가 아니면 거절. 설정 없이 SQL로 `test_grant`를 넣으면 거절.

**G. 비밀·로그·정보 노출**

1. 키가 든 URL 오류, Steam 응답 원문 오류를 주입하고 로그 캡처에 `STEAM_PUBLISHER_API_KEY` 값, `PAYMENT_QUOTE_SECRET` 값, 요청 URL 쿼리가 없다. 클라이언트 응답·`star_order_events.detail`에도 없다.
2. 플레이어 응답에 `fail_reason`, 정지 사유, 점수가 없다(`fail_code` 4종만). 관리자 응답만 상세.
3. 키 거절(mock 401) -> 상태 변경 없음 + critical 경보 `payment_key_rejected`.

**H. Steam 샌드박스 실측(구현 단계, 7.8 확인 항목)**

승인·거절·만료·환불·차지백(샌드박스가 지원하는 범위), InitTxn 금액과 QueryTxn 금액의 세금 처리, 이미 확정된 주문에 FinalizeTxn 재호출 응답, 상태 문자열 철자, GetUserInfo 응답(국가·통화), GetReport 형식, 콜백 필드. 결과로 `mock`의 응답 형태를 실제와 맞춘다.

**I. 정합성·장애**

1. I6의 각 항목이 의도적으로 틀어 둔 데이터(지급 줄 누락, 로트 합 불일치, 금액 불일치, 정체 주문)를 찾는다.
2. Steam 5xx 연속 -> 회로 차단, B1·B2 `STEAM_UNAVAILABLE`, 열린 주문은 만료 시각이 지나도 Steam 확인 전에는 `expired`로 닫히지 않는다. 회로 복구 뒤 정상 처리.
3. 리포트에 우리 기록에 없는 주문 -> `unknown_steam_order` critical.
4. 락 순서: 소비(① -> ④ -> ⑤)와 환불·지급(③ -> ④ -> ⑤)과 B2(② -> ③)를 섞은 동시 부하에서 교착이 없다.

## 19. 결정 대기 (선택지와 권장)

> **확정(2026-10-06, 사용자 대리 결정)**: D1~D19 전부 아래 권장안으로 확정하고 구현했다. 가격·묶음(`server/data/star_products.json`)과 한도·속도 제한 숫자(환경변수 기본값)는 시작값이며 **운영 전 확정**이다.

사용자가 "알아서 해 달라"고 해서 권장안이 그대로 채택될 수 있도록 **보수적·안전한 쪽**을 권장으로 했다. 값(한도, 가격)은 가격표가 정해진 뒤 환경변수로 조정한다.

| # | 항목 | 선택지 | 권장 | 이유 |
|---|---|---|---|---|
| D1 | 부채·음수 잔액 | (a) 음수 잔액 허용 / (b) 잔액은 `>= 0` 유지, 부채 열 + 소비 차단 | **(b)** | 잔액 `CHECK(>= 0)`는 재화 원칙이고, 음수 잔액은 원장·표시·정합성 점검 전부를 복잡하게 한다. 부채 열은 소비를 막는 데는 같은 효과다 |
| D2 | 소비 순서 | (a) 유료분 먼저 / (b) 무료분 먼저, 유료는 오래된 로트부터 | **(b)** | 환불 대상인 유료분이 오래 남아 회수가 쉽고 정직한 환불 요청자의 미사용분이 보존된다. 무료분은 환불 대상이 아니라 먼저 써도 불리하지 않다 |
| D3 | 환불·차지백 후 얻은 것 | (a) 자동 회수 / (b) 유지 + 부채 + 정지, 확정 부정만 owner가 PA13으로 회수 / (c) 전부 유지 | **(b)** | 정직한 환불자의 계정 물건을 자동으로 빼앗으면 오탐 피해가 크고 되돌리기 어렵다. 부채·반복 정지·경제 정지·owner 회수로 막는다. 남는 손실 상한 = 기간 한도 x 별조각 단가 |
| D4 | 부채 상환 방식 | (a) 모든 입금이 먼저 상환 / (b) 구매 입금만 / (c) 운영 탕감만 | **(a)** | 분해 입금으로 세탁하는 길을 막고 규칙이 하나다. 보상을 주려면 먼저 탕감(2인 승인) |
| D5 | 한도 단위·값 | 별조각 수량 / 금액. 시작값(10.2 표) | **별조각 수량, 표의 시작값** | 통화가 여럿이라 환산이 필요 없다. 가격표가 정해지면 금액 상한으로 환산해 재조정 |
| D6 | 일일 별조각 소비 상한 | (a) 없음 / (b) 켠다(기본 30,000) | **(b)** | 탈취한 토큰·악성 코드가 지갑을 하루에 비우는 것을 막는다. 정직한 큰 소비(게이지 채우기)는 결제 한도보다 크게 잡아 막지 않는다 |
| D7 | 하루 뽑기 횟수 상한 | (a) 두지 않음 / (b) 횟수 상한 | **(a)** | 소비 상한이 이미 묶고, 횟수 상한은 정직한 10+1 반복만 불편하게 한다 |
| D8 | 차지백 전파 | (a) 연결 계정에 경제 정지 전파 / (b) 연결 계정은 제한 단계 + 검토만 | **(b)** | 같은 기기 오탐으로 무고한 계정의 경제 전체를 막지 않는다. 결제 주체는 Steam ID라 패밀리 공유 소유자에게는 전파하지 않는다 |
| D9 | 결제 정지·payment 경제 정지 해제 권한 | operator / owner | **owner** | 돈 문제는 더 높은 권한. 해제 후 14일 restricted |
| D10 | 운영 별조각 지급 | (a) 만들지 않음 / (b) 2인 승인 + 상한 | **(b)** | 보상 사고에 필요할 수 있으나 가장 좁게(대상 1계정, 상한, 사유, 감사, 무료분) |
| D11 | 상품 구성 | 큰 묶음 위주 / 소액 묶음 위주, 보너스 별조각 | **소액 묶음 위주, 보너스 없음** | 신규·제한 단계 한도 안에서 살 수 있고, 보너스가 없으면 한 주문이 유료 로트 하나에 정확히 대응한다. 가격·묶음 자체는 사용자 결정 |
| D12 | 지원 통화·국가 | 출시 국가를 정할 때까지 | **상품표 `prices`에 있는 통화만, 출시 국가 확정 전에는 KRW 단일부터** | 지역 가격 자동 변환 가능 여부는 Steamworks 확인(7.8) 뒤 결정 |
| D13 | 환불·차지백 감시 기간 | 90 / 180일 | **180일** | 차지백은 늦게 도착할 수 있다. 간격을 늘려 호출량은 작다 |
| D14 | 결제 기록 보관·IP 보관 | 법정 보관 기간 | **주문·이벤트는 지우지 않음, IP는 180일 뒤 NULL** | 법정 보관 기간과 개인정보 처리방침은 법무 확인 필요(출시 전) |
| D15 | 확률표 공개 방식 | (a) 서버 공개 엔드포인트 / (b) 운영이 스냅샷에서 내보낸 파일을 홈페이지에 게시 | **(b)** | 인증 없는 엔드포인트를 만들지 않는다. 게시 의무 범위는 법무 확인 |
| D16 | 상품표 원본 | 사람이 관리하는 `server/data/star_products.json` / Unity 내보내기 | **사람이 관리하는 JSON + 기동 검증** | 가격은 Steamworks 설정과 같아야 하고 Unity C#에 있는 값이 아니다 |
| D17 | 뽑기 장비의 NPC 판매 경로 | (a) 지금은 두고 측정 / (b) 뽑기 장비 판매 금지 | **(a) 측정 후 판단** | 같은 키·귀속의 스택에 출처가 없어 서버만으로 구분할 수 없고(스키마 변경 필요), 별조각 -> 골드 환율이 게임 밸런스 문제다. 결제를 켠 뒤 장비 뽑기의 기대 판매 골드를 계산한다 |
| D18 | 새 계정 기준 | 계정 나이 14일 / 첫 결제 후 N일 | **계정 나이 14일** | 단순하고 서버가 이미 안다 |
| D19 | `test_grant`와 `scripts/test-stars.ts`를 운영 서버에 남길지 | 삭제 / 가드 유지 | **가드 유지(스테이지 + DB 설정)** | 시험 서버에서 필요하고 DB 트리거가 마지막 방어선이다. 운영 이미지에서 제외하는 것은 7단계 배포 절차에 맡긴다 |

"PLAN과 달라진 점"은 2.2절 표와 D1(부채 모델), D4(모든 입금 상환), D8(전파 안 함)이다. 모두 PLAN의 원칙(재화는 서버만, 원장 추가 전용, 클라이언트는 행동만 보고)을 바꾸지 않고 결제라는 새 돈 경로를 그 원칙 아래에 붙인 것이다.

## 20. 구현 기록 (2026-10-06, 서버 구현 시점)

구현 위치: 마이그레이션 `server/migrations/0023_payments.sql`(+ `schema.sql` 맨 아래), 지갑 SQL `server/src/domains/starshop/starWallet.ts`, 결제 `server/src/domains/payments/*`, 관리자 `server/src/admin/payments/*`, 작업 `server/src/ops/jobs/paymentJobs.ts`, 환경변수 `server/src/config/payEnv.ts`, 상품표 `server/data/star_products.json`(+ `server/src/gamedata/starProducts.ts`), 테스트 `server/test/pay*.test.ts`, `starWalletOnly.test.ts`.

### 20.1 Steam 실측 (7.8 [확인] 항목): 아직 하지 않았다

실제 Steam Web API는 어댑터(`payments/steamPartner.ts` 인터페이스, `steamPartnerHttp.ts` 실제 구현, `steamPartnerMock.ts` 가짜) 뒤에 두었고 **테스트는 가짜 어댑터로만** 돈다(실제 Steam 호출 없음). 아래 10개는 모두 코드 주석에 `TODO [확인]`로 남아 있고 샌드박스 실측(체크리스트 13번) 전에는 확정 값이 아니다. 실제 구현(`steamPartnerHttp.ts`)의 경로·버전·파라미터명·응답 필드는 설계 시점의 추정이다.

| # | 항목 | 현재 코드의 가정 | 상태 |
|---|---|---|---|
| 1 | 퍼블리셔 API 호스트, 샌드박스 사용법 | `STEAM_PARTNER_API_BASE`(기본 `https://partner.steam-api.com`), 샌드박스는 `ISteamMicroTxnSandbox` | 미확인 |
| 2 | InitTxn 파라미터·버전·`usersession`·`ipaddress`·`amount` 단위·세금 | v3, `usersession=client`, amount = 통화 최소 단위 정수 | 미확인 |
| 3 | QueryTxn 응답 필드와 상태 문자열 전체 | `Init, Approved, Succeeded, Failed, Refunded, PartialRefund, Chargeback, RefundedSuspectedFraud, RefundedFriendlyFraud`. 모르는 값은 `needs_review` | 미확인 |
| 4 | FinalizeTxn 재호출 응답, 승인 후 미확정 주문의 Steam 쪽 만료 | 재시도하지 않고 QueryTxn으로 판별 | 미확인 |
| 5 | GetUserInfo 응답(국가·통화) | `country`, `currency` 필드 | 미확인 |
| 6 | GetReport 형식·기간·최대 건수·반영 지연 | `type=STANDARD`, `orders[]` | 미확인 |
| 7 | 환불·차지백 정책, 통지 지연, 부분 환불 | 감시 180일, 부분 환불은 전량 취급 + 검토 | 미확인 |
| 8 | `MicroTxnAuthorizationResponse` 콜백 필드 | 서버는 콜백 내용을 받지 않는다(sync 신호만) | 클라이언트 쪽 확인 필요 |
| 9 | 키의 서버 IP 제한, 교체 절차 | 환경변수 교체 후 재시작 | 미확인 |
| 10 | 주문 번호(uint64) 유일성 범위 | 시퀀스 `star_order_no_seq`(100만부터, 서버 재시작과 무관) | 미확인 |

### 20.2 설계와 달라진 점

| 항목 | 설계 | 구현 | 이유 |
|---|---|---|---|
| `PAYMENTS_STEAM_MODE=mock`과 켜기 | 16절 기동 검사 1번은 켤 때 `sandbox|live`만 허용 | 비운영(`NODE_ENV != production`)에서는 `mock`으로도 켤 수 있다. `mock` 에서는 `STEAM_AUTH_MODE=web_api`, 퍼블리셔 키, `STAR_RATES_ACK_REQUIRED=true` 를 요구하지 않는다(견적 비밀은 요구). 운영에서 `mock`은 기동 거부 | 18절 테스트가 가짜 Steam으로 결제를 켠 채 돌아야 한다 |
| 환불 주문 감시 | `payment-watch`는 `granted`만 | `refunded` 주문도 감시 기간 동안 조회한다(차지백 승격 감지). `star_orders_due` 부분 인덱스 조건에 `refunded` 추가, `acquireLease`가 `refunded`도 임대 가능 | "환불 뒤 차지백으로 승격"(9.2)을 감지하려면 환불된 주문을 계속 봐야 한다 |
| 주문·계정 행 잠금 종류 | 3절 락 순서에서 "행 잠금(FOR UPDATE)" | 주문 행(③)과 `accounts` 행(②)은 `FOR NO KEY UPDATE`로 잠근다 | 소비가 `star_spend_allocs.order_id` FK를 넣을 때 주문 행에 `FOR KEY SHARE`를 잡아, `FOR UPDATE`면 회수(주문 -> 지갑)와 소비(지갑 -> 배분 FK -> 주문)가 교착한다. 동시 부하 시험(18절 I-4)에서 실제로 났다 |
| `advanceOrder` 반복 | 조회 -> 판정 -> 확정 -> 재조회 | 한 번의 호출에서 최대 6단계까지 이어서 진행(승인 -> 확정 -> 지급이 한 sync로 끝난다). FinalizeTxn은 호출당 1회 | sync 한 번에 지급까지 가야 클라이언트 UX가 맞다 |
| 만료된 `created` + Steam `Approved` | 9번 시나리오는 만료 뒤 승인 | 만료 시각이 지난 `pending_init`/`created`는 Steam이 `Init`이든 `Approved`이든 확정하지 않고 `expired`로 닫는다. 이미 `authorized`가 된 주문은 만료 뒤에도 확정한다 | 7.7: 회로가 열린 동안에는 Steam 확인 없이 닫지 않는다(조회 성공 뒤에만 닫음) |
| PA3 재확인 | `runAdminAction` | `runAdminActionDetached`(Steam 호출을 트랜잭션 밖에서 하고 감사 행을 나중에 쓴다) | 외부 호출을 행 잠금·트랜잭션 안에서 하지 않는다는 3절 원칙 |
| PA13 범위 | 외형·여분·장비·게이지 | 소비 원장 줄의 결과를 **그 요청 전체**로 본다(한 뽑기 요청이 여러 주문 로트에 걸쳐도 같다). 합성 결과(`star_synth_log`)와 착용 중인 외형 해제는 하지 않는다 | 착용 외형을 서버가 저장하는지 확인 필요(TODO). 합성은 입력이 여러 외형이라 추적이 모호 |
| 확률표 스냅샷 내용 | 확률·가격·천장·게이지·중복 규칙 | 위 + 장비 뽑기 등급·품목표(직업·레벨 단계별, 게임 데이터에서 계산) | 장비 데이터가 바뀌면 `RATES_VERSION`을 올려야 기동한다 |
| E11 `/auth/steam/link` 가드 | 주문 기록이 있는 계정의 연결 변경 거절 | 코드 변경 없음. 계정당 Steam 신원은 하나(`auth_identities_account_provider_uq`)라 이미 `409 ACCOUNT_ALREADY_LINKED`로 막힌다. 테스트로 고정 | 같은 효과 |
| 로그인 직후 대사 | 7.4 | `/auth/steam` 재로그인 때 열린 주문 1개를 백그라운드로 대사(응답을 기다리지 않음) | |
| 상품표 값 | 문서에 값 없음 | 시작값 3종(`stars_300`, `stars_1000`, `stars_3000`, KRW 1,100 / 3,300 / 9,900, `steam_item_id` 1001~1003). **운영 전 확정** | D11, 가격은 사용자 결정 |
| `payment-report` 일정 | 매일 | 매일 KST 04:50 | 다른 새벽 배치와 겹치지 않게 |
| I6-3 금액 점검 | 상품·통화별 합계 | 지급된 주문마다 `steam_amount_minor = amount_minor`(주문 스냅샷 기준) | 상품표가 바뀌어도 과거 주문은 스냅샷이 정본(5.1) |
| `unknown_steam_order` | 플래그 | 계정을 찾으면 플래그, 못 찾으면 작업 결과(`job_runs.detail.unknown_orders`)와 경보만. 스냅샷의 `unknown_orders_open`이 둘을 합친다 | 14절 `payment_flags.account_id NOT NULL` |

### 20.3 클라이언트 계약 요약

15절 그대로다. 구현으로 확정된 응답 형태: B2 신규 주문 `201`(`state` `created`면 `next=wait_steam_authorization`, 그 외 `sync`), 같은 `request_id` 재전송 `200`. B3 `data = { order, wallet }`(`granted`일 때만 `wallet`, 필드 `balance`·`paid_balance`·`free_balance`·`debt`). 뽑기 본문에 `rates_version`(선택, `STAR_RATES_ACK_REQUIRED=true`면 필수). 새 에러 코드 목록은 3절 표 그대로(`STAR_DEBT`는 `errors.debt`, `STAR_SPEND_CAP`은 `limit·used·resets_at`, `RATES_CHANGED`는 `rates_version`). `GET /meta`에 `payments.enabled`, `GET /characters/{uuid}/starshop`에 `paid_balance`·`free_balance`·`debt`·`spend_cap`·`payments.enabled`가 추가된다(기존 필드는 그대로).

### 20.4 운영 전 확정 목록

가격·묶음(`star_products.json`), 한도(`PAY_LIMIT_*`)·속도 제한(`PAY_*`, `RATE_PAY_*`)·소비 상한(`STAR_SPEND_DAILY_CAP`) 숫자, 운영 지급 상한(`PAY_ADMIN_GRANT_*`), Steam 호스트·파라미터(20.1), 활성 owner 2명 확보, 옛 뽑기 장비 스택(귀속 없음) 정리 점검(체크리스트 14번).
