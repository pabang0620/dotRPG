dotRPG 판타지 사냥터 업데이트 — 2026-10-06, jaein

실제 적용
- 기존 숲 4개: 뿌리에 잠긴 석조 성목, 청록 버섯, 제한된 부유 입자.
- 기존 협곡 4개: 붉은 사암 천문 유적과 호박색 광맥. 획일적인 포장 바닥을 흙/풍화 암반/남은 석판으로 변경.
- 기존 눈꽃 4개: 빙결된 유적과 청색 암벽, 얼음 조각, 느린 눈 입자. 마을의 기존 벽면은 유지.
- 성소 4개 추가: 침수된 회랑 → 북쪽 잠긴 서고 / 남쪽 뿌리 잠식 지하묘 → 망각의 내전.
- 성소 상단 왼쪽 단상의 새 표식에서 회랑 진입. 내전에서는 두 분기 쪽으로 되돌아갈 수 있음. 새 마을은 추가하지 않음.
- 각 성소 사냥터 60×48타일, Lv40 석상 60마리(근접/방패/원거리 각20), 마리당80XP, 재생성25초.
- 기존 12사냥터의 몬스터 수, 보상, 지형 충돌과 연결 구조 유지. 기존 필드 보스 데이터 보존.
- 지도 지역 목록은 휠 스크롤 가능. 성소까지 모든 지역을 선택해 열람 가능.
- 물가 자동 이동은 중심선만 보던 검사를 캐릭터 몸통 폭과 발 기준 오프셋까지 고려하도록 수정.

리소스
Assets/StreamingAssets/HuntingScenery/{forest_relic,canyon_relic,winter_relic}.png
  이번 작업에서 imagegen으로 새로 제작한 투명 배경 랜드마크 3종. 타 게임 이미지는 복사하지 않음.
Assets/StreamingAssets/RegionalMonsters/sanctum_sentinel{,_guard,_thrower}.{png,json}
  이번 작업에서 새로 제작한 5방향×8포즈의 석상병 시트 3종, 합계120포즈. JSON은 원본 PNG의 프레임 경계/피벗.
Assets/StreamingAssets/SunkenSanctum/
  기존에 제작한 아치, 탑, 기둥, 제단, 돌바닥 아트를 새 사냥터에 재사용.
HuntingScenery.cs / HuntingAtmosphere.cs
  실제 게임에서 사용하는 코드 기반 바닥·물가 색 단계, 작은 버섯·결정, 입자. 임시 대체 그림 없음.

주요 코드 위치 (Assets/Scripts/Runtime 기준)
World/SanctumHunting.cs: 네 사냥터의 지형과 몬스터 배치
World/WorldBuilder.SanctumFields.cs: 충돌/포털/스폰/미니맵 연결
Art/HuntingScenery.cs: 랜드마크 및 성소 배경
World/HuntingAtmosphere.cs: 맵 소유의 제한된 환경 입자
Art/CanyonTerrain.cs, Art/WinterTerrain.cs: 사냥터 전용 바닥·절벽 표현
Art/RegionalMonsterArt.cs: 성소 석상 역할별 외형과 투사체
World/MapRegistry.cs, World/WorldRoutes.cs, World/HuntingGrounds.cs: 지역 연결/사냥 설정
UI/WorldMapScreen.cs: 지역 목록 스크롤
World/GridPath.cs: 물가 모서리 몸통 폭 검사
Core/DevCapture.SanctumFields.cs: 무음 로컬 체험/실행 검증
server/data/maps.json 및 data_version: 새 4맵 등록, 기존 맵 레코드 변경 없음

확인
- 런타임 C# 컴파일 성공 (기존 obsolete/member hiding 경고 존재).
- 성소 신규/배경 점검183통과, 전체 경로237통과, 기존 성소 회귀37통과. 합계457통과.
- 새 120포즈 로딩, 역할3종, XP, 물/벽 차단, 캠프 접근, 실제 포털 전환과 도착 후 재전환 없음 확인.
- 실제 키 입력/물리 이동으로 네 사냥터의 통과 확인. 지형만 검사하기 위해 이 시험 동안 몬스터를 정지시키고 몬스터 충돌을 제외했으며, 일반 플레이에는 적용하지 않음.
- 이전 성소의 동료 추종/계단 통과도 회귀 점검 통과.
- 맵 퇴장 후 수면 시스템 제거, 재진입 중복 없음, 제한된 객체 수 확인.
- 모든 실행 검증에서 AudioListener 및 게임 볼륨0.
- 서버 데이터의 기존 맵 레코드가 HEAD와 동일함을 비교. 새 4맵/레벨/개체 수/데이터 해시 검증.
- 두 클라이언트 온라인 플레이와 대규모 장시간 성능 부하는 미검증.
- Unity 전체 플레이어 재빌드는 기존 라이선스 제한으로 수행하지 않음. 기존 로컬 실행본에 새로 컴파일한 런타임 DLL과 StreamingAssets를 반영하여 검증.

로컬 체험
바탕화면: dotRPG 판타지 사냥터.lnk
실행파일: C:/Users/darac/Documents/Codex/2026-09-30/new-chat-2/work/window-shortcuts-player/dotRPG.exe
옵션: -dotrpgSanctumFields <별도 체험 폴더>
일반 세이브와 분리, Lv40, 임시 무적, 볼륨0, 방향키 이동/X공격/M지도. 체험 저장은 일반 슬롯에 덮어쓰지 않음.
Git 커밋/푸시는 이번 변경에 대해 아직 수행하지 않음.

참고 확인한 공식 자료
Sea of Stars 공식 프레스킷: https://sabotagestudio.com/presskits/sea-of-stars/
Eastward 공식 미디어: https://eastwardgame.com/media/
두 작품의 환경/조명 소개를 참고. 이 프로젝트에서는 기존 렌더러를 유지하며 색 단계, 레이어 및 지역 랜드마크로 해석했고 원본 리소스를 사용하지 않음.
