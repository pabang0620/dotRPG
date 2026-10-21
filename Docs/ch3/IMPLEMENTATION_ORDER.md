# 챕터 3 구현 순서 (2026-10-21)

의존 순서대로 8단계. 단계 안에서는 파일 소유를 나눠 병렬로 할 수 있다. 각 단계는 "완료 기준"을 통과해야 다음 단계로 간다. 기간 견적은 쓰지 않는다. 서버 DB 마이그레이션은 0049(1단계 계승)·0050(5단계 보스 시즌 레벨) 두 개다. 커밋은 단계마다 한 번, 브랜치 wonho.

## 0단계: 만렙 SSOT 정리

| 항목 | 내용 |
|---|---|
| 목적 | "40"이 직접 박힌 곳을 상수·플래그 참조로 바꿔 이후 단계가 숫자 하나로 움직이게 한다 |
| 서버 | `env.ts` `CHAPTER3_OPEN`, `config`에 `levelCap()`. `economyContext.grantXp`·`dailyService`·`miniDungeonRules.tierOf`·`bossRushRules.BOSS_LEVEL`·`bossRushService`를 `levelCap()`으로. `/meta`에 `chapter3_open`·`level_cap` |
| 클라이언트 | `Progression.MaxLevel = 50`, `Progression.LevelCap`(서버 meta 또는 GameConfig `chapter3Open`), `Chapter3.Enabled`. `Progression.cs:63,77`, `WorldMapScreen.Atlas.cs:292`, `MercenaryDatabase.cs:161`, `BossRushCatalog.cs:22`를 `LevelCap`으로. `OnlineSession` meta 읽기 |
| 도구 | `theory_progress.py`·`theory_gold.py`·`extract-dotrpg-v3.mjs`에 `MAX_LEVEL` 상수 |
| 테스트 | `server/test/` 10개 파일의 Lv40 기대값을 상한 상수 참조로. 새 `chapter3Lock.test.ts`(플래그 0일 때 Lv40 멈춤, 1일 때 41 진행) |
| 소유 | 서버 1명(config·economy·dailies·minidungeon·bossrush·system meta), 클라 1명(Progression·World·Party·BossRush·Net), 도구 1명 |
| 완료 기준 | 플래그 0에서 기존 테스트 전부 통과, 데이터 버전 불변. 플래그 1에서 Lv40 캐릭터가 경험치를 받아 41이 된다(테스트) |

## 1단계: 데이터·곡선·장비

| 항목 | 내용 |
|---|---|
| 곡선 | `progression.json` xpToNext 50칸(공식 그대로), `GameDataExport`의 monsters `xpByLevel` 50칸 |
| 장비 | `GearCatalog` TierLevels 45·50, WeaponBase·HealthBase·BlockBase, 이름 80개(`ART_LIST.md` 6절), 레전더리 Lv50, 아이콘 밴드 8~9 |
| 강화·판매 | `EnhanceRules.GoldByTier` 9,830·12,780. `GearSell`은 levelTier 공식으로 자동(변경 없음) |
| 승급·계승 | `PromoteRules.InheritCost`, 서버 `promote` 도메인 `inherit` 엔드포인트(트랜잭션·행 잠금·request_id·원장 사유 `inherit`) |
| 밴드·보상 | `income_caps.json`(theory_income.py 재생성), `daily_quests.json` 두 밴드, `level_rewards.json` 45·50, `achievementDefs.ts` level_50·spec_first·spec_all |
| 마이그레이션 | 0049: `gold_ledger` 사유 CHECK에 `inherit`, `anomaly_log` kind에 `inherit` |
| 소유 | 클라 장비·규칙 1명, 서버 promote·데이터 1명 |
| 테스트 | `enhance.test.ts`·`promote.test.ts` 티어 9·10, `inherit.test.ts`(등급별 비용, 강화 유지, 커먼~레어 거부, 닫힌 상태 거부, 멱등) |
| 완료 기준 | 내보내기 뒤 `theory_gold.py --to 50`이 Lv50 KIT 기준 0.80~0.95(미세 조정은 7단계), 테스트 통과 |

## 2단계: 특화

| 항목 | 내용 |
|---|---|
| 시작 조건 | 기본 스킬 10개의 시전 함수 목록 확정: `SkillCaster.cs:166-187` `Cast` 스위치 → `SkillCaster.Skills.cs`의 `Crush`·`Lance`·`Whirl`·`WarCry`·`Nova`·`FrostOrb`·`Blades`·`Meteor`·`FireField`, `SkillCaster.Charge.cs`의 `Charge`(`SKILL_SPECIALIZATION.md` 5절) |
| 데이터 | `Assets/Resources/Data/SkillSpecs.json` 96건(`SKILL_SPECIALIZATION.md` 3절 표 그대로), 내보내기 `server/data/skill_specs.json` |
| 수치 | `SkillSpecs.Apply(gem, side)`를 `SkillGems`·`CareerCatalog`의 SkillGem 생성 한 곳에 |
| 시전 | `CareerCombat.*.cs` 33개 함수 + 기본 스킬 10개 시전 경로의 상수를 `c.params`로. `extra` 태그 분기. `CareerFx.SkillColor`·`Rim`로 색 교체(99곳) |
| 저장·UI | `CareerSave.specs`, `Progression.Career.cs` SpecOf/SetSpec/SpecUnlocked, 스타일에 specs, 스킬 창 특화 탭, 아이콘 테두리, 이츠나·익시 대화 항목, 선택 시 쿨타임 |
| 서버 | `careerSchema` v2 + specs, `styleSchema` specs, `validateCareer` 규칙 6개, `economyData` skillSpecs, `AOE_CAP` 8 |
| 도구 | `Tools/balance/theory_specs.py` |
| 소유 | 데이터·수치·도구 1명, 시전 함수 Fighter+Guardian 1명, Arcanist+Bishop+기본 10개 1명, UI·저장 1명, 서버 1명 |
| 테스트 | `skillSpecs.test.ts` 6개 항목, Unity 컴파일 |
| 완료 기준 | `theory_specs.py`가 96줄 전부를 내고 A·B 차이 10% 초과 줄이 없다. 측정 모드 `-measureSpecs a|b|none` 사냥 3종이 같은 사냥터에서 처치 속도 ±15% 안 |

## 3단계: 세계·퀘스트

| 항목 | 내용 |
|---|---|
| 아트 선행 | `ART_LIST.md` 1~3절(마을 타일·소품·NPC 8명·몬스터 5종)을 이 단계 시작 전에 만든다 |
| 세계 | `MapTheme.DemonTown`·`DryCanyon`, WorldBuilder 분기, `Maps/Sayab.txt`, `MapRegistry.Sayab`, `HuntingGrounds` 3줄, `MonsterDatabase` 5종, NPC 8명 배치·상점·대장간·게시판, 월드맵 노드·귀환 목적지, BGM |
| 이야기 | `Quests.json` 메인 12 + 서브 10, `Cutscenes.json` 8, `StoryDialogues.json`, `I18N_GLOSSARY.md`, `STORY.md` 3장 절 갱신 |
| 요일 던전 | `DifficultyDef Lord`, `DungeonClearFloor` 벤치마크 45, 던전 창 탭 |
| 서버 | `quest_index.json` 내보내기로 자동. `dungeons.json`에 군주 난이도. 퀘스트 보상 검증은 데이터 기반이라 코드 변경 없음 |
| 소유 | 세계(맵·테마·몬스터·NPC) 1명, 이야기(퀘스트·컷신·대사) 1명, 요일 던전 1명 |
| 테스트 | `quests.test.ts` 3장 사슬(requires·minLevel·플래그), `dungeons.test.ts` 군주. 번역 11개 언어(`i18n.py check` 0) |
| 완료 기준 | 측정 모드 `-measureOnly hunting`이 dry_* 3곳에서 분당 15~20, 군주 던전 혼자 + AI 3명 120~300초. 3장 메인을 자동 처치 안내로 끝까지 진행(DevCapture 3장 체크) |

## 4단계: 레이드

| 항목 | 내용 |
|---|---|
| 아트 선행 | 보스 2종 프레임, 배너 2장, 레이드 방 맵 5장(`ART_LIST.md` 3·5절) |
| 정의 | `DungeonDatabase` XibDef·KamatzDef(`RAIDS.md` 표), `RaidDefs`에 추가, `MapRegistry` DgnXib1/2/Boss·DgnKamatz1/Boss |
| 보스 | `MonsterDatabase` boss_xib·boss_kamatz, 신규 기믹 2개(`BossBrain.VoidRift.cs`, `BossBrain.GreyBrand.cs`), `CompanionBrain` 균열 밟기·낙인 나누기 |
| 보상 | `RaidRewards.Xib`·`Kamatz`, 재료 2종, `RaidShop` 4개, `IsRaidMaterial` |
| 서버 | 레이드 id·보상 표·상점은 내보내기 데이터. `dungeonResult`의 닫힘 거부 목록에 두 id |
| 소유 | 정의·보상 1명, 보스 기믹·AI 1명 |
| 테스트 | `raidRewards.test.ts`·`raidShop.test.ts` 3장 항목, `raids.test.ts` 해금·열쇠, 측정 `-measureOnly raids -measureRaid raid_xib|raid_kamatz` |
| 완료 기준 | 측정 봇 일반 난이도 클리어 절반 이상, 기믹 수행 로그(균열 닫힘 횟수, 낙인 나눔 횟수) 기록 |

## 5단계: 라이브 콘텐츠

| 항목 | 내용 |
|---|---|
| 미니 던전 | 티어 40 정의·맵, 티어별 상수(40: 36,000/3,600), 서버 MINI_TIERS·티어별 상수 |
| 월드·길드보스 | 입장 레벨 40 고정, 보스 레벨 = levelCap, 시즌 `boss_level`(마이그레이션 0050: `boss_seasons.boss_level INT NOT NULL DEFAULT 40`), 월드보스 체력 x2.0 |
| 의뢰 | 밴드 2개, 주간 템플릿 |
| 해금·안내 | `ContentUnlocks` Chapter3·Specialization, 할 일 줄 3개, 성장 추천 사냥터·군주 |
| 소유 | 서버 1명, 클라 1명 |
| 테스트 | `miniDungeon.test.ts` 티어 40, `bossRush.test.ts` Lv50 시즌, `dailies.test.ts` 밴드 |
| 완료 기준 | Lv41 캐릭터로 할 일 창에 특화·군주·사냥터 줄이 뜨고 각 "가기"가 동작(DevCapture 체크) |

## 6단계: 잠금

| 항목 | 내용 |
|---|---|
| 서버 | 닫힘 거부 목록 전부(`LIVE_CONTENT.md` 6절, 사냥터·필드 세션 진입 포함), `/meta`, README 3절·10절 |
| 클라이언트 | 닫힌 상태에서 특화 탭·군주 탭·사얍 노드·3장 퀘스트·레이드가 보이지 않음. 귀환 주문서·파티 따라가기·성장 추천의 3장 맵 이동 차단(`MapRegistry.IsChapter3`). 오프라인 `GameConfig.chapter3Open` |
| 테스트 | `chapter3Lock.test.ts` 전체. DevCapture `-dotrpgChapter3Lock`: 닫힘·열림 두 번 돌려 화면 요소 유무 확인 |
| 완료 기준 | 마스터 문서 6절 완료 기준 1·2 |

## 7단계: 측정·시뮬·테스트

| 항목 | 내용 |
|---|---|
| 도구 | `theory_progress.py`·`theory_gold.py` Lv50, `theory_specs.py`, player-sim v3 extract 50 |
| 측정 | 측정 빌드(`BuildWindowsQA`)로 사냥(dry_* 3 + 지하 4, 특화 a/b/none), 던전 군주 5 x 2, 레이드 2 x 2 난이도 x 직업 4, 보스 방 Lv50. 폴더 분리·`taskkill` 규칙은 메모리 절차 그대로 |
| 시뮬 | 20명 에이전트 30일 x 10시드, 90일 x 5시드(Lv40 시작 캐릭터 포함) |
| 조정 | 40→50 시간 22~27시간, 골드 0.85~0.90, 레이드 일반 클리어 절반 이상, 보스 방 피해 상한 50% 아래. 조정 손잡이: 3장 퀘스트 XP·골드, `GoldByTier[8..9]`, 미니 던전 40 상수, 몬스터 체력, 레이드 hpMul |
| 서버 | 전체 테스트(메모리 부족 시 나눠서) |
| 문서 | `BALANCE_MEASURE`에 3장 절, `TEST_LOG.md` 줄, `PROGRESS.md`, `PLAN_SERVER.md` |
| 완료 기준 | 마스터 문서 6절 3~7 |

## 공통 규칙

- 파일 500줄 상한: `CareerCombat.*.cs`·`DungeonDatabase.cs`·`MonsterDatabase.cs`가 넘으면 partial 분할(`DungeonDatabase.Chapter3.cs`, `MonsterDatabase.Chapter3.cs`, `CareerCombat.Fighter.Specs.cs`).
- 새 ko 문구는 L.T/L.F로 쓰고 단계 끝에 11개 언어 번역(`i18n.py check` 0, `sync-server`).
- 단계 끝에 Unity 컴파일·내보내기, 관련 서버 테스트, 커밋. 빌드·배포는 요청이 있을 때만.
- 출시 전까지 서버 `.env`에 `CHAPTER3_OPEN`을 쓰지 않는다(기본 0).
