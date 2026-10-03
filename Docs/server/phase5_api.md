# 서버 5단계 API 명세 (채팅·친구)

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §6(WebSocket), §7(확장), §9 5단계("지금 목업 채팅 창이 실제 서버로 동작한다"), [PLAN_ONLINE.md](../PLAN_ONLINE.md) §2.3(채팅), §5.1(채팅 책임). 앞 단계: [phase1_2_api.md](phase1_2_api.md), [phase3_api.md](phase3_api.md), [phase4_api.md](phase4_api.md). 스키마: `server/schema.sql`, `server/migrations/0006_chat_social.sql`. 게임 값·C# 대응: [phase5_mapping.md](phase5_mapping.md).
이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만든다. 게임 값(채팅 길이·간격·반복 한도·신고 줄 수·신고 사유)은 문서에 복사하지 않고 `server/data/chat.json` 필드 이름으로만 참조한다(mapping 3절). 본문의 수치는 서버 정책 상수(11절, 환경변수)이거나 근거 예시다.

## 0. 핵심 설계 (먼저 읽기)

1. **WebSocket 하나(`/ws`), 같은 Node 프로세스.** HTTP 서버에 `ws`를 붙인다(별도 포트·프로세스 없음). 로그인·계정·캐릭터 규칙은 REST와 같은 JWT를 쓴다. 동시 100명 미만에서 한 프로세스가 충분하고, 연결 목록·제한 상태·알림 전달은 전부 인터페이스 뒤에 두어 확장 1단계(Redis, 서버 여러 대)에서 구현만 바꾼다(12절).
2. **서버가 저장한 뒤에 전달한다.** 채팅은 `chat_messages`에 INSERT가 성공해야 받는 사람에게 간다. 저장 순서(`id`)가 곧 전달 순서이고 클라이언트는 이 `seq`를 커서로 들고 있다가 재접속 때 "그 뒤의 것"만 받는다(놓친 메시지 따라잡기, 3.6). 저장 실패면 아무에게도 가지 않고 보낸 사람이 오류를 받는다.
3. **판정은 전부 서버.** 길이·간격·반복·금칙어·차단·파티 소속·채팅 금지 제재는 서버가 최종 판정한다. 클라이언트 `ChatRules` 검사는 미리보기다(PLAN_ONLINE §2.3). 클라이언트가 보내는 것은 "이 채널에 이 문장을 보낸다(`cid`)"뿐이고 시각·보낸 사람 이름·순번은 받지 않는다.
4. **신고 증거는 서버가 찍는다.** 지금 C#은 신고 때 자기 화면의 마지막 20줄을 붙이지만, 서버는 그 내용을 믿지 않는다. 신고 요청에는 대상과 사유만 있고, 증거는 서버가 접수 순간 `chat_messages`에서 복사해 `report_lines`에 남긴다(7절). 원본 채팅은 7일 뒤 지워져도 증거는 남는다.
5. **차단은 받는 쪽이 정한다.** 내가 차단한 계정의 일반·파티·귓속말·친구 요청·파티 초대는 서버가 나에게 아예 보내지 않는다(클라이언트 필터는 중복 방어). 차단은 계정 단위라 상대가 다른 캐릭터로 와도 막힌다. 차단당한 쪽에는 차단 사실이 드러나지 않는다.
6. **친구는 상호 수락.** 요청 -> 수락으로 맺고, 접속 상태·위치는 맺어진 친구에게만 보인다. 지금 C# `AddFriend`처럼 일방 추가를 허용하면 모르는 사람의 접속 상태를 엿볼 수 있다(결정 대기 3).
7. **4단계 폴링은 알림만 WebSocket으로 옮긴다.** 파티·신청·매칭·판 상태 변화는 `party.changed` 힌트로 푸시하고, 기존 GET을 곧바로 다시 부르게 한다. 폴링은 안전망(WS 연결 중 15초, 끊김 중 기존 간격)으로 남기고, **하트비트·호스트 인계·정산은 REST 그대로 둔다**(9절).
8. **재화가 움직이는 경로가 없다.** 이 단계에는 골드·아이템·경험치 변동이 없으므로 원장·잔액 CHECK는 만들지 않는다. 멱등성은 REST는 `request_log`(`request_id`), 채팅 전송은 `chat_messages` UNIQUE(`sender_account_id`, `client_msg_id`)가 맡는다. 날짜 경계(06:00 일일, 목요일 06:00 주간)는 쓰지 않는다. 이 단계의 기간(7일 보관, 24시간 쿨다운 등)은 전부 "지금부터 N"이라 `resetBoundaries`와 무관하다.

## 1. 공통 규칙

1~2단계 0.1(응답 형식), 0.2(인증), 0.3(버전 헤더), 0.4(멱등성), 3단계 0.2(락)와 4단계 1절을 그대로 쓴다. 이 단계에서 달라지는 것만 적는다.

### 1.1 버전 검사
| 경로 | 클라이언트 버전 | 데이터 버전 |
|---|---|---|
| `/friends*`, `/blocks*`, `/reports` | 함 | **안 함**(`/auth/*`, `/me`와 같다. 게임 데이터에 의존하는 판정이 없다) |
| `/characters/{uuid}/party/invites*` | 함 | 함(`/characters*` 규칙) |
| WebSocket `hello` | 함(`client_version`, 아래 3절) | 안 함 |

### 1.2 계정 단위와 캐릭터 단위
- 채팅은 **캐릭터**가 말한다(이름이 보인다). 연결은 계정당 하나이고 `hello`에서 캐릭터 하나를 고른다.
- 친구·차단·신고 제재는 **계정** 단위다. 요청의 대상은 캐릭터 `uuid`(채팅 줄의 `from.id`)로 지목하고, 서버가 그 캐릭터의 계정을 찾는다. **삭제된(소프트 삭제) 캐릭터도 차단·신고 대상으로 지목할 수 있다**(채팅 줄에 남은 사람을 막고 신고할 수 있어야 한다).
- 응답에는 uuid만 싣는다(내부 bigint 키 금지). 예외 하나: 채팅 `seq`(`chat_messages.id`)는 커서라서 그대로 나간다. 이 값으로 객체를 조회하는 API가 없어 IDOR 대상이 아니다(스키마 머리말에 근거를 적었다).

### 1.3 공통 에러
`400 VALIDATION`, `401 TOKEN_*`, `403 ACCOUNT_BANNED`, `404 CHARACTER_NOT_FOUND`(내 캐릭터가 아니거나 삭제됨), `422 IDEMPOTENCY_MISMATCH`, `426`, `429 RATE_LIMITED`(3단계와 같다). 이 단계의 새 코드는 엔드포인트별 표와 3.8절(WebSocket)에 있다.

### 1.4 속도 제한 버킷
`app.ts`의 IP당 일반 한도(120/분)는 같은 공유기·PC방에서 여러 명이 접속하면 쉽게 걸린다. 3단계 `ECONOMY_PATH`처럼 **`SOCIAL_PATH`(`/friends`, `/blocks`, `/reports`) 별도 IP 버킷**(`RATE_SOCIAL_IP_MAX`, 기본 600/분)을 두고 아래 계정별 한도를 쓴다. `/characters/{uuid}/party/invites`는 이미 경제 버킷에 들어간다. WebSocket 업그레이드는 Express 미들웨어를 지나지 않으므로 핸드셰이크 한도를 따로 둔다(3.9).

## 2. 엔드포인트 요약 (REST 12개 + WebSocket 1개 + 4단계 확장 1개)

| # | 메서드 | 경로 | 하는 일 |
|---|---|---|---|
| W1 | GET (Upgrade) | `/ws` | WebSocket 연결(채팅, 푸시). 프로토콜은 3절 |
| F1 | GET | `/friends` | 친구 목록 + 받은 요청 + 보낸 요청 |
| F2 | POST | `/friends/requests` | 친구 요청 보내기 |
| F3 | POST | `/friends/requests/{id}/respond` | 받은 요청 수락·거절 |
| F4 | DELETE | `/friends/requests/{id}` | 보낸 요청 취소 |
| F5 | DELETE | `/friends/{id}` | 친구 삭제 |
| B1 | GET | `/blocks` | 내 차단 목록 |
| B2 | PUT | `/blocks/{character_id}` | 차단(대상 캐릭터의 계정) |
| B3 | DELETE | `/blocks/{id}` | 차단 해제 |
| X1 | POST | `/reports` | 신고(증거는 서버가 첨부) |
| I1 | POST | `/characters/{uuid}/party/invites` | 파티 초대(방장) |
| I2 | POST | `/characters/{uuid}/party/invites/{id}/respond` | 초대 수락·거절 |
| I3 | DELETE | `/characters/{uuid}/party/invites/{id}` | 보낸 초대 취소(방장) |
| P3+ | GET | `/characters/{uuid}/party` (4단계 확장) | 응답에 `invites_incoming` 추가(8.4) |

일반·파티·귓속말 전송, 놓친 메시지 따라잡기, 접속 상태 푸시는 REST가 아니라 W1의 메시지다(3.5).

## 3. WebSocket 프로토콜 (W1)

### 3.1 연결과 서버 구성
- 주소: `wss://<host>/ws`(개발은 `ws://`). 같은 HTTP 서버의 `upgrade` 이벤트에서 경로가 `/ws`일 때만 `ws`의 `WebSocketServer({ noServer: true, maxPayload: 4096, perMessageDeflate: false })`로 넘긴다. 다른 경로의 업그레이드는 404로 닫는다.
- 클라이언트 IP는 Express와 같은 `trust proxy` 규칙으로 구한다(프록시 뒤에서 모두 같은 IP로 보이는 사고 방지).
- 프레임은 **JSON 텍스트**만 쓴다(바이너리 프레임은 `BAD_FRAME`). 모든 프레임은 `{ "t": "<type>", ... }`이고 키는 snake_case, 시각은 UTC ISO 문자열이다. 프로토콜 버전 `v`는 지금 1이다.
- 서버 프레임은 `ws.send` 한 번에 한 메시지다. 한 세션의 송신 큐가 `WS_SEND_QUEUE_MAX`건을 넘거나 `ws.bufferedAmount`가 1MB를 넘으면 느린 소비자로 보고 4008로 끊는다(한 명이 서버 메모리를 먹지 못하게).
- 연결은 **계정당 하나**다. 같은 계정이 새로 인증하면 먼저 있던 연결에 `bye`(`REPLACED`)를 보내고 4001로 끊는다. 캐릭터를 바꾸면(타이틀로 나갔다 다시 들어오면) 클라이언트가 연결을 닫고 새로 맺는다.

### 3.2 인증과 시작 (hello)
토큰을 URL이나 헤더에 두지 않는다(URL은 프록시·접속 로그에 남는다). **연결 직후 첫 프레임**으로 인증한다.

```
C -> S  {"t":"hello","v":1,"token":"<액세스 JWT>","client_version":"0.5.0","character_id":"<캐릭터 uuid>","since":1042}
```
`since`는 클라이언트가 마지막으로 받은 `seq`(없으면 `null`, 3.6).

- `WS_HELLO_TIMEOUT_MS`(5초) 안에 `hello`가 없으면 4007로 끊는다. 인증 전에는 `hello` 외 프레임을 받지 않는다(프로토콜 위반).
- 검사 순서(REST의 `versionCheck` -> `requireAuth` -> 캐릭터 소유 검사와 같다): ① `client_version`이 `MIN_CLIENT_VERSION` 미만이면 `error`(`CLIENT_OUTDATED`) 후 4426 ② 토큰 서명·만료(만료면 4002, 그 밖은 4004) ③ 계정 정지·삭제 확인(정지면 `sanction` 프레임 후 4003) ④ `character_id`가 내 살아 있는 캐릭터인지(아니면 4010) ⑤ 같은 계정의 기존 연결 대체(4001) ⑥ 채팅 금지 제재와 아직 알리지 않은 제재 읽기 ⑦ 내 차단 목록을 세션 메모리에 적재 ⑧ `accounts.last_character_id` 갱신 ⑨ 일반 채널 방(shard) 배정.
- 이 검사 코드는 `authMiddleware.ts`의 토큰 검증 부분을 함수(`verifyAccessToken`)로 꺼내 REST와 WebSocket이 같이 쓴다. 로직 복사 금지.
- 성공하면 서버는 아래를 이 순서로 보낸다.

```
S -> C  {"t":"ready","v":1,"server_time":"...","account_id":"<uuid>","character":{"id":"<uuid>","name":"..."},
         "shard":1,"token_expires_at":"...","ping_every_ms":15000,"idle_timeout_ms":45000,
         "limits":{"max_length":60,"min_interval_ms":1000,"resend_seconds":30,"party_poll_seconds":15},
         "mute":null | {"ends_at":"...","source":"repeat"|"sanction"}}
S -> C  {"t":"backlog","lines":[ <chat.msg>... ],"gap":false,"cursor":1050}
S -> C  {"t":"friends.presence","list":[ ... ]}
S -> C  {"t":"party.invite", ... }       // 대기 중인 초대가 있으면(30초 안에 만든 것)
S -> C  {"t":"sanction", ... }           // 아직 알리지 않은 경고·금지가 있으면
```
`limits.max_length`, `limits.min_interval_ms`는 `chat.json`에서 읽은 값이다(클라이언트 미리보기가 같은 값을 쓰게 한다). `ready` 이후 서버가 `cursor` 이하의 모든 것을 이미 보냈으므로 클라이언트는 `last_seq = cursor`로 둔다.

### 3.3 하트비트와 유휴 정리
- 클라이언트는 `ping_every_ms`(15초)마다 `{"t":"ping","n":<정수>}`를 보낸다. 서버는 `{"t":"pong","n":<같은 값>,"server_time":"..."}`로 답한다(시계 오차 보정에도 쓴다).
- 서버는 마지막 클라이언트 프레임 이후 `WS_IDLE_TIMEOUT_MS`(45초) 동안 아무것도 없으면 연결을 끊는다(반쯤 끊긴 소켓 정리. 프로토콜 수준 ping/pong 프레임은 Unity `ClientWebSocket`의 `KeepAliveInterval` 동작에 기대지 않고 앱 수준 `ping`을 쓴다).
- 이 `ping`은 **채팅 연결의 생존 신호일 뿐 파티 판의 하트비트(R5)가 아니다**(9절).

### 3.4 토큰 수명
액세스 토큰은 15분이라 연결이 더 오래 간다.
- 만료 60초 전에 서버가 `{"t":"token.expiring","expires_at":"..."}`를 보낸다.
- 클라이언트는 REST로 토큰을 갱신하고 `{"t":"auth","token":"<새 토큰>"}`를 보낸다. 서버는 같은 계정(`sub`)인지 확인하고 `{"t":"auth.ok","token_expires_at":"..."}`로 답한다. 다른 계정이거나 무효면 4004.
- `WS_TOKEN_GRACE_SECONDS`(30초)가 지나도록 갱신이 없으면 `bye` 후 4002로 끊는다. 클라이언트는 갱신한 뒤 바로 다시 연결한다.
- 정지는 이 연결에서도 즉시 반영된다: 제재가 생기면(`dotrpg_sanction` LISTEN) 해당 계정 연결의 채팅 금지 상태를 갱신하고, `ban`이면 `sanction` 후 4003으로 끊는다. LISTEN이 끊겼을 때를 위한 안전망으로 연결마다 `WS_REVALIDATE_SECONDS`(60초)에 한 번 `accounts.banned_until`을 확인한다.

### 3.5 메시지 형식

**클라이언트 -> 서버** (모두 zod `.strict()`, 알 수 없는 `t`나 키는 `error`(`BAD_FRAME`). 10초 안에 3회면 4005)

| t | 필드 | 의미 |
|---|---|---|
| `hello` | `v`, `token`, `client_version`, `character_id`, `since?` | 3.2 |
| `auth` | `token` | 토큰 갱신(3.4) |
| `ping` | `n?` | 3.3 |
| `chat.send` | `cid`(uuid), `channel`(`general` \| `party` \| `whisper`), `text`(1..400자 원문), `to?`(캐릭터 uuid), `to_name?`(1..16자) | 채팅 전송. 귓속말만 `to`와 `to_name` 중 **정확히 하나**를 쓴다. `/w 이름 내용`을 이름만 알고 보내는 현재 UI를 위해 `to_name`을 받는다(서버가 이름으로 접속 중인 캐릭터를 찾는다, 대소문자 무시). 일반·파티에서 `to`를 보내면 `BAD_FRAME` |
| `presence.set` | `map_id` | 내가 있는 맵(친구 목록의 "접속 중 + 위치" 표시용). `maps.json`의 id가 아니면 무시하고 `error`(`BAD_FRAME`) |

받지 않는 값: 보낸 사람 이름, 시각, 순번, 수신자 목록, 금칙어 판정 결과, 채널 방 번호.

**서버 -> 클라이언트**

| t | 필드 | 언제 |
|---|---|---|
| `ready`, `backlog` | 3.2 | 인증 직후 |
| `pong` | `n`, `server_time` | `ping` 응답 |
| `token.expiring`, `auth.ok` | 3.4 | |
| `chat.msg` | `seq`, `channel`, `from:{id,name}`, `to?:{id,name}`(귓속말), `text`, `at` | 다른 사람이 보낸 줄(일반·파티·귓속말). 내가 보낸 줄은 오지 않고 `chat.ack`만 온다 |
| `chat.ack` | `cid`, `seq`, `at`, `text`(서버가 가린 최종 문장), `filtered`, `replay` | 내 `chat.send`가 저장·전달됨. 같은 `cid` 재전송이면 `replay: true`로 처음 결과를 그대로 준다 |
| `chat.err` | `cid`, `code`, `message`, `retry_after_ms?`, `muted_until?` | 내 `chat.send`가 거절됨(3.8) |
| `chat.sys` | `text`, `at` | 서버 공지 한 줄(점검 안내, 이후 단계의 경매 판매 알림 등). 저장하지 않고 `seq`가 없다 |
| `friends.presence` | `list:[{id, online, character:{id,name,class,level}, where:{map_id, activity}\|null}]` | 접속 직후 전체 1회, 이후 친구가 접속·종료·맵 이동·판 입장·캐릭터 변경할 때 바뀐 친구만. `id`는 친구 관계 uuid(F1의 `friends[].id`), `activity`는 `field`\|`dungeon`\|`raid`\|`null`(서버가 `party_run_members` 상태에서 계산, 맵 id는 표시용이며 클라이언트가 보고) |
| `friends.changed` | `reason`: `request` \| `accepted` \| `removed` | 받은 요청이 생김, 내 요청이 수락됨, 친구가 삭제됨. 클라이언트는 `GET /friends`를 다시 부른다 |
| `party.changed` | `scope`: `party` \| `run`, `run_id?` | 4단계 알림 힌트(9절). 클라이언트는 `GET /party`(scope=party) 또는 `GET /party-runs/{id}`(scope=run)를 곧바로 다시 부른다 |
| `party.invite` | `id`, `from:{id,name,class,level}`, `party:{dungeon_id,difficulty,members,max_members,min_power,message}`, `expires_at` | 파티 초대를 받음 |
| `party.invite.closed` | `id`, `state`: `declined` \| `expired` \| `cancelled` \| `accepted` | 내가 보낸(또는 받은) 초대가 끝남 |
| `sanction` | `kind`: `warning` \| `chat_mute` \| `ban`, `ends_at?`, `reason_code`, `message` | 새 제재. 접속 때 아직 못 받은 것도 이 프레임으로 한 번씩 |
| `error` | `ref?`(문제가 된 `t`), `code`, `message` | 채팅이 아닌 프레임의 오류(`BAD_FRAME`, `NOT_AUTHENTICATED`) |
| `bye` | `code`, `reason`, `reconnect`(bool), `retry_after_ms?` | 서버가 끊기 직전. 끊는 모든 경우에 먼저 보낸다 |

- `chat.msg.from.id`는 보낸 캐릭터의 uuid다. 클라이언트는 이것으로 귓속말·친구 요청·차단·신고·초대 대상을 지목한다(이름은 중복·재사용될 수 있으므로 id를 쓴다).
- **일관성 규칙**: 서버는 한 연결에 `seq`가 증가하는 순서로만 `chat.msg`를 보낸다(쓰기 큐가 직렬이므로, 4.5). 클라이언트는 `seq <= last_seq`인 `chat.msg`를 중복으로 버린다.
- 클라이언트 쪽 낙관적 표시: 보낸 줄은 즉시 화면에 넣고(`cid`로 표시), `chat.ack`가 오면 서버가 가린 문장으로 바꾸고, `chat.err`가 오면 줄을 지우고 `message`를 띄운다.

### 3.6 놓친 메시지 따라잡기
연결이 끊긴 동안(또는 끊김을 서버가 알아채기 전 최대 45초 동안) 서버가 보낸 메시지는 소켓 버퍼에서 사라진다. 따라잡기는 두 겹이다.

1. **저장된 줄 재전송**: `hello.since = last_seq`이면 서버가 `since` 이후의 줄을 `backlog`로 한 번에 보낸다.
   - 일반: `chat_messages_general`로 내 shard의 `id > since`, 최근 `CHAT_BACKLOG_GENERAL`(20)줄이고 `CHAT_BACKLOG_MINUTES`(10분) 이내.
   - 파티: 내가 **지금 속한** 파티의 `id > since`이면서 `created_at >= 내 가입 시각`(들어오기 전의 대화는 보이지 않는다), 최근 `CHAT_BACKLOG_PARTY`(50)줄.
   - 귓속말: `chat_messages_whisper`로 내가 받은 `id > since`, 최근 `CHAT_BACKLOG_WHISPER`(50)줄이고 `CHAT_BACKLOG_WHISPER_MINUTES`(30분) 이내.
   - 세 종류를 `seq` 순으로 합치고, 내가 차단한 계정의 줄은 뺀다(서버가 세션 메모리의 차단 목록으로 거른다). 한도에 걸려 일부를 못 줬으면 `gap: true`(클라이언트가 "일부 메시지를 놓쳤습니다." 한 줄을 로컬로 띄운다).
   - `since`가 없으면(첫 접속·앱 재시작) 같은 한도로 최근 줄만 준다(`gap`은 항상 false).
2. **보낸 쪽 재전송**: 클라이언트는 `chat.ack`를 못 받은 보낸 줄(`cid`)을 `ready` 뒤에 같은 `cid`로 다시 보낸다(`CHAT_RESEND_SECONDS`(30초) 이내인 것만). 이미 저장돼 있으면 서버는 새로 저장·전달하지 않고 `chat.ack`(`replay: true`)만 준다. 이 검사는 속도·반복 검사보다 **앞**에서 한다(재전송이 한도를 소모하지 않는다).

**경합 방지(접속 중 새 메시지가 빠지지 않게)**: 서버는 ① 세션을 일반·파티 수신자 목록에 먼저 등록하고(이후 전달분은 세션 큐에 쌓는다) ② `backlog` 쿼리를 읽어 보낸 뒤 ③ 큐에 쌓인 것 중 `seq > cursor`인 것을 이어서 보낸다. 쿼리와 등록 사이에 커밋된 메시지가 둘 다에서 누락되지 않는다.

귓속말은 **오프라인 수신자에게 저장해 두었다가 주지 않는다**(4.4). 따라잡기의 귓속말은 "서버가 연결 끊김을 알아채기 전에 전달을 시도한 줄"을 구하는 용도다.

### 3.7 재접속 규약 (클라이언트)
| 상황 | 서버 close 코드 | 클라이언트 동작 |
|---|---|---|
| 서버 재시작·점검 | 1001 + `bye`(`reconnect:true`, `retry_after_ms`) | 지수 백오프로 재연결(아래) |
| 네트워크 끊김, 비정상 종료 | 1006(서버 발신 아님) | 지수 백오프로 재연결 |
| 토큰 만료 | 4002 | REST로 토큰 갱신 후 **바로** 재연결(실패하면 로그인 화면) |
| 다른 곳에서 접속 | 4001 | 자동 재연결하지 않는다. "다른 곳에서 접속했습니다" 표시 |
| 이용 정지 | 4003 | 재연결하지 않는다. `sanction`의 문구 표시 |
| 토큰 무효, 형식 오류, 캐릭터 무효 | 4004, 4005, 4010 | 재연결하지 않는다. 로그인(4004) 또는 캐릭터 선택(4010)으로 |
| 업데이트 필요 | 4426 | 재연결하지 않는다. 업데이트 안내 |
| 느린 소비자·도배 폭주 | 4008, 4006 | 백오프로 재연결(채팅 입력은 막지 않는다) |
| 인증 시간 초과 | 4007 | 백오프로 재연결 |

- **백오프**: 1, 2, 4, 8, 16, 30초(상한) 곱하기 0.8~1.2의 난수. 60초 이상 안정적으로 연결돼 있었으면 처음 값으로 되돌린다. 서버 재시작 직후 100명이 한꺼번에 오는 것을 `retry_after_ms`(서버가 무작위로 3~8초 사이에서 정해 `bye`에 실어 보낸다)와 이 난수가 흩는다.
- 연결이 없는 동안 클라이언트는 채팅 입력을 로컬에서 거절한다("채팅 서버에 다시 연결하는 중입니다."). 보낸 줄을 쌓아 두었다가 한꺼번에 보내지 않는다(위 3.6의 재전송은 끊기기 직전에 보낸 것만).
- 연결이 끊긴 사이에 REST(친구·차단·신고)는 그대로 동작한다. 파티는 4단계 폴링으로 돌아가므로(9절) 기능이 줄지 않는다.
- 서버 종료(`SIGTERM`)는 기존 `shutdown`을 확장한다: 새 업그레이드를 막고, 모든 연결에 `bye`(1001, `reconnect:true`)를 보내 닫고, 쓰기 큐를 비운 뒤 HTTP 서버를 닫는다(열린 WebSocket이 `server.close`를 막지 않게).

### 3.8 채팅 오류 코드 (`chat.err`)
`message`는 서버가 만든 한국어 문장이고 C# `MockChatService.Send`의 문구를 그대로 따른다(클라이언트는 토스트로 띄우기만 한다).

| code | 뜻 | 추가 필드 |
|---|---|---|
| `TEXT_EMPTY` | 정리한 뒤 비어 있다 | |
| `TEXT_TOO_LONG` | `maxLength` 초과(코드포인트 기준. 클라이언트는 자르고 보내므로 정상 흐름에서는 없다) | |
| `RATE_LIMITED` | 최소 간격 미달 또는 분당 상한 초과 | `retry_after_ms` |
| `MUTED_REPEAT` | 같은 말 반복으로 막힘 | `muted_until` |
| `MUTED_SANCTION` | 제재로 채팅 금지 중 | `muted_until` |
| `MESSAGE_BLOCKED` | 금칙어를 숨기려 변형한 문장(4.3) | |
| `CHANNEL_INVALID` | 알 수 없는 채널 | |
| `NOT_IN_PARTY` | 파티 채널인데 파티가 없다 | |
| `TARGET_REQUIRED` | 귓속말 상대 없음 | |
| `TARGET_NOT_FOUND` | 그 이름·id의 캐릭터가 없다 | |
| `TARGET_OFFLINE` | 상대가 접속 중이 아니거나 다른 캐릭터로 접속 중 | |
| `TARGET_BLOCKED` | 내가 상대를 차단 중("{name}님은 차단 중입니다.") | |
| `SELF_WHISPER` | 자기 자신에게 귓속말 | |
| `WHISPER_TARGETS_LIMIT` | 1분에 말을 건 서로 다른 상대가 너무 많다(광고 방지) | `retry_after_ms` |
| `CHAT_UNAVAILABLE` | 저장 실패(DB 오류). 아무에게도 전달되지 않았다 | |

상대가 **나를 차단한 경우**는 오류를 주지 않는다: 서버가 저장·전달 없이 `chat.ack`를 정상으로 돌려주고 한도는 그대로 센다(차단당한 사실이 드러나지 않게). 이때 저장하지 않는 이유는 받을 수 없는 대화를 증거로 쌓지 않기 위해서다.

### 3.9 속도 제한과 방어 (WebSocket)

**연결 단계(IP 기준, 메모리, 확장 시 Redis)**

| 대상 | 한도 | 근거 |
|---|---|---|
| 핸드셰이크 시도 | IP당 `WS_HANDSHAKE_PER_MIN_IP`(60)/분 | 같은 IP에 PC방 여러 명을 허용하는 값. 서버 재시작 후 재연결 폭주도 흡수 |
| 인증 전 소켓 | IP당 `WS_UNAUTH_PER_IP`(20) 동시 | `hello` 없이 소켓만 쌓는 공격 |
| 전체 연결 | `WS_MAX_CONNECTIONS`(500) | 메모리 안전장치. 초과면 업그레이드를 503으로 거절 |

**연결 후(계정 기준)**

| 대상 | 한도 | 위반 시 |
|---|---|---|
| 프레임 수 | 연결당 초당 `WS_FRAMES_PER_SEC`(10) | 4006으로 끊는다(폭주 방어. 정상 사용은 초당 1~2개) |
| 프레임 크기 | `WS_MAX_PAYLOAD_BYTES`(4096) | 1009 |
| 채팅 최소 간격 | `chat.json minIntervalSeconds` x `CHAT_INTERVAL_TOLERANCE`(0.8) | `RATE_LIMITED`. 클라이언트는 1초를 지키지만 네트워크 지터로 1초 간격이 0.9초로 도착할 수 있어 서버는 조금 느슨하다 |
| 채팅 분당 상한 | 계정당 `CHAT_PER_MINUTE`(30) | `RATE_LIMITED` |
| 같은 문장 반복 | `repeatLimit`(3)회 연속 -> `muteSeconds`(10) 금지 | 3번째는 전송되고 이후 막힌다(C# `MockChatService.Send`와 같은 시점). `MUTED_REPEAT` |
| 귓속말 상대 수 | 계정당 1분에 서로 다른 상대 `CHAT_WHISPER_TARGETS_PER_MIN`(10) | `WHISPER_TARGETS_LIMIT` |
| 금칙어 가림 횟수 | 계정당 `CHAT_FILTER_WINDOW_MINUTES`(10) 안에 `CHAT_FILTER_STRIKES`(5) | 자동 채팅 금지(10절) |

- 제한 상태(마지막 전송 시각, 직전 문장, 반복 횟수, 최근 전송 목록, 귓속말 상대 집합, 금칙어 횟수)는 **연결이 아니라 계정에 붙인다**(`ChatLimiterStore` 인터페이스 뒤 메모리, 마지막 사용 후 10분 보관). 재접속해도 초기화되지 않아 끊었다 다시 붙는 것으로 한도를 우회할 수 없다.
- 거절된 전송은 `lastSentAt`·반복 횟수를 갱신하지 않는다(정상 사용자가 막힌 직후 바로 다시 보낼 수 있다). 프레임 수 한도가 거절 연타를 막는다.

## 4. 채팅 처리

### 4.1 채널
| 채널 | 받는 사람 | 비고 |
|---|---|---|
| `general` | 같은 shard에 접속한 모든 세션(보낸 사람 제외, 보낸 사람을 차단한 세션 제외) | shard는 일반 채널 방. 시작은 1개이고 `CHAT_SHARD_SIZE`(150)를 넘으면 다음 번호를 쓴다. PLAN_ONLINE의 "같은 마을(채널)"을 "서버 방"으로 단순화했다(동시 100명 미만에서 마을별 분리는 대화를 쪼갠다). 접속 중에 shard를 바꾸지 않는다 |
| `party` | 보내는 순간 같은 파티(`party_members.left_at IS NULL`)의 접속 중인 멤버 | 파티 채널은 shard와 무관하다. 파티 소속은 **보낼 때마다 DB에서** 확인한다(`party_members_one_active`, 1초에 한 번 이하라 비용이 작고, 강퇴당한 사람이 캐시 때문에 계속 보내는 일을 막는다) |
| `whisper` | 지정한 캐릭터의 접속 중인 세션 한 개 | 4.4 |
| 시스템 | 나 | 서버가 `chat.sys`로 보내는 공지와 `sanction` 안내만. 클라이언트 `PostSystem`(강화 +10 알림 등)은 로컬 표시를 그대로 쓴다 |

### 4.2 전송 처리 순서 (`chat.send`)
**첫 실패에서 중단**한다. 모든 판정은 서버가 다시 한다(클라이언트 미리보기와 같은 규칙이지만 결과가 최종이다).

1. 세션이 인증·캐릭터 결합 상태인가(아니면 `error NOT_AUTHENTICATED`).
2. 프레임 검증(zod). 틀리면 `error BAD_FRAME`(3.5).
3. **멱등성**: `(sender_account_id, cid)` 행이 있으면 `chat.ack`(`replay: true`)를 돌려주고 끝. 한도를 소모하지 않는다.
4. 제재 채팅 금지 중인가(`MUTED_SANCTION`) -> 반복 금지 중인가(`MUTED_REPEAT`).
5. 문장 정리: 유니코드 NFC 정규화, 줄바꿈·제어 문자·제로폭 문자·방향 제어 문자 제거, 연속 공백을 하나로, 앞뒤 공백 제거. 비면 `TEXT_EMPTY`. 길이는 코드포인트 수로 `maxLength` 이하(`TEXT_TOO_LONG`). `<`, `>`는 그대로 둔다(클라이언트 `Safe()`가 리치 텍스트로 해석되지 않게 바꾼다).
6. 최소 간격·분당 상한(`RATE_LIMITED`).
7. 채널별 검사: 파티는 소속 확인(`NOT_IN_PARTY`), 귓속말은 4.4.
8. 금칙어 처리(4.3). 변형 우회면 `MESSAGE_BLOCKED`.
9. 반복 검사 갱신: 정리한 문장(소문자·공백 제거 비교)이 직전 전송과 같으면 횟수 +1, 아니면 1. 횟수가 `repeatLimit`에 닿으면 **이 메시지는 보내고** `muteSeconds` 동안 금지한다(C#과 같은 시점).
10. **쓰기 큐에 넣는다**(4.5): INSERT -> 수신자 목록 계산 -> 차단 필터 -> 전달 -> 보낸 사람에게 `chat.ack`.

### 4.3 금칙어
- 목록은 운영 파일 `server/data/banned_words.json`(4단계 `utils/bannedWords.ts`가 이미 읽는다, 파일이 없으면 빈 목록 - **현재 파일이 레포에 없으므로 필터가 비어 있다**, 결정 대기 5). 게임 데이터가 아니라 운영 목록이며 파일 수정 시각이 바뀌면 다시 읽는다(재시작 불필요).
- 모집 메시지(4단계)는 폼 입력이라 **거부**(`MESSAGE_BLOCKED`)하지만 채팅은 C# 미리보기와 같이 **가림**(`*`로 치환, 길이 유지)이 기본이다: 정규화한 문장(NFKC, 소문자)에서 금칙어와 일치하는 구간을 원문의 같은 구간에 대응시켜 `*`로 바꾼다. 가렸으면 `filtered=true`이고 `chat.ack.text`로 보낸 사람이 가려진 결과를 본다.
- **우회 감지**: 공백·기호·점·밑줄을 모두 지운 사본에 금칙어가 있으면(예: "시 발", "씨.발") 가림 대신 `MESSAGE_BLOCKED`로 거절한다. 구간 대응이 불가능하기도 하고, 숨기려는 의도가 분명하기 때문이다. 이것도 금칙어 횟수에 센다.
- `utils/bannedWords.ts`에 `maskBannedWords(text): { text, hit }`와 `hasObfuscatedBannedWord(text): boolean`을 더한다(기존 `containsBannedWord`는 그대로).
- 가림·거절이 한 번 일어날 때마다 계정의 금칙어 횟수가 1 늘고, 창 안에서 `CHAT_FILTER_STRIKES`에 닿으면 자동 채팅 금지(10.2).

### 4.4 귓속말
- 대상: `to`(캐릭터 uuid) 또는 `to_name`(이름, 살아 있는 캐릭터에서 대소문자 무시로 찾는다. `characters_name_alive` 인덱스). 없으면 `TARGET_NOT_FOUND`.
- **접속 중인 그 캐릭터**에게만 간다. 상대 계정이 다른 캐릭터로 접속 중이어도 `TARGET_OFFLINE`이다. 오프라인 귓속말은 저장해서 나중에 전달하지 않는다(광고 쪽지 방지, 우편은 6단계). 상대가 접속 중이면 서버가 수신자를 확인한 뒤 저장한다.
- 내가 상대 계정을 차단 중이면 `TARGET_BLOCKED`. 상대가 나를 차단 중이면 조용히 버리고 `chat.ack`만 준다(3.8).
- 자기 자신: `SELF_WHISPER`.
- 보낸 사람 화면: 보낸 줄은 `from=나`, `to=상대`로 낙관적으로 표시하고 `chat.ack`로 확정한다. 받는 사람은 `chat.msg`(`channel: whisper`, `from`, `to`)를 받는다.

### 4.5 쓰기 큐와 순서 보장
- 채팅 쓰기는 프로세스 안 **단일 큐**(`ChatWriter`)로 직렬화한다: 한 번에 하나씩 `INSERT -> 커밋 -> 전달`. 그래서 `id`(BIGSERIAL) 순서가 커밋 순서이고 전달 순서와 같다. 서로 다른 트랜잭션이 `id`를 먼저 받고 나중에 커밋해서 커서(`id > since`)가 메시지를 건너뛰는 일이 없다.
- 처리량: 직렬 INSERT는 1건에 수 ms라 초당 수백 건을 감당한다. 이 단계의 상한(100명 x 1초 1건)이 그 밖이다.
- 서버가 여러 대가 되면(확장 1단계) 이 가정이 깨진다. 같은 순서 보장을 DB 쪽에서 얻으려면 INSERT 트랜잭션 앞에 `pg_advisory_xact_lock(<채팅 키>)`를 잡아 커밋 순서를 직렬화한다(이 단계 규모에서는 비용이 없다). 인터페이스 `ChatWriter`가 이 부분을 가린다.

## 5. 친구 (F1~F5)

계정 단위, 상호 수락이다. 표시되는 캐릭터는 그 계정의 `last_character_id`(접속 중이면 지금 결합한 캐릭터)다. 친구 수 한도 `FRIEND_MAX`(50), 보낸 요청 `FRIEND_PENDING_OUT_MAX`(20), 받은 요청 `FRIEND_PENDING_IN_MAX`(50). 대기 중 요청은 `FRIEND_REQUEST_DAYS`(14)일이 지나면 지연 만료한다(요청이 닿을 때 조건부 UPDATE로 `cancelled` 처리).

### 5.1 GET /friends (F1)
- 응답 `data`:
```
{ "friends":  [ { "id": uuid, "character": { "id": uuid, "name": "...", "class": "warrior", "level": 12 },
                  "online": true, "where": { "map_id": "forest_village", "activity": "field" } | null, "since": ISO } ],
  "incoming": [ { "id": uuid, "from": { "id", "name", "class", "level" }, "created_at": ISO, "expires_at": ISO } ],
  "outgoing": [ { "id": uuid, "to":   { "id", "name", "class", "level" }, "created_at": ISO } ] }
```
- 정렬: 접속 중 먼저, 이름 순. `online`/`where`는 서버 메모리 접속 목록에서 읽는다(`friends.presence`와 같은 소스). 레벨은 DB 값이다.
- 인덱스: `friendships_requester`와 `friendships_target`(둘 다 `state IN ('pending','accepted')` 부분 인덱스)를 UNION으로 읽는다. 이유: 내가 요청한 쪽인지 받은 쪽인지는 행마다 달라 한 컬럼 조건으로 못 쓰고, 친구가 50명 이하라 두 번의 인덱스 조회가 가장 싸다.
- 속도 제한: 계정당 초당 1회. 클라이언트는 창을 열 때와 `friends.changed`를 받았을 때만 부른다(폴링 금지).

### 5.2 POST /friends/requests (F2)
- 요청(`.strict()`): `{ request_id: uuid, character_id: uuid, target: uuid }`. `character_id`는 요청하는 내 캐릭터(요청자 이름 표시용), `target`은 상대 캐릭터. 받지 않는 값: 상대 계정 id, 시각.
- 처리(내 캐릭터 소유 확인 -> 상대 캐릭터에서 계정 찾기):
  1. 자기 자신 `422 CANNOT_TARGET_SELF`. 상대 캐릭터 없음 `404 PLAYER_NOT_FOUND`.
  2. 내가 상대를 차단 중 `409 YOU_BLOCKED_TARGET`(클라이언트가 해제를 안내).
  3. 이미 친구 `409 ALREADY_FRIENDS`.
  4. 내 친구 수 >= `FRIEND_MAX` `422 FRIEND_LIST_FULL`. 내 보낸 요청 수 >= `FRIEND_PENDING_OUT_MAX` `429 TOO_MANY_PENDING`.
  5. 같은 상대에게 이미 보낸 대기 요청이 있으면 그 요청을 그대로 돌려준다(`200`, 멱등).
  6. 상대가 나에게 보낸 대기 요청이 있으면 **상호 요청으로 보고 바로 수락**한다(`200 { status: "accepted", friend }`).
  7. 내가 같은 상대에게 보냈다가 거절·취소된 요청이 `FRIEND_REREQUEST_HOURS`(24) 안에 있으면 `429 REREQUEST_COOLDOWN`(`errors.retry_after_sec`). 같은 사람에게 반복 요청으로 괴롭히는 것을 막는다.
  8. **조용한 무시**: 상대가 나를 차단했거나, 상대의 받은 요청·친구 수가 가득 찼으면 행을 만들지 않고 정상 응답(`201 { status: "pending", request: null }`)을 준다(차단·상태가 드러나지 않게).
  9. `friendships` INSERT(`pending`). 상대가 접속 중이면 `friends.changed(request)`를 푸시.
- 동시 요청: A->B와 B->A가 동시에 오면 `friendships_pair_live`(두 계정 쌍의 부분 유니크 인덱스)가 둘 중 하나를 `23505`로 막는다. 막힌 쪽은 기존 행을 다시 읽어 6번 규칙으로 수락한다.
- 응답 `201`(새 요청) / `200`(기존 요청, 상호 수락) `data`: `{ "status": "pending" | "accepted", "request"?: { id, to: {...}, created_at }, "friend"?: { id, character, online, where, since } }`.
- 멱등성: `request_id`. 인덱스: `friendships_pair_live`(쌍 검사), `friendships_pair_hist`(쿨다운).

### 5.3 POST /friends/requests/{id}/respond (F3), DELETE /friends/requests/{id} (F4), DELETE /friends/{id} (F5)
| 경로 | 요청 | 규칙 | 에러 |
|---|---|---|---|
| F3 | `{ request_id, accept: boolean }` | 받은 사람만. `UPDATE ... WHERE id AND state='pending'`의 영향 행 수로 판정(동시 수락·취소 경합을 DB가 직렬화). 수락 시 양쪽 친구 수 확인(`422 FRIEND_LIST_FULL`). 응답 `{ friend }`(수락) / `{ declined: true }`(거절). 요청자에게 `friends.changed(accepted)` | `404 REQUEST_NOT_FOUND`(내 요청이 아님 포함), `410 REQUEST_EXPIRED`, `409 REQUEST_NOT_PENDING` |
| F4 | 없음 | 보낸 사람만. `pending -> cancelled`. 이미 취소된 것을 또 취소해도 `200`(멱등) | `404 REQUEST_NOT_FOUND` |
| F5 | 없음 | 친구 관계의 양쪽 누구나. `accepted -> removed`(`ended_by` 기록). 이미 삭제됐으면 `200`(멱등). 상대에게 `friends.changed(removed)`(상대가 접속 중일 때) | `404 FRIEND_NOT_FOUND`(내 관계가 아님 포함) |

속도 제한: F2 계정당 분당 10회, F3~F5 계정당 초당 2회. 멱등성: F3은 `request_id`, F4·F5는 자연 멱등.

## 6. 차단 (B1~B3)

계정 단위다(상대가 다른 캐릭터로 와도 막힌다). 한도 `BLOCK_MAX`(100). 차단하면 **서버가** 상대의 일반·파티·귓속말을 나에게 보내지 않고, 친구 요청·파티 초대는 조용히 버려진다(상대에게는 정상 응답).

### 6.1 GET /blocks (B1)
응답 `data`: `{ "blocks": [ { "id": uuid, "name": "차단 당시 캐릭터 이름", "blocked_at": ISO } ] }`. 속도 제한 계정당 초당 1회. 클라이언트는 접속 직후와 창을 열 때 부른다.

### 6.2 PUT /blocks/{character_id} (B2)
- 요청 본문 없음. `character_id`는 지목한 캐릭터(채팅 줄의 `from.id`, 삭제된 캐릭터 포함). 자연 멱등이라 `request_id`를 받지 않는다.
- 처리(한 트랜잭션): 대상 캐릭터에서 계정 찾기(`404 PLAYER_NOT_FOUND`), 자기 자신 `422 CANNOT_TARGET_SELF`, 이미 차단 중이면 `200`으로 기존 차단을 돌려준다, 한도 `422 BLOCK_LIST_FULL` -> `blocks` INSERT -> 두 사람 사이의 친구 관계는 `removed`(`ended_by` = 나), 대기 요청은 `declined`/`cancelled`, 대기 파티 초대는 `cancelled`로 닫는다 -> 커밋 뒤 내 접속 세션의 차단 집합을 갱신.
- 응답 `201`(새로) / `200`(이미) `data`: `{ "block": { id, name, blocked_at } }`.
- 같은 파티에 있는 상대는 **자동으로 내보내지 않는다**(파티 규칙은 4단계, 차단은 소통 필터다). 파티 채팅은 서버가 거르므로 보이지 않는다.

### 6.3 DELETE /blocks/{id} (B3)
`blocks.deleted_at = now()`(소프트 삭제). 이미 해제됐으면 `200`(멱등). 남의 차단 id는 `404 BLOCK_NOT_FOUND`. 해제해도 끝난 친구 관계는 복구되지 않는다(다시 요청해야 한다).
속도 제한: B2·B3 계정당 분당 20회.

### 6.4 서버 메모리의 차단 집합
세션은 `blocks`(내가 차단한 계정 id 집합)를 접속 때 적재하고 B2·B3 성공 때 갱신한다. 전달할 때 `수신자.blocks.has(보낸 사람 계정)`이면 보내지 않는다(DB 조회 없음). 서버가 여러 대가 되면 갱신을 알림 채널(12절)로 전파한다.

## 7. 신고 (X1)

### 7.1 POST /reports
- 요청(`.strict()`): `{ request_id: uuid, character_id: uuid, target: uuid, reason: "abuse" | "spam" | "scam_ad" | "cheat" | "other" }`. `character_id`는 신고하는 내 캐릭터(증거의 "내가 본 것"을 가르는 기준). 받지 않는 값: 증거 줄, 채팅 로그, 시각, 신고자 이름.
- 사유 코드와 C# `ChatRules.ReportReasons`: 욕설·비하 `abuse`, 도배 `spam`, 광고·사기 `scam_ad`, 불법 프로그램 의심 `cheat`, 기타 `other`(mapping 2.4).
- 처리(한 트랜잭션):
  1. 대상 확인(삭제된 캐릭터 포함, `404 PLAYER_NOT_FOUND`), 자기 자신 `422 CANNOT_TARGET_SELF`.
  2. **만난 적 있는 사람만**(허위 신고·표적 신고 방지): 대상 계정이 최근 `REPORT_CONTEXT_HOURS`(24) 안에 ① 내가 볼 수 있는 채팅 줄(내 shard의 일반, 내 파티 채팅, 나와 주고받은 귓속말)을 남겼거나 ② 같은 파티에 같이 있었거나 ③ 지금 친구인 경우만 접수한다. 아니면 `422 REPORT_NO_CONTEXT`. 내 shard는 접속 중 세션에서 읽고 접속이 없으면 일반 채널 줄은 증거 후보에서 빼며 ②③만 본다.
  3. 한도: 계정당 시간당 `REPORT_PER_HOUR`(5), 일일 `REPORT_PER_DAY`(20) -> `429 REPORT_LIMIT`(`reports_reporter` 인덱스로 센다).
  4. 중복: 같은 (신고자, 대상, 사유)의 처리 전(`open`/`reviewing`) 신고가 있으면 `reports_open_uq`가 막고, 기존 신고를 `200 { duplicate: true }`로 돌려준다.
  5. **증거 수집**(서버가 `chat_messages`에서): `REPORT_LOOKBACK_MINUTES`(30) 안에서 (a) 내가 볼 수 있던 최근 `reportLines`(20)줄(내가 차단한 계정의 줄은 포함하지 않는다) + (b) 그 안에서 대상이 한 최근 `REPORT_TARGET_LINES`(10)줄을 합쳐 `seq`로 중복을 없애고 순서대로 `report_lines`에 복사한다(대상의 줄은 `is_target=true`). **귓속말은 나와 대상 사이의 것만** 붙인다(내가 제3자와 나눈 귓속말은 운영자에게 가지 않는다).
  6. `reports`(`open`, `line_count`) INSERT. 선택 설정(`REPORT_AUTO_MUTE_REPORTERS`, 기본 0=끔)이 켜져 있고 24시간 안에 서로 다른 신고자가 그 수에 닿으면 자동 채팅 금지(10.2).
- 응답 `201` `data`: `{ "report": { "id": uuid, "line_count": 20, "state": "open" } }`. 클라이언트는 `line_count`로 "최근 대화 N줄을 첨부했습니다." 문구를 만든다.
- 에러: `404 PLAYER_NOT_FOUND`, `422 CANNOT_TARGET_SELF`, `422 REPORT_NO_CONTEXT`, `429 REPORT_LIMIT`.
- 멱등성: `request_id`. 속도 제한: 계정당 시간당 5회 + 일 20회(위 3), IP는 `SOCIAL_PATH` 버킷.
- 인덱스와 이유: `chat_messages_general (shard, id)`(내 shard의 최근 줄), `chat_messages_party (party_id, id)`(내 파티의 줄), `chat_messages_whisper (recipient_account_id, id)`(나->대상, 대상->나 두 번 조회), `chat_messages_sender (sender_account_id, id DESC)`(대상의 최근 줄), `reports_open_uq`(중복), `reports_reporter`(한도).
- 신고자에게 처리 결과를 알리지 않는다(이 단계). 신고하고 나서 차단은 자동으로 하지 않는다(UI가 따로 권한다).

## 8. 파티 초대 (I1~I3)와 4단계 확장

4단계 12절은 "친구 초대는 5단계"로 넘겼고 `parties.listed=false`(비공개 파티)는 "5단계 초대용"으로 남겨 두었다. 초대는 방장이 상대를 지목해 파티로 부르는 길이다. 모집 게시판의 신청 -> 수락과 같은 검사를 지나므로 **초대가 새 우회로가 되지 않는다**.

### 8.1 POST /characters/{uuid}/party/invites (I1)
- 요청(`.strict()`): `{ request_id: uuid, target: uuid }`. 받지 않는 값: 시각, 전투력.
- 처리: 내 파티가 있고 `forming`이어야 한다(`404 NOT_IN_PARTY`, `409 PARTY_BUSY`), **방장만**(`403 NOT_LEADER`), 정원 여유(`현재 멤버 + 대기 초대 < max_members`, 아니면 `409 PARTY_FULL`), 대상 캐릭터 존재(`404 PLAYER_NOT_FOUND`)와 자기 자신(`422 CANNOT_TARGET_SELF`), **대상이 그 캐릭터로 접속 중**(`422 TARGET_OFFLINE`), 내가 대상을 차단 중(`409 YOU_BLOCKED_TARGET`), 대상이 파티·대기열·판 중이면 `409 TARGET_BUSY`, 쿨다운(방장당 분당 `INVITE_PER_MIN`(10), 같은 대상에게 `INVITE_PER_TARGET_SECONDS`(10)초에 한 번: `429 INVITE_COOLDOWN`).
- 상대가 **나를 차단**했으면 조용히 버리고 정상 응답을 준다(차단 사실이 드러나지 않게).
- `party_invites` INSERT(`expires_at = now + PARTY_INVITE_SECONDS(30)`), 상대 세션에 `party.invite` 푸시. 응답 `201` `data`: `{ "invite": { id, state: "pending", expires_at } }`.
- 멱등성: `request_id`. 같은 파티가 같은 대상에게 이미 대기 초대가 있으면(`party_invites_pending_uq`) 기존 초대를 `200`으로 돌려준다.

### 8.2 POST /characters/{uuid}/party/invites/{id}/respond (I2)
- 요청 `{ request_id, accept: boolean }`. 받은 사람(그 초대의 `invitee_character_id`가 내 캐릭터)만. 아니면 `404 INVITE_NOT_FOUND`. 대기 아님 `409 INVITE_NOT_PENDING`, 만료(30초 경과, 지연 처리) `410 INVITE_EXPIRED`.
- `accept=true`: 4단계 P7(신청 수락)과 **같은 검사와 같은 잠금 순서**를 쓴다: 내 캐릭터 잠금 -> `parties` 잠금 -> 내가 이미 파티·대기·판에 있으면 `ALREADY_IN_PARTY`/`IN_QUEUE`/`IN_PARTY_RUN`, 파티가 사라졌거나 `forming`이 아니면 `PARTY_NOT_FOUND`, 정원 `PARTY_FULL`, `checkEntry`(던전 자격·레벨·입장 횟수) -> `power_estimate >= min_power`(`POWER_TOO_LOW`) -> `party_members` INSERT, 초대 `accepted`, 내 다른 대기 신청·초대는 `cancelled`, 멤버 `ready` 해제, `version`+1. 즉 최소 전투력이나 입장 자격을 초대로 건너뛸 수 없다.
- 방장은 `party.changed`(scope=party)와 `party.invite.closed`(`accepted`)를 받는다.
- 응답 `data`: 수락 `{ "party": PartyView }`, 거절 `{ "declined": true }`(방장에게 `party.invite.closed`(`declined`)).
- 멱등성: `request_id`.

### 8.3 DELETE /characters/{uuid}/party/invites/{id} (I3)
방장이 보낸 초대를 취소한다(`pending -> cancelled`). 이미 끝난 초대는 `200`(멱등), 남의 초대는 `404 INVITE_NOT_FOUND`. 받는 사람에게 `party.invite.closed`(`cancelled`).

### 8.4 4단계 `GET /characters/{uuid}/party` 확장 (P3+)
응답 `data`에 `invites_incoming`을 더한다: `[ { id, from: { id, name, class, level }, party: { dungeon_id, difficulty, members, max_members, min_power, message }, expires_at } ]`(`state='pending'`이고 만료 전인 것, `party_invites_invitee` 인덱스). WebSocket이 끊겨 있는 동안에도 폴링으로 초대를 받을 수 있게 하는 안전망이다. `PartyView` 자체는 바꾸지 않는다. 지연 만료는 이 폴링과 I2가 처리한다.

## 9. 4단계 알림·하트비트를 WebSocket으로 옮길 것인가: 판단과 권장

**권장: 알림(변화 통지)만 WebSocket으로 옮기고, 하트비트·호스트 인계·정산은 REST에 둔다. 폴링은 안전망으로 남긴다.**

| 항목 | 4단계 지금 | 5단계 판단 | 이유 |
|---|---|---|---|
| 파티 구성·신청·매칭 완료·강퇴·해산 알림 | `GET /party` 폴링 1.5초(파티 중)/5초(평시) | **WS `party.changed(party)` 푸시로 이동**. 받으면 곧바로 같은 GET 호출. 폴링은 WS 연결 중 `PARTY_POLL_WS_SECONDS`(15)초로 늦추고, 끊김 중엔 기존 간격 | 지연이 1.5초 -> 거의 즉시. 신청 30초 만료·매칭 60초 같은 짧은 기한이 체감상 정확해진다. 폴링 요청이 평시 초당 약 20건 -> 7건(100명 가정)으로 줄지만 **이득은 부하보다 지연**이다 |
| 판 상태(gathering -> playing, 호스트 변경, 판 종료) | `GET /party-runs/{id}` 1초 폴링 | **WS `party.changed(run)` 푸시** + 폴링 안전망 | begin/인계가 늦으면 전투 시작·재연결이 늦어진다. 힌트 푸시만으로 최대 5초(하트비트)·1초(폴링) 지연이 거의 0이 된다 |
| 파티 초대 | 없음(5단계 신규) | WS `party.invite` 푸시 + `GET /party`의 `invites_incoming` 안전망 | 30초짜리 알림이라 폴링만으로는 부족 |
| 판 하트비트 R5(5초) | REST | **REST 유지** | ① 하트비트는 알림이 아니라 **서버 판정의 입력**이다(`last_seen_at`, `HOST_STALE_SECONDS`, 재접속 60초, 인계 자격). 요청-응답이라 오류 코드·`seen_epoch` 검증·속도 제한·멱등성이 이미 REST에서 검증됐다 ② 채팅 연결의 상태와 판 생존을 묶으면 채팅 쪽 문제(폭주 차단 4006, 느린 소비자 4008, 서버 재시작, 토큰 갱신)가 파티 판의 "연결 끊김" 판정으로 번진다. 두 신호는 고장 모드가 달라 분리해야 한다 ③ WS가 살아 있어도 방장 PC와의 P2P가 죽을 수 있다(4단계 6.7의 `HOST_ALIVE` 사례). 서버는 WS 상태가 아니라 하트비트로만 판단한다 |
| 호스트 인계 R6 | REST | **REST 유지**, 성공하면 서버가 모든 판 멤버에게 `party.changed(run)`을 푸시 | 인계 판정은 `party_runs` 잠금 아래 REST 한 곳에서 한다. 새 방장 소식은 힌트로 즉시 전달해 멤버가 5초 하트비트를 기다리지 않고 재연결하게 한다 |
| 결과 정산 대기(`settle`) | REST 폴링(`settle_after_ms` 2초) | **REST 유지** | 한 판에 몇 번뿐이고 응답에 결과가 들어 있다. 이득이 없다 |

**왜 힌트만 보내고 전체 상태를 밀지 않나.** `PartyView`는 받는 사람마다 다르다(방장만 `applications`를 본다, 멤버마다 `is_me`가 다르다). 푸시에 상태를 싣으면 직렬화 코드를 두 번 만들고 권한 누수 위험이 생긴다. 힌트 + 기존 GET은 이미 검증된 경로(소유 검사, 버전 검사, 지연 전이)를 그대로 쓰고, 힌트가 유실돼도 안전망 폴링이 따라잡는다. 4단계의 `after_version` 폴링(`changed:false`로 응답이 작다)은 그대로라 **4단계 클라이언트·서버 동작은 WS 없이도 변하지 않는다.**

**서버 변경(4단계 코드에 닿는 곳)**: 트랜잭션 커밋 뒤에 `RealtimeNotifier.partyChanged(characterIds, scope, runId?)`를 부른다. 같은 계정에 200ms 안의 여러 번은 하나로 묶는다. 호출 지점은 `partyRepository.bump`와 `closeParty`가 한 트랜잭션에서 `parties.version`을 바꾸는 곳 전부(`partyService`의 모든 변경, `matchService.runMatchTick`이 만드는 매칭 파티, `partyruns` 서비스의 R1·R3·R4·R6·R8과 지연 전이, 판 종료 판정). 트랜잭션 도중에 부르면 커밋 전 데이터를 보고 GET이 옛 값을 돌려주므로 반드시 **커밋 뒤**여야 한다: `db/pool.ts`의 `withTransaction`에 `afterCommit(fn)`을 더해 롤백되면 버리게 한다. 인터페이스는 한 줄 구현(WS 세션이 없으면 아무 일도 안 함)이라 WS가 꺼져 있어도 4단계는 그대로 돈다.

## 10. 보관과 운영(신고·제재)

### 10.1 보관 기간
| 데이터 | 기간 | 이유 |
|---|---|---|
| `chat_messages`(일반·파티·귓속말 모두) | `CHAT_RETENTION_DAYS`(7일) | 따라잡기에는 몇 분이면 되고 신고 증거는 접수 때 `report_lines`로 복사된다. 원본은 길게 둘 이유가 없다(귓속말은 개인 대화라 짧게). 규모: 접속 100명 x 분당 2건이면 하루 약 29만 건, 7일 약 200만 건(행당 인덱스 포함 수백 바이트, 수백 MB 이내) |
| `report_lines` | 신고가 닫힌(`actioned`/`dismissed`) 뒤 `REPORT_RETENTION_DAYS`(180일). 처리 전 신고는 유지 | 제재 분쟁 확인 |
| `reports`, `account_sanctions` | 삭제하지 않는다 | 제재 이력은 계정 기록. 행 수가 작다 |
| `friendships` 끝난 행 | 90일 | 재요청 쿨다운과 괴롭힘 확인에 필요한 기간 |
| `party_invites` | 7일 | 짧은 알림 기록 |
| `blocks` 해제된 행 | 90일 | 이력 확인 |

정리는 7단계 전까지 4단계의 `purgeExpiredRequestLogs`와 같은 방식으로 서버 프로세스가 매시간 돈다(`server.ts`의 `purge` 타이머에 `purgeChatData`를 더한다). `DELETE ... WHERE id IN (SELECT id ... LIMIT 5000)`을 반복해 한 번에 큰 잠금을 만들지 않는다. `chat_messages_created` 인덱스가 쓰인다. 귓속말 내용이 저장·보관된다는 점은 개인정보 처리방침에 적어야 한다(결정 대기 1).

### 10.2 제재 단계
자동 제재는 **서버가 직접 본 사실**(금칙어, 도배)에만 건다. 신고는 사람이 판단한다.

| 단계 | 조건 | 제재 | 기록 |
|---|---|---|---|
| 서버 즉시 제한 | 간격 미달, 같은 말 반복 | 거절 / `muteSeconds`(10초) 금지 | 기록 없음(메모리) |
| 자동 1 | `CHAT_FILTER_WINDOW_MINUTES`(10) 안에 금칙어 가림·거절 `CHAT_FILTER_STRIKES`(5)회, 또는 반복 금지가 같은 창에서 `CHAT_REPEAT_MUTE_STRIKES`(5)회 | 채팅 금지 `CHAT_AUTO_MUTE_MINUTES`(10분) | `account_sanctions(chat_mute, auto_filter\|auto_spam, system)` |
| 자동 2 | 24시간 안에 자동 금지가 `CHAT_AUTO_ESCALATE_COUNT`(3)번째 | 채팅 금지 `CHAT_AUTO_MUTE_ESCALATED_MINUTES`(1440분) | 같음 |
| 사람 검토 | 그 뒤 반복, 사람이 처리한 신고 | 운영자가 경고·채팅 금지·정지를 정한다 | `account_sanctions(source=admin)` + `reports.state` |
| 신고 누적(선택) | 24시간 안에 서로 다른 신고자 `REPORT_AUTO_MUTE_REPORTERS`명(기본 0 = 꺼짐) | 채팅 금지 `REPORT_AUTO_MUTE_MINUTES`(30분) | `auto_report` |

- 자동 제재는 `ban`을 만들지 않는다. 정지는 항상 사람이 정한다.
- 제재가 생기면 `notify_account_sanction()` 트리거가 `pg_notify('dotrpg_sanction', account_id)`를 보내고, 서버의 전용 LISTEN 연결이 받아 그 계정의 세션 금지 상태를 갱신하며 `sanction` 프레임을 보낸다(`notified_at` 채움). 운영자가 SQL로 제재를 넣어도 같은 경로로 반영된다. `ban`이면 같은 트랜잭션에서 `accounts.banned_until`도 갱신해야 인증 미들웨어가 막는다.
- 채팅 금지 중에는 모든 채널의 `chat.send`가 `MUTED_SANCTION`이다. 받는 것은 막지 않는다. 금지 상태는 `ready.mute`와 `sanction` 프레임으로 알린다.

### 10.3 운영 절차 (7단계 관리자 도구 전)
관리자 화면은 7단계다. 그 전에는 운영자가 SQL로 처리한다(서버 코드가 아니라 문서의 절차).

- 대기 신고 보기: `reports`를 `reports_queue` 순서(`state IN ('open','reviewing') ORDER BY created_at`)로 읽고 `report_lines`를 `report_id`로 붙인다.
- 채팅 금지: `INSERT INTO account_sanctions (account_id, kind, source, reason_code, report_id, ends_at, created_by) VALUES (<id>, 'chat_mute', 'admin', 'abuse', <report id>, now() + interval '1 day', '<운영자>')` 후 `UPDATE reports SET state='actioned', handled_at=now(), handled_by='<운영자>' WHERE id=<report id>`.
- 이용 정지: 같은 트랜잭션에서 `kind='ban'` 행을 넣고 `UPDATE accounts SET banned_until = <시각 또는 'infinity'> WHERE id=<id>`.
- 해제: `UPDATE account_sanctions SET revoked_at=now(), revoked_by='<운영자>' WHERE id=<id>`(ban이면 `accounts.banned_until=NULL`도).
- 위반 없음: `UPDATE reports SET state='dismissed', handled_at=now(), handled_by='<운영자>', note='...'`.
- 이의 제기 절차·신고자 피드백은 이 단계에 없다.

## 11. 서버 정책 상수 (환경변수, 시작값)

채팅·신고의 **게임 쪽 값**(길이, 간격, 반복, 금지 시간, 신고 줄 수, 신고 사유)은 `server/data/chat.json`에서 읽는다(mapping 3절). 아래는 서버 운영·방어 값이다.

| 이름 | 시작값 | 의미 |
|---|---|---|
| `CHAT_INTERVAL_TOLERANCE` | 0.8 | 서버 최소 간격 = `minIntervalSeconds` x 이 값(네트워크 지터 흡수) |
| `CHAT_PER_MINUTE` | 30 | 계정당 분당 채팅 상한 |
| `CHAT_WHISPER_TARGETS_PER_MIN` | 10 | 1분에 말 걸 수 있는 서로 다른 귓속말 상대 수 |
| `CHAT_SHARD_SIZE` | 150 | 일반 채널 방 정원 |
| `CHAT_BACKLOG_GENERAL`, `CHAT_BACKLOG_MINUTES` | 20, 10 | 따라잡기 일반 줄 수, 최대 경과 |
| `CHAT_BACKLOG_PARTY` | 50 | 따라잡기 파티 줄 수 |
| `CHAT_BACKLOG_WHISPER`, `CHAT_BACKLOG_WHISPER_MINUTES` | 50, 30 | 따라잡기 귓속말 줄 수, 최대 경과 |
| `CHAT_RESEND_SECONDS` | 30 | 클라이언트가 못 받은 ack를 재전송하는 한도(`ready.limits`로 알림) |
| `CHAT_FILTER_WINDOW_MINUTES`, `CHAT_FILTER_STRIKES` | 10, 5 | 금칙어 반복 자동 금지 기준 |
| `CHAT_REPEAT_MUTE_STRIKES` | 5 | 같은 창에서 반복 금지 횟수 기준 |
| `CHAT_AUTO_MUTE_MINUTES`, `CHAT_AUTO_ESCALATE_COUNT`, `CHAT_AUTO_MUTE_ESCALATED_MINUTES` | 10, 3, 1440 | 자동 채팅 금지 단계 |
| `CHAT_RETENTION_DAYS` | 7 | `chat_messages` 보관 |
| `WS_HELLO_TIMEOUT_MS` | 5000 | 인증 프레임 대기 |
| `WS_PING_EVERY_MS`, `WS_IDLE_TIMEOUT_MS` | 15000, 45000 | 클라이언트 ping 간격(`ready`로 알림), 유휴 끊김 |
| `WS_TOKEN_GRACE_SECONDS`, `WS_REVALIDATE_SECONDS` | 30, 60 | 토큰 만료 유예, 정지 재확인 간격 |
| `WS_MAX_PAYLOAD_BYTES`, `WS_FRAMES_PER_SEC`, `WS_SEND_QUEUE_MAX` | 4096, 10, 200 | 프레임 크기·빈도·송신 큐 한도 |
| `WS_HANDSHAKE_PER_MIN_IP`, `WS_UNAUTH_PER_IP`, `WS_MAX_CONNECTIONS` | 60, 20, 500 | 연결 방어 |
| `PARTY_POLL_WS_SECONDS` | 15 | WS 연결 중 파티 폴링 안전망 간격(클라이언트 상수, `ready`로 알림) |
| `FRIEND_MAX`, `FRIEND_PENDING_OUT_MAX`, `FRIEND_PENDING_IN_MAX` | 50, 20, 50 | 친구·요청 한도 |
| `FRIEND_REQUEST_DAYS`, `FRIEND_REREQUEST_HOURS` | 14, 24 | 요청 만료, 거절·취소 뒤 재요청 쿨다운 |
| `BLOCK_MAX` | 100 | 차단 한도 |
| `REPORT_PER_HOUR`, `REPORT_PER_DAY` | 5, 20 | 신고 한도 |
| `REPORT_CONTEXT_HOURS`, `REPORT_LOOKBACK_MINUTES`, `REPORT_TARGET_LINES` | 24, 30, 10 | 만난 적 있는 사람 판정 기간, 증거 수집 기간, 대상 줄 수 |
| `REPORT_AUTO_MUTE_REPORTERS`, `REPORT_AUTO_MUTE_MINUTES` | 0(끔), 30 | 신고 누적 자동 금지 |
| `REPORT_RETENTION_DAYS` | 180 | 닫힌 신고 증거 보관 |
| `PARTY_INVITE_SECONDS`, `INVITE_PER_MIN`, `INVITE_PER_TARGET_SECONDS` | 30, 10, 10 | 초대 유효 시간과 쿨다운 |
| `RATE_SOCIAL_IP_MAX` | 600 | `/friends`, `/blocks`, `/reports` IP 버킷(분당) |

## 12. 확장 대비 (PLAN_SERVER §7)

상태를 가진 것은 모두 인터페이스 뒤에 둔다. 시작은 전부 프로세스 메모리 구현이고 확장 1단계에서 구현만 바꾼다.

| 인터페이스 | 하는 일 | 확장 1(Redis, 서버 여러 대) |
|---|---|---|
| `SessionRegistry` | 계정 -> 연결, shard별·파티별 수신자 | 노드마다 자기 연결만 갖고 나머지는 Broadcaster로 |
| `Broadcaster` | 계정·shard·파티로 프레임 전달 | Redis pub/sub |
| `ChatLimiterStore` | 계정별 간격·반복·금칙어·귓속말 상대 집합 | Redis(키 만료) |
| `PresenceStore` | 접속 중 캐릭터, 현재 맵·활동 | Redis(만료 키) |
| `RealtimeNotifier` | `party.changed`, `friends.changed`, 초대, 제재 푸시 | pub/sub로 연결된 노드에 전달. 제재는 이미 PG `LISTEN/NOTIFY`라 여러 노드가 각자 받는다 |
| `ChatWriter` | 직렬 저장 | `pg_advisory_xact_lock`으로 커밋 순서 직렬화(4.5) |
| `RateLimitStore`(기존) | IP·계정 한도 | 이미 교체 가능 |

연결 하나가 서버 한 대에 붙는 것은 변하지 않는다(재접속이 다른 노드로 가도 `since` 커서로 따라잡는다). 서버를 분리하는 확장 2단계에서는 WebSocket 프로세스만 따로 띄우고 같은 인터페이스를 쓴다.

## 13. 5단계 테이블 요약 (`server/migrations/0006_chat_social.sql`)

| 표 | 역할 | 주요 인덱스와 이유 |
|---|---|---|
| `accounts`(확장) | `last_character_id` | 친구 목록의 대표 캐릭터 |
| `chat_messages` | 채팅 기록(쓰기 직렬) | `_general (shard,id)` / `_party (party_id,id)` / `_whisper (recipient_account_id,id)` 따라잡기와 증거, `_sender (sender_account_id,id DESC)` 대상의 최근 줄, `_created` 보관 정리, UNIQUE(`sender_account_id`,`client_msg_id`) 멱등성 |
| `friendships` | 요청·친구 | `_pair_live`(쌍당 하나, 양방향 동시 요청 처리), `_requester`/`_target`(내 목록), `_pair_hist`(쿨다운), `_ended`(정리) |
| `blocks` | 차단 | `_pair_live` 중복 방지, 접속 때 내 차단 목록 |
| `reports` | 신고 | `_open_uq` 중복, `_queue` 운영자 대기열, `_target` 서로 다른 신고자 수, `_reporter` 한도 |
| `report_lines` | 증거 스냅샷 | UNIQUE(`report_id`,`seq`), UPDATE 금지 트리거 |
| `account_sanctions` | 제재 | `_active` 지금 금지인가, `_unnotified` 접속 때 알림, `_history` 단계 올리기, `notify_account_sanction` 트리거 |
| `party_invites` | 파티 초대 | `_pending_uq` 중복, `_invitee` 받는 사람 조회, `_party` 정원 여유 계산, `_created` 정리 |

## 14. PLAN_SERVER와 달라진 점

| 항목 | PLAN_SERVER / PLAN_ONLINE | 이 설계 | 이유 |
|---|---|---|---|
| 일반 채널 범위 | PLAN_ONLINE §2.3 "같은 마을(채널)" | 서버 단일 방(shard, 정원 150 초과 시 분할) | 동시 100명 미만에서 마을별 분리는 대화를 쪼갠다. shard 열은 확장 대비 |
| 소셜 테이블 | PLAN_ONLINE §5.2 `friends / blocks / reports / chat_logs` | `friendships`(요청+관계), `blocks`, `reports`+`report_lines`, `chat_messages`, `account_sanctions`, `party_invites` | 요청 흐름·증거 스냅샷·제재 이력을 따로 두었다. `chat_logs`는 보관 7일 `chat_messages`와 영구 증거 `report_lines`로 나눴다 |
| 4단계 폴링 | PLAN_SERVER §6 "WebSocket: 채팅·매칭 알림·파티 상태" | 알림만 WS 힌트로 이동(폴링 안전망 유지), 하트비트·인계·정산은 REST | 9절 |
| 소셜 경로 | 4단계는 모든 경로가 `/characters/{uuid}/...` | 친구·차단·신고는 계정 단위라 `/friends`, `/blocks`, `/reports`. 파티 초대는 캐릭터 경로 | 친구·차단은 캐릭터를 바꿔도 유지돼야 한다 |
| 데이터 버전 | `/characters*`는 데이터 버전을 검사 | 소셜 경로·WebSocket은 클라이언트 버전만 | 게임 데이터에 의존하는 판정이 없다 |
| 내부 id 비노출 | 프로젝트 규칙 | 채팅 `seq`(= `chat_messages.id`)만 커서로 노출 | 조회 키로 쓰이지 않는다. uuid 열 하나를 더 두는 비용을 피한다 |
| 신고 증거 | PLAN_ONLINE §2.3 "채팅 로그 마지막 20줄 첨부"(클라이언트가 만든다는 전제) | 서버가 접수 순간 직접 수집 | 클라이언트가 보낸 로그는 위조할 수 있다(원칙 §3) |
| 범위 추가 | §9 5단계는 "WebSocket 채널, 귓속말, 친구·차단·신고" | 파티 초대(I1~I3) 포함 | 4단계 12절이 "친구 초대는 5단계"로 넘겼고 현재 `SocialScreen`에 "파티 초대" 버튼이 있다 |
| 게임 데이터 | `server/data/*.json` 목록 | `chat.json` 추가(채팅 한도·신고 사유) | 클라이언트 `ChatRules`와 서버 값이 어긋나지 않게 |

## 15. 결정 대기

| # | 항목 | 선택지 | 권장 |
|---|---|---|---|
| 1 | 채팅 보관과 귓속말 저장 | (a) 7일 보관, 귓속말 포함(증거는 신고 때 복사) (b) 30일 이상 (c) 귓속말은 저장하지 않음(신고 증거에서 귓속말 제외, 귓속말 따라잡기 불가) | (a). 귓속말을 저장하지 않으면 귓속말 욕설·사기 신고를 처리할 근거가 없다. 개인정보 처리방침에 "귓속말은 신고 처리를 위해 7일 보관"을 적어야 한다(고지 문구·법적 요건은 사용자 확인 필요) |
| 2 | 친구·차단의 단위 | (a) 둘 다 계정 단위 (b) 둘 다 캐릭터 단위 (c) 차단만 계정, 친구는 캐릭터 | (a). 캐릭터를 바꿔 차단을 우회하는 것을 막고, 친구 목록이 캐릭터마다 갈라지지 않는다. 대신 친구 목록에는 그 계정이 마지막으로 접속한 캐릭터가 나온다 |
| 3 | 친구 추가 방식 | (a) 요청 -> 수락(상호) (b) 지금 C#처럼 즉시 추가(일방) | (a). (b)는 모르는 사람의 접속 상태·위치를 엿볼 수 있다. (a)면 `SocialScreen`에 "받은 요청 / 보낸 요청" 보기가 하나 늘어난다(mapping 4.3) |
| 4 | 신고 누적 자동 채팅 금지 | (a) 끈다(`REPORT_AUTO_MUTE_REPORTERS=0`), 신고는 사람이 본다 (b) 서로 다른 신고자 3명이면 30분 자동 금지 | (a). 동시 100명 미만에서는 운영자가 직접 볼 수 있고, (b)는 여럿이 짜고 특정인을 신고해 막을 수 있다. 서버가 직접 관측하는 금칙어·도배의 자동 금지는 켜 둔다 |
| 5 | 금칙어 목록 제공 (운영 항목) | `server/data/banned_words.json`이 레포에 없어 지금은 필터가 비어 있다. (a) C# `ChatRules.Banned` 6개를 초기 내용으로 옮기고 운영자가 늘린다 (b) 사용자가 별도 목록을 준다 | (a)로 시작. 서버 최종 판정이므로 출시 전에 목록을 보강해야 한다(우회 감지가 공백·기호를 지우는 방식이라 목록은 정규형 단어만 있으면 된다) |
