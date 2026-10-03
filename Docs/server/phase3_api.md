# 서버 3단계 API 명세 (경제 판정)

기준: [PLAN_SERVER.md](../PLAN_SERVER.md) §3(재화 경로), §5(부정행위 방어), §9. 앞 단계: [phase1_2_api.md](phase1_2_api.md). 스키마: `server/schema.sql`, `server/migrations/0002_economy.sql`(확정 범위), `0003_dungeon_solo.sql`(9절 제안, 선택). 게임 값 대응: [phase3_mapping.md](phase3_mapping.md).
이 문서는 설계다. 서버 코드는 dotrpg-backend-coder가 이 문서대로 만든다. 게임 값(가격·확률·보상량)은 문서에 복사하지 않고 `server/data/*.json` 이름으로만 참조한다. 본문에 나오는 수치는 부정행위 방어의 근거 예시이거나 서버 정책 상수이고, 서버는 json에서 계산한다.

3단계의 원칙은 한 줄이다. **클라이언트는 "무슨 일이 일어났나 / 무엇을 하겠다"만 보내고, 서버가 얻은 것·잃은 것·확률·시간을 정한다.** 아래 모든 요청 스키마에는 금액, 수량 보상, 확률, 시간, 레벨, 경험치가 없다(`.strict()`라 보내면 `400 VALIDATION`).

## 0. 공통 규칙

1~2단계의 0.1(응답 형식), 0.2(인증), 0.3(버전 헤더와 426), 0.4(멱등성)를 그대로 쓴다. 3단계 경로는 전부 `/characters/{uuid}/...` 아래라 클라이언트 버전·데이터 버전을 둘 다 검사한다(데이터 버전이 다르면 가격·확률이 어긋나므로 판정을 시작하지 않는다).

### 0.1 멱등성 적용
- 상태를 바꾸는 모든 `POST`는 본문에 `request_id`(UUID v4, 사용자 행동 1회마다 새로 만든다)를 받고 `request_log`에 같은 트랜잭션으로 응답을 저장한다. 같은 id 재전송은 처음 응답을 그대로 돌려주고 `Idempotent-Replay: true`를 붙인다. 같은 id에 다른 본문은 `422 IDEMPOTENCY_MISMATCH`.
- 요청 해시에는 메서드, 경로(캐릭터 uuid 포함), 정규화한 본문을 넣는다(다른 캐릭터·다른 엔드포인트에 같은 id를 재사용하면 불일치).
- 4xx로 실패한 요청은 `request_log`에 저장하지 않는다(롤백). 고쳐서 같은 id로 다시 보내도 된다. 단 **이상 기록(`anomaly_log`)은 롤백되지 않도록 별도 트랜잭션으로 남긴다.**
- 원장 행에는 `request_id`를 같이 적어 "이 요청이 만든 모든 변동"을 찾는다.

### 0.2 동시성과 잠금
- 한 캐릭터를 바꾸는 모든 요청은 트랜잭션 시작 직후 `SELECT ... FROM characters WHERE id=$1 AND deleted_at IS NULL FOR UPDATE`로 그 캐릭터 행을 잠근다. 이것이 유일한 잠금 순서다(다른 행을 먼저 잠그지 않는다 → 교착 없음). 한 캐릭터의 경제 요청은 직렬화되고, 다른 캐릭터끼리는 막지 않는다.
- 캐릭터 소유 검사와 삭제된 캐릭터 처리는 1~2단계와 같다(`404 CHARACTER_NOT_FOUND`로 통일).
- 잠금 아래에서 한 문장으로 끝나야 하는 "한 번만" 규칙(드롭 줍기, 상자, 퀘스트 청구)은 DB 제약이 한 번 더 막는다: `drops.claimed_at IS NULL` 조건부 UPDATE, `character_chests` PK, `quest_claims` PK, `gold_ledger_drop_uq`/`item_ledger_drop_uq`. 코드 버그가 있어도 중복 지급은 DB에서 실패한다.

### 0.3 응답의 `delta` (클라이언트가 로컬 상태를 맞추는 공통 형태)
상태를 바꾸는 응답은 모두 `data.delta`를 가진다. 클라이언트는 이 값으로 자기 복사본을 고치고, 다시 목록을 내려받지 않는다.
```
"delta": {
  "gold": 1234,                    // 바뀐 경우만. 새 잔액
  "level": 5, "xp": 120,           // 바뀐 경우만. 새 값
  "stacks": [ { "item_key": "mat_bone", "location": "bag", "count": 7 } ],  // 건드린 (item_key, location)의 새 수량. 0 = 사라짐
  "worn": [ { "slot": 0, "item_key": "eq_sword_iron+1" } ]                   // 착용 슬롯이 바뀐 경우만. item_key null = 비어 있음
}
```
- 레벨업 시 패시브 포인트는 서버 상태가 아니다. 클라이언트가 `레벨 - 1 - 찍은 개수`로 계산하고, 2단계 `PUT /state`의 패시브 검증이 레벨 대비 개수를 확인한다(기존 규칙 그대로).

### 0.4 날짜 경계
1~2단계 0.5의 `resetBoundaries(nowUtc)` 하나를 쓴다. 3단계는 "현재 일일 구간의 시작"이 필요하므로 이 함수의 반환에 `dailyStartAt`(직전 06:00 KST)과 `weeklyStartAt`(직전 목요일 06:00 KST)을 **같은 함수 안에서** 더한다(`nextDailyAt`/`nextWeeklyAt`과 한 쌍). 던전 입장 횟수(9절)가 `dailyStartAt`을 쓴다. 클라이언트 시계는 어디에도 쓰지 않는다.

### 0.5 서버 RNG
- 모든 확률 굴림(드롭, 강화, 던전 카드)은 서버가 Node `crypto.randomInt`로 굴린다. 시드 고정은 테스트에서 주입 가능한 `Rng` 인터페이스로만 한다.
- C# 규칙과 맞출 점: 확률 비교는 C#과 같은 방향(재료 `Random.value > chance`면 실패 → 서버는 `u <= chance`일 때 성공), 정수 범위는 반개구간 `[min, max)`(필드 골드) 또는 폐구간(재료 수량, 골드 해골 `goldMin..goldMax`)을 mapping 문서의 표대로 따른다. 경험치 반올림은 C# `Mathf.RoundToInt`(.5는 짝수 쪽)와 같게 한다.

### 0.6 속도 제한 (메모리, 나중에 Redis 교체 가능한 인터페이스 뒤)
초과하면 `429 RATE_LIMITED` + `Retry-After`. 값은 환경변수로 바꾼다. 3단계 경로는 IP당 한도(1~2단계의 "그 외" 120회/분)를 **600회/분으로 올리고**(전투 중 보고가 잦다) 캐릭터별 한도를 쓴다.

| 엔드포인트 | 캐릭터당 한도 | 근거 |
|---|---|---|
| 처치 보고 | 1초 6회, 분당 90회(거절 포함) | 한 번에 여러 마리가 죽는 범위기 최대 4~5(3.2절), 지속 상한 0.64마리/초의 여유 |
| 줍기 | 초당 5회 | 한 번에 최대 50개를 묶어 보낸다 |
| 채집 | 초당 3회 | 노드 사이 이동과 타격 시간 |
| 노드 쿨다운 조회 | 초당 1회 | 맵 입장 때 1회 |
| 상점, 장착, 창고 | 초당 5회 | 사람이 누르는 속도 |
| 강화 | 초당 2회 | 연타 방지(연출 시간) |
| 아이템 사용 | 초당 3회 + 같은 아이템 0.5초 간격 | C# 물약 재사용 대기 0.6초(PlayerController.PotionReady)에 지연 여유 |
| 퀘스트 청구, 상자, 납품, 던전 | 초당 1회 | 사람이 누르는 속도 |

## 1. 엔드포인트 요약 (신규 18개 + 확장 1개)

경로는 모두 `/characters/{uuid}` 아래이고 인증은 액세스 토큰이다. 표의 단계 열은 "3"이 확정 범위, "3b"가 9절 제안(솔로 던전)이다.

| # | 메서드 | 경로 (`/characters/{uuid}` 뒤) | 하는 일 | 단계 |
|---|---|---|---|---|
| 0 | GET | `` (기존) | 상세에 필드 추가(2절) | 3 |
| 1 | POST | `/kills` | 처치 보고: 경험치 + 드롭 굴림 | 3 |
| 2 | POST | `/drops/claim` | 드롭 줍기(드롭 id로만, 한 번만) | 3 |
| 3 | POST | `/gathers` | 채집 보고(목재·돌·당근) | 3 |
| 4 | GET | `/maps/{map_id}/nodes` | 재생 중인 노드 목록 | 3 |
| 5 | POST | `/chests/open` | 상자 열기(캐릭터당 한 번) | 3 |
| 6 | POST | `/deliveries` | 공사장 재료 납품 | 3 |
| 7 | POST | `/quests/{quest_id}/claim` | 퀘스트 보상 청구(한 번만) | 3 |
| 8 | POST | `/shop/buy` | 상점 구매 | 3 |
| 9 | POST | `/shop/sell` | 상점 판매 | 3 |
| 10 | POST | `/enhance` | 강화(서버가 굴림) | 3 |
| 11 | POST | `/items/use` | 물약·주문서·당근 사용 | 3 |
| 12 | POST | `/storage/move` | 가방 <-> 창고 이동 | 3 |
| 13 | POST | `/equipment/equip` | 장착 | 3 |
| 14 | POST | `/equipment/unequip` | 해제 | 3 |
| 15 | GET | `/dungeons` | 오늘의 던전 상태 | 3b |
| 16 | POST | `/dungeon-runs` | 던전 입장(일일 횟수 차감) | 3b |
| 17 | POST | `/dungeon-runs/{run_id}/result` | 결과 보고: 검증, 랭크, 클리어 경험치, 카드 | 3b |
| 18 | POST | `/dungeon-runs/{run_id}/cards/pick` | 카드 한 장 고르기 | 3b |

PLAN_SERVER §3 표에 없던 재화 경로 4개를 코드에서 찾아 추가했다: 상자(5), 공사장 납품(6), 퀘스트 최대 체력 보너스(7의 응답), 황금 해골 타격 골드(3.1의 `hits`). 상세는 mapping 문서 5절.

공통 에러(엔드포인트마다 반복하지 않는다): `400 VALIDATION`, `401 TOKEN_*`, `403 ACCOUNT_BANNED`, `404 CHARACTER_NOT_FOUND`, `422 IDEMPOTENCY_MISMATCH`, `426`, `429 RATE_LIMITED`. 규칙 위반은 `422`, 이미 처리된 "한 번만" 규칙은 `409`.

## 2. GET /characters/{uuid} 확장 (엔드포인트 0)

1~2단계의 `CharacterDetail`에 아래 필드를 더한다(기존 필드·모양은 그대로, `items`에는 이제 `bag`, `storage`, `worn` 행이 모두 나온다).

| 필드 | 형태 | 의미 |
|---|---|---|
| `bonus_max_health` | int | 청구한 퀘스트 보상의 `max_health` 합계(`quest_claims.reward`). 클라이언트가 기본 최대 체력에 더한다 |
| `claimed_quests` | string[] | 보상을 청구한 퀘스트 id. 접속 때 로컬 상태와 맞추는 데 쓴다(7절) |
| `opened_chests` | string[] | 연 상자 id(`SaveData.openedChests` 대체) |
| `enhance_pity` | `[{item_key, pity}]` | 강화 천장. 클라이언트가 확률 표시에 쓴다 |
| `deliveries` | `[{site_id, items:[{item_key, delivered, required}]}]` | 납품 진행(`required`는 서버 데이터) |
| `storage_capacity` | int | 창고 칸 수(서버 데이터) |

- `items[].id`(행 uuid)는 3단계에서 행이 지워지고 다시 생기므로 안정적이지 않다. 3단계 API는 모두 `item_key + location`으로 대상을 가리킨다.
- 조회 인덱스: `character_items_char_loc`, `quest_claims`/`character_chests`/`character_enhance_pity`/`site_deliveries`는 PK `(character_id, ...)` 접두 조회.

## 3. 처치·드롭·줍기

### 3.1 POST /kills (엔드포인트 1)
- 요청(zod `.strict()`):
```
{
  request_id: uuid,
  map_id: string(1..32),
  monster_id: string(1..40),
  hits?: int(0..60),          // 황금 해골류가 타격마다 골드를 흘린 횟수. 해당 몬스터가 아니면 무시(0)
  run_id?: uuid,              // 던전 처치(9절). 함께 room_index 필수
  room_index?: int(0..9)
}
```
  `refine`: `run_id`와 `room_index`는 둘 다 있거나 둘 다 없다. **받지 않는 값: 경험치, 골드, 드롭 종류·수량, 몬스터 레벨, 시각.**
- 클라이언트 규칙: 소환체(사령술사가 부른 해골, 보스 토템 등 `noLoot`)는 보고하지 않는다.
- 처리(한 트랜잭션, 0.2의 잠금 후):
  1. 몬스터 정의 조회(`monsters.json`). 없는 id는 `422 MONSTER_UNKNOWN`. `noLoot`이면 `422 MONSTER_NO_REWARD`. `raid`면 `422 RAID_NOT_AVAILABLE`(레이드는 4단계).
  2. 맥락 판정과 그럴듯함 검사(3.2). 실패하면 `anomaly_log`에 별도 트랜잭션으로 기록하고 거절한다.
  3. 몬스터 레벨 결정(필드 1, 던전은 `1 + 난이도 monsterLevel + 그룹 levelOffset`). 경험치 = `round(monster.xp * (1 + xpPerLevel * (레벨 - 1)))`.
  4. 경험치 지급(Progression.AddXp 규칙, 3.1.1) → `xp_ledger('kill')`, `characters.level/xp`.
  5. 드롭 굴림(3.1.2) → `drops` 행들(만료 시각 = 지금 + `DROP_TTL_SECONDS`).
  6. `kill_log` INSERT, `kill_stats` 누적 +1(`ON CONFLICT DO UPDATE`).
  7. 응답을 `request_log`에 저장하고 커밋.
- 응답 `200` `data`:
```
{
  "granted_xp": 20, "leveled_up": false,
  "drops": [ { "id": uuid, "item_key": "gold", "count": 12, "expires_at": ISO } ],
  "delta": { "level": 5, "xp": 130 }
}
```
  골드는 `item_key: "gold"`인 드롭으로 내려온다(줍기 전에는 잔액이 늘지 않는다). 드롭 한 행이 C# `Pickup` 하나에 대응한다.
- 에러: `422 MONSTER_UNKNOWN`, `422 MONSTER_NO_REWARD`, `422 RAID_NOT_AVAILABLE`, `422 KILL_REJECTED`(그럴듯하지 않음, 사유를 알리지 않는다), `429 KILL_RATE_LIMITED`(속도 창 초과, `Retry-After`), `403 KILL_BLOCKED`(최근 이상 기록 반복으로 일시 차단, 3.2.4), `409 RUN_NOT_PLAYING`(9절), 공통 에러.
- 멱등성: `request_id`. 재전송은 같은 드롭 id 목록을 돌려준다(경험치·드롭 이중 지급 없음).
- 인덱스: `kill_log_char_time`(속도 창), `kill_stats` PK, `drops_char_open`(미수령 개수 상한).

#### 3.1.1 경험치 지급 규칙 (Progression.AddXp)
`amount <= 0`이거나 레벨이 만렙(`progression.json.maxLevel`)이면 아무 일도 없다. 아니면 `xp += amount`, `xp >= xpToNext[level-1]`인 동안 `xp -= xpToNext[level-1]; level++`. 만렙에 닿으면 `xp = 0`. 이 함수 하나가 처치, 퀘스트, 던전 클리어의 경험치를 모두 처리한다. 파티 분배는 4단계라 지금은 처치한 본인이 전부 받는다(C#도 솔로에서 100%).

#### 3.1.2 드롭 굴림 (EnemyController.DropLoot / MonsterLoot)
`noLoot`이 아닌 모든 몬스터가 아래를 굴린다(보스 포함). 값은 전부 데이터에서 읽는다.
1. **기본 골드 1더미**: `monsters.json`의 `fieldGoldMin .. fieldGoldMaxExclusive`(반개구간).
2. **몬스터 추가 골드**(`goldMax > 0`인 몬스터: 황금 해골, 보스): 총액 `goldMin..goldMax`(폐구간)를 굴려 더미 수 `clamp(총액 / 20, 2, 6)`개로 나눈다(마지막 더미가 나머지). 나눗셈 상수는 mapping 문서 4절의 추가 내보내기 대상.
3. **타격당 골드**(`goldPerHitMax > 0`인 몬스터, 황금 해골): `hits`번 각각 `goldPerHitMin..goldPerHitMax` 더미 1개. `hits`는 `min(보고값, ceil(몬스터 실효 HP / 플레이어 기본 공격력), 40)`으로 자른다(어떤 타격도 기본 공격력 이상을 입히므로 이 이상은 불가능). 자른 값은 `kill_log.hits`.
4. **재료**: `shop.json.materials`를 표 순서대로, 각각 `u <= dropChance`이면 `minDrop..maxDrop`(폐구간)개. 한 개가 한 드롭 행이다.
5. **장비**: `u <= equipmentDropChance(0.4)`이면 `shop.json.equipment` 중 `dropWeight > 0`이고 캐릭터 직업이 쓸 수 있는(`classOnly`가 null이거나 같은 직업) 장비를 `dropWeight` 가중으로 하나. 아이템 키는 +0 기본 id.

### 3.2 처치 보고의 그럴듯함 기준
서버는 처치가 **실제로 일어났는지** 알 수 없다. 대신 "물리적으로 가능한 최대치"를 넘는 보고만 막는다. 정직한 클라이언트는 이 한도에 걸리지 않게 한도를 공급·화력 한계보다 느슨하게 잡는다. 규칙은 싼 것부터 순서대로 검사한다.

#### 3.2.1 맥락과 대상 (kill_target)
- `run_id`가 없으면 필드 맥락이다. `map_id`가 `maps.json`에 있고 `instanced=false`여야 하며:
  - `maps[map_id].fieldSpawns`에 `monster_id`가 있으면 `context='field'`.
  - 아니면 `maps[map_id].scriptedSpawns`에 있고(연출 스폰, 현재 마을의 해골 습격) 그 캐릭터의 누적 처치 수 `kill_stats` < 총 스폰 수이면 `context='scripted'`.
  - 그 밖에는 거절한다(예: 마을에서 보스 처치, 숲에서 황금 해골).
- `run_id`가 있으면 9절의 던전 맥락이다.
- `map_id`는 클라이언트가 보고한 값이지만 서버가 위 표에 대조한다(2단계 문서가 `PUT /state`의 `map_id`를 신뢰하지 않겠다고 한 약속). 저장된 `character_state.map_id`와는 비교하지 않는다(저장은 주기적이라 최신이 아니다).

#### 3.2.2 속도 (kill_rate, kill_supply)
| 검사 | 필드(forest 해골) | 던전 방 | 연출 스폰 | 근거 |
|---|---|---|---|---|
| 1초 창 처치 수 | 4 이하 | 6 이하 | 4 이하 | 범위 스킬이 한 번에 죽이는 수: 번개 사슬 첫 대상 + 연쇄 3 = 4(theory_skills.py), 던전 무리 5마리 + 1(theory_skills.py `PACK=5`) |
| 리스폰 창 처치 수 | `ceil(fieldSpawns.points * 1.1)` 이하 / `fieldSpawns.respawnSeconds`초 | 방의 그룹 `count` 합을 넘지 못함(누적) | 평생 총 스폰 수 | 스폰점마다 처치 후 `respawnDelay`초가 지나야 다시 나온다(EnemySpawner). 스폰점 수 N, 리스폰 R초이면 R초 안에 최대 N마리다 |

- 필드 공급 상한 = `N / R`(마리/초). 현재 숲은 스폰점 15개, 리스폰 25초라 **0.6마리/초가 이론 최대**다(모든 점을 쉬지 않고 돌아도 넘을 수 없는 값). 스폰점 간격이 약 6~13칸이고 이동 속도가 4.2칸/초라 현실 속도는 대략 0.3~0.5마리/초이므로 정직한 플레이는 한도 아래다. 값(15, 25)은 근거 예시이고 서버는 `maps.json`/`monsters.json`에서 읽는다.
- 속도 창은 **서버 수신 시각** 기준으로 `kill_log`에서 센다(`created_at > now() - 창`). 네트워크 지연으로 보고가 뭉쳐 도착해도 1초 창 한도(4~6)가 흡수하도록 필드 한도에 1 여유를 두었다.
- 1초 창 초과는 `429 KILL_RATE_LIMITED`(정직한 클라이언트도 드물게 닿을 수 있으므로 재시도 안내), 리스폰 창 초과는 `422 KILL_REJECTED` + `anomaly_log(kill_supply)`.

#### 3.2.3 화력 상한 (kill_power) - 레벨 대비 상한
처치한 몬스터들의 실효 HP 합이 이 캐릭터의 화력 상한 x 시간을 넘으면 거절한다. 화력 상한은 **서버가 아는 상태**(레벨, 착용 장비, 직업)로 계산한다.
```
attackCap(level, worn) = (baseAttack + sum(착용 장비 공격력(강화 단계 반영))) * (1 + 0.11 * (level - 1))
dpsCap                 = attackCap * (1 / attackCooldown) * 2.0          // 2.0 = 스킬이 더하는 몫의 상한
창 안 실효 HP 합 <= dpsCap * 창 길이 * AOE_CAP(5) + 창 안 가장 큰 몬스터 HP
effHp = monster.hp * hpMul * (1 + hpPerLevel * (monsterLevel - 1))        // 필드 hpMul = 1, 던전은 난이도 hpMul * 파티 배율
```
- 근거: 플레이어 기본 공격력 `PlayerStats.attackDamage`(10), 전사 기본 공격 간격 0.36초(`attackCooldown`, 마법사 구슬은 0.5초이므로 느슨한 쪽인 0.36을 쓴다), 패시브는 점당 피해 약 5.5%(theory_balance.py)이고 모든 점을 피해에 쓴다고 가정해도 레벨당 5.5%라서 노드 효과 차이를 감안해 2배(11%)를 상한으로 둔다(passive_tree.json에 효과를 내보내면 정밀화, mapping 4절), `AOE_CAP`은 theory_skills.py의 무리 한도 5.
- 창 길이: 필드는 직전 10초, 던전은 입장 후 서버 경과 시간 + 5초.
- **필드에서는 이 검사가 거의 걸리지 않는다**: 해골 HP 30은 장비 없는 Lv1(공격 10)도 3타(약 1.1초), 철검(공격 +10)이면 2타, 이후엔 1타에 죽는다. 마리당 처치 시간은 1초 안팎이라 공급 상한(3.2.2)이 먼저 걸린다. 화력 상한은 보스 방과 높은 난이도(HP 배율 x 몬스터 레벨 보정)에서 의미가 있고, 저레벨 캐릭터가 고난이도 몬스터를 비현실적으로 빨리 잡는 보고를 막는 초기 방어선이다. 이 상한은 느슨하게 잡은 것이라 속도·구조 검사가 주된 방어다.

#### 3.2.4 반복 이상과 차단
- 위 검사로 거절할 때마다 `anomaly_log`에 `kind`, `severity`(경계 오차 가능 1 / 의심 2 / 불가능 3), `detail`(창 안 수, 한도, 맵·몬스터)을 남긴다.
- 최근 10분(`ANOMALY_WINDOW_MINUTES`) 안에 같은 캐릭터의 `kill_*` 이상이 20회(`ANOMALY_BLOCK_COUNT`) 이상이면 10분간 처치 보고를 `403 KILL_BLOCKED`로 막는다. 이 숫자는 정직한 클라이언트가 네트워크 지연으로 가끔 닿는 수준(severity 1이 몇 번)보다 훨씬 위로 잡은 시작값이고 운영하며 조정한다. 영구 제재(정지)는 7단계 관리자 도구가 `anomaly_log`를 보고 정한다.
- PLAN_SERVER §5의 "지급 보류"는 **거절 + 기록**으로 구현한다(보류 대기열은 두지 않는다: 처치는 되돌릴 근거가 없고, 던전 결과만 9절에서 `held` 상태로 보류한다).

### 3.3 POST /drops/claim (엔드포인트 2)
- 요청: `{ request_id: uuid, drop_ids: uuid[1..50] }`. 드롭 id 이외의 값은 없다(어떤 아이템·수량인지는 서버 행이 정한다).
- 처리(한 트랜잭션, 0.2의 잠금 후):
  1. `UPDATE drops SET claimed_at = now() WHERE uuid = ANY($ids) AND character_id = $me AND claimed_at IS NULL AND expires_at > now() RETURNING ...` 한 문장. 반환된 행만 지급한다.
  2. 골드 드롭은 합산해 `characters.gold += 합`, `gold_ledger('drop_claim')` **드롭마다 1행**(`ref` = 드롭 uuid). 아이템 드롭은 가방 스택에 더하고(`ON CONFLICT (character_id, location, item_key) WHERE location IN ('bag','storage') DO UPDATE SET count = character_items.count + EXCLUDED.count`) `item_ledger('drop_claim')` 드롭마다 1행.
  3. 요청에 있었지만 반환되지 않은 id를 분류해 결과에 싣는다: 이미 수령(`already_claimed`), 만료(`expired`), 내 것이 아니거나 없음(`not_found`). 남의 id가 하나라도 섞이면 `anomaly_log(drop_foreign)`을 남긴다(내 것과 남의 것을 구별할 수 없게 둘 다 `not_found`).
- 응답 `200` `data`:
```
{
  "results": [ { "id": uuid, "result": "claimed" | "already_claimed" | "expired" | "not_found" } ],
  "delta": { "gold": 5000, "stacks": [ { "item_key": "mat_bone", "location": "bag", "count": 7 } ] }
}
```
- 한 번만 보장: ① 조건부 UPDATE, ② 캐릭터 행 잠금, ③ `gold_ledger_drop_uq`/`item_ledger_drop_uq`(같은 드롭의 원장 행은 하나뿐). 동시에 같은 id를 두 요청이 보내도 한쪽만 `claimed`다.
- 에러: 공통 에러만. 일부 id가 실패해도 `200`이다(결과로 알린다). 전부 실패해도 `200`.
- 인덱스: `drops.uuid` UNIQUE(조건부 UPDATE의 조회 키).

## 4. 채집·노드·상자·납품

### 4.1 POST /gathers (엔드포인트 3)
- 요청: `{ request_id: uuid, map_id: string, node_id: string }`. `node_id`는 `"맵id:x:y"`(WorldBuilder 격자 좌표, 상자 id와 같은 규칙)이고 `maps.json`의 `nodes[].id`에 있는 값이다. 지급 수량·재생 시간은 받지 않는다.
- 처리: 노드 조회(`maps[map_id].nodes`, 없으면 `422 NODE_UNKNOWN` + `anomaly_log(gather_node)`) → 종류(`tree`/`rock`/`crop`)로 지급 아이템·수량·재생 시간을 서버 데이터(`gameconfig.json`)에서 읽는다 → `character_node_state`의 `last_gathered_at`이 없거나 `now >= last + 재생 시간 - GATHER_GRACE_SECONDS(3)`이면 허용 → 가방에 지급(`item_ledger('gather')`, `ref`=node id) → `character_node_state` upsert.
- 응답 `200` `data`: `{ "granted": { "item_key": "wood", "count": 2 }, "ready_at": ISO, "delta": {...} }`
- 에러: `422 NODE_UNKNOWN`, `409 NODE_NOT_READY`(`errors.ready_at` 포함, `anomaly_log(gather_early)` severity 1), `429`.
- **서버가 노드 상태를 가진다(게임 동작 변경)**: C#은 맵에 들어올 때마다 노드가 가득 찬 상태로 다시 만들어져 맵을 나갔다 오면 즉시 다시 캘 수 있다. 서버는 재생 시간을 실제 시각으로 센다. 그래서 클라이언트는 맵을 불러올 때 4.2를 읽어 아직 재생 중인 노드를 그루터기 상태로 시작한다. `GATHER_GRACE_SECONDS`는 보고 지연 여유다(C#은 노드가 쓰러진 시점부터 재생을 세는데 서버는 보고를 받은 시점부터 센다).
- 허용량 = 노드별 재생 시간이다. 노드 수 N인 맵에서 시간 T 동안 얻을 수 있는 최대 채집 수는 `N x (1 + T / 재생 시간)`을 넘을 수 없다(노드별 규칙의 합).

### 4.2 GET /maps/{map_id}/nodes (엔드포인트 4)
- 응답 `200` `data`: `{ "map_id": "forest", "cooling": [ { "node_id": "forest:12:30", "kind": "tree", "ready_at": ISO } ] }` - 아직 `ready_at`이 지나지 않은 노드만. 없는 맵·던전 방은 `422 MAP_INVALID`.
- 조회: `character_node_state_map` 인덱스(`character_id, map_id, last_gathered_at`)를 `last_gathered_at > now() - 최대 재생 시간`으로 읽는다.

### 4.3 POST /chests/open (엔드포인트 5)
- 요청: `{ request_id: uuid, chest_id: string }`. 보상은 서버 데이터(`gameconfig.json.chestReward`)가 정한다.
- 처리: `maps.json`의 `chests[]`에 없는 id는 `422 CHEST_UNKNOWN` + `anomaly_log(chest_unknown)` → `character_chests` INSERT(PK 충돌이면 `409 CHEST_ALREADY_OPENED`) → 가방에 지급, `item_ledger('chest')`.
- 응답 `200` `data`: `{ "chest_id": "...", "granted": { "item_key": "...", "count": 1 }, "delta": {...} }`

### 4.4 POST /deliveries (엔드포인트 6)
- 요청: `{ request_id: uuid, site_id: "workshop" }`. 수량은 받지 않는다(서버가 `필요량 - 낸 양`과 가방 수량의 최솟값을 옮긴다. C# `DeliverMaterials`와 같다).
- 처리: `gameconfig.json.deliverySites[site_id]`의 `requiresQuestClaimed`(선행 퀘스트)가 청구되어 있어야 한다(`422 SITE_LOCKED`) → 이미 다 냈으면 `409 SITE_COMPLETE` → 각 재료 `min(필요량 - 낸 양, 가방 수량)`을 가방에서 빼고(`item_ledger('delivery')`) `site_deliveries`를 올린다. 옮긴 양이 0이면 `422 NOTHING_TO_DELIVER`.
- 응답 `200` `data`: `{ "site_id": "workshop", "items": [ { "item_key": "wood", "delivered": 6, "required": 6 } ], "complete": true, "delta": {...} }`. `complete`가 되면 클라이언트가 공방 완공 연출을 하고 스토리 플래그를 로컬에 세운다(플래그는 2단계 `PUT /state`로 저장되며, 퀘스트 청구는 플래그가 아니라 `site_deliveries`를 직접 확인한다, 5절).

## 5. 퀘스트 보상 청구

### POST /quests/{quest_id}/claim (엔드포인트 7)
- 요청: `{ request_id: uuid }`. 보상은 요청에 없고 `quest_index.json`의 `reward`가 정한다.
- 청구는 클라이언트가 저장한 퀘스트 상태(`character_state.quests`)를 읽지 않는다. 상태 저장은 비동기라 순서가 어긋나기 때문이다. 서버는 자기 기록(`quest_claims`, `kill_stats`, `site_deliveries`, `dungeon_runs`)으로 판단한다.
- 처리(0.2의 잠금 후, 위에서부터 첫 실패에서 중단):
  1. 퀘스트 존재(`422 QUEST_UNKNOWN`). 이미 청구했으면 `409 QUEST_ALREADY_CLAIMED`(다른 `request_id`로 재청구해도 같다. 같은 `request_id`면 멱등 응답).
  2. 선행 퀘스트(`requires`)가 모두 청구되어 있다(`422 QUEST_PREREQ`, `errors.missing[]`). `minLevel <= 레벨`(`422 QUEST_LEVEL`). `requiresFlags`는 저장된 `story_flags`에 있어야 한다(`422 QUEST_FLAGS`, 클라이언트 저장 값이라 약한 검사).
  3. **서버가 검증할 수 있는 목표**(`quest_index.json`의 요약 필드):
     - `level` 목표: 서버 레벨 >= 필요 레벨.
     - `kill` 목표: `kill_stats`의 누적 처치 >= **(이미 청구한 퀘스트들과 이 퀘스트의 같은 몬스터 처치 목표 합)**. 목표가 퀘스트마다 0부터 다시 세어지는 C#보다 같거나 약하며, 서버가 받아들인 처치만 센다.
     - `collect + consume` 목표: 가방에 필요 수량이 있어야 하고 **청구 시점에** 가져간다(`item_ledger('quest_consume')`). C#은 단계가 끝나는 때 가져가지만 현재 데이터에서 consume 퀘스트는 한 단계짜리라 시점 차이가 없다.
     - `flag` 목표 중 공방 완공(`workshop_built`): `site_deliveries`가 필요량을 채웠어야 한다.
     - `dungeon` 목표: 9절을 채택하면 `dungeon_runs`의 클리어 수 >= (청구한 퀘스트들과 이 퀘스트의 같은 목표 합), 채택하지 않으면 `422 QUEST_OBJECTIVE_UNSUPPORTED`.
     - `raid` 목표: 4단계 전까지 `422 QUEST_OBJECTIVE_UNSUPPORTED`(`errors.types`에 목표 종류).
  4. **서버가 검증할 수 없는 목표**(`talk`, `cutscene`, `interact`, `reach`, 그 밖의 `flag`): 검사하지 않는다. 아래 "남는 위험"을 참고.
  5. 지급: 경험치(3.1.1 규칙, `xp_ledger('quest_reward')`), 골드(`gold_ledger('quest_reward')`), 아이템(`item_ledger('quest_reward')`), `quest_claims` INSERT(`reward` 스냅샷에 `max_health`, `set_flags`, 가져간 재료 포함).
- 응답 `200` `data`:
```
{
  "quest_id": "c1_rebuild",
  "reward": { "xp": 200, "gold": 0, "items": [ { "item_key": "eq_ring_ruby", "count": 1 } ], "max_health": 20, "set_flags": [] },
  "consumed": [ { "item_key": "wood", "count": 5 } ],
  "bonus_max_health": 20,
  "leveled_up": false,
  "delta": {...}
}
```
  `set_flags`는 서버가 `character_state`에 쓰지 않고 알려 주기만 한다(플래그는 추가만 허용되는 클라이언트 상태라 서버가 건드리면 다음 `PUT /state`와 충돌한다). 클라이언트가 로컬에 세우고 다음 저장에 포함한다.
- 접속 동기화: 클라이언트는 접속 때 로컬에서 `Completed`인데 `claimed_quests`에 없는 퀘스트를 발견하면 청구를 다시 보낸다(청구 요청 응답을 못 받고 끊긴 경우의 복구). 청구는 `quest_id` 기준으로 한 번만이라 안전하다.
- **남는 위험(수용 여부는 사용자 결정, 11절)**: `talk`·`cutscene`·`interact`·`reach` 목표는 서버가 확인할 방법이 없다. 선행 퀘스트 사슬과 위 검증으로 막히지 않는 구간은 조작으로 앞당겨 청구할 수 있다. 상한이 있고(퀘스트당 한 번) 값은 유한하다: 현재 데이터의 전체 퀘스트 보상 합계는 경험치 약 31,900(레벨 25 도달에 해당), 골드 약 88,300이다. 레이드 목표가 있는 퀘스트(`c1_fortress` 이후)는 4단계 전까지 막히므로 3단계에서 실제로 청구 가능한 구간은 9절을 채택하면 `c1_stronger`까지(메인 경험치 약 1,300, 골드 약 3,300), 채택하지 않으면 요일 던전 목표가 있는 `c1_stronger` 앞의 `c1_trail`까지와 레이드와 무관한 부가 퀘스트다. 청구 시각이 `quest_claims.claimed_at`에 남아 사후 분석이 가능하다.
- 에러: `422 QUEST_UNKNOWN`, `409 QUEST_ALREADY_CLAIMED`, `422 QUEST_PREREQ`, `422 QUEST_LEVEL`, `422 QUEST_FLAGS`, `422 QUEST_NOT_DONE`(`errors.objective`에 부족한 목표: 처치 수·재료·레벨·납품), `422 QUEST_OBJECTIVE_UNSUPPORTED`, `429`. 목표 부족·선행 누락처럼 정직한 클라이언트가 만들 수 없는 상황은 `anomaly_log(quest_denied)`.
- 인덱스: `quest_claims` PK(청구 여부, 선행 퀘스트 일괄 확인 `character_id = $1 AND quest_id = ANY($2)`), `kill_stats` PK.

## 6. 상점

가격은 서버 데이터가 정한다(`shop.json`). 클라이언트는 아이템과 개수만 보낸다. 상점 NPC 근처인지는 서버가 알 수 없어 검사하지 않는다(위치는 비신뢰 상태).

### POST /shop/buy (엔드포인트 8)
- 요청: `{ request_id: uuid, item_id: string, count: int(1..999) }`
- 처리: `item_id`가 `shop.json.stock`에 있어야 한다(`422 NOT_FOR_SALE`). `총액 = buyPrice * count`, 골드가 모자라면 `422 NOT_ENOUGH_GOLD`(`errors.need`, `errors.have`). 전부 아니면 없음(C#은 모자라면 가능한 만큼만 사는데, 그 계산은 클라이언트가 가진 골드 표시로 해서 가능한 개수를 보낸다). `characters.gold -= 총액`(`CHECK >= 0`이 마지막 안전장치), 가방에 지급, 원장 `shop_buy`(골드 1행 + 아이템 1행, `ref` = item id).
- 응답: `{ "item_id", "count", "unit_price", "total", "delta" }`
- 에러: `422 NOT_FOR_SALE`, `422 NOT_ENOUGH_GOLD`.

### POST /shop/sell (엔드포인트 9)
- 요청: `{ request_id: uuid, item_key: string, count: int(1..9999) }`
- 처리: 대상은 가방(`bag`)의 스택이다(착용·창고는 못 판다). 판매가는 서버가 계산: 장비 키는 `round_half_away(shop.json.equipment[base].sellPrice * (1 + enhancedSellBonusPerLevel * 강화 단계))`, 그 밖은 `shop.json.materials/sellPrices`. 판매가 0(보호권 등)이면 `422 NOT_SELLABLE`. 수량이 모자라면 `422 NOT_ENOUGH_ITEMS`. 가방에서 빼고 `characters.gold += 총액`, 원장 `shop_sell`.
- 응답: `{ "item_key", "count", "unit_price", "total", "delta" }`
- 강화된 장비를 한 번 누름으로 파는 것을 막는 확인창은 클라이언트 몫이다.

## 7. 강화

### POST /enhance (엔드포인트 10)
- 요청: `{ request_id: uuid, target: { worn_slot: int(0..5) } | { bag_key: string } }` (둘 중 정확히 하나). **받지 않는 값: 확률, 비용, 주사위 값, 결과.**
- 처리(0.2의 잠금 후, C# `Equipment.TryEnhance`와 같은 규칙):
  1. 대상 키 확인: 착용 슬롯의 행 또는 가방 스택 수량 >= 1. 없으면 `422 ENHANCE_INVALID_TARGET`. 장비가 아니면 같은 에러.
  2. 레벨 >= `enhance.json.maxEnhance`이면 `422 ENHANCE_MAX_LEVEL`.
  3. 시도 정보: `enhance.json.steps[아이템].levels[현재 레벨]`의 `successPercent`, `failure`, `pity`(천장 적용 레벨인지), 비용(`gold`, `bone`, `ore`, `essence`). 천장이 적용되는 레벨이면 `character_enhance_pity`의 값을 더한다(`min(100, 표 확률 + 천장)`). 보호권 사용 여부 = `failure == Destroy`이고 가방에 `ticket_protect`가 있고 시작 장비가 아니다.
  4. 비용 확인: 골드와 재료(`mat_bone`, `mat_ore`, `mat_essence`)가 가방에 충분한지. 모자라면 `422 ENHANCE_NOT_ENOUGH`(`errors.need`, `errors.have`). **창고의 재료는 쓰지 않는다**(C#도 가방만).
  5. 비용 지급(골드 `enhance_cost`, 재료 `enhance_cost`) 후 서버가 `roll = randomInt(0, 100)`을 굴려 `roll < 성공 확률`이면 성공.
  6. 결과 적용:
     - 성공: 키 +1. 시도한 키의 천장 행 삭제.
     - 실패 + `Keep`: 변화 없음. 실패 + `Drop3`: 레벨 `max(0, 레벨 - 3)`(시도한 키의 천장 +1, 상한 `maxPity`). 실패 + `Destroy`: 보호권이 있으면 보호권 1개를 쓰고 +0 기본 id로 초기화(`protected`), 없으면 장비가 사라진다(`destroyed`). 천장이 적용되는 레벨의 실패는 천장 +1.
     - 대상이 착용 슬롯이면 슬롯 행의 키를 바꾸고, 가방이면 가방 스택에서 옛 키를 1 빼고 새 키를 1 더한다. 원장 `enhance_result`(옛 키 -1, 새 키 +1, 파괴면 -1만).
     - **파괴된 착용 무기**: 시작 무기(`starter.json.gear[직업]`)를 같은 슬롯에 다시 지급한다(원장 `enhance_result`, `ref`는 같은 enhance_log). C# `Equipment.FixSlots`는 전사를 빼는 조건(`cls != Warrior`)이라 전사는 파괴 후 무기 슬롯이 빈다. 의도인지 확인이 필요하다(mapping 문서 6절, 11절).
  7. `enhance_log` INSERT(굴린 값, 확률, 천장 전후, 비용, 보호권 사용). 원장 `ref`가 이 행의 uuid를 가리킨다.
- 응답 `200` `data`:
```
{
  "outcome": "success" | "keep" | "drop3" | "destroyed" | "protected",
  "old_key": "eq_sword_iron+9", "new_key": "eq_sword_iron+10",   // 파괴되면 new_key null
  "old_level": 9, "new_level": 10,
  "roll": 12, "success_percent": 30,
  "cost": { "gold": 120, "bone": 10, "ore": 3, "essence": 0, "ticket_used": false },
  "pity": 0,                                                       // 시도한 키의 천장 값(시도 후)
  "delta": { "gold": ..., "stacks": [...], "worn": [...] }
}
```
- 에러: `422 ENHANCE_INVALID_TARGET`, `422 ENHANCE_MAX_LEVEL`, `422 ENHANCE_NOT_ENOUGH`. (C#의 `NotEnough`/`MaxLevel`/`Invalid` 결과는 아무 일도 없으므로 에러로 낸다. 에러 응답은 `request_log`에 남지 않아 같은 id로 재시도할 수 있다.)
- 천장 키 단위: `item_key`(`id+레벨`)별이라 같은 키의 복사본이 천장을 공유한다. C# `Equipment.pity`와 같다. 이 규칙이 의도인지는 mapping 문서 6절의 확인 사항.
- 인덱스: `character_enhance_pity` PK, `enhance_log_char_idx`.

## 8. 물약·창고·장착 (기존 상태 검증 그대로)

### POST /items/use (엔드포인트 11)
- 요청: `{ request_id: uuid, item_id: string }`. `item_id`는 `potion_hp`, `potion_mp`, `scroll_town`, `carrot`만(`gameconfig.json.usableItems`). 그 밖은 `422 NOT_USABLE`(`ticket_protect`·재료·장비는 직접 쓰지 않는다).
- 처리: 가방 수량 >= 1(`422 NOT_ENOUGH_ITEMS`), 같은 아이템의 직전 사용과 0.5초 이상 간격(`429 ITEM_COOLDOWN`) → 1개 소모(`item_ledger('use_item')`).
- 서버는 HP·MP를 모른다. 회복량·"체력이 가득 차 있다"·마을 이동 가능 여부는 클라이언트가 처리하고 서버는 **소모만** 한다(회복은 어뷰징 이득이 없다, 2단계 mapping 1절). 클라이언트는 가득 찬 상태에서는 요청을 보내지 않는다(C#도 소모하지 않는다).
- 응답: `{ "item_id", "delta" }`

### POST /storage/move (엔드포인트 12)
- 요청: `{ request_id: uuid, moves: [ { item_key: string, to: "storage" | "bag", count: int(1..9999) | "all" } ] (1..60개) }`. 재료 일괄 맡기기·전부 꺼내기는 클라이언트가 여러 항목을 한 요청으로 보낸다.
- 처리: 항목을 순서대로 처리한다. 항목마다 독립이며 하나가 실패해도 나머지는 진행한다(C# `DepositMaterials`가 창고가 가득 차면 건너뛰는 동작과 같다): 출발 위치의 수량 확인(`NOT_ENOUGH_ITEMS`), 창고로 갈 때 도착에 같은 키가 없고 창고 종류 수가 `storage_capacity`이면 `STORAGE_FULL`. 이동은 출발 스택 -n, 도착 스택 +n, 원장 `storage_move`가 (출발 -n, 도착 +n) 두 행. 골드와 착용 장비는 이동 대상이 아니다(`item_key`가 `gold`면 `400`, 착용은 위치가 `worn`이라 대상이 아니다).
- 응답: `{ "results": [ { "item_key", "to", "moved": int, "error": null | "NOT_ENOUGH_ITEMS" | "STORAGE_FULL" } ], "delta": {...} }`. 항목 실패는 `200` 안의 `error`로 알린다.

### POST /equipment/equip, POST /equipment/unequip (엔드포인트 13, 14)
- 장착 요청: `{ request_id: uuid, item_key: string, slot?: int(2..3) }`. `slot`은 반지에만 의미가 있다(반지 슬롯 2, 3을 지정). 해제 요청: `{ request_id: uuid, slot: int(0..5) }`.
- 장착 처리: 가방에 그 키가 있어야 하고(`422 NOT_ENOUGH_ITEMS`), 장비 키여야 하며(`422 NOT_EQUIPMENT`), 직업이 쓸 수 있어야 한다(`shop.json.equipment.classOnly`, `422 CLASS_MISMATCH`). 대상 슬롯은 카테고리로 정한다(무기 0, 목걸이 1, 상의 4, 하의 5). 반지는 `slot`이 없으면 C# `TargetSlotFor`와 같게: 반지1이 비었으면 반지1, 아니면 반지2가 비었을 때 반지2, 둘 다 차 있으면 반지1을 교체. `slot`이 카테고리와 맞지 않으면 `422 SLOT_MISMATCH`. 가방 스택 -1(+ 원장 `equip`), 슬롯의 이전 장비는 가방으로(+1), 새 키가 슬롯에(`worn`, `count=1`).
- 해제 처리: 슬롯이 비어 있으면 `422 SLOT_EMPTY`. 슬롯 행을 지우고 가방 스택 +1(`unequip`).
- 응답: `{ "delta": { "stacks": [...], "worn": [...] } }`
- 자동 장착(`Equipment.AutoEquip`)은 클라이언트가 최적 조합을 계산해 장착 요청 여러 개로 보낸다(서버는 순서대로 검증). 서버에 따로 두지 않는다.
- 인덱스: `character_items_slot_uq`(슬롯 중복 방지), `character_items_stack_uq`(스택 upsert), `character_items_char_loc`.

## 9. 솔로 던전을 3단계에 넣을지 (제안: 넣는다, 요일 던전만)

### 9.1 판단과 이유
추천: **솔로 요일 던전(입장 횟수, 처치 연결, 결과 검증, 카드 보상)을 3단계에 넣는다. 레이드와 파티 분배는 4단계에 둔다.**

1. **처치 보고가 던전에서 끝난다.** 던전 몬스터도 죽을 때마다 `DropLoot`를 굴리고(경험치·드롭), 황금 해골과 보스의 추가 골드, 황금 해골의 타격 골드는 **던전에서만 나온다**(필드 숲에는 일반 해골뿐). 3.1이 이 값들을 다루는데 던전을 빼면 이 경로가 열리지 않는다.
2. **메인 스토리가 던전에 걸려 있다.** `c1_stronger`는 요일 던전 12회 클리어 + 레벨 22를 요구하고, 2장 퀘스트도 요일 던전 목표가 이어진다. 던전이 없으면 온라인 캐릭터가 1장 중반에서 막힌다.
3. **다시 만들지 않는다.** `dungeon_runs`는 "한 캐릭터의 한 번 도전"이라 4단계 파티 던전에서 방 식별자 열 하나만 더하면 멤버별 행으로 쓸 수 있다.
4. **범위가 작다.** 요일 던전 5종은 서로 구조가 같고 방 구성이 이미 `dungeons.json`에 있다.

빼는 쪽의 장점은 "던전은 4단계에서 한 번에 설계한다"는 단순함뿐이고, 그 대가가 위 1~2이다. 던전을 빼기로 정하면 `0003`과 이 절, schema.sql의 3단계-b 구역을 지우고, 온라인 모드의 던전 입구를 "파티 단계에서 개방"으로 막는다.
3단계 던전은 `party_size=1`(AI 용병 없음)로 한다. 용병은 `partyMercs`와 함께 4단계 설계 대상이라 3단계에서는 입장 때 비운다(11절 결정 대기).

### 9.2 GET /dungeons (엔드포인트 15)
- 응답 `200` `data`:
```
{
  "reset": { "daily_start_at": ISO, "next_daily_at": ISO },
  "entries": { "limit": 3, "used": 1, "left": 2 },
  "active_run": { "id": uuid, "dungeon_id": "gold_vein", "difficulty": 0, "started_at": ISO } | null,
  "dungeons": [ { "id": "gold_vein", "open_today": true,
                  "difficulties": [ { "difficulty": 0, "unlocked": true, "cleared": true, "best_rank": 2 } ] } ]
}
```
- `limit`은 `dungeons.json.dailyEntries`(현재 요일 던전 전체 공유). `open_today`는 서버 KST 기준 게임 요일(06:00 경계)이 `openDays`에 있거나 토·일(전부 개방)인지. `unlocked`는 일반은 항상, 그 위 난이도는 바로 아래 난이도 클리어 이력. 레이드는 목록에 넣지 않는다(4단계).
- 조회: `dungeon_runs_char_day`(오늘 입장 수), `dungeon_runs_clears`(해금·최고 랭크).

### 9.3 POST /dungeon-runs (엔드포인트 16)
- 요청: `{ request_id: uuid, dungeon_id: string, difficulty: int(0..3) }`
- 처리(0.2의 잠금 후): 던전 존재·요일 던전(`422 DUNGEON_UNKNOWN`, 레이드면 `422 RAID_NOT_AVAILABLE`) → 오늘 열렸는지(`422 DUNGEON_CLOSED_TODAY`) → 난이도 해금(`422 DIFFICULTY_LOCKED`) → 오늘 입장 수(`reset_day = dailyStartAt`인 행 수, 모든 상태 포함) < `dailyEntries`(`422 NO_ENTRIES_LEFT`) → 진행 중인 판이 있으면 `RUN_STALE_SECONDS`(3600)가 지났을 때만 `abandoned`로 닫고 아니면 `409 RUN_ACTIVE`(`errors.run_id`) → `dungeon_runs` INSERT(`party_size=1`, `reset_day`, `room_index=0`). 입장하면 횟수를 쓴다(실패·방치도 소진, C# `UseEntry`와 같다).
- 응답 `201` `data`: `{ "run": { "id": uuid, "dungeon_id", "difficulty", "party_size": 1, "started_at": ISO }, "entries_left": 2 }`
- 동시성: 캐릭터 행 잠금으로 직렬화되고, `dungeon_runs_one_playing`(진행 중 한 판)가 DB에서 한 번 더 막는다. 시계를 되돌려 횟수를 늘리는 것은 서버 시계로만 판정하므로 불가능하다.

### 9.4 처치 보고의 던전 맥락 (3.1에 `run_id`, `room_index`를 함께 보냄)
- 진행 중인 내 판이어야 한다(`409 RUN_NOT_PLAYING`). `map_id`는 방의 `mapId`와 같아야 한다. 몬스터는 `dungeons.json.rooms[room_index].groups`에 있는 몬스터이고 `(방, 몬스터)`별 받아들인 처치 수가 그 그룹의 `count`를 넘지 못한다(`room_kills`).
- 방 진행: `room_index`는 현재 방이거나 다음 방(+1)만 가능하다. 다음 방은 현재 방의 처치 수가 방 총 마릿수의 `DUNGEON_ROOM_CLEAR_RATIO`(0.8, 보고 누락 여유) 이상일 때만 열리고 열리면 `dungeon_runs.room_index`가 올라간다. 소환체는 보고하지 않으므로 방 총 마릿수는 그룹 `count` 합이다.
- 몬스터 레벨 = `1 + difficulties[난이도].monsterLevel + group.levelOffset`. 경험치는 3.1과 같은 식.
- 1초 창 6, 화력 상한은 입장 후 서버 경과 시간으로 계산(3.2.3).
- 보스 방(`bossRoom`)의 보스 그룹은 클리어 판정에 쓰이므로 반드시 보고되어야 한다.

### 9.5 POST /dungeon-runs/{run_id}/result (엔드포인트 17)
- 요청:
```
{
  request_id: uuid,
  outcome: "cleared" | "failed",
  stats: { elapsed_ms: int, hits_taken: int(0..999), max_combo: int(0..9999), revives_used: int(0..9) }
}
```
  클라이언트는 "무슨 일이 있었나"의 사실만 보낸다. 랭크, 경험치, 카드는 서버가 정한다.
- 처리(0.2의 잠금 후): 진행 중인 내 판이 아니면 `409 RUN_NOT_PLAYING`.
  - `failed`: 판을 `failed`로 닫는다. 이미 받은 처치 경험치·드롭은 유지된다. 보상 없음.
  - `cleared`: 검증한다.
    1. 구조: 보스 방에 도달했고(`room_index == bossRoom`) 보스 그룹이 처치되었고, 보스 앞의 각 방이 `DUNGEON_ROOM_CLEAR_RATIO` 이상 처치되었다.
    2. 시간: `elapsed_ms`는 서버가 잰 입장 후 경과 시간 + 5초 이하여야 하고, 아래 **최소 클리어 시간** 이상이어야 한다. 최소 클리어 시간 = `0.6 x minClearSeconds[난이도]`. `minClearSeconds`(실측 최솟값)를 내보내기 전에는 `0.5 x referenceSeconds[난이도]`를 쓴다. PLAN_SERVER §5는 "60%"를 말하지만 기준 시간(`referenceSeconds`)은 최솟값이 아니다: 일반 던전의 실측 최소가 87초이고 기준 시간은 150초라 기준의 60%(90초)로 자르면 정직한 빠른 클리어가 걸린다.
    3. 화력: 3.2.3 상한.
    4. 값 범위: `revives_used <= 난이도 revives`, `max_combo <= elapsed_ms / 1000 / attackCooldown`.
    - 구조·시간·화력·범위가 어긋나면 `held`로 닫고(`hold_reason` 기록, `anomaly_log(dungeon_result)`) 보상을 주지 않는다. 응답은 `200`에 `result: "held"`(사유를 알리지 않는다). 관리자 도구(7단계)가 검토 후 해제한다.
    5. 통과하면 랭크 점수를 서버가 계산한다: 시간 = 위에서 자른 `elapsed_ms`와 `referenceSeconds`, 처치 = **서버가 받아들인 처치 수**와 서버가 아는 방 총 마릿수(클라이언트 값 아님), 타격 수·최대 콤보·부활은 클라이언트 보고(서버가 검증 못 함). 랭크 구간·점수 상수는 `dungeons.json.ranking`.
    6. 클리어 경험치 = `round(clearXp x 난이도 rewardMul x xpMul x (1 + 랭크 보너스%/100))`를 지급(`xp_ledger('dungeon_clear')`).
    7. 카드 4장을 서버가 굴려 `dungeon_runs.cards`에 저장한다(굴림 규칙은 mapping 문서 4절: 던전 `rewards`, 난이도 보호권 가중, 장비 굴림). 선택 전에는 내용을 클라이언트에 주지 않는다.
- 응답 `200` `data`: `{ "result": "cleared", "rank": "S", "score": { "time": 40, "hits": 24, "kills": 10, "combo": 20, "revive_penalty": 0, "total": 94 }, "granted_xp": 1190, "leveled_up": false, "card_count": 4, "delta": {...} }` / `{ "result": "failed" }` / `{ "result": "held" }`
- **남는 위험**: `hits_taken`, `max_combo`, `revives_used`는 서버가 검증하지 못해 랭크 점수에만 쓰인다. 조작해 얻는 이득은 클리어 경험치의 랭크 보너스(최대 +50%)뿐이고, 카드는 랭크와 무관하다(C# `DealCards`가 랭크를 쓰지 않는다). 4단계에서 방장 보고와 멤버 보고를 대조해 줄인다.
- 에러: `409 RUN_NOT_PLAYING`, 공통 에러. 멱등성은 `request_id`이고, 이미 끝난 판에 새 `request_id`로 보내면 `409 RUN_NOT_PLAYING`.

### 9.6 POST /dungeon-runs/{run_id}/cards/pick (엔드포인트 18)
- 요청: `{ request_id: uuid, index: int(0..3) }`
- 처리: 판이 `cleared`이고 카드가 있어야 하고(`422 NO_CARDS`) `DUNGEON_CARD_TTL_HOURS`(24) 안이어야 한다(`410 CARDS_EXPIRED`). 이미 골랐으면 `409 CARD_ALREADY_PICKED`. 고른 카드를 지급한다(골드면 `gold_ledger('dungeon_card')`, 아이템이면 가방 스택 + `item_ledger('dungeon_card')`, 장비 카드는 +0 키). `card_picked`/`card_picked_at` 기록.
- 응답 `200` `data`: `{ "card": { "item_key": "gold", "count": 450 }, "cards": [ ... 4장 전부 공개 ... ], "delta": {...} }`. 나머지 카드는 동료 AI가 뒤집는 연출용 공개일 뿐 지급되지 않는다(C#은 본인 한 장만 가방에 넣는다).
- 인덱스: `dungeon_runs.uuid` UNIQUE.

## 10. 서버 정책 상수 (환경변수, 코드에 흩어 두지 않는다)

게임 데이터가 아니라 **방어·운영 정책**이다. 시작값이며 운영하며 조정한다.

| 이름 | 시작값 | 의미 |
|---|---|---|
| `DROP_TTL_SECONDS` | 300 | 드롭 만료. 만료 후 줍기는 `expired`. C#은 맵을 나갈 때까지 바닥에 남으므로 전투 중 자리를 비우는 시간을 감안한 5분 |
| `DROP_OPEN_PER_CHARACTER` | 300 | 미수령·유효 드롭 개수 상한. 넘으면 처치 보고는 받아들이되 드롭 굴림만 건너뛴다(`drops_char_open`으로 센다) |
| `KILL_BURST_FIELD` / `KILL_BURST_DUNGEON` | 4 / 6 | 1초 창 처치 상한(3.2.2) |
| `KILL_SUPPLY_MARGIN` | 1.1 | 리스폰 창 여유 배율 |
| `POWER_PASSIVE_PER_LEVEL` | 0.11 | 화력 상한의 레벨당 피해 증가 상한(3.2.3) |
| `POWER_SKILL_FACTOR` / `POWER_AOE_CAP` | 2.0 / 5 | 스킬 몫, 한 번에 맞는 수 상한 |
| `ANOMALY_WINDOW_MINUTES` / `ANOMALY_BLOCK_COUNT` | 10 / 20 | 처치 보고 일시 차단 기준(3.2.4), 차단 시간 10분 |
| `GATHER_GRACE_SECONDS` | 3 | 채집 재생 시간 보고 지연 여유 |
| `ITEM_USE_MIN_GAP_MS` | 500 | 같은 아이템 연속 사용 간격 |
| `RUN_STALE_SECONDS` | 3600 | 진행 중인 던전 판을 방치로 보는 시간(최대 기준 시간 230초의 15배 이상) |
| `DUNGEON_ROOM_CLEAR_RATIO` | 0.8 | 다음 방·클리어 판정에 필요한 처치 비율(보고 누락 여유) |
| `DUNGEON_CARD_TTL_HOURS` | 24 | 카드 선택 가능 시간 |
| 보관 | request_log 7일, kill_log 7일, drops 1일, anomaly_log 30일 | 정리 스크립트(7단계에서 정식화) |

## 11. 사용자가 정해야 할 항목

| 항목 | 선택지 | 추천 |
|---|---|---|
| 솔로 요일 던전을 3단계에 넣을지 | (a) 넣는다(9절, `0003` 적용) (b) 4단계로 미루고 온라인 던전은 막는다 | (a). 1장 중반 이후 스토리와 황금 해골·보스 골드 경로가 던전에 걸려 있다. (b)이면 0003과 9절을 지운다 |
| 3단계 던전에 AI 용병 포함 | (a) 제외(`party_size=1`) (b) 지금 포함 | (a). 용병은 `partyMercs`와 함께 4단계에서 설계한다 |
| 황금 해골 타격당 골드 | (a) 처치 보고에 `hits`를 싣고 서버가 상한으로 자름(3.1.2) (b) 서버가 처치 시 일괄로 정해 타격 연출만 클라이언트에 둔다 | (a). 게임 동작을 바꾸지 않는다. 상한 때문에 조작 이득은 작다 |
| 시작 장비 강화 | (a) 허용 - `enhance.json`에 시작 장비 행을 추가(C# 동작 유지) (b) 온라인에서 막는다 | (a). 시작 장비는 값이 낮고 파괴 시 재지급 규칙(전사 포함 여부 확인 필요)만 정하면 된다 |
| 파괴된 착용 무기의 시작 무기 재지급 | (a) 직업 무관 재지급 (b) C# 그대로(전사 제외) | (a). C# `FixSlots`의 `cls != Warrior`가 의도로 보이지 않는다. 확인 후 C#도 같이 고친다 |
| 드롭 만료 시간 | (a) 5분 (b) 더 길게/짧게 | (a). 값은 환경변수라 CBT에서 조정 |
| 퀘스트 보상의 검증 한계 수용 | (a) 지금 수준 수용(5절 남는 위험) (b) 퀘스트별 최소 소요 시간 같은 추가 방어 | (a). 값이 유한(경험치 약 31,900, 골드 약 88,300)하고 1회성이다. 레이드 퀘스트는 4단계 전까지 막힌다 |
| 채집 노드 상태를 서버가 가짐 | (a) 서버가 재생 시간을 센다(맵 재입장으로 즉시 재생 불가) (b) 재입장 재생을 허용하고 맵별 시간당 총량만 제한 | (a). 정확하고 단순하다. 클라이언트는 맵 입장 때 쿨다운 목록을 읽는다 |
