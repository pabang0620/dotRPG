# 실측 기반 플레이 테스트 2차 (2026-10-12)

1차(`Docs/PLAN_SIM_FEEDBACK_2026-10-12.md`)는 게임을 돌리지 않고 코드 규칙과 가정(분당 처치 12, 던전 승률 로지스틱)으로 20명 시뮬레이션을 했다.
2차는 **게임 안에서 자동 플레이로 실제 수치를 잰 뒤** 그 값을 시뮬레이터에 넣고, 오늘 추가한 기능까지 반영해 다시 돌린다. 결과로 개선점을 찾아 고친다.

## 1. 실측 (게임 안 자동 플레이, 새 모드 `-dotrpgMeasure`)

자동 전투 AI(동료 AI 또는 자동 사냥)로 플레이어 캐릭터를 움직이고, 시간 배속(Time.timeScale)으로 짧게 돌린다. 무적은 쓰지 않는다(피해·물약·사망도 잰다).

| # | 측정 | 조건 | 기록 |
|---|---|---|---|
| M1 | 사냥터 20곳 | 그 사냥터 권장 레벨, 직업 4종(전직 전 구간은 전사·마법사), 그 레벨 기본 장비, 3분(게임 시간) | 분당 처치, 분당 경험치·골드·드롭, 분당 받은 피해, 물약 사용, 사망 |
| M2 | 요일 던전 5종 x 난이도 4 | 권장 레벨, 직업 4종, AI 동료 3명 | 클리어 시간, 보스 시간, 사망·부활, 실패 여부 |
| M3 | 레이드 4종 x 일반·영웅 | 권장 레벨, 혼자 / 혼자 + AI 3 | 클리어 시간, 보스 시간, 사망, 실패 여부 |

산출: `C:\Users\admin\Desktop\games\analysis\player-sim\data\dotrpg-measured.json`

```json
{
  "meta": { "build": "<git sha>", "timeScale": 4, "measuredAt": "..." },
  "hunting": [ { "zone": "canyon_ridge", "mlv": 22, "career": "fighter", "level": 22, "minutes": 3,
                 "kills_per_min": 0, "xp_per_min": 0, "gold_per_min": 0, "drops_per_min": 0,
                 "damage_taken_per_min": 0, "potions_per_min": 0, "deaths": 0 } ],
  "dungeons": [ { "id": "gold_vein", "difficulty": 0, "level": 5, "career": "fighter", "party": "1+3ai",
                  "result": "clear", "clear_s": 0, "boss_s": 0, "deaths": 0, "revives": 0 } ],
  "raids": [ { "id": "raid_skeleton_king", "difficulty": 0, "level": 20, "career": "fighter", "party": "1+3ai",
               "result": "clear", "clear_s": 0, "boss_s": 0, "deaths": 0 } ]
}
```

이론 계산(`Tools/balance/theory_*.py`)과 실측을 나란히 놓아 어긋나는 곳(모델이 틀린 곳)도 같이 본다.

## 2. 시뮬레이터 갱신 (`analysis/player-sim`)

- 분당 처치 12 가정과 던전·레이드 승률 공식을 실측 표로 바꾼다(사냥터·직업·레벨차 보간, 실측 사망률로 실패 확률).
- 오늘 기능 반영: 천장 게이지(봉인된 상자 100, 장비 뽑기 300), 별빛 정기권(₩4,400), Lv15 일일·주간 의뢰와 관문 안내 퀘스트 9개, 영웅 레이드(첫 클리어 후, 주 1회), 인원 보정 1/1.6/2.1/2.5.
- 더 복잡하게: 하루 접속 패턴(평일·주말, 세션 여러 번), 피로·지루함 누적(같은 사냥터 반복), 직업 선택 분포, 사람 파티 성사(요일·시간대), 물약·골드 소모 실측 반영.
- 규칙·퀘스트 데이터 재추출(`data/dotrpg-rules.json`, `data/dotrpg-quests.json`).
- 20명 x 10시드 x 30일, 5시드 x 60일, 1차 결과와 비교.

## 3. 개선

결과 보고서(`reports/report-dotrpg-v2.md`)에서 개선점을 뽑아 이 문서 4절에 우선순위를 적고 고친다. 이미지가 필요한 화면은 생성해서 쓴다.

## 4. 결과와 개선 목록

(측정·시뮬레이션 후 채운다)
