# 12단계: 부활 코인 (그 자리에서 부활)

## 1. 규칙

| 항목 | 값 |
|---|---|
| 대상 | 캐릭터별 |
| Lv.10 이하 | 그 자리 부활 무료, 횟수 제한 없음(코인 소모 없음) |
| Lv.11 이상 | 부활 1회에 부활 코인 1개 |
| 지급 | 게임 하루(06:00 KST 초기화, 던전 입장 횟수와 같은 기준)마다 1개 |
| 보유 상한 | 5개. 상한이면 그날 지급은 버려진다 |
| 첫 보유 | 새 캐릭터와 기존 캐릭터 모두 1개로 시작 |
| 사용처 | 던전(요일던전)·레이드 사망만. 사냥터(필드) 부활은 레벨과 상관없이 항상 무료(2026-10-07 변경). 던전 난이도별 판당 부활 횟수 제한은 그대로 |

지급은 배치가 아니라 조회·사용 시점에 계산한다(lazy). 마지막 지급 일자 이후 지난 게임 일수만큼 더하되 상한 5.

## 2. 스키마 (마이그레이션 0024_revive_coins.sql)

```sql
ALTER TABLE characters
  ADD COLUMN revive_coins SMALLINT NOT NULL DEFAULT 1 CHECK (revive_coins BETWEEN 0 AND 5),
  ADD COLUMN revive_coin_day DATE NULL;  -- 마지막으로 일일 지급을 반영한 게임 일자, NULL = 아직 없음(오늘 기준으로 시작)

CREATE TABLE revive_log (
  id BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  request_id UUID NOT NULL,
  context TEXT NOT NULL CHECK (context IN ('field', 'dungeon')),
  free BOOLEAN NOT NULL,
  coins_after SMALLINT NOT NULL,
  level INT NOT NULL,
  map_id TEXT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (character_id, request_id)
);
```

## 3. API

### GET /characters/:id/revive
현재 상태(일일 지급 반영 후). 지급 반영은 쓰기를 동반하므로 트랜잭션 + 행 잠금.

```json
{ "success": true, "data": { "coins": 3, "max": 5, "free": false, "free_until_level": 10, "next_grant_at": "2026-10-07T21:00:00Z" } }
```

### POST /characters/:id/revive
요청: `{ "request_id": uuid, "context": "field" | "dungeon", "map_id"?: string }`

처리(한 트랜잭션, characters 행 FOR UPDATE):
1. 같은 request_id가 revive_log에 있으면 그때 응답을 그대로 돌려준다(멱등).
2. 일일 지급 반영.
3. 레벨 ≤ 10이면 free=true, 코인 그대로.
4. 아니면 코인 0이면 409 `NO_REVIVE_COIN`, 있으면 1 감소.
5. revive_log 기록 후 응답.

응답: `{ "success": true, "data": { "free": bool, "coins": n, "max": 5, "next_grant_at": iso } }`

에러: 404 캐릭터 없음/남의 캐릭터, 409 `NO_REVIVE_COIN`, 422 검증 실패. 계정 정지·재화 홀드 등 기존 공통 미들웨어 규칙을 그대로 따른다.

사망 자체는 서버가 판정하지 않는다(전투는 클라이언트). 부활은 코인 소모만 서버가 책임지고, 같은 사망에 두 번 쓰는 것은 클라이언트가 막는다. 남용 지표로 revive_log만 남긴다.

## 4. 클라이언트

- 필드 사망: 게임 오버 창에 "그 자리에서 부활"(Lv.10 이하 무료 / 코인 1개, 보유 N) 버튼을 추가. 성공 시 HP·MP 전부, 무적 3초.
- 던전 사망: 기존 10초 부활 카운트다운에서 수락할 때 같은 API를 부른다. 코인이 없으면 포기만 가능.
- 오프라인(서버 없이 플레이): 세이브에 같은 규칙으로 로컬 카운터를 둔다.
