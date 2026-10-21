# 챕터 3 성장·장비 (2026-10-21, 검토 반영판)

기준: `PLAN_CHAPTER3_2026-10-21.md` 3~4절·8절. 현재 수치의 근거는 `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 2~3절.

## 1. 경험치 곡선 41~50

`Progression.XpToNext`를 그대로 이어 간다(`BaseXpToNext(L) x 36 x 0.8 x 2.2`). `Progression.MaxLevel`만 50. Lv1~40 값은 변하지 않는다.

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

- 40→50 합 6,872,974. 내보내기는 `progression.json` `xpToNext`가 49칸(Lv1~49, Lv50은 만렙이라 없음)인지, `monsters.json` `xpByLevel`이 50칸인지 `GameDataExport`가 단언한다.

### 1.1 공급 설계 (하루 2시간 = 미니 10분 + 던전·의뢰 30분 + 사냥·퀘스트 처치 80분)

| 출처 | 수치 | 근거 |
|---|---|---|
| 처치 경험치(지상 dry_*) | Lv42 87, Lv46 102, Lv49 115 | `HuntingGrounds.cs:86`, `HuntingZone.KillXp` |
| 처치 경험치(지하 B1~B4) | +25%: Lv42 109, Lv47 133, Lv53 165 | 같은 곳 |
| 사냥 시간당 | 지상 분당 18 x 60 x 87~115 = 94,000~124,000, 지하 분당 15 x 60 x 109~165 = 98,000~149,000. 평균 120,000 | 2026-10-16 실측 |
| 3장 퀘스트 | 메인 12개 300,000 + 서브 10개 60,000 = 데이터 360,000(x6 = 2,160,000, 전체의 31%) | `WORLD_AND_STORY.md` 2·5절 |
| 미니 던전 40 | 하루 10분 200마리, 11일 2,200마리 / 800 = 2.75막대 = 1,900,000(28%) | `LIVE_CONTENT.md` 1절 |
| 요일 던전 군주·일일 의뢰 | 1~40 비율(하루 111,000) 유지, 11일 1,330,000 | `HuntingGrounds.cs` DungeonClearFloor(벤치마크 45), `daily_quests.json` |
| 사냥·퀘스트 처치 | 11일 x 80분 = 14.7시간 x 120,000 = 1,760,000 | 위 |

합산: 2,160,000 + 1,900,000 + 1,330,000 + 1,760,000 = 7,150,000. 필요 6,872,974라 11일(22시간)에 Lv50이 되고 여유는 4%다. 완료 기준 22~27시간 안. 확정 검증은 `theory_progress.py --measured --mini 20 --to 50`(7단계).

### 1.2 몬스터 경험치 표

`server/data/monsters.json`의 `xpByLevel`은 30칸이다. 내보내기에서 50칸으로 늘리고 단언한다. 범위 밖 공식은 그대로.

### 1.3 스탯 성장

HP +8/Lv(전사 +14), MP +4/Lv는 50까지 그대로. `LevelDamageMul`은 Lv15 x1.56 고정 그대로. 41~50의 힘은 장비 티어와 특화에서 나온다.

## 2. 장비 티어 9·10

| 티어 | 레벨 | WeaponBase | HealthBase | BlockBase | 등급 |
|---|---|---|---|---|---|
| 8(기존) | 40 | 40 | 160 | 10 | 커먼~유니크, 레전더리 |
| 9 | 45 | 47 | 190 | 11 | 커먼~유니크 |
| 10 | 50 | 55 | 225 | 12 | 커먼~유니크, 레전더리 |

- 등급 배율·방어 배율·이동 속도 그대로. Lv40 레전더리 무기 128 > Lv50 에픽 69, = Lv50 유니크 127. 계승으로 Lv50 레전더리 176.
- 이름 80개(`ART_LIST.md` 6절). 드롭 규칙 그대로(Lv45 몬스터부터 티어 9, Lv50부터 티어 10). 레전더리 Lv50 출처: 카마츠 카드 1%, 카마츠·시바크 레전더리 상자, 계승.
- 아이콘: 티어 밴드 대체 아트 배열에 밴드 8~9.

## 3. 강화·판매가

| 항목 | 티어 9 | 티어 10 | 근거 |
|---|---|---|---|
| `EnhanceRules.GoldByTier` | 8,690 | 10,000 | 티어 8 7,560 x1.15씩(5절 경제 목표에서 역산. x1.3이면 0.72로 목표 미달) |
| 성공률·GoldMul·실패 규칙 | 그대로 | 그대로 | |
| `ConsumableDatabase.GearSell` | 배열 변경 없음 | | 등급별 6칸에 `x(1 + 0.25 x levelTier)`가 곱해져 levelTier 8·9는 x3.0·x3.25 |

## 4. 승급·계승

- 승급(`PromoteRules.CostInto`) 그대로. 핵 수급: 시바크 { 1, 2, 2, 3 }, 카마츠 { 2, 3, 4, 6 }.
- 계승(`PromoteRules.InheritCost`, 아카브·무쇠 대장간): Lv40 유니크·레전더리 → 같은 종류·같은 등급의 Lv50 장비. 커먼~에픽은 계승하지 않는다(에픽은 Lv50 몬스터 드롭으로 대체). Lv45 티어는 대상도 결과도 아니다.

| 등급 | 핵 | 침식 결정(시바크) | 잿빛 인장(카마츠) | 골드 |
|---|---|---|---|---|
| 유니크 | 3 | 5 | 10 | 150,000 |
| 레전더리 | 6 | 10 | 20 | 400,000 |

- 결과 키 규칙: `eq_{kind}_50_{grade}` 새 아이템 행을 만들고 원본 행을 소비한다. 같은 키끼리 스택을 합치지 않는다(장비는 개별 행). 강화 수치·보호권 이력(`enhancePity`)·장착 상태를 이전한다. 경매 가격 키는 티어별로 분리돼 있어(`eq_*_50_*`) Lv40 시세와 섞이지 않는다.
- 서버: `promote` 도메인 `inherit` 엔드포인트. 승급과 같은 트랜잭션·행 잠금·request_id 멱등·원장(`gold_ledger` 사유 `inherit`, 마이그레이션 0049). `assertOpen('inherit', id)`.

## 5. 골드 수지 40~50

목표 재정의: (Lv40 잔고 중앙값 + 40~50 수입) / (Lv50 한 벌 + 계승) = 0.85~0.90. Lv40 잔고 중앙값은 20명 시뮬 v3의 910,000(`BALANCE_MEASURE_2026-10-16.md` 5절).

| 항목 | 값 | 근거 |
|---|---|---|
| Lv50 한 벌 | 1,800,000 | Lv40 한 벌 1,360,000 x (GoldByTier 10,000 / 7,560) |
| 계승(유니크 1개) | 150,000 | 4절 |
| 아카브 재료 교환 | 40,000 | 침식 결정 5 + 20,000골드 → 잿빛 인장 1(주 2회 상한). 2주 4회 |
| 지출 합 | 1,990,000 | |
| Lv40 잔고 | 910,000 | 시뮬 중앙값 |
| 40~50 수입: 퀘스트 | 143,000 | 286,000 x 0.5 |
| 사냥 골드 더미 + 재료·장비 판매 | 352,000 | 11일 x 80분 x (더미 250 + 판매 150)/분. 더미는 Lv40 실측 144~326/분의 중앙값 |
| 군주 던전 | 33,000 | 11일 x 3회 x 1,000 |
| 일일 의뢰 | 50,000 | 11일 x 3 x 1,500 |
| 레이드 확정 골드 | 76,000 | 시바크 12,500 x 4판 + 카마츠 20,000 x 1판 + 심화 |
| 미니 던전 골드 | 79,000 | 2,200마리 x 36 |
| 월드·길드보스 | 120,000 | 주 80,000 x 1.5주 |
| 수입 합 | 853,000 | |
| 비율 | (910,000 + 853,000) / 1,990,000 = 0.886 | 목표 안 |

- 손잡이: 계승 골드(4절), 티어 10 강화 배율(x1.15), 아카브 교환 골드. `theory_gold.py`에 Lv50 KIT·티어 9·10·계승·교환·미니 골드를 넣고 `--to 50 --mini 20`으로 돈다(7단계).

## 6. 수입 상한·레벨 보상·업적

| 항목 | 변경 |
|---|---|
| `income_caps.json` | 밴드 41~44(dry_floor, 42), 45~47(dry_stones, 46), 48~50(dry_rift, 49). `theory_income.py` 재생성. 0단계 테스트: Lv41+ 밴드가 없을 때 상한 검사가 마지막 밴드를 쓰지 않고 거부하는지 |
| `daily_quests.json` | (36~999) → (36~40), (41~45), (46~50). 0단계 테스트: 절단 뒤 Lv40 의뢰 그대로 |
| `level_rewards.json` | tiers { 45, 2000 }, { 50, 5000 }. 패스 45·50 |
| `achievementDefs.ts` | `level_40` 문구 "레벨 40을 달성한다"(최고 레벨 표현 삭제), `level_50` "경계를 넘은 자"(레벨 50), `spec_first` "첫 특화"(특화 1개 선택), `spec_all` "두 갈래"(특화 포인트 10점 전부 배치) |

## 7. 보스 방 상한 재계산 (DAMAGE_SLACK)

- 티어 10 공격력 55는 티어 8 40의 1.375배. `attemptDamageCap`은 attackCap에 비례하므로 상한도 1.375배.
- 집중형 특화는 보스 1체에서 원값보다 센 한 방(crush 4.0/2.7, f_focus 8.8/4.4, m_ice 7.8/2.6)이라 보스 방 피해는 최대 1.5배가 된다(60초 총량 기준).
- 2026-10-20 실측 전사 피크 270,000(상한 731,000, 비율 0.37). 티어 10 + 집중형: 270,000 x 1.375 x 1.5 = 557,000, 상한 1,005,000, 비율 0.55. 비숍(직업 배율 2.2): 73,000 x 1.375 x 1.5 x 2.2 = 331,000, 상한 1,087,000, 비율 0.30.
- 둘 다 `HIGH_DAMAGE_RATIO` 0.8 아래라 `DAMAGE_SLACK`은 0.6을 유지한다. `theory_specs.py`가 특화별 비율을 내고 0.8에 닿는 줄을 표시한다.

## 8. 만렙 하드코딩 교체 목록

현재 시스템 2.1절 목록이 작업 목록이다. 원칙: 클라이언트는 `Progression.MaxLevel`(50)과 `Progression.LevelCap`(서버 상한, 닫히면 40), 서버는 `progression.maxLevel`(50)과 `levelCap()`.

| 위치 | 바꿀 내용 |
|---|---|
| `Progression.cs:14` | `MaxLevel = 50`, `LevelCap` 추가 |
| `Progression.cs:63,77` | 만렙 표기·XpNeeded 0 조건을 `LevelCap`으로 |
| `UI/WorldMapScreen.Atlas.cs:292`, `Party/MercenaryDatabase.cs:161` | `LevelCap` |
| `MiniDungeon/MiniDungeonCatalog.cs:23-25` | 티어 40 추가, 티어별 상수 |
| `BossRush/BossRushCatalog.cs:22` | `DefaultLevel`(보스 레벨)을 `LevelCap`으로, `MinLevel` 40 고정 상수 추가 |
| `server/src/gamedata/economyData.ts:49,585` | maxLevel 50 로드, chapter 태그 |
| `server/src/domains/economy/economyContext.ts:176-183` | grantXp 멈춤을 `levelCap()`으로 |
| `server/src/domains/dailies/dailyService.ts:97` | `levelCap()` |
| `server/src/domains/minidungeon/miniDungeonRules.ts:10-11` | `level < 40` 삭제 |
| `server/src/domains/bossrush/bossRushRules.ts:15`, `bossRushService.ts:66,83-84` | 보스 레벨 `levelCap()`, 입장 레벨 40 고정 |
| `server/src/domains/achievements/achievementDefs.ts:45` | 6절 |
| `server/data/daily_quests.json`, `income_caps.json`, `monsters.json` | 6절·1.2절 |
| `Tools/balance/theory_progress.py` 13곳, `theory_gold.py`, `analysis/player-sim/tools/extract-dotrpg-v3.mjs:25,60` | `MAX_LEVEL` 상수, TIERS에 45·50 |
| `server/test/` 10개 파일 | Lv40 기대값을 상한 상수 참조로 |
