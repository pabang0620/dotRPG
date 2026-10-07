# 운영자 가이드: 관리자 계정, 보상 우편, 소탕, 부정 방지 켜기

서버 운영자가 실제로 하는 일만 순서대로 적었다. 명령은 서버의 `server/ops` 폴더에서 실행한다. 설치·배포·백업은 `server/ops/README.md`를 따른다.

```bash
alias dotrpg-admin='docker compose -f compose.prod.yml --env-file ../.env exec api node dist/admin/cli/cli.js'
```

## 1. 관리자 계정

### 정한 운영 방식

| 항목 | 결정 |
|---|---|
| 소유자(owner) | 2명: `wonho`, `jaein`. 보상 우편은 작성자가 아닌 다른 owner가 승인해야 발송된다(2인 확인) |
| 운영자(operator) | 필요할 때만 추가. 신고 처리·제재·점검·조회는 되고, 관리자 추가와 우편 승인은 못 한다 |
| 조회자(viewer) | 로그·현황 조회만 |
| 로그인 | 아이디 + 비밀번호 + 2FA(TOTP) 필수. 임시 비밀번호는 첫 로그인에서 바꾼다 |

### 처음 한 번 (서버 셸)

```bash
docker compose -f compose.prod.yml --env-file ../.env exec api node dist/admin/bootstrap.js create-owner wonho
dotrpg-admin login          # 임시 비밀번호로 로그인
dotrpg-admin passwd         # 새 비밀번호
dotrpg-admin totp enroll    # 휴대폰 OTP 앱 등록
dotrpg-admin admins add jaein --name 재인 --role owner   # jaein 임시 비밀번호가 출력된다. 직접 전달
```

### 평소 관리

| 하려는 일 | 명령 |
|---|---|
| 목록 | `dotrpg-admin admins list` |
| 운영자 추가 | `dotrpg-admin admins add <아이디> --name <이름> --role operator` |
| 권한 변경 | `dotrpg-admin admins role <admin_uuid> viewer|operator|owner` |
| 퇴사·분실 시 막기 | `dotrpg-admin admins disable <admin_uuid>` |
| 비밀번호·2FA 초기화 | `dotrpg-admin admins reset-password <uuid>`, `admins reset-2fa <uuid>` |
| 2FA 기기를 잃은 owner (셸) | `... exec api node dist/admin/bootstrap.js reset-totp <아이디>` |
| 누가 무엇을 했나 | `dotrpg-admin audit --since 24h` |

## 2. 보상 우편 (점검·사과·이벤트·출석)

예시 파일이 `server/ops/campaigns/`에 있다. 날짜·보상만 바꿔 쓴다.

| 파일 | 용도 |
|---|---|
| `maintenance.example.json` | 정기 점검 보상(전체) |
| `apology.example.json` | 오류 사과 보상(전체) |
| `returning.example.json` | 휴면 복귀(직전 접속이 기준일 이전 + 계정 최고 레벨 조건) |

```bash
cp campaigns/maintenance.example.json campaigns/2026-10-10-maint.json   # 날짜·보상 수정
dotrpg-admin campaign create --file campaigns/2026-10-10-maint.json      # wonho가 작성 -> 상태 pending
dotrpg-admin campaign approve <campaign_uuid>                            # jaein이 승인 -> 발송 시작
dotrpg-admin campaign show <campaign_uuid>                               # 받은 수·남은 수
dotrpg-admin campaign cancel <campaign_uuid> --reason "금액 오류" --revoke  # 잘못 보냈을 때 안 받은 것 회수
```

- 우편은 한 번에 뿌리지 않는다. 대상이 기간 안에 접속할 때 계정당 1통씩 만들어진다(부캐 수만큼 늘지 않는다).
- 첨부는 최대 5개: 골드, 아이템, 이벤트 클리어권(받은 날부터 14일).
- 골드 상한: 우편 1통 20만, 캠페인 1개 총 1억(`.env`의 `CAMPAIGN_MAX_GOLD_PER_MAIL`, `CAMPAIGN_MAX_GOLD_TOTAL`). 넘으면 작성이 거절된다.
- 캐릭터 한 명에게만 보낼 때(개별 보상·환불)는 `dotrpg-admin grant add <character_uuid> --code compensation --gold 1000 --memo <사유>`.

## 3. 기능 켜는 순서

| 순서 | 할 일 | 설정 |
|---|---|---|
| 1 | 새 서버 배포(마이그레이션 0020~0022 자동 적용) | 기능 스위치는 모두 꺼진 채 |
| 2 | 새 클라이언트 배포 | |
| 3 | 소탕 켜기 | `SWEEP_ENABLED=true` |
| 4 | 대부분이 새 클라이언트로 바뀐 뒤 우편 배달 켜기 | `CAMPAIGN_DELIVERY_ENABLED=true` (옛 클라이언트에는 운영 우편이 빈 우편으로 보인다) |
| 5 | 새 클라이언트 배포와 함께 차단 켜기(플레이어가 적어 관찰 기간 생략, 2026-10-06 결정) | `DEVICE_LIMIT_MODE=enforce`(PC당 2개), `PRESENCE_KILL_MODE=enforce`. 옛 클라이언트가 사라지면 `DEVICE_INFO_REQUIRED=true` |
| 6 | 재화 이상 2주 분포 확인 후 자동 정지 켜기 | `ECONOMY_HOLD_MODE=enforce` |

배포 직후부터 바로 걸리는 것: 전직·각성 서버 승인, 레이드·파티 던전 기여 판정, 이름 금칙어, 필드 캐리 감쇠, 경매 구매 자격, 별조각 장비 계정 귀속.

필수 `.env`: `DEVICE_HASH_PEPPER`(무작위 64자, 한 번 정하면 바꾸지 않는다), `DEPLOY_STAGE=live`, 위 골드 상한 2개. `.env`는 커밋하지 않는다.

## 4. 재화 이상으로 멈춘 계정 처리

| 하려는 일 | 명령 |
|---|---|
| 멈춘 목록 | `dotrpg-admin holds list` |
| 내용 확인 | `dotrpg-admin holds show <hold_uuid>`, `dotrpg-admin char ledger <character_uuid> --since 7d` |
| 정상이면 풀기 | `dotrpg-admin holds release <hold_uuid> --note <사유>` |
| 부정이면 회수 | `dotrpg-admin holds clawback <hold_uuid> --note <사유>` 후 필요하면 `sanction add` |
