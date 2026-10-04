# 게임 서버 설계 (v1, 2026-10-03 · 진행 현황 2026-10-04)

## 진행 현황 (2026-10-04)

1~7단계 구현 완료. 단계별 상세 명세는 `Docs/server/`(phase1_2, phase3~6의 api·mapping, phase7_ops), 서버 코드는 `server/`, 운영 절차는 `server/ops/README.md`.

| 단계 | 상태 | 커밋 | 명세 | 비고 |
|---|---|---|---|---|
| 1·2 기반·온라인 캐릭터 | 완료 | c40f2f5 | phase1_2_api.md, phase1_2_mapping.md | |
| 3 경제 판정 | 완료 | b391f4e | phase3_api.md, phase3_mapping.md | 솔로 요일던전 포함 |
| 4 파티 협동 | 완료(전송은 개발용 UDP) | b172263 | phase4_api.md, phase4_mapping.md | Steam P2P·Steam 인증은 시험 앱 ID 확보 후 |
| 5 채팅·친구 | 완료 | 58beda7 | phase5_api.md, phase5_mapping.md | 일반 채널은 서버 단일 방(150명 초과 시 분할) |
| 6 경매장 | 완료 | 58beda7 | phase6_api.md, phase6_mapping.md | 귀속은 획득 경로 기준(재고 행 단위) |
| 7 운영 | 완료(배포 전 단계) | 9b38dc7 | phase7_ops.md | 관리자 도구는 CLI. 실제 배포는 업체·도메인 결정 후 |
| 8 전투 중계 + 필드 파티 | 설계 완료(미구현) | - | phase8_api.md, phase8_mapping.md | 구현 순서 R1(중계+던전) -> R2(필드) -> R3(Steam 480) -> R4(출시 품질). 마이그레이션 0009는 초안 |

검증: 서버 자동 테스트 442개 통과(`npm test`), Unity 배치 컴파일 통과, 단계마다 재화·보안 점검 에이전트 지적 반영. 창 2개 협동은 실제로 플레이해 보지 않았다(서버 흐름은 계정 2개로 확인).

### 단계별로 확정한 결정 (각 명세의 결정 대기에서 권장안 채택)
- 3단계: 솔로 요일던전 포함, 시작 장비 강화 가능, 노드 재생은 서버 시각, 드롭 5분 만료
- 4단계: 빈자리 AI 허용, 레이드 보상은 사람 2명 이상, 레이드 연습판은 처치 보상 없음, 매칭 전투력은 서버 추정치, 입장 레벨 하한(권장 레벨 - 5)
- 5단계: 채팅 7일 보관(귓속말 포함), 친구·차단은 계정 단위, 친구는 요청 후 수락, 신고 누적 자동 채팅 금지 없음, 금칙어 초기 목록은 클라이언트 6개
- 6단계: 귀속은 획득 경로 기준, 운영 매물 없음, 계정 창고 없음, 봉인 열쇠 거래 불가
- 7단계: 관리자 도구 CLI, DB는 같은 머신 + 오프사이트 백업, 알림은 웹훅, 원장 영구 보관, 외부 시험은 운영자 발급 개발 계정

### 남은 일
- Steam 연동(시험 앱 ID 480으로 P2P·`AuthenticateUserTicket` 실측), 실제 배포(업체·도메인·백업 저장소·웹훅·비밀값), 개인정보 처리방침, 금칙어 목록 보강
- 결정 대기: 파티에서 저레벨 캐릭터의 처치 경험치 감쇠 여부, 강화 직후 판매 차익이 의도인지

온라인 기획 원본: [PLAN_ONLINE.md](PLAN_ONLINE.md) (동기화 방식·결정 O1~O5), 경매장: [PLAN_AUCTION.md](PLAN_AUCTION.md).
이 문서는 그 결정을 실제로 만들 수 있게 기술 선택·구조·단계·1단계 상세까지 내린 것이다.

## 0. 사용자 결정

| # | 질문 | 결정 |
|---|---|---|
| S1 | Steam 출시 | 출시한다. 다만 게임 고도화가 먼저라 Steam 연동은 나중 단계에 붙인다 |
| S2 | 언어 | TypeScript |
| S3 | 서버 위치 | 빌린다(클라우드). 업체는 아직 정하지 않는다 |
| S4 | 규모 | 초반 동시 접속 100명 미만. 사람이 늘면 단계적으로 확장한다 |

## 1. 서버가 두 종류다

| 구분 | 하는 일 | 담당 |
|---|---|---|
| 실시간 전투 연결 | 같은 던전·같은 필드의 최대 4명이 이동·공격을 초당 수십 번 주고받는다 | 방장 PC가 몬스터를 계산한다(O2). 바이트 전달은 **우리 서버 중계(`/relay`, 기본)**와 Steam P2P(대안)를 서버가 판마다 골라 쓴다(8단계, `Docs/server/phase8_api.md`). 중계는 내용을 보지 않고 라우팅만 한다 |
| 백엔드 서버 | 로그인, 온라인 캐릭터 저장, 강화·드롭·보상 판정, 파티 모집·매칭, 채팅, 경매장 | **Node.js + TypeScript + PostgreSQL** (이 문서의 대상) |

- 전투를 Node로 옮기지 않는다: 전투 로직이 전부 Unity C#에 있어 다시 짜야 하기 때문이다.
- 전용 Unity 던전 서버(헤드리스)도 쓰지 않는다: 던전마다 서버비가 들고 운영이 무겁다. 대신 결과를 서버가 검증한다(§5).

## 2. 오프라인과 온라인 분리 (O1)

| | 오프라인 모드 | 온라인 모드 |
|---|---|---|
| 캐릭터 | PC 세이브 파일(지금 그대로) | 서버 DB. 오프라인 세이브는 절대 가져오지 않는다 |
| 아이템·골드·경험치를 얻고 쓰는 판정 | PC | **서버** |
| 파티 | AI 용병 | 사람 + 빈자리 AI |
| 경매장·채팅·친구 | 없음(목업) | 서버 |

- 타이틀에서 "오프라인 / 온라인"을 고르면 각자 다른 캐릭터 목록이 나온다.
- 같은 게임 코드를 쓰되, 온라인일 때만 판정 창구(`IAuthority` 계열)가 서버를 부른다.

## 3. 핵심 원칙: 클라이언트는 "일어난 일"을 보고하고, 서버가 "얻은 것"을 정한다

지금 게임에서 재화가 생기거나 사라지는 경로(2026-10-03 코드 기준)와 온라인 처리:

| 경로 | 지금 코드 | 온라인에서 |
|---|---|---|
| 몬스터 처치 경험치 | `PartyManager` 경험치 분배 | 클라이언트가 "몬스터 X를 맵 Y에서 처치" 보고 → 서버가 경험치 지급 |
| 몬스터 드롭 | `Authority.RollMonsterDrops` → 바닥에 `Pickup` | 서버가 드롭을 굴려 **드롭 id**를 발급 → 줍기 요청은 그 id로만 가능(중복 줍기 불가) |
| 채집(목재·돌·당근) | `Pickup` | 채집 보고 → 서버가 맵·재생 시간 기준으로 허용량 확인 후 지급 |
| 퀘스트 보상 | `QuestManager` 직접 지급 | 퀘스트 완료 보고 → 서버가 퀘스트 데이터의 보상표대로 지급(한 번만) |
| 던전 경험치·보상 카드 | `DungeonDirector`, `DungeonAuthority` | 던전 결과 보고 → 서버 검증 후 랭크·카드 결정(PLAN_ONLINE §5.3) |
| 상점 구매·판매 | `ServiceScreens` 직접 처리 | 서버 API(가격은 서버 데이터 기준) |
| 강화 | `Authority.EnhanceRoll` | 서버가 주사위·비용·파괴 판정 |
| 물약 사용, 창고 이동, 장착 | 클라이언트 직접 | 서버 API(수량 확인) |
| 경매·우편 | `MockAuctionService` | 서버 경매 서비스 |
| 시작 지급품 | `GameSession` | 캐릭터 생성 시 서버가 지급 |

- 이동, 전투 연출, 퀘스트 진행 단계, 맵 위치처럼 재화가 아닌 상태는 클라이언트가 주기적으로 저장 요청한다(서버는 형식만 확인).
- 클라이언트 쪽 작업: 위 경로를 전부 하나의 창구(`IEconomy` 같은 이름)로 모으고, 오프라인 구현은 지금 동작 그대로, 온라인 구현은 서버 호출로 만든다. 이게 온라인 작업의 가장 큰 클라이언트 작업이다.

## 4. 게임 데이터 공유

드롭표, 강화 확률, 장비 수치, 상점 가격, 퀘스트 보상, 던전·몬스터 수치는 지금 C# 코드 안에 있다. 서버가 같은 값을 써야 한다.

- Unity 에디터 메뉴로 게임 데이터를 JSON으로 내보낸다 → `server/data/*.json` (아이템, 몬스터·드롭, 강화, 상점, 퀘스트 보상, 던전·레이드).
- 내보낼 때 데이터 버전(해시)을 같이 기록한다. 클라이언트는 접속 시 자기 데이터 버전을 보내고, 서버와 다르면 업데이트를 요구한다.
- 원본은 계속 C# 코드다(한 곳에서만 고친다).

## 5. 부정행위 방어 (방장 권한 방식의 약점 보완)

| 위협 | 방어 |
|---|---|
| 강화·드롭·카드 조작 | 판정 자체를 서버가 한다 |
| 처치 보고 부풀리기 | 맵에 존재하는 몬스터인지, 처치 간격(초당 상한), 캐릭터 레벨 대비 가능한지 확인. 이상하면 지급 보류 + 기록 |
| 던전 결과 조작 | 최소 클리어 시간의 60% 미만, 파티 총 딜 < 몬스터 총 체력 같은 불가능한 값은 보상 보류(PLAN_ONLINE §5.1) |
| 같은 요청 재전송(이중 지급) | 모든 지급 요청에 `request_id`(클라이언트가 만든 uuid). 같은 id는 처음 결과를 그대로 돌려준다 |
| 동시 요청으로 골드 복사 | 재화 변경은 트랜잭션 + 행 잠금(`SELECT ... FOR UPDATE`) |
| 시계 조작(입장 횟수 초기화) | 06:00 일일·목요일 주간 초기화는 서버 시계로만 판정 |
| 사후 추적 | 골드·아이템 변동은 전부 원장(추가만 하는 로그)에 남긴다 |

## 6. 기술 선택

| 항목 | 선택 | 이유 |
|---|---|---|
| 런타임 | Node.js LTS + TypeScript | S2 |
| 웹 프레임워크 | Express | 기존 프로젝트(wecom 등)와 같은 3계층 규칙(Routes / Controller / Service / Repository)을 그대로 쓴다 |
| 입력 검증 | zod | 기존 규칙 |
| DB | PostgreSQL + `pg`(SQL 직접) | 경매·지갑처럼 트랜잭션·행 잠금이 중요한 코드는 SQL을 직접 보는 편이 안전하다 |
| 마이그레이션 | SQL 파일 + 마이그레이션 도구(node-pg-migrate 등) | |
| 실시간 | WebSocket(`ws`) | 채팅·매칭 알림·파티 상태 |
| 로그 | pino(JSON 로그) | |
| 테스트 | jest | 돈 처리는 동시성 테스트 포함 |
| 배포 | Docker Compose(api + postgres) | S3: 업체가 정해지면 VM 한 대에 올린다 |

응답 형식은 신규 프로젝트 기본값 `{ success, data, message }` / 실패 `{ success: false, message, errors? }`.

## 7. 확장 단계 (S4)

| 단계 | 규모 | 구성 |
|---|---|---|
| 시작 | 동시 100명 미만 | VM 한 대: Node 프로세스 하나(API + WebSocket) + PostgreSQL. 매칭 대기열·접속자 목록은 메모리 |
| 확장 1 | 수백 명 | DB를 관리형으로 분리, Redis 추가(대기열·접속자·채팅 채널), Node 2대 이상 + 부하 분산 |
| 확장 2 | 그 이상 | 실시간(WebSocket) 서버를 API와 분리, DB 읽기 복제본, 경매 검색 캐시 |

처음부터 지켜 둘 것: 서버 프로세스에 꼭 필요한 상태만 두고(대기열·접속자), 그것도 인터페이스 뒤에 둬서 나중에 Redis로 바꿀 수 있게 한다.

## 8. 저장소 구조

게임과 같은 저장소의 `server/` 폴더에 둔다(Unity는 `Assets/` 밖을 읽지 않는다). 게임 데이터 JSON과 통신 형식을 한 커밋에서 같이 바꿀 수 있다.

```
server/
  src/
    app.ts, server.ts           Express 앱, HTTP + WebSocket 시작
    config/                     환경변수(zod로 검증, 없으면 시작 안 함)
    db/                         pg 풀, 트랜잭션 도우미, 멱등성 도우미
    domains/
      auth/                     routes · controller · service · repository · schema
      characters/
      economy/                  지급·소모(원장 기록), 드롭, 상점, 강화
      quests/
      dungeons/                 입장 토큰, 결과 검증, 카드
      party/                    모집 게시판, 매칭(대기열은 인터페이스 뒤 메모리)
      chat/                     WebSocket 채널
      auction/                  PLAN_AUCTION
    gamedata/                   server/data/*.json 로더, 데이터 버전
  data/                         Unity가 내보낸 게임 데이터
  migrations/                   SQL
  test/
  docker-compose.yml
```

## 9. 개발 단계

| 단계 | 내용 | 끝났다는 기준 |
|---|---|---|
| 1 기반 | 서버 뼈대, DB, 개발용 로그인, 토큰, 버전·데이터 버전 확인, 게임 데이터 내보내기 | 클라이언트가 로그인해서 내 계정 정보를 받는다 |
| 2 온라인 캐릭터 | 캐릭터 생성·목록·삭제, 재화 아닌 상태 저장, 타이틀 온라인 모드 | 온라인 캐릭터로 접속해 마을을 돌아다니고, 다시 접속해도 위치·퀘스트 단계가 남는다 |
| 3 경제 판정 | §3 표의 모든 경로를 서버로: 처치·드롭·줍기·채집·퀘스트 보상·상점·강화·물약·창고·장착 | 온라인 캐릭터로 혼자 사냥·퀘스트·강화가 되고, 재화 변화가 전부 원장에 남는다 |
| 4 파티 협동 | 파티 모집·매칭 API, 입장 토큰, Steam P2P 전송(`ITransport`), 던전 결과 검증, 방장 이탈 인계 | 두 사람이 같은 던전을 깨고 각자 보상을 받는다 |
| 5 채팅·친구 | WebSocket 채널, 귓속말, 친구·차단·신고 | 지금 목업 채팅 창이 실제 서버로 동작한다 |
| 6 경매장 | 등록·입찰·즉시 구매·정산·우편·시세 | PLAN_AUCTION 시나리오 테스트 통과(동시 입찰 포함) |
| 7 운영 | 배포, 백업, 모니터링, 점검 공지, 관리자 도구 | 서버 한 대에 올려 외부에서 접속된다 |
| 8 전투 중계 + 필드 파티 사냥 | `/relay` WebSocket 중계, 전송 선택·자동 전환(중계 / Steam P2P), 필드 세션(같은 파티가 같은 필드에서 함께 사냥) | 인터넷으로 친구와 던전·필드를 같이 한다(R1 중계+던전, R2 필드, R3 Steam, R4 출시 품질 마감) |

Steam 연동(로그인·P2P)은 4단계에서 붙인다. 그 전에는 개발용 로그인과 지금 만든 같은 PC 창 2개 연결(`UdpTransport`)로 시험한다. Steam 앱 ID가 생기기 전에는 Valve 시험용 앱 ID로 P2P를 시험할 수 있다.

## 10. 1단계 상세

### 10.1 인증

- 인증 방식은 공급자 인터페이스 뒤에 둔다: 지금은 **개발용 로그인**(아이디·비밀번호, 운영 환경에서는 꺼짐), 나중에 **Steam 세션 티켓**.
- 로그인 성공 → 액세스 토큰(JWT 15분) + 갱신 토큰(14일, DB에는 해시만 저장, 쓸 때마다 교체).
- 모든 요청 헤더: 클라이언트 버전, 게임 데이터 버전. 최소 버전보다 낮으면 426 응답.

### 10.2 테이블 (1~3단계 범위)

```sql
-- 외부 노출은 uuid, 내부 키는 bigserial (프로젝트 규칙: 이중 ID)
create table accounts (
  id bigserial primary key,
  uuid uuid not null unique default gen_random_uuid(),
  created_at timestamptz not null default now(),
  banned_until timestamptz,
  deleted_at timestamptz
);

create table auth_identities (          -- 개발용 로그인, 나중에 steam
  id bigserial primary key,
  account_id bigint not null references accounts(id),
  provider text not null check (provider in ('dev', 'steam')),
  subject text not null,                -- dev: 아이디, steam: steam_id
  secret_hash text,                     -- dev 비밀번호 해시(steam은 null)
  created_at timestamptz not null default now(),
  unique (provider, subject)
);

create table refresh_tokens (
  id bigserial primary key,
  account_id bigint not null references accounts(id),
  token_hash text not null unique,
  expires_at timestamptz not null,
  revoked_at timestamptz
);

create table characters (
  id bigserial primary key,
  uuid uuid not null unique default gen_random_uuid(),
  account_id bigint not null references accounts(id),
  name text not null,
  class text not null check (class in ('warrior', 'mage')),
  level int not null default 1,
  xp int not null default 0,
  gold bigint not null default 0 check (gold >= 0),
  version int not null default 0,       -- 낙관적 잠금
  created_at timestamptz not null default now(),
  deleted_at timestamptz
);
create unique index characters_name_alive on characters (lower(name)) where deleted_at is null;

create table character_state (          -- 재화가 아닌 상태: 클라이언트가 저장 요청
  character_id bigint primary key references characters(id),
  map_id text not null,
  pos_x real not null, pos_y real not null,
  quests jsonb not null,                -- 퀘스트 단계·스토리 플래그
  passives jsonb not null,              -- 패시브 노드(서버가 레벨 대비 개수 확인)
  skill_gems jsonb not null,            -- 보조 젬 장착(해금 레벨 확인)
  updated_at timestamptz not null default now()
);

create table character_items (
  id bigserial primary key,
  uuid uuid not null unique default gen_random_uuid(),
  character_id bigint not null references characters(id),
  item_key text not null,               -- 기본 id + 강화 단계 (예: eq_sword_iron+7)
  count int not null check (count > 0),
  location text not null check (location in ('bag', 'storage', 'worn', 'mail', 'auction')),
  slot int,
  bind text not null default 'none' check (bind in ('none', 'account', 'character')),
  version int not null default 0
);

create table drops (                    -- 서버가 굴린 드롭. 줍기는 이 id로만
  id bigserial primary key,
  uuid uuid not null unique default gen_random_uuid(),
  character_id bigint not null references characters(id),
  item_key text not null, count int not null,
  source text not null,                 -- monster:skel_gold@forest 등
  expires_at timestamptz not null,
  claimed_at timestamptz
);

create table request_log (              -- 멱등성: 같은 request_id는 처음 응답을 돌려준다
  request_id uuid primary key,
  account_id bigint not null references accounts(id),
  endpoint text not null,
  response jsonb not null,
  created_at timestamptz not null default now()
);

create table gold_ledger (              -- 추가만 한다
  id bigserial primary key,
  character_id bigint not null references characters(id),
  delta bigint not null, balance_after bigint not null,
  reason text not null, ref text,       -- reason: kill, quest, shop_buy, enhance ...
  created_at timestamptz not null default now()
);

create table item_ledger (              -- 추가만 한다
  id bigserial primary key,
  character_id bigint not null references characters(id),
  item_key text not null, delta int not null,
  reason text not null, ref text,
  created_at timestamptz not null default now()
);
```

던전·파티·경매·채팅 테이블은 해당 단계에서 PLAN_ONLINE §5.2와 PLAN_AUCTION §7.2를 기준으로 추가한다.

### 10.3 API (1~2단계)

| 메서드 | 경로 | 요청 | 응답 |
|---|---|---|---|
| GET | `/health` | | 서버·DB 상태 |
| GET | `/meta` | | 최소 클라이언트 버전, 게임 데이터 버전, 서버 시각(초기화 판정 기준) |
| POST | `/auth/dev/register` | 아이디, 비밀번호 | 토큰 (운영에서는 꺼짐) |
| POST | `/auth/dev/login` | 아이디, 비밀번호 | 액세스·갱신 토큰 |
| POST | `/auth/refresh` | 갱신 토큰 | 새 토큰 쌍 |
| POST | `/auth/logout` | | 갱신 토큰 폐기 |
| GET | `/characters` | | 내 캐릭터 목록(이름·직업·레벨·마지막 위치) |
| POST | `/characters` | 이름, 직업, `request_id` | 캐릭터 + 시작 지급품(원장 기록) |
| DELETE | `/characters/{uuid}` | | 소프트 삭제 |
| GET | `/characters/{uuid}` | | 전체: 레벨·골드·아이템·상태 |
| PUT | `/characters/{uuid}/state` | 위치, 퀘스트, 패시브, 젬, `version` | 저장 결과(버전 충돌 시 409) |

3단계 API(처치 보고, 드롭 줍기, 채집, 퀘스트 보상, 상점, 강화, 물약, 창고, 장착)는 2단계가 끝난 뒤 이 표에 이어서 명세한다.

### 10.4 1단계 클라이언트 작업 (Unity)

- `ApiClient`: UnityWebRequest 래퍼, 토큰 자동 갱신, `request_id` 생성, 오류 메시지 한국어 처리.
- 타이틀: "온라인" 버튼 → 로그인 화면(개발용) → 온라인 캐릭터 목록.
- 게임 데이터 내보내기 에디터 메뉴: `server/data/*.json` + 데이터 버전.

## 11. 다음에 정할 것

- 서버 업체(S3): 실제 배포 전에 정한다. 1~7단계는 로컬(내장 PostgreSQL, `npm run dev`)로 개발했다.
- 온라인 캐릭터 수 제한(계정당), 이름 규칙(길이·금칙어).
- 온라인 전용 밸런스(드롭률·경험치)를 오프라인과 같게 둘지.
