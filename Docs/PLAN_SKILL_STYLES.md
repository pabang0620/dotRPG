# 스킬 스타일 (빌드 프리셋) 계획 (2026-10-11)

## 1. 목표

- 파티용과 솔플용 빌드를 따로 만들어 두고 버튼 하나로 바꾼다.
- 스타일 1(파티), 스타일 2(솔로)는 무료. 스타일 3(자유)은 별조각으로 산다(계정 단위 1회, 성장 패스와 같은 구매 흐름).

## 2. 스타일 하나에 들어가는 것

| 항목 | 저장 위치(지금) | 스타일별로 |
|---|---|---|
| 전직 기술 배분(nodes) | career.nodes | 예 |
| 스킬 키·보조 젬(Q W E R T + 보조 2칸) | skill_gems | 예 |
| 패시브 트리 배분 | passives | 예 |
| 전직 직업, 각성 단계, 환급 포인트, 수련 기록 | career | 아니오(공용) |

- 각 스타일은 포인트를 따로 쓴다(같은 레벨의 포인트를 스타일마다 처음부터 배분).
- 처음 스타일을 열면 빈 배분에서 시작한다. 스타일 1은 지금 빌드를 그대로 가져간다.

## 3. 저장 형식 (서버 계약)

`career` 객체에 두 필드를 더한다(옛 세이브는 없음 = 스타일 1만 있는 것과 같다).

```
career.activeStyle: 0 | 1 | 2              // 생략 가능, 기본 0
career.styles: [                            // 생략 가능, 최대 3
  { nodes: [{id, rank}],                    // career.nodes와 같은 규칙
    gems: string[15],                       // 슬롯 5 x (주 스킬 1 + 보조 2), 빈칸은 ""
    passives: string[] }                    // passives와 같은 규칙
]
```

- 지금 쓰는 스타일의 값은 지금처럼 `career.nodes`, `skill_gems`, `passives`에도 들어간다(기존 검사·전투 경로는 그대로).
- `styles[activeStyle]`는 그 값과 같은 내용이다(클라이언트가 바꿀 때마다 덮어쓴다).

## 4. 서버

- 검사(`careerRules.validateCareer`, 상태 저장): 각 `styles[i]`의 nodes를 기존 nodes와 같은 규칙(레벨·선행·예산)으로, passives를 기존 passives 검사와 같은 규칙으로, gems의 주 스킬을 `validCareerActive`/기본 젬 규칙으로 검사한다.
- 스타일 3: `activeStyle === 2` 이거나 `styles[2]`에 무엇이든 있으면 계정이 스타일 3을 가지고 있어야 한다(없으면 422 `SKILL_STYLE_LOCKED`).
- 표 `skill_style_unlocks`(마이그레이션 0031): account_id UNIQUE, stars, request_id UNIQUE, created_at.
- API
  - `GET /skill-styles` -> `{ style3_owned, price }`
  - `POST /skill-styles/buy {request_id}` -> `{ style3_owned: true, price, balance }`. 계정 잠금 -> 같은 request_id면 같은 결과 -> 이미 보유 409 `ALREADY_OWNED` -> 별조각 차감(무료분 먼저) -> 기록. 원장 사유 `skill_style_buy`, ref `skill_style_3`.
- 가격: 별조각 3,000(서버 상수, `server/data`에 두지 않고 코드 상수. 바꾸기 쉽게 한 곳).

## 5. 클라이언트

- `CareerSave`에 `activeStyle`, `styles`(직렬화 클래스 `CareerStyle { nodes, gems, passives }`).
- `Progression.SwitchStyle(i)`: 지금 값을 `styles[active]`에 저장 -> `styles[i]`를 불러온다(없으면 빈 배분). 던전·레이드 진행 중에는 바꿀 수 없다.
- 스킬 창 위쪽에 `스타일 1 · 파티` `스타일 2 · 솔로` `스타일 3` 버튼. 스타일 3이 잠겨 있으면 별조각 3,000 구매 확인창 -> 구매 성공 시 열림.
- 바꾼 직후 자동 저장(서버 상태 업로드).

## 6. 남기는 것

- 스타일 이름 바꾸기, 스타일별 장비 세트는 이번 범위가 아니다.
