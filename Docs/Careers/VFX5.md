# 전직 이펙트 원화 해상도 개선 — 2026-10-05

현재 적용 버전은 VFX5다. VFX4의 형태와 전개를 유지하면서 원화를 96×96에서 **192×192**로 재제작했다. 프레임당 픽셀 공간은 4배이며, 12프레임 시트는 2304×192다.

기존 PNG를 확대하지 않았다. `CareerRaster`가 최종 해상도에서 선·곡선·다면체를 직접 래스터화하고, 더 촘촘한 곡선 샘플과 반 픽셀 단위의 디자인 좌표를 사용한다. 안티앨리어싱·블러 없이 최종 정수 픽셀에 그린다. 28개 주효과 모두 단순 2배 확대 이미지에는 없는 2×2 블록 내부의 세부 픽셀을 포함하는지 검사했다.

## 추가한 디테일

- 파이터: 검날 곡선과 절삭면, 검기 내부 색 띠, 가는 강철색 잔상.
- 수호자: 방패의 한 픽셀 테두리 음영, 음각 무늬와 리벳, 중앙 문장.
- 메이지: 원소 결정의 가는 면 경계, 불꽃 내부의 끊긴 밝은 선, 비전 룬과 균열 내부 문양.
- 비숍: 깃털 양쪽의 깃줄, 가는 색 선, 날개와 성역 아치의 윤곽.

## 연결과 크기 유지

- `Assets/Scripts/Runtime/Art/CareerRaster.cs`: 전직 이펙트 전용 192px 래스터 대상. 공통 PixelCanvas와 기본 직업 원화는 변경하지 않는다.
- `Assets/Scripts/Runtime/Art/CareerVfxArt.cs`, `CareerVfxArt.Reforged.cs`: 새 밀도로 재제작한 원화, 곡선과 세부 묘사.
- `Assets/Scripts/Runtime/Art/CareerArt.cs`: 192px 프레임을 **96 PPU**로 읽는다. 이전 96px/48 PPU와 월드 크기가 같다.
- `Assets/Scripts/Editor/CareerAssetExport.cs`: 192px 시트와 새 PPU를 내보내며, 최대 텍스처 크기를 4096으로 설정한다. 2304px 시트가 Unity 임포트에서 2048px로 축소되는 것을 방지한다.
- `Assets/Resources/Art/Careers/Reforged/`: PNG 136개를 새 해상도로 교체. Point 보간, 무압축, 투명 배경. 이 중 56개는 주효과의 후면/전면 파생 시트다.
- 프레임 수, 타격 시점, 범위 경계, 화면상의 효과 크기, 앞뒤 렌더링 정책은 유지했다.

## 검증 자료

- [해상도와 세부 형태 비교](VFX5/density_comparison.png): 같은 화면 크기로 왼쪽 96px, 오른쪽 192px를 비교. 아래 줄은 세부 확대다.
- [게임 내 일반 스킬 비교](VFX5/normal_comparison.gif), [각성기 비교](VFX5/awakening_comparison.gif).
- [밀도 및 반경 검사](VFX5/density-checks.json): 28개 주효과 모두 네이티브 세부 픽셀과 87.5px 이내 반경 확인. 반경 값이 두 배인 것은 픽셀 밀도 증가이며 월드 범위는 동일하다.
- [불변 파일 검사](VFX5/invariants.json): 전투 코드, 기본 스킬, 직업 수치·조건, 서버 스킬 데이터 동일.
- [Runtime 컴파일](VFX5/compile.txt), [Editor 컴파일](VFX5/editor-compile.txt), [게임 실행 결과](VFX5/runtime-results.txt).

실행 검증은 볼륨 0이며 기존 플레이어에 새 Runtime DLL을 적용한다. 바탕화면 `dotRPG 전직 체험` 바로가기 대상도 같은 DLL을 사용한다. 정상 플레이의 저장된 볼륨과 저장 캐릭터는 바꾸지 않았다.

정식 Unity Resources 재번들링 및 배포 빌드는 이번에 수행하지 않았다. 로컬 실행은 PNG와 동일한 C# 원화 제작 소스로 생성한 메모리 스프라이트를 사용했다. 실제 두 PC 온라인 및 저사양 FPS는 미측정이다. 해상도 증가로 같은 수의 텍스처가 사용하는 메모리는 늘어난다. 기존 캐시·파편 수·효과 객체 수 제한과 종료 정리를 유지한다.

작업 브랜치: `jaein`. 이번 변경은 아직 커밋·푸시하지 않았다.
