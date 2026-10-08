# 모바일(Android 가로) UI/UX 전면 개편 계획서

기준 커밋 53dcfed(터치 HUD·조이스틱·SafeAreaFitter·LongPress·모바일 CanvasScaler·최소 글꼴 22). 브랜치 wonho.
이 문서는 계획만 담는다. 코드는 아직 수정하지 않았다.

## 0. 한 줄 요약

- 지금 모바일은 PC용 1280x720 창을 그대로 0.8배로 줄여 쓰고 있어서 글꼴과 버튼이 작고, 호버·우클릭·키보드에 기대는 조작이 길게 누르기로 땜질돼 있다.
- 모바일 전용 창 틀(전체 화면, 상단 탭 바, 하단 버튼 바)과 아이템 상세 시트를 공용으로 만들고, 창을 하나씩 그 틀로 옮긴다.
- PC 코드 경로는 건드리지 않는다. 모든 변경은 `TouchUi.Enabled` 분기 안에 있고, 새 코드는 새 파일(`UI/Mobile/`, `*.Mobile.cs`)에 둔다.

## 1. 현재 상태 조사 (근거)

### 1.1 화면 크기와 0.8배 문제 (계산값)

- 모바일 캔버스 기준 해상도는 1024x576, 모드는 Expand(`UIRoot.cs:76-84`, `UIFactory.cs:48`). 배율 = min(가로/1024, 세로/576).
- 2400x1080: 배율 1.875, 캔버스 1280x576. 1920x1080: 배율 1.875, 캔버스 1024x576. 2048x1536 태블릿: 배율 2.0, 캔버스 1024x768. 모든 폰은 세로가 576으로 고정이다.
- `WindowScreen.CreateWindow`는 1280x720 레이아웃을 만들고 `FitToParent`가 `min(1, 가로/1280, 세로/720)`로 줄인다(`WindowScreen.cs:28-30`, `EquipmentScreen.cs:80-81`, `EquipmentScreen.cs:400-415`). 세로 576이면 위 세 해상도 모두 k=0.8이다.
- 그래서 창 안의 모든 크기가 0.8배가 된다. 최소 글꼴 22가 실제로는 약 17.6, 높이 44짜리 버튼은 약 35다.
- 손가락 크기 환산(2400x1080, 약 6.7인치 가정, 픽셀당 약 0.065mm): 캔버스 1단위는 약 0.12mm. 9mm는 약 74단위, 7.7mm(안드로이드 권장 48dp 근처)는 약 64단위다. 실제 기기 인치는 미확인이므로 수치는 "약"이다.
- 현재 창 요소의 실제 크기(0.8배 반영): 가방 칸 94 -> 75(기준 충족), 강화 칸 92 -> 74, 창고 칸 76 -> 61, 상점 행 60 -> 48, 퀘스트 행 54 -> 43, 우편 행 58 -> 46, 경매 행 42 -> 34, 파티 찾기 행 44 -> 35, 지도 NPC 행 30 -> 24, 페이지 버튼 50x34 -> 40x27(`UiSizes.cs:12`), 받기 버튼 140x44 -> 112x35(`UiSizes.cs:16`). 가방 칸과 강화 칸 말고는 전부 기준 미달이다.
- 메뉴 패널(`BuildPanel`)은 행 46, 설정은 12행이라 패널이 576을 넘어 `FitToScreen`이 줄인다(`MenuScreens.cs:36-45`, 설정 `:197-282`). 계산상 k는 약 0.8, 행 높이는 약 36(약 4mm)이다.

### 1.2 입력 의존 (호버·우클릭·키)

| 의존 | 위치 | 현재 터치 대응 |
|---|---|---|
| 호버 툴팁 | `GearTooltip.Hook`(`GearTooltip.cs:80-98`), 가방 `EquipmentScreen.Layout.cs:169-171`, 창고 `StorageScreen.cs:96-99`, 지도 핀 `WorldMapScreen.cs:193-194`, 스킬 `SkillScreen.cs:106-108` | 0.5초 길게 누르기(`LongPress.cs`). 숨은 조작이라 발견이 어렵다 |
| 우클릭 | 가방 메뉴(`EquipmentScreen.Drag.cs:48`), 창고 이동 방향(`StorageScreen.cs:100`), 스킬 소켓 되돌리기(`SkillScreen.cs:109`), 슬롯 해제(`SkillScreen.Slots.cs:70`), 강화 티켓 전환(`EnhanceScreen.Ticket.cs:24`), 메뉴 값 줄이기(`MenuList.cs:277`) | 길게 누르기로 대체 |
| 마우스 휠 | 퀘스트 행(`QuestScreen.cs:39`), 가챠(`GachaScreen.cs:218`) | 퀘스트만 `SwipeSteps`. 나머지 목록은 페이지 버튼(◀▶) |
| 키 | 대화(Enter), 채팅 열기 Enter(`ChatView.cs:173`), 빠른 신호 4~8번(`ChatView.cs:176`), 던전/스킬 탭 전환 Tab(`DungeonSelectScreen.cs:385`, `SkillScreen.cs:266`), 강화 티켓 `[` `]`(`WindowScreens.Attempt.cs:186-187`), 로그인 Tab/Enter(`OnlineScreens.cs:206-207`) | 대화 탭 레이어만 있음. 나머지는 터치 수단 없음 |
| 드래그 | 가방 -> 착용 칸(`EquipmentScreen.Drag.cs:42-61`), 스킬 -> 슬롯(`SkillScreen.Slots.cs:19`) | 동작은 하나 탭만으로는 못 하는 흐름이 있음 |
| 키보드(소프트) | 채팅/로그인/이름/경매 검색/탈퇴/파티 초대 `InputField` | 가림 대응 없음. `TouchScreenKeyboard` 검색 결과 0건 |

### 1.3 기타 확인 사실

- 안드로이드 뒤로가기는 `KeyCode.Escape`로 들어와 `CancelPressed`가 된다(`InputReader.Legacy.cs:16-18`). 창은 이미 닫힌다(`WindowScreen.cs` Update). 별도 작업 불필요, 확인만 한다.
- 터치 메뉴 격자는 11개 창만 연다(`TouchControls.Menu.cs:12-24`). 채팅을 여는 터치 수단이 없다(`TouchControls*.cs`에 채팅 없음).
- 대화창은 이미 탭으로 넘어가고(`DialogueBoxView.cs:29-37`) 안전 영역을 쓴다(`:21`). 건너뛰기 버튼 46 높이는 약 5.6mm라 작다(`:96`).
- 설정의 "키보드 설정"은 `!Application.isMobilePlatform`로 숨기는데(`MenuScreens.cs:148,178,238`) `-touchUI`를 켠 PC에서는 보인다.
- 메뉴 패널(`BuildPanel`)과 가방(`EquipmentScreen`, MenuScreen 상속)은 `WindowScreen`이 아니다. 창 틀 변경은 두 계통을 따로 다뤄야 한다.
- 이벤트 시스템의 드래그 시작 거리(`pixelDragThreshold`)는 기본값이다(`grep` 0건). 고밀도 화면에서는 손가락이 조금만 떨려도 탭이 드래그로 처리돼 버튼이 안 눌린다. 모바일에서만 올린다(단계 1).
- DevCapture에 UI 캡처 모드가 이미 있다: `-dotrpgUi <폴더>`(`DevCapture.cs:103`, `DevCapture.Ui.cs`). `-screen-width/-screen-height`를 `ApplyRequestedResolution`이 적용한다(`DevCapture.Ui.cs:143-154`). `TouchUi.Enabled`는 `-touchUI` 인자로 켜진다(`InputReader.Touch.cs:14-20`). 다만 이 모드는 PC 사이드 메뉴 버튼(`Btn_메뉴`, `Btn_<라벨>`)을 눌러 창을 열기 때문에(`DevCapture.Ui.cs:61,148`) 터치 UI에서는 그대로 쓸 수 없고, 시트·키보드·안전 영역 상태도 찍지 못한다. 새 모드가 필요하다. 캡처는 `DOTRPG_RELEASE` 빌드에서 제외된다(`DevCapture.cs:37,62`).

## 2. 화면 전수 조사표

표기: k=0.8은 위 1.1의 줄임 배율. 크기는 캔버스 단위(PC 레이아웃 값 -> 모바일 환산값). 난이도: 하(수치·배치 조정), 중(레이아웃 재작성+입력 교체), 상(구조 변경 또는 연출 연동).
우선순위: P0 = 혼자 마을에서 장비를 맞추고 던전을 돌고 저장·설정·로그인하는 핵심 흐름에 필수. P1 = 온라인·성장·부가. P2 = 손볼 필요가 적거나 모바일에서 쓰지 않음.
파일은 모두 `Assets/Scripts/Runtime/UI/` 기준(다른 폴더는 경로 표기).

### 2.1 창 틀과 핵심 창

| # | 화면 | 파일 | 현재 크기·배치 | 입력 | 모바일 문제 | 목표 배치 | 우선 | 난이도 |
|---|---|---|---|---|---|---|---|---|
| 1 | 창 공통 베이스 | `WindowScreen.cs:18-55` | 1280x720 고정 레이아웃을 FitToParent로 축소. 헤더 76, 뒤로 96x52(ESC 표기), 콘텐츠 1220x594 | Esc/Cancel/가방키 닫기(Update) | 전 요소 k=0.8. ESC 표기 무의미. 탭·하단 버튼이 창마다 제각각 위치 | 3절 공용 틀 | P0 | 상 |
| 2 | 가방(장비) | `EquipmentScreen.cs:19-20`, `.Layout.cs:66-122`, `.Drag.cs`, `.Input.cs`, `.Tooltip.cs` | 6x4 칸 94(간격 8), 착용 칸 88, 탭 54, 자동장착 168x60, 정렬 150x60, 페이지 50x34, 메뉴 170x118(버튼 46) | 호버 툴팁, 우클릭 메뉴(터치는 탭=메뉴 `:48`), 드래그 장착, 키/패드 커서 | 툴팁은 길게 누르기뿐. 메뉴 버튼 37. 페이지 버튼 27. 소비 아이템은 탭=즉시 사용(`Use :266`)이라 장비와 일관성 없음. 툴팁 박스 고정 280x400 | 상단 바 탭(전체·무기·방어구·장신구·소모품·기타). 좌: 캐릭터+착용 6칸(72). 중: 격자 6x4 칸 72. 칸 탭=상세 시트(장착·해제·사용·강화로 이동). 하단 바: 자동장착·정렬·닫기. 격자 좌우 스와이프=페이지 | P0 | 상 |
| 3 | 잡화점(상점) | `ServiceScreens.cs:17-100`, `BulkSellPanel.cs` | 한 페이지 7행 x 높이 60(간격 6), 목록 660, 구매·판매 탭 160x50, 구매 버튼 3종 | `GearTooltip.Hook`(`:71`), 행 클릭, 일괄 판매 행 토글(`BulkSellPanel.cs:46`) | 행 48. 툴팁은 길게 누르기. 수량 버튼 작음 | 상단 바 구매/판매 탭. 행 72, 세로 스와이프. 행 탭=시트(구매 1개·수량·최대 / 판매). 일괄 판매는 하단 바 버튼+체크 행 72 | P0 | 중 |
| 4 | 강화 | `WindowScreens.cs:24-60`, `.Attempt.cs`, `EnhanceScreen.Ticket.cs`, `.Promote.cs`, `EnhanceFx.cs` | 5x4 칸 92(간격 8), 한 페이지 20, 우측 패널, 티켓 버튼 158x54 | 티켓 전환 우클릭(`Ticket.cs:24`)·길게 누르기(`:25`)·`[` `]` 키(`.Attempt.cs:186`) | 티켓 전환이 숨은 조작. 시도 버튼·연출 k=0.8 | 좌: 격자(칸 72). 우: 선택 장비, 비용·확률, 티켓 `◀ 이름 ▶` 버튼. 하단 바: 강화·닫기. 시트로 아이템 상세 | P0 | 중 |
| 5 | 창고 | `StorageScreen.cs:16-110` | 6x4 칸 76(간격 8), 용량 48, 두 방향 이동 | 클릭=이동, 우클릭=반대 방향(`:100`), 길게 누르기(`:101`), 호버 상세(`:98,194`) | 칸 61. 반대 방향 이동이 숨은 조작. 설명이 호버일 때만 갱신 | 상단 바 탭 [가방 | 창고]. 칸 72. 칸 탭=시트(넣기/꺼내기, 수량). 좌우 스와이프=페이지 | P0 | 중 |
| 6 | 스킬 | `SkillScreen.cs`, `.Nodes.cs:25`, `.Slots.cs`, `.Career.cs` | 트리 노드 183x116, 젬 슬롯 행 92(너비 1220), 탭(트리·젬·직업) | 호버 정보(`:106-108,288`), 소켓 우클릭(`:109`)·길게 누르기(`:110`), 슬롯 해제 우클릭(`Slots.cs:70-72`), 드래그(`Slots.cs:19`), Tab 키(`:266`) | 호버 의존, 소켓 되돌리기 숨은 조작, 행 74(k) | 상단 바 탭. 노드·행 탭=시트(설명 + [슬롯에 등록 1~5] [해제] [젬 ◀▶]). 드래그는 남기되 탭 흐름으로 모든 일이 가능 | P0 | 상 |
| 7 | 퀘스트 | `QuestScreen.cs:16-110` | 9행 x 54, 목록 420, 우측 상세 | 휠 -> `SwipeSteps`(`:39`) | 행 43. 상세 글꼴 작음 | 행 72, 한 화면 약 5행. 스와이프 유지. 상세 패널 글꼴 24 | P0 | 하 |
| 8 | 지도 | `WorldMapScreen.cs:73-250`, `.Atlas.cs` | 지역 목록 ScrollRect(`:77`), NPC 행 30(`:73`), 핀 + 호버 툴팁(`:193-194`) | 핀 호버 -> `ShowTip`, 클릭 선택 | 행 24. 핀이 작고 호버 의존 | 행 72. 핀 반경 터치 영역 72(보이는 크기는 유지). 핀 탭=선택+하단 상세 줄. 호버 코드는 PC 전용으로 남김 | P0 | 중 |
| 9 | 던전 선택(요일·레이드) | `DungeonSelectScreen.cs:17-60,385` | 목록 380, 행 96(간격 8), 난이도 버튼 150x46, 슬롯 8칸 x 92 | Tab/조이패드 탭 전환(`:385`) | 난이도 버튼 37. 모드 전환이 키뿐 | 상단 바 탭 [요일 | 레이드]. 난이도 버튼 높이 72. 행 높이 유지(96 > 72) | P0 | 중 |
| 10 | 소탕 | `DungeonSelectScreen.Sweep.cs` (174줄) | 던전 선택 안의 소탕 UI | 클릭 | 버튼·수량 조절 크기 | 수량 조절 버튼 72, 하단 바 확인 | P1 | 하 |
| 11 | 레이드 상점/카드 | `DungeonSelectScreen.RaidShop.cs:16,82`, `RaidCardView.cs:98` | 열 3개 x 402, 칸 높이 190 | 아이템 툴팁 `Hook` | 툴팁 길게 누르기 | 카드 탭=시트(읽기 전용+구매) | P1 | 중 |
| 12 | 던전 결과 | `DungeonResultScreen.cs:17-99`, `.Raid.cs` | 좌 380 / 우 828, 카드 150x210(간격 20), 버튼 250x62 | 카드 툴팁 `Hook`(`:74`) | 카드 상세가 길게 누르기. 버튼 높이 50 | 카드 탭=뒤집기, 뒤집힌 장비 탭=시트(닫기만). 하단 바 [다시 도전][던전 선택][마을로] 높이 72 | P0 | 중 |
| 13 | 던전 HUD | `DungeonHudView.cs:51-108` | 시계·콤보·방 지도가 우상단(-20,-16부터), 마을로 버튼 140x44(`:108`), 부활 패널 520x210 | 버튼 클릭 | 터치 상단 버튼(메뉴·일시정지 64)과 겹칠 수 있음. 버튼 44 | 상단 버튼 영역과 겹침 제거, 마을로·부활 버튼 높이 72 | P0 | 중 |
| 14 | 대화창 | `DialogueBoxView.cs:15-100` | 박스 980x170 하단 중앙, 글꼴 26, 건너뛰기 250x46 | 탭 레이어(터치), Enter | 건너뛰기 46(약 5.6mm). 박스가 터치 하단 컨트롤과 겹치는지 미확인 | 건너뛰기 높이 72. 조이스틱·부채꼴 영역과 안 겹치게 확인 | P0 | 하 |

### 2.2 메뉴 패널 계통 (`MenuScreen.BuildPanel` + `MenuList`)

| # | 화면 | 파일 | 현재 크기·배치 | 입력 | 모바일 문제 | 목표 배치 | 우선 | 난이도 |
|---|---|---|---|---|---|---|---|---|
| 15 | 확인 창 | `ConfirmScreen.cs:8-121`, `UIRootConfirmExtensions.cs` | 패널 560(넓게 820), 행 46, 예/아니오 세로 | 선택+Cancel=아니오, 제한 시간 옵션 | 행 36. 본문이 길면 패널이 576을 넘어 축소 | 행 64 이상, 예/아니오 가로 2버튼(각 높이 72), 본문 길면 스크롤 | P0 | 하 |
| 16 | 타이틀 | `MenuScreens.cs:102-158` | 로고 560x141 + 메뉴 | 메뉴 행 선택 | 행 46 | 공통 MenuList 모바일 행(64) | P0 | 하 |
| 17 | 일시정지 | `MenuScreens.cs:159-196` | 패널 420, 행 46 | 동일 | 행 크기 | 동일 | P0 | 하 |
| 18 | 설정 | `MenuScreens.cs:197-282` | 패널 640, 12행 x 46, 값 ◀ ▶ 화살표 폭 = 글꼴x1.8 | 행 클릭=증가, 우클릭/길게=감소(`MenuList.cs:277`) | 패널이 576 초과로 축소(행 약 36). 화살표 폭 약 34. 감소가 숨은 조작 | 전체 화면 틀 + 스크롤 목록, 행 64, ◀ ▶ 폭 64 이상. 맨 아래에 "터치 버튼 설정"(단계 4) | P0 | 중 |
| 19 | 게임 오버 | `MenuScreens.cs:322-356` | 패널 520 | 메뉴 | 행 크기 | 공통 | P0 | 하 |
| 20 | 엔딩 | `MenuScreens.cs:357-387` | 패널 620 | 메뉴 | 행 크기 | 공통 | P2 | 하 |
| 21 | 조작 안내 | `MenuScreens.cs:283-321` | 패널 700, 키보드 안내 본문 | 없음 | 키보드 안내가 모바일에 무의미 | 터치 조작 안내 본문으로 교체 | P1 | 하 |
| 22 | 줍기 설정 | `LootFilterScreen.cs` | 패널 600, 옵션 4행 | 메뉴 | 행 크기 | 공통 | P1 | 하 |
| 23 | 저장 슬롯 / 도움말 | `OptionScreens.cs:8-125` | 슬롯 패널 820, 도움말 900(글꼴 18) | 메뉴 | 도움말 글꼴 18 -> 모바일 22 clamp 시 패널 초과 | 본문 스크롤 영역 | P1 | 하 |
| 24 | 키 설정 | `KeyBindScreen.cs` (266줄) | 키보드 그림(단위 45x38 `:140`) | 키/마우스 | 모바일에서 쓸 일 없음 | 변경 없음. 진입 숨김 조건을 `TouchUi.Enabled`로 통일(`MenuScreens.cs:148,178,238` 3줄) | P2 | 하 |
| 25 | 캐릭터 선택(새 게임) | `CharacterSelectScreen.cs:10-146` | 패널 760, 이름 입력 필드(`:79-95`) | Tab로 이름칸 포커스(`:113`) | 이름 입력 시 소프트 키보드가 칸을 가림 | 키보드 위로 패널 이동, 이름칸 높이 64 | P0 | 중 |
| 26 | 온라인 로그인·캐릭터·생성 | `OnlineScreens.cs:79-416` | 로그인 패널 640, 아이디·비번·서버 칸 | Tab/Enter(`:206-207`) | 키보드 가림. Enter 없는 키보드에서 제출 수단 | 키보드 회피, [접속] 버튼 상시 표시 | P0 | 중 |
| 27 | 회원 탈퇴 | `WithdrawalScreen.cs:12-186` | 패널 760, 행 50, 입력 2칸 | Enter(`:75`) | 키보드 가림, 행 40 | 키보드 회피, 행 64 | P1 | 중 |
| 28 | 친구·차단·신고 | `SocialScreen.cs:10-193` | 패널 640, 목록형 메뉴 | 메뉴 | 행 크기 | 공통 MenuList | P1 | 하 |

### 2.3 온라인·성장·부가 창 (`WindowScreen` 계열, 대부분 `OnlineWindow` 상속)

| # | 화면 | 파일 | 현재 크기·배치 | 입력 | 모바일 문제 | 목표 배치 | 우선 | 난이도 |
|---|---|---|---|---|---|---|---|---|
| 29 | 파티 | `PartyScreen.cs:12-14` | 슬롯 290x104, 카드 290x372(간격 300) | 클릭 | 카드 3~4장이 가로로 꽉 참, 버튼 크기 | 카드 가로 스와이프 or 축소 없는 배치, 버튼 72 | P1 | 중 |
| 30 | 파티 로비 | `PartyLobbyScreen.cs:26,161` | 행 50, 폭 1180, 버튼 250x42, 초대 입력칸(`:36`) | Enter로 초대(`:55`) | 행 40, 버튼 34, 키보드 가림 | 행 72, 버튼 72, 키보드 회피, 초대 버튼 | P1 | 중 |
| 31 | 파티 찾기 | `OnlineWindows.cs:66-71` | 9행 x 44, 표 폭 1220, 페이지 버튼 | 클릭, 페이지 | 행 35 | 행 72, 세로 스와이프, 행 탭=상세/신청 | P1 | 중 |
| 32 | 우편 | `MailScreen.cs:17-76` | 9행 x 58, 목록 500, 상세 690, 받기 140x44 | 첨부 `Hook`(`:76`) | 행 46, 받기 35 | 행 72, 받기 높이 72, 첨부 탭=시트 | P1 | 중 |
| 33 | 경매장 | `AuctionScreen.cs:15-109` | 9행 x 42, 표 폭 1220, 검색 `InputField`(`:29`), 등록 목록 | 행 `Hook`(`:84,109`), 페이지 | 행 34(가장 작음), 검색 키보드 가림, 표 열이 많음 | 상단 바 탭(조회·등록·내 경매·우편), 행 72(2줄 구성: 이름+가격/남은 시간), 행 탭=시트(입찰·즉시구매·등록), 검색 키보드 회피 | P1 | 상 |
| 34 | 가챠 | `GachaScreen.cs:15-216`, `.Refresh.cs`, `.Reveal.cs`, `.Sealed.cs`, `.Tiers.cs` | 목록 260 x 카드 높이 80, 큰 영역 940x440, 결과 칸 118x158, ScrollRect | 결과 `Hook`(`:170`), 휠 | 카드 64, 결과 상세 길게 누르기, 연출 k | 목록 카드 72, 결과 칸 탭=시트(읽기), 연출은 비율 유지 | P1 | 중 |
| 35 | 레벨 보상 | `LevelRewardScreen.cs:15-21` | 8행 x 56, 받기 버튼 | 클릭 | 행 45, 받기 35 | 행 72, 받기 72 | P1 | 하 |
| 36 | 업적 | `AchievementScreen.cs:15-16` | 2열 x 5행, 칸 590x64 | 클릭 | 칸 51 | 칸 72, 1열 or 2열 스와이프 | P1 | 하 |
| 37 | 옷장(코스튬 상점) | `CosmeticShopScreen.cs:16-63` | 목록 430, 행 72, 탭 46, ScrollRect | 클릭, 키(`:339`) | 탭 37, 행 58 | 상단 바 탭, 행 72 유지(k 제거 후 72), 탭 64 | P1 | 중 |
| 38 | 코스튬 합성 | `CosmeticSynthScreen.cs:17-25` | 규칙 600x144, 여분 행 56, 세트 행 88 | 클릭 | 여분 행 45, 소형 버튼 | 행 72, 버튼 72 | P2 | 중 |

### 2.4 HUD·연출 (평소 화면)

| # | 화면 | 파일 | 현재 | 입력 | 모바일 문제 | 목표 | 우선 | 난이도 |
|---|---|---|---|---|---|---|---|---|
| 39 | 채팅 | `ChatView.cs:23-100` | 하단 좌 400x154, 입력 30, 탭 라벨 22, 글꼴 16(clamp 22) | Enter 열기(`:173`), Esc 닫기, 4~8 신호키(`:176`), 탭 라벨 버튼 | 터치로 열 방법 없음. 입력창 30(작음). 키보드에 가림. 신호 5종 못 씀 | 평소 한 줄 접힘 스트립(높이 48), 탭=입력 열기+키보드 위로, 펼치면 최근 6줄+탭 칩(전체·파티·귓속말)+신호 칩 5개 | P1 | 중 |
| 40 | 토스트 | `HudView.cs:35-47,175-194` | 하단 중앙 700x200, 수명 2.4초 | 없음 | 조이스틱·채팅과 겹치는지 미확인 | 위치 확인, 글꼴 22 이상 | P1 | 하 |
| 41 | 미니맵 | `MinimapView.cs:37-76` | 우상단 지름 192, 라벨 판 | 없음 | 터치 상단 버튼과 간격 확인 | 확인만 | P1 | 하 |
| 42 | 컷신·페이드 | `Quest/CutscenePlayer.cs:65`, `AwakeningCutIn.cs`, `ScreenFader.cs` | Expand 스케일러 이미 적용 | 건너뛰기=대화창 건너뛰기 판 | 건너뛰기 크기(#14와 함께) | #14 처리로 충족 | P1 | 하 |
| 43 | HUD 잔여 | `PartyFramesView.cs:15`(206x50), `BuffBarView.cs`, `BossHpBarView.cs`, `TipView.cs`, `SideMenuView.cs`, `QuickItemBar.cs` | 일부는 터치에서 이미 숨김(`HudView.cs:68,94`) | 없음(보기 전용) | 겹침·글꼴 확인만 | 겹침 확인 후 필요 시 조정 | P2 | 하 |

집계: 화면 43건(표 행 기준). P0 19건, P1 20건, P2 4건(#20, #24, #38, #43).
P0 목록: 창 틀(#1), 가방(#2), 상점(#3), 강화(#4), 창고(#5), 스킬(#6), 퀘스트(#7), 지도(#8), 던전 선택(#9), 던전 결과(#12), 던전 HUD(#13), 대화창(#14), 확인 창(#15), 타이틀(#16), 일시정지(#17), 설정(#18), 게임 오버(#19), 캐릭터 선택(#25), 온라인 로그인류(#26).

## 3. 공용 틀 설계

### 3.1 원칙

1. 모든 모바일 코드는 `TouchUi.Enabled`로만 진입한다. PC 경로의 기존 줄은 지우거나 바꾸지 않는다. 허용되는 PC 파일 변경은 (a) `class X` -> `partial class X` 한 단어, (b) 기존 함수 맨 앞/맨 뒤에 `if (TouchUi.Enabled) { 새 함수(); return; }` 한 줄 추가뿐이다.
2. 새 코드는 새 파일에 둔다. 폴더 `UI/Mobile/`(공용)과 창별 `XxxScreen.Mobile.cs`(partial). 파일은 500줄 이하(`rules/coding-style.md`).
3. 모바일 창은 0.8배 축소를 쓰지 않는다. 실제 안전 영역 크기에 맞춰 앵커 기반으로 배치한다(1.1의 문제 제거).
4. 손가락 크기, 글꼴, 간격은 `UI/Mobile/MobileUi.cs`의 상수 한 곳에서만 정한다. 창 코드에 숫자를 직접 쓰지 않는다.

### 3.2 모바일 창 틀 (WindowScreen)

`WindowScreen.CreateWindow`(`WindowScreen.cs:18`) 맨 앞에 `if (TouchUi.Enabled) return CreateMobileWindow<T>(canvas, name, title, icon);` 한 줄을 넣는다. PC 본문은 그대로다.

사용 영역: `SafeAreaFitter.Wrap(root)`로 얻은 안전 영역 S. 설계 하한은 S = 960x576(1920x1080 폰에서 컷아웃을 뺀 값 가정), 상한은 1280x768(태블릿). 모든 배치는 S의 비율·앵커로 하고 960 폭에서도 깨지지 않게 한다.

```
+----------------------------------------------------------------+ 상단 바 높이 64
| [아이콘 제목]   [탭1][탭2][탭3]...(가로 스크롤 가능)   [재화 칩]  |
+----------------------------------------------------------------+
|                                                                |
|                  내용 영역 (S 높이 - 64 - 72 = 440)            |
|                                                                |
+----------------------------------------------------------------+ 하단 바 높이 72
| [닫기 (좌)]                    [보조 버튼]   [주요 버튼 (우)]   |
+----------------------------------------------------------------+
```

- 배경은 안전 영역 밖까지 꽉 채운다(기존과 같이 `Bg`는 전체 화면, 내용만 안전 영역 안). 노치 쪽 가로 여백은 `SafeAreaFitter`가 처리한다.
- 상단 바: 제목 글꼴 32, 탭은 가로 `ScrollRect`(탭이 많은 창 대비), 탭 최소 폭 112 x 높이 56, 선택 탭은 강조색+밑줄. 재화 칩(골드 등)은 우측. 상점 주인 대사(`keeperLine`)는 모바일에서 내용 영역 상단 한 줄로 내리거나 생략한다(상단 바 폭 부족).
- 하단 바: 좌측 `닫기` 고정(폭 140 x 높이 64, 항상 같은 자리). 우측에 창이 정하는 버튼 0~3개(높이 64, 주요 버튼은 72, 최소 폭 140). 안드로이드 뒤로가기=`Close()`와 같은 동작이다.
- 뒤로가기: 이미 `Cancel`로 처리됨(1.3). 시트가 열려 있으면 시트만 닫는다(시트가 Cancel을 먼저 소비. 3.3).
- 창이 쓰는 API(`WindowScreen`에 모바일에서만 의미 있는 선택 함수 추가, 기본 구현은 아무것도 안 함):

```csharp
// 창 파일(XxxScreen.Mobile.cs)이 override. PC에서는 호출되지 않는다.
protected virtual void BuildMobile(MobileFrame frame) { }   // frame.Top / frame.Content / frame.Bottom
protected void SetMobileTabs(string[] names, int selected, Action<int> onSelect);
protected Button AddBottomButton(string label, Action onClick, bool primary);
protected bool UsesMobileLayout { get; }                    // BuildMobile을 override했는가
```

- 아직 옮기지 않은 창의 기본 동작(안전망): `UsesMobileLayout == false`이면 현재 방식(1280x720 레이아웃+FitToParent)에 하단 바의 `닫기`만 붙인다. 따라서 창을 하나씩 옮겨도 중간 상태가 항상 동작한다.
- 가방(`EquipmentScreen`)은 `MenuScreen` 계통이라 `WindowScreen`을 상속하지 않는다. 같은 `MobileFrame`을 직접 만들어 쓴다(단계 2 W1이 담당).
- 메뉴 패널 계통(타이틀·일시정지·확인·게임오버·캐릭터 선택·로그인): 전체 화면 틀을 쓰지 않고 가운데 카드 형태를 유지한다. `MenuList`에 모바일 모드를 둔다: 행 높이 64(최소), 글꼴 26, ◀ ▶ 폭 64, 값 줄이기는 ◀ 버튼으로 한다(길게 누르기 의존 제거). `MenuScreen.FitToScreen`(`MenuScreens.cs:28-47`)은 모바일에서 축소 하한을 0.9로 두고, 그래도 넘치는 패널(설정·도움말)은 목록을 `ScrollRect`에 넣는다. 설정은 전체 화면 틀을 쓴다.
- 이벤트 시스템: 모바일에서 `EventSystem.pixelDragThreshold`를 24로 올린다(`UIRoot.cs:160` `EnsureEventSystem`). 목록 스와이프와 탭이 구분된다.

### 3.3 아이템 상세 시트 (`ItemDetailSheet`)

호버 툴팁(`GearTip`, 가방 `tooltip`)과 우클릭 메뉴(`EquipmentScreen.OpenMenu`)를 모바일에서 한 부품으로 합친다. 새 파일 `UI/Mobile/ItemDetailSheet.cs`.

```csharp
public struct SheetAction { public string label; public Action onClick; public bool primary; public Func<string> disabledReason; }

public sealed class ItemDetailRequest
{
    public string itemKey;                 // 장비 키("eq_sword_iron+12"), 재료, 소모품 모두 가능
    public int count;                      // 0이면 수량 줄 숨김
    public string extra;                   // 가격/재고/남은 시간 등 창이 붙이는 한두 줄 (리치텍스트)
    public List<SheetAction> actions;      // 0~4개. 비면 [닫기]만
    public RectTransform anchor;           // 눌린 칸. 시트는 anchor의 반대편에 뜬다
    public Action onClosed;
}

public sealed class ItemDetailSheet : MonoBehaviour
{
    public static bool IsOpen { get; }
    public static void Show(ItemDetailRequest req);   // 이미 열려 있으면 내용만 교체
    public static void Hide();
    public static void Hook(Graphic icon, Func<string> key); // 읽기 전용 아이콘(보상 카드, 우편 첨부 등): 탭=시트(닫기만)
}
```

- 모양: 안전 영역 높이 전체, 폭 360. anchor 중심이 화면 오른쪽 절반이면 왼쪽에, 아니면 오른쪽에 붙는다. 시트 밖 영역은 반투명 어둠(탭하면 시트만 닫힘, 뒤 창으로 탭이 새지 않음). 뒤로가기도 시트만 닫는다.
- 구성(위에서 아래): 머리(아이콘 64, 이름 28 - 등급색, 닫기 X 64x64) / 본문 스크롤(등급·요구 레벨·능력치 비교·설명·귀속) / 버튼 줄(최대 3개 보이고 넘치면 본문 하단에 스크롤, 각 높이 64, 주요 72, 간격 8).
- 본문 글은 기존 `GearTooltip.Append`, `AppendBind`, `StatRow`(`GearTooltip.cs:20-77`)와 `ItemText`가 만드는 리치텍스트를 그대로 쓴다. 내용을 새로 만들지 않는다. 단 `AppendBind`가 `<size=16>`을 쓰므로(`GearTooltip.cs:61-63`) 시트가 `<size=N>`을 22 이상으로 올리는 후처리(`MobileUi.ClampRichSize`)를 한다.
- 장비 비교(▲▼)는 이미 `Append`에 들어 있다. 착용 중인 칸을 탭하면 비교 없이 [해제]가 나온다.
- 소비 아이템은 [사용](가방에서 탭하면 즉시 사용하던 동작(`EquipmentScreen.Use`)을 시트 버튼으로 옮겨 오조작을 막는다).
- 어느 창이 쓰는가:

| 창 | 칸/행 탭 시 시트의 버튼 |
|---|---|
| 가방 | 장착·해제·사용·(강화 창으로 이동은 강화 NPC 앞에서만이라 제외) |
| 상점 | 구매(수량 선택 포함)·판매 |
| 창고 | 넣기·꺼내기 |
| 강화 | 닫기 / [선택] (강화 대상 지정) |
| 스킬 | 슬롯 등록 1~5·해제·젬 변경 (아이템이 아니라 스킬 정보를 같은 시트 틀에 넣는 `Show` 오버로드: 키 대신 제목·본문 문자열 전달) |
| 던전 결과·레이드 카드·가챠·우편·경매 | 읽기 전용 + 해당 창 고유 버튼(받기·입찰 등) |

- `GearTooltip.Hook`의 터치 분기(`GearTooltip.cs:89-94`, 길게 누르기)는 시트가 준비된 창부터 `ItemDetailSheet.Hook`으로 바꾼다. 행 전체가 이미 버튼인 창(상점·창고 등)은 `Hook`을 쓰지 않고 행 탭 핸들러가 `Show`를 직접 부른다(아이콘에 클릭 핸들러를 달면 행 버튼으로 이벤트가 안 올라가기 때문).
- `LongPress`는 시트로 대체한 곳에서는 제거한다. 시트 도입 후에도 남는 용도(예: 채팅 이름 길게 누르기 귓속말)가 없으면 `LongPress.cs`의 `LongPress` 클래스는 사용처 0건 확인 후 두고, 삭제는 하지 않는다(PC 영향 없음, 승인 없는 삭제 금지).

### 3.4 손가락 크기 목록·격자 규칙 (기준 해상도 1024x576, 모바일 전용)

| 항목 | 값 | 환산(2400x1080, 약 0.12mm/단위) |
|---|---|---|
| 탭 대상 최소 변 | 64 | 약 7.7mm |
| 기본 탭 대상(목록 행 높이, 격자 칸, 버튼 높이) | 72 | 약 8.7mm(목표 9mm) |
| 주요 버튼 높이 | 72 | 약 8.7mm |
| 보조 버튼 높이 | 64 | 약 7.7mm |
| 서로 붙은 탭 대상 사이 간격 | 8 이상 | 약 1mm |
| 격자 칸 | 72, 간격 6 (6열 = 462 폭) | 한 화면 4행 = 306 + 페이지 줄 64 |
| 아이템 아이콘 | 칸의 72% = 52 | |
| 목록 행(아이콘 있는 행) | 72, 아이콘 52, 이름은 줄바꿈 없이 한 줄, 보조 정보는 우측 | |
| 한 화면 목록 행 수 | 내용 영역 440 / 72 = 6행(헤더 행 있으면 5행) | |
| 스크롤 | 목록은 세로 스와이프(`SwipeSteps`), 격자 페이지는 가로 스와이프(`SwipePage`, 신규) + ◀▶ 버튼 유지 | |

계산 근거: 내용 영역 440 = 안전 영역 576 - 상단 바 64 - 하단 바 72. 태블릿(캔버스 768)에서는 같은 규칙으로 행이 더 많이 보인다(수치는 상수, 행 수는 계산).
스크롤 방식은 `SwipeSteps`(행 단위, 관성 없음)를 기본으로 하고 이미 `ScrollRect`인 목록(가챠·옷장·지도 지역)은 그대로 둔다. 관성 있는 스크롤 전환은 8절 결정 항목이다.

### 3.5 글꼴 체계 (모바일, 캔버스 단위)

현재 `UIFactory.Text`는 요청 크기에 0.88을 곱하고 최소 22로 clamp한다(`UIFactory.cs:128-130`, `UiTheme.cs:54`). 모바일 틀은 0.8배 축소가 없으므로 값이 실제 크기다. 모바일 창 코드는 `MobileUi.Text(...)`로 토큰을 정확한 크기로 만든다(0.88 곱을 건너뜀).

| 토큰 | 크기 | 용도 |
|---|---|---|
| Title | 32 | 창 제목 |
| Heading | 28 | 시트 이름, 섹션 제목, 탭 |
| Button | 26 | 모든 버튼 |
| Body | 24 | 본문, 목록 이름 |
| Label | 22 | 보조 정보, 수치(이보다 작게 쓰지 않음) |

규칙: 22 미만 금지(`UIFactory.MinFontSize` 유지). 시트 리치텍스트의 `<size=16>`도 22로 올림(3.3). PC 토큰(`UiTheme.FontTitle` 40 등)은 건드리지 않는다.

### 3.6 텍스트 넘침 대책

- 한 줄 고정 칸(이름·가격·버튼 글자): `MobileUi.FitLine(Text, minSize 20)` - BestFit(최소 20, 최대 토큰 크기) + 세로 `Truncate`. 그래도 넘치면 `MobileUi.Ellipsize`가 `TextGenerator.GetPreferredWidth`로 재서 "…"로 줄인다(레거시 `Text`는 줄임표 기능이 없다). 전체 이름은 시트에서 본다.
- 여러 줄 본문: 고정 높이 박스 대신 `ScrollRect` 안에 넣는다(시트 본문, 확인 창 본문, 도움말).
- 고정 폭 숫자(골드·가격): 자리수 최대치(예: 99,999,999)로 폭을 잡고 BestFit.
- 확인 캡처 시 가장 긴 문자열(긴 아이템 이름, 레전더리 이름 + 강화 +12, 긴 퀘스트 제목)을 일부러 넣은 상태를 찍는다(6절).

### 3.7 확인 창

- 가운데 카드 형태 유지(전체 화면 틀 쓰지 않음). 폭 560(넓게 820)은 안전 영역 폭의 85%를 상한으로 둔다.
- 예/아니오 가로 2버튼(각 높이 72, 폭 균등), 본문은 스크롤 영역(최대 높이 = S 높이의 55%). 기본 선택·제한 시간·`onNo` 옵션은 기존 `Setup` 로직 그대로(`ConfirmScreen.cs:50-`).
- 시트가 열려 있을 때 확인 창이 필요하면(예: 판매 확인) 시트 위로 뜬다(UIRoot 스택 순서: 창 < 시트 < 확인).

### 3.8 기존 부품과의 관계

| 부품 | 관계 |
|---|---|
| `UIFactory` | 변경 없음. 모바일 전용 도우미는 `MobileUi`에 둔다(`Rect`, `Place`, `Image`, `Text` 재사용). 필요하면 `MobileUi.Text`가 내부에서 호출 |
| `UiTheme` | 색 토큰(`Background`, `Panel`, `Accent` 등)은 그대로 사용. 크기 토큰은 PC 값 유지, 모바일 값은 `MobileUi` |
| `UiSizes` | PC 값 유지(`PageButton` 50x34 등). 모바일은 `MobileUi.Tap` 계열로 대체 |
| `UiDrag` | 드래그 장착·슬롯 등록은 그대로 둔다. 탭 흐름(시트 버튼)으로도 같은 결과가 되게 해서 드래그가 필수가 아니게 한다. `pixelDragThreshold` 상향이 영향을 준다(드래그 시작에 24 이상 필요) |
| `GearTooltip` | `Append`/`AppendBind`/`StatRow`는 텍스트 소스로 재사용. `Hook`의 터치 분기만 시트로 교체 |
| `LongPress` | 시트로 대체되는 곳에서 사용 중단. 클래스는 보존 |
| `SwipeSteps` | 목록 스와이프에 계속 사용. 가로 페이지용 `SwipePage`를 `UI/Mobile/`에 추가 |
| `SafeAreaFitter` | 창 틀이 사용. 캡처용 `-fakeSafeArea` 인자 추가(5.1) |
| `PointerRelay`/`HoverRelay` | 변경 없음. 모바일 창은 호버 대신 탭 |

## 4. 구현 단계, 에이전트, 파일 소유권

공통 규칙
- 에이전트는 General-purpose 구현 에이전트(`model: sonnet`, effort medium)로 보낸다. dotRPG UI 담당 에이전트가 `rules/agents.md` 표에 없으므로 같은 유형이 2회째면 그 자리에서 표에 행을 추가한다.
- 한 파일은 한 에이전트만 고친다(아래 소유권 표). 다른 에이전트 소유 파일이 필요하면 고치지 말고 메인에게 요청 사항으로 보고한다.
- 에이전트는 Unity를 실행하지 않는다(프로젝트 잠금 때문에 동시에 못 연다). 컴파일·캡처는 메인이 단계 끝에 1회 한다.
- 프롬프트에는 대상 파일 절대경로, 이 문서의 해당 절, 허용되는 PC 파일 변경 형태(3.1), 완료 기준, 보고 줄 수 상한(15줄)을 적는다.
- 위임 결과는 메인이 `git diff`(삭제 줄 검사)와 컴파일로 확인한다. 보고 문장만 믿지 않는다.

### 단계 0 - 준비 (메인 직접)

작업
1. `Core/DevCapture.MobileUi.cs` 신규: 모드 `-dotrpgMobileUi <폴더>`. 5.1 참고. 창 그룹별 빈 메서드 `MobileShotsBag`, `MobileShotsShop`, ... 를 `DevCapture.MobileUi.<그룹>.cs` 스텁 파일로 미리 만들어 둔다(이후 각 에이전트가 자기 파일만 채움).
2. `DevCapture.cs`: `Modes` 배열(`:29-30`)과 모드 분기(`:100-111`)에 한 줄씩 추가.
3. `SafeAreaFitter.cs`: `#if !DOTRPG_RELEASE` 안에 `-fakeSafeArea l,b,r,t` 인자 처리(비율 값) 추가.
4. 기준선 촬영: 현재 커밋에서 (a) PC 1280x720·1920x1080, `-touchUI` 없음, (b) 현재 터치 상태 4개 해상도를 `Logs/ui_baseline_pc/`, `Logs/ui_before_touch/`에 찍는다(`Logs`는 `.gitignore` 대상). 이후 PC 회귀 비교의 기준이다.

완료 기준: 컴파일 오류 0. 새 모드가 PC(인자 없음)에서 창 사진을 찍고 종료한다. 기준선 폴더가 생긴다.
의존: 없음.

### 단계 1 - 공용 틀 (에이전트 3 + 메인 1)

선행: 메인이 `UI/Mobile/MobileUi.cs`(상수·도우미 시그니처)를 먼저 쓴다. 이후 아래 세 에이전트가 병렬.

| 에이전트 | 소유 파일 | 작업 |
|---|---|---|
| S1-A 창 틀 | `WindowScreen.cs`, `UIRoot.cs`(스택 순서·`EnsureEventSystem` 드래그 임계값·시트 생성), `UI/Mobile/MobileFrame.cs`(신규), `UI/Mobile/SwipePage.cs`(신규), `UiTheme.cs`/`UIFactory.cs`(추가만) | 3.2 모바일 창 틀, 탭 바, 하단 바, 안전망 기본 동작 |
| S1-B 시트 | `UI/Mobile/ItemDetailSheet.cs`(신규), `GearTooltip.cs`(터치 분기), `ItemText.cs`(필요 시 추가만) | 3.3 시트, `ClampRichSize` |
| S1-C 메뉴 패널 | `MenuList.cs`, `MenuScreens.cs`, `ConfirmScreen.cs`, `UIRootConfirmExtensions.cs`, `OptionScreens.cs`, `LootFilterScreen.cs` | 3.2 메뉴 패널 모바일 모드, 3.7 확인 창, 설정을 전체 화면 틀+스크롤로, 키보드 설정 숨김 조건 통일, 조작 안내 터치용 본문 |
| 메인 | `MobileUi.cs`, `DevCapture.MobileUi.*.cs`(시트·메뉴 촬영 줄 추가) | 상수·도우미 작성, 단계 끝 컴파일·검증 |

`UIRoot.cs`는 S1-A만 고친다. S1-B가 시트 생성 호출이 필요하면 `ItemDetailSheet.Create(Transform)` 시그니처를 먼저 합의해 두고 S1-A가 호출 한 줄을 넣는다(메인이 시그니처를 프롬프트에 박는다).

완료 기준
- 컴파일 오류 0.
- PC 회귀: 3.1 규칙 위반 0(7절 diff 검사), PC 캡처 비교 동일.
- `-touchUI`에서 아직 안 옮긴 창을 열면 현재와 같은 모양+하단 `닫기`가 있다.
- 시트 테스트 캡처(샘플 장비 3종, 소모품 1종, 긴 이름 1종)와 확인 창, 설정, 일시정지 캡처 확보.
의존: 단계 0.

### 단계 2 - P0 창 (에이전트 8, 병렬)

선행: 단계 1 컴파일 통과, API(`MobileFrame`, `SetMobileTabs`, `AddBottomButton`, `ItemDetailSheet.Show/Hook`, `MobileUi` 상수) 동결.

| 에이전트 | 소유 파일(수정) | 신규 파일 | 대상 |
|---|---|---|---|
| W1 가방 | `EquipmentScreen.cs`, `.Layout.cs`, `.Drag.cs`, `.Input.cs`, `.Tooltip.cs` | `EquipmentScreen.Mobile.cs`, `DevCapture.MobileUi.Bag.cs` | #2 |
| W2 상점·창고 | `ServiceScreens.cs`, `BulkSellPanel.cs`, `StorageScreen.cs` | `ShopScreen.Mobile.cs`, `StorageScreen.Mobile.cs`, `DevCapture.MobileUi.Shop.cs` | #3, #5 |
| W3 강화 | `WindowScreens.cs`, `WindowScreens.Attempt.cs`, `EnhanceScreen.Promote.cs`, `EnhanceScreen.Ticket.cs`, `EnhanceFx.cs` | `EnhanceScreen.Mobile.cs`, `DevCapture.MobileUi.Enhance.cs` | #4 |
| W4 스킬 | `SkillScreen.cs`, `.Nodes.cs`, `.Slots.cs`, `.Career.cs` | `SkillScreen.Mobile.cs`, `DevCapture.MobileUi.Skill.cs` | #6 |
| W5 퀘스트·지도 | `QuestScreen.cs`, `WorldMapScreen.cs`, `WorldMapScreen.Atlas.cs` | `QuestScreen.Mobile.cs`, `WorldMapScreen.Mobile.cs`, `DevCapture.MobileUi.Map.cs` | #7, #8 |
| W6 던전 | `DungeonSelectScreen.cs`, `.Sweep.cs`, `.RaidShop.cs`, `DungeonResultScreen.cs`, `.Raid.cs`, `RaidCardView.cs` | `DungeonSelectScreen.Mobile.cs`, `DungeonResultScreen.Mobile.cs`, `DevCapture.MobileUi.Dungeon.cs` | #9~#12 |
| W7 HUD·대화 | `DungeonHudView.cs`, `DialogueBoxView.cs`, `MinimapView.cs`, `HudView.cs`, `HudView.Update.cs` | `DevCapture.MobileUi.Hud.cs` | #13, #14, #40, #41, #42 |
| W8 입력창 | `OnlineScreens.cs`, `CharacterSelectScreen.cs`, `WithdrawalScreen.cs`, `ChatView.cs` | `UI/Mobile/KeyboardAvoider.cs`, `DevCapture.MobileUi.Input.cs` | #25, #26, #27, #39 |

설명
- W8은 키보드 회피 부품(`TouchScreenKeyboard.area`로 키보드 높이를 읽어 패널을 위로 올림)을 만든다. 개발 캡처용 가짜 키보드 높이 인자 `-fakeKeyboardHeight <px>`도 이 부품에 둔다(`#if !DOTRPG_RELEASE`).
- W8의 채팅: 접힘 스트립, 탭=입력 열기(+`KeyboardAvoider`), 펼침 상태, 신호 칩. 채팅 창 위치는 8절 결정 항목.
- W2가 시트 버튼의 구매/판매/넣기/꺼내기 로직을 기존 함수 재사용으로 연결한다(새 경제 로직 금지, 서버 동기화 경로 `OnlineEconomy` 호출은 기존 그대로).
- W1은 가방의 소비 아이템 탭=즉시 사용을 시트 버튼으로 바꾼다(모바일만).

완료 기준(단계 전체)
- 컴파일 오류 0.
- PC 회귀: 규칙 위반 0, PC 캡처 비교 동일.
- 모바일 4개 해상도 캡처에서 P0 창이 3.4~3.6 규칙을 지킨다(탭 대상 64 이상, 글꼴 22 이상, 넘침 없음)를 메인이 수치 점검 스크립트로 확인한다: 캡처 모드가 창 안의 `Button`/`Selectable`의 화면 크기를 로그로 내보내고(5.1) 64 미만 항목을 목록으로 출력한다. 시각 판정은 사용자.
- 모든 P0 창이 터치만으로 끝까지 조작된다(호버·우클릭·키 없이): 창별 "터치 조작 점검표"(표 9.2)를 체크.
의존: 단계 1.

### 단계 3 - P1 창 (에이전트 4, 병렬)

| 에이전트 | 소유 파일(수정) | 신규 파일 | 대상 |
|---|---|---|---|
| X1 파티·소셜 | `OnlineWindows.cs`, `PartyScreen.cs`, `PartyLobbyScreen.cs`, `SocialScreen.cs` | `PartyScreen.Mobile.cs`, `PartyLobbyScreen.Mobile.cs`, `PartyFinderScreen.Mobile.cs`, `DevCapture.MobileUi.Party.cs` | #28~#31 |
| X2 우편·경매 | `MailScreen.cs`, `AuctionScreen.cs` | `MailScreen.Mobile.cs`, `AuctionScreen.Mobile.cs`, `DevCapture.MobileUi.Trade.cs` | #32, #33 |
| X3 가챠 | `GachaScreen.cs`, `.Refresh.cs`, `.Reveal.cs`, `.Sealed.cs`, `.Tiers.cs` | `GachaScreen.Mobile.cs`, `DevCapture.MobileUi.Gacha.cs` | #34 |
| X4 보상·옷장 | `LevelRewardScreen.cs`, `AchievementScreen.cs`, `CosmeticShopScreen.cs`, `CosmeticSynthScreen.cs`, `TipView.cs`, `PartyFramesView.cs`, `BuffBarView.cs`, `BossHpBarView.cs`, `SideMenuView.cs`, `QuickItemBar.cs` | `LevelRewardScreen.Mobile.cs`, `AchievementScreen.Mobile.cs`, `CosmeticShopScreen.Mobile.cs`, `CosmeticSynthScreen.Mobile.cs`, `DevCapture.MobileUi.Extra.cs` | #35~#38, #43 |

주의: `OnlineWindows.cs`의 `OnlineWindow` 기반 클래스는 X1만 고친다. X2~X4는 `OnlineWindow`를 상속만 하고 그 파일을 고치지 않는다. 기반 클래스에 필요한 변경(미리보기 배너 위치, 재화 칩 등)은 X1이 단계 3 시작 시 먼저 처리하고 X2~X4는 그 뒤에 출발한다(메인이 X1의 기반 변경만 먼저 컴파일 확인).
완료 기준: 단계 2와 같다(P1 창 대상).
의존: 단계 2 완료(공용 틀 사용법이 실제 창에서 검증된 뒤). X2~X4는 X1 기반 변경 이후.

### 단계 4 - 선택 기능과 정리 (에이전트 2)

| 에이전트 | 소유 파일 | 작업 |
|---|---|---|
| Y1 터치 버튼 설정 | `TouchControls.cs`, `TouchControls.Parts.cs`, `TouchControls.Menu.cs`, `Settings/SettingsData.cs`, `Settings/SettingsManager.cs`, `MenuScreens.cs`(설정 한 줄만), 신규 `TouchLayoutScreen.cs`, `DevCapture.MobileUi.Touch.cs` | 요구 (5): 설정에서 터치 버튼 크기·위치 조절 |
| Y2 정리 | 단계 1~3 중 남은 지적 사항 파일 | 캡처 판정 후 사용자가 지적한 항목 수정, `GearTip`/가방 `tooltip`의 모바일 미사용 코드 확인(삭제 아님, 보고) |

Y1 상세: 현재 위치·크기가 상수(`TouchControls.cs:20-27` `AttackSize`, `DodgePos`, `SkillPos`, `InteractPos` 등)이므로 "기본 배치 + 사용자 오프셋·배율"을 `SettingsData`에 저장한다. 설정 화면 항목 "터치 버튼 설정"은 편집 화면을 연다: 실제 HUD 위에서 버튼을 드래그하고 배율 슬라이더(0.8~1.4), 초기화, 저장, 겹침·화면 밖 방지(안전 영역 안으로 clamp). 기본값은 지금 배치와 같다. 설정 데이터는 세이브 호환(기본값 있는 필드 추가).
완료 기준: 컴파일 0, PC 회귀 동일, 편집 화면 캡처, 저장·복원·초기화 동작(로직 검토 + 메인 1회 실행은 사용자 요청 시).
의존: 단계 2(설정 화면 틀). Y1은 단계 3과 병렬 가능(파일 겹침 없음: `MenuScreens.cs`는 단계 1 이후 Y1만 수정).

### 단계 5 - 최종 검증과 문서 (메인)

1. 전체 컴파일(0 오류), PC 회귀 diff·캡처 비교.
2. 모바일 캡처 전체 세트 촬영(6절) -> 사용자에게 폴더 경로 전달. 판정은 사용자.
3. 문서 갱신: `Docs/ANDROID.md`(4절 후속 항목 중 설정창 항목 완료 표시, 6절의 "터치 입력/HUD" 항목), `Docs/UI_AUDIT.md`에 모바일 절 추가.
4. 커밋은 사용자 요청 시에만 한다.

## 5. 캡처 도구 계획

### 5.1 새 모드 `-dotrpgMobileUi`

기존 `-dotrpgUi`가 PC 사이드 메뉴 버튼을 눌러 창을 열기 때문에(`DevCapture.Ui.cs:61,148`) 터치 UI에는 맞지 않는다. 새 모드를 둔다.

실행 예
```
dotRPG.exe -dotrpgMobileUi <출력폴더> -touchUI -screen-width 1200 -screen-height 540 [-fakeSafeArea 0.04,0,0.04,0] [-fakeKeyboardHeight 540]
```

- 설정: `UiShowcase`의 준비 부분(새 게임 마법사, 가방·골드·재료 채우기, `AddXp`, 젬 장착 `DevCapture.Ui.cs:30-60`)을 따라 한다. 기존 `UiShowcase`는 수정하지 않고(기존 함수 수정 금지) 새 파일에서 필요한 줄만 다시 쓴다.
- 창 열기: 버튼 클릭이 아니라 직접 호출(`Game.Flow.OpenInventory()`, `Game.Flow.OpenWindow(Game.UI.Shop)` 등. 근거 `UIRoot.cs:55-70`, `GameFlow.cs:84-92`). 상점·강화는 `SetKeeper` 후 `OpenWindow`.
- 시트: `ItemDetailSheet.Show`를 샘플 아이템으로 직접 호출. 탭 시뮬레이션은 `ExecuteEvents.Execute(..., pointerClickHandler)`(기존 방식 `DevCapture.Ui.cs:58-60`).
- 해상도: `ApplyRequestedResolution`(`DevCapture.Ui.cs:143`)을 호출. 캔버스 배율이 해상도에 비례하므로 같은 가로세로비의 절반 크기(1200x540 = 2400x1080의 절반)로 찍어도 캔버스 단위 배치는 같다. 데스크톱 모니터가 작아 원 해상도가 안 잡히면 절반 크기로 찍는다(글꼴 거칠기만 다름). 원 해상도 창이 잡히는지는 미확인이다.
- 노치: PC는 `Screen.safeArea`가 전체 화면이므로 `-fakeSafeArea`(좌,하,우,상 비율)를 `SafeAreaFitter.Apply`에서 읽는다(`#if !DOTRPG_RELEASE`).
- 키보드: `-fakeKeyboardHeight`를 `KeyboardAvoider`가 읽는다(`#if !DOTRPG_RELEASE`).
- 수치 점검 로그: 각 창에서 활성 `Selectable`과 `Text`의 화면상 크기를 계산해 `mobile_audit.txt`에 `창, 객체, 폭x높이(캔버스 단위), 글꼴` 로 기록하고 64 미만 탭 대상과 22 미만 글꼴을 모은다. 시각 판정이 아니라 규칙 위반 목록이다.
- 출력 이름: `<해상도>_<번호>_<창>_<상태>.png`. 끝나면 `Application.Quit()`(기존 모드와 같다, `DevCapture.cs:126-137`).
- 릴리스 빌드 제외: 새 파일 전체를 `#if !DOTRPG_RELEASE`로 감싼다.

### 5.2 PC 회귀 비교

같은 모드를 `-touchUI` 없이 1280x720·1920x1080으로 실행(PC에서도 같은 창 열기 호출이 동작해야 하므로 창 호출은 `TouchUi`에 의존하지 않게 쓴다). 결과를 `Logs/ui_baseline_pc/`와 PNG 바이트 비교(또는 픽셀 차이 개수)한다. 연출·파티클이 다른 창은 차이 허용치를 두고 목록으로 보고한다.

## 6. 검증 계획

### 6.1 단계마다

| 항목 | 방법 | 통과 기준 |
|---|---|---|
| 컴파일 | 메인이 `Unity -batchmode -nographics -quit -projectPath <프로젝트> -logFile Logs/mobile_ui_compile.log` 실행 후 `grep "error CS"` (`Docs/ANDROID.md`의 배치 실행 방식과 같다. 에디터가 이미 열려 있으면 먼저 닫아야 한다) | 오류 0 |
| PC 경로 diff | `git diff -U0 -- Assets/Scripts/Runtime | grep '^-[^-]'` 로 삭제/변경된 줄을 뽑아 검사 | 삭제 줄은 `class` 선언 줄(`partial` 삽입)뿐. 그 밖의 `-` 줄이 있으면 불합격 |
| PC 추가 줄 검사 | `git diff -U0` 의 `+` 줄 중 기존 파일(신규 아님)에 추가된 것이 `TouchUi.Enabled` 분기 호출이거나 `partial` 뿐인지 확인 | 위반 0 |
| PC 캡처 비교 | 5.2 | 동일 또는 설명된 차이만 |
| 규칙 점검 로그 | `mobile_audit.txt`의 64 미만 탭 대상, 22 미만 글꼴 | 대상 창에서 0건(의도된 예외는 문서에 사유) |
| 터치 조작 점검표 | 9.2 표를 창마다 코드 읽기로 확인 | 호버·우클릭·키 의존 0 |

### 6.2 사용자가 볼 캡처 목록

해상도 4종(각각 `-touchUI`): 2400x1080(안전 영역 없음), 1920x1080, 2340x1080(노치: `-fakeSafeArea 0.045,0,0.045,0`), 2048x1536(태블릿).
각 해상도에서 찍는 상태(번호 순):

1. 평소 HUD(조이스틱 구역·부채꼴·채팅 접힘·퀘스트 줄·미니맵·상단 버튼)
2. 터치 메뉴 격자
3. 가방: 전체 탭 / 무기 탭 / 기타 탭 / 시트(장비, 비교 ▲▼) / 시트(소비 아이템) / 착용 칸 시트 / 긴 이름 장비
4. 상점: 구매 / 판매 / 시트(수량) / 일괄 판매
5. 강화: 장비 선택 / 티켓 전환 / 결과 연출 직전
6. 창고: 가방 탭 / 창고 탭 / 시트
7. 스킬: 트리 / 젬 / 직업 / 시트(슬롯 등록)
8. 퀘스트 목록 / 상세, 지도(지역 목록, NPC 선택 상태)
9. 던전 선택: 요일 / 레이드 / 소탕 / 레이드 상점, 던전 결과(카드 뒤집기 전후, 시트)
10. 던전 HUD(시계·콤보·방 지도·마을로 버튼·부활 패널)
11. 대화창(일반 / 이름표 / 건너뛰기 판), 컷신 건너뛰기
12. 타이틀 / 일시정지 / 설정(맨 위, 맨 아래 스크롤) / 확인 창(짧은 본문, 긴 본문) / 게임 오버
13. 로그인(키보드 없음 / `-fakeKeyboardHeight` 있음), 캐릭터 선택(이름 입력 중), 온라인 캐릭터 목록
14. 채팅: 접힘 / 입력 열림(키보드 있음) / 펼침+신호 칩
15. P1: 파티, 파티 로비, 파티 찾기, 우편(목록·상세·첨부 시트), 경매(조회·등록·입찰 시트), 가챠(목록·결과), 레벨 보상, 업적, 옷장
16. 단계 4: 터치 버튼 설정 편집 화면(기본 / 버튼 이동·확대 후)

해상도 4종 x 약 70상태는 많으므로 모든 상태를 4종에서 찍지 않는다: 1번~16번 전체는 2400x1080에서, 노치(2340)에서는 상단 바·하단 바·시트·HUD가 있는 대표 10상태, 1920x1080에서는 가방·상점·설정·로그인·채팅 5상태, 태블릿에서는 가방·상점·스킬·설정 4상태를 찍는다.
캡처 판정은 사용자가 한다.

### 6.3 기기 확인 (사용자)

에뮬레이터/실기기 확인은 `Docs/ANDROID.md` 3절의 미확인 항목에 이어서 한다. 소프트 키보드 높이(`TouchScreenKeyboard.area`)와 실제 안전 영역은 PC 가짜 값으로는 검증이 안 되므로 기기 1회 확인이 필요하다(미확인).

## 7. 위험

| 위험 | 영향 | 대응 |
|---|---|---|
| PC 경로를 건드리는 실수(들여쓰기·partial 추가 외 편집) | PC 회귀 | 3.1 규칙, 6.1 diff 검사(삭제 줄 `class` 선언만), PC 캡처 비교 |
| 여러 에이전트가 같은 파일을 고침 | 충돌·덮어쓰기 | 4절 소유권 표, 에이전트 프롬프트에 "소유 파일 외 수정 금지" |
| 파일 500줄 초과 | 규칙 위반 | 모바일 코드는 `*.Mobile.cs`로 분리. 기존 파일이 이미 500줄 근처(`ServiceScreens.cs` 467, `RaidCardView.cs` 443)이므로 추가 줄은 새 파일에만 둔다 |
| `partial` 추가가 필요한 클래스가 non-partial(`QuestScreen`, `StorageScreen`, `PartyScreen`, `ShopScreen` 등) | 단어 하나 변경이 PC 파일에 들어감 | 허용 변경으로 명시. 검사 스크립트가 이 형태만 통과시킴 |
| `pixelDragThreshold` 상향이 드래그 장착·슬롯 등록 감도를 바꿈 | 드래그 조작이 둔해짐 | 모바일 전용, 24 시작값을 기기 확인 후 조정. 모든 드래그 동작에 탭 대안 제공 |
| 레거시 `InputField`의 안드로이드 키보드 동작(키보드 가림, 입력 칸 포커스 유지) | 로그인·채팅 불편 | `KeyboardAvoider` + 기기 확인. 키보드 영역 API가 환경별로 다를 수 있음(미확인) |
| 안전 영역/노치 값이 기기마다 다름 | 가장자리 잘림 | 모든 창을 `SafeAreaFitter` 안에 배치, 960 폭 하한으로 설계 |
| 시트가 가리는 영역에 중요한 정보가 있는 창 | 비교 불편 | 시트가 anchor 반대편에 뜸(3.3). 캡처로 창마다 확인 |
| 서버 동기화 경로(구매·판매·넣기)를 시트 버튼이 우회 | 재화 오류 | 시트 버튼은 기존 함수만 호출(새 경제 로직 금지). `OnlineEconomy` 분기 그대로 |
| 원 해상도 캡처가 데스크톱 해상도 제한으로 안 잡힘 | 캡처 정밀도 | 절반 크기 + 같은 비율로 대체(5.1) |
| 한 단계에 에이전트 8개 병렬 | 합치기 부담, 컴파일 오류 한꺼번에 노출 | 소유 파일이 겹치지 않고 신규 파일 위주. 컴파일 오류는 소유 에이전트에게 돌려보내 수정 |

## 8. 결정이 필요한 항목 (추천안 포함)

1. 스크롤 방식. (A) 지금의 `SwipeSteps`(행 단위, 관성 없음) 유지, 격자는 가로 스와이프 페이지. (B) 모든 목록을 `ScrollRect`+행 재사용으로 바꿔 관성 스크롤. 추천은 A다. 이유: 창마다 데이터 연결을 다시 짜야 하는 B는 변경 범위가 크고, A로도 한 손가락 스와이프는 된다. 관성이 아쉽다는 의견이 캡처/기기 확인 뒤에 나오면 목록이 긴 창(경매·우편·상점)부터 B로 바꾼다.
2. 시트 위치. 추천은 "눌린 칸의 반대편 가장자리, 폭 360". 하단 시트는 세로 576이라 본문이 너무 좁아져 제외한다. 다른 선호가 있으면 알려 달라.
3. 채팅 접힘 스트립 위치. 후보: 화면 상단 중앙(상태바와 퀘스트 줄 사이), 좌측 중단. 조이스틱 구역은 좌하 가로 46% x 세로 64%(`TouchControls.cs:56-60`)라 하단은 피한다. 추천은 상단 중앙이다. 캡처 후 조정.
4. 캡처 실행 시점. 추천은 단계 2 끝과 단계 5 끝 두 번만 실행하고(그 사이 단계는 수치 점검 로그 + 컴파일), 단계 1 끝에는 시트·메뉴 샘플만 찍는다. 단계마다 전체 캡처를 원하면 알려 달라.
5. 단계 4(터치 버튼 크기·위치 설정)를 이번에 할지. 사용자 요구 (5)는 "선택"이었다. 추천은 단계 3 끝난 뒤 진행. 설정 데이터 필드 추가가 필요하므로 서버 세이브 호환(`SettingsData`)은 기본값 있는 필드 추가로 처리.
6. 모바일에서 소비 아이템 탭 동작. 지금은 탭=즉시 사용이다. 추천은 시트의 [사용] 버튼으로 바꿔 오조작(물약 낭비)을 줄인다. 한 번에 사용하는 편이 좋으면 가방 소비 탭만 예외로 둘 수 있다.
7. 가방 소비 아이템 외 "버리기/파기" 기능은 현재 없다(코드 조사 결과). 시트에 넣지 않는다. 추가가 필요하면 별도 기능으로 요청.
8. 패널형 메뉴(타이틀·일시정지·확인·로그인 등)는 전체 화면 틀 대신 가운데 카드 유지가 추천이다. 모두 전체 화면으로 통일하길 원하면 알려 달라(설정·도움말만 전체 화면으로 이 계획에 포함).

## 9. 부록

### 9.1 모바일 파일 배치

```
UI/Mobile/MobileUi.cs            상수(탭 크기·글꼴 토큰), Text/FitLine/Ellipsize/ClampRichSize
UI/Mobile/MobileFrame.cs         상단 바·탭·하단 바
UI/Mobile/ItemDetailSheet.cs     아이템 상세 시트
UI/Mobile/SwipePage.cs           가로 스와이프 페이지
UI/Mobile/KeyboardAvoider.cs     소프트 키보드 회피
UI/XxxScreen.Mobile.cs           창별 모바일 배치(partial)
Core/DevCapture.MobileUi*.cs     캡처 모드(+그룹별 파일)
```

### 9.2 창별 터치 조작 점검표 (단계 2~3 완료 기준)

각 창에서 아래가 터치만으로 가능해야 한다.
- 열기와 닫기(하단 `닫기`, 뒤로가기)
- 목록 이동(스와이프 또는 ◀▶)
- 항목 선택 -> 상세(시트 또는 상세 패널)
- 모든 실행 버튼(구매·판매·장착·강화·받기·입찰 등)
- 이전에 우클릭·길게 누르기·키로만 가능했던 동작: 가방 메뉴, 창고 반대 방향 이동, 스킬 소켓 되돌리기와 슬롯 해제, 강화 티켓 전환, 던전/스킬 탭 전환, 설정 값 줄이기, 채팅 열기·보내기·신호
- 글이 길 때 잘리지 않고 시트나 스크롤로 끝까지 읽힌다
