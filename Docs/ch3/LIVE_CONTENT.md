# 챕터 3 라이브 콘텐츠 (2026-10-21)

기준: `PLAN_CHAPTER3_2026-10-21.md` 4절 연결고리 표. 현재 값은 `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 7~8절.

## 1. 미니 던전 티어 40

| 항목 | 값 |
|---|---|
| 정의 | `MiniDungeonCatalog.All`에 Tier 40, MinLevel 40, MaxLevel 49, 이름 "잿빛 수련의 협곡", MapId `mini_dungeon_40`, MonsterId void_hound, Theme DryCanyon, Music `music_sayab` |
| 서버 | `MINI_TIERS = [10, 20, 30, 40]`, `tierOf`는 `level < levelCap`만 본다(직접 40 삭제) |
| 경험치·골드 | 티어 40 전용 상수 KillsPerBar 36,000 / 따라잡기 3,600(1~30 티어는 8,000/800 그대로, `MiniDungeonCatalog`·`miniDungeonRules`에 티어별 상수). 근거: 하루 600마리 x 12일 = 7,200마리, 따라잡기가 거의 매일 켜지므로 약 2막대 = 1,400,000 안팎(xpToNext 41~49 평균 700,000 기준). 40→50 전체 6,872,974의 약 20%로 1~40의 미니 비중과 같다. 처치 골드 36 그대로. 따라잡기 게이트는 3-2~3-11이 레벨마다 켜 준다 |
| 열림 | Lv40이고 `CHAPTER3_OPEN=1`일 때. 닫혀 있으면 Lv40은 지금처럼 미니 던전 없음 |
| 테스트 | `miniDungeon.test.ts`에 티어 40 입장·만렙 50 경계·닫힌 상태 거부 |

## 2. 월드보스·길드보스

| 항목 | 값 |
|---|---|
| 입장 레벨 | 40 고정(`BossRushCatalog.MinLevel`, 서버 `min_level`). Lv40~49도 계속 참가한다. 보스 레벨만 `levelCap()`(열리면 50)을 따르고, 시즌 행에 `boss_level`을 기록한다(마이그레이션 0050) |
| 보스 | 같은 `boss_abyss_titan`(심연의 거신 지파크), `BossRushCatalog.DefaultLevel = LevelCap`. 방 안 체력 x46·피해 x1.0·직업 배율(가디언 1.4, 아르카니스트 1.3, 비숍 2.2) 그대로 |
| 피해 상한 | `attemptDamageCap`은 참가자 각자의 `killRules.attackCap(자기 레벨, 자기 착용 장비)`로 계산한다(기존 그대로). 상한 배율 0.6 유지. 7단계 보스 방 측정(Lv50, 티어 10 장비)으로 실제 피해가 상한의 50% 아래인지 확인 |
| 월드보스 체력 | 시즌 생성 때 `WORLD_BOSS_HP`(기본 40,000,000)를 쓰고, 보스 레벨 50 시즌은 x2.0(80,000,000)으로 둔다. 조정은 env `WORLD_BOSS_HP`로 한다 |
| 길드보스 | 점수제 그대로. 보스 레벨만 따라간다 |

## 3. 랭킹

- 전투력은 서버가 착용 장비로 계산하므로 티어 9·10이 들어가면 자동 반영.
- 월드보스·길드보스 보드는 시즌 단위라 그대로.
- 새 보드 없음.

## 4. 일일·주간 의뢰

- `daily_quests.json` 밴드 (41~45) `sayab_low`, (46~50) `sayab_high`. giver "사얍 마을 게시판". 템플릿은 밴드마다 kill 4 + dungeon 1(군주 던전 1회).
- 기존 마지막 밴드(36~999)는 36~40으로 자른다. 골드: (41~45) 1,400, (46~50) 1,600. `maxLevelGold` 3,000 유지(Lv50 만렙).
- 사얍 밴드 템플릿에 수집 1개를 더한다: 리아의 약초밭(giver 리아, type collect, 재 속 약초 5, dry_stones). 보상은 밴드 골드와 회복 물약 10.
- 주간 의뢰 템플릿에 "시브 레이드 1회", "카마츠 레이드 1회" 추가(Lv45·50 밴드).

## 5. 해금·안내

`ContentUnlocks.Feature`에 두 개 추가.

| Feature | quest | 조건 | 팁 |
|---|---|---|---|
| Chapter3 | c2_end 완료 + `Chapter3.Enabled` | 메인 3-1이 레오나에게서 열린다 | "협곡 너머로 가는 길이 열렸어요. 레오나를 찾아가세요." |
| Specialization | 없음(레벨) | Lv41 도달 + `Chapter3.Enabled` | "특화를 고를 수 있어요. 스킬 창의 특화 탭에서 두 갈래 중 하나를 고르세요(마을에서 바꿀 수 있어요)." |

- 할 일 창(`TodayScreen`): "새 특화를 고를 수 있어요"(해금됐는데 선택 안 한 스킬이 있으면), "군주 던전 오늘 N회", "시브·카마츠 레이드 열림". 순서는 기존 규칙(메인 퀘스트 → 새 콘텐츠 → 보상 → 던전 → 레이드 → 의뢰 → 강화 → 성장 추천).
- 성장 추천(`GrowthGuide`): Lv41+ 사냥터 추천에 dry_* 3곳과 지하 B1~B4를 레벨대로 넣는다. 군주 던전 추천은 Lv45부터.
- 메뉴 잠금: 닫힌 상태에서는 특화 탭·군주 탭·사얍 노드가 보이지 않는다(흐리게가 아니라 없음. 열리기 전의 콘텐츠는 존재를 드러내지 않는다).

## 6. 잠금: CHAPTER3_OPEN

| 층 | 동작 |
|---|---|
| 서버 env | `CHAPTER3_OPEN: z.enum(['0','1']).default('0')`. `levelCap()` = 열리면 `progression.maxLevel`(50), 닫히면 40. 새 주의 시즌을 만들 때만 읽는 월드보스처럼 "다음 요청부터" 적용된다 |
| 서버 거부 목록(닫힘) | grantXp가 Lv40에서 멈춤, 3장 퀘스트 id(c3_*) 보상 청구 422 `CHAPTER_LOCKED`, 레이드 raid_xib·raid_kamatz 클리어 422, 군주 난이도 클리어 422, 미니 던전 티어 40 입장 422, specs 비어 있지 않으면 422 `SPECS_LOCKED`, 계승 422, 티어 9·10 장비가 들어오는 모든 경로(드롭 보고·상점·경매 등록) 422, 사냥터 진입: `killService`가 map_id `dry_floor`·`dry_stones`·`dry_rift`·`sayab`·`mini_dungeon_40`의 처치 보고와 몬스터 `void_*`·`grey_*` 처치 보고를 422, 3장 맵의 필드 세션 생성(`fieldsessions`) 422 |
| /meta | `chapter3_open`, `level_cap` 필드 추가. 클라이언트 `OnlineSession`이 로그인 때 읽어 `Chapter3.Enabled`·`Progression.LevelCap`에 넣는다 |
| 클라이언트(오프라인) | `Chapter3.Enabled`의 빌드 기본값은 `Resources/Data/GameConfig`의 `chapter3Open`(출시 빌드 false). 오프라인 플레이는 이 값으로 같은 규칙을 적용한다 |
| 클라이언트(닫힘 경로) | 월드맵 노드 숨김 외에 귀환 주문서 목적지 목록, 파티 따라가기(`PartyNet` 호스트 맵 이동 추종), 성장 추천·할 일의 이동 버튼에서도 3장 맵(dry_*·sayab·mini_dungeon_40)으로 가지 않는다(`MapRegistry.IsChapter3(mapId)` 한 함수로 판정) |
| 데이터 | 3장 데이터(퀘스트·몬스터·레이드·장비·특화)는 전부 내보내고 데이터 버전에 들어간다. 플래그를 바꿔도 데이터 버전은 바뀌지 않으므로 재빌드 없이 연다 |
| 테스트 | `server/test/chapter3Lock.test.ts`: 닫힘에서 위 거부 목록 전부, 열림에서 전부 통과, 플래그 전환 전후 Lv40 캐릭터 경험치 |
| 운영 | `server/ops/README.md` 3절 표에 `CHAPTER3_OPEN` 행. 여는 절차: env 수정 → `systemctl restart dotrpg-api` → `/meta` 확인 → 공지 |

## 7. 기존 유저 보호

- Lv40 캐릭터는 열리는 순간 그대로 Lv40이고, 사냥·던전·의뢰 경험치가 다시 쌓인다. 환산·초기화는 없다.
- Lv40 레전더리는 Lv50 유니크와 같은 급이고 계승으로 Lv50 레전더리가 된다(`PROGRESSION_AND_GEAR.md` 2·4절). 열리기 전 모은 핵·열쇠 조각·레이드 재료는 그대로 쓰인다.
- 월드보스·길드보스는 Lv40 그대로 계속 참가한다(입장 레벨 40 고정). 보스만 Lv50이 되고 상한은 각자의 레벨·장비로 계산되므로 Lv40 참가자의 기록이 잘리지 않는다.
- 스킬 스타일 3개는 특화 없음 상태로 유지된다. 특화를 고르기 전까지 모든 스킬은 지금과 같다.
