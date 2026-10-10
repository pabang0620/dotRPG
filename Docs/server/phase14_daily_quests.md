# 일일 의뢰 (2026-10-10)

목적: Lv20 이후 메인 단계 간격이 레벨 1~2개(2~5시간)로 벌어지는 구간에서, 하루 플레이마다 짧은 목표와 보상을 준다. 하루 횟수가 정해져 있어 적게 하는 사람이 상대적으로 더 이득이다(휴식 경험치와 같은 효과). 근거와 시뮬레이션은 `Docs/BALANCE_EARLY_PROGRESSION.md` 7절.

## 1. 데이터 `server/data/daily_quests.json` (서버 정본)

레벨 보상(`level_rewards.json`)과 같은 방식: 서버만 읽고 data_version 해시에 넣지 않는다. 클라이언트는 API 응답의 문구를 그대로 보여 준다.

- `unlockQuest`: 이 퀘스트를 청구한 캐릭터부터 열린다 (`c1_stronger`, 1-19, Lv20).
- `perDay`: 하루 칸 수 (3). 게임 날짜는 06:00 KST 경계(`resetBoundaries`).
- `carryDays`: 전날 칸 중 받지 않은 칸을 오늘도 할 수 있는 날 수 (1). 그보다 오래된 칸은 사라진다.
- `xpShare`: 칸 하나의 경험치 = 청구 시점 레벨의 레벨업 필요 XP(`progression.json` xpToNext) x 0.05, 반올림. 만렙이면 경험치 0, 대신 `maxLevelGold`를 골드로 준다.
- `bands`: 레벨 구간마다 의뢰 목록. 캐릭터 레벨이 `minLevel..maxLevel`인 구간을 쓴다. 구간 `gold`는 칸 하나의 골드.
- 의뢰 `type`
  - `kill`: `map`에서 `monster`를 `count`마리. 수락 시각 이후 그 맵에서 기록된 처치만 센다(`kill_log.map_id`, `created_at >= accepted_at`). 같은 몬스터 id가 여러 레벨 맵에 나오므로 맵을 반드시 본다.
  - `dungeon`: 수락 이후 요일 던전(레이드 제외) 클리어 `count`회.

## 2. 칸 뽑기

- 칸 = (게임 날짜, 0..perDay-1). 아직 수락하지 않은 칸의 의뢰는 (캐릭터 내부 id, 게임 날짜)로 시드한 결정적 셔플로 고른다. 같은 날 같은 레벨 구간이면 몇 번 조회해도 같다.
- 후보: 현재 구간의 의뢰 중 kill은 그 맵의 필드 몬스터 레벨 <= 캐릭터 레벨 + 2인 것만, dungeon은 항상. 후보에서 서로 다른 의뢰를 perDay개 고른다(후보가 모자라면 앞에서부터 반복).
- 수락하면 의뢰 id와 수락 시각이 행에 고정된다. 이후 레벨이 바뀌어도 그 칸의 의뢰는 그대로다.

## 3. 테이블 `daily_quests` (마이그레이션 0029)

| 열 | 설명 |
|---|---|
| character_id BIGINT | characters(id) |
| game_day DATE | 06:00 KST 기준 게임 날짜 |
| slot SMALLINT | 0..perDay-1 |
| template_id TEXT | 수락한 의뢰 |
| accepted_at TIMESTAMPTZ | 진행 집계 시작 |
| claimed_at TIMESTAMPTZ NULL | 청구 시각 |
| reward JSONB NULL | 지급 스냅샷 {xp, gold} |
| request_id UUID NULL | 청구 요청 |

PK (character_id, game_day, slot). 행이 있으면 수락, claimed_at이 있으면 청구 완료. 하루 한 칸 한 번은 PK로 보장한다.

## 4. API (모두 캐릭터 소유 확인, 쓰기는 request_id 멱등)

- `GET /characters/:uuid/dailies`
  - 응답 `{ character_id, unlocked, level, game_day, next_reset_at, giver, slots: [...] }`
  - slot: `{ day, slot, template_id, type, title, text, label, map, count, progress, state, reward: { xp, gold } }`
  - state: `offered`(수락 전) / `active`(진행 중) / `ready`(progress >= count) / `claimed`
  - 오늘 칸 perDay개 + 전날(carryDays 안) 칸 중 수락했지만 청구하지 않은 칸과 수락하지 않은 칸. 전날 칸의 의뢰는 그날 날짜로 뽑는다.
  - reward는 지금 레벨 기준 예상치(청구 때 다시 계산)
- `POST /characters/:uuid/dailies/accept` body `{ day, slot, request_id }`: 행 생성. 이미 있으면 그대로 돌려준다. 잠김(DAILY_LOCKED), 기간 밖(DAILY_EXPIRED), 칸 번호 오류(422).
- `POST /characters/:uuid/dailies/claim` body `{ day, slot, request_id }`: 행 잠금 후 진행 재확인, 미완료면 `DAILY_NOT_DONE`(422), 이미 청구면 409 `DAILY_ALREADY_CLAIMED`. 경험치는 `grantXp(xp, 'daily_quest', template_id)`, 골드는 퀘스트 보상과 같은 원장 경로(사유 `daily_quest`). 응답은 목록 응답 + `{ granted: { xp, gold }, level, xp, gold }`.

## 5. 클라이언트

- `Net/DailyQuestClient.cs`: `LevelRewardClient`와 같은 구조(캐릭터 문맥, Refresh, Accept, Claim, Changed 이벤트). 오프라인 플레이에는 없다.
- `UI/DailyQuestScreen.cs`: 칸 목록(제목, 설명, 진행 n/m, 보상), 수락/보상 받기 버튼, 다음 초기화 시각. 사이드 메뉴 "일일 의뢰" 버튼, 받을 것이 있으면 표시.
- 청구 뒤 레벨·경험치·골드는 서버 응답으로 맞춘다(퀘스트 보상 청구와 같은 경로).

## 6. 주간 의뢰 (같은 파일 `weekly`)

- 같은 해금 조건(`unlockQuest`), 주 3개, 목요일 06:00 KST 초기화(`resetBoundaries().weeklyStartAt`). 이월 없음.
- 칸 = (주 시작 날짜, 0..perWeek-1). 뽑기는 (캐릭터 내부 id, 주 시작 날짜) 시드, 후보 규칙은 일일과 같다(kill은 맵 몬스터 레벨 <= 캐릭터 레벨 + 2).
- 보상: 칸 하나에 청구 시점 레벨 막대 x `xpShare`(0.15) + 구간 `gold`. 만렙은 `maxLevelGold`.
- 의뢰 `type`
  - `kill`: 일일과 같다(맵 + 수락 이후). 일일 의뢰와 같은 처치가 함께 집계되어도 된다.
  - `dungeon`: 수락 이후 요일 던전 클리어 `count`회.
  - `raid`: 수락 이후 `target` 레이드 클리어(`dungeon_runs` cleared) `count`회.
  - `daily`: 수락 이후 청구한 일일 의뢰 수 `count`.
- 저장: 일일과 같은 테이블에 `period`('day' | 'week') 열을 두고 PK (character_id, period, game_day, slot). 주간 행의 game_day는 주 시작 날짜(KST).
- API: 일일 API의 응답과 요청에 `period`를 더한다. `GET /characters/:uuid/dailies` 응답에 `weekly: { week_start, next_reset_at, slots }`를 같이 넣고, accept/claim body에 `period`('day' 기본 | 'week')를 받는다.
- 클라이언트: 일일 의뢰 창 아래쪽에 "주간 의뢰" 구역을 둔다(같은 칸 표시, 진행 n/m).
