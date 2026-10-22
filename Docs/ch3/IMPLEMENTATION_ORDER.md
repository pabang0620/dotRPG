# 챕터 3 구현 순서 (2026-10-21, 검토 반영판)

의존 순서대로 8단계. 단계 안에서는 파일 소유를 나눠 병렬로 할 수 있다. 각 단계는 완료 기준을 통과해야 다음 단계로 간다. 기간 견적은 쓰지 않는다. 서버 DB 마이그레이션 번호: 0049_gear_inherit(출시용 장비 계승, wonho에서 옴. 40→50 계승도 같은 원장 사유를 쓴다)·0050_rogue_class(출시용 새 직업 도적, wonho에서 옴)·0051_chapter3_seal_exchange(1단계 아카브 교환)·0052_lord_difficulty(3단계 군주 난이도 CHECK)·0053(5단계 보스 시즌 레벨, 계획). 출시 서버에는 0049부터 아직 적용 전이다. 커밋은 단계마다 한 번, 브랜치 ch3. 출시 빌드는 3장을 포함한 빌드이고 잠금 회귀 테스트는 매 빌드 항목이다.

## 0단계: 만렙 SSOT·잠금 뼈대

| 항목 | 내용 |
|---|---|
| 목적 | "40"이 직접 박힌 곳을 상수·플래그 참조로 바꾸고, 챕터 태그·assertOpen 뼈대를 먼저 세워 이후 단계가 전부 그 위에 올라가게 한다 |
| 서버 | `env.ts` `CHAPTER3_OPEN`, `config.levelCap()`, `gamedata/chapterGate.ts`의 `assertOpen(kind, id)`(태그는 1단계부터 채운다). `economyContext.grantXp`·`dailyService`·`miniDungeonRules.tierOf`·`bossRushRules`·`bossRushService`를 `levelCap()`으로. `/meta`에 `chapter3_open`·`level_cap` |
| 클라이언트 | `Progression.MaxLevel = 50`, `Progression.LevelCap`, `Chapter3.Enabled`(`GameConfig.chapter3Open`), `MapRegistry.IsChapter3`. `Progression.cs:63,77`, `WorldMapScreen.Atlas.cs:292`, `MercenaryDatabase.cs:161`, `BossRushCatalog.cs:22`. `OnlineSession` meta 읽기 |
| 데이터 | `daily_quests.json` 마지막 밴드 36~40 절단 |
| 도구 | `theory_progress.py`·`theory_gold.py`·`extract-dotrpg-v3.mjs`에 `MAX_LEVEL` |
| 테스트 | `server/test/` 10개 파일 Lv40 기대값을 상수 참조로. 새 `chapter3Lock.test.ts`: 플래그 0에서 Lv40 멈춤, 1에서 41 진행, `income_caps` Lv41+ 밴드 없음 거부, 일일 의뢰 밴드 절단 뒤 Lv40 그대로 |
| 소유 | 서버 1명, 클라 1명, 도구·데이터 1명 |
| 완료 기준 | 플래그 0에서 기존 테스트 전부 통과, 데이터 버전 불변. 플래그 1에서 Lv40 캐릭터가 41이 된다 |

## 1단계: 데이터·곡선·장비·계승

| 항목 | 내용 |
|---|---|
| 곡선 | `progression.json` xpToNext 49칸, `monsters.json` xpByLevel 50칸, `GameDataExport` 단언 |
| 장비 | `GearCatalog` TierLevels 45·50, WeaponBase·HealthBase·BlockBase, 이름 80개, 레전더리 Lv50, 아이콘 밴드 8~9 |
| 강화 | `EnhanceRules.GoldByTier` 8,690·10,000. `GearSell`은 공식 자동 |
| 계승 | 출시용 `InheritRules` 경로표에 40→50 한 칸(유니크·레전더리만, 공통 공식 골드 + 핵·침식 결정·잿빛 인장, `MaterialCost`), 결과 키 `eq_{kind}_50_{grade}+N^k`·원본 소비·`enhancePity` 이전·경매 가격 키 분리. 서버 `inherit` 도메인(`POST /characters/:uuid/inherit`, `assertOpen`, 원장 `inherit`). 옛 `PromoteRules.InheritCost`·`/promote/inherit`는 2026-10-22 wonho 병합 때 없앴다 |
| 아카브 교환 | 상점 데이터에 교환 품목(침식 결정 5 + 20,000 → 잿빛 인장 1, 주 2회) |
| 밴드·보상 | `income_caps.json`(theory_income.py), `daily_quests.json` 두 밴드 + 리아의 약초밭 템플릿, `level_rewards.json` 45·50, `achievementDefs.ts` level_40 문구·level_50·spec_first·spec_all |
| 태그 | `economyData`에 chapter:3 태그(장비·재료·상점·레이드 id·맵 id·몬스터 id·특화) |
| 마이그레이션 | 0049_gear_inherit(출시용): `gold_ledger`·`item_ledger` 사유 `inherit`, 경매 키 `^k`. 0051_chapter3_seal_exchange(도적 병합 때 0050에서 옮김): `gold_ledger` 사유 `seal_exchange`, 주간 카운터 `seal_exchange` |
| 세이브 | 클라 세이브 버전 8(옛 빌드 거부, `specs` 누락 null 안전) |
| 소유 | 클라 장비·규칙·세이브 1명, 서버 inherit·데이터·태그 1명 |
| 테스트 | `enhance.test.ts`·`promote.test.ts` 티어 9·10, `inherit.test.ts`(출시용 경로 + 40→50 재료·공통 골드, 유지·실패, 커먼~에픽·Lv45·Lv50 거부, 닫힌 상태 거부, 멱등, 결과 키), `shop.test.ts` 교환 상한 |
| 완료 기준 | 내보내기 단언 통과, `theory_gold.py --to 50 --mini 20`이 5절 표대로 0.85~0.90, 테스트 통과 |

## 2단계: 특화

| 항목 | 내용 |
|---|---|
| 시작 조건 | 기본 스킬 10개 시전 함수 목록 확정(`SkillCaster.cs:166-187` `Cast` → `SkillCaster.Skills.cs` `Crush`·`Lance`·`Whirl`·`WarCry`·`Nova`·`FrostOrb`·`Blades`·`Meteor`·`FireField`, `SkillCaster.Charge.cs` `Charge`) |
| 데이터 | `SkillSpecs.json` 96건 + `points`(`SKILL_SPECIALIZATION.md` 3·4절), 내보내기 `skill_specs.json` |
| 수치 | `SkillSpecs.Apply(gem, side)` 한 곳 |
| 시전 | `CareerCombat.*.cs` 33개 함수 + 기본 10개의 상수를 `c.params`로. `extra` 태그 분기. 엄폐(`SafeCover`)·정면 방어(`Guard`) 재사용. `CareerFx.SkillColor`·`Rim`(99곳) |
| 저장·UI | `CareerSave.specs`, `Progression.Career.cs` SpecOf/SetSpec/SpecPoints, 스타일에 specs, 특화 탭(포인트·비용·축), 아이콘 테두리, 이츠나·익시 대화, 선택 시 쿨타임 |
| 서버 | `careerRules.ts` careerSchema v2 + specs, styleSchema specs, validateCareer 규칙(포인트 합·직업·중복·미전직·assertOpen), `economyData` skillSpecs, ctx `aoeCap()`(Lv41+ 확산형 있을 때만 8) |
| 도구 | `Tools/balance/theory_specs.py`(축별 기대값 + 보스 방 상한 비율) |
| 소유 | 데이터·수치·도구 1명, Fighter+Guardian 1명, Arcanist+Bishop+기본 10개 1명, UI·저장 1명, 서버 1명 |
| 테스트 | `skillSpecs.test.ts` 7개 항목, Unity 컴파일 |
| 완료 기준 | `theory_specs.py`가 96줄 전부를 내고 축별 차이 10% 초과 줄과 상한 비율 0.8 이상 줄이 없다. 측정 모드 `-measureSpecs a|b|none` 사냥 3종이 같은 사냥터에서 처치 속도 ±15% 안(근거: 2026-10-16 실측에서 같은 사냥터의 직업 간 처치 속도 편차가 ±15%이고, 특화가 그 편차를 넘지 않아야 사냥 밸런스가 유지된다) |

## 3단계: 세계·퀘스트

| 항목 | 내용 |
|---|---|
| 아트 선행 | `ART_LIST.md` 1~3절(사얍 건물 3·사냥터 타일·소품, NPC 8, 몬스터 7 + 정예 1) |
| 세계 | `MapTheme.DryCanyon`, WorldBuilder 분기, `Maps/Sayab.txt`(Canyon 테마), `MapRegistry.Sayab`, `HuntingGrounds` 3줄, 필드 공허 균열(dry_rift), 정찰대장 등장 규칙, `MonsterDatabase` 7종 + 정예, NPC 8명·거래 잠금 플래그·상점·대장간·게시판, 월드맵 노드(서쪽)·귀환 목적지, BGM |
| 이야기 | `Quests.json` 메인 12 + 서브 10, `Cutscenes.json` 12, `StoryDialogues.json`(NPC 성격 대사 포함), 리아 재회 컷신(선택지 2 같은 분기, 1장 키 재사용), `I18N_GLOSSARY.md`, `STORY.md` 3장 절. 3장 텍스트는 `chapter3.json` 묶음으로 분리(6단계에서 로딩 경로 완성) |
| 동료 | 쿠크 `StoryCompanions` 임시 동료, 3-6 레이드 1·2번 방 AI 슬롯 강제 편성, 3-7 방어전 웨이브·수비대 AI 동참, 발체 3단계 |
| 요일 던전 | `DifficultyDef Lord`, `lordPatterns`, 군주 확정 보상, `DungeonClearFloor` 벤치마크 45, 던전 창 탭 |
| 서버 | `quest_index.json`·`dungeons.json` 내보내기, 태그 |
| 소유 | 세계(맵·테마·몬스터·NPC) 1명, 이야기(퀘스트·컷신·대사) 1명, 동료·방어전·요일 던전 1명 |
| 테스트 | `quests.test.ts` 3장 사슬, `dungeons.test.ts` 군주. 번역 11개 언어 0 |
| 완료 기준 | 측정 `-measureOnly hunting`이 dry_* 3곳에서 분당 15~20, 군주 던전 혼자 + AI 3명 120~300초. DevCapture 3장 체크가 3-1~3-끝을 끝까지 진행 |

- 방어전·동료 약속(이야기 포크가 퀘스트 데이터에 쓰는 값): 3-7 `c3_defense`의 방어 단계 목표는 `{"type":"flag","target":"c3_defense_won","map":"dry_floor"}`. 그 단계가 현재이고 주인공이 dry_floor에 있으면 `StoryDefense`가 웨이브 4회(grey_ranger·grey_priest)를 서쪽에서 보내고 쿠크(`story_kukum`)·카나(`story_kana`)가 게스트로 함께 싸운다. 마지막 웨이브를 막으면 플래그가 서고 단계가 끝난다. 쓰러지거나 맵을 떠나면 다음 방문에 다시. 쿠크는 3-3 완료부터 3-끝 전까지 AI 동료 창에서 고를 수 있고(강제 편성 없음), 3-6 완료 전 `raid_xibak` 1·2번 방에는 게스트로 들어왔다가 보스 방에서 빠진다(`StoryCompanions.ForcedSlot`). 발체는 3-3~3-5 피하고, 3-7~3-11 사라지고, 3-11 뒤 사얍에서 주인공 옆을 따른다(`BalcheMood`, npc id `balche`).
- 군주 난이도는 `DungeonDifficulty.Lord = 4`라 난이도 CHECK를 넓히는 마이그레이션 0052(`0052_lord_difficulty.sql`, 처음 0050이던 것을 장비 계승·도적 병합 때 두 번 옮겼다)가 3단계에 들어갔다. 5단계 보스 시즌 레벨은 0053이다.

## 4단계: 레이드

| 항목 | 내용 |
|---|---|
| 아트 선행 | 보스 2종 프레임, 배너 2장, 방 맵 5장 |
| 정의 | `DungeonDatabase` XibakDef·KamatzDef, `RaidDefs` 추가, `MapRegistry` 방 id |
| 보스 | `MonsterDatabase` boss_xibak·boss_kamatz, `BossBrain.VoidRift.cs`(치유형 균열, 사람 수별 닫는 시간, `Telegraph.SafeCircle` 노출), `BossBrain.GreyBrand.cs`(`RaidDoom.SpotFor` 방식, AI 50% 상쇄), 패턴별 외침 |
| 보상 | `RaidRewards.Xibak`·`Kamatz`, 재료 2종, `RaidShop` 4개, `IsRaidMaterial` |
| 소유 | 정의·보상 1명, 보스 기믹 1명 |
| 테스트 | `raidRewards.test.ts`·`raidShop.test.ts` 3장 항목, `raids.test.ts` 해금·열쇠, 측정 `-measureOnly raids -measureRaid raid_xibak|raid_kamatz` |
| 완료 기준 | 측정 봇 일반 난이도 클리어 절반 이상, 봇 코드 추가 없이 기믹 수행 로그(균열 닫힘·낙인 상쇄 횟수) 기록 |

## 5단계: 라이브 콘텐츠

| 항목 | 내용 |
|---|---|
| 미니 던전 | 티어 40 정의·맵, 티어별 `DailySeconds`(40은 600), 서버 MINI_TIERS·티어별 상한 |
| 월드·길드보스 | 입장 40 고정, 보스 레벨 = levelCap, 시즌 `boss_level`(마이그레이션 0053: `boss_seasons.boss_level INT NOT NULL DEFAULT 40`), 월드보스 체력 x2.0 |
| 의뢰 | 주간 템플릿 2개 |
| 해금·안내 | `ContentUnlocks` Chapter3·Specialization, 할 일 줄 3개, 성장 추천 |
| 소유 | 서버 1명, 클라 1명 |
| 테스트 | `miniDungeon.test.ts` 티어 40·10분, `bossRush.test.ts` Lv50 시즌·Lv40 참가, `dailies.test.ts` 밴드 |
| 완료 기준 | Lv41 캐릭터로 할 일 창에 특화·군주·사냥터 줄이 뜨고 이동이 동작(DevCapture) |

## 6단계: 잠금·스포일러

| 항목 | 내용 |
|---|---|
| 서버 | `assertOpen` 호출 지점 11곳 전부(`LIVE_CONTENT.md` 6절), `GET /content/chapter3`(열림에만 응답, 11개 언어 포함), README 3절·10절 |
| 클라이언트 | 닫힘에서 특화 탭·군주 탭·사얍 노드·3장 퀘스트·레이드 없음, `MapRegistry.IsChapter3` 경로 차단, `chapter3.json` 런타임 병합(로그인 뒤 캐시), 출시 빌드 제외 목록에 `chapter3.json` |
| 테스트 | `chapter3Lock.test.ts` 전체, `content.test.ts`(닫힘 404·열림 응답), DevCapture `-dotrpgChapter3Lock`(닫힘·열림 두 번) |
| 완료 기준 | 마스터 6절 완료 기준 1·2. 매 빌드 항목으로 등록 |

## 7단계: 측정·시뮬·테스트

| 항목 | 내용 |
|---|---|
| 도구 | `theory_progress.py`·`theory_gold.py` `--to 50`, `theory_specs.py`, player-sim v3 extract 50 |
| 측정 | 측정 빌드로 사냥(dry_* 3 + 지하 4, 특화 a/b/none), 던전 군주 5 x 2, 레이드 2 x 2 난이도 x 직업 4, 보스 방 Lv50(티어 10 + 집중형). 폴더 분리·`taskkill` 규칙은 메모리 절차 그대로 |
| 시뮬 | 20명 에이전트 30일 x 10시드, 90일 x 5시드(Lv40 시작 캐릭터 포함) |
| 조정 | 40→50 22~27시간, 골드 0.85~0.90, 레이드 일반 클리어 절반 이상, 보스 방 상한 비율 0.8 미만. 손잡이: 3장 퀘스트 XP·골드, `GoldByTier[8..9]`, 계승 골드, 미니 40 시간, 몬스터 체력, 레이드 hpMul |
| 서버 | 전체 테스트(메모리 부족 시 나눠서) |
| 문서 | `BALANCE_MEASURE`에 3장 절, `TEST_LOG.md`, `PROGRESS.md`, `PLAN_SERVER.md` |
| 완료 기준 | 마스터 6절 3~7 |

## 공통 규칙

- 파일 500줄 상한: `CareerCombat.*.cs`·`DungeonDatabase.cs`·`MonsterDatabase.cs`가 넘으면 partial 분할(`DungeonDatabase.Chapter3.cs`, `MonsterDatabase.Chapter3.cs`, `CareerCombat.Fighter.Specs.cs`).
- 새 ko 문구는 L.T/L.F로 쓰고 단계 끝에 11개 언어 번역(`i18n.py check` 0, `sync-server`). 3장 이야기 문구는 `chapter3.json` 안에서 번역한다.
- 단계 끝에 Unity 컴파일·내보내기, 관련 서버 테스트, 커밋. 빌드·배포는 요청이 있을 때만.
- 출시 전까지 서버 `.env`에 `CHAPTER3_OPEN`을 쓰지 않는다(기본 0).
