# 서버 1~2단계 게임 값 대응표

게임 쪽 정의(C# 필드·json 키) ↔ 서버 테이블·컬럼 ↔ API. API는 [phase1_2_api.md](phase1_2_api.md), 스키마는 `server/schema.sql`.
값(수치·목록)은 여기 복사하지 않고 `server/data/*.json` 이름으로만 참조한다.

## 1. SaveData 필드별 처리 (오프라인 세이브 `SaveData` 기준)

| SaveData / 게임 필드 | 온라인에서 | 테이블·컬럼 | API |
|---|---|---|---|
| `playerClass` | 생성 시 한 번 정함, 바꾸지 않음 | `characters.class` | POST /characters |
| `playerName`(QuestJournal.PlayerName) | 캐릭터 이름과 같은 것으로 취급. 이야기 속 호칭의 기본값(`DefaultName`)은 클라이언트가 이름 대신 쓰지 않는다 | `characters.name` | POST /characters |
| `mapId` | 저장(비-던전 맵만) | `character_state.map_id` | PUT state |
| `playerX`, `playerY` | 저장. 오프라인의 `-1` 같은 "맵 시작 지점" 표식은 `pos: null`로 보낸다 | `character_state.pos_x/pos_y` (NULL 쌍) | PUT state |
| `playerFacing` | 저장 | `character_state.facing` | PUT state |
| `quests` (QuestSave: id, status, step, counts) | 저장(형식·퇴보 검사) | `character_state.quests` (jsonb) | PUT state |
| `storyFlags` | 저장(추가만) | `character_state.story_flags` | PUT state |
| `trackedQuest` | 저장 | `character_state.tracked_quest` | PUT state |
| `passives` | 저장(레벨 대비 개수·연결 검사) | `character_state.passives` (시작 노드 S 제외) | PUT state |
| `gemSlots` (슬롯 5 x [액티브, 보조1, 보조2] 평탄 배열) | 보조 칸만 `{slot, supports[2]}` 형태로 변환해 저장. 액티브 칸(socket 0)은 클래스·슬롯으로 정해져 있어 보내지 않는다 | `character_state.skill_gems` (jsonb) | PUT state |
| `level`, `xp` | **클라이언트가 바꿀 수 없음.** 3단계 서버 판정 전용. 응답으로만 받는다 | `characters.level`, `characters.xp` | GET (읽기만) |
| 골드(`StarterPack`의 Gold 항목, 인벤토리의 골드) | **클라이언트가 바꿀 수 없음.** 시작 지급만 원장과 함께 | `characters.gold`, `gold_ledger` | GET (읽기만) |
| `inventory`, `storage`, `equipped` | 시작 지급만 서버가 만든다. 이동·사용·장착은 3단계 | `character_items`, `item_ledger` | GET (읽기만) |
| `enhancePity` | 3단계(강화 판정) | (2단계에 없음) | - |
| `openedChests` | 상자 보상이 걸려 있어 3단계(퀘스트·상자 보상과 함께) | (2단계에 없음) | - |
| `partyMercs`(AI 용병 명단) | 4단계(파티) 설계 때 결정 | (2단계에 없음) | - |
| `dungeonDailyStamp`, `dungeonEntriesUsed`, `raidClaimedStamp`, `dungeonBestRanks`, `dungeonCleared`, `raidClaims` | 던전·레이드 입장·보상 판정은 서버가 따로 관리(4단계) | (2단계에 없음) | - |
| `playerHealth`, `playerMaxHealth` | 저장하지 않음. 접속 시 클라이언트가 최대값으로 시작한다(HP는 재화가 아니고 어뷰징 이점이 없다) | - | - |
| `playTimeSeconds`, `savedAtUtc` | 저장하지 않음. 서버의 `updated_at`이 대신한다 | `character_state.updated_at` | - |
| `quest`(구 QuestProgress), `enhanceLevels`, `version` | 오프라인 세이브 마이그레이션용 필드. 온라인에서 쓰지 않는다 | - | - |

오프라인 세이브는 온라인 캐릭터로 가져오지 않는다(PLAN_SERVER §2).

## 2. 시작 지급 (C# 원본과 서버 대응)

| C# 원본 | 서버 처리 |
|---|---|
| `Assets/Scripts/Runtime/Items/ConsumableDatabase.cs` `StarterPack` (골드 + 소모품 목록) | `starter.json`의 `gold`와 `items`. 골드는 `characters.gold` + `gold_ledger`, 소모품은 `character_items`(bag) + `item_ledger` |
| `Assets/Scripts/Runtime/Items/EquipmentDatabase.cs` `StarterWeapon(cls)` | `starter.json`의 `gear[class]`. `character_items`(worn) + `item_ledger` |
| `Assets/Scripts/Runtime/Core/GameSession.cs` `ResetForNewGame` (StarterPack 사용처, 시작 맵 `MapRegistry.Village`) | `starter.json`의 `startMap` → `character_state.map_id`. 퀘스트·플래그·패시브는 빈 값으로 시작 |
| `Assets/Scripts/Runtime/Save/SaveSystem.cs` Migrate의 `StarterPack` (v3 이전 세이브 보정) | 온라인에서 쓰지 않음 |

- C# `Progression.Reset`은 패시브에 시작 노드만 넣는다 → 서버는 `passives = {}`(시작 노드 암묵). 젬 장착은 빈 상태.
- 시작 장비의 `item_key`는 +0 장비의 기본 id 형태다(C# `EquipmentDatabase.KeyFor`가 +0을 기본 id로 돌려준다). 강화 키 규칙(`id+N`, 최대 강화)은 3단계에서 `items.json`에 더한다.

## 3. 검증 규칙 ↔ C# 원본

| 서버 검증 | C# 원본 | 데이터 |
|---|---|---|
| 패시브 개수 <= 레벨-1 (시작 노드 무료) | `Progression.PointsLeft` = `(Level-1) - Math.Max(0, Allocated.Count-1)` | `characters.level` (서버 값) |
| 패시브 노드 존재·시작 노드에서 연결 | `Progression.Restore`(`PassiveTree.Get`, `AllConnected`), `PassiveTree.Start` | `passive_tree.json` |
| 슬롯 개방 레벨 | `Progression.IsSlotOpen`, `SkillGems.SlotLevels`, `SkillGems.Slots` | `skill_gems.json` `slotLevels`, `slots` |
| 보조 젬 해금·종류·클래스·슬롯 내 중복 | `Progression.IsUnlocked`(`gem.UsableBy(cls)`, `unlockLevel`), `OptionsFor`, `Restore`의 `GemKind.Support` 검사, `SkillGems.SupportsPerSlot` | `skill_gems.json` `gems[]`, `supportsPerSlot` |
| 맵 id 존재, 던전 방 제외 | `MapRegistry.Get`, `MapRegistry.All`, `MapInfo.instanced` | `maps.json` |
| facing 범위 | `Facing` enum | `enums.json` `facingCount` |
| 퀘스트 id·status·step·counts | `QuestSave`, `QuestStatus`, `QuestDatabase`(`Quests.json`) | `quest_index.json`, `enums.json` `questStatusMax` |

## 4. Unity에서 내보내야 할 데이터 (`server/data/*.json`)

내보내기 도구(Unity 에디터 메뉴)는 메인이 Unity 쪽에 만든다. 각 파일은 원본 C#·json이 바뀔 때 다시 내보내고, 서버는 시작할 때 읽어 **스키마 검증(zod)에 실패하면 기동하지 않는다.** 파일 최상위에는 모두 `"schema": 1`을 둔다.

### 4.1 `data_version.json`
| 필드 | 설명 |
|---|---|
| `version` | string. 아래 모든 데이터 파일(이 파일 제외) 내용을 정렬된 이름 순으로 이어 붙인 SHA-256 hex의 앞 16자. 같은 내용이면 항상 같은 값 |
| `files` | `{ "<파일명>": "<그 파일의 SHA-256 hex>" }` (디버깅용) |
| `exportedAt` | UTC ISO 문자열 |

- 클라이언트는 같은 내보내기 결과의 `version`을 `X-Data-Version`으로 보낸다(빌드에 포함).

### 4.2 `maps.json`
출처: `MapRegistry.All`과 던전 방(`MapRegistry.Dungeon.cs`의 `DungeonRooms`, `MapRegistry.Get`으로 얻는 것) 전체.
| 필드 | 설명 |
|---|---|
| `maps[].id` | string, 맵 id (예: `MapInfo.id`) |
| `maps[].instanced` | bool, `MapInfo.instanced` |
| `maps[].safe` | bool, `MapInfo.safe` (3단계 처치·채집 판정에서 사용) |
| `maps[].bounds` | (선택) `{ minX, minY, maxX, maxY }` 월드 좌표. 맵 레이아웃에서 계산 가능하면 내보낸다. 없으면 서버는 좌표 범위 검사를 건너뛴다 |

### 4.3 `starter.json`
| 필드 | 출처 | 설명 |
|---|---|---|
| `startMap` | `MapRegistry.Village` | string |
| `gold` | `StarterPack`에서 골드 항목의 수량 | int. 골드는 `items`와 분리해서 낸다 |
| `items[]` | `StarterPack`에서 골드를 뺀 나머지 | `{ itemKey: string, count: int }` |
| `gear.warrior`, `gear.mage` | `StarterWeapon(cls)` | `{ itemKey: string, slot: int }`, `slot`은 `EquipSlot.Weapon`의 번호 |

### 4.4 `items.json`
출처: `ConsumableDatabase`(소모품·재료), `EquipmentDatabase`(장비). 2단계 서버는 시작 지급 id의 존재 검증에만 쓴다. 3단계가 가격·강화 등 필드를 더한다.
| 필드 | 설명 |
|---|---|
| `items[].id` | string, 아이템 기본 id (장비는 `+N` 없는 id) |
| `items[].kind` | string: `consumable` / `material` / `equipment` 등 (원본 분류를 그대로) |
| `items[].stackable` | bool |

### 4.5 `passive_tree.json`
출처: `PassiveTree`(`Progression/SkillData.cs`).
| 필드 | 설명 |
|---|---|
| `start` | string, `PassiveTree.Start` |
| `nodes[].id` | string |
| `nodes[].kind` | string, `PassiveKind` 이름 (`Start`/`Small`/`Notable` 등) |
| `nodes[].links[]` | string[], 이웃 노드 id (양방향이 되도록 서버가 로드 시 대칭 검사) |

### 4.6 `skill_gems.json`
출처: `SkillGems`(`Progression/SkillData.cs`) - `UseLegacy=false`일 때의 활성 세트.
| 필드 | 설명 |
|---|---|
| `slots` | int, `SkillGems.Slots` |
| `supportsPerSlot` | int, `SkillGems.SupportsPerSlot` |
| `slotLevels[]` | int[], `SkillGems.SlotLevels` (길이 = `slots`) |
| `gems[].id` | string |
| `gems[].kind` | `"active"` / `"support"` (`GemKind`) |
| `gems[].classOnly` | `"warrior"` / `"mage"` / null (`SkillGem.classOnly`) |
| `gems[].unlockLevel` | int |
| `gems[].slot` | int, 액티브만 (그 외 -1) |

### 4.7 `quest_index.json`
출처: `Assets/Resources/Data/Quests.json`을 가공한 색인(서버가 전체 퀘스트 내용은 필요 없다).
| 필드 | 설명 |
|---|---|
| `quests[].id` | string |
| `quests[].stepCount` | int, `steps` 개수 |
| `quests[].maxObjectives` | int, 단계 중 최대 `objectives` 개수 |
| `flags[]` | string[], 퀘스트 `setFlags`, `StoryIds.PrologueFlags`, `QuestManager.WorkshopBuiltFlag`, 컷신·대화가 세우는 플래그 중 수집 가능한 전부 |

### 4.8 `enums.json`
| 필드 | 출처 |
|---|---|
| `facingCount` | `Facing` enum의 값 개수 |
| `questStatusMax` | `QuestStatus` 최댓값(현재 Completed) |

## 5. 에러 코드 대응 (클라이언트 한국어 처리용)

클라이언트 `ApiClient`가 `errors.code`로 분기한다. 문장은 서버 `message`를 그대로 보여 주되, 아래는 화면 동작이다.

| code | 클라이언트 동작 |
|---|---|
| `CLIENT_OUTDATED`, `DATA_OUTDATED` | "업데이트 필요" 화면, 진행 중단 |
| `TOKEN_EXPIRED` | refresh 1회 후 원래 요청 재시도 |
| `REFRESH_*` | 로그인 화면으로 |
| `NAME_TAKEN`, `VALIDATION`(name) | 이름 입력 화면에 사유 표시 |
| `CHARACTER_LIMIT_REACHED` | 슬롯 가득 안내 |
| `VERSION_CONFLICT` | GET으로 서버 상태를 받아 다시 저장 |
| `INVALID_*` (state) | 저장 실패 안내 + 서버 상태로 되돌림(조작 의심 구간) |
