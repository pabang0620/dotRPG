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

## 5. 부적절한 캐릭터 이름 처리 (운영정책 제3조·제5조 "이름 변경 요청")

자동 검사(예약어·금칙어)를 피해 만든 부적절한 이름은 운영자가 이름 변경을 요청한다(operator 이상).

```bash
dotrpg-admin character rename <character_uuid> --note "플레이어에게 보일 사유(200자 이하)"
```

- 서버가 한 번에 처리하는 것
  - 이름을 임시 이름 `이름변경0000~9999`로 바꾼다.
  - `rename_required`를 켠다.
  - 계정에 경고(warning, 사유 other, 메모 "부적절한 캐릭터 이름: 옛 이름")를 남긴다.
  - 감사 로그(`character.force_rename`)를 쓴다.
- 플레이어 쪽: 캐릭터 카드에 "(이름 변경 필요)"가 붙는다. 접속하기를 누르면 이름 변경 창("운영정책에 따라 캐릭터 이름을 바꿔야 합니다." + 사유)이 먼저 뜬다. 새 이름은 생성 때와 같은 규칙(2~8자, 예약어·금칙어·중복 금지)으로 한 번 고르고, 고르면 바로 접속한다.
- 2차·3차 위반이면 이 명령과 함께 `sanction create`로 정지를 따로 건다(운영정책 제3조 표).
- 탈퇴 유예 중인 계정에 요청하면, 철회해도 옛 이름은 되살아나지 않고 임시 이름이 남는다.

## 6. 청약철회(구매 취소) 요청 처리 (유료 서비스 정책 제4조)
1. 요청 확인: 캐릭터 이름과 결제 일시로 주문을 찾는다(PA 결제 조회). 구매 후 7일 이내인지, 그 주문의 유료 별조각이 쓰이지 않았는지 본다. 정기권은 매일 지급분·상자를 하나도 받지 않았는지 본다.
2. 철회 가능하면 Steamworks 파트너 사이트에서 해당 거래를 환불한다. 서버 감시 작업이 환불을 감지해 남은 유료 별조각을 자동으로 회수한다(쓴 것이 있으면 부채로 남는다).
3. 영업일 3일 안에 결과를 회신한다. 미성년자 결제 취소(제5조)는 법정대리인 확인 자료를 받은 뒤 같은 절차로 처리한다.

## 7. 정책 문서와 약관 동의
- 문서 원문: `server/policies/<ko|en>/*.md`, 운영자 정보는 `server/policies/operator.json`(빈 값은 페이지에 "(미정)"으로 보인다). 페이지 주소 `https://<서버>/policies/<ko|en>/<terms|privacy|operation|paid|youth|probability>`. 확률 페이지는 서버 확률표에서 자동으로 만든다.
- 약관을 바꾸면 `server/src/domains/policies/policyVersions.ts`의 버전을 올린다. 다음 접속 때 모든 이용자에게 동의 화면이 다시 뜬다. 시행 7일 전(불리한 변경은 30일 전)에 공지한다.
- 확률을 바꾸기 7일 전에 게임 공지와 웹 페이지로 알린다(운영정책 제9조).

## 8. 운영 서버 설정 점검 (정책과 실제 동작을 맞추기)
| 설정 | 운영 값 | 이유 |
|---|---|---|
| `POLICY_CONSENT_ENFORCE` | `true` | 동의 없이 게임 기능을 쓰지 못하게(시험 서버는 동의 화면이 든 새 클라이언트를 배포한 뒤 켠다) |
| `WITHDRAW_DESTROY_ENABLED` | `true` | 개인정보처리방침의 "탈퇴 후 5년 보관 뒤 파기"를 실제로 하게(기본값은 세기만 함) |
| `server/policies/operator.json` | 모든 칸 | 운영사명, 대표자, 주소, 사업자번호, 통신판매업 신고번호, 이메일, 개인정보·청소년 보호책임자, 호스팅 국가 |
