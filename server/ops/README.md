# dotRPG 서버 운영 런북

설계 원본은 `Docs/server/phase7_ops.md`다. 이 문서는 서버 호스트에서 실제로 치는 명령만 모았다.
서버 업체와 도메인은 아직 정하지 않았다: 업체에 묶이는 값(도메인, 오프사이트 저장소 자격 증명, 웹훅 URL)은 모두 `.env` 자리표시이고, 이 저장소에는 값이 없다.

## 0. 구성과 파일

| 파일 | 역할 |
|---|---|
| `compose.prod.yml` | caddy(80/443만 공개) + api + postgres. api·postgres는 `expose`만 한다 |
| `Caddyfile` | TLS 자동 인증서, WebSocket 통과, `/admin` 404, api가 내려갔을 때 503 MAINTENANCE JSON. 도메인은 `api.example.com` 자리표시 |
| `postgres.conf` / `postgres.Dockerfile` | PostgreSQL 시작값 / WAL 아카이브(pgBackRest)용 이미지 |
| `backup.sh` | 일일 `pg_dump -Fc` -> age 암호화 -> 목적지(로컬 경로 또는 rclone) -> 목록 확인 -> 하트비트 |
| `restore-drill.sh` | 최신 덤프를 훈련용 컨테이너에 복구하고 확인 항목을 점검, `DRILL_LOG.md` 에 한 줄 |
| `deploy.sh` | 핫 배포 / 점검 배포 / 롤백 단계 스크립트 |
| `host-check.sh` | 디스크·메모리·컨테이너·WAL·백업·훈련·TLS 만료를 웹훅으로 알림(cron 5분) |

관리자 도구는 같은 이미지의 CLI다(웹 페이지 없음). 서버 안 `127.0.0.1:3001`에만 열려 있고 어떤 포트로도 공개하지 않는다.

```
alias dotrpg-admin='docker compose -f compose.prod.yml --env-file ../.env exec api node dist/admin/cli/cli.js'
dotrpg-admin login            # 아이디, 비밀번호, TOTP 코드는 프롬프트로(셸 이력에 남지 않는다)
dotrpg-admin ops status
```
컨테이너 안에서는 `-t` 가 필요할 수 있다(`exec -t`). 모든 명령에 `--json` 이 있다.

## 1. 처음 올릴 때 (업체와 도메인이 정해진 뒤)

1. 도메인을 사서 A 레코드를 서버 고정 IP에 연결한다. `Caddyfile` 의 `api.example.com` 과 `email` 을 바꾼다.
2. 서버: 운영자별 계정과 SSH 키, 비밀번호 로그인·루트 로그인 끔, 방화벽(인바운드 80/443, 22는 관리 IP만, 나머지 거부), 자동 보안 업데이트, `chrony`(시계 동기). docker는 `ufw`를 우회하므로 외부 포트 스캔으로 확인한다.
3. Docker·compose 설치. `server/` 폴더를 올리고 `server/.env` 를 만든다(권한 0600). 값은 아래 3절.
4. 이미지를 빌드해 호스트로 옮긴다(레지스트리가 없으면 `docker save | ssh host docker load`). 태그는 `dotrpg-api:<git 짧은 해시>`, `latest` 는 쓰지 않는다.
5. 마이그레이션: `API_IMAGE=dotrpg-api:<태그> docker compose -f compose.prod.yml --env-file ../.env run --rm api node dist/db/migrate.js up`
6. 기동: `docker compose -f compose.prod.yml --env-file ../.env up -d`
7. 첫 owner: `docker compose -f compose.prod.yml --env-file ../.env exec api node dist/admin/bootstrap.js create-owner <아이디>` (임시 비밀번호가 한 번만 출력된다). 이어서 `dotrpg-admin login` -> `passwd` -> `totp enroll`.
8. 백업 cron(KST 04:10 = UTC 19:10) 등록, 외부 업타임 점검(`https://도메인/health/ready` 1분)과 하트비트·웹훅 설정, `host-check.sh` cron(5분), 첫 복구 훈련.
9. 점검표(설계 9.2)와 시나리오(설계 13절)를 전부 확인한다.

## 2. 접속 확인

```
curl -s https://도메인/meta            # 200, data_version, maintenance
curl -s https://도메인/health/ready    # 200 {"status":"ok"}
```

## 3. 환경변수 (서버 `.env`, 값은 저장소에 두지 않는다)

운영에서 맞춰야 하는 값이 틀리면 서버가 기동하지 않는다(가드 G1~G7).

| 이름 | 운영 값 |
|---|---|
| `NODE_ENV` | `production` |
| `JWT_SECRET` | 난수 48바이트 `node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))"` |
| `POSTGRES_PASSWORD` | 난수 32바이트 hex |
| `ADMIN_SECRET_KEY` | `openssl rand -base64 32`. **오프라인 사본을 따로 보관**(잃으면 모든 관리자 2FA 재등록) |
| `STEAM_AUTH_MODE` / `PARTY_TRANSPORT` | `web_api` / `steam` (`STEAM_APP_ID`, `STEAM_WEB_API_KEY`, `STEAM_IDENTITY` 필요, 480 거부) |
| `MIN_CLIENT_VERSION` | 출시 클라이언트 버전(배포마다 확인) |
| `ALERT_WEBHOOK_URL`, `OPS_HEARTBEAT_URL`, `BACKUP_*` | 자리표시. 업체·서비스가 정해지면 채운다(비면 해당 기능만 꺼지고 경고 로그) |

Steam 연동 전 외부 시험(운영자가 발급한 개발용 계정만): `STEAM_AUTH_MODE=off`, `PARTY_TRANSPORT=dev`, `AUTH_DEV_ENABLED=true`, `ALLOW_DEV_AUTH_IN_PRODUCTION=true`, `AUTH_DEV_REGISTER_ENABLED=false`. 계정은 `dotrpg-admin account dev-create <아이디>` 로 발급한다(공개 가입 없음).

## 4. 배포

### A. 핫 배포 (마이그레이션·데이터 버전·프로토콜 변경 없음, 접속이 적은 시간)
```
./deploy.sh hot <새 태그>
```
구 컨테이너가 SIGTERM을 받아 최대 30초 안에 정상 종료하고(접속자는 `bye` 후 자동 재연결, REST는 같은 request_id로 재시도해 중복 지급 없음) 새 컨테이너가 뜬다. ready 확인 뒤 1분 동안 로그와 `/meta` 의 `data_version` 을 본다.

### B. 점검 배포 (마이그레이션 또는 데이터 버전·프로토콜 변경이 있을 때의 표준)
```
dotrpg-admin maint schedule --in 30m --duration 20m --notice "업데이트"   # 30·10·5·1분 전 공지, 10분 전부터 새 로그인·새 판 차단
dotrpg-admin maint drain                                                 # 점검이 시작되고 ready_to_stop 이 true 가 될 때까지 확인
./deploy.sh maintenance <새 태그>                                        # 스냅샷 -> api 정지 -> 마이그레이션 -> 기동
dotrpg-admin ops status                                                  # 정합성·작업·틱 확인, 개발 계정으로 핵심 동선 확인
dotrpg-admin maint end <창 uuid>                                         # 플레이어 입장. 15분 동안 ops status 와 로그를 본다
```
데이터(`server/data/*.json`)가 바뀌는 배포는 항상 B로 한다(`data_version` 이 바뀌면 모든 클라이언트가 426이 된다). Steam 클라이언트 업데이트가 끝나기 전에 서버만 먼저 올리지 않는다.

### 마이그레이션 규칙
- 새 코드보다 먼저 적용되는 마이그레이션은 구 코드가 그대로 동작하는 변경(표·열 추가, `NOT NULL` 없는 열, 인덱스)이어야 한다. 구 열을 지우거나 이름을 바꾸는 변경은 두 번의 배포로 나눈다.
- 큰 표(원장, `kill_log`, `chat_messages`)에 인덱스를 만들 때는 파일에 `-- no-transaction` 줄을 넣어 `CREATE INDEX CONCURRENTLY` 를 쓴다(문장은 줄 끝 `;` 로 나뉜다).
- 원장 `reason` CHECK 를 바꿀 때는 `DROP` + `ADD ... NOT VALID` 를 한 마이그레이션에, `VALIDATE CONSTRAINT` 를 다음 마이그레이션에 둔다.
- DOWN 은 운영에서 실행하지 않는다(원장 행을 지운다). 운영 롤백은 아래 표.

### 롤백

| 상황 | 방법 |
|---|---|
| 마이그레이션 실패 | 그 파일은 트랜잭션 롤백(스키마 무변경). `deploy.sh` 가 구 이미지로 다시 띄운다 |
| 새 코드 이상, 마이그레이션은 추가형 | `./deploy.sh rollback <이전 태그>` (스키마는 그대로) |
| 새 코드 이상 + 파괴적 변경 포함 | 배포 전 스냅샷(`predeploy-<태그>`)으로 복구(5절 R1). 점검 창을 연장하고 공지 |
| 복구 뒤 플레이어 손실 | 표를 직접 고치지 말고 운영 지급(우편)으로 보상: `dotrpg-admin grant add ...` |

## 5. 백업과 복구

백업: `BACKUP_DEST` 를 로컬 경로 또는 `rclone:<remote>:<bucket>/dotrpg` 로 둔다(자리표시, 업체가 정해지면 rclone config). `BACKUP_AGE_RECIPIENT` 는 age 공개키(개인키는 서버에 두지 않고 오프라인 2곳에 보관, 분기마다 복호화 확인). 보관은 일일 14개, 주간 8개, 월간 6개(로컬은 스크립트가, 오프사이트는 저장소 수명 주기 규칙이 맡는다). 정식 출시 전에는 WAL 아카이브(PITR, `postgres.Dockerfile` + `postgres.conf` 주석 해제)를 추가한다.

```
BACKUP_DEST=/var/backups/dotrpg BACKUP_AGE_RECIPIENT=age1... ./backup.sh            # 수동 1회
BACKUP_DEST=/tmp/b BACKUP_ALLOW_PLAINTEXT=1 PG_DUMP_CMD='...' ./backup.sh test      # 로컬 시험(암호화 없음)
```

### R1. 덤프에서 복구(전체)
1. `dotrpg-admin maint schedule --in 0m --duration 60m --notice "긴급 복구"`, `maint drain`, `docker compose stop api`.
2. 현재 DB 보존: `ALTER DATABASE dotrpg RENAME TO dotrpg_broken_<날짜>` (지우지 않는다).
3. 새 DB 생성, 복호화(`age -d -i <개인키>`) 후 `pg_restore -d dotrpg --no-owner --exit-on-error`.
4. `dotrpg-admin ops run integrity-nightly --full` 로 불일치 0 확인.
5. api를 `AUCTION_TICK_ENABLED=false` 로 먼저 기동해 `/health/ready` 와 `ops status` 를 본 뒤 틱을 켠다(마감이 지난 경매가 한꺼번에 정산된다).
6. 복구 시점 이후의 손실은 보존해 둔 DB와 비교해 SQL로 뽑고 운영 지급(우편)으로 보상한다.
7. 점검 종료.

### R2. 시점 복구(PITR, WAL 아카이브가 있을 때)
`pgbackrest restore --type=time --target='<KST 시각>' --target-action=promote` 를 새 데이터 디렉터리에 하고 R1의 4~7. 대상 시각은 감사 로그와 `request_log` 로 정한다.

### R3. 한 계정만 되돌리기
훈련용 DB에 백업을 복구해 필요한 행(원장·소지품)을 확인하고, 운영 DB에는 우편 지급으로만 돌려 준다. 운영 표를 직접 INSERT/UPDATE 하지 않는다.

### 복구 훈련 (매월)
`./restore-drill.sh` : 복구 종료 코드 0, 표별 행 수, `schema_migrations` 마지막 이름 일치, (`DRILL_IMAGE` 가 있으면) `/health/ready` 200, 걸린 시간(RTO)을 `DRILL_LOG.md` 에 한 줄 남긴다. 분기마다 복호화 키 확인과 PITR 복구 1회. 마지막 훈련이 35일을 넘으면 `host-check.sh` 가 경고한다.

## 6. 모니터링과 알림

- 외부 업타임 점검(`/health/ready`, 1분, 2회 연속 실패 시 알림)과 하트비트(`OPS_HEARTBEAT_URL`)는 서버가 죽어도 알려 준다.
- 앱 안 감시자가 매분 규칙(5xx 비율, 풀 대기, 정합성 불일치, 디스크, 경매 정산 지연, 작업 정지, 보류 판·신고 지연 등)을 평가해 웹훅으로 보낸다. 같은 알림은 30분 안에 반복하지 않고 해소되면 한 번 알린다. 웹훅이 없으면 로그(`alert.sent`)에만 남는다.
- 지표는 `dotrpg-admin ops status` 와 로그의 `ops.snapshot` 한 줄(매 60초). 작업 상태와 정합성 결과는 `ops jobs`.
- 로그: `docker compose logs api | jq`. 요청 로그에 `req_id`(`X-Request-Id`) 와 `account` 가 있다.
- 정합성 점검(`integrity-nightly`, 매일 KST 04:30, 일요일은 전체)에서 불일치 >= 1 이면 긴급 알림이다. 재화 복사·원장 우회를 의심하고 즉시 조사한다(`char ledger <uuid>`, 같은 `request_id` 로 한 요청이 만든 줄 묶음).

## 7. 운영 업무 (관리자 CLI)

| 일 | 명령 |
|---|---|
| 계정 찾기·상세 | `account find <uuid \| name:이름 \| steam:ID \| dev:아이디>`, `account show <uuid>`, `char show`, `char ledger <uuid> --kind gold --since 24h` |
| 제재 | `sanction add <계정 uuid> --kind chat_mute --for 1d --reason abuse --note ...` (operator 정지 최대 30일, 영구는 owner), `sanction revoke` |
| 신고 | `report list`, `report show` (증거 줄), `report take`, `report resolve --dismiss` 또는 `--sanction --kind ... --reason ...` |
| 보류 던전 판 | `held list`, `held show`, `held release <uuid> --note ...`(보상 확정, 한 판에 한 번), `held reject` |
| 이상 기록 | `watch list`(의심 계정 순위), `anomalies`, `flags`, 검토 뒤 `account ack <uuid>` |
| 운영 지급 | `grant add <캐릭터 uuid> --code compensation --gold 5000 --memo "문의 #123"` (system 우편으로만, 한도는 `ADMIN_GRANT_*`). 회수 기능은 없다: 사고는 정지 + 개별 대응 |
| 점검·방송 | `maint status/schedule/cancel/extend/end/drain`, `broadcast "문구"` |
| 작업 | `ops jobs`, `ops run purge-hourly \| purge-daily \| stale-runs \| integrity-nightly [--full]` |
| 회원 탈퇴 | `withdraw list [--state requested\|completed\|cancelled] [--deferred]`, `withdraw show <계정 uuid>`, `withdraw start <계정 uuid> --note ...`(정보주체 요청 대행, 정지 계정 포함. `--no-cancel` 은 owner), `withdraw cancel <탈퇴 uuid> --note ...`, `withdraw hold <탈퇴 uuid> --on\|--off --note ...`, `withdraw anonymize-now <탈퇴 uuid> --note ... [--override-deferral]`(owner), `tombstone find --steam <ID>`, `tombstone release <id> --note ...`(owner). 작업 `ops run withdrawal-anonymize \| withdrawal-destroy`. 메모에 Steam ID·실명을 쓰지 않는다 |
| 관리자 계정(owner) | `admins list/add/role/disable/enable/reset-2fa/unlock/reset-password`, `audit` |

기기를 잃어 2FA 를 못 쓰면(셸 접근자만): `docker compose exec api node dist/admin/bootstrap.js reset-totp <아이디>`.

## 8. 비밀값 교체

| 비밀 | 교체하면 |
|---|---|
| `JWT_SECRET` | 모든 액세스 토큰이 무효(수명 15분, 갱신 토큰은 DB 해시라 클라이언트가 자동 갱신). 점검 창 없이 가능 |
| `POSTGRES_PASSWORD` | `ALTER ROLE` + `.env` + api 재시작(짧은 점검) |
| `ADMIN_SECRET_KEY` | 모든 관리자의 TOTP 를 다시 등록해야 한다 |
| `STEAM_WEB_API_KEY` | Steamworks 에서 재발급 후 `.env` 교체, api 재시작(접속자는 재연결) |
| 웹훅·하트비트 URL | URL 자체가 비밀이다. 새로 만들어 교체 |

`.env` 와 `docker compose config`, `env` 출력을 채팅·이슈에 붙이지 않는다(값이 펼쳐진다).

## 9. 회원 탈퇴 5년 파기 (`withdrawal-destroy`, 기본 꺼짐)

설계는 `Docs/server/phase12_withdrawal.md` 8절. 기본(`WITHDRAW_DESTROY_ENABLED=false`)에서는 매일 KST 04:55에 보관 기한이 지난 탈퇴 계정 수와 표별 파기 예정 행 수만 `job_runs.detail`(`mode: dry_run`)에 기록하고 아무것도 지우지 않는다. 첫 대상은 탈퇴 5년 뒤에야 생긴다.

켜기 전에: 법무 확인(보관 범위, 백업 안의 개인정보 보관 기간)과 dry-run 건수 점검. 켤 때만 아래 역할을 만든다(슈퍼유저 작업, 앱 풀은 이 역할로 붙지 않는다).

```sql
CREATE ROLE dotrpg_purge LOGIN PASSWORD '<비밀값>';
-- 파기 대상 표만 준다(정책: withdrawalPolicy.ts PURGE_GRANT_TABLES = DESTROY_ORDER + characters, account_withdrawals, accounts). admin_audit_log 등 forever 표는 주지 않는다
GRANT SELECT, DELETE ON
         anomaly_log, drops, kill_log, kill_stats, raid_claims, party_run_members, dungeon_sweeps,
         sweep_ticket_ledger, sweep_ticket_lots, dungeon_runs, revive_log, character_achievements,
         character_career, character_career_trials, character_chests, character_enhance_pity,
         character_node_state, character_state, quest_claims, daily_quests, site_deliveries, character_items,
         gold_ledger, item_ledger, xp_ledger, enhance_log, account_level_rewards,
         account_pass_claims, sealed_pulls, gacha_pulls, star_synth_log, account_collections,
         account_cosmetics, account_growth_pass, account_sealed_state, account_week_counters,
         economy_holds, account_sanctions, admin_account_notes, admin_grants, auction_flags,
         auction_sinks, mail_attachments, mail_campaign_deliveries, mails, auction_trade_flags,
         auction_trades, auction_bids, auction_listings, friendships, blocks, report_lines, reports,
         party_applications, party_members, party_invites, field_session_members,
         party_run_host_reports, party_runs, field_sessions, parties, star_spend_allocs,
         star_ledger, star_paid_lots, star_order_events, payment_flags, star_admin_grants,
         star_orders, star_wallets, payment_profiles, characters, account_withdrawals, accounts
  TO dotrpg_purge;
GRANT INSERT ON account_destruction_log TO dotrpg_purge;               -- 파기 관리대장(추가만)
GRANT USAGE ON SEQUENCE account_destruction_log_id_seq TO dotrpg_purge;
```

접속 문자열은 `PURGE_DATABASE_URL`(비밀값)에 둔다. 추가 전용 원장 트리거(`ledger_block_mutation`)는 `session_user = dotrpg_purge` 의 DELETE 만 허용 표(13개 + 계정 한 곳에 속한 4개)에서 통과시키고 UPDATE, TRUNCATE, 감사 로그 삭제는 이 역할도 거절한다. 외래 키로 막히는 행(상대가 아직 활동 중인 친구·경매 기록 등)은 지우지 않고 계정 껍데기(개인 정보 없음)를 남기며, 다음 실행에서 다시 시도한다.
