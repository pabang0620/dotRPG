# 13단계: 레벨 달성 보상

## 1. 규칙

| 항목 | 값 |
|---|---|
| 지급 단위 | 계정. 같은 단계는 계정당 한 번 (부캐로 반복 수령 불가) |
| 조건 | 요청 경로에 지정한 현재 캐릭터의 서버 레벨이 단계 레벨 이상. 같은 계정의 다른 캐릭터 레벨로 해제되지 않음 |
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

### GET /characters/:uuid/level-rewards
본인 소유이며 삭제되지 않은 캐릭터만 조회할 수 있다. 없거나 남의 캐릭터이면 404 `CHARACTER_NOT_FOUND`.

`{ success, data: { character_id: uuid, character_level: n, max_level: n, tiers: [ { level, stars, claimable, claimed } ], pass_owned, pass_price, pass_tiers } }`

`max_level`은 기존 응답 형식을 위한 `character_level`의 별칭이다. 계정 최고 레벨을 의미하지 않는다. 무료·패스 `claimable`은 이 캐릭터의 레벨로 계산하고, `claimed`는 계정 공통 수령 기록을 표시한다. 이미 다른 캐릭터로 받은 단계는 현재 캐릭터가 낮은 레벨이어도 `claimed: true, claimable: false`로 표시한다.

### POST /characters/:uuid/level-rewards/claim
요청 `{ request_id: uuid, level: int }`. 한 트랜잭션:
1. 본인 소유이며 삭제되지 않은 캐릭터 행을 잠근 뒤 계정 행을 잠근다. 같은 request_id 기록이 있으면 같은 캐릭터·단계인지 확인하고 기존 지급 결과를 반환한다(멱등). 다른 캐릭터나 단계에 재사용하면 409 `REQUEST_ID_REUSED`.
2. 단계 표에 없는 level이면 422 `UNKNOWN_TIER`.
3. 잠근 캐릭터의 서버 레벨 < level이면 409 `LEVEL_NOT_REACHED`.
4. 이미 받은 단계면 409 `ALREADY_CLAIMED`.
5. starWallet의 무료 지급 경로로 별조각 지급(원장 reason `level_reward`, ref `level:<n>`), account_level_rewards 기록.
응답 `{ success, data: { character_id, character_level, level, stars, balance, tiers: [...] } }`. 재전송의 지급 수량·잔액은 기존 지급 결과이고 목록과 캐릭터 레벨은 현재 상태다.

### 호환·성장 패스

- 캐릭터가 없는 구 `GET /level-rewards`, `POST /level-rewards/claim`은 400 `CHARACTER_REQUIRED`로 거절한다.
- `POST /level-rewards/pass/buy`는 계정 단위 구매를 유지하며 캐릭터별 목록을 반환하지 않는다.
- `POST /characters/:uuid/level-rewards/pass/claim`도 수령 캐릭터의 서버 레벨을 검사한다. 응답은 `character_id`, `character_level`, `level`, `rewards`, `pass_tiers`, `delta`를 포함한다.
- 무료·패스 보상 모두 계정당 단계별 한 번이며, 기존 수령 기록·재화는 유지한다. 이 변경으로 과거 지급을 취소하거나 회수하지 않는다.

재화 홀드(ECONOMY_HOLD)·계정 정지 등 기존 공통 규칙을 따른다. 유료 별조각 환불·차감 순서에는 영향이 없다(무료분).

## 4. 클라이언트 상태와 검증

- 화면에는 현재 캐릭터의 서버 레벨을 표시한다. 이미 지급한 단계는 `계정 수령`으로 표시한다.
- 로그인·로그아웃·캐릭터 입장·퇴장·접속 교체 때 보상 캐시를 초기화한다. 이전 세션의 늦은 조회·수령 응답은 새 캐릭터에 적용하지 않는다.
- 수령 직전에 시작한 조회가 늦게 완료되어도 이미 받은 보상을 다시 수령 가능 상태로 덮어쓰지 않는다.
- 화면 버튼과 클라이언트 요청 모두 현재 캐릭터의 단계 조건을 검사한다. 실제 지급의 최종 검사는 서버 트랜잭션이 담당한다.
- 클라이언트와 서버를 함께 업데이트해야 한다. 구 서버는 캐릭터별 무료 보상 경로를 제공하지 않으며, 구 클라이언트의 캐릭터 없는 경로는 새 서버가 거절한다.

서버 회귀 검증: `npm test -- --runTestsByPath test/levelRewards.test.ts test/growthPass.test.ts test/sealedRevoke.test.ts` — 31개 통과. 같은 계정의 Lv40/Lv1 조합, 무료 보상 각 단계의 직전/정확한 레벨, 타인·삭제 캐릭터, 구 경로, 요청 재전송, 동시 수령, 패스 아이템 지급을 확인한다. `npm run build`도 통과했다.

클라이언트 회귀 검증: `Tools/tests/level-rewards-client/run.ps1` — 실제 `LevelRewardClient.cs`와 `MiniJson.cs`를 컴파일하여 24개 통과. 캐릭터·계정·오프라인 전환 뒤 지연 응답, 수령 전후 역순 조회, 레벨 미달 플래그, 정확한 레벨 수령과 실패 처리를 검증한다. 네트워크·게임 경계는 테스트 대역이므로 실제 온라인 화면 검증을 대체하지 않는다.
