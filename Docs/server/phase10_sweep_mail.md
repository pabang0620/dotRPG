# 서버 10단계 설계: 던전 클리어권(소탕)과 운영 우편 캠페인

기준: [PLAN_SWEEP_AND_MAIL.md](../PLAN_SWEEP_AND_MAIL.md)(기획 SSOT, 규칙은 바꾸지 않는다). 앞 단계: [phase1_2_api.md](phase1_2_api.md)(응답 형식, 멱등성), [phase3_api.md](phase3_api.md)(`EconCtx`, 원장, `delta`), [phase6_api.md](phase6_api.md)(우편), [phase7_ops.md](phase7_ops.md)(관리자, 감사 로그, 작업), [phase9_anti_abuse.md](phase9_anti_abuse.md)(경제 정지 `assertNoHold`, 프레즌스 `assertActionPresence`, 계정 귀속). 마이그레이션: `server/migrations/0021_sweep_and_mail.sql`(이 문서 9절이 초안, 정본은 구현 때 만든다), 스키마: `server/schema.sql`(구현 때 맨 아래에 합친다).

이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만들고, Unity 클라이언트와 Unity 내보내기(`sweep.json`)는 메인이 따로 한다(12절 "클라이언트 계약"이 그 경계다). 게임 값(경험치·카드 표·가격·한도)은 SQL에 복사하지 않고 `server/data/*.json` 이름으로만 참조한다. 단 3절의 "이론 계산 표"는 사용자가 요청한 계산 결과이므로 데이터 기준일(2026-10-06)의 계산값을 적었다. 서버는 이 표를 읽지 않는다.

## 0. 핵심 설계 (먼저 읽기)

1. **소탕은 "전투 없는 정산"이다.** 클라이언트가 보내는 것은 `request_id`, 던전 id, 난이도뿐이다. 경험치·카드·장비는 서버가 `dungeons.json`으로 계산한다(전투 값이 없어 조작할 입력 자체가 없다).
2. **소탕은 `dungeon_runs`에 넣지 않고 새 표 `dungeon_sweeps`에 쓴다.** `dungeon_runs.state='cleared'`를 읽는 곳이 많아(난이도 해금 `clearSummary`, 업적 `achievementRepository`, 퀘스트 `clearCounts`, 최고 랭크 표시, 관리자 요약) 소탕 행이 섞이면 "해금 불인정"이 새는 곳이 생긴다. 별도 표로 분리하면 새지 않는다. 대신 하루 입장 횟수(`countEntries`)와 퀘스트 "던전 클리어 N회"(`clearCounts`) 두 곳만 소탕 표를 더해 읽는다(5.5, 8.2절).
3. **클리어권은 "계정 지갑"이다(권장안 D1, 결정 대기).** 이벤트 클리어권은 "받은 날부터 14일" 기한이 있어 `character_items`의 "키별 한 행(수량 합)" 규칙에 담을 수 없다. 그래서 일반권과 이벤트권을 모두 `sweep_ticket_lots`(계정 소유, 묶음 단위)에 두고 `sweep_ticket_ledger`(추가만)를 쓴다. 지갑에 있으므로 가방·창고·경매 어디에도 못 들어가 "경매·판매 불가"가 구조로 보장된다(5.2절).
4. **재화가 움직이는 모든 새 경로는 기존 틀을 그대로 쓴다.** `runEconomy`(캐릭터 행 잠금, `request_log` 멱등성), `EconCtx`(`changeGold`, `grantXp`, `addItem`, `delta()`), `assertNoHold`, `assertActionPresence`, `resetBoundaries`. 새 재화 단위인 클리어권만 자기 원장을 가진다.
5. **운영 우편은 "캠페인 한 줄 + 접속할 때 한 통"이다.** 대상 전원에게 미리 우편을 넣지 않는다. 승인된 캠페인이 있는 동안 대상이 접속(프레즌스 진입) 또는 우편 요약을 부르면 서버가 그때 한 통을 만든다. `UNIQUE(campaign_id, delivery_key)`가 계정당 1통을, `issued_count < cap_count` 조건부 UPDATE가 총 지급 상한을 DB에서 강제한다(7.2절).
6. **기존 우편은 그대로 동작한다.** `mails`는 열만 더한다(제목·본문·`campaign_id`·`attach_n`). 옛 우편(경매, 운영 지급 `admin_grants`)은 건드리지 않고, 첨부가 여러 개인 새 우편만 `mail_attachments`를 쓴다(6절).
7. **새 기능은 모두 기능 플래그로 서버를 먼저 배포한다.** `SWEEP_ENABLED`, `CAMPAIGN_DELIVERY_ENABLED`가 꺼져 있으면 새 경로는 `503 FEATURE_DISABLED`, 캠페인 배달은 하지 않는다. 클라이언트가 나간 뒤 켠다(11절).

### 0.1 PLAN_SWEEP_AND_MAIL과 달라진 점 / 해석을 보탠 곳

| 항목 | PLAN | 이 설계 | 이유 |
|---|---|---|---|
| 클리어권 저장 | "아이템 2종" | 계정 지갑(로트 표, 원장 포함). 가방 아이템이 아님(D1) | 이벤트권의 14일 기한을 `character_items` 규칙에 담을 수 없다. 계정 귀속·거래 불가가 구조로 보장된다 |
| 클리어권 표시 | 아이템 이름 | `items.json`에 표시용 항목 2개(이름·아이콘, 귀속 `account`)만 두고 서버가 가방에 넣는 것을 금지 | 우편 첨부 아이콘·이름을 클라이언트가 같은 경로로 그린다 |
| "B등급 기준, 등급 보너스 없음" | 문장이 두 가지로 읽힘 | 보너스 0%로 계산(권장, D2). 값은 `sweep.json xpBonusPercent`라 데이터만 바꾸면 +10%(B 등급 값)로 바뀐다 | 3절 표: 0%일 때 King·Hero가 목표 55~65%에 들어온다 |
| 소탕 해금 기록 | "그 난이도 직접 클리어, 최고 B 이상" | 직접 클리어 중 **보상이 잠긴 판(`reward_locked`)은 제외** | 기여 부족(`LOW_CONTRIBUTION`)으로 업혀 간 판이 소탕 자격이 되는 것을 막는다 |
| 경제 정지 차단 범위 | "소탕, 우편 수령" | 소탕, 우편 수령, **클리어권 구매, 주간 활동 보상 수령** | 계정 지갑이라 정지된 캐릭터의 골드·성과가 정지 안 된 다른 캐릭터의 소탕으로 옮겨질 수 있다(8.1절) |
| 프레즌스 | 언급 없음 | 소탕 요청에 `assertActionPresence`(신선도만) | 소탕은 입력이 없는 경험치 경로라 HTTP만으로 돌리는 봇의 표적이 된다 |
| 주 7장 구매 한도 | "계정당 주 7장" | `account_week_counters(sweep_buy)`로 센다. 주 경계는 `resetBoundaries().weeklyStartAt` | 날짜 경계 함수 한 곳 |
| 우편 수령 단위 | 언급 없음 | 캠페인 우편은 첨부 전부 한 번에 수령(부분 수령 없음) | 골드 상한 초과 등은 통째로 거절해 "반쯤 받은 우편"을 만들지 않는다 |

### 0.2 이 문서가 미루는 것

- 클리어권을 경제 정지 회수(H4 `clawback`) 대상에 넣는 것: 정지 중에는 지갑을 쓸 수 없으므로(8.1절) 해제 전까지 효력이 막힌다. 회수가 필요하면 후속에 `sweep_ticket_ledger` 사유를 더한다.
- 소탕 업적·소탕 랭킹: 소탕은 업적에 세지 않는다(D8).
- 별조각으로 클리어권 구매: PLAN 3절대로 넣지 않는다.
- 캠페인 내용 수정: 만든 뒤에는 수정하지 못한다(취소 후 새로 만든다). 승인자가 본 내용과 발송 내용이 달라지는 틈이 없다.
- 레이드·필드 보스·파티 판 소탕: PLAN 2절 제외 항목.

## 1. 공통 규칙

1~2단계 0.1~0.4(응답 형식 `{ success, message, data, meta? }` / 실패 `{ success: false, message, errors? }`, 인증, 버전 헤더, 멱등성), 3단계 0.2~0.3(락, `delta`), 9단계 1절(`ECONOMY_HOLD` 응답 본문, 모드 환경변수)을 그대로 쓴다. 이 단계에서 달라지는 것만 적는다.

- **새 REST 경로**는 `/characters/{uuid}/sweep...`(플레이어)와 `/admin/mail-campaigns...`(관리자 서버)이다. 플레이어 경로는 클라이언트·데이터 버전을 둘 다 검사한다.
- **멱등성**: 상태를 바꾸는 플레이어 요청은 `request_id`(UUID)와 `UNIQUE(account_id, request_id)`(`runEconomy`가 맡는다)를 쓴다. 같은 id에 다른 본문은 `422 IDEMPOTENCY_MISMATCH`(기존 코드). 관리자 요청은 `runAdminAction`의 `(admin_id, request_id)`다. 읽기(GET)는 `request_id`가 없다.
- **날짜 경계**: 06:00 일일, 목요일 06:00 주간은 `utils/resetBoundaries.ts`의 `resetBoundaries(now)` 하나만 쓴다. 이 단계의 사용처: 하루 입장 횟수의 `reset_day`(= `dailyStartAt`), 주 7장 구매와 주간 활동의 `week_start`(= `weeklyStartAt`), 요일 던전 개방(`isOpenToday`, 내부가 `gameWeekday`). KST를 직접 계산하는 코드를 새로 쓰지 않는다.
- **락 순서**(9단계 위에 이어 붙인다): ① 캐릭터 행(id 오름차순) ② `accounts` 행(클리어권·주간 카운터·우편 수령을 만질 때만, 캐릭터 행 다음) ③ `mails` 행(기존) ④ `sweep_ticket_lots`(id 오름차순)·`account_week_counters` 행 ⑤ `mail_campaigns` 행(배달 트랜잭션은 맨 마지막 한 문장, 관리자 상태 변경은 이 행만 잠근다). 우편 수령 경로(M2, M3)는 계정 행을 **우편 행을 읽기 전에** 잠근다(수령할 우편에 클리어권 첨부가 있는지 읽기 전에는 모르기 때문). `finalizeCleared`의 주간 카운터 upsert는 그 요청의 마지막 쓰기다.
- **새 에러 코드**(422 규칙 위반 / 409 상태 충돌 / 403 권한·정지 / 404 없음 / 410 기한 / 429 속도 / 503 꺼짐):

| 코드 | 상태 | 어디서 | 뜻 |
|---|---|---|---|
| `FEATURE_DISABLED` | 503 | 모든 새 경로 | 서버 기능 플래그가 꺼져 있다 |
| `SWEEP_NOT_ALLOWED` | 422 | S2, S3 | 소탕 대상이 아닌 던전(레이드) |
| `SWEEP_NOT_CLEARED` | 422 | S2, S3 | 이 던전·난이도를 직접 클리어한 기록이 없다(보상 잠긴 판은 기록으로 안 친다) |
| `SWEEP_RANK_LOW` | 422 | S2, S3 | 최고 등급이 B보다 낮다(`errors:{ need_rank:'B', best_rank }`) |
| `NO_TICKET` | 422 | S2, S3 | 쓸 수 있는 클리어권이 없다 |
| `WEEKLY_LIMIT` | 422 | T1 | 이번 주 구매 한도 초과(`errors:{ limit, used }`) |
| `WEEKLY_NOT_READY` | 422 | T2 | 주간 활동 목표 미달(`errors:{ goal, progress }`) |
| `WEEKLY_ALREADY_CLAIMED` | 409 | T2 | 이번 주 보상을 이미 받았다 |
| `CAMPAIGN_STATE` | 409 | MC4, MC5 | 이 상태에서는 할 수 없는 동작 |
| `CAMPAIGN_SELF_APPROVAL` | 403 | MC4 | 작성자는 승인할 수 없다 |
| `CAMPAIGN_LIMIT` | 422 | MC1 | 상한(골드, 수량, 기간, 대상 수) 초과(`errors:{ field }`) |
| `CAMPAIGN_TARGET_INVALID` | 422 | MC1 | 대상 조건 오류(없는 계정 id 등) |
| `CAMPAIGN_WINDOW_PASSED` | 422 | MC4 | 배달 기간이 이미 끝났다 |

기존 코드 재사용: `ECONOMY_HOLD`(403), `PRESENCE_REQUIRED`(409), `CHARACTER_NOT_FOUND`(404), `DUNGEON_UNKNOWN`·`DUNGEON_CLOSED_TODAY`·`LEVEL_TOO_LOW`·`NO_ENTRIES_LEFT`(422), `RUN_ACTIVE`·`IN_PARTY_RUN`(409), `NOT_ENOUGH_GOLD`(422), `MAIL_NOT_FOUND`(404), `MAIL_ALREADY_CLAIMED`(409), `MAIL_EXPIRED`(410), `GOLD_CAP_EXCEEDED`(422), `ITEM_NOT_FOUND`(422), `RATE_LIMITED`(429).

## 2. 요약

### 2.1 신규 엔드포인트 11개

| # | 메서드 | 경로 | 인증 | 하는 일 | 절 |
|---|---|---|---|---|---|
| S1 | GET | `/characters/{uuid}/sweep` | 액세스 | 소탕 현황(클리어권, 입장, 구매 한도, 주간 활동, 던전별 가능 여부·예상 경험치) | 4.4 |
| S2 | POST | `/characters/{uuid}/sweep/run` | 액세스 | 소탕 1회 | 4.5 |
| S3 | POST | `/characters/{uuid}/sweep/run-all` | 액세스 | 남은 입장 횟수만큼 한 번에 소탕 | 4.6 |
| T1 | POST | `/characters/{uuid}/sweep/tickets/buy` | 액세스 | 잡화점 클리어권 구매(주 7장) | 5.1 |
| T2 | POST | `/characters/{uuid}/sweep/weekly/claim` | 액세스 | 주간 활동 보상(클리어권 3장) 수령 | 5.3 |
| MC1 | POST | `/admin/mail-campaigns` | operator | 캠페인 작성(승인 대기) | 7.4 |
| MC2 | GET | `/admin/mail-campaigns` | viewer | 캠페인 목록 | 7.4 |
| MC3 | GET | `/admin/mail-campaigns/{uuid}` | viewer | 캠페인 현황(발송·수령·미수령·회수 수치) | 7.4 |
| MC4 | POST | `/admin/mail-campaigns/{uuid}/approve` | owner(작성자 외) | 승인(2인 확인), 발송 시작 | 7.4 |
| MC5 | POST | `/admin/mail-campaigns/{uuid}/cancel` | owner(대기 중은 작성자도) | 취소, 선택으로 미수령 회수 | 7.4 |
| MC6 | GET | `/admin/mail-campaigns/{uuid}/deliveries` | viewer | 발송 대상별 수령 현황 | 7.4 |

### 2.2 기존 변경 (E1~E12)

| # | 대상 | 변경 | 절 |
|---|---|---|---|
| E1 | `dungeonRepository.countEntries` | 하루 입장 횟수에 소탕을 더한다(직접 입장, 파티 시작, 목록 모두 같은 함수) | 5.5 |
| E2 | `dungeonRepository.clearCounts`(퀘스트 전용) | "던전 클리어 N회"에 소탕을 더한다. `clearSummary`는 변경 없음(해금 불인정) | 8.2 |
| E3 | `dungeonResult.finalizeCleared` | 보상이 잠기지 않은 요일 던전 직접 클리어 때 주간 카운터 +1 | 5.3 |
| E4 | `antiabuse/killPresence.ts` `ActionKind` | `'sweep'` 추가(맵 없음, 신선도만) | 8.1 |
| E5 | `antiabuse/incomeMeter.ts` | `XP_REASONS`에 `dungeon_sweep` 추가 | 8.3 |
| E6 | 경제 정지 대상 경로 | `assertNoHold`: 소탕, 클리어권 구매, 주간 보상 수령 추가(우편 수령은 기존) | 8.1 |
| E7 | `economy/economyContext.addItem` | 클리어권 키를 가방에 넣으려 하면 오류(프로그래밍 오류 방지) | 5.2 |
| E8 | 우편 M1~M4 | 제목·본문·첨부 여러 개·기한 필드, 수령 분기, 탭 필터 | 6 |
| E9 | `auction/auctionTicker.expireMailById` | 첨부 표가 있는 우편의 기한 폐기(아이템 원장 -n, 골드 소각 기록) | 6.4 |
| E10 | `ops/jobs/integrity.ts` | 우편 보존식에 `mail_attachments`와 `admin_grants`/캠페인 분리 반영 | 6.5 |
| E11 | `chat/mailNotify` | 캠페인 우편 도착 알림에 제목을 싣는다 | 7.2 |
| E12 | `antiabuse/presenceService` 진입, `mail/mailService.mailSummary` | 캠페인 배달 호출(커밋 뒤, 실패해도 응답에 영향 없음) | 7.2 |

### 2.3 신규 테이블 (0021)

`sweep_ticket_lots`, `sweep_ticket_ledger`, `dungeon_sweeps`, `account_week_counters`, `mail_campaigns`, `mail_campaign_attachments`, `mail_attachments`, `mail_campaign_deliveries` (8개). 기존 표 변경: `mails`(+4열, 제약 교체), `characters`(인덱스 1개), `gold_ledger`(사유 `sweep_ticket_buy`), `xp_ledger`(사유 `dungeon_sweep`), `anomaly_log`(kind `sweep_denied`), `admin_audit_log`(대상 `campaign`). SQL은 9절.

### 2.4 기능별 테이블 사용처 (schema.sql 머리말 표에 더할 행)

| 기능 | 읽기 | 쓰기 |
|---|---|---|
| 소탕 현황 S1 (10단계) | characters, accounts, dungeon_runs(클리어 기록), dungeon_sweeps(오늘 횟수), sweep_ticket_lots, account_week_counters, economy_holds | - |
| 소탕 S2, S3 (10단계) | characters(행 잠금), accounts(행 잠금), dungeon_runs, dungeon_sweeps, sweep_ticket_lots(행 잠금), economy_holds, online_sessions | dungeon_sweeps, sweep_ticket_lots, sweep_ticket_ledger(sweep_use), characters(level, xp, gold), xp_ledger(dungeon_sweep), gold_ledger·item_ledger(dungeon_card), character_items, income_hourly, anomaly_log(sweep_denied), request_log |
| 클리어권 구매 T1 (10단계) | characters(행 잠금, 골드), accounts(행 잠금), characters(계정 최고 레벨), account_week_counters, economy_holds | characters.gold, gold_ledger(sweep_ticket_buy), sweep_ticket_lots, sweep_ticket_ledger(shop_buy), account_week_counters(sweep_buy), request_log |
| 주간 활동 수령 T2 (10단계) | characters(행 잠금), accounts(행 잠금), account_week_counters, economy_holds | account_week_counters(activity_claim), sweep_ticket_lots, sweep_ticket_ledger(weekly_activity), request_log |
| 요일 던전 직접 클리어 (10단계 변경) | (기존) | account_week_counters(direct_clear) |
| 우편 조회·요약 (10단계 변경) | mails, mail_attachments, mail_campaigns(캠페인 캐시) | (배달 시 아래 행) |
| 우편 수령·모두 받기 (10단계 변경) | characters(행 잠금), accounts(행 잠금), mails(행 잠금), mail_attachments, sweep_ticket_lots, economy_holds | mails.claimed_at, characters.gold, gold_ledger(mail_claim), character_items, item_ledger(mail_claim), sweep_ticket_lots, sweep_ticket_ledger(campaign_claim), request_log |
| 캠페인 배달 (10단계) | mail_campaigns(캐시), mail_campaign_deliveries, accounts, characters, mail_campaign_attachments | mails, mail_attachments, item_ledger(admin_grant, 위치 mail), mail_campaign_deliveries, mail_campaigns.issued_count |
| 캠페인 관리자 MC1~MC6 (10단계) | mail_campaigns, mail_campaign_attachments, mail_campaign_deliveries, mails, accounts, admin_users | mail_campaigns, mail_campaign_attachments, admin_audit_log |
| 클리어권 만료·캠페인 정리 작업 (10단계) | sweep_ticket_lots, mail_campaigns, mails | sweep_ticket_lots(remaining 0), sweep_ticket_ledger(expire), mail_campaigns(status), mails.expires_at(회수), job_runs |

### 2.5 서버 폴더 (새 코드의 위치)

`server/src/domains/sweep/`(도메인 단위, 지역성 우선): `sweepRules.ts`(순수 함수: 경험치, 카드, 가격), `sweepData.ts`(`sweep.json` zod), `ticketWallet.ts`(지갑 add/consume/expire/total), `sweepService.ts`·`sweepRoutes.ts`·`sweepValidation.ts`·`sweepController.ts`·`sweepRepository.ts`(S1~S3, T1, T2), `weeklyCounter.ts`. 우편 확장은 `domains/mail/`에 `mailAttachments.ts`(첨부 읽기·수령·폐기), 캠페인 배달은 `domains/mail/campaignDelivery.ts`(+`campaignCache.ts`). 관리자는 `admin/mailcampaigns/`(`Validation`, `Repository`, `Service`, `Controller`, `Routes`). 작업은 `ops/jobs/sweepTicketExpire.ts`, `campaignSweep.ts`, `campaignRevoke.ts`.

## 3. 소탕 보상 계산식과 이론 계산

### 3.1 계산식 (서버 단독)

입력은 서버가 읽은 `dungeons.json`(던전 `d`, 난이도 `diff`)과 캐릭터의 직업뿐이다.

```
소탕 경험치 = roundHalfEven( f32( max( f32(d.clearXp x diff.rewardMul), d.clearXpFloor[tier] ) x d.xpMul )
                             x f32(1 + sweep.xpBonusPercent / 100) )
              tier = 난이도 번호(0 일반, 1 모험, 2 왕, 3 영웅)   // 기존 clearXp()와 같다
```

기존 `dungeonRules.clearXp(eco, d, diff, rank)`가 `xpBonusPercent(eco, rank)`를 곱하는 부분만 "보너스 퍼센트를 직접 받는 형태"로 나눠(`clearXpBase`와 `applyBonus`) 소탕은 `sweep.xpBonusPercent`(권장 0)를 쓴다. 랭크로 보너스를 고르지 않는다. **언더레벨 감쇠(9단계 A4)는 적용하지 않는다**(소탕은 권장 레벨 이상만 가능하므로 격차가 없다). 만렙이면 `grantXp`가 0을 돌려주므로 경험치만 0이고 카드는 나온다.

| 요소 | 소탕 | 근거 키 |
|---|---|---|
| 클리어 경험치 | 위 식 | `dungeons[].clearXp`, `clearXpFloor[tier]`, `xpMul`, `difficulties[].rewardMul`, `sweep.json xpBonusPercent` |
| 등급 보너스 | 없음(0%) | `dungeons.json ranking.xpBonus`는 쓰지 않는다 |
| 처치 경험치 | 없음 | `monsters.json`을 읽지 않는다 |
| 드롭(처치 드롭) | 없음 | `rollKillDrops`를 부르지 않는다 |
| 카드 | 무작위 1장(`sweep.json cardCount`=1) | `cards.count`(4)는 직접 플레이용 |
| 카드 풀 | 던전의 `rewards[]` + 난이도 `ticketWeight`(장비 보호권), 개수 배율 `rewardMul`(보호권 제외), 장비 최소 등급 `minGearRarity`, 장비 단계 `tierOfLevel(recommendedLevel)` | 직접 플레이와 같은 `rollGear` |
| 장비 확률 | 직접의 **정확히 70%**(`sweep.json gearKeepPercent`) | 아래 3.2 |
| 대박 카드 | 없음 | `difficulties[].jackpotPerMille`를 읽지 않는다 |
| 카드 받기 | 선택 단계 없이 즉시 지급(`dungeon_card` 사유, 직접 플레이와 같은 원장 경로) | `grantCard`와 같은 코드 |

### 3.2 카드 한 장 굴리기와 "장비 확률 70%"의 정의 (D3)

`rollSweepCard(eco, d, diff, cls, rng)`:

1. `rollCards`의 표 구성과 같은 가중 풀을 만든다(보호권 항목 포함).
2. 가중 추첨으로 항목 하나를 고른다.
3. 고른 항목이 `gear`이면 `rng.int(0, 100) >= gearKeepPercent`(30% 확률)일 때 **장비가 아닌 항목들만의 가중 풀에서 다시 추첨**한다. 비장비 항목이 하나도 없으면 장비로 둔다.
4. 장비면 `rollGear(eco, cls, diff.minGearRarity, rng, tier)`, 아니면 직접 플레이와 같은 개수 규칙(`min..max` 균등, 보호권 외에는 `rewardMul` 곱, 최소 1).
5. 대박 굴림은 하지 않는다.

결과: 장비 카드가 나올 확률은 직접 플레이의 카드 한 장 확률 x 0.7로 **정확히** 맞고(예: 장비 가중 10 / 합 104면 9.6%에서 6.7%로), 줄어든 몫은 비장비 항목이 비례해서 가져간다. (가중치만 0.7배로 줄이는 방식은 확률이 정확히 0.7배가 되지 않아 쓰지 않는다.)

### 3.3 이론 계산 (직접 S등급 대비 55~65% 목표 확인)

**모델**(데이터 기준일 2026-10-06, 단일 사용자 솔로, AI 동반 없음, 전 몬스터 처치, 클리어 후 카드 한 장):

- 직접 S등급 경험치 = `round(기본 클리어 경험치 x 1.30)`(`ranking.xpBonus`의 S 값 30) + 처치 경험치 합. 처치 경험치는 몬스터 레벨 `1 + monsterLevel + levelOffset`의 `xpByLevel`, 보스는 레벨 오프셋 3.
- 직접 처치 드롭 가치(NPC 판매가 기준, 골드는 액면): 몬스터 1마리당 기본 골드 평균 12 + 재료 기대값(뼈 조각 1.5개 x 5, 강화석 0.35개 x 20, 마력 정수 0.08개 x 80 = 20.9) = 32.9. 보스는 추가 골드 평균 200, 황금 해골은 추가 골드 평균 80. **빼고 센 것**: 장비 드롭(처치당 40%), 황금 해골의 타격당 골드, 대박 카드, 장비 보호권. 모두 직접 쪽에만 있는 이득이라 아래 비율은 실제보다 **높게(소탕에 유리하게)** 나온다.
- 카드 기대값: 비장비 항목의 기대 가치(개수 평균 x 개수 배율 x NPC 판매가)를 가중 평균. 직접은 장비 확률 p, 소탕은 0.7p로 비장비가 늘어나는 보정 `(1 - 0.7p) / (1 - p)`를 곱한다(p: 6개 던전 중 무기고 0.40, 나머지 0.096, 영웅은 보호권 가중 때문에 약간 작다).
- 비율 두 가지: **경험치만**(w=0), **경험치 + 드롭·카드 가치를 1:1로 환산**(w=1, "골드 1 = 경험치 1"은 단순 가정이고 실제 환산은 이 사이에 있다고 본다).

소탕 경험치 = 클리어 기본값(보너스 0%). 직접 S의 경험치 = 클리어(S) + 처치.

| 던전 | 난이도 | 소탕 경험치 | 직접 S 경험치 (클리어+처치) | 비율 w=0 | 비율 w=1 |
|---|---|---:|---:|---:|---:|
| 황금 광맥 | 일반 | 1,102 | 2,329 (1,433+896) | 47.3% | 32.6% |
| 황금 광맥 | 모험 | 2,771 | 5,054 (3,602+1,452) | 54.8% | 45.1% |
| 황금 광맥 | 왕 | 5,913 | 9,777 (7,687+2,090) | 60.5% | 54.6% |
| 황금 광맥 | 영웅 | 12,474 | 18,862 (16,216+2,646) | 66.1% | 62.6% |
| 버려진 제련소 | 일반 | 1,153 | 2,344 (1,499+845) | 49.2% | 38.9% |
| 버려진 제련소 | 모험 | 2,849 | 5,078 (3,704+1,374) | 56.1% | 50.1% |
| 버려진 제련소 | 왕 | 6,036 | 9,814 (7,847+1,967) | 61.5% | 58.0% |
| 버려진 제련소 | 영웅 | 12,624 | 18,907 (16,411+2,496) | 66.8% | 64.7% |
| 마력의 묘지 | 일반 | 1,270 | 2,513 (1,651+862) | 50.5% | 41.2% |
| 마력의 묘지 | 모험 | 3,077 | 5,395 (4,000+1,395) | 57.0% | 51.7% |
| 마력의 묘지 | 왕 | 6,423 | 10,351 (8,350+2,001) | 62.1% | 59.1% |
| 마력의 묘지 | 영웅 | 13,308 | 19,832 (17,300+2,532) | 67.1% | 65.4% |
| 수련의 숲 | 일반 | 1,911 | 3,342 (2,484+858) | 57.2% | 47.5% |
| 수련의 숲 | 모험 | 4,638 | 7,409 (6,029+1,380) | 62.6% | 57.4% |
| 수련의 숲 | 왕 | 9,662 | 14,544 (12,561+1,983) | 66.4% | 63.6% |
| 수련의 숲 | 영웅 | 20,002 | 28,508 (26,003+2,505) | 70.2% | 68.7% |
| 망자의 무기고 | 일반 | 1,322 | 2,662 (1,719+943) | 49.7% | 40.5% |
| 망자의 무기고 | 모험 | 3,198 | 5,679 (4,157+1,522) | 56.3% | 51.0% |
| 망자의 무기고 | 왕 | 6,663 | 10,845 (8,662+2,183) | 61.4% | 58.4% |
| 망자의 무기고 | 영웅 | 13,796 | 20,699 (17,935+2,764) | 66.6% | 64.9% |

(수련의 숲의 기본 클리어 경험치는 `xpMul 1.5`가 곱해진 값이다. 처치 경험치에는 그 배율이 없어 소탕 비율이 다른 던전보다 3~4%p 높다.)

보조 수치(참고): 가격 환산 없는 비교.

| 항목 | 직접 플레이(1회) | 소탕(1회) |
|---|---|---|
| 장비 기대 개수 | 처치 드롭 약 6.8~7.7개 + 카드 | 카드 0.067개(무기고 0.26~0.28개) |
| 대박(유니크 이상) 카드 | 카드 1장당 0.3~0.6% | 0 |
| 카드 장비 확률 | `10/104` = 9.6% (무기고 40%) | 6.7% (무기고 28%) |
| 시간 | 던전 기준 150~230초 + 이동·결과 | 0 |

**결론**

1. 계획의 규칙 그대로(보너스 0%)이면 **왕·영웅 난이도가 목표 55~65%에 들어온다**(w=0과 w=1 사이 표 범위: 왕 54.6~66.4%, 영웅 62.6~70.2%). 소탕으로 주로 돌리는 난이도는 자기 레벨의 가장 높은 난이도이므로 목표 구간은 여기서 평가하는 것이 맞다.
2. 일반·모험은 33~57%로 목표 아래다. 직접 플레이의 처치 드롭(황금 광맥은 황금 해골의 추가 골드)이 상대적으로 커서 나오는 현상이고, 낮은 난이도를 소탕할 이유가 약해지는 방향이라 의도에 맞는다(별도 보정하지 않는다, D2에 포함).
3. "B등급 값(+10%)"으로 읽으면 영웅이 68~77%로 목표 위로 올라가므로 권장하지 않는다. 다만 이 값은 `sweep.json xpBonusPercent` 한 곳이라 구현 뒤에도 데이터만 바꿔 조정할 수 있다.
4. 수련의 숲 영웅(68.7~70.2%)만 목표를 약간 넘는다. `xpMul` 때문이며 그대로 둔다(D2). 줄이려면 `sweep.json`에 던전별 계수를 더하는 후속이 필요하다.
5. 이 표는 `Tools/balance/theory_sweep.py`(구현 단계에서 만든다)가 `dungeons.json`, `monsters.json`, `shop.json`에서 다시 계산해 같은 표를 내도록 하고, 소탕 값이나 던전 값이 바뀌면 다시 돌린다. 서버는 이 스크립트나 표를 읽지 않는다.

### 3.4 기존 소득 상한(9단계)과의 정합

`income_caps.json`의 일일 던전 덩어리(`perDay.dungeonXp`, `dungeonGoldEq`)는 "하루 입장 3회를 최고 보상으로 다 쓴 값"이다. 소탕은 같은 3회 입장을 쓰고 보상이 직접 S보다 작으므로(3.3 표) **상한 표를 바꿀 필요가 없다**(예: 영웅 대역 `dungeonXp 90,011` > 소탕 3회 최대 20,002 x 3 = 60,006). 소탕 경험치를 속도 집계에 넣기 위해 `incomeMeter.XP_REASONS`에 `dungeon_sweep`만 더한다(8.3절). 이 정합은 테스트 B-14로 고정한다.

## 4. 소탕 (S1~S3)

### 4.1 조건 (PLAN 2절 그대로, 확인 순서)

판정은 **이 순서로 처음 걸리는 하나**를 거절한다(S3도 같다). 5~7번은 UI가 이미 막는 항목이라 걸리면 우회 시도로 보고 `anomaly_log(kind='sweep_denied', severity 1)`를 남긴다(`RAID_LOCKED`의 `AnomalyError` 방식).

| # | 검사 | 실패 |
|---|---|---|
| 1 | 기능 플래그, 점검 중 새 판 차단(`POST /dungeon-runs`가 거치는 같은 차단 지점) | `503 FEATURE_DISABLED` / 점검 응답 |
| 2 | `assertNoHold(client, accountId, characterId)` (캐릭터 행을 잠근 뒤) | `403 ECONOMY_HOLD` |
| 3 | `assertActionPresence(ctx, 'sweep', null)` (신선도만, 맵 없음, 모드는 `PRESENCE_KILL_MODE`를 따른다) | `409 PRESENCE_REQUIRED` |
| 4 | 진행 중인 판이 없다(`findPlayingRun`), 파티 판에 참여 중이 아니다(`inPartyRun`) | `409 RUN_ACTIVE` / `409 IN_PARTY_RUN` |
| 5 | 던전 존재, 레이드 아님, 오늘 개방(`isOpenToday`) | `422 DUNGEON_UNKNOWN` / `SWEEP_NOT_ALLOWED` / `DUNGEON_CLOSED_TODAY` |
| 6 | 그 요일 던전 **그 난이도**를 직접 클리어한 기록이 있고, 그 최고 등급이 `sweep.json minRank`(B=4) 이상(번호가 작을수록 좋다). 기록은 `dungeon_runs.state='cleared' AND NOT reward_locked`만 센다 | `422 SWEEP_NOT_CLEARED` / `SWEEP_RANK_LOW` |
| 7 | 캐릭터 레벨 >= `diff.recommendedLevel`(`partyMinLevelSlack` 여유를 **적용하지 않는다**) | `422 LEVEL_TOO_LOW` (`errors:{ need, have }`) |
| 8 | 하루 입장 횟수: `countEntries`(직접 입장 + 소탕) < `dailyEntries` | `422 NO_ENTRIES_LEFT` |
| 9 | 쓸 수 있는 클리어권 >= 1 | `422 NO_TICKET` |

소탕 해금 기록은 **캐릭터별**이다(D7). 부캐는 자기 캐릭터로 직접 클리어해야 한다(계정 단위 클리어권으로 부캐를 레벨링하는 지름길을 막는다).

### 4.2 처리 흐름 (한 번의 소탕, 한 트랜잭션 안)

`runEconomy`가 캐릭터 행을 잠그고 `request_log` 멱등성을 처리한다. 핸들러:

1. 4.1의 1~7번 검사. 8번 입장 횟수는 `entries.left`로 읽는다.
2. `SELECT id FROM accounts WHERE id = $1 FOR UPDATE`(락 순서 ②). 지갑 행을 `ticketWallet.lockLive(accountId, now)`로 읽는다(`remaining > 0 AND (expires_at IS NULL OR expires_at > now)`, `ORDER BY expires_at NULLS LAST, id`, `FOR UPDATE`).
3. 횟수 `n`: S2는 1, S3는 `min(entries.left, 지갑 합계)`. `n = 0`이면 `NO_ENTRIES_LEFT`(입장 0) 또는 `NO_TICKET`.
4. `n`번 반복:
   1. 소탕 uuid를 서버가 만든다(`randomUUID()`).
   2. 지갑 소모: 만료가 가장 가까운 이벤트 로트부터, 그다음 일반 로트 한 장. `UPDATE sweep_ticket_lots SET remaining = remaining - 1 WHERE id = $1 AND remaining >= 1`이 1행이 아니면 `NO_TICKET`(동시 소모 방어). 원장 `sweep_ticket_ledger(reason='sweep_use', delta=-1, ref=소탕 uuid)`.
   3. 경험치: 3.1 식. `ctx.grantXp(xp, 'dungeon_sweep', 소탕 uuid)`.
   4. 카드: `rollSweepCard`. 골드면 `ctx.changeGold(count, 'dungeon_card', 소탕 uuid)`, 아니면 `ctx.addItem('bag', item_key, count, 'dungeon_card', 소탕 uuid)`(귀속은 `bindFor`가 정한다).
   5. `INSERT INTO dungeon_sweeps (uuid, character_id, dungeon_id, difficulty, reset_day, lot_id, xp_granted, card, request_id)`.
5. 응답 조립(4.5). `delta`는 `ctx.delta()` 한 번(요청 끝의 최종 상태).

**전부 아니면 없음**: 중간에 어떤 오류가 나도 트랜잭션이 롤백되어 일부만 소탕되는 일이 없다. 같은 `request_id` 재전송은 첫 응답을 그대로 돌려준다.

### 4.3 직접 플레이와 달라지는 것 (구현자 체크)

| 직접 플레이 경로 | 소탕 |
|---|---|
| `dungeon_runs` 행 | 만들지 않음. `dungeon_sweeps`에 쓴다 |
| 카드 4장 저장 후 `cards/pick` | 카드 1장을 즉시 지급, 선택 API 없음, `unpickedRuns`에 안 나온다 |
| `xp_ledger` 사유 `dungeon_clear` | 사유 `dungeon_sweep` |
| 처치 보고, 결과 검증, 보류(`held`) | 없음(검증할 클라이언트 값이 없다) |
| 난이도 해금, 최고 랭크, 업적 | 반영 안 함 |

### 4.4 S1 `GET /characters/{uuid}/sweep`

- 인증: 액세스 토큰, 내 캐릭터. 캐릭터 행은 잠그지 않는다(읽기 전용).
- 응답 `200` `data`:

```json
{
  "server_time": "2026-10-06T03:00:00.000Z",
  "reset": { "daily_start_at": "...", "next_daily_at": "...", "weekly_start_at": "...", "next_weekly_at": "..." },
  "tickets": {
    "total": 5, "normal": 3,
    "event": [ { "count": 2, "expires_at": "2026-10-20T03:00:00.000Z" } ]
  },
  "entries": { "limit": 3, "used": 1, "left": 2 },
  "shop": { "weekly_limit": 7, "weekly_used": 2, "weekly_left": 5, "unit_price": 5000 },
  "weekly_activity": { "goal": 10, "progress": 6, "reward_tickets": 3, "claimed": false, "claimable": false },
  "hold": false,
  "dungeons": [
    { "id": "gold_vein", "open_today": true,
      "difficulties": [
        { "difficulty": 0, "can_sweep": true, "block": null, "best_rank": 2, "need_level": 5, "xp": 1102 },
        { "difficulty": 3, "can_sweep": false, "block": "NOT_CLEARED", "best_rank": null, "need_level": 27, "xp": 12474 }
      ] }
  ]
}
```

- `block` 값: `null | 'CLOSED_TODAY' | 'NOT_CLEARED' | 'RANK_LOW' | 'LEVEL_TOO_LOW'`(4.1의 5~7번). 입장·클리어권·정지는 던전별이 아니라 `entries`, `tickets`, `hold`로 읽는다. `can_sweep`은 던전 조건(5~7번)만 본다.
- `xp`는 3.1 식의 서버 값(안내용, 클라이언트가 계산하지 않는다).
- 에러: `401`, `404 CHARACTER_NOT_FOUND`, `429`. 속도 제한: 캐릭터당 초당 2회(`RATE_SWEEP_STATUS_PER_SEC`). 멱등성: 읽기.
- 조회: 클리어 기록은 `dungeon_runs_clears (character_id, dungeon_id, difficulty) WHERE state='cleared'`가 이미 있어 `GROUP BY dungeon_id, difficulty`로 한 번에 읽는다(`reward_locked` 제외 조건은 이 부분 인덱스 위의 필터).

### 4.5 S2 `POST /characters/{uuid}/sweep/run`

- 요청(`.strict()`):

```
z.strictObject({
  request_id: z.uuid(),
  dungeon_id: z.string().min(1).max(40),
  difficulty: z.number().int().min(0).max(3),
})
```

받지 않는 값: 경험치, 카드, 횟수, 시간, 점수.

- 응답 `201` `data`:

```json
{
  "sweeps": [
    { "id": "<uuid>", "dungeon_id": "gold_vein", "difficulty": 2,
      "xp": 5913, "leveled_up": false,
      "card": { "item_key": "mat_bone", "count": 14 },
      "ticket": "event" }
  ],
  "summary": { "count": 1, "total_xp": 5913 },
  "entries": { "limit": 3, "used": 2, "left": 1 },
  "tickets": { "total": 4, "normal": 3, "event": [ { "count": 1, "expires_at": "..." } ] },
  "delta": { "gold": 1200, "level": 12, "xp": 340, "stacks": [ ... ] }
}
```

- `ticket`은 그 소탕에 쓴 클리어권 종류(`'event' | 'normal'`). `card.item_key`는 직접 플레이와 같은 키 체계(장비는 +0 기본 키, 골드는 `gold`).
- 에러: `400 VALIDATION`, `401`, `403 ECONOMY_HOLD`, `404 CHARACTER_NOT_FOUND`, `409 PRESENCE_REQUIRED | RUN_ACTIVE | IN_PARTY_RUN`, `422 DUNGEON_UNKNOWN | SWEEP_NOT_ALLOWED | DUNGEON_CLOSED_TODAY | SWEEP_NOT_CLEARED | SWEEP_RANK_LOW | LEVEL_TOO_LOW | NO_ENTRIES_LEFT | NO_TICKET | IDEMPOTENCY_MISMATCH`, `429 RATE_LIMITED`, `503 FEATURE_DISABLED`.
- 멱등성: `request_id`. 같은 id의 재전송은 같은 응답과 같은 상태 코드(201)를 돌려주고 원장은 늘지 않는다.
- 속도 제한: 캐릭터당 초당 2회(`RATE_SWEEP_RUN_PER_SEC`). 하루 3회 입장이 실제 상한이다.

### 4.6 S3 `POST /characters/{uuid}/sweep/run-all`

요청·에러는 S2와 같다. 횟수 `n = min(남은 입장 횟수, 지갑 합계)`를 서버가 정하고 클라이언트는 횟수를 보내지 않는다("남은 횟수만큼 한 번에"). 응답 `201`의 `sweeps`가 `n`개(오래된 순), `summary.count = n`, `summary.total_xp`, `leveled_up`은 항목마다. 쓰는 클리어권은 만료가 가까운 이벤트권부터다. 속도 제한: 캐릭터당 초당 1회(`RATE_SWEEP_ALL_PER_SEC`). 멱등성: `request_id`(한 요청이 한 트랜잭션).

`n`이 입장 횟수보다 클리어권이 부족해서 줄어들면 응답에 `limited_by: 'tickets' | 'entries'`를 더해 클라이언트가 안내한다(n=0이면 오류).

### 4.7 요일 던전 보상 표와의 대응

3.1절 표가 키 대응이다. 구현은 `dungeonRules.rollCards`의 한 번 굴림 본문을 `rollOneCard`로 분리해 직접 플레이와 소탕이 같은 카드 풀·개수 규칙·`rollGear`를 쓰게 한다(복사하지 않는다). 소탕은 `rollOneCard`에 "장비 70% 재굴림, 대박 없음" 옵션을 준다. 직접 플레이의 결과는 한 비트도 바뀌지 않아야 한다(기존 `rollCards` 테스트 유지, 난수 호출 순서 불변).

## 5. 클리어권 지갑, 구매, 주간 활동

### 5.1 T1 `POST /characters/{uuid}/sweep/tickets/buy`

- 요청: `z.strictObject({ request_id: z.uuid(), count: z.number().int().min(1).max(7) })`. 금액은 받지 않는다.
- 가격: **서버가 계산**한다.

```
unit_price = sweep.shop.basePrice x (tierOfLevel(계정 최고 레벨) + 1)
계정 최고 레벨 = max(characters.level) WHERE account_id = $1   // 삭제한 캐릭터 포함(D5)
total = unit_price x count
```

`tierOfLevel`은 기존 장비 단계 함수(`utils/gearTier.ts`, 경계 1/10/15/20/25/30/35/40)다(D5). 가격 예(기본 1,000 기준): 레벨 1~9는 1,000, 10~14는 2,000, ..., 40은 8,000. 주 7장 비용은 7,000~56,000이고 같은 대역의 사냥터 한 시간 `goldEq`(`income_caps.json`) 대비 9%~45%다(약한 골드 소모처. 가격 배수는 `sweep.json`에서 조정 가능, D5에 기재).

- 처리(`runEconomy`): `assertNoHold` -> 계정 행 `FOR UPDATE` -> `account_week_counters(account_id, week_start, 'sweep_buy')`를 읽어 `used + count <= weeklyLimit`(아니면 `WEEKLY_LIMIT`) -> 골드 확인(`NOT_ENOUGH_GOLD`) -> `ctx.changeGold(-total, 'sweep_ticket_buy', 'sweep_ticket')` -> 지갑에 일반권 `count`장 추가(`sweep_ticket_lots` 일반 행 upsert, 원장 `shop_buy`, ref=요청의 `request_id`) -> 카운터 `used = used + count`(`UPDATE ... WHERE used + $count <= $limit`가 0행이면 `WEEKLY_LIMIT`, 안전장치).
- 응답 `200` `data`: `{ count, unit_price, total, tickets, shop: { weekly_limit, weekly_used, weekly_left, unit_price }, delta }`.
- 에러: `400`, `401`, `403 ECONOMY_HOLD`, `404`, `422 WEEKLY_LIMIT | NOT_ENOUGH_GOLD | IDEMPOTENCY_MISMATCH`, `429`, `503`. 속도 제한: 캐릭터당 초당 1회(`RATE_SWEEP_BUY_PER_SEC`). 멱등성: `request_id`.
- 정지 중에는 막는다: 정지된 캐릭터의 골드가 계정 지갑으로 옮겨져 정지 안 된 다른 캐릭터의 소탕으로 쓰이는 길을 막기 위해서다(8.1절).

### 5.2 지갑 규칙 (`ticketWallet.ts`)

| 규칙 | 내용 |
|---|---|
| 소유 | 계정(`account_id`). 같은 계정의 어느 캐릭터든 쓴다. 캐릭터 행이 아니라 계정 행을 잠가 직렬화 |
| 일반 로트 | 계정당 1행(`UNIQUE (account_id) WHERE kind='normal'`), `expires_at` 없음. 추가는 `INSERT ... ON CONFLICT DO UPDATE SET granted = granted + n, remaining = remaining + n` |
| 이벤트 로트 | 지급(우편 수령)마다 1행, `expires_at = 수령 시각 + sweep.eventTicketDays(14)일`(D4) |
| 소모 순서 | 만료가 가까운 이벤트 로트, 그다음 일반 로트 |
| 만료 | 조회와 소모는 `expires_at > now`만 센다(작업이 늦어도 못 쓴다). 작업 `sweep_ticket_expire`가 지난 로트의 `remaining`을 0으로 만들며 원장 `expire`를 쓴다 |
| 보존식 | 로트별 `SUM(원장 delta) = remaining`(만료 작업 지연 구간 제외). `granted >= remaining` |
| 거래 불가 | 가방·창고·경매 어디에도 못 들어간다. `character_items`에는 클리어권 키가 없고, `EconCtx.addItem`이 `sweep.json`의 클리어권 키를 받으면 오류를 던진다(E7). `shop.json` 진열에도 두지 않는다(구매는 T1만) |
| 지급 경로 | 구매(T1), 주간 활동(T2), 캠페인 우편 수령(7절), 그 밖에 없다 |

### 5.3 T2 `POST /characters/{uuid}/sweep/weekly/claim`

- 요청: `z.strictObject({ request_id: z.uuid() })`.
- 집계: 요일 던전(레이드 아님) **직접 클리어 중 보상이 잠기지 않은 판**의 이번 주 횟수를 **계정 합산**으로 센다(D6). `finalizeCleared`가 성공적으로 확정할 때(`!dungeon.isRaid && !locked`) `account_week_counters(account_id, week_start, 'direct_clear')`를 `used + 1`로 올린다. `week_start = resetBoundaries(inp.at).weeklyStartAt`(클리어 시각 기준. 보류가 풀려 늦게 확정돼도 그 주에 쌓인다). 소탕은 세지 않는다.
- 처리: `assertNoHold` -> 계정 행 잠금 -> `direct_clear >= sweep.weekly.directClears(10)` 확인(`WEEKLY_NOT_READY`) -> `INSERT ... account_week_counters(account_id, week_start, 'activity_claim', used=1) ON CONFLICT DO NOTHING`이 0행이면 `409 WEEKLY_ALREADY_CLAIMED` -> 지갑에 일반권 `sweep.weekly.rewardTickets(3)`장(원장 `weekly_activity`, ref=`weekly:{계정 uuid}:{week_start ISO}`, `(reason, ref)` 유일 인덱스가 이중 지급을 DB에서 막는다).
- 응답 `200` `data`: `{ claimed: { tickets: 3 }, tickets, weekly_activity: { goal, progress, reward_tickets, claimed: true, claimable: false } }`.
- 에러: `401`, `403 ECONOMY_HOLD`(D12), `404`, `409 WEEKLY_ALREADY_CLAIMED`, `422 WEEKLY_NOT_READY | IDEMPOTENCY_MISMATCH`, `429`, `503`. 속도 제한: 캐릭터당 초당 1회(`RATE_SWEEP_CLAIM_PER_SEC`). 멱등성: `request_id` + 위 DB 유일 제약.

### 5.4 주간 경계와 청소

주 경계는 목요일 06:00 KST(`weeklyStartAt`). 다음 주가 되면 `week_start`가 달라져 새 행이 생긴다(카운터를 초기화하는 작업이 없다). 지난 주 행은 `purge`가 60일 뒤 지운다(`PLAY_TIME_RETENTION_DAYS` 쪽 규칙에 한 줄 추가).

### 5.5 하루 입장 횟수 변경 (E1)

`countEntries(db, characterId, resetDay)`를 다음과 같이 바꾼다(호출처 `dungeonService.listDungeons`, `entryRules.checkEntry`, 파티 시작 모두 이 함수라 한 곳만 고친다).

```sql
SELECT (SELECT count(*) FROM dungeon_runs   WHERE character_id = $1 AND reset_day = $2 AND counts_entry)
     + (SELECT count(*) FROM dungeon_sweeps WHERE character_id = $1 AND reset_day = $2) AS n
```

둘 다 `(character_id, reset_day)` 인덱스를 쓴다. 소탕과 직접 입장이 같은 캐릭터 행 잠금 아래서 일어나므로 동시에 4회가 되지 않는다.

## 6. 우편 확장 (M1~M4 변경)

### 6.1 데이터 모델 (하위 호환)

- 기존 열(`item_key`, `count`, `bind`, `gold`, `kind`, `system_code`, 경매 참조)은 그대로다. 옛 우편은 한 줄도 바뀌지 않는다.
- `mails`에 `title`, `body`, `campaign_id`, `attach_n`을 더한다. 옛 우편은 `title`, `body`가 `NULL`, `attach_n=0`이다(클라이언트는 종류와 `system_code`로 문구를 조립하던 방식 그대로).
- 첨부가 여러 개인 새 우편(캠페인 우편)은 `attach_n > 0`이고 첨부는 `mail_attachments`에 있다. 그때 `mails.item_key`는 `NULL`, `mails.gold`는 0이다(CHECK로 강제). 그래서 `SUM(mails.gold WHERE kind='system')`을 쓰는 기존 보존식, `gold > 0` 탭 필터, `admin_grants` 1:1 규칙이 그대로 맞는다.
- 첨부 종류: `gold`(금액), `item`(키, 개수, 귀속), `sweep_ticket`(이벤트 클리어권 개수). 최대 5개, 골드와 클리어권은 각각 최대 1개.
- `system_code`에 `maintenance`, `apology`, `attendance`, `other`를 더한다(기존 `compensation`, `event`, `refund`, `notice` 유지). 캠페인 `category`와 1:1이다(`event`는 그대로 `event`).

### 6.2 M1 `GET /characters/{uuid}/mail` (변경: 필드 추가만)

요청 쿼리는 그대로(`tab`: `all|gold|item`, `page`, `limit`). `tab` 필터는 첨부 표를 함께 본다: `gold` = (`mails.gold > 0` 또는 골드 첨부 있음), `item` = (`mails.item_key IS NOT NULL` 또는 아이템·클리어권 첨부 있음). 목록 한 건:

```json
{
  "id": "<uuid>", "kind": "system", "system_code": "maintenance",
  "title": "정기 점검 보상", "body": "점검에 협조해 주셔서 감사합니다.\n작은 선물을 드립니다.",
  "campaign": true,
  "attachments": [
    { "slot": 1, "kind": "gold", "item_key": null, "count": 5000, "bind": null },
    { "slot": 2, "kind": "item", "item_key": "potion_hp", "count": 10, "bind": "none" },
    { "slot": 3, "kind": "sweep_ticket", "item_key": "ticket_sweep_event", "count": 1, "bind": null, "valid_days_after_claim": 14 }
  ],
  "ref_item_key": null, "ref_count": null, "item": null, "gold": 0,
  "created_at": "...", "expires_at": "...", "days_left": 12
}
```

- 옛 우편도 `attachments`를 가진다(서버가 `gold` -> `item` 순서로 합성, `title`/`body`는 `null`, `campaign` false). 옛 필드 `item`, `gold`는 그대로 남는다(옛 클라이언트와 새 클라이언트가 둘 다 읽을 수 있다. 캠페인 우편은 옛 클라이언트에서 빈 우편처럼 보이므로 캠페인 배달 플래그는 새 클라이언트가 나간 뒤 켠다, 11절).
- 한 페이지의 첨부는 `WHERE mail_id = ANY($ids)` 한 번으로 읽는다(우편마다 쿼리를 날리지 않는다).
- 만료된 우편은 목록에 없다(기존 `expires_at > now`).

### 6.3 M2·M3 수령 (변경: 분기)

`claimOne(ctx, mail, now, strict)`:

1. `attach_n = 0`이면 기존 경로 그대로.
2. `attach_n > 0`이면 첨부를 읽는다(`ORDER BY slot`). 골드 합계가 `ctx.gold + 합계 > GOLD_CLIENT_MAX`이면 우편 전체를 못 받는다(M2는 `422 GOLD_CAP_EXCEEDED`, M3는 건너뛰고 `skipped`에 센다. 부분 수령 없음).
3. `markClaimed`(조건부 UPDATE, 이미 받았으면 `409 MAIL_ALREADY_CLAIMED`).
4. 첨부별로: `gold`는 한 번에 `ctx.changeGold(합계, 'mail_claim', 우편 uuid)`. `item`은 `item_ledger(mail, -n, 'mail_claim')` 후 `ctx.addItem('bag', key, n, 'mail_claim', 우편 uuid, bind)`(캠페인 생성 때 `admin_grant`로 `mail +n`을 넣어 두었다). `sweep_ticket`은 이벤트 로트 생성(`expires_at = now + eventTicketDays`일) + 원장 `campaign_claim`(ref=우편 uuid, `(reason, ref)` 유일).
5. 응답 `claimed`에 `attachments`(받은 것), 클리어권이 있으면 `tickets`(S1과 같은 형태)를 더한다. `delta`는 기존대로.

M2·M3 모두 `assertNoHold`(기존) 뒤에 **계정 행을 먼저 잠근다**(1절 락 순서). M3의 한 번 최대 통수(`MAIL_CLAIM_ALL_MAX`)와 오래된 순 규칙은 그대로다. 속도 제한: 기존 `RATE_MAIL_CLAIM_PER_SEC`, `RATE_MAIL_CLAIM_ALL_PER_SEC` 그대로.

### 6.4 폐기와 회수 (E9)

기한이 지난 미수령 우편의 폐기(`expireMailById`)는 `attach_n > 0`이면 첨부를 읽어 `item`은 `item_ledger(mail, -n, 'mail_expire')`, `gold`는 `auction_sinks(kind='mail_expire')` 소각 기록, `sweep_ticket`은 아무 기록 없음(아직 지갑에 들어가지 않았다). 캠페인 취소 회수(7.5절)는 `expires_at`을 지금으로 앞당겨 이 경로에 태운다.

### 6.5 보존식·알림·삭제 규칙 (E10, E11)

- 골드 보존식: `SUM(mails.gold WHERE kind='system') = SUM(admin_grants.gold)`은 그대로 맞는다(캠페인 우편의 `mails.gold`는 0). 새 식: `SUM(mail_attachments.amount WHERE kind='gold')` = 캠페인별 `issued_count x 골드 첨부 합`의 합.
- 아이템 보존식(`integrity.ts`의 mail 위치 합): 미수령·미폐기 우편의 `mails.item_key` 합에 `mail_attachments(kind='item')`의 같은 조건 합을 더한다.
- `announceMail`의 이벤트에 `title`을 선택 필드로 더해 `[우편] {title}` 한 줄을 보낸다(없으면 기존 문구).
- 캐릭터 삭제 거절(`hasOpenMail`)은 미수령 우편이면 모두 해당하므로 캠페인 우편도 자동 포함된다.
- 폐기·수령된 `system` 우편과 첨부는 지우지 않는다(7단계 "지우지 않는다" 목록에 `mail_attachments`, `mail_campaign*`, `dungeon_sweeps`, `sweep_ticket_*` 추가).

### 6.6 M4 `GET /characters/{uuid}/mail/summary` (변경)

`new[]` 항목에 `title`(없으면 `null`)을 더한다. 이 요청이 캠페인 배달의 두 번째 트리거다(7.2절, 요청 처리 뒤 비동기).

## 7. 운영 우편 캠페인

### 7.1 상태

```
pending --(승인: 작성자 외 owner)--> active --(기간 종료 또는 상한 도달: 작업)--> ended
   |                                    |                                         |
   +--------(취소)----------------------+--------(취소)---------------------------+--> cancelled (ended는 회수 요청이 있을 때만)
```

`pending`에서 `ends_at`이 지나면 작업이 `cancelled`(`cancelled_by NULL`, `cancel_reason='unapproved_expired'`)로 닫는다. 내용(제목, 본문, 첨부, 대상, 한도, 기간)은 만든 뒤 바뀌지 않는다(DB 트리거가 막는다, 9절).

### 7.2 배달 방식 (접속 시 생성, 계정당 1통, 첫 접속 캐릭터 기준)

1. **트리거 두 곳**(둘 다 같은 함수 `deliverCampaignsFor(accountId, characterId, now)`를 비동기로 부른다. 실패해도 원래 응답에 영향이 없고 로그만 남긴다):
   - 프레즌스 진입(P1에서 새 `online_sessions` 행을 만든 트랜잭션이 커밋된 뒤): "접속"의 정의가 프레즌스 진입이다(9단계 4.1).
   - 우편 요약(M4): 이미 접속 중인 사람이 승인 직후 받게 하고, 진입 때 실패한 배달을 다음 폴링(최대 60초 간격)에 다시 시도한다.
2. **캐시**: 서버는 진행 중인 캠페인(`status='active' AND starts_at <= now < ends_at AND issued_count < cap_count`)을 메모리에 들고 `CAMPAIGN_CACHE_SECONDS`(30)마다 다시 읽는다. 후보가 없으면 DB를 전혀 건드리지 않는다(평소 비용 0).
3. **대상 판정**(7.3절 조건)을 후보마다 메모리에서 한다.
4. **한 통 만들기**(대상별 한 트랜잭션, 캐릭터 행은 잠그지 않는다: 잔액을 건드리지 않는다):
   1. 이미 받았는지 `SELECT 1 FROM mail_campaign_deliveries WHERE campaign_id = $1 AND delivery_key = $2`(싸게 거르기).
   2. `INSERT INTO mails (kind='system', system_code=category, title, body, campaign_id, attach_n, expires_at = now + mail_days일, character_id = 지금 접속한 캐릭터)`.
   3. `INSERT INTO mail_attachments` (캠페인 첨부 복사. 귀속은 캠페인 생성 때 정해진 값).
   4. 아이템 첨부마다 `item_ledger(mail, +n, 'admin_grant', ref=우편 uuid)`.
   5. `INSERT INTO mail_campaign_deliveries (campaign_id, delivery_key, account_id, character_id, mail_id)`. `UNIQUE(campaign_id, delivery_key)` 위반(동시 접속으로 두 요청이 겹침)이면 전체 롤백하고 정상 종료한다.
   6. **마지막 한 문장**: `UPDATE mail_campaigns SET issued_count = issued_count + 1 WHERE id = $1 AND status = 'active' AND now() >= starts_at AND now() < ends_at AND issued_count < cap_count RETURNING issued_count`. 0행이면 전체 롤백(상한 도달, 취소, 기간 종료)하고 캐시를 즉시 새로 읽도록 표시한다.
   7. 커밋 뒤 `announceMail(title)`.
5. **계정당 1통**: `delivery_key`는 `delivery_unit='account'`이면 계정 id, `'character'`이면 캐릭터 id. 받는 캐릭터는 그 계정에서 **처음 이 함수를 부른 캐릭터**(= 처음 접속·폴링한 캐릭터)다. 부캐를 만들어 다시 받는 길이 없다. 삭제한 캐릭터가 받은 우편은 삭제 거절 규칙 때문에 수령 전에는 삭제할 수 없다.
6. 총 지급 상한은 6번 문장이 DB에서 강제한다(`issued_count <= cap_count` CHECK도 있다). 인기 캠페인의 마지막 문장은 한 행을 짧게 잠그는 직렬점이다(수령이 아니라 배달 한 번당 한 문장이라 허용).

### 7.3 발송 대상 조건 (`target`, 만들 때 검증)

`target`은 `{ "all": true }`이거나 아래 조건의 조합이다(둘 다 비어 있으면 거절: 실수로 전체 발송이 되지 않게 `all`을 명시해야 한다). 모든 조건은 AND.

| 키 | 뜻 | 단위 제한 |
|---|---|---|
| `min_account_level`, `max_account_level` | 계정 최고 레벨(삭제한 캐릭터 포함) | account 단위: 가능. character 단위: 그 캐릭터 레벨로 읽는 `min_level`, `max_level` 사용 |
| `classes` (`['warrior','mage']`) | 직업 | **character 단위만**(account 단위에서는 받을 캐릭터가 정해지기 전이라 의미가 모호) |
| `account_created_from`, `account_created_to` | 가입일(ISO) | 공통 |
| `last_login_before` | 마지막 로그인이 이 시각 이전(휴면 복귀 보상) | 공통 |
| `account_ids` (uuid 배열, 최대 `CAMPAIGN_MAX_TARGET_IDS` 2,000) | 특정 계정 목록 | 공통. 없는 id가 있으면 `CAMPAIGN_TARGET_INVALID` |

"기간 내 접속자"는 배달이 접속 때 일어나므로 `starts_at ~ ends_at` 사이에 접속하는 사람이 곧 대상이라 따로 조건이 필요 없다.

### 7.4 관리자 API

관리자 listener(7단계)의 규칙을 그대로 쓴다: 응답 `{ success, message, data }`, 변경 요청은 `runAdminAction`(같은 트랜잭션에 감사 행, `(admin_id, request_id)` 멱등성), 역할은 `requireAdmin`, 속도 제한은 `adminRate('mutate' | 'read')`. 대상 종류 `campaign`(0021이 `admin_audit_log.target_type`에 추가), 감사 액션: `campaign.create`, `campaign.approve`, `campaign.cancel`, `campaign.view`, `campaign.list`, `campaign.deliveries`.

**MC1 `POST /admin/mail-campaigns`** (operator 이상)

```
z.strictObject({
  request_id: z.uuid(),
  title: z.string().trim().min(1).max(40),
  body: z.string().max(1000),                         // 줄바꿈 허용, 그 밖의 제어문자는 거절
  category: z.enum(['maintenance', 'apology', 'event', 'attendance', 'other']),
  delivery_unit: z.enum(['account', 'character']).default('account'),
  target: targetSchema,                               // 7.3
  mail_days: z.number().int().min(1).max(30),         // 우편 수령 기한(배달 시각부터)
  starts_at: z.iso.datetime({ offset: true }),
  ends_at: z.iso.datetime({ offset: true }),          // 배달 기간(이 뒤에는 더 만들지 않는다)
  cap_count: z.number().int().min(1),                 // 총 지급 통수 상한(필수)
  attachments: z.array(z.discriminatedUnion('kind', [
    z.strictObject({ kind: z.literal('gold'), amount: z.number().int().min(1) }),
    z.strictObject({ kind: z.literal('item'), item_key: z.string().regex(ITEM_KEY_RE), count: z.number().int().min(1),
                     bind: z.enum(['none', 'account', 'character']).optional() }),   // 키의 하한보다 약하게는 못 한다
    z.strictObject({ kind: z.literal('sweep_ticket'), count: z.number().int().min(1) }),  // 이벤트 클리어권만
  ])).min(1).max(5),
  memo: z.string().min(1).max(200),                   // 지급 사유(문의 번호, 사고 설명 등), 필수
})
```

서버 검증(실패는 `422 CAMPAIGN_LIMIT` + `errors:{ field }`, 아이템은 `ITEM_NOT_FOUND`): `ends_at > starts_at`, 기간 <= `CAMPAIGN_MAX_WINDOW_DAYS`(60), `ends_at`이 지금보다 뒤, `cap_count <= CAMPAIGN_MAX_CAP_COUNT`, 골드 첨부 <= `CAMPAIGN_MAX_GOLD_PER_MAIL`이고 `amount x cap_count <= CAMPAIGN_MAX_GOLD_TOTAL`(총 지급 골드 상한), 아이템 개수 <= `ADMIN_GRANT_MAX_ITEM_COUNT`(기존 값 재사용), 클리어권 개수 <= `CAMPAIGN_MAX_TICKETS_PER_MAIL`, 같은 `item_key` 중복 불가, 골드·클리어권 첨부는 각각 한 개까지, 아이템은 `items.json`에 있고 `currency`가 아니고 클리어권 키가 아니다(`item`으로 클리어권 키를 보내면 거절, 클리어권은 `sweep_ticket` 종류만), `account_ids`는 모두 존재. 아이템 귀속은 `strongerBind(요청 bind, 키의 하한)`이고 장비는 최소 `character`(기존 운영 지급 규칙 `economyAdminService.createGrant`와 같다).

응답 `201` `data`: `{ campaign: { id, title, category, status: 'pending', delivery_unit, target, mail_days, starts_at, ends_at, cap_count, issued_count: 0, attachments:[...], created_by: <표시 이름>, memo, needs_approval_by: 'owner other than author' } }`. 에러: `400`, `401`, `403`(역할), `422 CAMPAIGN_LIMIT | ITEM_NOT_FOUND | CAMPAIGN_TARGET_INVALID | IDEMPOTENCY_MISMATCH`, `429`. 멱등성: `(admin_id, request_id)`.

**MC2 `GET /admin/mail-campaigns`** (viewer): 쿼리 `status?`, `limit`(1~100, 기본 50), `before?`(id 커서). 응답 `data: { items: [{ id, title, category, status, delivery_unit, starts_at, ends_at, cap_count, issued_count, created_by, approved_by }], next_before }`. 조회 기록(`campaign.list`)을 남긴다.

**MC3 `GET /admin/mail-campaigns/{uuid}`** (viewer): 상세와 현황.

```json
{ "campaign": { "...MC1 응답과 같은 필드, approved_by, approved_at, cancelled_by, cancel_reason ..." },
  "stats": { "issued": 12043, "claimed": 9120, "unclaimed_open": 2511, "expired": 412, "revoked": 0,
             "gold_promised": 60215000, "gold_claimed": 45600000, "tickets_promised": 12043, "tickets_claimed": 9120 },
  "revoke": { "requested": false, "remaining": 0 } }
```

`stats`는 `mails WHERE campaign_id = $1`의 상태별 집계다(`mails_campaign_once`, `mails_campaign_open` 인덱스). 관리자 전용이라 짧게(10초) 캐시해도 된다.

**MC4 `POST /admin/mail-campaigns/{uuid}/approve`** (owner, 작성자 본인 불가): 요청 `{ request_id }`. 처리: 캠페인 행 `FOR UPDATE` -> `status='pending'`이고 `created_by <> 내 id`, `ends_at > now` 확인 -> `status='active'`, `approved_by`, `approved_at`(DB의 `approved_by <> created_by` CHECK가 이중 장치). 응답 `200` `data: { campaign }`. 에러: `403 CAMPAIGN_SELF_APPROVAL | 역할`, `404`, `409 CAMPAIGN_STATE`, `422 CAMPAIGN_WINDOW_PASSED`. 승인된 순간부터 `starts_at`이 지났으면 곧 배달된다(캐시 갱신 최대 30초, 승인 시 `pg_notify('dotrpg_campaign')`로 즉시 갱신하는 것은 선택).

**MC5 `POST /admin/mail-campaigns/{uuid}/cancel`**: 요청 `{ request_id, reason: string(1..200), revoke_unclaimed: boolean }`. 권한: `pending`은 작성자 또는 owner, `active`와 `ended`는 owner. 처리: 캠페인 행 `FOR UPDATE` -> `pending|active`는 `status='cancelled'`로(더 만들지 않는다), `ended`는 `revoke_unclaimed=true`일 때만 허용(아니면 `409 CAMPAIGN_STATE`) -> `revoke_unclaimed`이면 `revoke_requested=true`. 미수령 회수는 작업 `campaign_revoke`가 한다(7.5). 응답 `200` `data: { campaign, revoke: { requested, remaining } }`. 에러: `403`, `404`, `409 CAMPAIGN_STATE`, `422`.

**MC6 `GET /admin/mail-campaigns/{uuid}/deliveries`** (viewer): 쿼리 `state?`(`claimed|open|expired|revoked`), `limit`, `before?`. 응답 `items: [{ account_id, character_id, character_name, delivered_at, state, claimed_at }]`(`mail_campaign_deliveries` + `mails`). 개인정보 성격이라 조회 기록(`campaign.deliveries`)을 남긴다.

### 7.5 취소와 미수령 회수 (`campaign_revoke`)

- 취소 시점에 이미 배달된 우편 중 **받지 않은 것**만 회수한다. 받은 것은 이 경로로 되돌리지 않는다(필요하면 9단계 H4 회수 도구).
- 작업(30초마다): `revoke_requested AND revoke_done_at IS NULL`인 캠페인마다 한 번에 `CAMPAIGN_REVOKE_BATCH`(2,000)통씩 `UPDATE mails SET expires_at = now() WHERE id IN (SELECT id FROM mails WHERE campaign_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > now() ORDER BY id LIMIT 2000)`. 이후 우편은 기존 폐기 틱(6.4)이 `mail_expire` 원장·소각 기록으로 닫는다. 수령과 겹치면 수령 쪽이 우편 행을 잠그고 `expires_at <= now` 판정으로 `410 MAIL_EXPIRED`가 되거나 먼저 받아 가므로 둘 중 하나만 성립한다. 더 처리할 우편이 없으면 `revoke_done_at`과 `revoked_count`를 기록한다. 회수 처리는 캠페인 행을 우편 행과 함께 잠그지 않는다(락 순서 ⑤).

### 7.6 지급 상한과 감사 기록

| 장치 | 내용 |
|---|---|
| 캠페인 총 통수 | `cap_count`(필수) + 배달의 조건부 UPDATE + `CHECK (issued_count <= cap_count)` |
| 총 지급 골드 | 만들 때 `골드 x cap_count <= CAMPAIGN_MAX_GOLD_TOTAL`(서버 환경변수, 운영 필수). 통수가 DB에서 막히므로 이 합계도 지켜진다 |
| 1통당 | 골드 <= `CAMPAIGN_MAX_GOLD_PER_MAIL`, 아이템 개수 <= `ADMIN_GRANT_MAX_ITEM_COUNT`, 클리어권 <= `CAMPAIGN_MAX_TICKETS_PER_MAIL`, 수령 기한 <= 30일 |
| 2인 확인 | 승인자 owner, 작성자와 다른 사람(API + DB CHECK) |
| 감사 | 관리자 행동은 `admin_audit_log`(작성, 승인, 취소, 조회). 한 통 한 통은 `mail_campaign_deliveries`(누구에게 어느 캐릭터로 언제), 아이템 지급은 `item_ledger(admin_grant, ref=우편 uuid)`, 수령은 `gold_ledger/item_ledger(mail_claim)`·`sweep_ticket_ledger(campaign_claim)`. 같은 `memo`가 캠페인에 남는다 |
| 불변 | 캠페인 내용 열과 첨부는 만든 뒤 못 바꾼다(트리거). 삭제도 못 한다 |
| 기본 비활성 | `CAMPAIGN_DELIVERY_ENABLED=false`면 승인돼도 배달하지 않는다 |

## 8. 부정 방지 연결, 퀘스트·업적

### 8.1 경제 정지, 계정 귀속, 프레즌스

| 경로 | `assertNoHold` | `assertActionPresence` | 근거 |
|---|---|---|---|
| S2, S3 소탕 | 예 | 예(`'sweep'`, 신선도만) | PLAN 5절. 소탕은 입력이 없는 경험치 경로 |
| T1 구매 | 예 | 아니오 | 정지된 캐릭터의 골드를 계정 지갑으로 옮겨 다른 캐릭터가 쓰는 길을 막는다 |
| T2 주간 수령 | 예 | 아니오 | 정지 대상(봇 의심) 캐릭터의 클리어 성과가 계정 지갑으로 흘러가는 것을 막는다(D12) |
| M2, M3 우편 수령 | 예(기존) | 아니오 | 클리어권 첨부도 포함 |
| S1 현황 | 아니오(읽기) | 아니오 | `hold` 불리언만 알린다 |

`assertNoHold`는 항상 캐릭터 행을 잠근 뒤, 계정 행을 잠그기 전에 부른다. `ECONOMY_HOLD` 응답 본문과 "수치를 싣지 않는다"는 9단계 1절 그대로다. 정지 대상 경로는 7개에서 10개가 된다(E6). `ActionKind`에 `'sweep'`만 더한다(E4): 처치와 같은 `PRESENCE_KILL_MODE` 스위치를 따르고 `log` 모드는 기록만 한다.

**계정 귀속**: 클리어권은 지갑에만 있고 가방에 못 들어간다(5.2). 운영 우편 첨부 아이템은 캠페인 생성 때 정해진 귀속(`strongerBind`)으로 가방에 들어가며 장비는 최소 `character` 귀속이라 경매에 못 나온다. 일반 재료·소모품(귀속 `none`)은 원래 거래 가능하다(기존 운영 지급과 같다).

**부캐 곱하기 방지**: 주 7장 구매와 주간 활동 수령은 계정 단위(`account_week_counters`), 캠페인 우편은 계정당 1통(`delivery_key`), 하루 입장 3회는 캐릭터 단위이므로 지갑 총량(7 + 3 + 이벤트)이 부캐 수만큼 늘지 않는다.

### 8.2 퀘스트 "던전 클리어 N회", 난이도 해금

| 대상 | 소탕 처리 | 구현 |
|---|---|---|
| 퀘스트 "던전 클리어 N회"(`questService`의 `dungeonNeeds`) | **인정** | `clearCounts`가 `dungeon_runs.state='cleared'` 수에 `dungeon_sweeps`의 던전별 수를 더해 돌려준다(`total`과 `byDungeon` 모두). 레이드 목표(`raidNeeds`)는 소탕이 레이드가 아니므로 영향 없음 |
| 난이도 해금(`checkEntry`의 `clearSummary`), 던전 목록 `unlocked/cleared/best_rank` | **불인정** | 소탕은 `dungeon_runs`가 아니므로 `clearSummary`가 읽지 못한다(코드 변경 없음) |
| 소탕 자격(4.1의 6번) | 직접 클리어만 | `dungeon_runs` 기준, 보상 잠긴 판 제외 |
| 주간 활동 카운터 | 불인정 | `finalizeCleared`에서만 올린다 |
| 업적 `dungeon_clears`, `party_clears`, `raid_clears` | 불인정(D8) | `achievementRepository`는 `dungeon_runs`만 읽는다(변경 없음) |
| 관리자 보류 해제, 판 기록 | 해당 없음 | 소탕은 보류가 없다 |

클라이언트의 퀘스트 진행 카운터(`QuestJournal`)는 소탕 결과를 받으면 던전 클리어 이벤트를 한 번 올리되(12절) 난이도 해금·랭크 기록은 건드리지 않는다. 서버 청구 검증은 위 `clearCounts` 합계로 한다.

### 8.3 소득 감시 (E5)

`incomeMeter.XP_REASONS`에 `dungeon_sweep`을 더한다(소탕 경험치가 `income_hourly.xp`에 쌓여 경제 속도 정지 평가에 들어간다). 소탕 카드는 직접 플레이와 같은 사유 `dungeon_card`라서 `GOLD_REASONS`, `ITEM_REASONS`에 이미 들어 있다. 3.4절대로 `income_caps.json`은 바꾸지 않는다.

### 8.4 이상 기록

`anomaly_log.kind`에 `sweep_denied`를 더한다(4.1의 6, 7번과 `SWEEP_NOT_ALLOWED`: 화면이 막은 조건을 우회한 요청, 심각도 1). 한 계정이 짧은 시간에 반복하면 기존 감시 목록에서 보인다(차단 근거로 쓰지 않는다).

## 9. 마이그레이션 0021_sweep_and_mail.sql 초안

대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0020과 같다. 선행: 0020_anti_abuse. 값(한도, 가격)은 SQL에 넣지 않는다.

```sql
-- 0021_sweep_and_mail: 던전 클리어권(소탕)과 운영 우편 캠페인 (Docs/server/phase10_sweep_mail.md)
-- 대상: PostgreSQL 14 이상. 선행: 0020_anti_abuse.
--
-- 이 마이그레이션이 하는 일
--   1. 클리어권 지갑(sweep_ticket_lots)과 추가 전용 원장(sweep_ticket_ledger).
--   2. 소탕 기록(dungeon_sweeps). dungeon_runs와 섞지 않는다(해금·업적이 새지 않게).
--   3. 계정 주간 카운터(account_week_counters): 클리어권 주 구매 수, 주간 직접 클리어 수, 주간 보상 수령.
--   4. 운영 우편 캠페인(mail_campaigns, mail_campaign_attachments, mail_campaign_deliveries).
--   5. 우편 확장(mails 제목·본문·캠페인·첨부 수, mail_attachments). 기존 우편 열은 그대로.
--   6. 원장 사유, 이상 기록 종류, 관리자 감사 대상 확장. 계정 최고 레벨 조회용 인덱스.

-- ============ UP ============

-- ---------- 1. 클리어권 지갑 ----------

CREATE TABLE sweep_ticket_lots (
  id         BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  kind       TEXT NOT NULL CHECK (kind IN ('normal', 'event')),
  granted    INT NOT NULL CHECK (granted > 0),
  remaining  INT NOT NULL CHECK (remaining >= 0),
  expires_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT sweep_lots_expiry_chk    CHECK ((kind = 'event') = (expires_at IS NOT NULL)),
  CONSTRAINT sweep_lots_remaining_chk CHECK (remaining <= granted)
);
COMMENT ON TABLE  sweep_ticket_lots IS '던전 클리어권 지갑(계정 소유). normal은 계정당 1행(누적), event는 지급(우편 수령)마다 1행. 가방·창고·경매에 들어가지 않는다. 외부에 id를 노출하지 않는다(잔량과 만료 시각만 응답에 나간다)';
COMMENT ON COLUMN sweep_ticket_lots.granted    IS 'normal은 누적 지급 수, event는 그 로트의 지급 수. remaining <= granted';
COMMENT ON COLUMN sweep_ticket_lots.remaining  IS '남은 장수. 소모는 UPDATE ... WHERE remaining >= 1로만. 변경은 sweep_ticket_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN sweep_ticket_lots.expires_at IS 'event만 값이 있다(수령 시각 + 서버 데이터 eventTicketDays일). normal은 NULL(기한 없음). 지난 로트는 조회·소모에서 제외하고 작업이 remaining을 0으로 만든다';
-- 계정당 일반 로트는 하나 (추가는 ON CONFLICT DO UPDATE)
CREATE UNIQUE INDEX sweep_lots_normal_uq ON sweep_ticket_lots (account_id) WHERE kind = 'normal';
-- 소탕 때 쓸 로트 조회: 계정의 남은 로트를 만료 가까운 순(일반은 NULLS LAST)으로
CREATE INDEX sweep_lots_live ON sweep_ticket_lots (account_id, expires_at) WHERE remaining > 0;
-- 만료 작업: 기한이 지났고 남은 event 로트
CREATE INDEX sweep_lots_expiring ON sweep_ticket_lots (expires_at) WHERE kind = 'event' AND remaining > 0;

CREATE TABLE sweep_ticket_ledger (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  lot_id        BIGINT NOT NULL REFERENCES sweep_ticket_lots(id),
  character_id  BIGINT REFERENCES characters(id),
  delta         INT NOT NULL CHECK (delta <> 0),
  balance_after INT NOT NULL CHECK (balance_after >= 0),
  reason        TEXT NOT NULL CHECK (reason IN ('shop_buy', 'weekly_activity', 'campaign_claim', 'sweep_use', 'expire')),
  ref           TEXT,
  request_id    UUID,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT sweep_ledger_sign_chk CHECK ((reason IN ('sweep_use', 'expire')) = (delta < 0))
);
COMMENT ON TABLE  sweep_ticket_ledger IS '클리어권 변동 원장(추가만, 트리거가 UPDATE/DELETE 차단). 로트별 SUM(delta) = sweep_ticket_lots.remaining(만료 작업 지연 구간 제외)';
COMMENT ON COLUMN sweep_ticket_ledger.character_id IS '요청한 캐릭터. 만료 작업은 NULL';
COMMENT ON COLUMN sweep_ticket_ledger.balance_after IS '변동 직후 그 계정의 쓸 수 있는 클리어권 합계';
COMMENT ON COLUMN sweep_ticket_ledger.ref IS 'shop_buy: 요청 request_id / weekly_activity: weekly:{계정 uuid}:{주 시작 ISO} / campaign_claim: 우편 uuid / sweep_use: 소탕 uuid / expire: 로트 id';
-- 계정별 이력 조회, 한 요청이 만든 행 찾기
CREATE INDEX sweep_ledger_account ON sweep_ticket_ledger (account_id, id);
CREATE INDEX sweep_ledger_request ON sweep_ticket_ledger (request_id) WHERE request_id IS NOT NULL;
-- 같은 주 활동 보상·같은 우편의 이중 지급을 DB가 막는다
CREATE UNIQUE INDEX sweep_ledger_grant_uq ON sweep_ticket_ledger (reason, ref) WHERE reason IN ('weekly_activity', 'campaign_claim');
CREATE TRIGGER sweep_ticket_ledger_append_only BEFORE UPDATE OR DELETE ON sweep_ticket_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER sweep_ticket_ledger_no_truncate BEFORE TRUNCATE ON sweep_ticket_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 2. 소탕 기록 ----------

CREATE TABLE dungeon_sweeps (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id   TEXT NOT NULL,
  difficulty   SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  reset_day    TIMESTAMPTZ NOT NULL,
  lot_id       BIGINT NOT NULL REFERENCES sweep_ticket_lots(id),
  xp_granted   INT NOT NULL CHECK (xp_granted >= 0),
  card         JSONB NOT NULL CHECK (jsonb_typeof(card) = 'object'),
  request_id   UUID NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  dungeon_sweeps IS '던전 소탕 한 번(추가만). dungeon_runs와 섞지 않는다: 난이도 해금·업적·최고 랭크는 이 표를 읽지 않는다. 하루 입장 횟수와 퀘스트 클리어 횟수만 이 표를 더해 읽는다';
COMMENT ON COLUMN dungeon_sweeps.reset_day IS '소탕 시각이 속한 일일 초기화 구간의 시작(resetBoundaries().dailyStartAt). 오늘 입장 수 집계 키';
COMMENT ON COLUMN dungeon_sweeps.lot_id    IS '쓴 클리어권 로트(이벤트인지 일반인지)';
COMMENT ON COLUMN dungeon_sweeps.xp_granted IS '실제로 들어간 경험치(만렙이면 0)';
COMMENT ON COLUMN dungeon_sweeps.card      IS '지급한 카드 {item_key, count}';
COMMENT ON COLUMN dungeon_sweeps.request_id IS '요청의 request_id. 모두 소탕 한 번이 만든 행들이 같은 값';
-- 오늘 입장 횟수(countEntries), 소탕은 캐릭터 단위
CREATE INDEX dungeon_sweeps_char_day ON dungeon_sweeps (character_id, reset_day);
-- 퀘스트 "던전 클리어 N회"의 던전별 합계
CREATE INDEX dungeon_sweeps_char_dungeon ON dungeon_sweeps (character_id, dungeon_id);
CREATE TRIGGER dungeon_sweeps_append_only BEFORE UPDATE OR DELETE ON dungeon_sweeps
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER dungeon_sweeps_no_truncate BEFORE TRUNCATE ON dungeon_sweeps
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 3. 계정 주간 카운터 ----------

CREATE TABLE account_week_counters (
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  week_start TIMESTAMPTZ NOT NULL,
  kind       TEXT NOT NULL CHECK (kind IN ('sweep_buy', 'direct_clear', 'activity_claim')),
  used       INT NOT NULL DEFAULT 0 CHECK (used >= 0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, week_start, kind),
  CONSTRAINT week_counters_claim_chk CHECK (kind <> 'activity_claim' OR used = 1)
);
COMMENT ON TABLE  account_week_counters IS '계정 주간 카운터. week_start는 resetBoundaries().weeklyStartAt(목요일 06:00 KST). 주가 바뀌면 새 행이 생겨 초기화 작업이 없다';
COMMENT ON COLUMN account_week_counters.kind IS 'sweep_buy 이번 주 클리어권 구매 장수 / direct_clear 이번 주 보상이 잠기지 않은 요일 던전 직접 클리어 수(계정 합산) / activity_claim 주간 보상 수령(행이 있으면 받음, used=1)';
-- 지난 주 행 정리(purge)
CREATE INDEX account_week_counters_week ON account_week_counters (week_start);

-- ---------- 4. 운영 우편 캠페인 ----------

CREATE TABLE mail_campaigns (
  id               BIGSERIAL PRIMARY KEY,
  uuid             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  title            TEXT NOT NULL CHECK (char_length(title) BETWEEN 1 AND 40),
  body             TEXT NOT NULL DEFAULT '' CHECK (char_length(body) <= 1000),
  category         TEXT NOT NULL CHECK (category IN ('maintenance', 'apology', 'event', 'attendance', 'other')),
  delivery_unit    TEXT NOT NULL DEFAULT 'account' CHECK (delivery_unit IN ('account', 'character')),
  target           JSONB NOT NULL CHECK (jsonb_typeof(target) = 'object'),
  mail_days        SMALLINT NOT NULL CHECK (mail_days BETWEEN 1 AND 30),
  starts_at        TIMESTAMPTZ NOT NULL,
  ends_at          TIMESTAMPTZ NOT NULL,
  cap_count        INT NOT NULL CHECK (cap_count > 0),
  issued_count     INT NOT NULL DEFAULT 0 CHECK (issued_count >= 0),
  status           TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'active', 'ended', 'cancelled')),
  memo             TEXT NOT NULL CHECK (char_length(memo) BETWEEN 1 AND 200),
  created_by       BIGINT NOT NULL REFERENCES admin_users(id),
  approved_by      BIGINT REFERENCES admin_users(id),
  approved_at      TIMESTAMPTZ,
  ended_at         TIMESTAMPTZ,
  cancelled_by     BIGINT REFERENCES admin_users(id),
  cancelled_at     TIMESTAMPTZ,
  cancel_reason    TEXT CHECK (char_length(cancel_reason) <= 200),
  revoke_requested BOOLEAN NOT NULL DEFAULT false,
  revoke_done_at   TIMESTAMPTZ,
  revoked_count    INT NOT NULL DEFAULT 0 CHECK (revoked_count >= 0),
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT mail_campaigns_window_chk     CHECK (ends_at > starts_at),
  CONSTRAINT mail_campaigns_cap_chk        CHECK (issued_count <= cap_count),
  CONSTRAINT mail_campaigns_two_person_chk CHECK (approved_by IS NULL OR approved_by <> created_by),
  CONSTRAINT mail_campaigns_state_chk CHECK (
       (status = 'pending'            AND approved_by IS NULL AND cancelled_at IS NULL)
    OR (status IN ('active', 'ended') AND approved_by IS NOT NULL AND approved_at IS NOT NULL AND cancelled_at IS NULL)
    OR (status = 'cancelled'          AND cancelled_at IS NOT NULL))
);
COMMENT ON TABLE  mail_campaigns IS '운영 우편 캠페인(공지 우편 한 종류). 대상이 접속할 때 우편을 만든다(전체 발송 때 한 번에 넣지 않는다). 내용 열은 만든 뒤 못 바꾸고(트리거) 삭제하지 않는다(상태로 닫는다)';
COMMENT ON COLUMN mail_campaigns.category      IS '점검 보상 maintenance / 사과 보상 apology / 이벤트 event / 출석 attendance / 기타 other. 우편의 system_code로 그대로 간다';
COMMENT ON COLUMN mail_campaigns.delivery_unit IS 'account 계정당 1통(받을 캐릭터는 그 계정에서 처음 접속·폴링한 캐릭터) / character 캐릭터당 1통(드물게)';
COMMENT ON COLUMN mail_campaigns.target        IS '{"all":true} 또는 조건 조합(min_account_level, max_account_level, classes, account_created_from/to, last_login_before, account_ids). 서버가 만들 때 검증한다';
COMMENT ON COLUMN mail_campaigns.mail_days     IS '우편 수령 기한(배달 시각부터 일수, 1~30)';
COMMENT ON COLUMN mail_campaigns.starts_at     IS '배달 기간. 이 구간 밖에서는 우편을 만들지 않는다';
COMMENT ON COLUMN mail_campaigns.cap_count     IS '총 지급 통수 상한(필수). 배달의 조건부 UPDATE가 강제한다';
COMMENT ON COLUMN mail_campaigns.issued_count  IS '지금까지 만든 우편 수. 배달 트랜잭션의 마지막 문장에서만 +1';
COMMENT ON COLUMN mail_campaigns.created_by    IS '작성 관리자. 승인자와 달라야 한다(2인 확인)';
COMMENT ON COLUMN mail_campaigns.cancelled_by  IS 'NULL이면 시스템 취소(승인 전 기간 만료 등)';
COMMENT ON COLUMN mail_campaigns.revoke_requested IS '취소 때 미수령 회수를 요청했는가. 작업 campaign_revoke가 처리하고 revoke_done_at을 기록한다';
-- 캐시 갱신·상태 작업: 진행 중·대기 중 캠페인만
CREATE INDEX mail_campaigns_open ON mail_campaigns (status, ends_at) WHERE status IN ('pending', 'active');
-- 목록(최신순 커서)
CREATE INDEX mail_campaigns_created ON mail_campaigns (created_at DESC);
-- 회수 작업 대상
CREATE INDEX mail_campaigns_revoke ON mail_campaigns (id) WHERE revoke_requested AND revoke_done_at IS NULL;

CREATE FUNCTION mail_campaigns_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'mail_campaigns is never deleted (close it by status)';
  END IF;
  IF NEW.title IS DISTINCT FROM OLD.title OR NEW.body IS DISTINCT FROM OLD.body
     OR NEW.category IS DISTINCT FROM OLD.category OR NEW.delivery_unit IS DISTINCT FROM OLD.delivery_unit
     OR NEW.target IS DISTINCT FROM OLD.target OR NEW.mail_days IS DISTINCT FROM OLD.mail_days
     OR NEW.starts_at IS DISTINCT FROM OLD.starts_at OR NEW.ends_at IS DISTINCT FROM OLD.ends_at
     OR NEW.cap_count IS DISTINCT FROM OLD.cap_count OR NEW.created_by IS DISTINCT FROM OLD.created_by
     OR NEW.memo IS DISTINCT FROM OLD.memo OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
    RAISE EXCEPTION 'mail_campaigns content is immutable after creation';
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER mail_campaigns_guard_upd BEFORE UPDATE ON mail_campaigns
  FOR EACH ROW EXECUTE FUNCTION mail_campaigns_guard();
CREATE TRIGGER mail_campaigns_guard_del BEFORE DELETE ON mail_campaigns
  FOR EACH ROW EXECUTE FUNCTION mail_campaigns_guard();

CREATE TABLE mail_campaign_attachments (
  id          BIGSERIAL PRIMARY KEY,
  campaign_id BIGINT NOT NULL REFERENCES mail_campaigns(id),
  slot        SMALLINT NOT NULL CHECK (slot BETWEEN 1 AND 5),
  kind        TEXT NOT NULL CHECK (kind IN ('gold', 'item', 'sweep_ticket')),
  item_key    TEXT,
  amount      BIGINT NOT NULL CHECK (amount > 0),
  bind        TEXT CHECK (bind IN ('none', 'account', 'character')),
  UNIQUE (campaign_id, slot),
  CONSTRAINT mail_campaign_att_shape_chk CHECK (
       (kind = 'gold'         AND item_key IS NULL     AND bind IS NULL)
    OR (kind = 'item'         AND item_key IS NOT NULL AND item_key <> 'gold' AND bind IS NOT NULL AND amount <= 2147483647)
    OR (kind = 'sweep_ticket' AND item_key IS NOT NULL AND bind IS NULL AND amount <= 1000))
);
COMMENT ON TABLE  mail_campaign_attachments IS '캠페인 첨부(최대 5, 추가만). 배달 때 mail_attachments로 복사된다';
COMMENT ON COLUMN mail_campaign_attachments.amount   IS 'gold는 골드, item·sweep_ticket은 개수';
COMMENT ON COLUMN mail_campaign_attachments.item_key IS 'sweep_ticket은 표시용 이벤트 클리어권 키(서버 데이터 sweep.json이 정한다). 값은 서버가 검증한다';
-- 골드·클리어권 첨부는 캠페인당 하나
CREATE UNIQUE INDEX mail_campaign_att_gold_uq   ON mail_campaign_attachments (campaign_id) WHERE kind = 'gold';
CREATE UNIQUE INDEX mail_campaign_att_ticket_uq ON mail_campaign_attachments (campaign_id) WHERE kind = 'sweep_ticket';
CREATE TRIGGER mail_campaign_attachments_append_only BEFORE UPDATE OR DELETE ON mail_campaign_attachments
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 5. 우편 확장 (기존 열은 그대로) ----------

ALTER TABLE mails
  ADD COLUMN title       TEXT CHECK (char_length(title) BETWEEN 1 AND 40),
  ADD COLUMN body        TEXT CHECK (char_length(body) <= 1000),
  ADD COLUMN campaign_id BIGINT REFERENCES mail_campaigns(id),
  ADD COLUMN attach_n    SMALLINT NOT NULL DEFAULT 0 CHECK (attach_n BETWEEN 0 AND 5);
COMMENT ON COLUMN mails.title       IS '제목. 옛 우편은 NULL(클라이언트가 종류·system_code로 문구를 조립). kind=system일 때만';
COMMENT ON COLUMN mails.body        IS '본문(줄바꿈 허용). 옛 우편은 NULL';
COMMENT ON COLUMN mails.campaign_id IS '캠페인이 배달한 우편이면 그 캠페인';
COMMENT ON COLUMN mails.attach_n    IS 'mail_attachments의 첨부 수. 0이 아니면 첨부는 그 표에 있고 이 행의 item_key는 NULL, gold는 0이다';

-- 첨부가 표에 있는 우편은 내용 검사를 통과시키고(content), 옛 열과 섞이지 않게 한다(mode)
ALTER TABLE mails DROP CONSTRAINT mails_content_chk;
ALTER TABLE mails ADD CONSTRAINT mails_content_chk CHECK (item_key IS NOT NULL OR gold > 0 OR attach_n > 0);
ALTER TABLE mails ADD CONSTRAINT mails_attach_mode_chk CHECK (attach_n = 0 OR (item_key IS NULL AND gold = 0));
ALTER TABLE mails ADD CONSTRAINT mails_title_chk CHECK ((title IS NULL AND body IS NULL) OR (kind = 'system' AND title IS NOT NULL));
ALTER TABLE mails ADD CONSTRAINT mails_campaign_chk CHECK (campaign_id IS NULL OR (kind = 'system' AND attach_n > 0));
ALTER TABLE mails ADD CONSTRAINT mails_attach_kind_chk CHECK (attach_n = 0 OR kind = 'system');

-- 캠페인 분류가 우편의 system_code (기존 4개 유지)
ALTER TABLE mails DROP CONSTRAINT mails_system_code_check;
ALTER TABLE mails ADD CONSTRAINT mails_system_code_check
  CHECK (system_code IN ('compensation', 'event', 'refund', 'notice', 'maintenance', 'apology', 'attendance', 'other'));
COMMENT ON COLUMN mails.system_code IS 'kind=system일 때만 값. compensation 보상 / event 이벤트 / refund 환불 / notice 안내 / maintenance 점검 보상 / apology 사과 보상 / attendance 출석 / other 기타. 캠페인 우편은 category가 그대로 온다';

-- 캠페인당 한 캐릭터에 한 통(배달 표와 이중 방어)
CREATE UNIQUE INDEX mails_campaign_once ON mails (campaign_id, character_id) WHERE campaign_id IS NOT NULL;
-- 취소 회수 작업: 캠페인의 미수령 우편을 id 순으로 배치 처리, 현황 집계
CREATE INDEX mails_campaign_open ON mails (campaign_id, id) WHERE campaign_id IS NOT NULL AND claimed_at IS NULL AND expired_at IS NULL;

CREATE TABLE mail_attachments (
  id       BIGSERIAL PRIMARY KEY,
  mail_id  BIGINT NOT NULL REFERENCES mails(id),
  slot     SMALLINT NOT NULL CHECK (slot BETWEEN 1 AND 5),
  kind     TEXT NOT NULL CHECK (kind IN ('gold', 'item', 'sweep_ticket')),
  item_key TEXT,
  amount   BIGINT NOT NULL CHECK (amount > 0),
  bind     TEXT CHECK (bind IN ('none', 'account', 'character')),
  UNIQUE (mail_id, slot),
  CONSTRAINT mail_att_shape_chk CHECK (
       (kind = 'gold'         AND item_key IS NULL     AND bind IS NULL)
    OR (kind = 'item'         AND item_key IS NOT NULL AND item_key <> 'gold' AND bind IS NOT NULL AND amount <= 2147483647)
    OR (kind = 'sweep_ticket' AND item_key IS NOT NULL AND bind IS NULL AND amount <= 1000))
);
COMMENT ON TABLE  mail_attachments IS '여러 첨부가 있는 우편의 첨부(추가만). 수령 여부는 mails.claimed_at이 정한다(부분 수령 없음). 첨부 아이템은 우편 위치(mail)의 item_ledger +n, 수령하면 mail -n / bag +n';
CREATE TRIGGER mail_attachments_append_only BEFORE UPDATE OR DELETE ON mail_attachments
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
-- 한 페이지 우편의 첨부를 mail_id IN (...)으로 읽는 것은 UNIQUE (mail_id, slot)이 맡는다

CREATE TABLE mail_campaign_deliveries (
  id           BIGSERIAL PRIMARY KEY,
  campaign_id  BIGINT NOT NULL REFERENCES mail_campaigns(id),
  delivery_key BIGINT NOT NULL,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  mail_id      BIGINT NOT NULL UNIQUE REFERENCES mails(id),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (campaign_id, delivery_key)
);
COMMENT ON TABLE  mail_campaign_deliveries IS '캠페인 배달 기록(추가만): 누구에게 어느 캐릭터로 언제. UNIQUE(campaign_id, delivery_key)가 계정당(또는 캐릭터당) 1통을 DB에서 강제한다';
COMMENT ON COLUMN mail_campaign_deliveries.delivery_key IS 'delivery_unit=account이면 계정 id, character이면 캐릭터 id';
-- 캠페인별 배달 목록(관리자 MC6, id 커서)
CREATE INDEX mail_campaign_deliveries_campaign ON mail_campaign_deliveries (campaign_id, id);
-- 한 계정이 받은 캠페인(계정 상세)
CREATE INDEX mail_campaign_deliveries_account ON mail_campaign_deliveries (account_id, created_at DESC);
CREATE TRIGGER mail_campaign_deliveries_append_only BEFORE UPDATE OR DELETE ON mail_campaign_deliveries
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 6. 보조 인덱스, 원장 사유, 이상 기록, 감사 대상 ----------

-- 클리어권 가격 계산: 계정의 최고 레벨(삭제한 캐릭터 포함). 기존 characters_account_alive는 살아 있는 캐릭터만 덮는다
CREATE INDEX characters_account_level ON characters (account_id, level DESC);

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy'));

ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check
  CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost', 'dungeon_sweep'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상 / dungeon_clear 던전 클리어 / test_boost 시험 서버 레벨 조정 / dungeon_sweep 던전 소탕(ref = 소탕 uuid)';

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution',
                  'sweep_denied'));
COMMENT ON COLUMN anomaly_log.kind IS '0020의 값 + sweep_denied 소탕 화면 조건을 우회한 요청(미클리어, 등급 미달, 레벨 부족, 소탕 불가 던전)';

ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold', 'campaign'));

-- ============ DOWN ============
-- 개발 DB 전용. 새 표·열을 지우고 CHECK를 0020 상태로 되돌린다(원장 행 삭제는 추가 전용 트리거를 잠시 끈다).
-- 캠페인 우편을 이미 수령해 올라간 골드·경험치 잔액은 되돌리지 않는다(개발 DB 정리용).

ALTER TABLE admin_audit_log DISABLE TRIGGER admin_audit_log_append_only;
DELETE FROM admin_audit_log WHERE target_type = 'campaign';
ALTER TABLE admin_audit_log ENABLE TRIGGER admin_audit_log_append_only;
ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold'));

DELETE FROM anomaly_log WHERE kind = 'sweep_denied';
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution'));
COMMENT ON COLUMN anomaly_log.kind IS '0020의 값';

ALTER TABLE xp_ledger DISABLE TRIGGER xp_ledger_append_only;
DELETE FROM xp_ledger WHERE reason = 'dungeon_sweep';
ALTER TABLE xp_ledger ENABLE TRIGGER xp_ledger_append_only;
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상 / dungeon_clear 던전 클리어 / test_boost 시험 서버 레벨 조정(scripts/test-level.mjs)';

ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason = 'sweep_ticket_buy';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback'));

DROP INDEX characters_account_level;

-- 캠페인 우편과 그 아이템 원장을 먼저 지운다(우편 -> 첨부·배달 -> 캠페인 순)
DROP TABLE mail_campaign_deliveries;
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE ref IN (SELECT uuid::text FROM mails WHERE campaign_id IS NOT NULL);
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
DROP TABLE mail_attachments;
DELETE FROM mails WHERE campaign_id IS NOT NULL;

DROP INDEX mails_campaign_open;
DROP INDEX mails_campaign_once;
ALTER TABLE mails DROP CONSTRAINT mails_system_code_check;
ALTER TABLE mails ADD CONSTRAINT mails_system_code_check CHECK (system_code IN ('compensation', 'event', 'refund', 'notice'));
COMMENT ON COLUMN mails.system_code IS 'kind=system일 때만 값이 있다. compensation 보상 / event 이벤트 / refund 환불 / notice 안내. 문구는 클라이언트가 이 코드로 조립한다(서버는 문장을 만들지 않는다)';
ALTER TABLE mails DROP CONSTRAINT mails_attach_kind_chk;
ALTER TABLE mails DROP CONSTRAINT mails_campaign_chk;
ALTER TABLE mails DROP CONSTRAINT mails_title_chk;
ALTER TABLE mails DROP CONSTRAINT mails_attach_mode_chk;
ALTER TABLE mails DROP CONSTRAINT mails_content_chk;
ALTER TABLE mails ADD CONSTRAINT mails_content_chk CHECK (item_key IS NOT NULL OR gold > 0);
ALTER TABLE mails DROP COLUMN attach_n, DROP COLUMN campaign_id, DROP COLUMN body, DROP COLUMN title;

DROP TABLE mail_campaign_attachments;
DROP TABLE mail_campaigns;
DROP FUNCTION mail_campaigns_guard();

DROP TABLE account_week_counters;

ALTER TABLE dungeon_sweeps DISABLE TRIGGER dungeon_sweeps_append_only;
DROP TABLE dungeon_sweeps;

ALTER TABLE sweep_ticket_ledger DISABLE TRIGGER sweep_ticket_ledger_append_only;
DROP TABLE sweep_ticket_ledger;
DROP TABLE sweep_ticket_lots;
```

**제약·인덱스 이유 요약**

| 대상 | 이유 |
|---|---|
| `sweep_lots_normal_uq` | 계정당 일반 로트 하나 -> 추가가 `ON CONFLICT DO UPDATE` 한 문장이고 행이 늘지 않는다 |
| `sweep_lots_live` | 소탕 때 "남은 로트를 만료 가까운 순"으로 읽는 유일한 조회(부분 인덱스라 소진된 로트는 안 든다) |
| `sweep_lots_expiring` | 만료 작업이 기한 지난 event 로트만 훑는다 |
| `sweep_ledger_grant_uq` | 주간 보상 이중 지급, 같은 우편의 이중 수령을 DB가 막는다(`gold_ledger_drop_uq`와 같은 방식) |
| `dungeon_sweeps_char_day` | `countEntries`(소탕 + 직접 입장)가 매 입장·소탕마다 읽는다 |
| `dungeon_sweeps_char_dungeon` | 퀘스트 `clearCounts`의 던전별 집계 |
| `account_week_counters` PK | 모든 조회가 (계정, 주, 종류) 정확 일치 |
| `characters_account_level` | T1 가격식의 `max(level)`이 삭제한 캐릭터까지 계정 기준으로 읽는다(기존 인덱스는 살아 있는 캐릭터 부분 인덱스) |
| `mails_campaign_once` | 캠페인당 캐릭터에 한 통(배달 표와 이중 방어) + 캠페인별 우편 집계 |
| `mails_campaign_open` | 취소 회수와 "미수령 현황"이 미수령 우편만 id 순으로 배치 처리 |
| `mail_campaign_deliveries` 2개 | MC6 목록(캠페인 + id 커서), 계정 상세(계정 + 최신순) |
| `mail_campaigns_open` | 캐시 갱신과 상태 작업이 대기·진행 캠페인만 읽는다 |

## 10. 데이터 계약 (`sweep.json`과 `items.json`, Unity 내보내기는 메인)

서버는 `server/data/sweep.json`을 읽는다(`gamedata/sweepData.ts` zod, `data_version.json`에 포함, `SWEEP_ENABLED`가 켜지면 없거나 `schema`가 다를 때 기동 실패). 값의 원본은 Unity의 소탕 상수(`DungeonSweep`)이고 내보내기가 파일을 만든다. 키(값은 PLAN_SWEEP_AND_MAIL이 정하고 여기에 복사하지 않는다):

| 키 | 뜻 |
|---|---|
| `ticketItem`, `eventTicketItem` | 클리어권 키(표시용 `items.json` 항목의 id) |
| `eventTicketDays` | 이벤트 클리어권 기한 일수(수령 시각부터) |
| `minRank` | 소탕 자격 최고 등급(번호, 작을수록 좋다. B) |
| `xpBonusPercent` | 소탕 경험치 보너스 퍼센트(권장 0, 3절) |
| `cardCount` | 소탕 카드 수(1) |
| `gearKeepPercent` | 장비 카드 유지 확률(70, 3.2절) |
| `shop.basePrice`, `shop.weeklyLimit` | 가격식의 기본값, 주 구매 한도 |
| `weekly.directClears`, `weekly.rewardTickets` | 주간 활동 목표, 보상 장수 |

`items.json`에는 표시용 항목 2개만 더한다(이름, 아이콘, `usable:false`, 귀속 `account`, `kind`는 기존 종류 중 `consumable`). 서버는 이 키가 가방·창고·경매에 들어가는 것을 거절한다(E7). 게임 쪽 던전·몬스터·진행·상점 값은 기존 파일(`dungeons.json`, `monsters.json`, `progression.json`, `shop.json`)을 그대로 읽는다.

## 11. 환경변수, 작업, 지표

| 이름 | 기본 | 뜻 |
|---|---|---|
| `SWEEP_ENABLED` | `false` | S1~S3, T1, T2 켜기(꺼지면 `503 FEATURE_DISABLED`). 클라이언트 배포 뒤 `true` |
| `CAMPAIGN_DELIVERY_ENABLED` | `false` | 캠페인 배달·관리자 API(MC4 승인) 켜기. 새 우편 클라이언트가 나간 뒤 `true` |
| `CAMPAIGN_CACHE_SECONDS` | `30` | 진행 중 캠페인 메모리 캐시 갱신 주기 |
| `CAMPAIGN_MAX_CAP_COUNT` | `200000` | 캠페인 총 통수 상한의 상한 |
| `CAMPAIGN_MAX_WINDOW_DAYS` | `60` | 배달 기간 최대 |
| `CAMPAIGN_MAX_GOLD_PER_MAIL` | (운영 필수, 개발 `ADMIN_GRANT_MAX_GOLD`와 같게) | 1통 골드 상한 |
| `CAMPAIGN_MAX_GOLD_TOTAL` | (운영 필수) | 골드 x 통수 총합 상한. 값이 없으면 골드 첨부 캠페인을 거절 |
| `CAMPAIGN_MAX_TICKETS_PER_MAIL` | `10` | 1통 이벤트 클리어권 장수 상한 |
| `CAMPAIGN_MAX_TARGET_IDS` | `2000` | 특정 계정 목록 크기 상한 |
| `CAMPAIGN_REVOKE_BATCH` | `2000` | 회수 작업 한 번 처리 통수 |
| `RATE_SWEEP_STATUS_PER_SEC` / `RATE_SWEEP_RUN_PER_SEC` / `RATE_SWEEP_ALL_PER_SEC` | `2` / `2` / `1` | 캐릭터당 S1 / S2 / S3 |
| `RATE_SWEEP_BUY_PER_SEC` / `RATE_SWEEP_CLAIM_PER_SEC` | `1` / `1` | 캐릭터당 T1 / T2 |

`CAMPAIGN_DELIVERY_ENABLED=true`인데 `CAMPAIGN_MAX_GOLD_TOTAL`이 운영에서 비어 있으면 기동 실패한다(9단계 14절의 기동 검사 방식).

**작업**(7단계 작업 틀 `job_runs`):

| 작업 | 주기 | 하는 일 |
|---|---|---|
| `sweep_ticket_expire` | 10분 | `expires_at <= now AND remaining > 0`인 event 로트마다: 계정 행 잠금 -> `remaining`을 0으로, 원장 `expire`(delta = -이전 잔량) |
| `campaign_sweep` | 60초 | `active`이고 `ends_at <= now` 또는 `issued_count >= cap_count`인 캠페인을 `ended`로(`ended_at`). `pending`이고 `ends_at <= now`는 시스템 취소 |
| `campaign_revoke` | 30초 | 7.5절 |
| `purge`(기존에 한 줄) | 일 1회 | `account_week_counters` 60일 지난 행 삭제 |

지난 로트와 소탕·원장·캠페인은 지우지 않는다(FK, 감사). 소진된 로트는 행이 작아 보관한다.

**지표**(`ops/snapshot.ts`에 한 블록): `sweep: { runs_1h, tickets_outstanding, buy_limit_hits_1h }`, `campaign: { active, delivered_1h, cap_reached, revoke_pending }`.

**관리자 CLI**(7단계 CLI, `cliCommands.ts`): `campaign create|list|show|approve|cancel|deliveries`를 MC1~MC6에 1:1로 둔다.

## 12. 클라이언트 계약 (메인이 직접 구현)

클라이언트는 아래만 안다. **금액·확률·보상량·횟수를 보내거나 계산하지 않는다.**

| 화면 | 호출 | 쓰는 응답 필드 |
|---|---|---|
| 던전 선택 | `GET /characters/{uuid}/sweep` (화면을 열 때와 소탕·구매 뒤) | `dungeons[].difficulties[].can_sweep`, `block`(안내 문구), `best_rank`, `need_level`, `xp`(예상 경험치), `entries`, `tickets`, `hold` |
| "소탕" 버튼 | `POST /characters/{uuid}/sweep/run` `{ request_id, dungeon_id, difficulty }` | `sweeps[0]`(`xp`, `leveled_up`, `card`, `ticket`), `entries`, `tickets`, `delta` |
| "모두 소탕" 버튼 | `POST /characters/{uuid}/sweep/run-all` 같은 본문 | `sweeps[]`, `summary`, `limited_by`, `entries`, `tickets`, `delta` |
| 소탕 결과 화면 | (위 응답으로 그림) | "소탕" 표시, 받은 경험치, 카드 1장(선택 단계 없음). 모두 소탕이면 항목별 카드 목록 |
| 잡화점 | `GET .../sweep`의 `shop`, 구매 `POST .../sweep/tickets/buy` `{ request_id, count }` | `shop.unit_price`, `weekly_left/limit`, 응답 `total`, `tickets`, `delta` |
| 주간 활동 | `GET .../sweep`의 `weekly_activity`, 수령 `POST .../sweep/weekly/claim` `{ request_id }` | `progress/goal`, `claimable`, `claimed` |
| 우편함 | 기존 `GET /mail`, `POST /mail/{id}/claim`, `POST /mail/claim-all` | `title`, `body`, `attachments[]`(종류·키·개수), `expires_at`, `days_left`, `campaign`. 수령 응답의 `claimed.attachments`, `tickets` |

규칙:

1. 소탕 전에 프레즌스(`POST /presence`)가 나가 있어야 한다. `409 PRESENCE_REQUIRED`면 프레즌스를 보낸 뒤 같은 `request_id`로 다시 보낸다(9단계 4.7).
2. `delta`는 기존대로 적용하고(골드, 레벨, 경험치, 스택), 클리어권은 응답의 `tickets` 블록으로 갱신한다. 클리어권은 가방에 나타나지 않는다.
3. 소탕 결과로 **난이도 해금, 최고 랭크, 업적, 던전 클리어 로컬 기록을 올리지 않는다**. 퀘스트 "던전 클리어 N회" 진행만 한 번 올린다(서버가 청구 때 소탕 횟수를 세어 인정한다).
4. 에러 문구: `SWEEP_NOT_CLEARED`("직접 한 번 클리어해야 합니다"), `SWEEP_RANK_LOW`("B등급 이상이어야 합니다"), `LEVEL_TOO_LOW`(권장 레벨 `need`), `NO_ENTRIES_LEFT`, `NO_TICKET`("클리어권이 없습니다"), `ECONOMY_HOLD`(기존 문구), `WEEKLY_LIMIT`("이번 주 구매 한도"), `WEEKLY_NOT_READY`, `WEEKLY_ALREADY_CLAIMED`, `GOLD_CAP_EXCEEDED`(우편), `MAIL_EXPIRED`, `FEATURE_DISABLED`(기능 준비 중).
5. 버튼 활성화는 `can_sweep`·`entries.left`·`tickets.total`로만 정한다(클라이언트가 자격을 재판정하지 않는다. 틀려도 서버가 거절한다).
6. 우편: 제목·본문(줄바꿈 그대로)·첨부 아이콘 여러 개·기한 표시. 클리어권 첨부에는 `valid_days_after_claim` 안내("받은 날부터 N일"). 옛 우편(`title` 없음)은 기존 방식으로 문구를 조립한다. "모두 받기"는 기존 M3.
7. 우편 알림: 기존 `GET /mail/summary` 폴링(창이 열려 있으면 15초, 닫혀 있으면 60초). 캠페인 우편은 접속 직후 첫 폴링 또는 접속 시 생성되어 `new[]`에 `title`과 함께 나타난다.
8. 응답 형식은 `{ success, message, data, meta? }`, 실패는 `{ success:false, message, errors? }`(코드는 `code`).

## 13. 구현 체크리스트 (dotrpg-backend-coder가 이 순서대로)

1. **선행 확인**: 9단계(0020, `assertNoHold`, `assertActionPresence`, `presenceService`)가 서버에 들어가 있다. 아니면 중단한다(같은 파일을 건드린다).
2. **데이터**(메인과 협의): Unity 내보내기가 `server/data/sweep.json`과 `items.json` 표시 항목 2개를 만들었다. 서버: `gamedata/sweepData.ts`(zod), 로더 연결, `data_version.json` 포함, `economyContext.addItem`에 클리어권 키 거부(E7).
3. **마이그레이션 0021**(9절) 작성, `schema.sql` 맨 아래에 UP 본문과 머리말 표 행(2.4) 추가. DB 제약 단위 테스트(CHECK, 유일 인덱스, 트리거).
4. **순수 함수**(`sweepRules.ts`): `sweepXp`, `rollOneCard`(기존 `rollCards` 분리, 직접 플레이 결과 불변), `rollSweepCard`, `ticketUnitPrice`. 골든 테스트(3.3 표의 경험치 값).
5. **지갑**(`ticketWallet.ts`): `lockLive`, `add`(normal upsert, event 신규), `consume`, `total`, `expireDue`, 원장 기록. 계정 행 잠금 규칙.
6. **기존 코드 변경**: `countEntries`(E1), `clearCounts`(E2, 퀘스트), `finalizeCleared` 주간 카운터(E3, `weeklyCounter.ts`), `ActionKind 'sweep'`(E4), `XP_REASONS`(E5).
7. **소탕 API**: S1(현황), S2, S3 + 검증·라우트·속도 제한·플래그. `runEconomy` 사용, 4.1의 검사 순서, `sweep_denied` 이상 기록.
8. **구매·주간 활동**: T1, T2.
9. **우편 확장**: `insertMail`/목록/요약/수령 분기(`mailAttachments.ts`), `expireMailById`(E9), `integrity.ts`(E10), `mailNotify` 제목(E11). 옛 우편 회귀 테스트가 먼저 통과해야 한다.
10. **캠페인 배달**: `campaignCache.ts`, `campaignDelivery.ts`, 트리거 두 곳(E12). 플래그 `CAMPAIGN_DELIVERY_ENABLED`.
11. **관리자 API**: MC1~MC6 + CLI + 감사, 승인자 규칙, 한도 검증.
12. **작업·지표**: `sweep_ticket_expire`, `campaign_sweep`, `campaign_revoke`, `purge` 한 줄, `snapshot` 블록.
13. **이론 표 스크립트** `Tools/balance/theory_sweep.py`로 3.3 표 재생성(검증용).
14. **문서 갱신**: `schema.sql` 머리말 표, 7단계 "지우지 않는다" 목록에 새 표 추가, 이 문서의 구현 차이 기록.
15. **켜는 순서**: 서버 배포(플래그 꺼짐) -> 클라이언트 배포 -> `SWEEP_ENABLED=true` -> 우편 새 클라이언트가 퍼진 뒤 `CAMPAIGN_DELIVERY_ENABLED=true`.

## 14. 테스트 시나리오

**A. 순수 함수 (`sweepRules.test.ts`)**

1. `sweepXp`가 3.3 표의 소탕 경험치와 같다(황금 광맥 4난이도 1,102/2,771/5,913/12,474, 수련의 숲 영웅 20,002(반올림 짝수 쪽), `xpBonusPercent` 0과 10 비교).
2. `rollSweepCard`: 난수 고정으로 장비가 뽑힌 뒤 70% 통과/30% 재굴림을 따른다. 대량 표본(10만)에서 장비 비율이 `rollCards`의 한 장 장비 비율의 0.7배(허용 오차 안), 대박 카드 0, 보호권 포함(영웅).
3. 직접 플레이 `rollCards` 결과가 분리 전과 비트 단위로 같다(고정 난수 시퀀스 회귀).
4. `ticketUnitPrice`: 레벨 9 -> 10, 14 -> 15, 39 -> 40 경계에서 단계가 바뀐다.

**B. 소탕 (`sweep.test.ts`)**

1. 정상 S2: `dungeon_sweeps` 1행, `sweep_ticket_ledger(sweep_use -1)`, `xp_ledger(dungeon_sweep)`, 카드 원장(`dungeon_card`), 응답 `entries.used` +1, `tickets.total` -1.
2. S3: `n = min(남은 입장, 클리어권)`, 클리어권 5장·입장 2회면 2회, 클리어권 1장·입장 3회면 1회와 `limited_by:'tickets'`. 전체가 한 트랜잭션(중간 오류 주입 시 아무것도 남지 않음).
3. 멱등성: 같은 `request_id`를 두 번 -> 같은 응답, 원장·소탕 행 증가 없음. 같은 id에 다른 본문은 `IDEMPOTENCY_MISMATCH`.
4. 동시성: 같은 계정 두 캐릭터가 마지막 클리어권 1장을 동시에 요청(`Promise.all`) -> 정확히 하나만 성공, 다른 하나 `NO_TICKET`. 같은 캐릭터가 입장 1회 남은 상태에서 S2 두 개 동시 -> 하나만 성공.
5. 소모 순서: 이벤트(만료 가까운 것) -> 이벤트 -> 일반. 만료된 로트는 쓸 수 없다(작업 전이라도).
6. 자격: 미클리어 `SWEEP_NOT_CLEARED`, 최고 C `SWEEP_RANK_LOW`, B는 통과, 레벨이 권장 -1이면 `LEVEL_TOO_LOW`(직접 입장은 슬랙으로 되는 상황에서도 거절), 레이드 `SWEEP_NOT_ALLOWED`, 닫힌 요일 `DUNGEON_CLOSED_TODAY`. 6, 7번 실패는 `anomaly_log(sweep_denied)`.
7. 보상 잠긴 직접 클리어(`reward_locked`, `LOW_CONTRIBUTION`)만 있는 캐릭터는 소탕 불가.
8. 소탕은 `clearSummary`에 안 나온다: 소탕 뒤 한 단계 위 난이도가 열리지 않는다, `GET /dungeons`의 `best_rank`·`cleared`가 그대로, 업적 `dungeon_clears`가 그대로, 주간 `direct_clear`가 그대로.
9. 퀘스트 "던전 클리어 N회": 직접 N-1회 + 소탕 1회로 청구 성공, 소탕만으로 해금 퀘스트(`difficulty` 목표가 아닌 것) 판정에 영향 없음.
10. 입장 횟수 공유: 직접 2회 + 소탕 1회 후 직접 입장 `NO_ENTRIES_LEFT`, 소탕 3회 후 직접 입장 거절. 06:00 경계 직후 다시 3회.
11. 진행 중인 판 `RUN_ACTIVE`, 파티 판 `IN_PARTY_RUN`.
12. 경제 정지(`holds`): `403 ECONOMY_HOLD`와 어떤 행도 안 생긴다. `PRESENCE_KILL_MODE=enforce`에서 프레즌스 없음 `409 PRESENCE_REQUIRED`, `log`는 통과 + 기록.
13. 만렙: 경험치 0, 카드는 지급, `xp_ledger` 행 없음.
14. 소득 정합: 영웅 대역 캐릭터가 하루 3회 소탕해도 `velocity` 평가가 `income_caps.json`의 일일 덩어리 안이다. `XP_REASONS`에 `dungeon_sweep`이 있어 `income_hourly.xp`에 들어간다.
15. 점검 창 중 새 판 차단과 같은 지점에서 소탕도 막힌다.

**C. 구매·주간 활동 (`sweepShop.test.ts`)**

1. T1 가격 = 기본가 x (단계 + 1), 삭제한 높은 레벨 캐릭터가 있으면 그 단계로 계산. 골드 부족 `NOT_ENOUGH_GOLD`(원장 없음).
2. 주 7장: 3장 + 4장 성공, 8번째 `WEEKLY_LIMIT`. 한 요청 `count`가 남은 한도를 넘으면 전체 거절. 목요일 06:00 직후 다시 7장.
3. 두 캐릭터가 동시에 합계 8장을 사려 하면(`Promise.all`) 정확히 7장까지만(계정 행 잠금 + 조건부 UPDATE).
4. 경제 정지 중 구매 거절.
5. T2: 직접 클리어 9회 `WEEKLY_NOT_READY`, 10회 성공(3장, 원장 `weekly_activity`), 재수령 `WEEKLY_ALREADY_CLAIMED`, 동시 두 요청 중 하나만. 보상 잠긴 판과 레이드와 소탕은 세지 않는다. 두 캐릭터의 클리어가 계정 합산으로 센다.
6. 보류가 풀려 늦게 확정된 클리어가 그 클리어 시각의 주에 쌓인다.

**D. 우편 (`mail.test.ts` 확장, `mailAttachments.test.ts`)**

1. 회귀: 옛 우편(경매, `admin_grants`)의 목록·수령·모두 받기·폐기가 변하지 않는다. 응답에 `attachments`가 합성되어 있다.
2. 캠페인 우편 M1: 제목, 본문(줄바꿈), 첨부 3종, `days_left`. 탭 `gold`/`item` 필터가 첨부 기준으로 맞다.
3. 수령 M2: 골드 + 아이템 + 클리어권이 한 번에 들어가고 원장이 맞다(`mail_claim` x2, `campaign_claim`), 이벤트 로트 `expires_at = 수령 + 14일`. 같은 우편 재수령 `MAIL_ALREADY_CLAIMED`. 이중 수령 동시 두 요청 중 하나만(`campaign_claim` 유일 인덱스).
4. 골드 상한 초과면 우편 전체 거절(`GOLD_CAP_EXCEEDED`), M3는 건너뜀(`skipped`), 부분 수령 없음.
5. 경제 정지 중 M2·M3 거절(기존)과 클리어권 첨부도 못 받는다.
6. 기한이 지난 우편은 `MAIL_EXPIRED`, 폐기 틱이 아이템 `mail_expire -n`, 골드 소각 기록을 남기고 보존식이 맞다.
7. 보존식: `SUM(mails.gold WHERE kind='system') = SUM(admin_grants.gold)`이 캠페인 후에도 유지, 아이템 mail 위치 합이 첨부 포함으로 맞다.

**E. 캠페인 (`mailCampaign.test.ts`)**

1. MC1 검증: 첨부 6개, 골드·클리어권 중복, 클리어권 키를 `item`으로, 없는 아이템, `ends_at` 과거, 기간 초과, 골드 총액 상한, 없는 `account_ids`, `target` 빈 객체(`all` 없음) 전부 거절. 정상 작성은 `pending`.
2. 승인: 작성자 본인 `CAMPAIGN_SELF_APPROVAL`(API), DB CHECK도 같은 결과(직접 UPDATE 시도 거절). owner가 아닌 역할 403. 다른 owner는 성공.
3. 내용 불변: `UPDATE mail_campaigns SET title=...`이 트리거로 실패, DELETE 실패, 첨부 UPDATE/DELETE 실패.
4. 배달: 승인 전 접속은 우편 없음. 승인 후 프레즌스 진입 또는 `GET /mail/summary`에서 한 통 생성(제목·본문·첨부 복사, 아이템 `admin_grant` 원장, `delivery_key`). 같은 계정이 다시 접속·폴링해도 추가 없음, 같은 계정의 다른 캐릭터도 없음(`account` 단위). `character` 단위는 캐릭터마다 1통.
5. 첫 접속 캐릭터 기준: A 캐릭터로 접속 -> A가 받는다. 이후 B로 접속해도 없다.
6. 총 지급 상한: `cap_count=3`에 5개 계정이 동시에(`Promise.all`) 접속 -> 정확히 3통, `issued_count=3`, 나머지는 우편 없음(롤백, 배달 표·우편 표에 찌꺼기 없음). `status`는 작업 뒤 `ended`.
7. 대상 조건: 레벨 이하·직업(character 단위)·가입일·특정 계정·휴면 복귀 각각 일치/불일치. `all` 아님 + 조건 불일치는 받지 않는다.
8. 기간 밖(시작 전, 종료 후) 접속은 배달 없음. `CAMPAIGN_DELIVERY_ENABLED=false`면 배달 없음.
9. 취소: 취소 후 새 배달 없음. `revoke_unclaimed=true`면 작업이 미수령 우편만 `expires_at = now`로 앞당기고 폐기 틱이 닫는다(원장·소각 기록). 이미 받은 우편은 그대로. 회수와 수령 동시 경합에서 수령 xor 만료(둘 다 성립하지 않는다).
10. 승인 전 취소는 작성자도 가능, 활성 취소는 owner만. 승인 전에 기간이 끝나면 시스템 취소.
11. 감사: 작성·승인·취소·조회가 `admin_audit_log`에 있고 `memo`가 캠페인에 있다. 같은 `request_id` 재전송은 같은 응답(`IDEMPOTENCY_MISMATCH` 포함).
12. 현황 MC3 수치가 DB 집계와 같다(발송, 수령, 미수령, 폐기, 골드 약속·수령).
13. 배달 실패 격리: 배달 함수에 오류를 주입해도 프레즌스 응답과 M4 응답이 정상이다(로그만).

**F. 락·보존식**

1. 계정 행을 잠그는 경로(S2, S3, T1, T2, M2, M3, 만료 작업)를 섞은 동시 부하에서 교착이 없다(락 순서 ①~⑤).
2. 로트별 `SUM(sweep_ticket_ledger.delta) = remaining`(만료 작업 실행 후).
3. 골드·아이템·경험치 원장 보존식(7단계 `integrity`)이 소탕·구매·수령 시나리오 뒤에도 맞다.

## 15. 결정 대기 (선택지와 권장)

| # | 항목 | 선택지 | 권장 | 이유 |
|---|---|---|---|---|
| D1 | 클리어권 저장 방식 | (A) 일반권은 가방 아이템 + 이벤트권만 별도 로트 표 / (B) 일반·이벤트 모두 계정 지갑(로트 표) | **B** | 이벤트권의 14일 기한은 `character_items` 규칙으로 못 담고, A는 두 방식이 섞여 소모 순서·보존식이 복잡하다. B는 계정 귀속·거래 불가가 구조로 보장된다. 가방에 안 보이는 대신 던전 선택·잡화점·우편에서 개수를 보인다 |
| D2 | "B등급 기준, 등급 보너스 없음"의 경험치 | (a) 보너스 0%(`xpBonusPercent` 0) / (b) B 등급 값 +10% | **(a)** | 3.3 표: (a)는 왕·영웅이 목표 55~65%에 들어오고, (b)는 영웅이 68~77%로 목표 위. 일반·모험(33~57%)이 목표 아래인 것과 수련의 숲 영웅이 약간 높은 것은 그대로 둔다. 구현 뒤에도 데이터 한 값으로 조정 가능 |
| D3 | "장비 확률 70%" | (a) 장비 카드 확률을 정확히 0.7배, 남는 몫은 비장비가 가져감 / (b) 장비 가중치만 0.7배 | **(a)** | 문장 그대로의 확률이 정확히 맞는다(무기고처럼 장비 비중이 큰 던전에서 (b)는 0.7배가 안 된다) |
| D4 | 이벤트 클리어권 14일의 시작 | (a) 우편 수령 시각 / (b) 우편 도착(배달) 시각 | **(a)** | 우편 자체 기한(최대 30일)과 겹쳐 "받기 전에 기한이 다 가는" 일이 없다. 모으기만 하는 사용자는 우편 기한이 정리한다 |
| D5 | 가격식의 "계정 최고 레벨 단계" | (a) 기존 장비 단계 `tierOfLevel` 8단계(1,000~8,000), 삭제한 캐릭터 포함 / (b) 던전 난이도 4단계 | **(a)** | 이미 서버에 있는 함수이고 레벨 구간이 촘촘하다. 삭제를 포함해 "낮은 가격을 노린 삭제"를 막는다. 주 7장 비용은 같은 대역 사냥 1시간 `goldEq`의 9~45%로 약한 골드 소모처라 필요하면 `shop.basePrice`로 올린다 |
| D6 | 주간 활동 집계 단위·대상 | (a) 계정 합산, 보상이 안 잠긴 요일 던전 직접 클리어(파티 포함) / (b) 캐릭터별 | **(a)** | 캐릭터별이면 부캐 수만큼 3장씩 늘어난다(PLAN 5절 계정 단위 원칙) |
| D7 | 소탕 해금 기록 단위 | (a) 캐릭터별 / (b) 계정(어느 캐릭터든 B 이상 클리어) | **(a)** | 계정 단위면 본캐가 깬 기록으로 새 부캐를 소탕만으로 키우는 지름길이 생긴다. 클리어권은 계정 지갑이라 총량은 이미 제한된다 |
| D8 | 업적 "던전 클리어"에 소탕 포함 | (a) 불인정 / (b) 인정 | **(a)** | PLAN은 퀘스트만 인정으로 적었다. 업적은 직접 플레이 성취라는 의미를 유지 |
| D9 | 2인 확인과 owner 수 | (a) owner가 2명 이상이어야 함 / (b) owner 1명이면 재인증(TOTP)으로 대체 | **(a)** | PLAN "작성자 외 소유자 권한 관리자가 승인". 운영 전에 owner 계정을 2개 만들어 둔다. (b)는 2인 확인의 목적을 약하게 한다 |
| D10 | 캠페인 배달 트리거 | (a) 프레즌스 진입 + 우편 요약 폴링 / (b) 로그인 요청 | **(a)** | 받을 캐릭터가 정해지는 시점이 프레즌스 진입이고, 이미 접속 중인 사람은 폴링으로 받는다 |
| D11 | 경제 정지 중 구매·주간 수령 | (a) 둘 다 차단 / (b) 소탕·우편만(PLAN 그대로) | **(a)** | 계정 지갑이라 정지된 캐릭터의 골드·성과가 정지 안 된 캐릭터의 소탕으로 옮겨진다(8.1절). PLAN은 소탕·우편 수령만 적었으므로 확장분이다 |

"PLAN과 달라진 점"은 0.1절 표와 D1(저장 방식), D11(차단 범위 확장), 소탕 해금 기록에서 보상 잠긴 판 제외, 소탕 요청의 프레즌스 요구 네 가지다. 모두 PLAN의 규칙(조건, 횟수, 보상, 한도)은 바꾸지 않고 구현 방식과 부정 방지 경계를 보탠 것이다.
