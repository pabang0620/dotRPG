# 서버 7단계 운영 설계 (배포, 백업, 모니터링, 점검 공지, 관리자 도구)

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §9 7단계("배포, 백업, 모니터링, 점검 공지, 관리자 도구", 끝났다는 기준 = 서버 한 대에 올려 외부에서 접속된다), §7(시작은 VM 한 대, Node 프로세스 하나 + PostgreSQL). 7단계로 미뤄 둔 일: 보류(held) 던전 결과 검토·해제([phase3_api.md](phase3_api.md) 9.5), `anomaly_log`·`auction_flags` 검토, 신고 처리와 제재([phase5_api.md](phase5_api.md) 10), 보관 기간 정리(phase3 10, phase4 14, phase5 10.1, phase6 3). 스키마: `server/migrations/0008_ops.sql`(초안), `server/schema.sql`. 구현된 것: `server/src/**`(1~6단계), 마이그레이션 0001~0007, `Dockerfile`, `docker-compose.yml`.
이 문서는 설계다. 서버 코드·운영 스크립트는 dotrpg-backend-coder가 이 문서대로 만든다(11절 목록). 게임 값(드롭률·가격·수수료 등)은 문서에 복사하지 않는다. 이 단계에는 그런 값이 없고, 본문의 수치는 운영 정책 상수(환경변수, 시작값)다.

사용자 결정(다시 묻지 않는다): 서버 업체는 나중에 정하므로 업체에 묶지 않고 **Docker + Linux 한 대 + 도메인**을 기준으로 하며, DB는 같은 머신 또는 관리형 둘 다 가능하게 쓴다(업체별 단계는 부록 A에 짧게). 초반 100명 미만, 점차 확장. TypeScript. 관리자 도구는 최소한의 웹 페이지 또는 CLI이고 하나를 권고한다(5.1).

## 0. 핵심 결정

| # | 항목 | 결정 | 이유 |
|---|---|---|---|
| 1 | 관리자 도구 | **CLI**(`dotrpg-admin`, 같은 이미지에 포함) + 관리자 API 44개. 웹 페이지는 만들지 않는다 | 5.1 |
| 2 | 관리자 접근 | 관리자 API는 **같은 프로세스의 별도 listener**(`127.0.0.1:3001`)이고 어떤 포트로도 공개하지 않는다. 접근은 `ssh 호스트 docker compose exec ...` | WebSocket 접속 목록(메모리)과 점검 상태가 같은 프로세스에 있어야 `bye`·`chat.sys`를 보낼 수 있다. 공개 포트가 하나도 늘지 않는다 |
| 3 | 관리자 인증 | 게임 계정과 분리된 `admin_users` + argon2id 비밀번호 + **TOTP 2FA 필수** + loopback/CIDR 제한 + 모든 행동 감사 로그(`admin_audit_log`) | 5.3, 5.5 |
| 4 | 배포 | **짧은 점검 방식**(점검 창 -> 드레인 -> 백업 -> 마이그레이션 -> 기동 -> 종료)이 표준이고, 마이그레이션·프로토콜 변경이 없는 배포는 **재시작 5~25초**짜리 핫 배포. 진짜 무중단은 하지 않는다 | 접속자 목록·매칭 대기열·속도 제한이 프로세스 메모리라 두 프로세스를 동시에 쓸 수 없다(PLAN §7 "확장 1"에서 Redis와 함께 가능) |
| 5 | 프록시·TLS | **Caddy**(자동 인증서, WebSocket 기본 지원). 인터넷에 열리는 것은 80/443뿐 | 3.7 |
| 6 | 점검 | `maintenance_windows` 한 행이 상태를 정한다. 공지(`chat.sys`) -> 새 로그인·새 던전 판 차단(시작 10분 전) -> 점검(`bye`+503) | 4절 |
| 7 | 정리 작업 | 프로세스 안 `JobRunner`(`pg_try_advisory_lock`, `job_runs` 기록), 새벽 KST 배치 | 6절 |
| 8 | 백업 | 매일 `pg_dump -Fc`(암호화, 오프사이트) **필수**, 정식 출시 전 **WAL 아카이브(PITR)** 추가, 매월 복구 훈련 | 7절 |
| 9 | 모니터링 | 외부 업타임 점검 + 앱 내부 감시자(watchdog) + 하트비트(죽음 감지). Prometheus는 확장 1에서 | 8절 |

## 1. 현재 구현에서 출시 전에 고칠 것

7단계 설계를 위해 구현을 읽다가 발견했다. 모두 11절 구현 요청에 번호로 들어 있다.

| # | 위치 | 문제 | 조치 |
|---|---|---|---|
| F1 | `Dockerfile` `CMD ["sh","-c","node dist/db/migrate.js up && node dist/server.js"]` | PID 1이 `sh`라 `SIGTERM`이 node에 전달되지 않는다. `server.ts`의 정상 종료(`bye`, 채팅 쓰기 큐 비우기, 풀 닫기)가 **한 번도 실행되지 않고** docker가 10초 뒤 `SIGKILL`한다. 또 기동할 때마다 마이그레이션이 돈다(두 컨테이너가 겹치는 배포에서 위험) | `CMD ["node","dist/server.js"]` + `init: true`, 마이그레이션은 별도 단계(3.5) |
| F2 | `docker-compose.yml` `ports: "3000:3000"` | 모든 인터페이스로 공개되고 docker가 만드는 iptables 규칙은 `ufw`를 우회한다. TLS도 없다 | api는 `expose`만, 80/443은 Caddy만 publish (3.2) |
| F3 | `docker-compose.yml` | 로그 로테이션, 메모리 제한, `stop_grace_period`(기본 10초), api 헬스체크, postgres 설정이 없다 | 3.2 |
| F4 | `config/env.ts` | 운영에서 `STEAM_AUTH_MODE=mock`을 거부한다고 `.env.example`에 적혀 있지만 코드는 `mock + PARTY_TRANSPORT=dev` 조합만 거부한다. **`mock + steam`은 통과**한다. mock 티켓 `mock:<steam_id64>:<nonce>`로 누구의 Steam ID로든 로그인할 수 있다(계정 탈취) | 운영에서 `mock` 무조건 거부 (9.1) |
| F5 | `config/env.ts`, `authRoutes.ts` | `AUTH_DEV_ENABLED=true`가 운영에서도 허용되고, 개발용 가입(`register`)과 로그인이 한 스위치다. `STEAM_APP_ID=480`(Valve 시험용)도 운영에서 허용된다 | 명시 허용 플래그 + 가입 스위치 분리 + 480 거부 (9.1) |
| F6 | `config/env.ts` `TRUST_PROXY` 기본 0 | 프록시 뒤에서 0이면 모든 접속자가 프록시 IP 하나로 보여 IP 속도 제한을 **전원이 공유**한다(한 명이 걸리면 모두 429). WebSocket `clientIp`도 같은 규칙 | 프록시 구성에서는 `TRUST_PROXY=1`을 필수로 검사 (9.1) |
| F7 | `systemService.getHealth` | `/health` 하나뿐이다. DB만 보고, 마이그레이션 일치·데이터 로드·종료 중 여부를 보지 않아 종료 중에도 200이다 | live/ready 분리 (8.1) |
| F8 | `server.ts` `shutdown` | 전체 제한 시간이 없고 진행 중 요청을 기다리지 않으며 점검 사유를 `bye`에 못 싣는다. `server.close`는 keep-alive 연결 때문에 오래 걸릴 수 있다 | 4.5 순서 |
| F9 | `phase5_api.md` 10.3 `banned_until = 'infinity'` | node-pg는 `infinity`를 `Date`가 아닌 값으로 돌려줘 `authMiddleware`의 `banned_until.getTime()`이 `TypeError`가 된다. 그 계정의 **모든 요청이 500**이 된다 | 영구 정지는 `9999-12-31T00:00:00Z`(5.8 SA1, 0008 주석) |
| F10 | `phase6_api.md` 3절 보관(`auction_trades` 180일, 종료 listing 180일, `auction_flags` 90일) | 추가 전용 트리거(`ledger_block_mutation`)가 DELETE를 막고, `auction_trades.listing_id`·`auction_sinks`·`mails`가 FK로 연결돼 있어 **실행할 수 없는 정책**이다 | 7단계에서 "지우지 않는다"로 정정 (6.3) |
| F11 | `drops.kill_id` FK `ON DELETE CASCADE` | `kill_id`에 인덱스가 없어 `kill_log` 정리 때 처치 한 행마다 `drops` 전체 스캔이 일어난다 | 0008 `drops_kill_idx` |
| F12 | `server.ts` 정리 `setInterval` | 작업 실행 기록이 없다(실패해도 로그 한 줄뿐, "정리가 3일째 안 돈다"를 알 수 없다). `request_log`는 배치 없는 한 문장 DELETE | `JobRunner` + `job_runs` (6.1) |
| F13 | `db/migrate.ts` | 파일마다 한 트랜잭션이라 `CREATE INDEX CONCURRENTLY` 불가. 원장 `reason` CHECK를 DROP/ADD하면 표 전체 스캔 + `ACCESS EXCLUSIVE`(출시 후 원장이 커지면 긴 잠금) | 출시 후 규칙 (3.5) |

## 2. 구성

```
인터넷 -- 80/443 --> [caddy] --(내부 네트워크)--> [api: Node 프로세스 하나]
                                                    |- 공개 listener :3000 (REST + /ws)
                                                    |- 관리자 listener 127.0.0.1:3001 (컨테이너 안에서만)
                                                    |- 틱·JobRunner·watchdog (같은 프로세스)
                                                    +--> [postgres]  (같은 머신 컨테이너, 또는 관리형 DB)
운영자 -- ssh --> 호스트 --> docker compose exec api node dist/admin/cli.js ...
호스트 cron --> 백업 스크립트(pg_dump / pgBackRest) --> 오프사이트 객체 저장소(S3 호환)
```

- 컨테이너 3개(`caddy`, `api`, `postgres`) + 호스트 cron. 관리형 DB를 쓰면 `postgres`만 빠진다.
- 컨테이너 네트워크는 하나의 사설 네트워크이고 `api`·`postgres`는 호스트 포트를 열지 않는다.
- 호스트 시간대는 UTC, NTP(`chrony` 등) 동기화 필수. 06:00 일일·목요일 주간 경계는 서버가 KST 함수(`resetBoundaries`) 하나로 계산하므로 호스트 시간대에 의존하지 않지만, **시계 자체가 틀리면 판정이 틀린다**(9.4 점검 항목).

## 3. 배포

### 3.1 Docker 이미지
- 이미지 태그는 `dotrpg-api:<git 짧은 해시>`. `latest`를 쓰지 않고, 호스트에 최근 3개를 남겨 롤백에 쓴다. 레지스트리가 없으면 `docker save | ssh host docker load`로 옮긴다(업체 무관).
- `server/data/*.json`(Unity가 내보낸 게임 데이터)은 이미지에 굽는다. 게임 데이터가 바뀌면 `data_version`이 바뀌고 모든 클라이언트가 `426 DATA_OUTDATED`가 되므로 **데이터가 바뀌는 배포는 항상 점검 창으로 한다**(Steam 클라이언트 업데이트가 끝나기 전에 서버만 먼저 올리지 않는다).
- 변경(F1, 11절 B): 비루트 사용자(`USER node`), `HEALTHCHECK`, 마이그레이션을 `CMD`에서 제거, `NODE_OPTIONS=--max-old-space-size`를 컨테이너 메모리 제한의 약 70%로(예: 제한 1GB면 700MB). 이미지에 `.env`·비밀값을 넣지 않는다(`.dockerignore` 확인).

### 3.2 compose 구성 (운영용, `server/ops/compose.prod.yml`)
형태만 적는다. 값은 구현 때 확정한다.

```yaml
services:
  caddy:
    image: caddy:2
    restart: unless-stopped
    ports: ["80:80", "443:443"]          # 인터넷에 열리는 유일한 포트
    volumes: [./Caddyfile:/etc/caddy/Caddyfile:ro, caddy_data:/data, caddy_config:/config]
    depends_on: { api: { condition: service_healthy } }
  api:
    image: ${API_IMAGE:?}                # dotrpg-api:<해시>
    init: true                           # 신호 전달, 좀비 정리 (F1)
    restart: unless-stopped
    stop_grace_period: 40s               # SHUTDOWN_GRACE_SECONDS(30) + 여유
    mem_limit: 1g
    env_file: .env
    environment: { NODE_ENV: production, TRUST_PROXY: "1", GAME_DATA_DIR: /app/data }
    expose: ["3000"]                     # 호스트에 publish하지 않는다 (F2)
    healthcheck:                         # slim 이미지에는 curl이 없다
      test: ["CMD","node","-e","fetch('http://127.0.0.1:3000/health/ready').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
      interval: 10s
      timeout: 3s
      start_period: 30s
      retries: 3
    logging: { driver: json-file, options: { max-size: "50m", max-file: "10" } }
    depends_on: { postgres: { condition: service_healthy } }
  postgres:                              # 관리형 DB를 쓰면 이 서비스를 지운다
    image: postgres:16                   # pgBackRest를 쓰면 ops/postgres.Dockerfile(postgres:16 + pgbackrest)
    restart: unless-stopped
    shm_size: 256mb
    command: ["postgres","-c","config_file=/etc/postgresql/postgresql.conf"]
    volumes: [pgdata:/var/lib/postgresql/data, ./postgres.conf:/etc/postgresql/postgresql.conf:ro]
    expose: ["5432"]
    logging: { driver: json-file, options: { max-size: "50m", max-file: "5" } }
volumes: { pgdata: {}, caddy_data: {}, caddy_config: {} }
```

`postgres.conf` 시작값(RAM 4GB 가정, 가정이므로 부하 시험 뒤 조정): `shared_buffers=1GB`, `effective_cache_size=2GB`, `max_connections=50`(앱 풀 10 + LISTEN 1 + 마이그레이션·백업·운영자 여유), `work_mem=8MB`, `wal_compression=on`, `checkpoint_timeout=15min`, `max_wal_size=2GB`, `log_min_duration_statement=500`, `log_lock_waits=on`, `idle_in_transaction_session_timeout=30s`, WAL 아카이브를 쓰면 `wal_level=replica`, `archive_mode=on`, `archive_timeout=60`, `archive_command`(7.2).
앱 풀은 연결 옵션으로 `statement_timeout=15s`, `idle_in_transaction_session_timeout=30s`를 건다(역할 전체에 걸면 마이그레이션·`pg_dump`가 끊긴다). 풀 크기는 `DB_POOL_MAX`(기본 10, 지금은 코드 상수 10).
**관리형 DB 주의**: `LISTEN dotrpg_sanction`(5단계)과 마이그레이션의 `pg_advisory_lock`은 **세션이 유지되는 연결**이 필요하다. PgBouncer 같은 트랜잭션 풀러를 앞에 두면 둘 다 깨지므로 직접(또는 세션 모드) 연결 문자열을 쓴다.

### 3.3 환경변수

**운영에서 반드시 맞출 기존 값**(틀리면 기동하지 않게 9.1의 가드를 둔다):

| 이름 | 운영 값 | 비고 |
|---|---|---|
| `NODE_ENV` | `production` | |
| `DATABASE_URL` | `postgres://dotrpg:<비번>@postgres:5432/dotrpg` | compose가 조립. 관리형이면 업체가 준 주소(TLS 필수, `sslmode=require` 이상) |
| `JWT_SECRET` | 48바이트 난수(base64url) | 자리표시 값(`changeme` 등) 거부 |
| `MIN_CLIENT_VERSION` | 출시 클라이언트 버전 | 배포마다 확인 |
| `TRUST_PROXY` | `1` | Caddy 한 단계. 앞에 CDN이 더 있으면 단수를 늘린다. 9.2에서 가짜 `X-Forwarded-For` 시험 |
| `STEAM_AUTH_MODE` / `PARTY_TRANSPORT` | `web_api` / `steam` | `mock`·`dev` 거부 |
| `STEAM_APP_ID`, `STEAM_WEB_API_KEY`, `STEAM_IDENTITY` | Steamworks 값 | `STEAM_APP_ID=480` 거부. 클라이언트가 티켓을 만들 때 쓰는 identity 문자열과 같아야 한다 |
| `AUTH_DEV_ENABLED` | `false` (Steam 연동 전 시험 기간만 5.6의 규칙으로 `true`) | |
| `AUCTION_MIN_LEVEL`, `AUCTION_MIN_ACCOUNT_AGE_DAYS` | 0 아님 | 이미 가드 있음 |
| `LOG_LEVEL` | `info` | |

**7단계 신규 값**(모두 시작값, 이름·기본값은 `config/env.ts`에 zod로 추가):

| 그룹 | 이름 = 기본값 | 뜻 |
|---|---|---|
| 관리자 | `ADMIN_ENABLED=true`, `ADMIN_BIND=127.0.0.1`, `ADMIN_PORT=3001`, `ADMIN_ALLOWED_CIDRS=127.0.0.1/32,::1/128` | listener. `ADMIN_ENABLED=true`이면 아래 `ADMIN_SECRET_KEY` 필수 |
| | `ADMIN_SECRET_KEY`(32바이트 base64, 기본 없음) | TOTP 비밀키 암호화(AES-256-GCM) |
| | `ADMIN_SESSION_IDLE_MINUTES=30`, `ADMIN_SESSION_MAX_HOURS=8`, `ADMIN_LOGIN_FAIL_MAX=5`, `ADMIN_LOCK_MINUTES=15`, `ADMIN_RATE_PER_MIN=120` | 세션·잠금·속도 제한 |
| | `ADMIN_GRANT_MAX_GOLD=1000000`, `ADMIN_GRANT_MAX_ITEM_COUNT=99`, `ADMIN_GRANT_DAILY_GOLD=5000000`, `ADMIN_OPERATOR_BAN_MAX_DAYS=30` | 운영 지급·정지 한도(5.4) |
| | `WATCHLIST_MIN_SCORE=10`, `WATCHLIST_WINDOW_HOURS=24` | 감시 목록(5.8 WT3) |
| 가입 | `AUTH_DEV_REGISTER_ENABLED`(기본: 비운영 `true`, 운영 `false`), `ALLOW_DEV_AUTH_IN_PRODUCTION=false` | 9.1 |
| 점검 | `MAINT_PRE_BLOCK_MINUTES=10`, `MAINT_ANNOUNCE_MINUTES=30,10,5,1`, `MAINT_POLL_SECONDS=5`, `MAINT_BYE_DELAY_SECONDS=3` | 4절 |
| 종료·DB | `SHUTDOWN_GRACE_SECONDS=30`, `DB_POOL_MAX=10`, `DB_STATEMENT_TIMEOUT_MS=15000` | 4.5, 3.2 |
| 정리 | `KILL_LOG_RETENTION_DAYS=7`, `DROP_RETENTION_DAYS=1`, `ANOMALY_RETENTION_DAYS=30`, `ANOMALY_SEVERE_RETENTION_DAYS=180`, `REFRESH_TOKEN_PURGE_DAYS=30`, `MAIL_CLAIMED_RETENTION_DAYS=180`, `PARTY_RECORD_RETENTION_DAYS=30`, `JOB_RUN_RETENTION_DAYS=90`, `ADMIN_SESSION_PURGE_DAYS=30`, `PURGE_BATCH=5000`, `PURGE_BATCH_SLEEP_MS=100`, `JOB_MAX_SECONDS=300` | 6절(기존 `REQUEST_LOG_TTL_DAYS`, `CHAT_RETENTION_DAYS`, `REPORT_RETENTION_DAYS`는 그대로) |
| 감시 | `OPS_SNAPSHOT_SECONDS=60`, `ALERT_WEBHOOK_URL`, `ALERT_WEBHOOK_FORMAT=discord`, `ALERT_MIN_INTERVAL_MINUTES=30`, `OPS_HEARTBEAT_URL`, `SERVER_NAME` | 8절. 웹훅·하트비트 URL이 비어 있으면 해당 기능만 끈다(경고 로그) |

### 3.4 비밀값 관리

| 비밀 | 만드는 법 | 보관 | 교체하면 |
|---|---|---|---|
| `POSTGRES_PASSWORD` | 난수 32바이트 hex | 호스트 `.env`(권한 0600, 배포 사용자 소유) + 비밀번호 관리자 | `ALTER ROLE` + `.env` + api 재시작(짧은 점검) |
| `JWT_SECRET` | 난수 48바이트 base64url | 같음 | 모든 액세스 토큰이 무효가 된다. 수명이 15분이고 갱신 토큰은 DB 해시라 영향이 없어 클라이언트가 자동 갱신한다(점검 창 없이 가능) |
| `ADMIN_SECRET_KEY` | 난수 32바이트 base64 | 같음 + **오프라인 사본** | 모든 관리자의 TOTP를 다시 등록해야 한다(재암호화 스크립트가 있으면 무영향) |
| `STEAM_WEB_API_KEY` | Steamworks | 같음 | 유출 시 Steamworks에서 재발급 후 교체. 코드·로그·이미지·클라이언트에 넣지 않는다 |
| 백업 암호화 키 | `age-keygen` | **공개키만 서버**. 개인키는 서버에 두지 않고 오프라인 2곳(비밀번호 관리자 + 인쇄) | 개인키를 잃으면 모든 백업이 쓸모없다. 분기마다 복호화 확인 |
| 오프사이트 저장소 키 | 저장소 콘솔 | 서버 `.env`. **쓰기 전용 + 삭제 불가 권한**(객체 잠금·버전 관리 지원 시 켠다) | 서버가 털려도 백업을 지우지 못하게 한다 |
| `ALERT_WEBHOOK_URL`, `OPS_HEARTBEAT_URL` | 각 서비스 | `.env` | URL 자체가 비밀 |
| SSH 키, 도메인(레지스트라·DNS) 계정 | | 운영자 개인 키·2FA | 계정 2FA 필수 |

규칙: `.env`는 git·이미지·로그에 넣지 않는다. `docker compose config`·`env` 출력을 채팅·이슈에 붙이지 않는다(값이 펼쳐진다). pino `redact`에 `*.token`, `*.password`, `*.secret`, `STEAM_WEB_API_KEY`를 더한다(11절). 비밀값 변경 이력은 `docs`가 아니라 운영자 개인 노트에 둔다(이 저장소에 값을 남기지 않는다).

### 3.5 마이그레이션 순서와 규칙

- **순서**: 새 이미지 빌드 -> (점검 창) -> 서버 정지 또는 구 서버가 새 스키마와 호환될 때는 그대로 -> `docker compose run --rm api node dist/db/migrate.js up` -> 새 api 기동. 마이그레이션은 `CMD`에서 빼고 이 한 줄로만 돌린다(F1). 러너의 `pg_advisory_lock(7301)`이 동시 실행을 막는다.
- **확장-정리(expand-contract) 규칙**: 새 코드보다 먼저 적용되는 마이그레이션은 **구 코드가 그대로 동작하는 변경**이어야 한다(표·열 추가, `NOT NULL` 없는 열, 인덱스 추가). 구 코드가 쓰는 열·표를 지우거나 이름을 바꾸는 변경은 두 번의 배포로 나눈다(1: 새 코드가 새 구조를 쓰기 시작, 2: 구 구조 제거).
- **원장 CHECK 규칙(출시 후)**: 원장(`gold_ledger`, `item_ledger`, `xp_ledger`)의 `reason` 목록을 바꿀 때는 `DROP CONSTRAINT` + `ADD CONSTRAINT ... NOT VALID`(즉시 끝남)로 두고 `VALIDATE CONSTRAINT`를 **다음 마이그레이션**에서 한다(검증은 쓰기를 막지 않는다). 지금(0008까지)은 출시 전이라 DROP/ADD를 그대로 쓴다.
- 인덱스는 출시 후 큰 표(원장·`kill_log`·`chat_messages`)에 만들 때 러너를 `-- no-transaction` 마커(러너 확장, 11절)로 돌려 `CREATE INDEX CONCURRENTLY`를 쓴다. 작은 표에는 일반 `CREATE INDEX`.
- **DOWN은 운영에서 실행하지 않는다**(0001~0008의 DOWN은 원장 행을 지우므로 개발 DB 전용이다). 운영 롤백은 3.6.
- 마이그레이션 파일 수와 `schema_migrations` 행 수가 다르면 `/health/ready`가 503(`schema_mismatch`)이다(8.1). 새 코드가 마이그레이션 전에 뜨는 사고를 막는다.

### 3.6 배포 절차

**A. 핫 배포(마이그레이션 없음, 데이터 버전·프로토콜 변경 없음, 접속이 적은 시간)**
1. 이미지 빌드·전송, `.env`의 `API_IMAGE`를 새 태그로.
2. `docker compose up -d api`. 구 컨테이너가 `SIGTERM`을 받아 4.5 순서로 종료(최대 30초)하고 새 컨테이너가 뜬다. 접속 중인 플레이어는 `bye`(`reconnect:true`, 3~8초 무작위)를 받고 자동 재연결한다. REST는 같은 `request_id`로 재시도하므로 중복 지급이 없다(멱등성).
3. `/health/ready`, `/meta`의 `data_version`, `docker logs api`(1분)를 확인. 이상하면 `API_IMAGE`를 이전 태그로 되돌려 `up -d`.

**B. 점검 배포(마이그레이션 또는 데이터 버전·프로토콜 변경이 있을 때의 표준)**
1. 점검 창 예약: `dotrpg-admin maint schedule --in 30m --duration 20m --notice "업데이트"`. 공지가 30·10·5·1분 전에 나가고 시작 10분 전부터 새 로그인·새 판이 막힌다(4절).
2. 시작 시각이 되면 `maint drain`으로 `ready_to_stop`을 확인한다(접속 0, 진행 중 요청 0, 틱·작업 정지).
3. **배포 전 스냅샷**: `pg_dump -Fc`(이름에 `predeploy-<해시>`)를 만들고 크기·`pg_restore --list` 성공을 확인한 뒤 `schema_migrations` 마지막 이름을 기록한다. 오프사이트 업로드까지 기다리지 않고 로컬 사본이면 충분하다(업로드는 이어서).
4. `docker compose stop api`. (Caddy가 502를 `503 MAINTENANCE` JSON으로 바꿔 준다. 3.7)
5. `docker compose run --rm api node dist/db/migrate.js up`(새 이미지). 실패하면 그 파일의 트랜잭션이 롤백돼 스키마는 그대로이므로 구 이미지로 `up -d api`하고 원인을 고친다.
6. `docker compose up -d api`(새 이미지). 점검 창 행이 DB에 있으므로 새 프로세스도 기동 직후부터 점검 상태다(플레이어는 못 들어온다). 관리자 listener는 열려 있다.
7. 점검 중 확인: `ops status`(정합성·작업·틱), Steam 테스트 계정(또는 개발 계정) 한 명으로 클라이언트 접속·핵심 동선을 운영자가 직접 확인. 필요하면 `maint extend`.
8. `maint end`. 플레이어가 들어온다. 15분 동안 `ops status`와 로그를 본다.

**롤백**

| 상황 | 방법 |
|---|---|
| 5번 마이그레이션 실패 | 스키마 무변경(트랜잭션 롤백). 구 이미지로 기동 |
| 새 코드 이상, 마이그레이션은 추가형(3.5 규칙 준수) | 구 이미지로 기동. 스키마는 그대로 둔다(구 코드가 동작) |
| 새 코드 이상 + 파괴적 변경 포함 | 배포 전 스냅샷으로 복구(7.4). 점검 창을 연장하고 공지 |
| 복구 뒤 플레이어 손실 보상 | 표를 직접 고치지 않고 **운영 지급(우편)** 으로 보상(5.8 EC2) |

### 3.7 TLS와 프록시 (Caddy)

- 자동 인증서(ACME)에는 도메인의 A 레코드가 이 머신을 가리키고 80/443이 열려 있어야 한다. 인증서 저장소는 `caddy_data` 볼륨(지우면 발급 한도에 걸린다).
- WebSocket은 `reverse_proxy`가 업그레이드를 그대로 통과시킨다(별도 설정 없음). 앱이 15초마다 ping하므로(`WS_PING_EVERY_MS`) 프록시 유휴 시간 제한에 걸리지 않는다. Unity의 `wss://`와 `UnityWebRequest`는 시스템 CA 저장소로 Let's Encrypt 체인을 검증한다.
- 형태 예시(문법은 배포 때 `caddy validate`로 확인):

```
{
  email 운영자@example.com
  servers { protocols h1 h2 }              # HTTP/3(UDP 443)을 끄면 방화벽 규칙이 하나 줄어든다
}
api.example.com {
  encode zstd gzip
  request_body { max_size 128KB }          # 앱 한도는 64KB
  @admin path /admin /admin/*
  respond @admin 404                       # 관리자 API는 어차피 이 네트워크에 없다. 이중 방어
  reverse_proxy api:3000 {
    health_uri /health/ready
    health_interval 10s
    health_timeout 3s
  }
  log { output stdout
        format json }
  handle_errors {
    @down expression {err.status_code} in [502, 503, 504]
    handle @down {
      header Content-Type application/json
      respond `{"success":false,"message":"서버 점검 중이거나 연결할 수 없습니다.","errors":{"code":"MAINTENANCE"}}` 503
    }
  }
}
```
- `handle_errors` 덕분에 api 프로세스가 내려가 있는 동안에도 클라이언트는 앱과 같은 형식(`errors.code=MAINTENANCE`)의 응답을 받는다.
- 접근 로그에는 IP가 남는다. 보관 14일(docker 로그 로테이션 + 개인정보 처리방침, 결정 대기 6).

### 3.8 방화벽과 호스트 설정

| 구분 | 설정 |
|---|---|
| 인바운드 | 80/tcp, 443/tcp 허용. 22/tcp는 관리 IP에서만(불가능하면 `ufw limit 22/tcp` + 키 인증 전용). 그 밖에는 전부 거부(`default deny incoming`). 3000·3001·5432는 어디에도 열지 않는다 |
| 아웃바운드 | 허용(Steam Web API, ACME, 백업 업로드, 웹훅). 필요하면 나중에 목적지 제한 |
| docker | compose에서 80/443만 `ports`. `api`·`postgres`는 `expose`. docker는 `ufw`를 우회하므로 "ufw가 막아 준다"고 믿지 않는다(9.2에서 외부 포트 스캔으로 확인) |
| 클라우드 방화벽 | 업체가 제공하면 같은 규칙을 한 번 더 건다(부록 A) |
| SSH | 비밀번호 로그인 끔, 루트 로그인 끔, 키 전용. 운영자별 계정 |
| OS | 자동 보안 업데이트, `chrony`(시계), 스왑(작게), 디스크 알림(8.3) |
| 사용자 | 배포 전용 비루트 사용자가 docker 그룹. 호스트에서 DB를 직접 열 일은 `docker compose exec postgres psql`뿐 |

## 4. 점검 공지와 서버 종료

### 4.1 상태 모델
`maintenance_windows`의 열린(`state='scheduled'`) 행 **하나**와 서버 시각으로 단계를 계산한다(시각 경계마다 DB를 쓰지 않는다). 서버는 `MAINT_POLL_SECONDS`(5초)마다 읽어 메모리에 둔다(운영자의 변경은 같은 프로세스라 즉시 반영).

| 단계 | 조건 | 의미 |
|---|---|---|
| `none` | 열린 창 없음 | 평상시 |
| `scheduled` | `now < block_login_at` | 예고만 있다. `/meta`와 공지 줄로 알린다 |
| `pre_block` | `block_login_at <= now < starts_at` | 새 로그인·새 던전 판 차단. 이미 접속한 사람은 계속 논다 |
| `active` | `starts_at <= now < ends_at` | 점검 중. 모두 접속 종료, 대부분의 요청이 503 |
| (종료) | `now >= ends_at` 또는 `maint end` | 작업 `maintenance-close`(1분)가 `state='ended'`로 닫는다. 예정 시각을 넘기면 자동으로 풀린다(연장은 `maint extend`) |

`block_login_at`은 기본 `starts_at - MAINT_PRE_BLOCK_MINUTES(10분)`이다. 근거: 판이 시작될 수 있는 마지막 순간(파티 모집 90초 `PARTY_GATHER_SECONDS`) + 가장 긴 던전 기준 시간(230초 이하, phase3 10) + 결과 대기(90초 `PARTY_RESULT_WAIT_SECONDS`) = 약 410초 < 10분. 진행 중인 판이 점검 전에 끝난다. 점검 시작 때 못 끝난 판은 기존 방치 정리(`RUN_STALE_SECONDS`)와 같은 경로로 `abandoned`가 된다(보상 없음).

### 4.2 단계별 동작

| 요청 | `scheduled` | `pre_block` | `active` |
|---|---|---|---|
| `POST /auth/dev/login`, `POST /auth/steam` | 허용 | `503 MAINTENANCE_PENDING` | `503 MAINTENANCE` |
| `POST /auth/refresh` | 허용 | **허용**(접속 중인 사람의 토큰 갱신이 끊기지 않게) | `503 MAINTENANCE` |
| 새 판: `POST /characters/{uuid}/dungeon-runs`, `.../party/start`, `.../match/queue`, `.../match/fill-ai` | 허용 | `503 MAINTENANCE_PENDING` | `503 MAINTENANCE` |
| 이미 모인 판: `.../party-runs/{id}/join`, `/begin`, 진행 중 처치·결과 보고 | 허용 | 허용 | `503 MAINTENANCE` |
| 그 밖의 REST(상점·강화·경매·우편 등) | 허용 | 허용 | `503 MAINTENANCE` |
| `GET /health*`, `GET /meta`, `POST /auth/logout` | 허용 | 허용 | **허용**(예외) |
| WebSocket 새 연결(`hello`) | 허용 | 허용(재연결) | 업그레이드 거절(`503`) |
| 관리자 listener | 항상 | 항상 | 항상 |

- 차단 응답: `503`, `Retry-After: min(남은 초, 300)`, `errors = { code: "MAINTENANCE" | "MAINTENANCE_PENDING", starts_at, ends_at, notice }`, `message`는 "서버 점검 중입니다. HH:mm(KST)에 종료될 예정입니다." 형태의 한국어 문장.
- 구현은 라우터 앞의 미들웨어 하나(`maintenanceGuard`)이고 위 표의 "새 판" 경로 목록은 한 곳에 상수로 둔다. 버전 검사(426)보다 **앞**에 둔다(점검 중에는 업데이트 안내보다 점검 안내가 먼저).
- 점검 중에도 틱(경매 정산·매칭)은 계속 돈다. 정산은 결과만 우편에 쌓고 접속을 요구하지 않으므로 해롭지 않다. 서버를 내릴 때는 어차피 멈춘다.

### 4.3 기존 채팅·`bye`로 알리기 (phase5 3절 확장)
- 서버가 만드는 문장만 쓴다(운영자가 쓰는 부분은 `notice` 100자와 방송 한 줄뿐). 시각은 KST 변환 함수 하나(`resetBoundaries`와 같은 모듈)로 만든다.

| 시점 | `chat.sys` 문장 | 
|---|---|
| 시작 30·10·5·1분 전(`MAINT_ANNOUNCE_MINUTES`) | `서버 점검이 {N}분 뒤({HH:mm})에 시작됩니다. 예상 종료 {HH:mm}.` + `notice`가 있으면 ` {notice}` |
| `block_login_at` | `지금부터 새 던전 입장과 새 로그인이 제한됩니다. 진행 중인 던전은 끝까지 하실 수 있습니다.`(기본값에서는 10분 전 문장과 같은 시각이라 한 줄로 합친다) |
| `starts_at` | `서버 점검을 시작합니다. 잠시 후 접속이 종료됩니다.` -> `MAINT_BYE_DELAY_SECONDS`(3초) 뒤 `bye` |

- `bye` 프레임: `{ t: "bye", code: 1001, reason: "MAINTENANCE", reconnect: true, retry_after_ms: clamp(ends_at - now, 30초, 10분) + 0~30초 무작위 }`. 기존 close 코드 1001(`GOING_AWAY`)을 그대로 쓰고 `reason`만 구별한다(구 클라이언트도 `reconnect`·`retry_after_ms`를 따른다). 클라이언트는 재연결 전에 `GET /meta`의 `maintenance`를 보고 안내 화면을 띄운다(4.6).
- 공지 타이머는 점검 창이 바뀔 때마다(예약·연장·취소) 다시 계산한다. 재시작으로 이미 지난 시점은 건너뛴다. 이미 보낸 시점은 메모리 집합(창 uuid + 분)으로 기억한다(서버가 여러 대가 되면 각자 자기 세션에 보낸다).
- **즉시 방송**(MN7): 점검과 무관한 긴급 공지(`chat.sys` 한 줄)를 운영자가 직접 보낸다. 10초에 한 번, 100자, 제어 문자 제거.
- `RealtimeNotifier`에 `systemBroadcast(text: string)`를 더한다(11절). 기존 `systemLine(characterUuid, text)`는 캐릭터 한 명용이다.

### 4.4 `/meta` 확장
`GET /meta`(인증 없음, 점검 중에도 응답)에 `maintenance`를 더한다.
```
"maintenance": null | { "phase": "scheduled" | "pre_block" | "active",
                        "block_login_at": ISO, "starts_at": ISO, "ends_at": ISO, "notice": "..." },
"auth": { "dev_login_enabled": false, "dev_register_enabled": false }     // dev_register_enabled 추가
```
`server_time`이 이미 있어 클라이언트가 자기 시계를 믿지 않고 남은 시간을 계산한다.

### 4.5 정상 종료(SIGTERM) 순서
`init: true` + `CMD node`(F1) 위에서 `shutdown`을 아래로 바꾼다. 전체 제한 `SHUTDOWN_GRACE_SECONDS`(30초), 넘으면 `exit(1)`.
1. 종료 중 플래그 켬: `/health/ready`는 503(Caddy가 새 요청을 보내지 않는다), 새 REST 요청은 `503 SHUTTING_DOWN`(+`Retry-After: 5`), 새 WebSocket 업그레이드 거절. `/health/live`와 `/meta`는 계속 응답.
2. 틱·JobRunner에 정지 신호(진행 중인 배치는 현재 배치만 끝내고 멈춘다).
3. 진행 중인 요청을 최대 10초 기다린다(요청 카운터).
4. 모든 WebSocket에 `bye`: 점검 창이 `active`면 `reason=MAINTENANCE`(`retry_after_ms`는 4.3), 아니면 `GOING_AWAY`(`retry_after_ms` 3~8초 무작위, 기존). 채팅 쓰기 큐 비우기(`chatWriter.drain`).
5. 진행 중인 틱·작업이 끝나기를 최대 10초 기다린다.
6. `server.close()` + `closeIdleConnections()`, LISTEN 연결 종료, 풀 종료, `exit(0)`.
종료 로그 한 줄에 각 단계의 걸린 시간을 남긴다(`shutdown.done`).

### 4.6 클라이언트(Unity)에 닿는 것
`/meta.maintenance` 표시(타이틀 배너, 점검 중 "접속" 버튼 비활성화와 종료 예정 시각), `bye.reason == "MAINTENANCE"`이면 재연결 대신 안내 화면 후 30초마다 `/meta` 확인, `503`의 `errors.code`가 `MAINTENANCE`·`MAINTENANCE_PENDING`·`SHUTTING_DOWN`일 때 한국어 안내(`message` 그대로), `mails`의 `kind=system`과 `system_code`(`compensation`/`event`/`refund`/`notice`)별 우편 문구, `bye` 코드 `4011 KICKED`(5.8 PL6) 처리(재연결 허용). 게임 코드 수정은 이 문서 범위 밖이다(메인이 한다).

## 5. 관리자 도구

### 5.1 CLI를 권고하는 이유
| 기준 | CLI | 웹 페이지 |
|---|---|---|
| 작업량·공격면 | 서버 안에 이미 있는 `argon2`·zod·pg만 쓴다. 프런트 빌드, 정적 서빙, 세션 쿠키, CSRF·XSS 방어가 없다 | 위 전부를 만들고 지켜야 한다 |
| 네트워크 노출 | SSH로 들어와 컨테이너 안에서만 실행. **공개 포트 추가 없음** | 어떤 경로로든 공개 주소가 생긴다(터널·VPN·IP 제한 추가) |
| 운영자 수 | 1~2명(개발자). 할 일이 "목록 보기 -> 처리" 형태라 표 출력으로 충분하다 | 비개발자나 3명 이상이면 이점이 생긴다 |
| 자동화 | 점검 절차(`maint schedule` -> `drain` -> 배포)를 스크립트에 넣을 수 있다 | 어렵다 |
| 약점 | 신고 증거 줄 읽기가 덜 편하다(pager와 `--json`으로 보완) | |

결론: **CLI**. 관리자 API(5.7)는 웹 페이지가 나중에 얹힐 수 있게 CLI와 분리된 REST로 만든다(운영자가 비개발자이거나 3명 이상이 되면 같은 API 위에 정적 페이지를 얹는다. 그때 `ADMIN_BIND`·프록시 접근 제한을 다시 설계한다).
CLI는 `node:util` `parseArgs`만으로 만든다(새 의존성 없음). 비밀번호·TOTP는 프롬프트로 받고(인자·환경변수·셸 이력에 남기지 않는다) 세션 토큰은 `~/.dotrpg-admin/session`(권한 0600, 서버 만료와 같은 수명)에 둔다. 모든 명령에 `--json`.

### 5.2 접근 방식
- 관리자 listener는 `createAdminApp()`이 만든 별도 Express 앱이고 **같은 프로세스**에서 `ADMIN_BIND:ADMIN_PORT`로 listen한다(WebSocket 세션·점검 상태·틱에 직접 닿아야 하기 때문이다). 공개 앱의 `versionCheck`, 게임 속도 제한 미들웨어는 지나지 않고 관리자 전용 미들웨어(출처 CIDR 검사, 토큰, 역할, 속도 제한, 감사)를 쓴다. `Cache-Control: no-store`.
- 운영자: `ssh 호스트 -t docker compose exec api node dist/admin/cli.js <명령>`. 컨테이너 안에서 `127.0.0.1:3001`에 붙는다(SSH가 네트워크 인증이고, 아래가 두 번째 요소다).
- 출처 제한: `ADMIN_ALLOWED_CIDRS`(기본 loopback)에 없으면 `403`. 누군가 실수로 포트를 publish해도 막힌다.
- 첫 관리자: `docker compose exec api node dist/admin/bootstrap.js create-owner <login_id>`가 DB에 직접 owner를 만들고 임시 비밀번호를 한 번 출력한다(감사 `bootstrap.create_owner`). 같은 도구에 `reset-totp <login_id>`(기기 분실). 셸 접근은 곧 서버 장악이라 이 둘은 API 인증을 요구하지 않는다.

### 5.3 인증
- **계정**: `admin_users`. 게임 `accounts`·JWT와 완전히 분리(서로의 토큰·비밀번호가 통하지 않는다). 아이디 소문자 3~32자.
- **비밀번호**: argon2id, 최소 12자. 새 계정은 임시 비밀번호 + `must_change_password`.
- **2FA(필수)**: TOTP(RFC 6238, 30초, 6자리, 앞뒤 1구간 허용). `node:crypto`의 HMAC으로 구현(의존성 없음). 비밀키는 `ADMIN_SECRET_KEY`로 AES-256-GCM 암호화해 저장. 같은 코드 재사용은 `totp_last_step`으로 막는다.
- **첫 로그인 흐름**: 비밀번호만 맞고 `must_change_password`이거나 TOTP 미등록이면 `scope='setup'` 세션을 준다(AU4·AU5·AU6만 호출 가능, 그 밖은 `403 SETUP_REQUIRED`). 비밀번호 변경 + TOTP 등록(`otpauth://` URI를 CLI가 터미널에 QR 대신 URI·비밀키로 출력)이 끝나야 `full` 세션을 준다.
- **세션**: 불투명 토큰(32바이트 난수), DB에는 SHA-256만. 유휴 `ADMIN_SESSION_IDLE_MINUTES`(30), 절대 `ADMIN_SESSION_MAX_HOURS`(8). 비활성화·역할 변경·비밀번호 변경 때 그 관리자의 모든 세션을 폐기.
- **잠금**: 연속 실패 `ADMIN_LOGIN_FAIL_MAX`(5)회 -> `ADMIN_LOCK_MINUTES`(15)분 잠금(없는 아이디도 같은 응답과 비슷한 시간으로, 더미 해시 검증). 로그인 전체에 분당 20회 상한(출처가 loopback이라 IP별 한도는 의미가 없다).
- **IP 제한**: 5.2의 listener 바인딩 + CIDR 검사. 공개 인터넷에 관리자 API가 없다는 것이 IP 제한의 실체다.

### 5.4 역할과 권한

| 기능 | viewer | operator | owner |
|---|---|---|---|
| 계정·캐릭터·원장·감시·경제 요약·점검 상태·작업 상태 조회 | O | O | O |
| 신고 목록(증거 줄 제외) | O | O | O |
| 신고 증거 줄·보류 판 상세(채팅 원문·판 기록) | | O | O |
| 메모·검토 확인, 접속 끊기(kick) | | O | O |
| 제재 생성·해제 (경고·채팅 금지, 정지 **최대 `ADMIN_OPERATOR_BAN_MAX_DAYS`일**) | | O | O |
| 영구 정지 | | | O |
| 신고 처리, 보류 판 해제·거절 | | O | O |
| 점검 예약·취소·연장·시작·종료, 방송 | | O | O |
| 운영 지급(`ADMIN_GRANT_*` 한도) | | | O |
| 개발용 계정 발급·비밀번호 재설정 | | | O |
| 관리자 계정 관리, 감사 로그 조회, 작업 수동 실행 | | | O |

운영자가 1~2명이면 둘 다 owner여도 된다. 역할이 있는 이유는 "조회만 하는 사람"과 "되돌릴 수 없는 일(영구 정지, 지급)"을 나누기 위해서다. 두 사람 승인 규칙은 이 규모에서는 두지 않는다.

### 5.5 감사 로그
- 모든 관리자 요청(health 제외)이 `admin_audit_log`에 한 줄이다.
  - **변경 작업**: 작업과 **같은 트랜잭션**에서 `result='ok'` 행(+ `response`, `request_id`)을 쓴다. 작업은 되고 기록은 없는 경우가 없다.
  - **거절·검증 실패·권한 부족·로그인 실패**: 롤백되지 않도록 별도 트랜잭션으로 `denied`/`invalid`/`error`.
  - **민감 조회**(계정 상세, 캐릭터 상세, 신고 증거, 보류 판 상세, 원장 조회)도 `account.view` 같은 행으로 남긴다(누가 어떤 계정을 봤는지).
- `params`에는 허용 목록 필드만(사유 코드, 기간 프리셋, 메모 길이 등). 비밀번호·TOTP·토큰·채팅 원문은 저장하지 않는다.
- 추가 전용(트리거), 지우지 않는다(작은 표). `GET /admin/audit`(owner)로 관리자·대상·행동·기간 필터.

### 5.6 공통 규칙
- 응답 형식은 기존과 같다: `{ success, message, data, meta? }` / `{ success: false, message, errors? }`. 외부 id는 uuid만(내부 bigint 비노출).
- 모든 변경 `POST`는 본문에 `request_id`(uuid). 멱등성은 `admin_audit_log`의 `(admin_id, request_id)` 유일(성공 행)로 하고, 같은 id의 재전송은 저장된 `response`를 `Idempotent-Replay: true`로 돌려준다. 같은 id + 다른 본문(정리한 `params`가 다름)은 `422 IDEMPOTENCY_MISMATCH`. CLI가 요청마다 새 uuid를 만든다.
- 속도 제한(관리자별): 조회 `ADMIN_RATE_PER_MIN`(120)/분, 변경 30/분, 원장 조회 30/분, `broadcast` 10초에 1회, 작업 수동 실행 작업당 1분에 1회.
- 공통 에러: `400 VALIDATION`, `401 ADMIN_TOKEN_MISSING|ADMIN_TOKEN_INVALID|ADMIN_TOKEN_EXPIRED`, `403 FORBIDDEN_ROLE`, `403 SETUP_REQUIRED`, `403 ORIGIN_DENIED`, `404 NOT_FOUND`, `422 IDEMPOTENCY_MISMATCH`, `429 RATE_LIMITED`.
- **개발용 계정과 가입**: Steam 연동 전 외부 시험(결정 대기 5)에서는 `AUTH_DEV_ENABLED=true`, `ALLOW_DEV_AUTH_IN_PRODUCTION=true`, `AUTH_DEV_REGISTER_ENABLED=false`로 두고 **운영자가 PL7로 발급한 계정만** 로그인한다(공개 가입 없음).

### 5.7 엔드포인트 요약 (관리자 44개 + 공개 변경 3개)
경로 접두 `/admin`, 인증은 관리자 세션 토큰. 역할 열은 5.4의 최소 역할.

| # | 메서드 | 경로 | 역할 | 하는 일 |
|---|---|---|---|---|
| AU1 | POST | `/admin/auth/login` | 없음 | 로그인(아이디·비밀번호·TOTP) |
| AU2 | POST | `/admin/auth/logout` | 모두 | 세션 폐기 |
| AU3 | GET | `/admin/me` | 모두 | 내 정보·역할·세션 만료 |
| AU4 | POST | `/admin/auth/password` | 모두(setup 가능) | 비밀번호 변경 |
| AU5 | POST | `/admin/auth/totp/start` | 모두(setup 가능) | TOTP 비밀키 발급(미확인 상태) |
| AU6 | POST | `/admin/auth/totp/confirm` | 모두(setup 가능) | 첫 코드 확인으로 등록 완료 |
| AM1 | GET | `/admin/admins` | owner | 관리자 목록 |
| AM2 | POST | `/admin/admins` | owner | 관리자 생성(임시 비밀번호 1회 표시) |
| AM3 | POST | `/admin/admins/{uuid}/actions` | owner | 역할 변경·비활성화·활성화·2FA 초기화·잠금 해제·비밀번호 초기화 |
| AM4 | GET | `/admin/audit` | owner | 감사 로그 조회 |
| PL1 | GET | `/admin/accounts` | viewer | 계정 찾기(`q`) |
| PL2 | GET | `/admin/accounts/{uuid}` | viewer | 계정 상세(요약 판) |
| PL3 | GET | `/admin/characters/{uuid}` | viewer | 캐릭터 상세 |
| PL4 | GET | `/admin/characters/{uuid}/ledger` | viewer | 원장 조회(골드·아이템·경험치) |
| PL5 | POST | `/admin/accounts/{uuid}/notes` | operator | 메모 / 검토 확인(`review_ack`) |
| PL6 | POST | `/admin/accounts/{uuid}/kick` | operator | 접속 끊기 |
| PL7 | POST | `/admin/accounts/dev` | owner | 개발용 계정 발급 |
| PL8 | POST | `/admin/accounts/{uuid}/dev-password` | owner | 개발용 비밀번호 재설정 |
| SA1 | POST | `/admin/accounts/{uuid}/sanctions` | operator | 제재 생성(경고·채팅 금지·정지) |
| SA2 | POST | `/admin/sanctions/{uuid}/revoke` | operator | 제재 해제 |
| RP1 | GET | `/admin/reports` | viewer | 신고 대기열 |
| RP2 | GET | `/admin/reports/{uuid}` | operator | 신고 상세(증거 줄 포함) |
| RP3 | POST | `/admin/reports/{uuid}/take` | operator | 검토 시작(`reviewing`) |
| RP4 | POST | `/admin/reports/{uuid}/resolve` | operator | 기각 또는 제재와 함께 처리 |
| HR1 | GET | `/admin/held-runs` | viewer | 보류 던전 판 대기 목록 |
| HR2 | GET | `/admin/held-runs/{uuid}` | operator | 보류 판 상세 |
| HR3 | POST | `/admin/held-runs/{uuid}/release` | operator | 보류 해제(보상 확정) |
| HR4 | POST | `/admin/held-runs/{uuid}/reject` | operator | 보류 거절(보상 없음으로 확정) |
| WT1 | GET | `/admin/anomalies` | viewer | `anomaly_log` 조회 |
| WT2 | GET | `/admin/auction-flags` | viewer | `auction_flags` 조회 |
| WT3 | GET | `/admin/watchlist` | viewer | 의심 계정 순위 |
| EC1 | GET | `/admin/economy/daily` | viewer | 일일 골드·경험치·경매 요약 |
| EC2 | POST | `/admin/grants` | owner | 운영 지급(system 우편) |
| EC3 | GET | `/admin/grants` | viewer | 운영 지급 이력 |
| MN1 | GET | `/admin/maintenance` | viewer | 점검 상태·열린 창·최근 이력 |
| MN2 | POST | `/admin/maintenance` | operator | 점검 예약 / 즉시 시작 |
| MN3 | POST | `/admin/maintenance/{uuid}/cancel` | operator | 시작 전 취소 |
| MN4 | POST | `/admin/maintenance/{uuid}/extend` | operator | 종료 연장 |
| MN5 | POST | `/admin/maintenance/{uuid}/end` | operator | 점검 종료 |
| MN6 | GET | `/admin/maintenance/drain` | viewer | 드레인 상태(`ready_to_stop`) |
| MN7 | POST | `/admin/broadcast` | operator | 즉시 방송(`chat.sys`) |
| OP1 | GET | `/admin/ops/status` | viewer | 서버 상태 스냅샷 |
| OP2 | GET | `/admin/ops/jobs` | viewer | 작업 목록·마지막 실행·정합성 결과 |
| OP3 | POST | `/admin/ops/jobs/{name}/run` | owner | 작업 수동 실행(허용 목록만) |

공개 서버의 변경 3개: `GET /health/live`, `GET /health/ready`(신규, `/health`는 `ready`의 별칭으로 유지), `GET /meta`에 `maintenance`·`auth.dev_register_enabled` 추가.

### 5.8 핵심 엔드포인트 상세
요청은 모두 zod `.strict()`이고 변경 `POST`는 `request_id: uuid`를 가진다(아래에서 생략). 금액·확률·보상량을 **플레이어가** 보내는 경로는 없다. 아래의 숫자는 운영자 입력이고 서버 한도(`ADMIN_*`)와 프리셋으로 다시 검사한다.

**AU1 로그인** `POST /admin/auth/login`
- 요청: `{ login_id: string(3..32), password: string(1..200), totp?: string(/^\d{6}$/) }`
- 처리: 아이디 조회(없거나 비활성화면 더미 argon2 검증 후 같은 `401 AUTH_FAILED`) -> 잠금이면 `423 ADMIN_LOCKED`(`errors.locked_until`) -> 비밀번호 검증 -> setup이 필요하면 `scope=setup` 세션 -> 아니면 TOTP 필수(`401 TOTP_REQUIRED` / 틀리면 `401 AUTH_FAILED`, 실패 횟수 +1) -> 성공이면 `failed_count=0`, `last_login_at`, 세션 생성. 모든 실패는 감사(`auth.login`, `denied`).
- 응답 `200` `data`: `{ token, expires_at, idle_timeout_minutes, scope: "full"|"setup", must_change_password, admin: { id: uuid, login_id, display_name, role } }`

**PL1/PL2 계정 찾기·상세**
- `GET /admin/accounts?q=` 한 가지 형식만 받는다: `uuid`(계정 또는 캐릭터), `name:캐릭터이름`(대소문자 무시, 삭제된 캐릭터 포함), `steam:<steam_id64>`, `dev:<아이디>`. 최대 20건. 인덱스: `auth_identities (provider, subject)` UNIQUE, `characters_name_alive`, uuid UNIQUE. 삭제된 캐릭터 이름 검색은 인덱스가 없어 전체 스캔(규모가 작아 허용).
- 상세 `data`: 계정(uuid, 생성·마지막 로그인, `banned_until`, 로그인 수단), 캐릭터 목록(uuid, 이름, 직업, 레벨, 골드, 삭제 여부), 활성 제재 + 최근 20건, 이 사람에 대한 신고(상태별 개수 + 최근 5건) / 이 사람이 한 신고(건수, 기각 비율: 허위 신고자 판별), `anomaly_log` 24시간·7일 종류별 개수와 최대 심각도, `auction_flags` 7일, 보류 판 수, 최근 7일 체결(건수·금액·상대 계정 수), 메모 최근 10건, 최근 관리자 행동 5건, 점수(WT3). 인덱스: `anomaly_log_account_time`, `auction_flags_account_time`, `reports_target`, `reports_reporter`, `account_sanctions_history`, `admin_account_notes_account`, `admin_audit_target`.

**PL4 원장 조회** `GET /admin/characters/{uuid}/ledger`
- 쿼리: `kind: "gold"|"item"|"xp" = "gold"`, `reason?`, `since?: ISO`, `before?: 커서`, `limit?: int(1..100) = 50`. 커서는 원장 `id`(최신순 키셋 페이지). `meta.next_before`. 인덱스 `*_ledger_char_idx (character_id, id)`. 외부에는 `id` 대신 응답 줄 순번과 `request_id`·`ref`를 준다(내부 키 비노출 규칙의 예외인 커서는 불투명 문자열로 감싼다).
- 용도: "이 캐릭터의 골드가 왜 이만큼인가", 복사 의심 시 `request_id`로 한 요청이 만든 줄 묶음(`gold_ledger_request_idx`).

**SA1 제재 생성** `POST /admin/accounts/{uuid}/sanctions`
- 요청: `{ kind: "warning"|"chat_mute"|"ban", reason_code: "abuse"|"spam"|"scam_ad"|"cheat"|"other", duration?: "1h"|"1d"|"7d"|"30d"|"permanent", report_id?: uuid, note: string(1..500) }`. 기간은 프리셋만(임의 시각을 받지 않는다). `warning`은 `duration` 없음, `chat_mute`는 `permanent` 불가, `ban`은 필수. 영구 정지는 owner, operator의 정지는 `ADMIN_OPERATOR_BAN_MAX_DAYS`일 이하(`403 FORBIDDEN_ROLE`).
- 처리(한 트랜잭션, 계정 행 `FOR UPDATE`로 정지 변경을 직렬화): `account_sanctions` INSERT(`source='admin'`, `created_by=login_id`, `report_id` 연결, `starts_at=서버 now`, `ends_at`) -> `ban`이면 `accounts.banned_until = max(기존 활성 정지 종료, 이번 ends_at)`(영구는 `9999-12-31T00:00:00Z`, `infinity` 금지: F9) -> `report_id`가 `open`/`reviewing`이면 `actioned` + `handled_by` -> 감사 행. 커밋되면 5단계의 `notify_account_sanction` 트리거가 `pg_notify`로 접속 중 세션에 `sanction` 프레임(정지면 4003 끊기)을 보낸다(코드 추가 없음).
- 응답 `201` `data`: `{ sanction: { id, kind, reason_code, ends_at }, banned_until: ISO|null }`. 에러: `404 ACCOUNT_NOT_FOUND`, `404 REPORT_NOT_FOUND`, `409 SANCTION_EXISTS`(같은 종류가 더 긴 기간으로 이미 활성: 늘리려면 해제 후 다시), `409 REPORT_CLOSED`, `422 BAD_DURATION`.
- **SA2 해제**: `{ note }`. `revoked_at`·`revoked_by`. 정지면 `banned_until`을 **남은 활성 정지 중 가장 늦은 종료**로 다시 계산(없으면 NULL).

**RP2/RP4 신고**
- RP1 쿼리: `state?: "open"|"reviewing" = "open,reviewing"`, `reason?`, 최대 50건, 오래된 순(`reports_queue`). 항목: `id, reason, target_name, line_count, created_at, state, 같은 대상에 대한 열린 신고 수`.
- RP2 상세: 신고 + `report_lines`(접수 때 서버가 찍은 증거, `is_target` 강조) + 대상의 제재 이력 + 같은 대상의 다른 신고(최근 30일) + 신고자의 기각 비율. 감사 `report.view`.
- RP4 `{ decision: "dismiss"|"sanction", note: string(1..500), sanction?: { kind, reason_code, duration }, close_similar?: boolean = true }`: `reports` 상태가 `open`/`reviewing`일 때만(`409 REPORT_CLOSED`). `sanction`이면 SA1과 같은 내부 함수로 제재를 만들고 이 신고를 연결한다. `close_similar`는 같은 대상·같은 사유의 다른 열린 신고를 같은 결과로 닫는다(`note`에 병합 표시). `handled_by`, `handled_at`. 신고자에게는 결과를 알리지 않는다(5단계 결정 유지).

**HR1~HR4 보류 판**
- 대기 목록 = `dungeon_runs.state='held'` 이고 `held_run_reviews`에 행이 없는 판, 오래된 순(`dungeon_runs_held`). 항목: `id, 던전·난이도, 캐릭터 이름, hold_reason(쉼표 목록), 종료 시각, party 여부, 대기 시간`.
- HR2 상세: 판 기록(`stats`, `room_kills`, 서버 경과, 시작·종료), 보류 사유별 설명(`TOO_FAST` 등)과 임계값(`minClearSeconds`), 처치 요약(`kill_log` 7일 안일 때만), 같은 시각의 `anomaly_log(dungeon_result/party_result)`, 파티 판이면 방장 보고(`party_run_host_reports`)와 **같은 판 다른 멤버의 상태**(보류가 방장 이상치 때문인지 보기 위해), 이 계정의 최근 보류 이력(반복 여부). 파티 판의 멤버별 행은 각각 따로 결정한다.
- **HR3 해제** `{ note: string(1..500) }`: 한 트랜잭션: 캐릭터 행 `FOR UPDATE`(3단계 `EconCtx`) -> 판 행 잠금 -> `state='held'`이고 검토 기록이 없어야 한다(`409 RUN_NOT_HELD`) -> **기존 `finalizeCleared`를 그대로 호출**(랭크 점수, 레이드 보상 잠금·청구, 클리어 경험치 `xp_ledger('dungeon_clear')`, 카드 굴림, 레이드 열쇠). 검증(`validateClear`)은 운영자가 대신 판단했으므로 건너뛴다. 시간 점수는 `scoredSeconds = max(stats.elapsed_ms/1000, minClearSeconds)`로 하한을 둬 불가능한 시간으로 최고 랭크를 받는 일을 막고, 콤보·부활은 난이도 한도(`revives`)와 `elapsed/attackCooldown`으로 자른다. `ended_at`을 해제 시각으로 옮겨(`prev_ended_at`은 검토 기록에 보관) 카드 선택 시간이 해제부터 시작한다 -> `held_run_reviews('released')` -> 감사. 응답 `200` `data`: `{ result: "cleared", rank, granted_xp, card_count, reward_locked?, lock_reason? }`. 레이드의 `ALREADY_CLAIMED`·`KEYS_MISSING`·`TOO_FEW_HUMANS`는 평소처럼 적용돼 `reward_locked`로 끝날 수 있다.
  - 플레이어에게 알림: 접속 중이면 `chat.sys` 한 줄("보류되었던 던전 보상이 확정되었습니다. 카드를 선택해 주세요."), 접속 중이 아니면 접속 때 `GET /characters/{uuid}/dungeons`가 **카드 미선택 판 목록**을 주어야 카드 창을 띄울 수 있다. 이 목록이 지금 있는지 구현을 확인하고 없으면 응답에 `unpicked_runs: [{ run_id, ended_at }]`을 더한다(11절 D5).
- **HR4 거절** `{ note }`: `held_run_reviews('rejected')`만 기록(판은 `held`로 남는다). 반복·고의 의심이면 SA1을 따로 쓴다.
- 에러: `404 RUN_NOT_FOUND`, `409 RUN_NOT_HELD`, `404 CHARACTER_NOT_FOUND`.

**WT1~WT3 이상 기록·감시 목록**
- WT1 `anomaly_log`: `account?`, `kind?`, `min_severity?`, `since?`, `before?`(커서), 최신순 50건. 인덱스 `anomaly_log_account_time`, `anomaly_log_severe`, `anomaly_log_created_idx`.
- WT2 `auction_flags`: `account?`, `kind?`, `since?`. 인덱스 `auction_flags_account_time`, `auction_flags_time`.
- **WT3 의심 계정 순위**: 최근 `WATCHLIST_WINDOW_HOURS`(24) 안의 `anomaly_log` + `auction_flags` + 보류 판을 계정별로 모아 점수 = 심각도 1 -> 1점, 2 -> 3점, 3 -> 10점, 보류 판 1건 -> 10점. 합이 `WATCHLIST_MIN_SCORE`(10) 이상인 계정을 점수순 20건. **계정의 마지막 `review_ack`(PL5, `kind='review_ack'`) 이후 기록만** 센다(검토한 계정이 매일 다시 올라오지 않게). 항목: `account, 점수, 종류별 개수, 마지막 발생, 활성 제재 여부, 마지막 검토 시각`. 이것이 "이상 기록 검토"의 작업 대기열이다: 보고 -> 필요하면 SA1 -> `review_ack`.

**EC1 일일 경제 요약** `GET /admin/economy/daily?day=`
- `day`는 게임 일(06:00 KST 시작) 날짜. 경계는 `resetBoundaries` 함수 하나로 계산(기본: 어제). 응답: 골드 유입·유출을 `reason`별 합, 경험치 `reason`별 합, 아이템 변동 상위 `reason`, 경매 체결 건수·금액·수수료·소각(`auction_sinks`)·쌍 한도 도달 횟수, 신규 계정, 접속한 계정 수(`last_login_at`). 직전 7일 중앙값 대비 배율을 함께 주어 급증(예: 골드 유입 3배)이 눈에 띄게 한다. 인덱스: 원장 `*_created_brin`(날짜 범위 스캔).

**EC2 운영 지급** `POST /admin/grants` (owner)
- 요청: `{ character_id: uuid, system_code: "compensation"|"event"|"refund"|"notice", gold?: int(1..ADMIN_GRANT_MAX_GOLD), item?: { item_key: string(ITEM_KEY_RE), count: int(1..ADMIN_GRANT_MAX_ITEM_COUNT) }, memo: string(1..200) }` (`gold`나 `item` 중 하나 이상). 요청의 숫자는 **운영자 입력**이며 서버가 한도·존재(`items.json`)를 다시 검사한다.
- 처리(한 트랜잭션, 대상 캐릭터 행 `FOR UPDATE`): 캐릭터 존재(삭제 안 됨, `404 CHARACTER_NOT_FOUND`) -> 관리자별 일일 합계 `SUM(gold) WHERE admin_id AND created_at >= 게임 일 시작` + 이번 <= `ADMIN_GRANT_DAILY_GOLD`(`422 GRANT_LIMIT`, `admin_grants_admin_time`) -> `mails` INSERT(`kind='system'`, `system_code`, `character_id`, `gold`, 첨부 아이템, 귀속은 서버가 정함: 장비는 `character`, 재료·소모품은 `none`, `expires_at=now+mailDays`) -> 아이템이 있으면 `item_ledger('admin_grant', location='mail', delta=+count, balance_after=count, ref=mail uuid)` -> `admin_grants` INSERT -> 감사. 커밋 뒤 `NotificationPublisher`로 `mail.arrived`(접속 중이면 알림).
- 골드는 우편이 들고 있다가 수령할 때 평소처럼 `gold_ledger('mail_claim')`로 들어간다. 골드 보존식(phase6 9.3)의 `SUM(mails.gold WHERE kind='system')` 항이 이 지급을 설명하므로 `SUM(admin_grants.gold)`와 같아야 한다(정합성 점검 I3).
- 응답 `201`: `{ grant: { id, character_id, gold, item, system_code }, mail_id }`. **회수(차감) 기능은 만들지 않는다**: 잘못 지급했거나 사고로 얻은 재화는 정지(SA1) + 개별 대응으로 처리하고, 캐릭터 잔액을 직접 고치는 경로를 만들지 않는다.

**MN1~MN7 점검** (4절의 상태 모델)
- MN2 `POST /admin/maintenance`: `{ start_in_minutes?: int(0..10080), starts_at?: ISO, duration_minutes: int(5..720), notice?: string(0..100) }`(`start_in_minutes`와 `starts_at` 중 하나). `start_in_minutes=0`은 즉시 시작(긴급 점검: `block_login_at=starts_at=now`). `block_login_at = max(now, starts_at - MAINT_PRE_BLOCK_MINUTES)`. 열린 창이 있으면 `409 MAINTENANCE_EXISTS`(`errors.id`). 응답 `201` `data`: `{ window: {...}, marks: [ISO...] }`(공지가 나갈 시각 목록).
- MN3 취소: 시작 전(`now < starts_at`)만, 이미 시작했으면 `409 MAINTENANCE_STARTED`(그때는 MN5). MN4 연장: `{ extend_minutes: int(5..240) }`(`ends_at` 증가). MN5 종료: `state='ended'`, `closed_by`, 즉시 `none`.
- MN6 드레인 `data`: `{ phase, in_flight_requests, ws_sessions, running_ticks: { match, settle, auction, jobs }, db_active_queries, playing_runs, ready_to_stop }`. `ready_to_stop = phase==active && in_flight==0 && ws_sessions==0 && running_ticks 모두 0 && playing_runs==0`. `playing_runs`는 `RUN_STALE_SECONDS`를 넘기지 않은 `playing` 판.
- MN7 `POST /admin/broadcast`: `{ text: string(1..100) }` -> `systemBroadcast`. 응답 `{ delivered: n }`(접속 세션 수).

**PL6 접속 끊기** `POST /admin/accounts/{uuid}/kick`: 그 계정의 WebSocket 세션에 `bye(4011, KICKED, reconnect:true)`. 접속을 막지 않는다(막으려면 SA1 정지). 상태를 고친 뒤 클라이언트를 다시 접속시킬 때 쓴다. 신규 close 코드 `4011`을 `wsProtocol.ts`에 더한다.

**PL7 개발용 계정 발급** (owner): `{ login_id: string(3..32, 소문자) }`. `AUTH_DEV_ENABLED=false`면 `409 DEV_AUTH_DISABLED`. 서버가 임시 비밀번호(난수 16자)를 만들어 argon2id로 `auth_identities(provider='dev')`에 저장하고 **응답에 한 번만** 돌려준다(감사에는 남기지 않는다). **PL8**은 같은 방식의 재설정.

**OP1 상태 스냅샷** `data` 필드는 8.2의 지표 표와 같다. **OP2**는 작업별 마지막 실행·마지막 성공·마지막 상태와 최근 정합성 점검 결과(`job_runs.detail`). **OP3** `{ }`: 허용 목록(`purge-hourly`, `purge-daily`, `stale-runs`, `integrity-nightly`)만, 이미 실행 중이면 `409 JOB_RUNNING`.

### 5.9 CLI 명령 대응

| 명령 | 엔드포인트 |
|---|---|
| `login`, `logout`, `whoami`, `passwd`, `totp enroll` | AU1~AU6, AU3 |
| `admins list/add/role/disable/enable/reset-2fa/unlock/reset-password`, `audit` | AM1~AM4 |
| `account find <q>`, `account show <uuid>`, `char show`, `char ledger <uuid> --kind gold --since 24h` | PL1~PL4 |
| `account note`, `account ack`, `account kick`, `account dev-create`, `account dev-reset` | PL5~PL8 |
| `sanction add --kind chat_mute --for 1d --reason abuse [--report <uuid>] --note ...`, `sanction revoke` | SA1, SA2 |
| `report list/show/take/resolve --dismiss|--sanction ...` | RP1~RP4 |
| `held list/show/release/reject` | HR1~HR4 |
| `watch list`, `anomalies`, `flags` | WT3, WT1, WT2 |
| `economy daily`, `grant add`, `grant list` | EC1~EC3 |
| `maint status/schedule/cancel/extend/end/drain`, `broadcast "..."` | MN1~MN7 |
| `ops status`, `ops jobs`, `ops run <job>` | OP1~OP3 |

## 6. 데이터 정리 작업

### 6.1 JobRunner
- 프로세스 안의 작은 스케줄러(`server/src/ops/jobRunner.ts`). 작업 = `{ name, schedule, run(ctx) }`. 스케줄은 "매 N분" 또는 "매일 HH:mm KST"(KST 변환은 `resetBoundaries`와 같은 모듈의 함수 하나). 모든 작업에 시작 시각 무작위 지연 0~60초를 둔다(여러 대가 될 때 겹침 방지).
- **한 번에 한 곳에서만**: 작업마다 `pg_try_advisory_lock(hashtext('job:' || name))`. 못 잡으면 기록 없이 건너뛴다(서버가 여러 대가 돼도 안전, `AUCTION` 틱과 같은 방식).
- **기록**: 시작 때 `job_runs(status='running')`, 끝나면 `ok`/`failed`와 `rows_affected`, `detail`(표별 행 수·배치 수·ms). 기동할 때 `running`으로 남은 행은 `failed`(`error='interrupted'`)로 닫는다.
- **동시 실행 제한**: 작업은 한 번에 하나만 돈다(큐). 풀 최대 10 중 작업이 연결 하나만 쓰므로 요청을 굶기지 않는다.
- **종료 신호**(4.5): 배치 사이에서 확인하고 현재 배치를 끝낸 뒤 멈춘다.
- 기존 1초 틱(매칭·파티 정산)과 경매 틱(60초)은 그대로 두고 `ops` 상태(마지막 실행·지연)만 노출한다.

### 6.2 배치 규칙 (모든 정리에 공통)
- 한 문장 `DELETE ... WHERE id IN (SELECT id ... LIMIT PURGE_BATCH)`(기본 5000)를 **각각 자기 트랜잭션**(자동 커밋)으로 반복. 이미 `chat_messages` 정리가 쓰는 방식이다.
- 배치마다 `SET LOCAL statement_timeout='30s'`, `SET LOCAL lock_timeout='2s'`. 잠금 시간 초과는 그 배치를 건너뛰고 다음 실행에서 이어간다(요청을 막지 않는다).
- 배치 사이 `PURGE_BATCH_SLEEP_MS`(100ms) 대기. 작업 하나의 최대 `JOB_MAX_SECONDS`(300초), 남으면 다음 실행에서 계속한다(정리는 멱등).
- **자식 -> 부모 순서**: `drops` -> `kill_log`(FK `ON DELETE CASCADE`가 `drops_kill_idx`로 빠르게 동작). 부모를 먼저 지우면 CASCADE가 행마다 일한다.
- 정리 대상 선택은 기존 인덱스를 쓴다(아래 표). 새 인덱스가 필요한 곳은 0008이 만들었다.
- 새벽 배치는 KST 04:10~04:40(06:00 초기화 직전의 한가한 시간대). 시간당 작업은 매시 :17.

### 6.3 보관과 정리 일정

**지우는 것**

| 대상 | 조건(보관) | 작업·주기 | 사용 인덱스 | 비고 |
|---|---|---|---|---|
| `request_log` | `created_at < now - REQUEST_LOG_TTL_DAYS(7)` | `purge-hourly` | `request_log_created_idx` | 기존 정리를 배치로 바꾼다(F12) |
| `chat_messages` | `CHAT_RETENTION_DAYS(7)` | `purge-hourly` | `chat_messages_created` | 기존 `purgeChatMessages` |
| `party_invites` | 7일 | `purge-hourly` | `party_invites_created` | 기존 |
| `drops` | `expires_at < now - DROP_RETENTION_DAYS(1)` | `purge-hourly`(먼저) | `drops_expires_idx` | 수령·만료 구별 없이 만료가 지난 행 |
| `kill_log` | `KILL_LOG_RETENTION_DAYS(7)` | `purge-hourly`(drops 다음) | `kill_log_created_idx` | `kill_stats`(평생 누적)와 무관 |
| `anomaly_log` | 심각도 1: `ANOMALY_RETENTION_DAYS(30)`, 심각도 2 이상: `ANOMALY_SEVERE_RETENTION_DAYS(180)` | `purge-daily` | `anomaly_log_created_idx` | 심각한 기록은 제재 근거라 길게 둔다(phase3의 "30일"을 심각도별로 바꿈) |
| `friendships`(끝난 행) / `blocks`(해제 행) | 90일 | `purge-daily` | 작은 표 전체 스캔 | 기존 `purgeChatData` |
| `report_lines` | 신고가 닫힌 뒤 `REPORT_RETENTION_DAYS(180)` | `purge-daily` | `reports` 조인 | 기존. 열린 신고의 증거는 유지 |
| `party_applications`(끝난 행) | `PARTY_RECORD_RETENTION_DAYS(30)` | `purge-daily` | 작은 표 전체 스캔 | |
| `party_members`(나간 행) | 30일 | `purge-daily` | 작은 표 전체 스캔 | 아무도 FK로 가리키지 않는다. 나간 뒤 알림은 최근 기록만 읽는다 |
| `refresh_tokens` | 만료·폐기 후 `REFRESH_TOKEN_PURGE_DAYS(30)` | `purge-daily` | `refresh_tokens_expires_idx` | |
| `mails`(수령된 일반 우편) | `claimed_at < now - MAIL_CLAIMED_RETENTION_DAYS(180)` 이고 `kind <> 'system'` | `purge-daily` | `mails_claimed_idx` | 소각 기록이 가리키는 폐기 우편과 `admin_grants`가 가리키는 system 우편은 남긴다 |
| `admin_sessions` | 만료·폐기 후 30일 | `purge-daily` | `admin_sessions_expires` | |
| `job_runs` | `JOB_RUN_RETENTION_DAYS(90)` | `purge-daily` | `job_runs_started` | |

**지우지 않는 것과 이유**

| 대상 | 이유 |
|---|---|
| `gold_ledger`, `item_ledger`, `xp_ledger`, `enhance_log` | 추가 전용 트리거 + 사후 추적·복사 조사의 근거(PLAN §5). 크기는 6.4 |
| `auction_trades`, `auction_sinks`, `auction_flags`, `auction_price_daily` | 추가 전용 트리거(F10). 쌍 한도·시세·소각 집계·보존식의 근거 |
| `auction_listings`, `auction_bids`, 폐기된 우편 | `auction_trades`·`auction_sinks`·`mails`가 FK로 가리킨다 |
| `dungeon_runs` | 난이도 해금·최고 랭크·퀘스트 "요일 던전 클리어 n회" 검증이 `cleared` 행을 읽는다(평생 필요). `party_runs`·`party_run_members`·`party_run_host_reports`·`parties`는 `dungeon_runs`·`chat_messages` FK로 묶여 함께 남는다(phase4의 "30일"은 이 이유로 취소) |
| `reports`, `account_sanctions`, `admin_*`, `held_run_reviews`, `maintenance_windows` | 이력이 곧 기록이다. 행 수가 작다 |
| `kill_stats`, `character_*`, `quest_claims`, `raid_claims` | 현재 상태 |

다른 곳의 보관 정책 문서(phase3 10, phase4 14, phase6 3, `schema.sql` 머리말)는 이 표가 정정한다.

### 6.4 크기 가정과 확인
원장은 영구 보관이라 가장 크게 자란다. **가정**: 활성 사용자 1명이 하루 약 3,000행(처치·드롭 줍기·채집)을 만들면 100명에서 하루 30만 행, 한 해 약 1억 행, 인덱스 포함 행당 약 150바이트로 약 15GB다. 이 값은 추측이므로 비공개 시험(CBT)에서 `pg_total_relation_size`로 실측해 정한다(8.2의 DB 크기 증가율, 결정 대기 4). 원장에 BRIN `created_at` 인덱스(0008)를 둔 것은 일일 집계·정합성 점검이 표 전체를 읽지 않게 하기 위해서다.

### 6.5 방치 상태 정리 (`stale-runs`, 10분마다)
phase4가 "요청 때 지연 처리"로 둔 전이를 같은 UPDATE로 한 번에 훑는다(상태가 보이게 하고 방치 행이 쌓이지 않게): `dungeon_runs` `playing`이 `RUN_STALE_SECONDS`를 넘으면 `abandoned`, `party_runs`의 오래된 `gathering`/`playing` 정리, `parties`의 `PARTY_IDLE_MINUTES` 초과 해산. 구현은 기존 지연 처리 함수를 `sweepStale()`로 꺼내 재사용한다(새 규칙을 만들지 않는다).

## 7. 백업과 복구

### 7.1 목표

| 항목 | 값 | 비고 |
|---|---|---|
| RPO(잃을 수 있는 최대 시간) | 7단계 완료: **24시간**(일일 덤프). 정식 출시 전(경제가 실제로 도는 시점): **1분 이내**(WAL 아카이브) | 골드·아이템이 사실상 재산이라 출시 전에 7.2 B가 필요하다 |
| RTO(복구 시간) | **60분 이내** | 매월 훈련에서 실측해 기록한다(7.5) |
| 보관 | 일일 14개, 주간 8개, 월간 6개 | 오프사이트 |

### 7.2 방식

**A. 논리 백업(필수)**: 매일 KST 04:10 호스트 cron이 `pg_dump -Fc --no-owner`(일관된 스냅샷, 낮은 우선순위 `nice`/`ionice`) -> `age`로 암호화 -> 오프사이트 S3 호환 저장소에 업로드(`rclone`) -> 성공 시 `OPS_HEARTBEAT_URL`에 별도 백업 핑. 업로드 후 `pg_restore --list`로 목록이 읽히는지 확인한다. 장점: 어떤 업체·어떤 PostgreSQL 버전·관리형 DB로도 복원된다(업체 이전 때도 이 파일을 쓴다). 보관 14일 + 일요일분 8주 + 매월 1일분 6개월.

**B. WAL 아카이브 + 주기 기본 백업(정식 출시 전)**: `archive_mode=on`, `archive_timeout=60`, `pgBackRest`(`archive-push`)로 오프사이트에 WAL을 보내고 일요일 04:30 전체 + 평일 차등 백업. 시점 복구(PITR)로 "배포 직전", "잘못된 운영 작업 직전"으로 돌아갈 수 있다. 공식 `postgres:16` 이미지에는 `pgbackrest`가 없어 `postgres:16` 위에 설치한 작은 이미지를 쓴다(`server/ops/postgres.Dockerfile`).
- `pg_stat_archiver`의 `failed_count` 증가 또는 `last_archived_time`이 10분을 넘으면 알림(8.3). 아카이브가 막히면 `pg_wal`이 디스크를 채우므로 디스크 알림과 함께 본다.

**관리형 DB를 쓸 때**: 업체의 PITR(7일 이상)을 켜고, A는 그대로 api 호스트에서 외부로 `pg_dump`한다(업체 락인 방지, 업체의 백업이 업체와 함께 사라지는 사고 대비). 업체 스냅샷만으로는 백업이 아니다.

**함께 백업할 것**: `.env`(비밀번호 관리자에 별도 보관), `server/ops/*`와 `Caddyfile`(git에 있음), 이미지 태그 목록. 게임 데이터 JSON은 git·이미지에 있다. Caddy 인증서(`caddy_data`)는 없어도 재발급되므로 백업 대상이 아니다.

### 7.3 암호화와 접근
- 모든 백업은 `age` 공개키로 암호화한다(서버에는 공개키만). 개인키는 오프라인 2곳. 저장소 쓰기 키는 삭제 불가 권한(객체 잠금·버전 관리를 지원하면 켠다)이라 서버가 털려도 과거 백업을 지울 수 없다.
- 백업 안에는 `request_log`(응답 본문), `chat_messages`(귓속말), `auth_identities`(비밀번호 해시)가 들어 있다. 백업은 운영 DB와 같은 수준의 개인정보로 취급한다.

### 7.4 복구 절차

**R1. 덤프에서 복구(전체)**
1. 점검 창 즉시 시작(`maint schedule --in 0m ...`), `maint drain`, `docker compose stop api`.
2. 현재 DB를 이름만 바꿔 보존(`ALTER DATABASE dotrpg RENAME TO dotrpg_broken_<날짜>` 또는 볼륨 보존). 지우지 않는다.
3. 새 DB 생성, 복호화 -> `pg_restore -d dotrpg --no-owner --exit-on-error`.
4. 정합성 점검(I1~I5, 8.4)을 `ops run integrity-nightly`로 돌려 불일치 0을 확인한다.
5. api를 `AUCTION_TICK_ENABLED=false`로 먼저 기동해 `/health/ready`와 관리자 `ops status`를 본 뒤 틱을 켠다(경매 `ends_at`이 이미 지난 건이 한꺼번에 정산된다).
6. 복구 시점 이후의 플레이어 손실은 **운영 지급(우편)** 으로 보상한다(표 직접 수정 금지). 보상 대상은 복구 전 DB(2번에서 보존)와 복구된 DB의 차이를 SQL로 뽑아 EC2로 지급한다.
7. 점검 종료.

**R2. 시점 복구(PITR, B가 있을 때)**: `pgbackrest restore --type=time --target='<KST 시각>' --target-action=promote`를 **새 데이터 디렉터리**에 하고 R1의 4~7. 대상 시각은 "문제의 요청이 일어나기 직전"을 감사 로그·`request_log`로 정한다.

**R3. 한 계정만 되돌리기**: 훈련용 DB(7.5)에 백업을 복구해 필요한 행(원장·소지품)을 SQL로 확인하고, 운영 DB에는 **우편 지급(EC2)** 으로만 되돌려 준다. 운영 표를 직접 INSERT/UPDATE하지 않는다(원장 규칙).

### 7.5 복구 훈련 (백업은 복구해 봐야 백업이다)
- **매월**: 최신 덤프를 훈련용 컨테이너(개발 PC도 가능)에 복구한다. 확인: ① `pg_restore` 종료 코드 0 ② 표별 행 수가 운영의 `reltuples`와 ±5% 이내 ③ 정합성 점검 I1~I5 불일치 0 ④ `schema_migrations` 마지막 이름이 저장소의 마지막 파일과 같음 ⑤ 운영과 같은 이미지를 `NODE_ENV=production`, `AUCTION_TICK_ENABLED=false`, `ADMIN_ENABLED=false`로 붙여 `/health/ready` 200 ⑥ 걸린 시간 기록(RTO). 결과는 `server/ops/DRILL_LOG.md`에 한 줄.
- **분기마다**: 복호화 키 확인(오프라인 사본으로 한 파일 복호화), PITR(B) 복구 1회.
- **알림**: 마지막 훈련이 35일을 넘으면 경고(8.3).
- 훈련용 DB에는 게임 계정 실데이터가 있으므로 훈련 뒤 삭제하고 외부에 올리지 않는다.

## 8. 모니터링

### 8.1 헬스 체크
| 경로 | 인증 | 확인 | 응답 | 쓰는 곳 |
|---|---|---|---|---|
| `GET /health/live` | 없음 | 이벤트 루프가 응답하는가(DB 안 봄) | 200 `{ status: "ok" }` | 프로세스 감시. 죽으면 재시작 |
| `GET /health/ready` | 없음 | ① DB `SELECT 1` 2초 이내 ② `schema_migrations` 행 수 = 이미지의 마이그레이션 파일 수(60초 캐시) ③ 게임 데이터 로드됨 ④ WebSocket 연결됨 ⑤ 종료 중 아님 | 정상 200 `{ status: "ok" }`, 아니면 503 `{ status: "degraded", checks: { db, schema, gamedata, shutting_down } }` | Caddy `health_uri`, compose `healthcheck`, 외부 업타임 점검 |
| `GET /health` | 없음 | `ready`와 같다 | | 호환(기존 compose·스크립트) |
- 점검 중(`active`)에도 `ready`는 200이다(프로세스는 건강하고, 플레이어에게는 앱이 `503 MAINTENANCE` JSON을 준다). 종료 중(SIGTERM)만 503.
- 요청 로그에서 제외(기존). 속도 제한도 제외(기존). 공개 응답에는 버전·가동 시간·내부 사정을 싣지 않는다(자세한 값은 관리자 OP1).

### 8.2 지표 (OP1과 `ops.snapshot` 로그)
프로세스 안 메모리 카운터(재시작하면 0)이고 `OPS_SNAPSHOT_SECONDS`(60)마다 `ops.snapshot` pino 로그 한 줄로 내보낸다. Prometheus 서버를 따로 두지 않는다(한 대 서버에서 수집할 대상이 하나뿐이고, 알림은 아래 감시자가 맡는다). 확장 1에서 `GET /metrics`를 더하는 것은 같은 카운터를 노출하면 된다.

| 그룹 | 지표 |
|---|---|
| 프로세스 | `uptime_s`, `rss_mb`, `heap_mb`, `eventloop_delay_p99_ms`(`perf_hooks.monitorEventLoopDelay`), `version`(이미지 태그), `data_version` |
| HTTP | 최근 1분 `requests`, `status_2xx/4xx/5xx`, `p50/p95/p99_ms`(전체), `in_flight`, 오류 코드별 상위 5(`errors.code`) |
| WebSocket | `sessions`, `max`(`WS_MAX_CONNECTIONS`), 사유별 close 수(`IDLE`, `SLOW_CONSUMER`, `FLOOD`...), 핸드셰이크 거절 수 |
| DB | 풀 `total/idle/waiting`, 쿼리 `p95_ms`(풀 래퍼), 앱의 `pg_stat_activity` 연결 수·`max_connections` 대비 %, DB 크기와 큰 표 상위 5의 크기(5분마다) |
| 틱·작업 | 매칭·정산 틱 마지막 실행 시각·소요 ms, 경매 틱 `lag_seconds`·마지막 성공, 작업별 `last_ok_at`·상태 |
| 큐 | 매칭 대기 인원, 열린 신고 수와 가장 오래된 나이, 보류 판 대기 수와 가장 오래된 나이, 미통보 제재 수(5분마다 DB 조회) |
| 점검 | 단계, 남은 시간 |

로그 기준(pino JSON, 이미 `request`·`auction.settled` 등이 있다): 요청 로그에 `req_id`(프록시의 `X-Request-Id` 또는 서버가 생성)와 `account`(uuid, 인증된 경우)를 더하고, 5xx는 `unhandled error`에 `req_id`를 붙인다. 로그 이벤트 이름 규칙: `auction.settle_failed`, `auction.tick_lag`, `job.failed`, `ws.close`, `shutdown.done`, `alert.sent`. 로그는 docker `json-file`(50MB x 10)에 있고 `docker compose logs`/`jq`로 본다. 외부 수집기는 확장 1에서(10명 안팎 운영에서는 필요 없다).

### 8.3 알림
**감시자(watchdog)**: 프로세스 안에서 매분 8.2의 값을 규칙으로 평가하고 `ALERT_WEBHOOK_URL`로 보낸다(`ALERT_WEBHOOK_FORMAT=discord|slack|json`). 같은 알림 키는 `ALERT_MIN_INTERVAL_MINUTES`(30) 안에 반복하지 않고 해소되면 한 번 알린다. **서버가 죽으면 감시자도 죽으므로** 다음 둘이 따로 있다: ① 외부 업타임 점검(아무 업체의 무료 등급, `https://도메인/health/ready`를 1분마다, 2회 연속 실패 시 이메일·웹훅) ② **하트비트**: 감시자가 매분 `OPS_HEARTBEAT_URL`에 핑하고 외부 서비스가 핑이 끊기면 알린다(프로세스·호스트·네트워크 사망 모두 한 장치로 감지). 호스트 스크립트(`server/ops/host-check.sh`, cron 5분)가 디스크·메모리·docker 상태·WAL 아카이브 상태·백업 하트비트를 같은 웹훅으로 보낸다.

| 등급 | 조건(시작값) | 의미·조치 |
|---|---|---|
| **긴급** | `ready` 2회 연속 실패 | 서비스 중단. 로그·DB·디스크 확인 |
| 긴급 | 5xx 비율 > 2% (5분, 요청 20건 이상) | 새 배포·DB 문제 의심. 롤백 검토 |
| 긴급 | 풀 `waiting >= 5` 1분 | DB 포화·잠금 대기. `pg_stat_activity`·`pg_locks` 확인 |
| 긴급 | **정합성 점검 불일치 >= 1** (8.4) | 재화 복사·원장 우회 의심. 즉시 조사(경제 사고의 첫 신호) |
| 긴급 | 디스크 사용 >= 90% 또는 WAL 아카이브 실패·10분 지연 | 데이터 파일·`pg_wal` 포화 위험 |
| 긴급 | 일일 백업 하트비트 26시간 끊김 | 백업 중단 |
| 경고 | `p95_ms` > 1000 (5분) 또는 이벤트 루프 지연 p99 > 200ms (5분) | 느려짐. 무거운 쿼리·작업 확인 |
| 경고 | 풀 `waiting > 0` 1분, DB 연결 > `max_connections`의 80% | |
| 경고 | 디스크 >= 80%, 메모리 RSS > 제한의 80%, 재시작 횟수 증가 | |
| 경고 | 경매 `lag_seconds` > 180 (> 600이면 긴급) | 정산 지연(결과는 틀리지 않는다) |
| 경고 | 작업 `purge-hourly` 마지막 성공 > 3시간, `purge-daily`/`integrity-nightly` > 30시간, `stale-runs` > 1시간 | 정리 정지 |
| 경고 | WebSocket 접속 >= `WS_MAX_CONNECTIONS`의 80%, `SLOW_CONSUMER` close > 10/분 | 용량·네트워크 |
| 경고 | 로그인 실패 > 100/5분(크리덴셜 스터핑), `anomaly_log` 심각도 3이 10분에 10건 이상 | 공격·부정 행위 |
| 경고 | 보류 판 대기가 24시간 넘음, 열린 신고 가장 오래된 것 48시간 | 운영 처리 지연 |
| 경고 | 골드 유입(일일 합)이 직전 7일 중앙값의 3배 초과 | 경제 이상(8.4와 별개로 일일 요약에서) |
| 경고 | 마지막 복구 훈련 35일 초과, TLS 인증서 만료 14일 이내(호스트 점검) | |
임계값은 시작값이고 운영하며 조정한다(상수는 `ops/alertRules.ts` 한 곳).

### 8.4 정합성 점검 (`integrity-nightly`, 매일 KST 04:30, `ops run`으로도 실행)
경제 사고를 가장 빨리 알려 주는 감시다. 비용이 활동량에 비례하도록 "지난 25시간 안에 원장 변동이 있던 캐릭터"만 본다(`*_ledger_created_brin`). 매주 일요일에는 전체 캐릭터.

| # | 점검 | 불일치의 뜻 |
|---|---|---|
| I1 | `characters.gold` = 그 캐릭터 `gold_ledger`의 마지막 `balance_after` = `SUM(delta)`. 그리고 원장 체인 연속(`balance_after - delta` = 직전 행의 `balance_after`, `LAG`) | 원장 없는 골드 변경 |
| I2 | `(character_id, location, item_key)`별 `item_ledger` `delta` 합 = `character_items.count` 합(귀속 합산, `worn` 포함) | 원장 없는 아이템 변경, 복사 |
| I3 | phase6 9.3 골드 보존식(경매 이유 원장 + 우편 + 소각) 좌우 일치. 좌변의 `SUM(mails.gold WHERE kind='system')` = `SUM(admin_grants.gold)`. 아이템 버전: `location in (auction, mail)`별 원장 합 = 진행 중 `auction_listings.count` + 미수령 `mails.count` | 경매·우편 중 재화 증발·복사 |
| I4 | 막힌 상태: 마감이 10분 넘게 지난 `active` 경매, 만료가 하루 넘게 지난 미수령 우편, `RUN_STALE_SECONDS`의 2배를 넘긴 `playing` 판, 2시간 넘은 활성 `party_runs` | 틱·정리 실패 |
| I5 | 정지 일관성: 활성 `ban` 제재가 있는데 `accounts.banned_until`이 없거나 그 반대 | 제재 반영 누락 |
결과는 `job_runs.detail`에 항목별 불일치 건수와 표본(최대 20건: 캐릭터 uuid, 기대·실제). 건수 > 0이면 긴급 알림. 점검은 읽기 전용이다(고치는 쿼리를 자동으로 실행하지 않는다).

## 9. 출시 전 보안 점검표

### 9.1 모의·개발 모드 가드 (기동 거부, 운영에서 반드시)
`loadConfig`(`config/env.ts`)에 아래를 더한다. 실패는 기존처럼 `환경변수 검증 실패: ...`로 던져 **기동하지 않는다**(F4~F6).

| # | 규칙 | 근거 |
|---|---|---|
| G1 | `NODE_ENV=production`이면 `STEAM_AUTH_MODE`는 `web_api`만(`off`·`mock` 거부. 단 Steam 연동 전 시험 기간은 `off` 허용 + G3) | mock 티켓으로 임의 Steam ID 로그인(F4) |
| G2 | 운영이면 `PARTY_TRANSPORT=steam` (Steam 연동 전 시험 기간만 `dev` 허용 + G3) | 같은 PC 연결용 전송 |
| G3 | 운영에서 `AUTH_DEV_ENABLED=true`는 `ALLOW_DEV_AUTH_IN_PRODUCTION=true`가 함께 있을 때만. 이때 기동 로그에 경고 배너를 매시간 남기고 `/meta`가 `dev_login_enabled`를 알린다 | 실수로 켠 개발 로그인 |
| G4 | 운영에서 `AUTH_DEV_REGISTER_ENABLED` 기본 `false`(공개 가입 차단). `/auth/dev/register` 라우트는 이 값과 `AUTH_DEV_ENABLED`가 모두 참일 때만 등록 | 공개 서버의 계정 폭주 |
| G5 | 운영에서 `STEAM_APP_ID`가 `480`이면 거부 | Valve 시험용 앱 |
| G6 | 운영에서 `TRUST_PROXY >= 1`(프록시 구성) / `JWT_SECRET` 자리표시 값·반복 문자 거부 / `ADMIN_ENABLED=true`면 `ADMIN_SECRET_KEY`(32바이트) 필수 / `ADMIN_BIND`가 `0.0.0.0`이면 거부(`ADMIN_ALLOWED_CIDRS` 명시 필요) | F6, 관리자 노출 |
| G7 | 경매 자격 값 0 거부(이미 있음) | 기존 |
- 테스트 전용 주입(`setClockOverride`, `setRng`)은 프로세스 코드에서 `NODE_ENV=production`일 때 호출하면 던지게 한다(운영에서 시계·난수를 바꾸는 경로를 닫는다).

### 9.2 점검표 (출시 전에 전부 확인, 확인 방법 포함)

| # | 항목 | 확인 방법 |
|---|---|---|
| 1 | G1~G7이 동작한다 | 각 값을 틀리게 해 기동이 거부되는지 |
| 2 | 외부에서 80/443만 열려 있다 | 다른 네트워크에서 `nmap -p 1-65535 <IP>` 결과가 80/443만. 3000·3001·5432 거부 |
| 3 | HTTP가 HTTPS로 리다이렉트되고 인증서 체인이 유효하다 | `curl -I http://도메인`, `openssl s_client`, Unity 클라이언트 실접속 |
| 4 | `X-Forwarded-For` 위조로 속도 제한을 우회하지 못한다 | 외부에서 헤더에 다른 IP를 넣어 로그인 실패를 한도 이상 보내 `429`가 걸리는지(Caddy가 헤더를 덮어쓰는지 확인) |
| 5 | WebSocket이 `wss://`로 붙고 점검·`bye`가 동작한다 | 4.3, 13절 시나리오 |
| 6 | 속도 제한 값이 아래 표와 같다 | `.env`와 `env.ts` 기본값 대조 |
| 7 | 관리자 listener가 어디에도 공개되지 않았다 | 외부 스캔, `docker compose ps`의 포트 열 |
| 8 | 관리자 2FA·잠금·감사가 동작한다 | 13절 A9 |
| 9 | 비밀값이 저장소·이미지·로그에 없다 | `git log -p`에서 키 패턴 검색, `docker history`, 로그 `grep` |
| 10 | `.env` 권한 0600, 호스트 SSH 키 전용·루트 로그인 끔 | |
| 11 | 서버 시계가 맞다(NTP 동기) | `chronyc tracking`, `/meta.server_time`과 외부 시각 대조 |
| 12 | 백업이 돌고 복구해 봤다 | 7.5 첫 훈련 완료 |
| 13 | 알림이 실제로 온다 | 일부러 `ready`를 실패시켜 웹훅·외부 업타임 알림 수신 |
| 14 | 개인정보: 처리방침에 채팅 7일(귓속말 포함)·접속 로그 보관이 적혀 있다 | 결정 대기 6 |
| 15 | 부하 확인 | 100 WebSocket 접속 + 처치·줍기·경매 요청을 반복하는 스크립트로 CPU·메모리·풀 대기·p95 확인(3.2의 postgres.conf 값을 조정) |
| 16 | 마이그레이션 `up`을 빈 DB와 운영 사본 DB 둘 다에서 돌려 봤다 | 훈련용 DB |

### 9.3 Steam 키
- `STEAM_WEB_API_KEY`: Steamworks에서 발급한 키. **서버 `.env`에만** 둔다(클라이언트·저장소·이미지·로그·채팅·이슈에 넣지 않는다). pino `redact`에 추가. 서버가 시작 로그에 키를 찍지 않는다.
- 개발용과 운영용 앱·키를 분리한다(개발은 Valve 시험용 480 + 시험 키, 운영은 실제 `STEAM_APP_ID` + 운영 키). 운영에서 480 거부(G5).
- `STEAM_IDENTITY`(`dotrpg-server`)는 클라이언트가 `GetAuthTicketForWebApi`로 티켓을 만들 때 쓰는 문자열과 같아야 한다(어긋나면 모든 로그인이 실패하므로 점검 배포 때 확인).
- 유출이 의심되면 Steamworks에서 키를 재발급하고 `.env`를 교체해 api를 재시작한다(재시작만으로 반영, 접속자는 재연결).
- 티켓 재사용 방지는 서버 메모리(4단계)라 재시작 직후 짧은 틈이 생길 수 있다. 티켓 유효 시간이 짧다는 전제이며 점검 창에서는 영향이 없다.

### 9.4 속도 제한 값 (출시 시점의 값과 결론)
프록시 뒤에서 `TRUST_PROXY=1`로 IP 키가 제대로 구해진다는 전제다(점검표 4).

| 대상 | 값(현재 기본) | 결론 |
|---|---|---|
| `POST /auth/dev/login` | IP당 20/분, 아이디당 실패 5/15분 | 유지 |
| `POST /auth/dev/register` | IP당 5/시간 | 운영은 라우트가 꺼져 있다(G4) |
| `POST /auth/steam` | IP당 20/분(`RATE_STEAM_IP_MAX`) | 유지. PC방·공유기를 고려해 올릴 수 있다 |
| `POST /auth/refresh` | 계정당 10/분 | 유지 |
| 일반(`/meta` 등) | IP당 120/분 | 유지 |
| 3단계 경제 경로 | IP당 600/분 + 캐릭터별 초당 한도 | 유지 |
| 소셜(`/friends`, `/blocks`, `/reports`) | IP당 600/분 | 유지 |
| WebSocket | 핸드셰이크 IP당 60/분, 인증 전 IP당 20, 전체 500, 프레임 초당 10, 페이로드 4096B | 유지. `WS_MAX_CONNECTIONS`는 서버 메모리에 맞춰 부하 시험 후 확정 |
| 관리자 | 5.6 | 신규 |
| 요청 본문 | 앱 64KB, Caddy 128KB | 유지 |
- 메모리 구현이라 재시작하면 카운터가 0이 된다(점검·핫 배포 직후 짧은 틈은 허용). 서버가 여러 대가 되면 `RateLimitStore`를 Redis로 바꾼다(기존 인터페이스).
- 한 공유기 뒤의 여러 사용자가 로그인할 때 걸리면 `RATE_LOGIN_IP_MAX`·`RATE_STEAM_IP_MAX`만 올린다(계정별 한도가 실제 방어다).

### 9.5 그 밖의 코드·설정 확인
- 공개 앱에 관리자 경로가 없다(`/admin`은 404). 오류 응답에 스택·SQL이 없다(`errorHandler`가 이미 문장만 돌려준다).
- `x-powered-by` 제거(이미 `app.disable`). CORS는 게임 클라이언트가 브라우저가 아니므로 열지 않는다.
- JWT는 HS256 단일 비밀. 교체 절차는 3.4. 갱신 토큰은 해시만 저장하고 재사용 감지(`family_id`)가 있다.

## 10. 마이그레이션 0008 요약 (`server/migrations/0008_ops.sql`)

| 변경 | 이유 | 인덱스·조회 패턴 |
|---|---|---|
| `admin_users`, `admin_sessions` | 게임 계정과 분리된 관리자 인증, 세션 | 로그인 `login_id` UNIQUE, 토큰 `token_hash` UNIQUE(매 요청), 세션 폐기 `admin_sessions_admin`, 정리 `admin_sessions_expires` |
| `admin_audit_log`(추가 전용) | 누가 무엇을 했나, 변경 작업의 멱등성 기록 | `admin_audit_request_uq`(같은 요청 재전송), 최근순 `admin_audit_time`, 관리자별 `admin_audit_admin`, 대상별 `admin_audit_target`(이 계정을 누가 건드렸나) |
| `admin_account_notes`(추가 전용) | 운영 메모, 검토 확인(`review_ack`) | 계정 상세·감시 목록 `admin_account_notes_account` |
| `mails.system_code` + `mails_system_chk`, `item_ledger` reason `admin_grant` + `item_ledger_admin_grant_uq`, `admin_grants`(추가 전용) | 운영 지급을 system 우편으로만(잔액 직접 수정 경로 없음), 아이템 보존식 유지 | 지급 이력 `admin_grants_character`, 일일 한도 `admin_grants_admin_time` |
| `held_run_reviews`(추가 전용), `dungeon_runs_held` | 보류 판의 해제·거절 결정(한 판에 한 번), 대기 목록 | 대기 목록 `dungeon_runs_held`(보류 판만 담는 부분 인덱스) |
| `maintenance_windows` + `maintenance_one_open`(열린 창 하나), `maintenance_time` | 점검 상태의 정본 | 서버가 5초마다 열린 행 한 건을 읽음 |
| `job_runs` | 정리·점검 작업 실행 기록, "작업이 멈췄다" 알림, 정합성 결과 | `job_runs_job_time`(작업별 마지막 실행), `job_runs_started`(보관 정리) |
| 원장 BRIN `created_at` 3개 | 일일 집계·"오늘 변동 캐릭터" 정합성 점검이 표 전체를 읽지 않게 | 범위 스캔 전용. 크기가 수 KB |
| `drops_kill_idx` | `kill_log` 정리의 CASCADE가 처치마다 `drops` 전체 스캔 | F11 |
| `mails_claimed_idx`, `auction_sinks_mail_idx` | 수령된 우편 정리와 FK 확인 | |
| `anomaly_log_account_time`, `anomaly_log_severe` | 계정별 조회(상세·감시 목록), 심각도 2 이상 최신순 | |
| 5개 표 `autovacuum_*_scale_factor` | 삭제가 잦은 표의 죽은 행이 오래 남지 않게 | |
| `accounts.banned_until` 주석 | 영구 정지는 `9999-12-31`, `infinity` 금지(F9) | |

DOWN은 개발 DB 전용(운영 롤백은 3.6).

## 11. 구현 요청 목록 (dotrpg-backend-coder)

**A. 설정·기동 가드**: `config/env.ts`에 3.3의 신규 값과 9.1 G1~G7, `AUTH_DEV_REGISTER_ENABLED`로 `/auth/dev/register`만 별도 등록(`authRoutes.ts`), `/meta`에 `maintenance`·`dev_register_enabled`, 운영에서 `setClockOverride`/`setRng` 호출 금지, `DB_POOL_MAX`·`statement_timeout`·`idle_in_transaction_session_timeout` 풀 옵션, `logger` redact 추가.
**B. 이미지·compose·운영 파일**(`server/ops/`): Dockerfile(F1: `CMD node`, 비루트, `HEALTHCHECK`, `NODE_OPTIONS`), `compose.prod.yml`, `Caddyfile`, `postgres.conf`, `postgres.Dockerfile`(pgBackRest), `backup.sh`, `restore-drill.sh`, `deploy.sh`(3.6 B의 단계 스크립트화), `host-check.sh`, `DRILL_LOG.md`, `.env.example` 갱신, README(복구 절차 요약). 마이그레이션 러너에 `-- no-transaction` 마커 지원(3.5).
**C. 헬스·종료**: `/health/live`, `/health/ready`(8.1), `shutdown` 재작성(4.5, 요청 카운터, 전체 제한 시간), 점검 상태 보관(`maintenanceState`)과 `maintenanceGuard`(4.2) 미들웨어, WebSocket 업그레이드 거절, `bye(reason=MAINTENANCE)`.
**D. 앞 단계 코드에 닿는 곳**:
- D1 `RealtimeNotifier.systemBroadcast(text)`(`MemoryNotifier`는 `registry.all()`에 `chat.sys`).
- D2 `CLOSE.KICKED = 4011`(`wsProtocol.ts`)과 계정 세션을 끊는 함수(`registry.ofAccount`).
- D3 제재 생성 함수를 하나로 합친다(`sanctionService`의 자동 제재와 관리자 SA1이 같은 INSERT + `banned_until` 갱신 경로를 쓴다. `9999-12-31` 규칙).
- D4 `finalizeCleared`·`minClearSeconds`·`runContext`를 관리자 해제(HR3)가 쓸 수 있게 내보낸다(동작 변경 없음). 파티 판 멤버 행도 같은 함수.
- D5 접속 때 "카드 미선택 `cleared` 판"을 알 수 있는지 확인(`GET /characters/{uuid}/dungeons`의 `unpicked_runs`)하고 없으면 추가.
- D6 시스템 우편 생성 함수 `createSystemMail`(`mailRepository`): `kind='system'`, `system_code`, 첨부, `item_ledger('admin_grant')`, 커밋 후 `mail.arrived` 알림. 클라이언트가 `system_code`로 문구를 조립한다.
- D7 `sweepStale()`: 기존 지연 전이(던전 판·파티 판·파티 방치)를 일괄 함수로(6.5).
**E. 관리자**: `createAdminApp()`(`src/admin/`: routes·controller·service·repository·schema, 3계층 규칙), 출처 CIDR·토큰·역할·속도 제한·감사 미들웨어, TOTP(`node:crypto`), `ADMIN_SECRET_KEY` AES-256-GCM, `bootstrap.js`, `cli.js`(`node:util parseArgs`, 프롬프트, `--json`), 엔드포인트 44개(5.7). 감사의 "변경 = 같은 트랜잭션, 거절 = 별도 트랜잭션" 헬퍼 하나.
**F. JobRunner·작업**: `src/ops/jobRunner.ts`, `purge-hourly`, `purge-daily`, `stale-runs`, `integrity-nightly`, `maintenance-close`(6절·8.4), 기존 `server.ts`의 정리 `setInterval` 대체, `job_runs` 기록, 기동 시 `running` 정리.
**G. 감시**: `src/ops/metrics.ts`(카운터·히스토그램), `ops.snapshot` 로그, watchdog(`alertRules.ts`, 웹훅·하트비트), OP1~OP3.
**H. 테스트**(끝났다는 기준 13절): 점검 단계 전이(시계 주입), `maintenanceGuard` 허용/차단 표, 공지 시점 계산, 정리 작업(배치·FK 순서·보관 경계), 감사 같은 트랜잭션 보장, 관리자 권한 표, 멱등 재전송, 보류 해제 정확히 한 번(동시 두 번), 제재 -> WS 4003, 운영 지급 보존식, 정합성 점검이 일부러 만든 불일치를 잡는지.

## 12. 결정 대기

사용자 결정이 꼭 필요한 것만 둔다. 나머지는 위에서 추천값으로 정했다.

| # | 항목 | 선택지 | 추천 |
|---|---|---|---|
| 1 | **DB를 어디에 둘지** (서버 업체 선택 때 함께 정한다) | (a) 같은 머신의 PostgreSQL 컨테이너 + 오프사이트 백업(7.2 A, 출시 전 B) (b) 업체의 관리형 DB | (a). 100명 미만에서는 관리형이 서버 임대료와 비슷하거나 더 드는 경우가 많다(업체가 정해지면 견적 확인). 이 설계는 (b)로도 그대로 동작하므로(7.2, 3.2) 확장 1에서 옮긴다. 조건: 오프사이트 백업과 복구 훈련을 지킨다 |
| 2 | **오프사이트 백업 저장소** | (a) 서버 업체와 **다른 업체**의 S3 호환 객체 저장소 (b) 같은 업체의 저장소 | (a). 같은 업체·같은 계정이면 계정 정지·과금 사고·업체 장애에 백업이 함께 사라진다. 월 수 GB 규모라 비용은 작다. 계정과 결제 수단 준비가 필요하다 |
| 3 | **알림을 받을 곳** | (a) Discord/Slack 웹훅 + 외부 업타임 서비스의 이메일 (b) 이메일(SMTP)만 (c) 둘 다 | (a). 웹훅 하나면 앱·호스트·백업 알림을 한 채널로 모을 수 있고 폰 알림이 된다. 채널과 웹훅 URL을 만들어 주어야 한다 |
| 4 | **원장 보관 기간** (6.4의 크기는 추측) | (a) 영구 보관으로 시작, 디스크가 50%에 닿거나 연 예상 크기가 임계를 넘기면 그때 아카이브 설계 (b) 13개월 뒤 압축 보관 | (a). 사고 조사와 복사 추적이 원장에 달려 있고 100명 규모의 크기는 CBT에서 실측해 정할 수 있다. (b)는 추가 전용 트리거와 보존식 때문에 별도 설계가 필요하다 |
| 5 | **Steam 연동 전에 외부 시험(CBT)을 할지** | (a) 한다: 운영자가 발급한 개발용 계정 10명 이내(공개 가입 없음, 5.6) (b) 공개 가입을 연다 (c) 하지 않는다: Steam 연동 뒤에만 공개 | (a). 7단계 끝 기준("외부에서 접속된다")을 실제 접속으로 확인하고 비용(`ALLOW_DEV_AUTH_IN_PRODUCTION` 한 줄)이 작다. (b)는 계정 폭주·부정 행위 방어가 없는 상태라 위험하다 |
| 6 | **개인정보 처리방침·이용약관의 고지** | 채팅 7일 보관(귓속말 포함, phase5 결정 이월), 접속(IP) 로그 14일, 신고 증거 180일, 계정·캐릭터 삭제 시 처리 | 출시 전에 문구를 확정해야 한다. 법적 요건(보관 기간·동의 방식)은 사용자가 확인해야 하므로 이 문서는 값을 정하지 않는다. 제안: 접속 로그 14일, 위 보관 기간을 그대로 적기 |

도메인 이름 확보와 서버 업체 결정은 7단계 시작의 선행 조건이다(사용자 작업).

## 13. 끝났다는 기준 (시나리오)

PLAN §9의 "서버 한 대에 올려 외부에서 접속된다"를 아래로 구체화한다. 외부 네트워크의 PC에서 실제 클라이언트 또는 스크립트로 확인한다.

| # | 시나리오 | 확인할 것 |
|---|---|---|
| A1 | 외부에서 `https://도메인/meta`, `wss://도메인/ws` hello -> ready | 200, 인증서 체인 유효, `data_version` 일치, 로그인·캐릭터 목록 동작(개발용 또는 Steam) |
| A2 | 외부에서 IP로 3000·3001·5432·80의 평문 접속 | 3000·3001·5432 거부, 80은 HTTPS로 리다이렉트 |
| A3 | 가짜 `X-Forwarded-For`로 속도 제한 우회 시도 | 우회 불가(`429`가 같은 키로 걸림) |
| A4 | 점검 시나리오: `maint schedule` -> 30·10·5·1분 공지가 `chat.sys`로 도착 -> `pre_block`에서 새 로그인·새 던전 거절(`MAINTENANCE_PENDING`), 진행 중 판은 끝까지 -> `active`에서 `bye(MAINTENANCE)`와 503 -> `drain`이 `ready_to_stop` -> `maint end` -> 재접속 | 단계별 응답 코드·문구, `/meta.maintenance` |
| A5 | `docker compose stop api`(SIGTERM) | 30초 안에 `shutdown.done`, 종료 코드 0, 접속자가 `bye`를 받고 재연결, 진행 중 요청 유실 없음(F1 해결 확인) |
| A6 | 배포 B 전체 리허설(마이그레이션 포함, 롤백까지) | 3.6 모든 단계, 롤백 후 정상 |
| A7 | 백업 -> 빈 DB로 복구 -> 정합성 점검 -> api 기동 | 불일치 0, `/health/ready` 200, RTO 기록 |
| A8 | `purge-hourly`·`purge-daily` 수동 실행(`ops run`) | `job_runs` 기록, 행 수·시간, 실행 중 요청 p95 영향 없음, `drops`->`kill_log` 순서에서 CASCADE 느려지지 않음 |
| A9 | 관리자: 로그인(2FA), 잠금(5회 실패), viewer의 권한 거부, 신고 처리(제재와 함께), 제재 -> 접속 중 플레이어에게 `sanction`·4003, 보류 판 해제(정확히 한 번, 동시 두 번), 운영 지급 -> 우편 수령 -> 보존식 성립, 감사 로그 | 5.4 권한 표, 5.5 같은 트랜잭션, 멱등 재전송 |
| A10 | 일부러 `ready` 실패 / 하트비트 중단 / 정합성 불일치 주입 | 2분 안에 웹훅 수신, 외부 업타임 알림 |
| A11 | 9.1 가드와 9.2 점검표 전 항목 | 통과 |
| A12 | 100 접속 부하(점검표 15) | 풀 대기 0, p95 < 1초, 메모리 안정 |

## 14. PLAN_SERVER와 앞 단계 문서에서 달라진 점

| 대상 | 원래 | 7단계 설계 | 이유 |
|---|---|---|---|
| PLAN §6 배포 | `Docker Compose(api + postgres)` | + `caddy`(프록시·TLS), 마이그레이션을 별도 단계로, 관리자 listener가 같은 프로세스의 별도 포트 | 인터넷 공개와 점검 절차에 필요 |
| PLAN §9 7단계 | "서버 한 대에 올려 외부에서 접속된다" | 13절의 12개 시나리오로 구체화 | 측정 가능한 기준 |
| PLAN §7 | 시작 구성 | 변경 없음. "무중단 배포"는 하지 않고 짧은 점검 + 5~25초 핫 배포 | 상태가 프로세스 메모리에 있다(확장 1에서 가능) |
| phase3 10, phase4 14, phase6 3 보관 | `anomaly_log` 30일, `party_*` 30일, `auction_trades`·종료 listing 180일, `auction_flags` 90일 | `anomaly_log`는 심각도별(30/180일), `party_*`·경매 계열은 **지우지 않음** | 추가 전용 트리거·FK로 실행 불가(F10), `dungeon_runs`가 영구 필요 |
| phase3 3.2.4·9.5 "지급 보류 + 기록" | 관리자 도구가 해제 | 해제 = `finalizeCleared` 재사용 + 시간 점수 하한, 거절은 기록만 | 5.8 HR3 |
| phase5 10.3 SQL 절차 | 영구 정지에 `'infinity'` | `9999-12-31T00:00:00Z` | F9 |
| phase5 3절 `bye` | `GOING_AWAY` 3~8초 | 점검 사유 `reason=MAINTENANCE`와 긴 `retry_after_ms`, 새 close 코드 `4011 KICKED` | 점검·kick |
| phase6 12절 / mapping | `system` 우편은 7단계 도구가 만든다 | `mails.system_code` 추가, 운영 지급은 `admin_grants` + `item_ledger('admin_grant')` | 보존식 유지, 클라이언트 문구 |
| `/health` | 하나 | `/health/live`, `/health/ready`(+ `/health` 별칭) | F7 |
| `/meta` | 버전·시각·초기화·dev 로그인 | + `maintenance`, `dev_register_enabled` | 점검 안내 |

## 부록 A. 업체가 정해지면 하는 일 (업체 무관 단계 + 업체별로 확인할 것)

업체 무관 단계: ① 도메인을 사고 A 레코드를 이 머신의 고정 IP에 연결 ② 운영자 계정·SSH 키 설정, 비밀번호 로그인·루트 로그인 끔 ③ 방화벽(3.8)과 자동 보안 업데이트, NTP ④ Docker·compose 설치 ⑤ `server/ops/`와 `.env`(3.3·3.4)를 올리고 `compose.prod.yml`로 기동 ⑥ 첫 owner 생성(5.2) ⑦ 오프사이트 저장소와 백업 cron, 복구 훈련 1회(7.5) ⑧ 알림·업타임 설정(8.3) ⑨ 9.2 점검표 ⑩ 13절 시나리오.

업체를 고를 때 확인할 것(업체 이름은 쓰지 않는다):

| 항목 | 기준 |
|---|---|
| 사양 | 시작은 2 vCPU / 4GB RAM / SSD 40~80GB 정도(**추정**, 9.2의 부하 시험 결과로 조정). Linux x86_64(LTS 배포판) |
| 네트워크 | 고정 공인 IPv4, 인바운드 80/443/22을 막지 않음, 아웃바운드 제한 없음(Steam·ACME·백업) |
| 방화벽 | 업체 방화벽(보안 그룹)이 있으면 3.8과 같은 규칙을 한 번 더 건다 |
| 스냅샷 | 있으면 쓰되 **백업의 대체가 아니다**(DB 일관성 보장 없음, 같은 업체 장애에 함께 사라짐) |
| 디스크 | DB 볼륨이 인스턴스와 분리되는 구조면 이전·복구가 쉽다. 용량 증설 방법 확인 |
| 관리형 DB를 쓰는 경우 | 세션 유지 연결 가능 여부(`LISTEN`, 7.2), TLS, PITR 보관 기간, 같은 지역 지연, 버전 16 이상 |
| 이전 | 업체를 바꿀 때는 7.2 A의 덤프로 옮기고 DNS TTL을 미리 낮춘다. 이전 당일은 점검 창 |
