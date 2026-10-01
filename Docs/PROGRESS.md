# dotRPG 개발 진행 현황

> 기준 브랜치: `feature/dungeon-raid` (main에는 아직 합치지 않음)
> 전체 할 일·결정 기록: [MASTER_CHECKLIST.md](MASTER_CHECKLIST.md) · 기획: [PLAN_DUNGEON_RAID.md](PLAN_DUNGEON_RAID.md), [PLAN_ONLINE.md](PLAN_ONLINE.md) · 음악: [BGM_PROMPTS.md](BGM_PROMPTS.md)

## 완료된 것

| 영역 | 내용 | 커밋 |
|---|---|---|
| 강화 | 던파 KR 공개 확률(+20까지), +11·+12 실패 시 3단계 하락과 확률 보정, +13부터 파괴(방어구·장신구는 +11부터), 장비 보호권, 장비 개체별 강화, 세이브 v4, 리뷰 지적 15건 수정 | 218a759, 5392680 |
| 파티 | AI 용병 4명(브론·카이·엘린·세라), 최대 4인, 캐릭터별 스탯, 어그로, 딜 미터, 파티 창·프레임 | 453a9aa |
| 던전 | 방 단위 진행, 문, 코인식 부활, CLEAR 연출, 랭크 SSS~F, 보상 카드 4장, 던전 선택 창, 안내원 NPC, 06:00/목요일 초기화, 던전 안 저장 금지 | 0d44c05 |
| 몬스터·보스 | 몬스터 7종, 요일 보스 5종, 해골왕(3페이즈·토템·망자의 심판), 예고 장판, 슈퍼아머, 무력화, 보스 HP 바 | d1446e2 |
| 요일던전·레이드 연결 | 요일별 몬스터·보스 배치, 방 데이터 검증, 레이드 수치 조정 | 2f26632 |
| 배경음악 | Flow Music 13곡 생성·마스터링(-14 LUFS), 마을·사냥터·던전 테마·보스·레이드·광폭화·클리어·실패별 재생, 1초 크로스페이드 | 2da8ef6, ca691b4 |
| 다른 작업자 병합 | 32px 고해상도 맵·캐릭터·UI, 기모노 카타나 전사, 3연타 불꽃 베기 (양쪽 기능 모두 유지) | bd167aa, a4ed03f |
| 아트 통합 | 몬스터·보스·토템 고해상도화(1-A), 던전 방 32px 지형 + 광산·동굴 톤(3-A) | 24d00f5, f310549 |
| 문서 | 마스터 체크리스트, 온라인·서버·경매장 기획(v0.1), 사용자 의도 기록 | adccf60 외 |

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

- 레이드 자동 플레이 테스트: 해골왕 2페이즈(토템 기믹)에서 전멸 — 자동 플레이가 토템을 노리지 않음
- 던전 테스트 1건: 동료가 먼저 잡아 몬스터 확인이 실패하는 경우가 있음(테스트 쪽 문제)
- 몬스터 장비는 16px 그림을 2배 확대한 것 — 32px로 새로 그릴지 미정
- 겨울 동굴 바닥이 밝은 눈밭이라 동굴 느낌이 약함 — 더 어둡게 할지 미정
- 마지막 두 병합(BGM, 아트 통합) 이후 실제 게임 자동 테스트는 돌리지 않음(사용자 지시). 컴파일만 확인

## 다음 작업 (체크리스트 순서)

1. E. UI/UX 상용화 개편 (감사 → 디자인 규칙 → 화면별 개편)
2. F. 온라인 클라이언트 구조 (ActorId, 원격 입력, 판정 권한 분리, 한 PC 창 2개 테스트)
3. G. 경매장 기획·UI (서버 연결 전에는 목업 데이터)
4. D. 레이드 기믹 마무리 (토템을 노리는 자동 플레이·동료 AI)
5. I. 상용화 마무리 (설정, 세이브 슬롯, 성능, 스토어 문구)

## 개발·검증 방법

- 작업 방식: 에이전트마다 별도 git worktree(`C:\Users\admin\Desktop\games\dotRPG-wt\<이름>`) → 각자 컴파일·검증 → 커밋 → `feature/dungeon-raid`에 병합·푸시
- 컴파일: `Unity.exe -batchmode -nographics -quit -projectPath <경로> -logFile <로그>`
- 빌드: 위에 `-executeMethod DotRPG.EditorTools.BuildScript.BuildWindows`
- 자동 테스트: `Builds\Windows\dotRPG.exe <모드> <결과폴더>` → `report.txt`의 `summary` 줄
  - `-dotrpgCapture`(전체·강화·BGM), `-dotrpgParty`, `-dotrpgDungeon`, `-dotrpgMonster`, `-dotrpgBalance`
  - 다른 작업자 쪽: `-dotrpgCanyon`, `-dotrpgWinter`, `-dotrpgChars`, `-dotrpgUi`, `-dotrpgDepth`, `-dotrpgStairs`, `-dotrpgSilver`
- 미리보기(에디터): `DotRPG.EditorTools.MonsterSheet.Render`(몬스터 시트), `DotRPG.EditorTools.DungeonRoomPreview.RenderAll`(던전 방)
- BGM 마스터링: WSL에서 `bash Tools/bgm_master.sh` (원본 WAV는 `AudioSource/Raw`, git 제외)
