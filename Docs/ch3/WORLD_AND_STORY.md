# 챕터 3 세계·이야기 (2026-10-21)

기준: `STORY.md` 3장 줄거리("주인공은 왕국에 등을 돌리고 리아를 찾아 마족 땅으로 간다. 리아는 마족 마을에서 살아 있다")와 7절 떡밥. 퀘스트·컷신·대사 데이터 구조는 `research/CH3_CURRENT_SYSTEMS_2026-10-21.md` 6절.

## 1. 이야기 줄기

- 2장 끝에서 아무도 믿지 않는 주인공에게 레오나가 길을 알려 준다: 수호석이 깨진 뒤 협곡 땅이 마르고 갈라지는데, 그 틈 너머가 마족 땅이다.
- 메말라 가는 협곡에는 깨진 수호석에서 번진 공허 침식체가 돌아다닌다. 왕국이 "마족의 짓"이라 부르는 것이 사실은 주인공이 깬 수호석의 결과다.
- 마족 온건파의 마을 사얍에서 익시(핏빛 달)가 주인공을 맞고, 리아가 살아 있다. 리아는 바르가스에게서 빠져나와 익시의 보호 아래 약초를 가꾸며 살았다.
- 적은 둘이다. 깨진 수호석에서 솟은 공허 침식체 시브(중간 레이드), 그리고 사얍을 찾아낸 잿빛 손(카엘의 조직)의 지휘관 카마츠(최종 레이드). 카엘 본인은 4장 최종 보스로 남기고 3장에는 편지와 부하들로만 나온다.
- 회수하는 떡밥: 핏빛 달 익시(리아를 숨긴 사람), "수호자들은 악이 아니다"(그라흐의 부하들이 사얍에 있다), 400년 주기(노흐의 기록). 4장을 위해 새로 심는 것: "용사는 둘이었다"는 기록 조각, 사얍에서도 등불을 둘씩 띄우는 풍습.

## 2. 메인 퀘스트 사슬

`chapter: 3`, `kind: main`. requires는 직전 퀘스트(3-1은 `c2_end`). 보상 XP·골드는 데이터 값(실제 지급은 XP x6, 골드 x0.5). 합계 XP 410,000, 골드 286,000.

| code | id | 제목 | 화자 | minLevel | 목표(objective) | XP / 골드 | 컷신·떡밥 |
|---|---|---|---|---|---|---|---|
| 3-1 | c3_depart | 협곡 너머로 | leona | 40 (requires c2_end) | reach dry_floor, kill void_hound 20 | 20,000 / 15,000 | 레오나: "수호석이 깨진 뒤 땅이 마른다. 틈 너머에 마족이 산다고 들었다" |
| 3-2 | c3_cracks | 갈라진 바닥 | leona | 41 | interact 침식 흔적 3(dry_floor), kill void_sentry 40 | 24,000 / 18,000 | 2-5에서 처형된 마족의 말을 주인공이 떠올린다 |
| 3-3 | c3_sayab | 사얍 마을 | (자동) | 42 | cutscene c3_gate, reach sayab, cutscene c3_ixi, cutscene c3_ria | 26,000 / 20,000 | 전사장 쿠쿰이 막아서다 리아의 머리끈을 보고 멈춘다. 익시 대면. 리아 재회("너도 하나, 나도 하나") |
| 3-4 | c3_trust | 믿음의 값 | ixi | 43 | kill void_sentry 50(dry_stones), collect 공허 정수 10 | 30,000 / 22,000 | 익시: "너는 수호석을 깬 손이다. 그 손으로 침식을 막아 보여라" |
| 3-5 | c3_record | 노흐의 기록 | noh | 44 | reach hollow_roots, collect 기록 조각 3(B2 interact) | 34,000 / 25,000 | 조각: "둘이 돌을 들었다", "한 주기는 열세 번의 스무 해". 400년 떡밥 |
| 3-6 | c3_xib | 공허 침식체 | ixi | 45 | cutscene c3_xib_rise, raid raid_xib 1 | 40,000 / 30,000 | 중간 레이드 해금(수·토·일). 깨진 수호석 자리에서 시브가 솟는다 |
| 3-7 | c3_greyhand | 잿빛 손의 그림자 | kukum | 46 | cutscene c3_scouts, kill grey_ranger 60(dry_floor·dry_stones) | 36,000 / 26,000 | 잿빛 손 정찰대. 카엘의 편지(큰 새 문장이 아닌 잿빛 손 인장): "그 아이가 거기 있다" |
| 3-8 | c3_stones | 잔해의 길 | akab | 47 | interact 수호석 잔해 3(dry_rift), kill void_caller 80 | 40,000 / 28,000 | 잔해가 주인공의 손에만 반응한다(혈통) |
| 3-9 | c3_oath | 익시의 맹세 | ixi | 48 | cutscene c3_oath, kill hollow_guard 100(B3·B4) | 44,000 / 30,000 | 익시가 봉인의 진실 일부를 말한다: "볼론은 너희가 배운 악이 아니다. 지하가 조용한 건 그가 버티기 때문이다" |
| 3-10 | c3_trace | 잿빛 손의 흔적 | kukum | 49 | interact 야영지 3(dry_rift), kill grey_knight 60 | 48,000 / 32,000 | 카마츠의 진영 위치. 열쇠 조각이 모이면 최종 레이드 |
| 3-11 | c3_kamatz | 지휘관 카마츠 | ixi | 50 | raid raid_kamatz 1, cutscene c3_kamatz_end | 60,000 / 26,000 | 최종 레이드(일요일, keyCost 60). 카마츠: "카엘 경은 눈꽃 숲으로 갔다. 지워야 할 것이 남았다더군"(4장 예고). 오르반의 환영 한 줄 |
| 3-끝 | c3_end | 적이라고 배웠던 사람들 | (자동) | 0 | cutscene c3_aftermath | 8,000 / 0, setFlags chapter3_done | 사얍의 봉인 축제. 마족도 등불을 둘씩 띄운다. 리아가 머리끈 하나를 주인공에게 돌려준다 |

- 따라잡기 게이트(`mainQuestLevelGate`)는 3-2부터 3-11까지 레벨마다 켜진다(41~50). 미니 던전 40의 따라잡기가 10레벨 내내 의미를 갖는다.
- 자동 진행(`QuestAutoPilot.ChapterOne`)은 3장에 넣지 않는다. 처치·수집 목표는 `QuestAutoPilot.SubQuests`가 사냥터로 안내한다(기존 2장과 같음).

## 3. 서브 퀘스트

`kind: sub`, chapter 3. 합계 XP 80,000. 리아의 약초밭(반복 수집)은 퀘스트 데이터에 반복 개념이 없어 일일 의뢰 사얍 밴드 템플릿으로 둔다(`LIVE_CONTENT.md` 4절).

| id | 제목 | 화자 | minLevel | 목표 | XP |
|---|---|---|---|---|---|
| c3s_kukum_drill | 전사장의 시험 | kukum | 42 | kill void_hound 40 | 5,000 |
| c3s_akab_supply | 밤 상인의 보급 | akab | 43 | collect 마른 가죽 12(dry_floor) | 5,500 |
| c3s_kanan_herbs | 치유사의 약초 | kanan | 43 | collect 재 속 약초 8(dry_stones) | 5,500 |
| c3s_noh_tales | 옛 노래 | noh | 44 | talk ixi, kukum, ria | 4,000 |
| c3s_balche_toy | 발체의 보물 | balche | 44 | interact 잃어버린 조약돌 1(dry_floor) | 4,000 |
| c3s_hollow_b1 | 뿌리 아래 순찰 | kukum | 45 | kill hollow_scarab 60 | 8,000 |
| c3s_hollow_b2 | 뒤엉킨 뿌리 | kukum | 46 | kill hollow_hexer 60 | 9,000 |
| c3s_grey_patrol | 정찰대 추격 | akab | 47 | kill grey_ranger 50 | 10,000 |
| c3s_hollow_b4 | 반딧불 심연 | noh | 49 | kill hollow_guard 60(B4) | 14,000 |
| c3s_ria_letter | 리아의 편지 | ria | 42 | talk noh, collect 마른 꽃 3(dry_floor) | 15,000 |

## 4. 사얍 마을

| 항목 | 결정 |
|---|---|
| 이름 | 사얍 마을(Sayab Village). 사얍 = 숨은 샘. 메말라 가는 협곡 깊은 곳, 마른 땅 아래에서 솟는 샘을 둘러싼 마을 |
| 맵 | `MapRegistry.Sayab`("sayab"), `Resources/Maps/Sayab.txt`, 새 테마 `MapTheme.DemonTown`(WorldBuilder 분기 추가), BGM `music_sayab`. 코바 마을과 같은 "Town" 구성(여관·대장간·상인·광장·게시판) |
| 이동 | 월드맵에서 바위 협곡 마을 남동쪽. 3-3 완료 뒤 월드맵 노드가 열린다. 귀환 주문서 목적지에 추가 |
| 시설 | 대장간(아카브, 강화·승급·계승), 상점(아카브), 여관, 기사단 게시판 대신 "마을 게시판"(일일 의뢰 41~50 밴드), 창고 |
| 특화 조정 | 익시 대화 "특화 조정"이 스킬 창 특화 탭을 연다(코바 마을은 이츠나) |

### NPC

| id | 이름 | 역할 | 유래 | 그림 |
|---|---|---|---|---|
| ixi | 익시 (Ixi) | 마족 온건파 지도자. 3장 메인 화자 | 익시크, 핏빛 달 | 신규 40장 |
| kukum | 전사장 쿠쿰 (Kukum) | 마을 수비 대장. 그라흐의 옛 부하. 일일 의뢰·서브 화자 | k'uk'um = 깃털 | 신규 40장 |
| akab | 밤 상인 아카브 (Akab) | 대장간·상점·계승 | ak'ab = 밤 | 신규 40장 |
| noh | 촌로 노흐 (Noh) | 기록 보관. 400년 주기·용사 둘 떡밥 | noh = 큰 | 신규 40장 |
| kanan | 치유사 카난 (Kanan) | 물약 상점, 약초 서브 | k'an = 귀한 | 신규 40장 |
| balche | 발체 (Balche) | 마족 아이. 서브 | b'alche' = 성스러운 나무 | 신규 40장 |
| ria | 리아 | 성장한 리아. 서브 c3s_ria_letter, 일일 의뢰 약초밭 giver | 기존 인물 | 신규 40장(성인 모습, 빨간 머리끈) |
| sayab_innkeeper | 여관 주인 | 여관 | | 기존 여관 주인 그림 재사용 금지, 신규 40장 |

## 5. 사냥터

`HuntingGrounds.All`에 3줄 추가(village "sayab", 테마 `MapTheme.DryCanyon` 신규, variant 0~2). 지하 B1~B4는 그대로 두고 만렙 50으로 경험치가 살아난다.

| id | 이름 | Lv | 몬스터 Lv | 몬스터 | 역할 |
|---|---|---|---|---|---|
| dry_floor | 갈라진 협곡 바닥 | 41~44 | 42 | void_hound, void_sentry, void_caller | 3-1·3-2·3-7, 티어 8 드롭 |
| dry_stones | 검은 수호석 잔해 | 44~47 | 46 | void_sentry, void_caller, grey_ranger | 3-4·3-7, 티어 9 드롭 시작 |
| dry_rift | 공허의 틈 | 47~50 | 49 | void_caller, grey_knight, void_hound | 3-8·3-10, 티어 9 |
| hollow_descent B1 | (기존) | 40~45 | 42 | hollow_* | 3-5 전 사냥 |
| hollow_roots B2 / hollow_fungal B3 | (기존) | 45~50 | 47 | hollow_* | 3-5·3-9 |
| hollow_depths B4 | (기존) | 50~55 | 53 | hollow_* | Lv50 사냥터, 티어 10 드롭 |

- 분당 처치 목표: 같은 레벨 레어 장비 기준 15~20(지하 실측 14~26과 같은 급). 7단계 측정으로 확인하고 몬스터 체력으로 맞춘다.
- `DryCanyon` 테마: 갈라진 마른 땅, 죽은 나무, 검은 돌 조각, 보랏빛 공허 균열. WorldBuilder에 `SanctumField`·`Underground`처럼 분기 추가. 타일·소품은 `ART_LIST.md` 2절.

### 몬스터

| id | 이름 | kind | Lv(존별) | 비고 |
|---|---|---|---|---|
| void_hound | 공허 들개 (Void Hound) | Charger | 42·49 | 빠르고 체력 낮음 |
| void_sentry | 공허 파수석 (Void Sentry) | ShieldGuard | 42·46 | 정면 방어 |
| void_caller | 공허의 부름꾼 (Void Caller) | Archer | 42·46·49 | 원거리, 처치 시 작은 균열 장판 1초 |
| grey_ranger | 잿빛 손 정찰병 (Grey Hand Ranger) | Archer | 46 | 사람 적. 처치 시 "잿빛 인장" 드롭(3-7·3-10 목표 아이템) |
| grey_knight | 잿빛 손 기사 (Grey Hand Knight) | Charger | 49 | 사람 적. 카엘과 같은 잿빛 망토 |

- 체력·피해는 같은 kind의 지하 몬스터(hollow_scarab·hollow_guard·hollow_hexer)의 값을 레벨 비율로 옮긴다. 경험치는 `HuntingZone.KillXp` 공식(지하가 아니므로 +25% 없음).
- 그림: 공허 3종 1 아틀라스, 잿빛 손 2종 1 아틀라스(5방향 x 8프레임, `RegionalMonsterArt` 규격). `ART_LIST.md` 3절.

## 6. 요일 던전 "군주" 난이도

기존 5개 던전(황금 광맥·버려진 제련소·마력의 묘지·수련의 숲·망자의 무기고)에 세 번째 `DifficultyDef`를 더한다. 신규 던전은 만들지 않는다(방·보스·아트 재사용, 다섯 집 떡밥 유지).

| 항목 | 왕(기존) | 영웅(기존) | 군주(신규) |
|---|---|---|---|
| id | King | Hero | Lord |
| recommendedLevel | 20 | 27 | 45 |
| recommendedPower | 4,200 | 6,000 | 13,000 |
| hpMul | 7.7 | 12 | 18 |
| damageMul | 2.0 | 2.6 | 3.2 |
| rewardMul | 2.4 | 3.4 | 4.6 |
| monsterLevel | 15 | 22 | 40 |
| revives | 3 | 2 | 2 |
| minGearRarity | Rare | Epic | Unique |
| ticketWeight / jackpotPerMille | 0 / 5 | 6 / 6 | 8 / 7 |

- 해금: `CHAPTER3_OPEN=1`이고 Lv41 이상이면 던전 창에 세 번째 탭. 하루 3회 횟수는 난이도 공통(기존 규칙).
- `DungeonClearFloor` 벤치마크 배열 { 10, 18, 26, 36 }에 45를 더한다(tier 3).
- 보상 테이블: 기존 rewards에 `rewardMul`만 곱한다. 군주 전용 추가 카드: 티어 9 장비 1(가중치 30).
- 측정 목표: 혼자 + AI 3명 120~300초(기존 왕·영웅과 같은 기준).

## 7. 컷신

| id | 자리 | 내용 |
|---|---|---|
| c3_gate | 3-3 | 메말라 가는 협곡 끝, 쿠쿰과 마족 수비대가 막아선다. 주인공의 머리끈을 본 쿠쿰이 창을 내린다. "그 끈... 익시 님께 안내하지" |
| c3_ixi | 3-3 | 사얍 광장. 익시: "용사의 피가 제 발로 왔군. 네가 깬 돌이 무엇이었는지 알고 왔나" |
| c3_ria | 3-3 | 리아의 약초밭. 리아가 돌아본다. 대사 없이 한 박자. "...늦었어." "응. 늦었어." 머리끈 둘 |
| c3_xib_rise | 3-6 | 깨진 수호석 자리가 갈라지며 시브가 솟는다. 익시: "저게 네 손이 한 일이다. 그리고 네 손만이 되돌릴 수 있다" |
| c3_scouts | 3-7 | 잿빛 손 정찰대가 마을 외곽에 인장을 남긴다. 쿠쿰이 편지를 주워 온다. 잿빛 손 인장 |
| c3_oath | 3-9 | 노흐의 집. 익시가 기록 조각 셋을 맞춘다. "둘이 돌을 들었다. 하나는 너희 용사, 하나는 우리 왕" |
| c3_kamatz_end | 3-11 | 쓰러진 카마츠. "카엘 경은 눈꽃 숲으로 갔다. 지워야 할 것이 남았다더군." 멀리 오르반의 환영이 웃는다 |
| c3_aftermath | 3-끝 | 사얍의 봉인 축제. 마족 아이들이 등불을 둘씩 띄운다. 리아가 머리끈 하나를 돌려준다. 주인공은 북쪽(눈꽃 숲)을 본다. flag chapter3_done |

컷신 cmd는 기존 27종(`CutscenePlayer.cs`)으로 충분하다. 새 actor id: ixi, kukum, akab, noh, kanan, balche, ria_adult, kamatz, grey_knight.

## 8. 대사·번역

- 새 대사는 `StoryDialogues.json`에 `c3_*` id로, 화자 이름은 4절 NPC 이름. 떡밥 문장은 2절·7절의 문구를 그대로 쓴다.
- `I18N_GLOSSARY.md` 고유 이름 표에 추가: 사얍 Sayab, 익시 Ixi(기존), 쿠쿰 Kukum, 아카브 Akab, 노흐 Noh, 카난 Kanan, 발체 Balche, 시브 Xib, 카마츠 Kamatz, 메말라 가는 협곡 the Withering Canyon, 공허 침식체 Void Blight, 잿빛 손 the Grey Hand(기존 번역 확인 후 통일).
