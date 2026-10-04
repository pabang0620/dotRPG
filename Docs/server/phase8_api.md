# 서버 8단계 API 명세 (전투 중계 + 필드 파티 사냥)

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §1(전투는 방장 PC, 경제는 서버), §3·§5(보고-판정, 부정행위 방어), §7(확장), [PLAN_ONLINE.md](../PLAN_ONLINE.md) §3.2(동기화). 앞 단계: [phase3_api.md](phase3_api.md)(처치 판정), [phase4_api.md](phase4_api.md)(파티 판, 입장 토큰, 호스트 인계, 12절 전송 규약), [phase5_api.md](phase5_api.md)(WebSocket `/ws`), [phase7_ops.md](phase7_ops.md)(운영). 스키마: `server/schema.sql`, `server/migrations/0009_relay_field.sql`. 게임 값·C# 대응과 클라이언트 변경 지점: [phase8_mapping.md](phase8_mapping.md).
이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만든다. 게임 값(몬스터 경험치·드롭률·리스폰·스폰점 수)은 문서에 복사하지 않고 `server/data/*.json` 이름으로만 참조한다. 본문의 수치는 서버 정책 상수(11절, 환경변수)이거나 판단 근거로 든 "현재 데이터 기준 계산"(6.6절)이다.

**이 문서의 품질 기준**: 시험용이 아니라 출시 품질이다. 보안·남용·끊김 복구·서버 비용·운영(지표, 한도)을 모든 절에서 같이 다룬다. "개발 전용"인 것은 출시 빌드·운영 서버에서 컴파일·기동 단계에서 제외된다(14절).

## 0. 핵심 설계 (먼저 읽기)

1. **전투 연결은 우리 서버 중계가 기본이다.** `/relay`(별도 WebSocket 경로, 바이너리 프레임, 같은 Node 프로세스)가 방 안의 최대 4명 사이에서 바이트를 나른다. 내용은 보지 않는다(라우팅만). PC·모바일·Steam 없는 빌드가 똑같이 동작한다. 4단계의 "방장 PC가 몬스터를 계산한다"는 그대로이고, 서버는 방장이 보낸 것을 멤버에게 복사해 줄 뿐이다.
2. **Steam P2P는 대안 전송이다.** `ISteamNetworkingSockets`(SDR 릴레이)로 서버를 거치지 않는 연결을 쓴다. 전송은 **서버가 판마다 하나를 정하고**(우선순위 `COMBAT_TRANSPORT_ORDER`, 기본 `relay,steam`), 연결에 실패하면 서버가 다음 후보로 바꾼다(자동 전환, 5절). 마지막 후보는 언제나 중계다.
3. **중계는 4단계 호스트 인계를 더 빠르고 안전하게 만든다.** 서버가 방장 연결을 직접 보므로 3초 안에 인계를 시작할 수 있고, 옛 방장의 호스트 전용 프레임은 서버가 막는다(전송 수준의 스플릿 브레인 방지, 4.8절).
4. **필드 파티 사냥은 "필드 세션"이다.** 같은 파티원이 같은 필드 맵에 있으면 한 사람의 PC(호스트)가 그 맵의 몬스터를 계산하고 나머지는 꼭두각시로 본다. 던전과 같은 구조이고 중계 방도 같은 것을 쓴다. 공유 단위는 서버 전체가 아니라 **파티**다(6.1절, 결정 대기 1).
5. **보상은 멤버마다 3단계 `/kills`로 따로 판정한다.** 경험치는 나누지 않고 각자 전부 받으며, 드롭은 개인 굴림이다. 경제가 깨지지 않는 근거는 "몬스터가 같은 맵의 공급량(스폰점 수 / 리스폰 시간)만큼만 있어서 멤버 한 명의 보상 상한이 솔로 상한과 같다"는 것이다(6.6절의 숫자).
6. **클라이언트가 보내는 것은 행동·대상 id·`request_id`뿐이다.** 금액·확률·보상량·시간은 어느 요청에도 없다. 처치 보고는 맵·간격·레벨·화력에 더해 세션 멤버십과 호스트 관찰(대조)로 그럴듯함을 검사한다.
7. **재화가 움직이는 새 경로가 없다.** 이 단계의 새 표는 연결·세션 상태뿐이고 골드·아이템·경험치는 3단계 `/kills`, `/drops/claim`의 원장을 그대로 쓴다(잔액 CHECK·원장 규칙 변경 없음). 날짜 경계(06:00 일일, 목요일 06:00 주간)는 쓰지 않는다. 이 단계의 모든 기간은 "지금부터 N초"이고 서버 시계(`now()`)로만 잰다.

### 0.1 구현 순서 요약 (친구와 가장 빨리 시험하기 위한 순서, 상세 15절)

| 순서 | 내용 | 끝나면 친구와 할 수 있는 것 |
|---|---|---|
| R1 | 중계 서버 + `RelayTransport` + 파티 판을 중계로 | 인터넷으로 던전·레이드를 같이 한다(Steam 불필요, 개발 계정으로 가능) |
| R2 | 필드 세션 + 필드 파티 사냥 | 같은 필드에서 함께 사냥한다 |
| R3 | Steam(인증, P2P 전송, 자동 전환) | Steam 앱 ID 480으로 P2P 시험, 중계와 서로 전환 |
| R4 | 출시 품질 마감(처치 대조 게이트, 지표·알림, 부하 시험) | 출시 점검표 통과 |

R1, R2만으로 친구와 필드·던전 시험이 된다. R3, R4는 그 뒤에 붙이되 **출시 전에는 4개를 모두 끝낸다**(시험 가능 시점과 출시 시점을 헷갈리지 않는다).

## 1. 공통 규칙

1~2단계 0.1~0.4(응답 형식 `{ success, message, data, meta? }` / 실패 `{ success: false, message, errors? }`, 인증, 버전 헤더, 멱등성), 3단계 0.2~0.3(락, `delta`), 4단계 1절(락 순서, 폴링)을 그대로 쓴다. 이 단계에서 달라지는 것만 적는다.

- 새 REST 경로는 전부 `/characters/{uuid}/...` 아래이고 클라이언트·데이터 버전을 둘 다 검사한다. 응답에는 uuid만 싣는다(내부 bigint 키 금지). 예외: 중계 `/relay`는 REST가 아니라 WebSocket이다(4절).
- **락 순서**(4단계 1.1에 한 줄 추가): ① 캐릭터 행(id 오름차순) ② `parties` ③ `party_runs` ④ `field_sessions`. `field_session_members`는 세션 행을 잠근 아래에서만 바꾼다. 필드 요청은 자기 캐릭터 하나만 잠그므로 교착이 없다. 호스트 선출은 멤버 행·`parties.leader_character_id`를 읽기만 한다(다른 캐릭터를 잠그지 않는다).
- 한 캐릭터는 동시에 파티 판(`party_run_members` 활성)과 필드 세션(`field_session_members` 활성)에 있을 수 없다. DB 인덱스가 각각의 유일성을 막고, 둘 사이의 배타성은 서비스가 같은 트랜잭션에서 확인한다(던전 출발은 필드 세션을 먼저 닫는다, 6.3절).
- 새 에러 코드는 엔드포인트 표에 있고 모두 `422` 규칙 위반 / `409` 상태 충돌 / `404` 없음·남의 것 / `503` 일시 불가다. 공통: `400 VALIDATION`, `401 TOKEN_*`, `403 ACCOUNT_BANNED`, `404 CHARACTER_NOT_FOUND`, `422 IDEMPOTENCY_MISMATCH`, `426`, `429 RATE_LIMITED`, `503 MAINTENANCE`(점검 중 새 방·세션·티켓 거절, phase7 4절).

## 2. 엔드포인트 요약 (REST 신규 9개 + WebSocket 1개 + 기존 변경 8곳)

경로는 `/characters/{uuid}` 아래(표에서 생략), 인증은 액세스 토큰이다.

| # | 메서드 | 경로 | 하는 일 | 절 |
|---|---|---|---|---|
| W2 | GET (Upgrade) | `/relay` (전체 경로, 인증은 첫 프레임의 티켓) | 전투 중계 연결 | 4 |
| T1 | POST | `/rooms/{kind}/{room_id}/relay-ticket` | 중계 방 입장 티켓 발급 (`kind` = `run` \| `field`) | 4.3 |
| T2 | POST | `/rooms/{kind}/{room_id}/transport/fallback` | 연결 실패 보고, 다음 전송으로 전환 | 5.3 |
| F1 | POST | `/field-sessions/enter` | 필드 맵 입장 보고: 세션 만들기·합류 | 6.4 |
| F2 | GET | `/field-sessions/{id}` | 세션 상태(폴링 안전망) | 6.4 |
| F3 | POST | `/field-sessions/{id}/heartbeat` | 생존 신호, 호스트·멤버·전송 수신 | 6.4 |
| F4 | POST | `/field-sessions/{id}/leave` | 맵을 떠남(맵 이동·종료) | 6.4 |
| F5 | POST | `/field-sessions/{id}/host/claim` | 호스트 인계 요청 | 6.4 |
| F6 | POST | `/field-sessions/{id}/observe` | 호스트의 기여 처치 관찰 보고 | 6.10 |
| F7 | GET | `/field-session` | 내 현재 세션(재접속 복구) | 6.4 |

기존 변경 8곳(7·8절): E1 `POST /kills`(`session_id`, `monster_ref`), E2 `RunView.transport`와 R1·R3·R5·R6·R2의 변경, E3 `GET /party-runs/{id}`의 `host_key`, E4 `/ws` `hello.caps`와 `field.changed`, E5 `/meta`의 중계·Steam 필드, E6 `POST /auth/steam` 강화(5.5절), E7 파티 서비스 훅(나가기·강퇴·해산이 필드 세션을 정리), E8 던전 출발이 필드 세션을 닫음.

## 3. 전송 선택과 근거 (A-1)

### 3.1 선택: 별도 WebSocket 경로 `/relay` (바이너리), 같은 프로세스

| 후보 | 장점 | 단점 | 판정 |
|---|---|---|---|
| **A. 별도 `/relay` WebSocket, 바이너리** | 모든 플랫폼(PC·모바일·WebGL 이후 가능). 방화벽·프록시(443)를 그대로 통과. TLS는 기존 Caddy가 처리. 기존 `ws` 라이브러리·점검·종료·속도 제한 패턴 재사용. 채팅과 한도·고장 모드를 분리 | TCP 위라 패킷 손실 시 뒤 프레임이 같이 지연(머리 막힘). 완화책 4.6절 | **채택** |
| B. 기존 `/ws` 재사용 | 연결이 하나 | `/ws`는 JSON 텍스트, 페이로드 4096B, 연결당 초당 10프레임 한도(폭주 시 4006 종료), 계정당 연결 1개(캐릭터 하나에 묶임). 전투 트래픽(초당 수십 프레임)을 얹으면 채팅 한도에 걸리거나 한도를 올려 채팅 방어를 약하게 만든다. 느린 소비자 처리(4008)가 채팅과 얽힘 | 기각 (phase5 9절이 하트비트를 REST에 둔 것과 같은 이유: 고장 모드를 섞지 않는다) |
| C. 원시 UDP(`dgram`) | 가장 낮은 지연, 손실 시 머리 막힘 없음 | 신뢰 채널(이벤트·제어)을 직접 구현해야 함. 모바일·가정용 NAT·방화벽에서 UDP 차단 사례. TLS 대신 자체 암호화. WebGL 불가. 서버 구현량이 큼 | 후속(4.6절의 지표가 필요하다고 말하면 선택 전송으로 추가, 같은 `ITransport` 뒤) |
| D. WebTransport/QUIC | 신뢰·비신뢰를 한 연결에서 | Unity 클라이언트 지원이 아직 제한적, 서버(`ws` 외) 의존성 | 후속 |

- **같은 Node 프로세스**(`RELAY_MODE=embedded`). 확장 2단계(PLAN_SERVER §7)에서 `standalone`(별도 프로세스·별도 VM)으로 떼어낼 수 있게 설계한다: 입장 인증은 서명된 티켓만 보면 되고(DB 없이 검증), 방 멤버십 변경은 인터페이스(`RoomDirectory`)로 받는다(임베디드는 함수 호출, 분리 후에는 PG `LISTEN/NOTIFY`로 같은 이벤트). 동시 100명 미만에서는 한 프로세스로 충분하다(4.11절 계산).
- 경로 처리는 `/ws`와 같은 `server.on('upgrade')`에서 `path === '/relay'`일 때만 별도 `WebSocketServer({ noServer: true, maxPayload: RELAY_FRAME_MAX_BYTES, perMessageDeflate: false })`로 넘긴다. 소켓마다 `setNoDelay(true)`(Nagle 끔). 클라이언트 IP는 `/ws`와 같은 `clientIp()`(`trust proxy` 규칙)를 쓴다.
- 인터페이스(상태를 가진 것은 전부 뒤에 둔다, phase5 12절과 같은 방식): `RelayRegistry`(방·연결), `RoomDirectory`(방 멤버십 질의·이벤트), `RelayTicketStore`(티켓 단일 사용), `RelayRateStore`(핸드셰이크 한도, 기존 `RateLimitStore` 재사용).

### 3.2 개발 전송은 출시물에 없다
`UdpTransport`(같은 PC 창 2개)는 개발 빌드(`UNITY_EDITOR || DEVELOPMENT_BUILD`)에만 컴파일된다. 서버는 `NODE_ENV=production`에서 `dev` 전송을 거부한다(14절 G8).

## 4. A. 전투 중계 `/relay`

### 4.1 방(room)
- 방 종류 두 가지: `run`(4단계 파티 판, 방 id = `party_runs.uuid`), `field`(6절 필드 세션, 방 id = `field_sessions.uuid`).
- **방의 멤버십 권한은 DB다.** 방은 첫 입장 요청이 올 때 서버 메모리에 만들어지고, 입장 자격은 `party_run_members`(`invited | joined | playing | disconnected`) 또는 `field_session_members`(`state <> 'left'`)로 정한다. 중계는 멤버십을 따로 저장하지 않는다(4.9절: 서버가 재시작해도 방이 DB에서 다시 만들어진다).
- 좌석(seat) 0..3: `run`은 `party_run_members.slot`, `field`는 `field_session_members.seat`. **좌석이 곧 피어 번호**이고 `ITransport`의 `from`/`to`와 PartyNet 슬롯이다(mapping 3절). 서버가 모든 프레임에 보낸 사람의 좌석을 도장 찍으므로 클라이언트가 보낸 사람을 속일 수 없다(UDP 개발 전송에서 필요했던 `OwnsSlot`·입장 토큰 검증이 중계에서는 서버 몫이다).
- 한 방의 최대 연결 4(`humans` 최대 4, AI는 연결이 아니다). 한 좌석은 연결 하나, 같은 좌석의 새 연결은 옛 연결을 `4001 REPLACED`로 끊는다(새 연결의 티켓이 유효할 때만).
- 방은 마지막 연결이 끊긴 뒤 `RELAY_ROOM_EMPTY_GRACE_SECONDS`(30)가 지나거나, 판이 `ended/cancelled`·세션이 `ended`가 되면 닫힌다. 닫힐 때 `relay_room_stats` 한 행을 쓴다(대역폭 요약, 내용 없음).

### 4.2 서버가 보지 않는 것
**페이로드(내용)를 읽지 않는다.** 서버가 아는 것은 채널 번호, 길이, 보낸 좌석, 받을 좌석, 시각뿐이다. 몬스터 위치·피해·카드(MemberCard) 같은 내용은 해석하지도, 로그에 남기지도 않는다(로그에는 프레임 수·바이트 수·종료 코드만). 라우팅 규칙(누가 누구에게 어느 채널로 보낼 수 있는가)은 내용이 아니라 헤더로만 적용한다(4.5절). 전투 결과의 진실은 4단계 그대로 서버가 처치 보고·대조로 정한다(중계가 할 일이 아니다).

### 4.3 인증: 액세스 토큰에서 방 멤버십 티켓으로 (T1)

접근 토큰(15분 JWT)은 "이 계정이다"만 말하고 어느 방에 들어갈 자격이 있는지는 말하지 않는다. 그래서 **짧은 단일 사용 티켓**을 따로 발급한다.

**T1 `POST /characters/{uuid}/rooms/{kind}/{room_id}/relay-ticket`**
- 요청: 본문 없음(`{}`, `.strict()`). 받지 않는 값: 좌석, 호스트, 시각.
- 처리: 내 캐릭터 소유 확인 → 방 조회(`kind=run`이면 `party_runs.uuid`, `field`면 `field_sessions.uuid`) → 내가 그 방의 활성 멤버(4.1)인가(아니면 `404 ROOM_NOT_FOUND`, 남의 방인지 없는 방인지 구분하지 않는다) → 방이 끝났으면 `409 ROOM_CLOSED` → 방의 현재 전송이 `relay`인가(아니면 `409 TRANSPORT_NOT_RELAY`, `errors.current`에 현재 전송, 클라이언트는 그 전송으로 간다) → 중계가 켜져 있고 수용 한도 안인가(아니면 `503 RELAY_UNAVAILABLE` + `Retry-After`) → 티켓 서명.
- 응답 `200` `data`:
```
{ "relay_url": "wss://<host>/relay", "ticket": "<base64url>", "expires_in": 60,
  "room": { "kind": "run", "id": uuid, "transport": { "current": "relay", "epoch": 1 },
            "seat": 1, "host_seat": 0, "host_epoch": 1,
            "members": [ { "seat": 0, "character_id": uuid, "state": "playing" } ] } }
```
- **티켓 내용**(HS256, 비밀 `RELAY_TICKET_SECRET`은 `JWT_SECRET`과 다른 값): `{ v:1, jti, aud:"dotrpg-relay", sub:<계정 uuid>, cid:<캐릭터 uuid>, k:<kind>, rid:<방 uuid>, seat, ep:<transport_epoch>, iat, exp(= iat + RELAY_TICKET_TTL_SECONDS 60) }`. 호스트 좌석·세대는 티켓에 넣지 않는다(바뀌므로 방 상태가 정한다).
- **단일 사용**: 중계가 `hello`에서 `jti`를 `RelayTicketStore`(메모리, 2분 보관, 인터페이스 뒤)에 소비하고 이미 있으면 `4004`로 끊는다. 재접속은 새 티켓(T1)을 받아 한다.
- 왜 액세스 토큰을 `hello`에 바로 쓰지 않는가: ① 15분짜리 범용 토큰이 중계 서버(장차 분리될 프로세스)에 퍼지는 것을 피한다 ② 방·좌석·전송 세대에 묶여 있어 다른 방·다른 전송 세대에 쓸 수 없다 ③ 접속 로그·프록시에 남아도(URL에는 넣지 않는다) 60초 뒤 무효다 ④ 분리된 중계 프로세스가 DB 없이 검증한다. 계정 정지는 기존 `dotrpg_sanction` LISTEN으로 중계 연결도 즉시 끊는다.
- 속도 제한: 캐릭터당 10초에 3회, 분당 10회. 멱등성: 발급 자체는 재화와 무관하고 티켓이 단일 사용이라 `request_id`를 받지 않는다.

### 4.4 연결 프로토콜

한 WebSocket 연결에서 **텍스트 프레임 = 제어(JSON, 드물다)**, **바이너리 프레임 = 데이터(초당 수십 개)**다.

**제어 프레임**(모두 `{ "t": "<type>", ... }`, 키 snake_case, `.strict()` 검증, 알 수 없는 `t`는 `error BAD_FRAME`, 10초 안 3회면 4005):

| 방향 | t | 필드 | 의미 |
|---|---|---|---|
| C->S | `hello` | `v`(1), `ticket`, `client_version`, `wire`(PartyNet 와이어 버전 정수), `resume`(bool) | 첫 프레임. `RELAY_HELLO_TIMEOUT_MS`(5초) 안에 없으면 4007 |
| S->C | `ready` | `v`, `server_time`, `room:{kind,id,transport_epoch}`, `seat`, `host_seat`, `host_epoch`, `peers:[seat]`, `limits:{...}`, `ping_every_ms`(2000), `idle_timeout_ms`(6000), `flush_ms`(20) | 입장 성공. 이 시점의 방 상태 전체 |
| S->C | `peer.joined` | `seat` | 다른 좌석이 연결됨 |
| S->C | `peer.left` | `seat`, `reason`: `closed` \| `timeout` \| `replaced` \| `removed` | 다른 좌석이 사라짐 |
| S->C | `host.changed` | `seat`, `epoch` | 호스트가 바뀜(서버가 정한다, 4.8절). 새 호스트 본인도 이것을 받고 호스트 노릇을 시작한다 |
| S->C | `transport.changed` | `current`, `epoch` | 방의 전송이 바뀜(5.3절). 곧이어 `bye(4014)` |
| S->C | `room.closed` | `reason` | 판·세션이 끝남. 곧이어 `bye(4012)` |
| S->C | `error` | `ref?`, `code`, `message` | 프레임 오류(`BAD_FRAME`, `NOT_AUTHENTICATED`) |
| S->C | `bye` | `code`, `reason`, `reconnect`(bool), `retry_after_ms?` | 서버가 끊기 직전. 끊는 모든 경우에 먼저 보낸다 |
| C->S | `leave` | 없음 | 정상 이탈(좌석 반납, `peer.left reason=closed`) |

`hello` 검사 순서(첫 실패에서 중단): ① 프로토콜 `v` ② `client_version >= MIN_CLIENT_VERSION`(아니면 4426) ③ 티켓 서명·`aud`·`exp`·`jti` 미사용(아니면 4004) ④ 점검·종료 중이면 4016(`reconnect:true`) ⑤ 계정 정지(4003) ⑥ `RoomDirectory`에서 방·멤버십 재확인(티켓 발급 뒤 강퇴·해산됐으면 4011/4012) ⑦ 방의 전송 세대 `ep`가 티켓과 같은가(아니면 4014) ⑧ `wire`가 방의 다른 연결과 같은가(아니면 `4015 WIRE_MISMATCH`, 서로 다른 빌드가 같은 방에서 바이트를 잘못 해석하는 사고 방지) ⑨ 같은 좌석의 옛 연결 대체 ⑩ 방 수용·연결 한도(4016) ⑪ `ready` 송신, 다른 좌석에 `peer.joined`, `RoomDirectory.peerConnected`(4.8절 자동 입장).

**데이터 프레임**(바이너리, 정수는 리틀 엔디언 = C# `BinaryWriter`와 같다). 첫 바이트가 종류다.

| type | 이름 | 구조 | 설명 |
|---|---|---|---|
| `0x02` | PING | `[0x02][nonce u32]` | C->S 2초마다. 서버가 같은 값으로 PONG |
| `0x03` | PONG | `[0x03][nonce u32]` | S->C. 클라이언트가 왕복 시간(RTT)을 잰다(HUD 핑 막대, PLAN_ONLINE §2.1-5) |
| `0x04` | BATCH | `[0x04][count u8]` 뒤에 `count`개의 `[channel u8][addr u8][len u16][payload]` | 한 프레임에 여러 패킷. 클라이언트도 서버도 한 번에 한 번(최대 `RELAY_FLUSH_MS`마다) 묶어 보낸다 |

- `addr`: C->S에서는 **받을 좌석**(0..3) 또는 `0xFF`(방 전체, 호스트만), S->C에서는 **보낸 좌석**(서버가 도장). 서버 자신이 보내는 데이터 패킷은 없다(제어는 텍스트로만).
- 단일 패킷도 BATCH `count=1`로 보낸다(형식이 하나라 파서가 단순하다). 한 BATCH의 총 길이 `RELAY_FRAME_MAX_BYTES`(8192) 이하, 한 패킷 페이로드 `RELAY_PACKET_MAX_BYTES`(6144) 이하(Welcome에 실리는 멤버 카드 3장이 약 2KB라 여유를 둔다).
- 왜 묶는가: 개별 WebSocket 메시지는 TLS·TCP·WS 헤더로 약 60바이트를 더한다. 멤버는 입력 20Hz + 상태 20Hz를 보내므로 묶지 않으면 업링크의 대부분이 헤더다. 20ms 묶음이면 지연은 최대 20ms(평균 10ms) 늘고 프레임 수는 1/4 이하다(4.11절 계산).

### 4.5 채널과 라우팅 규칙 (헤더만 본다)

채널 번호는 `NetChannel`(Transport.cs)과 같다. 서버는 채널별 **허용 방향**과 **신뢰 등급**만 안다.

| ch | 이름 | 허용 방향 | 신뢰 등급 | 설명 |
|---|---|---|---|---|
| 1 | Input | 멤버 -> 호스트 | 신뢰(순서 유지) | 이동·버튼 명령 20Hz. 버튼(공격) 하나를 잃으면 안 되므로 **Input을 신뢰 채널로 올린다**(`NetChannel.IsReliable` 변경, mapping 3절). Steam에서도 신뢰 |
| 2 | Chat | 중계 방에서 **금지** | - | 채팅은 `/ws`(phase5)다. 받으면 버리고 위반 1회 |
| 3 | Presence | 중계 방에서 **금지** | - | 마을 유령(NetPresence)은 이 단계 범위 밖(14절) |
| 4 | Snapshot | 호스트 -> 멤버(방 전체) | 비신뢰, 최신 우선 | 10Hz 위치·HP 스냅샷 |
| 5 | Event | 호스트 -> 멤버, 멤버 -> 호스트 | 신뢰(순서 유지) | 스폰·사망·행동·피해·방 이동, 멤버의 회복·카드 갱신 |
| 6 | Control | 양방향 | 신뢰(순서 유지) | 환영(Welcome)·인사(Hello)·호스트 변경 알림 |
| 7 | MemberState | 멤버 -> 호스트 | 비신뢰, 최신 우선 | 20Hz 자기 위치·방향·MP |

- **별 모양 강제**: 멤버가 보낼 수 있는 `addr`는 현재 호스트 좌석뿐이다. 멤버 -> 멤버, 멤버의 `0xFF`는 버린다(악의적 멤버가 다른 멤버에게 가짜 호스트 이벤트를 보내 화면을 망치거나 클라이언트를 크래시시키는 경로를 닫는다).
- **호스트 전용 채널은 현재 호스트에게서만 받는다**: Snapshot(4)은 `from == host_seat`일 때만 전달한다. 인계가 일어난 뒤 옛 호스트가 계속 보내는 스냅샷은 서버가 버린다(전송 수준의 스플릿 브레인 방지). Event(5)·Control(6)의 호스트 -> 멤버 방향도 같다.
- 위반(금지 채널, 멤버 -> 멤버, 호스트 아닌 좌석의 호스트 전용 채널, 범위 밖 좌석)은 프레임을 버리고 **연결당 분당 `RELAY_STRIKES_PER_MIN`(20)회**를 넘으면 `4006 FLOOD`로 끊고 `anomaly_log(relay_abuse, 2)`를 남긴다. 호스트 인계 직후의 옛 좌석 주소(`addr`가 옛 호스트)는 정상 지연이라 위반으로 세지 않는다(그냥 버림).

### 4.6 신뢰·비신뢰, 스냅샷, 큐 백로그

WebSocket은 TCP 위라 모든 프레임이 순서대로 신뢰적으로 간다. "비신뢰" 채널은 **보낼 필요 없는 오래된 값을 버리는 것**으로 구현한다.

- **송신 큐 두 줄**(서버의 연결마다, 클라이언트의 `RelayTransport`도 같은 규칙):
  - 신뢰 줄: 먼저 들어온 순서대로 보낸다. 한도(`RELAY_RELIABLE_QUEUE_BYTES` 256KB 또는 가장 오래된 항목 나이 `RELAY_RELIABLE_QUEUE_AGE_MS` 5초)를 넘으면 느린 소비자로 보고 `4008 SLOW_CONSUMER`로 끊는다. 지연된 전투를 계속 보게 하는 것보다 끊고 재접속해 재동기화(4.9절)하는 편이 낫다.
  - 비신뢰 줄: 키 `(보낸 좌석, 채널)`마다 **가장 최근 패킷 하나만** 보관한다(새 스냅샷이 오면 보내지 못한 옛 스냅샷을 덮어쓴다, `dropped_unreliable` 증가).
- **보내는 시점**: `RELAY_FLUSH_MS`(20ms, 50Hz) 틱마다 연결별로 신뢰 줄을 먼저, 이어서 비신뢰 줄의 최신 값을 BATCH 하나에 담아 보낸다(Event의 방 전환이 같은 틱의 스냅샷보다 앞서므로 기존 PartyNet의 "방이 다르면 스냅샷 무시" 규칙과 어긋나지 않는다). `ws.bufferedAmount > RELAY_BACKPRESSURE_BYTES`(32KB)이면 그 틱은 비신뢰 줄만 보류하고 신뢰 줄은 위 한도로 판정한다.
- **스냅샷은 호스트 -> 방 전체 브로드캐스트 한 번**으로 올라오고 서버가 멤버 수만큼 복사한다(호스트 업링크가 멤버 수에 곱해지지 않는다. Steam P2P에서는 호스트가 멤버마다 따로 보내야 해서 3배다).
- 순서·중복: 서버는 번호를 붙이지 않는다. 스냅샷은 자체 틱 번호가 있고(`PartyNet` `snapTick`) 멤버가 늦은 것을 버린다(기존 동작).
- 머리 막힘(TCP): 손실 구간에서 신뢰·비신뢰 모두 RTT 정도 멈출 수 있다. 10Hz 스냅샷과 클라이언트 보간(100~150ms 버퍼)으로 흡수되는 범위이고, 서버는 `relay_room_stats.rtt_p95_ms`, 재접속률, `4008` 비율을 지표로 모은다(4.12절). **이 지표가 "정직한 플레이어에게 끊김이 보인다"고 말하면** 후보 C(UDP)를 선택 전송으로 추가한다(결정 근거를 측정으로 둔다, 결정 대기 아님).

### 4.7 한도 (모두 환경변수, 11절)

**연결 단계**(IP 기준, `/ws`와 같은 방식, 메모리·`RateLimitStore`)

| 대상 | 한도 | 근거 |
|---|---|---|
| 핸드셰이크 시도 | IP당 `RELAY_HANDSHAKE_PER_MIN_IP`(30)/분 | 정상은 방 입장마다 1회. 서버 재시작 직후 재접속 폭주는 `bye.retry_after_ms`와 클라이언트 백오프가 흩는다 |
| 인증 전 소켓 | IP당 `RELAY_UNAUTH_PER_IP`(10) | `hello` 없이 소켓만 쌓는 공격 |
| 같은 IP의 인증된 연결 | `RELAY_CONN_PER_IP`(8) | PC방·공유기 여러 명을 허용 |
| 전체 연결 | `RELAY_MAX_CONNECTIONS`(400) | `/ws`의 `WS_MAX_CONNECTIONS`(500)와 별도 풀. 둘 다 같은 프로세스 메모리를 쓰므로 합이 서버 메모리 안에 들어야 한다(부하 시험 후 확정) |
| 방 수 | `RELAY_MAX_ROOMS`(120) | 방당 최대 4연결 |
| 수용 판단 | 연결·방이 한도의 `RELAY_ADMISSION_RATIO`(0.9)를 넘으면 새 티켓은 `503 RELAY_UNAVAILABLE`, 5.1절의 우선순위가 `steam`을 허용하면 새 방은 Steam으로 보낸다 | 과부하 시 서비스 전체가 아니라 새 입장만 늦춘다 |

**연결 후**(좌석 기준, 토큰 버킷)

| 대상 | 한도 | 위반 시 |
|---|---|---|
| 패킷 수 | 호스트 초당 `RELAY_HOST_PKT_PER_SEC`(300, 버스트 600), 멤버 `RELAY_MEMBER_PKT_PER_SEC`(120, 버스트 240) | 초과분 버림, 분당 20회 초과 시 4006 |
| 바이트 | 호스트 `RELAY_HOST_BYTES_PER_SEC`(128KB), 멤버 `RELAY_MEMBER_BYTES_PER_SEC`(32KB), 버스트 4배 | 같음 |
| 프레임 크기 | `RELAY_FRAME_MAX_BYTES`(8192), 패킷 `RELAY_PACKET_MAX_BYTES`(6144) | `1009` 끊김 |
| 방 전체 송신 | `RELAY_ROOM_EGRESS_BPS`(256KB/s). 정상 4인 방은 약 44KB/s(4.11절)라 6배 여유 | 초과 시 비신뢰 줄부터 버리고 지속되면 `4006`(버그·남용 방어) |
| 신뢰 줄 | 4.6절 | 4008 |
| 정상 값 | 호스트 초당 패킷 약 25, 멤버 약 40(묶기 전), 바이트는 위의 5% 안팎 | 한도는 정상의 5~10배 |

### 4.8 끊김 감지와 4단계 호스트 인계

**감지 신호 두 겹**

| 신호 | 누가 | 속도 | 용도 |
|---|---|---|---|
| 중계 연결 상태 | 서버 `ws` `close`/`error`, PING 무응답 `RELAY_IDLE_TIMEOUT_MS`(6초 = PING 3회), 서버 프로토콜 ping 10초(반쯤 끊긴 소켓) | 정상 종료·RST는 즉시, 무응답은 6초 | 빠른 경로(전투 연결 자체의 생존) |
| REST 하트비트(4단계 R5 / F3) | 클라이언트 5초마다 | 12초(`HOST_STALE_SECONDS`) | 게임 스레드 생존(연결은 살아 있는데 PC가 멈춘 경우), Steam·dev 전송, 중계의 백업 |

**중계 연결이 사라졌을 때 서버가 하는 일**(`RoomDirectory.peerLost(room, seat, cause)`를 서버 서비스가 받는다)
1. 다른 연결에 `peer.left`(reason `closed`/`timeout`).
2. 사라진 사람이 **멤버**면: 4단계 규칙 그대로 `disconnected`(60초 `PARTY_REJOIN_SECONDS` 안에 돌아오면 `playing`, 넘으면 `left`). 중계가 먼저 알려 주므로 12초를 기다리지 않고 바로 `disconnected`로 바뀐다. 다른 멤버 화면의 "연결 끊김 - AI가 대신 싸우는 중"은 `peer.left`로 즉시 뜬다.
3. 사라진 사람이 **호스트**면: `RELAY_HOST_GRACE_MS`(3초) 동안 기다린다(Wi-Fi 순간 끊김을 흡수). 돌아오지 않으면 **서버가 직접 인계한다**: 4단계 R6의 승인 조건 4번(가장 작은 활성 좌석)을 서버가 판정해 `host_epoch + 1`, 옛 호스트를 `disconnected`로 하고 모든 연결에 `host.changed { seat, epoch }`를 보내고 `/ws`로 `party.changed(run)`(필드는 `field.changed`)을 푸시한다. 후보가 없으면 필드는 호스트 없음(`host_character_id = NULL`), 던전은 4단계 규칙(전원 이탈 처리)이다.
4. 새 호스트 PC는 `host.changed`에서 자기 좌석이 호스트이면 4단계 `TakeOver`(마지막 스냅샷부터 몬스터를 이어받는다)를 한다. 전송은 새로 맺지 않는다(이미 같은 방에 연결돼 있다. Steam이면 새 리슨 소켓 + 재연결, 4단계 6.7).
5. 옛 호스트가 돌아오면(`resume`) 티켓의 좌석은 그대로이지만 `ready.host_seat`가 자기가 아니다. 클라이언트는 호스트 권한을 즉시 내려놓고 멤버로 재동기화한다(4단계 R5의 `host_changed` 규칙과 같다). 서버는 그동안 옛 호스트의 호스트 전용 프레임을 버렸으므로 멤버 화면이 오염되지 않았다.

**4단계 R6(`host/claim`)과의 관계**: 그대로 남는다. 서버 주도 인계가 있는 중계에서는 보통 필요 없지만(멤버가 먼저 알아채도 서버가 이미 정한다), Steam·dev 전송과 하트비트 12초 경로에서는 여전히 이 API가 인계를 요청한다. 승인 조건 3번에 "중계 방이고 호스트 연결이 `RELAY_HOST_GRACE_MS` 이상 없다"가 **추가**된다(하트비트가 신선해도 호스트 연결이 없으면 죽은 것으로 본다. 반대로 중계에 호스트가 붙어 있으면 `409 HOST_ALIVE`).

**소요 시간 비교**: 호스트 PC가 죽거나 네트워크가 끊겼을 때 4단계(하트비트 12초 + 클레임)는 약 12~15초, 중계는 RST면 약 3초(grace), 무응답이면 약 6~9초.

### 4.9 재접속과 재동기화
- 연결이 끊기면 클라이언트는 T1로 새 티켓을 받아 `hello(resume:true)`로 같은 좌석에 다시 붙는다(백오프 0.5, 1, 2, 4, 8초 x 0.8~1.2 난수, 60초 이상 안정적이면 초기화). 티켓 발급이 `409 ROOM_CLOSED`면 판·세션이 끝난 것이다. `503`은 `Retry-After`를 따른다.
- 서버는 끊긴 동안의 프레임을 **쌓아 두지 않는다**(보내려던 프레임은 버린다). 재접속 뒤 상태는 **PartyNet이 다시 맞춘다**: 멤버가 `Hello`를 다시 보내고 호스트가 `Welcome`과 살아 있는 몬스터의 `EnemySpawn`을 다시 보낸다(`PartyNet.Host.OnHello`가 이미 반복 인사를 처리한다, 멱등). 클라이언트 쪽 변경은 mapping 6절(`welcomed` 초기화).
- 호스트가 재접속하는 경우는 4.8절 5번(호스트 권한 상실 가능).
- 서버 재시작(배포·점검): 전체 연결에 `bye(1001, reconnect:true, retry_after_ms)`. 방 상태는 DB에서 다시 만들어지고 호스트 PC가 전투 상태를 들고 있으므로 **진행 중인 판·세션이 서버 재시작을 견딘다**(REST 처치 보고는 짧게 실패하고 재시도). 재시작 직후 몰리는 재접속은 `retry_after_ms`(3~8초 난수)와 핸드셰이크 한도가 흩는다.

### 4.10 보안·남용 요약
| 위협 | 방어 |
|---|---|
| 남의 방 입장 | 티켓은 방·좌석·전송 세대에 묶이고 단일 사용, DB 멤버십 재확인(4.4 6번) |
| 티켓 탈취·재사용 | 60초 수명, `jti` 단일 사용, TLS 필수(`wss`, 평문 `ws`는 개발 빌드만) |
| 보낸 사람 위조 | 서버가 `from` 좌석을 도장 |
| 멤버가 다른 멤버를 공격(가짜 이벤트) | 별 모양 강제, 호스트 전용 채널은 현재 호스트에게서만 |
| 중계를 임의 데이터 프록시로 악용 | 방 안의 4좌석 사이로만 라우팅, 외부 주소 없음, 채널 화이트리스트, 크기·속도 한도 |
| 대역폭 폭주(비용 공격) | 연결·방·방 전체 송신 한도(4.7), 한도 초과 방은 닫는다, 일일 송신 예산 알림(4.12) |
| 오래된 빌드가 섞임 | `wire` 불일치 4015 |
| 방장이 호스트 인계를 막고 멤버를 가둠 | 서버가 호스트 연결·하트비트로 판정, 방장의 PC가 아니라 서버가 인계 |
| 정지된 계정 | `sanction` LISTEN으로 연결 즉시 종료, 새 티켓 거부 |
| 로그 노출 | 페이로드·티켓 원문을 로그에 남기지 않는다(티켓 `jti` 앞 8자만) |

### 4.11 서버 비용 추정 (4인 방, 스냅샷 10Hz)

**가정**(코드에서 확인한 값): 스냅샷 한 개 = 헤더 약 7B + 멤버 4 x 18B + 몬스터 n x 23B(`BuildSnapshot`). 필드는 숲 스폰점 15개 -> 최대 약 350B, 던전 방은 약 25마리 -> 약 650B. 여유를 두어 **스냅샷 1.0KB, 이벤트 스트림 2KB/s**(피해 숫자·스폰·사망·행동)로 계산한다. 멤버 업링크는 입력 20Hz x 14B + 상태 20Hz x 24B 약 0.8KB/s. 묶지 않으면 프레임당 약 60B의 TLS·TCP·WS 헤더가 붙고, 20ms 묶음이면 이 헤더가 1/4 이하다.

| 구간 | 계산 | 초당 |
|---|---|---|
| 호스트 -> 서버 | 스냅샷 10 x 1.0KB + 이벤트 2KB + 헤더(묶음 50프레임/s x 60B 3KB 상한, 실제 약 1.5KB) | 약 13.5KB |
| 멤버 3명 -> 서버 | 3 x (0.8KB + 묶음 헤더 1.2KB) | 약 6KB |
| **서버 수신 합계** | | **약 19.5KB/s** |
| 서버 -> 멤버 3명 | 3 x (스냅샷 10KB + 이벤트 2KB + 헤더 1.5KB) | 약 40.5KB |
| 서버 -> 호스트 | 멤버 입력·상태 3 x 0.8KB + 묶음 헤더 1.2KB | 약 3.6KB |
| **서버 송신 합계(과금 대상)** | | **약 44KB/s ≈ 0.35Mbps** |

- 플레이어 한 시간당 송신: 44KB/s ÷ 4명 x 3600초 ≈ **40MB**(2인 방은 약 7.4KB/s/인 ≈ 27MB). 호스트 업링크는 13.5KB/s ≈ 0.11Mbps, 멤버 다운링크 약 13.5KB/s ≈ 0.11Mbps(모바일 데이터에도 부담이 작다).
- **동시 100명(4인 방 25개)**: 수신 약 0.49MB/s, 송신 약 1.1MB/s(8.8Mbps 피크 근처). 한 달 계산: 100명 x 하루 2시간 x 30일 = 6000 플레이어 시간 x 40MB ≈ **240GB/월 송신**. 클라우드 송신 요금 단가 0.01~0.09달러/GB 가정이면 월 2.4~22달러이고 VPS 대부분이 월 1TB 이상을 포함한다. 비용 단위 추정이며 업체가 정해지면 단가만 바꾼다(S3 미정).
- **CPU**: 방 하나가 초당 약 70회 수신, 약 125회 송신(묶음 기준) = 약 200 `ws` 연산. 25방 = 5000회/s로 `ws`가 감당하는 범위(코어 하나의 20% 안팎, 부하 시험에서 확정). 100방(400명)이면 코어 하나에 근접하므로 `RELAY_MODE=standalone` 분리를 검토한다(S4: 초반 100명 미만이라 해당 없음).
- **줄이는 수단**(필요해질 때, 지금은 쓰지 않는다): 몬스터 위치를 반정밀도 + 변화분만 보내기(스냅샷 약 50%), 멀리 있는 몬스터 갱신 주기 늘리기, 방 내 압축. 전부 클라이언트 와이어 변경이라 `wire` 버전을 올린다.
- **Steam P2P를 쓰면** 서버 송신이 0이다(REST만). 우선순위를 `steam,relay`로 바꾸는 것이 비용 절감 레버이고 중계가 과부하일 때 새 방을 Steam으로 보내는 것(4.7)이 자동 완충이다.
- 지연: 서버 경유 왕복은 (클라이언트 -> 서버 + 서버 -> 클라이언트) 편도 두 번 + 묶음 평균 10ms. 서버가 플레이어와 같은 지역(국내 가정)이면 편도 10~30ms라 +40~70ms 수준이다. 방장 권위 전투는 100ms 스냅샷 보간을 쓰므로 체감 범위 안이다(결정 대기가 아니라 측정 대상: 4.12의 `rtt_p50/p95`). 서버 위치는 S3(업체 결정)에서 정해지며 지역이 멀면 Steam P2P 우선이 지연에 유리할 수 있다.

### 4.12 운영 지표·알림 (phase7_ops.md 8절에 더한다)

`ops.snapshot`(60초 로그)에 **relay 그룹**을 더한다: `rooms`, `conns`(`max`와 함께), 지난 1분 `bytes_in/out`, `frames_in/out`, `dropped_unreliable`, 종료 코드별 수(`4008`, `4013`, `4006`, `4004`, `1006`), 재접속 수, 티켓 거절 사유별 수(`expired`, `replayed`, `room_closed`, `wire`), 방 RTT p50/p95(PING 왕복 측정값을 서버가 집계), 플러시 틱 지연 p99(20ms 틱이 얼마나 늦는가), 필드 세션 수·호스트 인계 수·세션 평균 인원, 전송 전환 수(`transport.fallback`).

| 등급 | 조건(시작값) | 의미 |
|---|---|---|
| 경고 | `conns >= RELAY_MAX_CONNECTIONS`의 80% 또는 방 수 80% | 용량. Steam 우선 전환·분리 검토 |
| 경고 | `SLOW_CONSUMER`(4008) 종료 > 10/분 또는 `PEER_TIMEOUT`(4013) 비율이 종료의 30% 초과 | 네트워크 품질·서버 부하 |
| 경고 | 플러시 틱 지연 p99 > 60ms 또는 이벤트 루프 지연 p99 > 200ms(기존) | 중계가 서버를 따라가지 못함 |
| 경고 | 지난 24시간 송신 > `RELAY_EGRESS_DAILY_BUDGET_GB`의 80% | 비용(예산은 업체 결정 후 설정) |
| 경고 | 방 RTT p95 > 250ms가 10분 지속 | 지역·경로 문제 |
| 경고 | `relay_abuse` 이상 기록이 10분에 5건 이상 | 남용 시도 |
| 경고 | 전송 전환(`steam -> relay`)이 10분에 5건 이상 | Steam 경로 장애(SDR 상태) |
| 긴급 | `RELAY_UNAVAILABLE` 응답이 연속 5분 | 중계 정지 |

`GET /health/ready`(phase7 8.1)에 `relay`(리스너 등록됨, 종료 중 아님)를 더한다. 로그 이벤트 이름: `relay.room_opened`, `relay.room_closed`(요약 포함), `relay.conn_closed`(코드), `relay.ticket_rejected`, `field.session_opened`, `field.session_ended`, `field.host_changed`, `transport.fallback`. 정합성 점검 I4(phase7 8.4)에 "마지막 하트비트가 2시간 넘은 활성 필드 세션"을 더한다.

## 5. 전송 선택(중계 / Steam P2P)과 자동 전환

### 5.1 서버가 정한다: `pickTransport`

전송은 클라이언트가 고르지 않고 **서버가 판(run)·세션(field)마다 하나** 정한다. 방 안의 모든 멤버가 같은 전송을 써야 하기 때문이다.

설정 `COMBAT_TRANSPORT_ORDER`(쉼표 목록, 기본 `relay,steam`)에서 차례로 후보를 검사해 **자격이 되는 것만** 남긴 것이 그 방의 `transport_order`(DB에 고정 저장), 첫 번째가 `transport`다.

| 후보 | 자격 조건 |
|---|---|
| `relay` | `RELAY_ENABLED`이고 수용 한도 안(4.7). 모바일·Steam 없는 계정 포함 모든 멤버가 쓸 수 있다 |
| `steam` | `STEAM_P2P_ENABLED`이고 **모든 사람 멤버가** ① `auth_identities`에 Steam 연결이 있고(`steam_id` 존재) ② 접속 중인 `/ws` 세션의 `hello.caps.steam_p2p == true`(클라이언트가 Steamworks 초기화를 마치고 로그인한 SteamID가 연결된 계정의 것과 같음을 확인했다는 뜻) |
| `dev` | `NODE_ENV != production`일 때만(같은 PC 시험) |

- 자격 후보가 하나도 없으면 `422 TRANSPORT_UNAVAILABLE`(출발·입장 거절). 모든 정상 구성에서는 `relay`가 있어 이 일은 중계 비활성·과부하 때만 생긴다.
- 기본 우선순위가 `relay,steam`인 이유: 중계가 모든 플랫폼에서 같게 동작하고 방장 업링크가 작으며(브로드캐스트 1회) 서버가 연결 상태를 알아 인계가 빠르다. `steam,relay`로 두면 Steam 자격이 되는 방은 P2P로 가서 서버 송신 비용이 0이고 중계는 폴백이다(비용 절감·시험용 설정, 결정 대기 2).
- `PARTY_TRANSPORT`(4단계 설정)는 `COMBAT_TRANSPORT_ORDER`로 대체된다(14절): `dev` -> `dev`, `steam` -> `steam,relay`.
- Steam 자격이 있는 방이 처음부터 중계로 가는 경우(기본 설정)에도 `steam`은 `transport_order`의 두 번째 후보로 남아 중계가 실패하면 전환 후보가 된다.

### 5.2 연결 시도 규칙(클라이언트, 모든 전송 공통)

| 전송 | 성공 기준 | 시도 한도 |
|---|---|---|
| `relay` | T1(티켓) 성공 -> WebSocket 연결 -> `ready` 수신 | 전체 8초 안에(재시도 0.5·1·2초 간격) |
| `steam` | `ConnectP2P` 후 연결 상태 `Connected`(멤버) / 리슨 소켓 수락(호스트) -> Hello/Welcome 교환 | 전체 12초 안에(SDR 경로 협상 포함) |
| `dev` | 소켓 바인드, 첫 Hello 응답 | 3초 |

한도를 넘기면 클라이언트는 **T2(전환 요청)**를 보낸다. 서버가 새 전송을 정하면 모든 멤버가 같은 판단을 따른다.

### 5.3 전환 프로토콜 (T2)

**T2 `POST /characters/{uuid}/rooms/{kind}/{room_id}/transport/fallback`**
- 요청(`.strict()`): `{ request_id: uuid, observed_epoch: int(1..), reason: "connect_timeout" | "connect_failed" | "repeated_drop" | "host_unreachable" }`. 받지 않는 값: 원하는 전송(서버가 순서대로 정한다), 시각.
- 처리(방 행 잠금, 멤버만): ① `observed_epoch == transport_epoch`가 아니면 `409 TRANSPORT_CHANGED`(`errors.current`에 현재 `{ transport, epoch }`, 이미 누가 바꿨으니 그 전송으로 간다) ② `host_unreachable`은 호스트가 서버 기준으로 살아 있을 때(하트비트 신선 또는 중계 연결 있음)만 의미가 있다 ③ 마지막 전환 뒤 `TRANSPORT_SWITCH_COOLDOWN_SECONDS`(10) 안이면 `429 TRANSPORT_SWITCH_COOLDOWN` ④ `transport_switches >= 4`이면 `409 TRANSPORT_EXHAUSTED` ⑤ `transport_order`에서 현재 다음 후보를 고른다. 없으면 `409 TRANSPORT_EXHAUSTED`(앞에서 한 바퀴 돌았다는 뜻, 클라이언트는 "연결할 수 없습니다. 파티원과 네트워크를 확인해 주세요" + 판 이탈/재시도 선택지) ⑥ `transport`, `transport_epoch + 1`, `transport_switches + 1`, `version + 1`, 같은 트랜잭션에서 방 상태 갱신.
- 커밋 뒤: 현재 중계 방의 모든 연결에 `transport.changed` + `bye(4014, reconnect:false)`, `/ws`로 `party.changed(run)` 또는 `field.changed`를 푸시(안전망으로 하트비트·GET에도 `transport`가 실린다).
- 응답 `200` `data`: `{ "transport": { "current": "relay", "epoch": 2, "order": [...], "relay": { "url": "wss://.../relay" } } }`
- 멱등성: `request_id`. 속도 제한: 캐릭터당 10초에 1회. 에러: 위 + 공통.
- **한 멤버의 실패가 방 전체를 옮긴다**: Steam이 한 사람만 안 되어도 방 전체가 중계로 바뀐다(중계는 모두 가능하다). 비용은 작고 구현이 단순하다.
- **되돌아가지 않는다**: 중계로 전환한 방은 Steam으로 자동 복귀하지 않는다(왕복 방지). 다음 판·세션은 `pickTransport`를 다시 한다.
- 전환 중 PartyNet은 기존 전투 상태를 유지한 채 전송만 교체한다(`PartyNet.ReplaceTransport`, mapping 6절). 호스트 PC가 살아 있으면 전투는 끊기지 않는다(짧은 정지만).

**전환 발생 지점 요약**

| 상황 | 전환 |
|---|---|
| 중계 연결 8초 안에 실패(WebSocket 차단 등)이고 REST는 동작 | `relay -> steam`(자격이 되면) |
| Steam 연결 12초 안에 실패, 또는 `ProblemDetectedLocally`가 반복 | `steam -> relay` |
| Steam 연결은 맺었지만 호스트와 3초 이상 통신 불가(P2P 단절), 서버는 호스트가 살아 있다고 봄(`HOST_ALIVE`) | `steam -> relay`(`host_unreachable`). 4단계에서 "방장 재연결을 기다리거나 이탈"이었던 구멍을 닫는다 |
| 중계 연결이 30초 안에 3번 비정상 종료 | `relay -> steam`(`repeated_drop`, 자격이 되면) |

### 5.4 4단계 설계와의 접합

| 4단계 항목 | 중계 전송 | Steam P2P 전송 |
|---|---|---|
| 연결 수락·명단 대조(4단계 6.2, 12절) | 서버가 티켓·멤버십으로 입장시키고 좌석을 도장 | 호스트 PC가 연결의 SteamID를 서버 명단(`members[].steam_id`)과 대조 |
| `entry_token`(HMAC(run_key, 판.캐릭터.자리)) | **사용 안 함**(서버가 인증) | 그대로 사용(호스트가 오프라인으로 검증). 필드 세션은 `session_key`로 같은 방식 |
| `host_key` | 호스트에게 계속 주지만 검증에 쓰이지 않는다(전송 전환에 대비해 항상 준다) | 그대로 |
| R3 `join` | **자동**: 중계 `hello` 성공이 입장 확인(`RoomDirectory.peerConnected`가 R3와 같은 상태 전이·마지막 입장자 begin을 수행). 클라이언트가 R3를 불러도 같은 결과(멱등) | 그대로(`entry_token`, `host_steam_id` 필수) |
| R3의 `host_steam_id` 검사 | 해당 없음(호스트 좌석은 서버가 안다) | 그대로: 멤버가 실제 연결한 SteamID가 서버가 기록한 호스트 SteamID와 같아야 한다. 가짜 방장에게 접속하는 사고를 막는다 |
| `RunView.members[].steam_id` | 채워질 수 있다(Steam 연결이 있는 계정이면, `PARTY_TRANSPORT`와 무관하게 Steam 계정이면 항상). 클라이언트는 `transport.current == steam`일 때만 쓴다 | 필수 |
| R5 하트비트 | 유지(게임 스레드 생존, 호스트 판정의 백업) | 유지 |
| R6 호스트 인계 | 서버 주도 + 클레임(4.8) | 4단계 그대로 + 단절 시 전환(5.3) |
| `STEAM_REQUIRED`(422) | **삭제**(중계가 있으므로 Steam 없는 멤버도 파티에 들어간다). 대신 `TRANSPORT_UNAVAILABLE` | - |
| R1 출발의 `422 MEMBER_NOT_ELIGIBLE` | 변화 없음 | 변화 없음 |

Steam ID는 4단계와 같이 같은 판·세션 멤버에게만 보인다(공개 프로필 식별자이고 SDR을 쓰면 IP는 노출되지 않는다. Steam 기본 설정은 직접 경로를 허용하므로 IP가 파티원에게 보일 수 있다, 결정 대기 6).

### 5.5 Steam 인증 경로 변경 (서버, `domains/auth/steamProvider.ts`, A1·A2)

4단계 3절의 흐름(클라이언트 `GetAuthTicketForWebApi(identity)` -> `POST /auth/steam` -> `ISteamUserAuth/AuthenticateUserTicket`)은 그대로이고, 출시 품질을 위해 아래를 바꾼다.

| # | 변경 | 이유 |
|---|---|---|
| S1 | `/meta`에 `steam: { identity, app_id }`를 싣는다(공개 값). 클라이언트는 `identity`를 하드코딩하지 않고 이 값으로 티켓을 만든다. 클라이언트는 `SteamUtils.GetAppID()`가 `/meta.steam.app_id`와 다르면 Steam 기능을 끄고 경고한다 | 시험(480)용 클라이언트가 운영 서버에, 또는 그 반대로 붙는 사고를 막는다. identity 불일치로 전원 로그인 실패하는 사고(phase7 9.3)를 구조로 제거 |
| S2 | 설정 `STEAM_WEB_API_BASE`(기본 `https://api.steampowered.com`)를 둔다. 퍼블리셔 키를 쓰면 `https://partner.steam-api.com`으로 바꾼다 | 480 시험 키(일반 Web API 키)와 출시 후 퍼블리셔 키의 호스트·권한이 다를 수 있다(4단계 3.2의 "확인 필요"를 설정 한 줄로 해소) |
| S3 | 응답 파싱: 성공은 `response.params.result == "OK"` 그대로. `response.error`(`errorcode`, `errordesc`) 또는 `result != OK`는 `401 STEAM_TICKET_INVALID`. **Steam HTTP 401/403/400(키·앱 ID 불일치 = 우리 설정 오류)은 `503 STEAM_UNAVAILABLE`**로 응답하고 서버 로그에 `steam.key_rejected`(error 등급), 알림 `steam_auth_misconfigured`(긴급). 사용자에게 우리 설정 잘못을 `401`로 돌리지 않는다 | 키 만료·앱 ID 오설정을 사용자 탓으로 오인하지 않고 운영자가 즉시 알게 한다 |
| S4 | 시간 제한 5초 유지, **네트워크 오류·5xx에 한해 1회 재시도**(`STEAM_WEB_API_RETRIES`=1, 총 8초 이내). 4xx·무효 티켓은 재시도 없음 | 일시 장애를 사용자에게 보이지 않게 |
| S5 | 간단한 회로 차단: 연속 `STEAM_BREAKER_FAILURES`(5)회 실패하면 `STEAM_BREAKER_OPEN_SECONDS`(30) 동안 Steam 호출 없이 `503 STEAM_UNAVAILABLE`(+ `Retry-After`)을 즉시 반환 | Steam 장애가 로그인 요청을 5초씩 붙잡아 서버 전체를 느리게 하지 않게 |
| S6 | 기동 자가 점검: `STEAM_AUTH_MODE=web_api`이면 시작 때 일부러 잘못된 티켓으로 한 번 호출해 응답이 "무효 티켓"(키·앱 ID 정상)인지 "403"(키 오류)인지 확인해 로그(`steam.selfcheck`)에 남긴다. 실패해도 서버는 뜬다(경고) | 480 시험 키가 `AuthenticateUserTicket`에 통하는지의 실측(4단계 3.2)을 배포마다 자동으로 확인 |
| S7 | 지표: `steam_auth_ok/fail/latency_p95`, 회로 상태를 `ops.snapshot`에 | 운영 |
| S8 | `ownersteamid != steamid`(패밀리 공유)는 지금처럼 로그만(정책은 운영) | 변화 없음 |
| S9 | 티켓 재사용 방지 집합은 메모리(10분)로 유지, `TicketReplayStore` 인터페이스라 확장 1단계에서 Redis | 변화 없음 |

- **앱 ID 480 시험 환경**: 4단계 가드 G5("운영에서 `STEAM_APP_ID=480` 거부")는 유지하되 판단 기준을 `NODE_ENV`가 아니라 새 설정 `DEPLOY_STAGE`(`test` \| `live`)로 옮긴다. 시험 서버는 `NODE_ENV=production` + `DEPLOY_STAGE=test`로 올려 다른 운영 가드(TLS·시크릿·관리자 바인드)는 그대로 켜 두고 480만 허용한다. `live`에서는 480과 `AUTH_DEV_ENABLED` 기본값 등 시험 설정을 거부한다(14절 G5·G9, 결정 대기 7).
- 480으로 두 PC가 P2P하는 방법은 mapping 7절(클라이언트)과 15절(R3 시험 순서)에 있다.

## 6. B. 필드 파티 사냥 (공유 필드)

### 6.1 모델: 파티 단위 필드 세션

같은 파티원이 같은 필드 맵에 있으면 **필드 세션** 하나가 생긴다. 한 PC(호스트)가 그 맵의 몬스터(`EnemySpawner`가 만드는 것)를 계산하고, 다른 파티원 PC는 호스트가 보내는 스냅샷으로 꼭두각시를 그린다. 구조는 4단계 던전과 같고 중계 방(`kind=field`)을 그대로 쓴다.

- **공유 단위는 파티**다. 같은 맵에 서로 다른 파티나 파티 없는 사람이 있어도 서로의 몬스터·위치는 보이지 않는다(각자의 세션 또는 로컬 몬스터). 이유: 전투를 한 PC가 계산하는 구조(PLAN_SERVER §1, O2)라 서로 모르는 사람 사이에 몬스터를 공유하려면 전용 서버가 필요하다. 이 단계는 참고 게임·메이플 식 "같은 필드에서 같이 사냥"을 **파티 범위**로 구현한다(결정 대기 1).
- 세션은 `(파티, 맵)` 쌍마다 하나다. 한 파티의 멤버가 서로 다른 맵에 있으면 맵마다 세션이 따로 생긴다(예: A·B는 숲, C는 마을이면 숲 세션에 A·B만).
- **세션이 필요한 맵**: `maps.json`에서 `instanced=false`이고 `fieldSpawns`가 있는 맵(현재 숲). 마을(안전 지대), 던전 방, 연출 스폰(`scriptedSpawns`) 맵은 세션을 만들지 않는다. 연출·퀘스트로 만들어지는 몬스터는 `EnemySpawner`가 만든 것이 아니라서 공유하지 않는다(각 PC 로컬, mapping 2절 `Shared`).
- 세션은 사람이 2명 이상일 때만 의미가 있다. 혼자 있으면 솔로와 같은 규칙으로 처치를 판정한다(서버가 활성 멤버 수로 판단, 6.5절). 파티가 1명뿐이면(`party_members` 활성 1명) 클라이언트가 세션을 만들지 않는다(F1이 `session: null`).
- 몬스터 계산 위치: 호스트 PC(스폰, 리스폰, AI, 피격, HP). 서버는 몬스터 상태를 저장하지 않는다(4단계와 같다). 호스트가 바뀌면 새 호스트가 마지막 스냅샷부터 이어받는다.

### 6.2 호스트 선출과 인계

**규칙**(사용자 지정): 파티 방장이 그 맵에 있으면 방장, 없으면 **번호가 가장 낮은 멤버**. 번호 = 파티 가입 순서(`party_members.joined_at` 오름차순, 방장 제외 순위)다. 좌석(`seat`)은 연결 슬롯일 뿐 선출 순서와 무관하다.

`electHost(session)`: 후보 = 세션 멤버 중 `playing`(연결·환영 완료)이고 하트비트가 신선한 사람. 후보가 없고 `joined`(방금 들어와 환영 전)만 있으면 그중에서 같은 순서로 고른다. 정렬 키 `(방장 여부 내림차순, party_members.joined_at 오름차순, seat)`. 후보가 전혀 없으면 `host_character_id = NULL`.

**선출이 일어나는 때**(그 밖에는 호스트를 바꾸지 않는다 = **고정 호스트**)

| 때 | 동작 |
|---|---|
| 세션 생성(첫 멤버 입장) | 입장한 사람이 호스트 |
| 세션 시작 선출 창 | 세션 생성 뒤 `FIELD_ELECTION_WINDOW_SECONDS`(5) 안에 더 우선인 멤버(방장)가 들어오면 호스트를 그에게 넘긴다. 파티가 같은 포털로 이동할 때 입장 순서가 뒤섞여도 방장이 호스트가 되게 하는 장치이고, 이 시점에는 아직 몬스터 상태가 거의 없어 인계 비용이 작다 |
| 호스트가 맵을 떠남(F4) | 즉시 `electHost`로 다음 호스트. 대기 없음 |
| 호스트 연결·하트비트 끊김 | 중계는 3초 grace, 하트비트 경로는 12초 뒤 서버가 `electHost`(4.8절) |
| 호스트 없음(NULL) 상태에서 멤버가 연결 확인 | 그 멤버(또는 가장 우선인 후보)가 호스트 |
| 파티 방장이 맵에 나중에 들어옴(선출 창 뒤) | **인계하지 않는다**(결정 대기 4). 전투 중 호스트 교체는 짧은 정지를 부르므로 호스트가 떠날 때까지 유지 |

**인계 시 상태 이어받기**(클라이언트, mapping 6절)
- 새 호스트는 자기 꼭두각시 몬스터를 실제 몬스터로 풀고(4단계 `TakeOver`와 같은 방식 `ReleasePuppet`), 스포너 상태를 복원한다. 스포너 상태(스폰점별 살아 있는 몬스터 번호 / 리스폰까지 남은 시간)는 호스트가 `PartyMsg.SpawnerState`로 2초마다 멤버에게 보내 둔 마지막 값이다. 값이 없으면 **보수적으로**: 몬스터가 없는 스폰점은 방금 죽은 것으로 보고 전체 리스폰 시간을 다시 센다(서버의 공급 상한 `kill_supply`를 넘는 몬스터가 생기지 않는다).
- 몬스터 번호(NetId)는 새 호스트가 본 최댓값 + 1부터 이어서 붙인다. 호스트 세대가 바뀌면 `monster_ref`(세대 x 2^20 + NetId)가 달라지므로 서버의 중복 검사와 부딪히지 않는다.
- 옛 호스트가 다른 맵으로 이동한 경우는 로컬 몬스터가 맵 전환으로 사라지므로 정리할 것이 없다. 연결이 끊긴 호스트가 돌아오면 4.8절 5번(권한 상실).

### 6.3 세션 시작·종료 (이벤트 표)

| 이벤트 | 서버 처리 | 클라이언트 |
|---|---|---|
| 맵 입장(마을 -> 숲, 포털, 귀환 주문서) | F1: 파티가 2명 이상이고 그 맵이 조건에 맞으면 세션 생성 또는 합류. 이전 필드 세션이 있으면 같은 트랜잭션에서 닫는다(`replaced` 또는 `map_move`). 활성 파티 판에 있으면 `409 IN_PARTY_RUN` | 맵 로딩이 끝나면 F1 호출. 응답 전에는 로컬 스포너를 `Pending`(스폰 보류)으로 두고, 결과가 호스트면 스폰 시작, 멤버면 호스트 접속·꼭두각시, 세션 없음이면 로컬 스폰(솔로) |
| 같은 맵에 파티원이 뒤늦게 합류 | F1이 기존 세션에 합류. `field.changed` 푸시로 기존 멤버가 세션 갱신 | 호스트는 `OnHello`로 합류 처리, 몬스터를 `EnemySpawn`으로 재전송 |
| 맵 이동·마을 귀환 | F4(`leave`): 멤버 행 `left(map_move)`. 호스트였으면 즉시 인계. 마지막 사람이면 세션 `ended(empty)` | 맵을 나가기 전에 F4(또는 F1이 이전 세션을 닫는다). 호스트는 이동해도 남은 멤버에게 몬스터를 넘긴다 |
| 파티 나가기·강퇴·해산(4단계 P9·P10·`closeParty`) | 같은 트랜잭션에서 그 멤버의 필드 세션 행을 닫음(`party_left`·`kicked`·`party_closed`), 호스트였으면 인계. 해산이면 세션 전체 종료 | `field.changed` 푸시 -> 세션이 없어졌으면 로컬 스폰(솔로)으로 복귀 |
| 연결 끊김(크래시, 네트워크, 앱 종료) | 하트비트 12초 정체 또는 중계 연결 끊김 -> `disconnected`, 60초 안에 돌아오면 `playing`, 넘으면 `left(rejoin_timeout)`. 호스트였으면 4.8절의 인계 | 재접속하면 F7로 내 세션을 찾고 F1 없이 T1로 다시 붙는다(맵이 같을 때) |
| 던전 출발(R1) 또는 솔로 던전(D1) | 해당 멤버 전원의 필드 세션 행을 `dungeon_start`로 닫는다(출발이 막히지 않는다) | 던전 입장 이동 중 F4가 와도 이미 닫혔으면 성공으로 본다(멱등) |
| 로그아웃·타이틀 복귀 | F4 시도, 안 오면 하트비트 정체로 정리 | |
| 방치 | `stale-runs` 작업(10분마다): 마지막 하트비트가 `FIELD_STALE_SECONDS`(300) 지난 활성 세션을 `ended(stale)` | |
| 서버 재시작 | DB의 세션은 그대로, 중계 방은 첫 `hello`에서 다시 만들어진다(4.9절) | 재접속(T1) |

세션 `ended` 뒤에 같은 파티가 같은 맵에 다시 들어오면 새 세션(새 uuid, 새 `session_key`)이다.

### 6.4 엔드포인트 (F1~F7)

공통: 모든 경로 `/characters/{uuid}/field-sessions...`, 내 캐릭터 소유 확인 후 처리, 세션은 내가 활성 멤버(`state <> 'left'`)일 때만 접근할 수 있다(아니면 `404 FIELD_SESSION_NOT_FOUND`, 없는 세션과 남의 세션을 구분하지 않는다).

**FieldSessionView**
```
{ "id": uuid, "version": 7, "map_id": "forest", "state": "active" | "ended",
  "host": { "character_id": uuid, "seat": 0, "steam_id": string | null, "epoch": 1 } | null,
  "transport": { "current": "relay", "epoch": 1, "order": ["relay","steam"], "relay": { "url": "wss://.../relay" } },
  "me": { "seat": 1, "state": "joined" | "playing" | "disconnected",
          "entry_token": string | null },          // steam·dev 전송일 때만(중계에서는 null)
  "members": [ { "character_id": uuid, "name": "...", "class": "warrior", "level": 12, "seat": 0,
                 "state": "playing", "is_leader": true, "steam_id": string | null,
                 "gear_hash": "<16 hex>" } ],       // 서버가 아는 착용 장비의 지문(카드 위조 방지, 6.9절)
  "election_until": ISO | null, "server_time": ISO }
```

**F1 `POST /field-sessions/enter`**
- 요청(`.strict()`): `{ request_id: uuid, map_id: string(1..32) }`. 받지 않는 값: 좌석, 호스트 여부, 시각, 인원.
- 처리(내 캐릭터 잠금 후): ① 내 활성 파티 조회. 없거나 활성 사람 멤버가 나 하나면 `200 { session: null, reason: "NO_PARTY" | "ALONE" }`(세션 없음은 오류가 아니다) ② 맵이 세션 가능한가(`maps.json`: `instanced=false`, `fieldSpawns` 있음), 아니면 `422 MAP_NOT_SHARED`(클라이언트는 로컬에서 먼저 걸러 이 호출을 하지 않는다) ③ 활성 파티 판 멤버면 `409 IN_PARTY_RUN` ④ 내 다른 필드 세션이 있으면 같은 트랜잭션에서 `left(map_move | replaced)` 처리, 호스트였으면 그 세션의 인계 ⑤ `(party_id, map_id)` 활성 세션을 `FOR UPDATE`로 찾는다. 없으면 생성(호스트 = 나, `session_key` 난수 32B, 전송 = `pickTransport`, 좌석 0, `host_since = now`) ⑥ 있으면 합류: 이미 이 세션에 내 행이 있으면 되살림(`joined`, 카운터 유지), 없으면 가장 낮은 빈 좌석(0..3, 4개가 다 차면 `409 FIELD_FULL`, 파티 최대 4명이라 방어용) ⑦ 선출 창이면 6.2절 규칙으로 호스트 갱신 ⑧ `attack_cap` 계산, `version + 1`, 파티 다른 멤버에게 `field.changed` 푸시(커밋 뒤).
- 응답 `201`(새 세션) / `200`(합류·세션 없음) `data`: `{ "session": FieldSessionView | null, "host_key": string | null, "reason"?: string }`. `host_key`(base64url `session_key`)는 내가 호스트일 때만(steam·dev 전송의 입장 토큰 검증용, 중계에서는 쓰이지 않지만 전환에 대비해 준다).
- 멱등성: `request_id`(같은 id 재전송은 같은 응답). 속도 제한: 캐릭터당 초당 1회, 분당 12회.
- 인덱스: `field_sessions_one_active`(`(party_id, map_id)` 조회·유일성), `field_session_members_one_active`(내 활성 세션 정리), `party_members_one_active`(내 파티).

**F2 `GET /field-sessions/{id}`**: 쿼리 `after_version?: int`. 응답 `{ "changed": true, "session": FieldSessionView }` 또는 `{ "changed": false }`. `/ws`의 `field.changed` 푸시가 안전망(폴링)으로 따라잡는다(WS 연결 중 15초, 끊김 중 2초, 4단계 1.2절과 같다). 속도 제한 초당 2회.

**F3 `POST /field-sessions/{id}/heartbeat`**
- 요청: `{ seen_epoch: int(1..), synced: boolean }`. 5초마다. `synced`는 이 PC가 호스트와 연결·환영을 마치고 월드가 준비됐다는 뜻(`joined -> playing` 승격, 호스트 후보 자격).
- 처리: 내 `last_seen_at = now`, `disconnected` 복귀(60초 안), `attack_cap` 재계산(레벨·착용 장비가 바뀌었을 수 있다), 다른 멤버의 12초 정체 지연 전이, 호스트 정체이면 6.2·4.8의 인계(서버 주도), 세션 `last_active_at` 갱신.
- 응답 `data`: `{ "state", "host": {character_id, seat, steam_id, epoch} | null, "host_changed": bool, "transport": {...}, "me": {state}, "members": [ { character_id, seat, state, level, gear_hash } ], "server_time" }`. `members[].level`·`gear_hash`는 호스트가 멤버 카드(`MemberCard`)를 믿어도 되는지 대조하는 서버 기준값이다(6.9절).
- 속도 제한: 2초에 1회. 인덱스: PK `(session_id, character_id)`.

**F4 `POST /field-sessions/{id}/leave`**: 요청 `{ request_id }`. 내 행 `left(left)`(맵 이동 사유를 서버가 구분할 필요가 없어 `map_move`는 F1이 닫을 때만 쓴다). 호스트였으면 즉시 `electHost`로 인계(응답에 새 호스트). 마지막 사람이면 세션 `ended(empty)`, `session_key = NULL`. 이미 `left`면 성공(멱등). 응답 `{ "left": true }`.

**F5 `POST /field-sessions/{id}/host/claim`**: 4단계 R6와 같은 조건·응답(요청 `{ request_id, observed_epoch }`). 승인 조건: 세션 `active`(`409 FIELD_SESSION_ENDED`) / `observed_epoch == host_epoch`(아니면 `409 HOST_CHANGED` + `errors.host`) / 현재 호스트가 죽은 것으로 서버가 봄(`HOST_STALE_SECONDS` 정체, 호스트 행 `left`/`disconnected`, 호스트 없음 NULL, 중계 방이면 호스트 연결 grace 초과). 아니면 `409 HOST_ALIVE` / 요청자가 후보 순서(6.2)의 첫 사람이어야 함(`409 NOT_NEXT_HOST`). 효과: `host_epoch + 1`, 새 호스트, 옛 호스트 `disconnected`, `version + 1`. 응답 `{ host, host_key, members }`. 멱등성 `request_id`.

**F7 `GET /field-session`**: 내가 활성 멤버인 세션 하나(없으면 `{ "session": null }`). 앱 재시작·재접속 복구용(맵 id가 같을 때 T1로 다시 붙는다). 속도 제한 초당 1회.

에러 코드(필드): `422 MAP_NOT_SHARED`, `404 FIELD_SESSION_NOT_FOUND`, `409 FIELD_SESSION_ENDED`, `409 FIELD_FULL`, `409 IN_PARTY_RUN`, `409 HOST_CHANGED`, `409 HOST_ALIVE`, `409 NOT_NEXT_HOST`, `403 NOT_HOST`(F6), `409 HOST_EPOCH_STALE`(F6).

### 6.5 처치 보고 변경 (E1, 3단계 `POST /kills`의 확장)

3단계 3.1~3.2와 4단계 7.2를 그대로 쓰고 아래를 더한다. 요청에 두 필드가 늘고(둘 다 선택, `.strict()` 유지) 다른 것은 같다.

```
{ request_id, map_id, monster_id, hits?, run_id?, room_index?,
  session_id?: uuid,          // 필드 세션에서의 처치
  monster_ref?: int(0..) }    // 호스트가 붙인 몬스터 식별(세대 x 2^20 + 번호). session_id와 함께만
```
`refine`: `session_id`와 `run_id`는 동시에 쓰지 않는다. `monster_ref`는 `session_id`가 있을 때만. 받지 않는 값은 그대로(경험치, 골드, 드롭, 시각, 레벨).

처리 순서(첫 실패에서 중단, `session_id`가 있을 때. 번호는 기존 단계 위에 끼워 넣는 위치):
1. **세션 멤버십**: 내가 그 세션의 활성 멤버(`state <> 'left'`)이고 세션 `active`이고 `session.map_id == map_id`여야 한다(아니면 `409 FIELD_SESSION_INVALID`, 클라이언트는 F7로 자기 상태를 다시 읽는다). 이상 기록 없음(정직한 지연으로 생길 수 있다).
2. 3단계 `resolveFieldTarget`(맵·몬스터 대조, 공급 상한)는 그대로. 몬스터 레벨은 `maps.json`의 `fieldSpawns[].monsterLevel`(기본 1)을 쓴다.
3. `monster_ref` 중복: `(field_session_id, character_id, monster_ref)` 유일 인덱스(`kill_log_field_ref_uq`). 중복이면 `409 KILL_DUPLICATE`(클라이언트 버그·재전송 방어, 이상 기록 없음).
4. **속도**: 1초 창 한도를 세션 인원에 맞춘다: `burst = KILL_BURST_FIELD + KILL_BURST_FIELD_PER_EXTRA(2) x (활성 멤버 수 - 1)`(4명이면 4 + 6 = 10). 근거: 파티원 각자의 범위기가 같은 순간에 죽인 몰이를 멤버 모두가 각자 보고한다. 리스폰 공급 상한은 **그대로 캐릭터마다** 센다(6.6절의 핵심).
5. **처치 대조 게이트**(6.10절): 활성 멤버 2명 이상이고 호스트 관찰이 신선하면 부채(`kills_accepted - kills_credited`)가 `FIELD_UNCREDITED_MAX`(24) 이상이면 `422 KILL_REJECTED`(`anomaly_log field_uncredited`, severity 1~2).
6. **화력 상한**: `session_cap = Σ(활성 멤버 attack_cap) x FIELD_POWER_SLACK(1.15)`를 3단계 `powerAllows`의 `cap`으로 쓴다(파티 전체의 딜이 들어가므로 본인 한 사람의 상한으로는 정직한 멤버가 걸린다. 던전 `power_cap`과 같은 발상). 활성 멤버가 나 하나면 3단계 방식.
7. **경험치·드롭**: 3.1.1 규칙으로 경험치를 계산한 뒤 **레벨 격차 감쇠 `xp_factor`**(6.7절)를 곱한다(활성 멤버 2명 이상일 때만). 드롭은 개인 굴림이고 재료·장비의 확률 비교(`u <= chance`)에 `xp_factor`와 `FIELD_PARTY_DROP_FACTOR`(기본 1.0)를 곱한 확률을 쓴다. 골드 더미의 금액은 감쇠하지 않는다(기본 골드는 몬스터가 흘리는 값이라 사람 수와 무관).
8. `kill_log`에 `field_session_id`, `monster_ref`, `xp_factor`를 기록하고 `field_session_members.kills_accepted + 1`(같은 트랜잭션, 이미 잠근 세션 행 아래에서 멤버 행만 갱신).
- 응답 `data`는 3단계와 같고 `field: { "shared": true, "xp_factor": 1.0 }`를 더한다(클라이언트가 감쇠를 표시할 수 있다).
- **일시 차단(`KILL_BLOCKED`) 귀속**: 4단계 7.2와 같은 규칙. 필드 세션에서 `kill_*` 거절은 모든 멤버 `anomaly_log`에 남기되 일시 차단 카운트는 호스트에게만 쌓고 멤버 거절은 severity 1이다(호스트가 몬스터를 즉사시키면 정직한 멤버의 보고가 같은 이유로 거절되는데 그 멤버를 막지 않기 위해서다).
- 속도 제한: 3단계 캐릭터당 초당 6회·분당 90회 그대로. 파티 4명이 각자 모든 처치를 보고해도 멤버당 요청 수는 솔로와 같다(맵 공급이 같으므로 6.6절).
- 처치를 보고하는 쪽(클라이언트)의 규칙: 호스트가 사망 이벤트에 실어 보내는 **기여 비트마스크**(`creditMask`)에 내 좌석이 있는 처치만 보고한다(6.10절, mapping 3절).

### 6.6 보상 설계와 경제: 숫자로 본 선택

**결정(권장)**: 멤버마다 자기 계정으로 처치를 보고하고(던전과 같다), 경험치는 **나누지 않고 각자 전부** 받고, 드롭은 **개인 굴림**이며, 기존 공급 상한은 **그대로 캐릭터마다** 적용한다. 필드 몬스터는 세션 호스트가 계산하지만 보상은 서버가 멤버별로 판정한다.

**전제 숫자**(현재 `server/data` 기준, 숲): 해골 경험치 20(몬스터 레벨 1), 체력 30, 기본 골드 8~16(평균 12), 스폰점 15개, 리스폰 25초(`maps.json fieldSpawns`, `monsters.json`), 경험치 곡선은 Lv1->2가 40, Lv10->11이 715(`progression.json`). 서버의 공급 상한은 캐릭터마다 `ceil(15 x 1.1) = 17`마리 / 25초(= 0.68마리/초)다(3단계 3.2.2). 정직한 솔로 사냥 속도는 0.3~0.5마리/초(3단계 3.2.2, 스폰점 간격과 이동 속도로 추정). 파티 4명은 맵 전체를 병렬로 돌아 약 0.5마리/초(공급 0.6의 대부분)까지 가능하다고 둔다.

| 안 | 멤버당 경험치/초 | 파티 합계/초 | 솔로(0.4/초 = 8) 대비 멤버 | 평가 |
|---|---|---|---|---|
| 솔로 기준 | 8 | 8 | 1.0x | 기준 |
| **A. 각자 전부(권장)** | 0.5 x 20 = **10** | 40 | **1.25x** | 멤버 상한은 공급 상한(0.68 x 20 = 13.6)이므로 솔로의 최대 상한과 같은 값. 파티 합계만 늘고 개인 상한은 불변 |
| B. 균등 분배(경험치 / 인원) | 2.5 | 10 | 0.31x | 같이 하면 혼자보다 3배 느림. 파티 사냥의 이유가 없다 |
| C. 분배 + 파티 보너스(인원당 +10%, 4명 +30%) | 0.5 x 20 x 1.3 / 4 = 3.25 | 13 | 0.41x | 여전히 혼자가 낫다 |
| D. 마지막 일격자만 | 평균 2.5(분산 큼) | 10 | 0.31x | 사냥 훔치기·분쟁 |
| E. 각자 전부 + 스폰 인원 비례(4명 2.5배) | 약 18 | 72 | 2.25x | 상한(13.6)을 넘으려면 캐릭터 공급 상한을 2.5배로 올려야 해서 솔로 상한이 깨진다 |

- **불변식**: 필드의 몬스터는 파티가 공유하는 한 벌(스폰점 15개)이다. 멤버 한 명이 한 창(25초)에 받을 수 있는 처치는 어떤 경우에도 17을 넘지 못한다(서버가 캐릭터마다 센다). 그래서 파티가 함께 사냥해도 **멤버 개인의 시간당 보상 상한은 솔로 상한과 같다**. 정직한 파티는 상한 근처까지 쓸 수 있어 솔로 평균 대비 +25%(상한 대비 최대 +50%) 정도 이득이고, 파티 전체의 합계가 최대 4배가 되는 것은 사람이 4명이라서다.
- **골드·재료·장비도 같은 배율**이다: 기본 골드 평균 12 x 0.4/초 x 3600 = 시간당 약 17,280(솔로) -> 파티 멤버 약 21,600(+25%), 상한 25,920(+50%). 재료·장비 드롭은 처치 수에 비례하므로 같은 비율로 는다. 경매장(6단계)으로 흘러들 장비 공급이 파티에서 최대 +50%(캐릭터당) 늘 수 있다는 뜻이고, 이 값을 지켜보는 지표(`faucet_party_ratio` = 파티 세션 처치 / 솔로 처치 대비 개인 처치 수)와 조정 손잡이(`FIELD_PARTY_DROP_FACTOR`, `FIELD_PARTY_XP_FACTOR`, 기본 1.0)를 둔다. 손잡이를 쓰는 기준: 개인 상한 대비 1.2배(= 솔로 상한의 1.2배)를 넘지 않게.
- **몬스터 HP는 인원에 따라 올리지 않는다**(기본). 던전은 `partyHpScale`로 시간을 맞추지만 필드는 공급(스폰점 / 리스폰)이 속도를 정하므로 HP를 올리면 파티가 손해일 뿐이다. 높은 레벨 필드가 생겨 체력이 의미를 가지면 `gameconfig.json`에 `fieldPartyHpScale`을 내보내고 서버 `hpMul`에 같은 값을 곱한다(현재 데이터에서는 필요 없다).
- **월드 재진입으로 공급 초기화?** 세션이 끝나고 다시 들어오면 몬스터가 새로 가득 차지만 서버 상한은 캐릭터마다 시간 창으로 센다(`kill_log`)라 영향이 없다(솔로와 같다).
- 서버가 막는 것: 한 멤버가 같은 몬스터를 두 번 보고(`monster_ref`), 존재하지 않는 몬스터·맵 밖 보고(3단계), 공급 상한 초과, 화력 초과, 기여하지 않은 처치의 반복(6.10).

### 6.7 저레벨 얹혀가기 (파워 레벨링)

문제: 고레벨 파티원이 몬스터를 잡아 주고 저레벨이 서 있기만 해도 높은 레벨 필드의 경험치를 받는다(PLAN_SERVER 결정 대기: "파티에서 저레벨 캐릭터의 처치 경험치 감쇠 여부").

**방어 세 겹**

1. **기여 증명**(6.10): 공격하지도, 가까이 있지도 않은 처치는 보고 대상이 아니다. 자리만 지키는 얹혀가기는 대조 게이트가 막는다.
2. **레벨 격차 감쇠**(파티 세션에서만, 솔로·던전은 변화 없음): 몬스터 레벨 `L`(`fieldSpawns[].monsterLevel`, 기본 1), 멤버 레벨 `c`일 때
   `xp_factor = clamp(1 - FIELD_CARRY_STEP x max(0, (L - FIELD_CARRY_SLACK) - c), FIELD_CARRY_MIN, 1)`
   (시작값 `FIELD_CARRY_SLACK`=5, `FIELD_CARRY_STEP`=0.12, `FIELD_CARRY_MIN`=0.2). 던전 입장 하한(권장 레벨 - 5, `PARTY_MIN_LEVEL_SLACK`)과 같은 폭이다.
   - 예(앞으로 생길 `L=20` 필드): `c=15` -> 1.0, `c=10` -> 1 - 0.12 x 5 = 0.40, `c=5` 이하 -> 0.2(하한). 경험치와 재료·장비 드롭 확률에 곱한다(골드 금액은 아니다).
   - **현재 데이터에서는 이 규칙이 움직이지 않는다**: 숲은 `L=1`이라 `max(0, 1 - 5 - c) = 0`이므로 모든 `c >= 1`에서 `xp_factor = 1`이다. 해골 경험치 20은 레벨이 오를수록 의미가 없다(Lv10 필요 경험치 715 대비 3%). 즉 지금은 숲에서 고레벨이 저레벨을 끌어올려도 얻을 것이 거의 없고, 규칙은 높은 레벨 필드가 데이터에 들어올 때 자동으로 켜진다(데이터 한 줄 `monsterLevel`).
3. **공급 상한**: 감쇠가 없더라도 저레벨 한 명이 받을 수 있는 처치는 위 불변식 안이다.

저레벨이 너무 높은 맵에 들어가는 것 자체는 막지 않는다(솔로도 막지 않는다). 던전은 입장 레벨 하한이 있다(4단계 5.2).

**결정 대기 3**: 이 감쇠를 켤지(권장: 켠다, 파티 세션 한정, 위 값).

### 6.8 채집 노드·상자·드롭: 공유하지 않는다 (캐릭터별 그대로)

| 대상 | 이 단계의 결정 | 근거 |
|---|---|---|
| 채집 노드(나무·돌·당근) | **캐릭터별 재생**(3단계 `character_node_state` 그대로, `POST /gathers`). 같은 나무를 멤버마다 따로 한 번씩 벨 수 있고, 한 멤버가 베어도 다른 멤버 화면의 나무는 서 있다(시각 상태는 로컬) | 노드를 놓고 경쟁하지 않는다(협동이 목적). 서버가 세션 단위 노드 상태를 가지면 호스트·인계·동시 채집 경합이 생긴다. **불변식 보존**: 캐릭터 한 명의 채집 상한은 노드별 재생 시간으로 정해지므로 파티가 되어도 그대로다 |
| 상자 | **캐릭터별 한 번**(`character_chests` PK 그대로) | 같은 이유 |
| 드롭 | **개인 드롭**(`drops.character_id`). 처치마다 멤버 각자 굴려 자기 `drops` 행을 받고, 줍기도 자기 id로만 한다. 다른 멤버의 드롭은 서로 보이지 않는다(각 PC가 자기 응답으로만 `Pickup`을 만든다) | 쟁탈·훔치기 없음(참고 게임 방식). 3단계 줍기 한 번만 규칙과 원장은 변경 없음 |
| 퀘스트 처치 목표 | 각자 `kill_stats`에 쌓인다(받아들여진 처치만) | 3단계 5절 |

### 6.9 AI 동료와 카드 위조 방지

- **다인 필드 세션에서는 AI 용병·스토리 동료를 숨긴다**(결정 대기 5, 권장). 이유: AI 몫의 딜을 서버가 알지 못하고(필드는 `ai_count`를 저장하지 않는다, 4단계 11절), AI가 인원 수에 따라 동적으로 바뀌면 호스트 PC의 계산 상태가 복잡해진다. 활성 멤버가 2명 이상인 동안 `Game.Party.SetRosterHidden(true)`, 혼자가 되면 복원. 서버 판정에는 영향이 없다(AI는 보고하지 않는다).
- **멤버 카드 위조 방지**: 호스트 PC는 멤버의 `MemberCard`(직업·레벨·착용 장비·패시브)로 그 멤버의 몸을 만든다. 멤버가 터무니없는 카드를 보내 호스트에서 강하게 싸운 것으로 처리돼도 **서버가 같은 처치를 화력 상한으로 거절**하므로 보상 이득은 없지만 정직한 파티원이 같이 거절당한다(처치 보고는 모두의 계정에 가기 때문). 그래서 서버가 기준값을 준다: F3/R5 응답의 `members[].level`과 `gear_hash`(착용 장비 키들의 정렬된 연결 해시 앞 16자). 호스트는 멤버 카드의 레벨이 서버 값보다 크거나 `gear_hash`가 다르면 그 카드를 쓰지 않고 마지막으로 검증된 카드(또는 서버 값으로 만든 최소 카드)를 쓴다. 장비를 바꾸면 서버 상태가 먼저 바뀌고(3단계 equip API) 다음 하트비트로 호스트가 새 지문을 알아 갱신된 카드를 받아들인다(`PartyMsg.CardUpdate`, mapping 3절). 패시브·젬은 서버가 레벨 대비 개수만 아는 클라이언트 저장 상태라 위조 가능 범위가 남는다(화력 상한이 막는다, 수용).

### 6.10 호스트 관찰과 처치 대조 게이트 (F6)

목적: 멤버가 **실제로 기여하지 않은 처치**를 계속 보고하는 것(자리만 지키는 계정, 얹혀가기 봇)을 막는다. 서버는 전투를 볼 수 없으므로 전투를 계산하는 호스트의 관찰을 한 번 더 본다. 던전 방장 보고(R7)와 같은 발상이고 규모가 작다.

**기여 정의**(호스트 PC가 정한다): 몬스터가 죽을 때 좌석 i가 그 몬스터에게 피해를 입혔거나(몬스터 수명 동안 한 번이라도), 살아 있는 상태로 `FIELD_CREDIT_RANGE`(클라이언트 상수, 기본 14칸) 안에 있었다. 호스트는 사망 이벤트(`PartyMsg.EnemyDie`)에 **기여 비트마스크**(좌석별 1비트)를 실어 보내고 멤버는 자기 비트가 켜진 처치만 보고한다.

**F6 `POST /field-sessions/{id}/observe`**
- 요청(`.strict()`): `{ request_id: uuid, host_epoch: int(1..), window_ms: int(1000..30000), credits: [ { seat: int(0..3), kills: int(0..99) } ] (1..4개) }`. 받지 않는 값: 경험치, 골드, 드롭, 시각. 호스트가 10초(`FIELD_OBSERVE_SECONDS`)마다 보내고 호스트를 떠날 때(인계 직전)도 한 번 더 보낸다.
- 처리: 현재 호스트이고 `host_epoch`가 현재 세대와 같아야 한다(`403 NOT_HOST`, `409 HOST_EPOCH_STALE`). 각 `seat`는 활성 멤버여야 한다. **그럴듯함**: 좌석별 `kills <= ceil(공급 상한 / 리스폰) x (window / 리스폰 + 1)` 형태의 공급 한도(3단계와 같은 근거)를 넘으면 그 항목을 무시하고 `anomaly_log(field_host, 2)`. 통과하면 `kills_credited = min(kills_credited + kills, kills_accepted + FIELD_CREDIT_SURPLUS(8))`(미리 쌓아 두는 크레딧으로 나중 얹혀가기를 숨기는 것을 막는다), `last_observe_at = now`.
- 응답 `200` `data`: `{ "accepted": true }`. 멱등성 `request_id`. 속도 제한 5초에 1회.
- **게이트(E1의 5번)**: 활성 멤버 2명 이상 + `now - last_observe_at <= FIELD_OBSERVE_GRACE_SECONDS`(30)일 때만 부채가 `FIELD_UNCREDITED_MAX`(24) 이상이면 새 처치를 거절한다. 정직한 멤버의 부채는 보고(즉시)와 크레딧(최대 10초 지연) 사이의 차이라 대략 0.5마리/초 x 10초 = 5마리 안팎이고 24는 그 4배 이상의 여유다. 호스트 관찰이 30초 넘게 없으면 게이트는 꺼지고 공급·화력·속도 상한만 남는다(정직한 멤버를 호스트 장애로 막지 않는다) + `anomaly_log(field_host, 1)`을 세션당 한 번.
- 한계(수용): 호스트와 멤버가 같은 사람(계정 두 개)이면 크레딧을 몰아줄 수 있다. 그래도 각 계정은 솔로 상한(6.6 불변식)과 화력·속도 검사 안에 있으므로 얻는 이득이 솔로 상한을 넘지 못한다(4단계 8.5와 같은 결론).

## 7. 4단계(파티 판) 기존 API 변경 (E2, E3, E7, E8)

| 대상 | 변경 |
|---|---|
| `RunView` (R1·R2·R3·R4·R5의 `run`) | `transport: { current, epoch, order, relay: { url } }`를 더한다. `members[].steam_id`는 Steam 계정이면 항상(전송과 무관) |
| R1 `POST /party/start` | `pickTransport`를 호출해 `party_runs.transport/transport_order`를 정한다(`422 TRANSPORT_UNAVAILABLE`). 멤버 전원의 필드 세션 행을 `dungeon_start`로 닫는다(같은 트랜잭션). `STEAM_REQUIRED`는 없어진다 |
| R2 `GET /party-runs/{id}` | 응답에 호스트 본인에게만 `host_key`를 더한다(서버 주도 인계 뒤·앱 재시작 뒤 복구에 필요. 이미 R1·R6 응답으로 같은 사람에게 주던 값이다) |
| R3 `POST .../join` | 중계 전송에서는 `entry_token`이 필수가 아니다(자동 입장, 5.4). Steam·dev는 그대로. 본문의 `entry_token`은 `transport != relay`일 때 필수가 된다 |
| R5 `POST .../heartbeat` | 응답에 `transport`와 `members[].level`, `gear_hash`를 더한다(호스트 카드 검증). `host_changed` 규칙 그대로 |
| R6 `POST .../host/claim` | 승인 조건 3번에 중계 연결 사실이 추가된다(4.8절). 새 호스트에게 `host.changed`가 이미 가 있을 수 있다(서버 주도 인계). 이 경우 클레임은 `409 HOST_CHANGED`로 현재 호스트를 알려 준다 |
| R8 `POST .../leave` / 판 종료 | 같은 트랜잭션 끝에서 중계 연결의 해당 좌석을 `peer.left(removed)`로 닫고 판이 끝나면 방을 닫는다(`RoomDirectory.memberRemoved`, `roomClosed`) |
| P9 나가기·P10 강퇴·`closeParty`(해산) | 같은 트랜잭션에서 그 멤버의 필드 세션 행을 닫는다(`party_left`·`kicked`·`party_closed`), 호스트면 인계 |
| D1 `POST /dungeon-runs`(솔로) | 필드 세션이 있으면 닫는다(`dungeon_start`) |
| `GET /characters/{uuid}` | 변경 없음 |

## 8. `/ws`(5단계)와 `/meta`(1단계) 변경

- `hello`에 선택 필드 `caps: { steam_p2p: boolean }`를 더한다(프로토콜 `v`는 그대로 1, 모르는 서버는 무시한다). 서버는 접속 세션 메모리(`PresenceStore`)에만 보관하고 DB에 쓰지 않는다. `caps.steam_p2p`는 그 계정에 Steam 연결이 있을 때만 참으로 인정한다(5.1절).
- 새 서버 -> 클라이언트 프레임: `field.changed { session_id, scope? }`(`party.changed`와 같은 힌트 방식, 받으면 F2/F7를 곧바로 다시 부른다). 호출 지점은 F1·F3·F4·F5의 상태 변화와 호스트 인계, 파티 서비스의 필드 세션 정리, 전송 전환(T2)이다. 커밋 뒤에 보내야 하므로 5단계 `afterCommit` 도우미를 쓴다.
- `/meta`(공개)에 `relay: { enabled }`, `steam: { identity, app_id }`, `combat_transport_order`(우선순위 문자열, 진단용)를 더한다. 점검 중이면 중계 티켓도 `503 MAINTENANCE`다(phase7 4절).

## 9. 운영(7단계) 변경 요약

| 항목 | 변경 |
|---|---|
| 가드 | G5를 `DEPLOY_STAGE=live`에서만 480 거부로, G2를 "운영에서 `COMBAT_TRANSPORT_ORDER`에 `dev` 거부"로, 신규 G8·G9·G10(14절) |
| 지표·알림 | 4.12절 |
| 정리 작업 | `stale-runs`(10분)에 방치 필드 세션 정리 추가(`FIELD_STALE_SECONDS`). `purge-daily`에 `field_sessions`·`field_session_members` 종료 후 30일, `relay_room_stats` 90일 삭제 추가. `kill_log`(7일)의 FK가 `field_sessions`를 가리키지만 세션이 더 오래 남으므로 삭제 순서 문제가 없다 |
| 정합성 점검 | I4에 "하트비트가 2시간 넘은 활성 `field_sessions`", "활성 멤버 없는 활성 세션" |
| 부하 시험(출시 점검표 15번 보강) | 25방 x 4연결(100 접속)이 10Hz 스냅샷·20Hz 입력을 30분 보내는 스크립트로 플러시 틱 지연 p99, CPU, 메모리, `4008` 비율, 모든 방 RTT p95를 측정한다. 합격: 중계 추가 지연 p99 < 40ms, 메모리 증가 없음, 종료 코드 `4008` 0건 |
| 점검 모드 | 새 티켓·F1 거절(`503 MAINTENANCE`), 기존 연결은 점검 단계에 맞춰 `bye(1001, reconnect:true)` |
| 프록시(Caddy) | `/relay`가 WebSocket 업그레이드를 통과하는지 첫 배포 점검표에 추가(기본 통과). 바이너리 프레임이라 압축·버퍼링 설정을 바꾸지 않는다 |

## 10. 속도 제한 정리 (캐릭터당, 메모리)

| 엔드포인트 | 한도 | 근거 |
|---|---|---|
| T1 티켓 | 10초 3회, 분당 10회 | 정상은 입장·재접속당 1회 |
| T2 전환 | 10초 1회 | 전환은 드물다, 폭주 방지 |
| F1 enter | 초당 1회, 분당 12회 | 맵 이동 속도 |
| F2 GET | 초당 2회 | 폴링 안전망 |
| F3 heartbeat | 2초 1회(클라이언트 5초) | 4단계 R5와 같다 |
| F4 leave | 초당 1회 | |
| F5 claim | 초당 1회 | |
| F6 observe | 5초 1회(클라이언트 10초) | |
| F7 GET | 초당 1회 | |
| E1 `/kills` | 3단계 한도 그대로(초당 6회, 분당 90회) | 6.5절 |
| `/relay` | 4.7절 | |

IP당 한도: 필드·중계 REST는 3단계 경제 버킷(600/분)에 속한다(F3 하트비트를 PC방 여럿이 보내도 걸리지 않는 값).

## 11. 서버 정책 상수 (환경변수, 시작값)

| 이름 | 시작값 | 의미 |
|---|---|---|
| `RELAY_ENABLED` | true | 중계 켜기 |
| `RELAY_MODE` | `embedded` | `embedded` \| `standalone`(후속, 4.2) |
| `RELAY_TICKET_SECRET` | (비밀) | 중계 티켓 서명 키. `JWT_SECRET`과 달라야 한다, 자리표시 값 거부, 로그·pino `redact` 대상 |
| `RELAY_TICKET_TTL_SECONDS` | 60 | 티켓 수명 |
| `RELAY_MAX_CONNECTIONS` / `RELAY_MAX_ROOMS` | 400 / 120 | 4.7 |
| `RELAY_ADMISSION_RATIO` | 0.9 | 새 입장 거절·Steam 우회 기준 |
| `RELAY_HANDSHAKE_PER_MIN_IP` / `RELAY_UNAUTH_PER_IP` / `RELAY_CONN_PER_IP` | 30 / 10 / 8 | 연결 단계 |
| `RELAY_HELLO_TIMEOUT_MS` | 5000 | `hello` 대기 |
| `RELAY_PING_EVERY_MS` / `RELAY_IDLE_TIMEOUT_MS` | 2000 / 6000 | 클라이언트 PING 간격 / 무응답 종료 |
| `RELAY_FLUSH_MS` | 20 | 묶음 송신 틱 |
| `RELAY_FRAME_MAX_BYTES` / `RELAY_PACKET_MAX_BYTES` | 8192 / 6144 | |
| `RELAY_HOST_PKT_PER_SEC` / `RELAY_MEMBER_PKT_PER_SEC` | 300 / 120 | 버스트는 2배 |
| `RELAY_HOST_BYTES_PER_SEC` / `RELAY_MEMBER_BYTES_PER_SEC` | 131072 / 32768 | 버스트는 4배 |
| `RELAY_ROOM_EGRESS_BPS` | 262144 | 방 전체 송신 한도 |
| `RELAY_RELIABLE_QUEUE_BYTES` / `RELAY_RELIABLE_QUEUE_AGE_MS` | 262144 / 5000 | 느린 소비자 |
| `RELAY_BACKPRESSURE_BYTES` | 32768 | 비신뢰 줄 보류 기준 |
| `RELAY_STRIKES_PER_MIN` | 20 | 규칙 위반 종료 기준 |
| `RELAY_HOST_GRACE_MS` | 3000 | 호스트 연결 끊김 후 인계까지 |
| `RELAY_ROOM_EMPTY_GRACE_SECONDS` | 30 | 빈 방 유지 |
| `RELAY_EGRESS_DAILY_BUDGET_GB` | (업체 결정 후) | 비용 알림 기준 |
| `COMBAT_TRANSPORT_ORDER` | `relay,steam` | 5.1 |
| `STEAM_P2P_ENABLED` | true | Steam 전송 후보 허용 |
| `TRANSPORT_SWITCH_COOLDOWN_SECONDS` | 10 | 전환 최소 간격 |
| `STEAM_WEB_API_BASE` / `STEAM_WEB_API_RETRIES` | `https://api.steampowered.com` / 1 | 5.5 |
| `STEAM_BREAKER_FAILURES` / `STEAM_BREAKER_OPEN_SECONDS` | 5 / 30 | 5.5 |
| `DEPLOY_STAGE` | `live` | `test` \| `live` |
| `FIELD_ELECTION_WINDOW_SECONDS` | 5 | 6.2 |
| `FIELD_STALE_SECONDS` | 300 | 방치 세션 정리 |
| `FIELD_OBSERVE_GRACE_SECONDS` | 30 | 호스트 관찰이 없으면 게이트 끔 |
| `FIELD_UNCREDITED_MAX` / `FIELD_CREDIT_SURPLUS` | 24 / 8 | 6.10 |
| `FIELD_POWER_SLACK` | 1.15 | 필드 파티 화력 상한 여유(`PARTY_POWER_SLACK`과 같은 값) |
| `KILL_BURST_FIELD_PER_EXTRA` | 2 | 6.5 |
| `FIELD_CARRY_SLACK` / `FIELD_CARRY_STEP` / `FIELD_CARRY_MIN` | 5 / 0.12 / 0.2 | 6.7 |
| `FIELD_PARTY_XP_FACTOR` / `FIELD_PARTY_DROP_FACTOR` | 1.0 / 1.0 | 경제 손잡이(6.6) |

기존 `PARTY_TRANSPORT`는 폐기(14절). 게임 데이터 값(스폰점 수·리스폰·몬스터 경험치·`monsterLevel`)은 정책 상수가 아니라 `server/data/*.json`에서 읽는다(mapping 4절).

## 12. 테이블 요약 (`server/migrations/0009_relay_field.sql`)

| 표 | 역할 | 주요 인덱스와 이유 |
|---|---|---|
| `party_runs`(확장) | `transport`, `transport_epoch`, `transport_switches`, `transport_order` | 조회는 기존 `party_runs.uuid`(티켓·전환) |
| `field_sessions` | 파티 + 맵 하나의 공유 사냥 세션 | `field_sessions_one_active (party_id, map_id) WHERE active`(enter의 조회·유일성), `field_sessions_stale (last_active_at) WHERE active`(방치 정리), `field_sessions_host`(호스트 권한) |
| `field_session_members` | 멤버·좌석·연결 상태·화력 상한·처치 대조 카운터 | PK `(session_id, character_id)`(하트비트·관찰), `_one_active (character_id) WHERE state <> 'left'`(한 캐릭터 한 세션, 내 세션 조회), `_seat_uq (session_id, seat) WHERE state <> 'left'`(좌석 도장의 근거), `_session`(멤버 목록·화력 합계) |
| `kill_log`(확장) | `field_session_id`, `monster_ref`, `xp_factor` | `kill_log_field_ref_uq (field_session_id, character_id, monster_ref) WHERE monster_ref IS NOT NULL`(이중 보고 차단, 7일 보관 표라 작다) |
| `anomaly_log`(kind 확장) | `field_uncredited`, `field_host`, `relay_abuse` | 기존 |
| `relay_room_stats` | 방 한 번의 대역폭·종료 요약 | `_ended (ended_at)`(일자별 합계·정리), `_room (room_kind, room_ref)` |

- 중계 연결 표는 없다(서버 메모리, 4.1). 필드 채집·드롭·상자 표는 새로 만들지 않는다(6.8).
- 원장·잔액 변경 없음: 필드 세션 처치는 3단계 `xp_ledger('kill')`, `drops`(`drop_claim` 원장)를 그대로 쓴다.
- 이상 기록 `kind`: `field_uncredited`(기여 부채 초과 거절), `field_host`(무효 관찰, 관찰 없음), `relay_abuse`(중계 규칙 위반 폭주).
- 보관: 종료된 `field_sessions`·`field_session_members` 30일, `relay_room_stats` 90일. `kill_log`는 7일(기존)이라 세션 삭제와 FK 순서 문제가 없다.

## 13. 서버 코드 변경 지점 (`server/src/`, dotrpg-backend-coder용)

| 파일(`server/src/`) | 변경 |
|---|---|
| 새 `domains/relay/` | `relayServer.ts`(`/relay` 업그레이드, 한도), `relaySession.ts`(연결, 토큰 버킷, 두 줄 큐), `relayRoom.ts`(방, 좌석, 라우팅 규칙표, 플러시 틱), `relayFrames.ts`(BATCH 코덱, 순수 함수), `relayTicket.ts`(서명·검증·`RelayTicketStore`), `roomDirectory.ts`(인터페이스 + DB 구현: 멤버십 질의, `peerConnected/peerLost/memberRemoved/roomClosed` 이벤트), `relayMetrics.ts` |
| 새 `domains/relay/relayController.ts`, `relayRoutes.ts` | T1·T2 |
| 새 `domains/fieldsessions/` | routes·controller·service·repository·validation(F1~F7), `hostElection.ts`(순수 함수 `electHost`, 4단계 `partyruns`의 인계 판정과 공용화), `fieldKillContext.ts`(처치 맥락 확장), `xpFactor.ts`(6.7 순수 함수), `observe.ts` |
| 새 `domains/transport/` | `pickTransport.ts`, `fallbackService.ts`(T2 공용, `party_runs`·`field_sessions` 둘 다) |
| `domains/kills/killTarget.ts` `resolveFieldTarget` | `sessionId` 인자: 세션 멤버십 검증, `monsterLevel`, 파티 `burst`, 세션 `powerCap`, 부채 게이트, `xpFactor`를 담은 `KillTarget` 반환 |
| `domains/kills/killService.ts`, `killRules.ts` | `xpFactor`를 경험치·드롭 확률에 적용(`rollKillDrops`에 확률 배율 인자), `kill_log` 새 컬럼 기록, `KILL_DUPLICATE` |
| `domains/kills/killValidation.ts` | `session_id`, `monster_ref`, `run_id`와의 배타(`refine`) |
| `domains/partyruns/` | `partyRunService`: R1에 `pickTransport`·필드 세션 닫기, R3 자동 입장(`peerConnected` 훅), R6 조건에 중계 사실, 서버 주도 인계(`hostLost` 훅), `runView.ts`에 `transport`·`host_key`(R2) |
| `domains/party/partyService.ts` | P9·P10·`closeParty`에 필드 세션 정리 훅 |
| `domains/chat/wsServer.ts`, `wsProtocol.ts` | `hello.caps`, `field.changed` 프레임, `realtimeNotifier`에 `fieldChanged` |
| `domains/chat/sanctionService.ts` | 제재 시 중계 연결 종료 호출 |
| `domains/auth/steamProvider.ts` | 5.5절 S2~S7 |
| `gamedata/economyData.ts`, `loader.ts` | `maps.json fieldSpawns[].monsterLevel` 파싱(기본 1) |
| `config/env.ts` | 11절 변수, 가드 G2·G5·G8~G10, `PARTY_TRANSPORT` 제거 |
| `ops/metrics.ts`, `ops/alertRules.ts`, `ops/jobs/*` | 4.12·9절 |
| `db/pool.ts` | `afterCommit`(5단계에서 추가됨) 재사용 |
| `routes` 등록, `/meta` | 8절 |

## 14. 개발 전용과 출시 가드, PLAN_SERVER와 달라진 점

**개발 전용(출시물에서 제외)**: `UdpTransport`(`UNITY_EDITOR || DEVELOPMENT_BUILD`에서만 컴파일), `-dotrpgParty` 명령줄(같은 조건), `STEAM_AUTH_MODE=mock`, 개발 로그인 계정. 서버는 아래 가드로 운영 기동을 거부한다. 개발 전용 기능이 출시 경로에 섞이는 일이 없게 가드로 증명한다.

| # | 가드(기동 거부, phase7 9.1에 더한다) |
|---|---|
| G2(변경) | `NODE_ENV=production`이면 `COMBAT_TRANSPORT_ORDER`에 `dev`가 있으면 거부, `relay`가 없고 `RELAY_ENABLED=false`이면 `steam`만 남기는 구성은 경고(Steam 없는 멤버가 입장 불가) |
| G5(변경) | `DEPLOY_STAGE=live`에서 `STEAM_APP_ID=480` 거부. `test`에서는 허용 |
| G8 | `RELAY_ENABLED=true`이면 `RELAY_TICKET_SECRET` 필수, 최소 32자, `JWT_SECRET`과 같으면 거부, 자리표시·반복 문자 거부 |
| G9 | `DEPLOY_STAGE=live`에서 `AUTH_DEV_ENABLED=true`는 `ALLOW_DEV_AUTH_IN_PRODUCTION`이 함께 있을 때만(기존 G3와 합친다) |
| G10 | 운영(`NODE_ENV=production`)에서 `RELAY_FRAME_MAX_BYTES` 등 한도 값이 0 이하이거나 `RELAY_MAX_CONNECTIONS + WS_MAX_CONNECTIONS`가 서버 메모리 설정(`NODE_MAX_OLD_SPACE_MB` 등)으로 감당할 수 없다고 판단되는 값이면 경고(부하 시험 후 확정) |

**PLAN_SERVER와 달라진 점**
- §1 표 "실시간 전투 연결: 방장 PC가 몬스터를 계산하고 Steam P2P가 무료로 중계, 서버 코드 거의 없음"은 **우리 서버 중계(`/relay`)가 기본이고 Steam P2P는 대안 전송**으로 바뀐다. 서버는 이제 전투 연결 바이트를 나른다(내용은 보지 않는다). 전용 Unity 던전 서버를 쓰지 않는 결정은 그대로다.
- §6 기술의 "WebSocket(채팅·매칭 알림·파티 상태)"에 **전투 중계 WebSocket**이 더해진다(별도 경로, 같은 프로세스).
- §7 확장 단계: 시작 구성(VM 한 대, Node 하나)이 중계 트래픽을 함께 진다(4.11절 계산: 100명이면 송신 약 1.1MB/s). 확장 2단계의 "WebSocket 서버를 API와 분리"가 이 중계를 포함한다.
- §9 단계 표에 **8단계(전투 중계 + 필드 파티 사냥)**를 더한다(사용자 지시).
- 4단계와의 차이: `PARTY_TRANSPORT` 폐기(`COMBAT_TRANSPORT_ORDER`), `STEAM_REQUIRED` 삭제, Input 채널 신뢰화, R3 자동 입장, 서버 주도 호스트 인계, `RunView.transport`, Steam 인증 강화(5.5), 가드 G5의 기준 변경.

## 15. 구현 순서와 끝났다는 기준

각 단계는 **그 단계에 속한 것을 출시 품질로** 끝낸다(남용 방어·끊김 복구·지표·한도 포함). 친구와의 시험은 R1부터 가능하다.

### R1: 중계 + 던전 (친구와 던전)
- 서버: 마이그레이션 0009의 `party_runs` 확장과 `relay_room_stats`(필드 표는 R2에서), `domains/relay/*`, T1(`kind=run`), R1·R3·R6·R8 훅, `/ws hello.caps`는 무시, 부하 시험 스크립트 초안. `pickTransport`는 `relay`만 반환(Steam 후보 없음).
- 클라이언트: `ITransport` 확장, `RelayTransport`, `PartyRunSession.EnsureNet/TakeOver`를 `CombatTransportFactory`로, `PartyNet.End(keepTransport)`·재동기화, Input 신뢰화(mapping 6절).
- 사전 조건: 시험 서버가 인터넷에서 `wss://`로 접근 가능(phase7 부록 A 또는 집 서버 + TLS 프록시), 개발 계정(운영자 발급, phase7 결정).
- 끝났다는 기준(시나리오):
  1. 두 계정이 서로 다른 PC에서 인터넷으로 같은 던전을 클리어하고 각자 보상을 받는다.
  2. 4인 방(두 PC + 창 둘 또는 네 PC)에서 10분 이상 끊김·`SLOW_CONSUMER` 없음.
  3. 호스트 프로세스 강제 종료 -> 10초 안에 다른 멤버가 호스트를 이어받고 전투 지속.
  4. 멤버 네트워크 차단 30초 후 복구 -> 같은 좌석으로 재접속·재동기화.
  5. 서버 프로세스 재시작 -> 진행 중 판이 이어진다(재접속 후 처치 보고가 계속 받아진다).
  6. 다른 방의 티켓·재사용 티켓·만료 티켓이 모두 4004로 거절된다. 멤버가 멤버에게 보낸 프레임이 전달되지 않는다.

### R2: 필드 세션 + 필드 파티 사냥 (친구와 필드)
- 서버: 0009의 `field_*`·`kill_log` 확장·kind 확장, F1~F5·F7, E1(세션 멤버십·`monster_ref`·파티 속도·세션 화력·`xp_factor`), 호스트 선출·인계, 파티 서비스 훅, `field.changed`, T1(`kind=field`). **F6 관찰·게이트는 R4**(R2 동안 게이트는 꺼진 상태 = 관찰 없음 경로와 같다).
- 클라이언트: `FieldSession`, `PartyNet` 필드 모드, `EnemySpawner` 모드, `EnemyController.Shared`, `OnlineEconomy.ReportKill` 확장, AI 동료 숨김(mapping 6절).
- 끝났다는 기준: ① 두 계정이 파티를 맺고 숲에서 몬스터를 함께 잡아 각자 경험치·드롭을 받는다 ② 한 명이 뒤늦게 숲에 들어오면 같은 몬스터가 보인다 ③ 호스트가 마을로 가면 남은 사람의 몬스터가 사라지지 않는다 ④ 파티를 나가면 로컬 몬스터로 복귀 ⑤ 같은 몬스터를 두 멤버가 각자 한 번씩 보고해도 이중 보상이 아니다(멤버 각자 1회) ⑥ 한 멤버 보고의 `monster_ref` 중복은 `409`.

### R3: Steam (시험 앱 ID 480)
- 서버: 5.5절 S1~S7, `COMBAT_TRANSPORT_ORDER`/`pickTransport`의 `steam`, T2, `/ws hello.caps`, `DEPLOY_STAGE`, `/meta`.
- 클라이언트: Steamworks.NET 패키지, `DotRPG.Steam` asmdef, `STEAMWORKS_NET` 심볼, `steam_appid.txt`(480), `SteamAuth`, `SteamP2PTransport`, `CombatTransportFactory`의 전환(mapping 7절).
- 시험 순서(두 PC, 서로 다른 Steam 계정): ① `STEAM_AUTH_MODE=web_api`, `STEAM_APP_ID=480`, `DEPLOY_STAGE=test`로 Steam 로그인 성공(`steam.selfcheck` 로그로 키 통과 확인, 안 되면 4단계의 확인 항목 결과를 문서에 기록) ② `COMBAT_TRANSPORT_ORDER=relay,steam`에서 정상(중계) ③ `steam,relay`로 바꿔 Steam P2P로 던전·필드 ④ Steam 연결을 방해(방화벽)해 `steam -> relay` 자동 전환 ⑤ 중계를 막아 `relay -> steam` 전환.
- 끝났다는 기준: 위 5개가 사람 손 조작 없이 통과.

### R4: 출시 품질 마감
- 서버: F6 관찰·게이트, 레벨 격차 감쇠 검증(데이터에 높은 레벨 필드가 없어도 단위 시험), 지표·알림·정합성 점검 확장, 정리 작업, 가드 G8~G10, 부하 시험 합격(9절), 혼돈 시험(패킷 손실·지연 시뮬레이션, 호스트·멤버 반복 강제 종료, 중계 재시작), 보안 점검표 갱신.
- 클라이언트: 기여 비트마스크 보고, 카드 검증(`gear_hash`), 핑 표시, 전환 UX 문구, 빌드 스크립트의 `steam_appid.txt` 제외(출시 빌드).
- 끝났다는 기준: 4개 단계의 시나리오 전부 통과 + 출시 점검표(phase7 9.2) 갱신본 통과.

## 16. 결정 대기

| # | 항목 | 선택지 | 권장 |
|---|---|---|---|
| 1 | 공유 필드의 범위 | (a) **파티 단위 공유**: 같은 파티원끼리 같은 맵에서 몬스터를 공유, 다른 파티·솔로는 서로 안 보임 (b) 서버 전체가 같은 맵의 몬스터를 공유(메이플식): 몬스터를 서버가 계산하는 전용 필드 서버가 필요(PLAN_SERVER §1의 "전용 서버 안 씀" 결정을 뒤집는다) | (a). 같은 구조로 지금 만들 수 있고 "친구와 같이 사냥"이라는 요구가 충족된다. (b)는 이 단계 범위 밖 |
| 2 | 전송 우선순위 기본값 | (a) `relay,steam`(중계 우선, 모든 플랫폼 동일, 인계 빠름, 서버 송신 비용 약 40MB/인/시간) (b) `steam,relay`(Steam 자격이 되는 방은 P2P, 서버 송신 0, 모바일·비 Steam은 중계) | 출시 기본값은 (a). 4.11절의 비용이 작고 중계가 더 예측 가능하다. Steam 시험 서버는 (b)로 두고 지표를 비교한 뒤 비용이 문제가 되면 운영에서 (b)로 바꾼다(설정 한 줄) |
| 3 | 파티 세션의 레벨 격차 경험치 감쇠(PLAN_SERVER 결정 대기) | (a) 켠다(6.7절 값, 현재 데이터에서는 움직이지 않고 높은 레벨 필드 도입 시 자동) (b) 켜지 않는다 | (a). 켜 두어도 지금 아무 영향이 없고, 높은 레벨 필드가 생기는 날 얹혀가기 경로가 이미 막혀 있다 |
| 4 | 방장이 세션에 늦게 들어올 때 호스트 | (a) **고정 호스트**: 시작 후 5초 선출 창이 지나면 호스트가 떠날 때까지 유지(전투 중 교체 없음) (b) 방장이 들어오면 항상 방장으로 인계 | (a). 인계는 몬스터 상태를 이어받는 짧은 정지를 부른다. 선출 창이 같이 이동하는 파티의 입장 순서 뒤섞임을 해결한다 |
| 5 | 다인 필드 세션의 AI 동료 | (a) 숨긴다(활성 멤버 2명 이상) (b) 호스트가 빈자리 수만큼 AI를 유지, 인원 변동에 따라 증감 | (a). 서버가 필드 AI를 모르고, 호스트 상태가 단순해진다. 던전의 "시작 때 고정" 규칙과 같은 일관성 |
| 6 | Steam 직접 경로(IP 노출) | (a) Steam 기본 그대로(직접 경로 허용: 지연이 낮고 파티원에게 IP가 보일 수 있음) (b) SDR 릴레이만(`P2P_Transport_ICE_Enable` 끔: IP 비노출, 경로가 길어져 지연 증가) | (a). 같은 파티원은 Steam 친구·모집 상대이고 중계 전송이 기본이라 Steam은 선택 경로다. 개인정보 처리방침에 IP 노출 가능성을 명시한다(phase7 결정 6과 함께) |
| 7 | 시험 앱 ID(480) 허용 방식 | (a) `DEPLOY_STAGE`(test/live) 신설, 시험 서버는 `NODE_ENV=production` + `DEPLOY_STAGE=test` (b) 시험은 `NODE_ENV=staging`으로 가드를 전부 끈 서버 | (a). 시험 서버도 TLS·시크릿·관리자 바인드 가드를 그대로 지키고 480만 허용한다 |

## 17. 알려진 한계와 확인할 것

| 항목 | 내용 | 조치 |
|---|---|---|
| TCP 머리 막힘 | 손실 구간에서 신뢰·비신뢰가 같이 지연 | 4.6절의 지표로 판단, 필요하면 UDP 전송 추가(`ITransport` 뒤) |
| WebGL | `ClientWebSocket`이 없다(`ChatSocket`과 같은 제약) | 출시 대상에 WebGL이 없다. 필요해지면 JS 플러그인 구현을 `RelayTransport` 뒤에 둔다 |
| 마을 유령(`NetPresence`) | 이 단계는 전투 방·필드 세션만 다룬다. 마을에서 서로 보이게 하려면 `kind=town` 방 종류를 더하면 되고 중계의 채널 표만 늘어난다 | 후속 |
| 480 + 일반 Web API 키 | `AuthenticateUserTicket`이 일반 키에 통하는지 | R3 첫 시험에서 `steam.selfcheck`로 확인하고 결과를 이 절에 기록 |
| 단일 프로세스 | 중계 100방 부근이 코어 하나의 한계 | `RELAY_MODE=standalone`(티켓 오프라인 검증 + `RoomDirectory` NOTIFY) |
| 호스트 PC의 신뢰 | 호스트는 몬스터를 계산하므로 보상을 몰아주려 할 수 있다 | 4단계 13절과 같다: 모든 보상은 멤버별 서버 판정(공급·화력·속도), 6.10 대조 |
| 서버 위치 | 지역이 멀면 중계 지연이 커진다 | S3 업체 결정 때 플레이어 지역 기준, 전송 우선순위(결정 2)로 보완 |
