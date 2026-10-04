# 친구와 온라인 멀티 시험하기

목표: 서로 다른 PC 두 대(나, 친구)가 인터넷으로 같은 서버에 접속해 **필드에서 함께 사냥**하고 **던전·레이드**를 같이 간다.
전투 연결은 우리 서버의 중계(`/relay`)가 기본이고, 둘 다 Steam이 켜져 있으면 Steam P2P도 쓸 수 있다(시험 앱 ID 480).

## 1. 서버를 친구도 들어올 수 있는 곳에 띄운다

게임 서버는 HTTPS 주소 하나만 있으면 된다(REST, 채팅 `/ws`, 전투 중계 `/relay`가 모두 같은 포트·같은 주소).

| 방법 | 언제 | 비고 |
|---|---|---|
| 클라우드 서버 한 대 (출시 경로) | 계속 쓸 서버 | `server/ops/README.md` 순서대로 Docker + Caddy(자동 TLS). 도메인 필요 |
| 지금 쓰는 PC나 집 서버 + 터널 | 빨리 한 번 시험 | Tailscale Funnel, Cloudflare Tunnel 등으로 `https://...` 주소를 받는다. WebSocket이 통과해야 한다 |

서버 `.env`에서 시험용으로 바꿀 값:

```
NODE_ENV=production
DEPLOY_STAGE=test                 # 시험 서버: 앱 480과 개발용 로그인을 허용
ALLOW_DEV_AUTH_IN_PRODUCTION=true # 아이디·비밀번호 로그인(개발용 계정)
AUTH_DEV_REGISTER_ENABLED=false   # 계정은 관리자 CLI로 발급: dotrpg-admin account dev-create
TRUST_PROXY=1                     # Caddy·터널 뒤에서 실제 IP를 보게
RELAY_PUBLIC_URL=wss://<주소>/relay
RELAY_TICKET_SECRET=<JWT_SECRET과 다른 32자 이상 난수>
COMBAT_TRANSPORT_ORDER=relay,steam   # 처음엔 중계로 확인. Steam을 먼저 시험하려면 steam,relay
```

Steam 로그인·Steam P2P까지 시험할 때만 추가:

```
STEAM_AUTH_MODE=web_api
STEAM_APP_ID=480
STEAM_WEB_API_KEY=<https://steamcommunity.com/dev/apikey 에서 발급>
```

## 2. 게임 빌드

- 중계만 시험: Unity 메뉴 `dotRPG ▸ Build ▸ Windows (x64)` -> `Builds/Windows/`
- Steam P2P까지 시험: `dotRPG ▸ Build ▸ Windows Steam test (app 480)` -> `Builds/WindowsSteamTest/` (실행 파일 옆에 `steam_appid.txt`(480)가 생긴다. 출시 빌드에는 넣지 않는다)
- 빌드 폴더를 통째로 친구에게 준다(압축).

## 3. 실행

둘 다 실행 옵션으로 서버 주소를 준다(바로가기 대상 끝에 붙이거나 명령줄):

```
dotRPG.exe -dotrpgServer https://<주소>
```

1. 타이틀 ▸ **온라인** ▸ 받은 개발용 아이디·비밀번호로 로그인 (Steam 시험 빌드에서 Steam이 켜져 있으면 **Steam으로 접속**도 보인다)
2. 캐릭터를 만들고 접속
3. 한 사람이 메뉴 ▸ **파티 찾기** ▸ 모집 글 등록, 다른 사람이 **참가 신청**, 방장이 파티 창에서 **수락**
4. **필드 사냥**: 둘 다 마을에서 해골 숲으로 이동하면 "파티원과 같은 사냥터에 있다" 안내가 뜨고 몬스터를 함께 본다. 각자 잡은(때렸거나 14칸 안에 있던) 몬스터의 경험치·드롭을 각자 받는다
5. **던전**: 방장이 파티 창에서 **준비**를 받고 **출발** -> 둘 다 자동으로 같은 던전에 들어간다
6. **레이드**: 레이드 보상은 사람 2명 이상일 때만 나온다(혼자 + AI는 연습 입장)

시험용으로 캐릭터 레벨을 올릴 때(시험 서버 전용, 운영에서는 거절): `cd server && node scripts/test-level.mjs <캐릭터 이름> <레벨>`. 경험치 원장에 `test_boost`로 남는다. 접속 중이면 다시 접속해야 보인다.

## 4. 확인할 것 (문제가 생기면 이 정보를 알려 주기)

- 필드: 서로의 캐릭터가 움직이는지, 몬스터 위치·HP가 같은지, 처치 경험치가 둘 다 들어오는지
- 던전: 방 이동·클리어·결과(랭크·카드)가 둘 다 맞는지
- 방장 PC를 끄면 3~10초 안에 남은 사람이 방장을 이어받는지
- 끊김이 보이면: 화면 안내 문구, 대략적인 시각, 서버 로그(`docker compose logs api` 또는 `npm run dev` 창)

## 5. 알려진 제한

- 필드 공유는 **같은 파티**끼리만이다(모르는 사람과 몬스터를 공유하려면 전용 필드 서버가 필요, phase8_api 6.1)
- 다른 파티원 화면에서 보스 공격 예고 장판·몬스터 투사체 연출은 아직 그려지지 않는다(피해 판정은 방장 PC에서 정상)
- 파티원은 쓰러지면 다음 방(필드는 마을 귀환)에서 일어난다(부활 코인은 방장만)
