# 서버 5단계 게임 값 대응표 (채팅·친구)

게임 쪽 정의(C# 필드·함수) <-> 서버 테이블·컬럼 <-> API. API는 [phase5_api.md](phase5_api.md), 스키마는 `server/schema.sql`(`0006_chat_social.sql`). 앞 단계: [phase4_mapping.md](phase4_mapping.md).
값(수치·목록)은 여기 복사하지 않고 `server/data/*.json` 이름과 C# 출처로만 참조한다. C# 출처는 2026-10-04 코드 기준이다.

## 1. C# 동작 <-> 서버 판정

### 1.1 채팅 규칙 (`Net/ChatService.cs` `ChatRules`, `MockChatService.Send`)

| C# 동작 | 지금 (오프라인 목업) | 서버 판정 / API | 비고 |
|---|---|---|---|
| `Send`: 빈 문장 `return ""` | 조용히 무시 | `TEXT_EMPTY` | 클라이언트 미리보기가 먼저 막는다 |
| `Send`: `channel == System` 거절 | "시스템 채널에는 쓸 수 없습니다." | 서버 채널 enum에 `system`이 없다(`CHANNEL_INVALID`) | 시스템 줄은 서버 `chat.sys`나 로컬 `PostSystem`뿐 |
| `Send`: 길이 `MaxLength`를 넘으면 **자른다** | 조용히 자름 | 서버는 자르지 않고 `TEXT_TOO_LONG`로 거절 | 클라이언트가 보내기 전에 자르므로 정상 흐름에서는 없다. 한도는 `chat.json maxLength` |
| `Send`: `clock < mutedUntil` | "같은 말을 반복해 N초 동안 채팅이 막혔습니다." | `MUTED_REPEAT`(`muted_until`) | 문구는 서버가 같은 형식으로 만든다 |
| `Send`: `clock - lastSent < MinInterval` | "채팅은 1초에 한 번만 보낼 수 있습니다." | `RATE_LIMITED`(`retry_after_ms`) | 서버 간격 = `minIntervalSeconds x CHAT_INTERVAL_TOLERANCE` |
| `Send`: 귓속말 상대 비어 있음 | "귓속말 상대를 적어 주세요. (/w 이름 내용)" | `TARGET_REQUIRED` | |
| `Send`: `blocked.Contains(whisperTo)` | "{name}님은 차단 중입니다." | `TARGET_BLOCKED`(내가 차단) | 상대가 나를 차단한 경우는 서버가 조용히 버리고 ack만 준다(mock에는 없던 경우) |
| `Send`: `repeats >= RepeatLimit` -> `mutedUntil` | 3번째는 보내고 이후 `MuteSeconds` 금지 | 같은 시점. `repeatLimit`, `muteSeconds` | 시스템 채널이 아닌 모든 채널에 걸친 연속 반복(`lastText` 하나) |
| `Send`: `ChatRules.Filter(text)` | 금칙어를 `*`로 가림(클라이언트 목록 6개) | 서버 `maskBannedWords` + 우회 변형은 `MESSAGE_BLOCKED` | 클라이언트 `Banned`는 미리보기 전용. 서버 파일 `banned_words.json` 초기 내용으로 옮긴다(결정 대기 5) |
| `Send`가 `string`을 즉시 반환(null = 보냄) | 동기 | **비동기**: `chat.ack` / `chat.err` | 4.2의 `SendFailed` 이벤트 |
| `Deliver(ChatLine)` | 다른 창·서버 푸시가 줄을 넣는 입구 | `chat.msg` 수신 -> `Deliver` | 차단 필터는 서버가 먼저 걸러 오므로 클라이언트 `Add`의 필터는 중복 방어 |
| `Add`: `lines.Count > ChatRules.Keep` | 120줄 유지 | 변경 없음 | 서버 따라잡기 한도(일반 20, 파티 50, 귓속말 50)보다 크다 |
| `PostSystem(text)`, `ChatView.OnToast`의 "[알림]" | 로컬 시스템 줄 | 변경 없음(로컬 표시). 서버 공지는 `chat.sys` | 강화 +10 알림은 서버 응답을 클라이언트가 해석해 지금처럼 로컬로 올린다 |
| `QuickSignals` 5개 | 파티 채널로 `Send` | 일반 파티 채팅과 같다(`chat.send`) | 서버는 신호를 특별 취급하지 않는다. 같은 신호를 3번 연속 누르면 반복 금지가 걸리는 현재 동작도 그대로 |
| `Tick(dt)`: 목업 마을 잡담 | 25~45초마다 가짜 대사 | 서버 구현에는 없음 | `ServerChatService.Tick`은 메시지 큐 비우기·재연결 타이머·ping |
| `IsOnline` | `false` | 서버 구현은 `true`(WS 결합 상태가 아니라 "서버 구현인가") | `NetPresence`가 이 값으로 개발용 중계를 끈다(4.5) |

### 1.2 친구·차단·신고 (`MockChatService`, `UI/SocialScreen.cs`)

| C# 동작 | 지금 | 서버 판정 / API | 비고 |
|---|---|---|---|
| `AddFriend(name)` | 즉시 목록에 추가(접속 중, "해골 숲 옆 작은 마을" 가짜 위치) | POST `/friends/requests` -> 상대 수락 후 친구 | 즉시 추가가 아니라 **요청 중**이 된다. 결정 대기 3 |
| `RemoveFriend(name)` | 목록에서 삭제 | DELETE `/friends/{id}` | |
| `Friends`(`ChatFriend.online`, `where`) | `online = i % 2 == 0` 가짜 값 | GET `/friends`의 `online`, `where`와 WS `friends.presence` | `where`는 `{map_id, activity}` -> 한국어 위치 문구는 클라이언트가 만든다(2.3) |
| `IsFriend(name)` | 이름 비교 | 로컬 캐시(`GET /friends`) 조회 | 이름이 같아도 다른 캐릭터일 수 있으므로 id로 비교한다 |
| `Block(name)` | 차단 + 친구에서 제거 | PUT `/blocks/{character_id}` | 서버가 친구 관계·요청·초대도 같이 닫는다. 계정 단위 |
| `Unblock(name)` | 해제 | DELETE `/blocks/{id}` | 끝난 친구 관계는 복구되지 않는다 |
| `IsBlocked(name)`, `Blocked` | 이름 집합 | GET `/blocks` 캐시 | |
| `RecentSpeakers()` | 최근 줄의 말한 사람 이름 | 줄의 `from.id`, `to.id`로 구성한 사람 목록 | 이름이 아니라 `(id, name)` |
| `Report(name, reason)` -> 한국어 상태 줄 | 클라이언트 로그 마지막 20줄을 목록에 보관 | POST `/reports`의 `line_count`로 문구를 만든다 | 증거는 서버가 수집한다. 클라이언트는 줄을 보내지 않는다. `ChatReport.log`는 온라인에서 쓰지 않는다 |
| `ChatRules.ReportLines`(20) | 첨부 줄 수 | `chat.json reportLines` | 서버가 같은 값으로 수집 |
| `SocialScreen`의 "파티 초대" | 토스트만("응답 없음") | POST `/characters/{uuid}/party/invites` | 방장만 보낼 수 있다. 방장이 아니거나 파티가 없으면 에러 코드로 안내(6절) |

### 1.3 파티 알림 (4단계 폴링 -> WebSocket 힌트, `Net/PartyClient.cs`, `PartyRunSession.cs`)

| C# 동작 | 지금 | 5단계 | 비고 |
|---|---|---|---|
| `PartyClient.Update`: `PollSeconds`(1.5)/`IdlePollSeconds`(5) | 항상 폴링 | WS 연결 중에는 15초(`ready.limits.party_poll_seconds`), 끊김 중에는 기존 값 | 폴링 코드는 지우지 않는다(안전망) |
| `PartyClient.PollNow()` | 요청 직후 즉시 폴링 | WS `party.changed(party)`를 받으면 호출 | |
| `PartyRunSession.Update`: `pollTimer`(1초, gathering) | 판 상태 폴링 | WS `party.changed(run)`를 받으면 `Fetch()`를 바로 호출 | gathering 폴링은 안전망으로 유지 |
| `PartyRunSession.Heartbeat`(5초), `ClaimHost`, 정산 폴링 | REST | **변경 없음** | 판 생존 판정과 채팅 연결을 분리한다(api 9절) |

## 2. 필드 대응

### 2.1 채널
| C# `ChatChannel` | 와이어 `channel` | 비고 |
|---|---|---|
| `General` | `general` | |
| `Party` | `party` | |
| `Whisper` | `whisper` | |
| `System` | (없음) | `chat.sys` 프레임은 `ChatChannel.System` 줄로 만든다 |

### 2.2 줄 (`ChatLine`)
| C# 필드 | 와이어 | 비고 |
|---|---|---|
| `channel` | `chat.msg.channel` | |
| `from` | `chat.msg.from.name` | 내가 보낸 줄은 `Game.Session.Journal.PlayerName` 대신 서버 `ready.character.name` |
| `to` | `chat.msg.to.name`(귓속말) | |
| `text` | `chat.msg.text` / `chat.ack.text` | 내 줄은 ack의 가려진 문장으로 교체 |
| `time` | (로컬 시계) | `at`은 표시에 쓰지 않는다(시간 표시가 없다). 필요하면 `server_time` 오차 보정 |
| `mine` | 수신이면 `false`, 보낸 줄은 낙관적으로 `true` | |
| (새) `fromId`, `toId` | `from.id`, `to.id` | 신고·차단·친구·초대 대상 지목 |
| (새) `cid` | 내가 보낸 줄의 `cid` | `chat.ack`/`chat.err` 매칭, 재전송 |
| (새) `seq` | `chat.msg.seq`, `chat.ack.seq` | 커서. `MiniJson.Int`는 `int`로 반올림하므로 `MiniJson.Num`을 `long`으로 받는다 |

### 2.3 친구 (`ChatFriend`)
| C# 필드 | 와이어 (GET `/friends`, `friends.presence`) | 비고 |
|---|---|---|
| `name` | `character.name` | |
| `online` | `online` | |
| `where` (문자열) | `where.map_id`, `where.activity` | `field`는 맵 이름, `dungeon`/`raid`는 "던전 중"/"레이드 중". 맵 id -> 한국어 이름은 기존 맵 이름 조회를 쓴다(`MapRegistry`에 이름 함수가 보이지 않는다, 확인 필요) |
| (새) `id` | `friends[].id` | 친구 관계 uuid. 삭제 경로에 쓴다 |
| (새) `characterId`, `cls`, `level` | `character.id`, `class`, `level` | 귓속말·초대 대상 지목, 목록 표시 |

### 2.4 신고 사유 (`ChatRules.ReportReasons` <-> `reports.reason`)
| C# 문자열(라벨) | 서버 코드 |
|---|---|
| 욕설·비하 | `abuse` |
| 도배 | `spam` |
| 광고·사기 | `scam_ad` |
| 불법 프로그램 의심 | `cheat` |
| 기타 | `other` |

C#은 문자열 배열이므로 서버 코드 배열(`ReportReasonCodes`, 같은 순서)을 나란히 두고 `SocialScreen.BuildReport`의 버튼이 같은 인덱스의 코드를 보낸다. 라벨은 클라이언트 UI 문구이고, 코드 5개는 `0006`의 `CHECK`와 같다.

### 2.5 서버 에러 코드의 한국어 (클라이언트)
`PartyClient.Explain`과 같은 방식으로 `ChatClient.Explain(ApiResult)`를 둔다. `message`는 서버가 한국어로 주므로 아래 표의 특별 처리만 한다.

| code | 클라이언트 동작 |
|---|---|
| `YOU_BLOCKED_TARGET` | "차단한 사람입니다. 먼저 차단을 해제하세요." |
| `ALREADY_FRIENDS` | 친구 목록을 다시 받는다(`GET /friends`) |
| `FRIEND_LIST_FULL`, `BLOCK_LIST_FULL`, `TOO_MANY_PENDING` | 서버 `message` 표시 |
| `REREQUEST_COOLDOWN` | "잠시 뒤에 다시 요청해 주세요."(`errors.retry_after_sec`) |
| `REQUEST_NOT_FOUND`, `REQUEST_EXPIRED`, `FRIEND_NOT_FOUND`, `BLOCK_NOT_FOUND` | 목록을 다시 받는다 |
| `REPORT_NO_CONTEXT` | "최근에 만난 적 없는 모험가는 신고할 수 없습니다." |
| `REPORT_LIMIT` | "신고는 잠시 후에 다시 할 수 있습니다." |
| `TARGET_OFFLINE`(초대) | "접속 중이 아닌 모험가입니다." |
| `NOT_IN_PARTY`, `NOT_LEADER`(초대) | "파티를 만들고 방장이 초대할 수 있습니다." |
| `TARGET_BUSY`(초대) | "다른 파티나 대기열에 있는 모험가입니다." |
| `INVITE_COOLDOWN` | "초대는 잠시 후에 다시 보낼 수 있습니다." |
| `INVITE_EXPIRED` | "초대가 만료되었습니다." |
| 4단계 코드(`POWER_TOO_LOW`, `PARTY_FULL` 등) | 4단계 `PartyClient.Explain`을 그대로 쓴다(초대 수락이 같은 검사를 지난다) |

## 3. 서버가 읽는 게임 값 (`server/data/chat.json`, 새 파일)

Unity 에디터 내보내기 메뉴(3단계 4절과 같은 도구)에 더한다. 내보내면 데이터 버전 해시가 바뀐다(채팅·소셜 경로는 데이터 버전을 검사하지 않으므로 이 파일만 바뀌어도 오래된 클라이언트가 막히지는 않는다. 값이 어긋나는 것은 서버 값이 최종이라 안전하다).

| `chat.json` 필드 | C# 출처 | 서버 용도 |
|---|---|---|
| `maxLength` | `ChatRules.MaxLength` | 길이 검사, `ready.limits.max_length`, `chat_messages.text` 이상값 CHECK의 기준 |
| `minIntervalSeconds` | `ChatRules.MinInterval` | 최소 간격 x `CHAT_INTERVAL_TOLERANCE` |
| `repeatLimit` | `ChatRules.RepeatLimit` | 반복 판정 |
| `muteSeconds` | `ChatRules.MuteSeconds` | 반복 금지 시간 |
| `reportLines` | `ChatRules.ReportLines` | 증거 줄 수 |
| `reportReasons` | `ChatRules.ReportReasons` + 코드(2.4) | 신고 사유 목록과 코드 검증 |

내보내지 않는 값: `ChatRules.Keep`(클라이언트 보관 줄 수), `QuickSignals`(문구, 서버가 특별 취급하지 않음), `Banned`(미리보기 전용, 서버 운영 파일이 최종). `ChannelName`, `ChannelColor`는 표시 전용이다.
운영 파일(게임 데이터가 아님): `server/data/banned_words.json`(4단계와 공유, 현재 파일 없음).

서버 정책 상수(길이·간격 외 운영 값)는 api 11절의 환경변수다.

## 4. 클라이언트가 바꿔야 할 지점 (파일:함수)

경로는 `Assets/Scripts/Runtime/` 아래. "새 파일"은 새로 만든다. WebSocket은 `System.Net.WebSockets.ClientWebSocket`을 쓴다(프로젝트 `apiCompatibilityLevel: 6`(.NET Standard 2.1)에서 스탠드얼론 빌드는 지원한다. WebGL 빌드는 지원하지 않으므로 Steam 데스크톱 전용이라는 전제를 문서에 못 박는다). 수신은 백그라운드 `Task`가 `ConcurrentQueue`에 쌓고 `Tick`(메인 스레드)이 비운다.

### 4.1 연결·세션
| 파일 | 변경 |
|---|---|
| 새 파일 `Net/ChatSocket.cs` | `ClientWebSocket` 래퍼: 연결, `hello` 전송, 15초 `ping`, 수신 큐, 백오프 재연결(api 3.7), `token.expiring` -> `ApiClient.RefreshOnce` -> `auth`, close 코드별 동작. 서버 주소는 `ApiClient`의 서버 키(`dotrpg.server`)에서 `http`->`ws`로 바꿔 만든다. 정적 이벤트 `PartyChanged(scope, runId)`, `InviteReceived`, `FriendsChanged`, `Sanction` |
| 새 파일 `Net/ServerChatService.cs` | `IChatService` 서버 구현. `ChatSocket`의 메시지를 `Lines`, `Friends`, `Blocked`에 반영. 보낸 줄은 낙관적으로 추가하고 `cid`를 보관(ack 대기 목록, 30초 재전송). `lastSeq` 관리 |
| `Net/PartyFinderService.cs` `OnlineServices` | `Chat` setter가 교체될 때 `event Action ChatChanged`를 올린다. `AttachChat()`/`DetachChat()` 도우미: 서버 구현 주입과 목업 복귀 |
| `Core/GameFlow.cs` `EnterOnline` | `PartyClient.AttachOnline()` 다음에 `OnlineServices.AttachChat()`(캐릭터 id 전달, `hello`) |
| `Net/OnlineSession.cs` `LeaveCharacter` | `PartyClient.DetachOnline()` 옆에 `OnlineServices.DetachChat()`(연결을 정상 종료 1000) |
| `Net/OnlineSession.cs` `Logout` | `DetachChat()` 보장(토큰을 지우기 전에 소켓 종료) |
| `Net/ApiClient.cs` | 서버 기본 주소 읽기 접근자(`BaseUrl`)를 공개해 `ChatSocket`이 같은 서버를 쓴다 |
| `Net/MiniJson.cs` | 변경 없음. 단 `seq`는 `MiniJson.Num`을 `long`으로 변환해서 읽는다 |

### 4.2 `IChatService`/`ChatLine`/`ChatFriend` (`Net/ChatService.cs`)
| 위치 | 변경 |
|---|---|
| `ChatLine` | `fromId`, `toId`, `cid`, `seq` 추가(2.2). 기존 필드는 그대로 |
| `ChatFriend` | `id`, `characterId`, `cls`, `level` 추가. `where`는 표시 문자열로 유지(`ServerChatService`가 `where.map_id`/`activity`에서 만든다) |
| 새 `ChatPerson` | `{ id, name }`. 서버 구현에서는 id가 채워지고 목업은 id가 null |
| `IChatService.Send` | 반환값 의미를 "서버에 보내도 되는 상태인가"로 좁힌다(로컬 검사: 연결 끊김, 비어 있음, 길이, 간격 미리보기). 서버 거절은 새 이벤트 `SendFailed(string cid, string message)`로 온다 |
| `IChatService` 추가 | `event Action<string,string> SendFailed`, `void Refresh()`(친구·차단 다시 받기), `IEnumerable<ChatPerson> RecentPeople()`(`RecentSpeakers()` 대체), 요청 목록 `IReadOnlyList<ChatRequest> RequestsIn/RequestsOut`, 비동기 변형 `void AddFriend(ChatPerson p, Action<string> done)`, `void RemoveFriend(ChatFriend f, Action<string> done)`, `void Block(ChatPerson p, Action<string> done)`, `void Unblock(string name, Action<string> done)`, `void Report(ChatPerson p, int reasonIndex, Action<string> done)`(상태 문구를 콜백으로), `void RespondFriendRequest(ChatRequest r, bool accept, Action<string> done)` |
| `MockChatService` | 새 비동기 변형은 즉시 `done`을 호출하는 얇은 구현. 기존 동기 메서드(`AddFriend(string)` 등)는 호출부가 바뀔 때까지 남긴다. 오프라인 동작은 바꾸지 않는다 |
| `ChatRules` | 서버 구현은 `ready.limits`로 `MaxLength`, `MinInterval`을 덮어쓸 수 있게 정적 상수를 읽기 전용 속성으로 바꿀지 검토(지금은 `const`). 값이 같으면 변경 불필요 |
| 오프라인 저장 | `OnlineServices.SaveSocial()`은 `chat is MockChatService`일 때만 저장하므로 서버 구현에서는 아무 일도 하지 않는다(이미 안전). `SettingsData.chatFriends/chatBlocked`는 오프라인 전용으로 유지 |

### 4.3 `UI/SocialScreen.cs`
| 함수 | 변경 |
|---|---|
| `enum View`, `ListNames`, `BuildList`의 `% 3` | 온라인일 때 `Requests`(받은 요청 수락·거절, 보낸 요청 취소)를 목록 보기에 추가(`% 4`). 받은 요청이 있으면 제목 옆에 개수 표시. 오프라인(목업)은 3개 그대로 |
| `Show` | `Service.Refresh()`(`GET /friends`, `GET /blocks`)를 호출하고 `SocialChanged`를 받을 때 `Rebuild`(지금은 사용자 동작 뒤에 수동 `Rebuild`) |
| `BuildList` Friends | `friend.where` 사용은 그대로. 온라인 표시는 `online`, `where`를 서버 값으로 |
| `BuildList` Recent | `Service.RecentSpeakers()` -> `RecentPeople()`. `OpenPerson`의 인자를 이름에서 `ChatPerson`으로 |
| `OpenPerson`, `BuildPerson` | 내부 `person`을 `ChatPerson`으로. 친구 추가 버튼은 요청 보내기(비동기 `done` 토스트 "친구 요청을 보냈습니다."), 이미 요청 중이면 비활성 표시. 친구 삭제·차단·해제는 비동기 `done` 뒤에 `Rebuild` |
| `BuildPerson`의 "파티 초대" | `GameEvents.RaiseToast("... 응답 없음")` -> `PartyClient.Invite(person.id, done)`(새, 4.6). 오프라인은 기존 토스트 |
| `BuildReport` | 사유 버튼이 인덱스를 `Service.Report(person, index, done)`로 넘긴다. 확인 문구의 "최근 대화 {ReportLines}줄이 함께 전달됩니다"는 "서버가 최근 대화를 함께 전달합니다"로 바꿔도 의미가 같다(온라인) |
| `Close`, `DevOpenPerson` | 변경 없음 |

### 4.4 `UI/ChatView.cs`
| 함수 | 변경 |
|---|---|
| `Create` | `Service.Received += OnReceived`가 만든 시점의 서비스에 묶인다. 온라인 접속 뒤 `OnlineServices.Chat`이 교체되므로 `OnlineServices.ChatChanged`를 구독해 이전 서비스 구독 해제 + 새 서비스 구독 + `Redraw`. `Service.SendFailed`도 구독해 토스트(`GameEvents.RaiseToast`) + 해당 `cid` 줄 제거 |
| `OnDestroy` | `ChatChanged`·`SendFailed` 구독 해제 |
| `SendTyped` | `Service.Send(...)`가 비동기 거절을 `SendFailed`로 받는 점만 다르다. `/w` 명령은 그대로 이름을 보낸다(서버가 `to_name`으로 해석). 연결이 없으면 `Send`가 로컬 오류 문자열을 돌려준다(기존 토스트 경로) |
| `QuickSignal` | `Service.Send(...)`의 로컬 오류만 보고 말풍선을 띄운다(서버 거절 시 말풍선은 이미 떴을 수 있다. 4단계 파티 판 UI가 아니므로 허용, 거절되면 토스트가 따로 나온다) |
| `Redraw`, `Format` | 변경 없음. `Format`의 `Safe()`가 리치 텍스트 주입을 막는다(서버는 `<`, `>`를 그대로 둔다) |
| `Update` | `Service.Tick`이 소켓 수신 큐를 비운다(이미 매 프레임 호출) |

### 4.5 `Net/NetPresence.cs` (개발용 두 창 중계)
| 함수 | 변경 |
|---|---|
| `Relay(ChatLine)` | `OnlineServices.Chat.IsOnline`이면 즉시 반환(서버로 가는 채팅을 UDP로 다시 중계하면 두 번 보인다) |
| `ReadChat` | 같은 조건에서 `Deliver`하지 않고 버린다 |
| `Begin` | 변경 없음. 오프라인 모드의 두 창 시험 도구로 남는다 |
| (확인) `Core/DevCapture.Net.cs`, `DevCapture.Online.cs` | 목업을 직접 쓰는 개발 점검이라 변경 없음. 서버 구현이 주입된 상태에서 돌리지 않는다 |

### 4.6 파티·초대 (`Net/PartyClient.cs`, `PartyRunSession.cs`, `UI/PartyLobbyScreen.cs`)
| 파일:함수 | 변경 |
|---|---|
| `PartyClient.Update` | WS 연결 중(`ChatSocket.Connected`)이면 `IdlePollSeconds`/`PollSeconds` 대신 `ready.limits.party_poll_seconds`(15). 끊김 중이면 기존 간격 |
| `PartyClient` 새 구독 | `ChatSocket.PartyChanged(scope=party)` -> `PollNow()` |
| `PartyRunSession` 새 구독 | `ChatSocket.PartyChanged(scope=run)` -> `Fetch()`를 바로 호출(`pollTimer` 초기화). 하트비트·`ClaimHost`·정산 폴링은 변경 없음 |
| `PartyClient` 새 함수 | `Invite(string characterId, Action<bool,string> done)`(I1), `RespondInvite(string inviteId, bool accept, ...)`(I2), `CancelInvite(...)`(I3). `ReadPoll`이 `invites_incoming`을 읽어 같은 이벤트로 알린다(WS가 끊긴 때의 안전망) |
| 초대 수신 UI | `ChatSocket.InviteReceived`/`ReadPoll`의 `invites_incoming` -> `Game.UI.Confirm("{이름}님이 {던전} 파티에 초대했습니다.", 수락/거절, overlay: true)`. 30초 안에 응답이 없으면 만료 문구. 수락 응답의 `party`는 `ReadParty`로 반영 |
| `UI/PartyLobbyScreen.cs` | 방장 화면에 "친구 초대" 진입(`SocialScreen`을 열어 사람을 고른다)은 선택 사항. 최소 범위는 `SocialScreen`의 "파티 초대" 버튼 |

### 4.7 설정·기타
| 파일 | 변경 |
|---|---|
| `Settings/SettingsData.cs` `chatFriends`, `chatBlocked` | 변경 없음(오프라인 목업 전용) |
| `Core/DevCapture.*` | 변경 없음 |
| 빌드 | `ClientWebSocket`은 `System.Net.WebSockets`라 어셈블리 정의 변경 없음. Steam P2P(4단계 `SteamP2PTransport`)와 별개 연결이다 |

## 5. 서버 쪽 변경 (dotrpg-backend-coder용)

### 5.1 새 모듈 (`server/src/`)
| 모듈 | 내용 |
|---|---|
| `domains/chat/wsServer.ts` | `upgrade` 처리(`/ws`만), `WebSocketServer({ noServer })`, 핸드셰이크 한도(IP), 인증 전 소켓 한도, 5초 hello 타이머, 유휴·ping 처리, 종료 시 `bye` |
| `domains/chat/wsProtocol.ts` | 클라이언트·서버 프레임 zod 스키마, close 코드·에러 코드 상수 |
| `domains/chat/chatSession.ts` | 세션 상태(계정, 캐릭터, shard, 토큰 만료, 차단 집합, 송신 큐) |
| `domains/chat/chatService.ts` | `chat.send` 파이프라인(api 4.2), 수신자 계산, 차단 필터, 귓속말 대상 확인 |
| `domains/chat/chatRules.ts` | 순수 함수: 정규화, 반복·간격 상태 전이, 마스킹 연동. 단위 테스트의 중심 |
| `domains/chat/chatWriter.ts` | 직렬 쓰기 큐(INSERT -> 전달). 멱등성(`client_msg_id`) 충돌 처리 |
| `domains/chat/chatRepository.ts` | `chat_messages` 쿼리(따라잡기, 증거 수집), 정리 |
| `domains/chat/backlogService.ts` | 접속 때 `backlog` 구성과 등록-쿼리-전달 순서(api 3.6) |
| `domains/chat/presenceService.ts` | 접속 목록, 친구 상태 푸시, 활동(`party_run_members`) 계산 |
| `domains/chat/limiterStore.ts` | `ChatLimiterStore` 인터페이스 + 메모리(계정 단위, 10분 보관) |
| `domains/chat/sanctionService.ts` | 활성 채팅 금지 조회, 자동 제재 생성, `LISTEN dotrpg_sanction` 처리, 미통보 제재 푸시 |
| `domains/chat/realtimeNotifier.ts` | `RealtimeNotifier`·`Broadcaster`·`SessionRegistry` 인터페이스와 메모리 구현 |
| `domains/friends/`, `domains/blocks/`, `domains/reports/`, `domains/partyinvites/` | routes / controller / service / repository / validation (기존 3계층 규칙) |
| `gamedata/chatData.ts` | `server/data/chat.json` 로더(필드 검증, 없으면 기동 실패) |

### 5.2 기존 코드 변경
| 파일 | 변경 |
|---|---|
| `server.ts` | `app.listen`의 HTTP 서버에 `upgrade` 연결, `LISTEN` 전용 pg 연결(재연결 포함), 매시간 `purgeChatData`(api 10.1), `shutdown`에서 모든 WS에 `bye`(1001) 후 닫기, 쓰기 큐 비우기 |
| `app.ts` | `SOCIAL_PATH`(`/friends`, `/blocks`, `/reports`) IP 버킷(`RATE_SOCIAL_IP_MAX`) 추가. `/ws`는 Express를 거치지 않는다 |
| `middleware/authMiddleware.ts` | 토큰 검증·계정 정지 확인을 `verifyAccessToken(token)` 함수로 분리해 REST와 WS가 공용 사용 |
| `middleware/versionCheck.ts` | 소셜 경로는 `{ data: false }`로 등록. WS `hello`는 같은 `compareSemver`를 호출 |
| `db/pool.ts` | `withTransaction`에 `afterCommit(fn)` 훅(롤백이면 폐기), LISTEN용 전용 클라이언트 생성 도우미 |
| `utils/bannedWords.ts` | `maskBannedWords`, `hasObfuscatedBannedWord` 추가(기존 `containsBannedWord`는 유지) |
| `config/env.ts` | api 11절의 환경변수와 `AppConfig` 필드. `chat.json`이 없으면 기동 실패 |
| `domains/party/partyService.ts`, `partyTx.ts`, `partyRepository.ts`(`bump`, `closeParty`) | 커밋 뒤 `RealtimeNotifier.partyChanged` 호출(api 9절). 로직 변경 없음 |
| `domains/match/matchService.ts` `runMatchTick` | 매칭 파티 생성 후 같은 알림 |
| `domains/partyruns/*` | R1·R3·R4·R6·R8, 지연 전이, 판 종료에서 알림(`scope=run`) |
| `domains/party/partyService.ts` `getMyParty` | 응답에 `invites_incoming`(8.4)과 지연 만료 처리 |
| `domains/characters/characterRepository.ts` | 이름으로 살아 있는 캐릭터 조회(대소문자 무시, `characters_name_alive`), uuid로 삭제된 캐릭터까지 조회하는 함수 |
| `routes/index.ts`(루트 라우터) | `/friends`, `/blocks`, `/reports` 등록(클라이언트 버전만 검사), `partyinvites`는 `/characters/{uuid}/party/invites` 아래 |
| `package.json` | `ws`(+ 타입) 추가(PLAN_SERVER §6에 이미 정해진 라이브러리) |

### 5.3 시험으로 고정할 것 (api의 판정 규칙)
- 쓰기 큐가 `seq` 증가 순서로만 전달하고, 접속 중 커밋된 메시지가 `backlog`와 라이브 사이에서 누락·중복되지 않는다.
- 같은 `cid` 재전송이 한도를 소모하지 않고 같은 ack를 돌려준다.
- 반복 3회째는 전송되고 이후 `muteSeconds` 동안 `MUTED_REPEAT`.
- 차단당한 귓속말은 저장되지 않고 ack만 간다. 차단한 계정의 일반·파티·귓속말은 따라잡기에도 없다.
- 파티 채널은 강퇴·탈퇴 직후 바로 보낼 수 없고, 가입 전 대화는 따라잡기에 없다.
- 증거에 제3자와의 귓속말이 섞이지 않는다.
- 친구 A->B와 B->A 동시 요청이 하나의 친구 관계로 끝난다.
- 초대 수락이 `min_power`·입장 자격을 건너뛰지 못한다(4단계 신청 수락과 같은 결과).
- 서버 재시작(`bye` 1001) 뒤 클라이언트 백오프 재연결과 `since` 따라잡기.
- 제재 INSERT(SQL) 즉시 접속 중 소켓이 `MUTED_SANCTION`으로 바뀌고 `ban`이면 끊긴다.

## 6. 알려진 불일치와 확인이 필요한 사항

| 항목 | 내용 | 조치 |
|---|---|---|
| `Send` 동기 반환 | C# `IChatService.Send`는 문자열을 즉시 돌려주고 `ChatView`/`DevCapture.Net`이 그 값으로 분기한다 | 4.2의 `SendFailed` 이벤트로 이원화. `DevCapture.Net.cs`는 목업 전용이라 영향 없음 |
| 이름 기반 API | `AddFriend`, `Block`, `Report`, `RecentSpeakers`가 이름 문자열을 받는다. 이름은 삭제 후 재사용될 수 있다 | 온라인은 `(id, name)`(`ChatPerson`). 목업은 id가 null |
| `ChatView`가 서비스 교체를 모른다 | `Create`가 구독한 뒤 `OnlineServices.Chat`이 바뀌면 이전 인스턴스에 매달린다 | `OnlineServices.ChatChanged` 이벤트(4.4) |
| `NetPresence` 중계 | 서버 채팅과 UDP 중계가 겹치면 같은 줄이 두 번 보인다 | `IsOnline`이면 중계 끔(4.5) |
| `MiniJson.Int` | `int`로 반올림해 큰 `seq`에서 깨질 수 있다 | `long`으로 읽는 `Num` 사용 |
| 금칙어 파일 | `server/data/banned_words.json`이 레포에 없어 필터가 비어 있다. 4단계 모집 메시지 검사도 지금은 통과한다 | 결정 대기 5 |
| 일반 채널 범위 | PLAN_ONLINE는 "같은 마을(채널)". 설계는 서버 단일 방(shard) | api 14절. 지금 클라이언트에는 마을별 채널 UI가 없어 영향 없음 |
| 친구 `where` | 서버가 아는 위치는 클라이언트가 보고한 맵 id(표시 전용)와 서버가 계산한 활동(던전·레이드)뿐이다. 맵 id는 신뢰하지 않는다 | 친구 목록의 부가 정보로만 쓴다. 보상·판정에 쓰지 않는다 |
| WebGL | `ClientWebSocket`은 WebGL에서 안 된다 | Steam 데스크톱 전제. WebGL 빌드가 필요해지면 JS 브리지로 교체(`ChatSocket` 내부만 바뀐다) |
| 오프라인 귓속말 | PLAN_ONLINE가 정하지 않았다 | 접속 중인 그 캐릭터에게만 보낸다. 오프라인 전달은 우편(6단계)의 몫 |
| 이름 변경 | 캐릭터 이름은 바뀌지 않는다(`characters_name_alive`) | 채팅 기록의 `sender_name` 스냅샷을 그대로 신뢰 |
