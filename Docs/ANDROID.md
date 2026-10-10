# Android 포팅 (플랫폼·빌드)

PC(Steam) 동작과 빌드는 그대로이고, Android 분기는 `Application.isMobilePlatform` / `RuntimePlatform.Android` 런타임 검사 또는 Editor 빌드 함수 안에서만 적용한다. 입력(터치)과 HUD/UI 쪽은 이 문서 범위가 아니다.

## 1. 개발용 APK 만들기

필요한 것 (Unity Hub > Installs > 6000.5.9f1 > Add modules)
- Android Build Support, 그 안의 OpenJDK, Android SDK & NDK Tools (한 묶음으로 설치)
- 폰: 개발자 옵션 + USB 디버깅 켜기

방법
1. Unity 메뉴 `dotRPG > Build > Android dev apk`
   - 배치 모드: `Unity -batchmode -quit -projectPath . -executeMethod DotRPG.EditorTools.BuildScript.BuildAndroidDev`
2. 결과: `Builds/Android/dotRPG-dev.apk` (Development 빌드, http 서버 허용)
3. 설치: `adb install -r Builds/Android/dotRPG-dev.apk`
4. 서버: 폰은 시험 서버 `https://35-216-2-121.sslip.io`(구글 클라우드, DB는 Supabase, `ApiClient.MobileTestServer`)에 자동으로 붙는다. 로그인 화면에 서버 주소 칸은 없다.
   - PC 서버로 폰을 시험하려면 `OnlineScreens.cs`의 `serverRow`를 `Application.isMobilePlatform`으로 되돌려 주소 칸을 다시 연다(예: `http://192.168.0.10:3000`, 같은 네트워크, PC 방화벽에서 포트 허용). 값은 PlayerPrefs `dotrpg.server`에 저장된다.
   - PC 개발 빌드의 기본은 그대로 `http://127.0.0.1:3000`이다.

빌드 직전에 코드(`ProjectSetup.ApplyAndroid`)가 적용하는 값
- 패키지 ID `com.dotrpg.game` (Android만), IL2CPP, ARM64, minSdk 26, targetSdk 36
- 가로 고정(좌우 가로만 자동 회전). 여러 플랫폼이 공유하는 값(화면 방향 목록, insecureHttpOption, AAB 스위치)은 빌드 뒤 원래 값으로 되돌린다. PC 빌드는 영향이 없다.
- 개발 APK만 `insecureHttpOption = AlwaysAllowed`, 릴리스는 `NotAllowed`
- 텍스처: `Assets/Resources` 아래 그림에 Android 개별 설정만 추가. ASTC 6x6(64px 이하는 4x4), 최대 2048. `GetPixels`로 읽는 그림(Read/Write 켜짐)은 압축하면 읽기가 실패하므로 RGBA32 그대로 둔다. 첫 빌드 때 기존 그림을 한 번 다시 임포트하므로 시간이 걸린다(.meta에 Android 항목이 추가됨).
- 런타임: 기본 품질 Low, vSync 0, 60fps 고정. 해상도/화면 모드 설정은 모바일에서 건너뛴다.

## 2. Play 스토어 출시용 (AAB)

1. `ApiClient.ReleaseServer`에 https 주소를 넣는다 (비어 있거나 https가 아니면 빌드를 거절한다).
2. Project Settings > Player > Android > Publishing Settings에서 키스토어 파일, 별칭, 비밀번호를 설정한다. 키스토어가 없으면 `BuildAndroidRelease`가 거절하고 안내를 출력한다. 키스토어는 저장소에 넣지 않고 따로 안전하게 백업한다(잃으면 같은 앱으로 업데이트 불가, Play 앱 서명 사용 권장).
3. 메뉴 `dotRPG > Build > Android release (aab)` -> `Builds/Android/dotRPG.aab` (`DOTRPG_RELEASE` 정의, 개발/캡처 경로 제외, 서버 주소 고정)
4. Play Console 등록: 내부 테스트 트랙에 먼저 올리고 확인한다.

개발용과 출시용 차이: APK vs AAB, Development 플래그, http 허용 여부, 서버 주소 입력 칸(개발 빌드 모바일에서만), 서명 키스토어.

## 3. 기기에서 확인할 핵심 흐름 (미확인: 아직 기기에서 돌려 보지 않았다)

- [ ] 앱이 켜지고 가로 화면으로 고정된다 (좌우 회전 모두)
- [ ] 아트가 보인다: 지역 몬스터(바위·예티·성소), 가라앉은 성소, 사냥터 소품, 겨울 마을 (StreamingAssets를 jar에서 읽는 경로)
- [ ] 로그인 화면에 "서버 주소" 칸이 보이고, 입력 후 로그인된다
- [ ] 캐릭터 선택 -> 마을 진입 -> 이동/전투 (터치 입력은 별도 작업)
- [ ] 홈 버튼으로 나갔다가 다시 들어와도 진행이 유지된다 (백그라운드 진입 시 로컬 세이브 + 대기 중인 서버 업로드)
- [ ] 던전 안에서 백그라운드로 가도 저장하지 않는다 (마을 저장본으로 이어하기)
- [ ] 설정 창: 해상도/화면 모드 항목이 모바일에서 숨겨져야 함 (UI 쪽 후속 작업, 4절 참고)
- [ ] 15분 이상 플레이 시 발열/프레임/메모리 (readable 그림이 RGBA32 그대로라 메모리가 큼, 미측정)
- [ ] 개발 APK 크기와 첫 실행 로딩 시간

## 4. 후속 / 다른 담당 파일

- `SettingsManager.HideDisplayOptions`(모바일이면 true)를 `UI/MenuScreens.cs`의 `SettingsScreen.Create`에서 읽어 "해상도", "화면 모드", "수직 동기화" 항목을 만들지 않게 해야 한다. 설정 매니저는 이미 모바일에서 해당 값을 무시한다.
- Steam: `DotRPG.Steam.asmdef`는 Editor/Linux/macOS/Windows만 포함하므로 Android에서는 컴파일되지 않고 `SteamBridge.Current`가 null이라 릴리스 서버 경로만 쓴다. 다만 Steamworks.NET 패키지의 `Plugins/androidarm64/libsteam_api.so`는 Android 호환이 켜져 있어 APK에 들어간다(로드하는 코드가 없어 동작에는 무해, 용량만 늘어남). 패키지는 읽기 전용(PackageCache)이라 빌드 전처리로 끄는 방식은 컴파일 검증 없이 넣기 위험해 구현하지 않았다. 필요하면 패키지를 `Packages/`로 임베드한 뒤 그 PluginImporter의 Android를 끈다.
- 로컬 세이브/설정은 `Application.persistentDataPath`를 쓰므로 Android에서도 앱 전용 저장소로 동작한다 (미확인).
- 리프레시 토큰은 Android에서 평문 PlayerPrefs로 저장된다(`TokenVault`는 Windows DPAPI만). 출시 전 Android Keystore 기반 저장으로 바꿔야 한다.

## 5. iOS로 확장할 때 필요한 일

- Steam 코드가 iOS에도 컴파일되지 않는지 확인 (asmdef가 PC만 포함하므로 Android와 같다. 미확인)
- Mac + Xcode, Apple Developer 계정, 서명/프로비저닝, Bundle ID
- 안전 영역(노치) 대응: HUD/UI 배치
- ATS: https 필수. 개발용 http는 예외 설정이 필요하고 릴리스에는 넣지 않는다
- 토큰 저장: 키체인으로 이전 (TokenVault 확장)
- 렌더링 Metal, 텍스처 압축 ASTC (Android 설정과 같은 값을 iOS 개별 설정에 추가)
- 결제 정책: 앱 안 결제(재화/패스)는 Apple IAP 사용 의무와 수수료, 외부 결제 유도 제한

## 6. 스토어 출시 전 남은 일

- 터치 입력/HUD (별도 작업), 설정 창 모바일 항목 정리
- 개인정보처리방침 URL, Play Console의 데이터 보안(Data safety) 양식 작성: 계정 ID, 비밀번호/토큰, 기기 식별자, 채팅 내용, 결제 기록 등 수집 항목과 서버 전송/삭제 방법(계정 탈퇴 기능은 있음)을 정확히 신고
- 콘텐츠 등급 설문, 타깃 연령, 광고 포함 여부
- 결제(Play Billing) 연동과 서버 영수증 검증 (현재 클라이언트에 없음, 미확인)
- 저사양 기기 성능/메모리 측정, 용량 기준(AAB 150MB 한도) 확인
- 크래시 수집, 16KB 페이지 정렬 호환 확인(targetSdk 35 이상 권장 사항)
- 앱 아이콘/적응형 아이콘, 스토어 스크린샷과 소개문
