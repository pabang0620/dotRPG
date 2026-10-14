# dotRPG Steam 출시 준비 — 2026-10-14

판정: **로컬 빌드와 온라인 기능 보강 완료, 출시 승인 불가.** 결제·Steam 업로드·공개·운영 서버 변경은 수행하지 않았다.

## 이번 작업

- Windows QA 빌드와 `DOTRPG_RELEASE` 조건 컴파일 확인용 빌드 생성. `WindowsReleaseCheck`는 운영 서버가 없는 검사 전용이며 업로드 대상이 아니다.
- 운영 HTTPS 주소 검증, 릴리스 빌드 증명 파일과 실행 파일 SHA-256 검증, 테스트 App ID 파일 및 Spacewar 업로드 차단 추가.
- SteamPipe 템플릿, 업로드 도구, 읽기 전용 preflight 추가. 업로드 dry run 및 거부 조건 6개 통과(`upload-tests.json`).
- 크레딧/라이선스 화면 및 실제 패키지 라이선스 텍스트 추가. 실행 화면에서 표시 확인.
- 파티원에게 보이지 않던 적 공격 예고와 투사체의 시각 정보 동기화 추가. 원격 시각 객체는 피해 판정에 참여하지 않으며 프로토콜을 8로 변경했다. 같은 버전끼리 접속해야 한다.
- 자동 화면 캡처의 검은 화면 문제와 리소스 준비 전 촬영 문제 수정.
- 스토어 제출 초안(`SUBMISSION.md`), 홍보 이미지 원본(`art/header-keyart-source.png`), 실제 실행 화면 후보 5장(`screenshots/`) 준비.

## 검증 결과

|검사|결과|근거|
|---|---|---|
|Windows QA 빌드|성공|`Logs/steam-QA-final.log`, `Builds/WindowsQA/dotRPG.exe`|
|릴리스 조건 컴파일|성공, 배포 불가|`Logs/steam-ReleaseCheck-final.log`|
|서버 TypeScript|통과|`tsc --noEmit -p server/tsconfig.json`|
|서버 Jest|108 suites, 1,232 passed, 1 todo|`evidence/server-tests.log`|
|온라인 로컬 회귀|45 passed, 0 failed|`evidence/online.txt`|
|UI 캡처|완료, 크레딧 육안 확인|`evidence/Ui.txt`|
|파티 회귀|23 passed, 4 failed|`evidence/Party.txt`|
|스토리 회귀|25 passed, 11 failed|`evidence/Story.txt`|
|던전 회귀|미완료, 레이드 진입 실패 후 테스트 코드 NullReferenceException|`evidence/Dungeon.txt`|

서버 테스트는 임시 PostgreSQL에서 실행했다. 모든 테스트 결과 출력 후 Jest에 열린 핸들이 남아 자동 종료되지 않아 해당 Jest 프로세스를 종료했다. 따라서 테스트 단언 통과와 실행기 정상 종료는 구분한다.

온라인 검사는 로컬 직렬화/루프백 검사다. Steam 실계정 두 PC 접속과 지연·패킷 손실·방장 이탈 검증을 대체하지 않는다.

## 남은 로컬 작업 — 출시 차단

1. 파티·던전·스토리 회귀 시나리오를 현재 콘텐츠에 맞춰 다시 검증해야 한다. 확인된 구식 가정은 용병 필드 상시 소환, 용병 공격 배율 0.6~0.7(현행 0.41), 액티브 전쟁 함성(현행 패시브), 1-9 레이드 해금(현행 1-20)이다. 이것만으로 모든 실패를 테스트 오류라고 단정하지 않는다.
2. 던전 검사의 레이드 진입 실패 후 null 접근을 수정하고 끝까지 실행해야 한다. 기존 던전 입장·클리어·보상·부활·일일 제한·저장 일부는 통과했지만 전체 회귀 통과 상태가 아니다.
3. 서버 테스트 종료 핸들 누수를 찾아 정리해야 한다.
4. 캡슐/라이브러리 이미지의 규격별 최종 출력과 검수를 해야 한다. 생성 이미지 원본만 존재하며 `preflight.json`의 이미지 항목은 BLOCKED다.
5. 화면 후보에는 테스트 진행도, 레벨업 알림 등이 포함된다. 정식 빌드에서 자연스러운 플레이 화면으로 최종 재촬영해야 한다. 홍보 원화를 게임 플레이 스크린샷으로 제출하지 않는다.

## 외부 정보가 필요한 작업

- 정식 Steam App ID와 Windows depot ID, 업로드 권한이 있는 계정.
- 확정된 운영 HTTPS 주소. `ApiClient.ReleaseServer`는 빈 값이며 테스트 주소를 운영 주소로 승격하지 않았다.
- 출시 국가·가격·지원 언어·지원 연락처·최소 사양 확정, 권리/AI 콘텐츠 설문 확인.
- Steamworks 스토어 설정·심사, 실제 계정 인증/Cloud/멀티플레이, 운영 백업/복구 확인.

결제는 현재 범위에서 제외한다. 비용이 드는 등록·계약·유료 서비스를 임의로 진행하지 않는다. 다음 빌드 절차는 `../../Tools/steam/README.md`와 `SUBMISSION.md`를 따른다.
