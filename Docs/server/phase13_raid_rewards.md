# 13단계: 레이드 보상 개편 (확정 골드, 카드 4장 모두 수령, 전용 재료, 레이드 상점)

서버와 Unity 클라이언트 구현자가 이 문서만 보고 작업할 수 있게 쓴다. 코드는 없다. 이 문서 확정 뒤 수치의 정본은 C# 상수(`RaidRewards.cs`, `RaidShop.cs`)와 거기서 내보낸 `server/data/dungeons.json`, `server/data/raid_shop.json`이다. 문서의 수치는 구현 때 C#으로 옮기는 값이다.

참고 게임의 레이드 보상 방식(클리어 확정 보상 + 결과 카드 + 전용 재료로 사는 상자)을 이 게임 구조에 맞춘다.

## 0. 한눈에

| 항목 | 지금 | 바뀐 뒤 |
|---|---|---|
| 골드 | 카드 한 장(해골왕 18.9%, 그라흐 18.2%) | 클리어하면 카드와 상관없이 확정 지급 |
| 카드 | 4장 중 1장 고르기 | 4장을 한 장씩 뒤집어 4장 모두 받기 |
| 카드 내용 | 골드, 강화석/마력 정수, 보호권, 레어 이상 장비 | 재료(강화석/마력 정수), 보호권, 전용 재료, 에픽 이상 장비. 골드 없음 |
| 전용 재료 | 없음 | 해골왕의 왕관 조각(`mat_raid_king`), 수호석 파편(`mat_raid_grah`) |
| 상점 | 없음 | 레이드 상점: 에픽 이상 무기 상자, 레전더리 무기 상자 (즉시 열어 내 직업 무기 지급) |
| 요일던전, 소탕, 대박 카드 | 4장 중 1장 | 그대로 (9절에서 확인) |

핵심 수치 요약 (3절에 근거):

| | 해골왕 (Lv.20 단계) | 수호자 그라흐 (Lv.40 단계) |
|---|---|---|
| 보상 횟수 | 개방일(수, 토, 일) 하루 1회, 최대 주 3회 | 일요일, 주 1회 |
| 확정 골드 | 3,000 ~ 4,500 (100 단위) | 12,000 ~ 18,000 (100 단위) |
| 카드 한 장이 에픽 이상 장비일 확률 | 6% (한 번 클리어에 평균 0.24장) | 12% (평균 0.48장) |
| 에픽 이상 장비 카드의 등급 | 에픽 85 / 유니크 14 / 레전더리 1 (%) | 에픽 58 / 유니크 41 / 레전더리 1 (%) |
| 전용 재료 | 한 번 클리어에 평균 10.6개 | 평균 10.24개 |
| 에픽 이상 무기 상자 | 재료 90개 (약 2.8주) | 재료 30개 (약 2.9주) |
| 레전더리 무기 상자 | 재료 320개 (약 10.1주) | 재료 100개 (약 9.8주) |

주 단위는 해골왕 주 3회, 그라흐 주 1회를 전부 한 사람 기준이다.

## 1. 지금 구조 (코드로 확인한 사실)

- 클리어 확정은 `server/src/domains/dungeons/dungeonResult.ts`의 `finalizeCleared` 한 곳이다. 솔로(AI 동반)와 파티 정산(`partyruns/partySettle.ts`의 `settleOne`)과 운영자 보류 해제(`admin/heldruns/heldRunsService.ts`)가 모두 이 함수를 부른다. 그래서 여기만 고치면 세 경로가 같이 바뀐다.
- 카드는 `rollCards`가 4장을 굴려 `dungeon_runs.cards`에 넣는다. 고르는 쪽은 `dungeonService.ts`의 `pickCard`이고 `card_picked` 한 열이 "이미 골랐다"를 막는다. 고르지 않은 판은 `runCardAutoPickTick`이 `DUNGEON_CARD_AUTO_PICK_MINUTES`(10분) 뒤 무작위 한 장을 지급한다.
- 카드 표는 `DungeonDatabase.cs`의 `RaidDef`(해골왕), `GrahDef`(그라흐)의 `rewards` + 난이도의 `ticketWeight`다. 골드는 카드 항목이다(해골왕 800~1500, 그라흐 4000~6000, 둘 다 `rewardMul` 3.0을 곱한다. 곧 2,400~4,500, 12,000~18,000).
- 해골왕은 `raidPeriod`가 "일일"이고 개방 요일이 수, 토, 일이라 한 캐릭터가 주 3번 보상을 받는다. 그라흐는 "주간"이고 일요일만 열려 주 1번이다. (기획서 6.2의 "주 1회 귀속"은 그라흐 기준이다. 해골왕은 코드가 일일 귀속이다.)
- 레이드 장비 카드의 최소 등급은 해골왕 레어, 그라흐 에픽이고 장비 카드의 0.5%가 그 단계 레전더리로 바뀐다(`RAID_LEGENDARY_PERMILLE = 5`). 대박 카드는 레이드 난이도에 `jackpotPerMille`이 없어 나오지 않는다.
- 고대의 핵(`mat_core`: 해골왕 1개, 그라흐 2개)과 봉인 열쇠 조각(해골왕 20~50개 지급, 그라흐 60개 소모)은 카드 밖에서 직접 지급된다. 이 문서에서도 그대로 둔다.
- 레이드 보상은 `raid_claims`의 기본키(캐릭터, 던전, 기간)로 한 번만 나간다. 사람이 2명 미만(연습판), 열쇠 부족, 기여 부족이면 잠겨서 경험치와 카드와 핵과 열쇠가 모두 없다. 새 확정 골드도 같은 잠금을 따른다.

## 2. 새 흐름

```
클리어 보고 (result 또는 settle)
  서버가 한 트랜잭션에서:
    raid_claims 청구 성공 (잠기면 아래 전부 없음)
    경험치, 고대의 핵, 봉인 열쇠 조각 (기존)
    확정 골드 굴림 -> gold_ledger(raid_gold)로 즉시 지급        <- 새로 추가
    카드 4장 굴림 -> dungeon_runs.cards에 숨겨 저장             <- 굴림 방식 변경
    cards_mode = 'take_all', cards_taken = 0
  응답: 랭크, 경험치, gold_gain, core_gain, key_gain, card_count 4

결과 화면
  확정 줄(골드, 핵, 열쇠)이 먼저 올라간다.
  플레이어가 카드를 한 장 뒤집을 때마다: POST .../cards/pick {index}
    서버가 그 카드 한 장만 가방에 지급하고 내용을 돌려준다 (순서 자유)
  4장을 다 뒤집으면 화면이 끝난다.

안 뒤집고 나간 판
  10분 뒤 서버 틱이 남은 카드를 모두 지급한다 (한 장이 아니라 전부).
```

파티 레이드는 멤버마다 자기 `dungeon_runs` 행과 자기 카드 4장이 있다. 동료 AI는 카드를 뒤집지 않는다(전부 플레이어 몫).

## 3. 수치

### 3.1 확정 골드

| 레이드 | 범위 | 단위 | 평균 | 기존 카드 골드의 한 번 클리어 기대값 |
|---|---|---|---|---|
| 해골왕 | 3,000 ~ 4,500 | 100 | 3,750 | 651 |
| 그라흐 | 12,000 ~ 18,000 | 100 | 15,000 | 2,727 |

- 굴림은 서버 난수로 균등 추첨 `goldMin + step * int(0 .. (goldMax-goldMin)/step)`. 난이도 배율(`rewardMul`)은 곱하지 않는다.
- 상한(해골왕 4,500, 그라흐 18,000)은 `income_caps.json`이 이미 레이드 하루/주 덩어리로 가정한 값(`raidMidGoldEq` 4500, `raidFinalGoldEq` 18000)과 같다. 그래서 속도 감시 상한을 바꾸지 않는다(3.6).
- 기대값이 기존의 5~6배인 이유: 기존에는 18%짜리 한 장이었고, 이제 매번 나온다. 골드는 카드 운이 아니라 "클리어 보수"다. 크다고 느끼면 골드 범위만 줄이면 되고 다른 표는 건드리지 않는다.

### 3.2 카드 한 장의 가중치 표 (카드 4장은 각각 독립 추첨)

해골왕 (합계 100):

| 내용 | 수량 | 가중치 | 확률 | 한 번 클리어(4장) 기대 수량 |
|---|---|---|---|---|
| 에픽 이상 장비 | 1 | 6 | 6% | 0.24장 |
| 해골왕의 왕관 조각 | 1 | 13 | 13% | 합쳐서 10.6개 |
| 해골왕의 왕관 조각 | 2 | 30 | 30% | |
| 해골왕의 왕관 조각 | 4 | 32 | 32% | |
| 해골왕의 왕관 조각 | 8 | 8 | 8% | |
| 강화석 | 24 | 4 | 4% | 3.84개 |
| 마력 정수 | 12 | 5 | 5% | 2.4개 |
| 장비 보호권 | 1 | 2 | 2% | 0.08개 |

그라흐 (합계 100):

| 내용 | 수량 | 가중치 | 확률 | 한 번 클리어(4장) 기대 수량 |
|---|---|---|---|---|
| 에픽 이상 장비 | 1 | 12 | 12% | 0.48장 |
| 수호석 파편 | 1 | 12 | 12% | 합쳐서 10.24개 |
| 수호석 파편 | 2 | 30 | 30% | |
| 수호석 파편 | 4 | 28 | 28% | |
| 수호석 파편 | 8 | 9 | 9% | |
| 마력 정수 | 16 | 6 | 6% | 3.84개 |
| 장비 보호권 | 1 | 3 | 3% | 0.12개 |

- 수량은 표의 값 그대로다(난이도 배율 없음). 기존 레이드 카드는 `rewardMul` 3.0을 곱했는데, 표를 읽기 쉽게 하려고 곱한 결과를 표에 적었다(강화석 평균 8 x 3 = 24. 마력 정수는 기존 평균이 해골왕 13.5, 그라흐 24였고 4장을 모두 받는 만큼 한 장 수량을 해골왕 12, 그라흐 16으로 정해 한 번 클리어 기대값이 기존과 비슷하게 맞췄다).
- 카드 한 장에 골드는 없다. 대박 카드(`jackpotPerMille`)는 레이드에 계속 없다.
- 4장 중 전용 재료가 하나도 없을 확률은 해골왕 0.08%, 그라흐 0.19%다.
- 4장 중 에픽 이상 장비가 한 장 이상일 확률은 해골왕 21.9%, 그라흐 40.0%다.

### 3.3 장비 카드 한 장 안의 규칙

1. 부위: 무기, 상의, 하의, 목걸이, 반지 중 균등(각 20%). 상의, 하의는 내 직업 것(전사 갑옷/각반, 마법사 로브/치마), 목걸이와 반지는 공용.
2. 단계: 그 레이드 권장 레벨의 단계. 해골왕 Lv.20 단계(`levelTier` 3), 그라흐 Lv.40 단계(`levelTier` 7). `tierOfLevel(raidNumbers.recommendedLevel)`로 구한다.
3. 등급: 가중치로 에픽/유니크/레전더리 중 하나.

| 레이드 | 에픽 | 유니크 | 레전더리 |
|---|---|---|---|
| 해골왕 | 85 | 14 | 1 |
| 그라흐 | 58 | 41 | 1 |

4. 결과 아이템은 (단계, 부위, 직업, 등급)마다 한 종류뿐이므로 그대로 `+0` 키(`eq_sword_20_e` 형태)가 된다. 기존 `rollGear`의 "몬스터 드롭표 절반 시도" 경로는 레이드 카드에서 쓰지 않는다.
5. 레전더리 공급처: 이 장비 카드(1%)와 레이드 상점 레전더리 상자뿐이다. 요일던전 대박은 계속 유니크만 나온다.

그라흐의 유니크 비율 41%는 기존 값(드롭표 절반 시도 + 풀 가중치로 계산하면 에픽 약 60% / 유니크 약 40%)을 이어받은 것이다. 해골왕 14%도 기존 유니크 기대값(한 번 클리어에 약 0.033장)에 맞췄다.

### 3.4 기존 대비 한 번 클리어 기대값 ("4배가 되지 않는다" 확인)

| 항목 | 해골왕 기존 | 해골왕 새 | 그라흐 기존 | 그라흐 새 |
|---|---|---|---|---|
| 골드 | 651 | 3,750 (확정) | 2,727 | 15,000 (확정) |
| 장비 카드 수 (모든 등급) | 0.42 (레어 이상) | 0.24 (에픽 이상만) | 0.50 (에픽 이상) | 0.48 (에픽 이상) |
| 에픽 이상 장비 | 약 0.10 | 0.24 (x2.4) | 0.50 | 0.48 (x1.0) |
| 그중 유니크 | 약 0.033 | 0.0336 | 약 0.198 | 0.197 |
| 그중 레전더리 | 약 0.0021 | 0.0024 | 약 0.0025 | 0.0048 |
| 강화석 | 3.40 | 3.84 | 0 | 0 |
| 마력 정수 | 2.55 | 2.40 | 3.27 | 3.84 |
| 장비 보호권 | 0.057 | 0.08 | 0.136 | 0.12 |
| 전용 재료 | 0 | 10.6 | 0 | 10.24 |

(기존 값은 "카드 4장 중 1장을 무작위로 고르는 것"이므로 카드 한 장의 기대값과 같다. 해골왕 기존 등급 비율은 `rollGear`의 드롭표 12회 시도와 풀 가중치를 손으로 계산한 근사다.)

재료와 보호권은 기존 기대값을 거의 그대로 유지했고, 늘어난 것은 골드(확정이 됨)와 해골왕의 에픽 이상 장비(레어를 빼고 전부 에픽 이상으로 바꿔서 2.4배)다. 해골왕의 에픽 이상 비율이 부담스러우면 에픽 이상 장비 가중치만 6에서 4로 내리고 그 2를 왕관 조각 x2 칸에 더한다(상자 가격 재계산 필요, 3.5의 식).

### 3.5 주간 획득량과 상자까지 걸리는 기간

한 번 클리어의 기대 전용 재료 m(해골왕 10.6, 그라흐 10.24), 상자 가격 P일 때 상자까지 기대 클리어 횟수는 `P / m`, 주 수는 `(P / m) / 주당 보상 횟수`다. 가격을 바꾸려면 이 식으로 다시 맞춘다.

| | 해골왕 (주 3회) | 해골왕 (주 1회만 한다면) | 그라흐 (주 1회) |
|---|---|---|---|
| 한 주 확정 골드 | 11,250 | 3,750 | 15,000 |
| 한 주 전용 재료 | 31.8 | 10.6 | 10.24 |
| 한 주 에픽 이상 장비 | 0.72장 (1.4주에 1장) | 0.24장 (4.2주에 1장) | 0.48장 (2.1주에 1장) |
| 한 주 유니크 장비 | 0.10 (약 10주에 1장) | 0.034 (약 30주) | 0.197 (약 5.1주) |
| 레전더리가 카드에서 나오기까지 | 약 139주 | 약 416주 | 약 208주 |
| 에픽 이상 무기 상자 (가격) | 90개: 2.8주 | 8.5주 | 30개: 2.9주 |
| 레전더리 무기 상자 (가격) | 320개: 10.1주 | 30.2주 | 100개: 9.8주 |
| 레전더리 상자 편차 (재료 합의 표준편차) | 약 +-0.7주 | 약 +-2.1주 | 약 +-1.4주 |

- 목표(8~12주)를 주 3회 해골왕과 주 1회 그라흐에 맞췄다. 해골왕을 주 1회만 하는 사람은 30주가 걸린다. 그 사람 기준으로 맞추려면 해골왕 가격을 3분의 1(에픽 이상 30, 레전더리 110)로 줄이면 된다. 이것이 열린 결정 1번이다.
- 그라흐를 열려면 열쇠 60개가 필요하고 해골왕이 한 번에 평균 35개를 주므로, 그라흐 하는 사람은 해골왕을 주 2번 이상 한다. 레벨 40 캐릭터도 해골왕을 돌릴 이유가 열쇠와 Lv.20 단계 상자로 남는다.
- 편차는 카드 한 장의 재료 수량 분산(해골왕 4.55, 그라흐 5.01)에서 구한 값이다. 재료 보호장치(천장)는 두지 않는다.

### 3.6 income_caps(속도 감시)에 걸리지 않는지

감시는 `gold_ledger`와 `item_ledger`의 획득 행을 시간 버킷으로 모아 상한의 3배를 넘으면 경제 정지를 건다(`phase9_anti_abuse.md` 12절).

| 지표 | 새로 생기는 값 | 상한(income_caps.json) | 결론 |
|---|---|---|---|
| 골드 환산 (한 번 클리어) | 해골왕 확정 골드 최대 4,500 + 카드 물건 환산 평균 약 340 | `raidMidGoldEq` 4,500 x (24시간 창 2일, 7일 창 3일) | 3배 임계에 크게 못 미친다 |
| | 그라흐 확정 골드 최대 18,000 + 카드 물건 환산 평균 약 590 | `raidFinalGoldEq` 18,000 x 2주 | 못 미친다 |
| 유니크 이상 개수 | 한 번에 최대 4장(확률 극히 낮음) | 하루 4 x 2일 + 시간당 0.12 | 못 미친다 |
| 에픽 이상 개수 | 한 번에 최대 4장 | 시간당 31.3 x 활동 시간 | 못 미친다 |

- 상한 JSON은 바꾸지 않는다. 다만 `Tools/balance/theory_income.py`의 `raid_lumps()`는 지금 `d["rewards"]`의 골드 항목 최댓값에 `rewardMul`을 곱해 레이드 골드 덩어리를 구한다. 카드에서 골드가 사라지면 이 값이 0이 되어 상한이 0으로 내려가 오탐 정지가 난다. 반드시 `raidReward.goldMax`를 쓰도록 고친다(곱하기 없음). 고친 뒤 스크립트를 돌려 `income_caps.json`의 `raidMidGoldEq`가 4500, `raidFinalGoldEq`가 18000으로 그대로인지 확인한다(이 문서를 쓴 환경에서는 스크립트를 실행하지 못해 위 값은 식으로 손계산했다).
- `incomeMeter.ts`의 `GOLD_REASONS`에 `raid_gold`를 넣는다(안 넣으면 확정 골드가 감시 버킷에 안 잡혀 속도 감시 정확도가 떨어진다). `ITEM_REASONS`에는 `raid_shop_*`를 넣지 않는다(재료는 판매가 0이고 상자 결과는 재료 공급이 상한이라 이중 계산이 된다. `promote_result`, `gacha`와 같은 취급).

### 3.7 기존 승급 경로와의 관계 (열린 결정 2번)

`PromoteRules`는 유니크에서 레전더리로 승급할 때 고대의 핵 12개 + 80,000G, 에픽에서 유니크로는 핵 6개 + 30,000G를 받는다(레전더리는 Lv.20, Lv.40 단계만). 핵은 해골왕 1개, 그라흐 2개씩 클리어마다 나온다.

- 해골왕에서: 에픽 무기 상자(2.8주)로 에픽 무기를 얻고, 핵 18개(주 3개씩 6주)와 골드 11만으로 승급하면 약 6주 만에 레전더리 무기가 된다. 레전더리 무기 상자(10.1주)보다 빠르다.
- 그라흐에서: 핵 18개가 9주라 상자(9.8주)와 비슷하다.

승급은 골드 11만이 필요하고 방어구와 장신구도 되는 별도 길이라 두는 것이 기본안이다. 해골왕 상자를 승급 경로보다 실제로 의미 있게 하려면 `CostInto`(유니크 6핵, 레전더리 12핵)를 올리는 방법이 있다. 이 문서는 승급 비용을 건드리지 않는다.

## 4. 전용 재료 아이템

이름은 둘로 나눈다. 이유: 해골왕은 주 3회, 그라흐는 주 1회라 하나로 합치면 Lv.20 레이드를 3배 빨리 돌려 Lv.40 레전더리를 얻게 된다. 재료가 곧 단계를 정하게 하면 상점에서 단계를 고르는 규칙도 필요 없다.

| id | 이름 | 설명 (게임 안 문구) | 종류 | 아이콘 키 (기존 재사용) | 등급 프레임 |
|---|---|---|---|---|---|
| `mat_raid_king` | 해골왕의 왕관 조각 | 해골왕이 쓰고 있던 왕관의 파편. 해골왕 레이드 보상 카드로만 얻고, 레이드 상점에서 해골왕 무기 상자로 바꾼다. | `ConsumableKind.Key` | `maticon_bone` | `ItemRarity.Unique` |
| `mat_raid_grah` | 수호석 파편 | 수호자 그라흐가 지키던 수호석의 파편. 그라흐 레이드 보상 카드로만 얻고, 레이드 상점에서 그라흐 무기 상자로 바꾼다. | `ConsumableKind.Key` | `maticon_ore` | `ItemRarity.Legendary` |

- 아이콘은 새 그림 없이 기존 `maticon_bone`, `maticon_ore`를 쓴다. 뼈 조각/강화석과 헷갈리지 않도록 등급 프레임 색(유니크 노랑, 레전더리 주황)과 이름으로 구분한다. 나중에 전용 그림을 그리면 `iconKey`만 바꾼다.
- 귀속: 캐릭터 귀속. `AuctionRules.BindFloor`에 두 id를 추가해 `CharacterBound`로 한다(고대의 핵과 같다). 그러면 `items.json`의 `bind`가 `character`로 내보내진다.
- 거래, 경매, 우편, 상점 판매: 불가. `bind=character`라 경매 등록과 우편 첨부가 막히고, `ItemPrices.SellPrice`가 0이라 상점에서 팔 수 없다. 창고 보관은 가능(고대의 핵과 같다).
- 가방 표시: "기타" 탭(`Tickets`에 `Key` 종류가 들어간다). 사용 불가(`usable=false`).
- 출처: 해당 레이드의 카드 추첨뿐이다. 요일던전, 소탕, 필드 드롭, 캐시샵, 우편에 없다.
- 쓰임: 레이드 상점의 상품 가격으로만 쓴다.

내보내기 `items.json`에 들어가는 행 형태:
`{"id":"mat_raid_king","kind":"consumable","stackable":true,"name":"해골왕의 왕관 조각","bind":"character","usable":false}` (수호석 파편도 같은 형태).

## 5. 레이드 상점

### 5.1 상품표

| 상품 id | 이름 | 단계 | 가격 | 결과 |
|---|---|---|---|---|
| `king_epic_weapon` | 해골왕 에픽+ 무기 상자 | Lv.20 단계 (해골왕) | 해골왕의 왕관 조각 90 | 내 직업 무기: 에픽 88% / 유니크 12% |
| `king_legend_weapon` | 해골왕 레전더리 무기 상자 | Lv.20 단계 | 해골왕의 왕관 조각 320 | 내 직업 무기: 레전더리 100% (전사 용골 대검, 마법사 별의 지팡이) |
| `grah_epic_weapon` | 그라흐 에픽+ 무기 상자 | Lv.40 단계 (그라흐) | 수호석 파편 30 | 내 직업 무기: 에픽 88% / 유니크 12% |
| `grah_legend_weapon` | 그라흐 레전더리 무기 상자 | Lv.40 단계 | 수호석 파편 100 | 내 직업 무기: 레전더리 100% (전사 창세의 별검, 마법사 창세의 별 지팡이) |

- 단계 선택 규칙: 사용자는 단계를 고르지 않는다. 상품이 레이드에 묶여 있고 가격이 그 레이드의 전용 재료로만 매겨져서 해골왕 상품 = Lv.20 단계, 그라흐 상품 = Lv.40 단계다. 단계 값은 서버가 레이드의 `recommendedLevel`로 계산한다(상품 표에 단계를 따로 저장하지 않는다).
- 직업 무기만: 서버가 `characters.class`로 정한다. 요청에 직업이나 부위가 없다.
- 에픽+ 상자에는 레전더리가 없다(레전더리는 레전더리 상자 전용). 에픽 88 / 유니크 12는 서버 난수.
- 레전더리 상자는 결과가 고정(그 단계 내 직업 무기 1종)이라 "확률 상자"가 아니라 "정해진 교환"이다. 이름과 연출은 상자로 유지한다.
- 구매 횟수 제한 없음. 재료가 곧 제한이다. 중복 보유도 허용한다.
- 레벨 제한 없음. 착용 레벨(`reqLevel`)은 장비 규칙이 따로 막는다. 해당 재료는 그 레이드를 클리어해야 생기므로 레벨이 낮은 캐릭터가 높은 단계 상자를 사는 일은 생기지 않는다.
- 결과 장비의 귀속: 캐릭터 귀속을 권한다(열린 결정 3번). 재료를 거래 불가로 막았는데 결과 장비가 경매로 팔리면 재료를 간접 거래하는 셈이라서다. 서버는 `addItem(..., 'character')`으로 넣는다. 카드에서 나온 장비는 지금처럼 `dungeon_card` 규칙(거래 가능)을 유지한다.
- 확률 공시: 응답과 화면에 `outcomes`(등급별 확률)를 보여 준다. 표 버전 `rates_version`은 `raidshop-1`이고 구매 기록에 남긴다.

### 5.2 상자를 가방에 넣는 방식 대신 "산 즉시 열기"를 고른 이유

| 비교 | 산 즉시 열기 (채택) | 가방에 상자를 넣고 나중에 열기 |
|---|---|---|
| 기존 구조 | `promote`처럼 요청 한 번에 재료 차감 + 결과 지급 | 상자 아이템 2종 신설, `items.json`에 `use` 종류 추가, `items/open` 분기, 상자 id마다 단계/직업 규칙 필요 |
| 가방 칸 | 상자가 칸을 쓰지 않는다 (가방은 24칸이고 페이지가 없다) | 상자가 칸을 쓴다 |
| 직업 | 구매 순간 캐릭터 직업으로 확정 | 계정 귀속 상자면 다른 직업이 열 수 있어 막는 규칙이 더 필요 |
| 원장 | `raid_shop_cost` + `raid_shop_result` 한 요청 | 구매, 열기 두 요청, 중간 상태 |
| 거래/우편 | 상자가 없어서 새 경로가 없다 | 상자의 귀속과 거래 규칙을 정해야 한다 |

연출은 구매 뒤 결과 카드 한 장이 뒤집히는 팝업(7.5)으로 "상자를 여는 느낌"을 준다.

### 5.3 진입 위치 (UI 변경이 가장 작은 곳)

던전 선택 창(`DungeonSelectScreen`)의 "레이드" 탭에 `레이드 상점` 버튼을 둔다. 이 창은 요일던전 탭에서 `소탕` 버튼 자리(우하단, 입장 버튼 왼쪽)에 소탕 패널을 띄우는 구조이고, 소탕은 레이드에서 쓰지 않으므로 레이드 탭에서는 같은 자리를 상점 버튼이 쓴다. 상점 패널은 소탕 패널과 같은 방식(상세 영역을 덮는 패널)이다. 새 NPC, 새 사이드 메뉴, 새 맵 배치가 필요 없다. 레이드 보상 카드를 받는 곳과 같은 창이라서 재료를 쓰러 가는 동선도 짧다.

### 5.4 `server/data/raid_shop.json` 형태 (Unity가 내보낸다)

```
{ "schema": 1, "ratesVersion": "raidshop-1",
  "products": [
    { "id": "king_epic_weapon", "raidId": "raid_skeleton_king", "materialItem": "mat_raid_king", "price": 90,
      "category": "Weapon", "rarityWeights": { "Epic": 88, "Unique": 12 } },
    { "id": "king_legend_weapon", ..., "price": 320, "rarityWeights": { "Legendary": 100 } },
    { "id": "grah_epic_weapon", "raidId": "raid_grah", "materialItem": "mat_raid_grah", "price": 30, ... },
    { "id": "grah_legend_weapon", ..., "price": 100, ... } ] }
```

서버 로더는 `materialItem`이 `items.json`에 있고, `raidId`가 레이드이며, 각 직업마다 (단계, 부위, 등급)에 맞는 장비가 정확히 하나씩 있는지 기동 때 검사한다(`gamedata` 계열의 다른 검사처럼 어긋나면 기동 실패).

## 6. 서버 변경

### 6.1 데이터 파일과 로더

- `dungeons.json`: 레이드 던전 객체에 `raidReward`를 추가한다.
  ```
  "raidReward": { "goldMin": 3000, "goldMax": 4500, "goldStep": 100, "materialItem": "mat_raid_king",
                  "gearCategories": ["Weapon","Top","Bottom","Necklace","Ring"],
                  "gearRarityWeights": { "Epic": 85, "Unique": 14, "Legendary": 1 },
                  "cards": [ { "itemId": "gear", "min": 1, "max": 1, "weight": 6 },
                             { "itemId": "mat_raid_king", "min": 1, "max": 1, "weight": 13 }, ... ] }
  ```
  `rewards`(기존 항목)는 레이드에서 비운다. 대신 `raidReward.cards`가 카드 표다. 레이드 `raidNumbers.ticketWeight`는 0으로(보호권이 `cards`에 있다), `minGearRarity`는 Epic으로 둔다.
- `server/src/gamedata/economyData.ts`: `raidReward` zod 검증(레이드면 필수). 가중치 합 > 0, `materialItem`과 카드의 모든 `itemId`가 `items.json`에 있어야 한다(`gear` 제외).
- 신규 `server/src/gamedata/raidShop.ts`: `raid_shop.json` 로더. `data_version.json`의 `files`에 `raid_shop.json`이 들어가야 한다(Unity 내보내기가 `files` 사전에 넣으면 해시에 자동 포함).

### 6.2 마이그레이션 `0028_raid_rewards.sql` (최신 0027 다음)

```sql
-- ============ UP ============
-- 1. 던전 판: 카드를 받는 방식, 받은 카드 집합, 확정 골드
ALTER TABLE dungeon_runs
  ADD COLUMN cards_mode     TEXT NOT NULL DEFAULT 'pick_one' CHECK (cards_mode IN ('pick_one', 'take_all')),
  ADD COLUMN cards_taken    SMALLINT NOT NULL DEFAULT 0 CHECK (cards_taken BETWEEN 0 AND 15),
  ADD COLUMN cards_taken_at TIMESTAMPTZ,
  ADD COLUMN raid_gold      INT NOT NULL DEFAULT 0 CHECK (raid_gold >= 0);
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_taken_chk CHECK ((cards_taken = 0) = (cards_taken_at IS NULL));
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_mode_chk
  CHECK (cards_mode = 'pick_one' OR (card_picked IS NULL AND card_picked_at IS NULL));
COMMENT ON COLUMN dungeon_runs.cards_mode IS 'pick_one 4장 중 1장(요일던전, 이 마이그레이션 전의 레이드 판) / take_all 4장 모두 받기(레이드). 클리어 확정 때 정해진다';
COMMENT ON COLUMN dungeon_runs.cards_taken IS 'take_all에서 이미 가방에 준 카드의 비트집합(비트 i = 카드 i). 15 = 4장 모두. 카드 수가 4가 아니게 바뀌면 이 열과 부분 인덱스를 함께 바꾼다';
COMMENT ON COLUMN dungeon_runs.cards_taken_at IS '마지막으로 카드를 준 시각';
COMMENT ON COLUMN dungeon_runs.raid_gold IS '클리어 확정 골드(레이드). gold_ledger raid_gold 행과 같은 값. 잠긴 판과 요일던전은 0';

-- 대기 카드 조회: 서버 틱(주기적으로 전체 스캔)과 접속 때 목록. 끝난 지 오래된 것부터 읽는다
CREATE INDEX dungeon_runs_cards_pending ON dungeon_runs (ended_at)
  WHERE state = 'cleared' AND cards IS NOT NULL
    AND ((cards_mode = 'pick_one' AND card_picked IS NULL) OR (cards_mode = 'take_all' AND cards_taken <> 15));

-- 2. 레이드 상점 구매 기록 (추가만, 확률 공시와 분쟁 조사용)
CREATE TABLE raid_shop_purchases (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  request_id    UUID NOT NULL,
  product_id    TEXT NOT NULL,
  material_key  TEXT NOT NULL,
  material_cost INT NOT NULL CHECK (material_cost > 0),
  result_key    TEXT NOT NULL,
  result_rarity TEXT NOT NULL CHECK (result_rarity IN ('Epic', 'Unique', 'Legendary')),
  rates_version TEXT NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id)
);
CREATE INDEX raid_shop_purchases_char_idx ON raid_shop_purchases (character_id, id DESC);
COMMENT ON TABLE raid_shop_purchases IS '레이드 상점 구매 기록. 재료 차감과 장비 지급은 item_ledger(raid_shop_cost, raid_shop_result)가 정본이고 이 표는 어떤 상품에서 어떤 확률표로 뭐가 나왔는지 남긴다';

-- 3. 원장 사유 확장
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold'));
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward',
                    'raid_shop_cost', 'raid_shop_result'));
COMMENT ON COLUMN gold_ledger.reason IS '... raid_gold 레이드 클리어 확정 골드(ref = 던전 판 uuid) ... 기존 설명은 0002, 0007, 0021 참고';
COMMENT ON COLUMN item_ledger.reason IS '... dungeon_card 카드 지급(레이드는 ref = 판 uuid:카드번호) / raid_shop_cost 레이드 상점 재료 차감 / raid_shop_result 레이드 상점 장비 지급 ...';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0026 DOWN과 같은 방식).
-- gold_ledger_append_only / item_ledger_append_only 를 끄고 raid_gold, raid_shop_cost, raid_shop_result 행을 지우고 켠 뒤
-- CHECK 목록을 0027 시점으로 되돌린다. 그 다음 DROP TABLE raid_shop_purchases, DROP INDEX dungeon_runs_cards_pending,
-- 제약 두 개와 열 네 개를 지운다. (take_all 판에 이미 지급한 카드는 되돌리지 않는다)
```

- 적용 직전에 `gold_ledger`, `item_ledger`의 현재 CHECK 목록을 확인해 위 목록과 다르면 합쳐서 쓴다(0026이 마지막으로 바꾼 상태를 기준으로 썼다).
- 기존에 이미 클리어되어 카드를 아직 안 고른 레이드 판은 기본값 `pick_one`이라 예전 방식(1장 고르기, 골드 카드 포함)으로 끝낸다. 새 클라이언트는 `card_mode`를 보고 분기한다(7.3).
- 인덱스 이유: `runCardAutoPickTick`이 주기적으로 "클리어, 카드 있음, 안 받은 카드 있음, 끝난 지 N분 지남"을 전체에서 찾는다. 지금은 `dungeon_runs` 전체를 훑는다. 대기 상태는 판의 극히 일부라 부분 인덱스가 작다. 접속 때 `unpickedRuns`(캐릭터별 최근 24시간)도 같은 조건을 쓴다.
- `raid_shop_purchases_char_idx` 이유: 운영자 캐릭터 상세에서 "이 캐릭터가 최근 산 상자" 조회. (캐릭터, 최신순).
- 소프트 삭제 열은 두지 않는다. 원장과 같은 추가 전용 기록이다(`sealed_pulls`와 같다). 개인정보 삭제는 기존 탈퇴 익명화 규칙을 따른다.
- 같은 변경을 `server/schema.sql`에도 합친다(머리말 "기능별 테이블 사용처" 표에 "레이드 클리어, 카드 뒤집기", "레이드 상점 구매" 두 줄 추가).

### 6.3 클리어 확정 (`finalizeCleared`)

잠기지 않은 레이드(`dungeon.isRaid && !locked`)에서 기존 `rollCards` 호출을 아래로 바꾼다. 요일던전은 지금 그대로다.

1. 난수 순서: 확정 골드 1회 -> 카드 4장(장비 카드는 부위 -> 등급 순). 시험이 고정 난수열로 검증할 수 있게 이 순서를 지킨다.
2. 확정 골드: `ctx.changeGold(gold, 'raid_gold', run.uuid)`. 골드는 `raid_claims` 청구가 성공한 같은 트랜잭션 안에서 나간다. 카드를 안 뒤집어도 이미 들어와 있다.
3. 카드: 신규 순수 함수 `rollRaidCards(eco, dungeon, cls, rng)`(`dungeonRules.ts`). 지금 `rollCards`/`rollOneCard`는 요일던전과 소탕 전용으로 남기고, `rollOneCard` 안의 레이드 레전더리 분기와 `RAID_LEGENDARY_PERMILLE`은 죽은 코드가 되므로 지운다.
4. `closeRun`에 `cardsMode: 'take_all'`, `raidGold`를 넘긴다. `cards_taken`은 0.
5. 핵과 열쇠는 기존 그대로 카드 밖에서 지급한다.
6. 응답 `data.raid`: `{ reward_locked, lock_reason?, key_gain?, key_cost?, core_gain?, gold_gain, card_mode: 'take_all' }`. `card_count`는 4.

잠긴 판(ALREADY_CLAIMED, LOW_CONTRIBUTION, TOO_FEW_HUMANS, KEYS_MISSING)과 연습판은 골드도 카드도 없다. "골드는 무조건"은 카드 운과 상관없이라는 뜻이고, 그 주(일)의 보상 청구가 성립해야 나간다.

### 6.4 API

공통: 응답 `{ success, message, data, meta? }`, 실패 `{ success: false, message, errors? }`. 인증은 기존 캐릭터 라우트와 같다(액세스 토큰, 내 계정의 삭제되지 않은 캐릭터 `:uuid`). 클라이언트가 보내는 값은 행동과 대상(카드 번호, 상품 id)과 `request_id`뿐이다. 골드, 수량, 확률, 가격, 등급을 받는 요청은 없다.

#### (변경) POST `/characters/:uuid/dungeon-runs/:run_id/result`, POST `.../settle`

요청은 그대로(`resultBody`, `settleBody`). 응답 `data`만 바뀐다.

```
data (cleared, 레이드): {
  result: 'cleared', rank, score, granted_xp, leveled_up, card_count: 4,
  raid: { reward_locked: boolean, lock_reason?: string, gold_gain?: int, key_gain?: int, key_cost?: int,
          core_gain?: int, card_mode?: 'take_all' },
  delta
}
```

- 파티 정산이 이미 끝난 뒤 다시 부를 때(`partySettle.ts`의 `storedResult`)도 같은 `raid`를 내려 주려고 `dungeon_runs.raid_gold`와 `cards_mode`를 읽어 `gold_gain`, `card_mode`를 채운다. (지금 `storedResult`는 `key_gain`, `core_gain`을 안 채운다. 이번에 새로 필요한 것은 `gold_gain`, `card_mode`만이다.)
- 에러와 멱등성, 속도 제한은 기존과 같다.

#### (변경) POST `/characters/:uuid/dungeon-runs/:run_id/cards/pick`

경로와 요청은 그대로다. 요일던전(`pick_one`)은 응답과 동작이 지금과 같다. 레이드(`take_all`)는 "이 카드를 뒤집어 받는다"로 바뀐다.

요청(zod, 기존 그대로):
```
z.strictObject({ request_id: requestId, index: z.number().int().min(0).max(3) })
```

처리(`take_all`):
1. 판 조회(`RUN_NOT_FOUND`), 상태 `cleared`이고 카드가 있어야 한다(`NO_CARDS`), 유효 기간 안이어야 한다(`CARDS_EXPIRED`) - 기존과 같다.
2. `index`가 카드 수 안이어야 한다(아니면 `NO_CARDS`).
3. `cards_taken`에 그 비트가 이미 있으면 `409 CARD_ALREADY_FLIPPED`.
4. 그 카드 한 장만 가방에 지급: `ctx.addItem('bag', item_key, count, 'dungeon_card', '<run uuid>:<index>')`.
5. `UPDATE dungeon_runs SET cards_taken = cards_taken | $bit, cards_taken_at = $now WHERE id = $1 AND (cards_taken & $bit) = 0`. 영향 행이 0이면 방어적으로 `CARD_ALREADY_FLIPPED`.

응답 `data` (200):
```
{ index: 2,
  card: { item_key, count },
  taken: [0, 2],                      // 지금까지 받은 카드 번호(오름차순)
  remaining: 2,
  cards: [ {item_key,count}|null, null, {item_key,count}, null ],   // 번호 위치에 맞춘 배열, 아직 안 받은 카드는 null
  delta }
```

에러:

| 상태 | 코드 | 언제 |
|---|---|---|
| 400 | `VALIDATION_ERROR` | `index`가 0~3 밖이거나 필드가 모자람/남음 |
| 404 | `RUN_NOT_FOUND` | 내 판이 아니거나 없음 |
| 422 | `NO_CARDS` | 클리어한 판이 아니거나 카드 없음/보류 중 |
| 409 | `CARD_ALREADY_FLIPPED` | 이미 받은 카드. `errors`/`extra`에 `{ index, card, taken }`을 실어 클라이언트가 상태를 맞추게 한다 |
| 410 | `CARDS_EXPIRED` | 유효 기간(`DUNGEON_CARD_TTL_HOURS`)이 지남 |
| 403 | `ECONOMY_HOLD` | 기존 경제 정지 규칙이 이 경로에 적용돼 있다면 그대로 |
| 429 | `RATE_LIMITED` | 속도 제한 |

- 멱등성: `runEconomy`의 `request_id`. 같은 `request_id` 재전송은 저장된 같은 응답을 돌려준다(원장 행이 더 생기지 않는다). 새 `request_id`로 이미 받은 번호를 다시 보내면 위의 409. 순서는 상관없다(0, 3, 1, 2 어떤 순서든 된다).
- 동시 요청: `runEconomy`가 캐릭터 행을 `FOR UPDATE`로 잠가 직렬화한다. 같은 카드를 서로 다른 `request_id`로 동시에 보내면 하나만 200, 나머지는 409다.
- 속도 제한: 기존 `dungeon-pick`(`slowPerSec`)을 그대로 쓴다.
- 요일던전(`pick_one`) 판에서는 지금 동작이 같다: 이미 골랐으면 `409 CARD_ALREADY_PICKED`, 응답에 4장 전부.
- 새 클라이언트가 예전 방식으로 남은 레이드 판(`pick_one`)에 보내도 위 요일던전 동작이 적용된다.

#### (변경) 자동 지급 틱 `runCardAutoPickTick`

- 대상 조건: 클리어, 카드 있음, `ended_at < now - DUNGEON_CARD_AUTO_PICK_MINUTES`, 그리고 (`pick_one`이면서 `card_picked IS NULL`) 또는 (`take_all`이면서 `cards_taken <> 15`). 한 번에 최대 50판.
- `pick_one`: 지금처럼 무작위 한 장.
- `take_all`: 아직 안 받은 카드를 번호 순서로 전부 지급한다. 한 판당 하나의 `runEconomy` 요청(요청마다 새 `request_id`)이며 카드마다 원장 행이 생긴다(ref `<run uuid>:<index>`). 도중 실패하면 그 트랜잭션 전체가 롤백되어 다음 틱이 다시 처음부터 한다.
- 틱이 먼저 지급한 뒤 늦게 도착한 사용자의 뒤집기는 `409 CARD_ALREADY_FLIPPED`(카드 내용 포함)로 응답하므로 화면은 문제없이 그 카드를 보여 줄 수 있다.

#### (변경) GET `/characters/:uuid/dungeon-runs/:run_id`

클리어한 판의 `run`에 다음을 더한다.
```
card_mode: 'pick_one' | 'take_all',
raid_gold: int,
taken: [ { index, item_key, count } ]      // take_all에서 이미 받은 카드의 내용 (안 받은 카드 내용은 주지 않는다)
```
기존 `card_picked`는 `pick_one`일 때만 의미가 있다(`take_all`이면 null).

#### (변경) GET `/characters/:uuid/dungeons`

`unpicked_runs[]` 항목에 `card_mode`와 `remaining`(아직 안 받은 카드 수)을 더한다. 대기 조건은 6.2의 부분 인덱스 조건과 같다. 보류가 풀려 늦게 확정된 레이드 판도 여기서 4장이 모두 대기로 보인다.

#### (신규) GET `/characters/:uuid/raid-shop`

- 인증: 위와 같음. 요청 본문 없음.
- 응답 `data`:
```
{ rates_version: 'raidshop-1',
  products: [ {
    id, name, raid_id, tier_level: 20 | 40,
    material: { item_key, name, price, have },          // have = 가방의 재료 수 (서버가 센 값)
    outcomes: [ { rarity: 'Epic', percent: 88, item_key: 'eq_sword_20_e' }, { rarity: 'Unique', percent: 12, item_key: '...' } ],
    affordable: boolean } ] }
```
`outcomes[].item_key`는 caller 직업의 장비다. 에러: `CHARACTER_NOT_FOUND`(404), `RAID_SHOP_UNAVAILABLE`(503, 데이터 파일이 없을 때).
- 속도 제한: `slowPerSec`(캐릭터 기준).

#### (신규) POST `/characters/:uuid/raid-shop/buy`

요청(zod):
```
z.strictObject({
  request_id: requestId,
  product_id: z.string().min(1).max(40).regex(/^[a-z][a-z0-9_]*$/),
})
```
수량, 가격, 직업, 등급은 받지 않는다(받으면 `strictObject`가 400).

처리(`runEconomy` 한 트랜잭션, 캐릭터 행 `FOR UPDATE`):
1. `assertNoHold`(경제 정지 중이면 403 `ECONOMY_HOLD`).
2. 상품 조회. 없으면 422 `UNKNOWN_PRODUCT`.
3. 가방의 재료 수 < 가격이면 422 `NOT_ENOUGH_MATERIAL`, `extra: { need, have }`.
4. 재료 차감: `ctx.removeItem('bag', material, price, 'raid_shop_cost', logId)`. 반환이 null이면 같은 422.
5. 등급 굴림(서버 난수, 상품의 `rarityWeights`), 결과 장비 = (캐릭터 직업, 레이드 단계, 부위 `Weapon`, 등급)의 `+0` 키.
6. 장비 지급: `ctx.addItem('bag', key, 1, 'raid_shop_result', logId, 'character')`.
7. `raid_shop_purchases` 한 줄 기록(재료, 가격, 결과, 확률표 버전).

응답 200:
```
{ product_id, item_key, rarity: 'Epic'|'Unique'|'Legendary',
  cost: { item_key, count }, material_left: int, delta }
```

에러:

| 상태 | 코드 | 언제 |
|---|---|---|
| 400 | `VALIDATION_ERROR` | 필드 형식 |
| 401 | `UNAUTHORIZED` | 토큰 없음/만료 |
| 403 | `ECONOMY_HOLD` | 경제 정지 |
| 404 | `CHARACTER_NOT_FOUND` | 내 캐릭터가 아님 |
| 422 | `UNKNOWN_PRODUCT` | 상품 id 없음 |
| 422 | `NOT_ENOUGH_MATERIAL` | 재료 부족 (`need`, `have`) |
| 429 | `RATE_LIMITED` | 속도 제한 |
| 503 | `RAID_SHOP_UNAVAILABLE` | 상점 데이터 없음 |

멱등성: `request_id`. 같은 `request_id` 재전송은 저장된 같은 응답(같은 장비)을 돌려주고 재료가 두 번 빠지지 않는다. `raid_shop_purchases (account_id, request_id)` 유일 제약이 한 번 더 막는다. 같은 `request_id`에 다른 `product_id`를 보내면 기존 `runEconomy`의 재사용 오류를 따른다.
속도 제한: `shopPerSec`(캐릭터 기준, 잡화점 구매와 같다).

### 6.5 변경 요약 (파일)

| 파일 | 변경 |
|---|---|
| `server/src/domains/dungeons/dungeonRules.ts` | `rollRaidCards`, `rollRaidGold`, `rollRaidGear` 추가. `rollOneCard`의 레이드 분기와 `RAID_LEGENDARY_PERMILLE` 삭제 |
| `server/src/domains/dungeons/dungeonResult.ts` | `finalizeCleared` 레이드 분기(6.3) |
| `server/src/domains/dungeons/dungeonRepository.ts` | `RunRow`에 `cards_mode`, `cards_taken`, `raid_gold`. `COLS`, `closeRun`(cardsMode, raidGold), `takeCard(runId, bit, at)` 신규, `unpickedRuns` 조건 교체 |
| `server/src/domains/dungeons/dungeonService.ts` | `pickCard` 분기, `runCardAutoPickTick` 분기, `getRun`, `listDungeons` |
| `server/src/domains/partyruns/partySettle.ts` | `storedResult`에 `gold_gain`, `card_mode` |
| `server/src/domains/antiabuse/incomeMeter.ts` | `GOLD_REASONS`에 `raid_gold` |
| `server/src/gamedata/economyData.ts`, 신규 `raidShop.ts` | 6.1 |
| 신규 `server/src/domains/raidshop/` (routes, controller, service, validation) | 6.4 신규 두 엔드포인트, 앱 라우터 등록 |
| `server/migrations/0028_raid_rewards.sql`, `server/schema.sql` | 6.2 |
| `Tools/balance/theory_income.py` | `raid_lumps()`가 `raidReward.goldMax` 사용 (3.6) |

## 7. 클라이언트 변경

### 7.1 데이터와 규칙 (오프라인과 온라인이 같은 표를 쓴다)

파일 길이 500줄 제한 때문에 `DungeonDatabase.cs`(478줄)에는 최소만 더하고 새 파일을 만든다.

| 파일 | 변경 |
|---|---|
| 신규 `Assets/Scripts/Runtime/Dungeon/RaidRewards.cs` | `RaidRewardDef`(goldMin, goldMax, goldStep, materialItem, gearCategories, gearRarityWeights, cards[]) 와 해골왕/그라흐 표(3절). `RaidShop`(상품 4개: id, raidId, materialItem, price, rarityWeights). 상수 `RaidMatKing = "mat_raid_king"`, `RaidMatGrah = "mat_raid_grah"` |
| `Dungeon/DungeonDatabase.cs` | `DungeonDef.raidReward` 필드. `RaidDef`, `GrahDef`의 `rewards`에서 골드, 강화석, 마력 정수 항목을 지우고 `raidReward`를 연결. `RaidNumbers(...)`의 `ticketWeight` 인자를 0으로, `minGear`를 Epic으로 |
| `Items/ConsumableDatabase.cs` | 전용 재료 2개 추가(4절). 가방 "기타" 탭에 자동으로 나온다 |
| `Net/AuctionService.cs` | `BindFloor`에 두 id 추가(캐릭터 귀속) |
| `Dungeon/DungeonRewards.cs` | `RollRaidCards`, `RollRaidGold`, `RollRaidGear` 추가. `RollCards`는 `dungeon.isRaid`면 `RollRaidCards`로 넘긴다. `Slots`/`Preview`는 레이드면 확정 골드 범위, 전용 재료, 에픽+ 장비, 강화석, 마력 정수, 보호권 칩을 보여 준다. 레이드 대박 카드 없음 유지 |
| `Dungeon/DungeonAuthority.cs` | `IDungeonAuthority`에 `int RaidGold(DungeonRun run)` 추가(로컬 구현은 난수 굴림) |
| `Dungeon/DungeonRun.cs` | `RaidGold`, `CardsTakeAll`(bool), `TakenMask`(int), 확정 줄 표시용 `RaidCoreGain`, `RaidKeyGain` 추가 |

난수 굴림 규칙은 6.3과 같다(골드 1회 -> 카드 4장). 오프라인과 서버가 같은 표에서 굴리지만 난수 결과가 비트 단위로 같을 필요는 없다(서로 다른 난수기이고 서버가 정본).

### 7.2 오프라인(로컬 권한) 경로

- `DungeonDirector.Clear.cs` `FinishCleared()`: `!run.RewardsLocked`이고 레이드면 `run.RaidGold = auth.RaidGold(run)`을 `Game.Session.Gold`에 더하고(기존 상점 판매와 같은 방식), `PayRaidKeys` 결과(핵, 열쇠 개수)를 토스트 대신 `run`에도 저장해 확정 줄에 보여 준다. 카드 `auth.DealCards(...)`는 레이드면 4장 모두 가방 대상이다(`run.CardsTakeAll = true`).
- `DungeonDirector.Leave.cs` `TakeCard(int index)`: 레이드면 `TakenMask`를 보고 이미 받은 번호는 null을 돌려주고, 아니면 가방에 넣고 비트를 켠다. 동료 AI의 `CompanionPick` 흐름은 레이드에서 쓰지 않는다.
- 오프라인 레이드 상점(`DungeonSelectScreen.RaidShop.cs`): 구매 시 로컬 `Inventory`에서 재료를 빼고 같은 확률표로 굴려 무기를 넣는다. 판매 제한도 같다(구매 횟수 제한 없음).
- 오프라인 세이브 영향: 없다. 두 재료는 일반 인벤토리 항목이고 확정 골드는 골드 잔액일 뿐, 새 저장 필드가 없다. 레이드 청구 기록(`raidClaims`)은 그대로다. `SaveData.CurrentVersion`(6)을 올리지 않는다.

### 7.3 온라인 경로

- `FinishClearedOnline()`: 응답 `raid.gold_gain`, `core_gain`, `key_gain`, `card_mode`를 읽어 `run`에 넣는다. `card_mode == 'take_all'`이면 `run.Cards`를 4칸 자리표시자로 만들고 `CardsTakeAll`을 켠다. 토스트("고대의 핵 +N")는 확정 줄이 대신한다.
- `Net/OnlineEconomy.cs`:
  - `PickCard(int index, done)` 응답 파서: `cards` 배열의 null 칸을 건너뛴다. 응답의 `card`와 `index`로 그 카드만 채운다. 재시도는 같은 `request_id`로 3번까지 이미 하는 `Post`를 그대로 쓴다(번호마다 한 번 만든 `request_id`를 재전송에 재사용).
  - `409 CARD_ALREADY_FLIPPED`는 실패가 아니라 "이미 받음"이다. 응답 `extra.card`로 그 카드를 채우고 뒤집기 연출을 이어 간다.
  - `ClaimUnpickedCards()`: `unpicked_runs[]`의 `card_mode`를 본다. `take_all`이면 번호 0~3을 각각 새 `request_id`로 보내 남은 카드를 모두 받고(`remaining`만큼), `pick_one`이면 지금처럼 무작위 한 장. 토스트는 한 번에 요약("확정된 레이드 보상: ...").
  - `LoadRaidShop(done)`, `BuyRaidShop(productId, done)`: 7.5의 상점이 쓴다.
- 서버가 틱으로 이미 지급한 판은 `getRun`의 `taken`으로 알 수 있다. 결과 창을 다시 열 일이 있을 때 이 값으로 뒤집힌 상태를 복원한다.
- `data_version` 불일치(7.7)이면 구버전 클라이언트는 서버가 막는다. 그래서 `card_mode`가 없는 응답을 새 클라이언트가 받는 경우는 없다. 방어용으로 `card_mode` 누락은 `pick_one`으로 본다.

### 7.4 결과 화면 디자인 명세 (코드 생성 uGUI)

파일 길이 때문에 `DungeonResultScreen.cs`(417줄)를 건드리지 말고 `DungeonResultScreen.Raid.cs`(partial)를 새로 만든다. 카드 한 장의 그림과 연출은 `RaidCardView`라는 작은 클래스로 만들어 결과 화면과 상점 팝업이 같이 쓴다. 지금 창 구조(왼쪽 랭크/점수, 오른쪽 카드 패널 828x340, 아래 딜 기여도, 버튼 3개)와 카드 크기(150x210, 간격 20)는 유지한다. 레이드 판(`run.CardsTakeAll`)에서만 아래가 적용된다.

#### (가) 확정 보상 줄 - 카드 패널 제목줄 오른쪽

- 제목 왼쪽: `<b>보상 카드</b>  <color=#ffe066>카드를 눌러 뒤집으세요 (2/4)</color>` (받은 장수 표시). 오른쪽에 확정 칩 3개를 오른쪽 정렬로 나란히 둔다(칩 한 개: 아이콘 24px + 숫자 18pt, 칩 사이 14px, 전체 폭 약 520px).
  1. 골드 칩: `icon_gold` + `+3,750 G` (색 `#ffe066`)
  2. 고대의 핵 칩: `icon_core` + `+1` (없으면 숨김)
  3. 봉인 열쇠 칩: `icon_key` + `+35` (해골왕만, 없으면 숨김)
- 연출: 랭크 도장이 내려앉은 뒤(0.55초) 0.2초 쉬고, 칩이 왼쪽에서 오른쪽으로 0.1초 간격으로 나타나며 숫자가 0에서 값까지 0.6초 동안 올라간다. 올라가는 동안 효과음 `pickup`을 숫자 변화 4번마다 한 번(너무 시끄럽지 않게 최대 6번). 값은 이미 가방/골드에 들어간 확정이므로 "받기" 버튼은 없다.
- 잠긴 판(보상 없음)에서는 줄 전체를 숨기고 기존 `NoCardsText`를 보여 준다.

#### (나) 카드 뒷면 `raid_cardback` (픽셀 40x56, 기존 `dgn_cardback`과 같은 비율, ProceduralArt로 생성)

- 바탕 `#1b1424`(어두운 보라 검정), 격자무늬 `#2a2036`(기존 뒷면처럼 8픽셀 대각선).
- 테두리 4픽셀: 바깥 `#8f2a3a`(진홍), 안쪽 한 줄 `#d8b36a`(뼈빛 금색), 모서리 2x2 금색 징 4개.
- 가운데 문양: 왕관 쓴 해골 15x15(해골 `#f2ecd8`, 눈구멍 `#1b1424`, 왕관 `#d8b36a`). 기존 마름모 문양을 쓰지 않는다.
- 외곽선 `#2a1c10`(기존과 같음).
- 대기 연출: 뒷면 4장 모두 등장할 때 아래에서 위로 40px 올라오며 페이드인 0.25초(카드마다 0.08초 지연). 이후 뒷면 가장자리 빛 `raid_glow`(흰색 부드러운 사각 빛, 색 `#d8b36a`)이 알파 0.12~0.28을 1.6초 주기로 숨 쉬듯 반복해 "눌러 보세요"를 알려 준다. 선택 커서가 있는 카드는 기존처럼 `UIColors.Highlight` 테두리.

#### (다) 카드 앞면 5종 (픽셀 40x56, 프레임 색만 다르고 구조는 같다)

| 앞면 키 | 쓰임 | 바탕 | 테두리 바깥 / 안쪽 줄 | 장식 | 글자색 |
|---|---|---|---|---|---|
| `raid_front_mat` | 전용 재료, 강화석, 마력 정수 | `#e4e9ee` | `#8fa3b8` / `#c7d3df` | 위아래 가는 구분선 `#cfd8e2` | `#2a1c10` |
| `raid_front_ticket` | 장비 보호권 | `#e4eef7` | `#5aa9ff` / `#a9d3ff` | 구분선 `#c7dff2` | `#2a1c10` |
| `raid_front_epic` | 에픽 장비 | `#efe6ff` | `#b77bff` / `#dcc2ff` | 구분선 `#d7c5f2`, 모서리 점 | `#2a1c10` |
| `raid_front_unique` | 유니크 장비 | `#fff3c6` | `#ffd84a` / `#fff1a0` | 모서리 마름모 `#b58a1c` 4개 | `#2a1c10` |
| `raid_front_legend` | 레전더리 장비 | `#2a1608`(어둡게) | 바깥 `#ff8a3d` / 안쪽 `#ffe0a0`(이중 테두리) | 위쪽 작은 왕관 9x5, 모서리 장식 5x5 `#ffb347` | `#fff0d0` |

- 등급 색은 게임의 `EquipmentDatabase.RarityHex`(에픽 `#b77bff`, 유니크 `#ffd84a`, 레전더리 `#ff8a3d`)와 같다.
- 앞면의 구성(위에서 아래로): 등급 띠 글자(8pt 느낌의 작은 글자: "재료", "보호권", "에픽", "유니크", "레전더리"), 아이콘 72x72(카드 위에서 34px), 이름과 수량(기존 `Face` 텍스트 위치). 장비면 이름은 등급 색, 아래 줄에 `에픽 · 무기`. 재료면 이름 아래 `x24`.
- 색만으로 구분하지 않도록 등급 글자 띠가 항상 있다.

#### (라) 뒤집기 연출 (모두 `unscaledDeltaTime`)

공통: 요청이 서버(또는 로컬)에서 돌아온 뒤에 시작한다(내용을 알아야 하므로). 카드가 눌리면 먼저 0.08초 눌림(스케일 0.96), 이어 `card_flip` 효과음과 함께 가로로 접힘 0.14초, 앞면 교체, 펼침 0.14초. 펼침이 끝나면 스케일 1.06에서 1.0으로 0.1초 튕김. 뒤집힌 카드 아래 라벨은 `획득`(`#8fe28f`).

| 종류 | 뒤집기 전 | 뒤집은 뒤 |
|---|---|---|
| 재료/보호권 | 없음 | 수량 글자가 0.15초 팝인. 전용 재료는 아이콘 뒤에 `raid_glow` 흰빛 0.3초 |
| 에픽 | 0.45초: 카드 테두리가 `#b77bff`로 두 번 깜빡, 카드가 6px 위로 뜸. 효과음 `select` | 보라 잔광(`raid_glow` 알파 0.35)이 계속 숨 쉼. 효과음 `pickup` |
| 유니크 | 0.9초: 기존 대박 연출 유지(금색 `#ffe15a`, 카드가 떨리며 커지고 알파 맥박). 효과음 `rank_reveal` | 금색 고리(`raid_ring` 0.4초 동안 1.0배에서 1.6배, 알파 1에서 0) + 금색 잔광. 효과음 `quest`. 토스트 `유니크 획득: 이름` |
| 레전더리 | 1.5초: 카드 패널의 다른 카드가 35% 어두워지고, 카드 뒤에 `raid_rays`(8갈래 빛줄기, 카드의 2.2배, 주황 `#ff8a3d`)가 알파 0에서 0.8로 올라가며 초당 30도 회전, 카드가 강하게 떨림(회전 +-4도, 스케일 1.12까지). 효과음 `rank_reveal`. 끝에서 흰 섬광(패널 전체 흰색 알파 0.8에서 0, 0.25초)과 카메라 흔들림(0.15, 0.3). | 주황 잔광과 위로 떠오르는 불꽃 입자 12개(작은 정사각형 4px, 1.2초 동안 위로 60px 이동하며 사라짐), 카드 위에 `레전더리!` 큰 글자(28pt, 주황 `#ff8a3d`, 외곽선 `#2a1608`) 1.5초. 효과음 `quest`. 토스트 |

- 연출 도중 같은 카드나 다른 카드를 누르면 무시한다(`picking` 상태). 한 장이 끝나야 다음 장을 누를 수 있다.
- 새 효과음은 만들지 않고 기존 이름(`card_flip`, `select`, `pickup`, `rank_reveal`, `quest`)만 쓴다.

#### (마) 조작과 끝내기

- ←/→ 커서 이동, Enter 또는 공격 키로 뒤집기. 뒤집은 뒤 커서는 다음 안 뒤집은 카드로 자동 이동.
- 첫 장을 뒤집은 뒤부터 카드 패널 오른쪽 아래 힌트 자리에 `남은 카드 모두 뒤집기` 버튼이 나온다. 누르면 남은 카드를 번호 순으로 0.45초 간격으로 자동 뒤집는다(각각 서버 요청 1번). 자동 뒤집기 중 유니크 이상이 나오면 그 카드의 연출이 끝날 때까지 다음 카드를 기다린다.
- 4장이 모두 뒤집혀야 `done`이고 버튼 3개(다시 도전/던전 선택/마을로)가 켜진다. `Close()`/Esc는 `done` 전에는 효과음 `cancel`만 낸다(기존과 같다).
- 동료 AI가 카드를 뒤집는 단계와 `파티원이 카드를 고르는 중…` 문구는 레이드에서 없다.
- 4장 완료 뒤 카드 패널 왼쪽 아래에 한 줄 요약: `해골왕의 왕관 조각 +9  (보유 41)`. 보유 수는 가방에서 센다(온라인은 `delta` 반영 뒤 값).

### 7.5 레이드 상점 화면 (`DungeonSelectScreen.RaidShop.cs`, partial 신규)

- `DungeonSelectScreen.cs`의 `Refresh`에 두 줄만 더한다: 레이드 탭이면 소탕 버튼을 숨기고 `레이드 상점` 버튼(`ui_btngray`, 소탕 버튼과 같은 자리, 폭 180)을 보인다. 키보드는 소탕과 같은 키(`UseItemPressed`)를 레이드 탭에서 상점으로 쓴다.
- 패널(`SweepPanel`과 같은 방식으로 상세 영역을 덮음): 제목 `레이드 상점`, 닫기 버튼, 2열.
  - 왼쪽 열 `해골왕 (Lv.20)`, 오른쪽 열 `수호자 그라흐 (Lv.40)`. 각 열 위에 재료 아이콘 + 이름 + `보유 N`.
  - 각 열에 상품 2개. 상품 한 칸: 상자 아이콘(에픽+는 `icon_box_sealed`, 레전더리는 `icon_box_enh_gold`), 이름, `재료 N개`(부족하면 붉은색), 결과 줄(`내 직업 무기 · 에픽 88% / 유니크 12%` 또는 `내 직업 무기 · 레전더리 확정`), 구매 버튼.
  - 레전더리 상품 칸에는 결과 장비 아이콘과 이름(전사 `용골 대검`, 마법사 `별의 지팡이` 등)을 작게 보여 주고, 마우스를 올리면 기존 `GearTooltip`이 뜬다.
- 구매 흐름: 버튼 -> 확인 팝업(`ConfirmScreen`: `재료 90개를 써서 해골왕 에픽+ 무기 상자를 엽니다`) -> 요청 -> 응답 장비로 카드 한 장 뒤집기 팝업. 이 팝업은 `RaidCardView`를 1.5배로 화면 가운데에 놓고 7.4(라)의 연출을 그대로 쓴다(재료 카드가 아니라 장비 카드이므로 에픽/유니크/레전더리 연출). 끝나면 `확인`.
- 구매 중 중복 클릭 방지(요청이 끝날 때까지 버튼 비활성). 요청 한 번에 `request_id` 하나, 네트워크 재시도는 같은 `request_id`.
- 오프라인은 같은 화면에서 로컬 구매(7.2).

### 7.6 기타 표시

- 던전 선택 창 레이드 상세의 보상 칩(`Slots`)과 한 줄 문구(`Preview`)를 새 구성에 맞춘다: `확정 골드 3,000~4,500`, 전용 재료 이름, `에픽 이상 장비`, `강화석`, `마력 정수`, `장비 보호권`, `고대의 핵 x1`(기존). 기존 문구 "유니크 · 레전더리 장비"는 유지해도 된다.
- 결과 화면 안내 문구 `NoCardsText`는 그대로다. 해골왕 문구는 "오늘 이 레이드 보상을 이미 받았습니다. 매일 06:00에 초기화됩니다."가 코드(일일 귀속)와 맞다.
- 전용 재료 툴팁: 설명문(4절) + `레이드 상점에서 무기 상자로 바꿉니다`.
- 소리, 한글 글꼴, 이모지 금지: 기존 UI 규칙을 따른다(화면에 OS 기본 이모지를 쓰지 않는다).

### 7.7 데이터 다시 내보내기와 `data_version` 갱신 절차

1. C# 변경(7.1)을 마친 뒤 Unity 메뉴 `dotRPG > Export Server Data` 또는 배치 `-executeMethod DotRPG.EditorTools.GameDataExport.Export`를 실행한다.
2. `GameDataExport.cs` 변경: `Items()`의 id 목록에 두 재료 추가(`kind=consumable`, `bind`는 `BindFloor`가 `character`로 내보냄), `Dungeons()`가 레이드마다 `raidReward` 객체를 쓰고, 새 `RaidShop()` 빌더를 만들어 `files` 사전에 `["raid_shop.json"] = RaidShop()`을 넣는다.
3. 결과로 `server/data/dungeons.json`, `items.json`, `raid_shop.json`, `data_version.json`(새 해시), `Assets/Resources/Data/DataVersion.txt`, `StreamingAssets/DataVersion.txt`가 바뀐다. `git diff`에서 이 다섯 계열 파일 외의 변경이 없는지 본다(그 외 파일이 바뀌면 의도치 않은 데이터 변경이다).
4. `python3 Tools/balance/theory_income.py`를 돌려 `income_caps.json`이 바뀌지 않았는지 확인한다(3.6).
5. 서버와 클라이언트를 같은 `data_version`으로 함께 배포한다. 클라이언트는 요청마다 `X-Data-Version` 헤더를 보내고 서버는 불일치를 막는다(`versionCheck.test.ts`). 서버 배포 순서는 마이그레이션 0028 -> 서버 코드 -> 클라이언트 빌드다.

## 8. 시험 목록

서버 jest (`server/test/`). 새 파일 `raidRewards.test.ts`(규칙, 통합)와 `raidShop.test.ts`(상점).

순수 규칙:
1. `rollRaidCards`: 4장, 모든 카드가 표에 있는 항목, `gold` 항목이 없다, 수량이 `min..max`(난이도 배율 없음).
2. 장비 카드: 해골왕은 `_20_`, 그라흐는 `_40_` 단계 키, 직업이 쓸 수 있는 장비, 등급은 에픽 이상, +0 키. 전사/마법사 모두.
3. 10만 장 표본: 카드 종류 비율이 표 가중치 +-0.8%p 안, 장비 카드 등급 비율(85/14/1, 58/41/1)이 +-1%p 안(레전더리는 표본이 작아 출현 여부와 상한만).
4. `rollRaidGold`: 범위 안, 100의 배수, 양 끝 값이 모두 나온다(표본 1만 회).
5. 데이터 정합: 두 레이드의 카드 가중치 합 100, 전용 재료 기대값 m(해골왕 10.6, 그라흐 10.24)을 구해 `레전더리 상자 가격 / m / 주당 횟수(3, 1)`가 8~12주 안, 에픽+ 상자가 2~4주 안. 가격이나 표를 바꿔 목표를 벗어나면 이 시험이 실패한다.
6. `gamedataIntegrity`: `raid_shop.json`의 재료 id가 `items.json`에 있고 `bind=character`이다. 각 상품마다 두 직업 모두 (단계, 부위, 등급)에 맞는 장비가 정확히 하나 있다. 전용 재료의 `sellPrice`가 0이다. `data_version.json`의 `files`에 `raid_shop.json`이 있다.
7. `income_caps.json`: `raidMidGoldEq >= 해골왕 goldMax`, `raidFinalGoldEq >= 그라흐 goldMax`.

통합 (DB):
8. 솔로(AI 동반이 아닌 사람 2명 이상 파티 포함) 레이드 클리어: `gold_ledger`에 `raid_gold` 한 행(범위 안), `dungeon_runs.raid_gold`와 같은 값, `cards_mode='take_all'`, `cards_taken=0`, 응답 `raid.gold_gain`, `card_count=4`, 응답에 카드 내용이 없다.
9. 잠긴 판(ALREADY_CLAIMED, TOO_FEW_HUMANS, KEYS_MISSING, LOW_CONTRIBUTION): 골드 행 없음, 카드 없음, `gold_gain` 없음. 그날(주) 두 번째 클리어도 골드 없음.
10. 뒤집기: 2번 -> 200, `item_ledger` 한 행(`dungeon_card`, ref `<run>:2`), `cards_taken=4`, 응답 `cards`는 2번만 채워지고 나머지 null, `remaining=3`. 같은 `request_id` 재전송은 같은 본문이고 원장 행이 늘지 않는다. 새 `request_id`로 같은 번호는 409 `CARD_ALREADY_FLIPPED`(카드 내용 포함).
11. 4장을 섞인 순서(3, 0, 2, 1)로 모두 받으면 `cards_taken=15`, 원장 4행, 가방에 합계가 맞게 들어 있다.
12. 동시성: 같은 카드를 다른 `request_id`로 동시에 두 번 보내면 200 하나와 409 하나, 원장 한 행.
13. 오류: `index=4`는 400, 다른 캐릭터의 판은 404, 클리어 아닌 판은 422 `NO_CARDS`, 유효 기간 지난 판은 410.
14. 틱: 아무것도 안 받은 판은 10분 뒤 4장 모두 지급(원장 4행). 1번만 받은 판은 나머지 3장만 지급(1번은 두 번 안 줌). 틱을 두 번 돌려도 추가 지급이 없다. 틱 이후 클라이언트의 뒤집기는 409(카드 내용 포함).
15. 파티 정산(사람 2명): 두 멤버가 각자 골드와 카드 4장을 받는다. 이미 정산된 판에 `settle` 재호출 시 `gold_gain`, `card_mode`를 돌려준다. 한 멤버가 잠기면 그 멤버만 골드와 카드가 없다.
16. 보류 해제(`adminOps`): 해제된 레이드 판은 골드가 나가고 `unpicked_runs`에 `card_mode=take_all`, `remaining=4`로 보인다.
17. 마이그레이션 호환: 0028 이전에 클리어된 레이드 판(기본값 `pick_one`)은 예전처럼 한 장 고르기가 되고 두 번째는 409 `CARD_ALREADY_PICKED`.
18. 속도 감시: 해골왕 하루 1회 클리어(확정 골드 최대 + 카드)가 감시 임계를 넘지 않는다(`economyHold.test.ts` 계열). `raid_gold`가 `gold_acq` 버킷에 집계되고 `raid_shop_*`는 집계되지 않는다.
19. 상점 조회: 상품 4개, `have`가 가방 수와 같다, `outcomes`의 장비가 caller 직업이다.
20. 상점 구매: 재료 부족이면 422 `NOT_ENOUGH_MATERIAL`(`need`, `have`)이고 아무것도 안 바뀐다. 성공이면 재료가 정확히 가격만큼 빠지고, 장비 +1(캐릭터 귀속), `item_ledger` 두 행(`raid_shop_cost` 음수, `raid_shop_result` +1, 같은 `request_id`), `raid_shop_purchases` 한 행. 같은 `request_id` 재전송은 같은 본문이고 재료가 다시 안 빠진다.
21. 상점 동시성: 재료가 한 번 살 만큼만 있을 때 서로 다른 `request_id`로 동시에 두 번 사면 하나만 성공.
22. 상점 결과 규칙: 레전더리 상품은 항상 caller 직업의 해당 단계 레전더리 무기(전사 검, 마법사 지팡이), 에픽+ 상품은 10만 회 표본에서 유니크 12% +-1%p이고 레전더리가 나오지 않는다. 다른 레이드 재료로는 살 수 없다.
23. 입력 검증: `price`, `count`, `class`, `rarity` 같은 여분 필드는 400. 모르는 `product_id`는 422. 경제 정지면 403.
24. 귀속: 상점으로 산 장비의 경매 등록과 우편 첨부가 거절된다(기존 귀속 오류 코드). 전용 재료도 마찬가지.

기존 시험 수정:
- `sweepRules.test.ts`의 `A3 직접 플레이 rollCards 불변`은 레이드를 비교 대상에서 뺀다(요일던전만 옛 루프와 같아야 한다). 레이드 레전더리 분기를 쓰던 `legacyRollCards`의 `RAID_LEGENDARY_PERMILLE` 부분은 지운다.
- `raids.test.ts`의 "중간 레이드 ... 카드" 시험은 `take_all` 기대값으로 고친다.

클라이언트(`DevCapture.Dungeon` 논리 검사만, 게임 실행 없이 결과 로그 확인):
- 오프라인 레이드 `DealCards`가 4장이고 골드 항목이 없다. `RaidGold`가 범위 안이다.
- `TakeCard`를 4번(0~3) 호출하면 가방에 모두 들어가고 같은 번호 재호출은 null이다.
- 요일던전 `DealCards`/`TakeCard` 기존 검사가 그대로 통과한다.

## 9. 요일던전, 소탕, 대박 카드가 영향받지 않는다는 확인

| 항목 | 확인 근거 |
|---|---|
| 요일던전 카드 4장 중 1장 | `finalizeCleared`의 새 분기는 `dungeon.isRaid`에서만 탄다. 요일던전은 `rollCards`, `cards_mode='pick_one'`(기본값), `pickCard` 기존 경로, `CARD_ALREADY_PICKED` 그대로다 |
| 소탕 | `sweepService.ts`가 쓰는 `rollSweepCard` -> `rollOneCard`는 이름과 동작을 바꾸지 않는다(레이드 전용 분기만 삭제). 소탕은 `d.isRaid`를 `SWEEP_NOT_ALLOWED`로 거절하므로 레이드가 들어올 수 없다. A3 시험이 요일던전의 난수열 동일성을 계속 보장한다 |
| 요일던전 대박 카드(유니크만) | `rollJackpot`, `jackpotPerMille`(난이도 표)은 건드리지 않는다. 레이드 난이도에는 원래 대박이 없다 |
| 요일던전 결과 화면 | `DungeonResultScreen`의 기존 경로(`run.CardsTakeAll == false`)는 코드가 바뀌지 않는다. 레이드 전용 코드는 partial 파일로 분리한다 |
| 파티 던전(레이드 아님) | `finalizeCleared`의 비레이드 분기와 `storedResult`의 비레이드 응답이 그대로다 |
| 고대의 핵, 봉인 열쇠 조각 | 지급 위치, 수량, 원장 사유(`raid_core`, `raid_key`, `raid_key_cost`)가 그대로다 |
| 보상 귀속(`raid_claims`) | 키와 판정 순서 그대로. 새 골드와 카드는 같은 잠금 뒤에서만 나간다 |
| 바르가스, 바위 심장(`Upcoming`) | 내보내지 않는 보관 레이드라 영향 없음. 열 때 각자 `raidReward`와 전용 재료, 상점 상품이 필요하다 |

## 10. 결정 (확정)

아래 7건은 모두 "기본값" 열의 선택으로 확정됐다(해골왕 주 3회 기준 가격, 승급 경로 그대로, 상점 장비 캐릭터 귀속, 해골왕 에픽+ 6%, 전용 재료 아이콘 재사용).

| # | 질문 | 확정 | 채택하지 않은 선택지 |
|---|---|---|---|
| 1 | 해골왕은 코드상 주 3회 보상인데, 기간 기준을 무엇으로 볼까 | 주 3회(해골왕), 주 1회(그라흐) 기준으로 상자 가격을 맞췄다 | 해골왕을 주 1회로 본다면 해골왕 가격을 3분의 1로(에픽+ 30, 레전더리 110) |
| 2 | 승급(핵 18개 + 11만 골드)으로 해골왕 레전더리가 약 6주에 나온다. 그대로 둘까 | 그대로 둔다 | `PromoteRules.CostInto`를 올려(유니크 6->9핵, 레전더리 12->18핵) 상자 쪽과 속도를 맞춘다 |
| 3 | 상점으로 산 장비의 귀속 | 캐릭터 귀속 | 거래 가능(카드 장비와 같음). 이 경우 재료가 간접 거래된다 |
| 4 | 해골왕 카드의 에픽 이상 장비 비율 | 카드당 6%(한 번에 0.24장, 기존의 2.4배) | 4%(0.16장)로 낮추고 남는 2를 왕관 조각에 더한다 |
| 5 | 확정 골드 크기 | 해골왕 3,000~4,500, 그라흐 12,000~18,000 | 기존 기대값에 가깝게 줄이면 해골왕 600~900 등. 속도 감시 상한은 어느 쪽이든 안전 |
| 6 | 전용 재료 그림 | 기존 아이콘 재사용(뼈 조각, 강화석 모양 + 등급 프레임 색) | 전용 그림 2장을 새로 그린다 |
| 7 | 에픽+ 무기 상자에 레전더리를 넣을까 | 넣지 않는다(에픽 88 / 유니크 12) | 0.5%를 넣는다. 레전더리 상자 가격 가치가 흔들린다 |

## 11. 이 문서가 확정된 뒤 같이 고칠 문서

- `Docs/PLAN_DUNGEON_RAID.md` 6.2 "보상" 줄과 "열린 레이드 정리"의 핵 문구
- `Docs/PLAN_GEAR_RENEWAL.md` 5절 "해골왕/그라흐 레이드" 행(해골왕: 에픽~유니크 + 레전더리, 레전더리는 상점으로도)
- `Docs/server/phase4_api.md`(레이드 파트), `phase3_api.md` 9절(카드 선택), `phase9_anti_abuse.md` 12절 표(레이드 골드 덩어리의 출처가 `raidReward.goldMax`)
- `server/schema.sql` 머리말의 "기능별 테이블 사용처" 표
