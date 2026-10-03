# 온라인 멀티 · 파티 구인 기획서 (v1.0)

> 상태: **v1.0 확정.** 서버는 기획만 한다(구현하지 않음). 클라이언트는 온라인을 붙일 수 있는 구조(§3.4)까지 만들었고, 모든 기능은 서버 없이 오프라인으로 동작한다.
> 관련 문서: [PLAN_DUNGEON_RAID.md](PLAN_DUNGEON_RAID.md) (던전·레이드·파티·강화) · [PLAN_AUCTION.md](PLAN_AUCTION.md) (경매장·우편함·귀속 규칙)

## 0. 사용자 의도 기록 (전부 반영 대상)

지금까지 받은 지시를 그대로 남긴다. 기획·구현은 이 목록과 어긋나면 안 된다.

| # | 의도 | 반영 위치 |
|---|---|---|
| U1 | 요일던전과 레이드를 만든다. 판단이 필요한 부분은 게임 전체 구조를 보고 정한다 | PLAN_DUNGEON_RAID §3, §6 |
| U2 | 게임 전체가 **던전앤파이터와 매우 유사**해야 한다 | 두 문서 전체 |
| U3 | 무기 강화 비율을 고친다(강화 +10 = 공격 +50이 장비 등급을 압도하던 문제) | PLAN_DUNGEON_RAID §4 (완료) |
| U4 | **12강 확률**은 던파와 같게 한다 | PLAN_DUNGEON_RAID §4 (완료: 던파 KR 공개 확률) |
| U5 | **던전 클리어** 흐름은 던파와 같게 한다(방 진행, 보스, 클리어, 랭크, 카드 보상) | PLAN_DUNGEON_RAID §6.0 |
| U6 | 작업은 병렬 에이전트로 빠르게 진행한다 | 작업 방식 |
| U7 | **파티원이 없으면 AI 동료와 함께 간다** | §2, §3.3 |
| U8 | **인원을 구인해서 온라인 멀티로 같이 플레이**할 수 있게 한다 | §2~§5 |
| U9 | **서버는 기획만** 한다 | §5 |
| U10 | 사용자 의도는 문서에 전부 넣어 둔다 | 이 표 (새 지시가 오면 행을 추가) |
| U11 | 온라인 기능은 **경매장을 포함해 전부** 넣는다. 결정은 알아서 고려·기획·검토해서 진행 | §6 확정, PLAN_AUCTION, MASTER_CHECKLIST F·G |
| U12 | UI/UX를 사용자 중심으로 개선해 **상용화 품질**까지 올린다. 필요하면 이미지 생성 후 배선 | MASTER_CHECKLIST E |
| U13 | 배경음악은 **Flow Music**으로 생성 | MASTER_CHECKLIST H1 |
| U14 | 요청 전부를 체크리스트로 만들고, 가능하면 병렬로 생산성 있게 | [MASTER_CHECKLIST.md](MASTER_CHECKLIST.md) |

## 1. 목표

| 목표 | 완료 기준 |
|---|---|
| 혼자서도 파티 | 사람이 없으면 AI 용병으로 빈자리를 채워 던전·레이드를 돈다 (구현됨: 파티 기반 1단계) |
| 파티 구인 | 던전·난이도를 정해 파티를 모집하거나, 모집 중인 파티에 참가하거나, 자동 매칭한다 (UI 목업 구현) |
| 온라인 협동 | 최대 4명이 같은 던전 인스턴스에서 함께 싸우고, 보상·강화·드롭은 서버가 판정한다 |
| 빈자리 AI | 사람이 모자라거나 도중에 나가면 그 자리를 AI 용병이 이어받는다 |
| 경매장 | 던파식 경매장으로 아이템을 사고판다 ([PLAN_AUCTION.md](PLAN_AUCTION.md), UI 목업 구현) |
| 서버 설계 | 계정, 캐릭터 저장, 로비·파티, 매칭, 던전 인스턴스, 경제·경매 판정, 채팅의 구조와 API·DB가 문서로 정해져 있다 |

## 2. 플레이 흐름 (던파 방식)

```
마을 ─┬─ 던전 선택 창 ─┬─ [혼자 입장] ────────────── AI 용병 0~3명 편성 → 입장
      │                ├─ [파티 모집] → 모집 글 등록 → 참가자 대기 → 빈자리 AI → 입장
      │                └─ [자동 매칭] → 같은 던전·난이도 대기열 → 4명 또는 60초 → 빈자리 AI → 입장
      ├─ 파티 찾기 창(모집 게시판) → 글 목록(던전·난이도·인원·최소 전투력) → 참가 신청 → 방장 수락
      └─ 경매장 창 / 경매장 NPC → 검색·구매·등록 → 판매 대금·만료 아이템은 우편함
```

### 2.1 파티 찾기 UX (사이드 메뉴 "파티 찾기")
1. **목록 탭**: 행마다 던전 이름, 난이도(색), 인원 `x/4`(사람·AI 표시), 최소 전투력, 방장 이름·직업·레벨, 한 줄 메시지, **[참가 신청]** 버튼. 위쪽에 던전·난이도 필터와 [새로고침](3초 쿨타임).
   - 내 전투력이 최소 전투력보다 낮으면 버튼이 회색 "전투력 부족", 가득 찬 글은 "모집 완료".
   - 신청하면 행에 "신청 중…"이 뜨고, 방장이 수락하면 파티 창으로, 거절·30초 무응답이면 "신청이 거절되었습니다" 토스트.
2. **모집 글 등록**: 던전, 난이도, 모집 인원(2~4), 최소 전투력(내 전투력의 50~100% 슬라이더), 메시지(최대 30자, 금칙어 필터). 등록하면 내 글이 목록 맨 위에 고정되고 [모집 취소]가 생긴다. 글은 10분 뒤 자동 만료.
3. **자동 매칭**: [자동 매칭] → 화면 위쪽에 "매칭 대기 중 00:42 / 1:00 · 2/4명" 표시와 [취소]. 60초가 되면(또는 바로 **[AI로 채워 출발]**) 모인 사람 + AI 용병으로 입장. 설정에서 "60초 후 AI로 자동 출발"을 끌 수 있다.
4. **파티 창**: 멤버 목록(사람/AI 아이콘, 직업, 레벨, 전투력, 준비 완료 체크), 방장 위임, 강퇴, AI 용병 추가/제거, [입장](방장만, 사람 전원 준비 완료 시 활성).
5. **연결 표시**: 파티 창·HUD 파티 프레임에 핑 막대(초록 <80ms, 노랑 <150ms, 빨강 그 이상)와 "연결 끊김 — AI가 대신 싸우는 중" 표시.

### 2.2 던전 안 · 이탈
- 방 진행·부활·랭크·카드는 PLAN_DUNGEON_RAID §6.0과 같다. 카드는 각자 한 장씩 뒤집고 자기 보상만 받는다.
- 사람이 나가면 그 캐릭터는 AI 용병으로 바뀌어 끝까지 간다(보상은 나간 사람에게 지급하지 않음). 방장이 나가면 방장 권한이 다음 사람에게 넘어가고, 새 방장 PC가 몬스터 계산을 이어받는다(마지막 스냅샷부터).
- 레이드: 4인 파티, 주간 보상 귀속은 캐릭터별.

### 2.3 채팅
| 채널 | 범위 | 비고 |
|---|---|---|
| 일반 | 같은 마을(채널) | 초당 1회, 같은 문장 3회 반복 시 10초 금지 |
| 파티 | 파티원 | 던전 안 기본 채널 |
| 귓속말 | 1:1 | 차단한 상대는 수신 안 됨 |
| 시스템 | 나 | 강화 성공(+10 이상) 알림, 경매 판매 알림 |
- **빠른 신호**(휠/숫자키): "모여", "부활해 줘", "기믹 위치", "준비 완료", "고마워". 던전 안 머리 위 말풍선 + 미니맵 핑.
- 친구 목록(접속 상태, 초대), 차단(채팅·초대·귓속말 차단), 신고(채팅 로그 마지막 20줄 첨부, 사유 선택).
- 금칙어 필터는 서버가 최종 판정한다(클라이언트는 미리보기만).

## 3. 클라이언트 구조

### 3.1 이미 있는 것 (파티 기반 1단계)
- 모든 파티원이 같은 `PlayerController`를 쓰고, 조작은 `IActorInput`으로 갈아 끼운다: `LocalInput`, `CompanionBrain`(AI), `ScriptedInput`(테스트).
- `PartyManager`(`Game.Party`): 멤버 최대 4, Local, Leader, 쓰러짐·부활, 딜 미터, 어그로.
- 캐릭터별 스탯(`CharacterData`), `DamageInfo.attacker`로 킬 크레딧. 던전 결과는 `IDungeonAuthority`.

### 3.2 동기화 원칙 (O2 혼합 확정)
- 전투(몬스터 AI, 피격, 위치)는 **방장 PC**가 계산한다. 다른 사람은 `ActorCommand`를 보내고 스냅샷을 받는다.
- 결과가 남는 난수(강화, 드롭, 카드, 경험치, 골드 증감)는 **서버**가 판정한다 → 클라이언트에서는 `IAuthority` 한 곳으로 모은다.
- 전송: 입력 초당 20틱, 위치·HP 스냅샷 초당 10회 + 보간, 스킬·피격·사망은 신뢰 이벤트.

### 3.3 AI 빈자리 규칙
- 입장할 때 사람이 4명 미만이면 방장이 고른 용병(없으면 역할이 겹치지 않게 자동 선택)으로 채운다.
- AI는 방장 PC가 계산한다. 다른 사람에게는 일반 원격 멤버처럼 보인다.
- 사람이 들어오면(던전 입장 전) AI 한 명이 빠진다. 던전 안에서는 교체하지 않는다.

### 3.4 구현된 클라이언트 API (`Assets/Scripts/Runtime/Net/`)
| 타입 | 역할 |
|---|---|
| `ActorId` | 파티원 고유 id. `Account(accountId, characterId)` / `Ai(partySlot)` / `LocalOffline`. 비교·해시·`ToString()` |
| `NetCommand` | `ActorCommand`의 전송용 압축 구조체(12바이트): tick(uint), 이동 방향 8비트×2 양자화, 조준 각도 8비트, 버튼 비트(공격·상호작용·회복·마나·귀환·조준), 스킬 슬롯. `Pack/Unpack`, `Write/Read(byte[])` |
| `NetworkInput : IActorInput` | 받은 `NetCommand`를 틱 순서대로 재생. 버퍼가 비면 마지막 이동을 0.25초 유지 후 정지(원 샷 버튼은 반복하지 않음) |
| `ITransport` / `LoopbackTransport` | 같은 프로세스 안의 두 피어(`LoopbackTransport.CreatePair()`), 전송 지연 틱 설정 가능. 실제 구현은 Steam P2P(O3)로 교체 |
| `RemotePeerDriver` | 피어 쪽 "가상 클라이언트": 명령을 만들어 보내고, 받는 쪽은 `NetworkInput`에 넣는다 |
| `IAuthority` / `LocalAuthority` / `Authority.Current` | 결과 난수·판정 창구(§3.5) |
| `OnlineSession` | 서버 소유 캐릭터 데이터의 자리(읽기 전용 스냅샷, `IsConnected=false`). 오프라인 `GameSession`·세이브와 완전히 분리 |
| `IPartyFinderService` / `MockPartyFinderService` | 파티 찾기·자동 매칭 |
| `IAuctionService` / `MockAuctionService` | 경매장·우편함 (PLAN_AUCTION) |

### 3.5 `IAuthority` 경로
| 판정 | 오프라인(LocalAuthority) | 온라인(ServerAuthority, 미구현) |
|---|---|---|
| 강화 주사위 `EnhanceRoll()` | `UnityEngine.Random.Range(0,100)` (기존과 동일 호출) | `POST /characters/{id}/enhance` 결과 |
| 몬스터 드롭 `RollMonsterDrops(cls, materials)` | 기존 `EnemyController.DropLoot`와 같은 순서·같은 난수 호출 | 방장이 처치 보고 → 서버가 드롭 목록 반환 |
| 던전 랭크·카드·클리어 경험치 | `Dungeon` 속성 = 기존 `DungeonAuthority.Current` | `POST /runs/{token}/result`, `/cards/{i}` |
| 경험치 지급 `GrantXp`, 골드 증감 `ApplyGold` | 그대로 통과(값 변경 없음) | 서버 응답 값으로 덮어씀 |
- 막기 판정(`PlayerController`)은 전투이므로 방장 PC 몫이라 `IAuthority`에 넣지 않는다.

## 4. 동기화 방식 (확정: C 혼합)

| 방식 | 설명 | 장점 | 단점 |
|---|---|---|---|
| A. 방장 권한 + 릴레이 | 방장 PC가 몬스터·판정을 돌리고, 나머지는 입력을 보낸다 | 서버 비용이 적고 빨리 만든다 | 방장 조작 가능, 방장 이탈 시 인계 필요 |
| B. 전용 던전 서버 | 서버가 던전 인스턴스를 돌린다(Unity 헤드리스) | 치팅에 강하고 공정 | 비용·운영 부담 |
| **C. 혼합 (채택)** | 전투는 A, **경제 판정은 백엔드 서버** | 비용을 아끼면서 아이템 경제를 지킨다 | 전투 결과를 서버가 검증해야 한다(§5.1 결과 검증) |

- 네트워크: **Steamworks.NET**(로비·친구 초대·P2P 릴레이, O3). Steam 로비 메타데이터에 파티 글 id를 넣어 친구 초대와 모집 게시판을 잇는다.

## 5. 서버 기획 (구현하지 않음)

```
[클라이언트] ──HTTPS──> [API 게이트웨이] ─┬─ 인증 서비스 (Steam 티켓)
      │                                    ├─ 캐릭터 서비스 (캐릭터·인벤토리·창고·장비·강화)
      │                                    ├─ 경제 서비스 (강화 판정, 드롭, 카드 보상, 상점)
      │                                    ├─ 경매 서비스 (등록·구매·입찰·정산·시세) ── 우편 서비스
      │                                    ├─ 콘텐츠 서비스 (요일 입장 횟수, 레이드 주간 귀속, 초기화)
      │                                    └─ 파티·매칭 서비스 (모집 게시판, 매칭 대기열, 초대)
      └──WebSocket──> [실시간 게이트웨이] ── 채팅, 매칭 알림, 파티 상태, 경매 판매 알림
      └──Steam P2P(릴레이)─> [방장 PC]
```

### 5.1 서비스별 책임
- **인증**: Steam 세션 티켓 검증 → 계정 생성·로그인 → 액세스 토큰(15분) + 갱신 토큰(14일). 접속 시 최소 클라이언트 버전 확인.
- **캐릭터**: 캐릭터 목록, 직업·레벨·스킬·패시브, 인벤토리(아이템 키 `기본id+강화`), 창고, 착용 장비. 모든 변경은 서버 트랜잭션 + 낙관적 잠금(`version`).
- **경제**: 강화는 서버가 난수를 굴린다(확률표 PLAN_DUNGEON_RAID §4). 드롭·카드·경험치는 결과 검증 뒤 지급. 상점 가격도 서버 기준.
- **경매**: [PLAN_AUCTION.md](PLAN_AUCTION.md) §7. 모든 거래는 경매 서비스 한 곳의 직렬 트랜잭션.
- **콘텐츠**: 06:00 일일·목요일 06:00 주간 초기화를 **서버 시계**로 판정.
- **파티·매칭**: 파티 생성·참가·강퇴·위임, 모집 글(10분 만료), 매칭 대기열(던전·난이도·전투력 구간 ±30%), 입장 시 인스턴스 토큰 발급.
- **던전 결과 검증**: 방장이 보낸 결과(클리어 시간, 방별 처치 수, 파티원별 딜·피격)를 몬스터 데이터와 비교. 최소 클리어 시간의 60% 미만, 파티 총 딜 < 몬스터 총 HP 같은 불가능한 값은 보상 보류 + 로그.
- **채팅**: 채널 라우팅, 금칙어, 도배 제한, 차단 목록, 신고 접수.

### 5.2 데이터 (PostgreSQL)
| 테이블 | 핵심 컬럼 |
|---|---|
| accounts | id, steam_id, created_at, banned_until |
| characters | id, account_id, name, class, level, xp, gold, version, deleted_at |
| character_items | id(uuid), character_id, item_key, count, location(bag/storage/worn/mail/auction), slot, bind(none/account/character), version |
| character_progress | character_id, passives, skills |
| content_counters | character_id, key, value, reset_at |
| party_posts | id, leader_character_id, dungeon, difficulty, max_members, min_power, message, expires_at, closed_at |
| party_members | party_id, character_id, slot, is_ai, ready, joined_at |
| match_tickets | id, character_id, dungeon, difficulty, power, queued_at, matched_party_id |
| run_instances | token, party_id, dungeon, difficulty, started_at, result(jsonb 없음: 컬럼별), validated |
| auction_listings / auction_bids / auction_trades / auction_price_daily | PLAN_AUCTION §7.2 |
| mails | id, character_id, kind, item_key, count, gold, expires_at, claimed_at |
| friends / blocks / reports / chat_logs | 소셜 |
| enhance_logs, reward_logs, gold_logs, item_logs | 추가만 하는 감사 로그 |
- 모든 로그 테이블은 추가만 한다(감사·CS 대응). 금액은 bigint.

### 5.3 API (REST, 전부 액세스 토큰 필요)
- `POST /auth/steam` → {accessToken, refreshToken}
- `GET /characters`, `GET /characters/{id}`
- `POST /characters/{id}/enhance` {target, protect} → {outcome, newKey, cost}
- `GET /parties?dungeon=&difficulty=` · `POST /parties` · `POST /parties/{id}/apply` · `POST /parties/{id}/accept/{charId}` · `POST /parties/{id}/kick/{charId}` · `POST /parties/{id}/start` → {instanceToken, lobbyId}
- `POST /match/queue` {dungeon, difficulty} / `DELETE /match/queue` / `POST /match/fill-ai`
- `POST /runs/{token}/kills` {roomIndex, monsterIds} → {drops}
- `POST /runs/{token}/result` {stats} → {rank, xp, cards}
- `POST /runs/{token}/cards/{index}` → {reward}
- 경매·우편: PLAN_AUCTION §7.3
- WebSocket 이벤트: `chat.message`, `party.updated`, `match.found`, `auction.sold`, `mail.arrived`

### 5.4 운영
- 서버 리전: 한국 1개로 시작. 매칭은 같은 리전끼리.
- 부정행위: 결과 검증, 강화·드롭·경매 서버 판정, 오프라인 세이브는 온라인 캐릭터에 쓰지 않음(O1).
- 요청 제한: 계정당 초당 10회(경매 검색은 초당 2회), 초과 시 429.
- 점검 공지·버전 체크: 접속 시 최소 클라이언트 버전 확인.

## 6. 결정 (확정)

| # | 질문 | 결정 | 이유 |
|---|---|---|---|
| O1 | 오프라인·온라인 캐릭터 | **따로**(온라인은 서버 캐릭터) | 오프라인 세이브는 조작할 수 있어서 온라인 경제에 섞으면 안 된다 |
| O2 | 동기화 방식 | **C 혼합**(전투 방장, 경제 서버) | 비용과 경제 보호의 균형 |
| O3 | 네트워크·로비 | **Steam 로비·P2P**(나중에 연결) | 친구 초대·릴레이 무료, 출시 플랫폼 |
| O4 | 아이템 거래·경매장 | **경매장 포함**(던파식) | U11. 상세는 PLAN_AUCTION |
| O5 | 클라이언트 온라인 구조 | **지금 만든다**(§3.4) | 던전 코드가 권한 분리를 지키는지 지금 검증 |

## 7. 남은 일 (서버 연결 시)
- 서버 기술 선택·단계·1단계 상세 설계: [PLAN_SERVER.md](PLAN_SERVER.md) (2026-10-03, Node.js + TypeScript + PostgreSQL)
- `ServerAuthority`(비동기 응답 대기 + 강화 창 "판정 중…" 표시), Steam `ITransport`, 스냅샷 동기화·보간, 방장 인계, `OnlineSession` 실제 데이터 로드, 채팅·친구 UI.
