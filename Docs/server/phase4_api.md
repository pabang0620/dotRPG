# 서버 4단계 API 명세 (파티 협동)

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §1(전투는 방장 PC, 경제는 서버), §5(방장 권한 약점 보완), §9 4단계. 앞 단계: [phase1_2_api.md](phase1_2_api.md), [phase3_api.md](phase3_api.md). 스키마: `server/schema.sql`, `server/migrations/0005_party.sql`. 게임 값·C# 대응: [phase4_mapping.md](phase4_mapping.md).
이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만든다. 게임 값(가격·확률·보상량·배율)은 문서에 복사하지 않고 `server/data/*.json` 이름으로만 참조한다. 본문의 수치는 서버 정책 상수(12절, 환경변수)이거나 근거 예시다.

## 0. 핵심 설계 (먼저 읽기)

1. **전투는 방장 PC, 보상은 멤버마다 서버.** 멤버 각자가 자기 `dungeon_runs` 행(3단계 표 재사용)을 갖고, 처치·결과를 자기 계정으로 보고한다. 파티 보상은 "나누기"가 아니라 멤버별 독립 지급이다(9절). 파티 인원은 몬스터 HP 배율(`partyHpScale`)로만 난이도에 반영된다.
2. **방장 보고 대 멤버 보고 대조.** 클리어 보상(경험치, 카드, 레이드 보상)은 멤버의 보고가 방장의 판 전체 보고 또는 다른 멤버의 보고와 일치할 때만 지급한다(8절). 대조할 다른 사람이 없으면 3단계 솔로와 같은 수준의 검증만 한다. 대조는 기존 검증에 더하는 것이라 솔로보다 약해지지 않는다.
3. **입장 토큰은 방장 PC가 오프라인으로 검증한다.** 서버가 판마다 난수 키를 만들어 방장에게만 주고, 각 멤버는 `HMAC(키, 판.캐릭터.자리)` 토큰을 받는다. 방장 PC가 P2P 연결 첫 메시지의 토큰을 검증한다(6.2). Steam 로비를 쓰지 않고 서버가 아는 명단(Steam ID)으로 연결을 허용한다.
4. **호스트 인계는 서버가 세대(epoch)로 직렬화한다.** 하트비트로 서버가 방장 생존을 보고, 살아 있는 가장 작은 자리의 멤버만 인계를 받을 수 있고, 옛 세대의 보고는 거절한다(6.4).
5. **WebSocket 없이 폴링.** 4단계의 로비·매칭·판 상태는 `version`이 붙은 GET 폴링(2초)과 하트비트(5초)다. 동시 100명 미만에서 충분하고 5단계 WebSocket은 같은 응답 모양을 이벤트로 바꾸기만 한다.

## 1. 공통 규칙

3단계 0.1~0.6(응답 형식, 멱등성, 락, `delta`, 날짜 경계, RNG, 속도 제한)과 1~2단계 0.2~0.3(인증, 버전 헤더)을 그대로 쓴다. 파티 경로는 전부 `/characters/{uuid}/...` 아래라 클라이언트·데이터 버전을 둘 다 검사한다(Steam 인증 2개만 예외, 3절). 응답에는 uuid만 싣는다(내부 bigint 키 금지). 다른 멤버를 가리키는 id는 그 캐릭터의 `uuid`이고, 모든 경로가 토큰 소유자 검사를 하므로 uuid를 안다고 남의 캐릭터를 조작할 수 없다.

### 1.1 락 순서 (4단계 추가)
- 3단계 규칙("자기 캐릭터 하나를 먼저 잠근다")에 더해 **여러 캐릭터를 잠글 때는 id 오름차순으로 한 번에 잠근 뒤** `parties`, `party_runs` 순으로 잠근다. 이 순서가 유일하다.
- 여러 캐릭터를 잠그는 요청은 파티 출발(`/party/start`)과 판 시작(`begin`, 마지막 `join`이 자동으로 부르는 경우 포함)뿐이다. 이유: 두 곳은 멤버 전원의 입장 횟수·자격을 확인하고 멤버별 `dungeon_runs`를 만든다. 다른 요청(처치, 결과, 정산, 하트비트)은 자기 캐릭터 하나만 잠그므로 교착이 없다.
- 판에 참여 중인 캐릭터(`party_run_members.state` 가 `invited | joined | playing | disconnected`)는 솔로 입장·다른 파티 출발이 `409 IN_PARTY_RUN`이다. 그래서 `begin`이 다른 멤버의 `dungeon_runs`를 만들 때 그 멤버가 동시에 입장 횟수를 쓰는 일이 없다. DB 안전장치는 `party_run_members_one_active`, `dungeon_runs_one_playing`이다.

### 1.2 폴링과 `version`
- `GET /party`는 `?after_version=N`을 받는다. 파티가 N 이후 바뀌지 않았고 대기열 상태도 같으면 `{ "changed": false }`만 돌려준다(응답 작음). 클라이언트는 2초마다 부른다.
- `parties.version`은 구성·상태·준비·신청이 바뀔 때마다 +1(같은 트랜잭션). 판 상태(`run`)는 `GET /party-runs/{id}`와 하트비트 응답으로 따로 본다.
- 시간 전이(신청 30초 만료, 게시 10분 만료, 입장 마감, 연결 끊김, 방치 정리)는 **타이머 없이 요청이 닿을 때 조건부 UPDATE로 지연 처리**한다(`WHERE state='...' AND 마감 < now()`). 한 대 서버·100명 미만에서 충분하고, 7단계에서 청소 작업을 더해도 같은 UPDATE를 쓴다.

### 1.3 서버가 계산하는 전투력 (`power_estimate`)
`min_power`(참가 조건)와 자동 매칭(±30%)은 전투력을 쓴다. **클라이언트가 보낸 전투력은 받지 않는다.** 서버가 레벨과 착용 장비(강화 반영)처럼 서버가 아는 값만으로 `power_estimate`를 계산한다(공식은 `player.json`의 `power` 가중치, mapping 4절). 패시브 효과는 서버가 모르므로 캐릭터 카드의 전투력과 다를 수 있다 - 온라인 모드의 파티 찾기는 서버 값으로 표시한다(결정 대기 4). `GET /characters/{uuid}` 응답에 `power_estimate`를 더한다.

### 1.4 공통 에러(파티 경로 전체)
`400 VALIDATION`, `401 TOKEN_*`, `403 ACCOUNT_BANNED`, `404 CHARACTER_NOT_FOUND`, `422 IDEMPOTENCY_MISMATCH`, `426`, `429 RATE_LIMITED`(3단계와 같다). 자주 쓰는 새 코드:

| code | 상태 | 뜻 |
|---|---|---|
| `ALREADY_IN_PARTY` | 409 | 이미 다른 파티에 있다(`errors.party_id`) |
| `NOT_IN_PARTY` | 404 | 속한 파티가 없다 |
| `NOT_LEADER` | 403 | 방장만 할 수 있다 |
| `IN_QUEUE` | 409 | 매칭 대기 중이다 |
| `IN_PARTY_RUN` | 409 | 파티 판에 참여 중이다(솔로 입장·다른 파티 불가) |
| `PARTY_FULL` | 409 | 정원이 찼다 |
| `PARTY_NOT_FOUND` | 404 | 없거나 마감된 파티(비공개·만료·해산을 구분하지 않는다) |
| `POWER_TOO_LOW` | 422 | 최소 전투력 미달(`errors.need`, `errors.have`) |
| `LEVEL_TOO_LOW` | 422 | 난이도 권장 레벨 - 허용 폭(`PARTY_MIN_LEVEL_SLACK`) 미달 |

던전 자격 코드(`DUNGEON_UNKNOWN`, `DUNGEON_CLOSED_TODAY`, `DIFFICULTY_LOCKED`, `NO_ENTRIES_LEFT`)는 3단계 9.3과 같다. 레이드용 새 코드: `RAID_LOCKED`(해금 퀘스트 미달), `KEYS_MISSING`(최종 레이드 봉인 열쇠 부족).

### 1.5 속도 제한 (메모리, 캐릭터당)
3단계 경로의 IP 한도(600/분)를 상속한다. 값은 환경변수.

| 엔드포인트 | 한도 | 근거 |
|---|---|---|
| GET `/parties` | 1초 1회 | UI 새로고침 3초 쿨타임 |
| GET `/party` | 초당 2회 | 2초 폴링 + 즉시 갱신 여유 |
| POST `/parties`, `/match/queue` | 3초 1회 | 사람이 누르는 속도 |
| 신청, 수락·거절, 준비, 강퇴, 위임, 나가기, PATCH | 초당 2회 | |
| 출발, 입장 확인, begin, 호스트 인계, 방장 보고, 결과, 정산, 이탈 | 초당 1회 | 판당 한 번씩 |
| 하트비트 | 2초 1회 | 클라이언트는 5초마다 |
| `/auth/steam` | IP당 20회/분 | 로그인 시도 |
| `/auth/steam/link` | 계정당 5회/분 | |

## 2. 엔드포인트 요약 (신규 27개 + 확장 6개)

경로는 `/characters/{uuid}` 아래(표에서 생략)이고 인증은 액세스 토큰이다. Steam 인증 2개는 예외다.

| # | 메서드 | 경로 | 하는 일 |
|---|---|---|---|
| A1 | POST | `/auth/steam` (전체 경로) | Steam 티켓으로 로그인 |
| A2 | POST | `/auth/steam/link` (전체 경로, 액세스 토큰 필요) | 기존 계정에 Steam 연결 |
| P1 | GET | `/parties` | 모집 게시판 목록 |
| P2 | POST | `/parties` | 파티 만들기(모집 글) |
| P3 | GET | `/party` | 내 파티·신청·판·매칭 상태(폴링) |
| P4 | PATCH | `/party` | 방장: 모집 설정 변경 |
| P5 | POST | `/parties/{party_id}/apply` | 참가 신청 |
| P6 | DELETE | `/party/applications/{application_id}` | 내 신청 취소 |
| P7 | POST | `/party/applications/{application_id}/respond` | 방장: 수락·거절 |
| P8 | POST | `/party/ready` | 준비 완료 토글 |
| P9 | POST | `/party/leave` | 파티 나가기 |
| P10 | POST | `/party/kick` | 방장: 강퇴 |
| P11 | POST | `/party/leader` | 방장 위임 |
| M1 | POST | `/match/queue` | 자동 매칭 대기열 입장 |
| M2 | DELETE | `/match/queue` | 대기 취소 |
| M3 | POST | `/match/fill-ai` | "AI로 채워 출발" |
| R1 | POST | `/party/start` | 방장: 던전 출발(입장 토큰 발급) |
| R2 | GET | `/party-runs/{id}` | 판 상태·내 입장 토큰 |
| R3 | POST | `/party-runs/{id}/join` | 방장에게 연결됨(입장 확인) |
| R4 | POST | `/party-runs/{id}/begin` | 방장: 들어온 사람으로 판 시작 |
| R5 | POST | `/party-runs/{id}/heartbeat` | 생존 신호, 호스트·멤버 상태 수신 |
| R6 | POST | `/party-runs/{id}/host/claim` | 호스트 인계 요청 |
| R7 | POST | `/party-runs/{id}/host-report` | 방장: 판 전체 관찰 보고 |
| R8 | POST | `/party-runs/{id}/leave` | 판에서 이탈(또는 입장 취소) |
| D3 | POST | `/dungeon-runs/{run_id}/settle` | 대조·정산(보상 지급) |
| D4 | GET | `/dungeon-runs/{run_id}` | 내 판 기록 조회 |
| D6 | GET | `/raids` | 레이드 해금·보상·열쇠 상태 |

확장 6개(3단계 엔드포인트 변경): D1 `POST /dungeon-runs`(AI 인원, 레이드), D2 `POST /dungeon-runs/{run_id}/result`(파티 판은 접수 후 대조), D5 `POST /kills`(파티·레이드 맥락), `GET /dungeons`(레이드 제외 규칙, `active_run`에 `party_run_id`), `GET /characters/{uuid}`(`power_estimate`), `GET /me`(`steam_linked`). 상세는 7절.

## 3. Steam 인증

### 3.1 경로
인증 공급자는 1단계 인터페이스 뒤에 있다(PLAN_SERVER §10.1). `auth_identities.provider='steam'`, `subject` = SteamID64 문자열(`0001`에 이미 있다). `0005`는 "계정당 공급자 하나" 유니크 인덱스만 더한다.

설정(환경변수, 값이 틀리면 서버가 뜨지 않는다):

| 이름 | 값 | 의미 |
|---|---|---|
| `STEAM_AUTH_MODE` | `off` \| `mock` \| `web_api` | `off`: A1·A2 라우트를 등록하지 않아 404. `mock`: 개발용. `web_api`: 실제 검증 |
| `STEAM_APP_ID` | 숫자 | 앱 ID. 앱 ID가 생기기 전에는 Valve 시험용 `480`. `web_api`에서 필수 |
| `STEAM_WEB_API_KEY` | 비밀 | `.env`로만 읽는다. `web_api`에서 필수. 로그에 남기지 않는다 |
| `STEAM_IDENTITY` | 문자열 | 티켓 발급 때 클라이언트가 쓰는 식별 문자열(예: `dotrpg-server`). 다른 서비스에서 만든 티켓을 재사용하지 못하게 한다 |
| `PARTY_TRANSPORT` | `dev` \| `steam` | `dev`: Steam ID 없이도 파티 가능(같은 PC `UdpTransport`). `steam`: 모든 멤버가 Steam 연결 필수(`422 STEAM_REQUIRED`) |

기동 검사: `NODE_ENV=production`이면 `STEAM_AUTH_MODE=mock`과 `PARTY_TRANSPORT=dev` 조합은 거부(서버가 뜨지 않음). `AUTH_DEV_ENABLED`(개발용 아이디 로그인)와 독립이다: 출시 후 `AUTH_DEV_ENABLED=false`, `STEAM_AUTH_MODE=web_api`.

### 3.2 앱 ID가 없을 때의 개발 모드
| 단계 | 모드 | 방법 |
|---|---|---|
| 지금 | `STEAM_AUTH_MODE=mock`, `PARTY_TRANSPORT=dev` | 같은 PC 창 2개(`UdpTransport`), 개발용 아이디 로그인. Steam 불필요 |
| Steam 시험 | `STEAM_AUTH_MODE=web_api`, `STEAM_APP_ID=480` | Steam 클라이언트 실행 + `steam_appid.txt`(480). 두 대의 PC에 서로 다른 Steam 계정이 필요하다(한 PC에서 Steam 계정 2개는 못 쓴다). Spacewar(480)는 모든 개발자가 공유하므로 로비·매치메이킹 목록은 쓰지 않는다(이 설계는 로비를 쓰지 않아 영향 없다) |
| 출시 | `web_api`, 실제 앱 ID | 앱 ID 발급 후 `.env`의 두 값만 바꾼다 |

`mock` 모드의 티켓은 `mock:<steam_id64>:<nonce>` 문자열이다. 네트워크 호출 없이 SteamID를 믿으므로 개발 환경에서만 켠다. 파티 코드가 Steam 없이도 `steam_id`가 채워진 응답 모양을 시험할 수 있다.

**확인이 필요한 것(첫 시험에서 실측)**: `480`으로 `ISteamUserAuth/AuthenticateUserTicket`을 부를 때 일반 사용자 Web API 키로 되는지, 앱별 퍼블리셔 키가 필요한지. 안 되면 앱 ID 발급 전까지 `mock`으로 개발하고 발급 후 `web_api`로 전환한다(서버 코드는 같다). 실측 결과는 이 문서에 기록한다.

### 3.3 POST /auth/steam (A1)
- 인증: 없음. 클라이언트 버전 검사 함(데이터 버전은 안 함, `/auth/*` 규칙).
- 요청(zod `.strict()`): `{ ticket: string(hex 정규식, 16..4096자) }`. 클라이언트가 SteamID를 같이 보내도 받지 않는다(서버가 티켓에서 얻는다).
- 처리:
  1. `mock`: 정규식 `^mock:(\d{17}):[A-Za-z0-9]{8,32}$`. 그 밖의 모드에서 `mock:`으로 시작하면 거부.
  2. `web_api`: `GET https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key=<STEAM_WEB_API_KEY>&appid=<STEAM_APP_ID>&ticket=<hex>&identity=<STEAM_IDENTITY>` (시간 제한 5초, 재시도 없음). 성공 판정: `response.params.result == "OK"`. SteamID는 `params.steamid`. `publisherbanned == true`면 `403 STEAM_BANNED`. `ownersteamid`(패밀리 공유)가 `steamid`와 다르면 로그에 남기되 로그인은 허용(정책은 운영에서).
  3. 티켓 재사용 방지: `sha256(ticket)`을 메모리 집합에 10분 보관, 이미 있으면 `401 TICKET_REPLAYED`(QueueStore처럼 인터페이스 뒤에 두어 나중에 Redis로).
  4. `auth_identities(provider='steam', subject=steamid)` 조회. 없으면 `accounts` + `auth_identities`를 한 트랜잭션으로 만든다(`201`). 있으면 로그인(`200`). 정지(`banned_until`)·삭제 확인은 dev 로그인과 같다.
  5. 토큰 쌍은 dev 로그인과 같은 형식으로 발급(새 `family_id`).
- 응답 `data`: dev 로그인과 같다(`account`, `access_token`, `access_expires_in`, `refresh_token`, `refresh_expires_at`) + `created: bool`.
- 에러: `400 VALIDATION`, `401 STEAM_TICKET_INVALID`(Steam이 거절, 형식 오류, 만료), `401 TICKET_REPLAYED`, `403 STEAM_BANNED`, `403 ACCOUNT_BANNED`, `404`(`off`), `426`, `429`, `503 STEAM_UNAVAILABLE`(Steam API 장애·시간 초과, 클라이언트는 재시도 안내).
- 멱등성: 없음(로그인이라 같은 티켓 재전송은 `TICKET_REPLAYED`. 클라이언트는 응답을 못 받으면 새 티켓을 받아 다시 보낸다).
- 로그: 티켓 원문을 로그에 남기지 않는다(앞 8자 해시만).

### 3.4 POST /auth/steam/link (A2)
- 인증: 액세스 토큰. 요청: A1과 같다. 개발 중 `dev` 계정을 Steam에 묶어 두 PC에서 시험하거나, 출시 전 테스터 계정을 Steam으로 옮길 때 쓴다.
- 처리: A1의 1~3과 같이 SteamID를 얻은 뒤 `auth_identities(account_id=나, provider='steam', subject)` INSERT. 이미 다른 계정에 묶인 SteamID는 `409 STEAM_ALREADY_LINKED`, 내 계정이 이미 Steam을 가졌으면 `409 ACCOUNT_ALREADY_LINKED`(`0005`의 유니크 인덱스가 마지막 안전장치).
- 응답 `200` `data`: `{ "linked": true }`. `GET /me`는 `steam_linked: bool`을 더한다.

## 4. 파티 모집 게시판·로비

### 4.1 공통 객체
`PartyView`(내 파티, `GET /party`·변경 응답):
```
{
  "id": uuid, "version": 12, "state": "forming" | "starting" | "in_run",
  "dungeon_id": "gold_vein", "difficulty": 1, "max_members": 4, "min_power": 2300,
  "message": "빠르게 돌아요", "listed": true, "listed_until": ISO | null, "source": "board" | "match",
  "start_by": ISO | null,
  "members": [ { "character_id": uuid, "name": "...", "class": "warrior", "level": 12, "power": 2410,
                 "ready": true, "is_leader": true, "is_me": false } ],
  "applications": [ { "id": uuid, "character": { "id": uuid, "name", "class", "level", "power" }, "expires_at": ISO } ],  // 방장에게만
  "run": RunView | null                                                                                              // 6.3
}
```
`PartyPost`(게시판 한 줄): `{ id, dungeon_id, difficulty, members(현재 사람 수), max_members, min_power, message, leader: { name, class, level }, mine: bool, applied: bool, full: bool, listed_until }`. 내부 키·계정 id는 싣지 않는다.

### 4.2 GET /parties (P1)
- 쿼리(zod): `dungeon_id?: string`, `difficulty?: int(0..3)`, `page?: int(1..)=1`, `limit?: int(1..50)=20`.
- 조회: `listed AND state='forming' AND listed_until > now()`. 정렬: 내 글, 빈자리 있는 글 먼저(`full` 나중), `min_power` 오름차순(C# `MockPartyFinderService.List`와 같은 순서), 같으면 최신순. 목록은 100명 미만이라 메모리 정렬 후 페이지로 자른다(인덱스는 `parties_board`로 후보만 좁힌다: 필터 컬럼과 최신순).
- 응답 `data`: `{ "posts": [PartyPost] }`, `meta`: `{ total, page, limit }`.
- 에러: 공통만. 멱등성: 읽기라 해당 없음.

### 4.3 POST /parties (P2)
- 요청(`.strict()`): `{ request_id: uuid, dungeon_id: string(1..40), difficulty: int(0..3), max_members: int(2..4), min_power: int(0..99999), message?: string(0..30), listed: boolean }`. 받지 않는 값: 방장 전투력, 시각, 만료.
- 처리(내 캐릭터 잠금 후): 이미 파티·대기·판에 있으면 `ALREADY_IN_PARTY` / `IN_QUEUE` / `IN_PARTY_RUN` → **던전 자격 검사**(5.2 `checkEntry`) → `min_power <= 내 power_estimate`(위반 시 `422 MIN_POWER_TOO_HIGH`, C# 슬라이더 50~100%와 같은 의미) → 메시지 금칙어(`422 MESSAGE_BLOCKED`, 5단계 채팅과 같은 필터 모듈, 목록은 게임 데이터가 아닌 운영 파일 `server/data/banned_words.json`) → `parties` + 방장 `party_members` INSERT, `listed_until = now + PARTY_LISTING_MINUTES(10)`.
- 응답 `201` `data`: `{ "party": PartyView }`.
- 에러: 위 + `422 DUNGEON_*`, `RAID_LOCKED`, `LEVEL_TOO_LOW`, `NO_ENTRIES_LEFT`, `MESSAGE_BLOCKED`.
- 멱등성: `request_id`(같은 id 재전송은 같은 파티를 돌려준다). 모집 글 중복은 `party_members_one_active`가 막는다.

### 4.4 GET /party (P3)
- 쿼리: `after_version?: int`. 응답 `data`:
```
{ "changed": true,
  "party": PartyView | null,
  "queue": { "dungeon_id", "difficulty", "queued_at": ISO, "depart_at": ISO, "humans_waiting": 2, "fill_ai_available": true } | null,
  "applications_mine": [ { "id": uuid, "party_id": uuid, "state": "pending" | "accepted" | "rejected" | "expired" | "cancelled", "expires_at": ISO } ],   // 최근 3건
  "notice": { "code": "PARTY_CLOSED" | "KICKED" | "START_TIMEOUT", "at": ISO } | null }
```
- `notice`는 최근 2분 안에 `party_members.left_reason`이 채워진 가장 최근 기록에서 만든다(파티가 해산되거나 강퇴됐다는 사실을 폴링으로 알린다). 같은 notice를 반복해서 주지 않도록 클라이언트가 `at`을 기억한다.
- 폴링마다 지연 전이를 처리한다: 만료된 내 파티 신청, 방치(30분) 파티 해산, 자동 매칭 파티의 `start_by` 초과(해산, `START_TIMEOUT`), 입장 마감이 지난 `gathering` 판(6.5).
- 인덱스: `party_members_one_active`(내 소속 1건), `party_members_party`, `party_applications_party`, `party_members_notice`.

### 4.5 PATCH /party (P4)
- 요청(`.strict()`, 하나 이상): `{ listed?: boolean, message?: string(0..30), min_power?: int, max_members?: int(2..4) }`. 방장만(`403 NOT_LEADER`). `max_members`는 현재 사람 수 이상. `min_power`는 방장 power 이하. 이미 `starting`/`in_run`이면 `409 PARTY_BUSY`.
- 효과: 구성 조건이 바뀌면 멤버 `ready`를 해제하고 `version`+1. `listed=true`로 바꾸면 `listed_until`을 다시 10분으로.
- 응답: `{ party: PartyView }`. 멱등성: 설정 대입이라 같은 요청 재전송은 같은 결과(별도 `request_id` 불필요).

### 4.6 POST /parties/{party_id}/apply (P5)
- 요청: `{ request_id: uuid }`. 받지 않는 값: 전투력.
- 처리: 파티가 `forming`·`listed`·게시 만료 전(`404 PARTY_NOT_FOUND`) → 내 파티·대기·판 없음 → 내 글 아님(`422 OWN_PARTY`) → 정원 `PARTY_FULL` → 던전 자격 `checkEntry` → `power_estimate >= min_power`(`POWER_TOO_LOW`) → 동시에 대기 중인 내 신청 3건 미만(`429 TOO_MANY_APPLICATIONS`) → `party_applications` INSERT(`expires_at = now + 30초`, `power_estimate` 스냅샷).
- 응답 `201` `data`: `{ "application": { id, party_id, state: "pending", expires_at } }`. 방장은 다음 폴링에서 `applications`로 본다.
- 멱등성: `request_id`. 같은 파티에 중복 신청은 `party_applications_pending_uq`가 막고 기존 신청을 돌려준다.

### 4.7 DELETE /party/applications/{application_id} (P6)
신청자가 취소. 내 신청이 아니거나 이미 처리됐으면 `404 APPLICATION_NOT_FOUND`. 응답 `{ cancelled: true }`. 이미 취소된 것을 또 취소해도 성공으로 본다(멱등).

### 4.8 POST /party/applications/{application_id}/respond (P7)
- 요청: `{ request_id: uuid, accept: boolean }`. 방장만.
- 처리(방장 캐릭터 잠금, `parties` 잠금): 신청이 `pending`이고 만료 전이어야 한다(`410 APPLICATION_EXPIRED`). `accept=true`: 정원 확인(`PARTY_FULL`), 신청자가 아직 자유로운지 확인(다른 파티·대기·판에 들어갔으면 `409 APPLICANT_UNAVAILABLE`, 신청은 `rejected`로 닫음), `party_members` INSERT(신청자의 `party_members_one_active`가 DB에서 막음), 신청 `accepted`, 신청자의 다른 대기 신청은 `cancelled`, 멤버 `ready` 해제, `version`+1. `accept=false`: 신청 `rejected`.
- 응답: `{ party: PartyView }`. 멱등성: `request_id`(이미 처리된 신청을 다른 `request_id`로 처리하면 `409 APPLICATION_NOT_PENDING`).
- 신청자는 폴링에서 `party`가 생긴 것(수락) 또는 `applications_mine`의 `state`(거절·만료)로 결과를 안다(4.4).

### 4.9 POST /party/ready (P8), /party/leave (P9), /party/kick (P10), /party/leader (P11)
| 경로 | 요청 | 규칙 | 에러 |
|---|---|---|---|
| `/party/ready` | `{ ready: boolean }` | 방장은 항상 준비라 `422 LEADER_ALWAYS_READY`. 판이 `starting`이면 `409 PARTY_BUSY` | `NOT_IN_PARTY` |
| `/party/leave` | `{ request_id }` | 방장이 나가면 가장 오래된 멤버가 방장(`joined_at` 순), 남은 사람이 없으면 파티 해산(`disbanded`). 판 참여 중이면 `409 IN_PARTY_RUN`(먼저 R8) | `NOT_IN_PARTY`, `IN_PARTY_RUN` |
| `/party/kick` | `{ request_id, target: uuid }` | 방장만, 자기 자신 불가(`422 CANNOT_KICK_SELF`), 판 진행 중 불가(`409 PARTY_BUSY`). 대상은 `kicked`로 나가고 다음 폴링에서 `notice.KICKED` | `NOT_LEADER`, `MEMBER_NOT_FOUND` |
| `/party/leader` | `{ request_id, target: uuid }` | 방장만, 대상은 활성 멤버. 판이 `starting`/`in_run`이면 불가 | `NOT_LEADER`, `MEMBER_NOT_FOUND`, `PARTY_BUSY` |

모두 `version`+1, 응답 `{ party: PartyView | null }`(나간 사람은 `null`). 멱등성은 `request_id`(ready는 값 대입이라 필요 없음). 구성이 바뀌면 `ready`가 풀리므로 클라이언트는 응답의 `members[].ready`로 표시한다.

## 5. 자동 매칭

### 5.1 대기열 (메모리)
- 대기열은 `QueueStore` 인터페이스 뒤의 메모리 구조다(PLAN_SERVER §7: 확장 1단계에서 Redis로 교체). 키는 `(dungeon_id, difficulty)`, 티켓은 `{ characterId, accountId, level, power, queuedAt, lastPollAt, forceDepart }`. **표를 만들지 않는다**: 대기는 60초짜리 일시 상태이고, 서버가 재시작하면 `GET /party`가 `queue: null`을 돌려주므로 클라이언트가 다시 건다. 맞춰진 결과만 `parties` 행이 된다.
- 티켓 제거: 취소, 맞춰짐, 폴링이 `MATCH_TICKET_STALE_SECONDS(15)` 동안 없음(접속 끊김), 계정 정지.

### 5.2 자격 검사 `checkEntry` (생성·신청·대기·출발·시작이 같은 함수)
`checkEntry(캐릭터, 던전, 난이도, now, 모드)`는 순서대로 첫 실패에서 중단한다: 던전 존재(`DUNGEON_UNKNOWN`) → 오늘 개방(`DUNGEON_CLOSED_TODAY`; **레이드는 `openDays`만 보고 `weekendOpensAll`을 적용하지 않는다**, C# `ResetClock.IsOpen`. 3단계 서버의 `isOpenToday`는 모든 던전에 토·일 전체 개방을 적용하므로 4단계에서 레이드 분기를 추가한다) → 레이드 해금(`RAID_LOCKED`, 10.2) → 난이도 해금(`DIFFICULTY_LOCKED`, 레이드 제외) → 레벨 하한(`LEVEL_TOO_LOW`: `레벨 >= difficulties[난이도].recommendedLevel - PARTY_MIN_LEVEL_SLACK`. 파워 레벨링 방지: 낮은 레벨이 고난이도 파티에 끼어 큰 경험치를 받는 것을 막는다. 레이드는 레이드 자체의 `recommendedLevel`) → 입장 횟수(`NO_ENTRIES_LEFT`, 요일 던전만) → 레이드 열쇠(`KEYS_MISSING`, 최종 레이드이고 이번 기간 보상 수령 전일 때만). `create/apply/queue` 시점의 통과는 자리 예약이 아니므로(그 사이 입장 횟수를 쓸 수 있다) R1과 R4에서 같은 함수를 다시 호출해 최종 판정한다.

### 5.3 POST /match/queue (M1)
- 요청: `{ request_id: uuid, dungeon_id: string, difficulty: int(0..3) }`. 파티·판 없는 캐릭터만(`ALREADY_IN_PARTY`, `IN_PARTY_RUN`), 이미 대기 중이면 기존 티켓을 돌려준다(멱등).
- 응답 `200` `data`: `{ "queue": { dungeon_id, difficulty, queued_at, depart_at, humans_waiting, fill_ai_available } }`. `depart_at = queued_at + MATCH_QUEUE_SECONDS(60)`(C# `PartyFinderRules.QueueSeconds`).

### 5.4 맞추기 규칙 (서버가 1초마다 + 요청이 닿을 때)
키별로 티켓을 `queuedAt` 순으로 본다. 가장 오래 기다린 티켓이 시드:
1. 시드와 `power`가 `±MATCH_POWER_RATIO(30%)` 안인 티켓을 모아 최대 4명. **4명이 되면 즉시 파티**.
2. 4명이 안 되면 시드가 `depart_at`에 닿았거나 시드가 `forceDepart`일 때 모인 사람으로 파티(1명이어도 된다). 빈자리는 AI(방장이 출발 때 `ai_count`로 정함, 기본 `4 - 사람 수`).
3. 파티가 만들어지면: `parties(source='match', listed=false, start_by=now+MATCH_START_SECONDS(60), max_members=4)` + 멤버 INSERT(`ready=true`, 방장 = 시드), 모인 티켓 제거. 그 사이 멤버가 자격을 잃었으면(입장 횟수 소진 등) 그 사람만 빼고 대기열로 되돌린다.
4. 만들어지면 멤버들의 다음 폴링에서 `queue: null`, `party != null`이 되고, 클라이언트는 이것을 `Departed` 이벤트로 본다. 방장 클라이언트는 곧바로 R1(출발)을 부른다.

### 5.5 DELETE /match/queue (M2), POST /match/fill-ai (M3)
- M2: 티켓 제거. 대기 중이 아니면 성공으로 본다(멱등). 응답 `{ cancelled: true }`.
- M3 요청 `{ request_id }`: 내 티켓을 `forceDepart`로 하고 즉시 5.4의 2번을 실행한다. 모인 사람(나 포함)으로 파티를 만들고 응답 `201` `data`: `{ "party": PartyView }`. 내 티켓이 없으면 `404 NOT_QUEUED`. 멱등성: `request_id`(재전송은 같은 파티).

## 6. 파티 던전 판

### 6.1 흐름
```
방장                    서버                                멤버
 |-- R1 start ---------->|  자격 검사(전원), party_runs(gathering), 토큰 발급
 |<- run + host_key -----|                                    |
 |                       |<--------------- GET /party (폴링) -| run 확인
 |                       |-- run + entry_token + 방장 steam_id ->|
 |<=========== P2P 연결 (토큰 + Steam ID 확인) ===============|
 |                       |<--- R3 join(entry_token) ----------|
 |  (전원 입장 시 서버가 자동 begin / 방장이 R4 begin)         |
 |                       |  멤버별 dungeon_runs 생성, 입장 횟수 소모, power_cap 고정
 |<=============== 전투(방장 PC 계산, 스냅샷 10Hz) ============>|
 |-- R5 heartbeat (5초) -|<--------------- R5 heartbeat ------|
 |                       |   각자 D5 kills (자기 계정)         |
 |-- R7 host-report ---->|<--- D2 result / D3 settle ---------|
```

### 6.2 입장 토큰
- 판이 `gathering`으로 만들어질 때 서버가 `run_key`(난수 32바이트)를 만들어 `party_runs.run_key`에 저장하고, **방장에게만** 응답(`host_key`, base64url)으로 준다. 호스트 인계 때 새 방장에게도 준다. 판이 끝나면 `run_key`는 `NULL`.
- 멤버의 `entry_token` = `base64url(HMAC-SHA256(run_key, "<판 uuid>.<캐릭터 uuid>.<자리>")[0..16])`(22자). 서버는 저장하지 않고 필요할 때 계산한다(`R2` 응답, `R3` 검증).
- **P2P 연결 규칙**: 멤버는 방장에게 연결한 뒤 첫 신뢰 메시지로 `{ run_id, character_id, slot, entry_token }`을 보낸다. 방장 PC가 같은 HMAC을 계산해 비교하고(서버 호출 없이 오프라인 검증), Steam 모드에서는 연결의 SteamID가 서버가 준 명단의 그 멤버 SteamID와 같은지도 본다. 통과해야 연결을 받는다. `dev` 모드(UDP)는 토큰만 본다. 토큰은 판·캐릭터·자리에 묶여 있어 다른 판이나 다른 사람의 것으로 쓸 수 없다.
- **서버가 토큰을 쓰는 곳**: R3(`join`)이 토큰을 요구한다. "방장 쪽으로 연결을 시도했다"는 멤버의 주장을 서버가 받는 단계이고, 토큰은 R2 응답(본인 인증된 요청)으로만 얻으므로 판 uuid만 안다고 입장 확인을 할 수 없다.
- 토큰 TTL은 판의 `gather_deadline_at`(출발 + 90초)이다. begin 이후에는 새 연결(재접속, 호스트 인계 뒤 새 방장에게 재연결)에도 같은 토큰을 쓴다(상태가 `playing`/`disconnected`인 멤버만).

### 6.3 RunView (판 상태)
```
{ "id": uuid, "state": "gathering" | "playing" | "ended" | "cancelled",
  "dungeon_id", "difficulty", "humans": 3, "ai_count": 1,
  "host": { "character_id": uuid, "steam_id": string | null, "epoch": 1 },
  "gather_deadline_at": ISO, "begun_at": ISO | null,
  "me": { "slot": 1, "state": "invited" | "joined" | "playing" | "disconnected" | "done" | "left",
          "entry_token": string | null,          // 내가 활성 멤버(invited, joined, playing, disconnected)일 때만. 재연결에도 같은 값
          "run_id": uuid | null,                 // begin 뒤 내 dungeon_runs uuid (처치·결과 경로의 run_id)
          "reward_locked": false, "lock_reason": null },
  "members": [ { "character_id": uuid, "name", "class", "level", "slot", "state", "steam_id": string | null } ] }
```
`steam_id`는 `PARTY_TRANSPORT=steam`일 때 SteamID64 문자열이고 `dev`이면 `null`. 판 멤버에게만 보인다.

### 6.4 R1 POST /party/start - 방장 출발
- 요청(`.strict()`): `{ request_id: uuid, ai_count: int(0..3) }`. 받지 않는 값: 파티 크기, 시각.
- 처리(**멤버 캐릭터 전원 id 오름차순 잠금**, `parties` 잠금): 방장만(`NOT_LEADER`) → 파티 `forming`(`PARTY_BUSY`) → 사람 수 `h`(방장 포함 1..4) → `h + ai_count <= 4`(`422 PARTY_TOO_BIG`), 레이드는 `maxParty` → 모든 사람 `ready`(방장 제외, `409 NOT_ALL_READY`) → 멤버별 `checkEntry`(실패하면 `422 MEMBER_NOT_ELIGIBLE`, `errors.members=[{ character_id, name, code }]`, 입장 횟수 같은 사유 코드만, 이 목록을 방장에게 보이는 것은 파티원이라 허용) → Steam 모드면 모든 멤버가 Steam 연결이 있어야 한다(`422 STEAM_REQUIRED`) → `party_runs`(`gathering`, `host=방장`, `epoch=1`, `humans=h`, `ai_count`, `run_key`, `gather_deadline_at=now+PARTY_GATHER_SECONDS(90)`) + `party_run_members`(방장 `joined`, 나머지 `invited`, 자리는 `joined_at` 순 0..) INSERT, `parties.state='starting'`, `version`+1.
- 응답 `201` `data`: `{ "run": RunView, "host_key": string }`. 멤버의 `entry_token`은 R2/폴링으로 준다.
- 에러: 위 + `NOT_LEADER`, `PARTY_BUSY`, `IN_PARTY_RUN`(누군가 이미 판에 참여 중).
- 멱등성: `request_id`. 인덱스: `party_runs_one_active`(한 파티 한 판), `party_run_members_one_active`.

### 6.5 R2 GET /party-runs/{id}, R3 POST /party-runs/{id}/join, R4 POST /party-runs/{id}/begin
- **R2**: 내가 그 판의 멤버(`party_run_members`)여야 한다(아니면 `404 RUN_NOT_FOUND`). 응답 `{ run: RunView }`. 지연 전이: `gathering`이고 마감이 지났으면 자동으로 6.5-begin(들어온 사람이 1명 이상) 또는 취소(`nobody_joined`, 방장도 안 들어옴).
- **R3** 요청 `{ request_id, entry_token: string(22자), host_steam_id?: string(17자리 숫자) }`. `host_steam_id`는 Steam 모드에서 필수: 멤버 클라이언트가 실제로 연결한 방장 SteamID이고 서버가 기록한 방장 SteamID와 같아야 한다(`422 HOST_MISMATCH`). 토큰이 틀리면 `403 ENTRY_TOKEN_INVALID`. 판이 `gathering`이고 내 상태가 `invited`일 때만(이미 `joined`면 성공으로 본다). 멤버 `joined`, `last_seen_at=now`. **마지막 사람이 들어오면 같은 트랜잭션에서 begin을 실행**한다. 응답 `{ state, begun: bool, run: RunView }`.
- **R4** 요청 `{ request_id }`. 방장만(`NOT_HOST`). `gathering`에서만 가능(마감 전에도 "들어온 사람으로 시작"). 처리(**멤버 전원 id 오름차순 잠금**, `party_runs` 잠금):
  1. `joined` 상태가 아닌 `invited`는 `no_show`로 닫는다(입장 횟수를 쓰지 않는다).
  2. 남은 멤버마다 `checkEntry` 재확인. 탈락자는 `dropped`.
  3. `humans` = 남은 수, `ai_count = min(3, 방장이 정한 ai_count + (출발 때 사람 수 - humans))`(불참한 자리는 AI가 채운다, 합 4 이하). 불참 때문에 AI가 늘어나는 것은 "빈자리 AI" 원칙(PLAN_ONLINE §3.3)과 같다.
  4. 멤버마다 `dungeon_runs` INSERT: `party_run_id`, `slot`, `humans`, `ai_count`, `party_size=humans+ai_count`, `started_at=begun_at`(전원 같은 값), `reset_day`(요일 던전은 입장 횟수 소모, 레이드는 `counts_entry=false`), `reward_locked`/`lock_reason`(레이드 규칙 10.3), `power_cap`.
  5. `power_cap` 계산(`party_runs.power_cap`과 각 `dungeon_runs.power_cap`에 같은 값): `Σ(사람 멤버의 attackCap(레벨, 착용 장비)) + ai_count x MERC_DAMAGE_SCALE x max(사람 멤버 attackCap)`. AI는 방장이 고른 용병이지만 용병의 레벨은 방장 레벨이고 딜은 같은 레벨 플레이어의 `damageScale`이라 가장 센 사람 기준의 상한으로 둔다(11절).
  6. `party_runs.state='playing'`, `humans`/`ai_count` 확정, 멤버 `playing`, `parties.state='in_run'`.
  - 응답 `201` `data`: `{ "run": RunView }`(내 `me.run_id` 포함). 멤버는 R2/하트비트 폴링으로 `playing`과 자기 `run_id`를 받는다.
  - 에러: `NOT_HOST`, `409 RUN_NOT_GATHERING`, `409 IN_PARTY_RUN`. 멱등성: `request_id`.

### 6.6 R5 POST /party-runs/{id}/heartbeat
- 요청: `{ seen_epoch: int(1..) }`. 5초마다. 멤버 누구나(방장 포함).
- 처리: 내 `last_seen_at = now`. 내 상태가 `disconnected`이고 끊긴 지 `PARTY_REJOIN_SECONDS(60)` 이내면 `playing`으로 복귀(재접속). 60초를 넘었으면 `left`(`rejoin_timeout`), 내 `dungeon_runs`는 `abandoned`(이미 받은 처치 경험치·드롭은 유지, 클리어 보상 없음). 다른 멤버의 `last_seen_at`이 `HOST_STALE_SECONDS(12)`보다 오래됐으면 `playing → disconnected`(`disconnected_at` 기록)로 지연 전이한다.
- 응답 `data`: `{ "state", "host": { character_id, steam_id, epoch }, "host_changed": bool, "me": { "state" }, "members": [ { character_id, slot, state } ], "server_time": ISO }`. `host_changed`는 `seen_epoch != 현재 epoch`. **옛 방장이 이것을 보면 즉시 권한을 내려놓고 멤버로 동작해야 한다**(mapping 5절).
- 인덱스: PK `(party_run_id, character_id)`(행 하나 갱신), 전체 멤버 읽기는 PK 접두.

### 6.7 호스트 인계 - R6 POST /party-runs/{id}/host/claim
방장 PC가 사라졌을 때 남은 사람 중 한 명이 새 호스트가 된다. 인계는 **서버가 하나만 승인**한다.
- 요청: `{ request_id, observed_epoch: int }`. 멤버가 방장과의 P2P 연결이 끊긴 것을 알아챈 뒤(Steam 연결 상태 콜백 또는 3초 무신호) 보낸다.
- 승인 조건(`party_runs` 잠금, 전부 만족해야 함):
  1. 판이 `playing`이다(`409 RUN_NOT_PLAYING`).
  2. `observed_epoch == 현재 epoch`. 아니면 이미 누가 인계받았다: `409 HOST_CHANGED`, `errors.host`에 현재 호스트(클라이언트는 그 호스트에 연결한다).
  3. 현재 방장이 죽은 것으로 서버가 본다: `now - 방장.last_seen_at > HOST_STALE_SECONDS(12)` 또는 방장의 상태가 `left`/`disconnected`. 아니면 `409 HOST_ALIVE`(P2P만 끊긴 분할 상황이라 서버는 인계를 허용하지 않고, 클라이언트는 방장 재연결을 시도하거나 R8로 이탈한다).
  4. 요청자가 **하트비트가 신선한(12초 이내) 활성 멤버(`playing`) 중 가장 작은 자리**다. 아니면 `409 NOT_NEXT_HOST`(`errors.next_slot`)  - 모든 멤버가 같은 규칙으로 후보를 정하므로 보통 한 명만 요청한다.
- 효과: `host_character_id`, `host_epoch + 1`, 옛 방장 멤버 상태는 `disconnected`(60초 안에 하트비트가 돌아오면 일반 멤버로 복귀, 방장 권한은 되찾지 못한다), `parties.version`+1.
- 응답 `200` `data`: `{ "host": { character_id, steam_id, epoch }, "host_key": string, "members": [...] }`. 새 방장은 `host_key`로 입장 토큰 검증을 이어받고, 나머지 멤버는 하트비트의 `host_changed`로 새 방장 SteamID를 받아 재연결한다.
- 새 방장 PC는 **자기가 받은 마지막 스냅샷부터** 몬스터 계산을 이어받는다(클라이언트 몫, mapping 5절). 서버는 전투 상태를 저장하지 않는다. 서버가 방 진행을 보는 곳은 멤버별 `dungeon_runs.room_index`/`room_kills`뿐이고 처치 보고가 계속 쌓이므로 인계 뒤에도 판정에 구멍이 없다.
- 멱등성: `request_id`(같은 요청 재전송은 같은 응답). 스플릿 브레인 방지: 옛 방장이 서버에 붙어 있으면(3번 실패) 인계가 거절되고, 인계된 뒤 옛 방장이 보내는 R7은 `409 HOST_EPOCH_STALE`.

### 6.8 R7 POST /party-runs/{id}/host-report - 방장의 판 전체 관찰
- 요청(`.strict()`):
```
{ request_id: uuid, host_epoch: int(1..),
  outcome: "cleared" | "failed",
  elapsed_ms: int(0..3600000),
  rooms: [ { room_index: int(0..9), kills: [ { monster_id: string(1..40), count: int(0..99) } ] } ],   // 최대 10
  members: [ { character_id: uuid, hits_taken: int(0..999), max_combo: int(0..9999),
               revives_used: int(0..9), damage_dealt: int(0..2147483647) } ],                           // 사람 멤버별 (1..4)
  ai: [ { slot: int(0..3), damage_dealt: int(0..2147483647) } ] }                                       // 최대 3
```
  **받지 않는 값: 경험치, 골드, 드롭, 랭크, 카드, 확률.** `damage_dealt`는 방장 PC가 관찰한 사실이고 지급에 쓰이지 않는다(화력 대조와 모순 검출에만 쓴다, 8.2).
- 처리: 현재 호스트이고 `host_epoch`가 현재 세대와 같아야 한다(`403 NOT_HOST`, `409 HOST_EPOCH_STALE`). 판 `playing`(`409 RUN_NOT_PLAYING`). 세대당 한 번: 같은 `request_id`는 멱등, 다른 `request_id`로 또 보내면 `409 REPORT_EXISTS`. `party_run_host_reports` INSERT, `party_runs.first_report_at` 없으면 `now`. 이때 판단은 하지 않고 기록만 한다(검증은 정산 때, 8절).
- 응답 `200` `data`: `{ "accepted": true, "epoch": 1 }`.

### 6.9 R8 POST /party-runs/{id}/leave
- 요청 `{ request_id }`. 판 멤버 누구나.
  - `gathering`: 방장이면 판 취소(`cancelled`, `host_cancel`, 입장 횟수 소모 없음, 파티는 `forming`으로 복귀), 멤버면 판에서 빠진다(`left`).
  - `playing`/`disconnected`: 멤버 `left`, 내 `dungeon_runs`는 `abandoned`(받은 처치 경험치·드롭 유지, 클리어 보상 없음, PLAN_ONLINE §2.2). 방장이 이탈하면 상태가 `left`가 되어 12초를 기다리지 않고 바로 R6 인계가 가능하다.
  - 이미 `left`/`done`이면 성공으로 본다.
- 응답 `{ "left": true }`. 이후 내 처치 보고는 `409 RUN_NOT_PLAYING`. 이탈한 캐릭터는 파티에는 남는다(P9로 나간다).

## 7. 던전 API 확장 (3단계 엔드포인트 변경과 신규 3개)

### 7.1 D1 POST /dungeon-runs (솔로, AI 동반, 레이드)
- 요청에 `ai_count?: int(0..3)=0`를 더한다. 레이드를 허용한다(3단계의 `RAID_NOT_AVAILABLE` 제거). 파티 판에 참여 중이면 `409 IN_PARTY_RUN`.
- 입장: `checkEntry`(5.2). 요일 던전은 3단계와 같이 `reset_day` 입장 횟수를 쓴다. **레이드는 `counts_entry=false`**(입장 횟수 소모 없음, 보상은 일일·주간 제한, 10절). `dungeon_runs`: `party_size = 1 + ai_count`, `humans=1`, `power_cap = 내 attackCap x (1 + ai_count x MERC_DAMAGE_SCALE)`, `reward_locked`(10.3).
- 응답 `201` `data`: 3단계의 `run`에 `humans`, `ai_count`, `reward_locked`, `lock_reason`을 더한다.
- 이것은 사람 1명이 AI와 가는 길이다. P2P·방장·대조가 없고 3단계와 같은 검증이다(11절 AI 권장안, 10절 레이드 보상 최소 인원).

### 7.2 D5 POST /kills (파티·레이드 맥락)
3단계 3.1과 9.4를 그대로 쓰고 아래만 바뀐다. 요청 모양은 같다(`run_id`는 **내** `dungeon_runs` uuid).
- **파티 판**: `run_id`가 내 파티 판 행이어야 하고 내 멤버 상태가 `playing`/`disconnected`, 판이 `playing`이다(아니면 `409 RUN_NOT_PLAYING`). 멤버 각자가 판의 모든 처치를 자기 계정으로 보고한다(자기가 마지막 일격을 넣었는지와 무관하다, 9절).
- **레이드 몬스터**: 해당 던전이 레이드인 판에서만 `raid` 몬스터를 받는다(그 밖의 맥락은 `RAID_NOT_AVAILABLE` 유지).
- 몬스터 레벨·HP 배율: `1 + 난이도 monsterLevel + group.levelOffset`, `난이도 hpMul x partyHpScale[party_size]`. 레이드의 난이도 수치는 레이드별 `raidNumbers`(mapping 4절).
- **화력 상한(3.2.3 변경)**: `dungeon_runs.power_cap`이 있으면 `power_cap x PARTY_POWER_SLACK(1.15)`를 그 판의 화력 상한으로 쓴다(파티 전체의 딜이 들어가므로 본인 한 사람의 상한으로는 정직한 멤버가 걸린다). 없으면 3단계 방식.
- **연습판(`reward_locked`)**: 처치를 받아들이되(방 진행, `room_kills`, `kill_log`, `kill_stats`) `RAID_PRACTICE_PAYS_KILLS=false`(기본)면 경험치·드롭을 주지 않는다. 응답 `granted_xp: 0`, `drops: []`, `reward_locked: true`. 무한 반복 입장이 가능한 레이드의 처치 경험치·골드 파밍을 막는다(결정 대기 3).
- **이상 기록 귀속(파티 판)**: `kill_*` 거절은 모든 멤버의 `anomaly_log`에 남지만 **일시 차단(`KILL_BLOCKED`) 카운트는 방장(`host_character_id`)에게만 쌓는다**. 방장이 몬스터를 즉사시키면(몬스터 계산이 방장 몫) 정직한 멤버의 보고가 같은 이유로 거절되는데, 그 멤버를 차단하지 않기 위해서다. 거절된 처치는 보상이 없으므로 이득이 없다. 멤버의 거절은 `severity=1`로 기록한다.
- 속도 제한: 3단계 한도(캐릭터당 초당 6회) 그대로. 멤버 4명이 같은 처치를 각자 보내므로 멤버당 요청 수는 솔로와 같다.

### 7.3 D2 POST /dungeon-runs/{run_id}/result (파티 판은 접수)
요청은 3단계 9.5와 같다(`outcome`, `stats`). `run_id`가 솔로(`party_run_id IS NULL`)면 3단계와 같이 즉시 판정한다. **파티 판**:
1. 진행 중인 내 판이어야 한다(`409 RUN_NOT_PLAYING`).
2. `failed`: 3단계처럼 즉시 닫는다(`failed`). 멤버 `done`. 대조 필요 없음(지급이 없다).
3. `cleared`: **먼저 자기 검증**(3단계 9.5의 구조, 시간, 화력, 값 범위. 화력은 `power_cap`, 시간 하한은 던전 `minClearSeconds`) → 어긋나면 3단계처럼 `held`(+ `anomaly_log dungeon_result`).
4. 통과하면 행을 `reported`(`reported_outcome`, `reported_at`, `ended_at=now`, `stats` 저장)로 하고 멤버 `done`, `party_runs.first_report_at` 없으면 기록 → 같은 트랜잭션에서 **내 행만** 정산을 시도한다(8절). 정산이 가능하면 응답이 최종 결과이고, 대기가 필요하면 `{ "result": "pending", "settle_after_ms": 2000 }`.
- 응답: 3단계의 결과 모양(`result: cleared|failed|held`) 또는 `pending`.
- 멱등성: `request_id`. 이미 `reported` 이후 행에 새 `request_id`로 보내면 `409 RUN_NOT_PLAYING`.

### 7.4 D3 POST /dungeon-runs/{run_id}/settle (정산, 신규)
- 요청 `{ request_id: uuid }`. 내 행이 `reported`일 때만 의미가 있다. 이미 `cleared`/`held`/`failed`면 저장된 결과를 그대로 돌려준다(멱등).
- 처리: 내 캐릭터 잠금 후 8절의 `reconcile(내 보고, 방장 보고, 다른 멤버 보고들)`을 실행한다. **다른 멤버의 행은 건드리지 않는다**(각자 자기 `settle`에서 자기 보상을 받는다). 결과에 따라: 통과면 3단계 9.5 5~7(랭크 점수, 클리어 경험치 `xp_ledger('dungeon_clear')`, 카드 굴림)과 레이드 정산(10.4)을 하고 행을 `cleared`로, 불일치면 `held`, 대기면 `pending`.
- 응답 `200` `data`: `{ "result": "pending" | "cleared" | "held" | "failed", "settle_after_ms"?, "rank"?, "score"?, "granted_xp"?, "leveled_up"?, "card_count"?, "raid"?: { "reward_locked": bool, "lock_reason"?, "key_gain"?, "key_cost"? }, "delta"? }`. `held`에는 사유를 싣지 않는다.
- 클라이언트는 `pending`이면 `settle_after_ms` 뒤 다시 부른다(최대 `PARTY_RESULT_WAIT_SECONDS` 동안).
- `cleared`로 정산할 때 `ended_at`을 정산 시각으로 갱신한다(카드 선택 TTL 24시간 기준). 카드 선택은 3단계 9.6과 같다(`POST /dungeon-runs/{run_id}/cards/pick`, 멤버마다 자기 카드 4장에서 한 장).

### 7.5 D4 GET /dungeon-runs/{run_id}
내 `dungeon_runs` 한 건 조회(재접속 복구용): `{ "run": { id, dungeon_id, difficulty, state, party_run_id?: uuid, humans, ai_count, party_size, started_at, ended_at, reward_locked, lock_reason, rank?, score?, granted_xp?, card_count?, card_picked? } }`. 카드 내용은 선택 전에는 싣지 않는다. 남의 `run_id`는 `404 RUN_NOT_FOUND`.

### 7.6 확장된 기존 응답
- `GET /dungeons`: `dungeons[]`는 요일 던전만(레이드는 `GET /raids`). `active_run`에 `party_run_id`를 더한다. `entries`는 `counts_entry` 행만 센다.
- `GET /characters/{uuid}`: `power_estimate: int`를 더한다(1.3).
- `GET /me`: `steam_linked: bool`.

## 8. 결과 대조 (방장 보고 대 멤버 보고)

### 8.1 입력과 무엇을 믿는가
| 보고 | 누가 | 담는 것 | 신뢰 |
|---|---|---|---|
| 멤버 보고 `M_i` | 멤버 i(자기 계정) | 결과, 경과 시간, 자기 `hits_taken`·`max_combo`·`revives_used` + 서버가 받은 자기 처치 기록(`dungeon_runs.room_kills`) | 서버가 직접 받은 처치 기록은 서버 사실, `stats`는 주장 |
| 방장 보고 `H` | 현재 방장 | 결과, 경과 시간, 방별 처치 수, 멤버·AI별 관찰(피격, 콤보, 부활, 딜) | 주장. 방장은 전투(몬스터 피격)를 직접 계산하므로 **피격·부활은 방장이 가장 정확**하다 |
| 서버 사실 | 서버 | 판 시작 시각(`begun_at`), 각 멤버가 보고해 받아들인 처치(`room_kills`), 몬스터 데이터, 각 멤버 레벨·착용 장비 | 신뢰 |

### 8.2 방장 보고의 자체 검증 (`H`가 유효한가)
`H`는 아래를 모두 만족해야 유효하다. 하나라도 어기면 `H`를 **없는 것으로** 취급하고 방장에게 `anomaly_log(party_host, 3)`를 남긴다(방장 보상은 방장 행의 정산에서 따로 판정된다).
1. 방별 `kills[monster_id].count <= 그 방 그룹 count 합`(방 구성을 넘는 처치 불가).
2. `elapsed_ms / 1000 <= 서버 경과(now - begun_at) + 5초`이고 `>= 최소 클리어 시간`(던전 `minClearSeconds`, 3단계와 같은 규칙).
3. **화력 일관성**: `Σ(members.damage_dealt) + Σ(ai.damage_dealt) >= PARTY_DAMAGE_MIN_RATIO(0.8) x Σ(서버가 받아들인 처치의 실효 HP)` 이고, 각 사람 멤버의 `damage_dealt <= 그 멤버 attackCap x 경과 시간 x ...`(3단계 화력 상한식, 멤버 하나 몫). 처치는 있는데 딜이 없거나, 한 사람이 불가능한 딜을 했다고 보고하면 무효.
4. 각 멤버 `revives_used <= 난이도 revives`.
5. `members[].character_id`가 판의 `playing`/`done` 멤버 집합에 속한다(외부 사람 보고 불가).

### 8.3 일치(consistent)의 정의
멤버 보고 `M`과 다른 보고 `X`(방장 보고 `H` 또는 다른 멤버 보고)가 일치한다:
1. 결과가 같다.
2. 경과 시간 차 `<= max(PARTY_ELAPSED_TOLERANCE_MS(5000), 5%)`. 멤버마다 로딩·연출 시간이 달라 플레이 시계가 조금 다르다.
3. 방별 처치 수: `X`가 `H`면 `H`의 방 합계와 `M`의 서버 기록 `room_kills`의 방 합계 차가 `ceil(0.2 x 방 총 마릿수)` 이내. `X`가 다른 멤버면 두 사람의 서버 기록끼리 같은 식.

### 8.4 판정표 (`settle`이 멤버 i에 대해 한 번에 계산하는 순수 함수 `reconcile`)
"증인" = i를 뺀, 이탈(`left`)·불참·탈락이 아닌 다른 사람 멤버(보고 전이라도 증인으로 센다). "대기 마감" = `first_report_at + PARTY_RESULT_WAIT_SECONDS(90)`. 90초는 연결 끊김 복귀 유예(60초)보다 길어야 끊긴 멤버가 증인에서 빠질 시간이 있다.

| 상황 | 판정 |
|---|---|
| 증인이 없다(사람 1명, 나머지가 이탈·불참) | **솔로 수준**: 자기 검증(7.3의 3)만 통과하면 정산 |
| i가 방장이 아니고, `H` 유효, `M_i`와 일치 | 정산 |
| i가 방장이 아니고, `H` 유효, 불일치, 방장이 아닌 다른 멤버 j가 보고했고 `M_j`가 `M_i`와 일치 | i는 정산. 방장 행은 정산 때 `held(HOST_OUTLIER)` + `anomaly_log(party_host, 3)` |
| i가 방장이 아니고, `H` 유효, 불일치, 뒷받침하는 다른 멤버 없음 | i `held(MISMATCH)`, 방장 `held(MISMATCH)`, 둘 다 `anomaly_log(party_result, 2)` |
| i가 방장이 아니고, `H` 없음 또는 무효, 대기 마감 전 | `pending` |
| 위 상황에서 대기 마감 후, i와 일치하는 다른 멤버 보고 있음 | 정산 |
| 위 상황에서 대기 마감 후, 뒷받침 없음 | `held(NO_HOST_REPORT)` |
| i가 방장 | 방장이 아닌 증인 j의 보고 `M_j`가 `H`(=i의 관찰)와 일치하면 정산. 증인이 보고하지 않았으면 대기 마감까지 `pending`, 마감 후에도 없으면 `held(NO_WITNESS)`. 증인이 전부 이탈하면 솔로 수준 |

정산 값(랭크 점수) 합성: 시간은 3단계 규칙(`max(주장, 서버 경과 - 오버헤드)`), 처치는 i의 서버 기록, `hits_taken`·`revives_used`는 `max(M_i, H의 i 항목)`(둘 중 나쁜 값, 랭크를 올리려 줄여 말하는 것을 막는다), `max_combo`는 `min(M_i, H의 i 항목)`(둘 중 낮은 값). `H`가 무효면 `M_i` 값을 쓴다. 랭크로 얻는 이득은 클리어 경험치의 랭크 보너스(최대 +50%)뿐이고 카드는 랭크와 무관하다(3단계 9.5의 남는 위험).

### 8.5 이 대조가 막는 것과 못 막는 것
- 막는다: 방장이 혼자 "클리어"를 보고해 다른 멤버에게 보상을 주는 것(멤버는 자기 보고가 필요), 멤버가 방장과 다른 결과·시간으로 보상을 받는 것, 한 사람이 처치·딜을 부풀려도 다른 사람의 서버 기록·방장 관찰과 어긋나는 것, 방장이 멤버의 피격·부활을 줄여 랭크를 올려 주는 것(나쁜 값을 쓴다).
- 못 막는다(수용): **같은 사람이 계정 두 개로 방장과 멤버를 같이 보고**하는 공모. 각 계정은 여전히 솔로 수준의 서버 검증(처치 구조, 속도, 화력, 시간 하한)을 통과해야 하므로 얻는 것은 솔로로 얻을 수 있는 범위를 넘지 못한다. 계정당 입장 횟수·일일·주간 보상 제한도 그대로 적용된다.

## 9. 파티 보상 분배

| 항목 | 규칙 | 근거 |
|---|---|---|
| 처치 경험치 | **전원이 각자 전부** 받는다(몬스터 경험치 x 레벨 보정, 나누지 않는다). 처치는 마지막 일격과 무관하게 판의 모든 처치를 각자 보고한다 | C# `PartyManager.OnEnemyKilled`가 처치마다 로컬 `Progression.AddXp`를 한 번 한다. 인원이 늘면 몬스터 HP가 `partyHpScale`로 늘어 시간으로 균형을 맞춘다 |
| 드롭(골드·재료·장비) | **멤버마다 따로 굴린다**(`drops`는 캐릭터 소유, 3단계와 같다). 남이 주울 수 없고 쟁탈이 없다 | 참고 게임 방식 개인 드롭. 서버가 굴리므로 클라이언트 `Pickup`은 각자 자기 것만 본다 |
| 클리어 경험치 | 멤버마다 같은 식(`clearXp x 난이도 rewardMul x xpMul x (1 + 랭크 보너스)`), 랭크는 멤버별 | 3단계와 같다 |
| 보상 카드 | 멤버마다 자기 직업으로 4장을 굴려 자기 한 장을 고른다. 다른 멤버의 카드는 공개 연출뿐 지급 없음 | C# `TakeCard`는 본인 한 장만 가방에 넣는다 |
| 최종 레이드 열쇠 소모, 중간 레이드 열쇠 획득 | 멤버마다 자기 가방에서(10.4) | C# `PayRaidKeys` |
| AI 용병 | 보상 없음. 몬스터 HP 배율과 화력 상한에만 반영 | |
| 도중 이탈·끊김 | 이탈하면 그 시점까지의 처치 경험치·드롭은 유지, 클리어 경험치·카드·레이드 보상 없음. 끊겼다가 60초 안에 돌아오면 정상 | PLAN_ONLINE §2.2 |
| 입장 횟수 | 요일 던전은 `begin`에서 멤버마다 1회 소모(불참·탈락자는 소모 없음) | 3단계 9.3 |
| 레벨 격차 | 입장 자격에서 `PARTY_MIN_LEVEL_SLACK`로 하한 | 파워 레벨링 방지(5.2) |

## 10. 레이드

### 10.1 구분
`dungeons.json`의 `isRaid`, `raidTier`(`Mid`/`Final`), `openDays`, `unlockQuest`, `keyCost`(최종), 중간 레이드의 열쇠 범위 `keyMin..keyMax`(내보내기 추가, mapping 4절), 레이드별 난이도 수치 `raidNumbers`. 레이드는 난이도가 하나(`difficulty=0`)이고 요일 던전의 일일 입장 횟수에 세지 않는다(`counts_entry=false`).

### 10.2 해금(`RAID_LOCKED`)
C# `DungeonDirector.RaidLockReason`은 해금 퀘스트를 "받았거나 완료"했으면 연다. 서버는 퀘스트 진행 상태를 믿지 않으므로(3단계 5절): `quest_claims`에 해금 퀘스트가 있거나, **해금 퀘스트의 선행(`requires`) 퀘스트가 전부 `quest_claims`에 있고** 클라이언트 저장 상태(`character_state.quests`)에 그 퀘스트가 수락 이상으로 있으면 연다(선행 사슬을 서버 기록으로 확인하는 것이 핵심). 실패는 `RAID_LOCKED`, 클라이언트가 UI로 막은 것을 우회한 경우만 `anomaly_log(raid_enter, 2)`.

### 10.3 보상 잠금(`reward_locked`) 판정 (판을 만들 때 멤버별로 계산)
| `lock_reason` | 조건 |
|---|---|
| `ALREADY_CLAIMED` | 이번 기간의 `raid_claims`가 이미 있다. 기간은 **중간 레이드: 일일(`dailyStartAt`), 최종 레이드: 주간(`weeklyStartAt`)**(`resetBoundaries` 한 함수) |
| `TOO_FEW_HUMANS` | 판을 만들 때 사람 수가 `RAID_REWARD_MIN_HUMANS`(기본 2) 미만(솔로 AI 레이드는 연습, 결정 대기 2). **정산 때도 다시 본다**: 이탈하지 않은 사람(`left`·`no_show`·`dropped`가 아닌 멤버)이 최소 인원 미만이면 잠근다(친구가 들어왔다가 바로 나가 대조를 피하는 것을 막는다) |
| `KEYS_MISSING` | **정산 때** 최종 레이드의 봉인 열쇠가 `keyCost`보다 적다(입장 때는 `checkEntry`가 막지만 판 도중 열쇠를 팔면 정산에서 잠긴다) |

잠긴 판은 "연습 입장"이다: 클리어 경험치·카드·열쇠·`raid_claims`가 없다. 처치 경험치·드롭은 `RAID_PRACTICE_PAYS_KILLS`에 따른다(7.2). 클리어 기록(`cleared`, 해금·최고 랭크)은 남는다(C# `RecordClear`는 잠금과 무관).

### 10.4 정산 (레이드 클리어, `settle`의 마지막 단계)
보상 가능(`reward_locked=false`)인 클리어를 3단계 9.5 5~7의 결과로 확정한 뒤, 같은 트랜잭션에서:
1. `raid_claims` INSERT(PK = `(캐릭터, 던전, 기간 시작)`). 충돌하면(동시에 다른 판이 먼저 받음) `reward_locked=true, ALREADY_CLAIMED`로 되돌리고 클리어 경험치·카드를 주지 않는다. **이 INSERT를 경험치·카드보다 먼저 한다**(DB가 "기간당 한 번"을 막는다).
2. 최종 레이드: 가방의 봉인 열쇠(`key_seal`) >= `keyCost`이면 `keyCost`만큼 `item_ledger('raid_key_cost')`로 소모(C#은 `min(cost, have)`를 쓰지만 서버는 부족하면 보상 자체를 잠근다 - 입장 때 부족하면 못 들어오므로 정직한 흐름에서는 같다).
3. 중간 레이드: `rng.int(keyMin, keyMax + 1)`개를 `item_ledger('raid_key')`로 지급(`ref` = 판 uuid).
- `GET /raids`가 "이번 주 n/3"을 위한 `clears_this_period`(중간 레이드: 같은 주간 구간의 `raid_claims` 행 수)를 준다.

### 10.5 D6 GET /raids
응답 `data`:
```
{ "reset": { "daily_start_at": ISO, "next_daily_at": ISO, "weekly_start_at": ISO, "next_weekly_at": ISO },
  "raids": [ { "id": "raid_...", "tier": "mid" | "final", "unlocked": true, "open_today": true,
               "reward_available": true, "period_kind": "daily" | "weekly", "clears_this_period": 1,
               "key": { "item": "key_seal", "have": 120, "cost": 100, "drop_min": 0, "drop_max": 0 },
               "min_humans_for_reward": 2 } ] }
```
`reward_available`은 `open_today && 이번 기간 청구 없음 && (최종이면 열쇠 >= 비용)`. 열쇠 숫자는 서버 데이터에서 읽는다. 클라이언트는 입장 창과 `DungeonDirector.CannotEnterReason`의 레이드 분기를 이 응답으로 대체한다.

## 11. 빈자리 AI 용병: 권장안

**권장: 허용한다. 던전 안에서만, 시작할 때 고정, 보상은 사람과 같다. 단 레이드 보상은 사람이 2명 이상일 때만(결정 대기 2).**

근거:
1. **사용자 의도(U7)와 PLAN_ONLINE §3.3이 이미 이것이다.** 사람이 모자란 자리를 AI가 채운다.
2. **서버 비용이 0이다.** AI는 방장 PC가 계산하고 서버는 `ai_count` 숫자 하나만 안다. 용병은 계정·인벤토리·골드가 없다(레벨은 방장 레벨, 장비는 레벨 구간 고정, `MercenaryDatabase`). 그래서 경제에 새 경로가 생기지 않는다.
3. **경제적으로 중립이다.** 파티 인원은 몬스터 HP를 `partyHpScale`(2명 1.7배, 3명 2.4배, 4명 3.0배)로 늘리고, 용병은 같은 레벨 플레이어 딜의 `damageScale`(0.65)이다. AI를 많이 데려가면 HP가 늘고 AI 딜이 늘어 클리어 시간이 비슷하게 유지된다. 그래서 서버는 `ai_count`를 판 시작 때 서버가 기록한 값으로 고정하고(클라이언트가 중간에 바꿀 수 없다) `party_size = 사람 + ai_count`로 몬스터 HP와 화력 상한을 같은 값에서 계산한다. `ai_count`를 낮춰 말하면 HP 배율이 낮아지고 화력 상한도 낮아져 얻는 것이 없고, 높여 말하면 둘 다 높아진다. 어느 쪽도 이득이 없다.
4. **3단계의 제한을 푼다.** 3단계는 솔로 입장에서 용병을 막았다(`party_size=1`). 4단계에서 `POST /dungeon-runs`의 `ai_count`와 파티 `start`의 `ai_count`로 푼다.
5. **일부러 둔 제한**:
   - 서버가 `ai_count`를 기록하는 곳은 던전·레이드 입장뿐이다. 필드의 용병은 3단계 그대로이고(서버는 모른다) 필드 처치 보고의 속도·공급 한도가 그대로 적용되므로 용병이 있어도 얻는 이득이 없다.
   - 던전 안에서 AI 인원을 바꾸지 않는다(PLAN_ONLINE §3.3). 중간에 사람이 나가면 그 자리를 AI가 이어받는 것은 클라이언트 연출이고 서버의 `ai_count`(판 시작 값)는 그대로다. 화력 상한이 처음부터 그 사람의 몫을 포함했으므로 서버 판정은 일관된다.
   - 레이드는 AI만으로는 보상을 받지 못하게 한다(결정 대기 2): 사람 1명 + AI 3명은 방장 보고가 곧 본인 보고라 대조가 없고, 레이드 보상(고급 장비, 열쇠)은 경매장(6단계)에서 거래되는 값이 크다.
6. **스토리 동료(카엘)**: `StoryCompanions.Refresh`로 던전·레이드에 따라오는 스토리 동료는 AI 1명으로 센다(`ai_count`에 포함, 사람+AI 합 4 이하). 방장 PC에서만 소환한다(mapping 5절).
7. **오프라인 `PartyRoster`는 쓰지 않는다.** 온라인 판의 AI 구성(어떤 용병)은 방장이 판 시작 때 정하고 P2P로 알리는 연출 정보다. 서버는 용병 id를 저장하지 않는다.

## 12. 전송 계층 규약 (ITransport, Steam P2P)

서버가 하는 일은 명단·토큰·인계 판정이고, 바이트 전송은 클라이언트 몫이다. 서버 설계가 의존하는 규약만 적는다(구현은 mapping 5절).
- **토폴로지**: 별 모양(star). 방장이 중앙, 멤버는 방장하고만 연결한다(멤버끼리 직접 연결하지 않는다). 방장이 입력 틱 20Hz를 모아 스냅샷 10Hz를 멤버에게 보낸다(PLAN_ONLINE §3.2).
- **Steam**: `ISteamNetworkingSockets`의 P2P 연결(방장 `CreateListenSocketP2P`, 멤버 `ConnectP2P`)을 쓴다. 이유: 연결 상태 콜백(끊김, 문제 감지)이 있어 호스트 인계의 "연결 끊김 3초" 판단이 쉽고, 연결 객체가 상대 SteamID를 알려줘 명단 대조가 쉽고, 호스트 인계는 새 리슨 소켓 + 재연결로 풀린다. SDR 릴레이는 `InitRelayNetworkAccess`로 자동 사용(PLAN_ONLINE O3의 무료 중계). 로비(`ISteamMatchmaking`)는 쓰지 않는다(서버가 방장 SteamID를 준다). 친구 초대는 5단계.
- **연결 수락**: 방장은 연결 요청이 오면 연결의 SteamID가 서버 명단(R2의 `members[].steam_id`)에 있는지 확인하고 수락/거절한다. 연결 뒤 첫 신뢰 메시지의 `entry_token`이 틀리면 즉시 끊는다.
- **채널**: 기존 `NetChannel`(Input 1, Chat 2, Presence 3)에 `Snapshot 4`(방장 → 멤버, 비신뢰), `Event 5`(스킬·피격·사망·방 이동, 신뢰), `Control 6`(핸드셰이크, 방 전환, 인계 알림, 신뢰)를 더한다.
- **끊김 판단**: 3초 무신호(`UdpTransport.TimeoutSeconds`와 같은 값)이면 그 피어가 끊긴 것으로 보고 그 멤버를 AI가 대신한다(`NetPresence`의 "연결 끊김 - AI가 대신 싸우는 중" 토스트 동작과 같다). 서버 하트비트의 `disconnected`(12초)는 별개의 서버 판단이다.
- **개발 모드(`PARTY_TRANSPORT=dev`)**: `UdpTransport`(127.0.0.1, 포트 47777/47778, 2피어 고정)로 같은 PC 창 2개를 연결한다. 서버 API(파티, 판, 정산)는 Steam과 똑같이 쓰고 `steam_id`만 `null`이다. 4명 시험은 `LoopbackTransport`를 별 모양 N피어로 확장해 진행한다(mapping 5절).

## 13. 방장 권한 약점 보완 (PLAN_SERVER §5) 대응

| 방장이 할 수 있는 나쁜 짓 | 방어 |
|---|---|
| 몬스터를 즉사시켜 멤버에게 경험치·드롭을 몰아주기 | 보상은 각 멤버의 서버 판정이다. 처치 속도, 방 구성, 파티 화력 상한(`power_cap`)이 모든 멤버의 처치 보고에 적용된다. 어긋나면 거절 + 기록. 방장 PC의 몬스터 HP를 바꿔도 서버 화력 상한을 넘지 못한다 |
| 가짜 클리어 결과를 보고해 멤버를 보상받게 하기 | 멤버는 자기 결과를 직접 보고해야 한다. 방장 단독 보고로는 지급이 없다(8절) |
| 멤버의 결과를 "실패"로 보고해 보상 방해 | 멤버가 보고한 결과가 다르면 `MISMATCH`이고, 다른 멤버가 같은 보고를 하면 방장이 이상치로 처리된다(8.4). 보상 지급은 보류(`held`)지 소멸이 아니다 |
| 멤버의 피격·부활을 줄여 랭크 부풀리기 | 나쁜 값을 쓴다(`max(멤버, 방장)`) |
| 입장 거절·강제 종료로 멤버 입장 횟수 소모시키기 | 입장 횟수는 `begin`에서만 소모한다(입장한 사람만). `gathering`에서 취소·불참하면 소모 없음 |
| 방장 PC 크래시·이탈 | 하트비트 + 인계(6.7), 세대 번호로 옛 방장의 보고 거절 |
| 방장이 호스트 인계를 막고 멤버를 가두기 | 서버 판정은 방장과 무관하다. 갇힌 멤버는 R8로 이탈하고(그때까지의 처치 보상 유지) 방장이 서버 하트비트를 끊으면 12초 뒤 인계가 가능하다 |
| 서버 요청 변조 | 3단계 원칙 그대로: 금액·확률·시간을 받지 않고 `request_id`로 멱등, 캐릭터 행 잠금 |

## 14. 4단계 테이블 요약 (`server/migrations/0005_party.sql`)

| 표 | 역할 | 주요 인덱스와 이유 |
|---|---|---|
| `parties` | 로비·모집 글 | `parties_board` 게시판 목록(필터 + 최신순, 부분 인덱스 `listed AND forming`), `parties_leader` 방장 권한, `parties_idle` 방치 정리 |
| `party_members` | 소속(이력 포함) | `party_members_one_active` 한 캐릭터 한 파티(DB 수준), `party_members_party` 멤버 목록, `party_members_notice` 해산·강퇴 알림 |
| `party_applications` | 참가 신청 | `party_applications_pending_uq` 중복 신청, `_party` 방장 폴링, `_char` 신청자 폴링·동시 3건 검사 |
| `party_runs` | 한 판 | `party_runs_one_active` 한 파티 한 판, `_active_idx` 방치 정리 |
| `party_run_members` | 판 멤버(토큰·하트비트·인계 근거) | PK `(판, 캐릭터)` 하트비트 갱신, `_one_active` 한 캐릭터 한 판(솔로 입장도 막음) |
| `party_run_host_reports` | 방장 보고 | `UNIQUE(판, 세대)` 세대당 한 번. 추가만 하는 표(트리거) |
| `raid_claims` | 레이드 일일·주간 보상 | PK `(캐릭터, 던전, 기간 시작)` 기간당 한 번을 DB가 보장 |
| `dungeon_runs`(확장) | 멤버별 한 줄 | `dungeon_runs_party_idx` 한 판의 모든 멤버 행(대조), `dungeon_runs_char_day`를 `counts_entry`만 세도록 부분 인덱스로 교체 |

- 매칭 대기열 표가 없다(5.1). PLAN_ONLINE §5.2의 `match_tickets`를 만들지 않는다.
- 3단계 제약 변경 2건: `dungeon_runs.state`에 `reported` 추가, `dungeon_runs_cleared_chk`를 "잠기지 않았으면(`reward_locked`가 아니면) 카드 필수"로 완화(연습 클리어는 카드가 없다). 3단계 코드의 `countEntries`는 `counts_entry` 조건을 더해야 하고(레이드 제외), `abandonRun`이 닫는 행에 대응하는 `party_run_members`가 있으면 같은 트랜잭션에서 상태를 `done`으로 바꾼다.
- 원장 변경: `item_ledger.reason`에 `raid_key`(중간 레이드 열쇠 획득), `raid_key_cost`(최종 레이드 열쇠 소모) 추가. 골드·경험치 원장은 3단계 `dungeon_card`, `dungeon_clear`를 그대로 쓴다. 이상 기록 `kind`: `party_result`(대조 불일치), `party_host`(무효 방장 보고·이상치), `raid_enter`(해금 우회 시도).
- 판 종료 판정: 멤버의 `dungeon_runs`가 `playing`을 벗어나는 모든 경로(결과 보고, 이탈, 방치 정리, 끊김 60초 초과)의 트랜잭션 끝에서 같은 판의 다른 행이 모두 `playing`이 아니면 `party_runs.state='ended'`(+`run_key=NULL`), `parties.state='forming'`으로 되돌린다(`dungeon_runs_party_idx`로 한 판의 행을 읽는다). 전이표는 mapping 3절.
- 보관: 판 종료 후 `party_run_members`·`party_runs`·`parties`(해산)·`party_applications`는 30일, `party_run_host_reports`는 `dungeon_runs`와 같이 보관(분쟁 확인용). 정리는 7단계 스크립트.

## 15. 서버 정책 상수 (환경변수, 시작값)

| 이름 | 시작값 | 의미 |
|---|---|---|
| `PARTY_LISTING_MINUTES` | 10 | 모집 글 게시 시간(PLAN_ONLINE §2.1) |
| `PARTY_APPLY_SECONDS` | 30 | 신청 무응답 만료(PLAN_ONLINE §2.1) |
| `PARTY_IDLE_MINUTES` | 30 | 방치 파티 해산 |
| `MATCH_QUEUE_SECONDS` | 60 | 대기 후 AI로 출발(`PartyFinderRules.QueueSeconds`) |
| `MATCH_POWER_RATIO` | 0.3 | 매칭 전투력 폭(PLAN_ONLINE §5.1 ±30%) |
| `MATCH_START_SECONDS` | 60 | 매칭 파티 출발 기한 |
| `MATCH_TICKET_STALE_SECONDS` | 15 | 폴링 끊긴 대기 티켓 제거 |
| `PARTY_GATHER_SECONDS` | 90 | 입장 마감 |
| `PARTY_MIN_LEVEL_SLACK` | 5 | 권장 레벨보다 낮아도 입장 가능한 폭 |
| `HOST_STALE_SECONDS` | 12 | 하트비트가 이보다 오래되면 끊김 |
| `PARTY_REJOIN_SECONDS` | 60 | 끊긴 뒤 복귀 유예 |
| `PARTY_RESULT_WAIT_SECONDS` | 90 | 결과 대조 대기 |
| `PARTY_ELAPSED_TOLERANCE_MS` | 5000 | 경과 시간 일치 허용(와 5% 중 큰 값) |
| `PARTY_KILL_SLACK_RATIO` | 0.2 | 방별 처치 수 일치 허용 비율 |
| `PARTY_POWER_SLACK` | 1.15 | 파티 화력 상한 여유 |
| `PARTY_DAMAGE_MIN_RATIO` | 0.8 | 방장 보고의 딜 합이 처치 HP 합의 이 비율 이상 |
| `RAID_REWARD_MIN_HUMANS` | 2 | 레이드 보상 최소 사람 수(결정 대기 2) |
| `RAID_PRACTICE_PAYS_KILLS` | false | 연습판 처치 경험치·드롭 지급(결정 대기 3) |
| `STEAM_AUTH_MODE`, `STEAM_APP_ID`, `STEAM_WEB_API_KEY`, `STEAM_IDENTITY`, `PARTY_TRANSPORT` | 3.1 | Steam 인증·전송 설정 |

게임 값은 `MERC_DAMAGE_SCALE`처럼 정책으로 두지 않고 `server/data/dungeons.json`(`mercenary.damageScale`)에서 읽는다.

## 16. 결정 대기

| # | 항목 | 선택지 | 권장 |
|---|---|---|---|
| 1 | 빈자리 AI 용병 허용 | (a) 던전·레이드에서 허용, 시작 시 고정, 보상은 사람과 같음(11절) (b) 허용하지 않고 사람만 | (a). 서버 비용 0, 경제에 새 경로 없음, 사용자 의도(U7)와 같음 |
| 2 | 레이드 보상에 필요한 최소 사람 수 | (a) 2명(사람 1명 + AI는 연습 입장) (b) 1명(AI 레이드도 보상) | (a). 사람 1명은 방장 보고가 본인 보고라 대조가 없고 레이드 보상은 경매 거래 가치가 크다. (b)로 바꾸려면 `RAID_REWARD_MIN_HUMANS=1`만 바꾼다 |
| 3 | 연습판(레이드 보상 잠금)의 처치 경험치·드롭 | (a) 주지 않는다(무한 입장 파밍 방지) (b) 준다(C# 오프라인과 같음) | (a). 레이드는 입장 횟수 제한이 없어 연습 반복이 곧 파밍이다. 게임 동작과 달라지는 점은 오프라인 레이드가 연습에서도 처치 보상을 준다는 것 |
| 4 | 최소 전투력·매칭 폭의 전투력 값 | (a) 서버가 레벨·장비로 계산한 `power_estimate`를 온라인 파티 찾기에 표시(패시브 제외라 카드 값과 다름) (b) 클라이언트가 보낸 전투력을 신뢰(참가 거부 우회는 가능, 보상 영향 없음) (c) 전투력 대신 레벨만 | (a). 값이 달라지는 대신 신뢰 문제가 없다. 나중에 패시브 효과를 내보내면 카드 값과 가까워진다 |
| 5 | 입장 레벨 하한(`PARTY_MIN_LEVEL_SLACK`) | (a) 권장 레벨 - 5(파워 레벨링 방지) (b) 제한 없음 | (a). 낮은 레벨이 고난이도에 끼어 큰 경험치를 받는 것을 막는다. 값은 환경변수 |
| 6 | Steam Web API 키 발급과 480 시험 | 키를 누가 언제 발급할지(Steam 계정 1개 필요, 발급 조건은 Steam 정책을 따른다) | 서버 코드는 `mock`으로 먼저 끝내고, Steam P2P 시험을 시작할 때 키를 발급해 `STEAM_APP_ID=480`으로 3.2의 확인 항목을 실측한다 |
