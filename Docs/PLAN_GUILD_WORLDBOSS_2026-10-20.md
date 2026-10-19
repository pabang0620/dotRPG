# 길드·길드보스·월드보스·랭킹 (2026-10-20)

만렙(Lv40)을 찍은 사람이 경쟁할 거리. 사용자 결정: 넷 다 출시 전에 넣는다, 보상은 명예 위주(칭호·이름 색 + 소량 재화). 최종 장비를 맞추는 속도(레이드 2달 설계)는 바꾸지 않는다.

## 0. 지금 구조에서 정해지는 것

- 전투는 파티(최대 4명) 호스트 PC가 돌린다. 여러 파티가 같은 몬스터를 실시간으로 공유하는 구조는 없다.
- 그래서 월드보스·길드보스는 "각자 자기 방에서 3분 동안 같은 보스를 때리고, 서버가 피해량을 모아 공유 체력에서 깎는" 방식이다. 방 안 전투는 레이드와 같다(혼자 + AI 동료. 사람 파티는 받지 않는다: 랭킹이 개인 기록이고, 파티 동기화 없이 각자 방이라 공정하다).
- 피해량은 호스트가 멤버별로 보고하고(기존 레이드 기여도 `damage_dealt`), 서버가 상한으로 자른다: 3분 x 그 캐릭터의 공격 상한(`killRules.attackCap`) x 여유 배율. 상한을 넘은 보고는 상한까지만 인정하고 이상 기록을 남긴다.
- 주간 경계는 기존 기준(목요일 06:00 KST, `resetBoundaries`), 정산은 `daily_kst` 작업이 목요일에 돈다. 보상은 시스템 우편.

## 1. 길드

| 항목 | 값 |
|---|---|
| 만들기 | Lv20 이상, 골드 100,000, 이름 2~12자(금칙어 필터), 서버 전체에서 유일 |
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
| 보스 | 길드마다 따로 체력. 1단계부터 시작, 체력을 다 깎으면 다음 단계(체력 x1.5, 피해 x1.15) |
| 도전 | 길드원마다 하루 2회, 회당 3분 |
| 랭킹 기준 | 이번 주 도달 단계, 같으면 그 단계 깎은 비율, 같으면 먼저 도달한 길드 |

보상(주간 정산, 우편):
- 참가한 길드원: 도달 단계 x 골드 10,000(최대 10단계)
- 길드 1위: 길드원 전원 칭호 "최강 길드" + 길드 이름 금색(다음 주까지)
- 길드 2~10위: 길드 이름 은색

## 4. 랭킹 창

- 탭 3개: 월드보스(개인, 이번 주 / 지난주), 길드보스(길드, 이번 주 / 지난주), 전투력(개인, 지금 장비 기준. 보상 없음)
- 100위까지 + 내 순위. 랭킹은 서버가 1분마다 캐시한다.
- 할 일 창: 오늘 남은 월드보스·길드보스 도전 줄을 더한다.

## 5. 서버

- 마이그레이션 0047: `guilds`, `guild_members`, `guild_applications`, `guild_cooldowns`(탈퇴 24시간), `boss_seasons`(주·종류·보스·체력·격파 시각), `boss_attempts`(캐릭터·시즌·피해·요청 ID·인정 피해·이상 플래그), `guild_boss_progress`(길드·시즌·단계·남은 체력), `rank_titles`(캐릭터·칭호·이름 색·만료), 우편 코드 `world_boss`, `guild_boss`, 채팅 채널 `guild`.
- 도메인: `guilds`(만들기·검색·신청·승인·탈퇴·추방·직위·공지·해체), `bossrush`(월드·길드보스 시즌 조회, 도전 시작·끝 보고, 랭킹), 주간 정산 작업.
- 도전은 시작 시 서버가 attempt를 만들고(횟수 차감, 멱등), 끝 보고에서 피해를 인정한다. 시작 없이 온 보고, 3분 + 여유를 넘긴 보고, 두 번째 보고는 받지 않는다.
- 정산은 시즌 행 잠금 + 정산 완료 표시로 한 번만 돈다.

## 6. 클라이언트

- 길드 창(단축키 G): 내 길드(멤버·직위·공지·신청 목록), 길드 찾기(검색·신청·만들기).
- 보스 창: 월드보스·길드보스 탭, 공유 체력 막대, 오늘 남은 도전, 들어가기. 전투 방은 레이드 방 구성을 쓰고 위에 3분 타이머와 내 피해량.
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
- `GET /characters/:uuid/bosses` -> `{ world: { season_id, boss_id, name, max_hp, hp_left, defeated_at, attempts_left, attempts_per_day: 3, my_best, my_rank }, guild: null | { season_id, boss_id, stage, max_hp, hp_left, attempts_left, attempts_per_day: 2, eligible_at, guild_rank }, week_ends_at, attempt_seconds: 180 }`
- `POST /characters/:uuid/bosses/:kind/attempts` `{ request_id, party_member_uuids?: [] }` -> `{ attempt_id, boss_id, level, hp_mul, damage_mul, stage?, started_at, ends_at }` (횟수 차감)
- `POST /characters/:uuid/bosses/:kind/attempts/:attemptId/finish` `{ request_id, damage_dealt, hits_landed, duration_s }` -> `{ accepted_damage, capped: boolean, best, rank, hp_left, stage?, defeated: boolean }`. 혼자 + AI 동료만이라 호스트 대리 보고는 없다.

랭킹
- `GET /rankings/:board?week=current|previous` board = `world_boss` | `guild_boss` | `power` -> `{ entries: [{ rank, name, guild_name, level, career, value, name_color }], me: { rank, value } | null, updated_at }` (guild_boss는 `{ rank, guild_name, stage, value }`)

꾸미기
- 채팅 송신 프레임과 town.pos 프레임에 `guild_name`, `name_color`('gold'|'orange'|'silver'|null)를 더한다. 랭킹 칭호는 `rank_titles`에 있고 기존 칭호 선택 목록에 같이 보인다.

### 7.1 서버 구현에서 정해진 것 (위 약속에 더한 점)

- `GET /characters/:uuid/bosses`: world·guild에 `name`(심연의 거신 / 길드의 거상), 최상위에 `min_level`(40). `season_id`는 시즌 uuid.
- 도전 시작 응답의 `hp_mul`은 방 안 보스 체력 배율(1000, 3분 안에 쓰러지지 않게), `level`은 40. 길드보스는 `stage`와 `damage_mul`(1.15^(단계-1)).
- 열린 도전이 있으면 시작이 409 `BOSS_ATTEMPT_OPEN`이고 `errors.attempt_id`로 그 도전을 알려 준다(끝 보고 기한 3분 + 60초가 지나면 다음 시작 때 자동으로 닫힌다). 하루 횟수를 다 쓰면 409 `BOSS_NO_ATTEMPTS`, 길드 가입 24시간 전이면 409 `GUILD_BOSS_NOT_YET`(`errors.eligible_at`).
- 끝 보고 응답에 `expired`(기한이 지나 피해 0으로 닫힘)가 더 있다. 길드보스의 `defeated`는 "이번 보고로 단계를 넘겼다"는 뜻이다. `hits_landed`가 0이면 피해를 받지 않는다. 피해 상한은 서버가 잰 경과 시간(최대 180초) x 착용 장비 화력 상한(초당) x 1.5.
- 랭킹: `GET /rankings/:board?week=&character_id=<내 캐릭터 uuid>`(character_id를 줘야 `me`가 온다). 응답에 `board`, `week`. 길드보스 줄은 `{ rank, guild_name, stage, cleared_ratio, value }`(value = 이번 주 누적 피해). 전투력은 Lv20 이상.
- 길드 상태의 `guild`에 `honor_color`('gold'|'silver'|null). 길드 에러 코드 추가: GUILD_NOT_FOUND, GUILD_APPLY_LIMIT, GUILD_NAME_BLOCKED, GUILD_NOT_EMPTY, GUILD_LEADER_MUST_TRANSFER, GUILD_OFFICER_LIMIT, GUILD_ROLE_INVALID, GUILD_MEMBER_NOT_FOUND, GUILD_SELF, GUILD_APPLICATION_NOT_FOUND.
- 꾸미기: `chat.msg`의 `from`과 `town.pos`에 `guild_name`, `guild_color`('gold'|'silver'), `name_color`('gold'|'orange')가 값이 있을 때만 붙는다(서버 값, 클라이언트가 보내지 않는다). 길드 채팅은 `channel: 'guild'`, 길드가 없으면 `chat.err` `NOT_IN_GUILD`. 놓친 길드 채팅 따라잡기는 없다.
- 칭호: 업적 목록 응답에 `rank_titles: [{ id, title, name_color, expires_at }]`, 칭호 장착(`POST /characters/:uuid/title`)에 `rank_world_1`·`rank_world_10`·`rank_world_100`·`rank_guild_1`을 그대로 쓴다.
- 우편 `system_code`: `world_boss`, `guild_boss`(참가·격파·단계 골드, 강화석 5개는 따로 한 통).
- 정산은 10분마다 "끝났는데 정산 안 한 시즌"을 찾는 작업(`boss-settle`)이라 경계 직후 서버가 꺼져 있어도 켜지면 정산된다. 월드보스 체력은 env `WORLD_BOSS_HP`(기본 16,000,000).
