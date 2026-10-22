# 챕터 3 라이브 콘텐츠 (2026-10-21, 검토 반영판)

기준: `PLAN_CHAPTER3_2026-10-21.md` 4절 연결고리 표. 현재 값은 `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 7~8절.

## 1. 미니 던전 티어 40

| 항목 | 값 |
|---|---|
| 정의 | `MiniDungeonCatalog.All`에 Tier 40, MinLevel 40, MaxLevel 49, 이름 "잿빛 수련의 협곡", MapId `mini_dungeon_40`, MonsterId void_hound, Theme DryCanyon, Music `music_sayab` |
| 서버 | `MINI_TIERS = [10, 20, 30, 40]`, `tierOf`는 `level < levelCap`만 본다(직접 40 삭제) |
| 경험치·골드 | 막대/8,000, 따라잡기 /800(기존 비율 그대로), 처치 골드 36. 티어 40만 하루 10분(`DailySeconds` 티어별: 10·20·30은 1,800, 40은 600). 하루 200마리, 따라잡기가 거의 매일 켜지므로 11일 2,200마리 = 2.75막대, xpToNext 41~49 평균 700,000으로 1,900,000(40→50 전체 6,872,974의 28%) |
| 열림 | Lv40이고 `CHAPTER3_OPEN=1`일 때. 닫혀 있으면 Lv40은 지금처럼 미니 던전 없음 |
| 테스트 | `miniDungeon.test.ts`에 티어 40 입장·10분 상한·만렙 50 경계·닫힌 상태 거부 |

## 2. 월드보스·길드보스

| 항목 | 값 |
|---|---|
| 입장 레벨 | 40 고정(`BossRushCatalog.MinLevel`, 서버 `min_level`). Lv40~49도 계속 참가한다. 보스 레벨만 `levelCap()`(열리면 50)을 따르고 시즌 행에 `boss_level`을 기록한다(마이그레이션 0051) |
| 보스 | 같은 `boss_abyss_titan`(심연의 거신 지파크). 방 안 체력 x46·피해 x1.0·직업 배율(가디언 1.4, 아르카니스트 1.3, 비숍 2.2) 그대로 |
| 피해 상한 | 참가자 각자의 `killRules.attackCap(자기 레벨, 자기 착용 장비)` x 180초 x `DAMAGE_SLACK` 0.6(`PROGRESSION_AND_GEAR.md` 7절의 재계산으로 0.6 유지) |
| 월드보스 체력 | 시즌 생성 때 `WORLD_BOSS_HP`(기본 40,000,000)를 쓰고, 보스 레벨 50 시즌은 x2.0(80,000,000)으로 둔다. 조정은 env `WORLD_BOSS_HP`로 한다 |
| 길드보스 | 점수제 그대로. 보스 레벨만 따라간다 |

## 3. 랭킹

- 전투력은 서버가 착용 장비로 계산하므로 티어 9·10이 들어가면 자동 반영.
- 월드보스·길드보스 보드는 시즌 단위라 그대로. 새 보드 없음.

## 4. 일일·주간 의뢰

- `daily_quests.json`: 기존 마지막 밴드(36~999)는 36~40으로 자른다. 새 밴드 (41~45) `sayab_low`, (46~50) `sayab_high`, giver "사얍 마을 게시판". 골드 (41~45) 1,400, (46~50) 1,600. `maxLevelGold` 3,000 유지.
- 템플릿: 밴드마다 kill 4 + dungeon 1(군주 던전 1회) + collect 1 "리아의 약초밭"(giver 리아, 재 속 약초 5, dry_stones, 보상은 밴드 골드 + 회복 물약 10).
- 주간 의뢰 템플릿에 "시바크 레이드 1회"(45 밴드), "카마츠 레이드 1회"(50 밴드).

## 5. 해금·안내

`ContentUnlocks.Feature`에 두 개 추가.

| Feature | quest | 조건 | 팁 |
|---|---|---|---|
| Chapter3 | c2_end 완료 + `Chapter3.Enabled` | 메인 3-1이 레오나에게서 열린다 | "서쪽으로 가는 길이 열렸어요. 레오나를 찾아가세요." |
| Specialization | 없음(레벨) | Lv41 도달 + `Chapter3.Enabled` | "특화 포인트를 받았어요. 스킬 창의 특화 탭에서 두 갈래 중 하나를 고르세요(마을에서 바꿀 수 있어요)." |

- 할 일 창(`TodayScreen`): "특화 포인트 N점 남음"(남은 포인트가 있으면), "군주 던전 오늘 N회", "시바크·카마츠 레이드 열림". 순서는 기존 규칙.
- 성장 추천(`GrowthGuide`): Lv41+ 사냥터 추천에 dry_* 3곳과 지하 B1~B4를 레벨대로. 군주 던전 추천은 Lv45부터.
- 메뉴 잠금: 닫힌 상태에서는 특화 탭·군주 탭·사얍 노드가 보이지 않는다(흐리게가 아니라 없음).

## 6. 잠금: CHAPTER3_OPEN과 assertOpen

| 층 | 동작 |
|---|---|
| 서버 env | `CHAPTER3_OPEN: z.enum(['0','1']).default('0')`. `levelCap()` = 열리면 `progression.maxLevel`(50), 닫히면 40 |
| 데이터 태그 | `economyData` 로드 때 3장 항목에 `chapter: 3` 태그를 단다: 퀘스트(c3_*), 맵(dry_floor·dry_stones·dry_rift·sayab·mini_dungeon_40·DgnXibak*·DgnKamatz*), 던전(raid_xibak·raid_kamatz, 군주 난이도), 몬스터(void_*·grey_*·boss_xibak·boss_kamatz), 아이템(티어 9·10 장비, mat_raid_xibak·mat_raid_kamatz, 3장 수집품), 상점(xibak_*·kamatz_* 상자, 아카브 교환), 특화(전부) |
| 공통 검사 | `server/src/gamedata/chapterGate.ts`의 `assertOpen(kind, id)`: 태그가 3이고 플래그가 0이면 422 `CHAPTER_LOCKED`. 호출 지점: kills(처치 보고의 map_id·monster_id), fieldsessions(세션 생성 map_id), partyruns(start의 dungeon id·difficulty), dungeons(dungeonResult의 id·difficulty), quests(수락·보상 청구), shop(구매·판매 item_key), auction(등록 item_key), promote(계승), careerRules(specs), minidungeon(티어 40 입장), bossrush(보스 레벨 50 시즌 생성). grantXp는 `levelCap()`으로 멈춘다 |
| /meta | `chapter3_open`, `level_cap` 필드. 클라이언트 `OnlineSession`이 로그인 때 읽어 `Chapter3.Enabled`·`Progression.LevelCap`에 넣는다 |
| 클라이언트(오프라인) | `Chapter3.Enabled`의 빌드 기본값은 `Resources/Data/GameConfig`의 `chapter3Open`(출시 빌드 false). 오프라인 플레이는 이 값으로 같은 규칙 |
| 클라이언트(닫힘 경로) | 월드맵 노드 숨김 외에 귀환 주문서 목적지, 파티 따라가기(`PartyNet` 호스트 맵 추종), 성장 추천·할 일의 이동 버튼에서도 3장 맵으로 가지 않는다(`MapRegistry.IsChapter3(mapId)`) |
| 데이터 버전 | 3장 데이터(맵·몬스터·레이드·장비·특화·상점)는 전부 내보내고 데이터 버전에 들어간다. 플래그를 바꿔도 데이터 버전은 바뀌지 않으므로 재빌드 없이 연다 |
| 테스트 | `server/test/chapter3Lock.test.ts`: 닫힘에서 호출 지점 11곳 전부 422, 열림에서 전부 통과, 플래그 전환 전후 Lv40 캐릭터 경험치. 0단계 항목으로 `income_caps` 밴드 없음(Lv41+)과 일일 의뢰 밴드 절단(36~40)도 같은 파일에서 검사한다 |
| 운영 | `server/ops/README.md` 3절 표에 `CHAPTER3_OPEN` 행. 여는 절차: env 수정 → `systemctl restart dotrpg-api` → `/meta` 확인 → 공지. 출시 빌드는 3장을 포함한 빌드이고, 잠금 회귀 테스트(`chapter3Lock` + DevCapture `-dotrpgChapter3Lock`)는 매 빌드 항목이다 |

## 7. 스포일러 보호

- 3장 퀘스트·대사·컷신 텍스트 묶음 `chapter3.json`(Quests·StoryDialogues·Cutscenes의 c3_* 항목)은 출시 빌드에 싣지 않는다. 코드·맵·몬스터·보스·아이템·특화·상점은 빌드에 싣는다(이름·수치만 노출되고 이야기는 노출되지 않는다).
- 서버 `GET /content/chapter3`이 `CHAPTER3_OPEN=1`일 때만 묶음을 응답한다(닫힘 404). 클라이언트는 로그인 뒤 `/meta`가 열림이면 한 번 받아 캐시하고, 퀘스트 DB·대사·컷신에 런타임 병합한다. 오프라인은 빌드에 묶음이 없으므로 3장을 열 수 없다(오프라인은 1~2장까지).
- 11개 언어 번역은 묶음 안에 같이 들어간다(`lang` 키).

## 8. 기존 유저 보호

- Lv40 캐릭터는 열리는 순간 그대로 Lv40이고 경험치가 다시 쌓인다. 환산·초기화는 없다.
- Lv40 레전더리는 Lv50 유니크와 같은 급이고 계승으로 Lv50 레전더리가 된다. 열리기 전 모은 핵·열쇠 조각·레이드 재료는 그대로 쓰인다.
- 월드보스·길드보스는 Lv40 그대로 참가한다. 상한은 각자의 레벨·장비로 계산되므로 기록이 잘리지 않는다.
- 스킬 스타일 3개는 특화 없음 상태로 유지된다. 세이브 버전 8은 `specs` 누락을 빈 목록으로 읽는다.
