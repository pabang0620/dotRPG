# 챕터 3 레이드 (2026-10-21, 검토 반영판)

틀은 기존 그대로다(`STORY.md` 6절, `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 5절): 중간 레이드 수·토·일 + 봉인 열쇠 조각(keyMin 20, keyMax 50), 최종 레이드 일요일 keyCost 60, 카드 4장, 재료 상점, 심화 레이드(x2.4 체력, x1.6 피해, 보상 x1.5), 혼자 + AI 동료도 보상.

## 1. 중간 레이드: 공허 침식체 지바크

| 항목 | 값 | 근거 |
|---|---|---|
| id | `raid_xibak` | |
| 이름 | 공허 침식체 지바크 (Void Blight Xibak) | xib = 두려움 |
| 무대 | 깨진 수호석 자리(`DgnXibak1`, `DgnXibak2`, `DgnXibakBoss`, 테마 DryCanyon) | |
| raidTier / chapter | Mid / 3 | |
| unlockQuest | c3_xibak | |
| openDays | 수·토·일 | |
| RaidNumbers | recommendedLevel 45, power 13,000, hpMul 5.2, damageMul 1.8, monsterLevel 40, minGear Epic, ticketWeight 8 | 그라흐(40, 10,400, 4.6, 1.7, 35)에서 레벨 5 증가분. monsterLevel은 권장 레벨보다 5 낮게(기존 규칙). power는 군주 던전과 같은 13,000 |
| rooms | 1: void_sentry 5 + void_caller 3 / 2: void_hound 6 + grey_ranger 3 / 보스: 지바크 + 졸개 void_hound. 1·2번 방은 AI 슬롯 하나가 쿠크로 강제 편성(`WORLD_AND_STORY.md` 4절) | |
| referenceSeconds | { 330, 495, 330, 330 } | 바위 심장과 같음 |
| clearXp | 1,200 | |
| 보스 | `boss_xibak`: hp 7,600, dmg 58, lines 70, speed 2.2, size 2.6, artScale 1.5, raid = true, phases { 0.7, 0.4, 0.15 }, maxMinions 4, groggyMax 760 | 그라흐 6,400/51에 x1.19/x1.14 |
| 패턴 | Slash3(4s, 0.9, 3, maxRange 3.6, weight 1.5), Fields(7s, 1.1, 7, phase 2), Donut(10s, 1.4, phase 2), Summon(14s, 2, void_hound), Repel(16s, 0.8, phase 2), Annihilate(22s, 2.2, phase 2), MarkOfFate(40s, phase 2, weight 0.8) | 기존 15종 + 레이드 전용 |
| 신규 기믹 | 공허 균열(VoidRift, `BossBrain.VoidRift.cs`): 페이즈 2부터 32초마다 방 안 두 곳에 균열이 열린다. 열린 균열마다 10초에 보스 최대 HP 1.5% 치유(`Seal`의 무적과 구분되는 "치유" 기믹). 균열 위에 사람 수 1·2·3명 이상에 따라 3초·2초·1초 서 있으면 닫힌다(서 있는 동안 받는 피해 +30%). 혼자일 때는 AI 동료가 밟는다 | 수호석 자리를 메운다는 이야기와 같은 동작. 3-8 필드 균열에서 먼저 배운다 |
| 기믹 구현 | 균열은 `Telegraph.SafeCircle`로 등록한 안전 지점으로 노출된다(`RaidDoom.SpotFor`와 같은 조회 인터페이스). 측정 봇(`QuestAutoPilot.Dodge`)과 `CompanionBrain`은 기존 안전 지점 조회 그대로 따라가므로 봇 코드 추가가 없다 | |
| 보스 외침 | Slash3 "갈라져라!" / "셋이면 충분하다" · Fields "발밑을 보아라" / "땅이 운다" · Donut "가까이 와라" / "멀면 죽는다" · Repel "밀려나라!" / "...돌아오지 마라" · Annihilate "모두 사라져라" / "숨어 봐야 소용없다" / "...빛의 원으로" · VoidRift "틈이 열린다" / "메워 봐라, 용사의 피" · 페이즈 3 진입 "...아푸크. 그분이 깨어난다"(한 번) | 패턴마다 2~3개, `Announce` 한 줄씩 |
| 보상 | 확정 골드 10,000~15,000, 재료 `mat_raid_xibak`(침식 결정), 카드: 장비 1(12), 재료 1(12)·2(30)·4(28)·8(9), 정수 16(6), 보호권 1(3). 장비 에픽 58 / 유니크 41 / 레전더리 1 | 그라흐 표 구조 |
| 열쇠 | 보상 받는 클리어마다 봉인 열쇠 조각 20~50(기존 `DungeonDatabase.SealKey`) | 2장 잔여 조각이 3장에 쓰인다 |
| 핵 | `PromoteRules.RaidCoreMid` { 1, 2, 2, 3 } | |

## 2. 최종 레이드: 잿빛 손 지휘관 카마츠 (발체 구출전)

| 항목 | 값 | 근거 |
|---|---|---|
| id | `raid_kamatz` | |
| 이름 | 잿빛 손 지휘관 카마츠 (Grey Hand Commander Kamatz) | 카마조츠, 박쥐 신 |
| 무대 | 공허의 틈 끝 잿빛 손 진영(`DgnKamatz1`, `DgnKamatzBoss`, DryCanyon 야영지). 보스 방 뒤편 우리에 발체 | |
| raidTier / chapter | Final / 3 | |
| unlockQuest | c3_kamatz, keyCost 60 | |
| openDays | 일 | |
| RaidNumbers | recommendedLevel 50, power 15,500, hpMul 5.0, damageMul 1.9, monsterLevel 45, minGear Epic, ticketWeight 0 | 그라흐에서 레벨 10 증가분. monsterLevel은 권장 레벨보다 5 낮게 |
| rooms | 1: grey_knight 5 + grey_priest 2 + grey_ranger 2 / 보스: 카마츠 + 졸개 grey_knight | 사제가 보호막을 주므로 먼저 잡는 방 |
| referenceSeconds | { 360, 540, 360, 360 } | 그라흐와 같음 |
| clearXp | 2,000 | |
| 보스 | `boss_kamatz`: hp 8,800, dmg 64, lines 80, speed 2.4, size 2.4, artScale 1.4, raid = true, phases { 0.7, 0.4, 0.15 }, maxMinions 4, groggyMax 800 | 사람형 중무장 기사 |
| 패턴 | Slash3(4s, 0.95, 3, maxRange 3.4, weight 1.5), Charge(7s, 1.3), ArrowVolley(8s, 1.0, 9), Guard(12s), BoltRing(6s, 1.0, 12, phase 2), Fields(7s, 1.1, 7, phase 2), Annihilate(22s, 2.2, phase 2), RuinRitual(48s, phase 2, weight 0.8), Judgment(35s, 8.0, phase 3, weight 0) | 사람형이라 Summon 대신 Guard·ArrowVolley |
| 신규 기믹 | 잿빛 낙인(GreyBrand, `BossBrain.GreyBrand.cs`): 페이즈 2부터 30초마다 파티원 한 명에게 낙인. 6초 안에 다른 사람 파티원과 2m 안에 서면 낙인이 사라진다. AI 동료와 붙으면 50%만 상쇄되어 최대 HP 35% 피해. 혼자면 70% | "너도 하나, 나도 하나"의 반대말. 사람 둘이 모이는 값 |
| 기믹 구현 | 낙인 대상의 "모일 자리"는 `RaidDoom.SpotFor` 방식으로 노출된다(가장 가까운 파티원 쪽 2m 지점). 측정 봇·`CompanionBrain`은 기존 조회로 다가간다. 봇 코드 추가 없음 | |
| 보스 외침 | Slash3 "잿빛 손은 멈추지 않는다" / "셋 다 받아라" · Charge "비켜라!" / "길을 열겠다" · ArrowVolley "쏴라" / "하늘을 봐라" · Guard "막는다" / "...무의미하다" · Annihilate "전부 치워라" / "방패 뒤로 숨어 봐라" · RuinRitual "의식을 시작하라" / "토템을 지켜라" · GreyBrand "낙인을 받아라" / "혼자서는 못 버틴다" / "둘이 모이면 지워지지" · 페이즈 3 "카엘 경... 이 아이는 내가 데려가겠습니다" | |
| 보상 | 확정 골드 16,000~24,000, 재료 `mat_raid_kamatz`(잿빛 인장), 카드: 장비 1(12), 재료 1(12)·2(30)·4(28)·8(9), 정수 16(6), 보호권 1(3). 장비 에픽 58 / 유니크 41 / 레전더리 1(Lv50) | |
| 핵 | `PromoteRules.RaidCoreFinal` { 2, 3, 4, 6 } | |

## 3. 레이드 상점

`RaidShop.Products`에 4개 추가. 가격 원칙은 기존과 같다(에픽+ 상자는 보상 1.5~2주, 확정 레전더리는 5~6주).

| id | 이름 | 재료 | 가격 | 내용 |
|---|---|---|---|---|
| xibak_epic_weapon | 지바크 에픽+ 무기 상자 | mat_raid_xibak | 25 | 티어 9 무기, 에픽 88 / 유니크 12 |
| xibak_legend_weapon | 지바크 레전더리 무기 상자 | mat_raid_xibak | 70 | 티어 10 레전더리 무기 |
| kamatz_epic_weapon | 카마츠 에픽+ 무기 상자 | mat_raid_kamatz | 20 | 티어 10 무기, 에픽 88 / 유니크 12 |
| kamatz_legend_weapon | 카마츠 레전더리 무기 상자 | mat_raid_kamatz | 60 | 티어 10 레전더리 무기 |

- 재료 기대값은 그라흐와 같은 카드 표라 판당 2.56개. 지바크 주 3회 + 심화 주 1회 = 주 약 20개 → 레전더리 상자 3.5주. 카마츠 주 1회 + 심화 = 주 약 10개 → 6주. 군주 던전 확정 보상(침식 결정 주 6~9, 잿빛 인장 주 4)이 더해진다.
- 재료는 계승(`PROGRESSION_AND_GEAR.md` 4절)과 아카브 교환(5절)에도 쓰인다. `RaidRewards.IsRaidMaterial`에 두 재료 추가.

## 4. 공통

- `DungeonDatabase.RaidDefs`에 두 정의를 넣고 내보낸다. `Upcoming`에는 넣지 않는다. 닫힌 동안의 잠금은 `assertOpen('raid', id)`(`LIVE_CONTENT.md` 6절).
- `ContentUnlocks.Raid`는 그대로. 3장 레이드는 unlockQuest 수락으로 열린다.
- 혼자 + AI 기준: 측정 봇(장판 회피 켬)이 일반 난이도를 절반 이상 클리어하고, 두 기믹을 AI 동료가 기존 안전 지점 조회로 수행한다(균열 밟기 횟수·낙인 상쇄 횟수를 측정 행에 기록).
- 심화 레이드 보상 주기·`raid_claims`·열쇠 이월은 기존 서버 규칙 그대로.
