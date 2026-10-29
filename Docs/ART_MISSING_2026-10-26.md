# 3장·도적 출시 그림 점검

기준: `wonho` 현재 작업 트리. `Docs/ch3/ART_LIST.md`, `PLAN_CLASS_ROGUE.md` 6절, 실제 `Assets/Resources`와 로딩 코드를 대조했다. `PLAN_CHAPTER4.md` 10절의 4장 그림은 모두 제외한다. 없는 파일명만 세지 않고 실제 별칭·공유 밴드 경로를 확인했다.

## 생성·교체 목록

아래 경로는 `Assets/Resources/` 기준이다. 11개 모두 Codex 내장 이미지 생성으로 만들고 규격 검사 및 모음판 확인 후 반영했다. 생성 원본·백업·검사 자료는 `C:/Users/admin/Desktop/games/dotRPG_art_work/2026-10-26/`에 보관한다.

| 경로 | 크기 | 발견 내용 | 새 그림·참조 |
|---|---|---|---|
| Art/Careers/Icons/career_a_awake.png | 128x128 | 옛 천영참 의미 | 여자 어쌔신의 그림자 변신. career_a_phantom·cutin_rogue 인물 참조 |
| Art/Careers/Icons/career_n_awake.png | 128x128 | 옛 분신 난무 의미 | 여자 닌자의 전방 표창 연사. career_n_volley 참조 |
| Art/Careers/Icons/career_a_kunai.png | 128x128 | 이전 노드 그림 재사용 | 날아가는 쿠나이와 그 위치로 이동하는 여자 어쌔신 |
| Art/Careers/Icons/career_n_veil.png | 128x128 | 이전 노드 그림 재사용 | 회색 연막을 남기며 뒤로 도약하는 여자 닌자 |
| Art/gem_stab_ninja.png | 64x64 | 없음, gem_stab 공유 | 급소 투척. 여자 닌자와 단일 쿠나이. gem_stab 화풍 |
| Art/gem_leap_ninja.png | 64x64 | 없음, gem_leap 공유 | 튕기는 쿠나이. 여자 닌자와 굽은 연쇄 궤적 |
| Art/gem_backstab_ninja.png | 64x64 | 없음, gem_backstab 공유 | 등 뒤 저격. 멀리서 표적 뒤를 겨누는 여자 닌자 |
| Art/gem_raid_ninja.png | 64x64 | 없음, gem_raid 공유 | 그림자 표창. 여자 닌자의 원거리 표창 |
| Art/cutin_assassin.png | 1024x1024 RGBA | 전직 인물 컷인, 새 변신 각성 장면 아님 | 기존 여자 도적 인물을 유지한 그림자 변신, 투명 배경 |
| Art/cutin_ninja.png | 1024x1024 RGBA | 전직 인물 컷인, 새 표창 각성 장면 아님 | 같은 여자 도적의 전방 표창 난사, 투명 배경 |
| Art/menuicon_spec.png | 64x64 | ART_LIST 4절 필수, 파일과 로딩 없음 | 은백 두 갈래의 특화 문양, 스킬 창 특화 탭에 연결 |

아이콘은 기존 2026-10-24 여성 캐릭터 장면형 그림과 맞추고 인물을 크게 둔다. 도적 이펙트와 특화 메뉴 아이콘은 강철 흰빛·회색이며 보라·자홍·네온을 쓰지 않는다. 기존 특화 A/B 테두리 색은 그대로다. 참조 모음판은 작업 폴더 `references.png`다.

검사 기록: 전직 4개 128x128, 젬 4개와 메뉴 1개 64x64, 컷인 2개 1024x1024 RGBA. 컷인은 생성 도구에서 실제 알파를 받아 보존했으며 불투명 마젠타 배경을 거치지 않았다. 완전 투명 픽셀 비율 어쌔신 27.91%, 닌자 39.56%. 검사식 기준 자홍 픽셀 비율은 11개 모두 0. 원본 1254x1254에서 규격별 축소, 기존 파일 6개는 먼저 백업했다. 모음판 확인에서 재시도 대상 없음.

작업 폴더 안 `manifest.json`은 파일별 프롬프트·생성 원본·게임 경로, `validation.json`은 크기·알파·SHA256, `process_assets.py`는 후처리·백업·모음판 재현 코드다. `generated/`에 생성 원본 11개, `final/`에 최종 11개, `backups/`에 교체 전 원본 6개, `contact-sheet.png`에 최종 모음판을 보관한다. 기존 PNG 메타는 유지하고 새 5개만 같은 종류 템플릿과 경로 SHA256 guid로 만들었다.

검증 완료: `GameDataExport.Export` 정상 종료 0, C# 컴파일 오류 0, 서버 데이터 21개 내보내기와 데이터 버전 `6fd32cd2e649bfac` 유지. 종료 단계의 Unity 통신 취소 로그(Curl 42)는 기록돼 있으나 내보내기 실패는 아니다. `i18n.py check` 11언어 미번역·형식 오류 0, `sync-server` 완료. 설치된 PNG 11개 SHA256은 검사 완료본과 전부 일치한다. 로그는 작업 폴더 `unity-export.log`에 보관한다.

## 파일이 있어 새로 만들지 않는 항목

| 항목 | 실제 확인 | 판정 |
|---|---|---|
| 3장 인물 10종 | char_ixi·kukum·akab·noho·kana·balche·ria_adult·sayab_inn·boss_xibak·boss_kamatz 각각 40프레임 | 존재 |
| 도적 동료 | char_merc_mir, char_merc_daon 각각 40프레임 | 존재 |
| 3장 초상 | portrait_ixi·kukum·akab·noho·kana·balche·ria_adult·sayab_inn·kamatz | 9개 존재. 아래 여관 별칭 연결 문제 수정 필요 |
| 3장 대화의 기존 인물 | portrait_leona·orban·ria | 존재. 플레이어 `{name}`는 NPC 초상 생성 대상 아님 |
| 3장 레이드 재료 | 아이템 mat_raid_xibak·mat_raid_kamatz의 실제 iconKey는 maticon_raid_xibak·maticon_raid_kamatz | 둘 다 존재. 아이템 id와 그림 키 혼동 제외 |
| 3장 장비 | Art/Gear/eqicon_band89_{sword,staff,dagger,plate,greaves,robe,skirt,neck,ring}.png | 9종 존재. GearCatalog.BandIcon은 45·50 비전설을 공유하도록 설계됨 |
| 45·50 단검 개별 12아이콘 | 개별 파일은 없지만 공용 band89_dagger 또는 전설 band fallback이 명시된 설계 | 필수 누락 아님. 12장을 임의 추가하지 않음 |
| 특화 테두리 | spec_frame_a·spec_frame_b | 존재 |
| 레이드 배너 | banner_raid_xibak·banner_raid_kamatz | 존재 |
| 사얍 건물·소품 | StoryWorld의 Town/sayab_ixi_house·sayab_spring_pavilion·sayab_noho_records 및 c3_herb_garden·sayab_lantern_post·sayab_herb_plot·sayab_black_stones·sayab_wreck_decor | 참조하는 PNG 모두 존재 |
| 환영 삼격·허수아비 | career_a_phantom·char_scarecrow | 존재. 옛 문서의 생성 대기 기록과 구분 |
| 도적 무기 | PlayerWeapons/wpn_kunai_0~3, Weapons/eqicon_dagger_0~3 및 도적 기본 무기·젬 아이콘 | 존재 |
| a_mark 이펙트 | VfxImg/a_mark.png 480x96, 5프레임. 유효 알파 픽셀 평균 RGB 137/144/154 | 이미 회색으로 교체된 상태, 문서의 자홍 0.82 기록은 과거 값 |
| r_backstab 이펙트 | VfxImg/r_backstab.png 1152x192, 6프레임. 유효 알파 픽셀 평균 RGB 197/176/174 | 파일 존재, 과거 문서만으로 누락 처리하지 않음. 평균값만으로 화풍 품질을 판정하지 않음 |

## 연결 변경

- 닌자 전직 상태에서만 기본 젬 4종을 `_ninja` 키로 표시한다. 다른 직업·어쌔신은 기존 키를 유지하고, SpriteLibrary의 원래 키 캐시에 다른 직업 그림을 넣지 않는다.
- 스킬바·모바일 터치 스킬 버튼·기본 스킬 목록·젬 안내·장착 슬롯·특화 목록에 같은 키 선택 규칙을 적용한다.
- 사얍 여관 NPC id는 `sayab_innkeeper`, 기존 초상 키는 `portrait_sayab_inn`이다. 그림 누락이 아니라 별칭 누락이므로 기존 초상을 연결한다.
- 스킨 전용 컷인은 기존 우선순위를 유지한다. 기본 전직 컷인 두 장은 같은 파일명으로 교체한다.
- 기존 `.meta`는 유지한다. 새 그림의 `.meta`만 동일 종류를 본떠 상대 경로 SHA-256 앞 32자리 guid로 생성한다.

## 변경 파일

- 위 생성·교체 표의 PNG 11개 및 새 젬 4개·메뉴 아이콘 1개의 `.png.meta`.
- `Assets/Scripts/Runtime/Combat/CareerFx.cs`
- `Assets/Scripts/Runtime/UI/SkillBarView.cs`
- `Assets/Scripts/Runtime/UI/TouchControls.cs`
- `Assets/Scripts/Runtime/UI/SkillScreen.cs`
- `Assets/Scripts/Runtime/UI/SkillScreen.GemGuide.cs`
- `Assets/Scripts/Runtime/UI/SkillScreen.Slots.cs`
- `Assets/Scripts/Runtime/UI/SkillScreen.Specs.cs`
- `Assets/Scripts/Runtime/UI/DialogueBoxView.cs`
- 이 문서. 원본·프롬프트·후처리 스크립트는 별도 작업 폴더에 보관.

## 범위 제외

4장 칠람·테우·카엘 보스형, FrozenRuin, 진각성 6종, 레벨55 장비와 그 이후 그림은 이번 생성 범위에 넣지 않는다. 기존 그림이 존재한다는 검사는 게임 실행 품질 검증을 뜻하지 않는다.
