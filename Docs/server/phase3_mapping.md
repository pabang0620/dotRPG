# 서버 3단계 게임 값 대응표 (경제 판정)

게임 쪽 정의(C# 필드·json 키) <-> 서버 테이블·컬럼 <-> API. API는 [phase3_api.md](phase3_api.md), 스키마는 `server/schema.sql`(`0002_economy.sql`, 선택 `0003_dungeon_solo.sql`). 1~2단계 대응표는 [phase1_2_mapping.md](phase1_2_mapping.md).
값(수치·목록)은 여기 복사하지 않고 `server/data/*.json` 이름과 C# 출처로만 참조한다. 이 문서의 "예"로 든 개수·수치는 근거 설명용이다.

## 1. 재화 경로 <-> API <-> 테이블

C# 경로는 2026-10-03 코드 기준. "원장"은 이 경로가 남기는 원장 행이다.

| 재화가 움직이는 경로 (C# 출처) | 온라인 API | 바뀌는 테이블 | 원장 reason |
|---|---|---|---|
| 몬스터 처치 경험치 (`PartyManager.OnEnemyKilled` -> `Progression.AddXp`) | POST `/kills` | `characters.level/xp`, `kill_log`, `kill_stats` | `xp_ledger.kill` |
| 몬스터 드롭 굴림 (`EnemyController.DropLoot` -> `IAuthority.Drop*`) | POST `/kills` 응답의 `drops` | `drops` | (굴림은 원장 없음, 줍기가 원장) |
| 황금 해골·보스 추가 골드 (`EnemyController.MonsterLoot`, `MonsterDef.goldMin/goldMax`) | POST `/kills` | `drops` | 줍기 때 `gold_ledger.drop_claim` |
| 황금 해골 타격당 골드 (`GoldRunnerBehaviour.OnHit`, `goldPerHitMin/Max`) | POST `/kills`의 `hits` | `drops`, `kill_log.hits` | 줍기 때 `gold_ledger.drop_claim` |
| 줍기 (`Pickup.Collect` -> `Inventory.Add`) | POST `/drops/claim` | `drops.claimed_at`, `characters.gold`, `character_items` | `gold_ledger.drop_claim`, `item_ledger.drop_claim` |
| 채집 (`ResourceNode.Deplete`, `CropPlot.Interact`) | POST `/gathers`, GET `/maps/{map_id}/nodes` | `character_node_state`, `character_items` | `item_ledger.gather` |
| 보물상자 (`TreasureChest.Interact`, `SaveData.openedChests`) | POST `/chests/open` | `character_chests`, `character_items` | `item_ledger.chest` |
| 공사장 납품 (`QuestManager.DeliverMaterials`, `ConstructionSite`) | POST `/deliveries` | `site_deliveries`, `character_items` | `item_ledger.delivery` |
| 퀘스트 보상 (`QuestManager.Complete` -> `GiveReward`) | POST `/quests/{id}/claim` | `quest_claims`, `characters`, `character_items` | `xp_ledger.quest_reward`, `gold_ledger.quest_reward`, `item_ledger.quest_reward`, `item_ledger.quest_consume` |
| 퀘스트 최대 체력 보너스 (`GiveReward` -> `PlayerController.AddMaxHealth`) | 같은 청구 응답, GET의 `bonus_max_health` | `quest_claims.reward.max_health` (합계는 계산) | - |
| 상점 구매 (`ServiceScreens.Trade`) | POST `/shop/buy` | `characters.gold`, `character_items` | `gold_ledger.shop_buy`, `item_ledger.shop_buy` |
| 상점 판매 (`ServiceScreens.Sell`) | POST `/shop/sell` | 같음 | `gold_ledger.shop_sell`, `item_ledger.shop_sell` |
| 강화 (`Equipment.TryEnhance`, `Authority.EnhanceRoll`) | POST `/enhance` | `characters.gold`, `character_items`, `character_enhance_pity`, `enhance_log` | `gold_ledger.enhance_cost`, `item_ledger.enhance_cost`, `item_ledger.enhance_result` |
| 물약·주문서·당근 (`PlayerController.UseConsumable`, `TryEatCarrot`, `GameFlow.UseTownScroll`) | POST `/items/use` | `character_items` | `item_ledger.use_item` |
| 창고 (`StorageScreen.Move/DepositMaterials/WithdrawAll`) | POST `/storage/move` | `character_items` | `item_ledger.storage_move` |
| 장착·해제 (`Equipment.Equip/Unequip`) | POST `/equipment/equip`, `/equipment/unequip` | `character_items` | `item_ledger.equip`, `item_ledger.unequip` |
| 던전 입장 횟수 (`DungeonProgress.UseEntry`) (제안) | POST `/dungeon-runs` | `dungeon_runs` | - |
| 던전 클리어 경험치 (`DungeonDirector.FinishCleared`) (제안) | POST `/dungeon-runs/{id}/result` | `dungeon_runs`, `characters` | `xp_ledger.dungeon_clear` |
| 던전 카드 (`DungeonDirector.TakeCard`) (제안) | POST `/dungeon-runs/{id}/cards/pick` | `dungeon_runs`, `characters.gold`, `character_items` | `gold_ledger.dungeon_card`, `item_ledger.dungeon_card` |
| 레이드 열쇠·주간 보상 (`DungeonDirector.PayRaidKeys`, `ClaimRaid`) | 4단계 | - | - |
| 경매·우편 (`AuctionService`) | 6단계 | - | - |

### IAuthority 멤버가 어디로 가는가
`Net/Authority.cs`의 `IAuthority`는 "클라이언트가 굴림 값을 권한자에게 묻는" 모양이라 서버가 보상을 정하는 온라인에는 그대로 맞지 않는다(서버는 같은 질문에 동기로 답할 수 없다). 온라인 구현은 아래처럼 호출 지점을 서버 요청으로 바꾼다.

| `IAuthority` / 호출 지점 | 온라인 |
|---|---|
| `EnhanceRoll()` | `Equipment.TryEnhance` 전체가 서버로 간다. 클라이언트는 응답의 `outcome/new_key/delta`를 화면에 반영한다 |
| `DropGold`, `DropChance`, `DropCount`, `DropEquipment` | 처치 보고의 응답 `drops`. 클라이언트는 `Pickup`을 드롭 id와 묶어 만들고 줍는 순간 `/drops/claim`을 부른다(여러 개는 0.3초 정도 모아 한 요청) |
| `GrantXp(reason, amount)` | 클라이언트는 경험치를 올리지 않는다. 응답의 `delta.level/xp`로만 바뀐다 |
| `ApplyGold(reason, delta)` | 6단계(경매) |
| `Dungeon.ScoreRun/ClearXp/DealCards` | 9절(제안). `CompanionPick`은 연출이라 클라이언트에 남는다 |

## 2. 규칙 대응 (C# 함수 <-> 서버 규칙 <-> 데이터)

| 서버 규칙 | C# 원본 | 데이터 |
|---|---|---|
| 경험치 지급, 레벨업, 만렙 | `Progression.AddXp`, `XpToNext`, `MaxLevel` (만렙이면 `Xp = 0`) | `progression.json` `maxLevel`, `xpToNext` |
| 몬스터 경험치 = `round(xp * (1 + xpPerLevel * (레벨 - 1)))` | `MonsterDatabase.SpawnDef`의 `xpReward`, `XpPerLevel`(0.1f), `Mathf.RoundToInt`(짝수 쪽 반올림) | `monsters.json`(4절에서 `xpPerLevel` 추가) |
| 던전 몬스터 레벨 | `DungeonMonsters.LevelFor` = `1 + 난이도 monsterLevel + 그룹 levelOffset` | `dungeons.json` `difficulties[].monsterLevel`, `rooms[].groups[].levelOffset` |
| 몬스터 실효 HP | `MonsterDatabase.SpawnDef`: `hp * hpMul * (1 + HpPerLevel * (레벨 - 1))`, `hpMul` = 난이도 `hpMul` x 파티 배율 | `monsters.json` `hpPerLevel`, `dungeons.json` `difficulties[].hpMul`, `partyHpScale` |
| 기본 골드 1더미 `[8, 17)` | `EnemyController.DropLoot`의 `DropGold(8, 17)` | `monsters.json` `fieldGoldMin`, `fieldGoldMaxExclusive` |
| 추가 골드 `[goldMin, goldMax]` 폐구간, 더미 수 `clamp(총액/20, 2, 6)` | `MonsterLoot` (`Rng.Next(goldMin, goldMax + 1)`) | `monsters.json` `goldMin/goldMax`, 더미 상수는 4절 |
| 타격당 골드 `[goldPerHitMin, goldPerHitMax]` 폐구간 | `GoldRunnerBehaviour.OnHit` | `monsters.json`(4절에서 `goldPerHitMin/Max` 추가) |
| 재료 드롭: `u <= dropChance`면 `minDrop..maxDrop`(폐구간) | `DropLoot`의 `DropChance`, `DropCount`(`LocalAuthority`는 `!(value > chance)`) | `shop.json` `materials[]` |
| 장비 드롭: 확률 후 `dropWeight` 가중, 직업이 쓸 수 있는 것만, 시작 장비 제외 | `EquipmentDatabase.RollDrop`, `EquipmentDropChance` | `monsters.json` `equipmentDropChance`, `shop.json` `equipment[].dropWeight/classOnly/starter` |
| 상점 구매가, 재고 | `ItemPrices.ShopStock`, `BuyPrice` | `shop.json` `stock[]` |
| 상점 판매가: 장비 `round(기본가 * (1 + 0.25 * 강화단계))`(.5는 올림), 그 밖은 표 | `ItemPrices.SellPrice` | `shop.json` `equipment[].sellPrice`(+0 값), `materials[]`, `sellPrices[]`, 보너스율은 4절 |
| 강화 확률·비용·실패 규칙 | `EnhanceRules.Chance/GoldFor/BoneFor/OreFor/EssenceFor/FailureFor` | `enhance.json` `steps[].levels[]` |
| 강화 천장: 무기 +10/+11 시도, 실패 +1%p, 성공 시 삭제, 상한 100 | `EnhanceRules.HasPity/MaxPity`, `Equipment.TryEnhance` | `enhance.json` `levels[].pity`, `maxPity`는 4절 |
| 파괴 시 보호권 소모, 시작 장비는 보호권을 쓰지 않음 | `EnhanceRules.CostFor`의 `usesTicket`, `Equipment.TryEnhance` | `enhance.json` `levels[].failure`, `shop.json` `equipment[].starter`, `items.json`의 보호권 |
| 장비 키 `id`(+0), `id+N`, 최대 +20 | `EquipmentDatabase.KeyFor/Canonicalize/MaxEnhance` | `enhance.json` `maxEnhance`. 서버는 `Canonicalize`를 하지 않고 정규 키만 받는다(그 밖은 `400`) |
| 장착 슬롯 번호와 카테고리 | `EquipSlot`(Weapon 0, Necklace 1, Ring1 2, Ring2 3, Top 4, Bottom 5), `EquipmentDatabase.Fits`, `Equipment.TargetSlotFor` | `shop.json` `equipment[].category` |
| 장착 직업 제한 | `EquipmentItem.UsableBy` | `shop.json` `equipment[].classOnly` |
| 사용 가능 아이템 | `ConsumableDatabase.IsUsable`(HealHp/HealMp/TownScroll), `PlayerController.TryEatCarrot` | `gameconfig.json` `usableItems`(4절) |
| 채집 지급·재생 시간 | `ResourceNode`(`treeWoodDrop`, `rockStoneDrop`, 재생 시간), `CropPlot`(당근 1개, 재생 시간) | `gameconfig.json` `nodeKinds`(4절) |
| 상자 보상 | `TreasureChest.Interact`의 고정 보상 | `gameconfig.json` `chestReward`(4절) |
| 납품 필요량 | `QuestConfig.asset` `requiredWood/requiredStone`, `QuestManager.WorkshopQuest` | `gameconfig.json` `deliverySites`(4절) |
| 퀘스트 보상·선행·목표 | `QuestRewardDef`, `QuestDef.requires/minLevel`, `ObjectiveDef` | `quest_index.json`(4절 확장) |
| 창고 칸 수 | `StorageScreen.Capacity` | `gameconfig.json` `storageCapacity`(4절) |
| 던전 입장 횟수, 요일, 해금 (제안) | `DungeonDatabase.DailyEntries`, `ResetClock.IsOpen`, `DungeonProgress.IsUnlocked/UseEntry` | `dungeons.json` `dailyEntries`, `openDays`(4절) |
| 클리어 경험치, 랭크 (제안) | `DungeonRewards.ClearXp`, `DungeonRanking.Score/RankOf/XpBonusPercent` | `dungeons.json` `clearXp`, `xpMul`, `difficulties[].rewardMul`, `ranking`(4절) |
| 카드 굴림 (제안) | `DungeonRewards.RollCards/Resolve/RollGear` | `dungeons.json` `dungeons[].rewards`, `cards`(4절) |

## 3. SaveData 필드의 3단계 처리 (1~2단계 표 `phase1_2_mapping.md` 1절의 "3단계" 항목 확정)

| SaveData 필드 | 온라인에서 | 테이블 | API |
|---|---|---|---|
| `inventory` | 서버가 정본. 가방 스택 | `character_items`(`bag`) | GET 상세, 전 경로 |
| `storage` | 서버가 정본 | `character_items`(`storage`) | `/storage/move` |
| `equipped` | 서버가 정본. 슬롯별 한 행 | `character_items`(`worn`, `slot`) | `/equipment/*`, `/enhance` |
| `enhancePity` | 서버가 정본 | `character_enhance_pity` | `/enhance`, GET의 `enhance_pity` |
| `openedChests` | 서버가 정본 | `character_chests` | `/chests/open`, GET의 `opened_chests` |
| `quest.woodDelivered/stoneDelivered/workshopBuilt` (구 `QuestProgress`) | **온라인에서도 쓴다**(1~2단계 표는 "쓰지 않는다"고 했으나 `ConstructionSite`가 아직 이 필드를 읽는다). 납품 수량은 서버가 정본, `workshopBuilt`는 플래그 `workshop_built`로만 | `site_deliveries`, 플래그는 `character_state.story_flags` | `/deliveries` |
| `quest.skeletonsDefeated` | 쓰지 않는다(퀘스트 목표는 `quests[].counts`) | - | - |
| `quests`, `storyFlags`, `trackedQuest` | 2단계 그대로 클라이언트 저장. 보상 지급의 근거로 쓰지 않는다 | `character_state` | `PUT /state` |
| `level`, `xp` | 서버 판정만 바꾼다 | `characters` | 응답 `delta` |
| 골드(인벤토리의 `gold`) | 서버 잔액. 가방 아이템이 아니라 `characters.gold` | `characters.gold`, `gold_ledger` | 응답 `delta.gold` |
| `dungeonDailyStamp`, `dungeonEntriesUsed`, `dungeonBestRanks`, `dungeonCleared` | (제안 채택 시) `dungeon_runs`에서 계산 | `dungeon_runs` | `/dungeons` |
| `raidClaimedStamp`, `raidClaims` | 4단계 | - | - |
| `partyMercs` | 4단계 | - | - |

## 4. 추가로 내보낼 데이터 (`server/data/*.json`에 아직 없는 값)

서버 판정에 필요한데 현재 json에 없는 값이다. 내보내기 도구(`Assets/Scripts/Editor/GameDataExport.cs`)에 더하는 일은 Unity 쪽 작업이다. 새로 쓰는 파일은 `"schema": 1`을 최상위에 둔다. 서버는 시작 시 zod로 검증해 실패하면 기동하지 않는다.

### 4.1 `monsters.json` 확장
| 필드 | 출처 | 설명 |
|---|---|---|
| `monsters[]`에 `skeleton` 행 추가: `id`, `name`, `kind: "Melee"`, `hp`, `damage`, `xp`, `noLoot: false`, `boss: false`, `raid: false`, `goldMin/goldMax: 0`, `respawnSeconds`, `respawnMinPlayerDistance` | `Assets/Resources/Data/SkeletonStats.asset`(`EnemyStats`: `enemyId`, `maxHealth`, `attackDamage`, `xpReward`(자산에 없어 `EnemyStats.cs` 기본값), `respawnDelay`, `respawnMinPlayerDistance`) | **필드 해골은 `MonsterDatabase`가 아니라 `EnemyStats` 자산이라 현재 `monsters.json`에 없다.** 퀘스트 처치 목표 `skeleton`도 이 id다 |
| `xpPerLevel` | `MonsterDatabase.XpPerLevel`(0.1f) | 몬스터 레벨 경험치 보정 |
| `monsters[].goldPerHitMin`, `goldPerHitMax` | `MonsterDef.goldPerHitMin/Max` | 황금 해골 타격 골드 |
| `loot`: `goldPileDivisor`(20), `goldPileMin`(2), `goldPileMax`(6) | `EnemyController.MonsterLoot`의 `total / 20`, `Mathf.Clamp(.., 2, 6)` | 추가 골드를 더미로 나누는 상수(코드에 박혀 있음) |
| (선택) `xpByLevel`: 몬스터별 레벨 1..30의 계산된 경험치 | `MonsterDatabase.SpawnDef`의 `Mathf.RoundToInt(def.xp * (1f + XpPerLevel * (level - 1)))` | float 계산·반올림을 서버가 다시 만들지 않고 표로 맞춘다(정확히 같은 값 보장) |

### 4.2 `maps.json` 확장 (몬스터가 어느 맵에 나오는지, 노드, 상자)
| 필드 | 출처 | 설명 |
|---|---|---|
| `maps[].fieldSpawns[]`: `{ monsterId, points }` | 맵 텍스트의 `k` 칸(`WorldBuilder.SpawnObject`의 `case 'k'` -> `EnemySpawner.Setup`)을 센 수, 몬스터는 `config.skeletonStats`(= `skeleton`). **현재 `k`가 있는 맵은 `Assets/Resources/Maps/Forest.txt`뿐**(마을·협곡·겨울·던전 방에는 없음, 숲에 15개로 확인) | 처치 그럴듯함의 "그 맵에 이 몬스터가 나오는가"와 리스폰 공급 상한 |
| `maps[].scriptedSpawns[]`: `{ monsterId, total, quest }` | `Assets/Resources/Data/Cutscenes.json`의 `c1_attack`의 `enemies` 명령(`skel_warrior` 3마리 + 2마리, `CutscenePlayer.SpawnEnemies`)이 마을에서 부름. 처치는 퀘스트 `c1_ashes`의 `kill skel_warrior` 목표 | 연출로 나오는 몬스터. 합계 5마리만 허용 |
| `maps[].nodes[]`: `{ id, kind }` (`kind` = `tree` / `rock` / `crop`) | 맵 텍스트의 채집 칸. `WorldBuilder.SpawnObject`와 해상도별 분기(`SpawnTownObject`, `SpawnCanyonHdObject`, `SpawnWinterObject`)의 `ResourceNode.Create`(나무 `T`/`O`/`t`, 바위 `R`), `CropPlot.Create`(`C`). `id` = `"{MapId}:{x}:{y}"` | 채집 판정. **칸 기호 해석이 맵 종류마다 달라 내보내기 도구가 `WorldBuilder`와 같은 규칙을 써야 한다**(맵 텍스트를 따로 파싱하면 어긋난다). x, y는 `WorldBuilder` 격자 좌표로 상자 id와 같은 규칙 |
| `maps[].chests[]`: id 문자열 | `TreasureChest.Create($"{MapId}:{x}:{y}", ...)`(`$` 칸. 숲·협곡·겨울 각 1개씩 보임) | 상자 열기 판정 |

### 4.3 `gameconfig.json` (새 파일)
| 필드 | 출처 | 설명 |
|---|---|---|
| `nodeKinds.tree`: `{ item: "wood", amount, respawnSeconds }` | `GameConfig`의 `treeWoodDrop`, `treeRegrowSeconds` (`Assets/Resources/Data/GameConfig.asset`), `ResourceNode`의 `ItemIds.Wood` | 나무 |
| `nodeKinds.rock`: `{ item: "stone", amount, respawnSeconds }` | `rockStoneDrop`, `rockRespawnSeconds`, `ItemIds.Stone` | 바위 |
| `nodeKinds.crop`: `{ item: "carrot", amount: 1, respawnSeconds }` | `cropRegrowSeconds`, `CropPlot.Interact`의 `Pickup.Create(ItemIds.Carrot, 1, ...)` | 당근밭(수량 1은 코드에 박혀 있음) |
| `usableItems[]`: `{ id, kind }` | `ConsumableDatabase.IsUsable`(`potion_hp`, `potion_mp`, `scroll_town`) + `carrot`(`PlayerController.TryEatCarrot`) | 아이템 사용 허용 목록 |
| `storageCapacity` | `StorageScreen.Capacity` | 창고 칸(종류 수) |
| `chestReward`: `{ itemKey, count }` | `TreasureChest.Interact`의 `const string reward` | 상자 보상(코드에 박혀 있음) |
| `deliverySites.workshop`: `{ questId, requiresQuestClaimed[], items:[{ itemKey, required }] }` | `QuestManager.WorkshopQuest`(`c1_rebuild`), 선행은 `Quests.json`의 `requires`, `Assets/Resources/Data/QuestConfig.asset`의 `requiredWood`, `requiredStone` | 납품처 |

### 4.4 `quest_index.json` 확장 (퀘스트마다)
출처는 `Assets/Resources/Data/Quests.json`(`QuestDef`).
| 필드 | 설명 |
|---|---|
| `quests[].kind`, `requires[]`, `requiresFlags[]`, `minLevel` | 선행·조건 |
| `quests[].reward`: `{ xp, gold, items:[{ id, count }], maxHealth, setFlags[] }` | `QuestRewardDef` 그대로 |
| `quests[].objectives`: 요약 `{ levelNeed, killNeeds:{ monsterId: 합계 }, consumes:[{ itemKey, count }], dungeonNeeds:[{ target, count }], raidNeeds:[{ target, count }], flagNeeds:[], unverifiable:[type...] }` | 전 단계의 목표를 종류별로 합산한 요약. `consumes`는 `collect`이고 `consume=true`인 목표만. 서버가 검증하는 종류와 검증하지 않는 종류(`talk`, `cutscene`, `interact`, `reach`)를 구분한다 |

### 4.5 `enhance.json` 확장
| 필드 | 출처 | 설명 |
|---|---|---|
| `steps[]`에 시작 장비 행 추가(`eq_sword_wood`, `eq_staff_oak`) | `GameDataExport.Enhance`가 `Where(e => !e.starter)`로 뺀다. C#은 시작 장비도 강화할 수 있다(`WindowScreens`가 시작 장비 실패 문구를 따로 가짐) | 시작 장비 강화 허용(11절 결정 (a))을 택할 때 필요 |
| `maxPity`(100), `pityPerFailure`(1) | `EnhanceRules.MaxPity`, `Equipment.TryEnhance`의 `+ 1` | 천장 규칙 상수 |
| `drop3Levels`(3) | `EnhanceRules.DroppedLevel`(`level - 3`) | `Drop3` 실패 시 하락 폭 |
| `ticketItem`(`ticket_protect`), `materials`(`mat_bone`, `mat_ore`, `mat_essence`) | `ConsumableDatabase.ProtectTicket`, `EnhanceRules.Bone/Ore/Essence` | 비용·보호권 아이템 id |
| `rollRange`(100) | `Authority.EnhanceRoll`(0..99) | 성공 판정 `roll < successPercent` |

### 4.6 `shop.json`, `items.json` 확장
| 파일·필드 | 출처 | 설명 |
|---|---|---|
| `shop.json` `enhancedSellBonusPerLevel`(0.25), `sellRounding: "awayFromZero"` | `ItemPrices.SellPrice`(`1.0 + 0.25 * 레벨`, `MidpointRounding.AwayFromZero`) | 강화 장비 판매가 |
| `items.json` `items[].bind`(`none`/`character`), `usable` | `AuctionRules.BindOf`(시작 장비와 보호권은 `CharacterBound`), `ConsumableDatabase.IsUsable` | `character_items.bind` 값 정함(보호권은 현재 어느 json에도 없다. `shop.json.equipment[].bind`는 장비만) |

### 4.7 `player.json` (새 파일, 화력 상한용)
| 필드 | 출처 | 설명 |
|---|---|---|
| `attackDamage`(기본 공격력), `attackCooldown` | `Assets/Resources/Data/PlayerStats.asset` (`attackDamage`, `attackCooldown`) | 3.2.3의 `baseAttack`, 공격 간격. 마법사 구슬 간격 0.5초는 `CharacterClassInfo`(현재 theory_skills.py 주석에서만 확인, 서버는 느슨한 전사 값을 쓰므로 필수 아님) |

### 4.8 `dungeons.json` 확장 (9절 제안을 채택할 때)
| 필드 | 출처 | 설명 |
|---|---|---|
| `dailyEntries`(3) | `DungeonDatabase.DailyEntries` | 일일 입장 횟수 |
| `weekendOpensAll`(true) | `ResetClock.IsOpen`(토·일은 전부 개방) | 개방 요일 규칙 |
| `dungeons[].rewards[]`: `{ itemId, min, max, weight }` | `DungeonDef.rewards`(`RewardEntry`, `itemId`가 `gear`이면 장비 카드) | 카드 표 |
| `cards`: `{ count: 4, gearRareWeight: 3, gearDropTries: 12 }` | `DungeonRewards.CardCount/RareGearWeight/DropTries` | 카드·장비 굴림 상수 |
| `ranking`: `{ timeMax, hitsMax, killsMax, comboMax, revivePenalty, timeZeroAt, pointsPerHit, comboTarget, thresholds[], xpBonus[] }` | `DungeonRanking` 상수와 `Thresholds`, `XpBonus` | 점수와 랭크 |
| `minClearSeconds[]` (난이도별 4개) | 실측 최솟값(`Tools/balance/theory_balance.py` 주석: 일반 5종 실측 87~180초). 아직 값이 없으면 서버는 `0.5 x referenceSeconds`로 대신한다 | 최소 클리어 시간 |
| `difficulties[]`는 이미 `revives`, `minGearRarity`, `ticketWeight`, `rewardMul`, `monsterLevel`, `hpMul`을 가짐 | - | 추가 필요 없음 |

### 4.9 (선택, 화력 상한 정밀화) `passive_tree.json`, `skill_gems.json`
| 필드 | 출처 | 설명 |
|---|---|---|
| `passive_tree.json` `nodes[].effects` | `PassiveNode` 효과(피해 증가 %) | 지금은 레벨당 11%(점당 5.5%의 2배)로 상한을 두었다. 효과를 내보내면 점 수 대비 최대 피해 증가를 계산해 상한을 줄인다 |
| `skill_gems.json` `gems[].damageMult`, `cooldown` | `SkillGem` | 스킬 몫 상한(지금 2.0)을 계산값으로 대체 |

## 5. PLAN_SERVER §3 표에 없던 재화 경로 (코드에서 찾아 3단계에 추가)

| 경로 | C# 출처 | 처리 |
|---|---|---|
| 보물상자 | `TreasureChest.Interact`(바닥에 고정 보상 `Pickup`을 만든다) | POST `/chests/open`(캐릭터당 한 번, 서버가 직접 지급) |
| 공사장 납품(목재·돌 소모) | `QuestManager.DeliverMaterials`, `ConstructionSite` | POST `/deliveries`. 클라이언트가 로컬 인벤토리에서 빼면 서버 재고와 어긋난다 |
| 퀘스트 최대 체력 보너스 | `QuestManager.GiveReward`의 `AddMaxHealth` | 청구 응답 + `bonus_max_health`(계산값). 재화는 아니지만 영구 능력치라 서버 기록(`quest_claims`)이 정본 |
| 황금 해골 타격 골드 | `GoldRunnerBehaviour.OnHit`(`IAuthority`를 거치지 않음) | 처치 보고의 `hits`(상한으로 자름) |
| 던전 몬스터 처치 드롭·경험치 | `EnemyController.Die`는 던전에서도 같은 `DropLoot`를 부른다 | 9절의 `run_id` 맥락 처치 보고 |

## 6. 알려진 불일치와 확인이 필요한 사항

| 항목 | 내용 | 조치 |
|---|---|---|
| 파괴된 착용 무기 재지급 | `Equipment.FixSlots`가 `cls != Warrior`일 때만 시작 무기를 채운다. 전사가 착용 무기를 파괴당하면 슬롯이 빈다. `EquipmentDatabase.StarterWeapon`은 전사 값(`eq_sword_wood`)도 갖고 있어 의도로 보이지 않는다 | 의도 확인. 서버 초안은 직업 무관 재지급(api 11절) |
| 맵 재입장 시 노드 재생 | C#은 맵을 불러올 때마다 노드가 가득 찬 상태로 다시 만들어진다(`WorldBuilder.Build`) | 서버가 재생 시간을 실제 시각으로 센다. 클라이언트는 입장 때 `/maps/{id}/nodes`를 읽어 반영 |
| 시작 장비 강화 | `enhance.json`이 시작 장비를 뺀다. C# `TryEnhance`는 시작 장비도 시도할 수 있다 | 4.5, 11절 결정 |
| 퀘스트 `collect + consume` 소모 시점 | C#은 단계가 끝나는 때(`FinishStep`) 가져간다. 서버는 청구 시점에 가져간다 | 현재 데이터(`c1s_herbs`, 한 단계)에서는 같은 결과 |
| 퀘스트 처치 목표 집계 | C#은 퀘스트마다 목표를 0부터 센다. 서버는 서버가 받은 평생 처치 수를 청구한 퀘스트들의 합과 비교한다 | 서버 쪽이 같거나 약한 검사 |
| 필드 몬스터 id | 필드 해골은 `skeleton`, 연출·던전 해골은 `skel_warrior`로 서로 다른 id | 4.1의 `skeleton` 행 추가로 해소 |
| 몬스터 경험치 반올림 | `Mathf.RoundToInt`는 .5를 짝수 쪽으로 반올림하고 float 계산이다 | `xpByLevel` 표(4.1)를 쓰면 같은 값을 보장 |
| 3단계 던전의 AI 용병 | 던전 몬스터 HP 배율이 `PartyScale(파티 인원)`이고 인원에 AI 용병이 들어간다 | 3단계는 `party_size=1`(용병 없이 입장). 용병 설계는 4단계 |
| `IAuthority`와 온라인 | 동기 호출 모양이라 서버가 보상을 정하는 구조와 맞지 않는다 | 본 문서 1절의 표대로 호출 지점을 요청으로 바꾼다(클라이언트의 가장 큰 작업) |

## 7. 원장 reason <-> 경로

| 원장 | reason | 경로 | `ref` |
|---|---|---|---|
| `gold_ledger` | `starter` | 캐릭터 생성(2단계) | 캐릭터 uuid |
| | `drop_claim` | 줍기 | 드롭 uuid |
| | `quest_reward` | 퀘스트 청구 | 퀘스트 id |
| | `shop_buy`, `shop_sell` | 상점 | 아이템 id |
| | `enhance_cost` | 강화 | enhance_log uuid |
| | `dungeon_card` (0003) | 카드 선택 | run uuid |
| `item_ledger` | `starter` | 캐릭터 생성 | 캐릭터 uuid |
| | `drop_claim` | 줍기 | 드롭 uuid |
| | `gather` | 채집 | 노드 id |
| | `chest` | 상자 | 상자 id |
| | `quest_reward`, `quest_consume` | 퀘스트 청구 | 퀘스트 id |
| | `delivery` | 납품 | 납품처 id |
| | `shop_buy`, `shop_sell` | 상점 | 아이템 id |
| | `enhance_cost`, `enhance_result` | 강화(재료·보호권 소모, 키 교체) | enhance_log uuid |
| | `equip`, `unequip` | 장착·해제(가방 <-> 착용 쌍) | 아이템 키 |
| | `storage_move` | 창고(가방 <-> 창고 쌍) | 아이템 키 |
| | `use_item` | 물약·주문서·당근 | 아이템 id |
| | `dungeon_card` (0003) | 카드 선택 | run uuid |
| `xp_ledger` | `kill` | 처치 | 몬스터 id |
| | `quest_reward` | 퀘스트 청구 | 퀘스트 id |
| | `dungeon_clear` (0003) | 던전 결과 | run uuid |

검증 쿼리(테스트와 점검 작업): `character_items`의 (character_id, location, item_key)별 `count` = `item_ledger`의 같은 키별 `delta` 합. `characters.gold` = `gold_ledger`의 `delta` 합. 마지막 `balance_after` = 현재 값. 이 세 식이 어긋나면 원장 없는 변경이 있다는 뜻이다.

## 8. 에러 코드 대응 (클라이언트 동작)

| code | 클라이언트 동작 |
|---|---|
| `KILL_RATE_LIMITED` | `Retry-After` 뒤 재시도(드물게만 나와야 한다) |
| `KILL_REJECTED`, `KILL_BLOCKED` | 그 처치의 보상 없음(연출은 유지). 반복되면 서버 상태 새로 받기 |
| `NODE_NOT_READY` | 노드를 그루터기 상태로 되돌리고 `errors.ready_at`까지 쿨다운 표시 |
| `NOT_ENOUGH_GOLD`, `ENHANCE_NOT_ENOUGH`, `NOT_ENOUGH_ITEMS` | 로컬 표시와 서버 불일치. GET으로 다시 맞춘다 |
| `QUEST_NOT_DONE`, `QUEST_PREREQ`, `QUEST_OBJECTIVE_UNSUPPORTED` | 완료 처리를 취소하고 안내("아직 완료할 수 없다") |
| `QUEST_ALREADY_CLAIMED`, `CHEST_ALREADY_OPENED`, `CARD_ALREADY_PICKED` | 이미 받은 것으로 처리(상태만 맞춘다) |
| `ITEM_COOLDOWN` | 무시(연타) |
| `RUN_NOT_PLAYING`, `RUN_ACTIVE` | 던전 상태를 `GET /dungeons`로 다시 받는다 |

## 9. 클라이언트 작업 요약 (온라인 경제 창구)

PLAN_SERVER §3의 `IEconomy`(가칭)는 위 1절 표의 경로를 하나로 모은 창구다. 오프라인 구현은 지금 동작 그대로 두고, 온라인 구현은 서버 요청을 보내 `delta`를 로컬 복사본에 반영한다.

- 로컬 인벤토리·골드·레벨·경험치는 온라인에서 **서버 응답으로만** 바뀐다(줍기·구매·강화를 로컬에서 먼저 반영하지 않는다).
- 요청마다 `request_id`를 새로 만들고, 응답이 없으면 같은 id로 재전송한다.
- 접속 직후 `GET /characters/{uuid}`로 전체를 받는다. 맵 입장 때 `GET /maps/{id}/nodes`로 쿨다운 노드를 받는다. 로컬에서 `Completed`인데 `claimed_quests`에 없는 퀘스트는 청구를 다시 보낸다.
- 전투 중 처치 보고와 줍기는 비동기로 보내고(전투를 막지 않는다), 실패하면 그 처치만 보상이 없다.
