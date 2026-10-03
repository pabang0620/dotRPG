# 서버 1~2단계 API 명세

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §0, §3, §6, §10. 스키마: `server/schema.sql`, `server/migrations/0001_init.sql`. 게임 값 대응: [phase1_2_mapping.md](phase1_2_mapping.md).
이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만든다.

## 0. 공통 규칙

### 0.1 응답 형식
- 성공 `{ "success": true, "message": "", "data": {...}, "meta"?: {...} }`
- 실패 `{ "success": false, "message": "한국어 설명", "errors": { "code": "ERROR_CODE", ... } }`
- `errors.code`는 클라이언트가 분기하는 값이고 `message`는 화면에 띄우는 한국어 문장이다. 상태 코드와 code 목록은 엔드포인트마다 적는다.
- 검증 실패 `400 VALIDATION`: `errors.fields = [{ "path": "name", "message": "..." }]`
- 응답에 내부 키(bigint id)를 싣지 않는다. 외부 id는 전부 uuid다.

### 0.2 인증
- `Authorization: Bearer <액세스 토큰>`. 액세스 토큰은 JWT(HS256 또는 RS256은 구현자가 정함), 수명 15분, claims는 `sub`(계정 uuid), `iat`, `exp`만 쓴다.
- 인증 필요 엔드포인트는 토큰이 없거나 만료면 `401 UNAUTHORIZED`(`errors.code`: `TOKEN_MISSING` | `TOKEN_EXPIRED` | `TOKEN_INVALID`). 만료 코드를 따로 두는 이유는 클라이언트가 refresh를 시도할지 정하기 위해서다.
- 매 요청마다 계정이 정지(`banned_until > now()`)거나 삭제됐는지 확인한다: `403 ACCOUNT_BANNED`(`errors.banned_until` 포함) / `401 TOKEN_INVALID`. DB 조회 1회 비용은 100명 미만 규모에서 문제없다.
- 캐릭터 소유 검사: `/characters/{uuid}`는 토큰의 계정이 소유자가 아니거나 삭제된 캐릭터면 **둘 다 `404 CHARACTER_NOT_FOUND`** 로 같게 응답한다(존재 여부를 알려주지 않는다, IDOR 방지).

### 0.3 버전 헤더와 426
모든 요청에 두 헤더를 싣는다.

| 헤더 | 형식 | 예 |
|---|---|---|
| `X-Client-Version` | `major.minor.patch` | `0.3.1` |
| `X-Data-Version` | 게임 데이터 버전 문자열(내보내기 도구가 만든 해시 앞 16자 hex) | `9f2c41ab07d3e6c1` |

검사 대상과 순서:

| 엔드포인트 | 클라이언트 버전 검사 | 데이터 버전 검사 |
|---|---|---|
| `/health`, `/meta` | 안 함(오래된 클라이언트도 `/meta`로 업데이트 필요를 알 수 있어야 한다) | 안 함 |
| `/auth/logout` | 안 함(언제든 로그아웃할 수 있어야 한다) | 안 함 |
| `/auth/*` 나머지, `/me` | 함 | 안 함 |
| `/characters*` | 함 | **함** (캐릭터 생성·상태 검증이 게임 데이터에 의존) |

- 헤더가 없거나 형식이 틀리면 `400 VALIDATION` (`errors.code` = `CLIENT_VERSION_MISSING` | `DATA_VERSION_MISSING`).
- `X-Client-Version` < 서버 `MIN_CLIENT_VERSION`(semver 비교) 이면 `426 CLIENT_OUTDATED`:
  `errors = { code, client_version, min_client_version }`, message "게임을 업데이트해 주세요."
- `X-Data-Version` != 서버 `data_version` 이면 `426 DATA_OUTDATED`: `errors = { code, client_data_version, data_version }`.
  해시는 순서가 없어 "오래됨"을 판단할 수 없으므로 불일치면 모두 426이다. 클라이언트는 같은 문구("업데이트 필요")를 보인다.
- 검사는 라우트 핸들러 앞의 미들웨어 하나에서 하고, 클라이언트 버전 → 데이터 버전 순으로 본다.
- 서버 설정(환경변수): `MIN_CLIENT_VERSION`. `data_version`은 서버가 시작할 때 `server/data/data_version.json`에서 읽는다.

### 0.4 멱등성 (`request_id`)
- 지급이 일어나는 `POST /characters`는 본문에 `request_id`(UUID v4, 클라이언트가 한 번의 사용자 행동마다 새로 만든다)를 받는다.
- 서버는 `request_log`의 `(account_id, request_id)`로 처리한다.
  - 처음: 처리 후 응답을 `request_log`에 같은 트랜잭션으로 저장한다.
  - 같은 id + 같은 본문(정규화 해시 일치): 저장된 응답을 같은 상태 코드로 그대로 돌려주고 헤더 `Idempotent-Replay: true`를 붙인다. 그 사이 이름이 선점되었어도 처음 응답을 준다.
  - 같은 id + 다른 본문: `422 IDEMPOTENCY_MISMATCH`.
  - 처음 요청이 4xx로 실패했다면 `request_log`를 저장하지 않는다(트랜잭션 롤백). 같은 id로 고쳐서 다시 보내도 된다.
- 보관 기간 7일(환경변수 `REQUEST_LOG_TTL_DAYS`). 정리 작업이 `created_at` 인덱스로 지운다.
- 동시에 같은 `request_id`가 두 번 들어오면 두 번째는 `UNIQUE` 충돌(23505)을 잡아 첫 요청 완료를 기다린 뒤 저장된 응답을 돌려준다(재조회).

### 0.5 날짜 경계
이 단계의 API는 일일·주간 경계를 판정하지 않는다. 다만 `/meta`가 그 값을 내보내므로 함수 하나로 정한다.
- `resetBoundaries(nowUtc)`: 서버 시각을 KST(UTC+9)로 바꿔 `nextDailyAt`(다음 06:00 KST)과 `nextWeeklyAt`(다음 목요일 06:00 KST)을 UTC ISO 문자열로 돌려준다. 서버 어디서든 이 함수만 쓴다. 클라이언트 시계는 쓰지 않는다. 경계 규칙(06:00, 목요일)은 PLAN_SERVER §5와 같고, 경계 직전·직후 시각을 단위 테스트로 고정한다.

### 0.6 속도 제한 (메모리 구현, 나중에 Redis로 교체 가능한 인터페이스 뒤에 둔다)
초과하면 `429 RATE_LIMITED` + `Retry-After` 헤더. 아래는 기본값이고 환경변수로 바꾼다.

| 대상 | 한도 |
|---|---|
| `POST /auth/dev/register` | IP당 5회 / 1시간 |
| `POST /auth/dev/login` | IP당 20회 / 1분, 아이디당 실패 5회 / 15분(성공하면 초기화) |
| `POST /auth/refresh` | 계정당 10회 / 1분 |
| `POST /characters` | 계정당 10회 / 1분 |
| `PUT /characters/{uuid}/state` | 캐릭터당 1회 / 1초 (오토세이브는 그보다 느리게 보낸다) |
| 그 외 | IP당 120회 / 1분 |

- 요청 본문 상한 64KB(`413 PAYLOAD_TOO_LARGE`).

## 1. 엔드포인트 요약 (12개)

| # | 메서드 | 경로 | 인증 | 단계 |
|---|---|---|---|---|
| 1 | GET | `/health` | 없음 | 1 |
| 2 | GET | `/meta` | 없음 | 1 |
| 3 | POST | `/auth/dev/register` | 없음 | 1 |
| 4 | POST | `/auth/dev/login` | 없음 | 1 |
| 5 | POST | `/auth/refresh` | 없음(본문의 갱신 토큰) | 1 |
| 6 | POST | `/auth/logout` | 없음(본문의 갱신 토큰) | 1 |
| 7 | GET | `/me` | 액세스 토큰 | 1 |
| 8 | GET | `/characters` | 액세스 토큰 | 2 |
| 9 | POST | `/characters` | 액세스 토큰 | 2 |
| 10 | GET | `/characters/{uuid}` | 액세스 토큰 | 2 |
| 11 | DELETE | `/characters/{uuid}` | 액세스 토큰 | 2 |
| 12 | PUT | `/characters/{uuid}/state` | 액세스 토큰 | 2 |

## 2. 1단계

### 2.1 GET /health
- 응답 200 `data`: `{ "status": "ok", "db": "ok", "uptime_sec": 1234 }`
- DB에 `SELECT 1`이 실패하면 `503` `data`: `{ "status": "degraded", "db": "down" }`. 버전·설정 값은 싣지 않는다.
- 로드밸런서·배포 확인용이라 로그를 남기지 않는다(요청 로그에서 제외).

### 2.2 GET /meta
- 응답 200 `data`:
  - `min_client_version`: string
  - `data_version`: string (서버가 쓰는 게임 데이터 버전)
  - `server_time`: UTC ISO 문자열 (클라이언트는 이 값으로 표시만 하고 판정에 쓰지 않는다)
  - `reset`: `{ "next_daily_at": ISO, "next_weekly_at": ISO }` (0.5 함수)
  - `auth`: `{ "dev_login_enabled": bool }` (클라이언트가 개발용 로그인 화면을 보일지 정한다)
  - `character`: `{ "max_per_account": 4, "name_min": 2, "name_max": 8 }` (서버 상수가 정본이라 클라이언트가 복사하지 않고 이 값을 쓴다)
- 캐시: `Cache-Control: no-store`.

### 2.3 POST /auth/dev/register
- 운영 환경 변수 `AUTH_DEV_ENABLED=false`면 이 경로와 login은 **라우트를 등록하지 않아 404**다(403이 아니라 존재 자체를 숨긴다). 기본값: `NODE_ENV=production`이면 false, 그 밖에는 true.
- 요청: `{ "login_id": string, "password": string }`
  - `login_id`: 소문자 영문·숫자·`_`, 4~20자. 서버가 소문자로 바꿔 저장·비교한다(대문자가 오면 소문자로 정규화해 받아준다).
  - `password`: 8~64자(문자 종류 제한 없음). 로그에 절대 남기지 않는다.
- 처리: 비밀번호를 argon2id로 해시 → `accounts` + `auth_identities(provider='dev', subject=login_id, secret_hash)` 한 트랜잭션 → 토큰 쌍 발급(새 `family_id`).
- 응답 `201` `data`: `{ "account": { "id": uuid, "created_at": ISO }, "access_token": string, "access_expires_in": 900, "refresh_token": string, "refresh_expires_at": ISO }`
- 에러: `400 VALIDATION`, `409 LOGIN_ID_TAKEN`(`UNIQUE(provider, subject)` 충돌), `404`(꺼진 환경), `426`, `429`.
- 멱등성: 없음(이미 있는 아이디면 409).

### 2.4 POST /auth/dev/login
- 요청: `{ "login_id": string, "password": string }`
- 처리: 아이디 조회 → 비밀번호 검증. **아이디가 없을 때도 더미 해시와 비교해 응답 시간을 같게 한다.** 성공하면 `accounts.last_login_at` 갱신, 새 `family_id`로 토큰 쌍 발급.
- 응답 `200`: register와 같은 `data`(account에 `last_login_at` 포함 가능).
- 에러: `401 INVALID_CREDENTIALS`(아이디 없음·비밀번호 틀림·삭제된 계정을 구분하지 않는다), `403 ACCOUNT_BANNED`, `404`(꺼진 환경), `426`, `429`.
- 정지 계정은 비밀번호가 맞을 때만 `ACCOUNT_BANNED`를 알려준다(아이디 존재 여부 노출 방지).

### 2.5 POST /auth/refresh
- 요청: `{ "refresh_token": string }`
- 토큰 형식: 32바이트 난수의 base64url 문자열. DB에는 SHA-256 hex만 저장한다(`refresh_tokens.token_hash`). 수명 14일(`expires_at`).
- 처리(한 트랜잭션):
  1. `token_hash`로 행을 `FOR UPDATE` 조회. 없으면 `401 REFRESH_INVALID`.
  2. `revoked_at` 또는 `used_at`이 있으면 **재사용 감지**: 같은 `family_id`의 모든 토큰에 `revoked_at=now()`를 넣고 `401 REFRESH_REUSED` (경고 로그).
  3. `expires_at` 지났으면 `401 REFRESH_EXPIRED`.
  4. 계정 정지·삭제 확인(`403 ACCOUNT_BANNED` / `401 REFRESH_INVALID`).
  5. 현재 토큰에 `used_at=now()`, 같은 `family_id`로 새 토큰 행 INSERT, 새 액세스 토큰 발급.
- 응답 `200` `data`: `{ "access_token", "access_expires_in": 900, "refresh_token", "refresh_expires_at" }`
- 클라이언트 규칙(구현 메모): refresh는 한 번에 한 요청만 보내고(직렬화), 새 토큰을 저장한 뒤에 이전 토큰을 버린다. 응답을 못 받고 재시도하면 `REFRESH_REUSED`로 로그아웃될 수 있다. 이 경우 화면은 다시 로그인으로 보낸다(초기 100명 규모에서는 허용, 문제가 되면 짧은 유예 규칙을 나중에 추가한다).
- `last_login_at`은 갱신하지 않는다.

### 2.6 POST /auth/logout
- 요청: `{ "refresh_token": string }` (액세스 토큰은 요구하지 않는다. 만료 상태에서도 로그아웃할 수 있어야 한다)
- 처리: 해시로 조회해 있으면 그 `family_id`의 모든 토큰을 폐기. 없거나 이미 폐기여도 성공으로 본다(멱등).
- 응답 `200` `data`: `{ "logged_out": true }`
- 액세스 토큰은 서버가 폐기할 수 없으므로 최대 15분 더 유효하다(허용). 정지·삭제된 계정은 0.2의 매 요청 검사로 막힌다.

### 2.7 GET /me
- 응답 `200` `data`: `{ "account": { "id": uuid, "created_at": ISO }, "provider": "dev", "login_id": string, "character_count": int, "character_limit": 4 }`
- `character_count`는 삭제되지 않은 캐릭터 수.
- 1단계 끝 기준("클라이언트가 로그인해서 내 계정 정보를 받는다")을 만족하는 엔드포인트다.

## 3. 2단계

### 3.1 공통 형태

캐릭터 요약 `CharacterSummary`:
`{ "id": uuid, "name", "class": "warrior"|"mage", "level": int, "map_id": string, "created_at": ISO }`
- 목록에는 마지막 위치(`pos`)와 `updated_at`도 싣는다: `"pos": {"x","y"} | null`, `"updated_at": ISO`.

캐릭터 상세 `CharacterDetail`:
```
{
  "id", "name", "class", "created_at",
  "level": int, "xp": int, "gold": int,            // 읽기 전용. 2단계에서 바꿀 방법이 없다
  "state": {
    "version": int,
    "map_id": string,
    "pos": { "x": number, "y": number } | null,    // null = 맵 시작 지점
    "facing": int,
    "quests": [ { "id", "status", "step", "counts": [int] } ],
    "story_flags": [string],
    "tracked_quest": string,
    "passives": [string],                            // 시작 노드 제외
    "skill_gems": [ { "slot": int, "supports": [string|null, string|null] } ],
    "updated_at": ISO
  },
  "items": [ { "id": uuid, "item_key": string, "count": int, "location": "bag"|"worn"|..., "slot": int|null } ]
}
```
- `items`의 `id`는 `character_items.uuid`다.

### 3.2 GET /characters
- 응답 `200` `data`: `{ "characters": [CharacterSummary + pos, updated_at], "limit": 4 }`. 삭제되지 않은 것만, `created_at` 오름차순.
- 조회: `characters_account_alive` 인덱스 + `character_state` 조인.

### 3.3 POST /characters
- 요청: `{ "request_id": uuid, "name": string, "class": "warrior" | "mage" }`
- 이름 규칙(서버가 정본, 클라이언트도 같은 규칙으로 미리 검사하되 서버가 최종):
  - 입력을 Unicode NFC로 정규화한 뒤 검사한다. 앞뒤 공백을 자동으로 잘라 주지 않고 규칙 위반으로 거절한다.
  - 길이 2~8자(코드포인트 기준), 문자는 한글 완성형(가-힣)·영문 대소문자·숫자만. 자모 단독(ㄱ, ㅏ)·공백·특수문자·이모지 불가. DB CHECK도 같은 정규식이다.
  - 살아 있는 캐릭터 기준으로 대소문자 무시 중복 불가(`characters_name_alive`). 삭제된 캐릭터의 이름은 바로 다시 쓸 수 있다.
  - 금칙어 필터는 이 단계에 넣지 않는다(결정 대기, 아래 5절).
- 처리(한 트랜잭션):
  1. `request_log` 조회(0.4).
  2. `SELECT ... FROM accounts WHERE id = $1 FOR UPDATE`로 계정 행을 잠가 **동시 생성으로 4개를 넘기는 것**을 막는다.
  3. 살아 있는 캐릭터 수가 4 이상이면 `422 CHARACTER_LIMIT_REACHED`.
  4. `characters` INSERT(`level=1, xp=0, gold=0`). 이름 UNIQUE 충돌(23505, 인덱스 `characters_name_alive`)은 `409 NAME_TAKEN`.
  5. `character_state` INSERT: `map_id`=`starter.json`의 `startMap`, `pos`=NULL, `facing`=0, 나머지 빈 값, `version`=0.
  6. 시작 지급(3.3.1).
  7. 응답 구성 후 `request_log` INSERT, 커밋.
- 응답 `201` `data`: `{ "character": CharacterDetail }` (지급 후 상태 그대로).
- 에러: `400 VALIDATION`(`errors.fields`에 `name` 사유: `NAME_LENGTH` | `NAME_CHARS`), `409 NAME_TAKEN`, `422 CHARACTER_LIMIT_REACHED`, `422 IDEMPOTENCY_MISMATCH`, `426`, `429`.

#### 3.3.1 시작 지급
- 지급 목록은 `server/data/starter.json`이 정본이다. 문서·SQL에는 값을 적지 않는다(필드는 mapping 문서 참조).
- 골드: `starter.json`의 `gold`가 0보다 크면 `characters.gold += gold`, `gold_ledger`에 1행(`delta`, `balance_after`, `reason='starter'`, `ref`=캐릭터 uuid).
- 소모품·재료: `starter.json`의 `items` 각 줄마다 `character_items`(`location='bag'`, `slot=NULL`, `count`) + `item_ledger` 1행(`delta=count`, `reason='starter'`, `ref`=캐릭터 uuid). 같은 `item_key`가 목록에 두 번 있으면 서버 시작 시 데이터 검증에서 실패시킨다.
- 시작 장비: `starter.json`의 `gear[<class>]`(`itemKey`, `slot`)가 있으면 `character_items`(`location='worn'`, `slot`, `count=1`) + `item_ledger` 1행. 현재 C# `GameSession.ResetForNewGame`이 하는 것과 같은 결과다.
- 서버 시작 시 `starter.json`의 모든 `itemKey`가 `items.json`에 있는지 검증하고, 없으면 기동 실패(조용히 건너뛰지 않는다).
- DB 안전장치: `gold_ledger_starter_uq`, `item_ledger_starter_uq` 부분 UNIQUE 인덱스가 같은 캐릭터에 시작 지급이 두 번 들어가는 것을 막는다.

### 3.4 GET /characters/{uuid}
- 응답 `200` `data`: `{ "character": CharacterDetail }`
- `items`는 `character_items_char_loc` 인덱스로 한 번에 읽는다.
- 에러: `404 CHARACTER_NOT_FOUND`(남의 캐릭터·삭제된 캐릭터·없는 id 모두 동일).

### 3.5 DELETE /characters/{uuid}
- 처리: 소유 확인 후 `characters.deleted_at = now()`. 아이템·원장·상태 행은 지우지 않는다(사후 추적).
- 이미 삭제된 캐릭터는 `404`가 아니라 **`200`**으로 같은 응답(재시도 안전, 멱등). 존재하지 않거나 남의 것은 `404 CHARACTER_NOT_FOUND`.
  - 구별 방법: 소유자가 같고 `deleted_at`이 있으면 200(이미 삭제), 그 외는 404.
- 응답 `200` `data`: `{ "deleted": true }`
- 삭제 즉시 이름이 풀리고 계정 슬롯이 하나 비워진다. 복구 기능은 이 단계에 없다.
- 삭제 확인 UI(이름 재입력 등)는 클라이언트 몫이다.

### 3.6 PUT /characters/{uuid}/state
재화가 아닌 상태를 통째로 저장한다(부분 갱신 없음). 클라이언트가 보내는 것은 "현재 상태 스냅샷"이고, 서버는 형식과 게임 규칙만 확인한다.

요청 본문(zod `.strict()` - 정의되지 않은 키가 하나라도 있으면 `400 VALIDATION`):
```
{
  "version": int,                                   // 마지막으로 받은 state.version
  "map_id": string,
  "pos": { "x": number, "y": number } | null,
  "facing": int,
  "quests": [ { "id": string, "status": int, "step": int, "counts": [int] } ],
  "story_flags": [string],
  "tracked_quest": string,
  "passives": [string],
  "skill_gems": [ { "slot": int, "supports": [string|null, string|null] } ]
}
```
- **`level`, `xp`, `gold`, `items`, 경험치·재화 관련 키는 요청 스키마에 존재하지 않는다.** `.strict()` 때문에 보내면 `400 VALIDATION`(`errors.fields[].message`: "허용되지 않는 필드")이다. 레벨·경험치·골드는 3단계 서버 판정만 바꾼다.
- 응답에도 이 값들은 없다.

처리(한 트랜잭션):
1. 소유 확인(404), 캐릭터의 `level`·`class`를 읽는다(서버 값. 요청 값 아님).
2. 형식 검증(zod): 위 구조, 배열 상한(`quests` ≤ 200, `story_flags` ≤ 500, 각 문자열 ≤ 64자, `passives` ≤ 200, `skill_gems` ≤ 슬롯 수), `x`·`y` 유한한 숫자.
3. 게임 규칙 검증(3.6.1). 실패하면 `422` + 사유.
4. 낙관적 잠금 갱신: `UPDATE character_state SET ..., version = version + 1, updated_at = now() WHERE character_id = $1 AND version = $expected RETURNING version`. 0행이면 `409 VERSION_CONFLICT`.
5. 커밋.

응답 `200` `data`: `{ "version": int, "updated_at": ISO }`

에러:
| 상태 | code | 의미 |
|---|---|---|
| 400 | VALIDATION | 형식 오류, 허용 안 되는 필드 |
| 404 | CHARACTER_NOT_FOUND | |
| 409 | VERSION_CONFLICT | `errors.current_version` 포함. 클라이언트는 GET으로 서버 상태를 받아 병합(또는 서버 값으로 덮어쓰기)한다 |
| 422 | INVALID_MAP | 없는 맵 또는 `instanced` 맵 |
| 422 | INVALID_POSITION | 좌표가 맵 범위 밖(범위 값을 내보냈을 때만 검사) |
| 422 | INVALID_PASSIVES | `errors.reason`: `UNKNOWN_NODE` / `DUPLICATE` / `OVER_POINTS` / `NOT_CONNECTED` |
| 422 | INVALID_GEMS | `errors.reason`: `UNKNOWN_GEM` / `NOT_SUPPORT` / `LOCKED_GEM` / `WRONG_CLASS` / `SLOT_LOCKED` / `DUPLICATE_IN_SLOT` / `BAD_SLOT` |
| 422 | INVALID_QUEST_STATE | `errors.reason`: `UNKNOWN_QUEST` / `BAD_STATUS` / `BAD_STEP` / `BAD_COUNTS` / `REGRESSION` / `DUPLICATE` |
| 426, 429 | | 0.3, 0.6 |

멱등성: `request_id` 없음. 응답을 못 받았다면 클라이언트가 `GET /characters/{uuid}`로 서버 `state.version`을 확인해 조정한다(내 저장이 반영됐으면 version이 올라가 있다).

#### 3.6.1 서버 검증 규칙 (게임 데이터는 `server/data/*.json`, 필드는 mapping 문서)

**맵** (`maps.json`)
- `map_id`가 `maps.json`에 있고 `instanced=false`여야 한다. 던전 방은 위치로 저장하지 않는다(C# `MapInfo.instanced` 주석: never saved as a position).
- `pos`가 null이 아니면 `maps.json`에 `bounds`가 있는 맵에 한해 범위 안인지 검사한다. 이 단계는 위치 이동의 개연성(걸어서 갈 수 있는지)을 검사하지 않는다. 처치·채집 판정을 하는 3단계가 `map_id`를 신뢰하지 않고 자체로 확인한다.

**패시브** (`passive_tree.json`)
- 입력 배열에서 시작 노드 id(`start`)는 있으면 무시한다(클라이언트 `Allocated`는 시작 노드를 포함하므로 어느 쪽으로 보내도 받는다).
- 나머지 id는 모두 트리에 존재하는 노드이고 `kind != start`, 중복 없음.
- **개수 <= 레벨 - 1** (서버가 가진 `characters.level` 기준, C# `PointsLeft = (Level-1) - max(0, Allocated.Count-1)`이 0 이상인 조건과 같다. 시작 노드는 무료).
- 모든 노드가 시작 노드에서 링크를 따라 이어져야 한다(C# `AllConnected`). 끊기면 `NOT_CONNECTED`.
- 레벨은 올라가기만 하므로(3단계) 한 번 통과한 저장이 나중에 무효가 되지 않는다. 레벨이 줄어드는 기능이 생기면 그때 별도로 설계한다.

**보조 젬** (`skill_gems.json`)
- 슬롯 수 = `slots`(현재 5), 슬롯당 보조 칸 = `supportsPerSlot`(현재 2). `skill_gems` 항목의 `slot`은 `0..slots-1`이고 서로 달라야 한다(`BAD_SLOT`). 항목이 없는 슬롯은 빈 슬롯이다.
- `supports`의 길이는 정확히 `supportsPerSlot`, 값은 gem id 또는 null.
- 슬롯이 열려 있어야 한다: `characters.level >= slotLevels[slot]` (C# `IsSlotOpen`). 닫힌 슬롯에 젬이 있으면 `SLOT_LOCKED`. (슬롯의 고정 액티브 스킬은 클래스·슬롯으로 정해지므로 요청에 없다.)
- 각 gem: 존재(`UNKNOWN_GEM`), `kind == "support"`(`NOT_SUPPORT`), `unlockLevel <= level`(`LOCKED_GEM`), `classOnly`가 없거나 캐릭터 클래스와 같음(`WRONG_CLASS`), 같은 슬롯 안에서 같은 gem 중복 금지(`DUPLICATE_IN_SLOT`). 다른 슬롯에는 같은 gem을 써도 된다(C# `OptionsFor`가 슬롯 단위로만 막는다).

**퀘스트·플래그** (`quest_index.json`)
- `quests[].id`가 존재하는 퀘스트, 중복 id 없음.
- `status`는 0..4(`enums.json`의 `questStatusMax`), `step`은 0 이상이고 `quest.stepCount` 이하, `counts`는 0 이상의 정수 배열이며 길이는 그 퀘스트의 어느 단계에서든 가질 수 있는 최대 목표 수 이하.
- 퇴보 금지: 서버에 저장된 `status`가 Completed(4)인 퀘스트는 Completed로 남아야 하고, 저장된 `story_flags`는 지워지면 안 된다(플래그는 추가만). 이 규칙은 3단계에서 완료 보상을 서버가 한 번만 주는 것과 충돌하는 재완료 조작을 미리 줄이기 위한 것이다. 단, 보상 지급 여부는 3단계에서 별도 기록으로 판정하므로 `quests` 컬럼을 보상 근거로 쓰지 않는다.
- `story_flags`는 형식(문자열, 64자 이하, 중복 없음, 최대 500개)만 강제한다. `quest_index.json.flags`에 없는 플래그는 거절하지 않고 경고 로그만 남긴다(수집 누락 때문에 저장이 막히는 것을 피하기 위함).
- `tracked_quest`는 빈 문자열이거나 존재하는 퀘스트 id.

## 4. 서버 환경변수 (config zod, 없으면 기동 실패)

| 이름 | 설명 |
|---|---|
| `DATABASE_URL` | Postgres 접속 |
| `JWT_SECRET` (또는 키 쌍) | 액세스 토큰 서명. 코드에 두지 않는다 |
| `MIN_CLIENT_VERSION` | 최소 클라이언트 버전 |
| `AUTH_DEV_ENABLED` | 개발용 로그인 켜기. production 기본 false |
| `REQUEST_LOG_TTL_DAYS` | 기본 7 |
| `GAME_DATA_DIR` | 기본 `server/data` |

## 5. 사용자가 정해야 할 항목

| 항목 | 선택지 | 추천 |
|---|---|---|
| 금칙어 필터 | (a) 2단계에 넣음 (b) 나중(운영 단계) | (b). 지금은 CBT 규모라 문자 규칙으로 충분하다. 필요해지면 `server/data/banned_words.json`을 읽는 검사 한 줄을 이름 검증에 더하면 된다 |
| 개발용 로그인을 운영에 켤지 | (a) 항상 끔 (b) 폐쇄 테스트 기간만 켬 | (a). 폐쇄 테스트가 필요하면 환경변수로 켜되, Steam 로그인이 붙으면 코드를 지운다 |
| 삭제 캐릭터 복구 | (a) 없음 (b) 7일 유예 후 영구 처리 | (a). 지금은 실수 삭제를 클라이언트 확인창으로 막고, 필요하면 `deleted_at`을 되돌리는 관리자 도구를 7단계에 둔다 |
