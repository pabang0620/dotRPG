# dotRPG Steam 출시 도구

결제는 이번 출시 준비 범위에서 제외한다. 서버는 `PAYMENTS_ENABLED=false`,
`PAYMENTS_STEAM_MODE=off`를 유지한다. 결제 키나 상품 등록은 필요하지 않다.

1. `ApiClient.ReleaseServer`에 확정된 운영 HTTPS 주소를 설정한다. 시험 서버 주소로 대체하지 않는다.
2. Unity에서 `DotRPG.EditorTools.BuildScript.BuildWindowsRelease` 실행.
3. `upload.ps1 -AppId <정식 앱 ID> -DepotId <Windows 디포 ID> -SteamUser <계정> -DryRun`으로 VDF 생성.
4. 내용을 확인한 뒤 같은 명령에서 `-DryRun`을 빼면 업로드한다. 기본 브랜치로 자동 공개하지 않는다.

업로드 대상은 `Builds/WindowsRelease`만이다. App ID 0·480, 시험용 `steam_appid.txt`,
실행 파일 누락은 거부한다. 암호는 저장하지 않으며 Steam Guard는 SteamCMD에서 처리한다.
`release-manifest.json`과 실행 파일 SHA-256도 검사한다. 검사 전용·시험 빌드는 업로드하지 못한다.
실행 설정은 `dotRPG.exe`, Windows 64비트, 기본 실행 인자 없음으로 등록한다.

`prepare-notices.py`는 설치된 패키지의 원본 라이선스로 게임 내 오프라인 크레딧을 갱신한다.

## Auto-Cloud 설정 초안

- Root: `WinAppDataLocalLow`
- Subdirectory: `dotRPG Team/dotRPG/Saves`
- Pattern: `slot_*.json` (재귀 해제)
- 백업도 동기화하려면 별도 규칙 `slot_*.json.bak`을 실제 저장 파일명과 대조 후 추가한다.
- 토큰·로그·온라인 계정 데이터는 포함하지 않는다. 온라인 캐릭터의 정본은 서버다.
- 다른 PC에서 다운로드·충돌·오프라인 플레이 후 재접속을 확인한 뒤 Cloud 지원을 표시한다.

## 외부에서 완료할 항목

Steamworks 앱·디포 ID, 납부/세금/정산 정보, 가격·출시일, 스토어 심사, AI 콘텐츠 설문,
Auto-Cloud 등록과 실기 검증은 로컬 파일 생성만으로 완료되지 않는다.
