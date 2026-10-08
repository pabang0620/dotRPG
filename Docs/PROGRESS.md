# dotRPG 개발 진행 현황

> 기준 브랜치: `wonho` (사용자 작업 브랜치. 브랜치는 main/jaein/wonho 3개)
> 전체 할 일·결정 기록: [MASTER_CHECKLIST.md](MASTER_CHECKLIST.md) · 기획: [PLAN_DUNGEON_RAID.md](PLAN_DUNGEON_RAID.md), [PLAN_ONLINE.md](PLAN_ONLINE.md) · 음악: [BGM_PROMPTS.md](BGM_PROMPTS.md)

## 완료된 것

| 영역 | 내용 | 커밋 |
|---|---|---|
| 강화 | 참고 게임 공개 확률(+20까지), +11·+12 실패 시 3단계 하락과 확률 보정, +13부터 파괴(방어구·장신구는 +11부터), 장비 보호권, 장비 개체별 강화, 세이브 v4, 리뷰 지적 15건 수정 | 218a759, 5392680 |
| 파티 | AI 용병 4명(브론·카이·엘린·세라), 최대 4인, 캐릭터별 스탯, 어그로, 딜 미터, 파티 창·프레임 | 453a9aa |
| 던전 | 방 단위 진행, 문, 코인식 부활, CLEAR 연출, 랭크 SSS~F, 보상 카드 4장, 던전 선택 창, 안내원 NPC, 06:00/목요일 초기화, 던전 안 저장 금지 | 0d44c05 |
| 몬스터·보스 | 몬스터 7종, 요일 보스 5종, 해골왕(3페이즈·토템·망자의 심판), 예고 장판, 슈퍼아머, 무력화, 보스 HP 바 | d1446e2 |
| 요일던전·레이드 연결 | 요일별 몬스터·보스 배치, 방 데이터 검증, 레이드 수치 조정 | 2f26632 |
| 배경음악 | Flow Music 13곡 생성·마스터링(-14 LUFS), 마을·사냥터·던전 테마·보스·레이드·광폭화·클리어·실패별 재생, 1초 크로스페이드 | 2da8ef6, ca691b4 |
| 다른 작업자 병합 | 32px 고해상도 맵·캐릭터·UI, 기모노 카타나 전사, 3연타 불꽃 베기 (양쪽 기능 모두 유지) | bd167aa, a4ed03f |
| 아트 통합 | 몬스터·보스·토템 고해상도화(1-A), 던전 방 32px 지형 + 광산·동굴 톤(3-A) | 24d00f5, f310549 |
| UI 개편(진행 중) | 전 화면 감사(UI_AUDIT.md), UiTheme 디자인 토큰, 버튼 피드백, HUD 중앙 배너, 최소 16px 글자, 어두운 불투명 메뉴 패널 | e2e2f51 ~ ce72947 |
| 온라인 클라이언트 구조(진행 중) | ActorId, NetCommand, NetworkInput, LoopbackTransport, IAuthority(강화 굴림·몬스터 드롭), OnlineSession, 파티 찾기·경매장 미리보기 창, PLAN_ONLINE/PLAN_AUCTION v1.0, `-dotrpgOnline` 캡처 | eb44a1c, 4621063 |
| 문서 | 마스터 체크리스트, 온라인·서버·경매장 기획(v0.1), 사용자 의도 기록 | adccf60 외 |
| 스킬 체계 v2 | 슬롯 역할 고정(단일·범위·제어·방어·각성), 범위 보너스 완화, 제어 감쇠, v1은 SkillGemsLegacy·태그 skills-v1로 보관 | ac135b9 |
| 게임 서버 1·2단계 | Node·TypeScript·PostgreSQL, 개발용 로그인, 온라인 캐릭터·상태 저장, 타이틀 온라인 모드 | c40f2f5 |
| 게임 서버 3단계 | 경제 판정 전부 서버로(처치·드롭·채집·퀘스트·상점·강화·창고·장착·솔로 던전), 원장 | b391f4e |
| 게임 서버 4단계 | 파티 모집·매칭·로비, 협동 던전(방장 계산 + 스냅샷), 방장 인계, 결과 대조, 레이드 | b172263 |
| 게임 서버 5·6단계 | WebSocket 채팅·친구·차단·신고·초대, 경매장·우편·시세 | 58beda7 |
| 게임 서버 7단계 | 관리자 CLI, 정리 작업, 백업·점검·배포 파일, 출시 전 수정 13건 | 9b38dc7 |

## 사용자 결정 기록

| # | 질문 | 결정 |
|---|---|---|
| D1 | 파티원 | 사람이 없으면 AI 용병, 사람을 구인하면 온라인 멀티 |
| 1 | 던전 몬스터 그림 | 고해상도로 다시 그림 (완료) |
| 2 | 용병 전사 외형 | 일반 고해상도 외형 유지 |
| 3 | 던전 방 바닥 | 새 32px 지형 적용 (완료) |
| 4 | 동료 전사 3연타 | 유지 |
| 5 | 보호권 아이콘 | 16px 그대로 |
| 6 | BGM 연결 병합 | 추가 테스트 없이 병합 |

## 남은 문제·확인 필요

- 레이드 자동 플레이 테스트: 해골왕 2페이즈(토템 기믹)에서 전멸 - 자동 플레이가 토템을 노리지 않음
- 던전 테스트 1건: 동료가 먼저 잡아 몬스터 확인이 실패하는 경우가 있음(테스트 쪽 문제)
- 몬스터 장비는 16px 그림을 2배 확대한 것 - 32px로 새로 그릴지 미정
- 겨울 동굴 바닥이 밝은 눈밭이라 동굴 느낌이 약함 - 더 어둡게 할지 미정
- UI·온라인 병합(2026-10-01) 후에도 컴파일만 확인(error CS 0). 실제 게임 자동 테스트와 화면 확인은 안 함
- 마지막 두 병합(BGM, 아트 통합) 이후 실제 게임 자동 테스트는 돌리지 않음(사용자 지시). 컴파일만 확인

## 다음 작업 (2026-10-04)

1. 창 2개 협동 실플레이 확인 (서버 `npm run dev` + 계정 2개, README 4장 "온라인으로 해 보기")
2. Steam 연동 (시험 앱 ID 480: SteamAuth·SteamP2PTransport, 서버 Web API 키)
3. 실제 배포 (업체·도메인·오프사이트 백업·웹훅 결정 후 `server/ops/README.md`)
4. 결정 대기 2건: 파티 저레벨 처치 경험치 감쇠, 강화 직후 판매 차익
5. 출시 전 운영 항목: 금칙어 목록 보강, 개인정보 처리방침
6. 출시 전 개선 계획: [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md)

## 현재 상태 스냅샷 (2026-10-08 기준)

| 항목 | 내용 |
|---|---|
| 작업 브랜치 | `wonho` (사용자 작업 브랜치). 브랜치는 `main`·`jaein`·`wonho` 3개이고 모두 원격에 있다 |
| 워크트리 | 쓰지 않는다(`wt/*` 체계 폐기). 작업 폴더 하나에서 브랜치로 나뉜다 |
| 엔진 | Unity 6000.5.9f1 (`ProjectSettings/ProjectVersion.txt`) |
| 게임 서버 | `server/`(Node·TypeScript·PostgreSQL). 단계별 진행 현황은 `PLAN_SERVER.md` 표와 `Docs/server/` |
| 검증 | 컴파일과 서버 자동 테스트는 작업 묶음 끝에 확인한다. 게임 실행 자동 테스트는 요청이 있을 때만 돌린다 |

### 체크리스트 진행률 요약

- 완료: A~C(강화·파티·던전), D 몬스터·보스·요일던전, E1, F3, G1, H1, H2
- 진행 중: D 레이드 기믹, E2·E3, F1·F2·F4, G2
- 미착수: D 밸런스 시뮬레이션, E4~E6, F5, G3, H3, I 전체

### UI 남은 항목 (Docs/UI_AUDIT.md "남은 항목")

- P1: 주/보조 버튼 규칙을 상점·결과·던전 선택에 적용, 창 하단 키 안내 위치 통일, 결과 화면 버튼 위계, 스킬 창 정보 밀도, 타이틀 로고·키아트
- P2: 공용 툴팁, 창 버튼 패드 내비게이션, 던전 선택 잠김 난이도 자물쇠, 방 지도·시계 스타일 통일, 부활 창 버튼 위계, 패드 버튼 아이콘

### 온라인·경제 기획 문서

- `Docs/PLAN_ONLINE.md` v1.0: 인증, 캐릭터, 경제, 콘텐츠, 파티·매칭, 결과 검증, 채팅, 운영
- `Docs/PLAN_AUCTION.md` v1.0: 경매장, 우편함, 귀속 규칙, 파티 찾기, 채팅
- 서버는 1~7단계 구현 완료(PLAN_SERVER 진행 현황). 남은 것: Steam 연동, 실제 배포, 창 2개 협동 실플레이

## 개발·검증 방법

- 작업 방식: 에이전트마다 별도 git worktree(`C:\Users\admin\Desktop\games\dotRPG-wt\<이름>`) → 각자 컴파일·검증 → 커밋 → `feature/dungeon-raid`에 병합·푸시
- 컴파일: `Unity.exe -batchmode -nographics -quit -projectPath <경로> -logFile <로그>`
- 빌드: 위에 `-executeMethod DotRPG.EditorTools.BuildScript.BuildWindows`
- 자동 테스트: `Builds\Windows\dotRPG.exe <모드> <결과폴더>` → `report.txt`의 `summary` 줄
  - `-dotrpgCapture`(전체·강화·BGM), `-dotrpgParty`, `-dotrpgDungeon`, `-dotrpgMonster`, `-dotrpgBalance`
  - 다른 작업자 쪽: `-dotrpgCanyon`, `-dotrpgWinter`, `-dotrpgChars`, `-dotrpgUi`, `-dotrpgDepth`, `-dotrpgStairs`, `-dotrpgSilver`
- 미리보기(에디터): `DotRPG.EditorTools.MonsterSheet.Render`(몬스터 시트), `DotRPG.EditorTools.DungeonRoomPreview.RenderAll`(던전 방)
- BGM: Flow Music 후보 생성 후 `Tools/audio/bgm_pick.py --apply`로 검증·루프·선정 (절차는 `Docs/BGM_PROMPTS.md`)
