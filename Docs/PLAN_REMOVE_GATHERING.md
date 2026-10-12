# 나무·돌 채집 제거 계획 (2026-10-12)

사용자 결정: 나무·돌 채집은 초반 퀘스트에만 쓰이고 이후 쓸 곳이 없으므로 없앤다. 그냥 지우지 말고 관련 코드를 전부 파악해 확실히 제거한다.

## 원칙
- 채집 기능(나무 베기, 바위 깨기, 목재·돌 획득, 공사장 납품)만 없앤다. 당근 밭(작물)은 같은 서버 `/gathers`·`character_node_state`를 쓰므로 그대로 둔다.
- 이미 가방·창고에 있는 목재·돌은 지우지 않는다(데이터 유실 방지). 아이템 정의와 판매가(개당 3골드)는 남기고 설명만 "더 이상 얻을 수 없는 옛 재료"로 바꾼다.
- DB 테이블(`site_deliveries`)은 마이그레이션 이력이라 지우지 않는다. 납품 API와 그것을 읽는 코드만 없앤다. 탈퇴 정책의 행 삭제는 그대로 둔다.
- 맵의 나무·바위는 없애지 않고 장식(부딪히기만 하는 소품)으로 바꾼다. 풍경은 그대로다.

## 1. 월드 (클라이언트, 메인 담당)
| 대상 | 처리 |
|---|---|
| `'t'` 베는 나무(Town 빌더: 마을 4, 숲 10) | `'T'`와 같은 정적 나무 |
| `'R'` 깨는 바위(Town·Canyon 빌더, 협곡 사냥터 생성 지형 포함) | 정적 바위 소품 `StaticProp("Rock", "town_rock_N")` (설원의 "Stone"과 같은 방식) |
| `'T' 'O' 'R'` 저해상도 대체 경로(Objects.cs) | 정적 나무·바위 소품 |
| `ResourceNode.cs`와 그 참조(PropIntegration 노드 덮어쓰기, SurfaceGrounding 접지 패치, SurfaceWorld·ForestCanopy 건너뛰기, PlayerCombat 채집 타격, MagicBolt, OnlineEconomy 재생 대기) | 클래스 삭제, 참조 정리 |
| GameConfig 나무·바위 체력·드롭·재생 값 | 삭제(작물 재생 값은 유지) |
| `tree_fall` 효과음 | 삭제(`chop`·`mine`·`rock_break`는 다른 곳에서도 써서 유지) |
| 조작 안내 "공격 / 채집", 직업 설명의 나무·바위 문구 | 문구 수정 |
| 나무꾼·광부 NPC의 도끼·곡괭이 연출 | 장식이라 유지 |

## 2. 공방 재건 퀘스트 `c1_rebuild` (메인 1-6)
- 목표 0 `flag workshop_built`: "목재·돌 전달" 대신 **동쪽 공사장에서 건축가를 돕기**(공사장에 다가가 상호작용하면 바로 재건 연출 후 플래그). 목표 1 해골 3마리 처치는 그대로.
- 공사장(ConstructionSite): 목재·돌 더미와 납품 처리 삭제, 상호작용 = 재건.
- 플래그 저장: 오프라인은 기존대로, 온라인은 서버 `story_flags`로 저장되고 불러올 때 `story_flags`에서 `workshopBuilt`를 복원한다(납품 기록 파싱 삭제). 이미 재건한 캐릭터는 이 플래그가 이미 있다.
- 서버: `deliverySites.workshop`을 없애면 `workshop_built`는 검증 대상이 아닌 일반 플래그가 된다(questService 규칙).
- 자동 진행(QuestAutoPilot): 채집 목표·납품 목표를 "공사장으로 가서 상호작용"으로 바꾼다.

## 3. 목재 수집 서브 퀘스트 4개 (클라이언트 Quests.json + 서버 quest_index.json + 대사)
| 퀘스트 | 지금 | 바꾼 목표 |
|---|---|---|
| `c1s_herbs` 리아의 약초밭 | 목재 5개 | 숲(forest) 해골 20마리 처치: 약초밭을 파헤치는 해골 |
| `c1s_fisher_net` 도윤의 그물 | 목재 8개 | 뼈 조각(mat_bone) 12개 가져가기(그물 추) |
| `c1s_lumber` 숲길의 나무꾼 | 해골 60 + 목재 10 | 해골 60마리만(나무꾼이 다닐 숲길 정리) |
| `w_firewood` 눈꽃 마을의 장작 | 목재 20개 | 서리 소나무 숲(winter_edge) 설산 예티(skel_warrior) 40마리 처치: 장작 길 확보 |
- 보상은 그대로. 퀘스트 id도 그대로(진행 중인 캐릭터 보존). 진행 중이던 목표 카운트는 목표가 바뀌므로 0부터 다시 센다.
- 대사(Dialogues.json, StoryDialogues.json)와 퀘스트 요약의 목재·돌·채집 문구를 새 목표에 맞게 고친다.

## 4. 서버 (fork 담당)
- 삭제: `/deliveries` 라우트·컨트롤러·서비스(deliver)·검증(deliveryBody)·저장소의 납품 함수, 캐릭터 응답의 `deliveries`, economyRepository의 납품 읽기, questService의 `getDeliveries` 사용(납품처가 없으면 필요 없음, `siteComplete`도 삭제).
- 유지: `/gathers`, 노드 상태 조회(작물).
- 데이터: `gameconfig.json`의 `nodeKinds.tree`·`nodeKinds.rock`·`deliverySites.workshop` 삭제(스키마는 빈 객체 허용), `maps.json`의 tree·rock 노드 삭제, `quest_index.json`의 5개 퀘스트를 3장 표대로.
- 테스트: 납품·바위 채집 테스트 삭제 또는 작물로 바꾸기, 목재 소비 퀘스트 테스트를 새 목표로. `npx tsc --noEmit`과 전체 테스트 통과.

## 5. 저장·개발 도구
- SaveData의 `woodDelivered`·`stoneDelivered`는 옛 세이브 호환을 위해 필드만 남기고 쓰지 않는다. `workshopBuilt`는 유지.
- DevCapture의 채집·노드 검사는 삭제, 상점·창고 시나리오의 목재는 다른 재료로.
- 에디터 내보내기(GameDataExport)의 노드 종류·납품처·노드 집계 삭제.

## 6. 마무리
- 데이터 버전 갱신(`dataver.py ordinal`), 클라이언트 컴파일, 서버 테스트·타입 검사, 커밋·푸시(wonho, main, jaein), 서버 배포, PC 빌드.
