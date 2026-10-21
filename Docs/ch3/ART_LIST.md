# 챕터 3 그림 목록 (2026-10-21)

규격은 `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 10절. 생성은 Flow(Nano Banana) 기본, 마젠타 배경 → 크로마키(`Tools/art/process_generated.py`), 캐릭터 시트는 `process_characters.py --dot`(64px, ppu 36), 임포트는 `GeneratedArtImport.Import`. 기존 그림 재색칠·재사용으로 때우지 않는다(메모리 규칙). 참조로 첨부할 기존 파일은 표에 적는다.

## 1. 사얍 마을 (테마 DemonTown)

| 항목 | 수량 | 규격 | 참조 |
|---|---|---|---|
| 바닥 타일 세트(마른 흙, 검은 돌 포장, 샘물, 샘 가장자리) | 4 | 기존 마을 HD 등록 절차(`register_map_hd.py`, `upscale_surface.py`) | `Resources/Maps/Canyon`·`Village` 등록본 |
| 건물(여관, 대장간, 상점, 익시의 집, 노흐의 집, 민가 2종, 샘 정자) | 8 | 소품 절차(`register_world_props.py`), 발밑 기준점 | `Docs/VILLAGE_*_PROMPTS.txt` |
| 소품(등불 기둥, 검은 돌 조각 무더기, 마른 나무, 가죽 천막, 약초밭 2종, 수호석 잔해 장식) | 7 | 소품 절차 | 같은 문서 |
| 월드맵 아틀라스 노드(사얍 마을·사냥터 3) | 4 | `WorldAtlas` 미리보기 절차 | 기존 노드 |
| BGM `music_sayab` | 1 | 기존 BGM 규격 | `music_canyon` |

## 2. 사냥터 테마 DryCanyon

| 항목 | 수량 | 규격 |
|---|---|---|
| 바닥 variant 3(갈라진 땅, 검은 돌 잔해, 보랏빛 균열) | 3 | 절차 생성 테마 타일(`HuntingLayouts` variant 0~2) |
| 소품(죽은 나무 2, 돌 조각 3, 균열 장식 2, 잿빛 손 야영 천막·깃발 2) | 9 | 소품 절차 |
| 공허 정수·기록 조각·잃어버린 조약돌·잿빛 인장 수집 아이콘 | 4 | 64px 아이콘 |

## 3. 캐릭터 프레임 (40장 규격: down/downside/side/upside/up x idle0·idle1·walk0~3·attack·hurt)

| id | 대상 | 비고 |
|---|---|---|
| char_ixi | 익시 | 붉은 달 문양, 긴 뿔 |
| char_kukum | 전사장 쿠쿰 | 깃털 장식 창 |
| char_akab | 밤 상인 아카브 | |
| char_noh | 촌로 노흐 | |
| char_kanan | 치유사 카난 | |
| char_balche | 발체(아이) | 작은 체격 |
| char_ria_adult | 성장한 리아 | 빨간 머리끈. 기존 `char_ria`(아이) 참조 |
| char_sayab_inn | 사얍 여관 주인 | |
| char_boss_xib | 공허 침식체 시브 | 보스 규격, artScale 1.5. 참조 `char_boss_abyss_titan` |
| char_boss_kamatz | 잿빛 손 지휘관 카마츠 | 사람형 중무장, 잿빛 망토. 참조 `char_kael`·`char_bargas` |
| 몬스터 아틀라스 void | void_hound·void_sentry·void_caller | `RegionalMonsterArt` 규격 5방향 x 8프레임 x 3종, 참조 `UnderworldMonsterArt` 아틀라스 |
| 몬스터 아틀라스 grey | grey_ranger·grey_knight | 같은 규격 x 2종 |

## 4. 특화

| 항목 | 수량 | 규격 |
|---|---|---|
| 특화 아이콘 테두리 `spec_frame_a`(진홍), `spec_frame_b`(은백) | 2 | 64x64 RGBA, 가운데 투명, 메뉴 아이콘과 같은 두께 |
| 특화 탭 아이콘 `menuicon_spec` | 1 | 64x64 |
| 이펙트 | 0 | 색만 바뀐다(`CareerFx.SkillColor`). 새 이펙트 그림 없음 |

## 5. 던전·레이드

| 항목 | 수량 | 규격 |
|---|---|---|
| 레이드 방 맵(DgnXib1, DgnXib2, DgnXibBoss, DgnKamatz1, DgnKamatzBoss) | 5 | 기존 레이드 방 텍스트 맵 + 테마 그림. 참조 `DgnGrah*` |
| 배너 `banner_raid_xib`, `banner_raid_kamatz` | 2 | 480x160 ppu 100. 참조 `banner_raid_grah` |
| 미니 던전 40 맵 `mini_dungeon_40` | 1 | 기존 미니 던전 맵 규격, DryCanyon 타일 |
| 군주 난이도 | 0 | 기존 던전 재사용 |

## 6. 장비

| 항목 | 수량 | 규격 |
|---|---|---|
| 티어 9·10 장비 이름 | 80 | 8종 x 2티어 x 5등급(`GearCatalog` 이름 배열). 테마: 티어 9 "마른 협곡·검은 돌"(예: 검 "균열 장검", "잔해 파쇄검", "메마른 땅의 대검", "공허 가르는 검", "검은 돌의 왕검"), 티어 10 "사얍·봉인"(예: "샘물 장검", "핏빛 달 장검", "사얍 수호검", "봉인의 쐐기", "볼론의 맹세") |
| 장비 아이콘 밴드 8~9 | 8 | 8종 x 1밴드(티어 9·10 공용), 레전더리 Lv50은 기존 레전더리 밴드 아트 규칙 |
| 재료 아이콘 `mat_raid_xib`(침식 결정), `mat_raid_kamatz`(잿빛 인장) | 2 | 64px |

## 7. 생성 순서

1. 3절 캐릭터·몬스터(3단계 시작 전) → 2. 1·2절 마을·사냥터(3단계) → 3. 5절 레이드(4단계) → 4. 4·6절 아이콘·이름(1·2단계와 병행 가능).
- 생성 기록은 `Docs/GENERATED_ASSETS.md` 방식으로 프롬프트·원본 경로·알파 실측을 남긴다. 원본 보관 `/home/lee/project/.playwright-mcp/dotrpg_refs/`.
