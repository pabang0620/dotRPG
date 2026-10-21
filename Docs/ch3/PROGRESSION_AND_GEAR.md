# 챕터 3 성장·장비 (2026-10-21)

기준: `PLAN_CHAPTER3_2026-10-21.md` 3~4절. 현재 수치의 근거는 `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 2~3절.

## 1. 경험치 곡선 41~50

`Progression.XpToNext`를 그대로 이어 간다(`BaseXpToNext(L) x 36 x 0.8 x 2.2`). `Progression.MaxLevel`만 50으로 바꾼다. Lv1~40 값은 변하지 않는다.

| 레벨 | 다음 레벨까지 | 누적(40부터) |
|---|---|---|
| 40 | 558,518 | 558,518 |
| 41 | 585,446 | 1,143,964 |
| 42 | 613,008 | 1,756,972 |
| 43 | 641,203 | 2,398,175 |
| 44 | 670,032 | 3,068,207 |
| 45 | 699,494 | 3,767,701 |
| 46 | 729,590 | 4,497,291 |
| 47 | 760,320 | 5,257,611 |
| 48 | 791,683 | 6,049,294 |
| 49 | 823,680 | 6,872,974 |

- 40→50 합 6,872,974. 1→40 합 6,558,217과 같은 크기다.
- 시간 목표 24시간은 경험치 공급으로 맞춘다. 1→40의 공급 비율(퀘스트 44%, 퀘스트 처치·사냥, 던전, 일일·주간 의뢰)을 따라 3장도 퀘스트 경험치가 전체의 40~45%가 되게 둔다.

### 1.1 공급 설계 (하루 2시간 기준)

| 출처 | 수치 | 근거 |
|---|---|---|
| 처치 경험치(지상 dry_*) | `HuntingGrounds.XpAt`은 몬스터 Lv로 계산. Lv42 87, Lv46 102, Lv49 115 | `HuntingGrounds.cs:86`, `HuntingZone.KillXp` |
| 처치 경험치(지하 B1~B4) | +25%: Lv42 109, Lv47 133, Lv53 165 | 같은 곳 |
| 사냥 시간당 | 지상 분당 처치 18 x 60 x 87~115 = 94,000~124,000, 지하 분당 15 x 60 x 109~165 = 98,000~149,000. 평균 120,000 | 2026-10-16 실측(`analysis/player-sim/data/dotrpg-measured.json`) |
| 3장 퀘스트 | 메인 12개 + 서브 9개, 데이터 XP 합 490,000(x6 = 2,940,000, 전체의 43%) | `WORLD_AND_STORY.md` 2~3절 |
| 미니 던전 40 | 하루 30분 600마리, 따라잡기 3,600마리 = 1막대. 12일 약 2막대 = 1,400,000 안팎 | `LIVE_CONTENT.md` 1절 |
| 요일 던전 군주 | clearXp 바닥은 `DungeonClearFloor`가 권장 레벨 45로 계산(벤치마크 배열 { 10, 18, 26, 36 }에 45 추가) | `HuntingGrounds.cs` DungeonClearFloor |
| 일일·주간 의뢰 | 밴드 41~45·46~50, xpShare 0.05 유지 | `server/data/daily_quests.json` |

합산(하루 2시간 = 미니 30분 + 던전·의뢰 30분 + 사냥·퀘스트 처치 60분, 12일): 퀘스트 2,940,000 + 미니 1,400,000 + 던전·의뢰 1,330,000 + 사냥 12시간 x 120,000 = 1,440,000, 합 7,110,000. 필요 6,872,974라 24시간 안에 Lv50이 되고 여유는 약 3%다. 확정 검증은 `theory_progress.py`를 50까지 늘린 뒤 `--measured --mini 20`으로 한다(`IMPLEMENTATION_ORDER.md` 7단계).

### 1.2 몬스터 경험치 표

`server/data/monsters.json`의 `xpByLevel`은 30칸이다(현재 시스템 2.1절). 내보내기(`GameDataExport`)에서 50칸으로 늘린다. 범위 밖 공식 `xp x (1 + xpPerLevel x (L-1))`은 그대로 둔다. 지하 몬스터(hollow_*)는 Lv42~53이라 표 안에 들어온다.

### 1.3 스탯 성장

HP +8/Lv(전사 +14), MP +4/Lv는 그대로 50까지 이어진다. 레벨 피해 보정(`LevelDamageMul`)은 Lv15에서 x1.56으로 고정돼 있고 그대로 둔다. 41~50의 힘은 장비 티어와 특화에서 나온다.

## 2. 장비 티어 9·10

`GearCatalog.TierLevels`에 45·50을 더한다. 기준값은 직전 티어 증가율(34→40 x1.18, 135→160 x1.19)을 이어 간다.

| 티어 | 레벨 | WeaponBase | HealthBase | BlockBase | 등급 |
|---|---|---|---|---|---|
| 8(기존) | 40 | 40 | 160 | 10 | 커먼~유니크, 레전더리 |
| 9 | 45 | 47 | 190 | 11 | 커먼~유니크 |
| 10 | 50 | 55 | 225 | 12 | 커먼~유니크, 레전더리 |

- 등급 배율(커먼 0.6, 언커먼 0.8, 레어 1, 에픽 1.25, 유니크 2.3, 레전더리 3.2)과 이동 속도·방어 배율은 그대로.
- 유지되는 가치: Lv40 레전더리 무기 40 x 3.2 = 128 > Lv50 에픽 55 x 1.25 = 69, Lv50 유니크 55 x 2.3 = 127. 2달 설계로 맞춘 Lv40 레전더리는 Lv50 유니크와 같은 급이고, 계승(4절)으로 Lv50 레전더리(176)가 된다. 사례 2절의 "첫 지역 퀘스트 장비가 레이드 장비를 바로 넘는" 일은 없다.
- 이름: 8종 x 2티어 x 5등급 = 80개. 테마는 메말라 가는 협곡(티어 9)과 사얍·공허(티어 10). 목록은 `ART_LIST.md` 6절.
- 드롭: 몬스터 레벨 이하 최고 티어 규칙 그대로(Lv45 몬스터부터 티어 9, Lv50부터 티어 10). 3장 사냥터 몬스터 Lv 42·46·49와 지하 42·47·53이 티어 8·9·9·8·9·10을 준다.
- 레전더리 Lv50의 출처: 카마츠 레이드 카드 1%, 카마츠 레전더리 무기 상자(재료 60), 계승.
- 아이콘: 티어 밴드 대체 아트 배열에 밴드 8~9 추가(`ART_LIST.md` 6절).

## 3. 강화·판매가

| 항목 | 티어 9 | 티어 10 | 근거 |
|---|---|---|---|
| `EnhanceRules.GoldByTier` | 9,830 | 12,780 | 티어 8 7,560 x1.3씩. 7단계→8단계가 x2.03이었던 것은 Lv40 만렙 체류 골드 싱크였고, 41~50은 레벨업이 계속되므로 수입 증가율(사냥 골드 더미·판매가 티어 증가 약 +20%)보다 약간 큰 x1.3 |
| 성공률·GoldMul·실패 규칙 | 그대로 | 그대로 | |
| `ConsumableDatabase.GearSell` | 배열 변경 없음 | | 등급별 6칸 { 20, 50, 120, 300, 600, 1,200 }에 `x(1 + 0.25 x levelTier)`가 곱해지므로 levelTier 8·9는 자동으로 x3.0·x3.25 |
| 재료 | 뼈·강화석·정수 판매가 그대로 | | |

## 4. 승급·계승

- 승급(`PromoteRules.CostInto`)은 그대로. 핵 수급: 시브 중간 { 1, 2, 2, 3 }, 카마츠 최종 { 2, 3, 4, 6 }(기존 표 재사용).
- 계승(새 규칙 `PromoteRules.InheritCost`): 대장간에서 Lv40 장비 + 고대의 핵 + 골드 → 같은 종류·같은 등급의 Lv50 장비. 강화 수치·보호권 이력 유지. 비용은 같은 등급 `CostInto`의 절반(소수점 올림).

| 등급 | 핵 | 골드 |
|---|---|---|
| 에픽 | 2 | 15,000 |
| 유니크 | 5 | 60,000 |
| 레전더리 | 12 | 200,000 |

- 커먼~레어는 계승하지 않는다(드롭으로 바로 대체된다). Lv45 티어는 계승 대상도 결과도 아니다.
- 서버: `promote` 도메인에 `inherit` 엔드포인트. 검증은 승급과 같은 트랜잭션·행 잠금·request_id 멱등·원장(`gold_ledger` 사유 `inherit`).
- 계승은 `CHAPTER3_OPEN=1`에서만 받는다(닫힌 동안 Lv50 장비가 존재하면 안 된다).

## 5. 골드 수지 40~50

목표: 무자본이 Lv50에서 "Lv50 클리어 한 벌"의 0.85~0.90.

- Lv50 클리어 한 벌(`theory_gold.py` KIT 확장): 에픽 무기 +11, 에픽 판금·각반 +10, 레어 목걸이·반지 +8을 티어 10으로. 지출 = 강화 골드(티어 10 12,780 기준) + 계승 골드(유니크 1개 60,000을 평균 지출에 포함).
- 수입: 사냥 골드 더미(Lv41+ 사냥터 더미 18~26, `HuntingZone` 골드 공식 그대로), 재료·장비 판매, 3장 퀘스트 골드(데이터 합 286,000 x 0.5), 요일 던전 군주, 레이드 확정 골드(시브 10,000~15,000, 카마츠 16,000~24,000), 월드·길드보스 주간, 의뢰.
- 조정 손잡이는 `GoldByTier[8..9]`와 3장 퀘스트 골드다. `theory_gold.py`에 Lv50 KIT·티어 9·10·계승·미니 골드(티어 40) 항목을 넣고 `--to 50 --mini 20`으로 돈다(7단계).

## 6. 수입 상한·레벨 보상·업적

| 항목 | 변경 |
|---|---|
| `server/data/income_caps.json` 밴드 | 41~44(zone dry_floor, 몬스터 42), 45~47(dry_stones, 46), 48~50(dry_rift, 49). `Tools/balance/theory_income.py`로 생성 |
| `server/data/daily_quests.json` 밴드 | (36~40) 유지, (41~45) 사얍 게시판, (46~50) 사얍 게시판. `maxLevelGold`는 그대로 |
| `server/data/level_rewards.json` | tiers에 { 45, 2000 }, { 50, 5000 } 추가. 패스 tiers에 45·50 |
| `achievementDefs.ts` | `level_50` "경계를 넘은 자"(레벨 50), `spec_first` "첫 특화"(특화 1개 선택), `spec_all` "두 갈래의 끝"(해금된 특화 10쌍 전부 한 번씩 선택) |

## 7. 만렙 하드코딩 교체 목록

현재 시스템 2.1절의 목록을 그대로 작업 목록으로 쓴다. 원칙: 클라이언트는 `Progression.MaxLevel`(50)과 `Progression.LevelCap`(서버 상한, 닫히면 40)을 쓴다. 서버는 `economy.progression.maxLevel`(50)과 `levelCap()`(플래그로 40 또는 50)을 쓴다.

| 위치 | 바꿀 내용 |
|---|---|
| `Progression.cs:14` | `MaxLevel = 50`, `LevelCap` 추가(`OnlineSession` meta의 `level_cap`, 오프라인은 `Chapter3.Enabled`에 따라 40 또는 50) |
| `Progression.cs:63,77` | 만렙 표기·XpNeeded 0 조건을 `LevelCap`으로 |
| `World/HuntingGrounds.cs:86` | `Math.Min(underground ? MaxMonsterLevel : Progression.MaxLevel, level)` 그대로(MaxLevel 50) |
| `UI/WorldMapScreen.Atlas.cs:292` | `LevelCap` |
| `Party/MercenaryDatabase.cs:161` | `LevelCap` |
| `MiniDungeon/MiniDungeonCatalog.cs:23-25` | 티어 40 추가, `DevCapture.MiniDungeon.cs:99` 10단위 검사는 그대로 |
| `BossRush/BossRushCatalog.cs:22` | `DefaultLevel`을 `LevelCap`으로 |
| `server/src/gamedata/economyData.ts:49,585` | maxLevel 50 로드 |
| `server/src/domains/economy/economyContext.ts:176-183` | grantXp 멈춤을 `levelCap()`으로 |
| `server/src/domains/dailies/dailyService.ts:97` | `levelCap()` |
| `server/src/domains/minidungeon/miniDungeonRules.ts:10-11` | `level < 40` 삭제, `level < levelCap`만 |
| `server/src/domains/bossrush/bossRushRules.ts:15`, `bossRushService.ts:66,83-84` | `BOSS_LEVEL`을 `levelCap()`으로 |
| `server/src/domains/achievements/achievementDefs.ts:45` | level_50 추가 |
| `server/data/daily_quests.json`, `income_caps.json`, `monsters.json` | 6절·1.2절 |
| `Tools/balance/theory_progress.py` 13곳, `theory_gold.py`, `analysis/player-sim/tools/extract-dotrpg-v3.mjs:25,60` | `MAX_LEVEL` 상수 하나로, TIERS에 45·50 |
| `server/test/` 10개 파일 | Lv40 기대값을 상한 상수 참조로 |
