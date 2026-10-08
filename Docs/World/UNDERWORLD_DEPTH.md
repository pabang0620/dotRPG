# 지하세계 배경화와 유적 고도화

> 이전 시안 기록이다. 2026-10-09의 재구성과 최신 확인 결과는 [공통 시점 재구성](UNDERWORLD_COMPOSITION.md)을 참조한다. 아래의 122개 검사는 이전 아트 버전 결과다.

네 사냥터마다 한 장의 이어진 원경 배경화를 제작하고 실제 지형 뒤에 연결했다. 모든 배경은 이번 프로젝트용으로 제작한 원본이며 다른 게임의 이미지를 복사하거나 추출하지 않았다.

## 지역별 풍경

| 지역 | 원경 배경화 | 가까운 주요 구조물 |
|---|---|---|
| B1 뿌리 아래 갱도 | 층층이 이어진 폐광 갤러리, 사슬 승강기, 거대한 권양 장치 | 석조 버팀대와 청동 권양기, 버려진 광차 |
| B2 뒤엉킨 뿌리굴 | 거대한 뿌리가 감싼 지하 회랑과 묘실 | 뿌리에 묻힌 각인 석비 |
| B2 푸른 포자 동굴 | 부서진 수로교, 층진 저수조와 균류 군락 | 버섯이 자라는 수로 유적 |
| B3 반딧불 심연 | 깊은 물과 고대 천문 관측 장치, 무너진 열주 | 깨진 청동 고리와 석주 |

## 게임 연결

- 맵마다 하나의 PNG를 전체 영역에 배치한다. 원경은 타일 반복 없이 연속되며 정렬 순서는 -31000이다.
- 보행 바닥·물·절벽 경계는 별도로 유지한다. 기존 `UnderworldContour`의 이동·충돌 윤곽을 사용한다.
- 밝은 윤곽선과 모든 방향을 동일하게 둘러싸던 암벽 띠를 제거했다. 실제 바닥 위·아래 위치를 조회해 남쪽은 바닥 아래로 내려가는 기초벽, 북쪽은 매몰된 옹벽, 측면은 짧은 벽 끝으로 구분한다. 고립된 수면에도 돌 제방을 유지한다.
- 자갈 바닥 대신 구조물과 같은 석판·축대 재질을 새로 제작했다. 낮은 대비의 먼 배경, 방향이 맞는 벽면, 바닥에 붙는 부벽·기둥으로 가까운 지형과 원경 사이를 잇는다.
- 주요 구조물은 기존에 막힌 넓은 암반 위에 놓인다. 새 충돌체나 통로를 만들지 않는다. `TreeFade`로 캐릭터 가림을 완화한다.
- 24개의 느린 먼지·포자와 한 장의 원경, 주요 유적·접촉 그림자, 최대 5개의 석조 받침 및 두 개의 기둥 잔해로 장면별 새 렌더러 수를 최대 34개로 제한한다. 반복적으로 파티클을 생성하지 않는다.
- 기존 내부 암반 섬 두 곳은 낮은 기둥 잔해로 표시한다. 기둥 발밑은 원래의 충돌 영역이고 상부만 깊이 정렬과 가림 완화를 사용한다. 바닥 석판은 기둥보다 작은 크기, 세로로 압축된 비율로 샘플링하여 벽면과 구분한다.
- 맵 루트를 제거할 때 유적과 환경 움직임도 제거한다. 재사용 PNG·스프라이트는 네 지역으로 한정된 캐시에 보관한다.
- 원경과 유적 텍스처는 Point 필터다. 가까운 유적과 부벽은 엔진에서 지형과 같은 32px/타일로 래스터화하여 밀도를 맞춘다. 부벽은 기존 암반 안으로만 그리며, 접촉 그림자는 보행 지면 안으로만 그린다. 카메라 배율, 이동 경로, 포털, 레벨, 몬스터와 무기 성능을 변경하지 않았다.

## 파일

- `Assets/StreamingAssets/Underworld/backdrop-descent.png`
- `Assets/StreamingAssets/Underworld/backdrop-roots.png`
- `Assets/StreamingAssets/Underworld/backdrop-fungal.png`
- `Assets/StreamingAssets/Underworld/backdrop-depths.png`
- `Assets/StreamingAssets/Underworld/depth-landmarks.png`: 투명 배경의 2×2 전용 유적 시트.
- `Assets/StreamingAssets/Underworld/ground-masonry.png`, `retaining-masonry.png`: 바닥과 축대용으로 함께 제작한 석조 재질.
- `Assets/StreamingAssets/Underworld/masonry-joinery.png`: 지지 기둥·부벽·낮은 벽의 투명 아트 시트.
- `Assets/Scripts/Runtime/Art/UnderworldArt.Depth.cs`: 배경 로딩, 유적 정렬, 제한된 환경 움직임.
- `Assets/Scripts/Runtime/Art/UnderworldArt.Structures.cs`: 실제 절벽 경계에 맞춘 석조 받침과 접촉 그림자.
- `Assets/Scripts/Runtime/Art/UnderworldArt.Terrain.cs`: 지역별 석재 색, 바닥 반복 완화, 먼 암벽 연결.
- `Assets/Scripts/Runtime/Art/UnderworldArt.cs`: 주요 유적 연결 및 주변 장식의 밀도 조정.
- `Assets/Scripts/Runtime/Art/WorldAtlasArt.cs`: 지하 연결지도에 폐광·묘실·수로·봉인 유적 표현.
- `Assets/Scripts/Runtime/Core/DevCapture.WorldLayers.cs`: 무음 실행 검사와 전체 맵·실제 플레이·유적 근접 캡처.

내장 **image_gen**으로 PNG를 제작했다. 원본은 프로젝트에 복사되어 개인 생성 캐시에 의존하지 않는다. 전체 생성·배치 수정 프롬프트는 [제작 기록](underworld-depth-art-prompts.json)에 있다. 임시 대체 이미지는 사용하지 않는다.

자연스러운 연결을 위한 추가 제작 기록: [바닥·벽 재질](underworld-masonry-art-prompts.json), [구조물 연결 부품](underworld-joinery-art-prompt.json).

## 참고한 환경 구성

[로스트아크의 모라이 유적 실제 게임 화면](https://www.gamesradar.com/lost-ark-morai-ruins/)과 [공식 엘가시아·카양겔 이미지](https://www.playlostark.com/en-us/game/releases/slay-to-elgacia-june)를 브라우저로 확인했다. 통로의 줄눈, 받침벽, 기둥이 같은 재료와 명암으로 연결되는 점을 관찰했고, 이를 이번 수정의 석조 재질 통일·방향별 기초벽·중간 거리 부벽에 반영했다. 이는 이미지 관찰을 통한 설계 해석이며 로스트아크의 내부 렌더링 구현을 재현한 것이 아니다.

[Blizzard의 Diablo IV 환경 제작 자료](https://news.blizzard.com/en-us/article/23788294/diablo-iv-quarterly-updatemarch-2022)에서 설명하는 지하 공간의 여러 깊이, 재료와 구조물을 통한 지역 구별, 고정 카메라에서의 전경 구성을 참고했다. 이를 이번 게임에서는 가까운 충돌 경계와 멀리 이어지는 유적 배경의 분리로 해석했다.

[Team Cherry의 Fungal Wastes 소개](https://www.teamcherry.com.au/blog/backer-ghosts-fungal-wastes-front-page-of-reddit)는 균류 지역의 생태적 개성을 참고하는 자료로 사용했다. 실제 적용 아트는 이 게임의 시점과 팔레트에 맞춰 새로 제작했다.

## 확인 방법

바닥은 기존 큰 석판 시안에서 작은 석판으로 한 번 더 조정했다. 구조물 벽돌과 크기가 지나치게 다르지 않도록 하고, 바닥의 세로 비율을 눌러 수직 벽면과 구별했다. 내부의 작은 통행 불가 암반 두 곳에는 기둥 잔해를 놓아 판정과 외형을 맞췄다.

바탕화면 **dotRPG 지상·지하 월드 체험** 바로가기를 실행한다. 뿌리샘 마을 중앙의 계단에서 지하로 내려갈 수 있다. 일반 저장 데이터와 분리된 로컬 체험이며 소리는 0으로 시작한다. 지도에서 **지하월드**를 누르면 지역별 세부 지도를 확인할 수 있다.

전체 플레이어를 Unity Editor에서 다시 빌드한 결과와 구분해야 한다. 이 환경은 Unity Editor 라이선스가 없어 기존 로컬 플레이어에 새로 컴파일한 런타임 DLL과 StreamingAssets를 적용해 검증한다. 온라인 다인 세션 및 여러 사양 PC의 성능 측정은 이번 시각 작업에서 수행하지 않았다.

## 최종 실행 확인 — 2026-10-08

- 전체 런타임 소스 컴파일 및 `DOTRPG_RELEASE` 조건 컴파일 성공. 기존 사용 중단 API/미사용 멤버 경고가 남아 있다.
- 실제 Windows 플레이어에서 무음 검사 **122 통과 / 0 실패**. [실행 기록](UnderworldDepth/native-scenery-report.txt).
- 네 맵 모두 출입구 사이 실제 이동, 경계 연결, 물·암반 충돌 1,636개 표본, 포털 도착 위치, 맵 재입장·지상 복귀 정리 확인.
- 주요 유적과 내부 기둥 잔해의 발밑이 기존 충돌 영역에 있고, 가까운 아트가 32px/타일·Point 필터를 사용함을 확인.
- 반복 입장 시 오브젝트 수가 일정하고, 지상 복귀 시 지하 환경 효과가 제거됨을 확인.
- QA 이동 검사는 몬스터 AI를 정지한 별도 테스트 세션에서 수행했다. 일반 로컬 체험에서는 전투 AI가 정상 동작한다. 이번 판정 결과를 온라인 전투·파티 성능 검증으로 간주하지 않는다.

실제 게임 렌더러에서 촬영한 비교와 미리보기:

| 맵 | 전체 지도 | 유적 근접 | 일반 플레이 |
|---|---|---|---|
| 폐광 | [전체](UnderworldDepth/hollow_descent-overview.png) | [근접](UnderworldDepth/hollow_descent-landmark.png) | [플레이](UnderworldDepth/hollow_descent-gameplay.png) |
| 뿌리굴 | [전체](UnderworldDepth/hollow_roots-overview.png) | [근접](UnderworldDepth/hollow_roots-landmark.png) | [플레이](UnderworldDepth/hollow_roots-gameplay.png) |
| 포자 동굴 | [전체](UnderworldDepth/hollow_fungal-overview.png) | [근접](UnderworldDepth/hollow_fungal-landmark.png) | [플레이](UnderworldDepth/hollow_fungal-gameplay.png) |
| 심연 | [전체](UnderworldDepth/hollow_depths-overview.png) | [근접](UnderworldDepth/hollow_depths-landmark.png) | [플레이](UnderworldDepth/hollow_depths-gameplay.png) |

[심연의 기존 암벽 배경](UnderworldDepth/hollow_depths-before.png)과 [수정된 전체 풍경](UnderworldDepth/hollow_depths-overview.png), [지하 월드 연결 지도](UnderworldDepth/world-atlas-underground.png)를 비교할 수 있다. 전체 지도와 근접 캡처는 경치를 살펴보기 위해 HUD와 체력바를 숨겼고, 일반 플레이 캡처는 기존 카메라와 UI를 그대로 사용했다.
