# 카카오·Google 로그인 (0045)

## 흐름
딥링크나 로컬 수신 없이 PC·안드로이드가 같은 방식으로 한다.

1. 게임: `POST /auth/oauth/start {provider: kakao|google, mode: login|link}` (연동은 로그인 토큰 필요)
   - 서버가 `oauth_sessions`에 state(32바이트)와 폴링 비밀(해시만 저장)을 만들고 `{authorize_url, state, poll_secret}`를 준다. 10분 뒤 만료.
2. 게임이 `authorize_url`을 브라우저로 연다. 사용자가 공급자 로그인을 마치면 공급자가 `GET /auth/oauth/callback/<provider>?code&state`로 돌려보낸다.
   - 서버가 인가 코드를 클라이언트 시크릿으로 교환해 사용자 번호만 세션에 적는다(카카오 `/v2/user/me`의 id, Google id_token의 sub를 tokeninfo로 확인).
   - 브라우저에는 "로그인 완료, 게임으로 돌아가세요"만 보인다. 토큰·번호는 싣지 않는다.
3. 게임: 2초마다 `POST /auth/oauth/poll {state, poll_secret, device}` -> `pending` | `error{code}` | `done`.
   - 로그인: `auth_identities(provider, subject)`로 계정을 찾거나 만들고 Steam 로그인과 같은 길(정지·탈퇴 유예·시간대 검사, 세션 교체, 로그인 기록 `oauth_login`)로 토큰을 준다.
   - 연동: 로그인한 계정에 신원을 붙인다. 다른 계정에 붙은 번호는 `OAUTH_ALREADY_LINKED`, 이미 같은 공급자가 있는 계정은 `ACCOUNT_ALREADY_LINKED`.
   - 결과는 한 번만 가져간다(다시 하면 `OAUTH_SESSION_USED`). 폴링 비밀이 틀리면 `OAUTH_SESSION_NOT_FOUND`.
- `GET /auth/oauth/providers` -> `{kakao, google}`: 켜진 공급자만 게임에 버튼이 보인다.
- `/me`의 `providers`: 연결된 로그인 수단 목록(캐릭터 화면의 연동 버튼을 숨기는 데 쓴다).
- 탈퇴 이월 표시(9.3)는 카카오·Google 신원도 남긴다(키 `<provider>:<번호>`).

## 환경변수
| 이름 | 값 |
|---|---|
| `OAUTH_PUBLIC_BASE` | 서버 공개 주소(운영은 https). 예: 시험 서버 `https://35-216-2-121.sslip.io` |
| `KAKAO_CLIENT_ID` / `KAKAO_CLIENT_SECRET` | 카카오 REST API 키 / 그 키의 카카오 로그인 클라이언트 시크릿 |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Google Cloud OAuth 클라이언트(웹 애플리케이션) |
| `RATE_OAUTH_START_IP_MAX` / `RATE_OAUTH_POLL_IP_MAX` | IP당 분당 시작 20 / 폴링 120 |

콜백 주소(공급자 콘솔에 그대로 등록) = `OAUTH_PUBLIC_BASE` + `/auth/oauth/callback/kakao` 또는 `/auth/oauth/callback/google`.

## 콘솔 설정
- 카카오(developers.kakao.com): 앱 만들기 -> 카카오 로그인 사용 ON -> 플랫폼 키의 REST API 키에 카카오 로그인 리다이렉트 URI 등록, 클라이언트 시크릿(카카오 로그인) 활성. 동의항목은 추가하지 않는다(회원번호만 쓴다).
- Google(console.cloud.google.com): API 및 서비스 -> 사용자 인증 정보 -> OAuth 클라이언트 ID(웹 애플리케이션) -> 승인된 리디렉션 URI에 콜백 주소. 범위는 `openid`만. OAuth 동의 화면이 "테스트"면 등록한 테스트 사용자만 로그인된다(게시하면 누구나).
- sslip.io 주소는 시험용이다. 정식 도메인이 생기면 `OAUTH_PUBLIC_BASE`와 두 콘솔의 리다이렉트 URI를 같이 바꾼다.
