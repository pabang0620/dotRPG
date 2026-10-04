# 서버 8단계 게임 값 대응표 (전투 중계 + 필드 파티 사냥)

게임 쪽 정의(C# 필드·함수·json 키) <-> 서버 테이블·컬럼 <-> API. API는 [phase8_api.md](phase8_api.md), 스키마는 `server/schema.sql`(`0009_relay_field.sql`). 앞 단계 대응표: [phase4_mapping.md](phase4_mapping.md), [phase3_mapping.md](phase3_mapping.md).
값(수치·목록)은 여기 복사하지 않고 `server/data/*.json` 이름과 C# 출처로만 참조한다. "예"로 든 수치는 근거 설명용이다. C# 출처는 2026-10-04 코드 기준이고 경로는 `Assets/Scripts/Runtime/` 아래다.

## 1. 용어

| 말 | 뜻 |
|---|---|
| 방(room) | 중계 연결이 모이는 단위. `run`(파티 던전 판) 또는 `field`(필드 세션) |
| 좌석(seat) | 방 안의 0..3 번호. 중계의 피어 번호이자 `PartyNet`의 슬롯. 서버가 도장 찍는다 |
| 호스트 | 몬스터를 계산하는 PC. 파티 방장(로비 방장)과 다를 수 있다(4단계 mapping 1.3) |
| 필드 세션 | 같은 파티가 같은 필드 맵에서 함께 사냥하는 덩어리(`field_sessions`) |
| 전송(transport) | 방의 바이트 운반 방식: `relay` / `steam` / `dev` |

## 2. C# <-> 서버 대응

### 2.1 전송 계층

| C# | 지금 | 8단계 서버/와이어 | 비고 |
|---|---|---|---|
| `ITransport.LocalPeer` (`Net/Transport.cs`) | 0 또는 1(2피어) | 내 **좌석**(0..3). 서버 `ready.seat` | 호스트 좌석도 0이 아닐 수 있다(호스트 인계, 필드는 먼저 들어온 사람) |
| `ITransport.Peers` | 호스트는 최대 3, 멤버는 호스트 하나 | 중계: 호스트는 연결된 다른 좌석 전부, **멤버는 호스트 좌석만**(별 모양, 서버도 강제) | 멤버 쪽 `PartyNet.Member.HostPeer => Peers[0]`가 그대로 맞는다 |
| `ITransport.IsConnected` | 두 피어 전제 | 중계 `ready` 수신 후 && (멤버면) 호스트 좌석이 방에 있음 | |
| `NetPacket.from` | 피어 번호 | 서버가 도장 찍은 보낸 좌석 | 위조 불가. `PartyNet.OwnsSlot(peer, slot)`이 그대로 성립(`peerSlot[peer] == slot`) |
| `NetChannel` 1~7 | 번호만 | 서버 채널 표(phase8_api 4.5): 방향·신뢰 등급이 서버 규칙 | |
| `NetChannel.IsReliable` | Event, Control, Chat | **Input 추가**(버튼 한 번을 잃으면 안 된다) | Steam 전송에서 `Reliable` 플래그를 쓰는 기준. 중계는 어차피 TCP이고 큐 줄 선택에 쓴다 |
| `NetChannel.Chat`(2), `Presence`(3) | 개발 UDP(마을 유령·채팅 중계) | 중계 방에서 **금지**(채팅은 `/ws`) | `NetPresence`는 개발 도구로 남는다(마을 유령은 이 단계 밖) |
| `LoopbackTransport.CreatePair` | 2피어 | 별 모양 N피어 `CreateStar(n, latency, loss)` 추가 | 4인 시험·카오스 시험용(출시 코드 안의 시험 도우미) |
| `UdpTransport(bool host, int basePort)` | 두 창 | `dev` 전송. 개발 빌드에서만 컴파일 | 출시물 제외 |
| (새) `ITransport.Kind` | - | `relay` / `steam` / `dev` -> `party_runs.transport`, `field_sessions.transport` | 전송 교체 판단 |
| (새) `ITransport.HostPeer` | - | 호스트 좌석(중계 `ready.host_seat`, `host.changed`) | 멤버가 호스트 좌석으로만 보낸다 |
| (새) `ITransport.State`, `PeerConnected/PeerDisconnected`, `RttMs(peer)`, `Close()` | - | 중계 `ready`·`peer.joined/left`·PING/PONG | HUD 핑 막대, 전환 판단 |
| `PartyNet.HostTick` 끊긴 피어 판정 | `transport.Peers`에 없으면 끊김 | `peer.left` 이벤트(즉시) + `Peers` | 서버가 3초 grace 뒤 인계(4.8) |
| `UdpTransport.TimeoutSeconds`(3초 무신호) | 개발 | 중계: 서버 PING 6초, 호스트 grace 3초 / Steam: 3초 무신호 | 서버 하트비트 12초와 별개 |

### 2.2 필드 몬스터·스포너 (`Enemies/EnemySpawner.cs`, `World/WorldBuilder.cs`, `Data/EnemyStats.cs`)

| C# | 지금 | 8단계 | 서버 데이터 |
|---|---|---|---|
| `EnemySpawner.Setup(stats, look, points)` | 맵 로드 때 스폰점마다 즉시 스폰 | 모드 `Local / Pending / Host / Follower`. `Pending`은 F1 응답 전까지 스폰 보류. `Host`는 스폰 시작, `Follower`는 스폰하지 않고 꼭두각시만 | `maps.json fieldSpawns[].points`(= `points.Count`) |
| `EnemySpawner.alive[]`, `respawnAt[]` | 호스트가 아니면 무의미 | `ExportState()`: 스폰점별 `{ netId, respawnInMs }`을 `PartyMsg.SpawnerState`로 2초마다 전송, `ImportState()`로 호스트 인계 복원 | 서버는 저장하지 않는다 |
| `EnemyStats.respawnDelay` (25) | 죽은 뒤 대기 | 변경 없음(호스트 PC) | `monsters.json respawnSeconds`(25). 서버 공급 상한(3.2.2)의 R |
| `EnemyStats.respawnMinPlayerDistance` (9) | `Game.Player`와의 거리 | **세션 활성 멤버 전원**과의 거리(가장 가까운 멤버 기준) | `monsters.json respawnMinPlayerDistance` |
| `WorldBuilder.cs` `skeletonSpawns.Count` (스폰점) | 로컬 스포너 | 필드 세션 가능 여부(`HasFieldSpawns`)의 근거. 서버 `fieldSpawns.points`와 같아야 한다(내보내기 검증) | `maps.json fieldSpawns` |
| `EnemyController.NetId` (`EnemyController.Net.cs`) | 던전 호스트가 붙임 | 필드도 호스트가 붙임. 인계 때 이어서 `max + 1`부터 | `monster_ref = 세대 x 2^20 + NetId` |
| (새) `EnemyController.Shared` | - | `EnemySpawner`가 만든 몬스터만 `true`. `PartyNet.AssignEnemyIds`가 `Shared`만 동기화(연출·퀘스트 스폰은 로컬) | |
| `EnemyController.PuppetDie(goldHits)` | 사망 이벤트 -> `Die()` -> 보고 | `PuppetDie(goldHits, creditMask)`. `creditMask`에 내 좌석 비트가 있을 때만 `OnlineEconomy.ReportKill` | 6.10 |
| `EnemyController.ReleasePuppet()` | 던전 인계 | 필드 인계에도 사용(스포너 상태 복원과 함께) | |
| 몬스터 레벨(필드) | 1(`MonsterDatabase`) | 변경 없음 | `maps.json fieldSpawns[].monsterLevel`(새 내보내기, 기본 1) -> 6.7 `xp_factor` |
| 맵 이동 `GameFlow.TravelTo` | 월드 재생성 | 떠나기 전 `FieldSession.Leave()`, 로드 뒤 `FieldSession.OnMapEntered(mapId)` | F4, F1 |

### 2.3 PartyNet 와이어(`Net/PartyNetWire.cs`, `PartyNet.Host.cs`, `PartyNet.Member.cs`)

`PartyNet.WireVersion`(정수 상수, 이 표의 변경과 함께 올린다)을 `Hello`와 중계 `hello.wire`에 싣는다. 서로 다른 값이면 중계가 `4015`로 거절하고(Steam은 호스트가 Hello를 거절) 클라이언트는 "파티원과 게임 버전이 다릅니다" 토스트를 띄운다.

| 메시지(채널) | 변경 | 내용 |
|---|---|---|
| `PartyMsg.Hello`(Control, 멤버 -> 호스트) | `wire`(int) 추가, 필드에서는 `runId` 자리에 세션 uuid | 기존 필드 + `wire` |
| `PartyMsg.Welcome`(Control, 호스트 -> 멤버) | `inRun` bool을 `kind` byte(0 없음, 1 던전, 2 필드)로 확장하고 필드면 `mapId`, 스포너 상태 추가 | `OnWelcome`의 `StartFollowing`은 `kind==1`일 때만 |
| `PartyMsg.EnemySpawn`(Event) | 변경 없음 | |
| `PartyMsg.EnemyDie`(Event) | `creditMask`(byte) 추가. 던전은 `0x0F`(전원) | 필드에서 호스트가 정한 기여 좌석 비트 |
| `PartyMsg.SpawnerState = 12`(Event, 호스트 -> 멤버, 2초마다) | 새 | `[count u8]` 후 스폰점마다 `[pointIndex u8][netId i32 (0 = 없음)][respawnInMs u16]` |
| `PartyMsg.CardUpdate = 13`(Event, 멤버 -> 호스트) | 새 | `MemberCard`(레벨업, 장비·패시브 변경 때). 호스트는 하트비트로 받은 서버 `level`/`gear_hash`와 대조한 뒤 적용 |
| `PartyMsg.FieldEnd = 14`(Event, 호스트 -> 멤버) | 새 | 세션 종료·호스트가 맵을 떠남을 알림(멤버는 곧 `host.changed`를 받는다) |
| `MemberCard`(`Of`, `ToData`, `Write`, `Read`) | 변경 없음 | 서버 대조 대상: `level`, `gear[]`(-> `gear_hash`) |
| Snapshot(`BuildSnapshot`) | 던전 방 번호 대신 필드는 `room = 0` | 멤버 `expectedRoom`도 0 |

중계 바이트 틀(BATCH)은 서버 규약이다(phase8_api 4.4): `[0x04][count][channel][addr][len u16 LE][payload]...`, PING `0x02`, PONG `0x03`. `RelayTransport`가 만들고 푼다. `PartyNet`은 BATCH를 모른다(`ITransport.Send(peer, channel, data)`만 쓴다).

### 2.4 필드 세션 필드 <-> 테이블 <-> API

| API 필드(`FieldSessionView`) | 테이블.컬럼 | C# 사용처 |
|---|---|---|
| `id` | `field_sessions.uuid` | `FieldSession.SessionId`, 처치 보고 `session_id`, 중계 방 id |
| `version` | `field_sessions.version` | `FieldSession.Read`(폴링 `after_version`) |
| `map_id` | `field_sessions.map_id` | `Game.World.MapId`와 같아야 한다 |
| `state` | `field_sessions.state` | |
| `host.character_id` / `host.seat` / `host.epoch` | `field_sessions.host_character_id` / (멤버 행의 `seat`) / `host_epoch` | `PartyNet.IsHost`, `monster_ref`의 세대 |
| `host.steam_id` | `auth_identities.subject`(provider steam) | `SteamP2PTransport` 연결 대상 |
| `transport.current/epoch/order` | `field_sessions.transport/transport_epoch/transport_order` | `CombatTransportFactory` |
| `me.seat` / `me.state` | `field_session_members.seat/state` | `PartyNet.MySlot` |
| `me.entry_token` | (계산: HMAC(`session_key`, 세션.캐릭터.좌석)) | steam·dev 전송의 Hello 토큰 |
| `members[].level` | `characters.level` | 호스트 카드 검증 |
| `members[].gear_hash` | (계산: `character_items` 착용 키 정렬 해시 앞 16자) | 호스트 카드 검증 |
| `members[].is_leader` | `parties.leader_character_id` | 호스트 선출 표시 |
| `election_until` | `field_sessions.host_since + FIELD_ELECTION_WINDOW_SECONDS` | HUD(선택) |
| (처치 보고) `session_id` | `kill_log.field_session_id` | `OnlineEconomy.FieldSessionId` |
| (처치 보고) `monster_ref` | `kill_log.monster_ref` | `(epoch << 20) \| EnemyController.NetId` |
| (응답) `field.xp_factor` | `kill_log.xp_factor` | 감쇠 안내 토스트(1 미만일 때) |
| (observe) `credits[].seat/kills` | `field_session_members.kills_credited` | 호스트의 `PartyNet` 기여 집계 |

### 2.5 RunView(4단계)의 8단계 추가

| API 필드 | 테이블.컬럼 | 비고 |
|---|---|---|
| `run.transport.current/epoch/order` | `party_runs.transport/transport_epoch/transport_order` | |
| `run.transport.relay.url` | 서버 설정(`/meta.relay`) | |
| `host_key`(R2, 호스트에게만) | `party_runs.run_key` | steam·dev 전송, 전환 대비 |

### 2.6 에러·종료 코드 대응(클라이언트 동작)

| code | 클라이언트 동작 |
|---|---|
| `ROOM_NOT_FOUND`, `ROOM_CLOSED`(T1) | 방이 끝났다. 판/세션 상태를 다시 읽고 로비·솔로로 복귀 |
| `TRANSPORT_NOT_RELAY` | `errors.current`의 전송으로 이동(Steam이면 P2P 연결) |
| `RELAY_UNAVAILABLE`, `503 MAINTENANCE` | `Retry-After` 뒤 재시도, 길어지면 "서버가 혼잡합니다" |
| `TRANSPORT_CHANGED` | `errors.current`로 이동(이미 다른 멤버가 전환함) |
| `TRANSPORT_EXHAUSTED` | "연결할 수 없습니다" + 이탈/재시도 선택 |
| `TRANSPORT_SWITCH_COOLDOWN` | 10초 뒤 재요청 |
| `TRANSPORT_UNAVAILABLE`(R1·F1) | 출발·입장 거절 토스트 |
| `MAP_NOT_SHARED` | 호출하지 않아야 정상(클라이언트가 먼저 거름). 오면 로컬 스폰 |
| `FIELD_SESSION_NOT_FOUND`, `FIELD_SESSION_ENDED`, `FIELD_SESSION_INVALID` | F7로 내 세션을 다시 읽고 없으면 로컬 스폰 복귀 |
| `HOST_CHANGED` / `HOST_ALIVE` / `NOT_NEXT_HOST` | 4단계 mapping 8절과 같다 |
| `KILL_DUPLICATE` | 무시(이미 반영) |
| `KILL_REJECTED`(필드, `field_uncredited`) | 조용히 무시(토스트 없음). 반복되면 호스트 관찰 확인 |
| 중계 close 4004 | 새 티켓으로 1회 재시도, 다시 4004면 방 상태 확인 |
| 4008 SLOW_CONSUMER, 4013 PEER_TIMEOUT, 1006 | 백오프 재접속 + 재동기화(`welcomed=false`) |
| 4011 MEMBER_REMOVED, 4012 ROOM_CLOSED | 재접속하지 않는다. 판/세션 상태를 읽는다 |
| 4014 TRANSPORT_CHANGED | 새 전송으로 즉시 전환 |
| 4015 WIRE_MISMATCH | "파티원과 게임 버전이 다릅니다", 방 이탈 |
| 4016 SERVER_BUSY | `retry_after_ms` 뒤 재시도 |
| 4426 CLIENT_OUTDATED, 4003 BANNED, 4001 REPLACED | `/ws`와 같은 처리(phase5 3.7) |

## 3. 서버가 새로 내보내야 하는 데이터 (`server/data/*.json`)

Unity 에디터 내보내기 메뉴(3단계 4절과 같은 도구)에 더한다. 내보내면 데이터 버전 해시가 바뀐다.

| 파일 | 추가 필드 | C# 출처 | 용도 |
|---|---|---|---|
| `maps.json` | `fieldSpawns[].monsterLevel`(기본 1) | `MonsterDatabase`의 필드 몬스터 레벨 | 6.7 레벨 격차 감쇠, `resolveFieldTarget`의 레벨 |
| `maps.json` | 각 맵 `sharedField`(bool: 필드 세션 가능 여부 = `instanced=false && fieldSpawns 있음`) | `MapRegistry`/`WorldBuilder` | 클라이언트가 F1 호출 전에 걸러낸다(서버도 같은 식으로 재판정) |
| (검증) `fieldSpawns[].points` == `WorldBuilder` 스폰점 수 | | | 내보내기 도구가 같은 맵 로드로 센 값과 비교해 어긋나면 내보내기 실패 |

이미 있는 것을 재사용한다: `fieldSpawns[].monsterId/points`, `monsters.json`의 `respawnSeconds`, `respawnMinPlayerDistance`, `xp`, `xpByLevel`, `monsterRules`(`xpPerLevel`, `hpPerLevel`, `fieldGold*`, `equipmentDropChance`), `progression.json`, `player.json`(`attackDamage`, `attackCooldown`, `power`), `enhance.json`(화력 상한의 장비 공격력), `shop.json`(드롭표).
경제 손잡이(`FIELD_PARTY_XP_FACTOR` 등)와 감쇠 상수(`FIELD_CARRY_*`)는 게임 데이터가 아니라 서버 정책 상수다(phase8_api 11절).

## 4. 클라이언트가 바꿔야 할 지점 (파일:함수)

경로는 `Assets/Scripts/Runtime/` 아래. "새 파일"은 새로 만든다.

### 4.1 전송 계층과 다피어(4명) 지원

| 파일 | 변경 |
|---|---|
| `Net/Transport.cs` `ITransport` | 위 2.1의 `Kind`, `HostPeer`, `State`, `PeerConnected/PeerDisconnected`, `RttMs(peer)`, `Close()` 추가. 기존 `Peers`는 "내가 주소를 쓸 수 있는 피어"로 의미를 고정(멤버는 호스트만) |
| `Net/Transport.cs` `NetChannel.IsReliable` | `Input` 추가 |
| `Net/Transport.cs` `LoopbackTransport` | 새 멤버 구현, `CreateStar(int n, int latencyTicks, float loss)` 추가(별 모양, 손실·지연 시뮬레이션) |
| `Net/UdpTransport.cs` | 새 멤버 구현, 클래스 전체를 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`로 감싼다(출시 제외) |
| 새 `Net/RelayFrames.cs` | BATCH/PING/PONG 코덱(순수 함수, 서버 규약 phase8_api 4.4와 같은 바이트). 단위 시험이 서버와 같은 벡터를 쓴다 |
| 새 `Net/RelayTransport.cs` | `ITransport`(+`IDisposable`) 구현. `System.Net.WebSockets.ClientWebSocket`(`ChatSocket.cs`와 같은 수신·송신 루프 구조, 데스크톱과 모바일 IL2CPP). 백그라운드 `Task`가 `ConcurrentQueue<NetPacket>`에 받고 `Tick()`이 메인 스레드에서 비운다. 생성자 `RelayTransport(Func<Task<RelayTicket>> ticketProvider, ...)`: 첫 연결과 재접속마다 T1을 호출해 새 티켓을 얻는다. 송신: 2줄 큐(신뢰 FIFO / 채널별 최신 한 개), 20ms마다 BATCH로 묶어 `SendAsync`(바이너리). 수신: BATCH를 풀어 `NetPacket{from, channel, data}`로. `Peers`는 `ready.peers`와 `peer.joined/left`로 유지(멤버는 호스트 좌석만 노출). 2초마다 PING, RTT 이동 평균을 `RttMs`로. `host.changed`는 이벤트(`HostChanged(seat, epoch)`)로 올린다. 백오프 재접속(0.5, 1, 2, 4, 8초 x 0.8~1.2), 3번 연속 비정상 종료면 `State = Failed`(전환 판단용). 느린 송신(큐가 한도를 넘음)은 신뢰 줄 한도 초과로 스스로 연결을 닫고 재접속 |
| 새 `Net/CombatTransportFactory.cs` | 방 정보(`RoomTransportInfo`: kind, id, transport.current/epoch/order, relay url, steam 호스트 ID, entry_token, host_key)를 받아 `ITransport`를 만든다: `relay` -> `RelayTransport`, `steam` -> `ISteamBridge.CreateTransport(...)`, `dev` -> `UdpTransport`(개발 빌드). 연결 대기 시한(중계 8초, Steam 12초)을 재고 넘기면 T2를 호출하고 응답대로 다음 전송을 만든다. `TRANSPORT_CHANGED`·`transport.changed`를 받으면 즉시 새 전송으로 교체 |

### 4.2 파티 판(던전) 중계 연결: `PartyRunSession` 교체 지점

| 파일 | 변경 |
|---|---|
| `Net/PartyRunSession.cs` `EnsureNet` | `new UdpTransport(true/false, UdpTransport.PartyPort)`를 `CombatTransportFactory.Create(roomInfo)`로 교체. 호스트·멤버 구분은 `RunView.host.character_id`가 정하므로 같은 전송을 두 쪽이 각자 만든다 |
| `Net/PartyRunSession.cs` `Read` | `RunView.transport`를 읽어 `roomInfo`를 갱신, 값이 바뀌면(`epoch` 증가) `OnTransportChanged` |
| `Net/PartyRunSession.cs` `Join` | 중계 전송에서는 호출하지 않아도 된다(서버가 `hello`로 자동 입장). Steam·dev는 그대로 |
| `Net/PartyRunSession.cs` `Update` | `PartyNet.Current.Transport.IsConnected`로 호스트 상실을 판단하는 블록은 **Steam·dev 전송에만** 쓴다. 중계에서는 `RelayTransport.HostChanged`(서버 주도 인계)와 `peer.left`를 따른다. 호스트 상실 3초 -> `ClaimHost`는 중계에서 서버가 이미 정했을 가능성이 높아 `409 HOST_CHANGED`를 정상 응답으로 처리 |
| `Net/PartyRunSession.cs` `TakeOver` | `PartyNet.End()` + `new UdpTransport`를 `PartyNet.PromoteToHost(hostKey)`로 교체: 전송은 그대로 두고(`End(keepTransport: true)`) 꼭두각시를 실제 몬스터로 푼다(`ReleasePuppet`). 서버 주도 인계(`host.changed`에서 내 좌석이 호스트)와 클레임 성공 둘 다 이 함수를 쓴다 |
| `Net/PartyRunSession.cs` `Heartbeat` | 응답의 `transport`(전환), `members[].level/gear_hash`(호스트일 때 카드 검증용 `PartyNet.SetServerRoster`) 처리. 호스트 상실 후 돌아온 옛 호스트가 `host_changed`로 권한을 잃는 처리(`wasHost && !amHost`)에 `PartyNet.DemoteToMember()` 추가(꼭두각시 재구성 + 재 Hello) |
| `Net/PartyNet.cs` `Create`, `End` | `End(bool keepTransport = false)`: `keepTransport`면 전송을 `Dispose`하지 않는다. `Create`는 모드(`Dungeon`/`Field`)를 받는다 |
| `Net/PartyNet.cs` (새) `ReplaceTransport(ITransport t)` | 전투 상태는 유지하고 전송만 교체. 멤버: `welcomed = false`로 되돌려 새 연결에서 Hello를 다시 보내고(`OnWelcome`의 `if (welcomed) return`을 "재동기화 중이면 통과"로), 호스트: `peerSlot`을 비우고 5초 동안 `MemberLost` 토스트를 억제하며 멤버의 재 Hello를 `OnHello`로 받는다(이미 멱등) |
| `Net/PartyNet.cs` `Update` | `transport.State == Failed`이면 `CombatTransportFactory`에 알린다. 개발용 `FromCommandLine`은 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` |
| `Net/PartyNet.Host.cs` `OnHello` | `Hello`의 `wire`를 확인(불일치면 무시 + 토스트). 중계에서는 `hostKey` 토큰 검증을 건너뛴다(`transport.Kind == relay`면 `from`이 서버가 도장 찍은 좌석) |
| `Net/PartyNet.Host.cs` `HostTick` | 끊긴 피어 판정을 `PeerDisconnected` 이벤트와 병행 |
| `Net/PartyNet.Member.cs` `MemberTick`, `OnWelcome` | 재동기화(`welcomed` 초기화) 지원, `HostPeer`는 `transport.HostPeer` |
| `Dungeon/DungeonDirector.cs` | 던전 쪽은 변경 없음(4단계 `EnterFollower`/`FollowRoom`/`FollowEnd` 그대로). 던전 입장 직전에 `FieldSession.Leave()`를 부른다(이미 닫혔으면 무시) |

### 4.3 필드 모드: `PartyNet`과 동기화

| 파일 | 변경 |
|---|---|
| 새 `Net/FieldSession.cs` | `PartyRunSession`과 짝인 상태기계(MonoBehaviour, 싱글턴): `OnMapEntered(mapId)`가 이전 세션을 정리하고 조건(온라인 + 활성 파티 사람 2명 이상 + `sharedField` 맵)이 맞으면 F1 호출. 응답에 따라 모드 결정: 호스트(스포너 `Host`, `PartyNet.BeginFieldHost`), 멤버(스포너 `Follower`, `PartyNet.BeginFieldMember`), 세션 없음(스포너 `Local`). F1 응답이 1.5초 안에 안 오면 로컬 스폰으로 시작하고 늦게 도착하면 합류하지 않는다(몬스터 이중화 방지, 다음 맵 진입에서 재시도). 하트비트 5초(`seen_epoch`, `synced`), 폴링 안전망, 호스트 관찰 10초(F6, 호스트만), 인계 처리(`TakeOver`와 같은 흐름에 스포너 상태 복원), 전환 처리. 파티 변화(`PartyClient` 폴링 / `party.changed`)를 구독해 파티원이 2명 이상이 되면 현재 맵으로 F1을 다시 호출, 1명이 되면 세션을 닫고 스포너를 `Local`로 |
| `Core/GameFlow.cs` `TravelTo(mapId, arriveAtSpawn)`의 `Transition` 콜백 | 맵을 바꾸기 전에 `FieldSession.Leave()`(F4, 응답을 기다리지 않는다), 월드 로드 뒤 기존 `GameEvents.RaiseMapEntered(mapId)`를 `FieldSession`이 구독 |
| `Core/GameFlow.cs`(시작 로드, `RaiseMapEntered` 호출 지점) | 접속 직후 같은 맵(숲 등)에서 재개하는 경우도 `OnMapEntered`가 불린다. 앱 재시작 복구는 F7 |
| `World/WorldBuilder.cs`(스포너 생성부, `SkeletonSpawner`) | `spawner.Setup(...)` 전에 `FieldSession.Resolve(mapId)`가 모드를 정해 주도록 `Setup(stats, look, points, mode)` 인자를 추가. `HasFieldSpawns`(`skeletonSpawns.Count > 0`) 노출 |
| `Enemies/EnemySpawner.cs` `Setup`, `SpawnAt`, `OnEnemyDied`, `Update` | 모드 4종. `Pending`: 스폰하지 않고 대기(타임아웃 시 `Local`로). `Follower`: `Update` 중단. `Host`: 리스폰 판단의 플레이어 거리를 `PartyNet.NearestMemberDistance(point)`로(세션 멤버 전원). `ExportState/ImportState`(2.2). 스폰한 몬스터에 `Shared = true`. `ImportState`에서 상태가 없는 스폰점은 보수적으로 "방금 죽음"으로 둔다 |
| `Enemies/EnemyController.cs`(+ `.Net.cs`) | `Shared`, `CreditMask`(호스트가 사망 시 계산하기 위한 기여자 집합: `Health.Damaged`의 `DamageInfo.attacker`로 좌석 비트 누적, 사망 시점에 살아 있고 `FIELD_CREDIT_RANGE`(상수 14칸) 안의 멤버 비트 추가). `PuppetDie(goldHits, creditMask)`. `DropLoot`(온라인 분기)는 `OnlineEconomy.ReportKill(monsterId, hits, at, parent, monsterRef)`를 `creditMask`의 내 비트가 켜졌을 때만 호출 |
| `Net/PartyNet.cs` | `BeginFieldHost/BeginFieldMember`, `Mode`, `NearestMemberDistance`, `SetServerRoster(members)`(호스트: 서버가 준 좌석·레벨·지문) |
| `Net/PartyNet.Host.cs` `BeginHost` -> 필드 변형 | `humans` 개념을 "서버가 아는 좌석 집합"으로 대체(필드는 최대 4). `RebuildHostSlots`: 필드에서는 AI 좌석 없음 |
| `Net/PartyNet.Host.cs` `OnHello` | `slot >= humans` 검사를 필드에서는 "서버 명단에 있는 좌석인가"로. 멤버가 맵에 늦게 들어와도 `Welcome` + 살아 있는 몬스터 `EnemySpawn` 재전송(기존 코드가 이미 한다) + `SpawnerState` |
| `Net/PartyNet.Host.cs` `WelcomeFor` | `kind=2`(필드), `mapId`, 스포너 상태 포함 |
| `Net/PartyNet.Host.cs` `HostTick` | `if (Game.Dungeon == null \|\| !Game.Dungeon.InRun) return;`를 `if (!SyncActive) return;`로(던전 InRun 또는 필드 모드). `SpawnerState` 2초 송신, 이벤트 기반 기여 집계 |
| `Net/PartyNet.Host.cs` `AssignEnemyIds` | `e.Shared`(필드) 또는 던전 몬스터만 번호를 붙인다. 사망 이벤트에 `creditMask` 포함 |
| `Net/PartyNet.Host.cs` `BuildSnapshot` | `Game.Dungeon.CurrentRoom` 의존 제거(필드는 0) |
| `Net/PartyNet.Host.cs` `HostReceive` | `CardUpdate` 수신: 서버 지문·레벨과 대조해 통과하면 멤버 복사본을 갱신(`copy.Data` 재구성) |
| `Net/PartyNet.Member.cs` `MemberTick` | `if (me == null \|\| Game.Dungeon == null \|\| !Game.Dungeon.InRun) return;`를 `!SyncActive`로. 레벨업·장비·패시브 변경 이벤트에서 `CardUpdate` 송신(구독: `Progression` 레벨업, `Equipment` 변경) |
| `Net/PartyNet.Member.cs` `OnWelcome`, `StartFollowing` | `kind==2`이면 던전 입장(`EnterFollower`) 대신 현재 맵이 `mapId`와 같은지 확인하고 `SpawnerState`를 받을 준비. 맵이 다르면 합류 취소(F4) |
| `Net/PartyNet.Member.cs` `FlushSpawns` | 던전 조건(`RoomIsReady`, `CurrentRoom == expectedRoom`)을 필드에서는 `Game.World != null && Game.World.MapId == fieldMap && 월드 준비됨`으로 |
| `Net/PartyNet.Member.cs` `ApplySnapshot` | `room != expectedRoom` 건너뛰기를 필드에서는 비활성(room 0) |
| `Net/PartyNet.Member.cs` `OnEvent` | `EnemyDie`의 `creditMask`, `SpawnerState`, `FieldEnd` 처리. 새 호스트로 인계될 때를 위해 마지막 `SpawnerState`를 보관 |
| `Net/OnlineEconomy.cs` `ReportKill`, `RunId`/`RoomIndex` 옆 | 정적 `FieldSessionId`, `ReportKill(..., long monsterRef)`: `session_id`, `monster_ref`를 본문에 추가. `SetField(sessionId)`/`ClearField()`. 응답의 `field.xp_factor < 1`이면 토스트("레벨 차이로 경험치가 줄었다") |
| `Party/PartyManager.cs` `SetRosterHidden`, `AddNetMember`, `RemoveNetMember`, `SpotFor` | 다인 필드 세션 동안 AI 동료를 숨김/복원(활성 멤버 2명 이상일 때). 멤버가 뒤늦게 합류·이탈하는 빈번한 경우를 위해 `AddNetMember/RemoveNetMember`가 던전 시작 전 호출만 가정하지 않는지 확인 |
| `Quest/StoryCast.cs` `StoryCompanions.Refresh(outsideTown)` | 다인 필드 세션에서는 소환하지 않는다 |
| `Net/ChatSocket.cs` 수신부 | `field.changed` 프레임 -> `FieldSession.FetchNow()`(기존 `party.changed(run)` -> `PartyRunSession.FetchNow()`와 같다). `hello`에 `caps: { steam_p2p }` 추가 |
| `Net/ServerPartyFinderService.cs`, `Net/PartyClient.cs` | 파티 상태 변화 통지를 `FieldSession.OnPartyChanged`로 전달 |
| HUD 파티 프레임(파일명 확인 필요) | `ITransport.RttMs(peer)`로 핑 막대(초록 80ms 미만, 노랑 150ms 미만, 빨강, PLAN_ONLINE §2.1-5) |

### 4.4 Steam (R3, 출시 품질): 패키지, asmdef, 심볼, 파일

| 파일/설정 | 변경 |
|---|---|
| `Packages/manifest.json` | Steamworks.NET 패키지(`com.rlabrecque.steamworks.net`, Steamworks.NET 공식 저장소의 Unity 패키지 경로)를 의존성에 추가. 버전은 구현 때 최신 안정 태그를 확인해 고정한다 |
| 새 `Assets/Scripts/Runtime/Steam/DotRPG.Steam.asmdef` | 이름 `DotRPG.Steam`, 참조: `DotRPG.Runtime`, Steamworks.NET 어셈블리(`Steamworks.NET`), `defineConstraints: ["STEAMWORKS_NET"]`, `includePlatforms: ["Editor", "StandaloneWindows64", "StandaloneOSX", "StandaloneLinux64"]`. **`DotRPG.Runtime.asmdef`는 Steam 어셈블리를 참조하지 않는다**(순환 방지, 모바일·비 Steam 빌드가 컴파일된다). 연결은 아래 `ISteamBridge` 등록으로 한다 |
| 프로젝트 설정(Scripting Define Symbols) | `STEAMWORKS_NET`을 **Standalone 플랫폼에만** 정의. 모바일 빌드에는 정의하지 않아 `DotRPG.Steam`이 컴파일에서 빠지고 중계만 쓴다 |
| 새 `Net/ISteamBridge.cs`(Runtime) | `static ISteamBridge Current`, `bool Ready`, `ulong SteamId`, `ulong AppId`, `void GetAuthTicket(string identity, Action<string hex> done)`, `ITransport CreateTransport(SteamRoomInfo info)`, `void Tick()`. 구현이 없으면 `Current == null`이고 호출부는 중계만 쓴다 |
| 새 `Steam/SteamBootstrap.cs`(DotRPG.Steam) | `[RuntimeInitializeOnLoadMethod]`로 `SteamAPI.Init()`(실패하면 Steam 기능만 끈다, 게임은 계속), `ISteamBridge.Current` 등록, 매 프레임 `SteamAPI.RunCallbacks()`, 종료 시 `SteamAPI.Shutdown()`. `SteamNetworkingUtils.InitRelayNetworkAccess()` 호출. `SteamUtils.GetAppID()`를 `/meta.steam.app_id`와 비교해 다르면 Steam 기능을 끄고 경고(480 클라이언트가 운영 서버에 붙는 사고 방지) |
| 새 `Steam/SteamAuth.cs`(DotRPG.Steam) | `GetAuthTicket(identity, done)`: `SteamUser.GetAuthTicketForWebApi(identity)` -> `GetTicketForWebApiResponse_t` 콜백 -> 티켓 바이트를 hex로 -> `done`. 로그인 요청이 끝나면 `SteamUser.CancelAuthTicket(handle)`로 취소(같은 티켓 재사용 방지의 클라이언트 쪽). `identity`는 `/meta.steam.identity`를 쓴다(하드코딩 금지) |
| 새 `Steam/SteamP2PTransport.cs`(DotRPG.Steam) | `ISteamNetworkingSockets` P2P. 호스트: `CreateListenSocketP2P(virtualPort)`, `SteamNetConnectionStatusChangedCallback_t`에서 연결 요청의 SteamID가 서버 명단에 없으면 거절, 있으면 `AcceptConnection`, `CreatePollGroup`으로 수신. 멤버: `ConnectP2P(identity, virtualPort)`. 송신 `SendMessageToConnection`(채널의 `IsReliable`에 따라 `Reliable`/`Unreliable` 플래그), 수신 `ReceiveMessagesOnPollGroup`/`...OnConnection`을 `Tick()`에서 비운다. 연결 상태 `ClosedByPeer`/`ProblemDetectedLocally` -> `PeerDisconnected`. 별 모양(멤버끼리 연결하지 않는다). `Peers`·`HostPeer`는 `SteamId -> 좌석` 매핑(서버 명단)으로 만든다. 첫 신뢰 메시지(`Hello`)의 `entry_token`을 호스트가 HMAC으로 검증(기존 `PartyNet.EntryToken`). 멤버는 R3 `host_steam_id`로 실제 연결한 호스트 SteamID를 서버에 보고. 연결 12초 안에 안 되면 `State = Failed` |
| `Net/OnlineSession.cs` `Login` | 4단계 mapping 5.1대로 `LoginSteam()`: `ISteamBridge.Current.GetAuthTicket(identityFromMeta, ...)` -> `POST /auth/steam`. `ISteamBridge.Ready`이고 로그인한 SteamID가 계정에 연결된 것과 같을 때만 `caps.steam_p2p = true`(`ChatSocket` hello) |
| `steam_appid.txt` | 내용 `480`(시험 앱 ID). 에디터 실행은 프로젝트 루트, 빌드는 실행 파일 옆. **출시 빌드에는 넣지 않는다**(실제 앱 ID는 Steam 클라이언트가 실행 맥락에서 정한다) |
| `Editor/BuildScript.cs` | 시험 빌드(`-steamTest`)는 `steam_appid.txt`(480)를 출력 폴더에 복사하고, 출시 빌드는 파일이 없는지 검사해 있으면 빌드 실패. Steamworks.NET 네이티브 라이브러리(`steam_api64.dll` 등)가 Standalone 출력에 포함되는지 확인 |
| 앱 ID 전환 | 시험 480 -> 출시: `steam_appid.txt` 삭제, 서버 `STEAM_APP_ID`/`DEPLOY_STAGE=live`, 키 교체. 클라이언트 코드는 바뀌지 않는다(앱 ID를 코드에 쓰지 않고 `SteamUtils.GetAppID()`와 `/meta`만 비교) |

## 5. 서버 쪽 변경 지점

[phase8_api.md 13절](phase8_api.md)과 같다. 핵심만: `domains/relay/*`(새), `domains/fieldsessions/*`(새), `domains/transport/*`(새), `kills/killTarget.ts`·`killService.ts`·`killRules.ts`·`killValidation.ts`(필드 세션), `partyruns/*`(전송 선택, R3 자동 입장, 서버 주도 인계), `party/partyService.ts`(세션 정리 훅), `chat/wsServer.ts`·`wsProtocol.ts`(`caps`, `field.changed`), `auth/steamProvider.ts`(5.5절), `config/env.ts`(변수·가드), `ops/*`(지표·알림·작업).

## 6. 상태 전이 <-> 서버 테이블 (필드 세션)

| 이벤트 | `field_sessions` | `field_session_members` | 중계 방 |
|---|---|---|---|
| F1 첫 입장 | 생성 `active`, 호스트 = 나, `epoch 1` | 행 `joined`, 좌석 0 | (첫 `hello`에서 생성) |
| F1 합류 | `version + 1` | 행 `joined`(또는 되살림), 가장 낮은 빈 좌석 | `peer.joined`(연결 후) |
| F3 `synced=true` | | `joined -> playing` | |
| F3 정체 12초 / 중계 연결 끊김 | | `playing -> disconnected` | `peer.left` |
| 60초 안에 복귀 | | `disconnected -> playing` | `peer.joined` |
| 60초 초과 | 호스트였으면 인계 | `-> left(rejoin_timeout)` | |
| 호스트 끊김(grace 후) / F4 호스트 이탈 | `host_epoch + 1`, 새 호스트(후보 없으면 NULL) | 옛 호스트 `disconnected` 또는 `left` | `host.changed` |
| F4 일반 이탈 / 맵 이동 | `version + 1` | `-> left(left|map_move)` | `peer.left` |
| 파티 나가기·강퇴 | 호스트였으면 인계 | `-> left(party_left|kicked)` | `peer.left(removed)` + 연결 종료(4011) |
| 파티 해산 / 마지막 멤버 이탈 | `-> ended(party_closed|empty)`, `session_key = NULL` | 전원 `left` | `room.closed`, 방 닫힘 |
| 던전 출발(R1·D1) | 호스트였으면 인계, 마지막이면 `ended` | `-> left(dungeon_start)` | `peer.left(removed)` |
| 방치 정리 | `-> ended(stale)` | `-> left(stale)` | 방 닫힘 |
| 전송 전환(T2) | `transport`, `transport_epoch + 1`, `version + 1` | 변화 없음 | `transport.changed` + `bye(4014)` |

## 7. 원장 reason, 이상 기록 kind

원장은 새로 만들지 않는다(필드 세션 처치는 3단계 `xp_ledger('kill')`, `drops` -> `drop_claim` 원장).

| `anomaly_log.kind` | 언제 | severity |
|---|---|---|
| `field_uncredited` | 기여 부채(accepted - credited)가 한도를 넘어 처치를 거절 | 1~2 |
| `field_host` | 무효한 호스트 관찰(공급 한도 초과), 호스트 관찰이 오래 없음 | 1~2 |
| `relay_abuse` | 중계 규칙 위반(금지 채널, 멤버 -> 멤버, 호스트 전용 위조, 한도 초과)이 분당 한도를 넘어 연결 종료 | 2 |
| (기존) `kill_*` | 필드 세션 처치의 속도·공급·화력 거절. 일시 차단 카운트는 호스트에게만 | 기존 |

## 8. 알려진 불일치와 확인이 필요한 사항

| 항목 | 내용 | 조치 |
|---|---|---|
| `NetChannel.Input`의 신뢰성 | 현재 비신뢰 목록. 버튼 한 번 손실 가능 | 신뢰로 올린다(2.1). 중계는 TCP라 영향 없음, Steam은 `Reliable` 플래그 |
| 4단계 문서의 "방장 재연결을 기다리거나 이탈" | Steam P2P만 끊긴 분할 상황에서 서버가 인계를 거절(`HOST_ALIVE`)해 멤버가 갇히는 구멍 | 전송 전환(`host_unreachable`)으로 중계로 옮겨 닫는다(phase8_api 5.3) |
| `PartyNet.OnHello`의 `slot >= humans` | 던전은 시작 때 사람 수 고정, 필드는 동적 | 서버 명단(좌석 집합) 기준으로 대체(4.3) |
| `PartyRunSession.TakeOver`의 `PartyNet.End()` | 전송까지 `Dispose` | `keepTransport`(4.2) |
| 마을 유령 `NetPresence` | 개발 UDP 전용 | 이 단계 범위 밖(phase8_api 17절) |
| `Steamworks.NET` 패키지 | 레포에 아직 없다(`Packages/manifest.json`에 없음) | R3에서 추가, 버전은 그때 확정 |
| 필드 맵 하나 | 현재 세션 가능한 맵은 숲(해골 15마리) 하나 | 규칙은 맵 일반형이고 맵이 늘면 `maps.json`만 바뀐다. `monsterLevel` 내보내기가 레벨 격차 규칙의 전제 |
| `kill_log.context` 'scripted' | 마을 해골 습격 같은 연출 스폰은 세션 대상이 아니다 | `session_id`와 함께 `scripted`로 오면 `422 KILL_REJECTED`(맥락 불일치) |
| 필드 AI 동료 | 서버는 필드의 `ai_count`를 모른다 | 다인 세션에서는 숨겨 일관성을 둔다(결정 대기 5) |
