# 안내 의뢰 (콘텐츠를 진행 퀘스트에 녹이기, 2026-10-12)

## 1. 목적

강화, 보조 젬, 스킬 스타일, 일일 의뢰, 경매장, 파티, 창고, 판매, 자동 진행·자동 사냥, 월드맵을 설명 창 대신 "직접 해 보게 하는" 짧은 부탁(sub)으로 가르친다. 각 시스템이 열리는 시점에 그 일과 어울리는 NPC가 맡긴다.

- 보상은 소모품·강화석·소량 골드만. 경험치 0이라 40시간 성장 곡선은 그대로다.
- 이미 그 레벨을 지난 캐릭터도 선행 조건만 맞으면 언제든 받는다(장 진행으로 영영 잠기지 않는다).
- 별조각·결제는 요구하지 않는다.

## 2. 의뢰 목록

| id | 레벨 / 선행 | NPC | 할 일 (flag 목표) | 보상 |
|---|---|---|---|---|
| g_equip | Lv2 / c1_rebuild | 대장장이 무쇠 | 장비창(I)에서 장비 착용 (guide_equip) | 골드 200, 체력 물약 5, 강화석 5 |
| g_enhance | Lv4 / g_equip | 대장장이 무쇠 | 장비 강화 1회 (guide_enhance, 성공·실패 무관) | 골드 300, 강화석 10 |
| g_sell | Lv2 / c1_rebuild | 잡화상인 미르 | 상점에 물건 팔기 (guide_sell) | 골드 100, 마나 물약 5 |
| g_storage | Lv3 / c1_rebuild | 창고지기 보람 | 창고에 맡기기 (guide_storage) | 체력 물약 5 |
| g_map | Lv3 / c1_rebuild | 짐꾼 소미 | 월드맵(M) 열기 (guide_worldmap), 자동 진행(F6) 켜기 (guide_autoquest) | 귀환 주문서 3 |
| g_autohunt | Lv6 / g_map | 촌장 모리 | 사냥터에서 자동 사냥(F7) (guide_autohunt) | 체력 물약 10 |
| g_gem | Lv5 / c1_rebuild | 촌장 모리 | 젬 탭에서 보조 젬 장착 (guide_gem) | 골드 300, 마나 물약 10 |
| g_auction | Lv10 / c1_rebuild | 잡화상인 미르 | 경매장(U) 열기 (guide_auction) | 골드 1,000 |
| g_party | Lv10 / c1s_firstdungeon | 촌장 모리 | 파티 찾기(O) 열기 (guide_party) | 체력·마나 물약 10씩 |
| g_daily | c1_stronger | 촌장 모리 | 일일 의뢰 수락 (guide_daily) | 골드 1,000, 귀환 주문서 5 |
| g_style | Lv15 / career_path | 전직 안내원 | 추천 배치 (guide_recommend), 스킬 스타일 바꾸기 (guide_style) | 골드 2,000, 강화석 10 |

## 3. 구현

- 플래그는 실제 행동 지점에서 `GuideFlags.Mark`로 세운다(`Quest/GuideFlags.cs`). 이미 세운 플래그도 의뢰를 받으면 완료로 친다.
  - 장비 착용 `Equipment.Equip`(내 세션), 강화 `WindowScreens.Attempt`, 판매 `ServiceScreens.Sell`, 창고 `StorageScreen`, 일일 수락 `DailyQuestClient.Accept`, 자동 진행·사냥 `QuestAutoPilot`, 보조 젬 `Progression.SetGem`, 스타일 `SwitchStyle`, 추천 배치 `ApplyRecommended`, 경매장·파티 찾기·월드맵은 `WindowScreen.Show`.
- 자동 진행은 이 단계에서 목표 문구를 그대로 보여 주고 직접 하도록 기다린다.
- 서버: `quest_index.json`에 11건(보상 동일, flagNeeds는 검증 불가 목록과 같은 취급). `guide_` 플래그는 상태 저장 경고에서 뺀다(`stateRules`, tip_와 같은 방식).
