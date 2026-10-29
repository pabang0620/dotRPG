# 시험 도구 기획 (2026-10-29)

친구들과 시험 서버에서 같이 시험할 때, 처음부터 키우지 않고 바로 원하는 구간을 볼 수 있게 하는 도구다.
시험 서버(35.216.2.121, `DEPLOY_STAGE=test`)에서 개발 로그인 계정만 쓴다. 운영 서버에는 경로 자체가 없다.

| 기능 | 상태 |
|---|---|
| (A) 메인 퀘스트 밀기 | 구현함 |
| (B) 경험치 밀기 | 구현함 |
| (C) 시험 키트 우편 지급 | 기획만 (이 문서 3절) |

## 1. 공통 규칙

- 서버: `server/src/domains/testtools/`. `routes/index.ts`가 `DEPLOY_STAGE=test`일 때만 등록한다. 운영(live)은 404
- 서비스도 한 번 더 막는다: 운영이면 403 `TEST_TOOLS_DISABLED`, 개발 로그인(`auth_identities.provider = 'dev'`)이 아니면 403 `TEST_TOOLS_DEV_ONLY`
- `/meta`에 `test_tools`(시험 서버면 true)를 싣는다. 클라이언트는 이 값이 true일 때만 일시정지 메뉴 맨 아래에 "시험 도구"를 보여 준다
- 모든 요청은 `request_id` 멱등(기존 경제 요청 틀 `runEconomy`). 같은 id를 다시 보내면 처음 응답 그대로
- 원장 사유는 기존 시험 스크립트와 같다: 경험치 `test_boost`, 아이템 `test_boost`. 퀘스트 보상은 실제 청구 경로라 `quest_reward`

## 2. 구현한 것 (A)(B)

### (A) 메인 퀘스트 밀기 `POST /characters/:uuid/test-tools/main-quest`

본문 `{ request_id, mode: "one" | "chapter" }`

- one: 다음 메인 퀘스트 한 칸을 완료한다. 다음 퀘스트 = 아직 보상을 받지 않았고 선행 퀘스트를 모두 받은 첫 메인 퀘스트(데이터 순서)
- chapter: 열린 챕터 끝까지 이어서 완료한다. 3장이 잠겨 있으면 2장 끝(`c2_end`)에서 멈춘다
- 한 칸마다 하는 일: ① 조건 채우기 ② 실제 청구 경로로 보상 받기 ③ 서버 퀘스트 상태 남기기
  - 조건 채우기(`scripts/test-checkpoint.mjs` 방식): 레벨(최소 레벨·레벨 목표까지 `test_boost`), 처치 누적(`kill_stats`), 던전·레이드 클리어 판(`dungeon_runs` 3일 전, 입장 횟수에 안 셈), 납품 재료(`mat_ore` 등을 `test_boost`로 넣고 청구가 소모)
  - 보상: 퀘스트 청구와 같은 함수(`processClaim`)로 경험치·골드·아이템을 준다
  - 서버 상태: `character_state.quests`에 완료(상태 4, 마지막 단계), 보상 플래그와 진행 플래그(예: 공방 완공)를 `story_flags`에 넣는다. version은 올리지 않는다(전직 기록과 같은 방식이라 클라이언트 저장이 충돌하지 않는다)
- 전직의 길(`career_path`)은 전직을 고르는 것이라 대신하지 않는다. 챕터 끝까지는 그 앞에서 멈추고 `stopped: { code: "TEST_NEEDS_CAREER" }`, 한 칸은 422 `TEST_NEEDS_CAREER`
- 더 밀 퀘스트가 없으면 409 `TEST_NO_MAIN_QUEST`
- 응답: `completed`(완료한 퀘스트 id), `stopped`, `quests`, `story_flags`, `bonus_max_health`, `delta`
- 클라이언트: `delta`로 레벨·골드·가방을 맞추고(레벨업 연출은 기존 경로), 완료한 퀘스트마다 `QuestManager.CompleteFromServer`로 단계 플래그·보상 플래그·최대 체력을 반영한 뒤 한 번 새로고침하고 저장한다. 재접속 없이 다음 메인 퀘스트가 열린다

### (B) 경험치 밀기 `POST /characters/:uuid/test-tools/level`

본문 `{ request_id, mode: "one" | "max" }`

- one: 다음 레벨 0경험치까지 넣는다. max: 레벨 상한까지(3장 잠금이면 40, 열리면 50)
- 상한이면 409 `TEST_LEVEL_MAX`
- 클라이언트는 `delta`의 레벨을 `Progression.SetFromServer`로 받아 레벨마다 레벨업 알림·스킬 해금이 기존대로 뜬다

### 클라이언트 화면

일시정지 메뉴 맨 아래 "시험 도구" → 버튼 4개: 메인 퀘스트 한 칸 진행 / 메인 퀘스트 챕터 끝까지(확인 창) / 레벨 한 칸 올리기 / 만렙까지 올리기(확인 창). 서버 응답이 올 때까지 버튼은 잠긴다.

### 시험

`server/test/testTools.test.ts`: 운영에서 404와 서비스 403, 시험 서버 퀘스트 한 칸, 챕터 끝(전직 앞 멈춤 → 전직 뒤 2장 끝), 레벨 한 칸, 만렙, 멱등, 개발 계정 외 거부.

## 3. 시험 키트 우편 지급 (C, 기획만)

### 무엇을 주나

시험 도구 화면에 "시험 키트 받기" 버튼을 하나 더 둔다. 누르면 서버가 본인 우편함에 우편을 만든다. 계정당 횟수 제한 없이 반복할 수 있다.

키트 구성(한 번 누를 때):

| 종류 | 아이템 | 수량 | 우편 첨부 |
|---|---|---|---|
| 골드 | - | 10,000,000 | gold |
| 체력 물약 | potion_hp | 500 | item |
| 마나 물약 | potion_mp | 500 | item |
| 상급 체력 물약 | potion_hp_hi | 100 | item |
| 상급 마나 물약 | potion_mp_hi | 100 | item |
| 강화석 | mat_ore | 1,000 | item |
| 마력 정수 | mat_essence | 1,000 | item |
| 장비 보호권 | ticket_protect | 50 | item |
| 던전 클리어권 | ticket_sweep | 100 | sweep_ticket |
| 별조각 | (계정 지갑) | 100,000 | 우편 불가, 아래 참고 |

- 우편 한 통의 첨부 칸은 5개(`mail_attachments.slot` 1~5)라 위 9칸을 우편 2통으로 나눈다
- 별조각(뽑기 재화)은 우편에 붙일 수 없다. 첨부 종류는 `gold`, `item`, `sweep_ticket`뿐이다. 대신 같은 요청 안에서 계정 지갑에 바로 넣는다: `scripts/test-stars.ts`와 같은 `creditFree(reason: 'test_grant')`. DB 트리거가 세션 설정 `dotrpg.allow_test_grant = 'on'`이 있을 때만 받으므로 그 트랜잭션에서만 켠다
  - 관리자 지급(`payment.grant.create`, 2인 승인·일일 상한)은 운영용이라 시험 키트에는 쓰지 않는다
- 골드는 받는 순간 클라이언트 상한(`GOLD_CLIENT_MAX`, 약 21억)을 넘으면 우편 수령이 거절된다. 키트를 여러 번 받으면 이 상한에 걸릴 수 있다

### 누가 어떻게 보내나

- `POST /characters/:uuid/test-tools/kit` `{ request_id }`. (A)(B)와 같은 막기(시험 서버만, 개발 로그인만, 멱등)
- 서버가 본인 캐릭터에 시스템 우편을 만든다(`createSystemMail` + `insertAttachments`)
- 원장: 우편 위치 아이템은 `item_ledger` 위치 `mail`, 사유 `test_boost`(지금 시스템 우편은 `admin_grant`를 쓴다). 골드는 우편 생성 때 원장이 없고 수령 때 `mail_claim`. 별조각은 `star_ledger` `test_grant`
- 클라이언트는 응답 뒤 우편함 표시를 새로 읽는다(기존 우편 알림 경로)

### 건드리면 안 되는 것

- 뽑기 천장·게이지: 지갑의 `pity`, `skin_pity`, `hidden_pity`, 합성 실패 누적(`synth_fail_*`), `ceiling_claims`, 봉인된 상자 상태(`account_sealed_state`)는 바꾸지 않는다. 별조각 잔액만 늘린다
- 별조각 결제 기록(`star_paid_lots`, 유료분)은 건드리지 않는다. 시험 별조각은 무료분
- 확률 공개·현금 가치 집계에 시험 지급이 섞이지 않는지 확인한다(`test_grant` 사유로 걸러지는지)

### 필요한 마이그레이션 (0056)

- `mails.system_code`에 `test` 추가(지금 값: compensation, event, refund, notice, maintenance, apology, attendance, other). 우편함에서 시험 키트를 구분하려면 필요하다. 안 하면 `other`로 보낸다
- `item_ledger`는 `test_boost`가 이미 있다(0011). `star_ledger`도 `test_grant`가 이미 있다. 골드 원장은 바꿀 것이 없다

### 시험 계획

- 운영 스테이지에서 404, 서비스 직접 호출 403, 개발 계정 외 403
- 키트 1회: 우편 2통, 첨부 9칸, 아이템 원장(mail, test_boost), 별조각 +100,000(무료분), 천장·게이지 값 그대로
- 수령: 가방·골드·클리어권 지갑에 들어가고 원장 합 = 잔액
- 같은 request_id 재전송: 우편이 늘지 않음
- 두 번 누르기: 우편 4통, 별조각 +200,000
- 골드 상한 근처에서 수령 거절 메시지 확인

### 질문

1. 키트 수량이 위 표로 괜찮은지(특히 골드 1천만, 별조각 10만)
2. 별조각을 우편이 아니라 바로 지갑에 넣어도 되는지(우편으로는 줄 수 없다)
3. 시험 우편을 `other`로 보낼지, 마이그레이션으로 `test` 종류를 새로 만들지
4. 강화권(`ticket_enh10` 등), 봉인된 상자(`box_sealed`), 투지의 주문서(`scroll_power`)도 넣을지
5. 3장이 열린 시험 서버라면 3장 재료(`mat_raid_xibak`, `mat_raid_kamatz`)도 넣을지
