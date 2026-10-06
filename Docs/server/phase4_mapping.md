# 서버 4단계 게임 값 대응표 (파티 협동)

게임 쪽 정의(C# 필드·함수) <-> 서버 테이블·컬럼 <-> API. API는 [phase4_api.md](phase4_api.md), 스키마는 `server/schema.sql`(`0005_party.sql`). 앞 단계 대응표는 [phase3_mapping.md](phase3_mapping.md).
값(수치·목록)은 여기 복사하지 않고 `server/data/*.json` 이름과 C# 출처로만 참조한다. "예"로 든 수치는 근거 설명용이다. C# 출처는 2026-10-03 코드 기준이다.

## 1. C# 동작 <-> 서버 판정

### 1.1 파티 찾기·자동 매칭 (`Net/PartyFinderService.cs`, `UI/OnlineWindows.cs`)

| C# 동작 | 지금 (오프라인 목업) | 서버 판정 / API | 비고 |
|---|---|---|---|
| `IPartyFinderService.List(dungeonFilter)` | 생성된 모집 글 9개, 내 글·빈자리·`minPower` 순 정렬 | GET `/parties` (`dungeon_id`, `difficulty`, `page`, `limit`) | 정렬 규칙은 서버가 같게 한다. `PartyPost.mine/applied/Full`은 응답의 `mine/applied/full` |
| `Create(dungeonId, difficulty, maxMembers, minPower, message)` | 내 글 하나만 유지(`posts.RemoveAll(p => p.mine)`), 메시지 30자 자름 | POST `/parties` | 서버는 `listed_until = 10분`, 메시지 금칙어 판정, `minPower <= 내 power_estimate`. 한 캐릭터가 한 파티만(`party_members_one_active`) |
| `Cancel(postId)` | 내 글 삭제 | PATCH `/party` `{ listed:false }` 또는 POST `/party/leave` | 모집 취소와 파티 해산을 구분한다(멤버가 있으면 파티는 남는다) |
| `Apply(postId, myPower)` -> 한국어 상태 한 줄 | 즉시 문자열 반환("방장 응답 없음") | POST `/parties/{id}/apply` + 폴링 `applications_mine`, `party` | **동기 문자열이 비동기 상태 변화로 바뀐다**: 신청 중 표시 -> 수락(파티 창) / 거절·만료(토스트). 메시지는 에러 코드(`POWER_TOO_LOW`, `PARTY_FULL`, `PARTY_NOT_FOUND`)를 클라이언트가 한국어로 만든다 |
| `PartyPost.minPower`, `MyPower`(`CharacterStats.Power`) | 클라이언트 계산값끼리 비교 | 서버 `power_estimate`끼리 비교(응답 `power`) | 결정 대기 4(phase4_api 16절). 온라인에서는 `GET /characters/{uuid}`의 `power_estimate`를 쓴다 |
| `PartyPost.members/maxMembers` | 사람+AI 구분 없음 | `members`는 현재 사람 수, `max_members`는 사람 정원 2~4 | AI는 출발 때(`ai_count`) |
| `PartyFinderRules.PresetMessages`, `MaxMessage = 30` | 프리셋 5개 + 자유 입력 30자 | `parties.message CHECK <= 30`, 금칙어는 서버 | 프리셋은 클라이언트가 그대로 보낸다 |
| 글 10분 만료 (PLAN_ONLINE §2.1) | 없음 | `parties.listed_until`, `PARTY_LISTING_MINUTES` | 만료돼도 파티는 남고 게시판에서만 빠진다 |
| 신청 30초 무응답 (§2.1) | 없음 | `party_applications.expires_at`, `PARTY_APPLY_SECONDS` | 지연 처리(요청이 닿을 때 만료) |
| `StartQueue(dungeonId, difficulty)` / `Queue.active` | `MatchQueueState` 로컬 | POST `/match/queue`, GET `/party`의 `queue` | `queued_at`, `depart_at`, `humans_waiting`. 대기열은 서버 메모리(표 없음) |
| `CancelQueue()` | 로컬 | DELETE `/match/queue` | |
| `Tick(dt)`: `Queue.elapsed += dt`, 60초면 `DepartWithAi()`, 목업이 18초·41초에 사람 +1 | 목업 타이머 | 서버가 `depart_at`과 맞추기 규칙(phase4_api 5.4)으로 판정, 클라이언트는 폴링으로 `queue`/`party`를 본다 | `Queue.humans`는 `humans_waiting`. 남은 시간 표시는 `depart_at - 서버시각`(`/meta`의 `server_time`으로 오차 보정) |
| `DepartWithAi()` -> AI 수 반환 | `PartyManager.MaxMembers - Queue.humans` | POST `/match/fill-ai` -> 파티 생성, 이어서 방장이 `start`의 `ai_count`로 AI 수를 정한다(기본 `4 - 사람 수`) | |
| `Departed(MatchQueueState, int aiCount)` 이벤트 | 타이머 만료 | 폴링에서 `queue: null` + `party != null`(source=match)을 보면 발생시킨다 | 방장 클라이언트는 곧바로 `start`를 부른다. 비방장은 로비 창을 연다 |
| 자동 매칭 전투력 ±30% (PLAN_ONLINE §5.1) | 없음 | `MATCH_POWER_RATIO` | 서버 `power_estimate` 기준 |
| `OnlineServices.PartyFinder`(setter로 교체) | `MockPartyFinderService` | `ServerPartyFinderService`(새 파일)를 온라인 접속 때 주입 | `IsOnline`이 true. 목업은 오프라인 모드에서 그대로 유지 |

### 1.2 던전 입장·진행·결과 (`Dungeon/*.cs`, `Net/OnlineEconomy.cs`)

| C# 동작 | 지금 | 4단계 서버 판정 / API | 비고 |
|---|---|---|---|
| `DungeonDirector.CannotEnterReason` (요일, 해금, 레이드 해금·열쇠, 입장 횟수, `Party.Count > maxParty`) | 로컬 시계·로컬 진행 | 서버 `checkEntry`가 같은 순서로 판정(phase4_api 5.2). 레이드 상태는 GET `/raids`, 요일 던전은 GET `/dungeons` | 클라이언트 검사는 안내용으로 유지하고 서버 에러 코드(`RAID_LOCKED`, `KEYS_MISSING` 등)를 같은 문구로 표시 |
| `Enter` -> `EnterOnline` | 솔로 요일 던전만, 레이드·용병 있으면 막음 | 솔로(+AI): POST `/dungeon-runs`(`ai_count`, 레이드 허용). 파티: POST `/party/start` -> `join`/`begin` | `EnterOnline`의 `isRaid` 차단과 `PartyRoster.Count > 0` 차단 제거 |
| `StartRun`: `new DungeonRun(dungeon, difficulty, party.Count)` | 파티 인원 = 로컬 멤버 수 | 인원 = `humans + ai_count`(서버 기록 `dungeon_runs.party_size`) | `run.RewardsLocked`는 서버 `reward_locked`로 채운다 |
| `LoadRoom`/`SpawnMonsters` (몬스터 생성, `DungeonMonsters.Spawn(..., run.HpMul, ...)`) | 로컬 | **방장 PC만** 생성·계산. 멤버는 스냅샷으로 만든 꼭두각시(puppet) | 서버가 아는 것은 처치 보고와 `room_index`뿐 |
| `OnDoorEntered`/`Transition(LoadRoom)` | 로컬 문 | 방장이 방 전환을 정하고 `Control` 메시지로 알린다. 서버 방 진행은 멤버 각자의 처치 보고(`room_index`)가 올린다 | |
| `EnemyController.DropLoot` -> `OnlineEconomy.ReportKill` | 처치한 PC에서 한 번 | **멤버 각자가** 자기 계정으로 보고한다. 방장 PC의 `Die()`와 멤버 PC의 puppet 사망 이벤트 모두 `ReportKill`을 부른다 | 소환체·`noLoot` 제외 규칙은 같다. `hits`(황금 해골 타격 골드)는 방장이 센 값을 사망 이벤트에 실어 보낸다 |
| `PartyManager.OnEnemyKilled`: 처치마다 로컬 `AddXp` | 한 번 | 온라인은 서버 응답으로만(3단계), 멤버 각자 전부 받는다 | 나누지 않는다(phase4_api 9절) |
| `Update`: `run.Elapsed += Time.deltaTime` (Playing && IsPlaying && !busy) | 로컬 | 결과 보고의 `stats.elapsed_ms`(주장), 서버는 `begun_at` 기준 경과로 검증 | 멤버 PC의 창이 열려 시계가 멈추면 방장 시계와 달라질 수 있어 허용 오차(5초, 5%)를 둔다 |
| `FinishCleared` -> `FinishClearedOnline` | 서버 결과 대기(최대 15초) | 파티 판: `result`(접수) -> `settle`(폴링) | `pending` 응답이면 `settle_after_ms` 뒤 재호출. 대기 UI는 "다른 파티원 결과 확인 중" |
| `Fail(reason)` -> `OnlineEconomy.FinishDungeon(false, ...)` | 실패 보고 | 같음. 파티 판의 `failed`는 즉시 닫힌다 | |
| `TakeCardOnline` / `OnlineEconomy.PickCard` | 카드 선택 | POST `/dungeon-runs/{run_id}/cards/pick` (3단계 그대로) | 멤버마다 자기 카드. 동료 AI·다른 사람의 카드 뒤집기는 연출 |
| `HandleLocalDeath`, `AcceptRevive`, `GiveUp`, `RevivesLeft` | 로컬 플레이어의 부활 코인 | 멤버별 `stats.revives_used`(<= 난이도 `revives`), 방장 보고의 멤버별 `revives_used`와 대조 | 부활 풀은 멤버별(현재 코드와 같다). `DungeonDatabase.RaidRevives` 주석은 "파티 공유"라고 했지만 코드는 로컬 플레이어만 센다(6절) |
| `ExitToVillage`, `AbortRun` -> `OnlineEconomy.LeaveDungeon()` | `RunId = null` | 판 도중 나가면 POST `/party-runs/{id}/leave`, 판이 끝난 뒤 나가면 그대로 | 이탈은 클리어 보상을 포기하는 것이다. `AbortRun`(타이틀로)은 먼저 R8 호출 시도 |
| `Retry()` | 같은 던전 재입장 | 솔로: `POST /dungeon-runs`. 파티: 파티 로비로 돌아가 `start` 다시 | 파티 판은 "다시 도전"을 방장만 누를 수 있다(방장이 `start`) |
| `OnlineEconomy.RunId`, `RoomIndex` (정적) | 내 판 하나 | 내 `dungeon_runs.uuid`(R2/begin 응답 `me.run_id`). 처치 보고의 `run_id`는 항상 **내** 행 | 파티 판이어도 클라이언트당 하나라 구조 변경 없음 |
| `DungeonRun.MemberDamage`/`SnapshotDamage` (표시용 딜 미터) | 로컬 `PartyManager.DamageDealt` | 방장 보고 `members[].damage_dealt`, `ai[].damage_dealt`(화력 대조용 사실) | 결과 화면의 딜 표시는 방장이 P2P로 보낸 값을 쓴다(서버는 표시에 관여하지 않음) |

### 1.3 파티·AI 용병 (`Party/PartyManager.cs`, `Party/MercenaryDatabase.cs`, `Quest/StoryCast.cs`)

| C# 동작 | 지금 | 4단계 | 비고 |
|---|---|---|---|
| `PartyManager.MaxMembers = 4`, `MaxCompanions = 3` | 로컬 + AI 용병 최대 3 | 사람 + `ai_count` <= 4. `parties.max_members` 2..4(사람), `party_runs.humans + ai_count <= 4`(CHECK) | |
| `AddCompanion`/`RemoveCompanion` (`Game.Session.PartyRoster`, 저장 데이터) | 영입한 용병이 세이브에 남는다 | 온라인 판의 AI 구성은 방장이 판 시작 때 정하는 일회성 연출 정보. `PartyRoster`를 쓰지 않는다 | 서버는 `ai_count`만 저장. `EnterOnline`의 `PartyRoster.Count > 0` 차단은 AI 수를 `ai_count`로 보내는 방식으로 대체 |
| `MercenaryDatabase.DamageScale = 0.65`, `CreateData(def, level)`(레벨 = 로컬 플레이어 레벨) | 용병 딜 = 같은 레벨 플레이어의 65% | `power_cap`의 AI 몫(`ai_count x damageScale x 최대 attackCap`) | `dungeons.json`에 `mercenary.damageScale`을 내보낸다(4절) |
| `DungeonDatabase.PartyHpScale = {1.0, 1.7, 2.4, 3.0}`, `PartyScale(size)` | 파티 인원(AI 포함)별 몬스터 HP 배율 | `dungeons.json.partyHpScale`(3단계에서 이미 내보냄). 서버는 `party_size = humans + ai_count` | 방장 PC가 `run.HpMul`로 몬스터를 만들고 서버는 같은 식으로 화력 상한을 계산 |
| `StoryCompanions.Refresh(outsideTown)`: 카엘이 던전·레이드에 따라옴 | 로컬 파티 멤버 1명 | AI 1명으로 센다(`ai_count`에 포함, 합 4 이하) | 방장 PC에서만 소환. 비방장 PC에서는 `Refresh`를 건너뛴다 |
| `PartyManager.Leader => Local` | 로컬 플레이어가 항상 방장 | "방장(로비)"과 "호스트(판의 몬스터 계산 PC)"를 구분한다. 서버의 `party_runs.host_character_id`가 호스트 | `Leader`를 서버 상태를 반영하도록 바꾸거나 `Host` 속성을 새로 둔다 |
| `ThreatTable`, 어그로 | 로컬 | 방장 PC가 계산 | 서버 무관 |
| `ActorId.Account(accountId, characterId, slot)`, `ActorIds.Assign` | 값 형식만 정의, `long account/character` | `slot`은 `party_run_members.slot`/`dungeon_runs.slot`. 계정·캐릭터 식별은 서버가 uuid를 쓰므로 판 안의 네트워크 식별은 **slot만** 쓴다 | `long` 필드는 쓰지 않는다(서버 외부 id가 uuid라 long이 없다). `ActorId`는 slot 중심으로 정리 |
| `NetCommand.Pack/Unpack`, `NetCommandRouter.Register(slot, NetworkInput)` | slot 한 바이트 + 9바이트 명령 | 그대로(멤버 -> 방장). 서버 무관 | PLAN_ONLINE §3.4의 "12바이트"는 실제 `Size = 9`와 다르다(문서 오차) |

### 1.4 레이드 (`Dungeon/DungeonProgress.cs`, `DungeonDirector.cs`, `DungeonDatabase.cs`)

| C# 동작 | 지금 | 서버 판정 / 테이블 | 비고 |
|---|---|---|---|
| `DungeonProgress.RaidRewardAvailable(raid, now)` | `raidClaims[raid.id]` 스탬프가 일일(중간)/주간(최종) 구간보다 오래됐는가 | `raid_claims`에 이번 기간 행이 없는가. 기간은 서버 `resetBoundaries`의 `dailyStartAt`(Mid)/`weeklyStartAt`(Final) | 로컬 시계를 쓰지 않는다 |
| `ClaimRaid(raid, now)` | 클리어 때 스탬프 기록 | `raid_claims` INSERT(PK로 한 번) | 경험치·카드보다 먼저 INSERT(phase4_api 10.4) |
| `RaidClearsThisWeek(raid, now)` ("이번 주 n/3") | 마지막 청구일까지의 개방 요일 수로 추정 | 같은 주간 구간의 `raid_claims` 행 수 | 정확한 값을 GET `/raids`가 준다 |
| `SaveData.raidClaimedStamp`, `raidClaims` (`RaidClaimSave`) | 세이브에 저장 | 온라인은 사용하지 않는다(서버 `raid_claims`가 정본) | 오프라인 세이브는 그대로 |
| `DungeonDirector.RaidLockReason(raid)` (`unlockQuest` 수락 이상) | 로컬 퀘스트 상태 | 서버 `RAID_LOCKED`: `quest_claims`에 해금 퀘스트, 또는 선행(`requires`) 전부 청구 + 클라이언트 저장 상태 수락 | phase4_api 10.2 |
| 최종 레이드 `keyCost` 확인 (`CannotEnterReason`) | 가방 `key_seal` 수 | 입장은 막지 않고 `lockAtEntry`·정산에서 `KEYS_MISSING` 보상 잠금 | 열쇠가 모자라면 연습 입장(보상 없음, 퀘스트 인정) |
| `PayRaidKeys`: 최종은 `min(cost, have)` 소모, 중간은 `keyMin..keyMax` 지급 | 로컬 `Random` | `item_ledger('raid_key_cost' / 'raid_key')`, 서버 RNG | 부족하면 서버는 소모 대신 보상 잠금(`KEYS_MISSING`) |
| `RunStart`의 `RewardsLocked`(연습 입장) | 클리어 경험치·카드·열쇠·청구 없음, 처치 경험치·드롭은 지급 | `dungeon_runs.reward_locked/lock_reason`. 처치 보상은 `RAID_PRACTICE_PAYS_KILLS`(기본 지급 안 함) | **게임 동작 변경**(결정 대기 3) |
| `ResetClock.IsOpen(dungeon, now)`: 레이드는 `openDays`만, 요일 던전은 토·일 전체 개방 | 로컬 | 서버 `isOpenToday`는 모든 던전에 토·일 전체 개방을 적용(3단계). **레이드 분기를 추가해야 한다** | 5절 서버 변경 목록 |
| `RaidDifficulty`/`raidNumbers`(레이드별 HP·피해·몬스터 레벨·부활·권장 레벨) | 코드 | `dungeons.json`에 레이드별 `raidNumbers`를 내보낸다(4절) | |

### 1.5 인증·연결 (`Net/OnlineSession.cs`, `Net/ApiClient.cs`, `Net/UdpTransport.cs`, `Net/NetPresence.cs`)

| C# 동작 | 지금 | 4단계 |
|---|---|---|
| `OnlineSession.Login(loginId, password, register)` | dev 아이디 로그인만 | Steam 경로 추가: `LoginSteam()` = Steamworks 초기화 -> `GetAuthTicketForWebApi(identity)` -> POST `/auth/steam`. 개발 중 dev 계정에 Steam 연결은 POST `/auth/steam/link` |
| `ApiClient.Get/Post/Put/Delete` | PATCH 없음 | `Patch(path, body, done)` 추가(PATCH `/party`) |
| `UdpTransport(bool host)`: 포트 47777/47778, 2피어 | 개발 시험용 | `PARTY_TRANSPORT=dev`일 때 그대로 사용. Steam 모드는 `SteamP2PTransport` |
| `NetPresence.Begin(host)`: `-dotrpgNet host/join` 명령줄 | 마을에서 서로 보이는 유령 + 채팅 중계 | 판 안에서는 `PartyRunSession`이 전송을 소유하고 `NetPresence`는 마을 시험용으로 남긴다 |

## 2. 필드 대응 (C# <-> 테이블 <-> API)

### 2.1 모집 글·대기·신청

| C# 필드 | 테이블.컬럼 | API 필드 |
|---|---|---|
| `PartyPost.id` (문자열 "P1") | `parties.uuid` | `PartyPost.id` (uuid) |
| `PartyPost.dungeonId`, `dungeonName` | `parties.dungeon_id` (이름은 클라이언트가 `DungeonDatabase`에서) | `dungeon_id` |
| `PartyPost.difficulty` (`DungeonDifficulty` 0~3) | `parties.difficulty` | `difficulty` |
| `PartyPost.members`, `maxMembers` | `party_members`(left_at NULL) 개수, `parties.max_members` | `members`, `max_members` |
| `PartyPost.minPower` | `parties.min_power` | `min_power` |
| `PartyPost.leaderName/leaderClass/leaderLevel` | `characters.name/class/level`(방장) | `leader.{name,class,level}` |
| `PartyPost.message` (<= 30) | `parties.message` | `message` |
| `PartyPost.mine` | `parties.leader_character_id = 내 캐릭터` | `mine` |
| `PartyPost.applied` | `party_applications`(pending, 내 캐릭터) 존재 | `applied` |
| `MatchQueueState.active/dungeonId/difficulty` | (서버 메모리 티켓) | `queue.{dungeon_id,difficulty}` / `queue = null`이면 비활성 |
| `MatchQueueState.elapsed`, `Remaining` | 티켓 `queuedAt` | `queue.queued_at`, `depart_at`(남은 시간 = `depart_at - 서버시각`) |
| `MatchQueueState.humans` | 티켓 모음 크기 | `queue.humans_waiting` |
| `PartyFinderRules.QueueSeconds` (60) | (정책 상수) | `MATCH_QUEUE_SECONDS` |

### 2.2 판·결과

| C# 필드 | 테이블.컬럼 | API 필드 |
|---|---|---|
| `DungeonRun.PartySize` | `dungeon_runs.party_size = humans + ai_count` | `run.party_size` |
| `DungeonRun.RewardsLocked` | `dungeon_runs.reward_locked`, `lock_reason` | `reward_locked`, `lock_reason` |
| `DungeonRun.Elapsed` | `dungeon_runs.stats.elapsed_ms`(주장), `party_runs.begun_at`(서버 사실) | result `stats.elapsed_ms`, host-report `elapsed_ms` |
| `DungeonRun.HitsTaken`, `MaxCombo`, `RevivesUsed` | `dungeon_runs.stats` | result `stats.*`, host-report `members[].*` |
| `DungeonRun.Kills`, `Monsters` | 서버 사실: `dungeon_runs.room_kills`, 몬스터 총수는 `dungeons.json` | (보내지 않는다) |
| `DungeonRun.MemberDamage` | `party_run_host_reports.members[].damage_dealt`, `.ai[].damage_dealt` | host-report |
| `DungeonRun.Rank`, `Score`, `XpGained` | `dungeon_runs.rank/score/xp_granted`(서버 계산) | settle 응답 `rank`, `score`, `granted_xp` |
| `DungeonRun.Cards` | `dungeon_runs.cards`(선택 전 비공개) | settle `card_count`, 선택 응답 `cards` |
| `DungeonRunState` (`Playing/Clearing/Cleared/Failed`) | `dungeon_runs.state` (`playing/reported/cleared/failed/abandoned/held`) | `state`. `Clearing`은 클라이언트 연출 단계라 서버 상태가 아니다 |
| `GameSession.PartyRoster` | (사용 안 함) | `ai_count` |

### 2.3 레이드

| C# 필드 | 테이블.컬럼 | API 필드 |
|---|---|---|
| `DungeonDef.isRaid`, `raidTier`, `openDays`, `unlockQuest`, `keyCost`, `keyMin/keyMax`, `maxParty` | `dungeons.json` (`keyMin/keyMax`는 4절 추가) | GET `/raids`의 `tier`, `open_today`, `key.*` |
| `DungeonProgress.raidClaims[raid.id]` (스탬프) | `raid_claims(character_id, dungeon_id, period_kind, period_start)` | `reward_available`, `clears_this_period` |
| `DungeonDatabase.SealKey` (`key_seal`) | `character_items`(bag), `item_ledger.reason = raid_key / raid_key_cost` | `key.have`, settle `raid.key_gain/key_cost` |

## 3. 판 상태 전이 <-> 서버 테이블

| 이벤트 | `parties.state` | `party_runs.state` | `party_run_members.state` | `dungeon_runs.state` |
|---|---|---|---|---|
| `start` | forming -> starting | (생성) gathering | 방장 joined, 나머지 invited | (없음) |
| `join` (각 멤버) | starting | gathering | invited -> joined | (없음) |
| `begin` (자동·수동) | starting -> in_run | gathering -> playing | joined -> playing, 미입장 no_show, 자격 미달 dropped | (생성) playing, 입장 횟수 소모 |
| 하트비트 끊김 12초 | in_run | playing | playing -> disconnected | playing |
| 60초 안에 복귀 | in_run | playing | disconnected -> playing | playing |
| 60초 넘김 / 이탈 | in_run | playing | -> left | playing -> abandoned |
| 결과 보고 `failed` | in_run | playing | -> done | playing -> failed |
| 결과 보고 `cleared` | in_run | playing | -> done | playing -> reported -> (settle) cleared / held |
| 호스트 인계 | in_run | playing, `host_epoch + 1` | (옛 방장 disconnected) | 변화 없음 |
| 모든 멤버 행이 playing을 벗어남 | in_run -> forming | playing -> ended | (done/left) | (cleared/failed/held/abandoned) |
| 마감 지나 아무도 안 들어옴 / 방장 취소 | starting -> forming | gathering -> cancelled | (정리) | (없음) |

`party_runs`를 `ended`로 닫고 `parties`를 `forming`으로 되돌리는 시점은 마지막 멤버 행이 `playing`을 벗어날 때(결과 보고, 이탈, 방치 정리)다. 서버는 이 판정을 각 요청의 트랜잭션 끝에서 한 번 한다.

## 4. 추가로 내보낼 데이터 (`server/data/*.json`에 아직 없는 값)

Unity 에디터 내보내기 메뉴(3단계 4절과 같은 도구)에 더한다. 내보내면 데이터 버전 해시가 바뀐다.

| 파일 | 추가 필드 | C# 출처 | 용도 |
|---|---|---|---|
| `dungeons.json` | 레이드별 `raidNumbers`: `{ recommendedLevel, recommendedPower, hpMul, damageMul, rewardMul, monsterLevel, revives, minGearRarity, ticketWeight }` | `DungeonDef.raidNumbers`(`RaidNumbers(...)`), 없으면 `DungeonDatabase.RaidDifficulty` | 레이드 몬스터 레벨·HP 배율, 보상 배율, 카드 굴림, 입장 레벨 하한 |
| `dungeons.json` | 중간 레이드 `keyMin`, `keyMax` | `DungeonDef.keyMin/keyMax` | 열쇠 획득 굴림 |
| `dungeons.json` | `keyItem: "key_seal"` | `DungeonDatabase.SealKey` | 열쇠 아이템 id |
| `dungeons.json` | `raidMinClearSeconds`(레이드별 실측 최솟값, 없으면 서버가 `0.5 x referenceSeconds`) | 실측 | 레이드 결과의 최소 클리어 시간(요일 던전의 `minClearSeconds`는 난이도별이라 레이드에 쓰지 않는다) |
| `dungeons.json` | `mercenary: { damageScale, maxCompanions }` | `MercenaryDatabase.DamageScale`, `PartyManager.MaxCompanions` | 파티 화력 상한의 AI 몫 |
| `player.json` | `power`: `CharacterStats.Power`(SkillData.cs)의 항 중 서버가 계산할 수 있는 것의 가중치(공격, 체력, 마나, 레벨)와 기본 체력·마나 | `CharacterStats.Power`, `PlayerStats` | `power_estimate`. 패시브 항(블록, 속도, 패시브 공격력)은 서버가 몰라 제외. **장비가 체력·마나를 올리면 그 수치를 `shop.json equipment`에 내보내야 한다(확인 필요)** |
| (운영 파일) `banned_words.json` | 금칙어 목록 | 게임 데이터가 아니라 운영 목록. 5단계 채팅과 공유 | 모집 메시지 필터 |

이미 3단계에서 내보낸 것을 재사용한다: `partyHpScale`, `difficulties[].revives/recommendedLevel/hpMul/rewardMul/...`, `openDays`, `weekendOpensAll`, `referenceSeconds`, `rewards`, `cards`, `ranking`, `unlockQuest`, `keyCost`, `maxParty`, `quest_index.json`의 `requires`, `monsters.json`의 `raid`, `player.json`의 `attackDamage/attackCooldown`.

## 5. 클라이언트가 바꿔야 할 지점 (파일:함수)

경로는 `Assets/Scripts/Runtime/` 아래. "새 파일"은 새로 만든다.

### 5.1 Steam 인증·연결
| 파일 | 변경 |
|---|---|
| `Net/OnlineSession.cs` `Login` | Steam 로그인 분기 추가(`LoginSteam`). 접속 직후 `GET /characters`는 그대로. `ToSaveData`는 변경 없음(`power_estimate`만 별도 보관) |
| 새 파일 `Net/SteamAuth.cs` | Steamworks.NET 초기화(`SteamAPI.Init`), `SteamUser.GetAuthTicketForWebApi(identity)` 콜백 -> 티켓 hex, 실패 처리(Steam 미실행). Steamworks.NET 패키지·asmdef 분리, 심볼 `STEAMWORKS_NET`로 감싸 오프라인 빌드가 깨지지 않게 한다. 개발 시험은 `steam_appid.txt`(480) |
| `Net/ApiClient.cs` | `Patch(...)` 추가 |
| `Net/ApiClient.cs` `DataVersion` | 변경 없음(파티 경로도 데이터 버전 헤더를 쓴다) |

### 5.2 전송 계층
| 파일 | 변경 |
|---|---|
| `Net/Transport.cs` `ITransport` | 다피어 지원: `int PeerCount`, `bool IsPeerConnected(int peer)`, `event Action<int> PeerDisconnected`, `void Close()`. 기존 `IsConnected`(두 피어 전제)는 "방장과의 연결"의 의미로 남긴다. `NetChannel`에 `Snapshot = 4`, `Event = 5`, `Control = 6` 추가 |
| `Net/Transport.cs` `LoopbackTransport.CreatePair` | 별 모양 N피어(`CreateStar(n)`)를 더해 4명 단위 시험 가능하게 한다. 지연·유실 시뮬레이션 옵션 |
| `Net/UdpTransport.cs` | `PARTY_TRANSPORT=dev` 전용으로 유지(포트 47777/47778 2피어). `IsPeerConnected`만 구현 |
| 새 파일 `Net/SteamP2PTransport.cs` | `ISteamNetworkingSockets` P2P(별 모양). 방장: 리슨 소켓 + 연결 요청 시 SteamID를 서버 명단과 대조해 수락/거절, 첫 신뢰 메시지의 `entry_token` HMAC 검증. 멤버: `ConnectP2P(방장 SteamID)`. 끊김 콜백 -> `PeerDisconnected`. `InitRelayNetworkAccess` 호출 |
| `Net/NetPresence.cs` `Begin`, `Update` | 단일 유령 -> 슬롯별 원격 플레이어. 판 중 연결 토스트를 `PeerDisconnected`로 이동. 명령줄 `-dotrpgNet`은 개발 시험으로 남긴다 |

### 5.3 방장(호스트) 권한과 판 세션
| 파일 | 변경 |
|---|---|
| 새 파일 `Net/PartyRunSession.cs` | 판 세션 상태기계: R2 폴링 -> P2P 연결(방장이면 리슨) -> `join` -> `begin` 후 `playing`. 하트비트 5초(`seen_epoch`), `host_changed` 처리, 연결 끊김 3초 -> 인계 요청(`host/claim`), 방장 보고(`host-report`) 조립, 입장 토큰 계산(방장)과 검증 |
| `Dungeon/DungeonDirector.cs` `LoadRoom`, `SpawnMonsters` | 방장 PC에서만 실제 몬스터 생성. 멤버 PC는 스냅샷 기반 puppet 생성(새 컴포넌트, 예: `RemoteEnemy`) |
| `Dungeon/DungeonDirector.cs` `OnDoorEntered`, `Transition` | 방장이 결정하고 `Control` 메시지로 방 전환을 알린다. 멤버는 방장 메시지로 `LoadRoom` |
| `Dungeon/DungeonDirector.cs` `Update`의 `bossAlive`/`AnyAlive` 클리어 판정 | 방장만 판정하고 `Event`로 클리어를 알린다. 멤버는 받은 이벤트로 `ClearRoutine` |
| `Dungeon/DungeonDirector.cs` `ClearRoutine`, `FinishClearedOnline`, `Fail` | `FinishDungeon` 뒤 `pending`이면 `settle` 폴링 루프 추가. 방장은 `host-report`도 보낸다(결과 직전). 15초 대기 한도를 `PARTY_RESULT_WAIT_SECONDS`(90초)까지 늘리고 "다른 파티원 결과 확인 중" 표시 |
| `Dungeon/DungeonDirector.cs` `ExitToVillage`, `AbortRun` | 판 도중이면 `leave` 호출 |
| `Net/OnlineEconomy.cs` `EnterDungeon` | `ai_count` 인자 추가. 파티 판은 `begin` 응답의 `me.run_id`로 `RunId` 설정(새 함수 `SetRun(runId)`) |
| `Net/OnlineEconomy.cs` `FinishDungeon` | 결과 보고 + `SettleDungeon(runId, done)` 폴링 추가 |
| `Net/OnlineEconomy.cs` `ReportKill` | 호출 지점이 방장 `EnemyController.DropLoot`뿐이라는 가정을 바꾼다. 멤버 puppet의 사망도 호출 |
| `Enemies/EnemyController.cs` `DropLoot`(online 분기) | puppet은 `EnemyController`가 아니므로 puppet 사망 처리에서 `OnlineEconomy.ReportKill(def.id, hits, ...)` 호출. 방장은 현재 그대로 |
| `Enemies/MonsterBehaviours.cs` `GoldRunnerBehaviour.GoldSpilled` | 방장이 센 값을 사망 이벤트로 멤버에게 전달 |
| `Party/PartyManager.cs` `Leader`, `OnEnemyKilled`, `OnLocalSpawned` | `Leader`와 `Host` 구분. 원격 멤버(사람) 슬롯은 `CompanionBrain` 대신 `NetworkInput`을 쓰는 `PlayerController`(이미 구조가 있다: `IActorInput`). 사람 멤버 레벨은 각자 값(`SyncCompanionLevels`는 AI에만) |
| `Party/PartyManager.cs` `AddCompanion`/`Spawn` | 온라인 판에서는 `PartyRoster` 대신 방장이 고른 AI 목록(최대 `4 - 사람 수`)을 `PartyRunSession`에서 받는다 |
| `Quest/StoryCast.cs` `StoryCompanions.Refresh` | 온라인 파티 판에서는 방장 PC만 소환, `ai_count`에 포함 |
| `Dungeon/DungeonDirector.cs` `CannotEnterReason` | 온라인이면 레이드 해금·열쇠·보상 가능 여부를 GET `/raids`와 GET `/dungeons`로 대체(로컬 `Progress`·`ResetClock` 호출 제거). 오프라인은 그대로 |
| `Dungeon/DungeonDirector.cs` `EnterOnline` | 레이드 차단·`PartyRoster` 차단 제거, `ai_count` 전달 |
| `Dungeon/DungeonDirector.cs` `StartRun` | 온라인이면 `run.RewardsLocked`를 서버 값으로, `PartySize`를 서버 값으로 |

### 5.4 파티 찾기·매칭·로비 UI
| 파일 | 변경 |
|---|---|
| `Net/PartyFinderService.cs` `IPartyFinderService` | `Apply`를 비동기 콜백으로(`void Apply(string postId, Action<string> done)`). `List`도 비동기 새로고침 + 캐시. `Queue`/`Tick`은 폴링으로 구동. 목업은 시그니처만 맞춘다 |
| 새 파일 `Net/ServerPartyFinderService.cs` | 위 서비스의 서버 구현(GET `/parties`, POST `/parties`, `/party` 폴링, `/match/*`). 온라인 로그인·캐릭터 입장 직후 `OnlineServices.PartyFinder`에 주입, 로그아웃·타이틀 복귀 때 목업으로 되돌림 |
| `UI/OnlineWindows.cs` `PartyFinderScreen.MyPower` | 온라인이면 `OnlineSession.PowerEstimate` |
| `UI/OnlineWindows.cs` `PartyFinderScreen` (`StartQueue`, `DepartWithAi`, `Apply`, `Create` 호출부) | 비동기 응답 처리, 신청 중/거절/만료 표시, 서버 에러 코드의 한국어 문구 |
| `UI/OnlineWindows.cs` `MatchQueueIndicator.Depart` | 서버가 파티를 만들면 로비 창 열기 + 방장은 `start` 호출. 기존 "AI로 채워 출발"은 `fill-ai` |
| 새 UI `PartyLobbyScreen` (PLAN_ONLINE §2.1-4) | 멤버 목록(직업, 레벨, 전투력, 준비), 방장 위임·강퇴·AI 수 설정·입장, 연결 핑 표시. `PartyView`를 그대로 그린다 |
| `Net/OnlineSession.cs` | `PowerEstimate`(GET `/characters/{uuid}`의 `power_estimate`)를 보관하고 `OnlineEconomy.ApplyDelta`가 레벨·장비 변화 뒤 갱신 필요 시 재조회 |

### 5.5 레이드
| 파일 | 변경 |
|---|---|
| `Dungeon/DungeonDirector.cs` `FinishCleared`(오프라인 경로), `PayRaidKeys` | 오프라인 그대로. 온라인은 서버가 열쇠를 지급·소모하므로 `PayRaidKeys`를 호출하지 않고 `delta.stacks`로 가방을 맞춘다 |
| `UI/DungeonWindow`(던전 선택 창, 파일명 확인 필요) | 온라인이면 GET `/raids`로 해금·이번 주 n/3·열쇠 표시 |
| `Dungeon/DungeonProgress.cs` | 온라인에서는 입장 횟수·레이드 청구·클리어 기록을 서버 응답으로 덮어쓴다(`RecordClear`는 settle 응답으로) |

## 6. 서버 쪽 기존 코드 변경 (3단계 -> 4단계, dotrpg-backend-coder용)

| 파일(`server/src/`) | 변경 |
|---|---|
| `domains/dungeons/dungeonRepository.ts` `countEntries` | `AND counts_entry` 추가. `insertRun`에 새 컬럼(`ai_count`, `humans`, `counts_entry`, `reward_locked`, `lock_reason`, `power_cap`, 파티 판은 `party_run_id`, `slot`). `abandonRun`이 닫는 행의 `party_run_members`를 `done`으로 |
| `domains/dungeons/dungeonService.ts` `processEnter` | `RAID_NOT_AVAILABLE` 제거, `ai_count`, `IN_PARTY_RUN`, `checkEntry` 공용화, 레이드 `counts_entry=false`와 `reward_locked` 계산 |
| `domains/dungeons/dungeonRules.ts` `isOpenToday` | 레이드는 `weekendOpensAll`을 적용하지 않고 `openDays`만 본다 |
| `domains/dungeons/dungeonKillContext.ts` `resolveDungeonTarget` | 파티 판 상태 확인(멤버·판 상태), `hpMul`은 `run.party_size`(고정값), 레이드 난이도 수치(`raidNumbers`), 화력 상한 덮어쓰기(`power_cap x PARTY_POWER_SLACK`), `reward_locked` 전달 |
| `domains/kills/killService.ts`, `killTarget.ts` | `KillTarget`에 `rewardLocked`, `powerCap`, `blockOwner`(차단 카운트를 방장에게만) 추가. `RAID_NOT_AVAILABLE`은 레이드 판 밖에서만 유지. 연습판 지급 건너뛰기 |
| `domains/dungeons/dungeonResult.ts` `settleResult` | 검증부를 순수 함수로 분리하고 `recordResult`(접수) + `settle`(대조·정산)로 나눈다. 솔로는 기존대로 한 번에 |
| 새 `domains/party/` | routes/controller/service/repository/validation: 게시판, 파티, 신청, 준비, 강퇴, 위임 |
| 새 `domains/match/` | `QueueStore`(인터페이스 + 메모리 구현), 맞추기 1초 틱, 대기열 API |
| 새 `domains/partyruns/` | 출발, 입장 확인, begin, 하트비트, 호스트 인계, 방장 보고, 이탈, 대조 순수 함수(`reconcile`), 입장 토큰 HMAC |
| 새 `domains/raids/` | GET `/raids`, 정산 단계(`raid_claims`, 열쇠) |
| 새 `domains/auth/steamProvider.ts` | `mock`/`web_api` 검증기(인터페이스), 티켓 재사용 방지 집합(`QueueStore`처럼 교체 가능) |
| `gamedata/economyData.ts`, `loader.ts` | 4절의 새 필드 파싱(`raidNumbers`, `keyMin/keyMax`, `mercenary`, `power`) |
| `config/env.ts` | phase4_api 15절의 환경변수, Steam 설정 검증(운영에서 `mock` 거부) |
| `utils/clock.ts`, `resetBoundaries.ts` | 변경 없음(`dailyStartAt`, `weeklyStartAt` 재사용) |
| `db/` 도우미 | 여러 캐릭터 id 오름차순 잠금 도우미(`lockCharacters(ids)`) 추가 |

## 7. 원장 reason, 이상 기록 kind

| 원장 | reason | 경로 | `ref` |
|---|---|---|---|
| `item_ledger` | `raid_key` (0005) | 중간 레이드 보상 정산(열쇠 획득) | 판 uuid |
| | `raid_key_cost` (0005) | 최종 레이드 보상 정산(열쇠 소모) | 판 uuid |
| `xp_ledger` | `dungeon_clear` (3단계) | 파티 판 정산(멤버별) | 판 uuid |
| `item_ledger`/`gold_ledger` | `dungeon_card` (3단계) | 카드 선택(멤버별) | 판 uuid |

| `anomaly_log.kind` | 언제 | severity |
|---|---|---|
| `party_result` | 결과 대조 불일치(MISMATCH) | 2 |
| `party_host` | 무효 방장 보고, 방장이 이상치로 판정됨 | 3 |
| `raid_enter` | 클라이언트 UI가 막은 레이드 해금·열쇠를 우회한 입장 시도 | 2 |

## 8. 에러 코드 대응 (클라이언트 동작)

| code | 클라이언트 동작 |
|---|---|
| `ALREADY_IN_PARTY`, `NOT_IN_PARTY` | 파티 상태를 GET `/party`로 다시 받는다 |
| `PARTY_NOT_FOUND`, `PARTY_FULL`, `APPLICATION_EXPIRED` | 목록 새로고침, "모집이 끝난 글입니다" / "모집 완료된 파티입니다" / "신청이 거절되었습니다"(기존 문구) |
| `POWER_TOO_LOW` | "전투력이 부족합니다 (최소 N)"(`errors.need`) |
| `LEVEL_TOO_LOW`, `DIFFICULTY_LOCKED`, `DUNGEON_CLOSED_TODAY`, `NO_ENTRIES_LEFT`, `RAID_LOCKED`, `KEYS_MISSING` | 기존 `CannotEnterReason` 문구와 같은 표시 |
| `MEMBER_NOT_ELIGIBLE` | 방장에게 `errors.members`의 이름과 사유 표시, 출발 취소 |
| `NOT_ALL_READY` | "전원이 준비해야 한다" |
| `HOST_MISMATCH`, `ENTRY_TOKEN_INVALID` | 판 입장 실패. 로비로 돌아간다 |
| `HOST_CHANGED` | `errors.host`의 새 방장에 연결 |
| `HOST_ALIVE`, `NOT_NEXT_HOST` | 인계 시도를 멈추고 방장 재연결을 기다리거나 이탈 |
| `HOST_EPOCH_STALE` | 이 PC는 더 이상 호스트가 아니다: 즉시 권한을 내려놓고 멤버로 동작 |
| `RUN_NOT_PLAYING` | 처치·결과 보고 중단, 판 상태를 `GET /party-runs/{id}`로 확인 |
| `pending`(settle 결과) | `settle_after_ms` 뒤 재호출, 대기 표시 |
| `held`(settle 결과) | "결과를 확인하는 중이다. 보상은 확인 후 지급된다."(기존 문구) |
| `STEAM_TICKET_INVALID`, `TICKET_REPLAYED`, `STEAM_UNAVAILABLE` | 새 티켓을 받아 재시도, 안 되면 "Steam 연결을 확인해 주세요" |
| `STEAM_REQUIRED` | 파티 멤버 중 Steam 연결이 없는 사람이 있다 |

## 9. 알려진 불일치와 확인이 필요한 사항

| 항목 | 내용 | 조치 |
|---|---|---|
| `RaidRevives` 주석 | `DungeonDatabase.RaidRevives`의 주석은 "레이드 부활(파티 공유)"이지만 `DungeonRun.RevivesUsed`는 로컬 플레이어의 부활만 센다(용병은 코인을 쓰지 않는다) | 서버는 멤버별 풀로 설계(`revives_used <= 난이도 revives`). 파티 공유로 바꾸려면 방장 보고의 합으로 검증을 바꾼다 |
| 연습 입장의 처치 보상 | 오프라인 레이드 연습(`RewardsLocked`)은 처치 경험치·드롭을 준다 | 온라인은 기본 지급 안 함(결정 대기 3) |
| 레이드 개방 요일 | `ResetClock.IsOpen`: 레이드는 `openDays`만. 3단계 서버 `isOpenToday`는 모든 던전에 토·일 전체 개방 | 서버에 레이드 분기 추가(6절) |
| 최종 레이드 열쇠 소모 | C#은 `min(cost, have)`만큼 소모하고 보상을 준다. 서버는 부족하면 보상을 잠근다 | 정직한 흐름(입장 때 충분)에서는 같다 |
| `RaidClearsThisWeek` | C#은 마지막 청구일만 기억해 개방 요일 수로 추정 | 서버는 `raid_claims` 행 수로 정확히 센다 |
| `NetCommand` 크기 | PLAN_ONLINE §3.4는 12바이트, 코드의 `Size`는 9 | 문서 오차(코드가 맞다). 대역폭 계산은 9바이트 기준 |
| `ITransport`가 두 피어 전제 | `IsConnected` 하나, `LoopbackTransport.CreatePair`, `UdpTransport` 포트 2개 | 5.2의 다피어 확장 |
| PLAN_ONLINE §5.2 표와 이 설계의 차이 | `party_posts`+`party_members`+`match_tickets`+`run_instances` -> `parties`+`party_members`+`party_applications`+`party_runs`+`party_run_members`+`party_run_host_reports`, 대기열은 메모리 | 이유: 모집 글과 매칭 결과를 한 표로, 인계·대조에 필요한 멤버 상태를 따로 |
| PLAN_ONLINE §5.3 API와 차이 | `POST /parties/{id}/accept/{charId}`, `/runs/{token}/kills`, `/runs/{token}/result` 등 | 이 설계는 모두 `/characters/{uuid}/...` 아래이고 처치·결과·카드는 3단계의 `dungeon-runs`를 재사용한다. `instanceToken`은 판 uuid + 멤버별 HMAC 토큰이며 Steam 로비 id는 쓰지 않는다 |
| PLAN_SERVER §6 WebSocket | 4단계에 WebSocket을 쓴다고 했다 | 4단계는 폴링으로 한다(5단계 채팅과 함께 WebSocket 도입). 응답 모양을 이벤트에 그대로 쓸 수 있다 |
| 4명 실제 시험 환경 | Steam 모드 시험은 PC 2대 이상, 서로 다른 Steam 계정 | 4명은 `LoopbackTransport` 별 모양 + 서버 통합 테스트(시나리오 스크립트)로 시험 |
