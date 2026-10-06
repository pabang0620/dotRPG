# 13단계: 레벨 달성 보상

## 1. 규칙

| 항목 | 값 |
|---|---|
| 지급 단위 | 계정. 같은 단계는 계정당 한 번 (부캐로 반복 수령 불가) |
| 조건 | 계정의 캐릭터(삭제되지 않은) 중 최고 레벨이 단계 레벨 이상 |
| 보상 | 무료 별조각 (paid_balance는 늘지 않는다) |
| 단계 | Lv.10: 300 / Lv.15: 300 / Lv.20: 600 / Lv.30: 1,000 / Lv.40(만렙): 3,000 |
| 수령 | 클라이언트 "레벨 보상" 창에서 단계별로 받기 (자동 지급 아님) |

단계와 수량은 `server/data/level_rewards.json`에 둔다(클라이언트도 같은 표를 보여 준다, 서버 값이 정본).

## 2. 스키마 (0025_level_rewards.sql)

```sql
CREATE TABLE account_level_rewards (
  id BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  level INT NOT NULL,
  stars INT NOT NULL CHECK (stars > 0),
  request_id UUID NOT NULL,
  character_id BIGINT NULL REFERENCES characters(id),
  claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, level),
  UNIQUE (account_id, request_id)
);
```

## 3. API

### GET /level-rewards
`{ success, data: { max_level: n, tiers: [ { level, stars, claimable, claimed } ] } }`

### POST /level-rewards/claim
요청 `{ request_id: uuid, level: int }`. 한 트랜잭션:
1. 같은 request_id 기록이 있으면 그때 응답을 그대로(멱등).
2. 단계 표에 없는 level이면 422 `UNKNOWN_TIER`.
3. 계정 행 잠금 후 계정 최고 레벨 < level이면 409 `LEVEL_NOT_REACHED`.
4. 이미 받은 단계면 409 `ALREADY_CLAIMED`.
5. starWallet의 무료 지급 경로로 별조각 지급(원장 reason `level_reward`, ref `level:<n>`), account_level_rewards 기록.
응답 `{ success, data: { level, stars, balance, tiers: [...] } }`

재화 홀드(ECONOMY_HOLD)·계정 정지 등 기존 공통 규칙을 따른다. 유료 별조각 환불·차감 순서에는 영향이 없다(무료분).
