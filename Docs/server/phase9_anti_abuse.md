# 서버 9단계 설계: 부정 행위 방지 1단계 (기기·접속·기여·경제 속도 정지)

기준: [PLAN_ANTI_ABUSE.md](../PLAN_ANTI_ABUSE.md) §1(발견 A1~A10, B7~B9, B11), §3 "1단계", §3-1(재화 이상 자동 정지), §3-2(1단계 보강). 앞 단계: [phase1_2_api.md](phase1_2_api.md)(인증, 응답 형식, 멱등성), [phase3_api.md](phase3_api.md)(처치·드롭·원장), [phase4_api.md](phase4_api.md)(파티 판, 정산·대조), [phase6_api.md](phase6_api.md)(경매), [phase7_ops.md](phase7_ops.md)(관리자·작업·보관), [phase8_api.md](phase8_api.md)(중계·필드 세션). 마이그레이션: `server/migrations/0020_anti_abuse.sql`(이 문서 13절이 초안, 정본은 구현 때 만든다), 스키마: `server/schema.sql`.

이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만들고, Unity 클라이언트 변경은 따로 한다(각 절의 "클라이언트 계약"이 그 경계다). 게임 값(몬스터 경험치·드롭률·가격·던전 보상)은 문서에 복사하지 않고 `server/data/*.json` 이름으로만 참조한다. 단 12절의 "기대 수입 표"는 사용자가 요청한 계산 결과이므로 데이터 기준일(2026-10-06)의 계산값을 적었고, 서버는 이 표를 문서에서 읽지 않고 스크립트가 만든 `server/data/income_caps.json`에서 읽는다(12.2절).

## 0. 핵심 설계 (먼저 읽기)

1. **"같은 사람" 신호를 기록한다.** 로그인마다 설치 id, 기기 지문 해시, IP, Steam id(소유자 id)를 `login_events`와 집계 표 `account_devices`·`account_ips`에 남긴다. 전부 클라이언트의 자기 신고라 우회할 수 있으므로 **단독 차단 근거로 쓰지 않고**, (a) 한 기기 동시 접속 2개 제한, (b) 레이드 사람 수 합산, (c) 의심 거래 기록, (d) 경제 정지의 전파 범위에만 쓴다. IP는 PC방·공유기가 있어 거절 근거가 아니라 감시 점수다.
2. **세션은 계정당 하나다.** 같은 계정이 다시 로그인하면 이전 리프레시 가족(family)을 폐기하고, 액세스 토큰에 세션 id(`sid`)를 넣어 15분짜리 옛 토큰도 즉시 무효로 만들며, 접속 중인 `/ws`·`/relay` 연결을 끊는다.
3. **프레즌스(생존 신호)가 "온라인"의 정의다.** 30초마다 맵·자동 진행·최근 입력을 보고하고, 서버 시계로 간격 60초 이하인 구간만 활동 시간으로 쌓는다(활동 시간은 벽시계 시간을 넘을 수 없다). 이 신호가 처치 보고의 맵 확인, 필드 보스 체류, 기기 동시 접속 제한, 경제 속도 정지의 분모를 모두 책임진다. 프레즌스도 자기 신고이므로 봇이 흉내 낼 수 있다. 그래서 프레즌스는 "맵 위조와 무신호 HTTP 봇을 막는 비용 상승"이고, 진짜 방어선은 기여 판정과 경제 속도 정지다.
4. **보상은 기여에 비례한다.** 레이드·파티 던전은 호스트 관찰(검증된 `damage_dealt`, 새 `hits_landed`)로 기여를 재서 부족한 멤버의 보상을 잠그고(`LOW_CONTRIBUTION`), 같은 기기·같은 Steam 소유자는 한 사람으로 센다. 필드 캐리는 서버 감쇠가 주 방어선이다.
5. **경제 속도 정지는 원장 위의 파생 집계다.** 재화가 움직이는 새 경로는 관리자 회수(`admin_clawback`) 하나뿐이고, 나머지는 기존 `EconCtx`가 원장을 쓸 때 시간별 집계 표(`income_hourly`)를 같은 트랜잭션에서 한 줄 더하는 것이다. 잔액 `CHECK(>= 0)`·원장 추가 전용·`request_log` 멱등성 규칙은 그대로다. 정지 중에는 "얻는 것"은 막지 않고 "쓰는 것·옮기는 것"을 막는다(보류 원장 대신 동결, 12.6절).
6. **새 규칙은 전부 모드(`off | log | enforce`)가 있다.** 클라이언트가 따로 배포되므로, 서버는 먼저 `log`로 켜서 이상 기록만 보고 클라이언트가 맞춰진 뒤 `enforce`로 올린다. 경제 속도 정지는 `log_only` 2주가 기본이다(PLAN 3-1).
7. **클라이언트가 보내는 것은 행동·대상 id·`request_id`와 상태 신호(맵, 두 불리언)뿐이다.** 금액·확률·보상량·시간·점수는 어느 새 요청에도 없다. 관리자 회수도 금액을 받지 않고 서버가 정지 근거 구간의 원장에서 계산한다.

### 0.1 PLAN_ANTI_ABUSE와 달라진 점 (그리고 이유)

| 항목 | PLAN | 이 설계 | 이유 |
|---|---|---|---|
| play_time | 캐릭터별 일 단위 | **시간 단위** 버킷(`play_time_hourly`) | 1시간 창을 정확히 재야 하고, 일 단위는 시간 단위의 합으로 얻는다 |
| 전직 서버 판정 | "전직 퀘스트 청구 기록이 있어야" | **전용 승급 API**(C1) + 서버 기록 `character_career` | `career_path` 퀘스트의 완료 조건이 "전직했음"(`career_promoted`)이라, 청구를 승급의 전제로 두면 순환한다(`questService.ts:104`). 승급을 서버 행위로 만들면 퀘스트는 그 결과를 읽기만 한다 |
| 정지 중 새 재화 | 보류 원장에 쌓았다 해제 때 지급/회수 | **동결 + 회수**(보류 원장 없음) | 12.6절: 모든 지급 경로를 갈아엎고 클라이언트 `delta`와 어긋나는 위험이 크다 |
| 경험치 회수 | 해제/회수 버튼 | 골드·아이템·우편 회수만, 경험치는 미룸 | `xp_ledger.delta > 0` 제약과 레벨 되돌림이 필요해 1단계 범위를 넘는다 |
| 기여 기준 | 피해 지분 5% 또는 타격 수 | 같음 + **치유·방어형 직업을 위해 타격 수 경로를 열어 둠**, 호스트 관찰이 없으면 레이드는 보류(`held`) | 5.2~5.3절 |

### 0.2 이 문서가 미루는 것 (1단계에 넣지 않음)

- B5 자동 진행 방치의 **서버 강제**: 프레즌스에 값은 받고 `unattended_seconds`만 기록한다. 정지·확인 창 강제는 2단계.
- 던전 카드·열쇠의 언더레벨 감쇠(경험치만 감쇠한다), 치유·방어량 기반 기여(`support_points`).
- 경험치·레벨 회수, 기존 이름 소급 개명 도구, 퀘스트 검증 불가 항목(B1), 던전 결과 최소 시간(B2), 캐릭터 찍어내기(B4), 가입 보호의 DB 속도 제한(B6), 관리자 화면(UI).
- 결제·환불(B10).
- 서버 여러 대 구성: 이 설계의 메모리 구조(더티 집합, 속도 제한)는 단일 프로세스 기준이고 12.8절이 다중 인스턴스에서 바뀌는 곳을 적는다.

## 1. 공통 규칙

1~2단계 0.1~0.4(응답 형식 `{ success, message, data, meta? }` / 실패 `{ success: false, message, errors? }`, 인증, 버전 헤더, 멱등성), 3단계 0.2~0.3(락, `delta`), 4단계 1절(락 순서)을 그대로 쓴다. 이 단계에서 달라지는 것만 적는다.

- **새 REST 경로**는 `/characters/{uuid}/...` 아래이고 클라이언트·데이터 버전을 둘 다 검사한다(8단계와 같다). 관리자 경로는 `/admin/...`(7단계 관리자 서버).
- **멱등성**: 상태를 바꾸는 요청(승급, 시련, 관리자 처리)은 `request_id`(UUID)와 `UNIQUE(account_id, request_id)`(관리자는 `admin_audit_log` 요청 id)를 쓴다. **프레즌스 신호는 예외**: 마지막 값이 이기는 상태 신호이고 재화가 없어 `request_id`를 받지 않는다(5단계 하트비트와 같은 이유).
- **락 순서**(4·8단계 위에 한 줄 추가): ① 캐릭터 행(id 오름차순) ② 계정 행(`accounts`, 세션 교체 때만) ③ 기존 `parties`~`field_sessions` ④ `economy_holds`(정지 생성·해제 때만). `online_sessions`·`income_hourly`·`play_time_hourly`는 캐릭터 행을 잠근 아래, 또는 프레즌스처럼 캐릭터 행을 잠그지 않는 경로에서 **한 문장 upsert**로만 바꾼다(교착 없음). 기기 한도 판정은 `pg_advisory_xact_lock(hashtextextended('device:' || device_hash, 0))`로 같은 기기끼리만 직렬화한다.
- **새 에러 코드**(422 규칙 위반 / 409 상태 충돌 / 403 권한·정지 / 404 없음 / 429 속도 / 401 인증):

| 코드 | 상태 | 어디서 | 뜻 |
|---|---|---|---|
| `SESSION_REPLACED` | 401 | 모든 인증 요청, refresh | 같은 계정이 다른 곳에서 로그인해 이 세션이 끝났다 |
| `DEVICE_LIMIT` | 409 | 프레즌스 | 이 기기에서 동시에 접속 가능한 캐릭터(계정) 수 초과 |
| `PRESENCE_REQUIRED` | 409 | 처치 보고 | 신선한 프레즌스가 없다(클라이언트가 프레즌스를 보낸 뒤 재시도) |
| `KILL_REJECTED` | 422 | 처치 보고 | 기존 코드. 맵 불일치·보스 체류 부족도 이 코드(이상 기록 `kill_presence`) |
| `LOW_CONTRIBUTION` | (코드 아님) | 던전 결과 `data.raid.lock_reason` / `data.reward_lock_reason` | 기여 부족으로 보상 잠금 |
| `BUYER_GATE` | 422 | 경매 구매·입찰 | 구매자 자격(레벨·계정 나이) 미달 |
| `NAME_FORBIDDEN` | 422 | 캐릭터 생성 | 예약어·금칙어 이름 |
| `ECONOMY_HOLD` | 403 | 경매 등록·구매·입찰, 상점 판매, 강화, 별조각 사용, 우편 수령 | 경제 정지 중(검토 중) |
| `ALREADY_PROMOTED`, `NOT_PROMOTED`, `STAGE_MISMATCH`, `STAGE_TOO_FAST`, `TRIAL_*`, `NOT_IN_TOWN`, `IN_PARTY_CONTENT` | 409/422/429 | 전직·각성 API | 8.2절 |

- **`ECONOMY_HOLD` 응답 본문**: `{ success:false, message:'경제 활동이 일시적으로 제한되었습니다. 문의해 주세요.', code:'ECONOMY_HOLD', errors:{ scope:'character'|'account', since: ISO } }`. 근거 수치(창, 상한, 값)는 **절대 싣지 않는다**(우회 학습 방지).
- **기기·IP 취급**: 서버는 클라이언트 `device_hash`(SHA-256 hex)를 **환경 비밀 `DEVICE_HASH_PEPPER`로 HMAC-SHA256**해 저장한다(DB 유출 때 다른 서비스와 대조되지 않게). IP는 `trust proxy` 규칙의 `req.ip`(WebSocket은 `clientIp()`)를 INET으로 저장한다. 둘 다 개인정보 성격이라 보관 기간과 고지가 필요하다(15절, 결정 대기 6).
- **모드 환경변수** 공통 규칙: 값은 `off | log | enforce`, 잘못된 값이면 기동 실패. `log`는 판정하되 막지 않고 `anomaly_log` 또는 구조화 로그(`anti_abuse.*`)만 남긴다. 운영(`NODE_ENV=production`)에서 `DEPLOY_STAGE`가 명시되지 않으면 기동 실패(11절).
- **날짜 경계**: 이 단계의 창(1시간·24시간·7일)은 "지금부터 N"의 **롤링 창**이라 06:00 일일·목요일 06:00 주간 경계를 새로 계산하지 않는다. 경매 쌍 한도의 "오늘"만 기존 `utils/resetBoundaries.ts`의 함수 하나를 그대로 쓴다. 12절의 일일 상한 합산은 롤링 창이 두 게임 일에 걸칠 수 있음을 계수(`days_touched`)로 보정한다.

## 2. 요약

### 2.1 신규 엔드포인트 16개

| # | 메서드 | 경로 | 인증 | 하는 일 | 절 |
|---|---|---|---|---|---|
| P1 | POST | `/characters/{uuid}/presence` | 액세스 | 생존 신호(맵·자동 진행·입력). 세션 진입 겸용 | 4 |
| P2 | POST | `/characters/{uuid}/presence/leave` | 액세스 | 접속 종료 알림(기기 칸 반환) | 4 |
| P3 | GET | `/characters/{uuid}/economy-hold` | 액세스 | 내 경제 정지 여부(수치 없음) | 12.9 |
| C1 | POST | `/characters/{uuid}/career/promote` | 액세스 | 전직(서버 기록) | 8 |
| C2 | POST | `/characters/{uuid}/career/awakening/trial/start` | 액세스 | 각성 시련 시작 기록 | 8 |
| C3 | POST | `/characters/{uuid}/career/awakening/trial/finish` | 액세스 | 시련 종료 보고(성공이면 3단계로) | 8 |
| C4 | POST | `/characters/{uuid}/career/awakening/advance` | 액세스 | 대화 단계 진행(0,1,3,4단계) | 8 |
| H1 | GET | `/admin/economy/holds` | viewer | 정지 목록 | 12.9 |
| H2 | GET | `/admin/economy/holds/{uuid}` | viewer | 정지 상세(근거·창별 수치·연결 계정) | 12.9 |
| H3 | POST | `/admin/economy/holds/{uuid}/release` | operator | 해제(선택: 연결 정지 일괄) | 12.9 |
| H4 | POST | `/admin/economy/holds/{uuid}/clawback` | owner | 회수(골드·아이템·우편 무효화) | 12.9 |
| H5 | POST | `/admin/economy/holds` | operator | 수동 정지 | 12.9 |
| H6 | GET | `/admin/accounts/{uuid}/links` | viewer | 기기·IP·Steam으로 묶인 계정 | 12.9 |
| H7 | GET | `/admin/auction/trade-flags` | viewer | 성공한 의심 거래 목록 | 6 |
| H8 | GET | `/admin/economy/income-caps` | viewer | 현재 상한 표·모드 | 12.9 |
| H9 | GET | `/admin/characters/{uuid}/velocity` | viewer | 캐릭터의 창별 수입/활동 시간/상한 | 12.9 |

### 2.2 기존 변경 (E1~E12)

| # | 대상 | 변경 | 절 |
|---|---|---|---|
| E1 | `POST /auth/dev/register`, `/auth/dev/login`, `/auth/steam`, `/auth/refresh` | `device` 필드, 같은 계정 세션 교체, `sid`, 기기 불일치 감지 | 3 |
| E2 | 모든 인증 요청 | 액세스 토큰 `sid` 대조, `401 SESSION_REPLACED` | 3 |
| E3 | `POST /characters/{uuid}/kills` | 신선한 프레즌스·현재 맵·보스 체류 검사 | 4.5 |
| E4 | 필드 세션 `enter`·`observe`, `GET` 뷰 | 프레즌스 맵 갱신, 멤버 카드 불일치 보고, `members[].worn/career` | 9 |
| E5 | 파티 던전 정산·방장 보고 | 기여 판정, 사람 수 합산, 언더레벨 경험치 감쇠, `hits_landed` | 5 |
| E6 | 필드 파티 경험치 | 감쇠 기본값 변경, 하드 격차, 골드 감쇠 | 6 |
| E7 | 경매 등록·구매·입찰·체결 | 구매자 자격, 양방향 쌍 한도, 상점 품목 상한, 의심 거래 기록 | 7 |
| E8 | 별조각 장비 뽑기 | 계정 귀속 | 8 |
| E9 | `PUT /characters/{uuid}/state` | 서버가 부여하지 않은 전직·각성 값 거절 | 8 |
| E10 | `POST /characters` | 이름 예약어·금칙어 | 10 |
| E11 | `/ws` `town.pos`, 접속 종료 | 외형·레벨·직업을 서버 값으로 교체, 세션 교체 종료 | 3, 11 |
| E12 | 정지 대상 7개 경로 | `ECONOMY_HOLD` 검사 | 12.5 |

### 2.3 신규 테이블 (0020)

`login_events`, `account_devices`, `account_ips`, `online_sessions`, `play_time_hourly`, `income_hourly`, `economy_holds`, `character_career`, `character_career_trials`, `auction_trade_flags` (10개). 기존 표 변경: `accounts`(+4열), `auth_identities`(+1), `refresh_tokens`(+3), `party_run_members`(+2), `dungeon_runs`(+1, 잠금 사유 확장), `gold_ledger`·`item_ledger`(사유 `admin_clawback`), `anomaly_log`(kind 6개). SQL은 13절.

### 2.4 기능별 테이블 사용처 (schema.sql 머리말 표에 더할 행)

| 기능 | 읽기 | 쓰기 |
|---|---|---|
| 로그인·가입·Steam 로그인 (9단계) | accounts(행 잠금), auth_identities, account_devices | accounts(active_*), refresh_tokens(가족 폐기·device), login_events, account_devices, account_ips, auth_identities.steam_owner_id |
| refresh (9단계) | refresh_tokens, accounts | refresh_tokens, login_events(기기가 바뀐 때만) |
| 프레즌스 (9단계) | characters, accounts(active_*), online_sessions(같은 기기 수) | online_sessions, play_time_hourly, login_events(kind=enter), anomaly_log(ip_cluster, device_limit) |
| 처치 보고 맵·체류 검사 (9단계) | online_sessions | anomaly_log(kill_presence) |
| 파티 판 시작·정산 (9단계) | party_run_members(device_hash, steam_key), party_run_host_reports, online_sessions, auth_identities | party_run_members(스냅샷), dungeon_runs(contribution, lock_reason), anomaly_log(contribution, member_card) |
| 경매 구매·입찰·체결 (9단계) | account_devices, account_ips, auth_identities, auction_trades | auction_trade_flags, income_hourly(auction_*) |
| 전직·각성 (9단계) | characters, character_career, character_career_trials, online_sessions, character_state(career 노드) | character_career, character_career_trials, character_state.career, request_log |
| 경제 속도 감시 (9단계) | income_hourly, play_time_hourly, characters(level), economy_holds, account_devices | economy_holds, anomaly_log |
| 경제 정지 대상 7개 경로 | economy_holds | - |
| 관리자 회수 (9단계) | characters(행 잠금), economy_holds, income_hourly, gold_ledger, item_ledger, mails | gold_ledger(admin_clawback), item_ledger(admin_clawback), characters.gold, character_items, mails.expires_at, economy_holds, admin_audit_log |

### 2.5 서버 폴더 (새 코드의 위치)

`server/src/domains/antiabuse/` 아래에 둔다(도메인 단위, 지역성 우선): `deviceRecords.ts`(기기·IP 기록·정규화), `sessionService.ts`(세션 교체·종료 통지), `presenceService.ts`·`presenceRoutes.ts`·`presenceValidation.ts`·`presenceController.ts`, `killPresence.ts`(처치 보고용 검사), `contribution.ts`(기여·사람 수 순수 함수), `incomeMeter.ts`(버킷 기록), `velocity.ts`(창 평가), `holds.ts`(정지 생성·해제·`assertNoHold`·전파), `holdSweep.ts`(주기 작업), `tradeFlags.ts`(의심 거래), `reservedNames.ts`. 전직·각성은 `domains/characters/`에 `careerGrant*.ts`. 관리자는 `admin/economyholds/`. 데이터: `server/data/reserved_names.json`, `server/data/income_caps.json`.

## 3. 기기·로그인 기록과 동시 세션 (A1)

### 3.1 클라이언트 계약

로그인·가입·Steam 로그인·리프레시 요청 본문에 `device`를 더한다.

```
device: z.strictObject({
  install_id: z.uuid(),                              // 최초 실행 때 UUID v4 생성, persistentDataPath 파일에 저장(삭제하면 새 id)
  device_hash: z.string().regex(/^[0-9a-f]{64}$/),   // SHA-256 hex 소문자 = H("dotrpg|" + SystemInfo.deviceUniqueIdentifier)
}).optional()
```

- `SystemInfo.deviceUniqueIdentifier`가 `SystemInfo.unsupportedIdentifier`(`"n/a"`)이면 `device_hash`를 **생략하고** `device: { install_id }`만 보낸다(`n/a`를 해시해 보내면 모든 미지원 기기가 한 기기로 묶인다). 그래서 위 스키마의 `device_hash`는 실제로는 `.optional()`이다(서버는 이때 기기를 모르는 세션으로 취급).
- 새 로그인 본문 예: `{ login_id, password, device: { install_id, device_hash } }`. Steam: `{ ticket, device }`. 리프레시: `{ refresh_token, device }`. **가입·로그인·Steam 로그인·리프레시 모두 같은 `device`**를 보낸다. SteamID는 여전히 받지 않는다(서버가 티켓에서 얻는다).
- `DEVICE_INFO_REQUIRED=false`(기본)면 `device`가 없어도 받는다(옛 클라이언트, `flags`에 `device_missing`). 클라이언트가 나간 뒤 `true`로 올리면 `400 VALIDATION`(`device` 필수).
- 응답 `data`에 `session: { replaced_other: boolean }`을 더한다(다른 곳의 세션을 종료시켰으면 `true`, 클라이언트는 "다른 기기의 접속을 종료했습니다" 안내 가능).

### 3.2 기록

- 매 로그인(`register`·`login`·`steamLogin`)과 프레즌스 진입(P1 첫 신호)에 `login_events` 한 줄: `kind`(`register|login|steam_login|refresh|enter`), 계정, 캐릭터(`enter`일 때만), `install_id`, 서버가 HMAC한 `device_hash`, `ip`, `steam_id`(Steam 로그인이면 티켓에서 얻은 값), `steam_owner_id`(`SteamIdentity.ownerSteamId`, 패밀리 공유일 때), `client_version`(요청 헤더), `flags[]`.
- **리프레시는 쓰기 증폭을 피한다**: 15분마다 오는 요청이라 `refresh` 줄은 **직전 기록의 `(device_hash, ip)`와 다를 때만** 남긴다(같으면 아무것도 쓰지 않는다).
- 같은 트랜잭션에서 집계 표 upsert: `account_devices(account_id, device_hash)`에 `last_seen_at = now()`, `seen_count + 1`(없으면 생성), `account_ips(account_id, ip)`도 같다. 이 두 표가 "같은 기기를 쓴 계정"(레이드·경매·정지 전파)의 조회 원본이고, `login_events`는 감사·관리자 조회용 원본이다.
- Steam 로그인·연동이면 `auth_identities.steam_owner_id`를 `ownerSteamId`로 갱신한다(패밀리 공유가 아니면 `NULL`). 사람 키 `steam_key = COALESCE(steam_owner_id, steam subject)`.
- **보관**: `login_events` 90일(`LOGIN_EVENT_RETENTION_DAYS`), `account_devices`·`account_ips`는 마지막 관측 후 180일(`ACCOUNT_DEVICE_RETENTION_DAYS`). 7단계 `purge` 작업에 줄을 더한다.

### 3.3 동시 세션: 같은 계정 재로그인은 이전 세션을 끝낸다

`SESSION_SINGLE_MODE=on`(기본, 시험 서버는 `off` 가능).

**서버 흐름**(로그인 성공 직후, 같은 트랜잭션):

1. `SELECT ... FROM accounts WHERE id = $1 FOR UPDATE`(동시 로그인 직렬화).
2. 새 가족 id를 발급(`issueTokens`의 `familyId`)하고 `accounts.active_family_id`, `active_install_id`, `active_device_hash`, `active_session_at`을 갱신한다.
3. 같은 계정의 다른 미폐기 가족을 모두 폐기: `UPDATE refresh_tokens SET revoked_at = now(), revoke_reason = 'replaced' WHERE account_id = $1 AND family_id <> $2 AND revoked_at IS NULL`. 폐기된 행이 있었으면 `session.replaced_other = true`.
4. `UPDATE online_sessions SET ended_at = now(), end_reason = 'replaced' WHERE account_id = $1 AND ended_at IS NULL`.
5. 커밋 뒤 `pg_notify('dotrpg_session', '<account_id>:<새 family_id>')`. 모든 서버 인스턴스의 리스너(`sanctionService`의 `LISTEN dotrpg_sanction`과 같은 방식, 별도 채널·전용 연결)가 받아: `registry.ofAccount(accountId)`의 `/ws` 세션 중 `familyId`가 새 값과 **다른** 것을 `CLOSE.REPLACED(4001)`로 종료하고, `/relay` 연결 중 같은 계정의 옛 가족 것을 `bye(reconnect:false, reason:'SESSION_REPLACED')`로 끊는다(코드 번호는 relay 종료 코드표의 빈 번호를 구현자가 정한다). 새 세션의 연결은 `familyId`가 같아 닫히지 않는다(통지가 늦게 도착해도 안전).

**액세스 토큰**: `signAccessToken(accountUuid, familyId)`가 JWT `sid` 클레임에 가족 id를 넣는다. `verifyAccessToken`은 이미 읽는 `accounts` 행에 `active_family_id`를 더 읽어 대조한다(**추가 쿼리 없음**).

| 토큰 | 판정 |
|---|---|
| `sid == active_family_id` | 통과 |
| `sid != active_family_id` | `401 SESSION_REPLACED` |
| `sid` 없음(배포 전 발급) | `active_family_id IS NULL`이면 통과, 아니면 `401 SESSION_REPLACED`(옛 토큰은 최대 15분 뒤 자연 소멸) |

**리프레시**: 폐기된 행의 `revoke_reason = 'replaced'`이면 `401 SESSION_REPLACED`로 답하고 **재사용 탐지 경고(`REFRESH_REUSED`)와 가족 재폐기를 하지 않는다**(정상 교체를 도난으로 오인하지 않는다). `device`가 오고 그 가족이 만들어질 때의 `device_hash`와 다르면(`REFRESH_DEVICE_BIND`): `log`는 `login_events`에 `flags=['device_mismatch']`를 남기고, `enforce`는 가족을 `device_mismatch`로 폐기하고 `401 REFRESH_INVALID`(토큰 도용 의심). 기본 `log`(하드웨어 교체 같은 정상 변화를 먼저 본다).

### 3.4 훅 위치

| 파일 | 함수 | 변경 |
|---|---|---|
| `auth/authValidation.ts` | `credentialsBody`, `steamBody`, `refreshBody` | `device` 추가 |
| `auth/authService.ts` | `register`, `login`, `steamLogin`, `refresh`, `issueTokens` | 기록, 세션 교체, `sid`, `revoke_reason` |
| `auth/authRepository.ts` | `revokeFamily`, `insertRefreshToken`, 신규 `endOtherFamilies`, `setActiveSession` | 열 추가 |
| `middleware/authMiddleware.ts` | `signAccessToken`, `verifyAccessToken` | `sid` |
| `chat/wsServer.ts` | `hello()` | 토큰의 `sid`를 `ChatSession.familyId`에 저장, `account.id` 중복 처리는 기존 `registry.ofAccount` 유지 |
| `chat/sanctionService.ts` 옆 | 신규 `sessionListener` | `LISTEN dotrpg_session`, 위 5번 |
| `relay/relayHub.ts` | 계정별 연결 종료 진입점(정지 처리가 쓰는 곳) | 같은 경로로 `SESSION_REPLACED` 종료 |
| `config/env.ts` | `buildConfig` | 모드·보관·`DEVICE_HASH_PEPPER` 검증(기능이 하나라도 켜지면 32자 이상 필수) |

### 3.5 환경변수

| 이름 | 기본 | 뜻 |
|---|---|---|
| `DEVICE_INFO_REQUIRED` | `false` | 로그인 본문에 `device` 필수 |
| `DEVICE_HASH_PEPPER` | (없음, 운영 필수 32자+) | HMAC 비밀. `JWT_SECRET`과 달라야 함 |
| `SESSION_SINGLE_MODE` | `on` | 같은 계정 재로그인 시 이전 세션 종료(`on|off`) |
| `REFRESH_DEVICE_BIND` | `log` | 리프레시 기기 불일치(`off|log|enforce`) |
| `LOGIN_EVENT_RETENTION_DAYS` | `90` | `login_events` 보관 |
| `ACCOUNT_DEVICE_RETENTION_DAYS` | `180` | `account_devices`·`account_ips` 마지막 관측 후 보관 |

### 3.6 테스트 (`test/antiAbuseSession.test.ts`, `auth.test.ts` 확장)

1. 로그인 한 번이 `login_events` 1줄과 `account_devices`·`account_ips` upsert를 만든다. `device` 없으면 `flags=['device_missing']`, `DEVICE_INFO_REQUIRED=true`면 400.
2. `device_hash`가 DB에 **HMAC된 값**으로 저장되고 원문이 아니다.
3. 같은 계정 두 번째 로그인: 첫 세션의 리프레시가 `401 SESSION_REPLACED`(`REFRESH_REUSED` 아님), 첫 액세스 토큰도 `401 SESSION_REPLACED`, `online_sessions.ended_at` 설정, 응답 `session.replaced_other = true`.
4. 두 로그인이 동시에 오면(Promise.all) 정확히 하나의 가족만 살아남는다(`active_family_id`와 일치).
5. 열려 있던 `/ws` 소켓이 4001로 닫히고 새 세션의 소켓은 닫히지 않는다(통지가 새 세션 연결 뒤에 와도).
6. `sid` 없는 옛 토큰: `active_family_id IS NULL`이면 통과, 설정된 뒤에는 거절.
7. 리프레시 `device_hash` 불일치: `log`는 기록만, `enforce`는 가족 폐기와 401.
8. 같은 `(device_hash, ip)`의 리프레시는 `login_events`를 늘리지 않는다.
9. `SESSION_SINGLE_MODE=off`면 두 세션이 공존한다.

## 4. 프레즌스, 온라인 정의, 기기 동시 접속, 플레이 시간, 처치 맵 확인 (A1, A5, B5 기반)

### 4.1 온라인의 정의

**온라인 = `online_sessions`에 그 계정의 행이 있고 `ended_at IS NULL`이며 `last_seen_at > now() - PRESENCE_ONLINE_SECONDS(90)`**. 계정당 행 하나(PK `account_id`)라 한 계정은 동시에 캐릭터 하나만 온라인이다(세션이 계정당 하나이므로). `/ws` 연결 여부는 온라인 판정에 쓰지 않는다(연결은 유지돼도 게임이 멈춘 경우가 있다). 행은 P1이 만들고 갱신하며 P2(`leave`), 세션 교체(3.3), 90초 무신호(조회 때 신선도 필터로 해석, 청소 작업이 `ended_at`을 채움)로 끝난다.

### 4.2 P1 `POST /characters/{uuid}/presence`

- 인증: 액세스 토큰(캐릭터는 내 것이어야 한다. 캐릭터 행은 **잠그지 않는다**).
- 요청(`.strict()`):

```
z.strictObject({
  map_id: z.string().min(1).max(64),    // maps.json의 id(instanced 맵도 허용: 던전·레이드)
  auto_play: z.boolean(),               // 자동 진행(QuestAutoPilot) 중인가
  input_recent: z.boolean(),            // 최근 30초 안에 사람 입력이 있었는가
})
```

받지 않는 값: 시각, 활동 시간, 좌표, 기기 정보(기기는 로그인 세션에서 온다).
- 응답 `200` `data`: `{ server_time, interval_seconds: 30, counted: boolean, device: { online: n, max: 2 } | null }`. `counted`는 이번 신호가 활동 시간에 더해졌는지, `device`는 기기를 알 때만 채운다.
- 에러: `400 VALIDATION`, `401 TOKEN_*|SESSION_REPLACED`, `403 ACCOUNT_BANNED`, `404 CHARACTER_NOT_FOUND`, `422 INVALID_MAP`(maps.json에 없음), `409 DEVICE_LIMIT`(`errors:{ limit:2, online_on_device:2 }`, 4.3절), `429 RATE_LIMITED`.
- **멱등성**: 같은 값을 다시 보내도 안전한 마지막-값-이김 신호. `PRESENCE_MIN_GAP_SECONDS(5)` 안에 다시 오면 상태만 갱신하고 활동 시간에는 더하지 않는다(`counted:false`).
- 속도 제한: 계정당 10초에 `RATE_PRESENCE_PER_10S(3)`, 분당 `RATE_PRESENCE_PER_MIN(12)`(30초 주기 + 맵 이동 즉시 신호를 허용).

**처리**(한 트랜잭션, 계정 단위 직렬화는 `online_sessions` PK upsert가 맡는다):

1. 캐릭터 소유 확인, 맵 존재 확인.
2. 현재 행을 읽는다. 없거나 `ended_at`이 있거나 신선하지 않거나 **다른 캐릭터**이면 "진입"이다. 진입이면 4.3절 기기 한도를 검사한다. 통과하면 행을 새로 만든다(`started_at = now()`), `login_events(kind='enter', character_id)` 한 줄.
3. 진입이 아니면: `gap = now - last_seen_at`.
   - `gap <= PLAY_TIME_MAX_GAP_SECONDS(60)`이고 `gap >= PRESENCE_MIN_GAP_SECONDS`면 `play_time_hourly`의 `date_trunc('hour', now())` 버킷에 `active_seconds += gap`, `auto_play`이면 `auto_seconds += gap`, `auto_play && !input_recent`이면 `unattended_seconds += gap`, `beats + 1`을 upsert한다. `counted:true`.
   - `gap > 60`이면 간격이 끊긴 것: 더하지 않고 `map_since = now()`로 초기화(보스 체류가 끊긴다).
4. 맵이 바뀌었으면(`map_id != 행.map_id`): `prev_map_id = 옛 map_id`, `map_id = 새 값`, `map_changed_at = now()`, `map_since = now()`.
5. `auto_play`, `input_recent`, `unattended_since`(자동 진행이고 입력이 없을 때 처음 본 시각, 아니면 `NULL`), `last_seen_at = now()`를 쓴다. 연속 무입력 자동 진행이 `AUTOPLAY_UNATTENDED_MAX_SECONDS(2400)`를 처음 넘는 신호에는 **구조화 로그 `anti_abuse.unattended`만** 남긴다(이상 기록·강제는 2단계).

**활동 시간의 불변식**: 서버 시계로만 재고, 한 신호가 더하는 시간은 `min(gap, 60)`이라 신호를 아무리 빨리 보내도 **벽시계 시간보다 많이 쌓을 수 없다**. `play_time_hourly.active_seconds`는 `CHECK(<= 3600)`다.

### 4.3 기기당 동시 접속 2개, IP는 감시 점수

`DEVICE_LIMIT_MODE`(`off|log|enforce`, 기본 `log`). 진입 때만 검사한다.

- 기기를 알 때(`accounts.active_device_hash` 있음): `pg_advisory_xact_lock(hashtextextended('device:' || hash, 0))` 후 **다른 계정의** 온라인 행 수를 센다: `SELECT count(*) FROM online_sessions WHERE device_hash = $1 AND account_id <> $2 AND ended_at IS NULL AND last_seen_at > now() - interval '90 seconds'`. `DEVICE_MAX_CONCURRENT(2)`는 "이 기기에서 동시에 온라인인 계정 수의 최대"이므로 다른 계정 수가 **1 이상**이면 내가 둘째, **2 이상**이면 거절이다. 즉 `others >= DEVICE_MAX_CONCURRENT`이면 `409 DEVICE_LIMIT`(`log`는 `anomaly_log(kind='device_limit', severity 2, detail{ device_slot_count })`만).
- 한 계정은 동시에 캐릭터 하나만 온라인이므로 "동시 접속 캐릭터 2개" = "동시 온라인 계정 2개"다(PLAN 문구와 같은 뜻).
- `device_hash`도 `install_id`도 없는 세션은 한도를 적용하지 않고 `login_events.flags`에 `device_missing`을 남긴다(옛 클라이언트). `device_hash`만 없고 `install_id`가 있으면 **install_id를 기기 키로 대신 쓴다**(19.10).
- **같은 IP**는 거절하지 않는다. 진입 때 같은 IP의 다른 온라인 계정 수가 `IP_WATCH_CONCURRENT(4)` 이상이면 `anomaly_log(kind='ip_cluster', severity 1, detail{ ip_group: <IP /24 해시 앞 8자>, online })`를 **같은 IP·같은 시간대당 한 번**만 남긴다. 이 기록이 감시 점수(관리자 H6의 IP 열)이고 차단 근거가 아니다.
- 한계(솔직하게): 기기 해시를 실행마다 바꾸는 클라이언트는 기기 한도를 피한다. 이는 프레즌스 없는 처치 거절(4.5)과 경제 속도 정지(12)가 받쳐 준다.

### 4.4 P2 `POST /characters/{uuid}/presence/leave`

요청 `{}`(`.strict()`), 응답 `200` `{ left: true }`. 해당 캐릭터의 활성 행이면 `ended_at = now()`, `end_reason = 'leave'`. 이미 끝났거나 다른 캐릭터면 아무것도 하지 않고 `left:true`(멱등). 클라이언트는 캐릭터 선택 화면 복귀·종료 때 보낸다(실패해도 90초 뒤 자동 해제).

### 4.5 처치 보고의 현재 맵 확인 (A5)

`PRESENCE_KILL_MODE`(`off|log|enforce`, 기본 `log`). 훅: `kills/killService.ts`의 `processKill`, `resolveTarget` 호출 **직전**에 `killPresence.assert(ctx, { mapId, context, isFieldBoss })`.

| 검사 | 조건 | `enforce` 결과 | `log` 결과 |
|---|---|---|---|
| 신선도 | 활성 행이 있고 `last_seen_at >= now - PRESENCE_STALE_REJECT_SECONDS(120)` | `409 PRESENCE_REQUIRED`(이상 기록 없음, 지표 `presence_required`) | 구조화 로그 |
| 맵 일치 | 필드·연출 처치(`context`가 `field|scripted`): `row.map_id == 보고 map_id` **또는** (`row.prev_map_id == 보고 map_id` **이고** `now - row.map_changed_at <= PRESENCE_MAP_GRACE_SECONDS(20)`) | `422 KILL_REJECTED`(`rejected('kill_presence', 2, { map_id, presence_map })`) | 이상 기록 `kill_presence` 1단계 |
| 필드 보스 체류 | 보고 몬스터가 그 맵의 `fieldBoss`이면 `now - row.map_since >= FIELD_BOSS_MIN_PRESENCE_SECONDS(30)` | `422 KILL_REJECTED`(`kill_presence`, 1, `why:'boss_presence'`) | 이상 기록 |
| 던전 | `context='dungeon'`: 맵 확인 없음(판 멤버십이 맵을 정한다), 신선도만 검사 | 신선도만 | 로그 |

- **맵 이동 유예의 근거**: 클라이언트는 맵 로드 직후, 첫 처치 전에 **즉시 신호**를 보낸다(계약 4.7). 그래서 정상 흐름에서는 새 맵 보고가 항상 `row.map_id`와 같다. 유예 20초는 "맵을 떠나는 순간 이미 날아간 처치 보고"(옛 맵 보고)만 받아 준다. 새 맵의 신호가 늦어 어긋난 보고는 `KILL_REJECTED`가 되지만 정직한 클라이언트는 신호를 먼저 보내므로 나오지 않는다.
- 필드 세션 입장(F1)과 호스트 인계가 서버에서 맵을 확정하는 순간에도 프레즌스 맵을 갱신한다(훅: `fieldsessions/fieldService.ts`의 `enterField`가 `presenceService.touchMap(characterId, session.map_id)`). 세션 맵이 이미 정해진 필드 파티 보고는 `resolveSessionTarget`이 `session.map_id !== mapId`를 이미 막는다.
- 이상 기록 3.2.4의 반복 차단(`ANOMALY_BLOCK_COUNT`)에 `kill_presence`도 센다(기존 `countRecentKillAnomalies`가 kind를 전부 센다면 변경 없음, 아니면 kind 목록에 추가).

### 4.6 훅 위치와 파일

| 파일 | 변경 |
|---|---|
| `antiabuse/presenceRoutes.ts` 신규 | P1, P2 라우트(`requireAuth`, 버전 검사, 속도 제한) |
| `kills/killService.ts` `processKill` | `killPresence.assert` |
| `fieldsessions/fieldService.ts` `enterField` | 프레즌스 맵 갱신 |
| `ops/jobs/purge.ts` | `play_time_hourly` 35일, `online_sessions` 끝난 행 7일 삭제 |
| `ops/jobs/index.ts` | `presence_sweep`(60초마다 `last_seen_at < now-90s`인 활성 행을 `ended_at = last_seen_at`, `end_reason='timeout'`로 정리) |
| `ops/snapshot.ts` | `presence: { online, device_limit_hits, presence_required }` 지표 |

### 4.7 클라이언트 계약 (정확히)

1. 캐릭터로 월드에 들어가기 **전에** P1을 보낸다. `409 DEVICE_LIMIT`이면 "이 PC에서는 동시에 2개까지 접속할 수 있습니다" 안내 후 캐릭터 선택으로 돌아간다. 성공 전에는 처치·채집 보고를 보내지 않는다.
2. 이후 **30초마다**, 그리고 **맵이 바뀌어 로드가 끝난 즉시(첫 처치 보고 전)** 보낸다. 게임이 일시정지(오프라인 일시정지 포함)여도 온라인이면 신호는 계속 보낸다(활동 시간은 정직하게 쌓이는 것이 이득이다).
3. `auto_play`는 자동 진행이 실제로 켜져 있을 때만 `true`, `input_recent`는 지난 30초 안에 키보드·마우스·패드 입력이 있었을 때만 `true`.
4. 캐릭터 선택·종료 때 P2. 실패해도 재시도하지 않는다.
5. `401 SESSION_REPLACED`를 받으면 "다른 곳에서 로그인했습니다" 안내 후 로그인 화면으로 간다(재시도·자동 리프레시 금지).
6. `409 PRESENCE_REQUIRED`를 처치 보고에서 받으면 P1을 먼저 보내고 같은 `request_id`로 다시 보고한다(멱등).

### 4.8 환경변수

| 이름 | 기본 | 뜻 |
|---|---|---|
| `DEVICE_MAX_CONCURRENT` | `2` | 한 기기에서 동시에 온라인인 계정 수 |
| `DEVICE_LIMIT_MODE` | `log` | `off|log|enforce` |
| `IP_WATCH_CONCURRENT` | `4` | 같은 IP 동시 온라인 계정이 이 수 이상이면 `ip_cluster` 기록 |
| `PRESENCE_INTERVAL_SECONDS` | `30` | 클라이언트에 알리는 주기(응답 `interval_seconds`) |
| `PRESENCE_ONLINE_SECONDS` | `90` | 온라인 신선도 |
| `PRESENCE_MIN_GAP_SECONDS` | `5` | 이보다 촘촘한 신호는 활동 시간에 더하지 않음 |
| `PLAY_TIME_MAX_GAP_SECONDS` | `60` | 이 간격 이하만 활동 시간 |
| `PRESENCE_MAP_GRACE_SECONDS` | `20` | 맵 이동 직후 옛 맵 보고 유예 |
| `PRESENCE_STALE_REJECT_SECONDS` | `120` | 처치 보고에 필요한 프레즌스 신선도 |
| `PRESENCE_KILL_MODE` | `log` | 처치 보고 프레즌스 검사 |
| `FIELD_BOSS_MIN_PRESENCE_SECONDS` | `30` | 필드 보스 처치 전 같은 맵 체류 |
| `AUTOPLAY_UNATTENDED_MAX_SECONDS` | `2400` | 입력 없는 자동 진행 기록 임계(기록만) |
| `PLAY_TIME_RETENTION_DAYS` | `35` | `play_time_hourly`·`income_hourly` 보관 |
| `RATE_PRESENCE_PER_10S` / `RATE_PRESENCE_PER_MIN` | `3` / `12` | 계정당 |

### 4.9 테스트 (`test/presence.test.ts`)

1. 첫 P1이 `online_sessions`와 `login_events(kind=enter)`를 만든다. 30초 간격 신호 두 번이 `active_seconds`에 약 30을 더한다(`counted:true`).
2. 간격 61초 신호는 활동 시간에 더하지 않고 `map_since`를 초기화한다. 3초 간격 두 번째 신호는 `counted:false`.
3. 벽시계보다 많이 쌓을 수 없다: 1시간에 1000번 신호를 보내도(속도 제한 무시한 단위 테스트) `active_seconds <= 3600`.
4. 같은 기기 세 번째 계정 진입: `enforce`는 `409 DEVICE_LIMIT`, `log`는 통과하고 `device_limit` 기록. 두 번째까지는 통과. 같은 계정이 캐릭터를 바꿔 진입하면 기기 수가 늘지 않는다.
5. 두 계정이 동시에 세 번째 자리를 노리면(Promise.all) 정확히 하나만 통과한다(advisory lock).
6. `leave` 뒤에 즉시 새 계정이 진입할 수 있다. 90초 무신호도 칸을 반환한다.
7. 같은 IP 4개 동시 진입이면 `ip_cluster`가 시간대당 한 번만 기록되고 거절은 없다.
8. 처치 보고: 프레즌스 없음(`enforce`) -> `409 PRESENCE_REQUIRED`. 다른 맵 -> `422 KILL_REJECTED`+`kill_presence`. 맵 이동 직후 20초 안 옛 맵 보고는 통과, 21초 뒤는 거절. 필드 보스는 맵 도착 29초 뒤 거절, 31초 뒤 통과. 던전 처치는 맵 검사 없이 통과.
9. `PRESENCE_KILL_MODE=log`는 어느 경우에도 거절하지 않고 기록만 한다.
10. 맵 위조: 프레즌스 맵 A에서 B의 몬스터 보고(HTTP 봇)가 거절된다.
11. `INVALID_MAP`, 남의 캐릭터 uuid(404), 정지 계정(403).

## 5. 레이드 사람 수·기여 판정, 파티 던전 기여, 언더레벨 감쇠 (A2, A4, B3)

### 5.1 한 사람으로 세는 규칙

판 시작(`partyRunService.beginRun`, 입장 확정 시점)에 각 사람 멤버의 **스냅샷**을 `party_run_members`에 쓴다:

- `device_hash`: 그 멤버의 `accounts.active_device_hash`(없으면 `NULL`).
- `steam_key`: `COALESCE(steam_owner_id, steam subject)`(Steam 연결이 없으면 `NULL`).

**같은 사람** = 기기 키(`device_hash`, 없으면 install_id 대체 키)가 같거나 `steam_key`가 같거나 `install_id`가 같은(각각 `NULL`이 아닐 때) 멤버끼리의 연결 요소(union-find). IP는 쓰지 않는다. `humans`는 "살아 있는 사람 멤버 수"가 아니라 **연결 요소 수**다. 순수 함수 `contribution.humanGroups(rows)`가 계산한다.

사용처 3곳: ① `partyRunService.beginRun`의 `humans = standing.length`를 `humanGroups(standing).length`로(`lockAtEntry`의 `TOO_FEW_HUMANS` 판정과 `dungeon_runs.humans` 기록) ② `dungeons/dungeonResult.ts` `humansStanding` ③ 5.3절 정산.

### 5.2 기여 판정의 근거

호스트가 보내 서버가 이미 검증하는 `party_run_host_reports.members[]`(`validateHostReport`가 `DAMAGE_OVER_CAP`, `dmgSum >= partyDamageMinRatio x hpSum`로 검증함)를 근거로 쓴다. 호스트가 거짓말하는 비용을 올리려고 두 가지를 더한다.

- 호스트 보고 `members[]`에 `hits_landed: int`(optional)를 더한다(호스트가 관찰한 그 멤버의 적중 횟수). 서버 검증: `hits_landed <= (serverElapsed + ELAPSED_SLACK) / attackCooldown x powerAoeCap`(콤보 검사와 같은 상한식).
- 멤버 본인의 결과 보고 `stats`에도 `damage_dealt`, `hits_landed`를 더한다(optional, 본인 주장). 호스트 값과 본인 주장이 **크게 어긋나고**(주장이 호스트 관찰보다 `CONTRIBUTION_DISPUTE_RATIO(2.0)`배 이상) 주장 기준으로는 임계를 넘는 경우 그 멤버의 정산은 **`held`**(기존 보류 경로, 관리자 검토)로 보낸다. 서버는 주장을 지급 근거로 쓰지 않고 호스트 값을 쓴다.

멤버 하나의 **기여 지분** = `damage_dealt / Σ(사람 멤버 damage_dealt)`(분모는 사람만, AI 제외. 치유형이 AI에 밀려 억울하게 잠기는 것을 피한다). **기여 충족** = `share >= RAID_MIN_SHARE(0.05)` 또는 `hits_landed >= RAID_MIN_HITS(40)`. 치유·방어형 직업은 피해량이 작아도 기본 공격 적중 40회면 충족한다(치유량·방어량 기반 `support_points`는 미룸).

`CONTRIBUTION_MODE=off|log|enforce`(기본 `enforce`: 이 기능은 서버가 이미 가진 `damage_dealt`만으로 판정되어 클라이언트 변경 없이 켤 수 있다. `hits_landed`가 없으면 지분만으로 판정).

### 5.3 정산 규칙 (`partyruns/partySettle.ts` `settleOne` -> `dungeons/dungeonResult.ts` `finalizeCleared`)

`settleOne`은 호스트 보고가 유효(`hostValid`)할 때 멤버별 `contribution`을 계산해 `ClearInput.contribution = { share, hits, source:'host', met: boolean }`로 넘긴다. `finalizeCleared`는 **`raid_claims` INSERT보다 먼저** 아래를 적용한다(잠금이면 기간당 1회 청구가 소진되지 않는다).

| 상황 | 결과 |
|---|---|
| 파티 던전(레이드 아님, `party_run_id` 있음), `met == false` | 클리어 보상 잠금: `reward_locked = true`, `lock_reason = 'LOW_CONTRIBUTION'`. 클리어 경험치·카드 없음. 처치 경험치(처치 보고 때 이미 지급)는 그대로. 요일 던전 입장 횟수는 소모된 채(우회 입장 반복을 막는다) |
| 레이드, 내 `met == false` | 같은 `LOW_CONTRIBUTION` 잠금. `raid_claims` 미삽입 |
| 레이드, 사람 수(연결 요소 중 **기여 충족 멤버가 한 명 이상 있는 요소**의 수) `< raidRewardMinHumans(2)` | 전원 `TOO_FEW_HUMANS` 잠금(기존 사유 그대로) |
| 레이드 + 호스트 보고 무효로 기여를 모름 + `CONTRIBUTION_MODE=enforce` | 그 멤버 정산을 `held`(관리자 검토). 보고를 일부러 무효로 해 기여 판정을 피하는 짝을 막는다 |
| 파티 던전(레이드 아님) + 호스트 보고 무효 | 잠금 없음, 이상 기록 `contribution` 1단계(판단 불가) |
| 솔로(`party_run_id` 없음) | 해당 없음 |

판정 순서(레이드): `ALREADY_CLAIMED`(미리 잠금) -> `LOW_CONTRIBUTION` -> 사람 수 `TOO_FEW_HUMANS` -> `KEYS_MISSING` -> 청구 INSERT. `dungeon_runs.contribution` JSONB에 `{ share, hits, source, met }`를 남긴다(관리자 검토용). 응답 `data`: 레이드는 기존 `data.raid.lock_reason`에 `'LOW_CONTRIBUTION'`이 들어가고, 파티 던전(레이드 아님)에는 `data.reward_locked: true`, `data.reward_lock_reason: 'LOW_CONTRIBUTION'`을 더한다(클라이언트가 "기여가 부족해 보상이 없습니다" 안내).

**같은 사람의 부계정**: 부계정이 서 있기만 하면 `met=false`로 잠기고, 본캐와 같은 사람이라 요소가 하나이므로 "다른 사람" 수에 더해지지 않는다. 본캐 + 친구 + 서 있는 부계정이면 요소 2개로 본캐·친구는 정상 보상, 부계정만 잠긴다.

### 5.4 언더레벨 경험치 감쇠 (A4)

권장 레벨보다 **10 이상** 낮은 멤버는 클리어 경험치를 깎는다. `gap = diff.recommendedLevel - 멤버 레벨`(멤버 레벨은 서버 값 `ctx.char.level`). `gap < DUNGEON_UNDERLEVEL_GAP(10)`이면 감쇠 없음. 이상이면 `factor = clamp(DUNGEON_UNDERLEVEL_FACTOR(0.25) - DUNGEON_UNDERLEVEL_STEP(0.05) x (gap - 10), DUNGEON_UNDERLEVEL_MIN(0.05), 0.25)`. 클리어 경험치(`clearXp`)에 곱하고 최소 1. 적용 위치: `finalizeCleared`의 `clearXp(...)` 직후, **레이드와 파티 던전과 솔로 모두**(솔로 레벨1이 고난이도에 들어가는 길도 같이 막는다). 레이드는 권장 레벨이 곧 입장 레벨이라 해당 없음. 카드·열쇠는 줄이지 않는다(미룸).

### 5.5 클라이언트 계약

- 호스트는 `host report`(방장 보고)의 `members[]`에 멤버별 `hits_landed`(호스트가 본 적중 횟수, 정수)를 더 보낸다. 없어도 서버는 동작한다(지분만).
- 멤버는 결과 보고 `stats`에 `damage_dealt`(float), `hits_landed`(int)를 더 보낸다(optional).
- 클라이언트는 `data.reward_locked`와 `lock_reason`/`reward_lock_reason`을 읽어 사유별 문구를 보인다(`LOW_CONTRIBUTION`: "전투 기여가 부족해 보상이 없습니다").

### 5.6 환경변수

| 이름 | 기본 | 뜻 |
|---|---|---|
| `CONTRIBUTION_MODE` | `enforce` | `off|log|enforce` |
| `RAID_MIN_SHARE` | `0.05` | 레이드 기여 지분 하한 |
| `RAID_MIN_HITS` | `40` | 지분 대신 인정하는 적중 수 |
| `DUNGEON_MIN_SHARE` | `0.05` | 파티 요일 던전 지분 하한 |
| `DUNGEON_MIN_HITS` | `40` | 파티 요일 던전 적중 수 |
| `CONTRIBUTION_DISPUTE_RATIO` | `2.0` | 멤버 주장이 호스트 관찰의 이 배수 이상이면 보류 |
| `DUNGEON_UNDERLEVEL_GAP` | `10` | 권장 레벨 - 멤버 레벨이 이 값 이상이면 감쇠 |
| `DUNGEON_UNDERLEVEL_FACTOR` | `0.25` | 격차 10일 때 클리어 경험치 배율 |
| `DUNGEON_UNDERLEVEL_STEP` | `0.05` | 격차 1 증가당 감소 |
| `DUNGEON_UNDERLEVEL_MIN` | `0.05` | 하한 |

(기존 `RAID_REWARD_MIN_HUMANS=2`는 그대로, 의미만 "연결 요소 수"로 바뀐다.)

### 5.7 테스트 (`raids.test.ts`, `partyRuns.test.ts`, 신규 `contribution.test.ts`)

1. `humanGroups`: 같은 `device_hash` 둘 -> 1, 같은 `steam_key` 둘 -> 1, 서로 다른 둘 -> 2, `device_hash = NULL` 둘은 같은 사람이 아님, 체인(A-B 기기, B-C Steam) -> 1.
2. 본캐 + 같은 기기 부계정(서 있음) 레이드 클리어: 둘 다 보상 없음(부계정 `LOW_CONTRIBUTION`, 본캐 `TOO_FEW_HUMANS`), `raid_claims` 행 없음.
3. 본캐 + 친구 + 같은 기기 서 있는 부계정: 본캐·친구 보상, 부계정 `LOW_CONTRIBUTION`.
4. 지분 4.9%이고 적중 39회 -> 잠금. 지분 5.0% -> 통과. 지분 1%이고 적중 40회(치유형) -> 통과.
5. 파티 요일 던전에서 기여 없는 멤버: 클리어 경험치·카드 없음, 입장 횟수 소모.
6. 잠긴 레이드 멤버의 주간 청구는 소진되지 않아 다음 기회에 청구할 수 있다.
7. 호스트 보고 무효 + 레이드 + `enforce`: 멤버 `held`. 파티 던전은 잠금 없음 + `contribution` 이상 기록.
8. 멤버 주장 `damage_dealt`가 호스트 관찰의 2배 이상이고 임계를 넘으면 `held`.
9. 언더레벨: 권장 20, 멤버 Lv10(gap 10) -> 0.25배, Lv6(gap 14) -> 0.05배, Lv11 -> 1배. 최소 1.
10. `hits_landed`가 물리 상한을 넘는 호스트 보고는 무효(`HITS_OVER_CAP`).
11. `CONTRIBUTION_MODE=log`: 잠그지 않고 `dungeon_runs.contribution`과 이상 기록만 남긴다.
12. 솔로 판은 영향 없음.

## 6. 필드 캐리 감쇠 기본값 변경 (A3)

서버 경험치 감쇠가 주 방어선이고 호스트의 "근처 기여" 판정은 그대로 둔다(클라이언트 변경 대상, 서버는 그 위에서 감쇠). 순수 함수 `fieldsessions/xpFactor.ts`와 `FieldConfig`(`phase8Env.ts`)를 바꾼다. 활성 멤버 2명 이상일 때만 적용(기존).

**공식**(`d = 몬스터 레벨 - 멤버 레벨`, 서버 값):

- `d <= FIELD_CARRY_SLACK(5)`: 배율 1.
- `d > 5`: `배율 = clamp(1 - FIELD_CARRY_STEP(0.15) x (d - 5), FIELD_CARRY_MIN(0.02), 1)`.
- `d >= FIELD_CARRY_HARD_GAP(15)`: 경험치는 **정확히 1**(배율 계산 결과와 무관), 재료·장비 드롭 확률 배율도 `FIELD_CARRY_HARD_DROP_MUL(0)`.

| d | 6 | 7 | 8 | 9 | 10 | 11 | 12~14 | 15 이상 |
|---|---|---|---|---|---|---|---|---|
| 배율 | 0.85 | 0.70 | 0.55 | 0.40 | 0.25 | 0.10 | 0.02 | 경험치 1 |

(기존 코드의 `gap = monsterLevel - SLACK - memberLevel`, `1 - STEP x gap`과 같은 식이다. 바꾸는 것은 기본값뿐이고 `d >= 15`의 하드 규칙만 새 분기다.) 기본값 변경: `FIELD_CARRY_STEP 0.12 -> 0.15`, `FIELD_CARRY_MIN 0.2 -> 0.02`, `FIELD_CARRY_SLACK 5`(유지), 신규 `FIELD_CARRY_HARD_GAP 15`, `FIELD_CARRY_HARD_DROP_MUL 0`. `kill_log.xp_factor`는 `NUMERIC(4,3)`이라 0.02를 담는다(하드 격차의 "경험치 1"은 `xp_factor`가 아니라 `xp_granted = 1`로 남는다).

**골드도 감쇠한다(추가 제안, 결정 대기 4)**: 지금 `rollKillDrops`의 기본 골드 1더미(8~16)는 `chanceMul`에 곱해지지 않아 캐리 중에도 처치마다 골드가 100% 나온다. `FIELD_CARRY_GOLD_SCALE=true`(기본)면 필드 세션에서 `factor < 1`일 때 기본 골드 더미에도 `factor`를 곱한다(최소 1, 하드 격차는 1). 훅: `kills/killRules.ts` `rollKillDrops`가 `goldMul` 인자를 더 받고 `kills/killService.ts`가 `target.field.xpFactor`에서 넘긴다.

훅 위치: `fieldsessions/xpFactor.ts`(함수), `fieldsessions/fieldKillContext.ts` `resolveSessionTarget`(7번 분기, 하드 격차 -> `xpFactor`와 `hardXp: true`를 `FieldKillInfo`에 실음), `kills/killService.ts`(`hardXp`이면 `shared = 1`), `config/phase8Env.ts`(기본값, 새 키).

| 환경변수 | 기본 | 뜻 |
|---|---|---|
| `FIELD_CARRY_SLACK` | `5` | 이 격차까지는 감쇠 없음(변경 없음) |
| `FIELD_CARRY_STEP` | `0.15` (기존 0.12) | 격차 1당 감소 |
| `FIELD_CARRY_MIN` | `0.02` (기존 0.2) | 배율 하한 |
| `FIELD_CARRY_HARD_GAP` | `15` | 이 격차 이상이면 경험치 1 |
| `FIELD_CARRY_HARD_DROP_MUL` | `0` | 하드 격차의 재료·장비 확률 배율 |
| `FIELD_CARRY_GOLD_SCALE` | `true` | 기본 골드 더미에도 배율 적용 |

**테스트** (`hunting.test.ts`, `fieldSessions.test.ts` 확장, 순수 함수 단위 테스트 포함):

1. 위 표의 배율 전부(`d = 5, 6, 10, 11, 12, 14`), `d <= 5`는 1.
2. `d = 15, 39`: `xp_granted = 1`, 재료·장비 드롭 없음, 기본 골드 1.
3. Lv1이 Lv40 사냥터 파티 세션에서 처치: 처치당 경험치 1(이전 15 이상). 혼자 사냥(세션 2인 미만)은 변하지 않는다.
4. `FIELD_CARRY_GOLD_SCALE=false`면 골드는 이전과 같다.
5. 레벨이 같거나 격차 5 이하 파티는 배율 1(정상 플레이 영향 없음 확인).
6. 이론 계산 확인(PLAN 4절): 하드 격차 구간의 시간당 경험치가 솔로의 0.1% 수준 이하임을 `Tools/balance/theory_progress.py` 확장으로 계산해 기록(스크립트 단계).

## 7. 경매: 구매자 자격, 양방향 쌍 한도, 상점 품목 상한, 의심 거래 기록 (A6, B7)

### 7.1 구매자 자격

등록 자격(Lv10, 계정 7일)을 구매·입찰에도 적용한다. 훅: `auction/auctionService.ts` `lockAndCheck`(내 계정 판정 직후, 쌍 한도 직전). 레벨은 `ctx.char.level`(서버), 계정 나이는 `repo.accountCreatedAt`. 미달이면 `FlagError(422, '아직 경매에서 구매할 수 없습니다.', 'BUYER_GATE', { kind:'gate', severity:1, detail:{ side:'buyer', level } }, { need_level, need_days })`. 환경변수 `AUCTION_BUYER_MIN_LEVEL=10`, `AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS=7`(운영에서 0 금지: 기존 `AUCTION_MIN_*`와 같은 기동 검사). 아이템을 이미 가진 입찰자의 반환·낙찰 정산(틱)은 자격과 무관하게 진행한다(입찰 당시 통과했으므로).

### 7.2 쌍별 한도를 양방향 합산

`repo.pairToday`를 양방향으로 바꾼다: 체결 `(seller=$1 AND buyer=$2) OR (seller=$2 AND buyer=$1)`의 건수·금액 합, 진행 중 최고 입찰도 `(seller=$1 AND bidder=$2) OR (seller=$2 AND bidder=$1)`. 기존 인덱스 `auction_trades_pair(seller_account_id, buyer_account_id, traded_at)`가 두 방향 범위 스캔(BitmapOr)에 그대로 쓰이므로 새 인덱스는 없다. **`repo.lockPair`의 키를 대칭으로 한다**: `auction-pair:${min}:${max}`(지금은 `seller:buyer` 순서라 반대 방향 요청이 서로 직렬화되지 않는다). 한도 값(`AUCTION_PAIR_DAILY_TRADES=3`, `AUCTION_PAIR_DAILY_GOLD=100_000_000`)은 그대로이고 결정 대기 5가 금액 한도 하향을 묻는다.

### 7.3 상점 판매 품목의 기준가 상한 = 상점가 x 3

`auction/auctionPricing.ts` `limitsOf`에 상한 한 줄을 더한다: 아이템 기본 id가 `shop.stock`(상점이 파는 것)에 있고 강화 단계가 0(`parseItemKey(key).level === 0`)이면 `max = min(max, shopBuyPrice x count x AUCTION_SHOP_CEIL_MULT(3))`. 기준가 출처(`history|vendor`)와 무관하게 적용한다(체결 기록이 부풀려져도 상점가의 3배를 못 넘는다). `min > max`가 되면 기존 규칙(`min = max`)이 따른다. 강화된 장비(+n)와 상점에서 팔지 않는 재료는 해당 없음(기존 100배 규칙 유지). **이미 등록된 매물에는 소급하지 않는다**(만료까지 둠).

### 7.4 성공한 의심 거래 기록 (`auction_trade_flags`)

체결 직후 같은 트랜잭션에서 `auction/auctionSettle.ts` `closeAsSold`가 `insertTrade`(이제 `RETURNING id`)의 결과로 `tradeFlags.evaluate(client, trade)`를 부른다. 틱 정산(캐릭터 락 없음)에서도 같은 함수가 돈다. **거절하지 않고 기록만 한다**(차단 여부는 `AUCTION_BLOCK_SAME_DEVICE`). 플래그(한 거래에 여러 개, `UNIQUE(trade_id, flag)`):

| flag | 조건 | `weight_pct`(경제 정지 가중, 12.3) |
|---|---|---|
| `CEILING_PRICE` | `price >= AUCTION_FLAG_CEIL_RATIO(0.9) x 체결 시점에 다시 계산한 limitsOf.max` | 150 |
| `NEW_BUYER` | 구매자 계정 나이 `< AUCTION_FLAG_NEW_BUYER_DAYS(14)`일 **그리고** `price >= AUCTION_FLAG_MIN_PRICE(100_000)` | 200 |
| `SAME_DEVICE` | 두 계정이 `AUCTION_FLAG_DEVICE_DAYS(30)`일 안에 같은 `device_hash`를 `account_devices`에 가짐 | 300 |
| `SAME_STEAM` | 두 계정의 `steam_key`(소유자 포함)가 같음 | 300 |
| `SAME_IP` | 두 계정이 `AUCTION_FLAG_IP_DAYS(7)`일 안에 같은 `ip`를 `account_ips`에 가짐 | 150 |
| `PAIR_REPEAT` | 최근 7일 두 계정 사이 체결(양방향)이 이번 포함 `AUCTION_FLAG_PAIR_REPEAT(3)`건 이상 | 150 |

`price >= AUCTION_FLAG_MIN_PRICE`가 아닌 `NEW_BUYER`는 기록하지 않는다(싼 거래의 소음 방지). 거래 가중치는 플래그 중 **최댓값**(합산하지 않음)을 `income_hourly.auction_in_w`에 쓴다(12.3). 같은 기기·Steam 쌍의 구매를 막으려면 `AUCTION_BLOCK_SAME_DEVICE=true`(기본 `false`, 결정 대기 3): `lockAndCheck`가 `SAME_DEVICE|SAME_STEAM`이면 `FlagError(403, ..., 'SAME_DEVICE_TRADE')`로 막고 플래그 `self_account` 계열로 기록한다.

H7 `GET /admin/auction/trade-flags?flag=&since=&min_price=&cursor=&limit=` (viewer): 목록 항목 `{ trade_id(uuid), item_key, price, seller:{account_id(uuid), character:{id,name}}, buyer:{...}, flags:[{flag, detail}], traded_at }`. 정렬 `traded_at DESC`, 인덱스 `auction_trade_flags_time`. 에러 `400 VALIDATION`.

### 7.5 훅 위치

`auctionService.ts` `lockAndCheck`(7.1), `auctionRepository.ts` `pairToday`·`lockPair`·`insertTrade`(7.2), `auctionPricing.ts` `limitsOf`(7.3), `auctionSettle.ts` `closeAsSold`(7.4), `antiabuse/tradeFlags.ts` 신규, `admin/economyholds/`에 H7.

### 7.6 환경변수

| 이름 | 기본 |
|---|---|
| `AUCTION_BUYER_MIN_LEVEL` | `10` |
| `AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS` | `7` |
| `AUCTION_SHOP_CEIL_MULT` | `3` |
| `AUCTION_FLAG_CEIL_RATIO` | `0.9` |
| `AUCTION_FLAG_NEW_BUYER_DAYS` | `14` |
| `AUCTION_FLAG_MIN_PRICE` | `100000` |
| `AUCTION_FLAG_DEVICE_DAYS` | `30` |
| `AUCTION_FLAG_IP_DAYS` | `7` |
| `AUCTION_FLAG_PAIR_REPEAT` | `3` |
| `AUCTION_BLOCK_SAME_DEVICE` | `false` |

### 7.7 테스트 (`auction.test.ts`, 신규 `auctionAbuse.test.ts`)

1. 레벨 9 또는 계정 6일 구매자: 즉시 구매·입찰 모두 `422 BUYER_GATE`, `auction_flags(kind=gate, side=buyer)`. 정확히 Lv10·7일은 통과.
2. 쌍 한도: A->B 3건 체결 후 B->A 구매 시도가 `PAIR_LIMIT`(방향 합산). 금액도 양방향 합.
3. 반대 방향 두 요청 동시(Promise.all)가 직렬화되어 한도를 넘는 체결이 없다(대칭 락 키).
4. 상점 판매 품목(예: 상점가 100인 커먼 장비 +0): 등록 상한 300 x 개수. 같은 품목 +3 강화본은 기존 규칙. 상점에 없는 재료는 기존 규칙.
5. 상점 상한 이후 `min > max`일 때 등록이 `min = max`로 정리된다.
6. 상한가 체결 -> `CEILING_PRICE` 행, 새 계정 + 10만 이상 -> `NEW_BUYER`, 같은 기기 -> `SAME_DEVICE`, 같은 IP -> `SAME_IP`. 체결 자체는 성공한다. 같은 플래그가 중복 INSERT되지 않는다.
7. 입찰 낙찰 정산(틱)도 플래그가 남는다.
8. `AUCTION_BLOCK_SAME_DEVICE=true`: 같은 기기 쌍 구매가 막힌다.
9. H7 필터·정렬·권한(viewer 가능, 인증 없음 401).

## 8. 전직·각성의 서버 기록 (A8), 뽑기 장비 귀속 (A7)

### 8.1 설계

문제: `validateCareer`는 클라이언트 값(`career`, `questStage`, `awakened`)을 레벨 15 조건만 보고 저장한다. 전직(`Progression.Promote`)은 레벨 15 이상이면 비용·퀘스트 없이 되고, 각성은 `questStage=5`와 `awakened=true`만 쓰면 된다. **서버가 승급과 각성 단계를 직접 기록**하고 `PUT state`는 그 기록과 일치하는 값만 받는다.

서버 기록 = `character_career(character_id PK, career 1..4, stage 0..5, promoted_at, stage_changed_at, source)`. 행이 없으면 미전직. `stage`는 C# `AwakeningStage`와 같다: 0~1 대화, 2 시련 대기, 3~4 대화, 5 각성 완료(= `awakened`).

### 8.2 엔드포인트

공통: 인증 액세스, `request_id` 필수, `runEconomy`와 같은 틀(캐릭터 행 잠금, `request_log` 멱등성 재생, 같은 `request_id`에 다른 본문이면 `422 IDEMPOTENCY_MISMATCH`). 속도 제한 `RATE_CAREER_PER_SEC(1)`(캐릭터당).

**C1 `POST /characters/{uuid}/career/promote`**
- 요청: `z.strictObject({ request_id: z.uuid(), career: z.number().int().min(1).max(4) })`(1 Fighter, 2 Guardian, 3 Arcanist, 4 Bishop, C# `Career` 열거).
- 검사: 레벨 `>= CAREER_PROMOTE_MIN_LEVEL(15)`(`422 LEVEL_TOO_LOW`, `errors:{need:15,have}`), 기본 직업 일치(`career 1,2`는 warrior, `3,4`는 mage, 아니면 `422 CAREER_BASE_MISMATCH`), 이미 전직했으면 `409 ALREADY_PROMOTED`(같은 직업을 같은 `request_id`로 재전송하면 멱등 재생).
- 효과: `character_career` INSERT(`stage 0`, `source='promote'`), `character_state.career`를 `{schema:1, career, nodes:[], training:<기존 값 유지>, refunded:<기존 값 유지>, questStage:0, awakened:false}`로 서버가 쓴다(훈련·환급 값은 저장된 상태에서 가져온다).
- 응답 `200` `data`: `{ career, stage: 0, promoted_at }`.
- 결과: `career_path` 퀘스트의 `career_promoted` 확인(`questService.ts:104`)은 `character_state.career`를 읽으므로 코드 변경 없이 서버가 부여한 값만 인정한다.

**C2 `POST .../career/awakening/trial/start`**
- 요청: `{ request_id }`.
- 검사: 전직했는가(`409 NOT_PROMOTED`), `stage == 2`(`409 STAGE_MISMATCH`, `errors:{stage}`), 프레즌스가 신선하고 맵이 **마을(`maps.json`의 `safe` 맵, 비instanced)**(`409 NOT_IN_TOWN`), 활성 파티 판·필드 세션 없음(`409 IN_PARTY_CONTENT`, 클라이언트 `CareerTrials.Begin`과 같은 규칙), 직업별 필수 노드 보유(`422 NODES_REQUIRED`: 저장된 `character_state.career.nodes`에 아르카니스트 `m_fire,m_ice,m_storm`, 비숍 `b_cleanse,b_wing`, 가디언 `g_taunt,g_wall`. 값은 `careers.json`의 새 `trials` 항목에서 읽는다, 8.5).
- 효과: 열린 시도가 있으면 그것을 돌려주고(멱등), 없으면 `character_career_trials`에 열린 행(`started_at = now()`)을 만든다.
- 응답 `200` `data`: `{ started_at, expires_in_seconds: AWAKEN_TRIAL_MAX_SECONDS }`(시험 시간 상한만 알린다. 최소 시간은 알리지 않는다).

**C3 `POST .../career/awakening/trial/finish`**
- 요청: `{ request_id, result: 'success' | 'fail' }`(클라이언트가 시련 결과를 판정하므로 "무슨 일이 일어났나"만 말한다. 서버는 성공 주장을 아래로 검증한다).
- `fail`: 열린 행을 `outcome='fail'`로 닫는다(재도전 가능). 응답 `{ stage: 2 }`.
- `success` 검사(순서대로, 첫 실패에서 중단): 열린 시도 없음 `409 TRIAL_NOT_STARTED`; `now - started_at > AWAKEN_TRIAL_MAX_SECONDS(120)`이면 시도를 `expired`로 닫고 `409 TRIAL_EXPIRED`; `now - started_at < 직업별 최소 시간`이면 `422 TRIAL_TOO_FAST`(최소 시간은 `careers.json trials[career].minSeconds`, 데이터가 없으면 `AWAKEN_TRIAL_MIN_SECONDS(6)`; 클라이언트 `CareerTrials.Update`가 가디언·비숍은 `elapsed >= 20` 성공 조건이라 20을 내보낸다) + `anomaly_log(career_state, 2, {why:'trial_too_fast'})`; 프레즌스가 신선하고 시도 내내 같은 마을 맵에 있었는가(`online_sessions.map_since <= started_at`이고 `map_id`가 마을, 아니면 `409 NOT_IN_TOWN`).
- 효과(성공): 시도 `outcome='success'`, `character_career.stage = 3`, `stage_changed_at = now()`, `character_state.career.questStage = 3`로 서버가 쓴다.
- 응답 `200` `data`: `{ stage: 3 }`.

**C4 `POST .../career/awakening/advance`**
- 요청: `{ request_id, from_stage: 0 | 1 | 3 | 4 }`(대화로 넘기는 단계만. 2는 시련 C3만 넘긴다: `from_stage: 2`는 `422 TRIAL_REQUIRED`, 5 이상은 `400`).
- 검사: `409 NOT_PROMOTED`, `from_stage == stage`(아니면 `409 STAGE_MISMATCH`, `errors:{stage}`로 현재 값을 알려 클라이언트가 맞춘다), `now - stage_changed_at >= AWAKEN_STAGE_MIN_GAP_SECONDS(10)`(아니면 `429 STAGE_TOO_FAST`, `retry_after_sec`; 대화는 몇 초 걸리고 단계 점프 자동화를 막는다), 프레즌스 마을(`409 NOT_IN_TOWN`).
- 효과: `stage + 1`. `4 -> 5`이면 `awakened`가 되고 `character_state.career`에 `questStage:5, awakened:true`를 서버가 쓴다(시작 노드 슬롯 장착은 클라이언트가 기존대로 한다).
- 응답 `200` `data`: `{ stage, awakened: boolean }`.

### 8.3 `PUT /characters/{uuid}/state`의 거절 규칙 (E9)

`characters/careerRules.ts` `validateCareer`에 서버 기록 `granted: { career, stage } | null`을 인자로 더한다(훅: `stateRules.ts` `validateState`가 `character_career` 행을 읽어 넘긴다).

| 입력 | 서버 기록 | 결과 |
|---|---|---|
| `career != 0`(`granted` 없음) | 행 없음 | `422 INVALID_CAREER` `reason:'NOT_GRANTED'` |
| `career != granted.career` | 행 있음 | `422 INVALID_CAREER` `reason:'NOT_GRANTED'` |
| `questStage > granted.stage` | | `422 INVALID_CAREER` `reason:'STAGE_NOT_GRANTED'` |
| `awakened == true`인데 `granted.stage != 5` | | `422 INVALID_CAREER` `reason:'NOT_GRANTED'` |
| `career == 0`인데 행 있음(전직 취소 시도) | | `422 INVALID_CAREER` `reason:'MISSING_STATE'`(기존 규칙) |
| `questStage < granted.stage`(클라이언트 값이 뒤처짐) | | **허용하지 않는다**: `422 INVALID_CAREER` `reason:'QUEST_REGRESSION'`(기존 규칙 `prev` 비교와 같은 코드. 서버가 값을 `PUT`보다 먼저 쓰므로 클라이언트는 C1/C3/C4 응답을 받은 뒤 저장한다) |

기존 규칙(레벨 15, 기본 직업, 노드·예산·선행 검사, `QUEST_REQUIRED` 등)은 그대로 위에 더해진다. `characters/careerRules.ts`의 `validCareerActive`(젬 슬롯 4 각성 스킬)는 `awakened == true && questStage == 5`를 이미 요구하므로 서버가 `stage 5`를 부여하지 않으면 각성 스킬 슬롯도 거절된다.

### 8.4 기존 캐릭터 이전

마이그레이션이 `character_state.career`가 있고 `career > 0`인 모든 캐릭터를 `character_career(source='legacy_backfill')`로 채운다(`stage`는 `questStage`, 5면 각성). 이미 저장된 값은 인정하되 `source`로 감사할 수 있다(운영자가 레벨 15 미만·비정상 조합을 쿼리로 찾을 수 있다). 소급 거절은 하지 않는다(이미 저장된 상태를 갑자기 무효로 만들면 정상 플레이어의 전직·각성이 사라진다. 의심 건은 `source`와 쿼리로 운영자가 따로 본다).

### 8.5 데이터 계약 (`careers.json`에 항목 추가, Unity 내보내기 변경)

`careers.json` 최상위에 `trials`를 더한다(클라이언트 `CareerTrials`와 같은 값, 서버는 읽기만):

```
"trials": { "1": { "minSeconds": 6,  "requiredNodes": [] },
            "2": { "minSeconds": 20, "requiredNodes": ["g_taunt","g_wall"] },
            "3": { "minSeconds": 6,  "requiredNodes": ["m_fire","m_ice","m_storm"] },
            "4": { "minSeconds": 20, "requiredNodes": ["b_cleanse","b_wing"] } }
```

(값은 `CareerTrials.cs`의 성공 조건에서 나온 것이다. 파일에 항목이 없으면 서버는 `AWAKEN_TRIAL_MIN_SECONDS`와 "필수 노드 검사 건너뜀"으로 동작한다.)

### 8.6 뽑기 장비 계정 귀속 (A7)

`economy/economyContext.ts` `bindFor(reason, itemKey)`에 한 줄: 장비이고 `reason === 'gacha'`이면 `strongerBind(floor, 'account')`(상점 구매 `shop_buy`와 같다). `starshop/starshopService.ts` `rollGear`의 `ctx.addItem('bag', item.id, 1, 'gacha', ...)`는 그대로 두면 새 `bindFor`가 적용된다. 계정 귀속 아이템은 `auctionService.processList`의 `ITEM_BOUND`로 거래 불가이고, 옛 스택(귀속 없음)은 **소급 변경하지 않는다**(결제 전 시험 데이터. 결정 대기 2). 기존 등록 중인 매물도 그대로 둔다.

### 8.7 환경변수·훅

| 이름 | 기본 | 뜻 |
|---|---|---|
| `CAREER_SERVER_TRUTH` | `enforce` | `off|log|enforce` (PUT 거절 규칙) |
| `CAREER_PROMOTE_MIN_LEVEL` | `15` | 전직 최소 레벨(데이터 내보내기 전까지의 기본값) |
| `AWAKEN_STAGE_MIN_GAP_SECONDS` | `10` | 대화 단계 사이 최소 간격 |
| `AWAKEN_TRIAL_MIN_SECONDS` | `6` | `careers.json trials` 없을 때 최소 시련 시간 |
| `AWAKEN_TRIAL_MAX_SECONDS` | `120` | 시련 시작 후 보고 마감(클라이언트 제한 45초 + 여유) |
| `RATE_CAREER_PER_SEC` | `1` | 캐릭터당 |

훅: `characters/careerGrantRoutes.ts`·`careerGrantService.ts` 신규, `stateRules.ts`·`careerRules.ts`(8.3), `characterService.ts` `getCharacter`(응답 `career`는 기존 `character_state.career` 그대로, 서버 기록과 항상 일치), `economyContext.ts` `bindFor`.

### 8.8 클라이언트 계약

전직: 전직 확정 시 C1을 부르고 성공 응답을 받은 뒤에만 `Progression.Promote`를 적용하고 저장한다. 시련: 시작 시 C2, 종료 시 C3(`success|fail`), `Finish(true)`가 로컬 `AdvanceAwakening(2)` 대신 C3 성공 응답 `stage:3`을 받아 적용. 대화 단계: C4 응답 후 로컬 `AdvanceAwakening`. 저장(`PUT state`)은 항상 이 응답들 이후. 오프라인 모드는 변경 없음(온라인 서버가 없으면 로컬 진행).

### 8.9 테스트 (`careers.test.ts` 확장)

1. C1: Lv14 `422 LEVEL_TOO_LOW`, 전사에 `career 3` `422 CAREER_BASE_MISMATCH`, 성공 시 `character_career`+`character_state.career` 기록, 재전송 멱등, 다른 직업으로 재시도 `409 ALREADY_PROMOTED`.
2. `PUT state`에 서버가 부여하지 않은 `career: 1` -> `422 INVALID_CAREER NOT_GRANTED`. `questStage: 5, awakened: true`만 쓴 상태 -> `STAGE_NOT_GRANTED`.
3. C2~C4 정상 흐름 0->1->2, 시련(C2, 20초 뒤 C3 success)->3->4->5, 각성 슬롯 `PUT` 통과.
4. C4 `from_stage: 2` `422 TRIAL_REQUIRED`, 현재 단계와 다르면 `409 STAGE_MISMATCH`(현재 값 반환), 10초 안에 연속 호출 `429 STAGE_TOO_FAST`.
5. C3 success가 최소 시간 전이면 `422 TRIAL_TOO_FAST`+이상 기록, 시작 없이 finish `409 TRIAL_NOT_STARTED`, 120초 뒤 `409 TRIAL_EXPIRED`.
6. 프레즌스가 던전 맵이거나 신선하지 않으면 C2~C4 `409 NOT_IN_TOWN`.
7. 파티 판·필드 세션 중 C2 `409 IN_PARTY_CONTENT`.
8. 백필: 이전 저장 값을 가진 캐릭터가 `legacy_backfill`로 채워지고 `PUT`이 통과한다.
9. `career_path` 퀘스트 청구는 서버 부여 전에는 `QUEST_NOT_DONE`(`career_promoted`), 부여 후 통과.
10. 뽑기 장비: `gacha` 장비가 `bind='account'`로 들어가고 경매 등록은 `ITEM_BOUND`, 상점 구매 장비와 같은 동작.

## 9. 멤버 카드 대조의 서버 계약과 서버 측 점검 (A9)

서버는 이미 필드 세션 뷰 `members[]`에 `level`, `gear_hash`(`gearHash.ts`: 착용 장비 키 정렬 후 SHA-256 앞 16자)를 준다. 파티 판 뷰(`runView.ts`)도 같은 값을 가진다. 호스트 클라이언트가 이 값으로 멤버 카드를 대조하려면 해시만으로는 능력치를 다시 만들 수 없으므로 **서버 뷰에 원본을 더한다**.

### 9.1 서버가 주는 값 (뷰 변경)

`FieldSessionView.members[]`와 `RunView.members[]`(파티 판)에 추가:

- `worn: [{ slot: int, item_key: string }]` (서버가 아는 착용 장비, `character_items location='worn'`, 최대 8개)
- `career: int`(서버 기록 `character_career.career`, 없으면 0)
- 기존 `level`, `gear_hash`, `name`, `class`는 그대로(전부 서버 값).

비용: 멤버 4명 x 8항목으로 응답이 약 1KB 늘고, 뷰는 이미 `gearHashes`를 만들 때 착용 키를 읽으므로(`wornKeysOf`) 추가 쿼리는 없다.

### 9.2 클라이언트 계약 (호스트 PC)

1. 멤버의 `Hello`에서 받은 카드 값과 서버 뷰를 대조한다: `card.level != view.level` 또는 `card.gear_hash != view.gear_hash` 또는 `card.career != view.career`.
2. 불일치하면 **서버 뷰 값으로 덮어쓴다**(레벨, `worn`으로 장비·능력치 재구성, 직업). 표시 이름은 항상 `view.name`(서버 값, 카드의 이름은 무시).
3. 멤버 위치는 이동 속도 상한으로 보정한다: 호스트는 틱마다 `|위치 변화| <= 최대 이동 속도 x dt x 1.5`로 자르고 초과분은 마지막 허용 위치에 둔다(순간이동 방지, 데이터 `player.json`의 이동 속도 사용).
4. 불일치가 있었던 멤버는 호스트가 `observe`(F6)와 방장 보고에 표시한다(9.3).

### 9.3 서버 측 점검 (정산 때 가능한 것)

호스트와 멤버 사이의 카드 교환은 서버가 못 보므로 서버가 직접 막을 수 있는 것은 아래뿐이다.

- **불일치 보고 기록**: 필드 세션 `observe`(F6) 본문과 방장 보고 `members[]`에 `card_mismatch: boolean`(optional)을 더한다. 서버는 `true`를 받으면 대상 멤버의 계정에 `anomaly_log(kind='member_card', severity 2, detail{ session_id|run_id, character_id })`를 남긴다(같은 대상·같은 판·세션에서 한 번만). 위조 카드를 계속 보내는 멤버를 사후 추적하는 근거다.
- **정산은 이미 서버 값**: 방장 보고 검증(`partySettle.ts` `validateHostReport`)은 멤버 `attackCap`을 서버가 아는 레벨·착용 장비로 계산하고 `MEMBER_UNKNOWN`·`DAMAGE_OVER_CAP`을 거른다. 카드 위조로 정산이 왜곡되지 않음을 테스트로 고정한다.
- **불일치 반복은 판 결과에 쓰이지 않는다**(호스트 거짓 보고로 남을 몰아내는 것을 막는다): `member_card`는 관리자 조회용이다.

훅: `fieldsessions/fieldView.ts` `buildFieldView`(`worn`, `career`), `partyruns/runView.ts`, `fieldsessions/fieldValidation.ts`(F6 본문), `fieldService.ts` `observe`, `partyruns/partyRunValidation`의 방장 보고 본문, `partySettle.ts`(기록).

환경변수: 없음(기록은 항상, 강제 없음).

### 9.4 테스트 (`fieldSessions.test.ts`, `partyRuns.test.ts`)

1. 뷰에 `worn`, `career`가 서버 값으로 실린다(장비를 바꾸면 `gear_hash`와 `worn`이 같이 바뀐다).
2. F6 `card_mismatch: true` -> `member_card` 이상 기록 한 건(중복 호출은 한 건).
3. 방장 보고 `members[].card_mismatch`도 같다.
4. 멤버가 부풀린 카드(레벨 999)를 호스트가 대조하지 않았어도 정산의 `attackCap`은 서버 레벨이라 `DAMAGE_OVER_CAP`으로 보고가 거절된다.
5. 남의 캐릭터 id를 `card_mismatch` 대상으로 보내면 무시한다(활성 멤버가 아니면 `404`가 아니라 무시).

## 10. 이름 규칙 (B8)

- 새 운영 목록 `server/data/reserved_names.json`(게임 데이터 아님, `banned_words.json`처럼 운영자가 수정하며 서버가 mtime으로 다시 읽는다):

```
{ "note": "운영 예약어",
  "substring": ["운영자","관리자","운영팀","개발자","스태프","공지","시스템","고객센터","administrator","moderator"],
  "token":     ["gm","admin","mod","staff","cs","dev","npc","system","sys"],
  "allow":     [] }
```

- **정규화(skeleton)**: NFKC -> 소문자 -> 혼동 문자 접기(`0->o, 1->i, l->i, |->i, 3->e, 4->a, 5->s, 7->t, 8->b, $->s, @->a`) -> 같은 문자 연속 3개 이상은 2개로 축약 -> 숫자 제거 사본을 따로 만든다.
- **판정**: ① `substring` 항목은 정규화 skeleton(숫자 제거 사본 포함)에 **부분 문자열**로 있으면 금지. ② `token` 항목(짧은 영문 토큰)은 숫자 제거 skeleton이 토큰과 **같거나 토큰으로 시작/끝**나면 금지(예: `GM`, `gm01`, `GM철수`, `admin7`, `AdminTest`; 토큰 중간에 낀 경우 `Pigmy`는 통과). ③ 기존 `utils/bannedWords.ts`의 `containsBannedWord`, `hasObfuscatedBannedWord`로 욕설도 검사한다. ④ `allow` 목록의 정확한 이름은 ①②를 통과시킨다(오탐 구제).
- 응답: `422 NAME_FORBIDDEN`, `errors:{ reason:'RESERVED' | 'BANNED' }`(어느 단어인지는 알리지 않는다). 기존 `NAME_TAKEN`, `VALIDATION`(길이·문자)보다 **뒤**(검증 통과 후 중복 검사 전)에서 판정한다.
- 훅: `characters/characterService.ts` `createCharacter`(`createCharacterBody` 통과 직후), `antiabuse/reservedNames.ts`. 파티 표시 이름을 서버 이름으로 쓰는 것은 9.2(클라이언트).
- 기존 캐릭터는 소급 개명하지 않는다. 대신 7단계 정합성 작업(`ops/jobs/integrity.ts`)에 "예약어에 걸리는 살아 있는 캐릭터 이름 수"를 점검 항목 `I-names`로 더한다(로그만).
- 환경변수 `NAME_RESERVED_MODE`(`off|enforce`, 기본 `enforce`).
- 테스트(`characters.test.ts`): `운영자`, `운영자1`, `GM`, `gm01`, `Gm철수`, `admin`, `Adm1n`(접기), `4dmin`, `공지`, `시스템2`, `시 스 템`은 NFC/문자 규칙(`NAME_CHARS`)이 먼저 거절하므로 400 VALIDATION, `Pigmy`·`개발자님`(`개발자` 포함이라 금지)·`길동`은 각각 통과/금지/통과, 욕설 금칙어 변형(`시발`, `씨1발` 숫자 접기), `allow`에 넣은 이름은 통과, 파일이 없으면 빈 목록으로 동작.

## 11. 시험 스크립트 가드, 마을 표시 서버 값 (B9, B11)

### 11.1 시험 스크립트는 `DEPLOY_STAGE=test`가 명시돼야만 실행

대상 스크립트 5개: `scripts/test-stars.ts`, `test-give.ts`, `test-release-held.ts`, `test-level.mjs`, `test-reset-entries.mjs`. 지금은 `DEPLOY_STAGE`가 없으면 `'dev'`로 보고 실행한다(서버는 `DEPLOY_STAGE` 기본을 `live`로 본다, `phase8Env.ts`). 공용 가드 `scripts/_stageGuard.mjs`(ts 스크립트도 import)를 만들어 **모두 맨 앞에서** 부른다:

1. `process.env.DEPLOY_STAGE`를 먼저 보고, 없으면 `server/.env` 파일에서 읽는다.
2. 값이 **정확히 `test`일 때만** 통과. 없음·`live`·`dev`·그 밖은 비정상 종료(코드 1)와 안내 `"DEPLOY_STAGE=test 가 명시된 환경에서만 실행합니다 (현재: <값|없음>)"`.
3. 통과하면 `[TEST SCRIPT] stage=test db=<호스트:DB명>`을 출력해 대상 DB를 눈으로 확인하게 한다(비밀번호는 출력하지 않는다).

로컬 개발자는 `server/.env`에 `DEPLOY_STAGE=test`를 한 번 적는다(개발 문서 안내 한 줄). **운영 기동 가드에 포함**: `config/env.ts`에 "`NODE_ENV=production`에서 `DEPLOY_STAGE`가 환경에 **명시되지 않으면** 기동 실패"를 더한다(지금은 조용히 `live`로 보이는 상태를 없애, `.env` 한 줄 누락이 시험/운영 오판으로 번지지 않게 한다). 테스트(`steamAndConfig.test.ts` 확장): production + 미지정 -> 기동 실패, production + `live`/`test` -> 통과, 개발 환경은 영향 없음. 스크립트 가드 테스트(`scripts/guard.test` 또는 `system.test.ts`): 변수 없음/`live`/`dev` -> 종료 1, `test` -> 통과.

### 11.2 마을 표시를 서버 값으로 (E11)

`chat/townPresence.ts` `handleTownPos`가 클라이언트의 `cls`, `career`, `skin`, `weapon`, `level`을 그대로 중계한다. `ChatSession`에 서버 프로필 `profile`을 둔다:

| 필드 | 서버 값의 출처 | 클라이언트 값 처리 |
|---|---|---|
| `cls` | `characters.class` | 무시 |
| `level` | `characters.level` | 무시 |
| `career` | `character_career.career`(없으면 0) | 무시 |
| `weapon` | 착용 무기 슬롯의 기본 아이템 id(`character_items location='worn'`, 강화 단계 제거) | 무시 |
| `skin` | 클라이언트 값이 `account_cosmetics`에 **그 계정이 소유**한 외형이면 그 값, 아니면 `''` | 소유 검사 |
| `name` | 이미 서버(`ChatSession.characterName`) | - |

`profile`은 `hello`에서 한 번 읽고(캐릭터·장비·전직·외형 소유를 한 쿼리 묶음으로), 마을 진입(`entering`)과 `WS_REVALIDATE_SECONDS` 틱에서 다시 읽어 레벨업·장비 교체가 늦어도 수십 초 안에 반영되게 한다. 쓰기 경로(`EconCtx`)가 레벨업을 알 때 `registry.ofAccount(accountId)`의 `profile.level`을 커밋 뒤 갱신하는 것은 선택 최적화다. 외형 소유 확인은 `ownedOf(accountId)`와 같은 쿼리(캐릭터당 1회, 세션 메모리 캐시). 훅: `chat/townPresence.ts` `handleTownPos`, `chat/chatSession.ts`(`profile`), `chat/wsServer.ts` `hello()`. 프로토콜은 변하지 않는다(필드는 있고 값만 서버 것).

테스트(`chatWs.test.ts`): 클라이언트가 `level: 999, career: 4, skin: '<미소유 스킨>'`을 보내도 이웃이 받은 `town.pos`는 서버 값(`level` 실제, `career` 0, `skin ''`); 소유한 스킨은 통과; 레벨업 후 다음 마을 진입 시 새 레벨; 무기 교체 반영.

## 12. 경제 속도 정지 (A10, 가장 중요한 새 기능)

### 12.1 목적과 동작 한눈에

플레이 시간(프레즌스 활동 시간)에 비해 재화 획득이 이론 최대의 3배를 넘으면 그 캐릭터(와 연결 계정)의 **쓰기·옮기기 경로**를 멈추고 운영자가 확인한다. 사냥·이동·처치 보고·드롭 줍기는 계속된다. 자동 해제는 없다.

흐름: ① `EconCtx`가 원장을 쓸 때 시간별 버킷 `income_hourly`를 같은 트랜잭션에서 증분(싸다) -> ② 변경된 캐릭터를 프로세스 안 "더티 집합"에 넣음 -> ③ 30초 워커가 더티 캐릭터의 창 합계를 상한과 비교 -> ④ 초과면 `economy_holds` 생성(모드가 `log_only`면 `shadow`, `enforce`면 `active`) + 연결 계정 전파 -> ⑤ 정지 대상 경로가 `assertNoHold`로 막음.

### 12.2 기대 수입 표 (데이터 계산, 3배 임계)

**서버가 읽는 것**: `server/data/income_caps.json`(스키마 `schema:1`). 생성: `Tools/balance/theory_income.py`(신규, `theory_progress.py`와 같은 방식으로 `server/data/*.json`을 읽음. 이 문서의 계산과 같은 식, 구현 단계에서 만들고 출력과 이 문서 값을 대조한다). 데이터가 바뀌면 스크립트를 다시 돌려 JSON을 갱신한다(수동 값 복사 금지). 형식 요약: `{ schema:1, killsPerMinute:30, bands:[{ minLevel, maxLevel, perHour:{ xp, goldEq, ore, essence, epicPlus }, perDay:{ dungeonXp, dungeonGoldEq, raidMidXp, raidMidGoldEq }, perWeek:{ raidFinalXp, raidFinalGoldEq } }], uniquePlus:{ perDay: 4, perHour: 0.12 } }`.

**가정**(전부 이름 있는 값이고 바꿀 수 있다):
- 이론 최대 처치 속도 `INCOME_MAX_KILLS_PER_MIN = 30`. 근거: 3마리 무리를 6초(이동 4초 + 전투 2초, `theory_progress.py`의 `TRAVEL=4`)마다 정리하는 숙련자. 설계 기준(분당 15마리, HUNTING_BALANCE)의 2배라 시간당 1800마리. **한 시간 연속으로 유지할 수 있는 사람은 없는 값이라 "이론 최대"다.**
- 구간(대역)은 HUNTING_BALANCE의 권장 레벨 구간. 각 대역에서 쓸 수 있는 가장 높은 사냥터는 **몬스터 레벨 <= 대역 최대 레벨 + 2**(`theory_progress.py`의 `level+2` 규칙).
- 처치 경험치는 `maps.json fieldSpawns[].xp`(서버가 지급하는 값 그대로). 골드 환산 = 기본 골드 평균 12(`monsters.json`의 `fieldGoldMin 8`, `fieldGoldMaxExclusive 17`) + 재료 판매가 기대값(뼈 0.75 x 평균 2개 x 5, 광석 0.35 x 1 x 20, 정수 0.08 x 1 x 80 = 20.9) + 장비 드롭 `0.4` x 그 레벨 단계 장비 평균 판매가(무기 풀 기준 근사, 스크립트는 전체 풀 사용). 골드로 팔지 않고 모아 둬도 감시되도록 **획득 시점에 상점 판매가로 환산**한다(12.3).
- 보스: `fieldBoss`(15분 창당 캐릭터 1회 -> 시간당 최대 4회). 보스 처치당 골드 환산 약 750(보스 골드 400~700 + 광석 3~5 + 정수 1 + 기본 드롭), 경험치는 `fieldBoss.xp`.
- 던전·레이드는 일일 한도 합(`dailyEntries 3`, 요일 던전 1일 3판, 레이드 개방 요일·주 1회)을 **일 단위 덩어리(lump)**로 따로 둔다.
- 퀘스트 보상은 캐릭터당 1회 청구(`quest_claims` PK)라 속도 감시에서 **제외**한다(12.3 사유 표).

**1) 시간당(활동 시간 1시간) 이론 최대와 3배 임계** (처치 속도 30/분 = 1800마리/시간)

| 대역 | 레벨 | 쓰는 사냥터(몬스터 Lv) | 처치 XP | XP/h (필드+보스) | 3배 XP/h | 골드환산/h (필드+보스) | 3배 골드환산/h | 광석/정수 per h (필드+보스) |
|---|---|---|---|---|---|---|---|---|
| 1 | 1-4 | 이끼 낀 유적(6) | 9 | 16,200 | 48,600 | 77,700 | 233,100 | 630 / 144 |
| 2 | 5-8 | 검은 뿌리 숲(10) | 12 | 21,600 | 64,800 | 87,200 | 261,600 | 630 / 144 |
| 3 | 9-12 | 붉은 돌 고개(14), 숲 보스(xp 300) | 17 | 31,800 | 95,400 | 90,200 | 270,600 | 646 / 148 |
| 4 | 13-16 | 버려진 채석장(18), 숲 보스 | 23 | 42,600 | 127,800 | 95,800 | 287,400 | 646 / 148 |
| 5 | 17-20 | 바람칼 능선(22), 숲 보스 | 30 | 55,200 | 165,600 | 101,500 | 304,500 | 646 / 148 |
| 6 | 21-24 | 서리 소나무 숲(26), 협곡 보스(xp 750) | 39 | 73,200 | 219,600 | 106,900 | 320,700 | 646 / 148 |
| 7 | 25-28 | 서리 소나무 숲(26), 협곡 보스 | 39 | 73,200 | 219,600 | 106,900 | 320,700 | 646 / 148 |
| 8 | 29-33 | 얼어붙은 호숫가(31), 협곡 보스 | 52 | 96,600 | 289,800 | 112,600 | 337,800 | 646 / 148 |
| 9 | 34-40 | 레벨 40 사냥터(40), 눈보라 보스(xp 1675) | 80 | 150,700 | 452,100 | 123,900 | 371,700 | 646 / 148 |

계산 예(대역 5): XP = 1800 x 30 + 보스 4 x 300 = 54,000 + 1,200. 골드환산 = 1800 x (12 + 20.9 + 0.4 x 54.6) + 4 x 750 = 98,500 + 3,000. (장비 평균 판매가 54.6은 레벨 단계 3 무기 풀 가중 평균.) 광석 = 1800 x 0.35 + 4 x 4, 정수 = 1800 x 0.08 + 4 x 1. 에픽 이상 장비/h = 1800 x 0.4 x (에픽 가중 3 / 풀 합 69) ≈ 31(대역 3부터, 대역 1~2의 단계 0 풀에는 에픽이 없다). **유니크 이상 장비**: 필드 일반 드롭에는 없다(`dropWeight 0`), 출처는 던전 카드 대박·필드 보스 장비(시간당 4 x `bossGearPermille 30`/1000 = 0.12)·레이드 레전더리라 `하루 4 + 활동 1시간당 0.12`로 둔다.

**2) 일 단위 덩어리(요일 던전 3판, 레이드)** (최고 등급 S +50% 경험치, `xpMul` 최댓값 1.5 기준)

| 대역 | 입장 가능 최고 난이도 | 던전 XP/일 | 던전 골드환산/일 | 중간 레이드 XP/일 (Lv20+) | 중간 레이드 골드환산/일 | 최종 레이드 XP/주 (Lv40) | 최종 레이드 골드환산/주 |
|---|---|---|---|---|---|---|---|
| 1~2 | 0 (권장 5) | 8,600 | 3,600 | - | - | - | - |
| 3~4 | 1 (권장 12) | 20,871 | 5,760 | - | - | - | - |
| 5~6 | 2 (권장 20) | 43,477 | 8,640 | 10,092 | 4,500 | - | - |
| 7~9 | 3 (권장 27) | 90,011 | 12,240 | 10,092 | 4,500 | 41,850 (대역 9만) | 18,000 |

계산: 던전 XP/일 = 3 x max(던전별 `clearXpFloor[난이도] x xpMul`) x 1.5. 예 난이도 2: `6441 x 1.5 x 1.5 x 3 = 43,477`. 던전 골드환산/일 = 3 x 카드 골드 최댓값 1200 x `rewardMul`(1, 1.6, 2.4, 3.4). 레이드: 중간 `6728 x 1.5 = 10,092`, 골드 카드 `1500 x 3`; 최종 `27,900 x 1.5 = 41,850`, 골드 카드 `6000 x 3`.

**3) 창별 상한 식** (`cap(window)`는 대역별 값, 임계 = `ECONOMY_HOLD_MULT(3) x cap`)

```
cap(metric, window) = Σ_{시간 버킷 b} rate(metric, band(level_b)) x active_seconds_b / 3600
                    + lump_day(metric, band(최고 레벨)) x days_touched(window)
                    + raid lumps(window)
rate:  표 1,  lump_day: 표 2,  band(level_b): 그 시간 버킷이 끝날 때의 레벨(없으면 최고 레벨)
days_touched: 1시간 창 = 2, 24시간 창 = 2, 7일 창 = 8   (롤링 창이 06:00 경계 하루 반에 걸칠 수 있음)
raid lumps:  중간 레이드 = 개방 요일 수 안에서 min(days_touched, 1시간·24시간 2, 7일 3), 최종 레이드 = 1시간·24시간 2, 7일 2
```

버킷마다 `rate x active_seconds_b`를 쓰므로 창 중간에 레벨이 올라도 정확하다. **프레즌스가 없는 시간의 수입은 활동 시간 0**이라 덩어리 상한만 허용된다(HTTP 봇을 그대로 잡는다).

**임계 적용 보조 규칙**: 절대 하한 `ECONOMY_HOLD_MIN_XP(10_000)`, `ECONOMY_HOLD_MIN_GOLD_EQ(50_000)`, `ECONOMY_HOLD_MIN_ORE(200)`, `ECONOMY_HOLD_MIN_ESSENCE(60)`, `ECONOMY_HOLD_MIN_UNIQUE(3)`을 **넘지 않는 값은 임계를 넘어도 무시**(새 계정의 작은 값·시간 0 구간의 오탐 방지). 서버 전체의 최초 버킷 시각부터 창 길이만큼 지나지 않았으면 그 창은 건너뛴다(배포 직후 7일 창).

**검증 계산(보정 근거)**:
- 서버가 허용하는 공급 상한(3종 x 24지점, 1.1배 여유, 25초 리스폰)은 25초에 81마리 = 분당 약 194마리(시간당 11,664). 대역 5의 사냥터에서 이 속도로 도는 봇은 XP 시간당 약 349,900으로 **3배 임계 165,600의 2.1배**라 1시간 창에서 잡힌다.
- 한계(솔직히): 대역 1의 단일 종 사냥터(공급 상한 시간당 약 6,200마리)는 XP로는 임계 안(43,000 대 48,600)이다. 이 영역은 값이 작아 경제 피해가 작고, 공급·화력 상한이 받친다.
- 정직한 상한 대비 여유: 설계 속도(분당 15마리)의 정상 플레이는 위 임계의 약 1/6이다. 오탐이 나면 먼저 `INCOME_MAX_KILLS_PER_MIN`을 올리지 말고 `log_only`의 `shadow` 기록으로 실제 분포를 본다(12.4).

### 12.3 집계(버킷)에 들어가는 것과 들어가지 않는 것

`EconCtx`의 쓰기 메서드가 요청 처리 중 메모리 누계를 만들고, `runEconomy`가 핸들러 성공 직후 **한 문장**으로 `income_hourly`에 upsert한다(`ON CONFLICT (character_id, hour_start) DO UPDATE SET xp = income_hourly.xp + EXCLUDED.xp, ...`). 시간 키는 `date_trunc('hour', ctx.now)`(UTC 기준, 게임 일 경계와 무관한 롤링 창용). 틱 정산 등 `EconCtx` 없는 경로(경매 마감)는 같은 함수로 직접 쓴다.

| 원장 | 사유(reason) | 버킷 열 |
|---|---|---|
| xp_ledger | `kill`, `dungeon_clear` | `xp` |
| xp_ledger | `quest_reward`, `test_boost` | **제외**(1회 청구 / 시험) |
| gold_ledger(+) | `drop_claim`, `dungeon_card` | `gold_acq` |
| gold_ledger(+) | `shop_sell` | **제외**(획득한 물건은 획득 시점에 이미 환산) |
| gold_ledger(+) | `quest_reward`, `mail_claim`, `admin_*` | **제외** |
| item_ledger(+) | `drop_claim`, `gather`, `chest`, `dungeon_card`, `raid_core`, `raid_key` | `item_value += sellPriceOf(item_key) x delta`(수량 환산, 판매가 0이면 0), 재료·장비별 카운터: `ore`, `essence`, `core`(승급 핵), `epic_plus`, `unique_plus`(장비 등급) |
| item_ledger(+) | `quest_reward`, `mail_claim`, `gacha`, `enhance_result`, `promote_result`, `gear_renewal`, `equip`, `unequip`, `storage_move`, `admin_*`, `test_boost`, `auction_*` | **제외** |
| auction_trades | 체결 시 판매자 | `auction_in += seller_payout`, `auction_in_w += seller_payout x (플래그 가중 최댓값 / 100, 없으면 1)` |
| auction_trades | 체결 시 구매자 | `auction_out += price` |

`gold_eq = gold_acq + item_value`. `income_hourly.level_max`는 그 버킷에서 본 최고 레벨이고(`ctx.level`), 평가 때 `band(level_b)`에 쓴다. 채집(`gather`)과 상자(`chest`)는 표 1에 따로 넣지 않았다(노드 수·재생 시간이 작아 처치 대비 무시할 수준이고 3배 여유에 흡수된다는 가정. `theory_income.py`가 `gameconfig.json nodeKinds`로 확인하고 크면 표에 더한다).

### 12.4 판정과 상태

평가 함수 `velocity.evaluate(characterId, now)`: 창 3개(`1h`, `24h`, `7d`)와 경매 창에 대해 12.2절 식으로 `value`, `cap`, `active_seconds`를 계산한다.

- **수입 위반**(`kind='velocity'`): 어떤 지표(`xp`, `gold_eq`, `ore`, `essence`, `epic_plus`, `unique_plus`)든 어떤 창에서 `value > 3 x cap`이고 절대 하한을 넘음.
- **경매 위반**(`kind='auction'`): 계정의 모든 캐릭터에 대해 24시간 `auction_in_w - auction_out > AUCTION_NET_RATIO(0.5) x cap_24h(gold_eq) + AUCTION_NET_FLOOR(100_000)`. 즉 다른 계정에서 들어온 순유입을 상대 위험도로 가중(동일 기기·Steam 3배, 새 계정 2배, 같은 IP·반복 1.5배)해 본다. 정직한 판매 수입은 이미 `gold_eq`(획득 시점 환산)에 들어 있어 이중 계산이 아니라 "자기 최대 수입의 절반을 넘는 순유입은 비정상"이라는 보조 규칙이다.
- **창 기준선**: 해제된 정지가 있으면 그 `released_at` 이전의 버킷은 이후 평가에서 제외한다(운영자가 그 수입을 승인한 것).

**상태**: `economy_holds.state` = `shadow`(`log_only` 모드, 막지 않음, 기록과 근거만), `active`(`enforce`, 경로 차단), `released`(운영자 해제), `clawed_back`(회수 완료, 이후 해제도 별도로 가능). 같은 `(account, scope)`에 `active`는 하나(부분 유일 인덱스). `shadow`는 같은 `(account, scope)`에 `ECONOMY_HOLD_SHADOW_DEDUPE_HOURS(6)`시간당 한 줄만 쓴다(코드 쪽 확인).

**모드** `ECONOMY_HOLD_MODE`: `off`(평가·정지 없음, 버킷은 계속 쌓음), `log_only`(기본, `shadow` 기록과 `anti_abuse.hold_shadow` 로그), `enforce`(`active` 생성). `log_only`에서 `enforce`로 올릴 때 이미 쌓인 `shadow`는 이력으로 남고 `active`로 바뀌지 않는다.

**범위**: 수입 위반은 그 **캐릭터**(`character_id` 채움)에 건다. 경매 위반과 연결 전파 대상은 **계정 전체**(`character_id IS NULL`). 차단 판정은 `account_id = $1 AND state = 'active' AND (character_id IS NULL OR character_id = $2)`.

### 12.5 정지 중 막히는 것 (`ECONOMY_HOLD`)

차단 지점은 한 헬퍼 `holds.assertNoHold(client, accountId, characterId)`(인덱스 조회 한 번: `economy_holds_account_active`). 호출 위치는 각 서비스 핸들러 맨 앞(캐릭터 행을 잠근 뒤):

| 경로 | 파일:함수 |
|---|---|
| 경매 등록 | `auction/auctionService.ts` `processList` |
| 경매 즉시 구매·입찰 | `auctionService.ts` `lockAndCheck`(listing 락 직후) |
| 상점 판매 | `shop/shopService.ts` `sell`의 핸들러 |
| 강화·승급 | `enhance/enhanceService.ts` `enhance` 핸들러(승급이 같은 함수면 함께) |
| 별조각 사용 | `starshop/starshopService.ts` `pull`, `exchange`, `claim`와 합성(`synth`) 핸들러 |
| 우편 수령 | `mail/mailService.ts` `claimMail`, `claimAll` |

막지 **않는** 것: 처치 보고, 드롭 줍기, 채집, 이동, 상점 구매(소모품), 인벤토리·창고 정리, 장착·해제, 던전·파티 진행, 채팅. 경매 취소·낙찰 반환은 막지 않는다(재료가 갇히지 않게). 우편은 수령만 막히고 쌓이므로 의심 거래의 대금이 정지 중 인출되지 않는다.

### 12.6 정지 중 새 재화: 보류 원장 대신 동결 + 회수 (선택과 근거)

**선택: 얻는 것은 막지 않는다. 쓰는 것·옮기는 것(12.5)을 막고, 부정이 확인되면 H4로 회수한다.**

| 안 | 장점 | 단점 | 판정 |
|---|---|---|---|
| A. 보류 원장(PLAN 초안): 정지 중 새 골드·아이템을 `held_gains`에 쌓았다가 해제 때 지급/회수 | 정직한 사람은 해제 뒤 손해 없음 | `EconCtx.changeGold/addItem/grantXp` 전부와 드롭·퀘스트·던전 경로를 가로채 두 번째 원장을 만들어야 한다. 클라이언트 `delta`(잔액·가방)와 서버 잔액이 어긋나 표시가 깨지고, 지급이 실제로 일어나지 않았는데 사냥은 계속되어 경험치·레벨·퀘스트 판정과 모순된다. 해제 때 재지급은 새 중복 지급 위험. 원장 사유·규칙이 크게 늘어난다 | 기각 |
| **B. 동결 + 회수 (채택)** | 기존 지급 경로 불변, 원장·`delta` 규칙 불변. 정지 대상(경매·상점 판매·강화·별조각·우편 수령)이 곧 "재화를 가치로 바꾸거나 옮기는 통로"라 정지 의도(작업장·현금 거래 차단)를 정확히 막는다. 확인되면 원장 한 줄(`admin_clawback`)로 회수하고, 오탐이면 해제만 하면 끝 | 정지 중 얻은 재화가 가방에 보인다(쓸 수 없을 뿐). 경험치·레벨 회수는 못 한다(미룸) | 채택 |
| C. 정지 중 처치 보상 지급 자체를 막음 | 단순 | 사냥이 막혀 "게임 정지"처럼 느껴지고 오탐 피해가 큼 | 기각 |

회수(H4)는 **금액을 요청으로 받지 않는다**. 서버가 해당 정지의 근거 창(`window_start`~`window_end`, 없으면 최근 24시간)에서 계산한다:
- 골드: `min(현재 잔액, 그 창의 gold_acq + 경매 순유입)`을 `characters.gold`에서 차감하고 `gold_ledger(reason='admin_clawback', ref=hold uuid)` 한 줄. 모자라면 `shortfall`을 응답에 적는다(잔액 `CHECK >= 0` 유지).
- 아이템: 그 창에서 `item_ledger`로 획득한 (`item_key`별) 순증가분과 현재 가방·창고 수량 중 작은 수량을 빼고 `item_ledger(reason='admin_clawback')`. 착용 중인 것은 회수하지 않는다(기록만).
- 우편: 정지 근거 거래의 미수령 우편은 `expires_at = now()`로 만료시키면 기존 우편 만료 정리(`auction_sinks mail_expire`)가 골드를 소각 처리한다(`void_mail: true`).
- 경험치: 미룸.

### 12.7 연결 계정으로 전파

정지가 생성되면(`shadow` 포함) 같은 트랜잭션 밖에서 `holds.propagate(holdId)`:

1. **기기**: 정지 계정이 `HOLD_LINK_DEVICE_DAYS(14)`일 안에 쓴 `device_hash`마다 같은 기기를 쓴 다른 계정을 찾되, **그 기기를 쓴 서로 다른 계정이 `HOLD_LINK_DEVICE_MAX_ACCOUNTS(6)`개를 넘으면 공용 PC(PC방)로 보고 연결하지 않는다**.
2. **Steam**: `steam_key`가 같은 계정(패밀리 공유 소유자 포함)은 항상 연결.
3. **경매 상대**: 최근 7일 이 계정과 체결한 상대 중 `auction_trade_flags`에 `SAME_DEVICE|SAME_STEAM|NEW_BUYER|CEILING_PRICE`가 걸린 거래의 상대는 연결.
4. 합쳐 최대 `HOLD_LINK_MAX_ACCOUNTS(10)`개까지 **계정 단위**(`character_id NULL`) `kind='linked'`, `origin_hold_id`=원본 정지로 생성한다(오탐 확산 방지 상한). 이미 `active`가 있는 계정은 건너뜀.

해제(H3) 시 `release_linked: true`이면 `origin_hold_id`가 그 정지인 연결 정지를 함께 해제한다.

### 12.8 실행 시점 (싸게 + 주기)

| 시점 | 무엇 | 비용·이유 |
|---|---|---|
| 모든 경제 쓰기 | 버킷 upsert 한 문장 + 캐릭터를 더티 집합에 추가 | 캐릭터 행이 이미 잠겨 있어 경합 없음. 평가는 여기서 하지 않는다 |
| 더티 워커 (`ECONOMY_HOLD_CHECK_SECONDS` 30) | 더티 캐릭터마다 `velocity.evaluate` | 캐릭터당 버킷 최대 168행(7일) 합계. 변경된 캐릭터만 |
| 경매 체결 직후 | 판매자 계정의 경매 창만 즉시 평가(동기, 같은 트랜잭션 밖 직후) | 큰 이전은 30초를 기다리지 않는다 |
| 전체 훑기 (`ECONOMY_HOLD_SWEEP_MINUTES` 10, 7단계 `job_runs` 작업 `economy_hold_sweep`) | 최근 7일 활동 캐릭터 전체의 7일 창과 전파 재확인 | 더티 집합이 프로세스 재시작으로 사라져도 따라잡음 |
| 일 1회 (`income_reconcile`) | `income_hourly` 합계와 원장 합계를 최근 2일치 대조 | 파생 표이므로 어긋나면 로그 + 재계산(정본은 원장). 정합성 점검에 `I-income`으로 포함 |
| 청소 (기존 `purge`) | `income_hourly`·`play_time_hourly` 35일 | `PLAY_TIME_RETENTION_DAYS` |

**다중 인스턴스**: 더티 집합이 프로세스 메모리라 인스턴스가 둘 이상이 되면 쓰기를 한 인스턴스가 못 본다. 그때는 `pg_notify('dotrpg_income_dirty', character_id)` 또는 "`income_hourly.updated_at`이 최근 2분인 캐릭터"를 워커가 직접 조회한다(후자는 이 설계에서 이미 쓸 수 있는 경로이며 전체 훑기와 같은 쿼리).

### 12.9 관리자 API

7단계 관리자 서버(`admin/`)에 추가한다. 응답 형식·인증(TOTP 세션)·`audit()`·`adminRate()`는 기존과 같다. `AuditTarget`에 `'hold'`를 더한다. 목록은 커서 기반 `meta:{ next_cursor }`.

**H1 `GET /admin/economy/holds`** (viewer): 쿼리 `state?`(`shadow|active|released|clawed_back`), `kind?`, `q?`(캐릭터 이름 또는 계정 uuid), `cursor?`, `limit?(<=100)`. 항목: `{ id(uuid), state, kind, account_id(uuid), character: { id, name, level } | null, window_kind, evidence_summary: { metric, value, cap }, created_at, linked_count }`. 정렬 `created_at DESC`. 인덱스 `economy_holds_state_time`.

**H2 `GET /admin/economy/holds/{uuid}`** (viewer): `{ hold, evidence(전체), windows: { '1h','24h','7d': { xp, gold_eq, ore, essence, epic_plus, unique_plus, auction_net, active_seconds, caps:{...}, over:[metric] } }, linked:[{ account_id, relation:'device'|'steam'|'auction', hold_id? }], flagged_trades:[...], ledger_by_reason:{ gold:[...], item:[...], xp:[...] } }`. 근거 확인용이므로 수치를 전부 보인다(관리자만).

**H3 `POST /admin/economy/holds/{uuid}/release`** (operator): `{ request_id, note: string(1..500), release_linked?: boolean }`. 효과: `state='released'`, `released_at`, `reviewed_by/at`, `note`; 유효 기준선 갱신(12.4). `release_linked`면 연결 정지도. 응답 `{ released: n }`. 이미 해제면 멱등(`released: 0`). `shadow`·`active`·`clawed_back` 모두 해제할 수 있다(`clawed_back`는 회수 뒤 풀어 주는 용도). 에러 `404 HOLD_NOT_FOUND`.

**H4 `POST /admin/economy/holds/{uuid}/clawback`** (owner): `{ request_id, note, gold: boolean, items: boolean, void_mail: boolean }`. 동작은 12.6. 한 트랜잭션: 캐릭터 행 잠금(`lockCharacterByUuid`, 정지가 계정 단위면 근거 거래의 수익자 캐릭터들) -> 원장 + 잔액/재고 -> `state='clawed_back'`, `clawback` JSON(`{ gold_clawed, shortfall, items:[{item_key,count}], mails_voided }`). 응답 `data`: 같은 요약. `request_id`로 멱등(재전송은 처음 요약). 에러 `404`, `409 HOLD_ALREADY_CLAWED`, `422 NOTHING_TO_CLAW`. 감사 `audit action='economy.hold.clawback'`.

**H5 `POST /admin/economy/holds`** (operator): `{ request_id, character_id?: uuid, account_id?: uuid, note }`(둘 중 정확히 하나). 수동 정지(`kind='manual'`, `state='active'`, 모드와 무관).

**H6 `GET /admin/accounts/{uuid}/links`** (viewer): `{ devices:[{ device_label(해시 앞 8자), first_seen_at, last_seen_at, accounts_on_device }], ips:[{ ip_group, last_seen_at, accounts_on_ip }], steam:{ key, accounts }, linked_accounts:[{ account_id, via:['device','ip','steam'], last_seen_at }] }`. 원본 IP 대신 /24(IPv6 /48) 마스킹을 기본으로 하고 `?full_ip=true`는 owner만(감사 기록).

**H8 `GET /admin/economy/income-caps`** (viewer): 현재 모드·배수·`income_caps.json` 버전과 대역 표. **H9 `GET /admin/characters/{uuid}/velocity`** (viewer): 12.2 식으로 현재 창별 `value/cap/active_seconds`와 최근 정지.

플레이어용 **P3 `GET /characters/{uuid}/economy-hold`**: `{ on_hold: boolean, since: ISO | null, scope: 'character'|'account'|null }`(수치 없음). 클라이언트는 `ECONOMY_HOLD` 오류를 받았을 때 또는 인벤토리 열 때 호출해 "경제 활동이 제한되었습니다" 안내를 보일 수 있다. 에러 `404`, `401`.

### 12.10 훅 위치 요약

| 파일 | 변경 |
|---|---|
| `economy/economyContext.ts` | `changeGold`, `grantXp`, `addItem`에서 누계를 모음(`incomeMeter.note*`). `bindFor`는 8.6 |
| `economy/economyService.ts` `runEconomy` | 핸들러 성공 뒤 같은 트랜잭션에서 `incomeMeter.flush(client, ctx)`, 커밋 뒤 더티 표시 |
| `auction/auctionSettle.ts` `closeAsSold` | 거래 플래그(7.4) + `auction_*` 버킷 증분 + 경매 평가 호출 |
| 7개 경로 핸들러(12.5) | `assertNoHold` |
| `ops/jobs/index.ts` | `economy_hold_sweep`, `income_reconcile` 등록 |
| `ops/jobs/purge.ts` | 보관 정리 |
| `ops/snapshot.ts`·`ops/alertRules.ts` | 지표: `holds_active`, `holds_shadow_24h`, `income_flush_ms`, 경보: 24시간 신규 `active` 정지 `>= 5`(정상 규모를 넘으면 상한 오설정 의심) |
| `admin/economyholds/*` 신규 | H1~H9 |
| `config/env.ts` | 새 키 검증(12.11) |

### 12.11 환경변수

| 이름 | 기본 | 뜻 |
|---|---|---|
| `ECONOMY_HOLD_MODE` | `log_only` | `off|log_only|enforce` |
| `INCOME_MAX_KILLS_PER_MIN` | `30` | 이론 최대 처치 속도(스크립트와 서버가 같은 값을 `income_caps.json killsPerMinute`로 공유, 불일치 시 기동 경고) |
| `ECONOMY_HOLD_MULT` | `3` | 임계 = 상한 x 이 값 |
| `ECONOMY_HOLD_MIN_XP` | `10000` | 이보다 작은 경험치는 무시 |
| `ECONOMY_HOLD_MIN_GOLD_EQ` | `50000` | 골드 환산 하한 |
| `ECONOMY_HOLD_MIN_ORE` / `_ESSENCE` / `_UNIQUE` | `200` / `60` / `3` | 아이템 하한 |
| `ECONOMY_HOLD_CHECK_SECONDS` | `30` | 더티 워커 주기 |
| `ECONOMY_HOLD_SWEEP_MINUTES` | `10` | 전체 훑기 주기 |
| `ECONOMY_HOLD_SHADOW_DEDUPE_HOURS` | `6` | 같은 대상 `shadow` 줄 간격 |
| `AUCTION_NET_RATIO` | `0.5` | 경매 순유입 허용 비율(24시간 골드환산 상한 대비) |
| `AUCTION_NET_FLOOR` | `100000` | 경매 순유입 절대 하한 |
| `HOLD_LINK_DEVICE_DAYS` | `14` | 기기 연결 관찰 기간 |
| `HOLD_LINK_DEVICE_MAX_ACCOUNTS` | `6` | 이보다 많은 계정이 쓴 기기는 공용 PC로 보고 연결 안 함 |
| `HOLD_LINK_MAX_ACCOUNTS` | `10` | 한 정지의 전파 상한 |

### 12.12 테스트 (`test/economyHold.test.ts`, 순수 함수는 `velocity.test.ts`)

1. 버킷: 처치·드롭 줍기·던전 카드 한 요청이 `income_hourly`를 정확히 증분(`xp`, `gold_acq`, `item_value`, 광석 등). 제외 사유(`quest_reward`, `shop_sell`, `mail_claim`, `gacha`, `admin_grant`)는 증분 없음. 경매 체결이 판매자 `auction_in`/`auction_in_w`, 구매자 `auction_out`.
2. 상한 식: 대역·활동 시간·창별로 12.2절 예(대역 5, 활동 4시간, 24시간: XP 상한 `3 x (55,200 x 4 + 2 x 43,477 + 2 x 10,092) = 983,814`)를 단위 테스트로 고정. 창 중간 레벨업에서 버킷별 대역이 쓰인다. 던전 일 단위 덩어리가 06:00 경계 두 날(`days_touched=2`)을 허용한다.
3. 프레즌스 없는 캐릭터가 하한을 넘는 XP를 얻으면 위반. 활동 시간이 충분한 정상 플레이(설계 속도)는 위반 없음. 절대 하한 아래는 무시. 서버 최초 버킷이 창 길이보다 새로우면 그 창 건너뜀.
4. `log_only`: `shadow` 행만 생기고 모든 경로가 막히지 않는다. `enforce`: `active`가 생기고 7개 경로가 `403 ECONOMY_HOLD`(응답에 수치 없음), 막히지 않는 경로(처치, 줍기, 채집, 장착, 취소)는 정상.
5. 경매 위반: 새 계정 + 같은 기기 상대가 상한가로 대량 구매하면 판매자 계정 정지(24시간 가중 순유입). 같은 금액이라도 낯선 정상 계정과의 거래는 가중이 낮아 하한·비율 안이면 정지 없음.
6. 전파: 같은 기기 계정 2개 -> `linked` 정지 생성(`origin_hold_id`). 6개 초과 계정이 쓴 공용 기기는 전파 없음. Steam 소유자 같으면 항상 전파. 상한 `HOLD_LINK_MAX_ACCOUNTS`.
7. H3 해제: `released`, 기준선 이후 재평가에서 이전 버킷 제외, `release_linked`. 멱등. 권한(viewer는 403).
8. H4 회수: 골드는 잔액까지만, `shortfall` 보고, `gold_ledger admin_clawback`과 잔액 일치, 아이템 원장, 착용 아이템은 회수 안 함, `void_mail`이 `expires_at`을 당긴다. `request_id` 재전송 멱등. 같은 정지 두 번째 회수 `409`. 금액을 본문으로 보내면 `400`(`.strict()`).
9. 동시성: 같은 계정에 평가와 해제가 겹쳐도 `active` 유일 인덱스가 중복을 막는다.
10. 정합성: `income_reconcile`가 의도적으로 틀어 둔 버킷을 찾아 고친다.
11. 모드 `off`는 정지를 만들지 않지만 버킷은 쌓인다.
12. P3: 정지 중 `on_hold:true` + `scope`, 수치 없음.

## 13. 마이그레이션 0020_anti_abuse.sql 초안

정본은 구현 때 `server/migrations/0020_anti_abuse.sql`로 만든다(`-- ============ UP ============` / `-- ============ DOWN ============` 마커, 0001~0019와 같은 형식). 아래는 UP 본문 초안이고 DOWN은 개발 DB 전용으로 표·열을 역순으로 지우고 CHECK 목록을 이전 값으로 되돌린다(원장 사유 되돌리기 전에 `admin_clawback` 행 삭제는 원장 트리거를 `DISABLE TRIGGER`/`ENABLE`로 감싼다).

```sql
-- 0020_anti_abuse: 부정 행위 방지 1단계 (Docs/server/phase9_anti_abuse.md)
-- 대상: PostgreSQL 14 이상. 선행: 0019_gear_renewal.
-- ============ UP ============

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

ALTER TABLE refresh_tokens
  ADD COLUMN install_id    UUID,
  ADD COLUMN device_hash   TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN revoke_reason TEXT CHECK (revoke_reason IN ('logout', 'reuse', 'replaced', 'device_mismatch', 'admin', 'legacy')),
  ADD CONSTRAINT refresh_tokens_revoke_chk CHECK (revoke_reason IS NULL OR revoked_at IS NOT NULL);
UPDATE refresh_tokens SET revoke_reason = 'legacy' WHERE revoked_at IS NOT NULL;
COMMENT ON COLUMN refresh_tokens.device_hash IS '이 가족을 만든 로그인의 기기. 리프레시 기기 불일치 감지용';
COMMENT ON COLUMN refresh_tokens.revoke_reason IS 'replaced는 같은 계정 재로그인으로 정상 교체(REFRESH_REUSED 경보를 내지 않는다)';

CREATE TABLE login_events (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  character_id  BIGINT REFERENCES characters(id),
  kind          TEXT NOT NULL CHECK (kind IN ('register', 'login', 'steam_login', 'refresh', 'enter')),
  install_id    UUID,
  device_hash   TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ip            INET,
  steam_id      TEXT CHECK (steam_id ~ '^[0-9]{17}$'),
  steam_owner_id TEXT CHECK (steam_owner_id ~ '^[0-9]{17}$'),
  client_version TEXT,
  flags         TEXT[] NOT NULL DEFAULT '{}',
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  login_events IS '로그인·세션 진입 기록(추가 위주, 90일 보관). 다계정 추적과 사후 조사의 원본. 외부 노출 id 없음(관리자 응답은 필드만)';
COMMENT ON COLUMN login_events.character_id IS 'kind=enter(프레즌스 첫 신호)일 때만';
COMMENT ON COLUMN login_events.flags IS 'device_missing / device_mismatch / replaced_other';
COMMENT ON COLUMN login_events.ip IS '개인정보 성격. 보관 기간 후 삭제';
-- 계정별 이력(관리자 조회), 보관 정리
CREATE INDEX login_events_account_time ON login_events (account_id, created_at DESC);
CREATE INDEX login_events_created ON login_events (created_at);
-- 기기·IP로 계정 묶기(관리자 H6, 드문 조회라 부분 인덱스로 쓰기 비용을 줄인다)
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
-- 이 기기를 쓴 다른 계정(전파·경매 플래그)
CREATE INDEX account_devices_device ON account_devices (device_hash, last_seen_at DESC);

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

-- ---------- 2. 프레즌스, 플레이 시간 ----------

CREATE TABLE online_sessions (
  account_id      BIGINT PRIMARY KEY REFERENCES accounts(id),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  family_id       UUID NOT NULL,
  install_id      UUID,
  device_hash     TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ip              INET,
  map_id          TEXT NOT NULL,
  prev_map_id     TEXT,
  map_since       TIMESTAMPTZ NOT NULL,
  map_changed_at  TIMESTAMPTZ NOT NULL,
  auto_play       BOOLEAN NOT NULL DEFAULT false,
  input_recent    BOOLEAN NOT NULL DEFAULT true,
  unattended_since TIMESTAMPTZ,
  started_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at    TIMESTAMPTZ NOT NULL,
  ended_at        TIMESTAMPTZ,
  end_reason      TEXT CHECK (end_reason IN ('leave', 'replaced', 'timeout', 'banned')),
  CONSTRAINT online_sessions_end_chk CHECK ((ended_at IS NULL) = (end_reason IS NULL))
);
COMMENT ON TABLE  online_sessions IS '계정당 한 행: 지금(또는 마지막) 온라인 캐릭터. 온라인 = ended_at IS NULL AND last_seen_at > now() - PRESENCE_ONLINE_SECONDS(90). 기기 동시 접속·처치 맵 확인·보스 체류의 근거';
COMMENT ON COLUMN online_sessions.map_since IS '현재 맵에 끊김 없이 있기 시작한 시각(맵 이동 또는 120초 넘는 신호 공백 때 갱신). 필드 보스 체류';
COMMENT ON COLUMN online_sessions.prev_map_id IS '직전 맵. 맵 이동 직후 PRESENCE_MAP_GRACE_SECONDS 동안 옛 맵 처치 보고를 받아 준다';
-- 같은 기기의 온라인 계정 수(진입 때 한 번)
CREATE INDEX online_sessions_device ON online_sessions (device_hash, last_seen_at) WHERE ended_at IS NULL AND device_hash IS NOT NULL;
-- 같은 IP 동시 온라인(감시 점수)
CREATE INDEX online_sessions_ip ON online_sessions (ip, last_seen_at) WHERE ended_at IS NULL AND ip IS NOT NULL;
-- 처치 보고가 캐릭터로 행을 찾는다
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
-- 최근 변경 캐릭터 조회(다중 인스턴스 보조 경로, 전체 훑기)
CREATE INDEX income_hourly_updated ON income_hourly (updated_at);
-- 보관 정리
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
-- 같은 (계정, 범위)에 active는 하나
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char) WHERE state = 'active';
-- 차단 헬퍼의 조회(요청마다, 부분 인덱스라 아주 작다)
CREATE INDEX economy_holds_account_active ON economy_holds (account_id) WHERE state = 'active';
-- 관리자 목록(상태별 최신순)
CREATE INDEX economy_holds_state_time ON economy_holds (state, created_at DESC);
-- shadow 줄 간격 확인
CREATE INDEX economy_holds_shadow ON economy_holds (account_id, scope_char, created_at DESC) WHERE state = 'shadow';
-- 전파·해제 일괄
CREATE INDEX economy_holds_origin ON economy_holds (origin_hold_id) WHERE origin_hold_id IS NOT NULL;

-- ---------- 4. 파티 사람 수, 기여 ----------

ALTER TABLE party_run_members
  ADD COLUMN device_hash TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN steam_key   TEXT CHECK (steam_key ~ '^[0-9]{17}$');
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
-- 캐릭터당 열린 시도는 하나
CREATE UNIQUE INDEX career_trials_one_open ON character_career_trials (character_id) WHERE ended_at IS NULL;
CREATE INDEX career_trials_char ON character_career_trials (character_id, started_at DESC);

INSERT INTO character_career (character_id, career, stage, promoted_at, stage_changed_at, source)
SELECT cs.character_id,
       (cs.career->>'career')::int,
       LEAST(5, GREATEST(0, COALESCE((cs.career->>'questStage')::int, 0))),
       now(), now(), 'legacy_backfill'
  FROM character_state cs
 WHERE cs.career IS NOT NULL AND COALESCE((cs.career->>'career')::int, 0) BETWEEN 1 AND 4;

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

-- ---------- 7. 원장 사유, 이상 기록 종류 ----------

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

-- ============ DOWN ============
-- 개발 DB 전용. 새 표·열을 지우고 CHECK를 0019 상태로 되돌린다(원장 행 삭제는 추가 전용 트리거를 잠시 끈다).
-- (구현 때 UP의 역순으로 작성: DROP TABLE auction_trade_flags, character_career_trials, character_career,
--  economy_holds, income_hourly, play_time_hourly, online_sessions, account_ips, account_devices, login_events;
--  ALTER TABLE ... DROP COLUMN (accounts 4개, auth_identities.steam_owner_id, refresh_tokens 3개+제약,
--  party_run_members 2개, dungeon_runs.contribution); dungeon_runs_lock_reason_check, gold/item_ledger 사유,
--  anomaly_log kind CHECK는 위 UP의 "이전 목록"으로 되돌린다.)
```

## 14. 환경변수 총람과 기동 검사

위 각 절의 표가 정본이고, 여기서는 기동 검사와 한 번에 보는 순서를 적는다.

| 그룹 | 키 |
|---|---|
| 기기·세션 | `DEVICE_INFO_REQUIRED`, `DEVICE_HASH_PEPPER`, `SESSION_SINGLE_MODE`, `REFRESH_DEVICE_BIND`, `LOGIN_EVENT_RETENTION_DAYS`, `ACCOUNT_DEVICE_RETENTION_DAYS` |
| 프레즌스 | `DEVICE_MAX_CONCURRENT`, `DEVICE_LIMIT_MODE`, `IP_WATCH_CONCURRENT`, `PRESENCE_*`(INTERVAL, ONLINE, MIN_GAP, MAP_GRACE, STALE_REJECT, KILL_MODE), `PLAY_TIME_MAX_GAP_SECONDS`, `FIELD_BOSS_MIN_PRESENCE_SECONDS`, `AUTOPLAY_UNATTENDED_MAX_SECONDS`, `PLAY_TIME_RETENTION_DAYS`, `RATE_PRESENCE_*` |
| 기여 | `CONTRIBUTION_MODE`, `RAID_MIN_SHARE`, `RAID_MIN_HITS`, `DUNGEON_MIN_SHARE`, `DUNGEON_MIN_HITS`, `CONTRIBUTION_DISPUTE_RATIO`, `DUNGEON_UNDERLEVEL_*` |
| 필드 캐리 | `FIELD_CARRY_STEP`(0.15), `FIELD_CARRY_MIN`(0.02), `FIELD_CARRY_HARD_GAP`, `FIELD_CARRY_HARD_DROP_MUL`, `FIELD_CARRY_GOLD_SCALE` |
| 경매 | `AUCTION_BUYER_MIN_*`, `AUCTION_SHOP_CEIL_MULT`, `AUCTION_FLAG_*`, `AUCTION_BLOCK_SAME_DEVICE`, `AUCTION_NET_*` |
| 전직 | `CAREER_SERVER_TRUTH`, `CAREER_PROMOTE_MIN_LEVEL`, `AWAKEN_*`, `RATE_CAREER_PER_SEC` |
| 이름 | `NAME_RESERVED_MODE` |
| 경제 정지 | `ECONOMY_HOLD_*`, `INCOME_MAX_KILLS_PER_MIN`, `HOLD_LINK_*` |

**기동 검사 추가** (`config/env.ts`, 기존 G 번호 뒤에 이어서):
- 운영(`NODE_ENV=production`)에서 `DEPLOY_STAGE` 미명시 -> 실패(11.1).
- 모드 값이 허용 집합 밖 -> 실패. `DEVICE_HASH_PEPPER`가 필요한 기능이 하나라도 켜져 있는데 없거나 32자 미만이거나 `JWT_SECRET`과 같으면 실패(`phase8Env.ts` `isWeak` 재사용).
- `ECONOMY_HOLD_MODE=enforce`인데 `server/data/income_caps.json`이 없거나 `schema`가 다르면 실패(`log_only`는 경고).
- 운영에서 `AUCTION_BUYER_MIN_LEVEL=0` 또는 `AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS=0` 실패(기존 `AUCTION_MIN_*` 검사와 같다).
- 필드 캐리: `FIELD_CARRY_MIN > 1`, `FIELD_CARRY_HARD_GAP <= FIELD_CARRY_SLACK`이면 실패.
- `data_version.json`(7단계 데이터 버전 검사)에 `careers.json trials`와 `income_caps.json`을 포함시킨다.

## 15. 보관, 개인정보, 운영

| 표 | 보관 | 이유 |
|---|---|---|
| `login_events` | 90일 | 다계정 조사와 분쟁. IP·기기 해시는 개인정보라 짧게 |
| `account_devices`, `account_ips` | 마지막 관측 후 180일 | 연결 판정 원본(집계라 작음) |
| `online_sessions` | 계정당 1행, 끝난 행 7일 후 삭제 | 현재 상태 표 |
| `play_time_hourly`, `income_hourly` | 35일 | 7일 창 + 여유, 이상 조사는 원장이 정본 |
| `economy_holds`, `auction_trade_flags`, `character_career*` | 삭제 안 함 | 제재·감사 기록(`account_sanctions`와 같은 성격) |

- 개인정보: 기기 지문 해시·IP·Steam ID를 수집·보관하므로 개인정보 처리방침 갱신과 보관 기간 고지가 필요하다(결정 대기 6). 서버는 기기 해시를 HMAC(비밀)해 저장하고 관리자 화면에는 해시 앞 8자와 IP /24 마스킹만 보인다. 원본 IP 열람은 owner 권한과 감사 기록.
- 7단계 `purge`·`integrity`·`snapshot`·`alertRules`·`/health/ready`에 위 줄을 더한다(각 절의 훅 표). 정합성 점검 신규: `I-income`(버킷 vs 원장), `I-names`(예약어 이름 수), `I-career`(서버 기록과 `character_state.career` 불일치 수), `I-holds`(`active` 정지가 24시간 넘게 미검토인 건).
- **롤아웃 순서**(클라이언트와 맞물림): ① 서버 배포(모드 `log`/`log_only`, `DEVICE_INFO_REQUIRED=false`) ② 클라이언트: `device` 필드, 프레즌스, 세션 교체 처리, 전직·각성 API, 기여 필드(`hits_landed` 등), 카드 대조, 이름·`worn` 표시 ③ 기록 확인 뒤 `DEVICE_INFO_REQUIRED=true`, `PRESENCE_KILL_MODE=enforce`, `DEVICE_LIMIT_MODE=enforce` ④ `ECONOMY_HOLD_MODE=log_only` 2주 분포 확인 후 `enforce`. 서버만 먼저 나가는 기능(기여 판정, 경매, 이름, 필드 캐리, 뽑기 귀속, 마을 표시, 시험 스크립트 가드)은 ① 단계에서 바로 켠다.

## 16. 구현 순서 (의존 관계만)

1. 0020 마이그레이션 + `schema.sql` 동기화 + 환경변수·기동 검사.
2. 3절 기기 기록·세션 교체(`sid`) -> 4절 프레즌스(P1, P2, 기기 한도) -> 4.5절 처치 보고 검사.
3. 12절 버킷 기록(`incomeMeter`)을 먼저 모든 경제 경로에 붙이고(버킷은 평가 없이도 쌓인다), 그 뒤 평가·정지·관리자(H1~H9). 상한 표 스크립트(`Tools/balance/theory_income.py`)와 `income_caps.json`이 12절 구현의 선행 조건.
4. 5절 기여·사람 수, 6절 필드 캐리, 7절 경매, 8절 전직·각성과 뽑기 귀속(독립, 병렬 가능).
5. 9절 뷰 필드, 10절 이름, 11절 스크립트 가드·마을 표시.
6. 각 단계 끝에 이 문서의 테스트 목록을 통과시킨다. 점검은 dotrpg-economy-auditor(원장 규칙, 정지 우회 경로, 회수의 원장 일치).

## 17. 사용자가 정해야 할 항목 (선택지와 추천)

1. **전직 서버 판정 방식**: (a) 전용 승급 API(C1)로 서버가 기록 - **추천**, (b) PLAN 문구대로 "전직 퀘스트 청구 기록". `career_path` 퀘스트의 완료 조건이 "전직했음"이라 (b)는 순환해 성립하지 않는다.
2. **기존 뽑기 장비 소급 귀속**: 새 뽑기부터만(**추천**, 결제 전이라 시험 데이터뿐) / 가방의 `gacha` 이력 장비도 계정 귀속으로 변환.
3. **같은 기기·Steam 쌍의 경매 구매 차단**: 기록만(기본, **추천**: 같은 PC 두 계정의 정상 이전도 있으나 경제 정지 가중이 걸림) / 차단(`AUCTION_BLOCK_SAME_DEVICE=true`).
4. **필드 캐리의 골드 감쇠**: 처치 골드도 감쇠(`FIELD_CARRY_GOLD_SCALE=true`, **추천**: 안 하면 캐리 중에도 처치당 골드가 100%) / 경험치·드롭만.
5. **경매 쌍별 일일 골드 한도 1억**: 그대로 / 1천만으로 하향(**추천**: 부계정 이전 통로의 규모를 직접 줄인다. 정상 쌍 거래가 하루 1천만을 넘는 일은 드물다).
6. **개인정보 고지와 보관 기간**: 기기 해시·IP·Steam ID를 90일(`login_events`)/180일(집계) 보관(**추천**). 처리방침에 반영하는 것은 사용자 몫.
7. **이론 최대 처치 속도 30/분과 3배**: 시작값(**추천**). `log_only` 2주 동안 `shadow` 분포를 보고 정한다(오탐이 많으면 `INCOME_MAX_KILLS_PER_MIN`을 올리지 말고 대역 표를 `theory_income.py`로 다시 계산).
8. **경험치 회수**: 1단계에서는 지원하지 않음(**추천**). 필요하면 레벨 되돌림 규칙을 따로 설계해야 한다.

## 19. 클라이언트 구현 메모

서버 구현(2026-10-06)이 실제로 받고 돌려주는 것만 적는다. 이 절이 클라이언트 변경의 기준이고, 본문 3~11절과 다르면 이 절이 맞다(다른 곳은 19.9의 "구현이 설계와 다른 점" 참조). 모든 새 규칙은 서버가 먼저 `log` 모드로 나가므로 클라이언트가 늦게 맞춰져도 지금은 아무것도 막히지 않는다(기본 `PRESENCE_KILL_MODE=log`, `DEVICE_LIMIT_MODE=log`, `ECONOMY_HOLD_MODE=log_only`). 예외로 서버만으로 바로 켜지는 것은 기여 판정(`CONTRIBUTION_MODE=enforce`), 전직 서버 진실(`CAREER_SERVER_TRUTH=enforce`), 이름 규칙, 필드 캐리 감쇠, 경매 구매자 자격, 뽑기 장비 귀속이다.

### 19.1 로그인 본문의 기기 필드

대상: `POST /auth/dev/register`, `POST /auth/dev/login`, `POST /auth/steam`, `POST /auth/refresh` 모두 같은 `device`를 더한다(`/auth/logout`은 받아도 무시).

```
device: {
  install_id: "<UUID v4>",          // 최초 실행 때 한 번 만들어 persistentDataPath 파일에 저장(삭제하면 새 id)
  device_hash: "<64자 소문자 hex>"  // SHA-256( "dotrpg|" + SystemInfo.deviceUniqueIdentifier )
}
```

- `SystemInfo.deviceUniqueIdentifier == SystemInfo.unsupportedIdentifier("n/a")`이면 `device_hash`를 **생략**하고 `device: { install_id }`만 보낸다(`n/a`를 해시하면 미지원 기기가 모두 한 기기로 묶인다).
- 허용되지 않는 필드가 있으면 `400 VALIDATION`. `DEVICE_INFO_REQUIRED=true`로 올라간 뒤에는 `device`가 없으면 `400 VALIDATION`(`errors.fields[0].path = "device"`).
- 응답 `data.session.replaced_other`(register·login·steam login): `true`면 다른 곳의 접속을 종료시킨 것이다(안내 문구 선택).
- 로그인 응답의 `access_token`에는 세션 id가 들어 있다. 클라이언트는 토큰 내용을 해석하지 않는다.

### 19.2 프레즌스(P1, P2)와 간격

`POST /characters/{uuid}/presence`(버전·데이터 버전 헤더 필수, `request_id` 없음, 본문은 `strict`)

```
{ "map_id": "<maps.json id>", "auto_play": <bool>, "input_recent": <bool> }
```

- 응답 `data`: `{ server_time, interval_seconds: 30, counted: bool, device: { online, max } | null }`. 주기는 응답의 `interval_seconds`를 따른다(기본 30초).
- 보내는 때: ① 캐릭터로 월드에 들어가기 **전에** 한 번(성공 전에는 처치·채집 보고를 보내지 않는다), ② 이후 **30초마다**, ③ **맵이 바뀌어 로드가 끝난 즉시**(그 맵의 첫 처치 보고보다 먼저). 일시정지·오프라인 일시정지 중에도 계속 보낸다.
- 5초보다 촘촘히 보내면 활동 시간에 더해지지 않는다. 속도 제한은 계정당 10초에 3회, 1분에 12회(`429 RATE_LIMITED`, `Retry-After`). 맵 이동 즉시 신호까지 포함해도 여유가 있다.
- `auto_play`는 자동 진행이 실제로 켜져 있을 때만 `true`, `input_recent`는 지난 30초 안에 키보드·마우스·패드 입력이 있었을 때만 `true`.
- 캐릭터 선택 화면 복귀·종료 때 `POST /characters/{uuid}/presence/leave`(본문 `{}`, 응답 `{ left: true }`). 실패해도 재시도하지 않는다(90초 뒤 서버가 정리).
- 필드 파티 세션 입장(`field-sessions/enter`)과 호스트 인계는 서버가 프레즌스 맵을 같이 맞춘다. 그래도 클라이언트는 맵 로드 직후 신호를 먼저 보낸다.
- 처치 보고(`POST /kills`)가 `409 PRESENCE_REQUIRED`면 프레즌스를 보낸 뒤 **같은 `request_id`로** 다시 보고한다(멱등).
- 내 경제 정지 여부: `GET /characters/{uuid}/economy-hold` -> `{ on_hold, since, scope: 'character'|'account'|null }`(수치 없음).

### 19.3 새 오류 코드와 서버 메시지

응답 형식은 기존과 같다(`{ success:false, message, errors:{ code, ... } }`). 아래 `message`는 서버가 내려주는 한국어 원문이고, 화면 문구는 클라이언트가 정해도 된다. `errors` 열은 코드 외에 추가로 오는 필드다.

| 코드 | 상태 | 어디서 | 서버 message | errors 추가 필드 / 클라이언트 처리 |
|---|---|---|---|---|
| `SESSION_REPLACED` | 401 | 모든 인증 요청, refresh | 다른 곳에서 로그인하여 이 접속이 종료되었습니다. | "다른 곳에서 로그인했습니다" 안내 후 로그인 화면. **자동 리프레시·재시도 금지** |
| `DEVICE_LIMIT` | 409 | 프레즌스 | 이 PC에서 동시에 접속할 수 있는 수를 넘었습니다. | `limit`, `online_on_device`. "이 PC에서는 동시에 2개까지 접속할 수 있습니다" 후 캐릭터 선택으로 |
| `PRESENCE_REQUIRED` | 409 | 처치 보고 | 위치 신호가 필요합니다. 잠시 후 다시 시도해 주세요. | 프레즌스 전송 후 같은 `request_id`로 재보고 |
| `KILL_REJECTED` | 422 | 처치 보고 | 처치를 인정할 수 없습니다. | 기존 코드. 맵·보스 체류 불일치도 이 코드 |
| `BUYER_GATE` | 422 | 경매 구매·입찰 | 아직 경매에서 구매할 수 없습니다. | `need_level`, `need_days`(등록 자격과 같다: Lv10, 계정 7일) |
| `SAME_DEVICE_TRADE` | 403 | 경매 구매·입찰 | 같은 기기의 계정과는 거래할 수 없습니다. | 서버가 `AUCTION_BLOCK_SAME_DEVICE=true`일 때만(기본은 기록만) |
| `NAME_FORBIDDEN` | 422 | 캐릭터 생성 | 사용할 수 없는 이름입니다. | `reason: 'RESERVED'|'BANNED'`. 어떤 단어인지는 알려 주지 않는다 |
| `ECONOMY_HOLD` | 403 | 경매 등록·구매·입찰, 상점 판매, 강화·승급, 별조각 사용(뽑기·교환·선택·합성), 우편 수령 | 경제 활동이 일시적으로 제한되었습니다. 문의해 주세요. | `scope: 'character'|'account'`, `since`(ISO). 수치는 오지 않는다. 재시도하지 않는다. 처치·줍기·채집·장착·경매 취소는 막히지 않는다 |
| `ALREADY_PROMOTED` | 409 | 전직 | 이미 전직했습니다. | |
| `NOT_PROMOTED` | 409 | 각성 | 아직 전직하지 않았습니다. | |
| `CAREER_BASE_MISMATCH` | 422 | 전직 | 이 직업으로는 전직할 수 없습니다. | 전사는 1(Fighter)·2(Guardian), 마법사는 3(Arcanist)·4(Bishop) |
| `LEVEL_TOO_LOW` | 422 | 전직 | 레벨이 부족합니다. | `need`, `have` |
| `STAGE_MISMATCH` | 409 | 각성 | 현재 단계와 다릅니다. / 지금은 시련을 시작할 수 없습니다. / 지금은 시련을 마칠 수 없습니다. | `stage`(서버의 현재 단계). 이 값으로 로컬 단계를 맞춘다 |
| `STAGE_TOO_FAST` | 429 | 각성 | 너무 빠릅니다. 잠시 후 다시 시도해 주세요. | `retry_after_sec`(`Retry-After` 헤더 포함). 대화 한 단계는 10초 이상 간격 |
| `TRIAL_REQUIRED` | 422 | 각성 | 시련은 시련 결과 보고로만 넘어갑니다. | `from_stage: 2`로 advance를 부른 경우 |
| `TRIAL_NOT_STARTED` | 409 | 시련 종료 | 시작한 시련이 없습니다. | |
| `TRIAL_EXPIRED` | 409 | 시련 종료 | 시련 시간이 지났습니다. 다시 시작해 주세요. | 시련 시작(C2)부터 다시 |
| `TRIAL_TOO_FAST` | 422 | 시련 종료 | 시련 결과를 인정할 수 없습니다. | 최소 시간 전의 성공 보고. 시도는 그대로 열려 있다 |
| `NODES_REQUIRED` | 422 | 시련 시작 | 시련에 필요한 노드가 없습니다. | `missing: [노드 id]` |
| `NOT_IN_TOWN` | 409 | 시련·각성 | 마을에서만 할 수 있습니다. | 프레즌스가 신선하고 마을 맵이어야 한다. 시련 성공은 시작부터 끝까지 같은 마을 |
| `IN_PARTY_CONTENT` | 409 | 시련 시작 | 파티 콘텐츠 중에는 시작할 수 없습니다. | 파티 판·필드 세션 중 |
| `INVALID_CAREER` | 422 | `PUT /characters/{uuid}/state` | 전직 상태가 올바르지 않습니다. | `reason`에 `NOT_GRANTED`(서버가 부여하지 않은 전직·각성), `STAGE_NOT_GRANTED`, `QUEST_REGRESSION`(서버 단계보다 뒤처진 값), `MISSING_STATE` 추가 |

`LOW_CONTRIBUTION`은 오류 코드가 아니라 던전 결과의 잠금 사유다: 레이드는 `data.raid.lock_reason`, 파티 요일 던전은 `data.reward_locked: true`와 `data.reward_lock_reason: 'LOW_CONTRIBUTION'`(재정산 응답도 같다). 권장 문구 "전투 기여가 부족해 보상이 없습니다". 이때 처치 경험치는 이미 지급되었고 클리어 경험치·카드·열쇠만 없다.

### 19.4 전직·각성 API(C1~C4)와 흐름

모두 `POST /characters/{uuid}/...`, `request_id`(UUID) 필수, 버전·데이터 버전 헤더 필수, 본문 `strict`. 같은 `request_id`는 처음 응답을 그대로 돌려준다(`Idempotent-Replay: true`). 캐릭터당 초당 1회.

| 경로 | 본문 | 성공 응답 `data` |
|---|---|---|
| `/career/promote` | `{ request_id, career: 1..4 }` | `{ career, stage: 0, promoted_at }` |
| `/career/awakening/trial/start` | `{ request_id }` | `{ started_at, expires_in_seconds: 120 }`(최소 시간은 알려 주지 않는다. 열린 시도가 있으면 그것을 돌려준다) |
| `/career/awakening/trial/finish` | `{ request_id, result: 'success'|'fail' }` | `{ stage: 3 }`(success) / `{ stage: 2 }`(fail, 재도전 가능) |
| `/career/awakening/advance` | `{ request_id, from_stage: 0|1|3|4 }` | `{ stage, awakened }`(4 -> 5면 `awakened: true`) |

- 전직 확정: C1 성공 응답을 받은 뒤에만 `Progression.Promote`를 적용하고 `PUT state`로 저장한다(서버가 `character_state.career`를 먼저 `{ career, nodes: [], training, refunded, questStage: 0, awakened: false }`로 쓴다). 저장 전에 `GET /characters/{uuid}`의 `state.career`와 맞추면 `training`·`refunded`가 어긋나지 않는다.
- 시련: `CareerTrials.Begin` -> C2, 종료 -> C3. 로컬 `AdvanceAwakening(2)` 대신 C3 성공 응답의 `stage: 3`을 적용한다. 서버 최소 시간은 `server/data/careers.json`의 `trials[career].minSeconds`(Unity 내보내기가 채워야 한다. 지금은 없어서 전 직업 6초와 필수 노드 검사 생략으로 동작한다. 가디언·비숍은 20초, 필수 노드는 가디언 `g_taunt,g_wall`, 아케니스트 `m_fire,m_ice,m_storm`, 비숍 `b_cleanse,b_wing`) 이상이어야 하고 시작 후 120초 안에 보고해야 한다.
- 대화 단계: C4 응답 후 로컬 `AdvanceAwakening`. 단계 사이는 10초 이상.
- C2~C4 직전에는 프레즌스를 보내 둔다(90초 안에 보낸 마을 맵 신호가 필요하다).
- 저장(`PUT state`)은 항상 이 응답들 이후. 서버 기록보다 앞선 값(`questStage`, `awakened`)이나 뒤처진 값은 `422 INVALID_CAREER`. 오프라인 모드는 변경 없음.
- 기존 캐릭터: 서버가 저장되어 있던 전직 값을 `legacy_backfill`로 이전했다. 그대로 저장해도 통과한다.

### 19.5 던전·레이드 결과 보고의 새 필드

- 방장 보고(`POST /party-runs/{id}/host-report`)의 `members[]`에 선택 필드: `hits_landed`(정수, 호스트가 본 그 멤버의 적중 횟수), `card_mismatch`(boolean, 멤버 카드와 서버 뷰가 달랐으면 `true`). `hits_landed`가 물리 상한을 넘으면 보고가 무효(`HITS_OVER_CAP`)가 된다. 레이드는 호스트 보고가 유효해야 기여를 판정하므로 **방장 보고를 반드시 보낸다**(없거나 무효면 레이드 정산이 보류된다).
- 멤버 결과 보고(`POST /dungeon-runs/{id}/result`)의 `stats`에 선택 필드: `damage_dealt`(실수), `hits_landed`(정수). 본인 주장이라 지급 근거로 쓰이지 않고, 호스트 관찰의 2배 이상으로 어긋나면 그 멤버 정산이 보류된다.
- 필드 세션 관찰(`POST /field-sessions/{id}/observe`)의 `credits[]` 항목에 선택 필드 `card_mismatch`(boolean).
- 호스트는 기여 지분 5% 또는 적중 40회 미만인 멤버가 보상을 못 받는다는 점을 안내할 수 있다(`LOW_CONTRIBUTION`).

### 19.6 멤버 카드 대조의 데이터 출처

호스트 PC가 멤버의 `Hello` 카드와 대조할 서버 값은 아래 응답의 `members[]`에서 온다(전부 서버 값):

- `GET /characters/{uuid}/field-sessions/{id}`, `POST .../field-sessions/enter`의 `session.members[]`, `GET /characters/{uuid}/party-runs/{id}`와 `POST .../party/start`의 `run.members[]`: `character_id`, `name`, `class`, `level`, `gear_hash`(착용 장비 키를 정렬해 이은 문자열의 SHA-256 앞 16자), `worn: [{ slot, item_key }]`(최대 8개), `career`(0이면 미전직).
- 필드 세션 하트비트 응답의 `members[]`는 `level`, `gear_hash`만 준다(가벼운 갱신용).

대조 규칙(호스트 클라이언트): `card.level != view.level` 또는 `card.gear_hash != view.gear_hash` 또는 `card.career != view.career`이면 **서버 뷰 값으로 덮어쓴다**(레벨, `worn`으로 장비·능력치 재구성, 직업). 표시 이름은 항상 `view.name`. 멤버 위치는 틱마다 `|위치 변화| <= 최대 이동 속도(player.json) x dt x 1.5`로 자른다. 불일치가 있었으면 `card_mismatch: true`를 방장 보고 또는 관찰 보고에 싣는다.

### 19.7 연결 종료와 마을 표시

- 같은 계정이 다른 곳에서 로그인하면 열려 있던 `/ws`는 `bye`(`code: 4001`, `reason: 'SESSION_REPLACED'`, `reconnect: false`) 후 close 4001, `/relay`도 같은 사유로 끊긴다(중계 연결은 새 티켓을 받아 다시 붙는다). 새 세션을 닫지 않으므로 4001을 받으면 재연결하지 않고 로그인 화면으로 간다.
- 마을 `town.pos`: 프로토콜은 그대로다. 서버는 `cls`, `level`, `career`, `weapon`을 **서버 값으로 바꿔 중계**하고(클라이언트가 보낸 값은 무시), `skin`은 그 계정이 소유한 외형 id일 때만 그대로, 아니면 빈 문자열로 보낸다. 받은 프레임의 값을 그대로 그리면 된다.
- 캐릭터 이름은 2~8자 규칙에 더해 운영 예약어·금칙어를 거절한다(`422 NAME_FORBIDDEN`, 길이·문자 오류 `400 VALIDATION`이 먼저).

### 19.8 롤아웃 순서(클라이언트와 맞물림)

① 서버 배포(모드 `log`/`log_only`, `DEVICE_INFO_REQUIRED=false`) ② 클라이언트: `device` 필드, 프레즌스, `SESSION_REPLACED` 처리, C1~C4, 기여 필드, 카드 대조, `worn`·`career` 표시 ③ 이상 기록 확인 뒤 `DEVICE_INFO_REQUIRED=true`, `PRESENCE_KILL_MODE=enforce`, `DEVICE_LIMIT_MODE=enforce` ④ `ECONOMY_HOLD_MODE=log_only` 2주 분포 확인 후 `enforce`. 서버 배포 직후부터 바로 걸리는 것: 레이드·파티 던전 기여 잠금(방장 보고 필수), 전직 서버 진실(C1 없는 전직 저장 거절), 이름 규칙, 필드 캐리 감쇠(하드 격차), 경매 구매자 자격·상점 품목 상한·양방향 쌍 한도(일일 골드 1천만), 별조각 장비 계정 귀속.

### 19.9 구현이 설계와 다른 점

1. 경제 정지의 막는 상태는 `active`와 `clawed_back`다(설계는 `active`만). 회수 뒤에도 운영자가 해제할 때까지 막는다(12.6의 "이후 해제도 별도로 가능"과 맞추기 위해 부분 인덱스 조건을 바꿨다).
2. `dungeon_runs.humans`와 `party_size`는 실제 머릿수 그대로 두고(몬스터 체력 배율과 AI 채우기에 쓰인다), 같은 기기·Steam을 한 사람으로 센 값은 보상 최소 인원(`TOO_FEW_HUMANS`) 판정에만 쓴다.
3. 같은 기기 쌍의 경매 구매는 기록만 한다(결정 3). `AUCTION_BLOCK_SAME_DEVICE`로 차단을 켤 수 있다.
4. `income_caps.json`·`careers.json trials`는 `data_version.json`에 넣지 않았다(전자는 서버가 만드는 파일, 후자는 Unity 내보내기가 채울 항목).
5. 정합성 점검의 `I-income`·`I-names`·`I-career`·`I-holds`는 `detail.info`에만 싣고 경보용 `mismatches`에는 합치지 않는다(파생 표는 일 1회 대조로 스스로 고쳐지고, 이름·정지는 운영 판단이다).
6. 경매 요청이 교착(40P01)의 희생자가 되면 롤백된 요청을 최대 2번 다시 처리한다(서로 반대 방향으로 거래하는 두 요청이 상대 캐릭터 행의 FK 잠금에서 맞물릴 수 있다).
7. 수동 정지(H5)는 연결 계정으로 전파하지 않는다(운영자가 직접 계정을 지정한다).

### 19.10 기기 신호 대체 규칙과 프레즌스 검사 확대 (감사 반영)

1. **기기 키 대체**: `device_hash`를 생략하면 서버가 `HMAC(pepper, 'install:' + install_id)`를 기기 키로 쓴다(`AccessSignal.deviceKey`). `accounts.active_device_hash`, `online_sessions.device_hash`, `account_devices`, `login_events.device_hash`, 기기 동시 접속 한도, 레이드·파티 사람 수가 모두 이 키를 쓴다. 리프레시 토큰의 기기 결박(`REFRESH_DEVICE_BIND`)만 클라이언트가 실제로 보낸 `device_hash`를 쓴다. `device_hash` 생략 자체는 `device_missing` 플래그로 남는다.
2. **새 기기 신호**: 이 계정이 이미 다른 기기 키를 가진 상태에서 처음 보는 기기 키로 접속하면 `login_events.flags`에 `new_device`를 남긴다(감시 신호, 차단 근거 아님). 매번 무작위 `device_hash`를 보내는 우회는 이 플래그가 로그인마다 쌓이는 것으로 드러난다.
3. **레이드 사람 수**: `party_run_members.install_id`(UUID, 판 시작 스냅샷)를 더했다. 같은 사람 = 기기 키가 같거나 Steam 소유자가 같거나 install_id가 같은 멤버끼리의 연결 요소. 무작위 `device_hash`를 보내도 install_id가 같으면 한 사람이다. 한계: install_id까지 매번 새로 만들면 구별할 수 없다(자기 신고 신호의 한계, 진짜 방어선은 기여 판정과 경제 속도 정지).
4. **프레즌스 검사 확대**: 처치 보고에만 있던 신선도·맵 검사(4.5)를 채집, 상자 열기, 드롭 줍기에도 건다(`assertActionPresence`). 같은 `PRESENCE_KILL_MODE` 스위치를 쓴다: `log`는 로그·`anomaly_log(kill_presence)`만 남기고 통과, `enforce`는 신선도 없음 `409 PRESENCE_REQUIRED`, 맵 불일치 `422 PRESENCE_MAP_MISMATCH`. 채집은 요청의 `map_id`, 상자는 `chest_id`가 속한 맵, 드롭 줍기는 맵 정보가 없어 신선도만 본다.
5. **회수(H4) 보정**: 골드 회수는 시간 버킷이 아니라 `gold_ledger`(획득 사유 양수)와 `auction_trades`(판매 대금 - 구매 대금)를 `[window_start, window_end]` 안의 정확한 시각으로 합산한다. 같은 회수에서 폐기하는 미수령 판매 대금 우편의 골드는 지갑 회수액에서 뺀다(이중 회수 방지). 아이템 회수는 아이템 키별로 `구간 획득 - 구간 사용·제거`(이동·회수 제외)까지만 가져간다(강화 단계가 키에 들어 있어 구간에서 얻지 않은 다른 단계 개체는 건드리지 않는다). `shadow`(log_only) 정지는 `409 HOLD_SHADOW`로 회수를 거절한다.
6. **`assertNoHold`**: 정지 행을 `FOR SHARE`로 잠그지 않는다(회수가 정지 -> 캐릭터 순으로 잠그므로 교착 위험). 캐릭터 락 이후 커밋된 정지는 같은 요청에 적용된다.
