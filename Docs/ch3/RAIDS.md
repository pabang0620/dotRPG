# 챕터 3 레이드 (2026-10-21)

틀은 기존 그대로다(`STORY.md` 6절, `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 5절): 중간 레이드 수·토·일 + 봉인 열쇠 조각(keyMin 20, keyMax 50), 최종 레이드 일요일 keyCost 60, 카드 4장, 재료 상점, 심화 레이드(x2.4 체력, x1.6 피해, 보상 x1.5), 혼자 + AI 동료도 보상.

## 1. 중간 레이드: 공허 침식체 시브

| 항목 | 값 | 근거 |
|---|---|---|
| id | `raid_xib` | |
| 이름 | 공허 침식체 시브 (Void Blight Xib) | xib = 두려움 |
| 무대 | 깨진 수호석 자리(`DgnXib1`, `DgnXib2`, `DgnXibBoss`, 테마 DryCanyon) | |
| raidTier / chapter | Mid / 3 | |
| unlockQuest | c3_xib | |
| openDays | 수·토·일 | |
| RaidNumbers | recommendedLevel 45, power 13,000, hpMul 5.2, damageMul 1.8, monsterLevel 40, minGear Epic, ticketWeight 8 | 그라흐(40, 10,400, 4.6, 1.7, 35)에서 레벨 5 증가분. monsterLevel은 권장 레벨보다 5 낮게(기존 규칙). power는 요일 던전 군주와 같은 13,000 |
| rooms | 1: void_sentry 5 + void_caller 3 / 2: void_hound 6 + grey_ranger 3 / 보스: 시브 + 졸개 void_hound | |
| referenceSeconds | { 330, 495, 330, 330 } | 바위 심장과 같음 |
| clearXp | 1,200 | 골렘 900과 그라흐 1,500 사이 |
| 보스 | `boss_xib`: hp 7,600, dmg 58, lines 70, speed 2.2, size 2.6, artScale 1.5, raid = true, phases { 0.7, 0.4, 0.15 }, maxMinions 4, groggyMax 760 | 그라흐 6,400/51에 x1.19/x1.14 |
| 패턴 | Slash3(4s, 0.9, 3, maxRange 3.6, weight 1.5), Fields(7s, 1.1, 7, phase 2), Donut(10s, 1.4, phase 2), Summon(14s, 2, void_hound), Repel(16s, 0.8, phase 2), Annihilate(22s, 2.2, phase 2), MarkOfFate(40s, phase 2, weight 0.8) | 기존 15종 + 레이드 전용 중에서 선택 |
| 신규 기믹 1개 | 공허 균열(VoidRift): 페이즈 2부터 32초마다 방 안 두 곳에 균열이 열리고 8초 뒤 균열마다 void_hound 2체가 나온다. 균열 위에 3초 서 있으면 닫힌다(서 있는 동안 받는 피해 +30%). 균열을 닫지 않으면 졸개가 maxMinions까지 쌓인다. 사람이든 AI든 밟으면 닫힌다 | "수호석 자리를 메운다"는 이야기와 같은 동작. 혼자일 때도 AI 동료가 밟는다 |
| 보상 | 확정 골드 10,000~15,000, 재료 `mat_raid_xib`(침식 결정), 카드: 장비 1(12), 재료 1(12)·2(30)·4(28)·8(9), 정수 16(6), 보호권 1(3). 장비 등급 에픽 58 / 유니크 41 / 레전더리 1 | 그라흐 표와 같은 구조, 골드만 Lv45 |
| 열쇠 | 보상 받는 클리어마다 봉인 열쇠 조각 20~50(기존 아이템 `DungeonDatabase.SealKey` 재사용) | 1·2장과 같은 아이템이라 2장 잔여 조각이 3장에 쓰인다 |
| 핵 | `PromoteRules.RaidCoreMid` { 1, 2, 2, 3 } | |

## 2. 최종 레이드: 잿빛 손 지휘관 카마츠

| 항목 | 값 | 근거 |
|---|---|---|
| id | `raid_kamatz` | |
| 이름 | 잿빛 손 지휘관 카마츠 (Grey Hand Commander Kamatz) | 카마조츠, 박쥐 신. 잿빛 손의 어둠 |
| 무대 | 공허의 틈 끝 잿빛 손 진영(`DgnKamatz1`, `DgnKamatzBoss`, 테마 DryCanyon 야영지) | |
| raidTier / chapter | Final / 3 | |
| unlockQuest | c3_kamatz, keyCost 60 | |
| openDays | 일 | |
| RaidNumbers | recommendedLevel 50, power 15,500, hpMul 5.0, damageMul 1.9, monsterLevel 45, minGear Epic, ticketWeight 0 | 그라흐에서 레벨 10 증가분. monsterLevel은 권장 레벨보다 5 낮게. 심화는 x2.4/x1.6 |
| rooms | 1: grey_knight 6 + grey_ranger 3 / 보스: 카마츠 + 졸개 grey_knight | 그라흐처럼 방 1 + 보스 |
| referenceSeconds | { 360, 540, 360, 360 } | 그라흐와 같음 |
| clearXp | 2,000 | |
| 보스 | `boss_kamatz`: hp 8,800, dmg 64, lines 80, speed 2.4, size 2.4, artScale 1.4, raid = true, phases { 0.7, 0.4, 0.15 }, maxMinions 4, groggyMax 800 | 사람형 보스(카엘과 같은 체격의 중무장 기사). 그라흐 x1.38/x1.25 |
| 패턴 | Slash3(4s, 0.95, 3, maxRange 3.4, weight 1.5), Charge(7s, 1.3), ArrowVolley(8s, 1.0, 9, phase 1), Guard(12s, phase 1), BoltRing(6s, 1.0, 12, phase 2), Fields(7s, 1.1, 7, phase 2), Annihilate(22s, 2.2, phase 2), RuinRitual(48s, phase 2, weight 0.8), Judgment(35s, 8.0, phase 3, weight 0) | 사람형이라 Summon 대신 Guard·ArrowVolley |
| 신규 기믹 1개 | 잿빛 낙인(GreyBrand): 페이즈 2부터 30초마다 카마츠가 파티원 한 명에게 잿빛 낙인을 찍는다. 6초 안에 다른 파티원(AI 포함)과 2m 안에 서면 낙인이 둘로 나뉘어 사라지고, 혼자면 낙인이 터져 그 사람에게 최대 HP 70% 피해. 혼자 + AI일 때는 AI가 낙인 대상에게 다가온다(`CompanionBrain`) | "너도 하나, 나도 하나"의 반대말. 둘이 나누면 사라지는 저주 |
| 보상 | 확정 골드 16,000~24,000, 재료 `mat_raid_kamatz`(잿빛 인장), 카드: 장비 1(12), 재료 1(12)·2(30)·4(28)·8(9), 정수 16(6), 보호권 1(3). 장비 등급 에픽 58 / 유니크 41 / 레전더리 1(Lv50 레전더리) | |
| 핵 | `PromoteRules.RaidCoreFinal` { 2, 3, 4, 6 } | |

## 3. 레이드 상점

`RaidShop.Products`에 4개 추가. 가격 원칙은 기존과 같다(에픽+ 상자는 보상 1.5~2주, 확정 레전더리는 5~6주. 2달 창은 챕터 3 안에서 다시 돈다).

| id | 이름 | 재료 | 가격 | 내용 |
|---|---|---|---|---|
| xib_epic_weapon | 시브 에픽+ 무기 상자 | mat_raid_xib | 25 | 티어 9 무기, 에픽 88 / 유니크 12 |
| xib_legend_weapon | 시브 레전더리 무기 상자 | mat_raid_xib | 70 | 티어 10 레전더리 무기(레전더리는 Lv50만 존재) |
| kamatz_epic_weapon | 카마츠 에픽+ 무기 상자 | mat_raid_kamatz | 20 | 티어 10 무기, 에픽 88 / 유니크 12 |
| kamatz_legend_weapon | 카마츠 레전더리 무기 상자 | mat_raid_kamatz | 60 | 티어 10 레전더리 무기 |

- 재료 기대값은 그라흐와 같은 카드 표라 판당 약 2.56개. 시브 주 3회 + 심화 주 1회 = 약 20개/주 → 레전더리 상자 3.5주. 카마츠 주 1회 + 심화 = 약 10개/주 → 6주.
- `RaidRewards.IsRaidMaterial`에 두 재료 추가. 서버 `raidshop` 도메인은 데이터(`raid_shop.json`)로 움직이므로 내보내기만.

## 4. 공통

- `DungeonDatabase.RaidDefs`에 두 정의를 넣고 내보낸다. `Upcoming`에는 넣지 않는다(바르가스·골렘은 그대로 Upcoming). 닫힌 동안의 잠금은 `CHAPTER3_OPEN`이 맡는다(`LIVE_CONTENT.md` 6절): 서버 `dungeonResult`가 3장 레이드 id의 클리어를 거부하고, 클라이언트 `DungeonDirector.CannotEnterReason`이 "아직 열리지 않은 레이드"를 돌려준다.
- `ContentUnlocks.Raid`는 그대로(1장 해골왕에서 열린다). 3장 레이드는 unlockQuest 수락으로 열린다.
- 혼자 + AI 기준: 측정 봇(장판 회피 켬)이 일반 난이도를 절반 이상 클리어하고, 신규 기믹 2개를 AI 동료가 수행한다(균열 밟기, 낙인 나누기). `CompanionBrain`에 두 행동을 추가한다.
- 심화 레이드 보상 주기·`raid_claims`·열쇠 이월은 기존 서버 규칙 그대로.
- 월드보스·길드보스는 레이드와 별개로 Lv50 보스 방을 쓴다(`LIVE_CONTENT.md` 2절).
