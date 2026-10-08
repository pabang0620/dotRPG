# 지하세계 공통 시점 재구성

2026-10-09. 이전 `UNDERWORLD_DEPTH.md`의 개별 파노라마·반복 석판 조합을 대체하는 작업이다. 지하 사냥터 네 곳만 다시 구성하며 지상 지역, 중앙 마을, 카메라 배율, 전투 수치와 월드 연결 관계는 유지한다.

## 관찰과 적용

[모라이 유적 실제 게임 화면](https://www.inven.co.kr/board/lostark/4821/5458)의 두 장면에서 보행면의 사선 축, 그 아래 긴 기둥, 더 낮은 회랑이 같은 시점으로 이어지는 것을 확인했다. [몽환의 궁전 실제 화면](https://gamerpillar.com/phantom-palace-chess-puzzle-lost-ark/)에서도 바닥의 사선과 주변 계단·수직 건축물의 방향을 비교했다. 이 관찰은 화면 구성 참고이며 로스트아크의 내부 엔진이나 높이 시스템을 재현했다는 뜻은 아니다. 원본 게임 이미지는 프로젝트에 넣지 않았다.

기존에는 별도로 그린 원경 위에 다른 각도의 평평한 지면을 얹어 접합점이 없었다. 이번에는 보행 테라스, 두꺼운 가장자리, 수직 지지벽, 낮은 회랑을 **한 구도에서 함께 그린 후 같은 좌표의 레이어로 분리**한다. 카메라에 따라 원경만 움직이는 시차 효과는 사용하지 않는다. 접합부의 위치가 어긋나기 때문이다.

| 지역 | 공간과 배경 |
|---|---|
| 뿌리 아래 갱도 | 꺾인 분기점, 채굴 단상, 장대한 축대와 아래층 운반 회랑 |
| 뒤엉킨 뿌리굴 | 중앙 협곡을 감싸는 회랑, 구조물 사이로 내려가는 거대한 뿌리 |
| 푸른 포자 동굴 | 하부 수로교와 빛나는 수면, 축대에 붙은 균류 군락 |
| 반딧불 심연 | 어긋난 석조 단상, 서쪽 물 웅덩이, 깊은 성당 열주와 창 |

## 실제 구현

- `UnderworldComposition.cs`: 56×48 지형 초안과 연결 동선. 기존 포털 좌표 유지.
- `UnderworldCompositionNav.cs`: 최종 그림의 바닥 윗면과 수면을 추적한 이동 윤곽. 그림 생성 중 달라진 윤곽을 충돌에 반영한다.
- `UnderworldContour.cs`: 선택한 지하 맵의 1/8타일 충돌 마스크와 거리장. 다른 맵의 기존 동작 보존.
- `WorldBuilder.Underworld.cs`: 기존 Tilemap/CompositeCollider2D, 포털, 몬스터 연결.
- `UnderworldArt.Composition.cs`: 한 원본을 동일한 원점·배율의 원경(-31000), 지지벽(-30500), 보행면/수면(-30000)으로 배타 분리한다. 픽셀을 덧그려 경계를 숨기지 않는다.
- `UnderworldArt.CompositionOcclusion.cs`: 심연의 기둥 7개를 같은 원본 픽셀에서 별도로 추출해 발밑 기준 앞뒤 정렬과 기존 가림 투명화를 적용한다. 이것은 캐릭터 가림을 위한 의도적인 중복 렌더링이다. 기둥 밑동 충돌은 이동 윤곽이 담당하며, 투명화해도 바닥에 빈 구멍이 생기지 않는다.
- 원경은 충돌과 분리된 시각 요소다. 낮은 회랑으로 이동하거나 두 높이 사이를 오가는 시스템을 새로 만들지 않았다. 통행 가능한 높이는 기존 한 층이다.
- 물결과 24개 환경 입자는 맵 루트에 속한다. 새 맵으로 이동하면 생성한 스프라이트·텍스처와 함께 정리한다.
- 지형 원화는 `Assets/StreamingAssets/Underworld/composition-{descent,roots,fungal,depths}.png`다. 모두 이번에 내장 image_gen으로 만든 실제 사용 리소스이며 임시 도면을 게임에 표시하지 않는다.
- 전체 프롬프트와 수정 기록: [underworld-composition-art-prompts.json](underworld-composition-art-prompts.json).

생성 원본은 약 1354~1355×1161픽셀이다. 엔진에서 1792×1536(32픽셀/타일)로 한 번 최근접 샘플링하며 Point 필터를 사용한다. 이것이 원본 자체를 네이티브 32픽셀 도트로 만든다는 의미는 아니다. 캐릭터보다 지형 명암이 세밀하고 부드러운 부분이 남는다.

## 검증 기록

첫 실행은 197 통과 / 2 실패였다. B1 물가의 좁은 고립 지점과 뿌리굴 계단에서 실패했다. 시각 오버레이로는 별도로 뿌리굴 상단 단상과 포자 동굴 북서 통로의 그림·충돌 불일치를 발견했다. 최종 그림의 보행 상면을 추적해 수정했다. 정적 검사 55개가 통과했으며, 모든 캠프의 몬스터 216마리 발 위치와 포털 연결을 포함한다.

최종 Windows 플레이어 실행: **211 통과 / 실패 0**, 볼륨 0. [검증 로그](UnderworldComposition/native-scenery-report.txt), [일반 컴파일](UnderworldComposition/runtime-compile.log), [배포 설정 컴파일](UnderworldComposition/release-compile.log). 최종 실행에는 다음을 포함한다.

- 네 맵에서 실제 플레이어가 출구 사이를 걷는 경로, 모든 통행 가능 타일 중심의 연결, 캠프 발 위치.
- 물·낭떠러지·기둥 밑동 충돌, 물 위 투사체 통과와 석벽 투사체 차단, 포털 도착 후 재진입 방지와 외곽 차단.
- 같은 원점·크기의 3개 레이어, 누락·중복·원본 픽셀 불일치 0, 최근접 필터, 기존 배경 렌더러로 되돌아가지 않음.
- 심연 기둥 7개의 발 기준 정렬. 실제 기둥 뒤에서 가림 투명도 0.45, 앞으로 나와 원상 복귀: [뒤](UnderworldComposition/hollow_depths-column-behind.png), [앞](UnderworldComposition/hollow_depths-column-front.png).
- 포자 동굴 중앙 구멍 남쪽 `(24.5,19.5)`는 실제 충돌로 차단. `(24.5,18.13)`의 연결된 바닥에서 [실제 발 위치](UnderworldComposition/hollow_fungal-inner-corner-gameplay.png) 확인.
- 재입장 시 렌더러·환경 효과 수 유지, 이전 스프라이트·텍스처 해제, 지상 복귀 시 지하 연출 제거, 몬스터 전투 값 유지.

동선 검사는 적의 몸에 막히는 현상을 지형 실패와 구별하도록 해당 검사 세션에서만 적 AI와 몸체 충돌을 잠시 멈춘다. 바탕화면의 일반 체험 모드에서는 전투 AI와 충돌이 활성화된다. 동료·온라인 파티 이동, 장시간 메모리 사용과 여러 PC 사양의 프레임 성능은 이 검사로 확인했다고 간주하지 않는다.

캠프 수 18개와 캠프당 몬스터 3마리, 종별 스탯·AI는 유지했다. 초기 배치를 격자 줄에서 분산했지만 주변 캠프가 함께 반응할 수 있으므로 한 번에 3마리만 전투하는 구조는 아니다.

픽셀 누락·중복 검사는 레이어 재합성이 원본과 같음을 검사한다. 원화가 실제 보행면을 올바르게 그렸는지는 자동 검사만으로 판정하지 않고 전체 지도, 일반 카메라, 충돌 오버레이를 직접 비교한다.

## 화면 비교

| 지역 | 이전 화면 | 적용 후 플레이 | 적용 후 전체 맵 |
|---|---|---|---|
| 뿌리 아래 갱도 | [이전](UnderworldDepth/hollow_descent-gameplay.png) | [플레이](UnderworldComposition/hollow_descent-gameplay.png) | [전체](UnderworldComposition/hollow_descent-overview.png) |
| 뒤엉킨 뿌리굴 | [이전](UnderworldDepth/hollow_roots-gameplay.png) | [플레이](UnderworldComposition/hollow_roots-gameplay.png) | [전체](UnderworldComposition/hollow_roots-overview.png) |
| 푸른 포자 동굴 | [이전](UnderworldDepth/hollow_fungal-gameplay.png) | [플레이](UnderworldComposition/hollow_fungal-gameplay.png) | [전체](UnderworldComposition/hollow_fungal-overview.png) |
| 반딧불 심연 | [이전](UnderworldDepth/hollow_depths-gameplay.png) | [플레이](UnderworldComposition/hollow_depths-gameplay.png) | [전체](UnderworldComposition/hollow_depths-overview.png) |

이전/이후는 동일 지역의 서로 다른 촬영 지점이다. 전체 지형을 다시 그렸으므로 동일 좌표를 고정한 픽셀 비교는 아니다. `UnderworldComposition/*-collision-overlay.png`의 녹색은 통행 가능한 지형, 파란색은 물, 붉은색은 차단 영역이다. `Nav/`에는 최종 마스크와 정적 검사 결과를 보관한다. 처음 생성에 쓴 가이드 그림은 원화 제작 이력이며 최종 충돌 도면과 구분한다.

## 로컬 확인

바탕화면 **dotRPG 지상·지하 월드 체험**을 실행한 뒤 뿌리샘 마을 중앙 계단으로 들어간다. 지도에서 지하월드를 선택할 수 있다. 체험 저장은 일반 저장과 분리되어 있고 볼륨은 0이다.

Unity Editor 라이선스가 없어 전체 Player 재빌드는 수행하지 못한다. 전체 런타임 C#을 컴파일하고 기존 Windows 플레이어의 DLL·StreamingAssets를 교체하는 방식으로 확인한다. 온라인 파티와 여러 PC 사양의 성능 검증은 이번 범위에서 수행하지 않는다.
