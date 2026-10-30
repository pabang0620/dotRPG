# 길드·길드보스·월드보스·랭킹 (2026-10-20)

만렙(Lv40)을 찍은 사람이 경쟁할 거리. 사용자 결정: 넷 다 출시 전에 넣는다, 보상은 명예 위주(칭호·이름 색 + 소량 재화). 최종 장비를 맞추는 속도(레이드 2달 설계)는 바꾸지 않는다.

## 0. 지금 구조에서 정해지는 것

- 전투는 파티(최대 4명) 호스트 PC가 돌린다. 여러 파티가 같은 몬스터를 실시간으로 공유하는 구조는 없다.
- 그래서 월드보스·길드보스는 "각자 자기 방에서 3분 동안 같은 보스를 때리고, 서버가 피해량을 모아 공유 체력에서 깎는" 방식이다. 방 안 전투는 레이드와 같다(혼자 + AI 동료. 사람 파티는 받지 않는다: 랭킹이 개인 기록이고, 파티 동기화 없이 각자 방이라 공정하다).
- 피해량은 호스트가 멤버별로 보고하고(기존 레이드 기여도 `damage_dealt`), 서버가 상한으로 자른다: 3분 x 그 캐릭터의 공격 상한(`killRules.attackCap`) x 배율 0.6(실측 최고 피해가 상한의 약 40%). 상한을 넘은 보고는 상한까지만 인정하고 이상 기록을 남긴다.
- 주간 경계는 기존 기준(목요일 06:00 KST, `resetBoundaries`), 정산은 `daily_kst` 작업이 목요일에 돈다. 보상은 시스템 우편.

## 1. 길드

| 항목 | 값 |
|---|---|
| 만들기 | Lv20 이상, 골드 30,000(2026-10-21 100,000에서 하향: Lv20 누적 수입의 15% 수준), 이름 2~12자(금칙어 필터), 서버 전체에서 유일 |
| 인원 | 최대 30명 |
| 직위 | 길드장 1, 부길드장 최대 3, 길드원. 길드장 위임, 부길드장 임명·해임은 길드장만 |
| 가입 | 길드 설정으로 "바로 가입" 또는 "승인 후 가입". 신청은 한 사람이 동시에 3곳까지, 승인·거절은 길드장·부길드장 |
| 내보내기 | 길드장·부길드장(부길드장은 길드원만) |
| 탈퇴·추방 뒤 | 24시간 동안 다른 길드 가입 불가(길드보스 기록 옮겨 다니기 방지) |
| 해체 | 길드장 혼자 남았을 때만 |
| 표시 | 이름표 아래 `<길드 이름>`, 채팅 길드 탭(초록) |
| 길드 소개 | 한 줄 공지(60자, 금칙어 필터) |

- 해금: 메인 퀘스트로 여는 콘텐츠 목록에 "길드"를 더한다(1-15 전직의 길, 일일·주간 의뢰와 같이). 길드보스는 길드 가입 상태 + Lv40.

## 2. 월드보스 (개인 경쟁)

| 항목 | 값 |
|---|---|
| 대상 | Lv40 |
| 보스 | 주마다 1마리, 서버 전체 공유 체력. 첫 보스는 "심연의 거신"(기존 보스 그림을 크게 키우고 색을 바꿈, 새 패턴 없이 레이드 패턴 조합) |
| 도전 | 하루 3회, 회당 3분. 시간이 끝나거나 전원이 쓰러지면 끝(실패 없음, 그때까지 피해가 기록) |
| 파티 | 혼자 + AI 동료(사람 파티 중이면 "파티를 나간 뒤 도전하세요"). 기록은 내 영웅이 준 피해만(AI 동료 피해 제외) |
| 공유 체력 | 주간 체력 풀. 다 깎이면 "격파" 표시, 그 뒤 도전도 랭킹 기록에는 들어간다 |
| 랭킹 기준 | 이번 주 가장 높은 1회 피해량(장비·실력 경쟁. 횟수로 밀어붙이기를 막음) |

보상(주간 정산, 우편):
- 참가(1회 이상): 골드 30,000 + 강화석 소량
- 격파한 주: 참가자 전원 골드 30,000 추가
- 1위: 칭호 "세계를 꺾은 자" + 이름 금색(다음 주 정산까지)
- 2~10위: 칭호 "거신 사냥꾼" + 이름 주황색
- 11~100위: 칭호 "거신에 맞선 자"
- 랭킹 칭호는 업적 칭호처럼 채팅 이름 앞에 단다. 이름 색은 채팅과 이름표 둘 다.

## 3. 길드보스 (길드 경쟁)

| 항목 | 값 |
|---|---|
| 대상 | 길드원 Lv40, 가입 24시간 뒤부터 |
| 보스 | 죽이는 보스가 아니라 점수를 쌓는다(사용자 결정 2026-10-20). 체력·단계 없음, 보스 세기는 고정 |
| 도전 | 길드원마다 하루 2회, 회당 3분, 혼자 + AI 동료 |
| 점수 | 도전마다 서버가 인정한 피해(상한 적용 뒤)가 이번 주 길드 점수에 더해진다 |
| 랭킹 기준 | 이번 주 길드 점수 합, 같으면 그 점수에 먼저 도달한 길드 |

보상(주간 정산, 우편):
- 이번 주 1회 이상 도전한 길드원: 골드 20,000(그 주에 길드를 옮겼어도 한 번)
- 길드 1위: 그 주에 그 길드 소속으로 도전을 끝낸 길드원 칭호 "최강 길드"(주가 끝난 뒤 들어온 사람은 못 받고, 기여하고 떠난 사람은 받는다) + 길드 이름 금색(다음 주까지)
- 길드 2~10위: 길드 이름 은색

## 4. 랭킹 창

- 탭 3개: 월드보스(개인, 이번 주 / 지난주), 길드보스(길드, 이번 주 / 지난주), 전투력(개인, 지금 장비 기준. 보상 없음)
- 100위까지 + 내 순위. 랭킹은 서버가 1분마다 캐시한다.
- 할 일 창: 오늘 남은 월드보스·길드보스 도전 줄을 더한다.

## 5. 서버

- 마이그레이션 0047: `guilds`, `guild_members`, `guild_applications`, `guild_cooldowns`(탈퇴 24시간), `boss_seasons`(주·종류·보스·체력·격파 시각), `boss_attempts`(캐릭터·시즌·피해·요청 ID·인정 피해·이상 플래그), `guild_boss_scores`(길드·시즌·점수·마지막으로 점수가 오른 시각), `rank_titles`(캐릭터·칭호·이름 색·만료), 우편 코드 `world_boss`, `guild_boss`, 채팅 채널 `guild`.
- 도메인: `guilds`(만들기·검색·신청·승인·탈퇴·추방·직위·공지·해체), `bossrush`(월드·길드보스 시즌 조회, 도전 시작·끝 보고, 랭킹), 주간 정산 작업.
- 도전은 시작 시 서버가 attempt를 만들고(횟수 차감, 멱등), 끝 보고에서 피해를 인정한다. 시작 없이 온 보고, 3분 + 여유를 넘긴 보고, 두 번째 보고는 받지 않는다.
- 정산은 시즌 행 잠금 + 정산 완료 표시로 한 번만 돈다. 정산이 끝난 시즌에 늦게 온 끝 보고는 기록만 남고 공유 체력·길드 점수를 바꾸지 않는다.
- 마이그레이션 0048(2026-10-21 점검 반영): 이상 기록 `boss_damage_high`(끝 보고가 상한의 80% 이상이면 인정은 그대로, 기록만), `boss_attempts.guild_id`가 길드 종류에서도 NULL 가능(도전 중 길드를 떠난 기록).
- 도전 시작은 서버가 사람 파티(2명 이상)를 거부한다(409 `BOSS_PARTY_NOT_ALLOWED`). 길드보스 도전 중 길드를 떠나거나 옮기면 그 끝 보고는 어느 길드 점수에도 들어가지 않는다(참가 보상은 남는다).
- 랭킹 칭호 만료는 매일 KST 06:05 작업(`rank-title-expire`)이 장착을 떼고, 업적 조회도 만료 칭호를 빈 것으로 돌려주며 뗀다.
- 캐릭터 삭제: 길드장은 위임이나 해체 뒤에만 삭제할 수 있다(409 `GUILD_LEADER_MUST_TRANSFER`). 길드원은 삭제와 같은 트랜잭션에서 탈퇴 처리(인원 수·대기 신청 정리). 회원 탈퇴 익명화는 거부하지 않고 길드장을 가장 오래된 부길드장, 없으면 가장 오래된 길드원에게 넘기고 혼자면 해체한다.

## 6. 클라이언트

- 길드 창(단축키 G): 내 길드(멤버·직위·공지·신청 목록), 길드 찾기(검색·신청·만들기).
- 보스 창: 월드보스 탭(공유 체력 막대), 길드보스 탭(이번 주 길드 점수·내 기여·길드 순위), 오늘 남은 도전, 들어가기. 전투 방은 레이드 방 구성을 쓰고 위에 3분 타이머와 내 피해량.
- 랭킹 창(단축키 R가 비어 있으면 R).
- 이름표 아래 길드 이름, 랭킹 이름 색, 채팅 길드 탭.
- 오프라인(서버 없이)에서는 길드·랭킹이 잠기고 "온라인에서 열려요"로 안내한다.

## 7. API (서버·클라이언트 약속)

응답은 기존 형식 `{success, message, data}`. 경로의 `:uuid`는 캐릭터. 쓰기 요청은 `request_id`(멱등).

길드
- `GET /characters/:uuid/guild` -> `{ guild: null | { id, name, notice, join_mode: 'open'|'approval', member_count, max_members: 30, my_role: 'leader'|'officer'|'member', members: [{ character_id, name, level, career, role, last_seen_at, joined_at }], applications: [{ id, character_id, name, level, career, applied_at }] (직위 있는 사람만) }, cooldown_until: string|null, my_applications: [{ guild_id, guild_name, applied_at }] }`
- `GET /guilds?query=&page=` -> `{ guilds: [{ id, name, notice, join_mode, member_count, max_members, leader_name }], page, total }`
- `POST /characters/:uuid/guild` body `{ name, notice?, join_mode, request_id }` 만들기
- `POST /characters/:uuid/guild/apply` `{ guild_id, request_id }` -> `{ joined: boolean }`(바로 가입 길드면 true)
- `POST /characters/:uuid/guild/applications/:id/accept|reject` `{ request_id }`
- `POST /characters/:uuid/guild/leave|disband` `{ request_id }`
- `POST /characters/:uuid/guild/members/:characterId/kick|promote|demote|transfer` `{ request_id }`
- `PATCH /characters/:uuid/guild` `{ notice?, join_mode?, request_id }`
- 캐릭터 id는 외부용 uuid. 에러 코드: GUILD_NAME_TAKEN, GUILD_FULL, GUILD_COOLDOWN, GUILD_FORBIDDEN, ALREADY_IN_GUILD, NOT_IN_GUILD, LEVEL_TOO_LOW, GOLD_SHORT.

보스(kind = 'world' | 'guild')
- `GET /characters/:uuid/bosses` -> `{ world: { season_id, boss_id, name, max_hp, hp_left, defeated_at, attempts_left, attempts_per_day: 3, my_best, my_rank }, guild: null | { season_id, boss_id, name, score, my_contribution, attempts_left, attempts_per_day: 2, eligible_at, guild_rank }, week_ends_at, attempt_seconds: 180 }`
- `POST /characters/:uuid/bosses/:kind/attempts` `{ request_id }` -> `{ attempt_id, boss_id, level, hp_mul, damage_mul, started_at, ends_at }` (횟수 차감, damage_mul은 늘 1. `party_member_uuids`는 받아도 무시, 사람 파티 중이면 409 `BOSS_PARTY_NOT_ALLOWED`)
- `POST /characters/:uuid/bosses/:kind/attempts/:attemptId/finish` `{ request_id, damage_dealt, hits_landed, duration_s }` -> 월드보스 `{ accepted_damage, capped, expired, best, rank, hp_left, defeated }`, 길드보스 `{ accepted_damage, capped, expired, best, rank, score, my_contribution, guild_changed }`(score = 이번 주 길드 점수, my_contribution = 이번 주 내 기여 합. 시작 뒤 길드를 떠났으면 guild_changed true, score·my_contribution 0, rank null). 혼자 + AI 동료만이라 호스트 대리 보고는 없다.

랭킹
- `GET /rankings/:board?week=current|previous` board = `world_boss` | `guild_boss` | `power` -> `{ entries: [{ rank, name, guild_name, level, career, value, name_color }], me: { rank, value } | null, updated_at }` (guild_boss는 `{ rank, guild_name, value }`, value = 이번 주 길드 점수)

꾸미기
- 채팅 송신 프레임과 town.pos 프레임에 `guild_name`, `name_color`('gold'|'orange'|'silver'|null)를 더한다. 랭킹 칭호는 `rank_titles`에 있고 기존 칭호 선택 목록에 같이 보인다.

### 7.1 서버 구현에서 정해진 것 (위 약속에 더한 점)

- `GET /characters/:uuid/bosses`: world·guild에 `name`(둘 다 심연의 거신), 최상위에 `min_level`(40). `season_id`는 시즌 uuid.
- 도전 시작 응답의 `hp_mul`은 방 안 보스 체력 배율(1, 클라이언트 기본 배율에 곱한다), `level`은 40, `damage_mul`은 1(길드보스도 고정 세기).
- 열린 도전이 있으면 시작이 409 `BOSS_ATTEMPT_OPEN`이고 `errors.attempt_id`로 그 도전을 알려 준다(끝 보고 기한 3분 + 60초가 지나면 다음 시작 때 자동으로 닫힌다). 하루 횟수를 다 쓰면 409 `BOSS_NO_ATTEMPTS`, 길드 가입 24시간 전이면 409 `GUILD_BOSS_NOT_YET`(`errors.eligible_at`).
- 끝 보고 응답에 `expired`(기한이 지나 피해 0으로 닫힘)가 더 있다. 길드보스 응답은 `score`·`my_contribution`이다(단계·체력·defeated 없음). `hits_landed`가 0이면 피해를 받지 않는다. 피해 상한은 서버가 잰 경과 시간(최대 180초) x 착용 장비 화력 상한(초당) x 0.6. 상한의 80% 이상인 보고는 인정하되 이상 기록(`boss_damage_high`)을 남긴다.
- 랭킹: `GET /rankings/:board?week=&character_id=<내 캐릭터 uuid>`(character_id를 줘야 `me`가 온다). 응답에 `board`, `week`. 길드보스 줄은 `{ rank, guild_name, value }`(value = 이번 주 길드 점수). 상태 응답의 순위는 1분 캐시라 끝 보고 직후에는 끝 보고 응답의 `rank`를 쓴다. 전투력은 Lv20 이상.
- 길드 상태의 `guild`에 `honor_color`('gold'|'silver'|null). 길드 에러 코드 추가: GUILD_NOT_FOUND, GUILD_APPLY_LIMIT, GUILD_NAME_BLOCKED, GUILD_NOT_EMPTY, GUILD_LEADER_MUST_TRANSFER, GUILD_OFFICER_LIMIT, GUILD_ROLE_INVALID, GUILD_MEMBER_NOT_FOUND, GUILD_SELF, GUILD_APPLICATION_NOT_FOUND.
- 꾸미기: `chat.msg`의 `from`과 `town.pos`에 `guild_name`, `guild_color`('gold'|'silver'), `name_color`('gold'|'orange')가 값이 있을 때만 붙는다(서버 값, 클라이언트가 보내지 않는다). 길드 채팅은 `channel: 'guild'`, 길드가 없으면 `chat.err` `NOT_IN_GUILD`. 놓친 길드 채팅 따라잡기는 없다.
- 칭호: 업적 목록 응답에 `rank_titles: [{ id, title, name_color, expires_at }]`, 칭호 장착(`POST /characters/:uuid/title`)에 `rank_world_1`·`rank_world_10`·`rank_world_100`·`rank_guild_1`을 그대로 쓴다.
- 우편 `system_code`: `world_boss`, `guild_boss`(월드보스 참가·격파 골드와 강화석 5개는 따로 한 통, 길드보스 참가 골드 20,000).
- 정산은 10분마다 "끝났는데 정산 안 한 시즌"을 찾는 작업(`boss-settle`)이라 경계 직후 서버가 꺼져 있어도 켜지면 정산된다. 월드보스 체력은 env `WORLD_BOSS_HP`(기본 40,000,000: 실측 1회 110,000 x 3회 x 7일 x 참여율 0.5 = 한 사람당 주 115만, 35명이면 1주 격파. 참가자가 100명 수준이면 160,000,000으로 올린다).

## 8. 보스 방 실측 (2026-10-20)

`-measureOnly bossrush`, 측정 봇(숙련 + 장판 회피), Lv40, 직업 4종 x 혼자/AI 3명 x 장비 2벌(레이드 에픽 무기 +5, 클리어 한 벌 에픽 +10~11). 원본 `analysis/player-sim/data/measure_run5/bossrush*/`.

| | 1차(보스 공격력 x1.7) | 2차(x1.0) |
|---|---|---|
| 3분 피해 | 6만~34만, 아르카니스트는 9~42초에 사망 | 5.4만~27만, 16판 모두 3분 버팀 |
| 서버 상한 대비 | 3~19%(상한 배율 1.5) | 9~42%(상한 배율 0.6) |

- 처음 1회 추정 15,000은 사냥터 몬스터 기준이라 10배 낮았다. 월드보스 체력을 1,600만에서 1억 6,000만으로 올렸다가, 2026-10-21 출시 초 인원(20~35명)에서도 격파가 나오도록 4,000만으로 다시 내렸다(인원이 늘면 env로 상향).
- 보스 공격력을 x1.7에서 x1.0으로 내렸다. 피해 랭킹에서 한 직업만 3분을 못 버티면 경쟁이 안 된다.
- 비숍(치유)은 6~7만으로 가장 낮다. 개인 피해 랭킹의 성격상 남는 차이다(길드보스 점수에는 그대로 기여).
- 같은 측정에서 +11 이상 무기의 강화 빛이 맞을 때마다 오류를 내던 결함을 찾아 고쳤다(`WeaponEnhanceVfx`, 실제 플레이에도 해당).

## 9. 2026-10-30 변경: 월드보스 2주 시즌, 영구 칭호, 길드보스 순위 보상

사용자 결정(2026-10-30). 위 2·4·5·7절의 "주"는 월드보스에 한해 "시즌(2주)"로 읽는다. 길드보스는 주간 그대로.

| 항목 | 값 |
|---|---|
| 월드보스 시즌 | 2주. 기준점 2026-10-29(목) 06:00 KST(`bossRushRules.WORLD_SEASON_ANCHOR_MS`)부터 격주 목요일 06:00 KST에 바뀐다. 모든 서버가 같은 상수를 쓴다. 하루 3회 도전·06:00 일일 경계는 그대로 |
| 시즌 찾기 | 지금 시각을 포함하는 월드보스 시즌 행이 있으면 그것, 없으면 기준점으로 계산한 2주 시즌을 만든다(전환 전에 만든 1주 시즌은 자기 끝까지 간다) |
| 공유 체력 | 시즌당 기본 80,000,000(예전 주 40,000,000 x 2: 35명이면 시즌 첫 주 끝 무렵 격파, 예전과 같은 시점). 보스 레벨 50 시즌 x2.0 = 160,000,000. env `WORLD_BOSS_HP`는 "한 시즌 체력"이고, 운영 env에 예전 값 40,000,000이 적혀 있으면 바꾼다 |
| 월드보스 보상 | 시즌마다 한 번이라 두 배: 참가 60,000 골드 + 강화석 10, 격파한 시즌 60,000 골드 추가. 주당 수입은 예전과 같다. 보스 우편은 `theory_gold.py`·`income_caps.json`에 들어 있지 않아 무자본 비율(0.85~0.90)에 영향 없음 |
| 월드보스 칭호 | 영구. 1위 '세계를 꺾은 자', 2~10위 '거신 사냥꾼', 11~100위 '거신에 맞선 자'를 한 번 받으면 언제든 장착한다(`rank_titles.expires_at` NULL). 이미 받은 월드보스 칭호도 영구로 바꿨다 |
| 이름 색 | 장착한 랭킹 칭호의 색을 따른다(1위 금색, 2~10위 주황색). 칭호를 떼거나 다른 칭호를 달면 색이 없다. 채팅·이름표·랭킹 창 모두 같은 규칙 |
| 길드보스 칭호 | '최강 길드'는 그대로 다음 주 정산 + 1시간까지(만료 작업이 뗀다). 칭호 코드를 같이 쓰지만 만료 규칙은 월드보스만 바뀌었다 |
| 길드보스 순위 보상 | 주간 정산 때 1~10위 길드의 길드원 전원(정산 시각 길드원, 그 주가 끝나기 전에 가입한 사람)에게 우편 `guild_boss`: 1위 15,000 골드 + 장비 보호권 1 + 마력 정수 30, 2~3위 10,000 골드 + 마력 정수 20, 4~10위 5,000 골드 + 마력 정수 10. 한 정산에서 한 사람은 한 번(주중에 길드를 옮겼으면 정산 시각 길드의 순위로). 참가 골드 20,000과 별개. 한 사람 주 최대 15,000 골드로 Lv40 사냥 약 12분어치(시간당 골드 환산 약 77,000) |
| 랭킹 창 | 월드보스 탭은 "이번 시즌 / 지난 시즌"(이번 시즌 = 지금을 포함하는 시즌, 지난 시즌 = 지금 전에 끝난 가장 최근 시즌), 응답에 `season_start`·`season_end` |
| 상태 응답 | `world.season_ends_at`(월드보스 정산 시각), `world.season_weeks`. 최상위 `week_ends_at`은 주간 경계(길드보스) 그대로 |
| 머리 위 칭호 | 장착한 칭호를 이름 위 한 줄에 랭킹 마크(`Resources/Art/UI/rank_mark_world_1`·`_10`·`_100`·`rank_mark_guild_1`, 없으면 글자만)와 함께 표시. 마을 이웃은 `town.pos`의 `title_id`·`title`, 파티원은 파티 목록 `members[].title_id`·`title`, 내 캐릭터는 업적 조회의 장착 칭호. 칭호가 없으면 아무것도 그리지 않는다 |
| 마이그레이션 0061 | `rank_titles.expires_at` NULL 허용 + 기존 월드보스 칭호 영구, 진행 중인 1주 월드보스 시즌이 기준점의 첫 주면 2주로 늘리고 체력 두 배 |

