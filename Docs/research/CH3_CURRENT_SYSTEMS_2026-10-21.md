# 챕터 3 설계용 현재 시스템 사실 표 (2026-10-21)

읽기 전용 조사. 모든 항목에 파일:줄 근거를 붙였다. 경로는 레포 루트 기준, 클라이언트는 `Assets/Scripts/Runtime/` 아래다. 수치는 조사 시점 코드 값이며 설계 제안은 담지 않는다.

## 1. 스킬

### 1.1 구조

| 항목 | 사실 | 근거 |
|---|---|---|
| 슬롯 | 8칸: 0~3 = Q W E R, 4 = T(각성), 5~7 = A S D. 보조 젬 칸 2개/슬롯 | `Progression/SkillData.cs:54,60` |
| 슬롯 해금 레벨 | { 2, 6, 12, 18, 15(각성), 20, 25, 30 } | `SkillData.cs:60` |
| 기본 직업 액티브 | 전사 5(crush·whirl·cry·charge·blades), 마법사 5(lance·frostorb·nova·firefield·meteor). 슬롯당 1개, `SkillGem` 수치(damageMult·cooldown·radius·range·hits·manaCost) | `SkillData.cs:77-87` |
| 보조 젬 6종 | sup_dmg(Lv3) sup_eff(5) sup_aoe(7) sup_leech(8) sup_multi(10) sup_chain=보스 사냥(14) | `SkillData.cs:89-100` |
| 최종 수치 계산 | `CharacterStatsCalc.Skill()`: 기본 공격력 x (1+inc%) x damageMult x more x rankScale x LevelDamageMul. 면적은 제곱근으로 커짐. 전직 스킬은 repeats·leech·bossPct 미적용, 주력 사냥기는 MP가 최대 MP 비율(MainHunterMpShare) | `SkillData.cs:248-300`, `CareerData.cs:60-63` |
| 전직 스킬 | 직업당 10개(index 0~9): 0·4 패시브, 1~3·5~7 액티브, 8 각성, 9 추가 노드(파이터 검기 태세 Lv18, 메이지 매직 배리어 Lv15). 수치는 `S()`가 배율로 가공(액티브 MP 0.65x, 쿨 0.7x, 반경 1.5x, 각성 쿨 1.25x·hits 1.5x, 쿨 있는 공격기 +10%/초 최대 2x) | `Progression/CareerData.cs:41-56,88-152,164-201` |
| 전직 조건·랭크 | Lv15 전직, 랭크 최대 3, 포인트 = Level-11(+환불-사용), 랭크 r 비용 = r(r+1)/2 누적. 각성은 questStage 5 | `Progression/Progression.Career.cs:11-16,25,34,53` |
| 주력 사냥기 | f_break·g_wall·m_storm·b_light, 전부 Lv18, 쿨 0 | `CareerData.cs:54-56` |
| 스킬 스타일 | 저장 빌드 3개(2번째는 유료), 노드·젬·패시브 세트 | `Progression/Progression.Styles.cs:14`, `Docs/PLAN_SKILL_STYLES.md` |
| 패시브 트리 | PassiveStat 16종, 노드 kind Start/Small/Notable/Mastery/Keystone, 키스톤 4종, 클러스터 0~3 = 스킬 슬롯 1~4 팔, 4 = 키스톤. 포인트 = Level-1 | `Progression/PassiveTree.cs:9-43`, `Progression.cs:81` |
| 서버 검증 | `careerSchema`·`validateCareer`: 직업별 id 목록(careerIds), 노드 선행·레벨(tier 0~1 Lv15, 2 Lv18, 3 Lv22, 주력기 18), 환불 포인트, 활성 슬롯 유효성 | `server/src/domains/characters/characterValidation.ts:12-31,62-90` |
| 서버 스타일 | 구매 검증만(styleBuyBody) | `server/src/domains/skillstyles/skillStyleValidation.ts:5` |

### 1.2 전직 스킬 표 (코드 원값, `S()` 가공 전)

| id | 이름 | 직업 | 칸 | 해금 | 발사체·타격 | 배율 | 쿨 | MP | 근거 |
|---|---|---|---|---|---|---|---|---|---|
| f_rhythm | 검기 연성 | 파이터 | 패시브 | 15 | 검기 3개 적립 | +20% | - | - | `CareerData.cs:164` |
| f_cross | 십자참 | 파이터 | 1 | 15 | 부채꼴 2회 | 1.7 | 3 | 10 | :165 |
| f_rush | 섬광보 | 파이터 | 2 | 18 | 직선 돌진 4m | 2.8 | 6 | 14 | :166 |
| f_flurry | 검귀 해방 | 파이터 | 3 | 22 | 버프 120초 | +20% | 62 | 20 | :167 |
| f_edge | 파죽지세 | 파이터 | 패시브 | 15 | HP80%+ 적 +15% | - | - | - | :168 |
| f_break | 파쇄 검기 | 파이터 | 5 | 18 | 관통 초승달 7m, 쿨 0 | 1.4 | 0 | 18(비율) | :169 |
| f_focus | 일섬 | 파이터 | 6 | 18 | 직선 9m 지연 베기 | 4.4 | 10 | 22 | :170 |
| f_execute | 단죄 | 파이터 | 7 | 22 | 단일 내려찍기, HP35% 미만 1.6배 | 5.2 | 12 | 26 | :171 |
| f_awake | 천검귀일 | 파이터 | 각성 | 15 | 반경 10m 1000% | 1.6 | 60 | 60 | :172 |
| f_stance | 검기 태세 | 파이터 | 9 | 18 | 토글, 기본 공격마다 검기, 3타째 3줄 | 1.1 | 0 | 5/검기 | :173 |
| g_steel | 강철의 심장 | 가디언 | 패시브 | 15 | 받는 피해 -6% | - | - | - | :174 |
| g_guard | 강철의 보루 | 가디언 | 1 | 15 | 무적 1초 + 60초 감소 + 거대 방패 | - | 62 | - | :175 |
| g_wall | 회귀의 방패 | 가디언 | 2 | 18 | 방패 투척 3~5개 왕복, 쿨 0 | - | 0 | 비율 | :176 |
| g_oath | 수호의 맹세 | 가디언 | 3 | 22 | 결계 4m 60초 | .15 | 62 | 28 | :177 |
| g_retal | 수호의 반향 | 가디언 | 패시브 | 15 | 피격 후 +20% | - | - | - | :178 |
| g_taunt | 대지의 호령 | 가디언 | 5 | 18 | 반경 7m 도발 | - | - | - | :179 |
| g_bash | 방패 강타 | 가디언 | 6 | 18 | 부채꼴 기절 | 3 | 7 | 16 | :180 |
| g_counter | 응보의 방진 | 가디언 | 7 | 22 | 60초 -40%, 3회 완전 방어 | - | 62 | - | :181 |
| g_awake | 천쇄방패 | 가디언 | 각성 | 15 | 반경 5m 속박·방어 붕괴 | - | - | - | :182 |
| m_elements | 원소의 기억 | 메이지 | 패시브 | 15 | 다른 원소 +12% | - | - | - | :183 |
| m_fire | 홍련구 | 메이지 | 1 | 15 | 화염구 폭발 1.8m + 화상 3, 쿨 0 | 1.2 | 0 | 8 | :184 |
| m_ice | 빙결삼창 | 메이지 | 2 | 18 | 얼음창 3개(정면, 좌우 20도) 6.5m 관통 | 2.6 | 8 | 24 | :185, `Combat/CareerCombat.Arcanist.cs:93-107` |
| m_storm | 연쇄전격 | 메이지 | 3 | 18 | 최대 5명 연쇄, 쿨 0 | 1.3 | 0 | 비율 | :186, `CareerCombat.Arcanist.cs:162-177` |
| m_flow | 마력 순환 | 메이지 | 패시브 | 15 | 전직 MP -10% | - | - | - | :187 |
| m_orbit | 성운 폭발(차징) | 메이지 | 5 | 18 | 차징 최대 2초, 범위 1.8배·피해 3배 | - | - | - | :188 |
| m_veil | 차원도약 | 메이지 | 6 | 18 | 순간이동 + 잔상 폭발 | 2.4 | 14 | 24 | :189 |
| m_rift | 중력 균열 | 메이지 | 7 | 22 | 2.6m 6타 + 붕괴 160% | .8 | 15 | 36 | :190 |
| m_awake | 유성우 | 메이지 | 각성 | 15 | 혜성 192개 4초 | - | - | - | :191 |
| m_barrier | 매직 배리어 | 메이지 | 9 | 15 | HP 35% 보호막 60초 | .35 | 40 | 30 | :192 |
| b_mercy | 생명의 숨 | 비숍 | 패시브 | 15 | 회복 +10% | - | - | - | :193 |
| b_heal | 치유의 깃 | 비숍 | 1 | 15 | 반경 2m 회복 | 2.8 | 5 | 20 | :194 |
| b_bloom | 생명의 파문 | 비숍 | 2 | 18 | 3.5m 6회 | .8 | 12 | 26 | :195 |
| b_cleanse | 정화의 종 | 비숍 | 3 | 22 | 4m 정화 + 빛 피해 | 1.4 | 11 | 24 | :196 |
| b_grace | 축복의 그릇 | 비숍 | 패시브 | 15 | 보호막 +12% | - | - | - | :197 |
| b_light | 심판의 광창 | 비숍 | 5 | 18 | 관통 7m, 쿨 0 | 1.25 | 0 | 비율 | :198 |
| b_wing | 신의 가호 | 비숍 | 6 | 18 | 5m 아군 무적 1초 | - | 25 | 20 | :199 |
| b_bless | 축복의 연결 | 비숍 | 7 | 22 | 5m +18% 60초 | 18 | 62 | 28 | :200 |
| b_awake | 천상의 행진 | 비숍 | 각성 | 15 | 12m 빛기둥 + 성역 8회 | - | - | - | :201 |

기본 직업 액티브(전사·마법사, 전직 뒤에도 쓰는 Q~R·T): `SkillData.cs:77-87` (파쇄 일격 2.7배, 회전 베기 1.7, 전쟁 함성 0.5 기절, 돌진 베기 2.5, 천검 강림 2.4x12타 / 번개 창, 빙뢰구 1.6, 서리 폭발 0.5 빙결 2.8초, 화염 장판 0.4x6, 메테오 2.8x8).

### 1.3 이펙트·색·분기점

| 항목 | 사실 | 근거 |
|---|---|---|
| 직업 색 | 파이터 Steel(0.55,0.82,1) / 가디언 Teal / 메이지 Violet / 비숍 Holy. `CareerFx.Main(career)`로 가져오고, 각 스킬 함수가 색을 직접 넘긴다(Arcanist 29곳, Fighter 30곳, Guardian 27곳, Bishop 13곳) | `Combat/CareerFx.cs:12-18`, 각 `CareerCombat.*.cs` |
| 아이콘 | `"career_" + id`(전직), `"gem_" + id`(기본·보조) | `CareerData.cs:40`, `SkillData.cs:63` |
| 시전 분기점 | `effect` 문자열 switch: Fighter `CareerCombat.Fighter.cs:17-26`, Guardian `:43-49`, Arcanist `:18-26`, Bishop `:13-28`. 발사체 수·갈래는 각 함수 안에서 고정(예: `IceSpears`가 -1..1 반복으로 3발, `ChainLightning` hits 5, `ShieldThrow` 랭크당 +1 최대 5) | 위 파일 |
| 서버 측 스킬 | 서버는 스킬 피해를 계산하지 않고 공격 상한(attackCap: POWER_PASSIVE_PER_LEVEL 0.11, POWER_SKILL_FACTOR 2, AOE_CAP 5)으로만 자른다 | `server/src/config/env.ts:48-50`, `server/src/domains/kills/killRules.ts:33-40` |
| VFX 자료 | 29개 스킬 판정·생성 그림 목록 | `Docs/PLAN_SKILL_VFX.md` 2~3절 |

## 2. 성장

| 항목 | 사실 | 근거 |
|---|---|---|
| 만렙 상수 | `Progression.MaxLevel = 40` | `Progression/Progression.cs:14` |
| 곡선 | base = 40 + (L-1)x30 + (L-1)^2x5, x CurveScaleAt(EarlyScale 4 → LateScale 36, Lv10~26 선형) x MidTrim(Lv18+ 0.8) x Pace(Lv20+ 2.2) | `Progression.cs:33-58` |
| xpToNext 내보낸 값 | Lv1 160 … Lv20 102,010 … Lv39 532,224, Lv40 558,518(마지막 값은 만렙 뒤라 미사용). maxLevel 40 | `server/data/progression.json` |
| 퀘스트 배율 | XP x6, 골드 x0.5 | `Progression.cs:57,74` |
| 스탯 | HP +8/Lv(전사 +14), MP +4/Lv, 레벨 피해 +4%/Lv Lv15까지(x1.56 고정) | `SkillData.cs:170-184,219-221` |
| 레벨 보상 | 별 300/300/600/1000/3000 at Lv10/15/20/30/40, 패스 Lv5~40 | `server/data/level_rewards.json` |
| 업적 | `level_40` "정점에 선 자" | `server/src/domains/achievements/achievementDefs.ts:45` |

### 2.1 "40"이 박힌 곳 (만렙을 올리면 전부 손대야 함)

| 위치 | 내용 |
|---|---|
| `Progression.cs:14` | MaxLevel 상수(유일한 클라이언트 SSOT, 아래는 이 상수를 참조하거나 직접 40을 씀) |
| `Progression.cs:63,77` | 만렙 표기·XpNeeded 0 |
| `World/HuntingGrounds.cs:86` | XpAt: BaseXpToNext 기반, 지하 +25% |
| `UI/WorldMapScreen.Atlas.cs:292` | 지하 입장 레벨 표시 Min(Progression.MaxLevel, …) |
| `Party/MercenaryDatabase.cs:161` | 동료 레벨 Clamp 1..MaxLevel |
| `MiniDungeon/MiniDungeonCatalog.cs:23-25` | 티어 10/20/30(Lv10~39)만, Lv40은 없음 |
| `Core/DevCapture.MiniDungeon.cs:99` | 티어 = 10단위 검사 |
| `BossRush/BossRushCatalog.cs:22` | DefaultLevel 40 |
| `server/src/gamedata/economyData.ts:49,585` | progression.maxLevel 스키마·로드 |
| `server/src/domains/economy/economyContext.ts:176-183` | grantXp 만렙 멈춤 |
| `server/src/domains/dailies/dailyService.ts:97` | 만렙 의뢰 보상 골드만 |
| `server/src/domains/minidungeon/miniDungeonRules.ts:10-11` | `level < maxLevel && level < 40` 직접 40 |
| `server/src/domains/bossrush/bossRushRules.ts:15`, `bossRushService.ts:66,83-84` | BOSS_LEVEL 40 직접 상수 + maxLevel |
| `server/src/domains/achievements/achievementDefs.ts:45` | level_40 |
| `server/data/daily_quests.json` bands | (15-19)(20-24)(25-30)(31-35)(36-999) |
| `server/data/income_caps.json` bands | (1-4)…(29-33)(34-40): Lv41+ 밴드 없음 |
| `server/data/monsters.json` xpByLevel | 몬스터마다 30칸 표(skel_warrior 20…78), 범위 밖은 `xp x (1 + xpPerLevel x (L-1))` | `server/src/domains/kills/killRules.ts:10-14` |
| `Tools/balance/theory_progress.py:47,100,173,202,220,298,305,322,326,348,364,369,381` | TIERS·루프·만렙 조건에 40 직접 |
| `analysis/player-sim/tools/extract-dotrpg-v3.mjs:25,60` | tierLevels·totalTo40 |
| `server/test/` 10개 파일 | bossRush·dailies·fieldCarry·growthPass·jobs·contribution·gachaCeiling·economyHold·auctionAudit·bannedWordsCorpus에 Lv40 기대값 |

## 3. 장비

| 항목 | 사실 | 근거 |
|---|---|---|
| 티어 | 8단계 TierLevels { 1, 10, 15, 20, 25, 30, 35, 40 }, 등급 커먼~유니크(Lv1은 에픽까지), 레전더리는 Lv20·Lv40만(+토벌형 b), 8종(검·판금·각반 / 지팡이·로브·치마 / 목걸이·반지), id `eq_{kind}_{level}_{grade}` | `Items/GearCatalog.cs:7-14,47,136,151,228-250` |
| 아트 | 아이콘은 티어 밴드(0/1~3/4~5/6~7, 레전더리 3)로 대체 아트 | `GearCatalog.cs:196-201` |
| 강화 | 최대 +20, 성공률 표 100…10(+11부터 15 이하), 골드 = GoldByTier[티어] x GoldMul[단계](72배까지), 티어별 { 100,180,320,880,1600,2530,3720,7560 }, 무기 +11 이하 실패 -3·그 위 파괴, 방어구 파괴, 보호권 | `Items/EnhanceRules.cs:104-117,135-143`, `Items/EquipmentDatabase.cs:14` |
| 승급 | 고대의 핵(DungeonDatabase.RaidCore) + 골드, CostInto(to): 언커먼 1/3000, 레어 2/8000, 에픽 4/30000, 유니크 10/120000, 레전더리 24/400000. 핵 수급 중간 {1,2,2,3}, 최종 {2,3,4,6} | `Items/PromoteRules.cs:13-17,32`, `Docs/BALANCE_GOLD_2026-10-16.md` |
| 드롭 | 몬스터 레벨 이하 최고 티어만, 클래스 무관, dropWeight(Lv15 전 에픽 5) | `EquipmentDatabase.cs:374-388`, `GearCatalog.cs:293-295` |
| 판매가 | 장비 GearSell { 20,50,120,300,600,1200 }, 재료 뼈 4/강화석 14/정수 56 | `Items/ConsumableDatabase.cs`(2026-10-21 x0.7) |
| 클리어 한 벌 | KIT: Lv40 에픽 무기 +11, 에픽 판금·각반 +10, 레어 목걸이·반지 +8 (지출 약 136만) | `Tools/balance/theory_gold.py:61-66` |
| 레이드 상점 | 해골왕 에픽+ 45 / 레전더리 120, 그라흐 20 / 60(재료 단위) | `Dungeon/RaidRewards.cs:119-126` |
| 데이터 | 클라 정의가 원본, `server/data/items.json`·`enhance.json`·`shop.json`은 Unity 내보내기 산출물 | `Assets/Scripts/Editor/GameDataExport.cs` |

## 4. 세계

| 항목 | 사실 | 근거 |
|---|---|---|
| 마을 | 코바 마을(village, Town), 바위 협곡 마을(canyon), 눈꽃 숲 마을(winter), 침수된 고대 성소(sunken_sanctum), 뿌리샘 마을(undergate, UnderTown) | `World/MapRegistry.cs:57-62,78-90` |
| 사냥터 20곳 | forest 1~4 / forest_ruins 5~8 / forest_depths 9~12 / forest_crossing 10~13 / canyon_pass 13~16 / canyon_mine 17~20 / canyon_ridge 21~24 / canyon_gate 22~25 / winter_edge 25~28 / winter_lake 29~33 / winter_peak 34~40 / winter_reach 36~40 / sanctum_hall·archive·roots·court 38~40(몬스터 Lv40) / hollow_descent B1 40~45(42) / hollow_roots B2 45~50(47) / hollow_fungal B3 45~50(47) / hollow_depths B4 50~55(53) | `World/HuntingGrounds.cs:55-74` |
| 지하 B1~B4 | 뿌리샘 마을 아래 4층, 몬스터 hollow_scarab·hollow_guard·hollow_hexer(전용 아트 UnderworldMonsterArt), 처치 XP +25%, Lv42~53 몬스터가 이미 있음(만렙 40이라 지금은 경험치 0) | `HuntingGrounds.cs:20-21,71-74`, `Art/RegionalMonsterArt.cs:15` |
| 지역 몬스터 아트 | 절벽 바위(cliff_rock)·설인(snow_yeti)·성소 석상(sanctum_sentinel) 종족 아틀라스, 5방향 x 8프레임 | `Art/RegionalMonsterArt.cs:11-19` |
| 맵 생성 | 마을은 `Resources/Maps/*.txt` 텍스트 맵 + 생성 그림, 사냥터는 테마(Forest/Canyon/Winter/SanctumField/Underground) x variant 0~3 절차 생성 | `World/HuntingLayouts.cs:25-38`, `World/WorldBuilder.cs:259-260`, `MapRegistry.cs:4` |
| 사냥터 추가 절차 | HuntingZone 한 줄(id·이름·마을·테마·레벨·variant·몬스터 3종) → MapRegistry가 마을별로 자동 등록 → WorldAtlas 미리보기·NPC 마커 → 측정 모드는 `HuntingGrounds.All`을 돌아 자동 포함 | `MapRegistry.cs:69-76`, `World/WorldAtlas.cs:31-151`, `Core/DevCapture.Measure.cs:213` |
| 몬스터 정의 | `MonsterDatabase`(hp·dmg·lines·speed·look·patterns), 내보내기 필드 id·name·kind·hp·damage·xp·boss·raid·noLoot·gold·bossGear·xpByLevel(30칸) | `Enemies/MonsterDatabase.cs`, `server/data/monsters.json` |

## 5. 던전·레이드

| 항목 | 사실 | 근거 |
|---|---|---|
| 요일 던전 5 | 월 황금 광맥 / 화 버려진 제련소 / 수 마력의 묘지 / 목 수련의 숲 / 금 망자의 무기고, 하루 3회(주말 전부 개방) | `Dungeon/DungeonDatabase.cs:294-383` |
| 일반 던전 3 | 해골 광산 소굴 / 안개 낀 묘지 / 얼어붙은 회랑, 매일 무제한 | `:416-459` |
| 레이드 4 정의 | 해골왕(Lv20, 1장 중간, 수·토·일, 열쇠 20~50) / 흑철의 바르가스(Lv40, 1장 최종, 일, keyCost 60) / 바위 심장(Lv30, 2장 중간) / 수호자 그라흐(Lv40, 2장 최종) | `:481-548` |
| 열린 레이드 | `RaidDefs = { RaidDef(해골왕), GrahDef }`만 목록·서버 내보내기. 바르가스·골렘은 `Upcoming`(닫힘, 서버 미내보내기) | `:562-565` |
| 해금 | unlockQuest 수락·완료 시(c1_fortress / c2_grah), ContentUnlocks.Raid도 c1_fortress | `Dungeon/DungeonDirector.cs:112-120`, `Quest/ContentUnlocks.cs:42` |
| 패턴 종류 | Slash3, Charge, Slam, Fields, Donut, ArrowVolley, BoltRing, Summon, Guard, GoldRain, Judgment(망자의 심판), Repel(파동), Annihilate(섬멸), RuinRitual(파멸의 의식 토템), MarkOfFate(낙인). 레이드 전용: Seal(결계 의식) `BossBrain.Seal.cs`, Doom `BossBrain.Doom.cs`, 광폭화·절망의 포효 `BossBrain.Raid.cs` | `Enemies/MonsterDatabase.cs:30-47`, `Docs/PLAN_RAID_MECHANICS.md` |
| 레이드 보상 | 카드 4장 뽑기: 해골왕 골드 3,000~4,500·재료 1/2/4/8·장비(에픽 85/유니크 14/레전더리 1), 그라흐 골드 12,000~18,000·재료·장비(에픽 58/유니크 41/레전더리 1). 혼자+AI도 보상(RAID_REWARD_MIN_HUMANS 1) | `Dungeon/RaidRewards.cs:39-74`, `server/src/config/env.ts:86` |
| 서버 레이드 | `server/src/domains/raids/`는 상태 조회(해금·열쇠·보상·영웅)만, 클리어 검증은 dungeons 도메인(dungeonResult.ts) | `server/src/domains/raids/raidService.ts` |

## 6. 콘텐츠 해금·퀘스트·이야기

| 항목 | 사실 | 근거 |
|---|---|---|
| 해금 규칙 13개 | Enhance c1_trail / Gem·AutoHunt c1s_patrol / WeekdayDungeon c1s_ruins / NormalDungeon·PartyFinder c1s_firstdungeon / Auction·Trade·MiniDungeon c1s_depths / Daily·SkillStyle·Guild career_path / Raid·Promote c1_fortress(Raid는 수락 시) | `Quest/ContentUnlocks.cs:30-44` |
| 퀘스트 데이터 | 124개(main 42, sub 82), 챕터 1: 52개, 2: 72개. 필드 chapter·code·giver·id·kind·minLevel·offerDialogue·requires·requiresFlags·reward·startCutscene·steps(cutscene·objectives·setFlags·text)·summary·title·turnIn·turnInDialogue. objective type: kill 102, talk 15, flag 15, cutscene 10, dungeon 6, collect 6, interact 5, reach 3, quests 3, level 2, raid 2 | `Assets/Resources/Data/Quests.json` |
| 메인 사슬 | c1_morning→festival→burning→ashes→rise→rebuild→trail→crossing→quarry→scout→stronger→fortress→bargas→road→c2_canyon→trial→priest→mine→growth→golem→north→grah→c2_end, 그 사이 w_*·s_*·c1s_*·c2s_* 안내·지역 메인 | 같은 파일 |
| 컷신 | 12개, cmds: line·letterbox·spawn·move·place·fadeOut/In·shake·despawn·tint·wait·title·sfx·fire·flag·emote·camera·music·enemies·face·clearProps·dialogue·cameraReset·unflag·heal·travel·leave | `Assets/Resources/Data/Cutscenes.json`, `Quest/CutscenePlayer.cs` |
| 대사 | 270개, { id, lines[{speaker,text}] } | `Assets/Resources/Data/StoryDialogues.json` |
| 자동 진행 | 1장(1-1~2-1)만 자동 플레이(`QuestAutoPilot.ChapterOne.cs`), 2장은 안내·성장 추천(`QuestAutoPilot.Growth.cs`·`GrowthGuide.cs`). 새 챕터는 자동 진행 대상 아님, 퀘스트 처치는 `QuestAutoPilot.SubQuests.cs`가 사냥터 목표로 보냄 | `Quest/QuestAutoPilot.ChapterOne.cs:1-12` |
| 스토리 SSOT | 1~2장 구현, 3~5장 줄거리만. 7절 마야 모티프 | `Docs/STORY.md` |

## 7. 라이브 콘텐츠의 Lv50 접점

| 콘텐츠 | 지금 | 바꿔야 할 지점 |
|---|---|---|
| 미니 던전 | 티어 10/20/30(Lv10~39), 처치당 막대/8000(막힘 /800), 골드 36, 하루 30분 | `MiniDungeon/MiniDungeonCatalog.cs:23-25,39,46`, `server/src/domains/minidungeon/miniDungeonRules.ts:10-11`(직접 40) |
| 월드보스·길드보스 | Lv40 고정(min_level = maxLevel, BOSS_LEVEL 40), 보스 Lv40 체력 x46 | `server/src/domains/bossrush/bossRushRules.ts:15`, `bossRushService.ts:66,83`, `BossRush/BossRushCatalog.cs:16,22` |
| 랭킹 | 전투력 = 서버가 착용 장비로 계산 | `server/src/domains/rankings/rankingRepository.ts:41` |
| 일일·주간 의뢰 | 밴드 36~999가 Lv40+를 덮지만 보상 수치는 만렙이면 골드만 | `server/data/daily_quests.json`, `dailyService.ts:97` |
| 수입 상한 | 밴드 34~40까지만 | `server/data/income_caps.json` |
| 요일 던전·일반 던전 | 영웅 단계 hpMul 7.7/12, 권장 레벨 고정 | `DungeonDatabase.cs:294-459` |
| 성소·지하 사냥터 | 이미 Lv40~55 몬스터 존재, 만렙이라 경험치 0 | `HuntingGrounds.cs:67-74` |

## 8. 서버 잠금 장치

| 장치 | 사실 | 근거 |
|---|---|---|
| 데이터 버전 | 클라이언트 `DataVersion.txt`와 서버 `data_version.json` 해시가 다르면 로그인 거부(426) | `Net/OnlineSession.cs:76-80`, `server/src/gamedata/loader.ts:16` |
| 닫힌 콘텐츠 | 레이드는 `DungeonDatabase.Upcoming`(목록 제외 + 서버 미내보내기). 다른 콘텐츠는 퀘스트 해금(ContentUnlocks)으로만 잠김. 서버 기능 플래그는 RAID_REWARD_MIN_HUMANS·RAID_PRACTICE_PAYS_KILLS·POLICY_CONSENT_ENFORCE·JOBS_ENABLED·AUTH_DEV_*·ADMIN_* 뿐, "챕터 개방" 플래그 없음 | `DungeonDatabase.cs:565`, `server/src/config/env.ts:21-231` |
| 점검 모드 | maintenance 창(MAINT_PRE_BLOCK_MINUTES) | `env.ts:203`, `Net/OnlineSession.cs:74-75` |
| gameconfig | usableItems·storageCapacity·chestReward만 | `server/data/gameconfig.json` |

## 9. 밸런스·측정 도구

| 도구 | 사실 | 근거 |
|---|---|---|
| theory_progress.py | Lv40 하드코딩 13곳, TIERS 8단계, 실측 kills/min은 Lv40 이하 행만, 퀘스트 사슬 c2_end까지 | `Tools/balance/theory_progress.py:47,100,173,202,220,298,305,322,326,348,364,369,381` |
| theory_gold.py | KIT Lv40까지, TIERS 공유, 미니 골드 옵션 | `Tools/balance/theory_gold.py:61-66` |
| player-sim v3 | extract가 tierLevels·totalTo40 전제 | `analysis/player-sim/tools/extract-dotrpg-v3.mjs:25,60` |
| 측정 모드 | hunting은 `HuntingGrounds.All` 전부(새 사냥터 자동), dungeons·raids·bossrush는 정의 목록. 영웅 Lv은 권장 레벨 | `Core/DevCapture.Measure.cs:206-213`, `DevCapture.Measure.BossRush.cs` |

## 10. 아트 파이프라인

| 항목 | 규격 | 근거 |
|---|---|---|
| 캐릭터 프레임 | 생성 시트 → `char_<id>_<dir>_<frame>.png` 128px(발 12px 위, ppu 72) 또는 `--dot` 64px(발 7px 위, ppu 36). 방향 down/downside/side/upside/up x idle0·idle1·walk0~3·attack·hurt = 40장 | `Tools/art/process_characters.py:1-16`, `Assets/Scripts/Editor/GeneratedArtImport.cs:136-148`, `Art/RegionalMonsterArt.cs:13-14` |
| 보스 | 같은 40장 규격(심연의 거신·골렘 사례), MonsterDatabase `look`·`artScale`로 크기 | `Enemies/MonsterDatabase.cs:492-499,529-548` |
| 아이콘·배너·로고 | 아이콘 64px ppu 64, 배너 `banner_<dungeon id>` 480x160 ppu 100, 로고 ppu 100, 메뉴 아이콘 64x64, UI 9-slice ppu 32 | `GeneratedArtImport.cs:104-128` |
| 장비 아이콘 | `Art/Gear/eqicon_{id}`, 없으면 티어 밴드 대체 | `GearCatalog.cs:196-201,292` |
| 스킬 아이콘 | `career_<id>`, `gem_<id>` | `CareerData.cs:40`, `SkillData.cs:63` |
| 맵 그림 | 마을 텍스트 맵 + HD 등록(`register_map_hd.py`, `upscale_surface.py`), 소품 `register_world_props.py`, 숲 `register_forest_fields.py` | `Tools/art/` |
| 생성 경로 | Flow(Nano Banana) 기본, 마젠타 → 크로마키(`process_generated.py`), 아틀라스 임포트 `GeneratedArtImport.Import` | `Tools/art/flow_image_gen.mjs`, 메모리 project_dotrpg_art_pipeline |
| 기존 생성 기록 | 캐릭터·소품 프롬프트 문서 | `Docs/RESEARCH_CHARACTERS.md`, `Docs/VILLAGE_*_PROMPTS.txt`, `Docs/Monsters/` |

## 11. 챕터 3 설계에 제약이 되는 사실

1. 만렙은 `Progression.MaxLevel` 한 곳이 SSOT지만, 서버·도구·데이터에 "40"이 직접 박힌 곳이 20곳 넘는다(2.1절). 미니 던전 티어·월드보스 레벨·수입 상한 밴드·몬스터 xpByLevel 30칸·theory_progress.py 13곳은 상수 참조가 아니라 숫자다.
2. 스킬 구조는 "슬롯당 직업 스킬 1개 고정 + 보조 젬 2개 + 전직 노드 랭크 3"이다. 발사체 수·갈래·연쇄 수는 각 스킬 함수 안에 상수로 박혀 있어(IceSpears 3발, ChainLightning 5연쇄, ShieldThrow 3~5) 데이터로 바꿀 수 없고, 서버 검증(careerIds·tier·레벨)도 id 목록 고정이다. 전직 스킬은 repeats·leech·bossPct 보조 효과가 꺼져 있다.
3. 장비 티어 배열 { 1,…,40 }과 레전더리(Lv20·40만), 강화 GoldByTier 8칸, 수입 상한 밴드가 모두 "Lv40이 끝"을 전제한다. 지하 B1~B4(Lv40~55 몬스터)와 성소 4곳은 이미 있어 Lv41+ 사냥터가 먼저 쓸 수 있지만 경험치 표(xpByLevel 30칸)가 Lv30까지만 있다.
4. 레이드는 챕터당 중간(수·토·일, 열쇠 조각)·최종(일, keyCost 60) 구조와 보상 카드 4장·재료 상점이 고정 틀이고, 바르가스·바위 심장은 정의만 있고 `Upcoming`으로 닫혀 있다. 서버에는 "챕터 개방" 플래그가 없어 잠금은 퀘스트 해금과 데이터 미내보내기뿐이다.
5. 이야기 데이터는 Quests.json(objective 11종)·Cutscenes.json(cmd 27종)·StoryDialogues.json으로 충분하지만, 자동 진행은 1장만 되고 새 지역은 HuntingZone 한 줄 + 테마 variant 절차 생성이라 "마족 마을" 같은 새 테마는 WorldBuilder 테마 추가(SanctumField·Underground처럼 분기)와 아트 파이프라인(40장 캐릭터, 몬스터 종족 아틀라스, 배너 480x160)이 필요하다.
