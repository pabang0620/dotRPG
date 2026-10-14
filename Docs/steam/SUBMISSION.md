# Steam 제출 준비 — 결제 제외 (2026-10-14)

## 출시 범위

인게임 결제 구매 화면·Steam 결제 설정·실결제는 이번 작업에 포함하지 않는다.
게임 본체 판매 가격은 별도 Steamworks 결정이며, 결제를 제외한다는 이유로 무료 게임으로 바꾸지 않는다.
도전과제와 Steam Cloud는 실제 제공·검증 전까지 스토어 지원 기능으로 표시하지 않는다.

## 등록 입력 자료

- 이름: dotRPG
- 실행 파일: dotRPG.exe, Windows 64비트
- 소개문: `../STORE.md`. 온라인 기능 설명은 정식 서버와 멀티 실검증 이후 확정한다.
- 정책: https://pabang0620.github.io/dotrpg-policy/
- 기존 광고 영상: 프로젝트 바깥 `dotRPG_videos/dotRPG_ad_30s.mp4`
- AI 콘텐츠 설문 초안: 이미지 일부는 Google Flow/Nano Banana 계열과 OpenAI 이미지 생성으로 제작하고 개발팀이 편집했다. 배경음악은 Google Flow Music으로 생성 후 편집했다. 게임 실행 중 이용자에게 생성형 AI 콘텐츠를 제공하는 기능은 이 작업에서 추가하지 않았다. 최종 설문은 전체 에셋 출처와 대조한다.
- 새 홍보 원화: `art/header-keyart-source.png`. 실제 플레이 스크린샷으로 등록하지 않는다.

## 이미지 내보내기 및 등록

원화는 최종 업로드 규격 파일과 구분한다. `preflight.py`는 파일명과 PNG 실제 크기를 검사하며,
원화를 만들었다는 이유만으로 이미지 준비를 완료 처리하지 않는다.

| 파일 | 크기 |
|---|---|
| header.png | 920×430 |
| small.png | 462×174 |
| main.png | 1232×706 |
| vertical.png | 748×896 |
| library.png | 600×900 |
| hero.png (문자 없음) | 3840×1240 |

로고는 투명 PNG로 별도 등록한다. 스크린샷은 최신 게임 플레이 화면을 사용한다.
공식 기준: https://partner.steamgames.com/doc/store/assets/standard
라이브러리: https://partner.steamgames.com/doc/store/assets/libraryassets

## 순서

1. 정식 App ID·Windows depot ID·운영 HTTPS 주소 확인.
2. 소개문, 이미지, 지원 언어, 실제 측정한 시스템 사양, 콘텐츠 설문, 가격을 입력.
3. 스토어 심사 제출 및 Coming Soon 공개.
4. 운영 환경에서 결제 OFF, 개발 로그인 OFF, Steam 실제 인증, 백업·경보 설정 확인.
5. `BuildWindowsRelease`, `Tools/steam/preflight.py`, 업로드 dry run, 비공개 테스트 브랜치 업로드.
6. 두 PC 로그인·필드·던전·레이드·방장 이탈·재접속, Cloud 충돌, 운영 복구 훈련 검증.
7. 빌드 심사. 승인·대기 기간 충족 확인 후 출시.

계정 등록·심사·도메인 계약·법률 판단을 로컬 검증 PASS로 대신하지 않는다.
