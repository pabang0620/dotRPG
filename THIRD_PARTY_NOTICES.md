# 서드파티 고지

dotRPG가 쓰는 외부 구성 요소와 라이선스. 게임 안 크레딧 화면에도 같은 내용을 넣는다.

## 클라이언트

| 구성 요소 | 라이선스 | 위치 |
|---|---|---|
| Galmuri11 폰트 (Lee Minseo) | SIL Open Font License 1.1 | `Assets/Resources/Fonts/UIFont.ttf`, 전문 `Assets/Resources/Fonts/Galmuri-LICENSE.txt` |
| Steamworks.NET (Riley Labrecque) | MIT | `Packages/manifest.json` (com.rlabrecque.steamworks.net 2025.164.1) |
| Steamworks SDK (Valve) | Steamworks SDK Access Agreement | Steamworks.NET이 함께 배포하는 `steam_api64.dll` |
| Unity 엔진과 Unity 공식 패키지 (2D Tilemap, Input System, uGUI, 내장 모듈) | Unity Terms of Service / Unity Companion License | `Packages/manifest.json` |

## 서버 (직접 의존성)

| 패키지 | 라이선스 |
|---|---|
| argon2 | MIT |
| express | MIT |
| jsonwebtoken | MIT |
| pg | MIT |
| pino | MIT |
| ws | MIT |
| zod | MIT |

간접 의존성은 `cd server && npx license-checker --production --summary`로 확인한다.

## 생성한 에셋

- 이미지: Google Flow(Nano Banana 2)로 생성한 뒤 직접 잘라 내고 다듬었다.
- 배경음악: Google Flow Music으로 생성한 뒤 `Tools/audio/bgm_loop.py`로 루프 지점을 고르고 음량을 맞췄다.
- 효과음: `Tools/sfx/make_sfx.py`로 직접 합성했다(외부 샘플 없음).

Steam 스토어 등록 때 콘텐츠 설문의 AI 생성 콘텐츠 항목에 이미지와 배경음악을 적는다.
